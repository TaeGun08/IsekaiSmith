using System.Collections.Generic;
using UnityEngine;

// Self-bootstrapping singleton (same Instance-auto-create pattern as GuidedTutorial) - scatters a
// small fixed number of Monster instances around the field and respawns each one a few seconds
// after it's defeated. No scene/prefab wiring - everything is built at runtime, anchored behind
// the midpoint between the existing LumberCamp/Quarry GameObjects (왼쪽 벌목장 - 가운데 사냥터 - 오른쪽
// 채석장 layout the user already decided, "더 뒤에" - further out than the camps, not level with
// them) so no new placement is needed and the spot is derived, not guessed. See
// combat_design_v1.html §3/§5.
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
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private Vector3 anchor;
    private Vector3 outwardDirection = Vector3.forward;
    private readonly List<Monster> monsters = new List<Monster>();
    private readonly List<float> respawnTimers = new List<float>();

    // Lets PlayerCombat (and anything else that needs "all field monsters") reuse this already-
    // tracked list instead of a scene-wide FindObjectsByType<Monster>() scan every attack check -
    // there's only ever a handful of monsters, but the search itself isn't free (최적화 요청).
    public IReadOnlyList<Monster> Monsters => monsters;

    // Lets StageGate derive its own position the same way this class derives the field hunting
    // ground's - "further out, same direction" from the LumberCamp/Quarry midpoint - instead of a
    // separately guessed coordinate. See stage_system_design_v1.html §2.
    public Vector3 Anchor => anchor;
    public Vector3 OutwardDirection => outwardDirection;
    public float SpawnRadius => SpawnAreaRadius;

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
        GameObject counterGO = GameObject.Find("SalesCounter");

        if (lumberCampGO != null && quarryGO != null)
        {
            Vector3 campMidpoint = Vector3.Lerp(lumberCampGO.transform.position, quarryGO.transform.position, 0.5f);

            // Push the hunting ground further out than the camps (not level with them) - reuses
            // the counter-to-camp depth as the backward offset (and its direction) instead of a
            // guessed constant, so this self-adjusts if that spacing ever changes.
            Vector3 offset;
            if (counterGO != null)
            {
                offset = new Vector3(0f, 0f, campMidpoint.z - counterGO.transform.position.z);
            }
            else
            {
                offset = new Vector3(0f, 0f, SpawnAreaRadius * 2f);
            }

            anchor = campMidpoint + offset;
            outwardDirection = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.forward;
        }
        else
        {
            anchor = Vector3.zero;
        }

        var placed = new List<Vector3>();
        for (int i = 0; i < MonsterCount; i++)
        {
            Vector3 position = FindSpawnPosition(placed);
            placed.Add(position);
            Monster monster = Monster.Spawn(position, transform);
            monster.SetStrength(StageBank.FieldStrengthMultiplier, StageBank.FieldStrengthMultiplier);
            monsters.Add(monster);
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

            // Re-applies the current field strength (not whatever it was when this monster last
            // spawned) so a stage cleared mid-session shows up on the next respawn instead of
            // waiting for a fresh Bootstrap().
            monsters[i].SetStrength(StageBank.FieldStrengthMultiplier, StageBank.FieldStrengthMultiplier);
            monsters[i].ResetAt(FindSpawnPosition(placed));
        }
    }
}
