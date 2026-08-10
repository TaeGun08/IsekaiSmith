using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Final-form onboarding, replacing TutorialUI's read-and-skip slideshow: a single welcome card,
// then a top banner + a floor arrow that points at the next objective and auto-advances the
// moment the player actually does it (no "tap to continue"). Self-contained runtime UI, same
// pattern as every other UI class in this project. See guided_tutorial_design.html.
public class GuidedTutorial : MonoBehaviour
{
    private const string SeenPrefsKey = "GuidedTutorialSeen";

    private static GuidedTutorial instance;

    public static GuidedTutorial Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GuidedTutorial");
                instance = go.AddComponent<GuidedTutorial>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private enum Step
    {
        Welcome,
        Move,
        GatherWood,
        GatherOre,
        Craft,
        Sell,
        Done
    }

    // Raised from "just 1" per user feedback - collecting a single item didn't feel like enough
    // of an accomplishment to advance on.
    private const int WoodGatherTarget = 4;
    private const int OreGatherTarget = 4;

    private Step step = Step.Welcome;
    private bool running;

    private GameObject welcomePanel;
    private GameObject bannerRoot;
    private TMP_Text bannerText;
    private Transform arrowRoot;

    private Transform lumberCamp;
    private Transform quarry;
    private Transform smithy;
    private Transform salesCounter;

    private int lastWood;
    private int lastOre;
    private int lastTool;
    private int lastGold;

    private void Awake()
    {
        BuildWelcomeCard();
        BuildBanner();
        BuildArrow();
        BuildHelpButton();
        welcomePanel.SetActive(false);
        bannerRoot.SetActive(false);
        arrowRoot.gameObject.SetActive(false);
    }

    // Called once from ResourceHUD.Start() - see TutorialUI's old comment for why that
    // particular class is the bootstrap point (it's just the one always-present component).
    public void ShowIfFirstTime()
    {
        if (PlayerPrefs.GetInt(SeenPrefsKey, 0) != 0)
        {
            return;
        }

        Begin();
    }

    public void Begin()
    {
        lumberCamp = FindTransform("LumberCamp");
        quarry = FindTransform("Quarry");
        smithy = FindTransform("Smithy");
        salesCounter = FindTransform("SalesCounter");

        step = Step.Welcome;
        running = true;
        bannerRoot.SetActive(false);
        arrowRoot.gameObject.SetActive(false);
        welcomePanel.SetActive(true);
    }

    private static Transform FindTransform(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go.transform : null;
    }

    private void OnWelcomeStart()
    {
        welcomePanel.SetActive(false);
        bannerRoot.SetActive(true);
        EnterStep(Step.Move);
    }

    private void EnterStep(Step next)
    {
        step = next;

        switch (step)
        {
            case Step.Move:
                bannerText.text = "Drag anywhere on screen to move";
                break;
            case Step.GatherWood:
                break; // text set every frame in Update() so it can show live progress
            case Step.GatherOre:
                break; // text set every frame in Update() so it can show live progress
            case Step.Craft:
                bannerText.text = "Craft a tool at the Smithy";
                break;
            case Step.Sell:
                bannerText.text = "Sell it at the counter";
                break;
            case Step.Done:
                bannerText.text = "You're all set - have fun!";
                arrowRoot.gameObject.SetActive(false);
                PlayerPrefs.SetInt(SeenPrefsKey, 1);
                PlayerPrefs.Save();
                running = false;
                Invoke(nameof(HideBanner), 3f);
                break;
        }

        lastWood = ResourceBank.Get(ResourceType.Wood);
        lastOre = ResourceBank.Get(ResourceType.Ore);
        lastTool = ToolInventory.Total;
        lastGold = SalesCurrency.Gold;
    }

    private void HideBanner()
    {
        bannerRoot.SetActive(false);
    }

    private void Update()
    {
        if (!running)
        {
            return;
        }

        UpdateArrow();

        switch (step)
        {
            case Step.Move:
                if (PlayerMotor.Instance != null && PlayerMotor.Instance.HasMovementInput)
                {
                    EnterStep(Step.GatherWood);
                }
                break;
            case Step.GatherWood:
            {
                int gathered = Mathf.Clamp(ResourceBank.Get(ResourceType.Wood) - lastWood, 0, WoodGatherTarget);
                bannerText.text = "Gather wood at the Lumber Camp (" + gathered + "/" + WoodGatherTarget + ")";
                if (gathered >= WoodGatherTarget)
                {
                    EnterStep(Step.GatherOre);
                }
                break;
            }
            case Step.GatherOre:
            {
                int gathered = Mathf.Clamp(ResourceBank.Get(ResourceType.Ore) - lastOre, 0, OreGatherTarget);
                bannerText.text = "Gather ore at the Quarry (" + gathered + "/" + OreGatherTarget + ")";
                if (gathered >= OreGatherTarget)
                {
                    EnterStep(Step.Craft);
                }
                break;
            }
            case Step.Craft:
                if (ToolInventory.Total > lastTool)
                {
                    EnterStep(Step.Sell);
                }
                break;
            case Step.Sell:
                if (SalesCurrency.Gold > lastGold)
                {
                    EnterStep(Step.Done);
                }
                break;
        }
    }

    private void UpdateArrow()
    {
        Transform target = null;
        switch (step)
        {
            case Step.GatherWood: target = lumberCamp; break;
            case Step.GatherOre: target = quarry; break;
            case Step.Craft: target = smithy; break;
            case Step.Sell: target = salesCounter; break;
        }

        if (target == null || PlayerMotor.Instance == null)
        {
            arrowRoot.gameObject.SetActive(false);
            return;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Vector3 toTarget = target.position - playerPos;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.04f)
        {
            arrowRoot.gameObject.SetActive(false);
            return;
        }

        arrowRoot.gameObject.SetActive(true);
        Vector3 direction = toTarget.normalized;
        arrowRoot.position = playerPos + direction * 1.6f + Vector3.up * 1.3f;
        arrowRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private void BuildWelcomeCard()
    {
        var canvasGO = new GameObject("GuidedTutorialWelcomeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        welcomePanel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        welcomePanel.transform.SetParent(canvasGO.transform, false);
        var panelRect = welcomePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        welcomePanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(welcomePanel.transform, false);
        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(860f, 620f);
        card.GetComponent<Image>().color = new Color(0.98f, 0.93f, 0.85f, 0.98f);

        var title = MakeText(card.transform, "Title", 40, new Vector2(0f, -80f), new Vector2(760f, 60f));
        title.color = new Color(0.16f, 0.13f, 0.1f);
        title.fontStyle = FontStyles.Bold;
        title.text = "Isekai Smith";

        var body = MakeText(card.transform, "Body", 26, new Vector2(0f, -200f), new Vector2(700f, 260f));
        body.color = new Color(0.25f, 0.21f, 0.17f);
        body.text = "Rebuild a blacksmith's forge in a ruined world.\n\nFollow the arrow on the ground -\nit'll show you where to go next.";

        Button startButton = MakeButton(card.transform, "StartButton", new Vector2(0f, 70f), new Vector2(280f, 110f), "Start", new Color(0.71f, 0.4f, 0.11f));
        startButton.onClick.AddListener(OnWelcomeStart);
    }

    private void BuildBanner()
    {
        var canvasGO = new GameObject("GuidedTutorialBannerCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        bannerRoot = new GameObject("Banner", typeof(RectTransform), typeof(Image));
        bannerRoot.transform.SetParent(canvasGO.transform, false);
        var bannerRect = bannerRoot.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -40f);
        bannerRect.sizeDelta = new Vector2(900f, 90f);
        bannerRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);

        bannerText = MakeText(bannerRoot.transform, "Text", 30, Vector2.zero, new Vector2(860f, 90f));
        bannerText.rectTransform.anchorMin = Vector2.zero;
        bannerText.rectTransform.anchorMax = Vector2.one;
        bannerText.rectTransform.offsetMin = Vector2.zero;
        bannerText.rectTransform.offsetMax = Vector2.zero;
        bannerText.color = new Color(1f, 0.86f, 0.5f);
        bannerText.fontStyle = FontStyles.Bold;
    }

    // Plain placeholder pointer (a single bright bar, like a compass needle) rather than a real
    // arrow mesh/sprite - matches this project's current art bar (Player is a bare capsule).
    // Points its local +Z at the target via LookRotation, floating just ahead of the player.
    private void BuildArrow()
    {
        var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arrow.name = "GuidedTutorialArrow";
        Destroy(arrow.GetComponent<Collider>());
        arrow.transform.localScale = new Vector3(0.18f, 0.18f, 1.4f);
        var renderer = arrow.GetComponent<MeshRenderer>();
        renderer.material.color = new Color(1f, 0.85f, 0.25f);
        arrowRoot = arrow.transform;
    }

    private void BuildHelpButton()
    {
        var canvasGO = new GameObject("GuidedTutorialHelpCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        // Bottom-left, matching where TutorialUI's help button used to live (top-right is
        // ResourceHUD's resource panel, top-left is DevAutoPlayController's debug buttons).
        Button helpButton = MakeButton(canvasGO.transform, "HelpButton", new Vector2(24f, 24f), new Vector2(96f, 96f), "?", new Color(0f, 0f, 0f, 0.5f));
        var helpRect = helpButton.GetComponent<RectTransform>();
        helpRect.anchorMin = new Vector2(0f, 0f);
        helpRect.anchorMax = new Vector2(0f, 0f);
        helpRect.pivot = new Vector2(0f, 0f);
        helpButton.onClick.AddListener(Begin);
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
}
