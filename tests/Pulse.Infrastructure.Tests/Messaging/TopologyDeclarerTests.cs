using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.Messaging;

public class TopologyDeclarerTests
{
    private sealed class FakeRabbitTopologyModel : IRabbitTopologyModel
    {
        public List<(string Name, string Type, bool Durable)> Exchanges { get; } = new();
        public List<(string Name, bool Durable, IReadOnlyDictionary<string, object> Arguments)> Queues { get; } = new();
        public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = new();

        public void ExchangeDeclare(string exchange, string type, bool durable) =>
            Exchanges.Add((exchange, type, durable));

        public void QueueDeclare(string queue, bool durable, IReadOnlyDictionary<string, object> arguments) =>
            Queues.Add((queue, durable, arguments));

        public void QueueBind(string queue, string exchange, string routingKey) =>
            Bindings.Add((queue, exchange, routingKey));
    }

    [Fact]
    public void Declares_exchange_once()
    {
        var model = new FakeRabbitTopologyModel();

        new RabbitMqTopologyDeclarer().Declare(model, OrderTopology.Build());

        var ex = Assert.Single(model.Exchanges);
        Assert.Equal("ex.orders", ex.Name);
        Assert.Equal("topic", ex.Type);
        Assert.True(ex.Durable);
    }

    [Fact]
    public void Declares_all_four_queues_with_ttl_and_dlx_arguments()
    {
        var model = new FakeRabbitTopologyModel();

        new RabbitMqTopologyDeclarer().Declare(model, OrderTopology.Build());

        Assert.Equal(4, model.Queues.Count);
        Assert.All(model.Queues, q => Assert.True(q.Durable));

        var timeout = model.Queues.Single(q => q.Name == "q.order.timeout");
        Assert.Equal(900000, Convert.ToInt32(timeout.Arguments["x-message-ttl"]));
        Assert.Equal("ex.orders", timeout.Arguments["x-dead-letter-exchange"]);

        var retry = model.Queues.Single(q => q.Name == "q.fulfillment.retry");
        Assert.Equal(5000, Convert.ToInt32(retry.Arguments["x-message-ttl"]));

        var dlq = model.Queues.Single(q => q.Name == "q.dlq.fulfillment");
        Assert.DoesNotContain(dlq.Arguments.Keys, k => k.Contains("x-message-ttl"));
    }

    [Fact]
    public void Binds_every_binding_from_definition()
    {
        var model = new FakeRabbitTopologyModel();
        var def = OrderTopology.Build();

        new RabbitMqTopologyDeclarer().Declare(model, def);

        Assert.Equal(def.Bindings.Count, model.Bindings.Count);
        Assert.Contains(model.Bindings, b => b == ("q.order.timeout", "ex.orders", "order.created"));
        Assert.Contains(model.Bindings, b => b == ("q.fulfillment", "ex.orders", "order.paid"));
    }

    [Fact]
    public void Default_declare_uses_order_topology()
    {
        var model = new FakeRabbitTopologyModel();

        new RabbitMqTopologyDeclarer().Declare(model);

        Assert.Contains(model.Exchanges, e => e.Name == "ex.orders");
        Assert.Equal(4, model.Queues.Count);
    }
}
