namespace Pulse.Infrastructure.Lab;

/// <summary>
/// M5 5.4：lab_controls 表的稳定 key。开关落 DB（不用 Redis，本地模式无 Redis），
/// API 与 Worker 两进程都能读。
/// </summary>
public static class LabControlKeys
{
    public const string Consumer = "consumer";
    public const string Publisher = "publisher";
    public const string Failure = "failure";

    public const string On = "on";
    public const string Off = "off";

    /// <summary>
    /// 缺行时的默认值。consumer / publisher 是「正常运行」开关，默认开；
    /// failure 是「故障注入」开关，默认必须关——否则所有履约都会被注入失败，
    /// 破坏正常支付→履约链路（与 DTO 的 failureInjectionEnabled 默认 false 一致）。
    /// </summary>
    public static string DefaultFor(string key) =>
        key == Failure ? Off : On;
}
