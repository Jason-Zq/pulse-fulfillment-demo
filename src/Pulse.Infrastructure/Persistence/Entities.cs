using Pulse.Domain;

namespace Pulse.Infrastructure.Persistence;

public class Product
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Inventory
{
    public string Sku { get; set; } = string.Empty;
    public int Available { get; set; }
    public int Reserved { get; set; }
    public int Version { get; set; }
}

public class Order
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int Version { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Order? Order { get; set; }
}

public class InventoryLedger
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long? RefOrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class OutboxMessage
{
    public long Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}
