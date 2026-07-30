using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Drag-and-drop material loading panel: player drags resource chips from storage onto
// recipe slots to choose/confirm what goes into the station before the minigame starts.
// Works with mouse or touch (standard UGUI drag interfaces), no keyboard dependency.
public class CraftingLoadUI : MonoBehaviour
{
    private static CraftingLoadUI instance;

    public static CraftingLoadUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("CraftingLoadUI");
                instance = go.AddComponent<CraftingLoadUI>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text hintText;
    private Transform sourceRow;
    private Transform slotRow;
    private Button startButton;
    private Button cancelButton;

    private readonly List<GameObject> spawnedChips = new List<GameObject>();
    private readonly List<GameObject> spawnedSlots = new List<GameObject>();
    private bool[] slotFilled;
    private bool started;
    private bool cancelled;

    private void Awake()
    {
        BuildUI();
        panel.SetActive(false);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("CraftingLoadCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        panelRect.sizeDelta = new Vector2(480f, 320f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.92f);

        titleText = MakeText(panel.transform, "Title", 22, new Vector2(0f, -16f), new Vector2(440f, 28f));
        hintText = MakeText(panel.transform, "Hint", 13, new Vector2(0f, -50f), new Vector2(440f, 22f));
        hintText.text = "Drag each material onto its matching slot below";
        hintText.color = new Color(0.8f, 0.78f, 0.75f);

        var sourceRowGO = new GameObject("SourceRow", typeof(RectTransform));
        sourceRowGO.transform.SetParent(panel.transform, false);
        var sourceRowRect = sourceRowGO.GetComponent<RectTransform>();
        sourceRowRect.anchorMin = new Vector2(0.5f, 1f);
        sourceRowRect.anchorMax = new Vector2(0.5f, 1f);
        sourceRowRect.pivot = new Vector2(0.5f, 1f);
        sourceRowRect.anchoredPosition = new Vector2(0f, -90f);
        sourceRowRect.sizeDelta = new Vector2(440f, 90f);
        sourceRow = sourceRowGO.transform;

        var slotRowGO = new GameObject("SlotRow", typeof(RectTransform));
        slotRowGO.transform.SetParent(panel.transform, false);
        var slotRowRect = slotRowGO.GetComponent<RectTransform>();
        slotRowRect.anchorMin = new Vector2(0.5f, 1f);
        slotRowRect.anchorMax = new Vector2(0.5f, 1f);
        slotRowRect.pivot = new Vector2(0.5f, 1f);
        slotRowRect.anchoredPosition = new Vector2(0f, -200f);
        slotRowRect.sizeDelta = new Vector2(440f, 90f);
        slotRow = slotRowGO.transform;

        startButton = MakeButton(panel.transform, "StartButton", new Vector2(-90f, 26f), new Vector2(160f, 44f), "START", new Color(0.35f, 0.6f, 0.35f));
        cancelButton = MakeButton(panel.transform, "CancelButton", new Vector2(90f, 26f), new Vector2(160f, 44f), "CANCEL", new Color(0.5f, 0.3f, 0.28f));
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

        var label = MakeText(go.transform, "Text", 15, Vector2.zero, size);
        label.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        label.GetComponent<RectTransform>().anchorMax = Vector2.one;
        label.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
        label.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        label.text = text;

        return go.GetComponent<Button>();
    }

    public IEnumerator RunLoadPanel(string title, ResourceType[] requiredTypes, int[] requiredAmounts, Action<bool> onComplete)
    {
        titleText.text = title;
        started = false;
        cancelled = false;

        ClearSpawned();
        slotFilled = new bool[requiredTypes.Length];

        float spacing = 440f / requiredTypes.Length;
        float startX = -220f + spacing * 0.5f;

        for (int i = 0; i < requiredTypes.Length; i++)
        {
            int index = i;
            ResourceType type = requiredTypes[i];
            int amount = requiredAmounts[i];
            int available = ResourceBank.Get(type);

            var chip = BuildChip(sourceRow, new Vector2(startX + spacing * i, -45f), type.ToString() + "\nx" + amount + " (have " + available + ")", type);
            spawnedChips.Add(chip);

            var slot = BuildSlot(slotRow, new Vector2(startX + spacing * i, -45f), type.ToString() + " Slot\nneed " + amount);
            spawnedSlots.Add(slot);
            var dropSlot = slot.GetComponent<CraftingDropSlot>();
            dropSlot.ResourceType = type;
            dropSlot.OnChipDropped = droppedType =>
            {
                slotFilled[index] = true;
                var img = slot.GetComponent<Image>();
                img.color = new Color(0.35f, 0.6f, 0.35f);
            };
        }

        startButton.onClick.RemoveAllListeners();
        startButton.onClick.AddListener(() => { if (AllFilled()) started = true; });

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(() => cancelled = true);

        panel.SetActive(true);

        while (!started && !cancelled)
        {
            startButton.interactable = AllFilled();
            yield return null;
        }

        panel.SetActive(false);
        onComplete?.Invoke(started);
    }

    private bool AllFilled()
    {
        if (slotFilled == null)
        {
            return false;
        }

        for (int i = 0; i < slotFilled.Length; i++)
        {
            if (!slotFilled[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ClearSpawned()
    {
        foreach (var go in spawnedChips)
        {
            if (go != null) Destroy(go);
        }

        foreach (var go in spawnedSlots)
        {
            if (go != null) Destroy(go);
        }

        spawnedChips.Clear();
        spawnedSlots.Clear();
    }

    private GameObject BuildChip(Transform parent, Vector2 anchoredPos, string label, ResourceType type)
    {
        var go = new GameObject("Chip_" + type, typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(CraftingDragChip));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(90f, 70f);
        go.GetComponent<Image>().color = new Color(0.55f, 0.45f, 0.3f);

        var text = MakeText(go.transform, "Label", 11, Vector2.zero, new Vector2(90f, 70f));
        text.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        text.GetComponent<RectTransform>().anchorMax = Vector2.one;
        text.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        text.text = label;

        go.GetComponent<CraftingDragChip>().ResourceType = type;
        return go;
    }

    private GameObject BuildSlot(Transform parent, Vector2 anchoredPos, string label)
    {
        var go = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(CraftingDropSlot));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(90f, 70f);
        go.GetComponent<Image>().color = new Color(0.3f, 0.28f, 0.26f);

        var text = MakeText(go.transform, "Label", 11, Vector2.zero, new Vector2(90f, 70f));
        text.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        text.GetComponent<RectTransform>().anchorMax = Vector2.one;
        text.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        text.text = label;

        return go;
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

public class CraftingDropSlot : MonoBehaviour, IDropHandler
{
    public ResourceType ResourceType;
    public Action<ResourceType> OnChipDropped;

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null)
        {
            return;
        }

        var chip = eventData.pointerDrag.GetComponent<CraftingDragChip>();

        if (chip != null && chip.ResourceType == ResourceType)
        {
            OnChipDropped?.Invoke(ResourceType);
        }
    }
}
