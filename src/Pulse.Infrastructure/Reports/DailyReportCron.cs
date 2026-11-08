using System;

namespace Pulse.Infrastructure.Reports;

/// <summary>
/// M10：日报 cron 的解析纯函数。默认 01:00（Asia/Shanghai）与 M5/M9 完全一致；
/// 只有显式配置（环境变量 <c>Reports__DailyCron</c>）才改变，用于让 Quartz 调度器
/// 在本机用一个短 cron 自己到点触发，真验证「定时任务真的会写日报」。
///
/// 非法配置抛异常而不是静默回退默认值：演示站宁可启动失败，也不要「以为改了其实没改」。
/// Quartz 自身会做完整的 cron 语法校验，这里只做段数的前置检查。
/// </summary>
public static class DailyReportCron
{
    public const string DefaultCron = "0 0 1 * * ?";

    /// <summary>null / 空白 -> 默认 01:00；合法 cron 规范化空白后原样返回；段数非法抛异常。</summary>
    public static string Resolve(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultCron;
        }

        var parts = configured.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Quartz cron 为 6 段（秒 分 时 日 月 周）或 7 段（末尾带年）。
        if (parts.Length is < 6 or > 7)
        {
            throw new ArgumentException(
                $"日报 cron 段数应为 6 或 7，实际 {parts.Length}: '{configured}'",
                nameof(configured));
        }

        return string.Join(" ", parts);
    }
}
