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
    private GameObject retreatButtonGO;

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

        // Leaves with no reward, same as dying mid-wave - an escape hatch if a wave turns out to
        // be too much (stage_system_design_v2.html §4).
        var retreatGO = new GameObject("RetreatButton", typeof(RectTransform), typeof(Image), typeof(Button));
        retreatGO.transform.SetParent(canvasGO.transform, false);
        retreatButtonGO = retreatGO;
        var retreatRect = retreatGO.GetComponent<RectTransform>();
        retreatRect.anchorMin = new Vector2(0.5f, 1f);
        retreatRect.anchorMax = new Vector2(0.5f, 1f);
        retreatRect.pivot = new Vector2(0.5f, 1f);
        retreatRect.anchoredPosition = new Vector2(0f, -170f); // 10px below the progress panel
        retreatRect.sizeDelta = new Vector2(200f, 60f);
        retreatGO.GetComponent<Image>().color = new Color(0.5f, 0.3f, 0.28f);

        var retreatLabelGO = new GameObject("Label", typeof(RectTransform));
        retreatLabelGO.transform.SetParent(retreatGO.transform, false);
        var retreatLabelRect = retreatLabelGO.GetComponent<RectTransform>();
        retreatLabelRect.anchorMin = Vector2.zero;
        retreatLabelRect.anchorMax = Vector2.one;
        retreatLabelRect.offsetMin = Vector2.zero;
        retreatLabelRect.offsetMax = Vector2.zero;
        var retreatText = retreatLabelGO.AddComponent<TextMeshProUGUI>();
        retreatText.text = "RETREAT";
        retreatText.fontSize = 22;
        retreatText.fontStyle = FontStyles.Bold;
        retreatText.alignment = TextAlignmentOptions.Center;
        retreatText.color = Color.white;

        retreatGO.GetComponent<Button>().onClick.AddListener(() => StageSceneController.Instance.RequestRetreat());

        panel.SetActive(false);
        retreatButtonGO.SetActive(false);
    }

    private void Update()
    {
        StageEncounterController controller = StageEncounterController.Instance;

        if (!controller.IsEncounterActive)
        {
            if (panel.activeSelf)
            {
                panel.SetActive(false);
                retreatButtonGO.SetActive(false);
            }

            return;
        }

        if (!panel.activeSelf)
        {
            panel.SetActive(true);
            retreatButtonGO.SetActive(true);
        }

        int totalWaves = controller.TotalWavesForStage(controller.ActiveStageNumber);
        label.text = "STAGE " + controller.ActiveStageNumber + " - Wave " + controller.ActiveWaveNumber + "/" + totalWaves
            + " (" + controller.RemainingMonsterCount + " left)";
    }
}
