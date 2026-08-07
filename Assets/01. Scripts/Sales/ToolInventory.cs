using System;
using System.Collections.Generic;

// Finished-goods inventory, kept separate from ResourceBank (raw materials). ResourceBank only
// ever counted Tool as a flat total with no grade attached, but customer orders need "at least
// this grade" (customer_order_design.html §2), so this tracks counts per CraftGrade instead.
// Deliberately not a general item-instance system - just enough to match orders against stock.
public static class ToolInventory
{
    private static readonly Dictionary<CraftGrade, int> counts = new Dictionary<CraftGrade, int>();
    private static readonly CraftGrade[] AscendingGrades = (CraftGrade[])Enum.GetValues(typeof(CraftGrade));

    public static int Get(CraftGrade grade)
    {
        return counts.TryGetValue(grade, out int value) ? value : 0;
    }

    public static int Total
    {
        get
        {
            int total = 0;
            foreach (CraftGrade grade in AscendingGrades)
            {
                total += Get(grade);
            }

            return total;
        }
    }

    public static void Add(CraftGrade grade, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        counts[grade] = Get(grade) + amount;
    }

    // Spends the lowest-graded item that still satisfies minGrade, so higher-grade stock stays in
    // reserve for orders that actually demand it instead of getting burned on an easy order.
    public static bool TrySpendAtLeast(CraftGrade minGrade, out CraftGrade spentGrade)
    {
        foreach (CraftGrade grade in AscendingGrades)
        {
            if (grade < minGrade || Get(grade) <= 0)
            {
                continue;
            }

            counts[grade] = Get(grade) - 1;
            spentGrade = grade;
            return true;
        }

        spentGrade = minGrade;
        return false;
    }
}
