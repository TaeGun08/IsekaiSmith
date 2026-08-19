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

    // stage_system_design_v1.html §3's difficulty table, indexed [stageNumber - 1][waveIndex].
    private static readonly WaveSpec[][] StageWaves =
    {
        new[] { new WaveSpec(3, 1.3f, 1.2f), new WaveSpec(2, 1.3f, 1.2f, 1, 2.2f, 1.6f) },
        new[] { new WaveSpec(4, 1.8f, 1.5f), new WaveSpec(3, 1.8f, 1.5f, 1, 3f, 2f) },
        new[] { new WaveSpec(5, 2.4f, 1.8f), new WaveSpec(3, 2.4f, 1.8f, 1, 4f, 2.5f) },
    };

    // Amount of (element-less, same as field drops - see stage_system_design_v1.html §1 "다음
    // 단계로 미룸") mana stone deposited on a clean clear, scaled with stage number.
    private static readonly int[] StageManaReward = { 6, 10, 16 };

    // dungeon_design_v1.html §2 - three basement floors (사용자 요청: "던전도 스테이지처럼
    // 점점 지하 1층 2층 3층 이런식으로 클리어하면 무조건 다음층으로 넘어가도록"), each harder
    // than any single stage's own waves, floor 3 standing in for the dungeon boss (no dedicated
    // boss system - same trim StageWaves' elites already use). Clearing a floor auto-advances to
    // the next one (Update() below) - there's no exit-and-re-enter between floors.
    private static readonly WaveSpec[] DungeonFloors =
    {
        new WaveSpec(5, 2.8f, 2f),
        new WaveSpec(5, 3.6f, 2.6f, 1, 6f, 3.2f),
        new WaveSpec(4, 4.5f, 3.2f, 1, 9f, 4.5f),
    };

    private static readonly Color EliteTint = new Color(0.55f, 0.18f, 0.16f);

    private readonly List<Monster> activeMonsters = new List<Monster>();

    public IReadOnlyList<Monster> ActiveMonsters => activeMonsters;
    public bool IsEncounterActive { get; private set; }
    public bool IsDungeonEncounter { get; private set; }
    public int ActiveStageNumber { get; private set; }
    public int ActiveWaveNumber { get; private set; } // 1-based, for StageEncounterUI
    public int TotalWavesForStage(int stageNumber) => StageWaves[stageNumber - 1].Length;
    public int TotalDungeonFloors => DungeonFloors.Length;

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

    public bool CanBeginDungeon => !IsEncounterActive && DungeonBank.IsUnlocked;

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
        IsDungeonEncounter = false;
        ActiveStageNumber = stageNumber;
        ActiveWaveNumber = 0;
        spawnCenter = waveSpawnPoint;

        SpawnNextWave();
    }

    // Same lifecycle as BeginEncounter, dungeon_design_v1.html's wave table instead of a stage's.
    public void BeginDungeonEncounter(Vector3 waveSpawnPoint)
    {
        if (!CanBeginDungeon)
        {
            return;
        }

        IsEncounterActive = true;
        IsDungeonEncounter = true;
        ActiveStageNumber = 0;
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
        WaveSpec[] waves = IsDungeonEncounter ? DungeonFloors : StageWaves[ActiveStageNumber - 1];
        if (ActiveWaveNumber >= waves.Length)
        {
            if (IsDungeonEncounter)
            {
                CompleteDungeon();
            }
            else
            {
                CompleteEncounter();
            }
        }
        else
        {
            // Unconditional auto-advance to the next floor, no exit/re-enter (user request: "클리어
            // 하면 무조건 다음층으로 넘어가도록") - only the dungeon calls out floor numbers, a
            // stage's own waves stay a quieter internal detail.
            if (IsDungeonEncounter)
            {
                ToastUI.Instance.Show("Floor " + ActiveWaveNumber + " Cleared! Descending to Floor " + (ActiveWaveNumber + 1) + "...", 2.5f);
            }

            SpawnNextWave();
        }
    }

    private void SpawnNextWave()
    {
        foreach (Monster leftover in activeMonsters)
        {
            if (leftover != null)
            {
                Destroy(leftover.gameObject);
            }
        }

        activeMonsters.Clear();

        WaveSpec[] waves = IsDungeonEncounter ? DungeonFloors : StageWaves[ActiveStageNumber - 1];
        WaveSpec wave = waves[ActiveWaveNumber];
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

        Monster monster = Monster.Spawn(position, transform);
        monster.SetStrength(hpMult, dmgMult);
        monster.SetAlwaysAggro(true);
        if (isElite)
        {
            monster.SetTint(EliteTint);
        }

        activeMonsters.Add(monster);
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
        bool wasLastStage = stageNumber == StageBank.StageCount && !StageBank.AllStagesCleared;

        StageBank.MarkCleared(stageNumber);
        ManaBank.DepositGathered(StageManaReward[stageNumber - 1]);
        EndEncounter(cleared: true);

        // A closure moment for finishing all of them, instead of the run just quietly ending the
        // same as any other stage clear - "컨텐츠와 시퀀스" completeness (사용자 피드백).
        if (wasLastStage)
        {
            ToastUI.Instance.Show("All Stages Cleared! The dungeon has opened.", 4f);
        }
    }

    // First-clear-only, same as a stage (user correction: "던전은 최초클리어만 가능하고...
    // 업그레이드가 주 목적이야") - no gold/mana payout, the quarry-ceiling upgrade
    // (OreBank.DungeonCeiling) IS the reward. DungeonBank.IsUnlocked goes false the instant
    // MarkClearedOnce runs, so this can never fire a second time for the same save.
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
        foreach (Monster leftover in activeMonsters)
        {
            if (leftover != null)
            {
                Destroy(leftover.gameObject);
            }
        }

        activeMonsters.Clear();
        IsEncounterActive = false;
        IsDungeonEncounter = false;
        ActiveWaveNumber = 0;

        OnEncounterEnded?.Invoke(cleared);
    }
}
