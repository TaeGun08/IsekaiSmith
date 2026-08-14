using UnityEngine;

// Self-built field "잡몹" (weak monster) - a tinted Sphere primitive, same low-fidelity vector
// convention as Player/Customer (Capsule) and resource nodes (Box). Spawned and pooled by
// FieldMonsterSpawner. Pure distance-check AI (no colliders/triggers), matching every other
// interactable class in this project (CraftingStation/StorageDepot/OrderQueueManager). Can carry
// one ManaElement status effect at a time (a fresh application replaces whatever was active - see
// weapon_diversity_design_v1.html §6). See combat_design_v1.html §3.
public class Monster : MonoBehaviour
{
    private const float MaxHP = 30f;
    private const float ContactDamage = 5f;
    private const float ContactInterval = 1.2f;
    private const float AggroRadius = 4f;
    private const float AttackRadius = 1.2f;
    private const float MoveSpeed = 1.8f;
    private const float FlashDuration = 0.15f;

    private float currentHP;
    private float contactTimer;
    private float flashTimer;
    private bool dead;

    private ManaElement activeStatus;
    private float statusTimer;
    private float statusTickClock;
    private int statusTicksRemaining;
    private float statusPowerMultiplier = 1f;

    private Material bodyMaterial; // cached once - repeatedly reading Renderer.material has real overhead
    private Color baseColor;

    public bool IsAvailable => !dead;

    public static Monster Spawn(Vector3 groundPosition, Transform parent = null)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Slime";
        body.transform.SetParent(parent, false);
        body.transform.position = groundPosition + Vector3.up * 0.5f;
        body.transform.localScale = Vector3.one * 0.9f;
        Object.Destroy(body.GetComponent<Collider>()); // visual only - AI uses plain distance checks

        var monster = body.AddComponent<Monster>();
        monster.bodyMaterial = body.GetComponent<Renderer>().material; // instantiates once, reused from here on
        monster.baseColor = new Color(0.35f, 0.68f, 0.4f);
        monster.bodyMaterial.color = monster.baseColor;
        monster.currentHP = MaxHP;
        return monster;
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
        currentHP = MaxHP;
        contactTimer = 0f;
        dead = false;
        activeStatus = ManaElement.None;

        // The killing blow always leaves flashTimer > 0 (TakeDamage sets it before checking for
        // death) and Update() stops running the instant the object deactivates, so that leftover
        // timer was still sitting there on respawn - Update()'s very first tick after
        // SetActive(true) would see flashTimer > 0 and immediately flash red again (user report:
        // "몬스터가 죽었다 다시 태어나면 빨간색으로 깜빡이는 경우가 잦아"). Clearing both here fixes it.
        flashTimer = 0f;
        bodyMaterial.color = baseColor;

        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (dead || PlayerMotor.Instance == null)
        {
            return;
        }

        UpdateStatusEffect();

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

        if (dead) // a DoT tick inside UpdateStatusEffect() above may have just finished it off
        {
            return;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Vector3 toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;
        float sqrDist = toPlayer.sqrMagnitude;

        bool stunned = activeStatus == ManaElement.Lightning;

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
                if (PlayerHealth.TakeDamage(ContactDamage))
                {
                    HitEffects.Instance.MonsterHitPlayer(playerPos);
                }
            }
            return;
        }

        contactTimer = 0f;

        if (!stunned && sqrDist <= AggroRadius * AggroRadius)
        {
            float effectiveSpeed = activeStatus == ManaElement.Frost ? MoveSpeed * ManaElementUtility.FrostSlowMultiplier : MoveSpeed;
            Vector3 direction = toPlayer.normalized;
            transform.position += direction * effectiveSpeed * Time.deltaTime;
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
