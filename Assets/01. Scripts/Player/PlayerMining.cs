using UnityEngine;

[RequireComponent(typeof(CarryStack))]
public class PlayerMining : MonoBehaviour
{
    [SerializeField] private GameObject oreItemPrefab;

    private CarryStack carryStack;
    private OreNode currentNode;
    private float tickTimer;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
    }

    private void OnTriggerEnter(Collider other)
    {
        OreNode node = other.GetComponentInParent<OreNode>();
        if (node != null)
        {
            currentNode = node;
            tickTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        OreNode node = other.GetComponentInParent<OreNode>();
        if (node != null && node == currentNode)
        {
            currentNode = null;
        }
    }

    private void Update()
    {
        if (currentNode == null || carryStack.IsFull || currentNode.IsDepleted)
        {
            return;
        }

        tickTimer += Time.deltaTime;
        if (tickTimer < currentNode.MineTickInterval)
        {
            return;
        }

        tickTimer = 0f;
        if (currentNode.TryMineTick())
        {
            carryStack.TryAdd(oreItemPrefab);
        }
    }
}
