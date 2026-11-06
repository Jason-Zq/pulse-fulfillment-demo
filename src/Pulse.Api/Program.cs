using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Orders;
using Pulse.Infrastructure.Persistence;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
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
        db.Database.EnsureCreated();
    }

    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new Product { Sku = "SKU-1", Name = "演示商品 A", Price = 99.5m },
            new Product { Sku = "SKU-2", Name = "演示商品 B", Price = 199m });
        db.Inventories.AddRange(
            new Inventory { Sku = "SKU-1", Available = 100, Reserved = 0, Version = 0 },
            new Inventory { Sku = "SKU-2", Available = 50, Reserved = 0, Version = 0 });
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
    var items = await db.Products.AsNoTracking()
        .Join(db.Inventories.AsNoTracking(),
            p => p.Sku,
            i => i.Sku,
            (p, i) => new { p.Sku, p.Name, p.Price, i.Available })
        .ToListAsync(ct);
    return Results.Ok(items);
})
.WithName("ListProducts");

app.MapPost("/api/orders", async Task<IResult> (PlaceOrderRequest request, PlaceOrderService service, CancellationToken ct) =>
{
    try
    {
        var result = await service.PlaceAsync(
            new PlaceOrderCommand(request.Sku, request.Quantity, request.IdempotencyKey), ct);
        return Results.Ok(new
        {
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
        order.Id,
        order.OrderNo,
        status = order.Status.ToString(),
        order.TotalAmount,
        order.CreatedAtUtc,
        order.ExpiresAtUtc,
        items = order.Items.Select(i => new { i.Sku, i.Quantity, i.UnitPrice })
    });
})
.WithName("GetOrder");

app.Run();

public sealed record PlaceOrderRequest(string Sku, int Quantity, string IdempotencyKey);

public partial class Program { }
