using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pulse.Infrastructure.Persistence;

/// <summary>
/// 仅供 dotnet ef 设计时生成 MySQL 迁移使用，运行时不读取此连接串。
/// </summary>
public class PulseDbContextFactory : IDesignTimeDbContextFactory<PulseDbContext>
{
    public PulseDbContext CreateDbContext(string[] args)
    {
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        var options = new DbContextOptionsBuilder<PulseDbContext>()
            .UseMySql("Server=localhost;Database=pulse;User=root;Password=pulse_dev", serverVersion)
            .Options;
        return new PulseDbContext(options);
    }
}
