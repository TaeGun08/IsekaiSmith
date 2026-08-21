using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Placeholder customer visual - a single tinted Capsule (same primitive Player itself uses, see
// Player.prefab's "Model" child) with a world-space speech bubble above its head. Built entirely
// at runtime like the game's other self-built UI, so no prefab/scene wiring is needed. Spawned,
// positioned, and despawned by CustomerVisualManager - this class only knows how to walk to a
// target and show/hide its own order display; it has no idea what a "slot" or "order queue" is.
// See customer_order_design_v7.html §3 - only the front-of-line customer is ever interactable
// (SetInteractable), everyone behind them just stands and shows their own requested count.
// The bubble itself stays hidden (SetBubbleVisible) until the customer has actually walked up to
// and arrived at that front slot (HasArrived) - customer_order_design_v7 always meant only the
// person currently being served shows a demand, but the bubble used to be visible the instant a
// customer spawned, even while still walking in from the back of the line (사용자 요청
// 2026-08-21: "카운터에 도착해서 물건을 받으려고 하기전까진 요구치가 뜨면 안돼").
public class Customer : MonoBehaviour
{
    private const float WalkSpeed = 2.4f;
    private const float ArriveThreshold = 0.08f;

    private Transform bubbleRoot;
    private TMP_Text gradeLabel;
    private Image progressFillImage;
    private Image bubbleBackgroundImage;
    private Button bubbleButton;

    private readonly Color progressFillColor = new Color(0.35f, 0.62f, 0.32f);
    private readonly Color activeBubbleAlpha = new Color(1f, 1f, 1f, 0.98f);
    private readonly Color inactiveBubbleAlpha = new Color(1f, 1f, 1f, 0.55f);
    private const float ProgressBarWidth = 150f;

    private Vector3 walkTarget;
    private bool despawnOnArrive;

    // True once this customer has actually walked up to its current walkTarget, not merely been
    // assigned one - WalkTo just retargets, it doesn't teleport, so a customer freshly bumped up
    // to the front of the line (queue[0]) still has to close the remaining distance before this
    // flips back to true.
    public bool HasArrived { get; private set; }

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
        bubbleBackgroundImage = bg.GetComponent<Image>();
        bubbleBackgroundImage.color = new Color(0.97f, 0.93f, 0.85f, 0.98f);
        // Not the tap target itself (see TapZone below) - visual only, must not steal raycasts.
        bubbleBackgroundImage.raycastTarget = false;

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

        // Hidden until SetBubbleVisible(true) - see the class-header note on why a freshly spawned
        // customer shouldn't show their demand while still walking in from the back of the line.
        canvasGO.SetActive(false);

        gradeLabel = MakeText(bg.transform, "Grade", 34, new Vector2(0f, -16f), new Vector2(200f, 44f));
        gradeLabel.color = new Color(0.16f, 0.13f, 0.1f);
        gradeLabel.fontStyle = FontStyles.Bold;

        var progressBg = new GameObject("ProgressBg", typeof(RectTransform), typeof(Image));
        progressBg.transform.SetParent(bg.transform, false);
        var progressBgRect = progressBg.GetComponent<RectTransform>();
        progressBgRect.anchorMin = new Vector2(0.5f, 0f);
        progressBgRect.anchorMax = new Vector2(0.5f, 0f);
        progressBgRect.pivot = new Vector2(0.5f, 0f);
        progressBgRect.anchoredPosition = new Vector2(0f, 16f);
        progressBgRect.sizeDelta = new Vector2(ProgressBarWidth, 16f);
        progressBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

        var fillGO = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(progressBg.transform, false);
        var fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(0f, 0f);
        progressFillImage = fillGO.GetComponent<Image>();
        progressFillImage.color = progressFillColor;
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

    // Called by CustomerVisualManager every frame the order is still active - shows delivered/
    // requested (e.g. "1/3") and fills the bar to match (customer_order_design_v7.html §1/§3 -
    // no grade requirement to show anymore, just a count).
    public void SetOrder(int deliveredCount, int requestedCount)
    {
        gradeLabel.text = deliveredCount + " / " + requestedCount;
        float progress01 = requestedCount > 0 ? (float)deliveredCount / requestedCount : 0f;
        progressFillImage.rectTransform.sizeDelta = new Vector2(ProgressBarWidth * progress01, 0f);
    }

    // Only the front-of-line customer can actually be served right now (OrderQueueManager.
    // TryFulfill always targets queue[0]) - everyone behind them still shows their order but their
    // bubble shouldn't invite a tap that would silently do nothing useful.
    public void SetInteractable(bool interactable)
    {
        bubbleButton.interactable = interactable;
        bubbleBackgroundImage.color = interactable
            ? new Color(0.97f, 0.93f, 0.85f, activeBubbleAlpha.a)
            : new Color(0.97f, 0.93f, 0.85f, inactiveBubbleAlpha.a);
    }

    // Only the arrived front-of-line customer ever shows a demand at all now - everyone else
    // (still walking up, or waiting further back) keeps the bubble hidden entirely rather than
    // visible-but-dimmed, since dimmed still read as "here's what I want" too early.
    public void SetBubbleVisible(bool visible)
    {
        bubbleRoot.gameObject.SetActive(visible);
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
        HasArrived = (transform.position - walkTarget).sqrMagnitude <= ArriveThreshold * ArriveThreshold;

        if (despawnOnArrive && HasArrived)
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
