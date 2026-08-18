using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small always-on-top readout ("STAGE 2 - Wave 1/2, 3 remaining") shown only while
// StageEncounterController has an active encounter running - stage_system_design_v1.html §2.
// Self-bootstrapping singleton, same code-generated-canvas convention as every other UI class
// here (ResourceHUD is the exception, being scene-wired).
public class StageEncounterUI : MonoBehaviour
{
    private static StageEncounterUI instance;

    public static StageEncounterUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StageEncounterUI");
                instance = go.AddComponent<StageEncounterUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text label;

    // Referencing Instance is enough to bootstrap this singleton - same explicit-call-for-
    // readability convention as PlayerCombat/PlayerInventoryUI's Activate().
    public void Activate()
    {
    }

    private void Awake()
    {
        var canvasGO = new GameObject("StageEncounterCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8; // above the field HUD, below modal panels (10+)
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -96f); // just below the resource HUD's top-left cluster, centered
        panelRect.sizeDelta = new Vector2(460f, 64f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(panel.transform, false);
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label = labelGO.AddComponent<TextMeshProUGUI>();
        label.fontSize = 26;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.86f, 0.5f);

        panel.SetActive(false);
    }

    private void Update()
    {
        StageEncounterController controller = StageEncounterController.Instance;

        if (!controller.IsEncounterActive)
        {
            if (panel.activeSelf)
            {
                panel.SetActive(false);
            }

            return;
        }

        if (!panel.activeSelf)
        {
            panel.SetActive(true);
        }

        int totalWaves = controller.TotalWavesForStage(controller.ActiveStageNumber);
        label.text = "STAGE " + controller.ActiveStageNumber + " - Wave " + controller.ActiveWaveNumber + "/" + totalWaves
            + " (" + controller.RemainingMonsterCount + " left)";
    }
}
