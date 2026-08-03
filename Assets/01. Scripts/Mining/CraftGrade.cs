using UnityEngine;

public enum CraftGrade
{
    Rough,
    Fine,
    Superior,
    Masterwork
}

// Pure quality->grade mapping - no MonoBehaviour/UI dependency, so it can be reused later by
// selling/reputation systems without dragging the crafting minigame along with it.
public static class CraftGradeUtility
{
    public static CraftGrade GradeFor(float quality01)
    {
        if (quality01 >= 0.9f)
        {
            return CraftGrade.Masterwork;
        }

        if (quality01 >= 0.7f)
        {
            return CraftGrade.Superior;
        }

        if (quality01 >= 0.4f)
        {
            return CraftGrade.Fine;
        }

        return CraftGrade.Rough;
    }

    public static int BonusAmount(CraftGrade grade)
    {
        switch (grade)
        {
            case CraftGrade.Superior:
                return 1;
            case CraftGrade.Masterwork:
                return Random.value < 0.5f ? 2 : 1;
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
            case CraftGrade.Fine:
                return "Fine";
            case CraftGrade.Superior:
                return "Superior";
            case CraftGrade.Masterwork:
                return "Masterwork";
            default:
                return grade.ToString();
        }
    }
}
