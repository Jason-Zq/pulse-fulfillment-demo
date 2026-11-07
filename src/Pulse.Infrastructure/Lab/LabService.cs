using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Dashboard;
using Pulse.Infrastructure.Experiments;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Lab;

public sealed record InjectRequest(string Type, int? Count, string? OrderNo);
public sealed record InjectResult(string Type, bool Applied, string Detail);
public sealed record ResetResult(string Status, int ProductsSeeded, DateTime ClearedAtUtc);

public sealed record LabState(
    bool ConsumerEnabled,
    bool PublisherEnabled,
    bool FailureInjectionEnabled,
    string Database,
    bool RedisConfigured,
    bool RabbitConfigured);

/// <summary>M5 5.7：实验台服务（traffic / inject / reset / state）。</summary>
public sealed class LabService
{
    private static readonly HashSet<string> InjectTypes = new(StringComparer.Ordinal)
    {
        "duplicate", "failure_on", "failure_off",
        "stop_consumer", "start_consumer", "stop_publisher", "start_publisher"
    };

    private static readonly (string Sku, string Name, decimal Price, int Available)[] DemoSkus =
    [
        ("SKU-1", "演示商品 A", 99.5m, 100),
        ("SKU-2", "演示商品 B", 199m, 50)
    ];

    private readonly PulseDbContext _db;
    private readonly ILabControlStore _controls;
    private readonly IMessagePublisher _publisher;
    private readonly TrafficGenerator _traffic;
    private readonly DashboardEnvironment _env;

    public LabService(
        PulseDbContext db,
        ILabControlStore controls,
        IMessagePublisher publisher,
        TrafficGenerator traffic,
        DashboardEnvironment env)
    {
        _db = db;
        _controls = controls;
        _publisher = publisher;
        _traffic = traffic;
        _env = env;
    }

    public static bool IsSupportedInjectType(string? type) =>
        type is not null && InjectTypes.Contains(type);

    public Task<TrafficResult> TrafficAsync(
        TrafficRequest request,
        CancellationToken cancellationToken = default) =>
        _traffic.GenerateAsync(request, cancellationToken);

    public async Task<LabState> GetStateAsync(CancellationToken cancellationToken = default) =>
        new(
            await _controls.IsEnabledAsync(LabControlKeys.Consumer, cancellationToken),
            await _controls.IsEnabledAsync(LabControlKeys.Publisher, cancellationToken),
            await _controls.IsEnabledAsync(LabControlKeys.Failure, cancellationToken),
            _env.Database,
            _env.RedisConfigured,
            _env.RabbitConfigured);

    public async Task<InjectResult> InjectAsync(
        InjectRequest request,
        CancellationToken cancellationToken = default)
    {
        switch (request.Type)
        {
            case "stop_consumer":
                await _controls.SetEnabledAsync(LabControlKeys.Consumer, false, cancellationToken);
                return new InjectResult(request.Type, true, "消费者已停止（consumer=off）");
            case "start_consumer":
                await _controls.SetEnabledAsync(LabControlKeys.Consumer, true, cancellationToken);
                return new InjectResult(request.Type, true, "消费者已启动（consumer=on）");
            case "stop_publisher":
                await _controls.SetEnabledAsync(LabControlKeys.Publisher, false, cancellationToken);
                return new InjectResult(request.Type, true, "Publisher 已停止（publisher=off）");
            case "start_publisher":
                await _controls.SetEnabledAsync(LabControlKeys.Publisher, true, cancellationToken);
                return new InjectResult(request.Type, true, "Publisher 已启动（publisher=on）");
            case "failure_on":
                await _controls.SetEnabledAsync(LabControlKeys.Failure, true, cancellationToken);
                return new InjectResult(request.Type, true, "故障注入已开启（failure=on）");
            case "failure_off":
                await _controls.SetEnabledAsync(LabControlKeys.Failure, false, cancellationToken);
                return new InjectResult(request.Type, true, "故障注入已关闭（failure=off）");
            case "duplicate":
            {
                var count = Math.Max(1, request.Count.GetValueOrDefault(1000));
                var eventId = $"dup-{Guid.NewGuid():N}";
                var orderNo = string.IsNullOrWhiteSpace(request.OrderNo)
                    ? "INJECT-DUP"
                    : request.OrderNo;
                var payload = JsonSerializer.Serialize(new
                {
                    orderNo,
                    sku = "SKU-1",
                    quantity = 1
                });
                var confirmed = 0;
                for (var i = 0; i < count; i++)
                {
                    var result = await _publisher.PublishAsync(
                        new PublishRequest(eventId, OrderTopology.RoutingKeyOrderPaid, payload),
                        cancellationToken);
                    if (result.Confirmed)
                    {
                        confirmed++;
                    }
                }

                return new InjectResult(request.Type, true,
                    $"已直投 {count} 条同 event_id 消息（eventId={eventId}, confirmed={confirmed}）");
            }
            default:
                return new InjectResult(request.Type, false, "未知注入类型");
        }
    }

    /// <summary>
    /// 清空业务表（Orders / OrderItems / InventoryLedgers / OutboxMessages /
    /// ProcessedEvents / DeadLetters / Inventory）并重新播种 2 个 SKU；
    /// 保留 daily_reports / experiment_results / alerts / lab_controls。
    /// </summary>
    public async Task<ResetResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        _db.OrderItems.RemoveRange(await _db.OrderItems.ToListAsync(cancellationToken));
        _db.Orders.RemoveRange(await _db.Orders.ToListAsync(cancellationToken));
        _db.InventoryLedgers.RemoveRange(await _db.InventoryLedgers.ToListAsync(cancellationToken));
        _db.OutboxMessages.RemoveRange(await _db.OutboxMessages.ToListAsync(cancellationToken));
        _db.ProcessedEvents.RemoveRange(await _db.ProcessedEvents.ToListAsync(cancellationToken));
        _db.DeadLetters.RemoveRange(await _db.DeadLetters.ToListAsync(cancellationToken));
        _db.Inventories.RemoveRange(await _db.Inventories.ToListAsync(cancellationToken));
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var (sku, name, price, available) in DemoSkus)
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
            if (product is null)
            {
                _db.Products.Add(new Product
                {
                    Sku = sku, Name = name, Price = price, IsActive = true
                });
            }
            else
            {
                product.Name = name;
                product.Price = price;
                product.IsActive = true;
            }

            _db.Inventories.Add(new Inventory
            {
                Sku = sku, Available = available, Reserved = 0, Version = 0
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ResetResult("ok", DemoSkus.Length, DateTime.UtcNow);
    }
}
