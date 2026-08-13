using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Placeholder customer visual - a single tinted Capsule (same primitive Player itself uses, see
// Player.prefab's "Model" child) with a world-space speech bubble above its head. Built entirely
// at runtime like the game's other self-built UI, so no prefab/scene wiring is needed. Spawned,
// positioned, and despawned by CustomerVisualManager - this class only knows how to walk to a
// target and show/hide its own order display; it has no idea what a "slot" or "order queue" is.
// See customer_order_design_v4.html §2/§3.
public class Customer : MonoBehaviour
{
    private const float WalkSpeed = 2.4f;
    private const float ArriveThreshold = 0.08f;

    private Transform bubbleRoot;
    private TMP_Text gradeLabel;
    private Image patienceFillImage;
    private Button bubbleButton;

    private readonly Color patienceGood = new Color(0.35f, 0.62f, 0.32f);
    private readonly Color patienceBad = new Color(0.82f, 0.28f, 0.1f);
    private const float PatienceBarWidth = 150f;

    private Vector3 walkTarget;
    private bool despawnOnArrive;

    public static Customer Spawn(Vector3 groundPosition, Color tint, UnityEngine.Events.UnityAction onTap, Transform parent = null)
    {
        var root = new GameObject("Customer");
        root.transform.SetParent(parent, false);
        root.transform.position = groundPosition;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Object.Destroy(body.GetComponent<Collider>()); // visual only - no physics, no tap raycast needed here
        var renderer = body.GetComponent<MeshRenderer>();
        renderer.material.color = tint;

        var customer = root.AddComponent<Customer>();
        customer.BuildBubble(root.transform, onTap);
        customer.walkTarget = groundPosition;
        return customer;
    }

    private void BuildBubble(Transform parent, UnityEngine.Events.UnityAction onTap)
    {
        var canvasGO = new GameObject("SpeechBubble", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(parent, false);
        canvasGO.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        canvasGO.transform.localScale = Vector3.one * 0.01f;
        bubbleRoot = canvasGO.transform;

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(220f, 110f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGO.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(0.97f, 0.93f, 0.85f, 0.98f);
        // Not the tap target itself (see TapZone below) - visual only, must not steal raycasts.
        bgImage.raycastTarget = false;

        // Queue slots stand only 1.4m apart (QueuePoint0/1/2 in the scene), but this bubble is a
        // full 2.2m wide (canvasRect 220 * 0.01 world scale) - if the whole background were the
        // tap target (as it used to be), tapping near the boundary between two customers could
        // hit the wrong one (user report: "옆에 버튼이 클릭될 때가 있다"). Instead, only a smaller
        // centered zone (1.0m world) is tappable, leaving 0.2m of dead space on each side within
        // the 1.4m slot spacing - the visual bubble stays full-size and readable, only the hit-box
        // shrinks.
        var tapZone = new GameObject("TapZone", typeof(RectTransform), typeof(Image), typeof(Button));
        tapZone.transform.SetParent(canvasGO.transform, false);
        var tapZoneRect = tapZone.GetComponent<RectTransform>();
        tapZoneRect.anchorMin = new Vector2(0.5f, 0.5f);
        tapZoneRect.anchorMax = new Vector2(0.5f, 0.5f);
        tapZoneRect.pivot = new Vector2(0.5f, 0.5f);
        tapZoneRect.anchoredPosition = Vector2.zero;
        tapZoneRect.sizeDelta = new Vector2(100f, 100f);
        tapZone.GetComponent<Image>().color = Color.clear;
        bubbleButton = tapZone.GetComponent<Button>();
        bubbleButton.onClick.AddListener(onTap);

        gradeLabel = MakeText(bg.transform, "Grade", 34, new Vector2(0f, -16f), new Vector2(200f, 44f));
        gradeLabel.color = new Color(0.16f, 0.13f, 0.1f);
        gradeLabel.fontStyle = FontStyles.Bold;

        var patienceBg = new GameObject("PatienceBg", typeof(RectTransform), typeof(Image));
        patienceBg.transform.SetParent(bg.transform, false);
        var patienceBgRect = patienceBg.GetComponent<RectTransform>();
        patienceBgRect.anchorMin = new Vector2(0.5f, 0f);
        patienceBgRect.anchorMax = new Vector2(0.5f, 0f);
        patienceBgRect.pivot = new Vector2(0.5f, 0f);
        patienceBgRect.anchoredPosition = new Vector2(0f, 16f);
        patienceBgRect.sizeDelta = new Vector2(PatienceBarWidth, 16f);
        patienceBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

        var fillGO = new GameObject("PatienceFill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(patienceBg.transform, false);
        var fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(PatienceBarWidth, 0f);
        patienceFillImage = fillGO.GetComponent<Image>();
        patienceFillImage.color = patienceGood;
    }

    private TMP_Text MakeText(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    // Called by CustomerVisualManager every frame the order is still active - grade rarely
    // changes (never, in practice - one order per customer) but patience does.
    public void SetOrder(CraftGrade minGrade, float patience01)
    {
        gradeLabel.text = CraftGradeUtility.DisplayName(minGrade) + "+";
        patienceFillImage.rectTransform.sizeDelta = new Vector2(PatienceBarWidth * patience01, 0f);
        patienceFillImage.color = Color.Lerp(patienceBad, patienceGood, patience01);
    }

    public void WalkTo(Vector3 groundPosition)
    {
        walkTarget = groundPosition;
    }

    // Walks to groundPosition and self-destroys on arrival - used for both "served" and "gave up
    // waiting" exits, CustomerVisualManager doesn't need to distinguish the two for despawning.
    public void ExitAndDespawn(Vector3 groundPosition)
    {
        walkTarget = groundPosition;
        despawnOnArrive = true;
        bubbleRoot.gameObject.SetActive(false);
    }

    private void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, walkTarget, WalkSpeed * Time.deltaTime);

        if (despawnOnArrive && (transform.position - walkTarget).sqrMagnitude <= ArriveThreshold * ArriveThreshold)
        {
            Destroy(gameObject);
        }
    }

    private void LateUpdate()
    {
        // Billboard the bubble only - the body never rotates, so this is the bubble's full world
        // rotation, not an addition to a parent rotation.
        if (bubbleRoot != null && Camera.main != null)
        {
            bubbleRoot.rotation = Camera.main.transform.rotation;
        }
    }
}
