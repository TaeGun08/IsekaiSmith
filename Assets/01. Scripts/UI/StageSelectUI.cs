using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Touch-first stage select screen (stage_system_design_v2.html §1) - a small always-visible icon
// (left edge, mirroring PlayerInventoryUI's "BAG" icon on the right edge) that opens a panel
// listing all StageBank.StageCount stages as tappable cards. Replaces v1's in-field StageGate -
// per user feedback, stage entry should be "따로 UI로 빼서 클릭해서 진행", not a world object.
//
// No replay: only the exact next unlocked stage is tappable. Already-cleared stages show
// "CLEARED" and don't respond to taps (user request: "전 스테이지로 돌아갈순 없고" - see
// stage_system_design_v2.html §6); the field's own mana drop-rate bump is the compensation for
// not being able to re-farm them (PlayerCombat.ManaDropChance).
public class StageSelectUI : MonoBehaviour
{
    private const float CardHeight = 130f;
    private const float CardGap = 16f;

    private static StageSelectUI instance;

    public static StageSelectUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StageSelectUI");
                instance = go.AddComponent<StageSelectUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private readonly Color clearedColor = new Color(0.4f, 0.68f, 0.45f);
    private readonly Color readyColor = new Color(0.75f, 0.6f, 0.2f);
    private readonly Color lockedColor = new Color(0.3f, 0.28f, 0.26f);

    private GameObject icon;
    private GameObject panel;
    private Transform cardsContainer;
    private bool panelOpen;

    // Referencing Instance is enough to bootstrap this singleton - same explicit-call-for-
    // readability convention as PlayerCombat/PlayerInventoryUI's Activate().
    public void Activate()
    {
    }

    private void Awake()
    {
        BuildIcon();
        BuildPanel();
        panel.SetActive(false);
    }

    private void BuildIcon()
    {
        var canvasGO = new GameObject("StageSelectIconCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        // Left edge, mirroring PlayerInventoryUI's BAG icon on the right edge - the two entry
        // points (equipment / stages) read as a matched pair instead of competing for the same
        // corner.
        var iconGO = new GameObject("StageSelectIcon", typeof(RectTransform), typeof(Image), typeof(Button));
        iconGO.transform.SetParent(canvasGO.transform, false);
        icon = iconGO;
        var rect = iconGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(24f, -256f);
        rect.sizeDelta = new Vector2(88f, 88f);
        iconGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var label = MakeText(iconGO.transform, "Label", 20, Vector2.zero, new Vector2(88f, 88f));
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = "STAGE";
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, 0.86f, 0.5f);

        iconGO.GetComponent<Button>().onClick.AddListener(TogglePanel);
    }

    private void BuildPanel()
    {
        var canvasGO = new GameObject("StageSelectPanelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(760f, 640f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var title = MakeText(panel.transform, "Title", 34, new Vector2(0f, -30f), new Vector2(700f, 48f));
        title.text = "Stages";
        title.fontStyle = FontStyles.Bold;

        var containerGO = new GameObject("Cards", typeof(RectTransform));
        containerGO.transform.SetParent(panel.transform, false);
        var containerRect = containerGO.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 1f);
        containerRect.anchorMax = new Vector2(0.5f, 1f);
        containerRect.pivot = new Vector2(0.5f, 1f);
        containerRect.anchoredPosition = new Vector2(0f, -100f);
        containerRect.sizeDelta = new Vector2(680f, (CardHeight + CardGap) * StageBank.StageCount);
        cardsContainer = containerGO.transform;

        Button closeButton = MakeButton(panel.transform, "CloseButton", new Vector2(0f, 40f), new Vector2(240f, 90f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(TogglePanel);
    }

    // Hidden until the tutorial's full craft->sell loop is taught (GuidedTutorial.IsStagesUnlocked)
    // - entering a stage half-armed just teaches "this game is unfair" (user request: "튜토리얼
    // 중에는 튜토리얼만 오로지 깰 수 있게... 스테이지 등이 열리는 게 좋을 것 같아").
    private void Update()
    {
        if (icon == null)
        {
            return;
        }

        bool unlocked = GuidedTutorial.IsStagesUnlocked;
        if (icon.activeSelf != unlocked)
        {
            icon.SetActive(unlocked);
        }
    }

    private void TogglePanel()
    {
        // Can't open mid-transition/mid-fight - there's nowhere sensible for "enter a different
        // stage" to go while one is already loading or running.
        if (!panelOpen && (StageSceneController.Instance.IsTransitioning || StageEncounterController.Instance.IsEncounterActive))
        {
            return;
        }

        panelOpen = !panelOpen;
        panel.SetActive(panelOpen);

        if (panelOpen)
        {
            RefreshCards();
        }
    }

    private void RefreshCards()
    {
        foreach (Transform child in cardsContainer)
        {
            Destroy(child.gameObject);
        }

        for (int i = 0; i < StageBank.StageCount; i++)
        {
            BuildCard(i, i + 1);
        }
    }

    private void BuildCard(int index, int stageNumber)
    {
        var cardGO = new GameObject("StageCard_" + stageNumber, typeof(RectTransform), typeof(Image), typeof(Button));
        cardGO.transform.SetParent(cardsContainer, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 1f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        cardRect.sizeDelta = new Vector2(680f, CardHeight);

        bool cleared = stageNumber <= StageBank.HighestStageCleared;
        bool isNext = stageNumber == StageBank.HighestStageCleared + 1;

        Color color = cleared ? clearedColor : (isNext ? readyColor : lockedColor);
        cardGO.GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0.28f);
        var border = cardGO.AddComponent<Outline>();
        border.effectColor = color;
        border.effectDistance = new Vector2(2f, -2f);
        border.enabled = isNext;

        TMP_Text nameText = MakeText(cardGO.transform, "Name", 30, new Vector2(0f, -32f), new Vector2(600f, 40f));
        nameText.text = "STAGE " + stageNumber;
        nameText.fontStyle = FontStyles.Bold;

        string statusLabel = cleared ? "CLEARED" : (isNext ? "TAP TO ENTER" : "LOCKED");
        TMP_Text statusText = MakeText(cardGO.transform, "Status", 20, new Vector2(0f, -80f), new Vector2(600f, 30f));
        statusText.text = statusLabel;
        statusText.color = color;

        Button button = cardGO.GetComponent<Button>();
        button.interactable = isNext;
        if (isNext)
        {
            button.onClick.AddListener(() =>
            {
                StageSceneController.Instance.EnterStage(stageNumber);
                TogglePanel();
            });
        }
    }

    private static TMP_Text MakeText(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size)
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
        tmp.color = Color.white;
        return tmp;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, string text, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;

        TMP_Text label = MakeText(go.transform, "Text", 26, Vector2.zero, size);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = text;
        label.fontStyle = FontStyles.Bold;

        return go.GetComponent<Button>();
    }
}
