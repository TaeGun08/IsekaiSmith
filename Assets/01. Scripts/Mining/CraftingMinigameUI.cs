using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed.
// CraftingStation calls CraftingMinigameUI.Instance.RunTemperature/RunHammering/ShowGradeResult.
// Input is tap/hold/drag (mouse or touch) - no keyboard dependency, mobile-friendly.
// Grade/reward judgement lives in CraftGradeUtility (pure logic, no UI dependency) - this class
// only renders and orchestrates, it doesn't decide what counts as good.
public class CraftingMinigameUI : MonoBehaviour
{
    private static CraftingMinigameUI instance;

    public static CraftingMinigameUI Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("CraftingMinigameUI");
                instance = go.AddComponent<CraftingMinigameUI>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    // Needle sweeps from bottom-left (value 0) through top (0.5) to bottom-right (value 1),
    // matching a real pressure gauge/speedometer.
    private const float DialSweepAngle = 120f;

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text instructionText;
    private TMP_Text resultText;

    // Temperature phase widgets - drag the handle up/down repeatedly to pump (each full
    // down-then-up stroke raises pressure). Heat always fades regardless of pumping, so only a
    // steady rhythm keeps up with it.
    private GameObject tempGroup;
    private Image crucibleImage;
    private RectTransform dialNeedleRect;
    private RectTransform tickMinRect;
    private RectTransform tickMaxRect;
    private RectTransform pumpTrackRect;
    private VerticalDragHandle pumpHandle;
    private float barWidth = 460f;

    private readonly Color crucibleCoolColor = new Color(0.45f, 0.2f, 0.14f);
    private readonly Color crucibleHotColor = new Color(1f, 0.85f, 0.3f);
    private readonly Color crucibleOverheatColor = new Color(1f, 0.35f, 0.25f);

    // Hammering phase widgets - hold the randomly-positioned marker on the blade to charge a
    // FIXED, clearly-visible power gauge (not tucked under the player's own thumb), then release
    // as close to the target % as possible.
    private GameObject hammerGroup;
    private RectTransform bladeRect;
    private RectTransform markerHoldRect;
    private PointerHoldTracker markerHoldTracker;
    private RectTransform hammerIconRect;
    private TMP_Text hammerTargetText;
    private RectTransform powerGaugeBgRect;
    private RectTransform powerFillRect;
    private RectTransform hammerTargetLineRect;
    private readonly List<GameObject> hitMarks = new List<GameObject>();

    private void Awake()
    {
        BuildUI();
        SetVisible(false);
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("CraftingMinigameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Was left at the default (0) - exactly tied with FloatingJoystick's full-screen touch
        // zone (HUD Canvas, also order 0), so touches during the hammering minigame could win on
        // the joystick instead of the drag handle, moving the character mid-swing (user report:
        // "망치질 부분에서 터치를 하면 조이스틱이 활성화하고 캐릭터가 움직여"). Matches
        // CraftingSilhouetteUI's order (10) - the two run sequentially in the same crafting flow
        // and never show at once, so sharing a value is fine.
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
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        titleText = MakeText(panel.transform, "Title", 38, new Vector2(0f, -46f), new Vector2(880f, 52f));
        resultText = MakeText(panel.transform, "Result", 28, new Vector2(0f, -120f), new Vector2(880f, 44f));
        instructionText = MakeText(panel.transform, "Instruction", 26, new Vector2(0f, -1620f), new Vector2(900f, 56f));

        BuildTemperatureGroup();
        BuildHammerGroup();
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

    // Anchored to the parent's left edge / vertical middle - used for every widget inside
    // tempGroup/hammerGroup so they all share one predictable coordinate frame.
    private TMP_Text MakeGroupText(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return tmp;
    }

    private void BuildTemperatureGroup()
    {
        tempGroup = new GameObject("TempGroup", typeof(RectTransform));
        tempGroup.transform.SetParent(panel.transform, false);
        var groupRect = tempGroup.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0.5f, 1f);
        groupRect.anchorMax = new Vector2(0.5f, 1f);
        groupRect.pivot = new Vector2(0.5f, 1f);
        groupRect.anchoredPosition = new Vector2(0f, -220f);
        groupRect.sizeDelta = new Vector2(barWidth, 900f);

        float centerX = barWidth * 0.5f;
        float dialCenterY = -280f;
        const float dialSize = 300f;

        var dialFaceGO = new GameObject("DialFace", typeof(RectTransform), typeof(Image));
        dialFaceGO.transform.SetParent(tempGroup.transform, false);
        var dialFaceRect = dialFaceGO.GetComponent<RectTransform>();
        dialFaceRect.anchorMin = new Vector2(0f, 0.5f);
        dialFaceRect.anchorMax = new Vector2(0f, 0.5f);
        dialFaceRect.pivot = new Vector2(0.5f, 0.5f);
        dialFaceRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        dialFaceRect.sizeDelta = new Vector2(dialSize, dialSize);
        var dialFaceImage = dialFaceGO.GetComponent<Image>();
        dialFaceImage.sprite = UIShapes.Circle();
        dialFaceImage.color = new Color(0.4f, 0.14f, 0.09f);

        var tickMinGO = new GameObject("TickMin", typeof(RectTransform), typeof(Image));
        tickMinGO.transform.SetParent(tempGroup.transform, false);
        tickMinRect = tickMinGO.GetComponent<RectTransform>();
        tickMinRect.anchorMin = new Vector2(0f, 0.5f);
        tickMinRect.anchorMax = new Vector2(0f, 0.5f);
        tickMinRect.pivot = new Vector2(0.5f, 0f);
        tickMinRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        tickMinRect.sizeDelta = new Vector2(8f, dialSize * 0.5f);
        tickMinGO.GetComponent<Image>().color = new Color(0.95f, 0.82f, 0.35f);

        var tickMaxGO = new GameObject("TickMax", typeof(RectTransform), typeof(Image));
        tickMaxGO.transform.SetParent(tempGroup.transform, false);
        tickMaxRect = tickMaxGO.GetComponent<RectTransform>();
        tickMaxRect.anchorMin = new Vector2(0f, 0.5f);
        tickMaxRect.anchorMax = new Vector2(0f, 0.5f);
        tickMaxRect.pivot = new Vector2(0.5f, 0f);
        tickMaxRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        tickMaxRect.sizeDelta = new Vector2(8f, dialSize * 0.5f);
        tickMaxGO.GetComponent<Image>().color = new Color(0.95f, 0.82f, 0.35f);

        var crucibleGO = new GameObject("Crucible", typeof(RectTransform), typeof(Image));
        crucibleGO.transform.SetParent(tempGroup.transform, false);
        var crucibleRect = crucibleGO.GetComponent<RectTransform>();
        crucibleRect.anchorMin = new Vector2(0f, 0.5f);
        crucibleRect.anchorMax = new Vector2(0f, 0.5f);
        crucibleRect.pivot = new Vector2(0.5f, 0.5f);
        crucibleRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        crucibleRect.sizeDelta = new Vector2(140f, 140f);
        crucibleImage = crucibleGO.GetComponent<Image>();
        crucibleImage.sprite = UIShapes.Circle();
        crucibleImage.color = crucibleCoolColor;

        var needleGO = new GameObject("DialNeedle", typeof(RectTransform), typeof(Image));
        needleGO.transform.SetParent(tempGroup.transform, false);
        dialNeedleRect = needleGO.GetComponent<RectTransform>();
        dialNeedleRect.anchorMin = new Vector2(0f, 0.5f);
        dialNeedleRect.anchorMax = new Vector2(0f, 0.5f);
        dialNeedleRect.pivot = new Vector2(0.5f, 0f);
        dialNeedleRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        dialNeedleRect.sizeDelta = new Vector2(6f, dialSize * 0.46f);
        needleGO.GetComponent<Image>().color = Color.white;

        var hubGO = new GameObject("DialHub", typeof(RectTransform), typeof(Image));
        hubGO.transform.SetParent(tempGroup.transform, false);
        var hubRect = hubGO.GetComponent<RectTransform>();
        hubRect.anchorMin = new Vector2(0f, 0.5f);
        hubRect.anchorMax = new Vector2(0f, 0.5f);
        hubRect.pivot = new Vector2(0.5f, 0.5f);
        hubRect.anchoredPosition = new Vector2(centerX, dialCenterY);
        hubRect.sizeDelta = new Vector2(20f, 20f);
        var hubImage = hubGO.GetComponent<Image>();
        hubImage.sprite = UIShapes.Circle();
        hubImage.color = Color.white;

        // Pump track + handle, below the dial.
        var trackGO = new GameObject("PumpTrack", typeof(RectTransform), typeof(Image));
        trackGO.transform.SetParent(tempGroup.transform, false);
        pumpTrackRect = trackGO.GetComponent<RectTransform>();
        pumpTrackRect.anchorMin = new Vector2(0f, 0.5f);
        pumpTrackRect.anchorMax = new Vector2(0f, 0.5f);
        pumpTrackRect.pivot = new Vector2(0.5f, 1f);
        pumpTrackRect.anchoredPosition = new Vector2(centerX, dialCenterY - dialSize * 0.5f - 40f);
        pumpTrackRect.sizeDelta = new Vector2(20f, 220f);
        trackGO.GetComponent<Image>().color = new Color(0.22f, 0.2f, 0.18f);

        var handleGO = new GameObject("PumpHandle", typeof(RectTransform), typeof(Image), typeof(VerticalDragHandle));
        handleGO.transform.SetParent(trackGO.transform, false);
        var pumpHandleRect = handleGO.GetComponent<RectTransform>();
        pumpHandleRect.anchorMin = new Vector2(0f, 0f);
        pumpHandleRect.anchorMax = new Vector2(0f, 0f);
        pumpHandleRect.pivot = new Vector2(0.5f, 0.5f);
        pumpHandleRect.anchoredPosition = new Vector2(pumpTrackRect.sizeDelta.x * 0.5f, 0f);
        pumpHandleRect.sizeDelta = new Vector2(96f, 60f);
        handleGO.GetComponent<Image>().color = new Color(0.5f, 0.35f, 0.2f);
        pumpHandle = handleGO.GetComponent<VerticalDragHandle>();
        pumpHandle.Track = pumpTrackRect;
        MakeGroupText(handleGO.transform, "PumpLabel", 16, Vector2.zero, new Vector2(96f, 60f), Color.white).text = "PUMP";

        tempGroup.SetActive(false);
    }

    private void BuildHammerGroup()
    {
        hammerGroup = new GameObject("HammerGroup", typeof(RectTransform));
        hammerGroup.transform.SetParent(panel.transform, false);
        var groupRect = hammerGroup.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0.5f, 1f);
        groupRect.anchorMax = new Vector2(0.5f, 1f);
        groupRect.pivot = new Vector2(0.5f, 1f);
        groupRect.anchoredPosition = new Vector2(0f, -220f);
        groupRect.sizeDelta = new Vector2(900f, 1300f);

        // The blade is purely a position target now (hold the marker on it) - the power
        // readout itself lives in a fixed spot to the right so it's never covered by a finger.
        var blade = new GameObject("Blade", typeof(RectTransform), typeof(Image));
        blade.transform.SetParent(hammerGroup.transform, false);
        bladeRect = blade.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0f, 1f);
        bladeRect.anchorMax = new Vector2(0f, 1f);
        bladeRect.pivot = new Vector2(0f, 1f);
        bladeRect.anchoredPosition = new Vector2(180f, -60f);
        bladeRect.sizeDelta = new Vector2(300f, 1150f);
        blade.GetComponent<Image>().color = new Color(0.34f, 0.36f, 0.4f);

        var markerGO = new GameObject("StrikeMarker", typeof(RectTransform), typeof(Image), typeof(PointerHoldTracker));
        markerGO.transform.SetParent(blade.transform, false);
        markerHoldRect = markerGO.GetComponent<RectTransform>();
        markerHoldRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerHoldRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerHoldRect.pivot = new Vector2(0.5f, 0.5f);
        markerHoldRect.sizeDelta = new Vector2(100f, 100f);
        var markerImage = markerGO.GetComponent<Image>();
        markerImage.sprite = UIShapes.Circle();
        markerImage.color = new Color(0.95f, 0.82f, 0.35f, 0.75f);
        markerHoldTracker = markerGO.GetComponent<PointerHoldTracker>();

        var hammerIconGO = new GameObject("HammerIcon", typeof(RectTransform), typeof(Image));
        hammerIconGO.transform.SetParent(blade.transform, false);
        hammerIconRect = hammerIconGO.GetComponent<RectTransform>();
        hammerIconRect.anchorMin = new Vector2(0.5f, 0.5f);
        hammerIconRect.anchorMax = new Vector2(0.5f, 0.5f);
        hammerIconRect.pivot = new Vector2(0.5f, 1f);
        hammerIconRect.sizeDelta = new Vector2(60f, 46f);
        var hammerIconImage = hammerIconGO.GetComponent<Image>();
        hammerIconImage.color = new Color(0.5f, 0.5f, 0.55f);
        hammerIconImage.raycastTarget = false;
        hammerIconGO.SetActive(false);

        float gaugeX = 650f;

        var hammerTargetGO = new GameObject("HammerTarget", typeof(RectTransform));
        hammerTargetGO.transform.SetParent(hammerGroup.transform, false);
        var hammerTargetRect = hammerTargetGO.GetComponent<RectTransform>();
        hammerTargetRect.anchorMin = new Vector2(0f, 1f);
        hammerTargetRect.anchorMax = new Vector2(0f, 1f);
        hammerTargetRect.pivot = new Vector2(0.5f, 1f);
        hammerTargetRect.anchoredPosition = new Vector2(gaugeX, -20f);
        hammerTargetRect.sizeDelta = new Vector2(240f, 36f);
        hammerTargetText = hammerTargetGO.AddComponent<TextMeshProUGUI>();
        hammerTargetText.fontSize = 26;
        hammerTargetText.alignment = TextAlignmentOptions.Center;
        hammerTargetText.color = new Color(0.95f, 0.82f, 0.35f);

        var gaugeBgGO = new GameObject("PowerGaugeBg", typeof(RectTransform), typeof(Image));
        gaugeBgGO.transform.SetParent(hammerGroup.transform, false);
        powerGaugeBgRect = gaugeBgGO.GetComponent<RectTransform>();
        powerGaugeBgRect.anchorMin = new Vector2(0f, 1f);
        powerGaugeBgRect.anchorMax = new Vector2(0f, 1f);
        powerGaugeBgRect.pivot = new Vector2(0.5f, 1f);
        powerGaugeBgRect.anchoredPosition = new Vector2(gaugeX, -70f);
        powerGaugeBgRect.sizeDelta = new Vector2(80f, 760f);
        gaugeBgGO.GetComponent<Image>().color = new Color(0.22f, 0.2f, 0.18f);

        var gaugeFillGO = new GameObject("PowerGaugeFill", typeof(RectTransform), typeof(Image));
        gaugeFillGO.transform.SetParent(gaugeBgGO.transform, false);
        powerFillRect = gaugeFillGO.GetComponent<RectTransform>();
        powerFillRect.anchorMin = new Vector2(0f, 0f);
        powerFillRect.anchorMax = new Vector2(1f, 0f);
        powerFillRect.pivot = new Vector2(0.5f, 0f);
        powerFillRect.offsetMin = Vector2.zero;
        powerFillRect.sizeDelta = new Vector2(0f, 0f);
        gaugeFillGO.GetComponent<Image>().color = new Color(0.95f, 0.7f, 0.3f);

        var targetLineGO = new GameObject("TargetLine", typeof(RectTransform), typeof(Image));
        targetLineGO.transform.SetParent(gaugeBgGO.transform, false);
        hammerTargetLineRect = targetLineGO.GetComponent<RectTransform>();
        hammerTargetLineRect.anchorMin = new Vector2(0f, 0f);
        hammerTargetLineRect.anchorMax = new Vector2(1f, 0f);
        hammerTargetLineRect.pivot = new Vector2(0.5f, 0.5f);
        hammerTargetLineRect.sizeDelta = new Vector2(0f, 10f);
        targetLineGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.95f);

        hammerGroup.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        panel.SetActive(visible);
    }

    // Live state exposed read-only for DevAutoPlayController's tutorial-driving mode (사용자 요청
    // 2026-08-24: "미니게임을 실제로 플레이") - it drives PumpHandle/MarkerHoldTracker directly
    // (same fields the real drag/hold interactions already write to) rather than needing a
    // separate simulated-input path, and reads these to react to the actual live values instead of
    // a blind fixed timing script.
    public bool IsTemperaturePhaseActive { get; private set; }
    public float CurrentTemperatureValue { get; private set; }
    public float TemperatureSweetMin { get; private set; }
    public float TemperatureSweetMax { get; private set; }
    public VerticalDragHandle PumpHandle => pumpHandle;

    public bool IsHammeringPhaseActive { get; private set; }
    public int CurrentHammerTargetPercent { get; private set; }
    public float HammerChargeDuration { get; private set; }
    public PointerHoldTracker MarkerHoldTracker => markerHoldTracker;

    // Bellows pump: drag the handle up and down repeatedly - each full down-then-up stroke
    // raises pressure. Heat always fades regardless of pumping (not just when idle), so only a
    // steady rhythm keeps up with it.
    public IEnumerator RunTemperature(string title, float duration, float sweetMin, float sweetMax, float strokeBump, float coolRate, float overheatPenaltyMultiplier, Action<float> onComplete)
    {
        SetVisible(true);
        tempGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";
        instructionText.text = "Drag the handle up and down to pump - heat always fades, keep the rhythm going!";

        IsTemperaturePhaseActive = true;
        TemperatureSweetMin = sweetMin;
        TemperatureSweetMax = sweetMax;

        float minAngle = Mathf.Lerp(DialSweepAngle, -DialSweepAngle, sweetMin);
        float maxAngle = Mathf.Lerp(DialSweepAngle, -DialSweepAngle, sweetMax);
        tickMinRect.localRotation = Quaternion.Euler(0f, 0f, minAngle);
        tickMaxRect.localRotation = Quaternion.Euler(0f, 0f, maxAngle);

        pumpHandle.ResetToBottom();

        float value = 0f;
        float elapsed = 0f;
        float timeInZone = 0f;
        float overheatTime = 0f;
        bool primedForStroke = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float handleY = pumpHandle.NormalizedY;

            if (handleY < 0.25f)
            {
                primedForStroke = true;
            }
            else if (handleY > 0.75f && primedForStroke)
            {
                primedForStroke = false;
                value = Mathf.Clamp01(value + strokeBump);

                if (CameraFollow.Instance != null)
                {
                    CameraFollow.Instance.Shake(0.06f, 0.06f);
                }
            }

            value = Mathf.Clamp01(value - coolRate * Time.deltaTime);
            CurrentTemperatureValue = value;

            float needleAngle = Mathf.Lerp(DialSweepAngle, -DialSweepAngle, value);
            dialNeedleRect.localRotation = Quaternion.Euler(0f, 0f, needleAngle);

            if (value >= sweetMin && value <= sweetMax)
            {
                timeInZone += Time.deltaTime;
                crucibleImage.color = Color.Lerp(crucibleCoolColor, crucibleHotColor, Mathf.Clamp01(value));
            }
            else if (value > sweetMax)
            {
                overheatTime += Time.deltaTime;
                crucibleImage.color = crucibleOverheatColor;
            }
            else
            {
                crucibleImage.color = Color.Lerp(crucibleCoolColor, crucibleHotColor, Mathf.Clamp01(value));
            }

            yield return null;
        }

        IsTemperaturePhaseActive = false;

        float quality = Mathf.Clamp01((timeInZone - overheatTime * overheatPenaltyMultiplier) / duration);
        resultText.text = "Heat control " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        tempGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }

    // Power-based hammering: hold the randomly-positioned marker on the blade to charge the
    // fixed power gauge, then release as close to the target % as possible.
    public IEnumerator RunHammering(string title, int rounds, float chargeDuration, float perfectTolerancePercent, float goodTolerancePercent, Action<float> onComplete)
    {
        SetVisible(true);
        hammerGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";
        instructionText.text = "Hold the marker, release when the gauge hits the target!";

        IsHammeringPhaseActive = true;
        HammerChargeDuration = chargeDuration;

        for (int i = 0; i < hitMarks.Count; i++)
        {
            if (hitMarks[i] != null)
            {
                Destroy(hitMarks[i]);
            }
        }
        hitMarks.Clear();

        float totalScore = 0f;
        float gaugeHeight = powerGaugeBgRect.sizeDelta.y;

        for (int round = 0; round < rounds; round++)
        {
            instructionText.text = "Round " + (round + 1) + " / " + rounds + " - hold the marker, release on target!";
            resultText.text = "";

            float bladeHalfWidth = bladeRect.sizeDelta.x * 0.5f - 70f;
            float bladeHalfHeight = bladeRect.sizeDelta.y * 0.5f - 70f;
            Vector2 markerPos = new Vector2(
                UnityEngine.Random.Range(-bladeHalfWidth, bladeHalfWidth),
                UnityEngine.Random.Range(-bladeHalfHeight, bladeHalfHeight));
            markerHoldRect.anchoredPosition = markerPos;

            int targetPercent = UnityEngine.Random.Range(20, 86);
            CurrentHammerTargetPercent = targetPercent;
            hammerTargetText.text = "Target " + targetPercent + "%";
            hammerTargetLineRect.anchoredPosition = new Vector2(0f, (targetPercent / 100f) * gaugeHeight);

            float power = 0f;
            bool released = false;
            float safetyTimer = 0f;
            float maxTime = chargeDuration + 2.5f;
            powerFillRect.anchorMax = new Vector2(1f, 0f);

            while (!released && safetyTimer < maxTime)
            {
                safetyTimer += Time.deltaTime;

                if (markerHoldTracker.IsHeld)
                {
                    power = Mathf.Clamp01(power + Time.deltaTime / chargeDuration);
                }

                powerFillRect.anchorMax = new Vector2(1f, power);

                if (markerHoldTracker.WasReleasedThisFrame)
                {
                    released = true;
                }

                yield return null;
            }

            int actualPercent = Mathf.RoundToInt(power * 100f);
            int diff = Mathf.Abs(actualPercent - targetPercent);
            float roundScore;
            float shakeAmplitude;
            Color sparkColor;
            int sparkCount;

            if (diff <= perfectTolerancePercent)
            {
                roundScore = 1f;
                shakeAmplitude = 0.16f;
                resultText.text = "Perfect! (" + actualPercent + "%)";
                sparkColor = new Color(1f, 0.85f, 0.3f);
                sparkCount = 10;
                SpawnHitMark(markerPos);
            }
            else if (diff <= goodTolerancePercent)
            {
                roundScore = 0.6f;
                shakeAmplitude = 0.1f;
                resultText.text = "Good (" + actualPercent + "%)";
                sparkColor = new Color(1f, 0.7f, 0.35f);
                sparkCount = 6;
                SpawnHitMark(markerPos);
            }
            else
            {
                roundScore = 0.15f;
                shakeAmplitude = 0f;
                resultText.text = released ? "Miss (" + actualPercent + "%)" : "Too Slow";
                sparkColor = new Color(0.6f, 0.56f, 0.52f);
                sparkCount = 3;
            }

            // Every real strike throws sparks (user report: "망치질할 때 스파크 튀거나 하는
            // 이펙트가 추가되었으면 해"), not just Perfect/Good hits - a Miss still physically
            // connects with the blade, just fewer/duller sparks. "Too Slow" never released, so no
            // swing (and no sparks) plays at all.
            if (released)
            {
                StartCoroutine(PlayHammerSwing(markerPos, sparkColor, sparkCount));
            }

            if (shakeAmplitude > 0f && CameraFollow.Instance != null)
            {
                CameraFollow.Instance.Shake(shakeAmplitude, 0.1f);
            }

            totalScore += roundScore;
            yield return new WaitForSeconds(0.45f);
        }

        IsHammeringPhaseActive = false;

        float quality = Mathf.Clamp01(totalScore / rounds);
        resultText.text = "Forging accuracy " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        hammerGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }

    // Sells "I'm hitting this exact spot" - plays on every release, so it always reads as a
    // real hammer strike.
    private IEnumerator PlayHammerSwing(Vector2 position, Color sparkColor, int sparkCount)
    {
        hammerIconRect.gameObject.SetActive(true);
        Vector2 upPosition = position + new Vector2(0f, 130f);
        hammerIconRect.anchoredPosition = upPosition;
        hammerIconRect.localRotation = Quaternion.identity;

        float t = 0f;
        const float downDuration = 0.09f;
        while (t < downDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / downDuration);
            hammerIconRect.anchoredPosition = Vector2.Lerp(upPosition, position, p);
            hammerIconRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-25f, 0f, p));
            yield return null;
        }

        // Hammer just connected - impact sparks right at the moment of contact, tinted/sized by
        // how accurate the strike was (see the caller's Perfect/Good/Miss branch).
        SpawnHammerSparks(position, sparkColor, sparkCount);

        t = 0f;
        const float upDuration = 0.16f;
        while (t < upDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / upDuration);
            hammerIconRect.anchoredPosition = Vector2.Lerp(position, upPosition, p);
            yield return null;
        }

        hammerIconRect.gameObject.SetActive(false);
    }

    private void SpawnHitMark(Vector2 position)
    {
        var markGO = new GameObject("HitMark", typeof(RectTransform), typeof(Image));
        markGO.transform.SetParent(bladeRect, false);
        var rect = markGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(14f, 14f);
        var image = markGO.GetComponent<Image>();
        image.color = new Color(0.15f, 0.14f, 0.13f, 0.85f);
        image.raycastTarget = false;
        hitMarks.Add(markGO);
    }

    // UI-space impact sparks for the hammering minigame - same "radial burst + shrink + fade"
    // technique HitEffects.SpawnSparks uses in world space, adapted to RectTransform/anchoredPosition
    // since this canvas isn't tied to a world position. Not pooled (unlike HitEffects) - short-lived,
    // low-volume (a handful of small Images per hammer round), self-destructs when done.
    private void SpawnHammerSparks(Vector2 origin, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var sparkGO = new GameObject("HammerSpark", typeof(RectTransform), typeof(Image));
            sparkGO.transform.SetParent(bladeRect, false);
            var rect = sparkGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = origin;
            rect.sizeDelta = Vector2.one * 16f;
            var image = sparkGO.GetComponent<Image>();
            image.sprite = UIShapes.Circle();
            image.color = color;
            image.raycastTarget = false;

            Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
            StartCoroutine(HammerSparkRoutine(rect, origin, direction));
        }
    }

    private IEnumerator HammerSparkRoutine(RectTransform spark, Vector2 origin, Vector2 direction)
    {
        const float lifetime = 0.22f;
        const float speed = 220f;
        float elapsed = 0f;

        while (elapsed < lifetime && spark != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);
            spark.anchoredPosition = origin + direction * speed * t;
            spark.localScale = Vector3.one * (1f - t);
            yield return null;
        }

        if (spark != null)
        {
            Destroy(spark.gameObject);
        }
    }

    public IEnumerator ShowGradeResult(WeaponType weapon, OreGrade oreGrade, ManaElement element, ManaGrade manaGrade, CraftGrade grade, int amount)
    {
        SetVisible(true);
        titleText.text = "Complete!";
        instructionText.text = "";

        string trait = CraftGradeUtility.RollTrait(grade);
        string traitSuffix = trait != null ? " (" + trait + ")" : "";
        string elementPrefix = element != ManaElement.None ? ManaGradeUtility.DisplayName(manaGrade) + " " + ManaElementUtility.DisplayName(element) + " " : "";
        resultText.text = elementPrefix + OreGradeUtility.DisplayName(oreGrade) + " " + WeaponTypeUtility.DisplayName(weapon) + " " + CraftGradeUtility.DisplayName(grade) + "!  x" + amount + traitSuffix;
        resultText.color = element != ManaElement.None ? ManaElementUtility.SparkColor(element) : GradeColor(grade);

        if (grade == CraftGrade.Masterwork || grade == CraftGrade.Legendary)
        {
            yield return SparkleFlourish(grade == CraftGrade.Legendary ? 1.1f : 0.7f);
        }
        else
        {
            yield return new WaitForSeconds(0.8f);
        }

        resultText.color = Color.white;
        SetVisible(false);
    }

    private IEnumerator SparkleFlourish(float duration)
    {
        Vector3 baseScale = resultText.transform.localScale;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float s = 1f + Mathf.Sin(t * 14f) * 0.08f * (1f - t / duration);
            resultText.transform.localScale = baseScale * s;
            yield return null;
        }

        resultText.transform.localScale = baseScale;
    }

    private static Color GradeColor(CraftGrade grade)
    {
        switch (grade)
        {
            case CraftGrade.Rough:
                return new Color(0.75f, 0.72f, 0.68f);
            case CraftGrade.Common:
                return new Color(0.68f, 0.78f, 0.6f);
            case CraftGrade.Fine:
                return new Color(0.55f, 0.85f, 0.6f);
            case CraftGrade.Superior:
                return new Color(0.5f, 0.7f, 0.95f);
            case CraftGrade.Exceptional:
                return new Color(0.68f, 0.55f, 0.9f);
            case CraftGrade.Masterwork:
                return new Color(0.95f, 0.8f, 0.35f);
            case CraftGrade.Legendary:
                return new Color(0.95f, 0.55f, 0.25f);
            default:
                return Color.white;
        }
    }
}

// Constrains its own drag to a vertical track and exposes a 0..1 normalized position - used by
// the furnace pump handle. No confirm/threshold logic here (unlike the crafting silhouette's
// sheet-chip drag) - the caller polls NormalizedY every frame to detect stroke completion.
public class VerticalDragHandle : MonoBehaviour, IDragHandler
{
    public RectTransform Track;
    public float NormalizedY { get; private set; }

    private RectTransform rect;
    private Canvas canvas;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (Track == null)
        {
            return;
        }

        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float trackHeight = Track.sizeDelta.y;
        float newY = Mathf.Clamp(rect.anchoredPosition.y + eventData.delta.y / scale, 0f, trackHeight);
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, newY);
        NormalizedY = trackHeight > 0f ? newY / trackHeight : 0f;
    }

    public void ResetToBottom()
    {
        NormalizedY = 0f;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0f);
    }

    // Dev-only: drives NormalizedY directly (same field OnDrag writes to) without a real drag
    // gesture - used by DevAutoPlayController to actually play the temperature minigame.
    public void DevSetNormalizedY(float value)
    {
        if (Track == null)
        {
            return;
        }

        float trackHeight = Track.sizeDelta.y;
        NormalizedY = Mathf.Clamp01(value);
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, NormalizedY * trackHeight);
    }
}
