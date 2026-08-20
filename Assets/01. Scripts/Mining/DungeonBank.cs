// Tracks dungeon unlock/progress state (roadmap ③ remainder - dungeon_design_v1.html). Unlocked
// after clearing just Stage 1 (사용자 요청 2026-08-20: "스테이지는 하나만 클리어하면 던전이
// 해금되는 식으로") - previously gated on all 3 stages, folded into "clearing Stage 3" as a stand-in
// boss-kill moment, but the guided tutorial now walks the player through the dungeon as one of its
// own steps (guided_tutorial_design_v2.html), so making them clear all 3 stages first would have
// stretched that one step into most of the game's stage content. Stage 2/3 remain individually
// gated by StageBank.IsUnlocked as before - only the dungeon's own unlock threshold moved.
//
// A repeatable deep-dive, not a one-time clear (user request: "던전을 최대한 많이 만들어도 좋아" -
// with many floors now, expecting one unbroken clean run to the bottom would be unreasonably
// harsh). DeepestFloorCleared only ever grows - it's credited the instant a floor's boss dies
// (DungeonEncounterController), so a later death/retreat never erases progress already banked.
// OreBank reads this to gradually raise the ore-grade ceiling and roll bias as the player proves
// they can go deeper.
public static class DungeonBank
{
    public static bool IsUnlocked => StageBank.HighestStageCleared >= 1;
    public static int DeepestFloorCleared { get; private set; }

    public static void ReportFloorCleared(int floorNumber)
    {
        if (floorNumber > DeepestFloorCleared)
        {
            DeepestFloorCleared = floorNumber;
        }
    }
}
