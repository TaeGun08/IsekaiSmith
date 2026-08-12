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

    private const float SwordSweepHalfAngle = 65f;

    // Vertical's hold keeps the blade's length axis pointing local +Y (up) - a neutral "held
    // upright" pose that never points back through the body.
    private static readonly Quaternion SwordVerticalHold = Quaternion.Euler(0f, 90f, 0f);
    // Horizontal's hold rotates the blade's length axis to point local +Z (forward, away from the
    // body) instead of -Z (back through the torso) - the original version used Euler(-90,0,0),
    // which pointed the blade backward at rest. Combined with the player not necessarily facing
    // the monster (now fixed in PlayerCombat), that's exactly what read as "clips through the
    // player's body" / "doesn't even look aimed at the monster".
    private static readonly Quaternion SwordHorizontalHold = Quaternion.Euler(90f, 0f, 0f);
    // Diagonal is just the midpoint between the two verified-safe holds above, instead of a
    // separately hand-picked Euler angle that could reintroduce the same backward-pointing bug.
    private static readonly Quaternion SwordDiagonalHold = Quaternion.Slerp(SwordVerticalHold, SwordHorizontalHold, 0.5f);

    // Three visually distinct swing shapes (user request: "사선으로 휘두르다던가 등... 종, 횡
    // 다양한 휘두름"), but ALL of them rotate around the sword's own local Z axis - the blade's
    // thickness axis, running parallel to its two flat faces. Rotating around that specific axis
    // is what makes the thin edge (not the wide flat side) lead through the arc; rotating around
    // any other axis (the original version rotated around local X for the "vertical" pattern)
    // sweeps the flat face first instead, which reads as swinging a blunt club - exactly the bug
    // reported: "검을 날 부분으로 휘둘러야지 이건 몽둥이 휘두르는거랑 차이가 없는데". Each
    // pattern's `holdOrientation` only changes which way that same edge-leading swing *looks*
    // like it's aimed (vertical/horizontal/diagonal) - never the rotation axis itself. Picked
    // randomly per swing (never repeating the immediately previous one) in PlaySwordSwing().
    private static readonly SwingPattern[] SwordPatterns =
    {
        MakeSwordPattern(SwordVerticalHold),
        MakeSwordPattern(SwordHorizontalHold),
        MakeSwordPattern(SwordDiagonalHold),
    };

    private static SwingPattern MakeSwordPattern(Quaternion holdOrientation)
    {
        return new SwingPattern
        {
            Rest = holdOrientation * Quaternion.Euler(0f, 0f, -SwordSweepHalfAngle),
            Swung = holdOrientation * Quaternion.Euler(0f, 0f, SwordSweepHalfAngle)
        };
    }

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
