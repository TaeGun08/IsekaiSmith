using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed.
// CraftingStation calls CraftingMinigameUI.Instance.RunTemperature/RunHammering/ShowGradeResult.
// Input is tap/hold (mouse or touch) - no keyboard dependency, mobile-friendly.
// Timing/quality judgement itself lives in PulsePump / CraftGradeUtility (pure logic, no UI
// dependency) - this class only renders and orchestrates, it doesn't decide what counts as good.
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
            }

            return instance;
        }
    }

    private GameObject panel;
    private TMP_Text titleText;
    private TMP_Text instructionText;
    private TMP_Text resultText;

    // Temperature phase widgets
    private GameObject tempGroup;
    private RectTransform sweetZoneRect;
    private RectTransform needleRect;
    private Image needleImage;
    private RectTransform pulseRingRect;
    private bool furnaceTapped;
    private TMP_Text comboText;
    private TMP_Text popText;
    private float popTimer;
    private float barWidth = 260f;

    private readonly Color cleanPopColor = new Color(0.95f, 0.82f, 0.35f);
    private readonly Color glancingPopColor = new Color(0.75f, 0.8f, 0.85f);
    private readonly Color missPopColor = new Color(0.7f, 0.35f, 0.32f);

    // Hammering phase widgets
    private GameObject hammerGroup;
    private RectTransform bladeRect;
    private RectTransform markerRect;
    private TMP_Text targetLabelText;
    private RectTransform powerFillRect;
    private PointerHoldTracker holdButton;
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
        panelRect.sizeDelta = new Vector2(560f, 640f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        titleText = MakeText(panel.transform, "Title", 26, new Vector2(0f, -18f), new Vector2(520f, 32f));
        instructionText = MakeText(panel.transform, "Instruction", 15, new Vector2(0f, -580f), new Vector2(520f, 26f));
        resultText = MakeText(panel.transform, "Result", 18, new Vector2(0f, -60f), new Vector2(520f, 26f));

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
    // tempGroup so they all share one predictable coordinate frame (matches the bar's anchor).
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
        groupRect.anchoredPosition = new Vector2(0f, -110f);
        groupRect.sizeDelta = new Vector2(barWidth, 22f);

        var bg = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(tempGroup.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(0f, 0.5f);
        bgRect.pivot = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(barWidth, 22f);
        bg.GetComponent<Image>().color = new Color(0.35f, 0.45f, 0.6f);

        var sweet = new GameObject("SweetZone", typeof(RectTransform), typeof(Image));
        sweet.transform.SetParent(tempGroup.transform, false);
        sweetZoneRect = sweet.GetComponent<RectTransform>();
        sweetZoneRect.anchorMin = new Vector2(0f, 0.5f);
        sweetZoneRect.anchorMax = new Vector2(0f, 0.5f);
        sweetZoneRect.pivot = new Vector2(0f, 0.5f);
        sweet.GetComponent<Image>().color = new Color(0.45f, 0.75f, 0.4f, 0.85f);

        var needle = new GameObject("Needle", typeof(RectTransform), typeof(Image));
        needle.transform.SetParent(tempGroup.transform, false);
        needleRect = needle.GetComponent<RectTransform>();
        needleRect.anchorMin = new Vector2(0f, 0.5f);
        needleRect.anchorMax = new Vector2(0f, 0.5f);
        needleRect.pivot = new Vector2(0.5f, 0.5f);
        needleRect.sizeDelta = new Vector2(4f, 32f);
        needleImage = needle.GetComponent<Image>();
        needleImage.color = Color.white;

        // Furnace pulse-tap widget - replaces the old plain "TAP TO PUMP" button. A ring of
        // heat pulses outward on a steady beat; tapping close to the beat (not just tapping
        // often) is what actually raises the temperature efficiently.
        float centerX = barWidth * 0.5f;
        float centerY = -170f;

        var strikeRingGO = new GameObject("StrikeRing", typeof(RectTransform), typeof(Image));
        strikeRingGO.transform.SetParent(tempGroup.transform, false);
        var strikeRingRect = strikeRingGO.GetComponent<RectTransform>();
        strikeRingRect.anchorMin = new Vector2(0f, 0.5f);
        strikeRingRect.anchorMax = new Vector2(0f, 0.5f);
        strikeRingRect.pivot = new Vector2(0.5f, 0.5f);
        strikeRingRect.anchoredPosition = new Vector2(centerX, centerY);
        strikeRingRect.sizeDelta = new Vector2(150f, 150f);
        var strikeRingImage = strikeRingGO.GetComponent<Image>();
        strikeRingImage.sprite = UIShapes.Circle();
        strikeRingImage.color = new Color(1f, 1f, 1f, 0.3f);

        var pulseGO = new GameObject("PulseRing", typeof(RectTransform), typeof(Image));
        pulseGO.transform.SetParent(tempGroup.transform, false);
        pulseRingRect = pulseGO.GetComponent<RectTransform>();
        pulseRingRect.anchorMin = new Vector2(0f, 0.5f);
        pulseRingRect.anchorMax = new Vector2(0f, 0.5f);
        pulseRingRect.pivot = new Vector2(0.5f, 0.5f);
        pulseRingRect.anchoredPosition = new Vector2(centerX, centerY);
        pulseRingRect.sizeDelta = new Vector2(150f, 150f);
        var pulseImage = pulseGO.GetComponent<Image>();
        pulseImage.sprite = UIShapes.Circle();
        pulseImage.color = new Color(0.95f, 0.55f, 0.25f, 0.7f);

        var coreGO = new GameObject("FurnaceCore", typeof(RectTransform), typeof(Image), typeof(Button));
        coreGO.transform.SetParent(tempGroup.transform, false);
        var coreRect = coreGO.GetComponent<RectTransform>();
        coreRect.anchorMin = new Vector2(0f, 0.5f);
        coreRect.anchorMax = new Vector2(0f, 0.5f);
        coreRect.pivot = new Vector2(0.5f, 0.5f);
        coreRect.anchoredPosition = new Vector2(centerX, centerY);
        coreRect.sizeDelta = new Vector2(96f, 96f);
        coreGO.GetComponent<Image>().color = new Color(0.85f, 0.4f, 0.15f);
        var furnaceButton = coreGO.GetComponent<Button>();
        furnaceButton.onClick.AddListener(() => furnaceTapped = true);
        MakeGroupText(coreGO.transform, "CoreLabel", 13, Vector2.zero, new Vector2(96f, 96f), Color.white).text = "TAP";

        comboText = MakeGroupText(tempGroup.transform, "ComboText", 16, new Vector2(centerX + 80f, centerY + 70f), new Vector2(90f, 26f), cleanPopColor);
        popText = MakeGroupText(tempGroup.transform, "PopText", 15, new Vector2(centerX, centerY + 100f), new Vector2(220f, 26f), Color.white);

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
        groupRect.anchoredPosition = new Vector2(0f, -100f);
        groupRect.sizeDelta = new Vector2(520f, 460f);

        var blade = new GameObject("Blade", typeof(RectTransform), typeof(Image));
        blade.transform.SetParent(hammerGroup.transform, false);
        bladeRect = blade.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0f, 1f);
        bladeRect.anchorMax = new Vector2(0f, 1f);
        bladeRect.pivot = new Vector2(0f, 1f);
        bladeRect.anchoredPosition = new Vector2(90f, 0f);
        bladeRect.sizeDelta = new Vector2(110f, 320f);
        blade.GetComponent<Image>().color = new Color(0.34f, 0.36f, 0.4f);

        var marker = new GameObject("StrikeMarker", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(blade.transform, false);
        markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = new Vector2(28f, 28f);
        marker.GetComponent<Image>().color = new Color(0.95f, 0.82f, 0.35f);

        targetLabelText = MakeText(hammerGroup.transform, "TargetLabel", 15, new Vector2(90f, -30f), new Vector2(220f, 24f));
        targetLabelText.color = new Color(0.95f, 0.82f, 0.35f);

        var gaugeBg = new GameObject("PowerGaugeBg", typeof(RectTransform), typeof(Image));
        gaugeBg.transform.SetParent(hammerGroup.transform, false);
        var gaugeBgRect = gaugeBg.GetComponent<RectTransform>();
        gaugeBgRect.anchorMin = new Vector2(0f, 1f);
        gaugeBgRect.anchorMax = new Vector2(0f, 1f);
        gaugeBgRect.pivot = new Vector2(0f, 1f);
        gaugeBgRect.anchoredPosition = new Vector2(-60f, -20f);
        gaugeBgRect.sizeDelta = new Vector2(24f, 320f);
        gaugeBg.GetComponent<Image>().color = new Color(0.22f, 0.2f, 0.18f);

        var gaugeFill = new GameObject("PowerGaugeFill", typeof(RectTransform), typeof(Image));
        gaugeFill.transform.SetParent(gaugeBg.transform, false);
        powerFillRect = gaugeFill.GetComponent<RectTransform>();
        powerFillRect.anchorMin = new Vector2(0f, 0f);
        powerFillRect.anchorMax = new Vector2(1f, 0f);
        powerFillRect.pivot = new Vector2(0.5f, 0f);
        powerFillRect.offsetMin = Vector2.zero;
        powerFillRect.sizeDelta = new Vector2(0f, 0f);
        gaugeFill.GetComponent<Image>().color = new Color(0.5f, 0.75f, 0.4f);

        var holdGO = new GameObject("HoldButton", typeof(RectTransform), typeof(Image), typeof(PointerHoldTracker));
        holdGO.transform.SetParent(hammerGroup.transform, false);
        var holdRect = holdGO.GetComponent<RectTransform>();
        holdRect.anchorMin = new Vector2(0.5f, 0f);
        holdRect.anchorMax = new Vector2(0.5f, 0f);
        holdRect.pivot = new Vector2(0.5f, 0f);
        holdRect.anchoredPosition = new Vector2(0f, 0f);
        holdRect.sizeDelta = new Vector2(280f, 72f);
        holdGO.GetComponent<Image>().color = new Color(0.75f, 0.35f, 0.2f);
        holdButton = holdGO.GetComponent<PointerHoldTracker>();
        MakeText(holdGO.transform, "HoldLabel", 17, Vector2.zero, new Vector2(280f, 72f)).text = "HOLD, RELEASE ON TARGET";

        hammerGroup.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        panel.SetActive(visible);
    }

    private void ShowPop(string text, Color color)
    {
        popText.text = text;
        popText.color = color;
        popTimer = 0.5f;
    }

    public IEnumerator RunTemperature(string title, float duration, float sweetMin, float sweetMax, float pulsePeriod, float cleanBump, float glancingBump, float coolRate, float overheatPenaltyMultiplier, Action<float> onComplete)
    {
        SetVisible(true);
        tempGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";
        instructionText.text = "Tap the furnace on the beat - stay in the green zone. Don't overheat!";
        popText.text = "";
        comboText.text = "";

        sweetZoneRect.anchoredPosition = new Vector2(sweetMin * barWidth, 0f);
        sweetZoneRect.sizeDelta = new Vector2((sweetMax - sweetMin) * barWidth, 26f);

        float value = 0f;
        float elapsed = 0f;
        float timeInZone = 0f;
        float overheatTime = 0f;
        float cycleTimer = 0f;
        int combo = 0;
        bool penaltyActive = false;
        furnaceTapped = false;
        popTimer = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cycleTimer += Time.deltaTime;
            if (cycleTimer >= pulsePeriod)
            {
                cycleTimer -= pulsePeriod;
            }

            float phase = cycleTimer / pulsePeriod;
            pulseRingRect.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, phase);

            if (furnaceTapped)
            {
                furnaceTapped = false;
                PulseHitQuality hit = PulsePump.Judge(phase, penaltyActive ? 0.6f : 1f);

                switch (hit)
                {
                    case PulseHitQuality.Clean:
                        combo++;
                        float mult = PulsePump.ComboMultiplier(combo);
                        value = Mathf.Clamp01(value + cleanBump * mult);
                        penaltyActive = false;
                        ShowPop(combo > 1 ? "Clean! x" + combo : "Clean!", cleanPopColor);
                        if (CameraFollow.Instance != null)
                        {
                            CameraFollow.Instance.Shake(0.08f, 0.08f);
                        }
                        break;
                    case PulseHitQuality.Glancing:
                        combo = 0;
                        value = Mathf.Clamp01(value + glancingBump);
                        ShowPop("Glancing", glancingPopColor);
                        break;
                    default:
                        combo = 0;
                        penaltyActive = true;
                        ShowPop("Miss", missPopColor);
                        break;
                }

                comboText.text = combo > 1 ? "x" + combo : "";
            }
            else
            {
                value = Mathf.Clamp01(value - coolRate * Time.deltaTime);
            }

            if (value >= sweetMin && value <= sweetMax)
            {
                timeInZone += Time.deltaTime;
                needleImage.color = Color.white;
            }
            else if (value > sweetMax)
            {
                overheatTime += Time.deltaTime;
                needleImage.color = new Color(0.95f, 0.3f, 0.25f);
            }
            else
            {
                needleImage.color = new Color(0.6f, 0.75f, 0.95f);
            }

            needleRect.anchoredPosition = new Vector2(value * barWidth, 0f);

            if (popTimer > 0f)
            {
                popTimer -= Time.deltaTime;
                if (popTimer <= 0f)
                {
                    popText.text = "";
                }
            }

            yield return null;
        }

        float quality = Mathf.Clamp01((timeInZone - overheatTime * overheatPenaltyMultiplier) / duration);
        resultText.text = "Heat control " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        tempGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }

    public IEnumerator RunHammering(string title, int rounds, float chargeDuration, float perfectTolerancePercent, float goodTolerancePercent, Action<float> onComplete)
    {
        SetVisible(true);
        hammerGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";

        for (int i = 0; i < hitMarks.Count; i++)
        {
            if (hitMarks[i] != null)
            {
                Destroy(hitMarks[i]);
            }
        }
        hitMarks.Clear();

        float totalScore = 0f;

        for (int round = 0; round < rounds; round++)
        {
            int targetPercent = UnityEngine.Random.Range(20, 86);
            targetLabelText.text = "Target " + targetPercent + "%";
            instructionText.text = "Round " + (round + 1) + " / " + rounds + " - release right on the target!";
            resultText.text = "";

            float bladeHalfWidth = bladeRect.sizeDelta.x * 0.5f - 16f;
            float bladeHalfHeight = bladeRect.sizeDelta.y * 0.5f - 16f;
            markerRect.anchoredPosition = new Vector2(
                UnityEngine.Random.Range(-bladeHalfWidth, bladeHalfWidth),
                UnityEngine.Random.Range(-bladeHalfHeight, bladeHalfHeight));

            float power = 0f;
            bool released = false;
            float safetyTimer = 0f;
            float maxTime = chargeDuration + 2f;

            powerFillRect.anchorMax = new Vector2(1f, 0f);

            while (!released && safetyTimer < maxTime)
            {
                safetyTimer += Time.deltaTime;

                if (holdButton.IsHeld)
                {
                    power = Mathf.Clamp01(power + Time.deltaTime / chargeDuration);
                }

                powerFillRect.anchorMax = new Vector2(1f, power);

                if (holdButton.WasReleasedThisFrame)
                {
                    released = true;
                }

                yield return null;
            }

            int actualPercent = Mathf.RoundToInt(power * 100f);
            int diff = Mathf.Abs(actualPercent - targetPercent);
            float roundScore;
            float shakeAmplitude;

            if (diff <= perfectTolerancePercent)
            {
                roundScore = 1f;
                shakeAmplitude = 0.16f;
                resultText.text = "Perfect! (" + actualPercent + "%)";
                SpawnHitMark();
            }
            else if (diff <= goodTolerancePercent)
            {
                roundScore = 0.6f;
                shakeAmplitude = 0.1f;
                resultText.text = "Good (" + actualPercent + "%)";
                SpawnHitMark();
            }
            else
            {
                roundScore = 0.15f;
                shakeAmplitude = 0f;
                resultText.text = "Miss (" + actualPercent + "%)";
            }

            if (shakeAmplitude > 0f && CameraFollow.Instance != null)
            {
                CameraFollow.Instance.Shake(shakeAmplitude, 0.1f);
            }

            totalScore += roundScore;
            yield return new WaitForSeconds(0.45f);
        }

        float quality = Mathf.Clamp01(totalScore / rounds);
        resultText.text = "Forging accuracy " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        hammerGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }

    private void SpawnHitMark()
    {
        var markGO = new GameObject("HitMark", typeof(RectTransform), typeof(Image));
        markGO.transform.SetParent(bladeRect, false);
        var rect = markGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = markerRect.anchoredPosition;
        rect.sizeDelta = new Vector2(10f, 10f);
        markGO.GetComponent<Image>().color = new Color(0.15f, 0.14f, 0.13f, 0.85f);
        hitMarks.Add(markGO);
    }

    public IEnumerator ShowGradeResult(CraftGrade grade, int amount)
    {
        SetVisible(true);
        titleText.text = "Complete!";
        instructionText.text = "";
        resultText.text = CraftGradeUtility.DisplayName(grade) + "!  x" + amount;
        resultText.color = GradeColor(grade);

        if (grade == CraftGrade.Masterwork)
        {
            yield return SparkleFlourish();
        }
        else
        {
            yield return new WaitForSeconds(0.8f);
        }

        resultText.color = Color.white;
        SetVisible(false);
    }

    private IEnumerator SparkleFlourish()
    {
        Vector3 baseScale = resultText.transform.localScale;
        float t = 0f;

        while (t < 0.7f)
        {
            t += Time.deltaTime;
            float s = 1f + Mathf.Sin(t * 14f) * 0.08f * (1f - t / 0.7f);
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
            case CraftGrade.Fine:
                return new Color(0.55f, 0.85f, 0.6f);
            case CraftGrade.Superior:
                return new Color(0.5f, 0.7f, 0.95f);
            case CraftGrade.Masterwork:
                return new Color(0.95f, 0.8f, 0.35f);
            default:
                return Color.white;
        }
    }
}
