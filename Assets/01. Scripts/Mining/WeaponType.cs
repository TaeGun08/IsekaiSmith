// Which kind of weapon a crafted item is (game_design_doc.html §3's 검/도끼/망치/단검 matrix).
// Every crafted-item system (ToolInventory, PlayerCombat, CraftingStation) is keyed on this from
// the start - adding a new value here plus its multipliers/swing mapping below is the whole cost
// of adding a weapon, no redesign of the inventory/combat plumbing needed. See
// weapon_diversity_design_v1.html §6, §8.
public enum WeaponType
{
    Sword,
    Axe,
    Hammer,
    Dagger
}

public static class WeaponTypeUtility
{
    public static string DisplayName(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Sword:
                return "Sword";
            case WeaponType.Axe:
                return "Axe";
            case WeaponType.Hammer:
                return "Hammer";
            case WeaponType.Dagger:
                return "Dagger";
            default:
                return type.ToString();
        }
    }

    // Multiplies OreGradeUtility.AttackPower - each weapon type trades power against attack speed
    // (below) rather than just being strictly better/worse, so switching weapons is a playstyle
    // choice, not a pure upgrade. Sword's 1x/1x is the untouched baseline (matches pre-expansion
    // behavior exactly). See weapon_diversity_design_v1.html §8.
    public static float AttackPowerMultiplier(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Sword:
                return 1f;
            case WeaponType.Axe:
                return 1.3f;
            case WeaponType.Hammer:
                return 1.8f;
            case WeaponType.Dagger:
                return 0.6f;
            default:
                return 1f;
        }
    }

    // Multiplies PlayerCombat's base attack interval (bigger = slower). Paired with
    // AttackPowerMultiplier above so sustained DPS stays roughly comparable across types (~17-19
    // at Iron) while the *feel* differs a lot - Hammer lands rare heavy hits, Dagger lands frequent
    // light ones (and re-applies mana-element status effects more often as a side effect of that).
    public static float AttackIntervalMultiplier(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Sword:
                return 1f;
            case WeaponType.Axe:
                return 1.15f;
            case WeaponType.Hammer:
                return 1.7f;
            case WeaponType.Dagger:
                return 0.55f;
            default:
                return 1f;
        }
    }
}
