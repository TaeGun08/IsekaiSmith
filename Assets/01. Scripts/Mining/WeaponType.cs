// Which kind of weapon a CraftingStation forges (game_design_doc.html §3's 검/도끼/망치/단검
// matrix). Only Sword exists today, but every crafted-item system (ToolInventory, PlayerCombat,
// CraftingStation) is keyed on this from the start - adding Axe/Hammer/Dagger later means adding
// enum values + new CraftingStation instances, not redesigning the inventory/combat plumbing.
// See weapon_diversity_design_v1.html §6.
public enum WeaponType
{
    Sword
}

public static class WeaponTypeUtility
{
    public static string DisplayName(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Sword:
                return "Sword";
            default:
                return type.ToString();
        }
    }
}
