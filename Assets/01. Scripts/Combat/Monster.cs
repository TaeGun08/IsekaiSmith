using UnityEngine;

// Self-built field "잡몹" (weak monster) - a tinted Sphere primitive, same low-fidelity vector
// convention as Player/Customer (Capsule) and resource nodes (Box). Spawned and pooled by
// FieldMonsterSpawner. Pure distance-check AI (no colliders/triggers), matching every other
// interactable class in this project (CraftingStation/StorageDepot/OrderQueueManager). See
// combat_design_v1.html §3.
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

    private Material bodyMaterial; // cached once - repeatedly reading Renderer.material has real overhead
    private Color baseColor;

    public bool IsAvailable => !dead;

    public static Monster Spawn(Vector3 groundPosition)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Slime";
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

    private void Defeat()
    {
        dead = true;
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

        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            bodyMaterial.color = flashTimer > 0f ? Color.red : baseColor;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Vector3 toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;
        float sqrDist = toPlayer.sqrMagnitude;

        if (sqrDist <= AttackRadius * AttackRadius)
        {
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

        if (sqrDist <= AggroRadius * AggroRadius)
        {
            Vector3 direction = toPlayer.normalized;
            transform.position += direction * MoveSpeed * Time.deltaTime;
        }
    }
}
