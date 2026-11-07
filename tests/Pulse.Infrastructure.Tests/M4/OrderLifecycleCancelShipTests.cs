using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M4;

/// <summary>
/// M4 4.1：订单取消与发货。状态流转只走 OrderStateMachine.Transition；
/// 取消释放预扣并写 Release 流水，业务变更与流水同事务。
/// </summary>
public class OrderLifecycleCancelShipTests
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

        /// <summary>造一个已预扣 qty 的订单（available=8, reserved=2 对应 qty=2）。</summary>
        public long Seed(OrderStatus status, int available = 8, int reserved = 2, int quantity = 2)
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

    [Fact]
    public async Task Cancel_created_order_releases_reservation_and_writes_release_ledger()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created);

        var status = await new OrderLifecycleService(f.Db).CancelAsync(id);

        Assert.Equal(OrderStatus.Cancelled, status);
        Assert.Equal(OrderStatus.Cancelled, (await f.Db.Orders.SingleAsync()).Status);
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(10, inventory.Available);   // 8 + 2
        Assert.Equal(0, inventory.Reserved);     // 2 - 2
        Assert.Equal(1, inventory.Version);      // 乐观锁版本递增
        var ledger = await f.Db.InventoryLedgers.SingleAsync();
        Assert.Equal("Release", ledger.ChangeType);
        Assert.Equal(2, ledger.Quantity);
        Assert.Equal(id, ledger.RefOrderId);
    }

    [Fact]
    public async Task Repeat_cancel_is_idempotent_without_double_release()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created);
        var sut = new OrderLifecycleService(f.Db);

        var first = await sut.CancelAsync(id);
        var second = await sut.CancelAsync(id);

        Assert.Equal(OrderStatus.Cancelled, first);
        Assert.Equal(OrderStatus.Cancelled, second);
        Assert.Equal(1, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(10, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(1, inventory.Version);
    }

    [Fact]
    public async Task Ship_paid_order_transitions_to_shipped_without_double_stock_mutation()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Paid);

        var status = await new OrderLifecycleService(f.Db).ShipAsync(id);

        Assert.Equal(OrderStatus.Shipped, status);
        Assert.Equal(OrderStatus.Shipped, (await f.Db.Orders.SingleAsync()).Status);
        // M4 发货端点只做状态流转；实扣由 M3 FulfillmentService 承担，不在此重复扣减 / 写流水。
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
        var inventory = await f.Db.Inventories.SingleAsync();
        Assert.Equal(8, inventory.Available);
        Assert.Equal(2, inventory.Reserved);
        Assert.Equal(0, inventory.Version);
    }

    [Fact]
    public async Task Ship_created_order_throws_illegal_transition()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).ShipAsync(id));

        Assert.Contains("非法", ex.Message);
        Assert.Equal(OrderStatus.Created, (await f.Db.Orders.SingleAsync()).Status);
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Cancel_shipped_order_throws_illegal_transition()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Shipped);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).CancelAsync(id));

        Assert.Contains("非法", ex.Message);
        Assert.Equal(OrderStatus.Shipped, (await f.Db.Orders.SingleAsync()).Status);
        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Cancel_missing_order_throws()
    {
        using var f = new Fixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).CancelAsync(999));

        Assert.Equal(0, await f.Db.InventoryLedgers.CountAsync());
    }

    [Fact]
    public async Task Ship_missing_order_throws()
    {
        using var f = new Fixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderLifecycleService(f.Db).ShipAsync(999));
    }

    [Fact]
    public async Task Stale_inventory_version_during_cancel_throws_concurrency_exception()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Created);
        using var sutDb = f.NewDbContext();
        // sutDb 先追踪订单与库存（version=0），模拟并发下持有陈旧实体。
        _ = await sutDb.Orders.Include(o => o.Items).SingleAsync(o => o.Id == id);
        _ = await sutDb.Inventories.SingleAsync(x => x.Sku == "SKU-1");

        using var racer = f.NewDbContext();
        var raced = await racer.Inventories.SingleAsync();
        raced.Version = 1;
        await racer.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            new OrderLifecycleService(sutDb).CancelAsync(id));
    }

    [Fact]
    public async Task Stale_order_version_during_ship_throws_concurrency_exception()
    {
        using var f = new Fixture();
        var id = f.Seed(OrderStatus.Paid);
        using var sutDb = f.NewDbContext();
        _ = await sutDb.Orders.Include(o => o.Items).SingleAsync(o => o.Id == id);

        using var racer = f.NewDbContext();
        var raced = await racer.Orders.SingleAsync(o => o.Id == id);
        raced.Version = 1;
        await racer.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            new OrderLifecycleService(sutDb).ShipAsync(id));
    }
}
