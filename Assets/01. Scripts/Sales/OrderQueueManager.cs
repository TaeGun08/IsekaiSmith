using System.Collections.Generic;
using UnityEngine;

// Runs the sales counter's order logic: fixed slots hold CustomerOrders, empty slots refill on a
// short delay (shorter during rush hour), patience ticks down per slot, and TryFulfill() checks a
// slot against ToolInventory for a payout. Simulation always runs (customers keep arriving/waiting/
// leaving whether or not the player is nearby - a shop that's only "open" when you're standing at
// the counter felt lifeless in testing); PlayerNear only gates TryFulfill, since you still have to
// physically walk up to hand over the goods. Pure order logic - CustomerVisualManager (sibling
// component) polls Slots/InRush to drive the physical customer characters; this class has no idea
// they exist. See customer_order_design_v4.html.
public class OrderQueueManager : MonoBehaviour
{
    [Header("Counter")]
    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private int slotCount = 3;

    [Header("Refill Pacing")]
    [SerializeField] private float refillDelayMin = 1f;
    [SerializeField] private float refillDelayMax = 2f;

    [Header("Rush Wave")]
    [SerializeField] private float waveIntervalMin = 60f;
    [SerializeField] private float waveIntervalMax = 90f;
    [SerializeField] private float waveDurationMin = 8f;
    [SerializeField] private float waveDurationMax = 12f;
    [SerializeField] private float rushRefillDelayMin = 0.3f;
    [SerializeField] private float rushRefillDelayMax = 0.6f;

    [Header("Order Content")]
    [SerializeField] private CraftGrade minGradeFloor = CraftGrade.Rough;
    // Capped at Common ("일반 등급") per playtest feedback - Superior+ felt out of reach for
    // early play since QUICK CRAFT only ever produces Fine and precise crafting needs real skill.
    [SerializeField] private CraftGrade minGradeCeiling = CraftGrade.Common;
    [SerializeField] private float patienceForRough = 25f;
    [SerializeField] private float patiencePerGradeStep = 6f;

    [Header("Payout")]
    [SerializeField] private float speedBonusMax = 0.5f;

    private CustomerOrder[] slots;
    private float[] refillTimers;
    private int nextOrderId;

    private bool inRush;
    private float waveClock;
    private float waveClockTarget;

    public bool PlayerNear { get; private set; }
    public IReadOnlyList<CustomerOrder> Slots => slots;
    public bool InRush => inRush;

    // Lets GuidedTutorial hide its floor arrow once the player is already within interacting
    // distance, instead of a separately hardcoded distance that could drift out of sync.
    public float InteractRadius => interactRadius;

    private void Awake()
    {
        slots = new CustomerOrder[slotCount];
        refillTimers = new float[slotCount];
        waveClockTarget = Random.Range(waveIntervalMin, waveIntervalMax);

        for (int i = 0; i < slotCount; i++)
        {
            refillTimers[i] = Random.Range(refillDelayMin, refillDelayMax);
        }

        InteractionPadIndicator.Attach(transform, interactRadius);
    }

    private void Update()
    {
        PlayerNear = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;

        TickWave(Time.deltaTime);
        TickSlots(Time.deltaTime);
    }

    private void TickWave(float dt)
    {
        waveClock += dt;
        if (waveClock < waveClockTarget)
        {
            return;
        }

        waveClock = 0f;
        inRush = !inRush;
        waveClockTarget = inRush
            ? Random.Range(waveDurationMin, waveDurationMax)
            : Random.Range(waveIntervalMin, waveIntervalMax);
    }

    private void TickSlots(float dt)
    {
        for (int i = 0; i < slotCount; i++)
        {
            if (slots[i] == null)
            {
                refillTimers[i] -= dt;
                if (refillTimers[i] <= 0f)
                {
                    SpawnOrder(i);
                }

                continue;
            }

            slots[i].PatienceRemaining -= dt;
            if (slots[i].PatienceRemaining <= 0f)
            {
                ExpireOrder(i);
            }
        }
    }

    private void SpawnOrder(int slotIndex)
    {
        CraftGrade minGrade = (CraftGrade)Random.Range((int)minGradeFloor, (int)minGradeCeiling + 1);
        float patience = patienceForRough + patiencePerGradeStep * (int)minGrade;
        slots[slotIndex] = new CustomerOrder(nextOrderId++, minGrade, patience);
    }

    private void ExpireOrder(int slotIndex)
    {
        slots[slotIndex] = null;
        refillTimers[slotIndex] = NextRefillDelay();
        Reputation.OnOrderFailed();
    }

    private float NextRefillDelay()
    {
        return inRush
            ? Random.Range(rushRefillDelayMin, rushRefillDelayMax)
            : Random.Range(refillDelayMin, refillDelayMax);
    }

    // Called when the player taps a customer's speech bubble (see Customer/CustomerVisualManager).
    // Returns whether it was fulfilled.
    public bool TryFulfill(int slotIndex)
    {
        if (!PlayerNear || slotIndex < 0 || slotIndex >= slots.Length)
        {
            return false;
        }

        CustomerOrder order = slots[slotIndex];
        if (order == null || !ToolInventory.TrySpendAtLeast(order.MinGrade, out CraftGrade spentGrade))
        {
            return false;
        }

        float speedBonus = order.Patience01 * speedBonusMax;
        int pay = Mathf.RoundToInt(SalesPricing.BaseFor(spentGrade) * (1f + speedBonus) * Reputation.Multiplier);
        SalesCurrency.Add(pay);
        Reputation.OnOrderSuccess();

        slots[slotIndex] = null;
        refillTimers[slotIndex] = NextRefillDelay();
        return true;
    }
}
