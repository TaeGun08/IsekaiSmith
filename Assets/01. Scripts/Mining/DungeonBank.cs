// Tracks dungeon unlock/clear state (roadmap ③ remainder - dungeon_design_v1.html). Unlocked
// once all 3 stages are cleared (the "보스 처치 -> 동굴 개방" gate, folded into "clearing Stage 3"
// since that stage's final wave already stands in for the stage boss - no separate boss system).
//
// One-time only, same as a stage (user correction: "던전은 최초클리어만 가능하고... 업그레이드가
// 주 목적이야") - the dungeon's entire point is the quarry-ceiling upgrade on first clear, not a
// repeatable farm, so IsUnlocked goes false again the moment HasClearedOnce flips true.
public static class DungeonBank
{
    public static bool HasClearedOnce { get; private set; }
    public static bool IsUnlocked => StageBank.AllStagesCleared && !HasClearedOnce;

    public static void MarkClearedOnce()
    {
        HasClearedOnce = true;
    }
}
