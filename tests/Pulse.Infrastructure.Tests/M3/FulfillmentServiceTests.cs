using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class FulfillmentServiceTests
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

        public long Seed(
            OrderStatus status,
            int available = 10,
            int reserved = 2,
            int quantity = 2)
        {
            var now = DateTime.UtcNow;
            var order = new Order
            {
                OrderNo = "PO-1",
                IdempotencyKey = "idem-1",
                TotalAmount = 10m * quantity,
                Status = status,
                CreatedAtUtc = now,
                PaidAtUtc = status == OrderStatus.Created ? null : now,
                ExpiresAtUtc = now.AddMinutes(15),
                Version = 0,
                Items =
                {
                    new OrderItem { Sku = "SKU-1", Quantity = quantity, UnitPrice = 10m }
                }
            };

            Db.Products.Add(new Product { Sku = "SKU-1", Name = "演示商品", Price = 10m });
            Db.Inventories.Add(new Inventory
            {
                Sku = "SKU-1",
                Available = available,
                Reserved = reserved,
                Version = 0
            });
            Db.Orders.Add(order);
            Db.SaveChanges();
            var id = order.Id;
            Db.ChangeTracker.Clear();
            return id;
        }

        public PulseDbContext NewDbContext() => new(
            new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private static OrderEvent PaidEvent() =>
        new() { OrderNo = "PO-1", Sku = "SKU-1", Quantity = 2 };

    [Fact]
    public async Task Paid_order_fulfillment_deducts_reserved_and_writes_deduct_ledger()
    {
        using var f = new Fixture();
        var orderId = f.Seed(OrderStatus.Paid);

        await new FulfillmentService(f.Db).FulfillAsync(PaidEvent());

        var order = await f.Db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Shipped, order.Status);

        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(10, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(1, inventory.Version);

        var ledger = await f.Db.InventoryLedgers.SingleAsync();
        Assert.Equal("Deduct", ledger.ChangeType);
        Assert.Equal(2, ledger.Quantity);
        Assert.Equal(orderId, ledger.RefOrderId);
    }

    [Fact]
    public async Task Repeat_fulfillment_of_same_event_does_not_double_deduct()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Paid);
        var sut = new FulfillmentService(f.Db);

        await sut.FulfillAsync(PaidEvent());
        await sut.FulfillAsync(PaidEvent());

        Assert.Equal(1, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(1, inventory.Version);
    }

    [Fact]
    public async Task Already_shipped_order_is_skipped_without_side_effects()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Shipped, available: 10, reserved: 0);

        await new FulfillmentService(f.Db).FulfillAsync(PaidEvent());

        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(0, inventory.Version);
    }

    [Fact]
    public async Task Created_order_fulfillment_throws_illegal_transition()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Created);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FulfillmentService(f.Db).FulfillAsync(PaidEvent()));

        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(2, inventory.Reserved);
    }

    [Fact]
    public async Task Stale_inventory_version_throws_concurrency_exception()
    {
        using var f = new Fixture();
        f.Seed(OrderStatus.Paid);
        using var sutDb = f.NewDbContext();
        _ = await sutDb.Inventories.SingleAsync(x => x.Sku == "SKU-1");
        _ = await sutDb.Orders.Include(o => o.Items).SingleAsync();

        using var racer = f.NewDbContext();
        var raced = await racer.Inventories.SingleAsync();
        raced.Version = 1;
        await racer.SaveChangesAsync();

        var sut = new FulfillmentService(sutDb);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => sut.FulfillAsync(PaidEvent()));
    }
}
