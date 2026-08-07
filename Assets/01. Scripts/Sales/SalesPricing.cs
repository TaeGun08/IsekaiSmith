using System.Collections.Generic;

// Grade -> base payout table (customer_order_design.html §5). Split out of OrderQueueManager so
// SalesCounterUI can show a ticket's estimated pay without reaching into the manager's internals.
public static class SalesPricing
{
    private static readonly Dictionary<CraftGrade, int> BasePay = new Dictionary<CraftGrade, int>
    {
        { CraftGrade.Rough, 10 },
        { CraftGrade.Common, 16 },
        { CraftGrade.Fine, 24 },
        { CraftGrade.Superior, 36 },
        { CraftGrade.Exceptional, 54 },
        { CraftGrade.Masterwork, 80 },
        { CraftGrade.Legendary, 150 },
    };

    public static int BaseFor(CraftGrade grade)
    {
        return BasePay.TryGetValue(grade, out int value) ? value : 0;
    }
}
