// Gold - the project has had no currency concept until now (customer_order_design.html §1).
// Deliberately as bare as ResourceBank: a static counter, no persistence/UI baked in.
public static class SalesCurrency
{
    public static int Gold { get; private set; }

    public static void Add(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Gold += amount;
    }

    public static bool TrySpend(int amount)
    {
        if (amount <= 0 || Gold < amount)
        {
            return false;
        }

        Gold -= amount;
        return true;
    }
}
