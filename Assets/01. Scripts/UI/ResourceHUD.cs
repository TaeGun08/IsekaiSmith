using TMPro;
using UnityEngine;

public class ResourceHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text woodText;
    [SerializeField] private TMP_Text oreText;
    [SerializeField] private TMP_Text manaText;
    [SerializeField] private TMP_Text toolText;
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private TMP_Text reputationText;

    private const int HudFontSize = 30;
    private const float HudLineHeight = 42f;
    private const float HudTopPadding = 12f;
    private const float HudMinPanelWidth = 260f;

    private int lastWood = -1;
    private int lastOre = -1;
    private int lastMana = -1;
    private int lastTool = -1;
    private int lastGold = -1;
    private int lastReputation = -1;

    private void Awake()
    {
        ApplyMobileFriendlySizing();
    }

    // ResourceHUD is the one always-present, scene-wired MonoBehaviour, so it's the bootstrap
    // point for every self-built system that needs one (GuidedTutorial, and now combat - see
    // combat_design_v1.html §5) - not because they're conceptually related, just because
    // something has to make the first call.
    private void Start()
    {
        GuidedTutorial.Instance.ShowIfFirstTime();
        FieldMonsterSpawner.Instance.Bootstrap();
        PlayerCombat.Instance.Activate();
        PlayerHealthHUD.Instance.Show();
        ManaStoneDepotBootstrap.Instance.Bootstrap();
        PlayerDeathPresentation.Instance.Activate();
    }

    // These six text objects were originally sized (fontSize 22) and packed (30px line spacing)
    // for a much denser layout than a real phone screen needs - 22 in this 1080-reference canvas
    // renders well under 10dp on a typical device, under any reasonable readable minimum. Resized
    // and respaced here in code (rather than hand-editing the scene) since it's pure styling, not
    // a structural change - also self-heals if the scene's baked values ever drift again.
    private void ApplyMobileFriendlySizing()
    {
        TMP_Text[] lines = { woodText, oreText, manaText, toolText, goldText, reputationText };
        RectTransform panel = null;

        for (int i = 0; i < lines.Length; i++)
        {
            TMP_Text line = lines[i];
            if (line == null)
            {
                continue;
            }

            line.fontSize = HudFontSize;
            RectTransform rect = line.rectTransform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -HudTopPadding - i * HudLineHeight);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, HudLineHeight - 4f);
            panel = rect.parent as RectTransform;
        }

        if (panel != null)
        {
            float height = HudTopPadding + lines.Length * HudLineHeight + 10f;
            panel.sizeDelta = new Vector2(Mathf.Max(panel.sizeDelta.x, HudMinPanelWidth), height);
        }
    }

    private void Update()
    {
        int wood = ResourceBank.Get(ResourceType.Wood);
        // Graded stock (OreBank), not the flat ResourceBank.Ore count - that one is now a
        // write-only "lifetime deposited" signal GuidedTutorial reads, no longer actual current
        // stock once crafting started spending from OreBank instead. See
        // weapon_diversity_design_v1.html §3.
        int ore = OreBank.TotalCurrent;
        int mana = ResourceBank.Get(ResourceType.ManaStone);
        int tool = ToolInventory.Total;
        int gold = SalesCurrency.Gold;
        int reputation = Mathf.RoundToInt(Reputation.Percent);

        if (wood == lastWood && ore == lastOre && mana == lastMana && tool == lastTool
            && gold == lastGold && reputation == lastReputation)
        {
            return;
        }

        lastWood = wood;
        lastOre = ore;
        lastMana = mana;
        lastTool = tool;
        lastGold = gold;
        lastReputation = reputation;

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

        if (reputationText != null)
        {
            reputationText.text = "Reputation " + reputation + "%";
        }
    }
}
