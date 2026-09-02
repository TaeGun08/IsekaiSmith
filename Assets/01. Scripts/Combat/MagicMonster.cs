using UnityEngine;

// Same "keep distance and fire" pattern as Ranged, but a landed hit also slows the player briefly -
// PlayerMotor.ApplySlow mirrors how Frost already slows a monster, just applied the other way.
// See monster_variety_design_v1.html §2/§3.
public class MagicMonster : RangedAttackerMonster
{
    private const float SlowDuration = 3f;
    private const float SlowMultiplier = 0.5f;

    protected override float BaseMaxHP => 18f;
    protected override float BaseMoveSpeed => 1.4f;
    protected override float PreferredRange => 7f;
    protected override float CastInterval => 2.5f;
    protected override float ProjectileDamage => 3f;
    protected override Color ProjectileColor => new Color(0.55f, 0.4f, 0.85f); // arcane violet
    protected override Color DefaultColor => new Color(0.4f, 0.32f, 0.6f);

    protected override void OnProjectileHit(Vector3 hitPosition)
    {
        if (PlayerMotor.Instance != null)
        {
            PlayerMotor.Instance.ApplySlow(SlowDuration, SlowMultiplier);
        }
    }
}
