using UnityEngine;

// Soft, no-game-over penalty for missed orders - only ever shows up as a payout multiplier
// (customer_order_design.html §6). Split out of OrderQueueManager so order-flow logic doesn't
// also have to own reputation math.
public static class Reputation
{
    private const float Max = 100f;
    private const float Min = 40f;
    private const float FailPenalty = 8f;
    private const float RecoverStep = 2f;
    private const int RecoverEvery = 3;

    private static float percent = Max;
    private static int successesSinceRecover;

    public static float Percent => percent;
    public static float Multiplier => percent / 100f;

    public static void OnOrderSuccess()
    {
        successesSinceRecover++;
        if (successesSinceRecover < RecoverEvery)
        {
            return;
        }

        successesSinceRecover = 0;
        percent = Mathf.Min(Max, percent + RecoverStep);
    }

    public static void OnOrderFailed()
    {
        successesSinceRecover = 0;
        percent = Mathf.Max(Min, percent - FailPenalty);
    }
}
