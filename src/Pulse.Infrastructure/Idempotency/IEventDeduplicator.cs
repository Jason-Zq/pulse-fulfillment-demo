namespace Pulse.Infrastructure.Idempotency;

/// <summary>消费端判重结果：Proceed=首次可处理；Duplicate=重复消息应跳过。</summary>
public enum DedupDecision
{
    Proceed,
    Duplicate
}

/// <summary>
/// 消费端幂等双保险端口：Redis 短钥匙 dedup:{event_id}（SETNX，TTL 24h）
/// + processed_events 唯一键最终保障（SPEC 第 6 节）。
/// </summary>
public interface IEventDeduplicator
{
    Task<DedupDecision> TryBeginAsync(
        string eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把 ProcessedEvent 加入当前 DbContext 并保存；与同一 DbContext 上未提交的业务变更
    /// 落在同一次 SaveChanges（同事务），保证业务生效即留下幂等标记。
    /// </summary>
    Task MarkProcessedAsync(
        string eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 业务失败且仍可重试时释放 Redis 短钥匙；否则重试消息会被判重直接 ack，
    /// 三次重试 / DLQ 闭环不成立（C 层 Docker 联调发现的真实缺陷）。
    /// </summary>
    Task ReleaseAsync(
        string eventId,
        CancellationToken cancellationToken = default);
}
