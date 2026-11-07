using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

/// <summary>
/// 回归：EF 的 EnsureCreated() 只建库、不给已存在的旧库补表。
/// M3 新增 ProcessedEvents / DeadLetters 后，M1 时代的本地演示库会缺表，
/// 导致 /api/dead-letters 返回 500。演示数据可清空，缺表时应重建。
/// </summary>
public sealed class DemoDatabaseInitializerTests : IDisposable
{
    private static readonly string[] ExpectedTables =
    [
        "Products", "Inventories", "Orders", "OrderItems", "InventoryLedgers",
        "OutboxMessages", "ProcessedEvents", "DeadLetters"
    ];

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"pulse-schema-{Guid.NewGuid():N}.db");

    [Fact]
    public void EnsureSchema_旧库缺表时_重建出全部表()
    {
        // 造一个「M1 时代」的旧库：只有一张表，模拟 schema 落后。
        using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE \"Products\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT)";
            cmd.ExecuteNonQuery();
        }

        using var db = CreateContext();
        DemoDatabaseInitializer.EnsureSchema(db);

        Assert.Equal(ExpectedTables.Length, CountExpectedTables(db));
    }

    [Fact]
    public void EnsureSchema_全新库_建出全部表且重复调用幂等()
    {
        using var db = CreateContext();
        DemoDatabaseInitializer.EnsureSchema(db);
        DemoDatabaseInitializer.EnsureSchema(db);

        Assert.Equal(ExpectedTables.Length, CountExpectedTables(db));
    }

    private PulseDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PulseDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options);

    private static int CountExpectedTables(PulseDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return ExpectedTables.Count(t => found.Contains(t));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
