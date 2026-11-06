using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pulse.Api.Tests;

public class PlaceOrderEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlaceOrderEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Post_order_returns_order_no_and_same_key_is_idempotent()
    {
        var client = _factory.CreateClient();
        var key = $"api-test-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.False(string.IsNullOrWhiteSpace(firstBody?.OrderNo));

        var second = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 1, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(firstBody!.OrderNo, secondBody!.OrderNo);
    }

    [Fact]
    public async Task Insufficient_stock_returns_409()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders",
            new { sku = "SKU-1", quantity = 999999, idempotencyKey = $"api-test-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record OrderResponse(string OrderNo, string Status, string Message);
}
