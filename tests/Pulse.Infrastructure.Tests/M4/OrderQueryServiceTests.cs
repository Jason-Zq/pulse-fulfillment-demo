using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M4;

/// <summary>
/// M4 4.2：订单列表查询——status / sku / 时间范围筛选 + 分页，按 CreatedAtUtc 倒序。
/// </summary>
public class OrderQueryServiceTests
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

        public long Seed(
            string orderNo,
            OrderStatus status,
            string sku,
            DateTime createdAtUtc,
            int itemCount = 1)
        {
            var order = new Order
            {
                OrderNo = orderNo,
                IdempotencyKey = $"idem-{orderNo}",
                TotalAmount = 10m * itemCount,
                Status = status,
                CreatedAtUtc = createdAtUtc,
                PaidAtUtc = status == OrderStatus.Created ? null : createdAtUtc.AddMinutes(1),
                ExpiresAtUtc = createdAtUtc.AddMinutes(15),
                Version = 0
            };
            for (var i = 0; i < itemCount; i++)
            {
                order.Items.Add(new OrderItem { Sku = sku, Quantity = 1, UnitPrice = 10m });
            }

            Db.Orders.Add(order);
            Db.SaveChanges();
            var id = order.Id;
            Db.ChangeTracker.Clear();
            return id;
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private static OrderListQuery All() => new(null, null, null, null, 1, 20);

    [Fact]
    public async Task List_returns_all_orders_in_created_at_desc_order_with_projected_fields()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Created, "SKU-1", T0.AddHours(1));
        f.Seed("B", OrderStatus.Paid, "SKU-2", T0.AddHours(3));
        f.Seed("C", OrderStatus.Cancelled, "SKU-1", T0.AddHours(2));

        var result = await new OrderQueryService(f.Db).ListAsync(All());

        Assert.Equal(3, result.Total);
        Assert.Equal(["B", "C", "A"], result.Items.Select(x => x.OrderNo));
        Assert.Equal("Paid", result.Items[0].Status);
        Assert.Equal("SKU-2", result.Items[0].Sku);
        Assert.Equal(10m, result.Items[0].TotalAmount);
        Assert.Equal(T0.AddHours(3), result.Items[0].CreatedAtUtc);
    }

    [Fact]
    public async Task Filter_by_status_returns_only_matching_orders()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Paid, "SKU-1", T0.AddHours(1));
        f.Seed("B", OrderStatus.Paid, "SKU-1", T0.AddHours(2));
        f.Seed("C", OrderStatus.Created, "SKU-1", T0.AddHours(3));

        var result = await new OrderQueryService(f.Db).ListAsync(All() with { Status = "Paid" });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, x => Assert.Equal("Paid", x.Status));
    }

    [Fact]
    public async Task Filter_by_sku_does_not_double_count_multi_item_orders()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Created, "SKU-1", T0.AddHours(1), itemCount: 2);
        f.Seed("B", OrderStatus.Paid, "SKU-1", T0.AddHours(2));
        f.Seed("C", OrderStatus.Created, "SKU-2", T0.AddHours(3));

        var result = await new OrderQueryService(f.Db).ListAsync(All() with { Sku = "SKU-1" });

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, x => Assert.Equal("SKU-1", x.Sku));
    }

    [Fact]
    public async Task Filter_by_time_range_returns_only_orders_in_window()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Created, "SKU-1", T0.AddHours(1));
        f.Seed("B", OrderStatus.Paid, "SKU-1", T0.AddHours(5));
        f.Seed("C", OrderStatus.Created, "SKU-1", T0.AddHours(10));

        var result = await new OrderQueryService(f.Db).ListAsync(
            All() with { FromUtc = T0.AddHours(2), ToUtc = T0.AddHours(8) });

        Assert.Equal(1, result.Total);
        Assert.Equal("B", result.Items[0].OrderNo);
    }

    [Fact]
    public async Task Pagination_returns_correct_total_and_requested_page()
    {
        using var f = new Fixture();
        for (var i = 1; i <= 5; i++)
        {
            f.Seed($"P{i}", OrderStatus.Created, "SKU-1", T0.AddHours(i));
        }

        var result = await new OrderQueryService(f.Db).ListAsync(
            new OrderListQuery(null, null, null, null, 2, 2));

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
        // 倒序：P5,P4 | P3,P2 | P1，第 2 页应为 P3,P2。
        Assert.Equal(["P3", "P2"], result.Items.Select(x => x.OrderNo));
    }

    [Fact]
    public async Task Out_of_range_page_returns_empty_items_but_correct_total()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Created, "SKU-1", T0.AddHours(1));
        f.Seed("B", OrderStatus.Created, "SKU-1", T0.AddHours(2));
        f.Seed("C", OrderStatus.Created, "SKU-1", T0.AddHours(3));

        var result = await new OrderQueryService(f.Db).ListAsync(
            new OrderListQuery(null, null, null, null, 99, 2));

        Assert.Equal(3, result.Total);
        Assert.Equal(99, result.Page);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Invalid_page_and_page_size_fall_back_to_defaults()
    {
        using var f = new Fixture();
        f.Seed("A", OrderStatus.Created, "SKU-1", T0.AddHours(1));

        var result = await new OrderQueryService(f.Db).ListAsync(
            new OrderListQuery(null, null, null, null, 0, 0));

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Single(result.Items);
    }
}
