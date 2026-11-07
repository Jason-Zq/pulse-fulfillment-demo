using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.1：4 张新表的实体 / DbContext 配置 / 演示库初始化器。
/// 严格 RED：这些类型此刻不存在，编译应失败。
/// </summary>
public class M5EntitiesTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public PulseDbContext Db { get; }

        public Fixture()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new PulseDbContext(
                new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
        }

        public PulseDbContext NewDbContext() => new(
            new DbContextOptionsBuilder<PulseDbContext>().UseSqlite(_connection).Options);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task DailyReportRecord_roundtrips_and_StatDate_is_the_unique_key()
    {
        using var f = new Fixture();
        var day = new DateOnly(2026, 11, 6);
        f.Db.DailyReportRecords.Add(new DailyReportRecord
        {
            StatDate = day,
            OrderCnt = 5,
            PaidCnt = 3,
            CancelCnt = 1,
            Gmv = 123.45m,
            CreatedAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var stored = await f.Db.DailyReportRecords.SingleAsync();
        Assert.Equal(day, stored.StatDate);
        Assert.Equal(5, stored.OrderCnt);
        Assert.Equal(123.45m, stored.Gmv);

        // 同一 stat_date 再插应触发唯一键冲突。
        using var db2 = f.NewDbContext();
        db2.DailyReportRecords.Add(new DailyReportRecord
        {
            StatDate = day,
            CreatedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task ExperimentResultRecord_roundtrips_with_json_string_fields()
    {
        using var f = new Fixture();
        f.Db.ExperimentResultRecords.Add(new ExperimentResultRecord
        {
            Name = "E1",
            Params = "{\"orders\":20}",
            Result = "{\"diff\":0}",
            RanAtUtc = DateTime.UtcNow,
            Passed = true
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var stored = await f.Db.ExperimentResultRecords.SingleAsync();
        Assert.Equal("E1", stored.Name);
        Assert.Equal("{\"orders\":20}", stored.Params);
        Assert.Equal("{\"diff\":0}", stored.Result);
        Assert.True(stored.Passed);
    }

    [Fact]
    public async Task AlertRecordEntity_defaults_and_roundtrips()
    {
        using var f = new Fixture();
        var blank = new AlertRecordEntity();
        Assert.Equal("info", blank.Level);
        Assert.False(blank.Silenced);
        Assert.Null(blank.SilencedUntilUtc);

        f.Db.AlertRecordEntities.Add(new AlertRecordEntity
        {
            AlertKey = "low_stock:SKU-1",
            Level = "warning",
            Message = "库存偏低",
            CreatedAtUtc = DateTime.UtcNow,
            Silenced = true,
            SilencedUntilUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var stored = await f.Db.AlertRecordEntities.SingleAsync();
        Assert.Equal("low_stock:SKU-1", stored.AlertKey);
        Assert.Equal("warning", stored.Level);
        Assert.True(stored.Silenced);
        Assert.NotNull(stored.SilencedUntilUtc);
    }

    [Fact]
    public async Task LabControl_roundtrips_and_is_keyed_by_Key()
    {
        using var f = new Fixture();
        Assert.Equal("on", new LabControl().Value);

        f.Db.LabControls.Add(new LabControl
        {
            Key = "publisher",
            Value = "off",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var stored = await f.Db.LabControls.SingleAsync();
        Assert.Equal("publisher", stored.Key);
        Assert.Equal("off", stored.Value);
    }

    [Fact]
    public void Model_configures_required_indexes_and_precision()
    {
        using var f = new Fixture();
        var model = f.Db.Model;

        var daily = model.FindEntityType(typeof(DailyReportRecord))!;
        Assert.NotNull(daily.FindIndex(daily.FindProperty(nameof(DailyReportRecord.StatDate))!));
        var gmv = daily.FindProperty(nameof(DailyReportRecord.Gmv))!;
        Assert.Equal(18, gmv.GetPrecision());
        Assert.Equal(2, gmv.GetScale());

        var alert = model.FindEntityType(typeof(AlertRecordEntity))!;
        Assert.NotNull(alert.FindIndex(alert.FindProperty(nameof(AlertRecordEntity.AlertKey))!));

        var exp = model.FindEntityType(typeof(ExperimentResultRecord))!;
        var idx = Assert.Single(exp.GetIndexes().Where(i =>
            i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(ExperimentResultRecord.Name), nameof(ExperimentResultRecord.RanAtUtc) })));
        Assert.NotNull(idx);
    }

    [Fact]
    public void DemoDatabaseInitializer_rebuilds_old_8_table_db_into_12_tables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulse-m5-schema-{Guid.NewGuid():N}.db");
        try
        {
            // 造一个「M3 时代」旧库：只有 8 张表，缺 M5 的 4 张。
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                foreach (var t in new[]
                         {
                             "Products", "Inventories", "Orders", "OrderItems", "InventoryLedgers",
                             "OutboxMessages", "ProcessedEvents", "DeadLetters"
                         })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"CREATE TABLE \"{t}\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT)";
                    cmd.ExecuteNonQuery();
                }
            }

            using var db = new PulseDbContext(
                new DbContextOptionsBuilder<PulseDbContext>().UseSqlite($"Data Source={path}").Options);
            DemoDatabaseInitializer.EnsureSchema(db);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var read = connection.CreateCommand();
            read.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            foreach (var t in new[]
                     {
                         "DailyReportRecords", "ExperimentResultRecords",
                         "AlertRecordEntities", "LabControls"
                     })
            {
                Assert.Contains(t, names);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
