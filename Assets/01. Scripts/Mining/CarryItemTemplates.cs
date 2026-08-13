using UnityEngine;

// Lazily-built, reusable "prefab-like" template for carried items that don't have a real
// authored prefab asset - GameObjectPool.Spawn() only ever needs a GameObject reference to
// Instantiate from (see GameObjectPool.cs), it doesn't require an actual .prefab asset. Matches
// this project's runtime-generated-asset convention (UIShapes does the same for 2D sprites).
public static class CarryItemTemplates
{
    private static GameObject manaStoneChip;

    // Small violet/purple shard - visually distinct from wood (brown, stacked behind the anchor)
    // and ore (grey, stacked on top). "매우 낮은 품질" per user request - just the flat resource
    // count for now, no grade/element tiers yet (those arrive with the Stage-tier mana system,
    // see combat_design_v1.html §1 "다음 단계로 미룸").
    public static GameObject ManaStoneChip
    {
        get
        {
            if (manaStoneChip == null)
            {
                manaStoneChip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                manaStoneChip.name = "ManaStoneChipTemplate";
                // Parked far below the map instead of SetActive(false) - Unity's Instantiate()
                // clones the exact active-state of its source, so a disabled template would spawn
                // invisible *first-use* clones (GameObjectPool only re-activates on the reuse/
                // pooled path, not on a fresh Instantiate - see GameObjectPool.Spawn). The 3-arg
                // Instantiate overload always repositions the clone explicitly, so where the
                // template itself sits doesn't matter as long as it stays out of view.
                manaStoneChip.transform.position = new Vector3(0f, -500f, 0f);
                manaStoneChip.transform.localScale = Vector3.one * 0.22f;
                manaStoneChip.GetComponent<Renderer>().material.color = new Color(0.55f, 0.4f, 0.75f);
                Object.Destroy(manaStoneChip.GetComponent<Collider>());
                Object.DontDestroyOnLoad(manaStoneChip);
                manaStoneChip.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return manaStoneChip;
        }
    }
}
