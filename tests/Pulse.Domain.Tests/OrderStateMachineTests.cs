using Pulse.Domain;
using Xunit;

namespace Pulse.Domain.Tests;

public class OrderStateMachineTests
{
    [Fact]
    public void Created_cannot_transition_directly_to_shipped()
    {
        Assert.Throws<InvalidOperationException>(
            () => OrderStateMachine.Transition(OrderStatus.Created, OrderStatus.Shipped));
    }

    [Fact]
    public void Happy_path_created_to_completed_is_allowed()
    {
        var current = OrderStatus.Created;
        current = OrderStateMachine.Transition(current, OrderStatus.Paid);
        current = OrderStateMachine.Transition(current, OrderStatus.Shipped);
        current = OrderStateMachine.Transition(current, OrderStatus.Completed);

        Assert.Equal(OrderStatus.Completed, current);
    }

    [Fact]
    public void Cancelled_order_cannot_be_paid_afterwards()
    {
        var cancelled = OrderStateMachine.Transition(OrderStatus.Created, OrderStatus.Cancelled);
        Assert.Equal(OrderStatus.Cancelled, cancelled);
        Assert.Throws<InvalidOperationException>(
            () => OrderStateMachine.Transition(OrderStatus.Cancelled, OrderStatus.Paid));
    }
}
