using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Messaging;

/// <summary>一次批量投递的结果汇总，用于日志与实验台指标。</summary>
public sealed record OutboxPublishSummary(int Attempted, int Published, int Retried, int Failed);

/// <summary>
/// 事务性 outbox 的投递端（SPEC 第 6 节）：读取 pending 消息投递到 MQ，
/// 依据 publisher confirm 结果回写状态；confirm 失败或异常只累加重试计数并保留 pending，
/// 达到上限才置 failed，等待人工/补偿处理。
/// </summary>
public sealed class OutboxPublisher
{
    public const string StatusPending = "pending";
    public const string StatusPublished = "published";
    public const string StatusFailed = "failed";

    /// <summary>同一条 outbox 消息的最大投递尝试次数。</summary>
    public const int MaxPublishAttempts = 3;

    private readonly PulseDbContext _db;
    private readonly IMessagePublisher _publisher;

    public OutboxPublisher(PulseDbContext db, IMessagePublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<OutboxPublishSummary> PublishPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "批量大小必须大于 0");
        }

        var pending = await _db.OutboxMessages
            .Where(m => m.Status == StatusPending)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        var retried = 0;
        var failed = 0;
        var now = DateTime.UtcNow;

        foreach (var message in pending)
        {
            PublishResult result;
            try
            {
                result = await _publisher.PublishAsync(
                    new PublishRequest(message.EventId, message.RoutingKey, message.Payload),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                result = new PublishResult(false, ex.Message);
            }

            if (result.Confirmed)
            {
                message.Status = StatusPublished;
                message.PublishedAtUtc = now;
                published++;
                continue;
            }

            message.RetryCount += 1;
            if (message.RetryCount >= MaxPublishAttempts)
            {
                message.Status = StatusFailed;
                failed++;
            }
            else
            {
                // 保持 pending，下一轮继续补投（A4：停 MQ 恢复后补投）。
                retried++;
            }
        }

        if (pending.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new OutboxPublishSummary(pending.Count, published, retried, failed);
    }
}
