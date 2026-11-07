using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Lab;

/// <summary>
/// M5 5.4：把运行态开关落在 lab_controls 表（key 主键，value = on/off），
/// 跨 API / Worker 两进程共享。Scoped（依赖 PulseDbContext）。
/// </summary>
public sealed class EfLabControlStore : ILabControlStore
{
    private readonly PulseDbContext _db;

    public EfLabControlStore(PulseDbContext db) => _db = db;

    public async Task<bool> IsEnabledAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var value = await _db.LabControls.AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);
        value ??= LabControlKeys.DefaultFor(key);
        return !string.Equals(value, LabControlKeys.Off, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetEnabledAsync(
        string key,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var value = enabled ? LabControlKeys.On : LabControlKeys.Off;
        var row = await _db.LabControls.FindAsync(new object[] { key }, cancellationToken);
        if (row is null)
        {
            row = new LabControl
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.LabControls.Add(row);
        }
        else
        {
            row.Value = value;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
