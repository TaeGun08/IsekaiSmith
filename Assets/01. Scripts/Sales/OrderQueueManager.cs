using System;
using System.Collections.Generic;
using UnityEngine;

// Runs the sales counter: a single-file line of CustomerOrders (index 0 = the one actually being
// served) fed by two separate things the player has to do in person - carry QUICK CRAFT weapons
// over from the smithy (auto-deposited into counter stock on approach, StorageDepot-style) and tap
// the front customer's speech bubble to hand some over. No per-order grade requirement anymore -
// a customer just wants RequestedCount weapons; TryFulfill spends counter stock cheapest(oldest)-
// grade-first and pays per unit at whatever grade that unit actually was, so a mid-queue forge
// upgrade never strands an order (old stock keeps selling until it's gone, then the exact same
// order keeps being served from new stock with nothing to reconcile). No patience/time limit and
// no reputation system either - customer_order_design_v7.html §0 Q3 dropped both; a customer just
// waits until stock shows up. Simulation always runs (customers keep arriving/waiting whether or
// not the player is nearby - CustomerVisualManager polls Queue to drive the physical characters);
// PlayerNear only gates deposit/fulfill, since both still require physically being at the counter.
// See customer_order_design_v7.html.
public class OrderQueueManager : MonoBehaviour
{
    [Header("Counter")]
    [SerializeField] private float interactRadius = 2.5f;
    // Conveyor-belt pacing (Pizza Ready / hyper-casual tycoon reference, 사용자 요청 2026-08-21) -
    // same one-item-at-a-time, only-while-in-zone, ramping-up rhythm as StorageDepot instead of the
    // old "dump the whole carried stack in a single instant transfer". See StorageDepot's matching
    // fields for the full rationale.
    [SerializeField] private float depositIntervalStart = 0.32f;
    [SerializeField] private float depositIntervalFloor = 0.06f;
    [SerializeField] private float depositIntervalAcceleration = 0.82f;
    // Up to 5 customers stand in a physical line at once (사용자 요청 2026-08-21: "최대 5명의
    // 손님이 줄을 서고" - Pizza Ready-style visible queue, not a single customer at a time). Only
    // queue[0] (the front of the line) is ever actually servable - TryFulfill always targets it,
    // and CustomerVisualManager only makes that one customer's speech bubble tappable - so a
    // crowded-looking line never means multiple orders are being taken at once, just multiple
    // people waiting their turn. The moment the front order is fully delivered and removed, the
    // whole line shifts forward one spot and a fresh arrival eventually fills the back on the
    // usual arrival-pacing delay.
    [SerializeField] private int maxQueueLength = 5;

    [Header("Arrival Pacing")]
    [SerializeField] private float arrivalIntervalMin = 3f;
    [SerializeField] private float arrivalIntervalMax = 5f;

    [Header("Rush Wave")]
    [SerializeField] private float waveIntervalMin = 60f;
    [SerializeField] private float waveIntervalMax = 90f;
    [SerializeField] private float waveDurationMin = 8f;
    [SerializeField] private float waveDurationMax = 12f;
    [SerializeField] private float rushArrivalIntervalMin = 1f;
    [SerializeField] private float rushArrivalIntervalMax = 2f;

    [Header("Order Content")]
    [SerializeField] private int minRequestedCount = 1;
    [SerializeField] private int maxRequestedCount = 3;

    private static readonly CraftGrade[] AscendingGrades = (CraftGrade[])Enum.GetValues(typeof(CraftGrade));

    private readonly List<CustomerOrder> queue = new List<CustomerOrder>();
    private readonly Dictionary<CraftGrade, int> counterStock = new Dictionary<CraftGrade, int>();

    private int nextOrderId;
    private float arrivalTimer;
    private float depositTimer;
    private float currentDepositInterval;
    private CarryStack playerCarryStack;

    private bool inRush;
    private float waveClock;
    private float waveClockTarget;

    public bool PlayerNear { get; private set; }
    public IReadOnlyList<CustomerOrder> Queue => queue;
    public bool InRush => inRush;

    // Lets GuidedTutorial hide its floor arrow once the player is already within interacting
    // distance, instead of a separately hardcoded distance that could drift out of sync.
    public float InteractRadius => interactRadius;

    private void Awake()
    {
        waveClockTarget = UnityEngine.Random.Range(waveIntervalMin, waveIntervalMax);
        arrivalTimer = UnityEngine.Random.Range(arrivalIntervalMin, arrivalIntervalMax);
        currentDepositInterval = depositIntervalStart;
        InteractionPadIndicator.Attach(transform, interactRadius);
    }

    private void Update()
    {
        PlayerNear = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;

        TickWave(Time.deltaTime);
        TickArrivals(Time.deltaTime);
        TickDeposit(Time.deltaTime);
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
            ? UnityEngine.Random.Range(waveDurationMin, waveDurationMax)
            : UnityEngine.Random.Range(waveIntervalMin, waveIntervalMax);
    }

    private void TickArrivals(float dt)
    {
        if (queue.Count >= maxQueueLength)
        {
            return;
        }

        arrivalTimer -= dt;
        if (arrivalTimer > 0f)
        {
            return;
        }

        arrivalTimer = NextArrivalDelay();
        int count = UnityEngine.Random.Range(minRequestedCount, maxRequestedCount + 1);
        queue.Add(new CustomerOrder(nextOrderId++, count));
    }

    private float NextArrivalDelay()
    {
        return inRush
            ? UnityEngine.Random.Range(rushArrivalIntervalMin, rushArrivalIntervalMax)
            : UnityEngine.Random.Range(arrivalIntervalMin, arrivalIntervalMax);
    }

    // Auto-deposit, same pacing as StorageDepot: standing near the counter with QUICK CRAFT
    // weapons on CarryLayer.Weapon pulls them into counter stock one at a time, ramping up the
    // longer the player stays in the zone, and only while they're actually in it (see
    // StorageDepot's matching fields for the full rationale). Grade is resolved right now
    // (ForgeUpgrade.CurrentTier), not tracked back to whenever each item was actually crafted -
    // same "resolve at deposit" convention OreBank.DepositMined already uses.
    private void TickDeposit(float dt)
    {
        if (!PlayerNear)
        {
            currentDepositInterval = depositIntervalStart;
            depositTimer = 0f;
            return;
        }

        depositTimer -= dt;
        if (depositTimer > 0f)
        {
            return;
        }

        if (playerCarryStack == null)
        {
            playerCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
            if (playerCarryStack == null)
            {
                return;
            }
        }

        if (playerCarryStack.GetCount(CarryLayer.Weapon) <= 0)
        {
            depositTimer = depositIntervalStart;
            return;
        }

        if (!playerCarryStack.TryDepositOne(CarryLayer.Weapon, transform.position + Vector3.up * 0.4f))
        {
            depositTimer = depositIntervalStart;
            return;
        }

        CraftGrade grade = ForgeUpgrade.CurrentTier;
        counterStock[grade] = GetStock(grade) + 1;

        depositTimer = currentDepositInterval;
        currentDepositInterval = Mathf.Max(depositIntervalFloor, currentDepositInterval * depositIntervalAcceleration);
    }

    private int GetStock(CraftGrade grade)
    {
        return counterStock.TryGetValue(grade, out int value) ? value : 0;
    }

    // Called when the player taps the front customer's speech bubble (see Customer/
    // CustomerVisualManager - only the front of the line gets a tappable bubble). Delivers as much
    // of the remaining request as counter stock allows, cheapest grade first - may be a partial
    // delivery if stock runs short, in which case the same order just waits for more. Pays per
    // unit actually handed over. Returns whether anything was delivered.
    public bool TryFulfill()
    {
        if (!PlayerNear || queue.Count == 0)
        {
            return false;
        }

        CustomerOrder order = queue[0];
        int remaining = order.RequestedCount - order.DeliveredCount;
        if (remaining <= 0)
        {
            return false;
        }

        int delivered = 0;
        int payout = 0;

        foreach (CraftGrade grade in AscendingGrades)
        {
            int stock = GetStock(grade);
            int take = Mathf.Min(stock, remaining - delivered);
            if (take <= 0)
            {
                continue;
            }

            counterStock[grade] = stock - take;
            payout += SalesPricing.BaseFor(grade) * take;
            delivered += take;

            if (delivered >= remaining)
            {
                break;
            }
        }

        if (delivered <= 0)
        {
            return false;
        }

        order.DeliveredCount += delivered;
        SalesCurrency.Add(payout);

        if (order.IsComplete)
        {
            queue.RemoveAt(0);
        }

        return true;
    }
}
