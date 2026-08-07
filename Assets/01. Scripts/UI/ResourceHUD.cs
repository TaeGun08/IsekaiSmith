using TMPro;
using UnityEngine;

public class ResourceHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text woodText;
    [SerializeField] private TMP_Text oreText;
    [SerializeField] private TMP_Text manaText;
    [SerializeField] private TMP_Text toolText;
    [SerializeField] private TMP_Text goldText;

    private int lastWood = -1;
    private int lastOre = -1;
    private int lastMana = -1;
    private int lastTool = -1;
    private int lastGold = -1;

    // ResourceHUD is the one always-present, scene-wired MonoBehaviour, so it's the bootstrap
    // point for the self-built TutorialUI (see tutorial_design.html) - not because the two are
    // conceptually related, just because something has to make the first call.
    private void Start()
    {
        TutorialUI.Instance.ShowIfFirstTime();
    }

    private void Update()
    {
        int wood = ResourceBank.Get(ResourceType.Wood);
        int ore = ResourceBank.Get(ResourceType.Ore);
        int mana = ResourceBank.Get(ResourceType.ManaStone);
        int tool = ToolInventory.Total;
        int gold = SalesCurrency.Gold;

        if (wood == lastWood && ore == lastOre && mana == lastMana && tool == lastTool && gold == lastGold)
        {
            return;
        }

        lastWood = wood;
        lastOre = ore;
        lastMana = mana;
        lastTool = tool;
        lastGold = gold;

        if (woodText != null)
        {
            woodText.text = "Wood " + wood;
        }

        if (oreText != null)
        {
            oreText.text = "Ore " + ore;
        }

        if (manaText != null)
        {
            manaText.text = "Mana " + mana;
        }

        if (toolText != null)
        {
            toolText.text = "Tool " + tool;
        }

        if (goldText != null)
        {
            goldText.text = "Gold " + gold;
        }
    }
}
