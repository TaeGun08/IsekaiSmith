using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Self-contained: builds its own Canvas/UI in Awake so no scene wiring is needed.
// CraftingStation just calls CraftingMinigameUI.Instance.RunTemperature/RunHammering.
// Input is button press/hold (mouse or touch) - no keyboard dependency, mobile-friendly.
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
    private PointerHoldTracker pumpButton;
    private float barWidth = 260f;

    // Hammering phase widgets
    private GameObject hammerGroup;
    private RectTransform targetRect;
    private Button hammerButton;
    private bool hammerTapped;

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
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 60f);
        panelRect.sizeDelta = new Vector2(340f, 280f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        titleText = MakeText(panel.transform, "Title", 24, TextAlignmentOptions.Center, new Vector2(0f, -14f), new Vector2(320f, 26f));
        instructionText = MakeText(panel.transform, "Instruction", 13, TextAlignmentOptions.Center, new Vector2(0f, -220f), new Vector2(320f, 24f));
        resultText = MakeText(panel.transform, "Result", 16, TextAlignmentOptions.Center, new Vector2(0f, -130f), new Vector2(320f, 24f));

        BuildTemperatureGroup();
        BuildHammerGroup();
    }

    private TMP_Text MakeText(Transform parent, string name, int fontSize, TextAlignmentOptions align, Vector2 anchoredPos, Vector2 size)
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
        tmp.alignment = align;
        tmp.color = Color.white;
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
        groupRect.anchoredPosition = new Vector2(0f, -70f);
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
        needle.GetComponent<Image>().color = Color.white;

        var pumpGO = new GameObject("PumpButton", typeof(RectTransform), typeof(Image), typeof(PointerHoldTracker));
        pumpGO.transform.SetParent(tempGroup.transform, false);
        var pumpRect = pumpGO.GetComponent<RectTransform>();
        pumpRect.anchorMin = new Vector2(0f, 0.5f);
        pumpRect.anchorMax = new Vector2(0f, 0.5f);
        pumpRect.pivot = new Vector2(0.5f, 1f);
        pumpRect.anchoredPosition = new Vector2(barWidth * 0.5f, -40f);
        pumpRect.sizeDelta = new Vector2(160f, 64f);
        pumpGO.GetComponent<Image>().color = new Color(0.75f, 0.35f, 0.2f);
        pumpButton = pumpGO.GetComponent<PointerHoldTracker>();
        MakeText(pumpGO.transform, "PumpLabel", 16, TextAlignmentOptions.Center, Vector2.zero, new Vector2(160f, 64f))
            .text = "HOLD TO PUMP";

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
        groupRect.anchoredPosition = new Vector2(0f, -55f);
        groupRect.sizeDelta = new Vector2(96f, 96f);

        var target = new GameObject("Target", typeof(RectTransform), typeof(Image), typeof(Button));
        target.transform.SetParent(hammerGroup.transform, false);
        targetRect = target.GetComponent<RectTransform>();
        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = new Vector2(0.5f, 0.5f);
        targetRect.sizeDelta = new Vector2(96f, 96f);
        target.GetComponent<Image>().color = new Color(0.85f, 0.4f, 0.2f);
        hammerButton = target.GetComponent<Button>();
        hammerButton.onClick.AddListener(() => hammerTapped = true);

        hammerGroup.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        panel.SetActive(visible);
    }

    public IEnumerator RunTemperature(string title, float duration, float sweetMin, float sweetMax, float pumpRate, float coolRate, Action<float> onComplete)
    {
        SetVisible(true);
        tempGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";
        instructionText.text = "Hold the button to pump the bellows - stay in the green zone as long as possible";

        sweetZoneRect.anchoredPosition = new Vector2(sweetMin * barWidth, 0f);
        sweetZoneRect.sizeDelta = new Vector2((sweetMax - sweetMin) * barWidth, 26f);

        float value = 0f;
        float elapsed = 0f;
        float timeInZone = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            bool pumping = pumpButton.IsHeld;
            value += (pumping ? pumpRate : -coolRate) * Time.deltaTime;
            value = Mathf.Clamp01(value);

            if (value >= sweetMin && value <= sweetMax)
            {
                timeInZone += Time.deltaTime;
            }

            needleRect.anchoredPosition = new Vector2(value * barWidth, 0f);

            yield return null;
        }

        float quality = Mathf.Clamp01(timeInZone / duration);
        resultText.text = "Heat control " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        tempGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }

    public IEnumerator RunHammering(string title, int rounds, float circleDuration, float perfectMin, float perfectMax, float goodMin, float goodMax, Action<float> onComplete)
    {
        SetVisible(true);
        hammerGroup.SetActive(true);
        titleText.text = title;
        resultText.text = "";

        float totalScore = 0f;

        for (int round = 0; round < rounds; round++)
        {
            instructionText.text = "Tap the target to strike! (" + (round + 1) + " / " + rounds + ")";
            resultText.text = "";
            hammerTapped = false;

            float elapsed = 0f;
            bool hit = false;
            float roundScore = 0f;

            while (elapsed < circleDuration && !hit)
            {
                elapsed += Time.deltaTime;
                float scale = Mathf.Clamp01(1f - elapsed / circleDuration);
                targetRect.localScale = new Vector3(scale, scale, 1f);

                if (hammerTapped)
                {
                    hit = true;

                    if (scale >= perfectMin && scale <= perfectMax)
                    {
                        roundScore = 1f;
                        resultText.text = "Perfect!";
                    }
                    else if (scale >= goodMin && scale <= goodMax)
                    {
                        roundScore = 0.6f;
                        resultText.text = "Good";
                    }
                    else
                    {
                        roundScore = 0.15f;
                        resultText.text = "Miss";
                    }
                }

                yield return null;
            }

            if (!hit)
            {
                roundScore = 0f;
                resultText.text = "Miss";
            }

            totalScore += roundScore;
            targetRect.localScale = Vector3.one;
            yield return new WaitForSeconds(0.35f);
        }

        float quality = Mathf.Clamp01(totalScore / rounds);
        resultText.text = "Forging accuracy " + Mathf.RoundToInt(quality * 100f) + "%";
        yield return new WaitForSeconds(0.6f);

        hammerGroup.SetActive(false);
        SetVisible(false);
        onComplete?.Invoke(quality);
    }
}
