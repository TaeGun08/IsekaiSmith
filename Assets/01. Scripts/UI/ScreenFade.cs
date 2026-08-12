using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Full-screen black fade overlay - self-built Canvas+Image like every other UI class in this
// project. Used to mask instant position changes (currently just the death/respawn teleport) so
// they read as a soft transition instead of a jump-cut. Self-bootstrapping singleton
// (GuidedTutorial-style). See combat_design_v1.html follow-up notes.
public class ScreenFade : MonoBehaviour
{
    private const float FadeDuration = 0.3f;

    private static ScreenFade instance;

    public static ScreenFade Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("ScreenFade");
                instance = go.AddComponent<ScreenFade>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private Image fadeImage;

    private void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("ScreenFadeCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above absolutely everything else, including welcome/help (50)
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        var imageGO = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        imageGO.transform.SetParent(canvasGO.transform, false);
        var rect = imageGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        fadeImage = imageGO.GetComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false; // must never swallow clicks, even mid-fade
    }

    // Fades to black, invokes duringBlack (position changes etc. happen invisibly there), fades
    // back in, then invokes onComplete.
    public void FadeOutAndIn(Action duringBlack, Action onComplete = null)
    {
        StartCoroutine(FadeRoutine(duringBlack, onComplete));
    }

    private IEnumerator FadeRoutine(Action duringBlack, Action onComplete)
    {
        yield return Fade(0f, 1f);
        duringBlack?.Invoke();
        yield return Fade(1f, 0f);
        onComplete?.Invoke();
    }

    private IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / FadeDuration));
            fadeImage.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        fadeImage.color = new Color(0f, 0f, 0f, to);
    }
}
