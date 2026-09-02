// Which AI/stat archetype a spawned Monster uses (monster_variety_design_v1.html §2) - drives
// MonsterFactory's choice of concrete subclass.
public enum MonsterRole
{
    Melee,
    Ranged,
    Magic,
    Tanker,
    Support
}
