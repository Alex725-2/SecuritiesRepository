using DotNet.Testcontainers.Builders;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Securities.Contracts;
using Securities.Domain;
using Securities.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Securities.IntegrationTests;

/// <summary>Verifies the accounting or transport contract.</summary>
public sealed class RabbitFactAttribute : FactAttribute
{
    /// <summary>Verifies the accounting or transport contract.</summary>
    public RabbitFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_RABBITMQ_TESTS") != "1")
            Skip = "Requires real RabbitMQ and PostgreSQL: RUN_RABBITMQ_TESTS=1 and Docker.";
    }
}

/// <summary>Verifies the accounting or transport contract.</summary>
public class RabbitTests
{
    static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (!await condition()) await Task.Delay(200, timeout.Token);
    }

    /// <summary>Confirms topology, delivery, receipt and DeliveredAt ordering.</summary>
    [RabbitFact]
    public Task Delivery() => RealTransportScenario("delivery");
    /// <summary>Confirms repeated transport delivery has one durable Inbox effect.</summary>
    [RabbitFact]
    public Task DuplicateDelivery() => RealTransportScenario("duplicate");
    /// <summary>Requires actual basic.return; a timeout cannot pass.</summary>
    [RabbitFact]
    public Task NoRoute() => RealTransportScenario("unroutable");
    /// <summary>Unavailable broker leaves the event pending.</summary>
    [RabbitFact]
    public Task BrokerUnavailable() => RealTransportScenario("outage");
    /// <summary>Recovery drains the pending event after a broker outage.</summary>
    [RabbitFact]
    public Task BrokerRecovery() => RealTransportScenario("recovery");

    static async Task RealTransportScenario(string scenario)
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await using var rabbit = new ContainerBuilder().WithImage("rabbitmq:3.13-alpine")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "demo").WithEnvironment("RABBITMQ_DEFAULT_PASS", "test-password")
            .WithPortBinding(5672, true).WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672)).Build();
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AccountingDb>(o => o.UseNpgsql(postgres.GetConnectionString()));
        services.AddMassTransit(x =>
        {
            x.AddConsumer<OperationConsumer>();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbit.Hostname, rabbit.GetMappedPublicPort(5672), "/", h => { h.Username("demo"); h.Password("test-password"); });
                cfg.ReceiveEndpoint("securities-operation-completed", e => e.ConfigureConsumer<OperationConsumer>(context));
            });
        });
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        var observer = new ReceiptObserver();
        using var observerHandle = bus.ConnectConsumeObserver(observer);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await bus.StartAsync(deadline.Token); // waits for receive topology
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
                await DatabaseSetup.Create(db); await DemoSeed.Run(db);
            }
            var dispatcher = new OutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcher>.Instance);
            await dispatcher.DispatchOnce(deadline.Token);
            await using (var scope = provider.CreateAsyncScope())
                Assert.Null((await scope.ServiceProvider.GetRequiredService<AccountingDb>().Set<OutboxMessage>().SingleAsync()).DeliveredAt);
            async Task<bool> HasReceipt()
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AccountingDb>().Set<InboxMessage>().AnyAsync();
            }
            await Eventually(HasReceipt);
            await dispatcher.DispatchOnce(deadline.Token);
            OperationCompleted duplicate;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
                var message = await db.Set<OutboxMessage>().SingleAsync();
                var operation = await db.Operations.SingleAsync();
                Assert.NotNull(message.DeliveredAt);
                duplicate = new(message.Id, operation.Id, operation.CompletedAt!.Value);
            }
            if (scenario == "delivery") return;
            if (scenario == "duplicate")
            {
            var endpoint = await bus.GetSendEndpoint(new Uri("queue:securities-operation-completed"));
            await endpoint.Send(duplicate, deadline.Token); await endpoint.Send(duplicate, deadline.Token);
            await Eventually(() => Task.FromResult(observer.Messages.Count >= 3));
            Assert.All(observer.Messages, message => Assert.Equal(duplicate.CompletedAt, message.CompletedAt));
            await using (var scope = provider.CreateAsyncScope())
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AccountingDb>().Set<InboxMessage>().CountAsync());

            return;
            }
            if (scenario == "unroutable")
            {
            // An exchange with no queue must return mandatory messages, despite publisher ACK.
            var unroutable = await bus.GetSendEndpoint(new Uri("exchange:no-route-" + Guid.NewGuid().ToString("N")));
            await Assert.ThrowsAsync<MessageReturnedException>(() => unroutable.Send(duplicate, context =>
            {
                var transport = context.GetPayload<RabbitMqSendContext>();
                transport.Mandatory = true; transport.AwaitAck = true;
            }, deadline.Token).WaitAsync(TimeSpan.FromSeconds(15)));

            return;
            }
            await rabbit.StopAsync();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
                var account = await db.Accounts.FirstAsync(); var security = await db.Securities.FirstAsync();
                await new AccountingService(db).Execute("Credit", new("outage",account.Id,null,security.Id,1,"external","doc"));
            }
            using (var unavailable = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await Assert.ThrowsAnyAsync<Exception>(() => dispatcher.DispatchOnce(unavailable.Token));
            await using (var scope = provider.CreateAsyncScope())
                Assert.Single(await scope.ServiceProvider.GetRequiredService<AccountingDb>().Set<OutboxMessage>().Where(x => x.DeliveredAt == null).ToListAsync());
            if (scenario == "outage") return;
            await rabbit.StartAsync();
            await Eventually(async () =>
            {
                try { await dispatcher.DispatchOnce(deadline.Token); }
                catch (Exception) when (!deadline.IsCancellationRequested) { return false; }
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
                return await db.Set<InboxMessage>().CountAsync() == 2 && !await db.Set<OutboxMessage>().AnyAsync(x => x.DeliveredAt == null);
            });
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await bus.StopAsync(stop.Token); }
    }

    sealed class ReceiptObserver : IConsumeObserver
    {
        public System.Collections.Concurrent.ConcurrentBag<OperationCompleted> Messages { get; } = [];
        public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;
        public Task PostConsume<T>(ConsumeContext<T> context) where T : class
        {
            if (context.Message is OperationCompleted message) Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class => Task.CompletedTask;
    }
}
