using UnityEngine;

// High HP, low threat, visibly bigger - otherwise identical to MeleeMonster (same contact-and-
// chase behavior, just different numbers), so it's a thin override rather than its own TickRole.
// See monster_variety_design_v1.html §2.
public class TankerMonster : MeleeMonster
{
    protected override float BaseMaxHP => 70f;
    protected override float BaseMoveSpeed => 1.2f;
    protected override float ContactDamage => 4f;
    protected override float ContactInterval => 1.4f;
    protected override float RoleScale => 1.3f;
    protected override Color DefaultColor => new Color(0.42f, 0.4f, 0.3f); // dull ochre - reads as "armored/heavy" against Melee's green
}
