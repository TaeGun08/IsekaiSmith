using UnityEngine;

// Common combat foundation shared by every monster role (monster_variety_design_v1.html) - HP,
// death, hit-flash, and the status-effect-received side (Fire/Poison/Lightning/Frost applied BY
// the player's weapon). Approach/attack behavior is a role's own concern via TickRole - see
// MeleeMonster/RangedMonster/MagicMonster/TankerMonster/SupportMonster. Spawned exclusively
// through MonsterFactory now (a bare Monster can't be instantiated - it's abstract), which is also
// the only place that knows the shared "tinted primitive" visual convention every role uses.
public abstract class Monster : MonoBehaviour
{
    protected const float FlashDuration = 0.15f;
    protected static readonly Vector3 BaseScale = Vector3.one * 0.9f;

    private float currentHP;
    private float flashTimer;
    private bool dead;

    // Field jabmops get gently scaled up as stages clear, stage-encounter monsters get scaled up
    // per stage/wave - see SetStrength(). Both default to x1 so the multiplier system behaves
    // exactly as it did before roles existed.
    private float hpMultiplier = 1f;
    private float damageMultiplier = 1f;
    private bool alwaysAggro;

    // Temporary outgoing-damage buff a SupportMonster grants nearby allies (Heal/ApplyDamageBuff) -
    // separate from damageMultiplier (SetStrength's permanent per-encounter scaling) so the two
    // stack multiplicatively without either one needing to know about the other.
    private float damageBuffMultiplier = 1f;
    private float damageBuffTimer;

    private ManaElement activeStatus;
    private float statusTimer;
    private float statusTickClock;
    private int statusTicksRemaining;
    private float statusPowerMultiplier = 1f;

    private Material bodyMaterial; // cached once - repeatedly reading Renderer.material has real overhead
    private Color baseColor;

    public bool IsAvailable => !dead;
    protected bool AlwaysAggro => alwaysAggro;
    // Combines the encounter's own strength scaling with any live ally buff from a SupportMonster -
    // every role's damage calculation should read this, not damageMultiplier directly.
    protected float EffectiveDamageMultiplier => damageMultiplier * damageBuffMultiplier;

    protected abstract float BaseMaxHP { get; }
    protected abstract float BaseMoveSpeed { get; }
    // Which role-flavored tint this monster starts with - SetTint (elite/boss retint) still wins
    // if called afterward, same as before roles existed.
    protected virtual Color DefaultColor => new Color(0.35f, 0.68f, 0.4f);
    // Multiplies BaseScale for this role specifically (e.g. a Tanker reads visibly bigger) -
    // SetScale (elite/boss) then multiplies again on top of this, not instead of it.
    protected virtual float RoleScale => 1f;

    protected virtual void Awake()
    {
        bodyMaterial = GetComponent<Renderer>().material; // instantiates once, reused from here on
        baseColor = DefaultColor;
        bodyMaterial.color = baseColor;
        transform.localScale = BaseScale * RoleScale;
        currentHP = BaseMaxHP * hpMultiplier;
    }

    // Scales this monster's HP/contact-or-projectile damage - called right after spawning (or
    // after ResetAt(), which is always full-HP) so currentHP is set exactly once at the new
    // multiplier, never mid-fight. FieldMonsterSpawner uses this for the gentle stage-clear-driven
    // strength ramp (StageBank.FieldStrengthMultiplier); the encounter controllers use it for
    // wave/floor/elite/boss scaling. See stage_system_design_v1.html §3.
    public void SetStrength(float hpMultiplier, float damageMultiplier)
    {
        this.hpMultiplier = hpMultiplier;
        this.damageMultiplier = damageMultiplier;
        currentHP = BaseMaxHP * hpMultiplier;
    }

    // Elites/stronger waves read as distinct without a dedicated mesh (same "reuse what exists,
    // just retint" approach as the weapon swing reuse - see ToolSwing.PlayWeaponSwing).
    public void SetTint(Color color)
    {
        baseColor = color;
        bodyMaterial.color = baseColor;
    }

    // Stage-lane monsters skip the short field AggroRadius entirely and start advancing on the
    // player the instant they spawn - "몬스터가 내려오는 느낌" (stage_system_design_v2.html §2).
    // Field jabmops never call this, so their normal wait-until-close behavior is untouched.
    public void SetAlwaysAggro(bool value)
    {
        alwaysAggro = value;
    }

    // A dungeon boss reads as "the big one" purely via a bigger silhouette (no dedicated boss
    // mesh) - same reuse-what-exists approach as SetTint. Multiplies this role's own RoleScale (a
    // Tanker boss is bigger still than a Tanker mob), not an absolute value, so this stays correct
    // regardless of RoleScale ever changing.
    public void SetScale(float multiplier)
    {
        transform.localScale = BaseScale * RoleScale * multiplier;
    }

    // Returns whether this hit was the killing blow - lets PlayerCombat know exactly once per
    // death (not "is currently dead") so it can roll a mana stone drop without double-rolling.
    public bool TakeDamage(float amount)
    {
        if (dead)
        {
            return false;
        }

        currentHP -= amount;
        flashTimer = FlashDuration;

        if (currentHP <= 0f)
        {
            Defeat();
            return true;
        }

        return false;
    }

    // Called by a SupportMonster on nearby living allies - restores a fraction of this monster's
    // own (multiplier-scaled) max HP, capped at that max.
    public void Heal(float fraction)
    {
        if (dead)
        {
            return;
        }

        currentHP = Mathf.Min(BaseMaxHP * hpMultiplier, currentHP + BaseMaxHP * hpMultiplier * fraction);
    }

    // Called by a SupportMonster on nearby living allies - temporary outgoing-damage buff, see
    // damageBuffMultiplier/EffectiveDamageMultiplier. A fresh application overwrites the timer
    // (not additive/stacking) - simple, matches how ApplyStatusEffect already treats reapplication.
    public void ApplyDamageBuff(float multiplier, float duration)
    {
        if (dead)
        {
            return;
        }

        damageBuffMultiplier = multiplier;
        damageBuffTimer = duration;
    }

    // Applied by an enchanted weapon hit (PlayerCombat, via ToolInventory.TryGetBestWeapon).
    // Fire/Poison tick bonus damage over time; Lightning stuns (no movement/attack); Frost slows
    // movement. Only one status is ever active - a fresh application replaces whatever was
    // running, rather than stacking (keeps this simple for a placeholder-level enchant system).
    // powerMultiplier comes from ManaGradeUtility.PowerMultiplier(equippedManaGrade) - Crude is
    // x1.0 so an un-graded (pre-ManaGrade-system) hit plays identically to before. Fire/Poison
    // scale tick damage (duration/tick count unchanged); Lightning/Frost scale duration (Frost's
    // slow *strength* stays flat - see mana_grade_and_ui_design_v1.html §1).
    public void ApplyStatusEffect(ManaElement element, float powerMultiplier = 1f)
    {
        if (dead || element == ManaElement.None)
        {
            return;
        }

        activeStatus = element;
        statusPowerMultiplier = powerMultiplier;
        statusTickClock = 0f;

        switch (element)
        {
            case ManaElement.Fire:
                statusTicksRemaining = ManaElementUtility.FireTicks;
                statusTimer = ManaElementUtility.FireTickInterval * ManaElementUtility.FireTicks;
                break;
            case ManaElement.Poison:
                statusTicksRemaining = ManaElementUtility.PoisonTicks;
                statusTimer = ManaElementUtility.PoisonTickInterval * ManaElementUtility.PoisonTicks;
                break;
            case ManaElement.Lightning:
                statusTimer = ManaElementUtility.LightningStunDuration * powerMultiplier;
                break;
            case ManaElement.Frost:
                statusTimer = ManaElementUtility.FrostSlowDuration * powerMultiplier;
                break;
        }
    }

    private void Defeat()
    {
        dead = true;
        activeStatus = ManaElement.None;
        gameObject.SetActive(false);
    }

    // Called by FieldMonsterSpawner when respawning this instance at a new scattered position
    // after RespawnDelay has passed.
    public void ResetAt(Vector3 groundPosition)
    {
        transform.position = groundPosition + Vector3.up * 0.5f;
        currentHP = BaseMaxHP * hpMultiplier;
        dead = false;
        activeStatus = ManaElement.None;
        damageBuffMultiplier = 1f;
        damageBuffTimer = 0f;

        // The killing blow always leaves flashTimer > 0 (TakeDamage sets it before checking for
        // death) and Update() stops running the instant the object deactivates, so that leftover
        // timer was still sitting there on respawn - Update()'s very first tick after
        // SetActive(true) would see flashTimer > 0 and immediately flash red again (user report:
        // "몬스터가 죽었다 다시 태어나면 빨간색으로 깜빡이는 경우가 잦아"). Clearing both here fixes it.
        flashTimer = 0f;
        bodyMaterial.color = baseColor;

        OnReset();
        gameObject.SetActive(true);
    }

    // Hook for a role's own per-instance timers (e.g. a cast/contact cooldown) to reset alongside
    // the shared state ResetAt already clears - keeps FieldMonsterSpawner's respawn from reusing
    // stale role-specific state from this instance's previous life.
    protected virtual void OnReset()
    {
    }

    private void Update()
    {
        if (dead || PlayerMotor.Instance == null)
        {
            return;
        }

        UpdateStatusEffect();
        UpdateDamageBuffTimer();
        UpdateFlashVisual();

        if (dead) // a DoT tick inside UpdateStatusEffect() above may have just finished it off
        {
            return;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Vector3 toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;
        float sqrDist = toPlayer.sqrMagnitude;
        bool stunned = activeStatus == ManaElement.Lightning;

        TickRole(toPlayer, sqrDist, stunned, playerPos);
    }

    // The one thing every role actually implements differently - how it reacts to the player each
    // frame (approach/hold distance/flee, when and how it deals damage). toPlayer/sqrDist are
    // horizontal-only (y already zeroed), matching how PlayerCombat's own range check works.
    protected abstract void TickRole(Vector3 toPlayer, float sqrDist, bool stunned, Vector3 playerPos);

    // Shared "walk toward the player" helper - Frost slows this the same way it always has,
    // regardless of which role is calling it.
    protected void MoveToward(Vector3 toPlayer, float speed)
    {
        float effectiveSpeed = activeStatus == ManaElement.Frost ? speed * ManaElementUtility.FrostSlowMultiplier : speed;
        Vector3 direction = toPlayer.normalized;
        transform.position += direction * effectiveSpeed * Time.deltaTime;
    }

    private void UpdateDamageBuffTimer()
    {
        if (damageBuffTimer <= 0f)
        {
            return;
        }

        damageBuffTimer -= Time.deltaTime;
        if (damageBuffTimer <= 0f)
        {
            damageBuffMultiplier = 1f;
        }
    }

    private void UpdateFlashVisual()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            bodyMaterial.color = flashTimer > 0f ? Color.red : baseColor;
        }
        else
        {
            // Sits between hit-flashes - a mild persistent tint showing an active burn/poison/
            // stun/slow, so the effect reads as ongoing rather than a one-off spark.
            bodyMaterial.color = activeStatus != ManaElement.None
                ? Color.Lerp(baseColor, ManaElementUtility.SparkColor(activeStatus), 0.5f)
                : baseColor;
        }
    }

    private void UpdateStatusEffect()
    {
        if (activeStatus == ManaElement.None)
        {
            return;
        }

        statusTimer -= Time.deltaTime;

        if (activeStatus == ManaElement.Fire || activeStatus == ManaElement.Poison)
        {
            statusTickClock -= Time.deltaTime;
            if (statusTickClock <= 0f && statusTicksRemaining > 0)
            {
                bool isFire = activeStatus == ManaElement.Fire;
                statusTickClock = isFire ? ManaElementUtility.FireTickInterval : ManaElementUtility.PoisonTickInterval;
                float tickDamage = (isFire ? ManaElementUtility.FireTickDamage : ManaElementUtility.PoisonTickDamage) * statusPowerMultiplier;
                statusTicksRemaining--;

                // No mana-stone-drop roll here (that's PlayerCombat's job off a direct hit) - a
                // DoT kill still gets the same defeat visual burst as a normal one, just without
                // the drop chance, so this stays a self-contained Monster/status concern.
                if (TakeDamage(tickDamage))
                {
                    HitEffects.Instance.MonsterDefeated(transform.position);
                }
            }
        }

        if (statusTimer <= 0f)
        {
            activeStatus = ManaElement.None;
        }
    }
}
