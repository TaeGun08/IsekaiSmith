// Plain data for one customer standing in the sales counter's line - no MonoBehaviour, so
// OrderQueueManager can hold a plain list of these instead of juggling child GameObjects for state
// that never needs a Transform of its own. No grade requirement anymore (customer_order_design_
// v7.html §1) - a customer just wants RequestedCount weapons, period; OrderQueueManager matches
// against counter stock regardless of grade (cheapest/oldest first), so which grade actually
// changes hands is a payout detail, not an order-eligibility one.
public class CustomerOrder
{
    public readonly int Id;
    public readonly int RequestedCount;

    public int DeliveredCount;

    public CustomerOrder(int id, int requestedCount)
    {
        Id = id;
        RequestedCount = requestedCount;
    }

    public bool IsComplete => DeliveredCount >= RequestedCount;
}
