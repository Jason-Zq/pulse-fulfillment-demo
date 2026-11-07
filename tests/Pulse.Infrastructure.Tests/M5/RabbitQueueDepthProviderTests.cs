using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.M5;

/// <summary>
/// M5 5.5：未配置 / 连接不可用时降级 available:false，深度返回 -1（不伪造 0）。
/// 真 broker 的 passive declare 仅编译验证，不连真 broker。
/// </summary>
public class RabbitQueueDepthProviderTests
{
    [Fact]
    public async Task Missing_connection_string_degrades_to_unavailable_with_minus_one_depths()
    {
        var provider = new RabbitQueueDepthProvider(new RabbitMqOptions(string.Empty));

        var snapshot = await provider.GetAsync();

        Assert.False(snapshot.Available);
        Assert.Equal(-1, snapshot.Fulfillment);
        Assert.Equal(-1, snapshot.OrderTimeout);
        Assert.Equal(-1, snapshot.FulfillmentRetry);
        Assert.Equal(-1, snapshot.Dlq);
    }

    [Fact]
    public void Unavailable_snapshot_never_reports_fake_zero()
    {
        var snapshot = QueueDepthSnapshot.Unavailable();
        Assert.False(snapshot.Available);
        Assert.All(new[] { snapshot.Fulfillment, snapshot.OrderTimeout, snapshot.FulfillmentRetry, snapshot.Dlq },
            d => Assert.Equal(-1, d));
    }
}
