using UnityEngine;

// Baseline role - walks straight at the player and deals contact damage in short range, exactly
// what the single Monster class used to do before roles existed. See monster_variety_design_v1.html
// §2. Contact damage/interval were tuned down here (5 -> 3, 1.2s -> 1.6s interval) as part of the
// same pass - stage_system_design_v1.html's Stage 1 Wave 1 (3 monsters converging on a fresh
// player at once) could deal over 100 damage before the wave died at the old numbers, more than a
// fresh player's whole health bar (사용자 요청 2026-08-24: "초반 몬스터부터 너무 쎄").
public class MeleeMonster : Monster
{
    protected override float BaseMaxHP => 30f;
    protected override float BaseMoveSpeed => 1.8f;
    protected virtual float AttackRadius => 1.2f;
    protected virtual float AggroRadius => 4f;
    protected virtual float ContactDamage => 3f;
    protected virtual float ContactInterval => 1.6f;

    private float contactTimer;

    protected override void OnReset()
    {
        contactTimer = 0f;
    }

    protected override void TickRole(Vector3 toPlayer, float sqrDist, bool stunned, Vector3 playerPos)
    {
        if (sqrDist <= AttackRadius * AttackRadius)
        {
            if (stunned)
            {
                contactTimer = 0f; // stays primed so it attacks right away once the stun ends
                return;
            }

            contactTimer -= Time.deltaTime;
            if (contactTimer <= 0f)
            {
                contactTimer = ContactInterval;

                // Only play the hit spark/shake if the hit actually landed (skips it during the
                // player's post-respawn invulnerability window, where nothing really happened).
                if (PlayerHealth.TakeDamage(ContactDamage * EffectiveDamageMultiplier))
                {
                    HitEffects.Instance.MonsterHitPlayer(playerPos);
                }
            }

            return;
        }

        contactTimer = 0f;

        if (!stunned && (AlwaysAggro || sqrDist <= AggroRadius * AggroRadius))
        {
            MoveToward(toPlayer, BaseMoveSpeed);
        }
    }
}
