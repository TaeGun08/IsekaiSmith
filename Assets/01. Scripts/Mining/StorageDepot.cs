using UnityEngine;

public class StorageDepot : MonoBehaviour
{
    [SerializeField] private CarryLayer acceptedLayer = CarryLayer.Wood;
    [SerializeField] private float depositRadius = 2f;
    [SerializeField] private float depositInterval = 0.5f;

    public static int TotalWood { get; private set; }
    public static int TotalOre { get; private set; }

    private int storedAmount;
    private float depositTimer;

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

        CarryStack carryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
        if (carryStack == null)
        {
            return;
        }

        int amount = carryStack.GetCount(acceptedLayer);
        if (amount <= 0)
        {
            return;
        }

        depositTimer = depositInterval;
        storedAmount += amount;
        carryStack.Clear(acceptedLayer);

        if (acceptedLayer == CarryLayer.Wood)
        {
            TotalWood += amount;
        }
        else
        {
            TotalOre += amount;
        }
    }
}
