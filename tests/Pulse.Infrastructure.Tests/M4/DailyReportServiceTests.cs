using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;
using Xunit;

namespace Pulse.Infrastructure.Tests.M4;

/// <summary>
/// M4 4.3：日报即时聚合（不落 daily_reports 表、不引入 Quartz）。
/// 口径：以订单 CreatedAtUtc 落在 UTC 自然日为基准；
///   orderCnt=当日订单数，paidCnt=当日已支付及之后状态数，cancelCnt=当日取消数，
///   gmv=当日已支付及之后订单金额合计；
///   reconciliation.orderAmount=当日订单金额合计，itemAmount=当日明细金额合计，
///   ledgerReserveQty=当日 Reserve 流水数量合计，
///   diff=|orderAmount-itemAmount| + |ledgerReserveQty-当日明细数量合计|。
/// </summary>
public class DailyReportServiceTests
{
    private static readonly DateTime T0 =
        new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public PulseDbContext Db { get; }

        public Fixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new PulseDbContext(
                new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
        }

        public long SeedOrder(
            string orderNo,
            OrderStatus status,
            DateTime createdAtUtc,
            decimal totalAmount,
            params (string Sku, int Qty, decimal UnitPrice)[] items)
        {
            var order = new Order
            {
                OrderNo = orderNo,
                IdempotencyKey = $"idem-{orderNo}",
                TotalAmount = totalAmount,
                Status = status,
                CreatedAtUtc = createdAtUtc,
                PaidAtUtc = status == OrderStatus.Created ? null : createdAtUtc.AddMinutes(1),
                ExpiresAtUtc = createdAtUtc.AddMinutes(15),
                Version = 0
            };
            foreach (var (sku, qty, unitPrice) in items)
            {
                order.Items.Add(new OrderItem { Sku = sku, Quantity = qty, UnitPrice = unitPrice });
            }

            Db.Orders.Add(order);
            Db.SaveChanges();
            var id = order.Id;
            Db.ChangeTracker.Clear();
            return id;
        }

        public void SeedReserveLedger(string sku, int qty, DateTime createdAtUtc, long? refOrderId = null)
        {
            Db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = sku,
                ChangeType = "Reserve",
                Quantity = qty,
                RefOrderId = refOrderId,
                CreatedAtUtc = createdAtUtc
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task With_data_day_aggregates_all_metrics_and_zero_diff()
    {
        using var f = new Fixture();
        var day = T0.AddHours(12);
        var a = f.SeedOrder("A", OrderStatus.Paid, day, 20m, ("SKU-1", 2, 10m));
        var b = f.SeedOrder("B", OrderStatus.Shipped, day, 30m, ("SKU-2", 3, 10m));
        var c = f.SeedOrder("C", OrderStatus.Cancelled, day, 10m, ("SKU-1", 1, 10m));
        f.SeedReserveLedger("SKU-1", 2, day, a);
        f.SeedReserveLedger("SKU-2", 3, day, b);
        f.SeedReserveLedger("SKU-1", 1, day, c);
        // 前一日数据不应计入当日。
        var prev = T0.AddDays(-1).AddHours(12);
        f.SeedOrder("PREV", OrderStatus.Paid, prev, 999m, ("SKU-9", 9, 111m));
        f.SeedReserveLedger("SKU-9", 9, prev);

        var result = await new DailyReportService(f.Db).GetAsync(new DateOnly(2026, 11, 7));

        Assert.Equal("2026-11-07", result.StatDate);
        Assert.Equal(3, result.OrderCnt);
        Assert.Equal(2, result.PaidCnt);      // Paid + Shipped
        Assert.Equal(1, result.CancelCnt);
        Assert.Equal(50m, result.Gmv);        // 20 + 30（仅已支付及之后）
        Assert.Equal(60m, result.Reconciliation.OrderAmount);      // 20 + 30 + 10
        Assert.Equal(60m, result.Reconciliation.ItemAmount);       // 20 + 30 + 10
        Assert.Equal(6, result.Reconciliation.LedgerReserveQty);  // 2 + 3 + 1
        Assert.Equal(0m, result.Reconciliation.Diff);
    }

    [Fact]
    public async Task Empty_day_returns_all_zero_and_zero_diff()
    {
        using var f = new Fixture();
        f.SeedOrder("A", OrderStatus.Paid, T0.AddHours(12), 20m, ("SKU-1", 2, 10m));
        f.SeedReserveLedger("SKU-1", 2, T0.AddHours(12));

        var result = await new DailyReportService(f.Db).GetAsync(new DateOnly(2026, 11, 8));

        Assert.Equal("2026-11-08", result.StatDate);
        Assert.Equal(0, result.OrderCnt);
        Assert.Equal(0, result.PaidCnt);
        Assert.Equal(0, result.CancelCnt);
        Assert.Equal(0m, result.Gmv);
        Assert.Equal(0m, result.Reconciliation.OrderAmount);
        Assert.Equal(0m, result.Reconciliation.ItemAmount);
        Assert.Equal(0, result.Reconciliation.LedgerReserveQty);
        Assert.Equal(0m, result.Reconciliation.Diff);
    }

    [Fact]
    public async Task Mismatched_order_amount_produces_positive_diff()
    {
        using var f = new Fixture();
        var day = T0.AddHours(12);
        // 订单金额 20，但明细金额只有 15 → 金额差 5；数量一致 → 数量差 0。
        var id = f.SeedOrder("A", OrderStatus.Paid, day, 20m, ("SKU-1", 1, 15m));
        f.SeedReserveLedger("SKU-1", 1, day, id);

        var result = await new DailyReportService(f.Db).GetAsync(new DateOnly(2026, 11, 7));

        Assert.Equal(20m, result.Reconciliation.OrderAmount);
        Assert.Equal(15m, result.Reconciliation.ItemAmount);
        Assert.Equal(5m, result.Reconciliation.Diff);
    }

    [Fact]
    public async Task Mismatched_reserve_quantity_produces_positive_diff()
    {
        using var f = new Fixture();
        var day = T0.AddHours(12);
        // 明细数量 2，但没有 Reserve 流水 → 数量差 2；金额一致 → 金额差 0。
        f.SeedOrder("A", OrderStatus.Paid, day, 20m, ("SKU-1", 2, 10m));

        var result = await new DailyReportService(f.Db).GetAsync(new DateOnly(2026, 11, 7));

        Assert.Equal(0, result.Reconciliation.LedgerReserveQty);
        Assert.Equal(0m, result.Reconciliation.OrderAmount - result.Reconciliation.ItemAmount);
        Assert.Equal(2m, result.Reconciliation.Diff);
    }

    [Fact]
    public async Task Only_orders_and_ledgers_inside_the_utc_natural_day_are_counted()
    {
        using var f = new Fixture();
        // 11-06 23:59 UTC 不计入 11-07；11-07 00:00 UTC 计入。
        var before = T0.AddMinutes(-1);
        var atStart = T0;
        f.SeedOrder("BEFORE", OrderStatus.Paid, before, 10m, ("SKU-1", 1, 10m));
        f.SeedReserveLedger("SKU-1", 1, before);
        var id = f.SeedOrder("ATSTART", OrderStatus.Paid, atStart, 10m, ("SKU-1", 1, 10m));
        f.SeedReserveLedger("SKU-1", 1, atStart, id);

        var result = await new DailyReportService(f.Db).GetAsync(new DateOnly(2026, 11, 7));

        Assert.Equal(1, result.OrderCnt);
        Assert.Equal(10m, result.Reconciliation.OrderAmount);
        Assert.Equal(1, result.Reconciliation.LedgerReserveQty);
        Assert.Equal(0m, result.Reconciliation.Diff);
    }
}
