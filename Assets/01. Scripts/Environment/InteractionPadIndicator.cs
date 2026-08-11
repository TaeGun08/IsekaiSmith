using UnityEngine;
using UnityEngine.UI;

// Ground "stand this close to interact" mark - reacts to the player entering its own radius
// (color shifts to a warm accent + widens slightly) rather than sitting there as a flat static
// primitive. Built as a World Space Canvas + Image laid flat on the ground (same runtime-UI
// convention as Customer's speech bubble / CustomerVisualManager's rush label) using a
// procedurally dashed ring sprite (UIShapes.Ring) - no new texture asset or shader needed.
// See interaction_range_indicator_design.html.
[RequireComponent(typeof(Image))]
public class InteractionPadIndicator : MonoBehaviour
{
    private static readonly Color IdleColor = new Color(0.15f, 0.14f, 0.13f, 0.8f);
    private static readonly Color ActiveColor = new Color(0.98f, 0.82f, 0.35f, 0.95f);
    private const float ColorLerpSpeed = 8f;
    private const float ScaleLerpSpeed = 8f;
    private const float ActiveScaleMultiplier = 1.12f;

    private float radius;
    private Image image;
    private Vector3 baseLocalScale;
    private float currentScaleFactor = 1f;

    // Attaches a full pad (Canvas + Image + this behavior) as a child of parent, sized to exactly
    // match radius - called once from each interactable's own Awake() with its own radius field
    // (CraftingStation.interactRadius, StorageDepot.depositRadius, OrderQueueManager.interactRadius),
    // so the mark can never visually drift out of sync with the actual gameplay check.
    public static void Attach(Transform parent, float radius)
    {
        var padGO = new GameObject("InteractionPad", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(InteractionPadIndicator));
        padGO.transform.SetParent(parent, false);
        padGO.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        // Lay the UI plane flat on the ground facing up, instead of standing upright facing the
        // camera like a normal world-space canvas.
        padGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Vector3 baseScale = Vector3.one * 0.01f;
        padGO.transform.localScale = baseScale;

        padGO.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var rect = padGO.GetComponent<RectTransform>();
        float diameterPixels = radius * 2f * 100f; // matches the 0.01 world scale set above
        rect.sizeDelta = new Vector2(diameterPixels, diameterPixels);

        var image = padGO.GetComponent<Image>();
        image.sprite = UIShapes.Ring();
        image.color = IdleColor;
        image.raycastTarget = false; // decorative - must not block movement/UI clicks

        var indicator = padGO.GetComponent<InteractionPadIndicator>();
        indicator.radius = radius;
        indicator.image = image;
        indicator.baseLocalScale = baseScale;
    }

    private void Update()
    {
        if (PlayerMotor.Instance == null || transform.parent == null)
        {
            return;
        }

        float sqrDist = (PlayerMotor.Instance.transform.position - transform.parent.position).sqrMagnitude;
        bool active = sqrDist <= radius * radius;

        image.color = Color.Lerp(image.color, active ? ActiveColor : IdleColor, Time.deltaTime * ColorLerpSpeed);

        float targetScaleFactor = active ? ActiveScaleMultiplier : 1f;
        currentScaleFactor = Mathf.Lerp(currentScaleFactor, targetScaleFactor, Time.deltaTime * ScaleLerpSpeed);
        transform.localScale = baseLocalScale * currentScaleFactor;
    }
}
