using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pulse.Infrastructure.Messaging;
using Pulse.Infrastructure.Persistence;
using Pulse.Infrastructure.Reports;
using Pulse.Worker;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddPulseMessaging(builder.Configuration);

// M5 5.2：Quartz 日报（RAMJobStore + 自定义 IJobFactory，落昨日 UTC 日报）。
builder.Services.AddScoped<DailyReportService>();
builder.Services.AddScoped<DailyReportWriter>();
builder.Services.AddScoped<DailyReportJob>();
builder.Services.AddSingleton<IHostedService, QuartzDailyReportHost>();

var host = builder.Build();

// 本地 SQLite 演示库由 Worker 自己初始化（API 会在自己的库里建表并播种）。
// 与 API 共用 DemoDatabaseInitializer，旧库缺 M3 新表时会重建，避免启动后刷 no such table。
// MySQL 模式下由 API 执行 Migrate()，Worker 不重复迁移，避免并发迁移竞态。
if (!useMySql)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
    DemoDatabaseInitializer.EnsureSchema(db);
}

host.Run();
