using UnityEngine;

public enum CraftGrade
{
    Rough,
    Common,
    Fine,
    Superior,
    Exceptional,
    Masterwork,
    Legendary
}

// Pure quality->grade mapping - no MonoBehaviour/UI dependency, so it can be reused later by
// selling/reputation systems without dragging the crafting minigame along with it.
public static class CraftGradeUtility
{
    private static readonly string[] TraitPool = { "Heavy", "Sharp", "Balanced", "Tempered" };

    public static CraftGrade GradeFor(float quality01)
    {
        if (quality01 >= 0.98f)
        {
            return CraftGrade.Legendary;
        }

        if (quality01 >= 0.9f)
        {
            return CraftGrade.Masterwork;
        }

        if (quality01 >= 0.75f)
        {
            return CraftGrade.Exceptional;
        }

        if (quality01 >= 0.6f)
        {
            return CraftGrade.Superior;
        }

        if (quality01 >= 0.45f)
        {
            return CraftGrade.Fine;
        }

        if (quality01 >= 0.25f)
        {
            return CraftGrade.Common;
        }

        return CraftGrade.Rough;
    }

    public static int BonusAmount(CraftGrade grade)
    {
        switch (grade)
        {
            case CraftGrade.Superior:
            case CraftGrade.Exceptional:
                return 1;
            case CraftGrade.Masterwork:
                return Random.value < 0.5f ? 2 : 1;
            case CraftGrade.Legendary:
                return 2;
            default:
                return 0;
        }
    }

    public static string DisplayName(CraftGrade grade)
    {
        switch (grade)
        {
            case CraftGrade.Rough:
                return "Rough";
            case CraftGrade.Common:
                return "Common";
            case CraftGrade.Fine:
                return "Fine";
            case CraftGrade.Superior:
                return "Superior";
            case CraftGrade.Exceptional:
                return "Exceptional";
            case CraftGrade.Masterwork:
                return "Masterwork";
            case CraftGrade.Legendary:
                return "Legendary";
            default:
                return grade.ToString();
        }
    }

    // Flavor-only trait roll - there's no per-item stat/inventory system yet for this to attach
    // to, so it's surfaced purely as completion-result flavor text (see design doc §6) until an
    // actual equipment/item-instance system exists to hook real stats onto.
    public static string RollTrait(CraftGrade grade)
    {
        float chance = TraitChance(grade);

        if (chance <= 0f || Random.value > chance)
        {
            return null;
        }

        return TraitPool[Random.Range(0, TraitPool.Length)];
    }

    private static float TraitChance(CraftGrade grade)
    {
        switch (grade)
        {
            case CraftGrade.Superior:
                return 0.15f;
            case CraftGrade.Exceptional:
                return 0.35f;
            case CraftGrade.Masterwork:
                return 0.65f;
            case CraftGrade.Legendary:
                return 1f;
            default:
                return 0f;
        }
    }
}
