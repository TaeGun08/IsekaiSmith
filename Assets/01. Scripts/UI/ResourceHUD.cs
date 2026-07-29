using TMPro;
using UnityEngine;

public class ResourceHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text woodText;
    [SerializeField] private TMP_Text oreText;
    [SerializeField] private TMP_Text ingotText;
    [SerializeField] private TMP_Text toolText;

    private int lastWood = -1;
    private int lastOre = -1;
    private int lastIngot = -1;
    private int lastTool = -1;

    private void Update()
    {
        int wood = ResourceBank.Get(ResourceType.Wood);
        int ore = ResourceBank.Get(ResourceType.Ore);
        int ingot = ResourceBank.Get(ResourceType.Ingot);
        int tool = ResourceBank.Get(ResourceType.Tool);

        if (wood == lastWood && ore == lastOre && ingot == lastIngot && tool == lastTool)
        {
            return;
        }

        lastWood = wood;
        lastOre = ore;
        lastIngot = ingot;
        lastTool = tool;

        if (woodText != null)
        {
            woodText.text = "Wood " + wood;
        }

        if (oreText != null)
        {
            oreText.text = "Ore " + ore;
        }

        if (ingotText != null)
        {
            ingotText.text = "Ingot " + ingot;
        }

        if (toolText != null)
        {
            toolText.text = "Tool " + tool;
        }
    }
}
