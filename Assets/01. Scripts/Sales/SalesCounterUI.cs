using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed, same pattern as
// InteractionPromptUI/CraftingMinigameUI. OrderQueueManager calls Show(self) every frame it wants
// the counter visible and Hide() otherwise - this class only renders slot state, it doesn't decide
// order flow itself (customer_order_design_v2.html §3).
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
    // in the Inspector without needing to rebuild UI at runtime.
    private const int MaxSlotCards = 5;
    private const float CardWidth = 170f;
    private const float CardGap = 14f;

    private readonly Color emptyColor = new Color(1f, 1f, 1f, 0.08f);
    private readonly Color filledColor = new Color(0.98f, 0.95f, 0.9f);
    private readonly Color patienceGood = new Color(0.44f, 0.6f, 0.35f);
    private readonly Color patienceBad = new Color(0.76f, 0.27f, 0.05f);

    private GameObject panel;
    private readonly List<SlotCard> cards = new List<SlotCard>();
    private TMP_Text goldText;
    private TMP_Text reputationText;
    private Image reputationFill;
    private TMP_Text comboText;
    private GameObject rushBanner;

    private OrderQueueManager activeManager;

    private class SlotCard
    {
        public GameObject Root;
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

        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 260f);
        panelRect.sizeDelta = new Vector2(1000f, 420f);

        float rowWidth = MaxSlotCards * CardWidth + (MaxSlotCards - 1) * CardGap;
        float startX = -rowWidth / 2f + CardWidth / 2f;
        for (int i = 0; i < MaxSlotCards; i++)
        {
            cards.Add(BuildSlotCard(i, new Vector2(startX + i * (CardWidth + CardGap), -20f)));
        }

        rushBanner = MakeText("RushBanner", 22, new Vector2(0f, -190f), new Vector2(rowWidth, 40f)).gameObject;
        var rushText = rushBanner.GetComponent<TMP_Text>();
        rushText.text = "RUSH! 손님이 몰려옵니다";
        rushText.color = new Color(1f, 0.55f, 0.25f);
        rushText.fontStyle = FontStyles.Bold;

        goldText = MakeText("GoldText", 26, new Vector2(-260f, -250f), new Vector2(300f, 40f));
        goldText.alignment = TextAlignmentOptions.Left;

        comboText = MakeText("ComboText", 22, new Vector2(260f, -250f), new Vector2(300f, 40f));
        comboText.alignment = TextAlignmentOptions.Right;
        comboText.color = new Color(0.95f, 0.82f, 0.42f);

        var repBg = new GameObject("ReputationBarBg", typeof(RectTransform), typeof(Image));
        repBg.transform.SetParent(panel.transform, false);
        var repBgRect = repBg.GetComponent<RectTransform>();
        repBgRect.anchorMin = new Vector2(0.5f, 1f);
        repBgRect.anchorMax = new Vector2(0.5f, 1f);
        repBgRect.pivot = new Vector2(0.5f, 1f);
        repBgRect.anchoredPosition = new Vector2(0f, -300f);
        repBgRect.sizeDelta = new Vector2(rowWidth, 18f);
        repBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

        var repFillGO = new GameObject("ReputationBarFill", typeof(RectTransform), typeof(Image));
        repFillGO.transform.SetParent(repBg.transform, false);
        var repFillRect = repFillGO.GetComponent<RectTransform>();
        repFillRect.anchorMin = new Vector2(0f, 0f);
        repFillRect.anchorMax = new Vector2(0f, 1f);
        repFillRect.pivot = new Vector2(0f, 0.5f);
        repFillRect.anchoredPosition = Vector2.zero;
        repFillRect.sizeDelta = new Vector2(rowWidth, 0f);
        reputationFill = repFillGO.GetComponent<Image>();
        reputationFill.color = new Color(0.85f, 0.65f, 0.25f);

        reputationText = MakeText("ReputationText", 16, new Vector2(0f, -300f), new Vector2(rowWidth, 18f));
        reputationText.color = Color.white;
    }

    private SlotCard BuildSlotCard(int index, Vector2 anchoredPos)
    {
        var root = new GameObject("Slot" + index, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(panel.transform, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = anchoredPos;
        rootRect.sizeDelta = new Vector2(CardWidth, 230f);
        var background = root.GetComponent<Image>();

        var gradeLabel = MakeText("Grade", 20, new Vector2(0f, -14f), new Vector2(CardWidth - 16f, 30f), root.transform);
        gradeLabel.fontStyle = FontStyles.Bold;

        var patienceBg = new GameObject("PatienceBg", typeof(RectTransform), typeof(Image));
        patienceBg.transform.SetParent(root.transform, false);
        var patienceBgRect = patienceBg.GetComponent<RectTransform>();
        patienceBgRect.anchorMin = new Vector2(0.5f, 1f);
        patienceBgRect.anchorMax = new Vector2(0.5f, 1f);
        patienceBgRect.pivot = new Vector2(0.5f, 1f);
        patienceBgRect.anchoredPosition = new Vector2(0f, -140f);
        patienceBgRect.sizeDelta = new Vector2(CardWidth - 20f, 14f);
        patienceBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

        var patienceFillGO = new GameObject("PatienceFill", typeof(RectTransform), typeof(Image));
        patienceFillGO.transform.SetParent(patienceBg.transform, false);
        var patienceFillRect = patienceFillGO.GetComponent<RectTransform>();
        patienceFillRect.anchorMin = new Vector2(0f, 0f);
        patienceFillRect.anchorMax = new Vector2(0f, 1f);
        patienceFillRect.pivot = new Vector2(0f, 0.5f);
        patienceFillRect.anchoredPosition = Vector2.zero;
        patienceFillRect.sizeDelta = new Vector2(CardWidth - 20f, 0f);
        var patienceFill = patienceFillGO.GetComponent<Image>();
        patienceFill.color = patienceGood;

        var payLabel = MakeText("Pay", 18, new Vector2(0f, -170f), new Vector2(CardWidth - 16f, 28f), root.transform);

        var emptyLabel = MakeText("EmptyLabel", 15, new Vector2(0f, -100f), new Vector2(CardWidth - 20f, 60f), root.transform);
        emptyLabel.text = "다음 손님\n대기중...";
        emptyLabel.color = new Color(1f, 1f, 1f, 0.55f);

        return new SlotCard
        {
            Root = root,
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
        for (int i = 0; i < cards.Count; i++)
        {
            SlotCard card = cards[i];
            bool inUse = i < slots.Count;
            card.Root.SetActive(inUse);
            if (!inUse)
            {
                continue;
            }

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
            fillRect.sizeDelta = new Vector2((CardWidth - 20f) * patience01, 0f);
            card.PatienceFill.color = Color.Lerp(patienceBad, patienceGood, patience01);

            int slotIndex = i;
            card.Button.onClick.RemoveAllListeners();
            card.Button.onClick.AddListener(() => activeManager.TryFulfill(slotIndex));
        }

        goldText.text = "Gold " + SalesCurrency.Gold;
        comboText.text = manager.Combo > 0 ? "Combo x" + manager.Combo : "";
        reputationText.text = "Reputation " + Mathf.RoundToInt(Reputation.Percent) + "%";
        var reputationFillRect = (RectTransform)reputationFill.transform;
        float rowWidth = MaxSlotCards * CardWidth + (MaxSlotCards - 1) * CardGap;
        reputationFillRect.sizeDelta = new Vector2(rowWidth * Reputation.Percent / 100f, 0f);
        rushBanner.SetActive(manager.InRush);
    }

    public void Hide()
    {
        activeManager = null;
        panel.SetActive(false);
    }
}
