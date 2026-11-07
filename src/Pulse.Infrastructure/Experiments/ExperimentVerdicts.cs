namespace Pulse.Infrastructure.Experiments;

/// <summary>
/// M5 5.6：E1/E2/E3 的 PASS/FAIL 判据纯逻辑（不依赖 broker / DB，便于严格红绿）。
/// passed 必须由真实断言产生，不许伪造 true。
/// </summary>
public static class ExperimentVerdicts
{
    /// <summary>E1 消息不丢：已发布数 == 造单数 且 日报对账 diff == 0。</summary>
    public static bool E1(int published, int orders, decimal diff) =>
        published == orders && diff == 0;

    /// <summary>E2 消费幂等：该订单 Deduct 流水恰好 1 条 且 processed_events 该 event_id 仅 1 行。</summary>
    public static bool E2(int deductLedgerCount, int processedEventRows) =>
        deductLedgerCount == 1 && processedEventRows == 1;

    /// <summary>
    /// E3 并发不超卖：available &gt;= 0、超卖数 0，且 reserved+deducted 与 Reserve 流水数量一致。
    /// </summary>
    public static bool E3(int available, int oversoldCount, long reservedPlusDeducted, long reserveLedgerQty) =>
        available >= 0 && oversoldCount == 0 && reservedPlusDeducted == reserveLedgerQty;
}
