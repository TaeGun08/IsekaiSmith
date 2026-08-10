using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed, same pattern as
// InteractionPromptUI/CraftingMinigameUI/SalesCounterUI. Slide wording lives in the `slides` array,
// kept separate from the build/layout code below it, so updating the copy later (e.g. once a real
// tutorial replaces this temporary one) doesn't require touching UI logic. See tutorial_design.html.
//
// ResourceHUD.Start() calls ShowIfFirstTime() once at game start (it's the one always-present,
// scene-wired MonoBehaviour to bootstrap from) - this class itself never shows on its own.
public class TutorialUI : MonoBehaviour
{
    private const string SeenPrefsKey = "TutorialSeen";

    private static TutorialUI instance;

    public static TutorialUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("TutorialUI");
                instance = go.AddComponent<TutorialUI>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private struct Slide
    {
        public readonly string Title;
        public readonly string Body;

        public Slide(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }

    // English-only for now - no Korean-compatible TMP font asset in the project yet (Korean copy
    // planned once one is added; keep this array as the single place to swap wording later).
    private readonly Slide[] slides =
    {
        new Slide("Isekai Smith", "Rebuild a blacksmith's forge in a ruined world.\n\nSwipe through to learn how to play."),
        new Slide("Move", "Drag anywhere on screen\nto move in that direction."),
        new Slide("Gather", "Walk up to trees at the lumber camp or\nrocks at the quarry to gather automatically.\n\nCarried materials are stored automatically\nnear the storage depot."),
        new Slide("Craft", "With enough materials, approach the smithy for\nCRAFT (precise minigame) or QUICK CRAFT (instant).\n\nFinished tools are graded by quality."),
        new Slide("Sell", "Approach the sales counter to see customer\norders. Tap an order to auto-deliver a\nmatching tool for gold.\n\nRepeat gather -> craft -> sell!"),
    };

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text bodyText;
    private readonly System.Collections.Generic.List<Image> dots = new System.Collections.Generic.List<Image>();
    private TMP_Text nextLabel;
    private Button nextButton;
    private Button skipButton;

    private int currentIndex;

    private void Awake()
    {
        BuildUI();
        panel.SetActive(false);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every other self-built UI (InteractionPrompt/CraftingMinigame/SalesCounter all
        // use 5) so the tutorial always reads on top regardless of what station the player is near.
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(panel.transform, false);
        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(880f, 900f);
        card.GetComponent<Image>().color = new Color(0.98f, 0.93f, 0.85f, 0.98f);

        titleText = MakeText(card.transform, "Title", 40, new Vector2(0f, -70f), new Vector2(760f, 60f));
        titleText.color = new Color(0.16f, 0.13f, 0.1f);
        titleText.fontStyle = FontStyles.Bold;

        bodyText = MakeText(card.transform, "Body", 26, new Vector2(0f, -400f), new Vector2(760f, 500f));
        bodyText.color = new Color(0.25f, 0.21f, 0.17f);
        bodyText.alignment = TextAlignmentOptions.Top;

        for (int i = 0; i < slides.Length; i++)
        {
            var dotGO = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image));
            dotGO.transform.SetParent(card.transform, false);
            var dotRect = dotGO.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0f);
            dotRect.anchorMax = new Vector2(0.5f, 0f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            float spacing = 26f;
            float startX = -(slides.Length - 1) * spacing / 2f;
            dotRect.anchoredPosition = new Vector2(startX + i * spacing, 110f);
            dotRect.sizeDelta = new Vector2(14f, 14f);
            dots.Add(dotGO.GetComponent<Image>());
        }

        skipButton = MakeButton(card.transform, "SkipButton", new Vector2(-215f, 60f), new Vector2(270f, 110f), "Skip", new Color(0.5f, 0.46f, 0.4f));
        skipButton.onClick.AddListener(Close);

        var nextGO = MakeButton(card.transform, "NextButton", new Vector2(215f, 60f), new Vector2(270f, 110f), "Next >", new Color(0.71f, 0.4f, 0.11f));
        nextButton = nextGO;
        nextLabel = nextGO.GetComponentInChildren<TMP_Text>();
        nextButton.onClick.AddListener(OnNext);

        // Sibling of Panel (not a child) so it stays visible even while the slideshow is closed -
        // this is how the player reopens the tutorial after the first-run auto-show. Bottom-left
        // corner: top-right is ResourceHUD's resource panel, top-left is DevAutoPlayController's
        // debug buttons - bottom-left is the one corner nothing else claims.
        var helpButton = MakeButton(canvasGO.transform, "HelpButton", Vector2.zero, new Vector2(96f, 96f), "?", new Color(0f, 0f, 0f, 0.5f));
        var helpRect = helpButton.GetComponent<RectTransform>();
        helpRect.anchorMin = new Vector2(0f, 0f);
        helpRect.anchorMax = new Vector2(0f, 0f);
        helpRect.pivot = new Vector2(0f, 0f);
        helpRect.anchoredPosition = new Vector2(24f, 24f);
        helpButton.onClick.AddListener(Open);
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
        tmp.color = Color.white;
        return tmp;
    }

    private Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, string text, Color color)
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

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 30;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go.GetComponent<Button>();
    }

    // Called once from ResourceHUD.Start() - the one always-present, scene-wired MonoBehaviour -
    // so the tutorial opens automatically on a player's very first session and never again after.
    public void ShowIfFirstTime()
    {
        if (PlayerPrefs.GetInt(SeenPrefsKey, 0) != 0)
        {
            return;
        }

        Open();
    }

    public void Open()
    {
        currentIndex = 0;
        RefreshSlide();
        panel.SetActive(true);
    }

    private void OnNext()
    {
        if (currentIndex >= slides.Length - 1)
        {
            Close();
            return;
        }

        currentIndex++;
        RefreshSlide();
    }

    private void RefreshSlide()
    {
        Slide slide = slides[currentIndex];
        titleText.text = slide.Title;
        bodyText.text = slide.Body;

        for (int i = 0; i < dots.Count; i++)
        {
            dots[i].color = i == currentIndex ? new Color(0.71f, 0.4f, 0.11f) : new Color(0f, 0f, 0f, 0.2f);
        }

        bool isLast = currentIndex == slides.Length - 1;
        nextLabel.text = isLast ? "Start" : "Next >";
        skipButton.gameObject.SetActive(!isLast);
    }

    private void Close()
    {
        PlayerPrefs.SetInt(SeenPrefsKey, 1);
        PlayerPrefs.Save();
        panel.SetActive(false);
    }
}
