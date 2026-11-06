namespace Pulse.Domain;

public static class OrderStateMachine
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Created] = [OrderStatus.Paid, OrderStatus.Cancelled],
            [OrderStatus.Paid] = [OrderStatus.Shipped, OrderStatus.Refunding],
            [OrderStatus.Shipped] = [OrderStatus.Completed],
            [OrderStatus.Refunding] = [OrderStatus.Refunded],
            [OrderStatus.Completed] = [],
            [OrderStatus.Cancelled] = [],
            [OrderStatus.Refunded] = []
        };

    public static OrderStatus Transition(OrderStatus current, OrderStatus target)
    {
        if (!Allowed.TryGetValue(current, out var next) || Array.IndexOf(next, target) < 0)
        {
            throw new InvalidOperationException($"非法订单状态跳转: {current} -> {target}");
        }

        return target;
    }
}
