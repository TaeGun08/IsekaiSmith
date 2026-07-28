using System.Collections;
using UnityEngine;

public class SmithyRoofFade : MonoBehaviour
{
    [SerializeField] private Renderer roofRenderer;
    [SerializeField] private float fadeDuration = 0.25f;
    [SerializeField] private float hiddenAlpha = 0.15f;

    private Material roofMaterial;
    private Color baseColor;
    private Coroutine fadeRoutine;
    private int playersInside;

    private void Awake()
    {
        if (roofRenderer != null)
        {
            roofMaterial = roofRenderer.material;
            baseColor = roofMaterial.color;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerMotor>() == null)
        {
            return;
        }

        playersInside++;
        PlayFade(hiddenAlpha);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerMotor>() == null)
        {
            return;
        }

        playersInside = Mathf.Max(0, playersInside - 1);

        if (playersInside == 0)
        {
            PlayFade(baseColor.a);
        }
    }

    private void PlayFade(float targetAlpha)
    {
        if (roofMaterial == null)
        {
            return;
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = roofMaterial.color.a;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            Color c = baseColor;
            c.a = Mathf.Lerp(startAlpha, targetAlpha, t);
            roofMaterial.color = c;
            yield return null;
        }

        Color final = baseColor;
        final.a = targetAlpha;
        roofMaterial.color = final;
        fadeRoutine = null;
    }
}
