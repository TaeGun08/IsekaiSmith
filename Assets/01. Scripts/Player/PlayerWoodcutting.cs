using UnityEngine;

[RequireComponent(typeof(CarryStack))]
public class PlayerWoodcutting : MonoBehaviour
{
    [SerializeField] private GameObject woodItemPrefab;
    [SerializeField] private float chopInterval = 0.6f;

    private CarryStack carryStack;
    private Tree currentTree;
    private float tickTimer;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
    }

    private void OnTriggerEnter(Collider other)
    {
        Tree tree = other.GetComponentInParent<Tree>();
        if (tree != null)
        {
            currentTree = tree;
            tickTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Tree tree = other.GetComponentInParent<Tree>();
        if (tree != null && tree == currentTree)
        {
            currentTree = null;
        }
    }

    private void Update()
    {
        if (currentTree == null || !currentTree.IsAvailable || carryStack.IsFull(CarryLayer.Wood))
        {
            return;
        }

        tickTimer += Time.deltaTime;
        if (tickTimer < chopInterval)
        {
            return;
        }

        tickTimer = 0f;

        if (currentTree.TryChop(out int woodAmount))
        {
            for (int i = 0; i < woodAmount; i++)
            {
                carryStack.TryAdd(woodItemPrefab, currentTree.transform.position, CarryLayer.Wood);
            }
        }
    }
}
