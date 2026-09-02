using System;
using System.Collections.Generic;
using UnityEngine;

// Runs one stage's wave-clear fight (stage_system_design_v2.html §2/§4) - spawns a burst of
// Monster instances at the lane's NE end (already aggroed - see Monster.SetAlwaysAggro), waits
// for the current wave to be fully cleared before spawning the next, and rewards + unlocks on a
// clean finish. Deliberately kept separate from FieldMonsterSpawner's always-on respawning pool
// (different lifecycle: this one starts, runs a fixed number of waves, and ends), but exposes its
// own monster list the same way so PlayerCombat can target both.
//
// Doesn't know anything about scenes/UI - StageSceneController owns loading/unloading StageScene
// and moving the player, and reacts to OnEncounterEnded to know when to send the player home.
//
// Dungeon handling moved out to DungeonEncounterController/DungeonSceneController (user request:
// "던전씬을 따로 만들어서 관리하는 게 좋을 것 같아") - the two encounter types diverged enough
// (lane vs surround-the-player arena, uniform waves vs mob-then-boss) that sharing this one class
// via an IsDungeonEncounter flag was starting to hide more than it shared.
public class StageEncounterController : MonoBehaviour
{
    private const float SpawnSpread = 3f;
    private const float MinSpacing = 1.6f;
    private const int MaxPlacementAttempts = 20;

    private static StageEncounterController instance;

    public static StageEncounterController Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StageEncounterController");
                instance = go.AddComponent<StageEncounterController>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private readonly struct WaveSpec
    {
        public readonly int NormalCount;
        public readonly float NormalHpMult;
        public readonly float NormalDmgMult;
        public readonly int EliteCount;
        public readonly float EliteHpMult;
        public readonly float EliteDmgMult;

        public WaveSpec(int normalCount, float normalHpMult, float normalDmgMult, int eliteCount = 0, float eliteHpMult = 1f, float eliteDmgMult = 1f)
        {
            NormalCount = normalCount;
            NormalHpMult = normalHpMult;
            NormalDmgMult = normalDmgMult;
            EliteCount = eliteCount;
            EliteHpMult = eliteHpMult;
            EliteDmgMult = eliteDmgMult;
        }
    }

    // stage_system_design_v1.html §3's difficulty table, indexed [stageNumber - 1][waveIndex] -
    // loaded from the data sheet (Assets/05. Data/Resources/StageWaveTable.asset) instead of a
    // hardcoded array (user request: "데이터 시트를 이용해서 미리... 스테이지도 마찬가지로"), so
    // tuning waves is an Inspector edit instead of a code change. Falls back to this exact same
    // table if the asset can't be found, so a missing/misconfigured asset degrades instead of
    // breaking the stage system outright.
    private static readonly WaveSpec[][] FallbackStageWaves =
    {
        new[] { new WaveSpec(3, 1.3f, 1.2f), new WaveSpec(2, 1.3f, 1.2f, 1, 2.2f, 1.6f) },
        new[] { new WaveSpec(4, 1.8f, 1.5f), new WaveSpec(3, 1.8f, 1.5f, 1, 3f, 2f) },
        new[] { new WaveSpec(5, 2.4f, 1.8f), new WaveSpec(3, 2.4f, 1.8f, 1, 4f, 2.5f) },
    };

    private WaveSpec[][] stageWaves;

    private WaveSpec[][] StageWaves
    {
        get
        {
            if (stageWaves == null)
            {
                stageWaves = LoadStageWaves();
            }

            return stageWaves;
        }
    }

    private static WaveSpec[][] LoadStageWaves()
    {
        StageWaveTable table = Resources.Load<StageWaveTable>("StageWaveTable");
        if (table == null || table.waves.Count == 0)
        {
            Debug.LogWarning("StageWaveTable.asset not found in Resources - using the built-in fallback wave table.");
            return FallbackStageWaves;
        }

        var result = new WaveSpec[StageBank.StageCount][];
        for (int s = 0; s < StageBank.StageCount; s++)
        {
            int stageNumber = s + 1;
            var rows = new List<StageWaveTable.WaveRow>();
            foreach (StageWaveTable.WaveRow row in table.waves)
            {
                if (row.stageNumber == stageNumber)
                {
                    rows.Add(row);
                }
            }

            if (rows.Count == 0)
            {
                result[s] = FallbackStageWaves[Mathf.Min(s, FallbackStageWaves.Length - 1)];
                continue;
            }

            rows.Sort((a, b) => a.waveIndex.CompareTo(b.waveIndex));

            var waves = new WaveSpec[rows.Count];
            for (int w = 0; w < rows.Count; w++)
            {
                StageWaveTable.WaveRow row = rows[w];
                waves[w] = new WaveSpec(row.normalCount, row.normalHpMultiplier, row.normalDamageMultiplier, row.eliteCount, row.eliteHpMultiplier, row.eliteDamageMultiplier);
            }

            result[s] = waves;
        }

        return result;
    }

    // Amount of (element-less, same as field drops - see stage_system_design_v1.html §1 "다음
    // 단계로 미룸") mana stone deposited on a clean clear, scaled with stage number.
    private static readonly int[] StageManaReward = { 6, 10, 16 };

    private static readonly Color EliteTint = new Color(0.55f, 0.18f, 0.16f);

    private readonly List<Monster> activeMonsters = new List<Monster>();

    public IReadOnlyList<Monster> ActiveMonsters => activeMonsters;
    public bool IsEncounterActive { get; private set; }
    public int ActiveStageNumber { get; private set; }
    public int ActiveWaveNumber { get; private set; } // 1-based, for StageEncounterUI
    public int TotalWavesForStage(int stageNumber) => StageWaves[stageNumber - 1].Length;

    // bool = whether the encounter ended on a clean clear (false = death/retreat, no reward) -
    // StageSceneController listens for this to know when to send the player back to the field.
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

    private Vector3 spawnCenter;

    private void Awake()
    {
        PlayerHealth.OnDeath += HandlePlayerDeath;
    }

    private void OnDestroy()
    {
        PlayerHealth.OnDeath -= HandlePlayerDeath;
    }

    public bool CanBegin(int stageNumber)
    {
        return !IsEncounterActive && StageBank.IsUnlocked(stageNumber) && stageNumber >= 1 && stageNumber <= StageBank.StageCount;
    }

    // waveSpawnPoint is the lane's NE end (StageSceneController computes it) - every wave spawns
    // there and its monsters immediately advance toward the player (SW), rather than scattering
    // around a fixed point the way FieldMonsterSpawner's permanent pool does.
    public void BeginEncounter(int stageNumber, Vector3 waveSpawnPoint)
    {
        if (!CanBegin(stageNumber))
        {
            return;
        }

        IsEncounterActive = true;
        ActiveStageNumber = stageNumber;
        ActiveWaveNumber = 0;
        spawnCenter = waveSpawnPoint;

        SpawnNextWave();
    }

    // Wired to a RETREAT button (StageSceneController) - leaves with no reward, same as dying.
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
                return; // wave still in progress
            }
        }

        // Every monster in the current wave is down.
        WaveSpec[] waves = StageWaves[ActiveStageNumber - 1];
        if (ActiveWaveNumber >= waves.Length)
        {
            CompleteEncounter();
        }
        else
        {
            SpawnNextWave();
        }
    }

    private void SpawnNextWave()
    {
        foreach (Monster leftover in activeMonsters)
        {
            MonsterPool.Despawn(leftover);
        }

        activeMonsters.Clear();

        WaveSpec wave = StageWaves[ActiveStageNumber - 1][ActiveWaveNumber];
        ActiveWaveNumber++;

        var placed = new List<Vector3>();
        for (int i = 0; i < wave.NormalCount; i++)
        {
            SpawnOne(placed, wave.NormalHpMult, wave.NormalDmgMult, isElite: false);
        }

        for (int i = 0; i < wave.EliteCount; i++)
        {
            SpawnOne(placed, wave.EliteHpMult, wave.EliteDmgMult, isElite: true);
        }
    }

    private void SpawnOne(List<Vector3> placed, float hpMult, float dmgMult, bool isElite)
    {
        Vector3 position = FindSpawnPosition(placed);
        placed.Add(position);

        // Elites are always Tanker (bigger/tougher reads naturally as "the strong one" - see
        // TankerMonster.RoleScale); normal spawns pick from whatever roles this stage has
        // unlocked (monster_variety_design_v1.html §4).
        MonsterRole role = isElite ? MonsterRole.Tanker : ChooseNormalRole(ActiveStageNumber);
        Monster monster = MonsterPool.Spawn(role, position, transform);
        monster.SetStrength(hpMult, dmgMult);
        monster.SetAlwaysAggro(true);
        if (isElite)
        {
            monster.SetTint(EliteTint);
        }

        activeMonsters.Add(monster);
    }

    // Stage 1 stays Melee-only (a fresh player's first real fight, kept simple and safe); Stage 2
    // mixes in Ranged; Stage 3+ mixes in Magic too. See monster_variety_design_v1.html §4.
    private static readonly MonsterRole[] Stage1Roles = { MonsterRole.Melee };
    private static readonly MonsterRole[] Stage2Roles = { MonsterRole.Melee, MonsterRole.Melee, MonsterRole.Ranged };
    private static readonly MonsterRole[] Stage3PlusRoles = { MonsterRole.Melee, MonsterRole.Melee, MonsterRole.Ranged, MonsterRole.Magic };

    private static MonsterRole ChooseNormalRole(int stageNumber)
    {
        MonsterRole[] pool;
        switch (stageNumber)
        {
            case 1:
                pool = Stage1Roles;
                break;
            case 2:
                pool = Stage2Roles;
                break;
            default:
                pool = Stage3PlusRoles;
                break;
        }

        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    private Vector3 FindSpawnPosition(List<Vector3> placed)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * SpawnSpread;
            Vector3 candidate = spawnCenter + new Vector3(offset.x, 0f, offset.y);

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

        Vector2 fallback = UnityEngine.Random.insideUnitCircle * SpawnSpread;
        return spawnCenter + new Vector3(fallback.x, 0f, fallback.y);
    }

    private void CompleteEncounter()
    {
        int stageNumber = ActiveStageNumber;
        // The dungeon unlocks after Stage 1 alone (DungeonBank.IsUnlocked), not all 3 - this is
        // specifically the clear that flips HighestStageCleared 0 -> 1, i.e. the one that actually
        // opens it (사용자 요청 2026-08-20).
        bool justUnlockedDungeon = StageBank.HighestStageCleared == 0 && stageNumber == 1;

        StageBank.MarkCleared(stageNumber);
        ManaBank.DepositGathered(StageManaReward[stageNumber - 1]);
        EndEncounter(cleared: true);

        // A closure moment for opening the dungeon, instead of the run just quietly ending the
        // same as any other stage clear - "컨텐츠와 시퀀스" completeness (사용자 피드백).
        if (justUnlockedDungeon)
        {
            ToastUI.Instance.Show("Stage 1 Cleared! The dungeon has opened.", 4f);
        }
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
        foreach (Monster leftover in activeMonsters)
        {
            MonsterPool.Despawn(leftover);
        }

        activeMonsters.Clear();
        IsEncounterActive = false;
        ActiveWaveNumber = 0;

        OnEncounterEnded?.Invoke(cleared);
    }
}
