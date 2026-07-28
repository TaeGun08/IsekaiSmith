using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SmithyRoofFade : MonoBehaviour
{
    [SerializeField] private Renderer[] roofRenderers;
    [SerializeField] private float fadeDuration = 0.25f;
    [SerializeField] private float hiddenAlpha = 0.15f;

    private readonly List<Material> roofMaterials = new List<Material>();
    private Color baseColor;
    private Coroutine fadeRoutine;
    private int playersInside;

    private void Awake()
    {
        if (roofRenderers == null)
        {
            return;
        }

        for (int i = 0; i < roofRenderers.Length; i++)
        {
            if (roofRenderers[i] == null)
            {
                continue;
            }

            Material instanceMaterial = roofRenderers[i].material;
            roofMaterials.Add(instanceMaterial);
            baseColor = instanceMaterial.color;
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
        if (roofMaterials.Count == 0)
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
        float startAlpha = roofMaterials[0].color.a;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            SetAlpha(alpha);
            yield return null;
        }

        SetAlpha(targetAlpha);
        fadeRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        Color c = baseColor;
        c.a = alpha;

        for (int i = 0; i < roofMaterials.Count; i++)
        {
            roofMaterials[i].color = c;
        }
    }
}
