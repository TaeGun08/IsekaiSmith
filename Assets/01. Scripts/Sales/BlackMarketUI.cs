using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Buy/sell panel opened by BlackMarketMerchant's TRADE prompt (black_market_design_v1.html §2/§3)
// - two BUY rows (the merchant's rolled ore/mana offer) and three fixed SELL quick-sell buttons,
// instead of a full itemized inventory browser (deferred - §4 "다음 단계로 미룸"). Self-
// bootstrapping singleton, same code-generated-canvas convention as every other UI class here.
public class BlackMarketUI : MonoBehaviour
{
    private static BlackMarketUI instance;

    public static BlackMarketUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("BlackMarketUI");
                instance = go.AddComponent<BlackMarketUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text oreOfferText;
    private Button oreBuyButton;
    private TMP_Text manaOfferText;
    private Button manaBuyButton;
    private TMP_Text sellOreText;
    private Button sellOreButton;
    private TMP_Text sellManaText;
    private Button sellManaButton;
    private TMP_Text sellWoodText;
    private Button sellWoodButton;

    private void Awake()
    {
        BuildPanel();
        panel.SetActive(false);
    }

    public void Open()
    {
        panel.SetActive(true);
        Refresh();
    }

    public void CloseIfOpen()
    {
        if (panel.activeSelf)
        {
            panel.SetActive(false);
        }
    }

    private void BuildPanel()
    {
        var canvasGO = new GameObject("BlackMarketCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        panelRect.sizeDelta = new Vector2(820f, 900f);
        panel.GetComponent<Image>().color = new Color(0.1f, 0.06f, 0.12f, 0.95f); // shady purple-black, matches the merchant's own tint

        var title = MakeText(panel.transform, "Title", 34, new Vector2(0f, -30f), new Vector2(700f, 48f));
        title.text = "Black Market";
        title.fontStyle = FontStyles.Bold;

        MakeText(panel.transform, "BuyLabel", 20, new Vector2(0f, -96f), new Vector2(700f, 30f)).text = "BUY - rare stock, steep price";

        oreOfferText = MakeText(panel.transform, "OreOfferText", 24, new Vector2(-360f, -140f), new Vector2(460f, 60f));
        oreOfferText.alignment = TextAlignmentOptions.MidlineLeft;
        oreBuyButton = MakeButton(panel.transform, "OreBuyButton", new Vector2(300f, -160f), new Vector2(160f, 70f), "BUY", new Color(0.55f, 0.4f, 0.2f));
        oreBuyButton.onClick.AddListener(() =>
        {
            BlackMarketMerchant.Instance.TryBuyOre();
            Refresh();
        });

        manaOfferText = MakeText(panel.transform, "ManaOfferText", 24, new Vector2(-360f, -226f), new Vector2(460f, 60f));
        manaOfferText.alignment = TextAlignmentOptions.MidlineLeft;
        manaBuyButton = MakeButton(panel.transform, "ManaBuyButton", new Vector2(300f, -246f), new Vector2(160f, 70f), "BUY", new Color(0.55f, 0.4f, 0.2f));
        manaBuyButton.onClick.AddListener(() =>
        {
            BlackMarketMerchant.Instance.TryBuyMana();
            Refresh();
        });

        MakeText(panel.transform, "SellLabel", 20, new Vector2(0f, -340f), new Vector2(700f, 30f)).text = "SELL - quick cash, below market";

        sellOreButton = MakeButton(panel.transform, "SellOreButton", new Vector2(0f, -400f), new Vector2(600f, 80f), "", new Color(0.3f, 0.28f, 0.26f));
        sellOreText = sellOreButton.GetComponentInChildren<TMP_Text>();
        sellOreButton.onClick.AddListener(() =>
        {
            BlackMarketMerchant.Instance.TryQuickSellOre(out _);
            Refresh();
        });

        sellManaButton = MakeButton(panel.transform, "SellManaButton", new Vector2(0f, -490f), new Vector2(600f, 80f), "", new Color(0.3f, 0.28f, 0.26f));
        sellManaText = sellManaButton.GetComponentInChildren<TMP_Text>();
        sellManaButton.onClick.AddListener(() =>
        {
            BlackMarketMerchant.Instance.TryQuickSellMana(out _);
            Refresh();
        });

        sellWoodButton = MakeButton(panel.transform, "SellWoodButton", new Vector2(0f, -580f), new Vector2(600f, 80f), "", new Color(0.3f, 0.28f, 0.26f));
        sellWoodText = sellWoodButton.GetComponentInChildren<TMP_Text>();
        sellWoodButton.onClick.AddListener(() =>
        {
            BlackMarketMerchant.Instance.TryQuickSellWood(out _);
            Refresh();
        });

        // Top-anchored (0,40 would collide with the title under this file's top-anchored
        // MakeButton, unlike other UI classes' bottom-anchored button helper) - placed a fixed
        // gap below the last sell row instead.
        Button closeButton = MakeButton(panel.transform, "CloseButton", new Vector2(0f, -780f), new Vector2(240f, 90f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(CloseIfOpen);
    }

    private void Refresh()
    {
        BlackMarketMerchant merchant = BlackMarketMerchant.Instance;

        if (!merchant.IsVisiting)
        {
            CloseIfOpen();
            return;
        }

        oreOfferText.text = merchant.HasOreOffer
            ? OreGradeUtility.DisplayName(merchant.OreOfferGrade) + " Ore x" + merchant.OreOfferRemaining + "\n" + merchant.OreOfferPricePerUnit + " G each"
            : "Sold out";
        oreBuyButton.interactable = merchant.HasOreOffer && SalesCurrency.Gold >= merchant.OreOfferPricePerUnit;

        manaOfferText.text = merchant.HasManaOffer
            ? ManaGradeUtility.DisplayName(merchant.ManaOfferGrade) + " Mana x" + merchant.ManaOfferRemaining + "\n" + merchant.ManaOfferPricePerUnit + " G each"
            : "Sold out";
        manaBuyButton.interactable = merchant.HasManaOffer && SalesCurrency.Gold >= merchant.ManaOfferPricePerUnit;

        sellOreText.text = "Sell " + BlackMarketMerchant.QuickSellOreAmount + " Ore (cheapest)";
        sellOreButton.interactable = OreBank.TotalCurrent >= BlackMarketMerchant.QuickSellOreAmount;

        sellManaText.text = "Sell " + BlackMarketMerchant.QuickSellManaAmount + " Mana (cheapest)";
        sellManaButton.interactable = ManaBank.TotalCurrent >= BlackMarketMerchant.QuickSellManaAmount;

        sellWoodText.text = "Sell " + BlackMarketMerchant.QuickSellWoodAmount + " Wood";
        sellWoodButton.interactable = ResourceBank.Get(ResourceType.Wood) >= BlackMarketMerchant.QuickSellWoodAmount;
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
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;

        var button = go.GetComponent<Button>();
        // AddComponent<Button>() leaves targetGraphic null, so the built-in disabled-state dim
        // never showed - a BUY/SELL button gated by insufficient gold/stock (Refresh() below)
        // looked completely normal while silently refusing clicks (user report: "buy sell이 안눌리는
        // 것 같아"). Wiring this is what actually makes `interactable = false` visible.
        button.targetGraphic = image;

        TMP_Text label = MakeText(go.transform, "Text", 24, Vector2.zero, size);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = text;
        label.fontStyle = FontStyles.Bold;

        return button;
    }
}
