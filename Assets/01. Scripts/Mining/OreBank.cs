using System;
using System.Collections.Generic;
using UnityEngine;

// Graded ore inventory, parallel to ToolInventory's per-CraftGrade dictionary pattern but for the
// raw-material axis (game_design_doc.html §3 - "두 자원 축"). ResourceBank's flat
// ResourceType.Ore count is deliberately left untouched and still written to on every deposit -
// GuidedTutorial detects "did ore go up" by watching that exact value, so removing it would stall
// the tutorial. This class is the new, additional source of truth for actual ore *stock* (what
// crafting spends from); ResourceBank.Ore becomes a write-only "lifetime deposited" signal that
// happens to still serve its one existing reader correctly. See weapon_diversity_design_v1.html §3.
public static class OreBank
{
    // How much *lifetime* ore has to be deposited before the next grade starts being mined - no
    // dungeon-clear gate exists yet (that's roadmap ③, "해금 게이트"), so this is an interim
    // placeholder standing in for it: the quarry "grows" with total ore banked instead of a boss
    // kill. Swap this for a real dungeon-clear flag once ③ exists - Ceiling is the only thing that
    // needs to change, the rest of OreBank's API doesn't care how the ceiling is decided.
    private const int SteelThreshold = 60;
    private const int MithrilThreshold = 180;
    private const int OrichalcumThreshold = 360;

    private static readonly Dictionary<OreGrade, int> counts = new Dictionary<OreGrade, int>();
    private static readonly OreGrade[] AscendingGrades = (OreGrade[])Enum.GetValues(typeof(OreGrade));

    public static int TotalMined { get; private set; }

    public static OreGrade Ceiling
    {
        get
        {
            if (TotalMined >= OrichalcumThreshold)
            {
                return OreGrade.Orichalcum;
            }

            if (TotalMined >= MithrilThreshold)
            {
                return OreGrade.Mithril;
            }

            if (TotalMined >= SteelThreshold)
            {
                return OreGrade.Steel;
            }

            return OreGrade.Iron;
        }
    }

    public static int Get(OreGrade grade)
    {
        return counts.TryGetValue(grade, out int value) ? value : 0;
    }

    public static int TotalCurrent
    {
        get
        {
            int total = 0;
            foreach (OreGrade grade in AscendingGrades)
            {
                total += Get(grade);
            }

            return total;
        }
    }

    // Called by StorageDepot alongside its existing ResourceBank.Add(Ore, amount) write - rolls
    // each deposited unit's grade between Iron and the current ceiling (same Random.Range pattern
    // OrderQueueManager.SpawnOrder already uses for order grades), and always bumps TotalMined
    // regardless of roll outcome, since that's what raises the ceiling for next time.
    public static void DepositMined(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int ceilingInclusive = (int)Ceiling + 1;
        for (int i = 0; i < amount; i++)
        {
            OreGrade grade = (OreGrade)UnityEngine.Random.Range(0, ceilingInclusive);
            counts[grade] = Get(grade) + 1;
        }

        TotalMined += amount;
    }

    public static bool TrySpend(OreGrade grade, int amount)
    {
        if (Get(grade) < amount)
        {
            return false;
        }

        counts[grade] -= amount;
        return true;
    }

    // Auto-picks the best-graded stock with enough units to satisfy amountNeeded - no material-
    // picker UI wired up to real consumption yet (see weapon_diversity_design_v1.html §1), so
    // "always use the best you've got" is the interim crafting rule.
    public static bool TryGetBestAvailable(int amountNeeded, out OreGrade grade)
    {
        for (int i = AscendingGrades.Length - 1; i >= 0; i--)
        {
            if (Get(AscendingGrades[i]) >= amountNeeded)
            {
                grade = AscendingGrades[i];
                return true;
            }
        }

        grade = OreGrade.Iron;
        return false;
    }
}
