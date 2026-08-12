using System.Collections.Generic;
using UnityEngine;

// Self-bootstrapping singleton (same Instance-auto-create pattern as GuidedTutorial) - scatters a
// small fixed number of Monster instances around the field and respawns each one a few seconds
// after it's defeated. No scene/prefab wiring - everything is built at runtime, anchored at the
// midpoint between the existing LumberCamp/Quarry GameObjects (왼쪽 벌목장 - 가운데 사냥터 - 오른쪽
// 채석장 layout the user already decided) so no new placement is needed and the spot is derived,
// not guessed. See combat_design_v1.html §3/§5.
public class FieldMonsterSpawner : MonoBehaviour
{
    private const int MonsterCount = 4;
    private const float SpawnAreaRadius = 8f;
    private const float MinSpacing = 3f;
    private const float RespawnDelay = 8f;
    private const int MaxPlacementAttempts = 20;

    private static FieldMonsterSpawner instance;

    public static FieldMonsterSpawner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("FieldMonsterSpawner");
                instance = go.AddComponent<FieldMonsterSpawner>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private Vector3 anchor;
    private readonly List<Monster> monsters = new List<Monster>();
    private readonly List<float> respawnTimers = new List<float>();

    // Called once from ResourceHUD.Start() alongside GuidedTutorial - idempotent so a stray
    // second call (e.g. scene reload) doesn't double-spawn.
    public void Bootstrap()
    {
        if (monsters.Count > 0)
        {
            return;
        }

        GameObject lumberCampGO = GameObject.Find("LumberCamp");
        GameObject quarryGO = GameObject.Find("Quarry");
        anchor = (lumberCampGO != null && quarryGO != null)
            ? Vector3.Lerp(lumberCampGO.transform.position, quarryGO.transform.position, 0.5f)
            : Vector3.zero;

        var placed = new List<Vector3>();
        for (int i = 0; i < MonsterCount; i++)
        {
            Vector3 position = FindSpawnPosition(placed);
            placed.Add(position);
            monsters.Add(Monster.Spawn(position));
            respawnTimers.Add(0f);
        }
    }

    private Vector3 FindSpawnPosition(List<Vector3> placed)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * SpawnAreaRadius;
            Vector3 candidate = anchor + new Vector3(offset.x, 0f, offset.y);

            bool farEnough = true;
            for (int i = 0; i < placed.Count; i++)
            {
                if (Vector3.Distance(candidate, placed[i]) < MinSpacing)
                {
                    farEnough = false;
                    break;
                }
            }

            if (farEnough)
            {
                return candidate;
            }
        }

        // Fallback if the area's too crowded to satisfy MinSpacing - still inside the field.
        Vector2 fallback = Random.insideUnitCircle * SpawnAreaRadius;
        return anchor + new Vector3(fallback.x, 0f, fallback.y);
    }

    private void Update()
    {
        for (int i = 0; i < monsters.Count; i++)
        {
            if (monsters[i].IsAvailable)
            {
                continue;
            }

            respawnTimers[i] += Time.deltaTime;
            if (respawnTimers[i] < RespawnDelay)
            {
                continue;
            }

            respawnTimers[i] = 0f;

            var placed = new List<Vector3>();
            for (int j = 0; j < monsters.Count; j++)
            {
                if (j != i && monsters[j].IsAvailable)
                {
                    placed.Add(monsters[j].transform.position);
                }
            }

            monsters[i].ResetAt(FindSpawnPosition(placed));
        }
    }
}
