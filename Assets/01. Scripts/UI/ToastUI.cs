using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Generic "flash a short message, then auto-hide" banner - top-center, above the quest banner
// (25) so it's never buried. Used for feature-unlock announcements (PlayerInventoryUI/
// StageSelectUI: "Equipment Unlocked!"/"Stages Unlocked!") and the first-stage-entry hint
// (StageSceneController) - one reusable class instead of each caller building its own transient
// popup. Self-bootstrapping singleton, same convention as every other UI class here.
public class ToastUI : MonoBehaviour
{
    private const float DefaultDuration = 2.5f;

    private static ToastUI instance;

    public static ToastUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("ToastUI");
                instance = go.AddComponent<ToastUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text label;
    private float hideAt;

    private void Awake()
    {
        var canvasGO = new GameObject("ToastCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 26; // above the quest banner (25), below welcome/help modals (50)
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -160f);
        panelRect.sizeDelta = new Vector2(760f, 120f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
        var outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 0.86f, 0.5f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(panel.transform, false);
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 8f);
        labelRect.offsetMax = new Vector2(-16f, -8f);
        label = labelGO.AddComponent<TextMeshProUGUI>();
        label.fontSize = 26;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.86f, 0.5f);
        label.textWrappingMode = TextWrappingModes.Normal;

        panel.SetActive(false);
    }

    public void Show(string message, float duration = DefaultDuration)
    {
        label.text = message;
        panel.SetActive(true);
        hideAt = Time.time + duration;
    }

    private void Update()
    {
        if (panel.activeSelf && Time.time >= hideAt)
        {
            panel.SetActive(false);
        }
    }
}
