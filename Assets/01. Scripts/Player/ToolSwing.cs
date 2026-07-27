using System.Collections;
using UnityEngine;

public class ToolSwing : MonoBehaviour
{
    [SerializeField] private Transform axeTool;
    [SerializeField] private Transform pickaxeTool;
    [SerializeField] private float axeSwingDuration = 0.45f;
    [SerializeField] private float pickaxeSwingDuration = 0.25f;
    [SerializeField] private float swingAngle = 70f;

    private Quaternion pickaxeRestRotation;
    private Coroutine axeSwingRoutine;
    private Coroutine pickaxeSwingRoutine;

    private void Awake()
    {
        if (pickaxeTool != null)
        {
            pickaxeRestRotation = pickaxeTool.localRotation;
        }

        SetToolActive(axeTool, false);
        SetToolActive(pickaxeTool, false);
    }

    public void PlayAxeSwing()
    {
        if (axeTool == null)
        {
            return;
        }

        if (axeSwingRoutine != null)
        {
            StopCoroutine(axeSwingRoutine);
        }

        // Axe lies on its side (X = -90) and swings sideways by sweeping Z from -50 to -180.
        Quaternion restRotation = Quaternion.Euler(-90f, 0f, -50f);
        Quaternion swungRotation = Quaternion.Euler(-90f, 0f, -180f);
        axeSwingRoutine = StartCoroutine(SwingRoutine(axeTool, restRotation, swungRotation, axeSwingDuration));
    }

    public void PlayPickaxeSwing()
    {
        if (pickaxeTool == null)
        {
            return;
        }

        if (pickaxeSwingRoutine != null)
        {
            StopCoroutine(pickaxeSwingRoutine);
        }

        // Downward strike.
        Quaternion swungRotation = pickaxeRestRotation * Quaternion.Euler(-swingAngle, 0f, 0f);
        pickaxeSwingRoutine = StartCoroutine(SwingRoutine(pickaxeTool, pickaxeRestRotation, swungRotation, pickaxeSwingDuration));
    }

    private IEnumerator SwingRoutine(Transform tool, Quaternion restRotation, Quaternion swungRotation, float duration)
    {
        SetToolActive(tool, true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI);
            tool.localRotation = Quaternion.Slerp(restRotation, swungRotation, t);
            yield return null;
        }

        tool.localRotation = restRotation;
        SetToolActive(tool, false);
    }

    private static void SetToolActive(Transform tool, bool active)
    {
        if (tool != null)
        {
            tool.gameObject.SetActive(active);
        }
    }
}
