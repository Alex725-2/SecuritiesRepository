using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Securities.Contracts;
using Securities.Domain;

namespace Securities.Infrastructure;
/// <summary>Доставляет Outbox; сбой после публикации может вызвать безопасный повтор.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> log) : BackgroundService
{
    /// <summary>One transactional delivery pass, also used by real transport tests.</summary>
    public async Task DispatchOnce(CancellationToken ct = default)
    {
        // Bound every pass, including callers other than the hosted loop. A broker
        // reconnect retry must not hold the Outbox transaction indefinitely.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromSeconds(30));
        ct = attempt.Token;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDb>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var messages = await db.Set<OutboxMessage>().FromSqlRaw("SELECT * FROM \"OutboxMessage\" WHERE \"DeliveredAt\" IS NULL ORDER BY \"CreatedAt\" LIMIT 20 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        foreach (var m in messages)
        {
            if (await db.Set<InboxMessage>().AnyAsync(x => x.OperationId == m.OperationId, ct))
            {
                m.DeliveredAt = DateTimeOffset.UtcNow;
                continue;
            }
            var operation = await db.Operations.AsNoTracking().SingleAsync(x => x.Id == m.OperationId, ct);
            var endpoint = await scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>()
                .GetSendEndpoint(new Uri("queue:securities-operation-completed"));
            await endpoint.Send(new OperationCompleted(m.Id, m.OperationId, operation.CompletedAt!.Value), context =>
            {
                context.MessageId = m.Id;
                context.Durable = true;
                var rabbit = context.GetPayload<RabbitMqSendContext>();
                rabbit.Mandatory = true;
                rabbit.AwaitAck = true;
            }, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (scope.ServiceProvider.GetRequiredService<IBusControl>().CheckHealth().Status != BusHealthStatus.Healthy)
            throw new InvalidOperationException("RabbitMQ bus is unhealthy.");
        var stale = DateTimeOffset.UtcNow.AddMinutes(-1);
        if (await db.Set<OutboxMessage>().AnyAsync(x => x.DeliveredAt == null && x.CreatedAt < stale, ct))
            throw new InvalidOperationException("Outbox has unacknowledged events older than one minute.");
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "securities-heartbeat"), DateTimeOffset.UtcNow.ToString("O"), ct);
    }

    /// <summary>Настраивает и выполняет инфраструктурную часть учёта.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        File.Delete(Path.Combine(Path.GetTempPath(), "securities-heartbeat"));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnce(stoppingToken);
            }
            catch (Exception e)when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Доставка Outbox будет повторена.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}

/// <summary>Идемпотентный обработчик: сохраняет факт обработки, остатки не изменяет.</summary>
public sealed class OperationConsumer(AccountingDb db) : IConsumer<OperationCompleted>
{
    /// <summary>Обрабатывает доставленное событие.</summary>
    public Task Consume(ConsumeContext<OperationCompleted> context) => Record(context.Message, context.CancellationToken);
    /// <summary>Атомарно фиксирует аудит доставки без повторного эффекта.</summary>
    public async Task Record(OperationCompleted m, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"InboxMessage\" (\"Id\",\"OperationId\",\"ProcessedAt\") VALUES ({m.EventId},{m.OperationId},{DateTimeOffset.UtcNow}) ON CONFLICT DO NOTHING", ct);
    }
}
