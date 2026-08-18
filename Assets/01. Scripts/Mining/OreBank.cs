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
    // How much *lifetime* ore has to be deposited before the next grade starts being mined - this
    // was the only unlock condition before stage clears existed (roadmap ③, "해금 게이트");
    // now it's kept as a second, parallel path (see StageCeiling below) so a player who farmed
    // ahead of clearing stages doesn't lose that progress.
    private const int SteelThreshold = 60;
    private const int MithrilThreshold = 180;
    private const int OrichalcumThreshold = 360;

    private static readonly Dictionary<OreGrade, int> counts = new Dictionary<OreGrade, int>();
    private static readonly OreGrade[] AscendingGrades = (OreGrade[])Enum.GetValues(typeof(OreGrade));

    public static int TotalMined { get; private set; }

    // Whichever of the two unlock paths (cumulative farming or stage clearing - see
    // stage_system_design_v1.html §2) is further along wins.
    public static OreGrade Ceiling
    {
        get
        {
            OreGrade cumulative = CumulativeCeiling;
            OreGrade stage = StageCeiling;
            return cumulative > stage ? cumulative : stage;
        }
    }

    private static OreGrade CumulativeCeiling
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

    private static OreGrade StageCeiling
    {
        get
        {
            if (StageBank.HighestStageCleared >= 3)
            {
                return OreGrade.Orichalcum;
            }

            if (StageBank.HighestStageCleared >= 2)
            {
                return OreGrade.Mithril;
            }

            if (StageBank.HighestStageCleared >= 1)
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

    // Cheapest-first counterpart to TryGetBestAvailable - the black market's quick-sell spends the
    // lowest grade that has enough stock, so higher-grade material earmarked for crafting doesn't
    // get sold off by accident. See black_market_design_v1.html §3.
    public static bool TrySpendCheapest(int amount, out OreGrade grade)
    {
        foreach (OreGrade candidate in AscendingGrades)
        {
            if (TrySpend(candidate, amount))
            {
                grade = candidate;
                return true;
            }
        }

        grade = OreGrade.Iron;
        return false;
    }

    // Credits a specific grade directly, bypassing the Ceiling roll DepositMined uses - the black
    // market explicitly sells grades the player's own gathering can't reach yet ("정상 유통
    // 경로로는 안 팔리는 희귀 재료"), so it has to write directly instead of rolling. See
    // black_market_design_v1.html §2.
    public static void AddDirect(OreGrade grade, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        counts[grade] = Get(grade) + amount;
    }
}
