using Pulse.Infrastructure.Experiments;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>M5 5.6：E1/E2/E3 判据纯逻辑（不依赖 broker / DB，可严格红绿）。</summary>
public class ExperimentVerdictsTests
{
    [Fact]
    public void E1_requires_all_orders_published_and_zero_diff()
    {
        Assert.True(ExperimentVerdicts.E1(published: 20, orders: 20, diff: 0m));
        Assert.False(ExperimentVerdicts.E1(published: 19, orders: 20, diff: 0m));
        Assert.False(ExperimentVerdicts.E1(published: 20, orders: 20, diff: 1m));
    }

    [Fact]
    public void E2_requires_exactly_one_deduct_ledger_and_one_processed_event_row()
    {
        Assert.True(ExperimentVerdicts.E2(deductLedgerCount: 1, processedEventRows: 1));
        Assert.False(ExperimentVerdicts.E2(deductLedgerCount: 2, processedEventRows: 1));
        Assert.False(ExperimentVerdicts.E2(deductLedgerCount: 1, processedEventRows: 0));
        Assert.False(ExperimentVerdicts.E2(deductLedgerCount: 0, processedEventRows: 1));
    }

    [Fact]
    public void E3_requires_no_oversell_and_ledger_consistency()
    {
        Assert.True(ExperimentVerdicts.E3(available: 50, oversoldCount: 0, reservedPlusDeducted: 100, reserveLedgerQty: 100));
        Assert.False(ExperimentVerdicts.E3(available: -1, oversoldCount: 0, reservedPlusDeducted: 100, reserveLedgerQty: 100));
        Assert.False(ExperimentVerdicts.E3(available: 50, oversoldCount: 1, reservedPlusDeducted: 100, reserveLedgerQty: 100));
        Assert.False(ExperimentVerdicts.E3(available: 50, oversoldCount: 0, reservedPlusDeducted: 99, reserveLedgerQty: 100));
    }
}
