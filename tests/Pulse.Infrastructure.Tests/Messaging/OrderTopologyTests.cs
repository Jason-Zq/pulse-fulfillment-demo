using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class OrderTopologyTests
{
    private static readonly TopologyDefinition Def = OrderTopology.Build();

    private static QueueSpec Queue(string name) =>
        Def.Queues.Single(q => q.Name == name);

    [Fact]
    public void Exchange_is_ex_orders_topic_and_durable()
    {
        Assert.Equal("ex.orders", Def.Exchange.Name);
        Assert.Equal("topic", Def.Exchange.Type);
        Assert.True(Def.Exchange.Durable);
    }

    [Fact]
    public void Declares_the_four_expected_queues()
    {
        var names = Def.Queues.Select(q => q.Name).OrderBy(x => x).ToArray();
        Assert.Equal(
            new[]
            {
                "q.dlq.fulfillment",
                "q.fulfillment",
                "q.fulfillment.retry",
                "q.order.timeout"
            }.OrderBy(x => x).ToArray(),
            names);
        Assert.All(Def.Queues, q => Assert.True(q.Durable));
    }

    [Fact]
    public void Order_timeout_queue_uses_TTL_and_DLX_instead_of_delayed_plugin()
    {
        var args = Queue("q.order.timeout").Arguments;

        Assert.Equal(900000, Convert.ToInt32(args["x-message-ttl"])); // 15 min
        Assert.Equal("ex.orders", args["x-dead-letter-exchange"]);
        Assert.True(args.ContainsKey("x-dead-letter-routing-key"));
        Assert.DoesNotContain(args.Keys, k => k.Contains("x-delayed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fulfillment_retry_queue_uses_5s_TTL_and_DLX()
    {
        var args = Queue("q.fulfillment.retry").Arguments;

        Assert.Equal(5000, Convert.ToInt32(args["x-message-ttl"])); // 5 s
        Assert.Equal("ex.orders", args["x-dead-letter-exchange"]);
        Assert.Equal(3, OrderTopology.FulfillmentMaxRetries);
    }

    [Fact]
    public void Dlq_queue_has_no_ttl_and_no_dead_lettering()
    {
        var args = Queue("q.dlq.fulfillment").Arguments;

        Assert.DoesNotContain(args.Keys, k => k.Contains("x-message-ttl", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args.Keys, k => k.Contains("x-dead-letter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bindings_route_created_to_timeout_and_paid_to_fulfillment()
    {
        Assert.Contains(Def.Bindings, b =>
            b.Queue == "q.order.timeout" && b.Exchange == "ex.orders" && b.RoutingKey == "order.created");
        Assert.Contains(Def.Bindings, b =>
            b.Queue == "q.fulfillment" && b.Exchange == "ex.orders" && b.RoutingKey == "order.paid");

        var queueNames = Def.Queues.Select(q => q.Name).ToHashSet();
        Assert.All(Def.Bindings, b =>
        {
            Assert.Equal("ex.orders", b.Exchange);
            Assert.Contains(b.Queue, queueNames);
        });
    }

    [Fact]
    public void Topology_does_not_reference_delayed_message_exchange_plugin()
    {
        Assert.NotEqual("x-delayed-message", Def.Exchange.Type);
        Assert.DoesNotContain(Def.Queues, q =>
            q.Arguments.Keys.Any(k => k.Contains("x-delayed", StringComparison.OrdinalIgnoreCase)));
    }
}
