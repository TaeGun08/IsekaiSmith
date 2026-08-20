// Which owned weapon combo (WeaponType, OreGrade, ManaElement, ManaGrade) PlayerCombat actually
// fights with. CraftGrade isn't part of the identity here - it doesn't affect combat stats, only
// which specific stack gets sold/consumed, so equipping "Steel Sword" applies regardless of which
// quality copy of it the player happens to own.
//
// Before the player ever taps a row in PlayerInventoryUI there's no explicit choice yet, so
// Resolve() falls back to ToolInventory.TryGetBestWeapon (the old "always use the best-owned
// weapon" behavior) - a session where nothing's been equipped plays identically to before this
// class existed. If the explicitly-equipped combo gets sold out from under the player (order
// fulfillment spends stock), Resolve() also falls back rather than silently fighting unarmed.
// See weapon_diversity_design_v1.html §8.
public static class EquippedWeapon
{
    private static bool hasExplicitChoice;
    private static WeaponType chosenWeapon;
    private static OreGrade chosenOre;
    private static ManaElement chosenElement;
    private static ManaGrade chosenManaGrade;

    // Lets GuidedTutorial detect "the player equipped something" without duplicating the
    // stock-fallback logic Resolve() already does - only ever true once a row has actually been
    // tapped in PlayerInventoryUI.
    public static bool HasExplicitChoice => hasExplicitChoice;

    public static void Equip(WeaponType weapon, OreGrade oreGrade, ManaElement element, ManaGrade manaGrade)
    {
        hasExplicitChoice = true;
        chosenWeapon = weapon;
        chosenOre = oreGrade;
        chosenElement = element;
        chosenManaGrade = manaGrade;
    }

    public static void Resolve(out WeaponType weapon, out OreGrade oreGrade, out ManaElement element, out ManaGrade manaGrade)
    {
        if (hasExplicitChoice && ToolInventory.OwnsAny(chosenWeapon, chosenOre, chosenElement, chosenManaGrade))
        {
            weapon = chosenWeapon;
            oreGrade = chosenOre;
            element = chosenElement;
            manaGrade = chosenManaGrade;
            return;
        }

        ToolInventory.TryGetBestWeapon(out weapon, out oreGrade, out element, out manaGrade);
    }

    // Lets PlayerInventoryUI mark whichever row is actually active in combat right now - true both
    // for an explicit pick still in stock and for the auto-resolved best weapon when nothing's been
    // explicitly equipped yet, so the UI never shows "nothing equipped" while combat is in fact
    // using something.
    public static bool IsEquipped(WeaponType weapon, OreGrade oreGrade, ManaElement element, ManaGrade manaGrade)
    {
        Resolve(out WeaponType resolvedWeapon, out OreGrade resolvedOre, out ManaElement resolvedElement, out ManaGrade resolvedManaGrade);
        return resolvedWeapon == weapon && resolvedOre == oreGrade && resolvedElement == element && resolvedManaGrade == manaGrade;
    }
}
