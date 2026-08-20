// Which grade QUICK CRAFT currently produces - a single fixed value, not a range. Starts at
// Rough for free and steps up through Common/Fine as the player pays into ForgeUpgradeNode (the
// single reusable floor node near the smithy - customer_order_design_v7.html §2). Capped at Fine
// on purpose: precise crafting (the silhouette/minigame flow) stays the only route to
// Superior-and-up, so it keeps a reason to exist once quick crafting is maxed out.
// See customer_order_design_v7.html §0/§2.
public static class ForgeUpgrade
{
    // Index 0 is the free starting tier - Costs[0] is unused (never spent to reach it).
    private static readonly CraftGrade[] Tiers = { CraftGrade.Rough, CraftGrade.Common, CraftGrade.Fine };
    private static readonly int[] Costs = { 0, 50, 150 };

    private static int tierIndex;

    public static CraftGrade CurrentTier => Tiers[tierIndex];
    public static bool IsMaxed => tierIndex >= Tiers.Length - 1;

    // Null once maxed - ForgeUpgradeNode uses this to know when to hide/disable itself instead of
    // offering an upgrade that no longer exists.
    public static CraftGrade? NextTier => IsMaxed ? (CraftGrade?)null : Tiers[tierIndex + 1];
    public static int NextCost => IsMaxed ? 0 : Costs[tierIndex + 1];

    // Called by ForgeUpgradeNode on every approach - cheap to call repeatedly since it's a no-op
    // (false) whenever gold is short or the ceiling's already reached.
    public static bool TryUpgrade()
    {
        if (IsMaxed || !SalesCurrency.TrySpend(NextCost))
        {
            return false;
        }

        tierIndex++;
        return true;
    }
}
