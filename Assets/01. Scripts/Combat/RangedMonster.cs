using UnityEngine;

// Keeps its distance and pelts the player with plain-damage projectiles - no on-hit effect,
// unlike Magic. See monster_variety_design_v1.html §2.
public class RangedMonster : RangedAttackerMonster
{
    protected override float BaseMaxHP => 20f;
    protected override float BaseMoveSpeed => 1.6f;
    protected override float PreferredRange => 6f;
    protected override float CastInterval => 2f;
    protected override float ProjectileDamage => 4f;
    protected override Color ProjectileColor => new Color(0.85f, 0.75f, 0.3f); // dull arrow-gold
    protected override Color DefaultColor => new Color(0.55f, 0.5f, 0.3f);
}
