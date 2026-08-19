// Tracks dungeon unlock/progress state (roadmap ③ remainder - dungeon_design_v1.html). Unlocked
// once all 3 stages are cleared (the "보스 처치 -> 동굴 개방" gate, folded into "clearing Stage 3"
// since that stage's final wave already stands in for the stage boss - no separate boss system).
//
// A repeatable deep-dive, not a one-time clear (user request: "던전을 최대한 많이 만들어도 좋아" -
// with many floors now, expecting one unbroken clean run to the bottom would be unreasonably
// harsh). DeepestFloorCleared only ever grows - it's credited the instant a floor's boss dies
// (DungeonEncounterController), so a later death/retreat never erases progress already banked.
// OreBank reads this to gradually raise the ore-grade ceiling and roll bias as the player proves
// they can go deeper.
public static class DungeonBank
{
    public static bool IsUnlocked => StageBank.AllStagesCleared;
    public static int DeepestFloorCleared { get; private set; }

    public static void ReportFloorCleared(int floorNumber)
    {
        if (floorNumber > DeepestFloorCleared)
        {
            DeepestFloorCleared = floorNumber;
        }
    }
}
