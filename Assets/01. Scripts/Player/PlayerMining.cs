using UnityEngine;

[RequireComponent(typeof(CarryStack))]
[RequireComponent(typeof(ToolSwing))]
public class PlayerMining : MonoBehaviour
{
    [SerializeField] private GameObject oreItemPrefab;

    private CarryStack carryStack;
    private ToolSwing toolSwing;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
        toolSwing = GetComponent<ToolSwing>();
    }

    private void OnTriggerEnter(Collider other)
    {
        OreNode node = other.GetComponentInParent<OreNode>();
        if (node == null || carryStack.IsFull(CarryLayer.Ore))
        {
            return;
        }

        if (node.TryCollect())
        {
            toolSwing.PlayPickaxeSwing();
            carryStack.TryAdd(oreItemPrefab, node.transform.position, CarryLayer.Ore);
        }
    }
}
