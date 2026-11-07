using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Reports;

public sealed record DailyReportReconciliation(
    decimal OrderAmount,
    decimal ItemAmount,
    int LedgerReserveQty,
    decimal Diff);

public sealed record DailyReportResult(
    string StatDate,
    int OrderCnt,
    int PaidCnt,
    int CancelCnt,
    decimal Gmv,
    DailyReportReconciliation Reconciliation);

/// <summary>
/// M4 4.3：日报即时聚合。按 UTC 自然日即时查询 orders / order_items / inventory_ledger
/// 并计算四方对账差异，不落 daily_reports 表、不引入 Quartz（日报持久化属 M5）。
/// 口径：以订单 CreatedAtUtc 落在 [00:00, 次日 00:00) 为当日集合；
///   paidCnt/gmv 统计其中「Paid 及之后」状态；
///   diff = |订单金额合计 - 明细金额合计| + |Reserve 流水数量 - 明细数量合计|。
/// </summary>
public sealed class DailyReportService
{
    private static readonly OrderStatus[] PaidStatuses =
    [
        OrderStatus.Paid,
        OrderStatus.Shipped,
        OrderStatus.Completed,
        OrderStatus.Refunding,
        OrderStatus.Refunded
    ];

    private readonly PulseDbContext _db;

    public DailyReportService(PulseDbContext db) => _db = db;

    public async Task<DailyReportResult> GetAsync(
        DateOnly statDate,
        CancellationToken cancellationToken = default)
    {
        var start = statDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var dayOrders = _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= start && o.CreatedAtUtc < end);

        // 即时查询后在内存聚合：避免依赖 SQLite provider 对 decimal 聚合 / Enum.ToString 的翻译，
        // 演示数据量下无性能问题，且不引入任何汇总表。
        var orders = await dayOrders
            .Select(o => new { o.Status, o.TotalAmount })
            .ToListAsync(cancellationToken);
        var orderCnt = orders.Count;
        var paidCnt = orders.Count(o => PaidStatuses.Contains(o.Status));
        var cancelCnt = orders.Count(o => o.Status == OrderStatus.Cancelled);
        var gmv = orders.Where(o => PaidStatuses.Contains(o.Status)).Sum(o => o.TotalAmount);
        var orderAmount = orders.Sum(o => o.TotalAmount);

        var dayOrderIds = dayOrders.Select(o => o.Id);
        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => dayOrderIds.Contains(i.OrderId))
            .Select(i => new { i.Quantity, i.UnitPrice })
            .ToListAsync(cancellationToken);
        var itemAmount = items.Sum(i => i.Quantity * i.UnitPrice);
        var itemQty = items.Sum(i => i.Quantity);

        var reserveQuantities = await _db.InventoryLedgers.AsNoTracking()
            .Where(l => l.ChangeType == "Reserve"
                && l.CreatedAtUtc >= start && l.CreatedAtUtc < end)
            .Select(l => l.Quantity)
            .ToListAsync(cancellationToken);
        var ledgerReserveQty = reserveQuantities.Sum();

        var diff = Math.Abs(orderAmount - itemAmount)
            + Math.Abs(ledgerReserveQty - itemQty);

        return new DailyReportResult(
            statDate.ToString("yyyy-MM-dd"),
            orderCnt,
            paidCnt,
            cancelCnt,
            gmv,
            new DailyReportReconciliation(
                orderAmount,
                itemAmount,
                ledgerReserveQty,
                diff));
    }
}
