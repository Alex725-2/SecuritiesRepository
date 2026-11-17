using Microsoft.Extensions.Logging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Securities.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddDbContext<AccountingDb>(o => o.UseNpgsql(DatabaseConnection.Resolve(builder.Configuration["ConnectionStrings:Database"], builder.Configuration["Database:Password"]) ?? throw new InvalidOperationException("Не задана БД.")));
builder.Services.Configure<MassTransitHostOptions>(o => { o.WaitUntilStarted = true; o.StartTimeout = TimeSpan.FromSeconds(60); });
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OperationConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "rabbitmq", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "demo");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? throw new InvalidOperationException("Не задан пароль RabbitMQ."));
        });
        cfg.ReceiveEndpoint("securities-operation-completed", e =>
        {
            e.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(3)));
            e.ConfigureConsumer<OperationConsumer>(context);
        });
    });
});
builder.Services.AddHostedService<OutboxDispatcher>();
await builder.Build().RunAsync();
