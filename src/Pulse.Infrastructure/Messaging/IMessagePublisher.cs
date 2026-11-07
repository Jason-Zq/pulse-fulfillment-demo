namespace Pulse.Infrastructure.Messaging;

/// <summary>一条待投递消息。</summary>
public sealed record PublishRequest(string EventId, string RoutingKey, string Payload);

/// <summary>publisher confirm 结果；Confirmed=false 表示未确认或投递失败。</summary>
public sealed record PublishResult(bool Confirmed, string? Error = null);

/// <summary>
/// 消息投递端口。真实实现（RabbitMQ.Client + publisher confirms）与内存 fake 均实现此接口，
/// 便于在不连真 broker 的情况下对 Outbox Publisher 做红绿测试。
/// </summary>
public interface IMessagePublisher
{
    Task<PublishResult> PublishAsync(
        PublishRequest request,
        CancellationToken cancellationToken = default);
}
