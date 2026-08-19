using System;
using System.Collections.Generic;
using UnityEngine;

// Data-sheet counterpart to DungeonFloorTable, for stage waves (user request: "스테이지도
// 마찬가지로" - same data-sheet approach). Rows are grouped by stageNumber so one flat list can
// hold every stage's waves; StageEncounterController filters/orders by stageNumber then
// waveIndex at load time. Loaded at runtime via Resources.Load
// (Assets/05. Data/Resources/StageWaveTable.asset). See stage_system_design_v1.html §3.
[CreateAssetMenu(fileName = "StageWaveTable", menuName = "IsekaiSmith/Stage Wave Table")]
public class StageWaveTable : ScriptableObject
{
    [Serializable]
    public struct WaveRow
    {
        public int stageNumber; // 1-based
        public int waveIndex; // 0-based, order within the stage
        public int normalCount;
        public float normalHpMultiplier;
        public float normalDamageMultiplier;
        public int eliteCount;
        public float eliteHpMultiplier;
        public float eliteDamageMultiplier;
    }

    public List<WaveRow> waves = new List<WaveRow>();
}
