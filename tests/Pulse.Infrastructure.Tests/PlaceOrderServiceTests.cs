using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests;

public class PlaceOrderServiceTests
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
        }

        public void Seed(int available)
        {
            Db.Products.Add(new Product { Sku = "SKU-1", Name = "演示商品", Price = 99.5m });
            Db.Inventories.Add(new Inventory
            {
                Sku = "SKU-1",
                Available = available,
                Reserved = 0,
                Version = 0
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }

        public PulseDbContext NewDbContext() => new(
            new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Place_order_creates_order_reserves_stock_and_writes_outbox()
    {
        using var f = new Fixture();
        f.Seed(10);

        var result = await new PlaceOrderService(f.Db).PlaceAsync(
            new PlaceOrderCommand("SKU-1", 2, "idem-1"));

        Assert.False(string.IsNullOrWhiteSpace(result.OrderNo));
        Assert.Equal(OrderStatus.Created, result.Status);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
        Assert.Equal(1, await f.Db.OrderItems.CountAsync());
        Assert.Equal(1, await f.Db.InventoryLedgers.CountAsync());
        Assert.Equal(1, await f.Db.OutboxMessages.CountAsync());

        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(8, inventory.Available);
        Assert.Equal(2, inventory.Reserved);
        Assert.Equal(1, inventory.Version);

        var message = await f.Db.OutboxMessages.SingleAsync();
        Assert.Equal("order.created", message.RoutingKey);
        Assert.Equal("pending", message.Status);
    }

    [Fact]
    public async Task Insufficient_stock_throws_without_side_effects()
    {
        using var f = new Fixture();
        f.Seed(1);

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            new PlaceOrderService(f.Db).PlaceAsync(
                new PlaceOrderCommand("SKU-1", 2, "idem-1")));

        Assert.Equal(0, await f.Db.Orders.CountAsync());
        Assert.Equal(0, await f.Db.OutboxMessages.CountAsync());
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());

        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(1, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(0, inventory.Version);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_existing_order_without_double_reserve()
    {
        using var f = new Fixture();
        f.Seed(10);
        var sut = new PlaceOrderService(f.Db);

        var first = await sut.PlaceAsync(new PlaceOrderCommand("SKU-1", 2, "same-key"));
        var second = await sut.PlaceAsync(new PlaceOrderCommand("SKU-1", 2, "same-key"));

        Assert.Equal(first.OrderNo, second.OrderNo);
        Assert.Equal(1, await f.Db.Orders.CountAsync());
        Assert.Equal(1, await f.Db.OutboxMessages.CountAsync());

        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(8, inventory.Available);
        Assert.Equal(2, inventory.Reserved);
    }

    [Fact]
    public void Inventory_version_is_enforced_as_concurrency_token()
    {
        using var f = new Fixture();
        f.Seed(5);
        using var db1 = f.NewDbContext();
        using var db2 = f.NewDbContext();

        var first = db1.Inventories.Single(x => x.Sku == "SKU-1");
        var stale = db2.Inventories.Single(x => x.Sku == "SKU-1");

        first.Available = 4;
        first.Reserved = 1;
        first.Version = 1;
        db1.SaveChanges();

        stale.Available = 3;
        stale.Reserved = 2;
        stale.Version = 1;
        Assert.Throws<DbUpdateConcurrencyException>(() => db2.SaveChanges());
    }
}
