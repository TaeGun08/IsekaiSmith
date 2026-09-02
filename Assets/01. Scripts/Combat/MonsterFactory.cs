using UnityEngine;

// Builds the shared "tinted Sphere primitive" body every monster role uses (same low-fidelity
// vector convention as Player/Customer/resource nodes) and attaches the role-specific component -
// the one place that knows Monster is spawned this way, so FieldMonsterSpawner/
// StageEncounterController/DungeonEncounterController don't each duplicate it.
// See monster_variety_design_v1.html §1.
public static class MonsterFactory
{
    public static Monster Spawn(MonsterRole role, Vector3 groundPosition, Transform parent = null)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = role + "Monster";
        body.transform.SetParent(parent, false);
        body.transform.position = groundPosition + Vector3.up * 0.5f;
        Object.Destroy(body.GetComponent<Collider>()); // visual only - AI uses plain distance checks

        Monster monster;
        switch (role)
        {
            case MonsterRole.Ranged:
                monster = body.AddComponent<RangedMonster>();
                break;
            case MonsterRole.Magic:
                monster = body.AddComponent<MagicMonster>();
                break;
            case MonsterRole.Tanker:
                monster = body.AddComponent<TankerMonster>();
                break;
            case MonsterRole.Support:
                monster = body.AddComponent<SupportMonster>();
                break;
            case MonsterRole.Melee:
            default:
                monster = body.AddComponent<MeleeMonster>();
                break;
        }

        return monster;
    }
}
