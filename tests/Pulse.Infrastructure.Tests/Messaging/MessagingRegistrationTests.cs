using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulse.Infrastructure.Idempotency;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class MessagingRegistrationTests
{
    private static ServiceCollection NewServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        services.AddDbContext<PulseDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddPulseMessaging(configuration);
        return services;
    }

    [Fact]
    public void Outbox_publisher_resolves_from_scope()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var provider = NewServices(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OutboxPublisher>());
    }

    [Fact]
    public void Message_publisher_resolves_without_connecting_to_broker()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var provider = NewServices(configuration).BuildServiceProvider();

        Assert.IsType<RabbitMqPublisher>(provider.GetRequiredService<IMessagePublisher>());
    }

    [Fact]
    public void Idempotency_guard_resolves_as_redis_implementation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var provider = NewServices(configuration).BuildServiceProvider();

        Assert.IsType<RedisIdempotencyGuard>(provider.GetRequiredService<IIdempotencyGuard>());
    }

    [Fact]
    public void Registers_outbox_publisher_hosted_service()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var provider = NewServices(configuration).BuildServiceProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), h => h is OutboxPublisherWorker);
    }

    [Fact]
    public void Messaging_options_come_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = "host=cfg-rabbit",
                ["ConnectionStrings:Redis"] = "cfg-redis:6379"
            })
            .Build();
        using var provider = NewServices(configuration).BuildServiceProvider();

        Assert.Equal("host=cfg-rabbit", provider.GetRequiredService<RabbitMqOptions>().ConnectionString);
        Assert.Equal("cfg-redis:6379", provider.GetRequiredService<RedisOptions>().ConnectionString);
    }
}
