using Microsoft.Extensions.Configuration;

namespace Pulse.Infrastructure.Messaging;

/// <summary>RabbitMQ 连接串，一律来自 <c>ConnectionStrings:RabbitMq</c>；未配置时为空串。</summary>
public sealed record RabbitMqOptions(string ConnectionString)
{
    public static RabbitMqOptions Load(IConfiguration configuration)
    {
        var value = configuration["ConnectionStrings:RabbitMq"];
        return new RabbitMqOptions(string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }
}

/// <summary>Redis 连接串，一律来自 <c>ConnectionStrings:Redis</c>；未配置时为空串。</summary>
public sealed record RedisOptions(string ConnectionString)
{
    public static RedisOptions Load(IConfiguration configuration)
    {
        var value = configuration["ConnectionStrings:Redis"];
        return new RedisOptions(string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }
}
