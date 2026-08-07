using UnityEngine;

// Plain data for one order occupying a counter slot - no MonoBehaviour, so OrderQueueManager can
// hold a plain array of these instead of juggling child GameObjects for state that never needs a
// Transform of its own.
public class CustomerOrder
{
    public readonly int Id;
    public readonly CraftGrade MinGrade;
    public readonly float PatienceMax;

    public float PatienceRemaining;

    public CustomerOrder(int id, CraftGrade minGrade, float patienceMax)
    {
        Id = id;
        MinGrade = minGrade;
        PatienceMax = patienceMax;
        PatienceRemaining = patienceMax;
    }

    public float Patience01 => PatienceMax > 0f ? Mathf.Clamp01(PatienceRemaining / PatienceMax) : 0f;
}
