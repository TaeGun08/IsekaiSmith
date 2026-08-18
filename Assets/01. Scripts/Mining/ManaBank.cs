using System;
using System.Collections.Generic;
using UnityEngine;

// Graded mana-stone inventory - mirrors OreBank.cs exactly (see OreBank.cs for the full pattern
// writeup). ResourceBank's flat ResourceType.ManaStone count is deliberately left untouched and
// still written to on every deposit (same reason as OreBank: it's a write-only "lifetime
// deposited" signal some other reader might rely on); this class is the new source of truth for
// actual mana *stock* that crafting spends from. See mana_grade_and_ui_design_v1.html §1.
public static class ManaBank
{
    // Field drops are capped at Common - Refined/Greater/Pristine are reserved for a future
    // stage/dungeon reward source (roadmap ③) that doesn't exist yet, so DepositGathered (the
    // only current writer) never rolls above Ceiling and Ceiling never exceeds Common.
    //
    // CommonThreshold is intentionally unreachable for now - per user request ("일단은 최하급만
    // 뜨도록"), field drops stay locked to Crude until this is deliberately lowered.
    private const int CommonThreshold = int.MaxValue;

    private static readonly Dictionary<ManaGrade, int> counts = new Dictionary<ManaGrade, int>();
    private static readonly ManaGrade[] AscendingGrades = (ManaGrade[])Enum.GetValues(typeof(ManaGrade));

    public static int TotalGathered { get; private set; }

    // Whichever of the two unlock paths (cumulative gathering or stage clearing - see
    // stage_system_design_v1.html §2) is further along wins, same rule OreBank.Ceiling uses.
    public static ManaGrade Ceiling
    {
        get
        {
            ManaGrade cumulative = TotalGathered >= CommonThreshold ? ManaGrade.Common : ManaGrade.Crude;
            ManaGrade stage = StageCeiling;
            return cumulative > stage ? cumulative : stage;
        }
    }

    private static ManaGrade StageCeiling
    {
        get
        {
            if (StageBank.HighestStageCleared >= 3)
            {
                return ManaGrade.Greater;
            }

            if (StageBank.HighestStageCleared >= 2)
            {
                return ManaGrade.Refined;
            }

            if (StageBank.HighestStageCleared >= 1)
            {
                return ManaGrade.Common;
            }

            return ManaGrade.Crude;
        }
    }

    public static int Get(ManaGrade grade)
    {
        return counts.TryGetValue(grade, out int value) ? value : 0;
    }

    public static int TotalCurrent
    {
        get
        {
            int total = 0;
            foreach (ManaGrade grade in AscendingGrades)
            {
                total += Get(grade);
            }

            return total;
        }
    }

    // Called by StorageDepot alongside its existing ResourceBank.Add(ManaStone, amount) write -
    // rolls each deposited unit's grade between Crude and the current ceiling, and always bumps
    // TotalGathered regardless of roll outcome, since that's what would raise the ceiling if it
    // were ever lowered below int.MaxValue.
    public static void DepositGathered(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int ceilingInclusive = (int)Ceiling + 1;
        for (int i = 0; i < amount; i++)
        {
            ManaGrade grade = (ManaGrade)UnityEngine.Random.Range(0, ceilingInclusive);
            counts[grade] = Get(grade) + 1;
        }

        TotalGathered += amount;
    }

    public static bool TrySpend(ManaGrade grade, int amount)
    {
        if (Get(grade) < amount)
        {
            return false;
        }

        counts[grade] -= amount;
        return true;
    }

    // Auto-picks the best-graded stock with enough units to satisfy amountNeeded - same "always use
    // the best you've got" rule OreBank.TryGetBestAvailable uses, since there's no manual
    // material-picker UI for grade yet (mana_grade_and_ui_design_v1.html §1, "확인 필요 1").
    public static bool TryGetBestAvailable(int amountNeeded, out ManaGrade grade)
    {
        for (int i = AscendingGrades.Length - 1; i >= 0; i--)
        {
            if (Get(AscendingGrades[i]) >= amountNeeded)
            {
                grade = AscendingGrades[i];
                return true;
            }
        }

        grade = ManaGrade.Crude;
        return false;
    }
}
