using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M6;

/// <summary>
/// M6b 6.4 RED：PlaceOrderService 需要一条与单 SKU 路径并存的多明细路径。
/// 单 SKU 既有测试（PlaceOrderServiceTests）一字不改仍须全绿。
/// </summary>
public class PlaceOrderMultiItemTests
{
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
            Db.Products.AddRange(
                new Product { Sku = "SKU-1", Name = "演示商品 A", Price = 99.5m },
                new Product { Sku = "SKU-2", Name = "演示商品 B", Price = 199m });
            Db.Inventories.AddRange(
                new Inventory { Sku = "SKU-1", Available = 100, Reserved = 0, Version = 0 },
                new Inventory { Sku = "SKU-2", Available = 50, Reserved = 0, Version = 0 });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private static PlaceOrderLine Line(string sku, int qty) => new(sku, qty);

    [Fact]
    public async Task Multi_item_order_writes_all_details_ledgers_and_one_outbox()
    {
        using var f = new Fixture();
        var result = await new PlaceOrderService(f.Db).PlaceMultiAsync(
            new PlaceMultiOrderCommand(
                new[] { Line("SKU-1", 2), Line("SKU-2", 3) }, "idem-multi-1"));

        Assert.False(string.IsNullOrWhiteSpace(result.OrderNo));
        Assert.Equal(2, await f.Db.OrderItems.CountAsync());
        Assert.Equal(2, await f.Db.InventoryLedgers.CountAsync(l => l.ChangeType == "Reserve"));
        Assert.Equal(1, await f.Db.OutboxMessages.CountAsync(m => m.RoutingKey == "order.created"));
        Assert.Equal(99.5m * 2 + 199m * 3, (await f.Db.Orders.SingleAsync()).TotalAmount);
        Assert.Equal(98, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
        Assert.Equal(47, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-2")).Available);
        Assert.Equal(1, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Version);
        Assert.Equal(1, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-2")).Version);
    }

    [Fact]
    public async Task Duplicate_sku_lines_are_merged_into_one_detail()
    {
        using var f = new Fixture();
        await new PlaceOrderService(f.Db).PlaceMultiAsync(
            new PlaceMultiOrderCommand(
                new[] { Line("SKU-1", 1), Line("SKU-1", 2) }, "idem-merge-1"));

        Assert.Equal(1, await f.Db.OrderItems.CountAsync());
        Assert.Equal(3, (await f.Db.OrderItems.SingleAsync()).Quantity);
        Assert.Equal(97, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_existing_order_without_double_reserve()
    {
        using var f = new Fixture();
        var sut = new PlaceOrderService(f.Db);
        var command = new PlaceMultiOrderCommand(
            new[] { Line("SKU-1", 2), Line("SKU-2", 1) }, "idem-multi-same");

        var first = await sut.PlaceMultiAsync(command);
        var second = await sut.PlaceMultiAsync(command);

        Assert.Equal(first.OrderNo, second.OrderNo);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
        Assert.Equal(2, await f.Db.OrderItems.CountAsync());
        Assert.Equal(1, await f.Db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Unknown_sku_throws_without_side_effects()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PlaceOrderService(f.Db).PlaceMultiAsync(
                new PlaceMultiOrderCommand(
                    new[] { Line("SKU-1", 1), Line("NO-SUCH-SKU", 1) }, "idem-unknown")));

        Assert.Equal(0, await f.Db.Orders.CountAsync());
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
        Assert.Equal(100, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
    }

    [Fact]
    public async Task Insufficient_stock_on_second_line_rolls_back_all_lines()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            new PlaceOrderService(f.Db).PlaceMultiAsync(
                new PlaceMultiOrderCommand(
                    new[] { Line("SKU-1", 1), Line("SKU-2", 999999) }, "idem-short")));

        Assert.Equal(0, await f.Db.Orders.CountAsync());
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
        Assert.Equal(100, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Available);
        Assert.Equal(50, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-2")).Available);
    }
}
