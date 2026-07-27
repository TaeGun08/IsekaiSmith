using System.Collections;
using UnityEngine;

public class ToolSwing : MonoBehaviour
{
    [SerializeField] private Transform axeTool;
    [SerializeField] private Transform pickaxeTool;
    [SerializeField] private float swingDuration = 0.25f;
    [SerializeField] private float swingAngle = 70f;

    private Coroutine axeSwingRoutine;
    private Coroutine pickaxeSwingRoutine;

    private void Awake()
    {
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

        axeSwingRoutine = StartCoroutine(SwingRoutine(axeTool));
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

        pickaxeSwingRoutine = StartCoroutine(SwingRoutine(pickaxeTool));
    }

    private IEnumerator SwingRoutine(Transform tool)
    {
        SetToolActive(tool, true);

        Quaternion start = Quaternion.identity;
        Quaternion swung = Quaternion.Euler(-swingAngle, 0f, 0f);

        float elapsed = 0f;
        while (elapsed < swingDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin(Mathf.Clamp01(elapsed / swingDuration) * Mathf.PI);
            tool.localRotation = Quaternion.Slerp(start, swung, t);
            yield return null;
        }

        tool.localRotation = start;
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
