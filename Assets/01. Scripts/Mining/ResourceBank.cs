using System.Collections.Generic;

public enum ResourceType
{
    Wood,
    Ore,
    Ingot,
    Tool
}

public static class ResourceBank
{
    private static readonly Dictionary<ResourceType, int> totals = new Dictionary<ResourceType, int>();

    public static int Get(ResourceType type)
    {
        return totals.TryGetValue(type, out int value) ? value : 0;
    }

    public static void Add(ResourceType type, int amount)
    {
        totals[type] = Get(type) + amount;
    }

    public static bool TrySpend(ResourceType type, int amount)
    {
        if (Get(type) < amount)
        {
            return false;
        }

        totals[type] -= amount;
        return true;
    }
}
