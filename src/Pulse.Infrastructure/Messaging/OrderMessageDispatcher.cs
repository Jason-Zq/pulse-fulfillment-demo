using System.Text.Json;
using Pulse.Infrastructure.Idempotency;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 消费者编排（SPEC 第 6 节）：按 routing key 分发 → 幂等包裹 → 业务处理 →
/// 成功 Ack / 可重试失败 RequeueRetry / 超限 PublishDead。
/// 只对窄端口 IEventDeduplicator / IRetryDecider / IMessageAck 编程，便于内存 fake 红绿测试。
/// </summary>
public sealed class OrderMessageDispatcher
{
    private readonly IEventDeduplicator _deduplicator;
    private readonly IRetryDecider _retryDecider;
    private readonly IMessageAck _ack;
    private readonly IReadOnlyDictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>> _handlers;

    public OrderMessageDispatcher(
        IEventDeduplicator deduplicator,
        IRetryDecider retryDecider,
        IMessageAck ack,
        IReadOnlyDictionary<EventRoute, Func<OrderEvent, CancellationToken, Task>> handlers)
    {
        _deduplicator = deduplicator;
        _retryDecider = retryDecider;
        _ack = ack;
        _handlers = handlers;
    }

    public async Task DispatchAsync(
        ConsumeMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var route = EventRoutes.Resolve(message.RoutingKey);

        // 未知 key / 未配置处理器：Ack 丢弃，避免毒消息阻塞队列。
        if (route == EventRoute.Unsupported || !_handlers.TryGetValue(route, out var handler))
        {
            await _ack.AckAsync(message, cancellationToken);
            return;
        }

        // 幂等包裹：重复消息直接 Ack，业务处理器不执行。
        if (await _deduplicator.TryBeginAsync(message.EventId, cancellationToken)
            == DedupDecision.Duplicate)
        {
            await _ack.AckAsync(message, cancellationToken);
            return;
        }

        try
        {
            var orderEvent = Parse(message);
            await handler(orderEvent, cancellationToken);
            await _deduplicator.MarkProcessedAsync(message.EventId, cancellationToken);
        }
        catch (Exception ex)
        {
            var decision = _retryDecider.Decide(message.FailureCount + 1);
            if (decision == RetryDecision.Retry)
            {
                // 先释放本次占用的 dedup:{event_id}，否则 TTL+DLX 回投的重试消息
                // 会被 TryBeginAsync 判重后直接 ack，重试次数永远停在 1。
                await _deduplicator.ReleaseAsync(message.EventId, cancellationToken);
                await _ack.RequeueRetryAsync(message, ex.Message, cancellationToken);
            }
            else
            {
                // 终态保留短钥匙：该事件不再处理，补偿台负责人工介入。
                await _ack.PublishDeadAsync(message, ex.Message, cancellationToken);
            }

            return;
        }

        await _ack.AckAsync(message, cancellationToken);
    }

    /// <summary>单条失败（含 ack 异常）不影响批次其余消息。</summary>
    public async Task DispatchBatchAsync(
        IEnumerable<ConsumeMessage> messages,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            try
            {
                await DispatchAsync(message, cancellationToken);
            }
            catch (Exception)
            {
                // 单条失败只记录、不退出批次；真实宿主另有日志。
            }
        }
    }

    private static OrderEvent Parse(ConsumeMessage message)
    {
        var orderEvent = string.IsNullOrWhiteSpace(message.Payload)
            ? new OrderEvent()
            : JsonSerializer.Deserialize<OrderEvent>(message.Payload) ?? new OrderEvent();
        return orderEvent with
        {
            EventId = message.EventId,
            RoutingKey = message.RoutingKey
        };
    }
}
