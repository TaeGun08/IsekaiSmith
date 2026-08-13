using UnityEngine;

public class StorageDepot : MonoBehaviour
{
    [SerializeField] private CarryLayer acceptedLayer = CarryLayer.Wood;
    [SerializeField] private float depositRadius = 2f;
    [SerializeField] private float depositInterval = 0.5f;

    private int storedAmount;
    private float depositTimer;
    private CarryStack playerCarryStack;

    public CarryLayer AcceptedLayer => acceptedLayer;
    public int StoredAmount => storedAmount;

    // Lets GuidedTutorial hide its floor arrow once the player is already within depositing
    // distance, instead of a separately hardcoded distance that could drift out of sync.
    public float DepositRadius => depositRadius;

    // Lets code-built depots (e.g. ManaStoneDepotBootstrap) configure which layer they accept
    // after AddComponent<StorageDepot>() - scene-placed crates keep using the serialized
    // Inspector value instead.
    public void SetAcceptedLayer(CarryLayer layer)
    {
        acceptedLayer = layer;
    }

    private void Awake()
    {
        InteractionPadIndicator.Attach(transform, depositRadius);
    }

    private void Update()
    {
        if (PlayerMotor.Instance == null)
        {
            return;
        }

        depositTimer -= Time.deltaTime;
        if (depositTimer > 0f)
        {
            return;
        }

        float sqrDist = (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude;
        if (sqrDist > depositRadius * depositRadius)
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

        CarryStack carryStack = playerCarryStack;
        int amount = carryStack.GetCount(acceptedLayer);
        if (amount <= 0)
        {
            return;
        }

        depositTimer = depositInterval;
        storedAmount += amount;
        carryStack.Deposit(acceptedLayer, transform.position + Vector3.up * 0.4f);

        ResourceBank.Add(ResourceTypeFor(acceptedLayer), amount);

        // Ore additionally rolls into the graded bank (weapon_diversity_design_v1.html §3) -
        // ResourceBank.Ore above is left untouched on purpose (GuidedTutorial watches it to
        // detect "ore was deposited"), OreBank is the new real source of truth crafting spends
        // from.
        if (acceptedLayer == CarryLayer.Ore)
        {
            OreBank.DepositMined(amount);
        }
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
