using UnityEngine;

// Lazily-built, reusable "prefab-like" template for carried items that don't have a real
// authored prefab asset - GameObjectPool.Spawn() only ever needs a GameObject reference to
// Instantiate from (see GameObjectPool.cs), it doesn't require an actual .prefab asset. Matches
// this project's runtime-generated-asset convention (UIShapes does the same for 2D sprites).
public static class CarryItemTemplates
{
    private static GameObject manaStoneChip;
    private static GameObject quickCraftWeaponProp;

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

    // Composite blade+hilt standing in for "a weapon fresh off QUICK CRAFT" (customer_order_
    // design_v7.html §2/§4) - carried on CarryLayer.Weapon from smithy to sales counter. A single
    // thin sliver read as a barely-visible smear at carry-stack scale (사용자 요청 2026-08-21:
    // "완성된 검이 시각적으로 잘보이도록") - a bright silver blade plus a short dark hilt gives it
    // an actual sword silhouette instead. No per-grade variants - grade is resolved when the
    // counter deposit happens (ForgeUpgrade.CurrentTier at that moment), same "resolve grade at
    // deposit, not at carry" convention OreBank.DepositMined already uses, so the prop itself
    // carries no data. Root pivot sits between the two pieces (hilt end slightly behind) so
    // CarryStack's Weapon-layer rotation (see LocalSlotRotation) reads as "held/carried by the
    // grip" rather than pivoting around the blade's middle.
    public static GameObject QuickCraftWeaponProp
    {
        get
        {
            if (quickCraftWeaponProp == null)
            {
                quickCraftWeaponProp = new GameObject("QuickCraftWeaponPropTemplate");
                quickCraftWeaponProp.transform.position = new Vector3(0f, -500f, 0f);

                GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = "Blade";
                blade.transform.SetParent(quickCraftWeaponProp.transform, false);
                blade.transform.localPosition = new Vector3(0f, 0f, 0.28f);
                blade.transform.localScale = new Vector3(0.1f, 0.03f, 0.56f);
                blade.GetComponent<Renderer>().material.color = new Color(0.85f, 0.88f, 0.92f);
                Object.Destroy(blade.GetComponent<Collider>());

                GameObject hilt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hilt.name = "Hilt";
                hilt.transform.SetParent(quickCraftWeaponProp.transform, false);
                hilt.transform.localPosition = new Vector3(0f, 0f, -0.09f);
                hilt.transform.localScale = new Vector3(0.14f, 0.06f, 0.18f);
                hilt.GetComponent<Renderer>().material.color = new Color(0.36f, 0.23f, 0.13f);
                Object.Destroy(hilt.GetComponent<Collider>());

                Object.DontDestroyOnLoad(quickCraftWeaponProp);
                quickCraftWeaponProp.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return quickCraftWeaponProp;
        }
    }
}
