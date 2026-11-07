using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Orders;

namespace Pulse.Infrastructure.Messaging;

/// <summary>消息链路的 DI 注册；连接串一律由 IConfiguration 提供。</summary>
public static class PulseMessagingServiceCollectionExtensions
{
    public static IServiceCollection AddPulseMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddLogging();
        services.AddSingleton(RabbitMqOptions.Load(configuration));
        services.AddSingleton(RedisOptions.Load(configuration));

        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<IIdempotencyGuard, RedisIdempotencyGuard>();
        services.AddSingleton<RabbitMqTopologyDeclarer>();
        services.AddScoped<OutboxPublisher>();
        services.AddHostedService<OutboxPublisherWorker>();

        // M3 履约链路服务（DI 友好，连接串仍由配置提供，解析不触发外部连接）。
        services.AddScoped<IEventDeduplicator, EventDeduplicator>();
        services.AddScoped<FulfillmentService>();
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<TimeoutOrderService>();
        services.AddSingleton<IRetryDecider, RetryDecider>();
        services.AddScoped<DeadLetterService>();
        services.AddHostedService<RabbitMqConsumerHost>();

        // M5 5.4：运行态开关落 DB，OutboxPublisherWorker / RabbitMqConsumerHost 每轮读取。
        services.AddScoped<ILabControlStore, EfLabControlStore>();

        return services;
    }
}
