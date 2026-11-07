using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.2：DailyReportWriter.UpsertAsync 复用 DailyReportService 口径，
/// stat_date 已存在则更新，重跑幂等（两次 upsert 只有 1 行且数值为最新）。
/// </summary>
public class DailyReportWriterTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

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

        public DailyReportWriter Writer() => new(Db, new DailyReportService(Db));

        public void SeedOrder(string orderNo, OrderStatus status, decimal amount, DateTime created, int qty = 1)
        {
            Db.Orders.Add(new Order
            {
                OrderNo = orderNo,
                IdempotencyKey = $"idem-{orderNo}",
                TotalAmount = amount,
                Status = status,
                CreatedAtUtc = created,
                PaidAtUtc = status == OrderStatus.Created ? null : created.AddMinutes(1),
                ExpiresAtUtc = created.AddMinutes(15),
                Version = 0,
                Items = { new OrderItem { Sku = "SKU-1", Quantity = qty, UnitPrice = amount / qty } }
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
    public async Task Upsert_writes_one_row_and_reuses_DailyReportService_figures()
    {
        using var f = new Fixture();
        var day = new DateOnly(2026, 11, 7);
        var at = T0.AddHours(12);
        f.SeedOrder("A", OrderStatus.Paid, 20m, at);
        f.SeedOrder("B", OrderStatus.Cancelled, 10m, at);

        var record = await f.Writer().UpsertAsync(day);

        Assert.Equal(day, record.StatDate);
        Assert.Equal(2, record.OrderCnt);
        Assert.Equal(1, record.PaidCnt);
        Assert.Equal(1, record.CancelCnt);
        Assert.Equal(20m, record.Gmv);
        Assert.Equal(1, await f.Db.DailyReportRecords.CountAsync());
    }

    [Fact]
    public async Task Upsert_twice_is_idempotent_and_keeps_only_one_row_with_latest_values()
    {
        using var f = new Fixture();
        var day = new DateOnly(2026, 11, 7);
        var at = T0.AddHours(12);
        f.SeedOrder("A", OrderStatus.Paid, 20m, at);

        await f.Writer().UpsertAsync(day);

        // 同一天新增订单后重跑：行数仍为 1，数值刷新为最新。
        f.SeedOrder("B", OrderStatus.Paid, 30m, at);
        f.SeedOrder("C", OrderStatus.Shipped, 40m, at);
        var second = await f.Writer().UpsertAsync(day);

        Assert.Equal(1, await f.Db.DailyReportRecords.CountAsync());
        Assert.Equal(3, second.OrderCnt);
        Assert.Equal(90m, second.Gmv);
        var stored = await f.Db.DailyReportRecords.AsNoTracking().SingleAsync();
        Assert.Equal(3, stored.OrderCnt);
        Assert.Equal(90m, stored.Gmv);
    }

    [Fact]
    public async Task Upsert_empty_day_writes_zero_row()
    {
        using var f = new Fixture();
        var day = new DateOnly(2026, 11, 8);

        var record = await f.Writer().UpsertAsync(day);

        Assert.Equal(0, record.OrderCnt);
        Assert.Equal(0m, record.Gmv);
        Assert.Equal(1, await f.Db.DailyReportRecords.CountAsync());
    }
}
