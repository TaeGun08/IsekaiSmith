using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Touch-first inventory viewer - a small always-visible icon button (bottom-right edge, below the
// quest card and above the HP bar - the one remaining gap on that edge) that toggles a panel
// listing every crafted item currently owned. This is a touch game, not a keyboard one (user
// report: "이 게임은 키보드를 눌러서 하는 게임이 아니라 터치를 이용해서 하는 게임"), so
// inventory needs its own tappable entry point instead of a keybind. Self-bootstrapping singleton
// (GuidedTutorial-style). See weapon_diversity_design_v1.html follow-up notes.
public class PlayerInventoryUI : MonoBehaviour
{
    private const float RowHeight = 64f;
    private const int MaxVisibleRows = 8;

    private static PlayerInventoryUI instance;

    public static PlayerInventoryUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("PlayerInventoryUI");
                instance = go.AddComponent<PlayerInventoryUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private GameObject panel;
    private Transform rowsContainer;
    private TMP_Text emptyLabel;
    private bool panelOpen;

    // Referencing Instance is enough to bootstrap this singleton - kept as an explicit call at
    // the ResourceHUD.Start() call site for readability, same as GuidedTutorial's pattern.
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
        var canvasGO = new GameObject("PlayerInventoryIconCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        // Right edge, in the gap between the quest card (spans roughly -88..-192 from vertical
        // center) and the HP bar (bottom-right corner) - the only free spot left on that edge,
        // computed from those two elements' real anchored positions rather than eyeballed.
        var iconGO = new GameObject("InventoryIcon", typeof(RectTransform), typeof(Image), typeof(Button));
        iconGO.transform.SetParent(canvasGO.transform, false);
        var rect = iconGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-24f, -256f);
        rect.sizeDelta = new Vector2(88f, 88f);
        iconGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var label = MakeText(iconGO.transform, "Label", 22, Vector2.zero, new Vector2(88f, 88f));
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = "BAG";
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, 0.86f, 0.5f);

        iconGO.GetComponent<Button>().onClick.AddListener(TogglePanel);
    }

    private void BuildPanel()
    {
        var canvasGO = new GameObject("PlayerInventoryPanelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30; // above the quest banner (25), below welcome/help modals (50)
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
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var title = MakeText(panel.transform, "Title", 34, new Vector2(0f, -30f), new Vector2(700f, 48f));
        title.text = "Inventory";
        title.fontStyle = FontStyles.Bold;

        Button closeButton = MakeButton(panel.transform, "CloseButton", new Vector2(0f, 40f), new Vector2(240f, 90f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(TogglePanel);

        var containerGO = new GameObject("Rows", typeof(RectTransform));
        containerGO.transform.SetParent(panel.transform, false);
        var containerRect = containerGO.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 1f);
        containerRect.anchorMax = new Vector2(0.5f, 1f);
        containerRect.pivot = new Vector2(0.5f, 1f);
        containerRect.anchoredPosition = new Vector2(0f, -100f);
        containerRect.sizeDelta = new Vector2(760f, RowHeight * MaxVisibleRows);
        rowsContainer = containerGO.transform;

        emptyLabel = MakeText(panel.transform, "Empty", 26, new Vector2(0f, -400f), new Vector2(700f, 60f));
        emptyLabel.text = "Nothing crafted yet";
        emptyLabel.color = new Color(0.7f, 0.66f, 0.6f);
    }

    private void TogglePanel()
    {
        panelOpen = !panelOpen;
        panel.SetActive(panelOpen);

        if (panelOpen)
        {
            RefreshRows();
        }
    }

    private void RefreshRows()
    {
        foreach (Transform child in rowsContainer)
        {
            Destroy(child.gameObject);
        }

        int index = 0;
        bool any = false;

        foreach (ToolInventory.Entry entry in ToolInventory.AllOwned())
        {
            any = true;
            if (index >= MaxVisibleRows)
            {
                break;
            }

            BuildRow(index, entry);
            index++;
        }

        emptyLabel.gameObject.SetActive(!any);
    }

    private void BuildRow(int index, ToolInventory.Entry entry)
    {
        var rowGO = new GameObject("Row", typeof(RectTransform), typeof(Image));
        rowGO.transform.SetParent(rowsContainer, false);
        var rowRect = rowGO.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -index * RowHeight);
        rowRect.sizeDelta = new Vector2(760f, RowHeight - 6f);

        Color tint = entry.Element != ManaElement.None ? ManaElementUtility.SparkColor(entry.Element) : new Color(0.3f, 0.28f, 0.25f);
        rowGO.GetComponent<Image>().color = new Color(tint.r, tint.g, tint.b, 0.25f);

        string elementPrefix = entry.Element != ManaElement.None ? ManaElementUtility.DisplayName(entry.Element) + " " : "";
        string itemName = elementPrefix + OreGradeUtility.DisplayName(entry.Ore) + " " + WeaponTypeUtility.DisplayName(entry.Weapon);
        string quality = CraftGradeUtility.DisplayName(entry.Craft);

        TMP_Text nameText = MakeRowText(rowGO.transform, "Name", new Vector2(20f, 0f), new Vector2(460f, RowHeight - 6f), TextAlignmentOptions.MidlineLeft);
        nameText.text = itemName + " (" + quality + ")";

        TMP_Text countText = MakeRowText(rowGO.transform, "Count", new Vector2(-20f, 0f), new Vector2(140f, RowHeight - 6f), TextAlignmentOptions.MidlineRight);
        countText.text = "x" + entry.Count;
    }

    // Anchored to the row's left/right edge, vertical middle - used for the name/count columns.
    private static TMP_Text MakeRowText(Transform parent, string name, Vector2 anchoredPos, Vector2 size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        bool rightAligned = anchoredPos.x < 0f;
        rect.anchorMin = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.anchorMax = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.pivot = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 24;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        return tmp;
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
