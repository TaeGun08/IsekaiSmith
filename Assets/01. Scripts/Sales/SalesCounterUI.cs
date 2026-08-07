using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed, same pattern as
// InteractionPromptUI/CraftingMinigameUI. OrderQueueManager calls Show(self) every frame it wants
// the counter visible and Hide() otherwise - this class only renders slot state, it doesn't decide
// order flow itself (customer_order_design_v3.html §3).
public class SalesCounterUI : MonoBehaviour
{
    private static SalesCounterUI instance;

    public static SalesCounterUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("SalesCounterUI");
                instance = go.AddComponent<SalesCounterUI>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    // Card visuals are built up to this many - covers OrderQueueManager.slotCount being tweaked
    // in the Inspector without needing to rebuild UI at runtime. Kept small (not e.g. 8) so the
    // backdrop/content width below stays sane on a 1080-wide reference canvas.
    private const int MaxSlotCards = 4;
    private const float CardWidth = 220f;
    private const float CardHeight = 250f;
    private const float CardGap = 18f;
    private const float ContentWidth = MaxSlotCards * CardWidth + (MaxSlotCards - 1) * CardGap + 64f;

    // Order tickets read as light "paper" cards (matches customer_order_design.html mockups) - so
    // labels on them use dark ink, not the white used everywhere else in this self-built UI style.
    private readonly Color cardInk = new Color(0.16f, 0.13f, 0.1f);
    private readonly Color emptyColor = new Color(0.62f, 0.58f, 0.5f, 0.35f);
    private readonly Color filledColor = new Color(0.97f, 0.93f, 0.85f);
    private readonly Color patienceGood = new Color(0.35f, 0.62f, 0.32f);
    private readonly Color patienceBad = new Color(0.82f, 0.28f, 0.1f);

    private GameObject panel;
    private readonly List<SlotCard> cards = new List<SlotCard>();
    private TMP_Text goldText;
    private TMP_Text reputationText;
    private Image reputationFill;
    private GameObject rushBanner;

    private OrderQueueManager activeManager;

    private class SlotCard
    {
        public RectTransform Rect;
        public Image Background;
        public TMP_Text GradeLabel;
        public Image PatienceFill;
        public TMP_Text PayLabel;
        public Button Button;
        public GameObject EmptyLabel;
    }

    private void Awake()
    {
        BuildUI();
        panel.SetActive(false);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("SalesCounterCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 280f);
        panelRect.sizeDelta = new Vector2(ContentWidth, 460f);
        // Dark backdrop behind everything - the cards/text used to float directly over the 3D
        // scene with no grouping surface, which made low-contrast text unreadable depending on
        // what was behind it (e.g. white text over light grass terrain).
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        for (int i = 0; i < MaxSlotCards; i++)
        {
            cards.Add(BuildSlotCard(i));
        }

        rushBanner = MakeText("RushBanner", 26, new Vector2(0f, -300f), new Vector2(ContentWidth - 40f, 44f)).gameObject;
        var rushText = rushBanner.GetComponent<TMP_Text>();
        rushText.text = "RUSH HOUR - orders incoming!";
        rushText.color = new Color(1f, 0.6f, 0.3f);
        rushText.fontStyle = FontStyles.Bold;

        goldText = MakeText("GoldText", 32, new Vector2(0f, -356f), new Vector2(ContentWidth - 40f, 44f));
        goldText.color = new Color(1f, 0.86f, 0.5f);
        goldText.fontStyle = FontStyles.Bold;

        var repBg = new GameObject("ReputationBarBg", typeof(RectTransform), typeof(Image));
        repBg.transform.SetParent(panel.transform, false);
        var repBgRect = repBg.GetComponent<RectTransform>();
        repBgRect.anchorMin = new Vector2(0.5f, 1f);
        repBgRect.anchorMax = new Vector2(0.5f, 1f);
        repBgRect.pivot = new Vector2(0.5f, 1f);
        repBgRect.anchoredPosition = new Vector2(0f, -412f);
        repBgRect.sizeDelta = new Vector2(ContentWidth - 40f, 26f);
        repBg.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

        var repFillGO = new GameObject("ReputationBarFill", typeof(RectTransform), typeof(Image));
        repFillGO.transform.SetParent(repBg.transform, false);
        var repFillRect = repFillGO.GetComponent<RectTransform>();
        repFillRect.anchorMin = new Vector2(0f, 0f);
        repFillRect.anchorMax = new Vector2(0f, 1f);
        repFillRect.pivot = new Vector2(0f, 0.5f);
        repFillRect.anchoredPosition = Vector2.zero;
        repFillRect.sizeDelta = new Vector2(ContentWidth - 40f, 0f);
        reputationFill = repFillGO.GetComponent<Image>();
        reputationFill.color = new Color(0.85f, 0.65f, 0.25f);

        reputationText = MakeText("ReputationText", 18, new Vector2(0f, -412f), new Vector2(ContentWidth - 40f, 26f));
        reputationText.color = Color.white;
        reputationText.fontStyle = FontStyles.Bold;
    }

    private SlotCard BuildSlotCard(int index)
    {
        var root = new GameObject("Slot" + index, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(panel.transform, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.sizeDelta = new Vector2(CardWidth, CardHeight);
        var background = root.GetComponent<Image>();

        var gradeLabel = MakeText("Grade", 24, new Vector2(0f, -16f), new Vector2(CardWidth - 20f, 34f), root.transform);
        gradeLabel.fontStyle = FontStyles.Bold;
        gradeLabel.color = cardInk;

        var patienceBg = new GameObject("PatienceBg", typeof(RectTransform), typeof(Image));
        patienceBg.transform.SetParent(root.transform, false);
        var patienceBgRect = patienceBg.GetComponent<RectTransform>();
        patienceBgRect.anchorMin = new Vector2(0.5f, 1f);
        patienceBgRect.anchorMax = new Vector2(0.5f, 1f);
        patienceBgRect.pivot = new Vector2(0.5f, 1f);
        patienceBgRect.anchoredPosition = new Vector2(0f, -166f);
        patienceBgRect.sizeDelta = new Vector2(CardWidth - 28f, 18f);
        patienceBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

        var patienceFillGO = new GameObject("PatienceFill", typeof(RectTransform), typeof(Image));
        patienceFillGO.transform.SetParent(patienceBg.transform, false);
        var patienceFillRect = patienceFillGO.GetComponent<RectTransform>();
        patienceFillRect.anchorMin = new Vector2(0f, 0f);
        patienceFillRect.anchorMax = new Vector2(0f, 1f);
        patienceFillRect.pivot = new Vector2(0f, 0.5f);
        patienceFillRect.anchoredPosition = Vector2.zero;
        patienceFillRect.sizeDelta = new Vector2(CardWidth - 28f, 0f);
        var patienceFill = patienceFillGO.GetComponent<Image>();
        patienceFill.color = patienceGood;

        var payLabel = MakeText("Pay", 22, new Vector2(0f, -198f), new Vector2(CardWidth - 20f, 32f), root.transform);
        payLabel.fontStyle = FontStyles.Bold;
        payLabel.color = cardInk;

        // English-only for now - no Korean-compatible TMP font asset in the project yet
        // (Korean copy planned once one is added).
        var emptyLabel = MakeText("EmptyLabel", 18, new Vector2(0f, -110f), new Vector2(CardWidth - 28f, 80f), root.transform);
        emptyLabel.text = "Waiting for\nnext customer...";
        emptyLabel.color = new Color(cardInk.r, cardInk.g, cardInk.b, 0.75f);

        return new SlotCard
        {
            Rect = rootRect,
            Background = background,
            GradeLabel = gradeLabel,
            PatienceFill = patienceFill,
            PayLabel = payLabel,
            Button = root.GetComponent<Button>(),
            EmptyLabel = emptyLabel.gameObject,
        };
    }

    private TMP_Text MakeText(string name, int fontSize, Vector2 anchoredPos, Vector2 size, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent != null ? parent : panel.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        return tmp;
    }

    public void Show(OrderQueueManager manager)
    {
        activeManager = manager;
        panel.SetActive(true);

        IReadOnlyList<CustomerOrder> slots = manager.Slots;
        // Cards are laid out centered around the active slot count each call, not MaxSlotCards -
        // otherwise a 3-slot counter renders its row shifted off-center inside 4 card sockets.
        int activeCount = Mathf.Min(slots.Count, cards.Count);
        float activeRowWidth = activeCount * CardWidth + Mathf.Max(0, activeCount - 1) * CardGap;
        float startX = -activeRowWidth / 2f + CardWidth / 2f;

        for (int i = 0; i < cards.Count; i++)
        {
            SlotCard card = cards[i];
            bool inUse = i < slots.Count;
            card.Rect.gameObject.SetActive(inUse);
            if (!inUse)
            {
                continue;
            }

            card.Rect.anchoredPosition = new Vector2(startX + i * (CardWidth + CardGap), -20f);

            CustomerOrder order = slots[i];
            bool filled = order != null;
            card.Background.color = filled ? filledColor : emptyColor;
            card.EmptyLabel.SetActive(!filled);
            card.GradeLabel.gameObject.SetActive(filled);
            card.PayLabel.gameObject.SetActive(filled);
            card.PatienceFill.transform.parent.gameObject.SetActive(filled);

            if (!filled)
            {
                continue;
            }

            card.GradeLabel.text = CraftGradeUtility.DisplayName(order.MinGrade) + "+";
            card.PayLabel.text = "~" + SalesPricing.BaseFor(order.MinGrade) + "G";

            float patience01 = order.Patience01;
            RectTransform fillRect = (RectTransform)card.PatienceFill.transform;
            fillRect.sizeDelta = new Vector2((CardWidth - 28f) * patience01, 0f);
            card.PatienceFill.color = Color.Lerp(patienceBad, patienceGood, patience01);

            int slotIndex = i;
            card.Button.onClick.RemoveAllListeners();
            card.Button.onClick.AddListener(() => activeManager.TryFulfill(slotIndex));
        }

        goldText.text = "Gold " + SalesCurrency.Gold;
        reputationText.text = "Reputation " + Mathf.RoundToInt(Reputation.Percent) + "%";
        var reputationFillRect = (RectTransform)reputationFill.transform;
        reputationFillRect.sizeDelta = new Vector2((ContentWidth - 40f) * Reputation.Percent / 100f, 0f);
        rushBanner.SetActive(manager.InRush);
    }

    public void Hide()
    {
        activeManager = null;
        panel.SetActive(false);
    }
}
