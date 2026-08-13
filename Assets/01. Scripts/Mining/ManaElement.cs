using UnityEngine;

// Mana stone enchant element (game_design_doc.html §3 - 화염/독/번개/냉기 -> 화상/중독/마비/동상).
// Independent axis from WeaponType/OreGrade/CraftGrade - any weapon type will be able to carry any
// element once more weapon types exist. None means "no mana was spent on this craft".
// See weapon_diversity_design_v1.html §6.
public enum ManaElement
{
    None,
    Fire,
    Poison,
    Lightning,
    Frost
}

public static class ManaElementUtility
{
    // Status-effect tuning - kept here (not in Monster.cs) so a future second afflictable target
    // type (e.g. a stage/dungeon boss) can reuse the same numbers instead of Monster owning them.
    public const float FireTickDamage = 3f;
    public const float FireTickInterval = 1f;
    public const int FireTicks = 3; // 3x3 = 9 bonus damage over 3s - fast burn

    public const float PoisonTickDamage = 2f;
    public const float PoisonTickInterval = 1f;
    public const int PoisonTicks = 5; // 5x2 = 10 bonus damage over 5s - slower drain than fire

    public const float LightningStunDuration = 1.5f;

    public const float FrostSlowDuration = 3f;
    public const float FrostSlowMultiplier = 0.5f;

    public static string DisplayName(ManaElement element)
    {
        switch (element)
        {
            case ManaElement.Fire:
                return "Fire";
            case ManaElement.Poison:
                return "Poison";
            case ManaElement.Lightning:
                return "Lightning";
            case ManaElement.Frost:
                return "Frost";
            default:
                return "";
        }
    }

    // Tints the impact spark so an elemental hit reads differently at a glance (HitEffects reuses
    // this instead of always using its own flat PlayerHitColor) - no new VFX system needed.
    public static Color SparkColor(ManaElement element)
    {
        switch (element)
        {
            case ManaElement.Fire:
                return new Color(1f, 0.5f, 0.15f);
            case ManaElement.Poison:
                return new Color(0.55f, 0.85f, 0.3f);
            case ManaElement.Lightning:
                return new Color(1f, 0.95f, 0.3f);
            case ManaElement.Frost:
                return new Color(0.5f, 0.85f, 0.95f);
            default:
                return new Color(1f, 0.9f, 0.4f); // matches HitEffects.PlayerHitColor
        }
    }

    // Field-dropped mana is "매우 낮은 품질" (combat_design_v1.html §7) with no element of its own
    // - until stage-sourced graded/elemental mana exists (roadmap ③), which element a craft gets
    // is rolled uniformly at random rather than chosen, matching that "unpredictable" flavor.
    public static ManaElement RollRandom()
    {
        int roll = Random.Range(1, 5); // 1..4, skipping None
        return (ManaElement)roll;
    }
}
