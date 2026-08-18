// Tracks stage-clear progress (roadmap ③ "해금 게이트" - see stage_system_design_v1.html).
// Deliberately minimal: just "how many stages cleared so far". OreBank/ManaBank read this to
// raise their grade Ceiling (replacing the interim cumulative-deposit-only gate their own
// comments already flagged for this), FieldMonsterSpawner reads it to scale field monster
// strength, StageGate reads it to decide which stage is next.
public static class StageBank
{
    public const int StageCount = 3;

    public static int HighestStageCleared { get; private set; }

    public static bool IsUnlocked(int stageNumber)
    {
        return stageNumber <= HighestStageCleared + 1;
    }

    public static bool AllStagesCleared => HighestStageCleared >= StageCount;

    // Called by StageEncounterController on a successful clear. No-ops on an already-cleared
    // stage so HighestStageCleared only ever moves forward (a stray double-call can't undo
    // progress).
    public static void MarkCleared(int stageNumber)
    {
        if (stageNumber > HighestStageCleared)
        {
            HighestStageCleared = stageNumber;
        }
    }

    // Field monster strength scaling (stage_system_design_v1.html §3) - read by
    // FieldMonsterSpawner, kept here so the formula has one home instead of being duplicated at
    // every call site. Iron/no-stages-cleared is exactly x1, so a session before any stage exists
    // plays identically to before this system existed.
    public static float FieldStrengthMultiplier => 1f + 0.25f * HighestStageCleared;
}
