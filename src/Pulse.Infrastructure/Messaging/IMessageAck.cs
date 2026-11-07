namespace Pulse.Infrastructure.Messaging;

/// <summary>一条消费消息（消息信封 + 业务 payload）。FailureCount 为已重试次数。</summary>
public sealed record ConsumeMessage(
    string EventId,
    string RoutingKey,
    string Payload,
    int FailureCount = 0);

/// <summary>
/// 消费确认端口：成功 Ack、可重试失败重投 retry 队列、超限投 DLQ。
/// 真实实现由 RabbitMqConsumerHost 提供，测试用内存 fake。
/// </summary>
public interface IMessageAck
{
    Task AckAsync(ConsumeMessage message, CancellationToken cancellationToken = default);

    Task RequeueRetryAsync(
        ConsumeMessage message,
        string error,
        CancellationToken cancellationToken = default);

    Task PublishDeadAsync(
        ConsumeMessage message,
        string error,
        CancellationToken cancellationToken = default);
}
