using UnityEngine;

[RequireComponent(typeof(CarryStack))]
[RequireComponent(typeof(ToolSwing))]
public class PlayerMining : MonoBehaviour
{
    [SerializeField] private GameObject oreItemPrefab;
    [SerializeField] private float mineInterval = 0.6f;

    private CarryStack carryStack;
    private ToolSwing toolSwing;
    private OreNode currentNode;
    private float tickTimer;

    private void Awake()
    {
        carryStack = GetComponent<CarryStack>();
        toolSwing = GetComponent<ToolSwing>();
    }

    private void OnTriggerEnter(Collider other)
    {
        OreNode node = other.GetComponentInParent<OreNode>();
        if (node != null && node != currentNode)
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
        if (currentNode == null || !currentNode.IsAvailable)
        {
            return;
        }

        if (PlayerMotor.Instance != null && !PlayerMotor.Instance.HasMovementInput)
        {
            PlayerMotor.Instance.FaceTarget(currentNode.transform.position);
        }

        tickTimer += Time.deltaTime;
        if (tickTimer < mineInterval)
        {
            return;
        }

        tickTimer = 0f;

        if (currentNode.TryMine(out int oreAmount))
        {
            toolSwing.PlayPickaxeSwing();

            for (int i = 0; i < oreAmount; i++)
            {
                carryStack.TryAdd(oreItemPrefab, currentNode.transform.position, CarryLayer.Ore);
            }
        }
    }
}
