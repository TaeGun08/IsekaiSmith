using UnityEngine;

public enum PulseHitQuality
{
    Clean,
    Glancing,
    Miss
}

// Pure timing-judgement logic for the furnace's rhythm pump minigame - no MonoBehaviour/UI
// dependency, so the timing rules can be tuned/reused independently of how they're rendered.
public static class PulsePump
{
    private const float CleanWindowFraction = 0.08f;
    private const float GlancingWindowFraction = 0.22f;

    // phase: 0..1 position within the current beat cycle - the beat itself sits at phase 0/1.
    public static PulseHitQuality Judge(float phase, float toleranceMultiplier = 1f)
    {
        float distanceToBeat = Mathf.Min(phase, 1f - phase);
        float cleanWindow = CleanWindowFraction * toleranceMultiplier;
        float glancingWindow = GlancingWindowFraction * toleranceMultiplier;

        if (distanceToBeat <= cleanWindow)
        {
            return PulseHitQuality.Clean;
        }

        if (distanceToBeat <= glancingWindow)
        {
            return PulseHitQuality.Glancing;
        }

        return PulseHitQuality.Miss;
    }

    public static float ComboMultiplier(int comboCount)
    {
        if (comboCount >= 4)
        {
            return 1.25f;
        }

        if (comboCount >= 2)
        {
            return 1.1f;
        }

        return 1f;
    }
}
