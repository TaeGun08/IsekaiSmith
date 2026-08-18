// Base gold value for raw materials (Ore/Mana grades, flat Wood) - separate from SalesPricing
// (which prices *finished* CraftGrade tools sold to customers). Nothing in the normal game loop
// sells raw materials directly, so this table only exists as the reference price BlackMarket
// multiplies up/down from ("시세보다 훨씬 비쌈"/"시세보다 낮음" - black_market_design_v1.html §2/§3).
public static class MaterialPricing
{
    public static int OreValue(OreGrade grade)
    {
        switch (grade)
        {
            case OreGrade.Iron:
                return 2;
            case OreGrade.Steel:
                return 4;
            case OreGrade.Mithril:
                return 8;
            case OreGrade.Orichalcum:
                return 16;
            default:
                return 2;
        }
    }

    public static int ManaValue(ManaGrade grade)
    {
        switch (grade)
        {
            case ManaGrade.Crude:
                return 2;
            case ManaGrade.Common:
                return 4;
            case ManaGrade.Refined:
                return 8;
            case ManaGrade.Greater:
                return 14;
            case ManaGrade.Pristine:
                return 22;
            default:
                return 2;
        }
    }

    public const int WoodValue = 1;
}
