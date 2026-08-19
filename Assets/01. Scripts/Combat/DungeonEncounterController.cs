using System;
using System.Collections.Generic;
using UnityEngine;

// Runs the dungeon fight (dungeon_design_v1.html) - split out from StageEncounterController
// (user request: "던전씬을 따로 만들어서 관리하는 게 좋을 것 같아") because the two diverged in
// shape: a stage is a lane where waves approach from one end, the dungeon is an arena where each
// floor's monster pack spawns in a ring and closes in from every direction (user request: "던전의
// 몬스터들은 사방으로 몰려오도록"), and only the dungeon has a distinct mob-phase-then-boss-phase
// floor structure.
//
// 3 floors total (지하 1층/2층/3층). Floors 1-2 are pure mob packs - clear one, auto-advance to
// the next (no exit/re-enter). Floor 3 is the same mob pack, but clearing it spawns a single
// boss monster instead of advancing further; the dungeon only counts as cleared once the boss is
// down (user request: "사방에 몰려오는 몬스터를 전부 잡았다면 보스 몬스터가 등장... 보스
// 몬스터를 클리어 해야 해당 던전이 클리어").
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

        public FloorSpec(int mobCount, float mobHpMult, float mobDmgMult)
        {
            MobCount = mobCount;
            MobHpMult = mobHpMult;
            MobDmgMult = mobDmgMult;
        }
    }

    // dungeon_design_v1.html §2's difficulty table - harder than any single stage floor-for-floor,
    // since there's no elite mixed into the mob pack anymore (the boss phase below replaces that).
    private static readonly FloorSpec[] Floors =
    {
        new FloorSpec(6, 2.6f, 1.9f),
        new FloorSpec(7, 3.4f, 2.4f),
        new FloorSpec(8, 4.2f, 3f),
    };

    private const float BossHpMult = 14f;
    private const float BossDmgMult = 5f;
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
    public int ActiveFloorNumber { get; private set; } // 1-based
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
    // teleported) - every floor's mob ring and the boss both spawn around this same point.
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
            if (ActiveFloorNumber >= Floors.Length)
            {
                SpawnBoss();
            }
            else
            {
                ToastUI.Instance.Show("Floor " + ActiveFloorNumber + " Cleared! Descending to Floor " + (ActiveFloorNumber + 1) + "...", 2.5f);
                StartNextFloor();
            }
        }
        else if (phase == Phase.Boss)
        {
            CompleteDungeon();
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

    private void SpawnBoss()
    {
        ClearActiveMonsters();
        phase = Phase.Boss;
        ToastUI.Instance.Show("The Dungeon Boss appears!", 3f);

        Vector3 position = arenaCenter + Vector3.forward * BossSpawnDistance;
        SpawnOne(position, BossHpMult, BossDmgMult, isBoss: true);
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

        ToastUI.Instance.Show("Dungeon Boss Defeated! The quarry's veins run deeper now.", 4f);
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
