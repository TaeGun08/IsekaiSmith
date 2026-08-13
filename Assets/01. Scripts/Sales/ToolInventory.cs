using System;
using System.Collections.Generic;

// Finished-goods inventory, kept separate from ResourceBank (raw materials). ResourceBank only
// ever counted Tool as a flat total with no grade attached, but customer orders need "at least
// this grade" (customer_order_design.html §2), so this tracks counts per CraftGrade instead.
// Now also tracks OreGrade (game_design_doc.html §3's other axis) alongside CraftGrade - a sword
// is both a material tier (Iron/Steel/Mithril/Orichalcum) and a finish quality (Rough~Legendary)
// at once. See weapon_diversity_design_v1.html §3. Deliberately not a general item-instance
// system - just enough to match orders against stock and read back the best-owned material tier.
public static class ToolInventory
{
    private static readonly Dictionary<(OreGrade, CraftGrade), int> counts = new Dictionary<(OreGrade, CraftGrade), int>();
    private static readonly CraftGrade[] AscendingCraftGrades = (CraftGrade[])Enum.GetValues(typeof(CraftGrade));
    private static readonly OreGrade[] AscendingOreGrades = (OreGrade[])Enum.GetValues(typeof(OreGrade));

    public static int Get(OreGrade oreGrade, CraftGrade grade)
    {
        return counts.TryGetValue((oreGrade, grade), out int value) ? value : 0;
    }

    public static int Total
    {
        get
        {
            int total = 0;
            foreach (int count in counts.Values)
            {
                total += count;
            }

            return total;
        }
    }

    // Highest ore grade among anything owned, any quality - stands in for "the equipped weapon"
    // until a real equip-selection UI exists (weapon_diversity_design_v1.html §1 "다음 단계로
    // 미룸"). Iron (the default) if nothing's been crafted yet.
    public static OreGrade BestOreGrade
    {
        get
        {
            for (int i = AscendingOreGrades.Length - 1; i >= 0; i--)
            {
                OreGrade oreGrade = AscendingOreGrades[i];
                foreach (CraftGrade grade in AscendingCraftGrades)
                {
                    if (Get(oreGrade, grade) > 0)
                    {
                        return oreGrade;
                    }
                }
            }

            return OreGrade.Iron;
        }
    }

    public static void Add(OreGrade oreGrade, CraftGrade grade, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        (OreGrade, CraftGrade) key = (oreGrade, grade);
        counts[key] = Get(oreGrade, grade) + amount;
    }

    // Spends the lowest-graded item (checking ore grade ascending within each quality step) that
    // still satisfies minGrade, so higher-grade stock stays in reserve for orders that actually
    // demand it instead of getting burned on an easy order. Ore grade doesn't factor into order
    // eligibility yet (orders only ever asked for a minimum quality - see
    // weapon_diversity_design_v1.html §1) so any ore grade combo works, cheapest first.
    public static bool TrySpendAtLeast(CraftGrade minGrade, out CraftGrade spentGrade)
    {
        foreach (CraftGrade grade in AscendingCraftGrades)
        {
            if (grade < minGrade)
            {
                continue;
            }

            foreach (OreGrade oreGrade in AscendingOreGrades)
            {
                if (Get(oreGrade, grade) <= 0)
                {
                    continue;
                }

                counts[(oreGrade, grade)] = Get(oreGrade, grade) - 1;
                spentGrade = grade;
                return true;
            }
        }

        spentGrade = minGrade;
        return false;
    }
}
