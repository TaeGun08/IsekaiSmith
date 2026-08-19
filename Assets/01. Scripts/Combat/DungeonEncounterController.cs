using System;
using System.Collections.Generic;
using UnityEngine;

// Runs the dungeon fight (dungeon_design_v1.html) - split out from StageEncounterController
// because the two diverged in shape: a stage is a lane where waves approach from one end, the
// dungeon is an arena where each floor's monster pack spawns in a ring and closes in from every
// direction (user request: "던전의 몬스터들은 사방으로 몰려오도록").
//
// 3 floors (지하 1층/2층/3층). Every floor is mob-pack-then-boss (user correction: "층마다 몹
// 무리와 보스가 있어야 해") - clear the mob ring, a floor boss spawns, clear the boss, auto-
// advance to the next floor's mob ring. The dungeon only counts as cleared once floor 3's boss is
// down.
public class DungeonEncounterController : MonoBehaviour
{
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

    private readonly struct FloorSpec
    {
        public readonly int MobCount;
        public readonly float MobHpMult;
        public readonly float MobDmgMult;
        public readonly float BossHpMult;
        public readonly float BossDmgMult;

        public FloorSpec(int mobCount, float mobHpMult, float mobDmgMult, float bossHpMult, float bossDmgMult)
        {
            MobCount = mobCount;
            MobHpMult = mobHpMult;
            MobDmgMult = mobDmgMult;
            BossHpMult = bossHpMult;
            BossDmgMult = bossDmgMult;
        }
    }

    // dungeon_design_v1.html §2's difficulty table - each floor's boss is meaningfully tougher
    // than that floor's own mob pack, and floor 3's boss is the dungeon's real final challenge.
    private static readonly FloorSpec[] Floors =
    {
        new FloorSpec(6, 2.6f, 1.9f, 10f, 3.5f),
        new FloorSpec(7, 3.4f, 2.4f, 13f, 4.2f),
        new FloorSpec(8, 4.2f, 3f, 16f, 5f),
    };

    private const float BossScale = 1.7f;

    private static readonly Color BossTint = new Color(0.5f, 0.05f, 0.05f);

    private enum Phase
    {
        None,
        Mobs,
        Boss
    }

    private Phase phase = Phase.None;
    private Vector3 arenaCenter;

    private readonly List<Monster> activeMonsters = new List<Monster>();

    public IReadOnlyList<Monster> ActiveMonsters => activeMonsters;
    public bool IsEncounterActive { get; private set; }
    public bool IsBossPhase => phase == Phase.Boss;
    public int ActiveFloorNumber { get; private set; } // 1-based, stays the same across both that floor's mob and boss phases
    public int TotalFloors => Floors.Length;

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
            if (ActiveFloorNumber >= Floors.Length)
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

        FloorSpec floor = Floors[ActiveFloorNumber - 1];
        SpawnRing(floor.MobCount, floor.MobHpMult, floor.MobDmgMult);
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

        FloorSpec floor = Floors[ActiveFloorNumber - 1];
        ToastUI.Instance.Show("Floor " + ActiveFloorNumber + " Boss appears!", 3f);

        Vector3 position = arenaCenter + Vector3.forward * BossSpawnDistance;
        SpawnOne(position, floor.BossHpMult, floor.BossDmgMult, isBoss: true);
    }

    private void SpawnOne(Vector3 position, float hpMult, float dmgMult, bool isBoss)
    {
        Monster monster = Monster.Spawn(position, transform);
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
            if (leftover != null)
            {
                Destroy(leftover.gameObject);
            }
        }

        activeMonsters.Clear();
    }

    // First-clear-only (DungeonBank.IsUnlocked goes false the instant MarkClearedOnce runs) - no
    // gold/mana payout, the quarry-ceiling upgrade (OreBank.DungeonCeiling) IS the reward (user
    // correction: "던전은... 업그레이드가 주 목적이야").
    private void CompleteDungeon()
    {
        DungeonBank.MarkClearedOnce();
        EndEncounter(cleared: true);

        ToastUI.Instance.Show("Dungeon Cleared! The quarry's veins run deeper now.", 4f);
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
