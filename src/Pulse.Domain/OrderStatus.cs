namespace Pulse.Domain;

public enum OrderStatus
{
    Created,
    Paid,
    Shipped,
    Completed,
    Cancelled,
    Refunding,
    Refunded
}
