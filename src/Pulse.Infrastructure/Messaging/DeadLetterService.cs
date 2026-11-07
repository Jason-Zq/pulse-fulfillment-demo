using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Messaging;

/// <summary>一条进入 DLQ 的失败消息（SPEC 第 5 节 dead_letters）。</summary>
public sealed record DeadLetterRecord(
    string OriginalEventId,
    string Payload,
    string Error,
    int RetryCount);

/// <summary>
/// 死信落库与人工补偿（A8 的逻辑层）：超限消息落 dead_letters，
/// RetryAsync 把记录置 handled 并写一条新的 pending outbox 供 OutboxPublisher 重投。
/// </summary>
public sealed class DeadLetterService
{
    public const string StatusPending = "pending";
    public const string StatusHandled = "handled";

    private readonly PulseDbContext _db;

    public DeadLetterService(PulseDbContext db) => _db = db;

    public async Task<DeadLetter> RecordAsync(
        DeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var entity = new DeadLetter
        {
            OriginalEventId = record.OriginalEventId,
            Payload = record.Payload,
            Error = record.Error,
            RetryCount = record.RetryCount,
            Status = StatusPending,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.DeadLetters.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<DeadLetter>> ListAsync(
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.DeadLetters.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken = default)
    {
        var letter = await _db.DeadLetters
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (letter is null)
        {
            return false;
        }

        // 已 handled 幂等返回，不重复写 outbox。
        if (letter.Status == StatusHandled)
        {
            return true;
        }

        letter.Status = StatusHandled;
        letter.HandledAtUtc = DateTime.UtcNow;

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = Guid.NewGuid().ToString("N"),
            RoutingKey = OrderTopology.RoutingKeyOrderPaid,
            Payload = letter.Payload,
            Status = "pending",
            RetryCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
