using Pulse.Domain;

namespace Pulse.Infrastructure.Persistence;

public class Product
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public string Category { get; set; } = string.Empty;
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

/// <summary>消费端幂等的最终保障：event_id 为主键（SPEC 第 5 节 processed_events）。</summary>
public class ProcessedEvent
{
    public string EventId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}

/// <summary>死信人工补偿记录（SPEC 第 5 节 dead_letters）。</summary>
public class DeadLetter
{
    public long Id { get; set; }
    public string OriginalEventId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}

/// <summary>M5 5.1：Quartz 日报持久化落表（stat_date 主键，重跑 upsert 幂等）。</summary>
public class DailyReportRecord
{
    public DateOnly StatDate { get; set; }
    public int OrderCnt { get; set; }
    public int PaidCnt { get; set; }
    public int CancelCnt { get; set; }
    public decimal Gmv { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>M5 5.1：实验台 E1/E2/E3/TRAFFIC 结果（params / result 为 JSON 字符串）。</summary>
public class ExperimentResultRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Params { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime RanAtUtc { get; set; }
    public bool Passed { get; set; }
}

/// <summary>M5 5.1：告警记录（alert_key 索引，静默状态快照）。</summary>
public class AlertRecordEntity
{
    public long Id { get; set; }
    public string AlertKey { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool Silenced { get; set; }
    public DateTime? SilencedUntilUtc { get; set; }
}

/// <summary>M5 5.1：跨进程运行态开关（key 主键，value = on/off）。</summary>
public class LabControl
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = "on";
    public DateTime UpdatedAtUtc { get; set; }
}
