namespace Pulse.Infrastructure.Idempotency;

/// <summary>
/// 幂等短钥匙端口：SETNX 语义，首次占用返回 true，未过期重复返回 false。
/// 真实实现用 Redis；内存实现用于本地演示与红绿测试。
/// </summary>
public interface IIdempotencyGuard
{
    Task<bool> TryAcquireAsync(
        string token,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放本次失败投递占用的短钥匙，让重试消息能被真正处理。
    /// 只在业务失败且仍可重试时调用；成功或进入 DLQ 的事件保留键，避免重复消费。
    /// </summary>
    Task ReleaseAsync(
        string token,
        CancellationToken cancellationToken = default);
}
