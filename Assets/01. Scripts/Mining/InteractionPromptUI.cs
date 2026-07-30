using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed.
// Shows tappable buttons (mobile-friendly - no keyboard dependency) when the player is
// near an interactable. Call Show(title, onCraft, onQuickCraft) each frame while in range.
public class InteractionPromptUI : MonoBehaviour
{
    private static InteractionPromptUI instance;

    public static InteractionPromptUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("InteractionPromptUI");
                instance = go.AddComponent<InteractionPromptUI>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text label;
    private Button craftButton;
    private Button quickCraftButton;

    private void Awake()
    {
        var canvasGO = new GameObject("InteractionPromptCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 260f);
        panelRect.sizeDelta = new Vector2(380f, 92f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(panel.transform, false);
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -6f);
        labelRect.sizeDelta = new Vector2(-16f, 26f);
        label = labelGO.AddComponent<TextMeshProUGUI>();
        label.fontSize = 16;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        craftButton = MakeButton("CraftButton", new Vector2(-95f, -20f), new Vector2(170f, 48f), "CRAFT", new Color(0.75f, 0.35f, 0.2f));
        quickCraftButton = MakeButton("QuickCraftButton", new Vector2(95f, -20f), new Vector2(170f, 48f), "QUICK CRAFT", new Color(0.4f, 0.45f, 0.5f));

        panel.SetActive(false);
    }

    private Button MakeButton(string name, Vector2 anchoredPos, Vector2 size, string text, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel.transform, false);
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
        tmp.fontSize = 15;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go.GetComponent<Button>();
    }

    public void Show(string title, Action onCraft, Action onQuickCraft)
    {
        panel.SetActive(true);
        label.text = title;

        craftButton.onClick.RemoveAllListeners();
        craftButton.onClick.AddListener(() => onCraft?.Invoke());

        quickCraftButton.onClick.RemoveAllListeners();
        quickCraftButton.onClick.AddListener(() => onQuickCraft?.Invoke());
    }

    public void Hide()
    {
        panel.SetActive(false);
    }
}
