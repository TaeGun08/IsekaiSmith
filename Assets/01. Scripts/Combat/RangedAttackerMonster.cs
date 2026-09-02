using UnityEngine;

// Shared base for the two "keep distance and fire" roles (Ranged/Magic) - holds PreferredRange
// once in range (retreating if the player closes in too far, approaching if too far away) and
// fires a Projectile on a timer. Magic's only difference is what happens on a landed hit (a slow
// debuff) - see OnProjectileHit. See monster_variety_design_v1.html §2/§3.
public abstract class RangedAttackerMonster : Monster
{
    protected abstract float PreferredRange { get; }
    protected abstract float CastInterval { get; }
    protected abstract float ProjectileDamage { get; }
    protected abstract Color ProjectileColor { get; }

    private float castTimer;

    protected override void OnReset()
    {
        castTimer = 0f;
    }

    protected override void TickRole(Vector3 toPlayer, float sqrDist, bool stunned, Vector3 playerPos)
    {
        float preferredSqr = PreferredRange * PreferredRange;
        float retreatRange = PreferredRange * 0.6f;
        float retreatSqr = retreatRange * retreatRange;

        if (!stunned)
        {
            if (sqrDist > preferredSqr)
            {
                MoveToward(toPlayer, BaseMoveSpeed);
            }
            else if (sqrDist < retreatSqr)
            {
                MoveToward(-toPlayer, BaseMoveSpeed); // back off to keep its distance
            }
        }

        castTimer -= Time.deltaTime;
        if (castTimer <= 0f)
        {
            castTimer = CastInterval;
            FireAt(playerPos);
        }
    }

    private void FireAt(Vector3 playerPos)
    {
        Projectile.Fire(transform.position + Vector3.up * 0.5f, playerPos, ProjectileDamage * EffectiveDamageMultiplier, OnProjectileHit, ProjectileColor);
    }

    // Magic overrides this to slow the player on a landed hit - see MagicMonster.
    protected virtual void OnProjectileHit(Vector3 hitPosition)
    {
    }
}
