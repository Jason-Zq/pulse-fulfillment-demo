using Microsoft.Extensions.Configuration;
using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class MessagingOptionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(
                p => p.Key,
                p => (string?)p.Value))
            .Build();

    [Fact]
    public void RabbitMq_options_read_connection_string_from_configuration()
    {
        var config = Config(("ConnectionStrings:RabbitMq", "host=from-config"));

        var options = RabbitMqOptions.Load(config);

        Assert.Equal("host=from-config", options.ConnectionString);
    }

    [Fact]
    public void Redis_options_read_connection_string_from_configuration()
    {
        var config = Config(("ConnectionStrings:Redis", "redis-from-config:6379"));

        var options = RedisOptions.Load(config);

        Assert.Equal("redis-from-config:6379", options.ConnectionString);
    }

    [Fact]
    public void Missing_rabbitmq_config_returns_empty_not_a_hardcoded_host()
    {
        var options = RabbitMqOptions.Load(Config());

        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.DoesNotContain("@", options.ConnectionString);
    }

    [Fact]
    public void Missing_redis_config_returns_empty_not_a_hardcoded_host()
    {
        var options = RedisOptions.Load(Config());

        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.DoesNotContain(":", options.ConnectionString);
    }

    [Fact]
    public void Blank_config_is_treated_as_missing()
    {
        var options = RabbitMqOptions.Load(Config(("ConnectionStrings:RabbitMq", "   ")));

        Assert.Equal(string.Empty, options.ConnectionString);
    }
}
