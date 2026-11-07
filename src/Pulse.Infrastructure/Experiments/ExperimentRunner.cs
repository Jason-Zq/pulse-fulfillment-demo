using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;

namespace Pulse.Infrastructure.Experiments;

public sealed record ExperimentOutcome(
    string Name,
    string ParamsJson,
    string ResultJson,
    bool Passed);

/// <summary>
/// M5 5.6：E1/E2/E3 实验编排。三者都把 params / result（JSON 字符串）/ passed 落
/// experiment_results；passed=false 也要如实落行，不吞异常、不伪造 true。
/// 每个实验有超时上限（ExperimentOptions），避免请求挂死。
/// </summary>
public sealed class ExperimentRunner
{
    private readonly PulseDbContext _db;
    private readonly PlaceOrderService _placeOrder;
    private readonly OrderLifecycleService _lifecycle;
    private readonly ILabControlStore _controls;
    private readonly IMessagePublisher _publisher;
    private readonly DailyReportService _reports;
    private readonly TrafficGenerator _traffic;
    private readonly ExperimentOptions _options;

    public ExperimentRunner(
        PulseDbContext db,
        PlaceOrderService placeOrder,
        OrderLifecycleService lifecycle,
        ILabControlStore controls,
        IMessagePublisher publisher,
        DailyReportService reports,
        TrafficGenerator traffic,
        ExperimentOptions options)
    {
        _db = db;
        _placeOrder = placeOrder;
        _lifecycle = lifecycle;
        _controls = controls;
        _publisher = publisher;
        _reports = reports;
        _traffic = traffic;
        _options = options;
    }

    public async Task<ExperimentResultRecord> RunAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(_options.RunTimeoutSeconds));
        var token = linked.Token;

        ExperimentOutcome outcome;
        try
        {
            outcome = name switch
            {
                "E1" => await RunE1Async(token),
                "E2" => await RunE2Async(token),
                "E3" => await RunE3Async(token),
                _ => throw new ArgumentException($"未知实验名: {name}", nameof(name))
            };
        }
        catch (Exception ex)
        {
            // 不吞异常、不伪造 true：失败 / 超时也如实落一条 passed=false 行，并保留错误信息。
            outcome = new ExperimentOutcome(
                name,
                JsonSerializer.Serialize(new { name }),
                JsonSerializer.Serialize(new { error = ex.Message, passed = false }),
                false);
        }

        var row = new ExperimentResultRecord
        {
            Name = outcome.Name,
            Params = outcome.ParamsJson,
            Result = outcome.ResultJson,
            RanAtUtc = DateTime.UtcNow,
            Passed = outcome.Passed
        };
        _db.ExperimentResultRecords.Add(row);
        // 用原始 cancellationToken：即使实验被整体超时取消，也要把结果行落库。
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    /// <summary>E1 消息不丢：关 Publisher → 造 N 单 → 记 pending → 开 Publisher → 轮询到 pending=0 → 校验 diff=0。</summary>
    private async Task<ExperimentOutcome> RunE1Async(CancellationToken ct)
    {
        var orders = _options.E1OrderCount;
        var param = JsonSerializer.Serialize(new
        {
            orders,
            pollTimeoutSeconds = _options.E1PollTimeoutSeconds
        });

        await _controls.SetEnabledAsync(LabControlKeys.Publisher, false, ct);
        var placed = 0;
        for (var i = 0; i < orders; i++)
        {
            await _placeOrder.PlaceAsync(
                new PlaceOrderCommand("SKU-1", 1, Guid.NewGuid().ToString("N")), ct);
            placed++;
        }

        var pendingBefore = await _db.OutboxMessages
            .CountAsync(m => m.Status == OutboxPublisher.StatusPending, ct);
        await _controls.SetEnabledAsync(LabControlKeys.Publisher, true, ct);

        // 轮询等待 Worker 的 OutboxPublisher 排空（本地无 Worker 时会到超时 → passed=false，如实落行）。
        var deadline = DateTime.UtcNow.AddSeconds(_options.E1PollTimeoutSeconds);
        var pendingAfter = pendingBefore;
        while (DateTime.UtcNow < deadline)
        {
            pendingAfter = await _db.OutboxMessages
                .CountAsync(m => m.Status == OutboxPublisher.StatusPending, ct);
            if (pendingAfter == 0)
            {
                break;
            }

            await Task.Delay(200, ct);
        }

        var published = await _db.OutboxMessages
            .CountAsync(m => m.Status == OutboxPublisher.StatusPublished, ct);
        var report = await _reports.GetAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);
        var diff = report.Reconciliation.Diff;
        var passed = ExperimentVerdicts.E1(published, orders, diff);
        var result = JsonSerializer.Serialize(new
        {
            orders, placed, pendingBefore, pendingAfter, published, diff, passed
        });
        return new ExperimentOutcome("E1", param, result, passed);
    }

    /// <summary>E2 消费幂等：造 1 单并支付 → 同一 event_id 直投 N 条 order.paid → 等待消费 → 校验去重。</summary>
    private async Task<ExperimentOutcome> RunE2Async(CancellationToken ct)
    {
        var count = _options.E2DuplicateCount;
        var param = JsonSerializer.Serialize(new
        {
            duplicateCount = count,
            consumeTimeoutSeconds = _options.E2ConsumeTimeoutSeconds
        });

        var place = await _placeOrder.PlaceAsync(
            new PlaceOrderCommand("SKU-1", 1, Guid.NewGuid().ToString("N")), ct);
        await _lifecycle.PayAsync(place.Id, ct);

        var eventId = $"e2-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            orderNo = place.OrderNo,
            sku = "SKU-1",
            quantity = 1
        });
        var confirmed = 0;
        for (var i = 0; i < count; i++)
        {
            var publishResult = await _publisher.PublishAsync(
                new PublishRequest(eventId, OrderTopology.RoutingKeyOrderPaid, payload), ct);
            if (publishResult.Confirmed)
            {
                confirmed++;
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(_options.E2ConsumeTimeoutSeconds);
        var deductCount = 0;
        var processedRows = 0;
        while (DateTime.UtcNow < deadline)
        {
            deductCount = await _db.InventoryLedgers
                .CountAsync(l => l.ChangeType == "Deduct" && l.RefOrderId == place.Id, ct);
            processedRows = await _db.ProcessedEvents
                .CountAsync(p => p.EventId == eventId, ct);
            if (deductCount > 0 || processedRows > 0)
            {
                break;
            }

            await Task.Delay(200, ct);
        }

        var passed = ExperimentVerdicts.E2(deductCount, processedRows);
        var result = JsonSerializer.Serialize(new
        {
            duplicateCount = count, confirmed, eventId, deductCount, processedRows, passed
        });
        return new ExperimentOutcome("E2", param, result, passed);
    }

    /// <summary>E3 并发不超卖：同 SKU 并发 N 单 → 校验 available&gt;=0、超卖率 0、reserved+deducted 与流水一致。</summary>
    private async Task<ExperimentOutcome> RunE3Async(CancellationToken ct)
    {
        var param = JsonSerializer.Serialize(new
        {
            orders = _options.E3OrderCount,
            concurrency = _options.E3Concurrency,
            quantity = _options.E3Quantity
        });

        var metrics = await _traffic.GenerateCoreAsync(
            new TrafficRequest("SKU-1", _options.E3OrderCount, 0,
                _options.E3Quantity, _options.E3Concurrency), ct);

        var inv = await _db.Inventories.AsNoTracking()
            .FirstAsync(i => i.Sku == "SKU-1", ct);
        var reserveQty = (await _db.InventoryLedgers.AsNoTracking()
            .Where(l => l.Sku == "SKU-1" && l.ChangeType == "Reserve")
            .Select(l => l.Quantity)
            .ToListAsync(ct)).Sum();
        var deductQty = (await _db.InventoryLedgers.AsNoTracking()
            .Where(l => l.Sku == "SKU-1" && l.ChangeType == "Deduct")
            .Select(l => l.Quantity)
            .ToListAsync(ct)).Sum();
        var reservedPlusDeducted = (long)inv.Reserved + deductQty;
        var oversoldCount = inv.Available < 0 ? 1 : 0;
        var passed = ExperimentVerdicts.E3(
            inv.Available, oversoldCount, reservedPlusDeducted, reserveQty);

        var result = JsonSerializer.Serialize(new
        {
            requested = metrics.Requested,
            created = metrics.Created,
            failed = metrics.Failed,
            throughputRps = metrics.ThroughputRps,
            p99Ms = metrics.P99Ms,
            available = inv.Available,
            reserved = inv.Reserved,
            reserveLedgerQty = reserveQty,
            deductLedgerQty = deductQty,
            oversoldCount,
            reservedPlusDeducted,
            passed
        });
        return new ExperimentOutcome("E3", param, result, passed);
    }
}
