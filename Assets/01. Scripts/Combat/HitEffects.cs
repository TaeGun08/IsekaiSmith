using System.Collections;
using UnityEngine;

// Lightweight combat "juice" - impact sparks + camera shake, since combat felt too bare with
// nothing but a color flash on the monster (user report: "현재 너무 허전해"). Mirrors OreNode/
// WoodNode's proven fragment-burst technique (spawn N small pieces flying outward + shrinking +
// spinning, then despawn via the pool) rather than inventing a new VFX approach - but self-
// contained here since Monster/PlayerCombat are runtime-built, not scene-wired with a
// fragmentPrefab to assign in an Inspector. Self-bootstrapping singleton (GuidedTutorial-style).
// See combat_design_v1.html follow-up notes.
public class HitEffects : MonoBehaviour
{
    private const int SparkCount = 5;
    private const int DefeatSparkCount = 8;
    private const float SparkSpeed = 3.5f;
    private const float SparkLifetime = 0.25f;

    private static readonly Color PlayerHitColor = new Color(1f, 0.9f, 0.4f); // warm gold spark
    private static readonly Color MonsterHitColor = new Color(0.85f, 0.25f, 0.2f); // red - "you got hit"
    private static readonly Color DefeatColor = new Color(0.5f, 0.85f, 0.55f); // matches the slime's own tint

    private static HitEffects instance;
    private static GameObject sparkTemplate;

    public static HitEffects Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("HitEffects");
                instance = go.AddComponent<HitEffects>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    // Player's sword connecting with a monster - light shake, doesn't want to feel heavier than
    // getting hit does.
    public void PlayerHitMonster(Vector3 position)
    {
        SpawnSparks(position, PlayerHitColor, SparkCount);
        Shake(0.08f, 0.08f);
    }

    // Monster's contact attack connecting with the player - a bit stronger, getting hit should
    // read as more alarming than landing one.
    public void MonsterHitPlayer(Vector3 position)
    {
        SpawnSparks(position, MonsterHitColor, SparkCount);
        Shake(0.12f, 0.1f);
    }

    // A bigger burst specifically on the killing blow, distinct from the regular per-hit sparks.
    public void MonsterDefeated(Vector3 position)
    {
        SpawnSparks(position, DefeatColor, DefeatSparkCount);
        Shake(0.15f, 0.12f);
    }

    private static void Shake(float amplitude, float duration)
    {
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.Shake(amplitude, duration);
        }
    }

    private void SpawnSparks(Vector3 origin, Color color, int count)
    {
        GameObject template = GetSparkTemplate();

        for (int i = 0; i < count; i++)
        {
            GameObject spark = GameObjectPool.Instance.Spawn(template, origin, Random.rotation);
            spark.GetComponent<Renderer>().material.color = color;

            Vector3 direction = Random.insideUnitSphere;
            direction.y = Mathf.Abs(direction.y) + 0.3f;
            direction.Normalize();

            StartCoroutine(SparkRoutine(spark.transform, origin, direction));
        }
    }

    private IEnumerator SparkRoutine(Transform spark, Vector3 origin, Vector3 direction)
    {
        Vector3 startScale = spark.localScale;
        float elapsed = 0f;

        while (elapsed < SparkLifetime && spark != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / SparkLifetime);

            Vector3 travel = direction * SparkSpeed * t;
            travel.y -= 4f * t * t; // gentle gravity arc, same trick OreNode's fragments use
            spark.position = origin + travel;
            spark.localScale = startScale * (1f - t);
            spark.Rotate(direction * 360f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (spark != null)
        {
            GameObjectPool.Instance.Despawn(spark.gameObject);
        }
    }

    // Lazily-built reusable template (same technique as CarryItemTemplates - GameObjectPool only
    // needs a GameObject reference to Instantiate from, not a real .prefab asset). Parked far
    // below the map instead of disabled - Unity's Instantiate() clones the exact active-state of
    // its source, so a SetActive(false) template would spawn invisible first-use clones.
    private static GameObject GetSparkTemplate()
    {
        if (sparkTemplate == null)
        {
            sparkTemplate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sparkTemplate.name = "HitSparkTemplate";
            sparkTemplate.transform.position = new Vector3(0f, -500f, 0f);
            sparkTemplate.transform.localScale = Vector3.one * 0.12f;
            Object.Destroy(sparkTemplate.GetComponent<Collider>());
            Object.DontDestroyOnLoad(sparkTemplate);
        }

        return sparkTemplate;
    }
}
