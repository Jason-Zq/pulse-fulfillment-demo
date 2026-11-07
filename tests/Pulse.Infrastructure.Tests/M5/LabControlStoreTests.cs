using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pulse.Infrastructure.Lab;
using Pulse.Infrastructure.Persistence;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.4：运行态开关落 lab_controls 表（跨 API / Worker 进程），
/// 默认 consumer/publisher 开、failure 注入关（不破坏正常履约），读写与覆盖幂等。
/// </summary>
public class LabControlStoreTests
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

        public ILabControlStore Store() => new EfLabControlStore(Db);

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    [Fact]
    public async Task Consumer_and_publisher_default_on_failure_injection_default_off()
    {
        using var f = new Fixture();
        var store = f.Store();

        Assert.True(await store.IsEnabledAsync(LabControlKeys.Consumer));
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Publisher));
        // failure 是「故障注入」开关，默认必须关，否则所有履约都会被注入失败，
        // 破坏 C 层已验证的支付→履约成功链路。consumer/publisher 则是「正常运行」开关，默认开。
        Assert.False(await store.IsEnabledAsync(LabControlKeys.Failure));
    }

    [Fact]
    public async Task SetEnabled_writes_on_off_row_and_is_readable()
    {
        using var f = new Fixture();
        var store = f.Store();

        await store.SetEnabledAsync(LabControlKeys.Publisher, false);
        Assert.False(await store.IsEnabledAsync(LabControlKeys.Publisher));
        var row = await f.Db.LabControls.AsNoTracking().SingleAsync();
        Assert.Equal(LabControlKeys.Publisher, row.Key);
        Assert.Equal("off", row.Value);

        await store.SetEnabledAsync(LabControlKeys.Failure, true);
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Failure));
        var failureRow = await f.Db.LabControls.AsNoTracking()
            .SingleAsync(x => x.Key == LabControlKeys.Failure);
        Assert.Equal("on", failureRow.Value);
    }

    [Fact]
    public async Task SetEnabled_overwrites_in_place_without_duplicate_rows()
    {
        using var f = new Fixture();
        var store = f.Store();

        await store.SetEnabledAsync(LabControlKeys.Consumer, false);
        await store.SetEnabledAsync(LabControlKeys.Consumer, false);
        await store.SetEnabledAsync(LabControlKeys.Consumer, true);

        Assert.Equal(1, await f.Db.LabControls.CountAsync());
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Consumer));
        Assert.Equal("on", (await f.Db.LabControls.AsNoTracking().SingleAsync()).Value);
    }

    [Fact]
    public void LabControlKeys_exposes_the_three_stable_keys()
    {
        Assert.Equal("consumer", LabControlKeys.Consumer);
        Assert.Equal("publisher", LabControlKeys.Publisher);
        Assert.Equal("failure", LabControlKeys.Failure);
    }

    [Fact]
    public void DefaultFor_returns_on_on_off()
    {
        // 团队契约修正：consumer/publisher 默认 on，failure 默认 off。
        Assert.Equal(LabControlKeys.On, LabControlKeys.DefaultFor(LabControlKeys.Consumer));
        Assert.Equal(LabControlKeys.On, LabControlKeys.DefaultFor(LabControlKeys.Publisher));
        Assert.Equal(LabControlKeys.Off, LabControlKeys.DefaultFor(LabControlKeys.Failure));
    }

    [Fact]
    public async Task Failure_on_then_off_roundtrip_starts_from_off_default()
    {
        using var f = new Fixture();
        var store = f.Store();

        Assert.False(await store.IsEnabledAsync(LabControlKeys.Failure));
        await store.SetEnabledAsync(LabControlKeys.Failure, true);
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Failure));
        await store.SetEnabledAsync(LabControlKeys.Failure, false);
        Assert.False(await store.IsEnabledAsync(LabControlKeys.Failure));
        // consumer/publisher 不受影响，仍是默认 on。
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Consumer));
        Assert.True(await store.IsEnabledAsync(LabControlKeys.Publisher));
    }
}
