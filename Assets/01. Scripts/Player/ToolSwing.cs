using System.Collections;
using UnityEngine;

public class ToolSwing : MonoBehaviour
{
    [SerializeField] private Transform axeTool;
    [SerializeField] private Transform pickaxeTool;
    [SerializeField] private Transform swordTool;
    [SerializeField] private float axeSwingDuration = 0.45f;
    [SerializeField] private float pickaxeSwingDuration = 0.4f;
    [SerializeField] private float swordSwingDuration = 0.32f;

    private Coroutine axeSwingRoutine;
    private Coroutine pickaxeSwingRoutine;
    private Coroutine swordSwingRoutine;
    private int lastSwordPatternIndex = -1;

    private struct SwingPattern
    {
        public Quaternion Rest;
        public Quaternion Swung;
    }

    // Three visually distinct swing shapes (user request: "사선으로 휘두르다던가 등... 종, 횡
    // 다양한 휘두름") - rotated around different local axes so they read as genuinely different
    // strikes rather than the same swing with a different duration. Picked randomly per swing
    // (never repeating the immediately previous one) in PlaySwordSwing().
    private static readonly SwingPattern[] SwordPatterns =
    {
        // Vertical overhead chop - swings top-down (rotates around local X).
        new SwingPattern { Rest = Quaternion.Euler(-70f, 0f, 0f), Swung = Quaternion.Euler(50f, 0f, 0f) },
        // Horizontal slash - swings side-to-side (rotates around local Z).
        new SwingPattern { Rest = Quaternion.Euler(0f, 0f, -70f), Swung = Quaternion.Euler(0f, 0f, 70f) },
        // Diagonal slash - corner-to-corner (X and Z rotate together).
        new SwingPattern { Rest = Quaternion.Euler(-45f, 0f, -55f), Swung = Quaternion.Euler(35f, 0f, 55f) },
    };

    private void Awake()
    {
        SetToolActive(axeTool, false);
        SetToolActive(pickaxeTool, false);
        SetToolActive(swordTool, false);
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

        // Y stays at 90 and Z sweeps from -30 to 90 for a downward pick strike.
        Quaternion restRotation = Quaternion.Euler(0f, 90f, -30f);
        Quaternion swungRotation = Quaternion.Euler(0f, 90f, 90f);
        pickaxeSwingRoutine = StartCoroutine(SwingRoutine(pickaxeTool, restRotation, swungRotation, pickaxeSwingDuration));
    }

    // Combat's attack swing (see PlayerCombat.cs) - picks one of SwordPatterns each call so
    // repeated attacks read as a varied flurry (vertical/horizontal/diagonal) instead of the same
    // single-direction chop every time.
    public void PlaySwordSwing()
    {
        if (swordTool == null)
        {
            return;
        }

        if (swordSwingRoutine != null)
        {
            StopCoroutine(swordSwingRoutine);
        }

        SwingPattern pattern = SwordPatterns[NextSwordPatternIndex()];
        swordSwingRoutine = StartCoroutine(SwingRoutine(swordTool, pattern.Rest, pattern.Swung, swordSwingDuration));
    }

    private int NextSwordPatternIndex()
    {
        if (SwordPatterns.Length <= 1)
        {
            return 0;
        }

        int index;
        do
        {
            index = Random.Range(0, SwordPatterns.Length);
        } while (index == lastSwordPatternIndex);

        lastSwordPatternIndex = index;
        return index;
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
