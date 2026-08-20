using UnityEngine;

// One unified deposit point (사용자 요청 2026-08-21: "보관함은 하나로 통일하자") - accepts every
// raw-gather layer (Wood/Ore/ManaStone) through the same box instead of a separate crate per
// resource. Weapon deposits still go through OrderQueueManager's own counter-side logic, not here
// - selling is a different destination/purpose than raw-material storage.
public class StorageDepot : MonoBehaviour
{
    [SerializeField]
    private CarryLayer[] acceptedLayers = { CarryLayer.Wood, CarryLayer.Ore, CarryLayer.ManaStone };
    [SerializeField] private float depositRadius = 2f;

    // Conveyor-belt pacing (Pizza Ready / hyper-casual tycoon reference, 사용자 요청) instead of the
    // old "dump the whole carried stack in one instant transfer" - each item leaves the player's
    // back one at a time, and only while they're actually standing in the zone (leaving mid-stream
    // simply stops the next pull; whatever's already mid-flight just finishes its short flight).
    // The interval between pulls starts slow and ramps up the longer the player stays put, instead
    // of a single fixed rate, so a big haul visibly "spins up" instead of ticking along uniformly.
    [SerializeField] private float depositIntervalStart = 0.32f;
    [SerializeField] private float depositIntervalFloor = 0.06f;
    [SerializeField] private float depositIntervalAcceleration = 0.82f;

    private readonly int[] storedAmounts = new int[System.Enum.GetValues(typeof(CarryLayer)).Length];
    private float depositTimer;
    private float currentInterval;
    private CarryStack playerCarryStack;

    public int StoredAmount(CarryLayer layer) => storedAmounts[(int)layer];

    // Lets DevAutoPlayController (and anything else) ask "what does this depot even take" without
    // hardcoding the layer list a second time - relevant now that a depot can accept more than one.
    public CarryLayer[] AcceptedLayers => acceptedLayers;

    // Lets GuidedTutorial hide its floor arrow once the player is already within depositing
    // distance, instead of a separately hardcoded distance that could drift out of sync.
    public float DepositRadius => depositRadius;

    private void Awake()
    {
        InteractionPadIndicator.Attach(transform, depositRadius);
        currentInterval = depositIntervalStart;
    }

    private void Update()
    {
        if (PlayerMotor.Instance == null)
        {
            return;
        }

        float sqrDist = (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude;
        if (sqrDist > depositRadius * depositRadius)
        {
            // Left the zone (or never entered) - the ramp-up resets so the next visit starts slow
            // again too, same "spin up from a stop" feel every time rather than staying fast
            // forever once triggered once.
            currentInterval = depositIntervalStart;
            depositTimer = 0f;
            return;
        }

        depositTimer -= Time.deltaTime;
        if (depositTimer > 0f)
        {
            return;
        }

        // Cached once - PlayerMotor.Instance is a singleton for the whole session, so its
        // CarryStack child never changes; no need to re-resolve it every idle frame in range.
        if (playerCarryStack == null)
        {
            playerCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
            if (playerCarryStack == null)
            {
                return;
            }
        }

        if (!TryDepositOneFromAnyLayer(playerCarryStack))
        {
            // Nothing to take right now - don't advance the acceleration ramp for an empty check;
            // just wait a beat before checking again.
            depositTimer = depositIntervalStart;
            return;
        }

        depositTimer = currentInterval;
        currentInterval = Mathf.Max(depositIntervalFloor, currentInterval * depositIntervalAcceleration);
    }

    // Tries each accepted layer in serialized order and pulls exactly one item from the first one
    // that has anything carried - simple fixed priority (Wood before Ore before ManaStone by
    // default) rather than interleaving, so a mixed haul drains one pile at a time instead of
    // hopping between them every tick.
    private bool TryDepositOneFromAnyLayer(CarryStack carryStack)
    {
        for (int i = 0; i < acceptedLayers.Length; i++)
        {
            CarryLayer layer = acceptedLayers[i];
            if (carryStack.GetCount(layer) <= 0)
            {
                continue;
            }

            if (!carryStack.TryDepositOne(layer, transform.position + Vector3.up * 0.4f))
            {
                continue;
            }

            storedAmounts[(int)layer]++;
            ResourceBank.Add(ResourceTypeFor(layer), 1);

            // Ore additionally rolls into the graded bank (weapon_diversity_design_v1.html §3) -
            // ResourceBank.Ore above is left untouched on purpose (GuidedTutorial watches it to
            // detect "ore was deposited"), OreBank is the new real source of truth crafting spends
            // from.
            if (layer == CarryLayer.Ore)
            {
                OreBank.DepositMined(1);
            }

            // Mana additionally rolls into the graded bank (mana_grade_and_ui_design_v1.html §1) -
            // same parallel-write pattern as Ore above.
            if (layer == CarryLayer.ManaStone)
            {
                ManaBank.DepositGathered(1);
            }

            return true;
        }

        return false;
    }

    private static ResourceType ResourceTypeFor(CarryLayer layer)
    {
        switch (layer)
        {
            case CarryLayer.Wood:
                return ResourceType.Wood;
            case CarryLayer.ManaStone:
                return ResourceType.ManaStone;
            default:
                return ResourceType.Ore;
        }
    }
}
