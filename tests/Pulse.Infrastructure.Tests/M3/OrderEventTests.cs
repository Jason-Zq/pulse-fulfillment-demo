using System.Text.Json;
using Pulse.Infrastructure.Messaging;
using Xunit;

namespace Pulse.Infrastructure.Tests.M3;

public class OrderEventTests
{
    [Fact]
    public void Order_event_deserializes_all_fields_from_camel_case_json()
    {
        const string json = """
            {"eventId":"evt-1","orderNo":"PO-1","sku":"SKU-1","quantity":3,
             "routingKey":"order.paid","expiresAtUtc":"2026-11-07T12:00:00Z"}
            """;

        var evt = JsonSerializer.Deserialize<OrderEvent>(json);

        Assert.NotNull(evt);
        Assert.Equal("evt-1", evt.EventId);
        Assert.Equal("PO-1", evt.OrderNo);
        Assert.Equal("SKU-1", evt.Sku);
        Assert.Equal(3, evt.Quantity);
        Assert.Equal("order.paid", evt.RoutingKey);
        Assert.Equal(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), evt.ExpiresAtUtc);
    }

    [Fact]
    public void Order_event_can_be_built_with_business_fields_only()
    {
        var evt = new OrderEvent { OrderNo = "PO-2", Sku = "SKU-2", Quantity = 1 };

        Assert.Equal("PO-2", evt.OrderNo);
        Assert.Equal("SKU-2", evt.Sku);
        Assert.Equal(1, evt.Quantity);
        Assert.Equal(string.Empty, evt.EventId);
        Assert.Equal(string.Empty, evt.RoutingKey);
    }

    [Theory]
    [InlineData("order.paid", EventRoute.Fulfillment)]
    [InlineData("order.timeout", EventRoute.Timeout)]
    [InlineData("order.retry", EventRoute.Retry)]
    [InlineData("order.dead", EventRoute.DeadLetter)]
    [InlineData("order.unknown", EventRoute.Unsupported)]
    public void Resolve_maps_routing_key_to_route(string key, EventRoute expected) =>
        Assert.Equal(expected, EventRoutes.Resolve(key));

    [Fact]
    public void Resolve_uses_order_topology_constants()
    {
        Assert.Equal(EventRoute.Fulfillment, EventRoutes.Resolve(OrderTopology.RoutingKeyOrderPaid));
        Assert.Equal(EventRoute.Timeout, EventRoutes.Resolve(OrderTopology.RoutingKeyOrderTimeout));
        Assert.Equal(EventRoute.Retry, EventRoutes.Resolve(OrderTopology.RoutingKeyFulfillmentRetry));
        Assert.Equal(EventRoute.DeadLetter, EventRoutes.Resolve(OrderTopology.RoutingKeyDeadLetter));
    }

    [Fact]
    public void Resolve_treats_blank_key_as_unsupported() =>
        Assert.Equal(EventRoute.Unsupported, EventRoutes.Resolve(string.Empty));
}
