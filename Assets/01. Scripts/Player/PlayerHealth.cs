using System;
using UnityEngine;

// Pure state - same convention as ResourceBank/ToolInventory/SalesCurrency/Reputation. Combat
// classes (Monster) call into TakeDamage(); PlayerHealthHUD polls Percent01 each frame to render
// a bar. Banked stock/gold/crafted tools survive death untouched (matches Reputation's soft
// penalty for missed orders - this game has generally avoided hard punishment); only unbanked
// *carried* resources are lost, handled by PlayerDeathPresentation (see CarryStack.ClearAll()).
// See combat_design_v1.html §4.
public static class PlayerHealth
{
    private const float MaxHP = 100f;
    private const float RespawnInvulnerabilitySeconds = 1.5f;

    private static float currentHP = MaxHP;
    private static float invulnerableUntil;

    public static float Max => MaxHP;
    public static float Current => currentHP;
    public static float Percent01 => currentHP / MaxHP;
    public static bool IsInvulnerable => Time.time < invulnerableUntil;

    public static event Action OnDamaged;
    public static event Action OnDeath;

    // Returns whether the hit actually landed (false during invulnerability or once already
    // dead) - lets Monster skip its hit-effect/camera-shake when nothing really happened.
    public static bool TakeDamage(float amount)
    {
        if (IsInvulnerable || currentHP <= 0f)
        {
            return false;
        }

        currentHP = Mathf.Max(0f, currentHP - amount);
        OnDamaged?.Invoke();

        if (currentHP <= 0f)
        {
            Die();
        }

        return true;
    }

    // Stays plain state here - no position change, no teleport. PlayerDeathPresentation
    // subscribes to OnDeath separately and owns the actual collapse/fade/teleport/revive
    // presentation (needs coroutines, which this static class can't run). See
    // combat_design_v1.html follow-up notes.
    private static void Die()
    {
        currentHP = MaxHP;
        invulnerableUntil = Time.time + RespawnInvulnerabilitySeconds;
        OnDeath?.Invoke();
    }
}
