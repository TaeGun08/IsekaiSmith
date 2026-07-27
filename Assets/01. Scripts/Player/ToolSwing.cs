using System.Collections;
using UnityEngine;

public class ToolSwing : MonoBehaviour
{
    [SerializeField] private Transform axeTool;
    [SerializeField] private Transform pickaxeTool;
    [SerializeField] private float swingDuration = 0.25f;
    [SerializeField] private float swingAngle = 70f;

    private Quaternion axeRestRotation;
    private Quaternion pickaxeRestRotation;
    private Coroutine axeSwingRoutine;
    private Coroutine pickaxeSwingRoutine;

    private void Awake()
    {
        if (axeTool != null)
        {
            axeRestRotation = axeTool.localRotation;
        }

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

        // Sideways swing: rotate around the axis perpendicular to the handle
        // (not the handle's own axis, or the head just spins in place like a fan).
        Quaternion swungRotation = axeRestRotation * Quaternion.Euler(0f, 0f, -swingAngle);
        axeSwingRoutine = StartCoroutine(SwingRoutine(axeTool, axeRestRotation, swungRotation));
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
        pickaxeSwingRoutine = StartCoroutine(SwingRoutine(pickaxeTool, pickaxeRestRotation, swungRotation));
    }

    private IEnumerator SwingRoutine(Transform tool, Quaternion restRotation, Quaternion swungRotation)
    {
        SetToolActive(tool, true);

        float elapsed = 0f;
        while (elapsed < swingDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin(Mathf.Clamp01(elapsed / swingDuration) * Mathf.PI);
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
