var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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
    dependencies = new
    {
        mysql = "not_configured",
        redis = "not_configured",
        rabbitmq = "not_configured"
    }
}))
.WithName("Health");

app.Run();

public partial class Program { }
