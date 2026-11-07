using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M6;

/// <summary>
/// M6b 补充 RED：SPEC v0.2 允许一笔订单多明细后，履约实扣与超时释放必须按
/// order_items 的事实源逐行处理，不能只按 payload 的单个 sku/quantity。
/// 单 SKU 订单行为不变（一个明细 → 一次扣减 / 一条流水）。
/// </summary>
public class MultiItemConsumptionTests
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

    /// <summary>直接落一笔两明细的 Created 订单 + 两行预扣，模拟 M6b 下单产物。</summary>
    private static async Task<Order> SeedTwoItemOrder(PulseDbContext db, string orderNo)
    {
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderNo = orderNo,
            IdempotencyKey = orderNo,
            TotalAmount = 99.5m * 2 + 199m * 3,
            Status = OrderStatus.Created,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(-1),
            Version = 0,
            Items =
            {
                new OrderItem { Sku = "SKU-1", Quantity = 2, UnitPrice = 99.5m },
                new OrderItem { Sku = "SKU-2", Quantity = 3, UnitPrice = 199m }
            }
        };
        db.Orders.Add(order);

        var lines = new[] { ("SKU-1", 2), ("SKU-2", 3) };
        foreach (var (sku, qty) in lines)
        {
            var inv = await db.Inventories.SingleAsync(i => i.Sku == sku);
            inv.Available -= qty;
            inv.Reserved += qty;
            inv.Version += 1;
            db.InventoryLedgers.Add(new InventoryLedger
            {
                Sku = sku, ChangeType = "Reserve", Quantity = qty, CreatedAtUtc = now
            });
        }

        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Fulfillment_deducts_every_item_of_multi_item_order()
    {
        using var f = new Fixture();
        var order = await SeedTwoItemOrder(f.Db, "MULTI-FULFILL");
        order.Status = OrderStatus.Paid;
        await f.Db.SaveChangesAsync();

        // payload 仍只带第一个 SKU（向后兼容 M1/M3 单 SKU 消息形状）。
        await new FulfillmentService(f.Db).FulfillAsync(new OrderEvent
        {
            OrderNo = order.OrderNo,
            Sku = "SKU-1",
            Quantity = 2
        });

        var inv1 = await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1");
        var inv2 = await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-2");
        Assert.Equal(0, inv1.Reserved);
        Assert.Equal(0, inv2.Reserved);
        Assert.Equal(2, await f.Db.InventoryLedgers.CountAsync(l => l.ChangeType == "Deduct"));
        Assert.Equal(OrderStatus.Shipped, (await f.Db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Timeout_releases_every_item_of_multi_item_order()
    {
        using var f = new Fixture();
        var order = await SeedTwoItemOrder(f.Db, "MULTI-TIMEOUT");

        var closed = await new TimeoutOrderService(f.Db).CloseExpiredAsync(new OrderEvent
        {
            OrderNo = order.OrderNo,
            Sku = "SKU-1",
            Quantity = 2
        });

        Assert.True(closed);
        var inv1 = await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1");
        var inv2 = await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-2");
        Assert.Equal(100, inv1.Available);
        Assert.Equal(50, inv2.Available);
        Assert.Equal(0, inv1.Reserved);
        Assert.Equal(0, inv2.Reserved);
        Assert.Equal(2, await f.Db.InventoryLedgers.CountAsync(l => l.ChangeType == "Release"));
    }

    [Fact]
    public async Task Single_item_order_still_writes_exactly_one_deduct_ledger()
    {
        using var f = new Fixture();
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderNo = "SINGLE-FULFILL",
            IdempotencyKey = "SINGLE-FULFILL",
            TotalAmount = 99.5m,
            Status = OrderStatus.Paid,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15),
            Items = { new OrderItem { Sku = "SKU-1", Quantity = 1, UnitPrice = 99.5m } }
        };
        f.Db.Orders.Add(order);
        var inv = await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1");
        inv.Available -= 1;
        inv.Reserved += 1;
        await f.Db.SaveChangesAsync();

        await new FulfillmentService(f.Db).FulfillAsync(new OrderEvent
        {
            OrderNo = order.OrderNo, Sku = "SKU-1", Quantity = 1
        });

        Assert.Equal(1, await f.Db.InventoryLedgers.CountAsync(l => l.ChangeType == "Deduct"));
        Assert.Equal(0, (await f.Db.Inventories.SingleAsync(i => i.Sku == "SKU-1")).Reserved);
    }
}
