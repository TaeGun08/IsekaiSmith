using System.Collections.Generic;
using UnityEngine;

// Reuses despawned Monster instances instead of Destroy()-ing them and CreatePrimitive-ing a fresh
// batch on every stage wave / dungeon floor transition (전체적인 최적화 패스, 사용자 요청
// 2026-08-24) - same Spawn/Despawn shape as GameObjectPool (used elsewhere for carried items/hit
// sparks), keyed by MonsterRole instead of a prefab reference since monsters are built at runtime
// as different component types per role, not instantiated from one shared prefab. Monster.ResetAt
// already existed for FieldMonsterSpawner's own respawn-in-place reuse, so this is mostly wiring
// that same mechanism up for StageEncounterController/DungeonEncounterController too, which used
// to destroy every monster at the end of each wave/floor.
public static class MonsterPool
{
    private static readonly Dictionary<MonsterRole, Queue<Monster>> pools = new Dictionary<MonsterRole, Queue<Monster>>();

    public static Monster Spawn(MonsterRole role, Vector3 groundPosition, Transform parent)
    {
        if (pools.TryGetValue(role, out Queue<Monster> queue))
        {
            while (queue.Count > 0)
            {
                Monster pooled = queue.Dequeue();
                if (pooled != null)
                {
                    pooled.transform.SetParent(parent, false);
                    pooled.ResetAt(groundPosition);
                    return pooled;
                }
            }
        }

        return MonsterFactory.Spawn(role, groundPosition, parent);
    }

    public static void Despawn(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        monster.gameObject.SetActive(false);

        if (!pools.TryGetValue(monster.Role, out Queue<Monster> queue))
        {
            queue = new Queue<Monster>();
            pools[monster.Role] = queue;
        }

        queue.Enqueue(monster);
    }
}
