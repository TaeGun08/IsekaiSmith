// Mana stone tier - mirrors OreGrade.cs exactly (see OreGrade.cs, weapon_diversity_design_v1.html
// §6/§3 and mana_grade_and_ui_design_v1.html §1). Independent from ManaElement: Element decides
// *which* status effect an enchant applies, Grade decides *how strong* that effect is. A weapon
// enchanted with no mana at all just carries ManaElement.None and this grade is meaningless for it.
public enum ManaGrade
{
    Crude,
    Common,
    Refined,
    Greater,
    Pristine
}

public static class ManaGradeUtility
{
    public static string DisplayName(ManaGrade grade)
    {
        switch (grade)
        {
            case ManaGrade.Crude:
                return "Crude";
            case ManaGrade.Common:
                return "Common";
            case ManaGrade.Refined:
                return "Refined";
            case ManaGrade.Greater:
                return "Greater";
            case ManaGrade.Pristine:
                return "Pristine";
            default:
                return grade.ToString();
        }
    }

    // Multiplies status-effect strength (Fire/Poison tick damage, Lightning stun duration, Frost
    // slow duration - see Monster.ApplyStatusEffect). Crude is exactly x1.0 so a session that's
    // only ever seen field-dropped (Crude) mana plays identically to before grades existed.
    public static float PowerMultiplier(ManaGrade grade)
    {
        switch (grade)
        {
            case ManaGrade.Crude:
                return 1.0f;
            case ManaGrade.Common:
                return 1.4f;
            case ManaGrade.Refined:
                return 1.9f;
            case ManaGrade.Greater:
                return 2.5f;
            case ManaGrade.Pristine:
                return 3.2f;
            default:
                return 1.0f;
        }
    }
}
