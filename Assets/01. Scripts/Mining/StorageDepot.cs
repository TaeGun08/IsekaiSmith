using UnityEngine;

public class StorageDepot : MonoBehaviour
{
    [SerializeField] private float depositRadius = 2f;
    [SerializeField] private float depositInterval = 0.5f;

    private int storedWood;
    private int storedOre;
    private float depositTimer;

    public int StoredWood => storedWood;
    public int StoredOre => storedOre;

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

        depositTimer = depositInterval;
        DepositAll(carryStack);
    }

    private void DepositAll(CarryStack carryStack)
    {
        int wood = carryStack.GetCount(CarryLayer.Wood);
        if (wood > 0)
        {
            storedWood += wood;
            carryStack.Clear(CarryLayer.Wood);
        }

        int ore = carryStack.GetCount(CarryLayer.Ore);
        if (ore > 0)
        {
            storedOre += ore;
            carryStack.Clear(CarryLayer.Ore);
        }
    }
}
