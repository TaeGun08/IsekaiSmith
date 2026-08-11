using UnityEngine;

// Shared placeholder ground mark for "stand this close to interact" - a flat dark disc sized to
// exactly match the caller's own interaction radius, so it can never visually drift out of sync
// with the actual gameplay check. Referenced from the reference image the user dropped in
// Assets/03. Art/Sprites (green dashed rings under workstation NPCs in a mobile tycoon game) -
// this project's version is a flat solid primitive instead, matching the low-fidelity vector
// style already used for CounterVisual/ApproachPath (CustomerVisualManager) rather than adding a
// new sprite/shader dependency. See interaction_range_indicator_design.html.
public static class InteractionPadVisual
{
    private static readonly Color PadColor = new Color(0.13f, 0.12f, 0.11f, 1f);

    // Called from each interactable's own Awake() with its own radius field (CraftingStation.
    // interactRadius, StorageDepot.depositRadius, OrderQueueManager.interactRadius) - deliberately
    // not a MonoBehaviour of its own so there's no separate radius to keep in sync by hand.
    public static void Build(Transform parent, float radius)
    {
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "InteractionPad";
        pad.transform.SetParent(parent, false);
        Object.Destroy(pad.GetComponent<Collider>()); // visual only - must not block movement/raycasts

        // Unity's primitive Cylinder is radius 0.5 / height 2 at scale 1 - convert the requested
        // world radius into an x/z scale, and flatten height into a thin ground mat.
        float diameterScale = radius / 0.5f;
        pad.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        pad.transform.localScale = new Vector3(diameterScale, 0.01f, diameterScale);
        pad.GetComponent<MeshRenderer>().material.color = PadColor;
    }
}
