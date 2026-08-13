// Raw-material tier (game_design_doc.html §3 "광물 등급") - completely separate axis from
// CraftGrade (finished-item quality: Rough~Legendary). A single crafted sword carries both at
// once, e.g. "Steel, Fine".
public enum OreGrade
{
    Iron,
    Steel,
    Mithril,
    Orichalcum
}

public static class OreGradeUtility
{
    public static string DisplayName(OreGrade grade)
    {
        switch (grade)
        {
            case OreGrade.Iron:
                return "Iron";
            case OreGrade.Steel:
                return "Steel";
            case OreGrade.Mithril:
                return "Mithril";
            case OreGrade.Orichalcum:
                return "Orichalcum";
            default:
                return grade.ToString();
        }
    }

    // How much attack power a sword forged from this ore grade carries - read by PlayerCombat
    // once weapon-based damage replaces the flat placeholder it shipped with (combat_design_v1.html
    // Phase 2 TODO). Iron matches that old flat value exactly, so a session with no sword crafted
    // yet plays identically to before this system existed. See weapon_diversity_design_v1.html §4.
    public static float AttackPower(OreGrade grade)
    {
        switch (grade)
        {
            case OreGrade.Iron:
                return 10f;
            case OreGrade.Steel:
                return 16f;
            case OreGrade.Mithril:
                return 24f;
            case OreGrade.Orichalcum:
                return 34f;
            default:
                return 10f;
        }
    }
}
