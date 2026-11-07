using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Pulse.Infrastructure.Persistence;

/// <summary>
/// 本地 SQLite 演示库的 schema 初始化（API 与 Worker 共用）。
///
/// EF 的 <c>EnsureCreated()</c> 只在新库上建表，不会给已存在的旧库补表；
/// M3 新增 ProcessedEvents / DeadLetters 后，M1 时代遗留的 pulse-demo.db 会缺表，
/// 导致 /api/dead-letters 报 "no such table"。演示数据本就可一键清空，
/// 因此检测到缺表时重建整个演示库，保证 schema 与当前 EF 模型一致。
///
/// 仅用于本地 SQLite 演示模式；compose / MySQL 模式仍由 API 执行 Migrate()。
/// </summary>
public static class DemoDatabaseInitializer
{
    private static readonly string[] RequiredTables =
    [
        "Products", "Inventories", "Orders", "OrderItems", "InventoryLedgers",
        "OutboxMessages", "ProcessedEvents", "DeadLetters",
        // M5 5.1：新增 4 张表，旧演示库缺表时自动重建。
        "DailyReportRecords", "ExperimentResultRecords", "AlertRecordEntities", "LabControls"
    ];

    public static void EnsureSchema(PulseDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!HasAllRequiredTables(db))
        {
            db.Database.EnsureDeleted();
        }

        db.Database.EnsureCreated();
    }

    private static bool HasAllRequiredTables(PulseDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = false;
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            openedHere = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            var names = string.Join(", ", RequiredTables.Select(t => $"'{t}'"));
            command.CommandText =
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ({names})";
            return Convert.ToInt32(command.ExecuteScalar()) == RequiredTables.Length;
        }
        catch
        {
            // 库文件不存在 / 不可读 / 非 SQLite provider：交给 EnsureCreated 处理。
            return false;
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }
    }
}
