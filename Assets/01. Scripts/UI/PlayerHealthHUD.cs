using UnityEngine;
using UnityEngine.UI;

// Small always-visible HP bar - self-built Canvas/Image like InteractionPromptUI/GuidedTutorial's
// banner, so it doesn't need a new field wired into ResourceHUD's scene-bound TMP_Text set.
// Bottom-right corner is the only screen corner nothing else claims (top-right: ResourceHUD,
// top-left: DevAutoPlayController debug panel, bottom-left: GuidedTutorial's help button,
// bottom-center: InteractionPromptUI). Polls PlayerHealth.Percent01 each frame - PlayerHealth
// itself stays a plain static data class (matches ResourceBank/Reputation); this is the only
// thing that reads it for display. See combat_design_v1.html §5.
public class PlayerHealthHUD : MonoBehaviour
{
    private const float BarWidth = 260f;
    private const float BarHeight = 22f;

    private static PlayerHealthHUD instance;

    public static PlayerHealthHUD Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("PlayerHealthHUD");
                instance = go.AddComponent<PlayerHealthHUD>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private RectTransform fillRect;

    // Referencing Instance is enough to bootstrap this singleton - kept as an explicit call at
    // the ResourceHUD.Start() call site for readability, same as GuidedTutorial's pattern.
    public void Show()
    {
    }

    private void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("PlayerHealthCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGO.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(1f, 0f);
        bgRect.anchorMax = new Vector2(1f, 0f);
        bgRect.pivot = new Vector2(1f, 0f);
        bgRect.anchoredPosition = new Vector2(-24f, 24f);
        bgRect.sizeDelta = new Vector2(BarWidth, BarHeight);
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.75f, 0.24f, 0.22f);
    }

    private void Update()
    {
        if (fillRect == null)
        {
            return;
        }

        fillRect.anchorMax = new Vector2(Mathf.Clamp01(PlayerHealth.Percent01), 1f);
    }
}
