using System;
using Pulse.Infrastructure.Reports;
using Xunit;

namespace Pulse.Infrastructure.Tests;

/// <summary>
/// M10 10.3：日报 cron 解析纯函数。默认必须与 M9 完全一致（01:00 Asia/Shanghai），
/// 只有显式配置短 cron 时才改变，用于让 Quartz 调度器自己到点触发的真验证。
/// </summary>
public sealed class M10DailyReportCronTests
{
    [Fact]
    public void DefaultCronIsUnchanged()
    {
        Assert.Equal("0 0 1 * * ?", DailyReportCron.DefaultCron);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConfigurationFallsBackToDefault(string? configured)
    {
        Assert.Equal(DailyReportCron.DefaultCron, DailyReportCron.Resolve(configured));
    }

    [Fact]
    public void ShortCronIsPassedThroughForRealTrigger()
    {
        Assert.Equal("0/10 * * * * ?", DailyReportCron.Resolve("0/10 * * * * ?"));
    }

    [Fact]
    public void SurroundingAndInnerWhitespaceIsNormalized()
    {
        Assert.Equal("0/10 * * * * ?", DailyReportCron.Resolve("  0/10  * * * * ?  "));
    }

    [Theory]
    [InlineData("0 0 1")]
    [InlineData("a b c d e f g h")]
    public void InvalidSegmentCountThrowsInsteadOfSilentlyFallingBack(string configured)
    {
        Assert.Throws<ArgumentException>(() => DailyReportCron.Resolve(configured));
    }
}
