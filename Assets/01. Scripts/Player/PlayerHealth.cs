using System;
using UnityEngine;

// Pure state - same convention as ResourceBank/ToolInventory/SalesCurrency/Reputation. Combat
// classes (Monster) call into TakeDamage(); PlayerHealthHUD polls Percent01 each frame to render
// a bar. No gold/material loss on death (matches Reputation's soft penalty for missed orders -
// this game has consistently avoided hard punishment). See combat_design_v1.html §4.
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

    public static void TakeDamage(float amount)
    {
        if (IsInvulnerable || currentHP <= 0f)
        {
            return;
        }

        currentHP = Mathf.Max(0f, currentHP - amount);
        OnDamaged?.Invoke();

        if (currentHP <= 0f)
        {
            Die();
        }
    }

    private static void Die()
    {
        OnDeath?.Invoke();
        currentHP = MaxHP;
        invulnerableUntil = Time.time + RespawnInvulnerabilitySeconds;

        if (PlayerMotor.Instance != null)
        {
            GameObject counterGO = GameObject.Find("SalesCounter");
            Vector3 respawnPoint = counterGO != null ? counterGO.transform.position : PlayerMotor.Instance.transform.position;
            PlayerMotor.Instance.transform.position = respawnPoint;
        }
    }
}
