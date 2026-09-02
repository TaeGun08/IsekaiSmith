using UnityEngine;

// Non-combatant - never deals damage, just tries to stay away from the player and periodically
// heals + buffs nearby living allies (Monster.Heal/ApplyDamageBuff). See
// monster_variety_design_v1.html §2/§3. A scene-wide FindObjectsByType scan here is fine given how
// few monsters are ever alive at once (a handful per wave/ring) - noted as a candidate for the
// separate optimization pass if that ever changes.
public class SupportMonster : Monster
{
    private const float FleeRadius = 5f;
    private const float BuffInterval = 3f;
    private const float BuffRadius = 6f;
    private const float HealFraction = 0.1f;
    private const float DamageBuffMultiplier = 1.2f;
    private const float DamageBuffDuration = 2f;

    protected override float BaseMaxHP => 16f;
    protected override float BaseMoveSpeed => 2f;
    protected override Color DefaultColor => new Color(0.35f, 0.55f, 0.75f); // cool blue - reads as "the healer"

    private float buffTimer;

    protected override void OnReset()
    {
        buffTimer = 0f;
    }

    protected override void TickRole(Vector3 toPlayer, float sqrDist, bool stunned, Vector3 playerPos)
    {
        if (!stunned && sqrDist < FleeRadius * FleeRadius)
        {
            MoveToward(-toPlayer, BaseMoveSpeed); // away from the player, never engages directly
        }

        buffTimer -= Time.deltaTime;
        if (buffTimer <= 0f)
        {
            buffTimer = BuffInterval;
            BuffNearbyAllies();
        }
    }

    private void BuffNearbyAllies()
    {
        Monster[] all = FindObjectsByType<Monster>(FindObjectsSortMode.None);
        float radiusSqr = BuffRadius * BuffRadius;

        foreach (Monster ally in all)
        {
            if (ally == this || ally == null || !ally.IsAvailable)
            {
                continue;
            }

            if ((ally.transform.position - transform.position).sqrMagnitude > radiusSqr)
            {
                continue;
            }

            ally.Heal(HealFraction);
            ally.ApplyDamageBuff(DamageBuffMultiplier, DamageBuffDuration);
        }
    }
}
