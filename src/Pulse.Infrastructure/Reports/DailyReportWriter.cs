using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Reports;

/// <summary>
/// M5 5.2：把 <see cref="DailyReportService"/> 的即时聚合结果落 <c>daily_reports</c>。
/// stat_date 为主键，已存在则更新（重跑幂等），口径与 GET /api/reports/daily 完全一致。
/// 供 Quartz 定时任务（Worker）与手工重放调用。
/// </summary>
public sealed class DailyReportWriter
{
    private readonly PulseDbContext _db;
    private readonly DailyReportService _report;

    public DailyReportWriter(PulseDbContext db, DailyReportService report)
    {
        _db = db;
        _report = report;
    }

    public async Task<DailyReportRecord> UpsertAsync(
        DateOnly statDate,
        CancellationToken cancellationToken = default)
    {
        var figures = await _report.GetAsync(statDate, cancellationToken);

        var record = await _db.DailyReportRecords
            .FindAsync(new object[] { statDate }, cancellationToken);
        if (record is null)
        {
            record = new DailyReportRecord
            {
                StatDate = statDate,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.DailyReportRecords.Add(record);
        }

        record.OrderCnt = figures.OrderCnt;
        record.PaidCnt = figures.PaidCnt;
        record.CancelCnt = figures.CancelCnt;
        record.Gmv = figures.Gmv;
        await _db.SaveChangesAsync(cancellationToken);
        return record;
    }
}
