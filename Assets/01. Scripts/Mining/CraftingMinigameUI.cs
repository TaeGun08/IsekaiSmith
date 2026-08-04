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
    private float barWidth = 460f;

    private readonly Color cleanPopColor = new Color(0.95f, 0.82f, 0.35f);
    private readonly Color glancingPopColor = new Color(0.75f, 0.8f, 0.85f);
    private readonly Color missPopColor = new Color(0.7f, 0.35f, 0.32f);

    // Hammering phase widgets - direct tap on the blade, no separate hold button.
    private GameObject hammerGroup;
    private RectTransform bladeRect;
    private Button bladeButton;
    private bool bladeTapped;
    private RectTransform targetRingRect;
    private RectTransform approachRingRect;
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
        panelRect.sizeDelta = new Vector2(980f, 1700f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        titleText = MakeText(panel.transform, "Title", 34, new Vector2(0f, -46f), new Vector2(880f, 48f));
        resultText = MakeText(panel.transform, "Result", 24, new Vector2(0f, -120f), new Vector2(880f, 40f));
        instructionText = MakeText(panel.transform, "Instruction", 20, new Vector2(0f, -1620f), new Vector2(900f, 50f));

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
        groupRect.anchoredPosition = new Vector2(0f, -220f);
        groupRect.sizeDelta = new Vector2(barWidth, 28f);

        var bg = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(tempGroup.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(0f, 0.5f);
        bgRect.pivot = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(barWidth, 28f);
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
        needleRect.sizeDelta = new Vector2(6f, 44f);
        needleImage = needle.GetComponent<Image>();
        needleImage.color = Color.white;

        // Furnace pulse-tap widget. NOTE: this mechanic is under review per player feedback
        // ("이게 아니었어") - kept functional but flagged for a follow-up redesign pass rather
        // than guessed at again blind. Only sizes here were enlarged to match the bigger panel.
        float centerX = barWidth * 0.5f;
        float centerY = -280f;

        var strikeRingGO = new GameObject("StrikeRing", typeof(RectTransform), typeof(Image));
        strikeRingGO.transform.SetParent(tempGroup.transform, false);
        var strikeRingRect = strikeRingGO.GetComponent<RectTransform>();
        strikeRingRect.anchorMin = new Vector2(0f, 0.5f);
        strikeRingRect.anchorMax = new Vector2(0f, 0.5f);
        strikeRingRect.pivot = new Vector2(0.5f, 0.5f);
        strikeRingRect.anchoredPosition = new Vector2(centerX, centerY);
        strikeRingRect.sizeDelta = new Vector2(260f, 260f);
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
        pulseRingRect.sizeDelta = new Vector2(260f, 260f);
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
        coreRect.sizeDelta = new Vector2(170f, 170f);
        coreGO.GetComponent<Image>().color = new Color(0.85f, 0.4f, 0.15f);
        var furnaceButton = coreGO.GetComponent<Button>();
        furnaceButton.onClick.AddListener(() => furnaceTapped = true);
        MakeGroupText(coreGO.transform, "CoreLabel", 20, Vector2.zero, new Vector2(170f, 170f), Color.white).text = "TAP";

        comboText = MakeGroupText(tempGroup.transform, "ComboText", 24, new Vector2(centerX + 150f, centerY + 140f), new Vector2(150f, 44f), cleanPopColor);
        popText = MakeGroupText(tempGroup.transform, "PopText", 22, new Vector2(centerX, centerY + 190f), new Vector2(360f, 44f), Color.white);

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

        // The blade itself is the tap target - "칼날 부위를 직접 클릭" - no separate button
        // elsewhere on screen. Tapping anywhere on the blade counts as the strike attempt;
        // WHEN you tap (matching the shrinking ring to the static target ring) is what's judged.
        var blade = new GameObject("Blade", typeof(RectTransform), typeof(Image), typeof(Button));
        blade.transform.SetParent(hammerGroup.transform, false);
        bladeRect = blade.GetComponent<RectTransform>();
        bladeRect.anchorMin = new Vector2(0f, 1f);
        bladeRect.anchorMax = new Vector2(0f, 1f);
        bladeRect.pivot = new Vector2(0f, 1f);
        bladeRect.anchoredPosition = new Vector2(280f, -60f);
        bladeRect.sizeDelta = new Vector2(340f, 1150f);
        blade.GetComponent<Image>().color = new Color(0.34f, 0.36f, 0.4f);
        bladeButton = blade.GetComponent<Button>();
        bladeButton.onClick.AddListener(() => bladeTapped = true);

        var targetRingGO = new GameObject("TargetRing", typeof(RectTransform), typeof(Image));
        targetRingGO.transform.SetParent(blade.transform, false);
        targetRingRect = targetRingGO.GetComponent<RectTransform>();
        targetRingRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRingRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRingRect.pivot = new Vector2(0.5f, 0.5f);
        targetRingRect.sizeDelta = new Vector2(90f, 90f);
        var targetRingImage = targetRingGO.GetComponent<Image>();
        targetRingImage.sprite = UIShapes.Circle();
        targetRingImage.color = new Color(0.95f, 0.82f, 0.35f, 0.55f);
        targetRingImage.raycastTarget = false;

        var approachRingGO = new GameObject("ApproachRing", typeof(RectTransform), typeof(Image));
        approachRingGO.transform.SetParent(blade.transform, false);
        approachRingRect = approachRingGO.GetComponent<RectTransform>();
        approachRingRect.anchorMin = new Vector2(0.5f, 0.5f);
        approachRingRect.anchorMax = new Vector2(0.5f, 0.5f);
        approachRingRect.pivot = new Vector2(0.5f, 0.5f);
        approachRingRect.sizeDelta = new Vector2(90f, 90f);
        var approachRingImage = approachRingGO.GetComponent<Image>();
        approachRingImage.sprite = UIShapes.Circle();
        approachRingImage.color = new Color(1f, 1f, 1f, 0.85f);
        approachRingImage.raycastTarget = false;

        targetRingGO.SetActive(false);
        approachRingGO.SetActive(false);

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
        sweetZoneRect.sizeDelta = new Vector2((sweetMax - sweetMin) * barWidth, 30f);

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

    // Direct-click hammering: a static target ring appears at a random spot on the blade and a
    // larger "approach" ring shrinks toward it over ringShrinkDuration. Tap the blade (anywhere)
    // when the approach ring visually matches the target ring - no separate hold button, no
    // power gauge, just "click the blade at the right moment".
    public IEnumerator RunHammering(string title, int rounds, float ringShrinkDuration, float perfectTolerancePercent, float goodTolerancePercent, Action<float> onComplete)
    {
        SetVisible(true);
        hammerGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";
        instructionText.text = "Tap the blade when the rings match!";

        for (int i = 0; i < hitMarks.Count; i++)
        {
            if (hitMarks[i] != null)
            {
                Destroy(hitMarks[i]);
            }
        }
        hitMarks.Clear();

        const float approachStartScale = 2.4f;
        const float targetScale = 1f;
        float totalScore = 0f;

        for (int round = 0; round < rounds; round++)
        {
            instructionText.text = "Round " + (round + 1) + " / " + rounds + " - tap when the rings match!";
            resultText.text = "";

            float bladeHalfWidth = bladeRect.sizeDelta.x * 0.5f - 100f;
            float bladeHalfHeight = bladeRect.sizeDelta.y * 0.5f - 100f;
            Vector2 pos = new Vector2(
                UnityEngine.Random.Range(-bladeHalfWidth, bladeHalfWidth),
                UnityEngine.Random.Range(-bladeHalfHeight, bladeHalfHeight));

            targetRingRect.anchoredPosition = pos;
            approachRingRect.anchoredPosition = pos;
            targetRingRect.gameObject.SetActive(true);
            approachRingRect.gameObject.SetActive(true);
            approachRingRect.localScale = Vector3.one * approachStartScale;

            bladeTapped = false;
            float elapsed = 0f;
            float maxTime = ringShrinkDuration + 0.6f;
            bool tapped = false;
            float finalScale = targetScale;

            while (!tapped && elapsed < maxTime)
            {
                elapsed += Time.deltaTime;
                float shrinkT = Mathf.Clamp01(elapsed / ringShrinkDuration);
                finalScale = Mathf.Lerp(approachStartScale, targetScale, shrinkT);
                approachRingRect.localScale = Vector3.one * finalScale;

                if (bladeTapped)
                {
                    bladeTapped = false;
                    tapped = true;
                }

                yield return null;
            }

            float diffPercent = Mathf.Abs(finalScale - targetScale) / (approachStartScale - targetScale) * 100f;

            if (!tapped)
            {
                diffPercent = 100f;
            }

            float roundScore;
            float shakeAmplitude;

            if (diffPercent <= perfectTolerancePercent)
            {
                roundScore = 1f;
                shakeAmplitude = 0.16f;
                resultText.text = "Perfect!";
                SpawnHitMark(pos);
            }
            else if (diffPercent <= goodTolerancePercent)
            {
                roundScore = 0.6f;
                shakeAmplitude = 0.1f;
                resultText.text = "Good";
                SpawnHitMark(pos);
            }
            else
            {
                roundScore = 0.15f;
                shakeAmplitude = 0f;
                resultText.text = tapped ? "Miss" : "Too Slow";
            }

            if (shakeAmplitude > 0f && CameraFollow.Instance != null)
            {
                CameraFollow.Instance.Shake(shakeAmplitude, 0.1f);
            }

            targetRingRect.gameObject.SetActive(false);
            approachRingRect.gameObject.SetActive(false);

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

    public IEnumerator ShowGradeResult(CraftGrade grade, int amount)
    {
        SetVisible(true);
        titleText.text = "Complete!";
        instructionText.text = "";

        string trait = CraftGradeUtility.RollTrait(grade);
        string traitSuffix = trait != null ? " (" + trait + ")" : "";
        resultText.text = CraftGradeUtility.DisplayName(grade) + "!  x" + amount + traitSuffix;
        resultText.color = GradeColor(grade);

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
