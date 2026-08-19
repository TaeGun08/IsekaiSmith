using System;
using System.Collections.Generic;
using UnityEngine;

// Data-sheet for dungeon floors (user request: "데이터 시트를 이용해서 미리 던전 정보(몬스터
// 종류, 몬스터 수, 보스 등) 불러와서 사용하는 식") - each row is one floor's mob pack + boss
// stats, editable in the Inspector as a list instead of a hardcoded C# array, so adding more
// floors ("던전을 최대한 많이 만들어도 좋아") doesn't require touching code. Loaded at runtime via
// Resources.Load (Assets/05. Data/Resources/DungeonFloorTable.asset) since
// DungeonEncounterController is a code-only singleton with nothing in a scene to drag a reference
// onto. See dungeon_design_v1.html §2.
[CreateAssetMenu(fileName = "DungeonFloorTable", menuName = "IsekaiSmith/Dungeon Floor Table")]
public class DungeonFloorTable : ScriptableObject
{
    [Serializable]
    public struct FloorRow
    {
        public int mobCount;
        public float mobHpMultiplier;
        public float mobDamageMultiplier;
        public float bossHpMultiplier;
        public float bossDamageMultiplier;
    }

    public List<FloorRow> floors = new List<FloorRow>();
}
