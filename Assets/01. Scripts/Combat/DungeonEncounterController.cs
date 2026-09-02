using System;
using System.Collections.Generic;
using UnityEngine;

// Runs the dungeon fight (dungeon_design_v1.html) - split out from StageEncounterController
// because the two diverged in shape: a stage is a lane where waves approach from one end, the
// dungeon is an arena where each floor's monster pack spawns in a ring and closes in from every
// direction (user request: "던전의 몬스터들은 사방으로 몰려오도록").
//
// As many floors as DungeonFloorTable has rows (user request: "던전을 최대한 많이 만들어도
// 좋아... 데이터 시트를 이용해서") - loaded from Assets/05. Data/Resources/DungeonFloorTable.asset
// instead of a hardcoded array, so adding more floors is an Inspector edit, not a code change.
//
// Every floor is mob-pack-then-boss (user correction: "층마다 몹 무리와 보스가 있어야 해") -
// clear the mob ring, that floor's boss spawns, clear the boss, auto-advance to the next floor's
// mob ring. This is now a repeatable deep-dive, not a one-shot clear: DungeonBank.DeepestFloorCleared
// records the best depth ever reached (credited the instant each floor's boss dies, so a later
// death/retreat doesn't erase progress already banked) and OreBank reads it to gradually raise
// both the ore-grade ceiling and the chance of rolling the best available grade (user request:
// "초반엔 0퍼로 맞추고 최대한 깊게 내려갈수록... 던전이 어려워질수록 더 많이 오르도록").
public class DungeonEncounterController : MonoBehaviour
{
    private const string TablePath = "DungeonFloorTable";
    private const float MobRingRadius = 9f;
    private const float BossSpawnDistance = 6f;

    private static DungeonEncounterController instance;

    public static DungeonEncounterController Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("DungeonEncounterController");
                instance = go.AddComponent<DungeonEncounterController>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private const float BossScale = 1.7f;
    private static readonly Color BossTint = new Color(0.5f, 0.05f, 0.05f);

    private enum Phase
    {
        None,
        Mobs,
        Boss
    }

    private List<DungeonFloorTable.FloorRow> floors;
    private Phase phase = Phase.None;
    private Vector3 arenaCenter;

    private readonly List<Monster> activeMonsters = new List<Monster>();

    public IReadOnlyList<Monster> ActiveMonsters => activeMonsters;
    public bool IsEncounterActive { get; private set; }
    public bool IsBossPhase => phase == Phase.Boss;
    public int ActiveFloorNumber { get; private set; } // 1-based, stays the same across both that floor's mob and boss phases
    public int TotalFloors => Floors.Count;

    // bool = whether the encounter ended on a clean clear (false = death/retreat, no reward) -
    // DungeonSceneController listens for this to know when to send the player home.
    public event Action<bool> OnEncounterEnded;

    public int RemainingMonsterCount
    {
        get
        {
            int count = 0;
            foreach (Monster monster in activeMonsters)
            {
                if (monster != null && monster.IsAvailable)
                {
                    count++;
                }
            }

            return count;
        }
    }

    // Lazy-loaded once, not in Awake - Resources.Load works fine in Awake too, but keeping it
    // behind a property means a missing/misconfigured asset only breaks the dungeon specifically
    // instead of throwing during this singleton's very first bootstrap.
    private List<DungeonFloorTable.FloorRow> Floors
    {
        get
        {
            if (floors == null)
            {
                DungeonFloorTable table = Resources.Load<DungeonFloorTable>(TablePath);
                floors = table != null && table.floors.Count > 0 ? table.floors : FallbackFloors();
            }

            return floors;
        }
    }

    // Only used if the data sheet can't be found - matches the original 3-floor hardcoded table
    // so the dungeon still works (just shallow) rather than breaking outright.
    private static List<DungeonFloorTable.FloorRow> FallbackFloors()
    {
        Debug.LogWarning("DungeonFloorTable.asset not found in Resources - using a 3-floor fallback.");
        return new List<DungeonFloorTable.FloorRow>
        {
            new DungeonFloorTable.FloorRow { mobCount = 6, mobHpMultiplier = 2.6f, mobDamageMultiplier = 1.9f, bossHpMultiplier = 10f, bossDamageMultiplier = 3.5f },
            new DungeonFloorTable.FloorRow { mobCount = 7, mobHpMultiplier = 3.4f, mobDamageMultiplier = 2.4f, bossHpMultiplier = 13f, bossDamageMultiplier = 4.2f },
            new DungeonFloorTable.FloorRow { mobCount = 8, mobHpMultiplier = 4.2f, mobDamageMultiplier = 3f, bossHpMultiplier = 16f, bossDamageMultiplier = 5f },
        };
    }

    private void Awake()
    {
        PlayerHealth.OnDeath += HandlePlayerDeath;
    }

    private void OnDestroy()
    {
        PlayerHealth.OnDeath -= HandlePlayerDeath;
    }

    public bool CanBegin => !IsEncounterActive && DungeonBank.IsUnlocked;

    // center is the arena's middle (DungeonSceneController computes it, also where the player is
    // teleported) - every floor's mob ring and boss both spawn around this same point.
    public void BeginEncounter(Vector3 center)
    {
        if (!CanBegin)
        {
            return;
        }

        IsEncounterActive = true;
        arenaCenter = center;
        ActiveFloorNumber = 0;
        StartNextFloor();
    }

    public void RequestRetreat()
    {
        if (IsEncounterActive)
        {
            EndEncounter(cleared: false);
        }
    }

    private void Update()
    {
        if (!IsEncounterActive)
        {
            return;
        }

        for (int i = 0; i < activeMonsters.Count; i++)
        {
            if (activeMonsters[i].IsAvailable)
            {
                return; // still fighting
            }
        }

        if (phase == Phase.Mobs)
        {
            SpawnFloorBoss();
        }
        else if (phase == Phase.Boss)
        {
            // Credited the instant this floor's boss dies, not at the end of the whole run - a
            // later death/retreat on a deeper floor never erases a depth record already banked.
            DungeonBank.ReportFloorCleared(ActiveFloorNumber);

            if (ActiveFloorNumber >= Floors.Count)
            {
                CompleteDungeon();
            }
            else
            {
                ToastUI.Instance.Show("Floor " + ActiveFloorNumber + " Cleared! Descending to Floor " + (ActiveFloorNumber + 1) + "...", 2.5f);
                StartNextFloor();
            }
        }
    }

    private void StartNextFloor()
    {
        ClearActiveMonsters();
        ActiveFloorNumber++;
        phase = Phase.Mobs;

        DungeonFloorTable.FloorRow floor = Floors[ActiveFloorNumber - 1];
        SpawnRing(floor.mobCount, floor.mobHpMultiplier, floor.mobDamageMultiplier);
    }

    // Evenly spaced around a full circle (plus light jitter so it doesn't look mechanically
    // perfect) instead of clustered at one point - "사방으로 몰려오도록", closing in on the
    // player from every direction at once instead of a single approach line.
    private void SpawnRing(int count, float hpMult, float dmgMult)
    {
        float anglePerMonster = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float angle = anglePerMonster * i + UnityEngine.Random.Range(-anglePerMonster * 0.25f, anglePerMonster * 0.25f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * MobRingRadius;
            SpawnOne(arenaCenter + offset, hpMult, dmgMult, isBoss: false);
        }
    }

    private void SpawnFloorBoss()
    {
        ClearActiveMonsters();
        phase = Phase.Boss;

        DungeonFloorTable.FloorRow floor = Floors[ActiveFloorNumber - 1];
        ToastUI.Instance.Show("Floor " + ActiveFloorNumber + " Boss appears!", 3f);

        Vector3 position = arenaCenter + Vector3.forward * BossSpawnDistance;
        SpawnOne(position, floor.bossHpMultiplier, floor.bossDamageMultiplier, isBoss: true);
    }

    // Shallow floors stay mostly Melee/Tanker; deeper floors mix in the full roster including
    // Support (monster_variety_design_v1.html §4 - "층이 깊어질수록 5종 전부 등장 확률 상승").
    private static readonly MonsterRole[] ShallowFloorRoles = { MonsterRole.Melee, MonsterRole.Melee, MonsterRole.Tanker };
    private static readonly MonsterRole[] MidFloorRoles = { MonsterRole.Melee, MonsterRole.Tanker, MonsterRole.Ranged };
    private static readonly MonsterRole[] DeepFloorRoles = { MonsterRole.Melee, MonsterRole.Tanker, MonsterRole.Ranged, MonsterRole.Magic, MonsterRole.Support };

    private static MonsterRole ChooseMobRole(int floorNumber)
    {
        MonsterRole[] pool;
        if (floorNumber >= 3)
        {
            pool = DeepFloorRoles;
        }
        else if (floorNumber == 2)
        {
            pool = MidFloorRoles;
        }
        else
        {
            pool = ShallowFloorRoles;
        }

        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    private void SpawnOne(Vector3 position, float hpMult, float dmgMult, bool isBoss)
    {
        // Every boss is Tanker-based (bigger/tougher reads as "the big one" via SetScale on top of
        // TankerMonster's own RoleScale) - matches the existing "reuse what exists, just scale it
        // up" boss convention (monster_variety_design_v1.html §4).
        MonsterRole role = isBoss ? MonsterRole.Tanker : ChooseMobRole(ActiveFloorNumber);
        Monster monster = MonsterPool.Spawn(role, position, transform);
        monster.SetStrength(hpMult, dmgMult);
        monster.SetAlwaysAggro(true);

        if (isBoss)
        {
            monster.SetTint(BossTint);
            monster.SetScale(BossScale);
        }

        activeMonsters.Add(monster);
    }

    private void ClearActiveMonsters()
    {
        foreach (Monster leftover in activeMonsters)
        {
            MonsterPool.Despawn(leftover);
        }

        activeMonsters.Clear();
    }

    // Reaching the bottom of the whole table - rare, and no longer the only way to make progress
    // (each floor's boss already banked its own depth record), but still worth its own closure
    // moment. No gold/mana payout here either - the quarry upgrade (OreBank's depth-based ceiling
    // and roll bias) IS the reward, same as every individual floor clear.
    private void CompleteDungeon()
    {
        EndEncounter(cleared: true);
        ToastUI.Instance.Show("Every dungeon floor cleared! The quarry has given up its deepest veins.", 4f);
    }

    private void HandlePlayerDeath()
    {
        if (IsEncounterActive)
        {
            EndEncounter(cleared: false);
        }
    }

    private void EndEncounter(bool cleared)
    {
        ClearActiveMonsters();
        IsEncounterActive = false;
        phase = Phase.None;
        ActiveFloorNumber = 0;

        OnEncounterEnded?.Invoke(cleared);
    }
}
