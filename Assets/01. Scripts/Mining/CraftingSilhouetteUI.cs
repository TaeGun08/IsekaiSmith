using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Weapon-silhouette material assembly panel. Fixed slots (blade ore, up to 3 blade mana, handle
// wood) are filled by tapping the slot then picking from a bottom sheet listing every owned
// material in that slot's category (MaterialCategoryUtility) - no drag-and-drop. New ore
// tiers/mana elements/wood types just show up in the sheet automatically since it's built from
// the category, not a hardcoded chip list, and there's no small drop-zone to miss on a touch
// screen (see crafting_design_v3.html §3/§7).
public class CraftingSilhouetteUI : MonoBehaviour
{
    private const int ManaSlotCount = 3;

    private static CraftingSilhouetteUI instance;

    public static CraftingSilhouetteUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("CraftingSilhouetteUI");
                instance = go.AddComponent<CraftingSilhouetteUI>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private class MaterialSlot
    {
        public MaterialCategory Category;
        public ResourceType? Filled;
        public Image SlotImage;
        public TMP_Text SlotLabel;
    }

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text hintText;

    private Image bladeImage;
    private Image handleImage;
    private readonly Color bladeBaseColor = new Color(0.3f, 0.32f, 0.36f);
    private readonly Color bladeOreColor = new Color(0.55f, 0.58f, 0.63f);
    private readonly Color bladeManaColor = new Color(0.6f, 0.3f, 0.55f);
    private readonly Color handleBaseColor = new Color(0.26f, 0.22f, 0.18f);
    private readonly Color handleWoodColor = new Color(0.55f, 0.4f, 0.24f);

    private readonly Color slotEmptyColor = new Color(1f, 1f, 1f, 0.08f);
    private readonly Color oreFilledColor = new Color(0.55f, 0.58f, 0.63f);
    private readonly Color manaFilledColor = new Color(0.69f, 0.27f, 0.56f);
    private readonly Color woodFilledColor = new Color(0.55f, 0.42f, 0.28f);

    private MaterialSlot oreSlot;
    private readonly MaterialSlot[] manaSlots = new MaterialSlot[ManaSlotCount];
    private MaterialSlot woodSlot;

    private GameObject sheet;
    private TMP_Text sheetTitleText;
    private Transform sheetGrid;

    private Button forgeButton;
    private Button cancelButton;

    private bool started;
    private bool cancelled;

    private void Awake()
    {
        BuildUI();
        panel.SetActive(false);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("CraftingSilhouetteCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
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
        panelRect.sizeDelta = new Vector2(980f, 1700f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        titleText = MakeText(panel.transform, "Title", 34, new Vector2(0f, -46f), new Vector2(880f, 48f));
        hintText = MakeText(panel.transform, "Hint", 20, new Vector2(0f, -104f), new Vector2(880f, 36f));
        hintText.text = "Tap a slot to choose a material";
        hintText.color = new Color(0.75f, 0.72f, 0.68f);

        float[] slotX = { -300f, -150f, 0f, 150f, 300f };
        oreSlot = BuildSlot(panel.transform, MaterialCategory.Ore, slotX[0], -170f, "Ore");
        manaSlots[0] = BuildSlot(panel.transform, MaterialCategory.ManaStone, slotX[1], -170f, "Mana");
        manaSlots[1] = BuildSlot(panel.transform, MaterialCategory.ManaStone, slotX[2], -170f, "Mana");
        manaSlots[2] = BuildSlot(panel.transform, MaterialCategory.ManaStone, slotX[3], -170f, "Mana");
        woodSlot = BuildSlot(panel.transform, MaterialCategory.Wood, slotX[4], -170f, "Wood");

        // Blade/handle silhouette - decorative, reflects what's been slotted in with a tint.
        var bladeGO = new GameObject("BladeVisual", typeof(RectTransform), typeof(Image));
        bladeGO.transform.SetParent(panel.transform, false);
        var bladeRect = bladeGO.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0.5f, 1f);
        bladeRect.anchorMax = new Vector2(0.5f, 1f);
        bladeRect.pivot = new Vector2(0.5f, 1f);
        bladeRect.anchoredPosition = new Vector2(0f, -340f);
        bladeRect.sizeDelta = new Vector2(170f, 700f);
        bladeImage = bladeGO.GetComponent<Image>();
        bladeImage.color = bladeBaseColor;
        MakeText(bladeGO.transform, "Label", 18, new Vector2(0f, 24f), new Vector2(200f, 28f)).text = "BLADE";

        var guardGO = new GameObject("Guard", typeof(RectTransform), typeof(Image));
        guardGO.transform.SetParent(panel.transform, false);
        var guardRect = guardGO.GetComponent<RectTransform>();
        guardRect.anchorMin = new Vector2(0.5f, 1f);
        guardRect.anchorMax = new Vector2(0.5f, 1f);
        guardRect.pivot = new Vector2(0.5f, 1f);
        guardRect.anchoredPosition = new Vector2(0f, -1040f);
        guardRect.sizeDelta = new Vector2(300f, 22f);
        guardGO.GetComponent<Image>().color = new Color(0.4f, 0.33f, 0.22f);

        var handleGO = new GameObject("HandleVisual", typeof(RectTransform), typeof(Image));
        handleGO.transform.SetParent(panel.transform, false);
        var handleRect = handleGO.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 1f);
        handleRect.anchorMax = new Vector2(0.5f, 1f);
        handleRect.pivot = new Vector2(0.5f, 1f);
        handleRect.anchoredPosition = new Vector2(0f, -1066f);
        handleRect.sizeDelta = new Vector2(110f, 240f);
        handleImage = handleGO.GetComponent<Image>();
        handleImage.color = handleBaseColor;
        MakeText(handleGO.transform, "Label", 18, new Vector2(0f, 24f), new Vector2(200f, 28f)).text = "HANDLE";

        forgeButton = MakeButton(panel.transform, "ForgeButton", new Vector2(-160f, 60f), new Vector2(280f, 90f), "FORGE", new Color(0.35f, 0.6f, 0.35f));
        cancelButton = MakeButton(panel.transform, "CancelButton", new Vector2(160f, 60f), new Vector2(280f, 90f), "CANCEL", new Color(0.5f, 0.3f, 0.28f));

        BuildSheet();
        sheet.SetActive(false);
    }

    private void BuildSheet()
    {
        sheet = new GameObject("Sheet", typeof(RectTransform), typeof(Image));
        sheet.transform.SetParent(panel.transform, false);
        var sheetRect = sheet.GetComponent<RectTransform>();
        sheetRect.anchorMin = new Vector2(0.5f, 0f);
        sheetRect.anchorMax = new Vector2(0.5f, 0f);
        sheetRect.pivot = new Vector2(0.5f, 0f);
        sheetRect.anchoredPosition = Vector2.zero;
        sheetRect.sizeDelta = new Vector2(980f, 480f);
        sheet.GetComponent<Image>().color = new Color(0.12f, 0.11f, 0.09f, 0.98f);

        sheetTitleText = MakeText(sheet.transform, "SheetTitle", 24, new Vector2(0f, -24f), new Vector2(880f, 40f));

        var gridGO = new GameObject("Grid", typeof(RectTransform));
        gridGO.transform.SetParent(sheet.transform, false);
        var gridRect = gridGO.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 1f);
        gridRect.anchorMax = new Vector2(0.5f, 1f);
        gridRect.pivot = new Vector2(0.5f, 1f);
        gridRect.anchoredPosition = new Vector2(0f, -84f);
        gridRect.sizeDelta = new Vector2(900f, 300f);
        sheetGrid = gridGO.transform;

        var closeButton = MakeButton(sheet.transform, "SheetClose", new Vector2(0f, 30f), new Vector2(220f, 64f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(CloseSheet);
    }

    private MaterialSlot BuildSlot(Transform parent, MaterialCategory category, float x, float y, string label)
    {
        var go = new GameObject("Slot_" + category, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(130f, 130f);
        var image = go.GetComponent<Image>();
        image.color = slotEmptyColor;

        var text = MakeText(go.transform, "Label", 16, Vector2.zero, new Vector2(130f, 130f));
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        text.text = label;

        var slot = new MaterialSlot
        {
            Category = category,
            Filled = null,
            SlotImage = image,
            SlotLabel = text
        };

        go.GetComponent<Button>().onClick.AddListener(() => OpenSheet(slot));
        return slot;
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

        var label = MakeText(go.transform, "Text", 22, Vector2.zero, size);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        label.text = text;

        return go.GetComponent<Button>();
    }

    public IEnumerator RunSilhouette(string title, Action<bool, int> onComplete)
    {
        titleText.text = title;
        started = false;
        cancelled = false;

        AssignSlot(oreSlot, null);
        AssignSlot(woodSlot, null);
        for (int i = 0; i < manaSlots.Length; i++)
        {
            AssignSlot(manaSlots[i], null);
        }

        sheet.SetActive(false);
        forgeButton.gameObject.SetActive(true);
        cancelButton.gameObject.SetActive(true);

        forgeButton.onClick.RemoveAllListeners();
        forgeButton.onClick.AddListener(() => { if (oreSlot.Filled.HasValue && woodSlot.Filled.HasValue) started = true; });

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(() => cancelled = true);

        panel.SetActive(true);

        while (!started && !cancelled)
        {
            forgeButton.interactable = oreSlot.Filled.HasValue && woodSlot.Filled.HasValue;
            yield return null;
        }

        panel.SetActive(false);
        onComplete?.Invoke(started, CountFilledMana());
    }

    private void OpenSheet(MaterialSlot slot)
    {
        sheetTitleText.text = "Choose " + MaterialCategoryUtility.DisplayName(slot.Category);

        foreach (Transform child in sheetGrid)
        {
            Destroy(child.gameObject);
        }

        const int chipsPerRow = 4;
        const float chipSize = 190f;
        const float chipGap = 20f;
        const float rowHeight = 130f;
        float rowWidth = chipsPerRow * chipSize + (chipsPerRow - 1) * chipGap;
        float startX = -rowWidth * 0.5f + chipSize * 0.5f;
        int index = 0;

        if (slot.Filled.HasValue)
        {
            BuildSheetChip(index, startX, chipSize, chipGap, rowHeight, chipsPerRow, "Remove", new Color(0.45f, 0.24f, 0.22f),
                () => { AssignSlot(slot, null); CloseSheet(); });
            index++;
        }

        foreach (ResourceType type in MaterialCategoryUtility.AllInCategory(slot.Category))
        {
            int owned = ResourceBank.Get(type);
            if (owned <= 0 && slot.Filled != type)
            {
                continue;
            }

            ResourceType capturedType = type;
            string label = MaterialCategoryUtility.DisplayName(type) + "\nx" + owned;
            BuildSheetChip(index, startX, chipSize, chipGap, rowHeight, chipsPerRow, label, ChipColor(slot.Category),
                () => { AssignSlot(slot, capturedType); CloseSheet(); });
            index++;
        }

        forgeButton.gameObject.SetActive(false);
        cancelButton.gameObject.SetActive(false);
        sheet.SetActive(true);
    }

    private void BuildSheetChip(int index, float startX, float chipSize, float chipGap, float rowHeight, int chipsPerRow, string label, Color color, Action onClick)
    {
        int row = index / chipsPerRow;
        int col = index % chipsPerRow;
        float x = startX + col * (chipSize + chipGap);
        float y = -row * rowHeight;

        var go = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(sheetGrid, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(chipSize, rowHeight - chipGap);
        go.GetComponent<Image>().color = color;

        var text = MakeText(go.transform, "Label", 18, Vector2.zero, new Vector2(chipSize, rowHeight - chipGap));
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        text.text = label;

        go.GetComponent<Button>().onClick.AddListener(() => onClick());
    }

    private void CloseSheet()
    {
        sheet.SetActive(false);
        forgeButton.gameObject.SetActive(true);
        cancelButton.gameObject.SetActive(true);
        forgeButton.interactable = oreSlot.Filled.HasValue && woodSlot.Filled.HasValue;
    }

    private void AssignSlot(MaterialSlot slot, ResourceType? type)
    {
        slot.Filled = type;
        UpdateSlotVisual(slot);
        UpdateBladeVisual();
        UpdateHandleVisual();
    }

    private void UpdateSlotVisual(MaterialSlot slot)
    {
        if (slot.Filled.HasValue)
        {
            slot.SlotImage.color = ChipColor(slot.Category);
            slot.SlotLabel.text = MaterialCategoryUtility.DisplayName(slot.Filled.Value);
        }
        else
        {
            slot.SlotImage.color = slotEmptyColor;
            slot.SlotLabel.text = MaterialCategoryUtility.DisplayName(slot.Category);
        }
    }

    private Color ChipColor(MaterialCategory category)
    {
        switch (category)
        {
            case MaterialCategory.Ore:
                return oreFilledColor;
            case MaterialCategory.ManaStone:
                return manaFilledColor;
            case MaterialCategory.Wood:
                return woodFilledColor;
            default:
                return Color.white;
        }
    }

    private void UpdateBladeVisual()
    {
        Color color = bladeBaseColor;

        if (oreSlot.Filled.HasValue)
        {
            color = bladeOreColor;
        }

        int manaCount = CountFilledMana();
        if (manaCount > 0)
        {
            color = Color.Lerp(color, bladeManaColor, Mathf.Clamp01(manaCount / (float)ManaSlotCount) * 0.7f);
        }

        bladeImage.color = color;
    }

    private void UpdateHandleVisual()
    {
        handleImage.color = woodSlot.Filled.HasValue ? handleWoodColor : handleBaseColor;
    }

    private int CountFilledMana()
    {
        int count = 0;

        for (int i = 0; i < manaSlots.Length; i++)
        {
            if (manaSlots[i].Filled.HasValue)
            {
                count++;
            }
        }

        return count;
    }
}
