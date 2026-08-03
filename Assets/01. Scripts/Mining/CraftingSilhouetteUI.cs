using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Weapon-silhouette material assembly panel (replaces the old abstract slot grid).
// Blade zone accepts Ore (required) + Mana Stone (optional, up to a cap) - dropping either
// tints the blade. Handle zone accepts Wood (required) - tints the handle. FORGE enables once
// both required zones are filled. Drag/drop works identically for mouse and touch.
public class CraftingSilhouetteUI : MonoBehaviour
{
    private const int ManaMax = 3;

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

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text hintText;

    private Image bladeImage;
    private Image handleImage;
    private TMP_Text bladeLabelText;
    private TMP_Text handleLabelText;
    private readonly Color bladeBaseColor = new Color(0.3f, 0.32f, 0.36f);
    private readonly Color bladeOreColor = new Color(0.55f, 0.58f, 0.63f);
    private readonly Color bladeManaColor = new Color(0.6f, 0.3f, 0.55f);
    private readonly Color handleBaseColor = new Color(0.26f, 0.22f, 0.18f);
    private readonly Color handleWoodColor = new Color(0.55f, 0.4f, 0.24f);

    private Transform tray;
    private Button forgeButton;
    private Button cancelButton;

    private bool oreFilled;
    private bool woodFilled;
    private int manaCount;

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
        hintText.text = "Drag materials onto the blade and handle";
        hintText.color = new Color(0.75f, 0.72f, 0.68f);

        // Blade zone (tall, upper)
        var bladeGO = new GameObject("BladeZone", typeof(RectTransform), typeof(Image), typeof(CraftingDropZone));
        bladeGO.transform.SetParent(panel.transform, false);
        var bladeRect = bladeGO.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0.5f, 1f);
        bladeRect.anchorMax = new Vector2(0.5f, 1f);
        bladeRect.pivot = new Vector2(0.5f, 1f);
        bladeRect.anchoredPosition = new Vector2(0f, -170f);
        bladeRect.sizeDelta = new Vector2(170f, 760f);
        bladeImage = bladeGO.GetComponent<Image>();
        bladeImage.color = bladeBaseColor;
        bladeGO.GetComponent<CraftingDropZone>().OnDropped = OnBladeDrop;
        bladeLabelText = MakeText(bladeGO.transform, "Label", 20, new Vector2(0f, 30f), new Vector2(300f, 32f));
        bladeLabelText.text = "BLADE (Ore)";

        // Guard bar (decorative)
        var guardGO = new GameObject("Guard", typeof(RectTransform), typeof(Image));
        guardGO.transform.SetParent(panel.transform, false);
        var guardRect = guardGO.GetComponent<RectTransform>();
        guardRect.anchorMin = new Vector2(0.5f, 1f);
        guardRect.anchorMax = new Vector2(0.5f, 1f);
        guardRect.pivot = new Vector2(0.5f, 1f);
        guardRect.anchoredPosition = new Vector2(0f, -930f);
        guardRect.sizeDelta = new Vector2(300f, 22f);
        guardGO.GetComponent<Image>().color = new Color(0.4f, 0.33f, 0.22f);

        // Handle zone (short, lower)
        var handleGO = new GameObject("HandleZone", typeof(RectTransform), typeof(Image), typeof(CraftingDropZone));
        handleGO.transform.SetParent(panel.transform, false);
        var handleRect = handleGO.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 1f);
        handleRect.anchorMax = new Vector2(0.5f, 1f);
        handleRect.pivot = new Vector2(0.5f, 1f);
        handleRect.anchoredPosition = new Vector2(0f, -956f);
        handleRect.sizeDelta = new Vector2(110f, 260f);
        handleImage = handleGO.GetComponent<Image>();
        handleImage.color = handleBaseColor;
        handleGO.GetComponent<CraftingDropZone>().OnDropped = OnHandleDrop;
        handleLabelText = MakeText(handleGO.transform, "Label", 20, new Vector2(0f, 30f), new Vector2(300f, 32f));
        handleLabelText.text = "HANDLE (Wood)";

        var trayGO = new GameObject("Tray", typeof(RectTransform));
        trayGO.transform.SetParent(panel.transform, false);
        var trayRect = trayGO.GetComponent<RectTransform>();
        trayRect.anchorMin = new Vector2(0.5f, 0f);
        trayRect.anchorMax = new Vector2(0.5f, 0f);
        trayRect.pivot = new Vector2(0.5f, 0f);
        trayRect.anchoredPosition = new Vector2(0f, 200f);
        trayRect.sizeDelta = new Vector2(900f, 220f);
        tray = trayGO.transform;

        forgeButton = MakeButton(panel.transform, "ForgeButton", new Vector2(-160f, 60f), new Vector2(280f, 90f), "FORGE", new Color(0.35f, 0.6f, 0.35f));
        cancelButton = MakeButton(panel.transform, "CancelButton", new Vector2(160f, 60f), new Vector2(280f, 90f), "CANCEL", new Color(0.5f, 0.3f, 0.28f));
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

    private GameObject BuildChip(ResourceType type, string label, Vector2 anchoredPos)
    {
        var go = new GameObject("Chip_" + type, typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(CraftingDragChip));
        go.transform.SetParent(tray, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(190f, 150f);

        Color chipColor = type == ResourceType.Ore ? new Color(0.48f, 0.46f, 0.43f)
            : type == ResourceType.ManaStone ? new Color(0.69f, 0.27f, 0.56f)
            : new Color(0.55f, 0.42f, 0.28f);
        go.GetComponent<Image>().color = chipColor;

        var text = MakeText(go.transform, "Label", 20, Vector2.zero, new Vector2(190f, 150f));
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        text.text = label;

        go.GetComponent<CraftingDragChip>().ResourceType = type;
        return go;
    }

    public IEnumerator RunSilhouette(string title, Action<bool, int> onComplete)
    {
        titleText.text = title;
        started = false;
        cancelled = false;
        oreFilled = false;
        woodFilled = false;
        manaCount = 0;

        bladeImage.color = bladeBaseColor;
        handleImage.color = handleBaseColor;
        bladeLabelText.text = "BLADE (Ore)";
        handleLabelText.text = "HANDLE (Wood)";

        foreach (Transform child in tray)
        {
            Destroy(child.gameObject);
        }

        BuildChip(ResourceType.Ore, "Ore\nx" + ResourceBank.Get(ResourceType.Ore), new Vector2(-280f, -20f));
        BuildChip(ResourceType.ManaStone, "Mana Stone\nx" + ResourceBank.Get(ResourceType.ManaStone), new Vector2(0f, -20f));
        BuildChip(ResourceType.Wood, "Wood\nx" + ResourceBank.Get(ResourceType.Wood), new Vector2(280f, -20f));

        forgeButton.onClick.RemoveAllListeners();
        forgeButton.onClick.AddListener(() => { if (oreFilled && woodFilled) started = true; });

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(() => cancelled = true);

        panel.SetActive(true);

        while (!started && !cancelled)
        {
            forgeButton.interactable = oreFilled && woodFilled;
            yield return null;
        }

        panel.SetActive(false);
        onComplete?.Invoke(started, manaCount);
    }

    private void OnBladeDrop(ResourceType type)
    {
        if (type == ResourceType.Ore && !oreFilled && ResourceBank.Get(ResourceType.Ore) > 0)
        {
            oreFilled = true;
            UpdateBladeVisual();
        }
        else if (type == ResourceType.ManaStone && manaCount < ManaMax && ResourceBank.Get(ResourceType.ManaStone) > manaCount)
        {
            manaCount++;
            UpdateBladeVisual();
        }
    }

    private void OnHandleDrop(ResourceType type)
    {
        if (type == ResourceType.Wood && !woodFilled && ResourceBank.Get(ResourceType.Wood) > 0)
        {
            woodFilled = true;
            UpdateHandleVisual();
        }
    }

    private void UpdateBladeVisual()
    {
        Color color = bladeBaseColor;

        if (oreFilled)
        {
            color = bladeOreColor;
        }

        if (manaCount > 0)
        {
            color = Color.Lerp(color, bladeManaColor, Mathf.Clamp01(manaCount / (float)ManaMax) * 0.7f);
        }

        bladeImage.color = color;
        bladeLabelText.text = oreFilled ? "BLADE - READY" : "BLADE (Ore)";
    }

    private void UpdateHandleVisual()
    {
        handleImage.color = woodFilled ? handleWoodColor : handleBaseColor;
        handleLabelText.text = woodFilled ? "HANDLE - READY" : "HANDLE (Wood)";
    }
}

public class CraftingDragChip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ResourceType ResourceType;

    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private Canvas canvas;
    private Vector2 originalAnchoredPosition;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalAnchoredPosition = rect.anchoredPosition;
        canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        rect.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;
        rect.anchoredPosition = originalAnchoredPosition;
    }
}

// Enlarged, forgiving drop target with a highlight ring that lights up while a compatible-looking
// drag is hovering over it, so the player gets live feedback before releasing (not just a silent
// miss if the drop lands a few pixels outside).
public class CraftingDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action<ResourceType> OnDropped;

    private Image highlightImage;
    private static readonly Color HighlightOn = new Color(1f, 0.9f, 0.5f, 0.55f);
    private static readonly Color HighlightOff = new Color(1f, 0.9f, 0.5f, 0f);

    private void Awake()
    {
        var go = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-16f, -16f);
        rect.offsetMax = new Vector2(16f, 16f);
        highlightImage = go.GetComponent<Image>();
        highlightImage.color = HighlightOff;
        highlightImage.raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (eventData.dragging)
        {
            highlightImage.color = HighlightOn;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        highlightImage.color = HighlightOff;
    }

    public void OnDrop(PointerEventData eventData)
    {
        highlightImage.color = HighlightOff;

        if (eventData.pointerDrag == null)
        {
            return;
        }

        var chip = eventData.pointerDrag.GetComponent<CraftingDragChip>();

        if (chip != null)
        {
            OnDropped?.Invoke(chip.ResourceType);
        }
    }
}
