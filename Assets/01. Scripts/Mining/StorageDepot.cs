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

        ResourceType type = acceptedLayer == CarryLayer.Wood ? ResourceType.Wood : ResourceType.Ore;
        ResourceBank.Add(type, amount);
    }
}
