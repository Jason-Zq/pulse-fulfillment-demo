namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// M5 5.5：4 个业务队列的当前深度快照。
/// 连接不可用 / 未配置时 <see cref="Available"/> = false，各深度返回 -1（不伪造 0）。
/// </summary>
public sealed record QueueDepthSnapshot(
    long Fulfillment,
    long OrderTimeout,
    long FulfillmentRetry,
    long Dlq,
    bool Available)
{
    public static QueueDepthSnapshot Unavailable() => new(-1, -1, -1, -1, false);
}

/// <summary>队列深度端口：真实实现用 RabbitMQ passive declare，测试用内存 fake。</summary>
public interface IQueueDepthProvider
{
    Task<QueueDepthSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
