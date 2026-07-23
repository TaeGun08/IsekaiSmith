using UnityEngine;

[RequireComponent(typeof(CarryStack))]
public class PlayerMining : MonoBehaviour
{
    [SerializeField] private GameObject oreItemPrefab;

    private CarryStack carryStack;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
    }

    private void OnTriggerEnter(Collider other)
    {
        OreNode node = other.GetComponentInParent<OreNode>();
        if (node == null || carryStack.IsFull)
        {
            return;
        }

        if (node.TryCollect())
        {
            carryStack.TryAdd(oreItemPrefab, node.transform.position);
        }
    }
}
