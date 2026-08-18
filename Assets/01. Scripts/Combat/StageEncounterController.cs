using System.Collections.Generic;
using UnityEngine;

// Runs one stage's wave-clear fight (stage_system_design_v1.html §3/§4) - spawns a burst of
// Monster instances near the gate, waits for the current wave to be fully cleared before spawning
// the next, and rewards + unlocks on a clean finish. Deliberately kept separate from
// FieldMonsterSpawner's always-on respawning pool (different lifecycle: this one starts, runs a
// fixed number of waves, and ends - it isn't a permanent hunting ground), but exposes its own
// monster list the same way so PlayerCombat can target both.
public class StageEncounterController : MonoBehaviour
{
    private const float SpawnRadius = 3f;
    private const float MinSpacing = 1.6f;
    private const int MaxPlacementAttempts = 20;

    // Cancels the encounter (monsters cleared, no reward, stage stays locked) if the player
    // wanders this far from the gate mid-fight - keeps the fight from trailing off into the
    // regular field, and gives an escape hatch if a wave is too much to handle.
    private const float LeashRadius = 14f;

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

    private static readonly Color EliteTint = new Color(0.55f, 0.18f, 0.16f);

    private readonly List<Monster> activeMonsters = new List<Monster>();

    public IReadOnlyList<Monster> ActiveMonsters => activeMonsters;
    public bool IsEncounterActive { get; private set; }
    public int ActiveStageNumber { get; private set; }
    public int ActiveWaveNumber { get; private set; } // 1-based, for StageEncounterUI
    public int TotalWavesForStage(int stageNumber) => StageWaves[stageNumber - 1].Length;

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

    public void BeginEncounter(int stageNumber, Vector3 gatePosition)
    {
        if (!CanBegin(stageNumber))
        {
            return;
        }

        IsEncounterActive = true;
        ActiveStageNumber = stageNumber;
        ActiveWaveNumber = 0;
        spawnCenter = gatePosition;

        SpawnNextWave();
    }

    private void Update()
    {
        if (!IsEncounterActive)
        {
            return;
        }

        if (PlayerMotor.Instance != null)
        {
            float sqrDist = (PlayerMotor.Instance.transform.position - spawnCenter).sqrMagnitude;
            if (sqrDist > LeashRadius * LeashRadius)
            {
                CancelEncounter();
                return;
            }
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
            if (leftover != null)
            {
                Destroy(leftover.gameObject);
            }
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

        Monster monster = Monster.Spawn(position, transform);
        monster.SetStrength(hpMult, dmgMult);
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
            Vector2 offset = Random.insideUnitCircle * SpawnRadius;
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

        Vector2 fallback = Random.insideUnitCircle * SpawnRadius;
        return spawnCenter + new Vector3(fallback.x, 0f, fallback.y);
    }

    private void CompleteEncounter()
    {
        int stageNumber = ActiveStageNumber;
        StageBank.MarkCleared(stageNumber);
        ManaBank.DepositGathered(StageManaReward[stageNumber - 1]);
        EndEncounter();
    }

    private void HandlePlayerDeath()
    {
        if (IsEncounterActive)
        {
            CancelEncounter();
        }
    }

    private void CancelEncounter()
    {
        EndEncounter();
    }

    private void EndEncounter()
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
        ActiveWaveNumber = 0;
    }
}
