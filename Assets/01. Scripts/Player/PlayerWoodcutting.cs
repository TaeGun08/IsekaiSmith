using UnityEngine;

[RequireComponent(typeof(CarryStack))]
[RequireComponent(typeof(ToolSwing))]
public class PlayerWoodcutting : MonoBehaviour
{
    [SerializeField] private GameObject woodItemPrefab;
    [SerializeField] private float chopInterval = 0.6f;

    private CarryStack carryStack;
    private ToolSwing toolSwing;
    private WoodNode currentTree;
    private float tickTimer;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
        toolSwing = GetComponent<ToolSwing>();
    }

    private void OnTriggerEnter(Collider other)
    {
        WoodNode tree = other.GetComponentInParent<WoodNode>();
        if (tree != null && tree != currentTree)
        {
            currentTree = tree;
            tickTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        WoodNode tree = other.GetComponentInParent<WoodNode>();
        if (tree != null && tree == currentTree)
        {
            currentTree = null;
        }
    }

    private void Update()
    {
        if (currentTree == null || !currentTree.IsAvailable)
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
            toolSwing.PlayAxeSwing();

            for (int i = 0; i < woodAmount; i++)
            {
                carryStack.TryAdd(woodItemPrefab, currentTree.transform.position, CarryLayer.Wood);
            }
        }
    }
}
