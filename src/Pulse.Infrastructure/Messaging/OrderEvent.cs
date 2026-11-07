using System.Text.Json.Serialization;

namespace Pulse.Infrastructure.Messaging;

/// <summary>
/// 订单链路的统一事件 DTO。payload 只放业务字段；eventId 与 routingKey 由投递层补齐
/// （M1 的 outbox 把 eventId 放在 OutboxMessage.EventId，payload 仅含业务字段）。
/// init 属性 + 默认值：既支持只含业务字段的 payload 反序列化，也便于用 with 补元数据。
/// </summary>
public sealed record OrderEvent
{
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("orderNo")]
    public string OrderNo { get; init; } = string.Empty;

    [JsonPropertyName("sku")]
    public string Sku { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("routingKey")]
    public string RoutingKey { get; init; } = string.Empty;

    [JsonPropertyName("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; init; }
}
