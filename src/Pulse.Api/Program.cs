using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Alerts;
using Pulse.Infrastructure.Dashboard;
using Pulse.Infrastructure.Experiments;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var sqliteConnection = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=pulse-demo.db";
var mysqlConnection = builder.Configuration.GetConnectionString("MySql") ?? string.Empty;
var useMySql = string.Equals(databaseProvider, "MySql", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<PulseDbContext>(options =>
{
    if (useMySql)
    {
        options.UseMySql(mysqlConnection, new MySqlServerVersion(new Version(8, 0, 36)));
    }
    else
    {
        options.UseSqlite(sqliteConnection);
    }
});
builder.Services.AddScoped<PlaceOrderService>();
// M3：支付入口与补偿台（不注册消息宿主，消费者只在 Worker 进程拉起）。
builder.Services.AddScoped<OrderLifecycleService>();
builder.Services.AddScoped<DeadLetterService>();
// M4：订单列表查询与日报即时聚合（前端按 DTO 契约对接）。
builder.Services.AddScoped<OrderQueryService>();
builder.Services.AddScoped<DailyReportService>();

// ---- M5：实验台 / 仪表盘 / 告警 的 DI 接线（懒连接，未配置时降级，不连真 broker）----
var rabbitOptions = RabbitMqOptions.Load(builder.Configuration);
var redisOptions = RedisOptions.Load(builder.Configuration);
var dashboardEnv = new DashboardEnvironment(
    useMySql ? "mysql" : "sqlite(local-demo)",
    !string.IsNullOrWhiteSpace(redisOptions.ConnectionString),
    !string.IsNullOrWhiteSpace(rabbitOptions.ConnectionString));

builder.Services.AddSingleton(rabbitOptions);
builder.Services.AddSingleton(redisOptions);
builder.Services.AddSingleton(dashboardEnv);
builder.Services.AddSingleton(ExperimentOptions.Load(builder.Configuration));
builder.Services.AddSingleton(TimeProvider.System);

// IMessagePublisher / IQueueDepthProvider 均为懒连接：构造不连 broker，未配置时降级。
builder.Services.AddSingleton<IMessagePublisher>(new RabbitMqPublisher(rabbitOptions));
builder.Services.AddSingleton<IQueueDepthProvider>(new RabbitQueueDepthProvider(rabbitOptions));

// Redis 未配置时用内存实现降级（便于本地演示与 Sqlite 单测）。
if (!string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
{
    builder.Services.AddSingleton<IAlertStateStore>(new RedisAlertStateStore(redisOptions));
}
else
{
    builder.Services.AddSingleton<IAlertStateStore, InMemoryAlertStateStore>();
}

builder.Services.AddScoped<ILabControlStore, EfLabControlStore>();
builder.Services.AddScoped<DailyReportWriter>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<DashboardService>();
// M6c 6.10：链路泳道节点真实计数（复用已注册的 IQueueDepthProvider 懒连接）。
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<TrafficGenerator>();
builder.Services.AddScoped<ExperimentRunner>();
builder.Services.AddScoped<LabService>();

var app = builder.Build();

// M6c 6.12：容器以 Production 运行，旧逻辑只让 Development 暴露 Swagger，
// 导致演示站没有接口浏览入口。改为「默认启用、可显式关闭」：
// 公网部署时可设 Swagger__Enabled=false 关闭（不改 compose，本地/CI 行为不变）。
var swaggerEnabled = !string.Equals(
    builder.Configuration["Swagger:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
    if (useMySql)
    {
        db.Database.Migrate();
    }
    else
    {
        // 共用初始化器：旧演示库缺 M3 新表时重建，避免 /api/dead-letters 报 no such table。
        DemoDatabaseInitializer.EnsureSchema(db);
    }

    // 单一数据源：与 /api/lab/reset 共用 DemoCatalog，避免商城分类与 SKU 数量漂移。
    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(DemoCatalog.Products.Select(s => new Product
        {
            Sku = s.Sku,
            Name = s.Name,
            Price = s.Price,
            Category = s.Category,
        }));
        db.Inventories.AddRange(DemoCatalog.Products.Select(s => new Inventory
        {
            Sku = s.Sku,
            Available = s.Available,
            Reserved = 0,
            Version = 0,
        }));
        await db.SaveChangesAsync();
    }
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Pulse",
    notice = "演示环境 · 全部数据为模拟 · 不涉及真实交易"
}))
.WithName("Root");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    checkedAtUtc = DateTimeOffset.UtcNow,
    database = useMySql ? "mysql" : "sqlite(local-demo)",
    dependencies = new
    {
        redis = "not_configured",
        rabbitmq = "not_configured"
    }
}))
.WithName("Health");

app.MapGet("/api/products", async (PulseDbContext db, CancellationToken ct) =>
{
    // M4 契约对齐 #3：deducted 按 SKU 聚合 ChangeType=="Deduct" 的流水数量，无流水为 0。
    var deductedBySku = await db.InventoryLedgers.AsNoTracking()
        .Where(l => l.ChangeType == "Deduct")
        .GroupBy(l => l.Sku)
        .Select(g => new { Sku = g.Key, Qty = g.Sum(x => x.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => x.Qty, ct);

    var rows = await db.Products.AsNoTracking()
        .Join(db.Inventories.AsNoTracking(),
            p => p.Sku,
            i => i.Sku,
            (p, i) => new { p.Id, p.Sku, p.Name, p.Price, p.IsActive, p.Category, i.Available, i.Reserved, i.Version })
        .ToListAsync(ct);

    var items = rows.Select(r => new
    {
        id = r.Id,
        r.Sku,
        r.Name,
        r.Price,
        r.Available,
        r.Reserved,
        deducted = deductedBySku.GetValueOrDefault(r.Sku),
        status = r.IsActive ? "active" : "inactive",
        // M7 商城分类：外显后端 Product.Category，供前端分类导航与筛选。
        category = r.Category,
        // M6b v0.2：乐观锁版本号外显，供商品详情 / 后台库存页展示。
        version = r.Version
    }).ToList();
    return Results.Ok(items);
})
.WithName("ListProducts");

// M4 契约对齐 #4：批量改价（SPEC 第 4.2 节）。整批校验 price > 0（非法 → 400，
// 不产生部分更新），事务内按 SKU 更新，未知 SKU 跳过，返回成功更新条数。
app.MapPost("/api/products/prices", async Task<IResult> (
    PriceUpdate[] request, PulseDbContext db, CancellationToken ct) =>
{
    if (request is null || request.Any(x => string.IsNullOrWhiteSpace(x.Sku) || x.Price <= 0))
    {
        return Results.BadRequest(new { error = "每项需提供有效 sku 且 price > 0" });
    }

    // 同 SKU 以最后一条为准，避免重复计数。
    var desired = new Dictionary<string, decimal>(StringComparer.Ordinal);
    foreach (var item in request)
    {
        desired[item.Sku] = item.Price;
    }
    var skus = desired.Keys.ToList();

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    var products = await db.Products
        .Where(p => skus.Contains(p.Sku))
        .ToListAsync(ct);
    foreach (var product in products)
    {
        product.Price = desired[product.Sku];
    }
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.Ok(new { updated = products.Count });
})
.WithName("UpdatePrices");

app.MapPost("/api/orders", async Task<IResult> (PlaceOrderRequest request, PlaceOrderService service, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new { error = "请求体不能为空" });
    }

    // M6b v0.2：单 SKU 请求体原样保留；新增可选 items[]，二者互斥。
    var hasItems = request.Items is { Length: > 0 };
    var hasSingle = !string.IsNullOrWhiteSpace(request.Sku);
    if (hasItems && hasSingle)
    {
        return Results.BadRequest(new { error = "items 与 sku/quantity 不能同时提供" });
    }
    if (request.Items is { Length: 0 })
    {
        return Results.BadRequest(new { error = "items 不能为空数组" });
    }

    try
    {
        PlaceOrderResult result;
        if (hasItems)
        {
            result = await service.PlaceMultiAsync(
                new PlaceMultiOrderCommand(
                    request.Items!.Select(x => new PlaceOrderLine(x.Sku ?? string.Empty, x.Quantity)).ToList(),
                    request.IdempotencyKey ?? string.Empty),
                ct);
        }
        else
        {
            // 兼容硬约束：M1/M3/M5 与 132 条容器断言都走这条路径，行为一字不改。
            result = await service.PlaceAsync(
                new PlaceOrderCommand(
                    request.Sku ?? string.Empty,
                    request.Quantity,
                    request.IdempotencyKey ?? string.Empty),
                ct);
        }

        return Results.Ok(new
        {
            id = result.Id,
            result.OrderNo,
            status = result.Status.ToString(),
            message = "订单已创建，等待支付"
        });
    }
    catch (InsufficientStockException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (StockConcurrencyException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("PlaceOrder");

// timeline 由订单现有时间字段与状态推导（M4 不新建表、不编造精确时间）。
static IReadOnlyList<TimelineEntry> BuildTimeline(Order order)
{
    var timeline = new List<TimelineEntry>
    {
        new("Created", order.CreatedAtUtc, "订单创建")
    };

    if (order.PaidAtUtc is { } paidAt)
    {
        timeline.Add(new TimelineEntry("Paid", paidAt, "支付完成"));
    }

    // 终态 / 已流转状态补一条当前节点；没有独立时间戳，用现有字段推导并在 note 里注明。
    if (order.Status is OrderStatus.Shipped or OrderStatus.Completed
        or OrderStatus.Cancelled or OrderStatus.Refunding or OrderStatus.Refunded)
    {
        timeline.Add(new TimelineEntry(
            order.Status.ToString(),
            order.PaidAtUtc ?? order.CreatedAtUtc,
            "当前状态（时间由订单现有字段推导）"));
    }

    return timeline;
}

app.MapGet("/api/orders/{id:long}", async Task<IResult> (long id, PulseDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders.AsNoTracking()
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == id, ct);
    if (order is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        id = order.Id,
        orderNo = order.OrderNo,
        status = order.Status.ToString(),
        totalAmount = order.TotalAmount,
        createdAtUtc = order.CreatedAtUtc,
        paidAtUtc = order.PaidAtUtc,
        expiresAtUtc = order.ExpiresAtUtc,
        items = order.Items.Select(i => new
        {
            sku = i.Sku,
            quantity = i.Quantity,
            unitPrice = i.UnitPrice
        }),
        timeline = BuildTimeline(order)
    });
})
.WithName("GetOrder");

// M4：订单列表（status / sku / 时间范围筛选 + 分页），响应字段按 DTO 契约。
app.MapGet("/api/orders", async Task<IResult> (
    string? status,
    string? sku,
    DateTime? from,
    DateTime? to,
    string? orderNo,
    int? page,
    int? pageSize,
    OrderQueryService service,
    CancellationToken ct) =>
{
    var result = await service.ListAsync(
        new OrderListQuery(status, sku, from, to, page ?? 1, pageSize ?? 20, orderNo), ct);
    return Results.Ok(new
    {
        items = result.Items.Select(x => new
        {
            id = x.Id,
            orderNo = x.OrderNo,
            status = x.Status,
            totalAmount = x.TotalAmount,
            createdAtUtc = x.CreatedAtUtc,
            expiresAtUtc = x.ExpiresAtUtc,
            sku = x.Sku
        }),
        total = result.Total,
        page = result.Page,
        pageSize = result.PageSize
    });
})
.WithName("ListOrders");

// M4：取消（Created -> Cancelled，释放预扣）；不存在 / 非法跳转 -> 404。
app.MapPost("/api/orders/{id:long}/cancel", async Task<IResult> (long id, OrderLifecycleService service, CancellationToken ct) =>
{
    try
    {
        var status = await service.CancelAsync(id, ct);
        return Results.Ok(new { status = status.ToString() });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("CancelOrder");

// M4：发货（Paid -> Shipped）；不存在 / 非法跳转 -> 404。
app.MapPost("/api/orders/{id:long}/ship", async Task<IResult> (long id, OrderLifecycleService service, CancellationToken ct) =>
{
    try
    {
        var status = await service.ShipAsync(id, ct);
        return Results.Ok(new { status = status.ToString() });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("ShipOrder");

// M4：日报即时聚合；M5 5.2 追加 persisted（查 daily_reports 是否有该日行），M4 字段不变。
app.MapGet("/api/reports/daily", async Task<IResult> (
    string? date, DailyReportService service, PulseDbContext db, CancellationToken ct) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var statDate))
    {
        return Results.BadRequest(new { error = "date 必须为 yyyy-MM-dd" });
    }

    var report = await service.GetAsync(statDate, ct);
    var persisted = await db.DailyReportRecords.AsNoTracking()
        .AnyAsync(x => x.StatDate == statDate, ct);
    return Results.Ok(new
    {
        statDate = report.StatDate,
        orderCnt = report.OrderCnt,
        paidCnt = report.PaidCnt,
        cancelCnt = report.CancelCnt,
        gmv = report.Gmv,
        reconciliation = new
        {
            orderAmount = report.Reconciliation.OrderAmount,
            itemAmount = report.Reconciliation.ItemAmount,
            ledgerReserveQty = report.Reconciliation.LedgerReserveQty,
            diff = report.Reconciliation.Diff
        },
        persisted
    });
})
.WithName("GetDailyReport");

app.MapPost("/api/orders/{id:long}/pay", async Task<IResult> (long id, OrderLifecycleService service, CancellationToken ct) =>
{
    try
    {
        var status = await service.PayAsync(id, ct);
        return Results.Ok(new { status = status.ToString() });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
.WithName("PayOrder");

app.MapGet("/api/dead-letters", async Task<IResult> (string? status, DeadLetterService service, CancellationToken ct) =>
{
    var items = await service.ListAsync(status, ct);
    return Results.Ok(items.Select(x => new
    {
        id = x.Id,
        originalEventId = x.OriginalEventId,
        payload = x.Payload,
        error = x.Error,
        retryCount = x.RetryCount,
        status = x.Status,
        createdAtUtc = x.CreatedAtUtc,
        handledAtUtc = x.HandledAtUtc
    }));
})
.WithName("ListDeadLetters");

app.MapPost("/api/dead-letters/{id:long}/retry", async Task<IResult> (long id, DeadLetterService service, CancellationToken ct) =>
{
    if (!await service.RetryAsync(id, ct))
    {
        return Results.NotFound(new { error = "死信记录不存在" });
    }

    return Results.Ok(new { status = DeadLetterService.StatusHandled });
})
.WithName("RetryDeadLetter");

// M6c 6.10：链路泳道 7 节点真实计数。字段名严格对齐 PLAN「M6 DTO 契约」表。
app.MapGet("/api/pipeline", async Task<IResult> (PipelineService service, CancellationToken ct) =>
{
    var snapshot = await service.GetAsync(ct);
    return Results.Ok(new
    {
        generatedAtUtc = snapshot.GeneratedAtUtc,
        nodes = snapshot.Nodes.Select(n => new
        {
            n.Key,
            n.Label,
            n.Middleware,
            n.Count,
            n.Unit,
            n.Source,
            n.Available,
            n.Link
        })
    });
})
.WithName("GetPipeline");

// ===================== M5 5.7：实验台 / 仪表盘 / 告警 9 个端点 =====================
// 字段名严格按 PLAN「DTO 契约」表，时间均 UTC ISO，id 均 long(number)，
// params/result 均为 JSON 字符串（不是对象）。

// 1. GET /api/dashboard
app.MapGet("/api/dashboard", async Task<IResult> (DashboardService service, CancellationToken ct) =>
{
    var d = await service.GetAsync(ct);
    return Results.Ok(new
    {
        generatedAtUtc = d.GeneratedAtUtc,
        today = new
        {
            orderCnt = d.Today.OrderCnt,
            paidCnt = d.Today.PaidCnt,
            cancelCnt = d.Today.CancelCnt,
            gmv = d.Today.Gmv
        },
        inventory = new
        {
            skuCount = d.Inventory.SkuCount,
            lowStockCount = d.Inventory.LowStockCount,
            totalAvailable = d.Inventory.TotalAvailable,
            lowStockThreshold = d.Inventory.LowStockThreshold
        },
        queues = new
        {
            fulfillment = d.Queues.Fulfillment,
            orderTimeout = d.Queues.OrderTimeout,
            fulfillmentRetry = d.Queues.FulfillmentRetry,
            dlq = d.Queues.Dlq,
            available = d.Queues.Available
        },
        outboxPending = d.OutboxPending,
        deadLetterPending = d.DeadLetterPending,
        alertsActive = d.AlertsActive,
        controls = new
        {
            consumerEnabled = d.Controls.ConsumerEnabled,
            publisherEnabled = d.Controls.PublisherEnabled,
            failureInjectionEnabled = d.Controls.FailureInjectionEnabled
        },
        source = new
        {
            database = d.Source.Database,
            redis = d.Source.Redis,
            rabbitmq = d.Source.Rabbitmq
        }
    });
})
.WithName("GetDashboard");

// 2. GET /api/alerts?limit=50（先 RefreshAsync 再返回）
app.MapGet("/api/alerts", async Task<IResult> (int? limit, AlertService service, CancellationToken ct) =>
{
    var items = await service.ListAsync(limit ?? 50, ct);
    return Results.Ok(items.Select(a => new
    {
        id = a.Id,
        alertKey = a.AlertKey,
        level = a.Level,
        message = a.Message,
        createdAtUtc = a.CreatedAtUtc,
        silenced = a.Silenced,
        silencedUntilUtc = a.SilencedUntilUtc
    }));
})
.WithName("ListAlerts");

// 3. POST /api/alerts/{key}/silence（minutes 1–1440，越界 400）
app.MapPost("/api/alerts/{key}/silence", async Task<IResult> (
    string key, SilenceMinutes body, AlertService service, CancellationToken ct) =>
{
    if (body is null || body.Minutes < 1 || body.Minutes > 1440)
    {
        return Results.BadRequest(new { error = "minutes 必须在 1–1440 之间" });
    }

    var result = await service.SilenceAsync(key, body.Minutes, ct);
    if (result is null)
    {
        return Results.NotFound(new { error = "告警不存在" });
    }

    return Results.Ok(new
    {
        alertKey = result.AlertKey,
        silenced = result.Silenced,
        silencedUntilUtc = result.SilencedUntilUtc
    });
})
.WithName("SilenceAlert");

// 4. GET /api/lab/state
app.MapGet("/api/lab/state", async Task<IResult> (LabService service, CancellationToken ct) =>
{
    var s = await service.GetStateAsync(ct);
    return Results.Ok(new
    {
        consumerEnabled = s.ConsumerEnabled,
        publisherEnabled = s.PublisherEnabled,
        failureInjectionEnabled = s.FailureInjectionEnabled,
        database = s.Database,
        redisConfigured = s.RedisConfigured,
        rabbitConfigured = s.RabbitConfigured
    });
})
.WithName("GetLabState");

// 5. POST /api/lab/traffic
app.MapPost("/api/lab/traffic", async Task<IResult> (
    TrafficRequest request, LabService service, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Sku)
        || request.Count < 1 || request.Quantity < 1)
    {
        return Results.BadRequest(new { error = "sku 必填，count/quantity 必须 > 0" });
    }

    var r = await service.TrafficAsync(request, ct);
    return Results.Ok(new
    {
        requested = r.Requested,
        created = r.Created,
        failed = r.Failed,
        elapsedMs = r.ElapsedMs,
        throughputRps = r.ThroughputRps,
        p99Ms = r.P99Ms,
        experimentId = r.ExperimentId
    });
})
.WithName("LabTraffic");

// 6. POST /api/lab/inject
app.MapPost("/api/lab/inject", async Task<IResult> (
    InjectRequest request, LabService service, CancellationToken ct) =>
{
    if (request is null || !LabService.IsSupportedInjectType(request.Type))
    {
        return Results.BadRequest(new { error = "未知注入类型" });
    }

    var r = await service.InjectAsync(request, ct);
    return Results.Ok(new { type = r.Type, applied = r.Applied, detail = r.Detail });
})
.WithName("LabInject");

// 7. POST /api/lab/reset
app.MapPost("/api/lab/reset", async Task<IResult> (LabService service, CancellationToken ct) =>
{
    var r = await service.ResetAsync(ct);
    return Results.Ok(new
    {
        status = r.Status,
        productsSeeded = r.ProductsSeeded,
        clearedAtUtc = r.ClearedAtUtc
    });
})
.WithName("LabReset");

// 8. GET /api/experiments
app.MapGet("/api/experiments", async Task<IResult> (PulseDbContext db, CancellationToken ct) =>
{
    var rows = await db.ExperimentResultRecords.AsNoTracking()
        .OrderByDescending(x => x.RanAtUtc)
        .ThenByDescending(x => x.Id)
        .ToListAsync(ct);
    return Results.Ok(rows.Select(x => new
    {
        id = x.Id,
        name = x.Name,
        @params = x.Params,
        result = x.Result,
        ranAtUtc = x.RanAtUtc,
        passed = x.Passed
    }));
})
.WithName("ListExperiments");

// 9. POST /api/experiments/run（同步执行并返回结果行；实验自身有超时上限）
app.MapPost("/api/experiments/run", async Task<IResult> (
    ExperimentRunRequest request, ExperimentRunner runner, CancellationToken ct) =>
{
    if (request is null || request.Name is not ("E1" or "E2" or "E3"))
    {
        return Results.BadRequest(new { error = "name 必须为 E1/E2/E3" });
    }

    var row = await runner.RunAsync(request.Name, ct);
    return Results.Ok(new
    {
        id = row.Id,
        name = row.Name,
        @params = row.Params,
        result = row.Result,
        ranAtUtc = row.RanAtUtc,
        passed = row.Passed
    });
})
.WithName("RunExperiment");

app.Run();

/// <summary>
/// M6b v0.2：单 SKU 字段保持原名与位置不变；新增可选 items[]，与 sku/quantity 互斥。
/// string? 让「只带 items」的请求体也能绑定，互斥与合法性在端点 / 服务层校验。
/// </summary>
public sealed record PlaceOrderRequest(
    string? Sku,
    int Quantity,
    string? IdempotencyKey,
    PlaceOrderItemLine[]? Items = null);

public sealed record PlaceOrderItemLine(string? Sku, int Quantity);

public sealed record PriceUpdate(string Sku, decimal Price);

public sealed record TimelineEntry(string Status, DateTime AtUtc, string Note);

// M5 请求 DTO（TrafficRequest / InjectRequest 来自 Infrastructure，字段名与契约表一致）。
public sealed record SilenceMinutes(int Minutes);

public sealed record ExperimentRunRequest(string Name);

public partial class Program { }
