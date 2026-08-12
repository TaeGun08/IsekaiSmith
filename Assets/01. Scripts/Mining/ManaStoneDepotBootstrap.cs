using UnityEngine;

// Self-bootstrapping (GuidedTutorial-style) creator for a third storage crate that accepts
// CarryLayer.ManaStone. StorageCrate1(wood)/StorageCrate2(ore) are existing scene objects, but a
// mana crate doesn't exist yet - built here in code rather than asking for new scene/prefab
// placement (only *decorative* placement is off-limits per [[feedback_no_map_design_autonomy]];
// a functional deposit point is exactly the kind of thing that stays in-scope).
//
// Position is derived, not guessed: it continues the exact line between the two existing crates
// using their own real spacing, so it reads as "the next crate in the row" instead of a blindly
// placed box. See combat_design_v1.html follow-up notes.
public class ManaStoneDepotBootstrap : MonoBehaviour
{
    private static ManaStoneDepotBootstrap instance;

    public static ManaStoneDepotBootstrap Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("ManaStoneDepotBootstrap");
                instance = go.AddComponent<ManaStoneDepotBootstrap>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private bool built;

    // Called once from ResourceHUD.Start() alongside GuidedTutorial/FieldMonsterSpawner - safe to
    // call again if the scene wasn't ready yet (e.g. a future scene reload before the crates exist).
    public void Bootstrap()
    {
        if (built)
        {
            return;
        }

        GameObject woodCrateGO = GameObject.Find("StorageCrate1");
        GameObject oreCrateGO = GameObject.Find("StorageCrate2");

        if (woodCrateGO == null || oreCrateGO == null)
        {
            return;
        }

        built = true;

        Vector3 a = woodCrateGO.transform.position;
        Vector3 b = oreCrateGO.transform.position;
        Vector3 direction = (b - a).normalized;
        float spacing = Vector3.Distance(a, b);
        Vector3 position = b + direction * spacing;

        var crateGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crateGO.name = "StorageCrateMana";
        crateGO.transform.position = position;
        crateGO.transform.rotation = oreCrateGO.transform.rotation;
        crateGO.transform.localScale = oreCrateGO.transform.localScale;
        crateGO.GetComponent<Renderer>().material.color = new Color(0.42f, 0.3f, 0.55f);

        StorageDepot depot = crateGO.AddComponent<StorageDepot>();
        depot.SetAcceptedLayer(CarryLayer.ManaStone);
    }
}
