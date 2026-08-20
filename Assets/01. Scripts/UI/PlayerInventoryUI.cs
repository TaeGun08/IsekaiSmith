using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Touch-first equipment screen - a small always-visible icon button (bottom-right edge, below the
// quest card and above the HP bar - the one remaining gap on that edge) that toggles a panel
// showing what's currently equipped plus every crafted item owned. This is a touch game, not a
// keyboard one (user report: "이 게임은 키보드를 눌러서 하는 게임이 아니라 터치를 이용해서 하는
// 게임"), so equipping/discarding needs its own tappable UI instead of a keybind or an implicit
// "best weapon auto-equips" rule.
//
// Redesigned per user feedback that the original plain list (tap-any-row-to-equip, no visible
// "what am I wearing", no way to discard clutter) didn't read as an equipment screen at all: a
// dedicated EQUIPPED card up top always shows the active weapon, and a DEL button per row opens a
// confirm dialog before deleting a stack (delete is a mistake-prone action, so it's never one tap).
// See weapon_diversity_design_v1.html §9. Self-bootstrapping singleton (GuidedTutorial-style).
public class PlayerInventoryUI : MonoBehaviour
{
    private const float RowHeight = 64f;
    private const int MaxVisibleRows = 6;

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

    private readonly Color equippedGold = new Color(1f, 0.86f, 0.4f);

    private GameObject icon;
    private GameObject panel;
    private Transform rowsContainer;
    private TMP_Text emptyLabel;
    private TMP_Text equippedNameText;
    private TMP_Text equippedStatText;
    private bool panelOpen;

    private GameObject confirmDialog;
    private TMP_Text confirmMessageText;
    private ToolInventory.Entry pendingDeleteEntry;

    // Referencing Instance is enough to bootstrap this singleton - kept as an explicit call at
    // the ResourceHUD.Start() call site for readability, same as GuidedTutorial's pattern.
    public void Activate()
    {
    }

    private bool uiBuilt;

    private void Awake()
    {
        EnsureUIBuilt();
    }

    // Guarded, idempotent build - CraftingSilhouetteUI hit a reproducible crash from Awake not
    // always finishing before its first real use (weapon_diversity_design_v1.html follow-up), so
    // every entry point into this panel (the icon's own click handler) goes through this instead
    // of trusting Awake alone to have run first.
    private void EnsureUIBuilt()
    {
        if (uiBuilt)
        {
            return;
        }

        // Flag flips AFTER a successful build, not before - see CraftingSilhouetteUI's
        // EnsureUIBuilt for why (flipping it first would permanently lock in a half-built state
        // if BuildIcon/BuildPanel throws partway through, instead of retrying).
        BuildIcon();
        BuildPanel();
        panel.SetActive(false);
        uiBuilt = true;
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
        icon = iconGO;
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
        label.text = "GEAR";
        label.fontStyle = FontStyles.Bold;
        label.color = equippedGold;

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
        panelRect.sizeDelta = new Vector2(820f, 800f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var title = MakeText(panel.transform, "Title", 34, new Vector2(0f, -30f), new Vector2(700f, 48f));
        title.text = "Equipment";
        title.fontStyle = FontStyles.Bold;

        BuildEquippedCard();

        var actionHint = MakeText(panel.transform, "ActionHint", 20, new Vector2(0f, -198f), new Vector2(760f, 28f));
        actionHint.text = "Tap a weapon to equip it - DEL to discard";
        actionHint.color = new Color(0.75f, 0.72f, 0.66f);

        Button closeButton = MakeButton(panel.transform, "CloseButton", new Vector2(0f, 40f), new Vector2(240f, 90f), "CLOSE", new Color(0.4f, 0.38f, 0.34f));
        closeButton.onClick.AddListener(TogglePanel);

        var containerGO = new GameObject("Rows", typeof(RectTransform));
        containerGO.transform.SetParent(panel.transform, false);
        var containerRect = containerGO.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 1f);
        containerRect.anchorMax = new Vector2(0.5f, 1f);
        containerRect.pivot = new Vector2(0.5f, 1f);
        containerRect.anchoredPosition = new Vector2(0f, -236f);
        containerRect.sizeDelta = new Vector2(760f, RowHeight * MaxVisibleRows);
        rowsContainer = containerGO.transform;

        emptyLabel = MakeText(panel.transform, "Empty", 26, new Vector2(0f, -420f), new Vector2(700f, 60f));
        emptyLabel.text = "Nothing crafted yet";
        emptyLabel.color = new Color(0.7f, 0.66f, 0.6f);

        BuildConfirmDialog();
    }

    // Always-visible card showing what's actually active in combat right now (EquippedWeapon.Resolve)
    // - the single biggest gap in the previous version, per user feedback: a tap-to-equip list with
    // no readout of "what am I wearing" doesn't read as an equipment screen.
    private void BuildEquippedCard()
    {
        var cardGO = new GameObject("EquippedCard", typeof(RectTransform), typeof(Image));
        cardGO.transform.SetParent(panel.transform, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 1f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.anchoredPosition = new Vector2(0f, -92f);
        cardRect.sizeDelta = new Vector2(760f, 96f);
        cardGO.GetComponent<Image>().color = new Color(equippedGold.r, equippedGold.g, equippedGold.b, 0.14f);
        var cardBorder = cardGO.AddComponent<Outline>();
        cardBorder.effectColor = new Color(equippedGold.r, equippedGold.g, equippedGold.b, 0.8f);
        cardBorder.effectDistance = new Vector2(2f, -2f);

        var equippedLabel = MakeText(cardGO.transform, "EquippedLabel", 18, new Vector2(0f, -10f), new Vector2(700f, 22f));
        equippedLabel.text = "EQUIPPED";
        equippedLabel.color = equippedGold;

        equippedNameText = MakeText(cardGO.transform, "EquippedName", 26, new Vector2(0f, -36f), new Vector2(700f, 32f));
        equippedNameText.fontStyle = FontStyles.Bold;

        equippedStatText = MakeText(cardGO.transform, "EquippedStat", 18, new Vector2(0f, -70f), new Vector2(700f, 22f));
        equippedStatText.color = new Color(0.78f, 0.75f, 0.68f);
    }

    private void BuildConfirmDialog()
    {
        confirmDialog = new GameObject("ConfirmDialog", typeof(RectTransform), typeof(Image));
        confirmDialog.transform.SetParent(panel.transform, false);
        var overlayRect = confirmDialog.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        confirmDialog.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image));
        boxGO.transform.SetParent(confirmDialog.transform, false);
        var boxRect = boxGO.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(620f, 320f);
        boxGO.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.1f, 0.98f);

        confirmMessageText = MakeText(boxGO.transform, "Message", 26, new Vector2(0f, -40f), new Vector2(560f, 120f));

        Button yesButton = MakeButton(boxGO.transform, "ConfirmYes", new Vector2(-150f, 50f), new Vector2(240f, 90f), "DELETE", new Color(0.55f, 0.22f, 0.2f));
        yesButton.onClick.AddListener(ConfirmDelete);

        Button noButton = MakeButton(boxGO.transform, "ConfirmNo", new Vector2(150f, 50f), new Vector2(240f, 90f), "CANCEL", new Color(0.4f, 0.38f, 0.34f));
        noButton.onClick.AddListener(HideConfirmDialog);

        confirmDialog.SetActive(false);
    }

    // Hidden until the tutorial's precise CRAFT succeeds (GuidedTutorial.IsEquipmentUnlocked) -
    // QUICK CRAFT output never lands in ToolInventory (sell-only, carried straight to the counter),
    // so there's genuinely nothing to equip before that, and showing it early just invites a
    // confused tap (user request: "튜토리얼 중에는 튜토리얼만 오로지 깰 수 있게").
    private void Update()
    {
        if (icon == null)
        {
            return;
        }

        bool unlocked = GuidedTutorial.IsEquipmentUnlocked;
        if (icon.activeSelf != unlocked)
        {
            icon.SetActive(unlocked);

            // Only announce it turning ON - a silent icon appearing is easy to miss entirely
            // (feedback that led to this whole gating system in the first place).
            if (unlocked)
            {
                ToastUI.Instance.Show("Equipment Unlocked!");
            }
        }
    }

    private void TogglePanel()
    {
        EnsureUIBuilt();
        panelOpen = !panelOpen;
        panel.SetActive(panelOpen);

        if (panelOpen)
        {
            HideConfirmDialog();
            RefreshEquippedCard();
            RefreshRows();
        }
    }

    private void RefreshEquippedCard()
    {
        // Nothing crafted yet -> EquippedWeapon.Resolve still returns its Iron Sword/no-element
        // placeholder (matches combat's own "unarmed = old flat baseline" fallback), but showing
        // that as "EQUIPPED: Iron Sword" when the player owns zero swords would read as a bug, not
        // a feature - gate the card on there being anything owned at all.
        if (ToolInventory.Total <= 0)
        {
            equippedNameText.text = "Nothing crafted yet";
            equippedStatText.text = "Fighting bare-handed";
            return;
        }

        EquippedWeapon.Resolve(out WeaponType weapon, out OreGrade oreGrade, out ManaElement element, out ManaGrade manaGrade);
        string elementPrefix = element != ManaElement.None ? ManaGradeUtility.DisplayName(manaGrade) + " " + ManaElementUtility.DisplayName(element) + " " : "";
        equippedNameText.text = elementPrefix + OreGradeUtility.DisplayName(oreGrade) + " " + WeaponTypeUtility.DisplayName(weapon);
        equippedStatText.text = string.Format("ATK x{0:0.0}   SPD x{1:0.0}", WeaponTypeUtility.AttackPowerMultiplier(weapon), WeaponTypeUtility.AttackIntervalMultiplier(weapon));
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
        var rowGO = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
        rowGO.transform.SetParent(rowsContainer, false);
        var rowRect = rowGO.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -index * RowHeight);
        rowRect.sizeDelta = new Vector2(760f, RowHeight - 6f);

        Color tint = entry.Element != ManaElement.None ? ManaElementUtility.SparkColor(entry.Element) : new Color(0.3f, 0.28f, 0.25f);
        rowGO.GetComponent<Image>().color = new Color(tint.r, tint.g, tint.b, 0.25f);

        // CraftGrade doesn't factor into which stack is "equipped" (EquippedWeapon ignores it -
        // it has no combat effect), so every row sharing the same (Weapon, Ore, Element, ManaGrade)
        // would show the same equipped state regardless of its own quality.
        bool isEquipped = ToolInventory.Total > 0 && EquippedWeapon.IsEquipped(entry.Weapon, entry.Ore, entry.Element, entry.ManaGrade);
        var border = rowGO.AddComponent<Outline>();
        border.effectColor = new Color(equippedGold.r, equippedGold.g, equippedGold.b, 0.9f);
        border.effectDistance = new Vector2(2f, -2f);
        border.enabled = isEquipped;

        string elementPrefix = entry.Element != ManaElement.None ? ManaGradeUtility.DisplayName(entry.ManaGrade) + " " + ManaElementUtility.DisplayName(entry.Element) + " " : "";
        string itemName = (isEquipped ? "> " : "") + elementPrefix + OreGradeUtility.DisplayName(entry.Ore) + " " + WeaponTypeUtility.DisplayName(entry.Weapon);
        string quality = CraftGradeUtility.DisplayName(entry.Craft);

        TMP_Text nameText = MakeRowText(rowGO.transform, "Name", new Vector2(20f, 0f), new Vector2(360f, RowHeight - 6f), TextAlignmentOptions.MidlineLeft);
        nameText.text = itemName + " (" + quality + ")";
        nameText.color = isEquipped ? equippedGold : Color.white;

        TMP_Text countText = MakeRowText(rowGO.transform, "Count", new Vector2(-104f, 0f), new Vector2(90f, RowHeight - 6f), TextAlignmentOptions.MidlineRight);
        countText.text = "x" + entry.Count;

        rowGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            EquippedWeapon.Equip(entry.Weapon, entry.Ore, entry.Element, entry.ManaGrade);
            RefreshEquippedCard();
            RefreshRows();
        });

        // Sits inside the row's own Button - taps land on whichever is topmost at that screen
        // point, so tapping DEL never also triggers the row's equip action.
        Button trashButton = MakeRowButton(rowGO.transform, "Trash", new Vector2(-20f, 0f), new Vector2(64f, 48f), "DEL", new Color(0.45f, 0.22f, 0.2f));
        trashButton.onClick.AddListener(() => RequestDelete(entry));
    }

    // Delete is one mistake away from losing a hard-won stack, so it always goes through a
    // confirm dialog instead of deleting on the first tap (user request: "바로 삭제가 아니라
    // 한번은 물어보고 삭제하는 걸로 하자").
    private void RequestDelete(ToolInventory.Entry entry)
    {
        pendingDeleteEntry = entry;

        string elementPrefix = entry.Element != ManaElement.None ? ManaGradeUtility.DisplayName(entry.ManaGrade) + " " + ManaElementUtility.DisplayName(entry.Element) + " " : "";
        string itemName = elementPrefix + OreGradeUtility.DisplayName(entry.Ore) + " " + WeaponTypeUtility.DisplayName(entry.Weapon) + " (" + CraftGradeUtility.DisplayName(entry.Craft) + ")";
        confirmMessageText.text = "Delete " + itemName + " x" + entry.Count + "?\nThis cannot be undone.";
        confirmDialog.SetActive(true);
    }

    private void ConfirmDelete()
    {
        ToolInventory.RemoveStack(pendingDeleteEntry.Weapon, pendingDeleteEntry.Ore, pendingDeleteEntry.Craft, pendingDeleteEntry.Element, pendingDeleteEntry.ManaGrade);
        HideConfirmDialog();
        RefreshEquippedCard();
        RefreshRows();
    }

    private void HideConfirmDialog()
    {
        confirmDialog.SetActive(false);
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

    // Row-embedded action button (the DEL button) - vertically centered like MakeRowText's
    // columns, unlike MakeButton which bottom-anchors relative to its parent (fine for panel-level
    // button rows, wrong for a button living inside a 58px-tall row).
    private static Button MakeRowButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, string text, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        bool rightAligned = anchoredPos.x < 0f;
        rect.anchorMin = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.anchorMax = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.pivot = new Vector2(rightAligned ? 1f : 0f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;

        TMP_Text label = MakeRowText(go.transform, "Text", Vector2.zero, size, TextAlignmentOptions.Center);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.fontStyle = FontStyles.Bold;
        label.text = text;

        return go.GetComponent<Button>();
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
