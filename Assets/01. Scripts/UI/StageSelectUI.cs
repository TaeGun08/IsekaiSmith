using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Touch-first stage select screen (stage_system_design_v2.html §1) - a small always-visible icon
// (left edge, mirroring PlayerInventoryUI's "BAG" icon on the right edge) that opens a panel.
// Only ever shows the single card that's actually actionable right now - one stage card for
// whichever stage is next (it turns into "STAGE 2" the moment Stage 1 clears, rather than the
// panel listing all 3 at once) plus the dungeon card once unlocked (사용자 요청 2026-08-24: "UI가
// 여러개 있는 게 아니라 스테이지 1를 클리어하면 2로 바뀌는 식으로"). Replaces v1's in-field
// StageGate - per user feedback, stage entry should be "따로 UI로 빼서 클릭해서 진행", not a
// world object.
//
// No replay: only the exact next unlocked stage is ever shown at all, so there's no separate
// "locked"/"cleared" card state to display anymore (user request: "전 스테이지로 돌아갈순 없고" -
// see stage_system_design_v2.html §6); the field's own mana drop-rate bump is the compensation for
// not being able to re-farm them (PlayerCombat.ManaDropChance).
public class StageSelectUI : MonoBehaviour
{
    private const float CardHeight = 130f;
    private const float CardGap = 16f;
    // At most one stage-or-all-cleared card plus one dungeon card are ever shown at once now.
    private const int MaxVisibleCards = 2;

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
        panelRect.sizeDelta = new Vector2(760f, 480f); // room for 1 stage-or-cleared card + the dungeon card
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
        containerRect.sizeDelta = new Vector2(680f, (CardHeight + CardGap) * MaxVisibleCards);
        cardsContainer = containerGO.transform;

        Button closeButton = MakeButton(panel.transform, "CloseButton", new Vector2(0f, 40f), new Vector2(240f, 90f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(TogglePanel);
    }

    // Hidden until the player has an equipped, precise-crafted weapon (GuidedTutorial.
    // IsStagesUnlocked) - entering a stage half-armed just teaches "this game is unfair" (user
    // request: "튜토리얼 중에는 튜토리얼만 오로지 깰 수 있게... 스테이지 등이 열리는 게 좋을 것
    // 같아"). Stages are themselves a taught tutorial step now (guided_tutorial_design_v2.html),
    // not something withheld until the whole tutorial ends.
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

            // No toast here - GuidedTutorial fires its own "Stages Unlocked!" the instant it
            // enters Step.StageProgress, which is exactly when this flips true.
        }
    }

    private void TogglePanel()
    {
        // Can't open mid-transition/mid-fight - there's nowhere sensible for "enter a different
        // stage/dungeon" to go while one is already loading or running.
        if (!panelOpen && (StageSceneController.Instance.IsTransitioning || StageEncounterController.Instance.IsEncounterActive
            || DungeonSceneController.Instance.IsTransitioning || DungeonEncounterController.Instance.IsEncounterActive))
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

        int cardIndex = 0;

        // Only the single next-actionable stage ever gets a card - locked/cleared states no
        // longer need their own card at all, since a cleared stage can't be replayed and a locked
        // one is, by definition, never the next one (사용자 요청 2026-08-24).
        if (StageBank.AllStagesCleared)
        {
            BuildAllStagesClearedCard(cardIndex++);
        }
        else
        {
            BuildStageCard(cardIndex++, StageBank.HighestStageCleared + 1);
        }

        // Shown once the prerequisite (Stage 1 cleared - DungeonBank.IsUnlocked, no longer all 3
        // - 사용자 요청 2026-08-20) is met. Placed after the stage card so it always reads as "the
        // next thing after stages" rather than jumping around.
        if (DungeonBank.IsUnlocked)
        {
            BuildDungeonCard(cardIndex++);
        }
    }

    private void BuildStageCard(int index, int stageNumber)
    {
        var cardGO = new GameObject("StageCard_" + stageNumber, typeof(RectTransform), typeof(Image), typeof(Button));
        cardGO.transform.SetParent(cardsContainer, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 1f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        cardRect.sizeDelta = new Vector2(680f, CardHeight);

        // Always "the next one to enter" now - RefreshCards only ever calls this for
        // HighestStageCleared + 1, so there's no locked/cleared variant to render here anymore.
        Color color = readyColor;
        cardGO.GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0.28f);
        var border = cardGO.AddComponent<Outline>();
        border.effectColor = color;
        border.effectDistance = new Vector2(2f, -2f);
        border.enabled = true;

        TMP_Text nameText = MakeText(cardGO.transform, "Name", 30, new Vector2(0f, -32f), new Vector2(600f, 40f));
        nameText.text = "STAGE " + stageNumber;
        nameText.fontStyle = FontStyles.Bold;

        TMP_Text statusText = MakeText(cardGO.transform, "Status", 20, new Vector2(0f, -80f), new Vector2(600f, 30f));
        statusText.text = "TAP TO ENTER";
        statusText.color = color;

        cardGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            StageSceneController.Instance.EnterStage(stageNumber);
            TogglePanel();
        });
    }

    // Shown once HighestStageCleared reaches StageBank.StageCount - a simple non-interactive
    // "you're done here" card instead of just silently having nothing where the stage card used
    // to be.
    private void BuildAllStagesClearedCard(int index)
    {
        var cardGO = new GameObject("StagesClearedCard", typeof(RectTransform), typeof(Image));
        cardGO.transform.SetParent(cardsContainer, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 1f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        cardRect.sizeDelta = new Vector2(680f, CardHeight);

        Color color = clearedColor;
        cardGO.GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0.28f);
        var border = cardGO.AddComponent<Outline>();
        border.effectColor = color;
        border.effectDistance = new Vector2(2f, -2f);

        TMP_Text nameText = MakeText(cardGO.transform, "Name", 30, new Vector2(0f, -32f), new Vector2(600f, 40f));
        nameText.text = "STAGES";
        nameText.fontStyle = FontStyles.Bold;

        TMP_Text statusText = MakeText(cardGO.transform, "Status", 20, new Vector2(0f, -80f), new Vector2(600f, 30f));
        statusText.text = "ALL CLEARED";
        statusText.color = color;
    }

    // Only ever built when DungeonBank.IsUnlocked (RefreshCards). Unlike a stage, this is a
    // repeatable deep-dive with as many floors as DungeonFloorTable has rows (user request:
    // "던전을 최대한 많이 만들어도 좋아") - always tappable, showing the best depth reached so far
    // instead of a one-time CLEARED lock.
    private void BuildDungeonCard(int index)
    {
        var cardGO = new GameObject("DungeonCard", typeof(RectTransform), typeof(Image), typeof(Button));
        cardGO.transform.SetParent(cardsContainer, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 1f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        cardRect.sizeDelta = new Vector2(680f, CardHeight);

        int deepest = DungeonBank.DeepestFloorCleared;
        Color color = deepest > 0 ? clearedColor : readyColor;
        cardGO.GetComponent<Image>().color = new Color(color.r, color.g, color.b, 0.28f);
        var border = cardGO.AddComponent<Outline>();
        border.effectColor = color;
        border.effectDistance = new Vector2(2f, -2f);
        border.enabled = true;

        TMP_Text nameText = MakeText(cardGO.transform, "Name", 30, new Vector2(0f, -32f), new Vector2(600f, 40f));
        nameText.text = "DUNGEON";
        nameText.fontStyle = FontStyles.Bold;

        TMP_Text statusText = MakeText(cardGO.transform, "Status", 20, new Vector2(0f, -80f), new Vector2(600f, 30f));
        statusText.text = deepest > 0 ? "Deepest: Floor " + deepest + " - TAP TO DIVE AGAIN" : "TAP TO ENTER";
        statusText.color = color;

        cardGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            DungeonSceneController.Instance.EnterDungeon();
            TogglePanel();
        });
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
