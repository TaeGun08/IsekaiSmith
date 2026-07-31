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
        panelRect.sizeDelta = new Vector2(600f, 900f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        titleText = MakeText(panel.transform, "Title", 30, new Vector2(0f, -24f), new Vector2(560f, 40f));
        hintText = MakeText(panel.transform, "Hint", 16, new Vector2(0f, -66f), new Vector2(560f, 26f));
        hintText.text = "Drag materials onto the blade and handle";
        hintText.color = new Color(0.75f, 0.72f, 0.68f);

        // Blade zone (tall, upper)
        var bladeGO = new GameObject("BladeZone", typeof(RectTransform), typeof(Image), typeof(CraftingDropZone));
        bladeGO.transform.SetParent(panel.transform, false);
        var bladeRect = bladeGO.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0.5f, 1f);
        bladeRect.anchorMax = new Vector2(0.5f, 1f);
        bladeRect.pivot = new Vector2(0.5f, 1f);
        bladeRect.anchoredPosition = new Vector2(0f, -110f);
        bladeRect.sizeDelta = new Vector2(90f, 420f);
        bladeImage = bladeGO.GetComponent<Image>();
        bladeImage.color = bladeBaseColor;
        bladeGO.GetComponent<CraftingDropZone>().OnDropped = OnBladeDrop;
        MakeText(bladeGO.transform, "Label", 15, new Vector2(0f, 20f), new Vector2(200f, 24f)).text = "BLADE";

        // Guard bar (decorative)
        var guardGO = new GameObject("Guard", typeof(RectTransform), typeof(Image));
        guardGO.transform.SetParent(panel.transform, false);
        var guardRect = guardGO.GetComponent<RectTransform>();
        guardRect.anchorMin = new Vector2(0.5f, 1f);
        guardRect.anchorMax = new Vector2(0.5f, 1f);
        guardRect.pivot = new Vector2(0.5f, 1f);
        guardRect.anchoredPosition = new Vector2(0f, -530f);
        guardRect.sizeDelta = new Vector2(160f, 16f);
        guardGO.GetComponent<Image>().color = new Color(0.4f, 0.33f, 0.22f);

        // Handle zone (short, lower)
        var handleGO = new GameObject("HandleZone", typeof(RectTransform), typeof(Image), typeof(CraftingDropZone));
        handleGO.transform.SetParent(panel.transform, false);
        var handleRect = handleGO.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 1f);
        handleRect.anchorMax = new Vector2(0.5f, 1f);
        handleRect.pivot = new Vector2(0.5f, 1f);
        handleRect.anchoredPosition = new Vector2(0f, -546f);
        handleRect.sizeDelta = new Vector2(56f, 140f);
        handleImage = handleGO.GetComponent<Image>();
        handleImage.color = handleBaseColor;
        handleGO.GetComponent<CraftingDropZone>().OnDropped = OnHandleDrop;
        MakeText(handleGO.transform, "Label", 15, new Vector2(0f, 20f), new Vector2(200f, 24f)).text = "HANDLE";

        var trayGO = new GameObject("Tray", typeof(RectTransform));
        trayGO.transform.SetParent(panel.transform, false);
        var trayRect = trayGO.GetComponent<RectTransform>();
        trayRect.anchorMin = new Vector2(0.5f, 0f);
        trayRect.anchorMax = new Vector2(0.5f, 0f);
        trayRect.pivot = new Vector2(0.5f, 0f);
        trayRect.anchoredPosition = new Vector2(0f, 130f);
        trayRect.sizeDelta = new Vector2(560f, 110f);
        tray = trayGO.transform;

        forgeButton = MakeButton(panel.transform, "ForgeButton", new Vector2(-100f, 40f), new Vector2(180f, 56f), "FORGE", new Color(0.35f, 0.6f, 0.35f));
        cancelButton = MakeButton(panel.transform, "CancelButton", new Vector2(100f, 40f), new Vector2(180f, 56f), "CANCEL", new Color(0.5f, 0.3f, 0.28f));
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

        var label = MakeText(go.transform, "Text", 17, Vector2.zero, size);
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
        rect.sizeDelta = new Vector2(110f, 90f);

        Color chipColor = type == ResourceType.Ore ? new Color(0.48f, 0.46f, 0.43f)
            : type == ResourceType.ManaStone ? new Color(0.69f, 0.27f, 0.56f)
            : new Color(0.55f, 0.42f, 0.28f);
        go.GetComponent<Image>().color = chipColor;

        var text = MakeText(go.transform, "Label", 14, Vector2.zero, new Vector2(110f, 90f));
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

        foreach (Transform child in tray)
        {
            Destroy(child.gameObject);
        }

        BuildChip(ResourceType.Ore, "Ore\nx" + ResourceBank.Get(ResourceType.Ore), new Vector2(-180f, -10f));
        BuildChip(ResourceType.ManaStone, "Mana Stone\nx" + ResourceBank.Get(ResourceType.ManaStone), new Vector2(0f, -10f));
        BuildChip(ResourceType.Wood, "Wood\nx" + ResourceBank.Get(ResourceType.Wood), new Vector2(180f, -10f));

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
    }

    private void UpdateHandleVisual()
    {
        handleImage.color = woodFilled ? handleWoodColor : handleBaseColor;
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

public class CraftingDropZone : MonoBehaviour, IDropHandler
{
    public Action<ResourceType> OnDropped;

    public void OnDrop(PointerEventData eventData)
    {
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
