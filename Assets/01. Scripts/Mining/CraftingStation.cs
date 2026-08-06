using System.Collections;
using UnityEngine;

// Unified forge workstation: furnace (melt) and anvil (hammer) are placed right next to each
// other, so interacting with either is treated as one combined crafting action instead of two
// separate stops. Flow: pick materials on the weapon silhouette -> melt (temperature minigame)
// -> hammer (hammering minigame) -> weapon is done.
public class CraftingStation : MonoBehaviour
{
    [SerializeField] private ResourceType oreType = ResourceType.Ore;
    [SerializeField] private int oreAmount = 2;
    [SerializeField] private ResourceType woodType = ResourceType.Wood;
    [SerializeField] private int woodAmount = 1;
    [SerializeField] private ResourceType manaStoneType = ResourceType.ManaStone;
    [SerializeField] private ResourceType outputType = ResourceType.Tool;
    [SerializeField] private int outputAmount = 1;
    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private string stationTitle = "Forge: Sword";

    [SerializeField] private Transform furnacePulseVisual;
    [SerializeField] private Transform anvilPulseVisual;
    [SerializeField] private float pulseStrength = 0.12f;
    [SerializeField] private float pulseSpeed = 10f;

    [Header("Smelting Minigame (Drag Pump) - drag the handle up/down; heat always fades")]
    [SerializeField] private float temperatureDuration = 9f;
    [SerializeField] private float sweetMin = 0.55f;
    [SerializeField] private float sweetMax = 0.75f;
    [SerializeField] private float strokeBump = 0.22f;
    [SerializeField] private float coolRate = 0.15f;
    [SerializeField] private float overheatPenaltyMultiplier = 1.5f;

    [Header("Hammering Minigame (Forge) - hold the marker, release on the target power %")]
    [SerializeField] private int hammerRounds = 4;
    [SerializeField] private float chargeDuration = 1.4f;
    [SerializeField] private float perfectTolerancePercent = 10f;
    [SerializeField] private float goodTolerancePercent = 30f;

    private Vector3 furnacePulseBaseScale;
    private Vector3 anvilPulseBaseScale;
    private bool isCrafting;
    private bool promptShown;

    private void Awake()
    {
        if (furnacePulseVisual != null)
        {
            furnacePulseBaseScale = furnacePulseVisual.localScale;
        }

        if (anvilPulseVisual != null)
        {
            anvilPulseBaseScale = anvilPulseVisual.localScale;
        }
    }

    private void Update()
    {
        UpdatePulse();

        if (isCrafting)
        {
            return;
        }

        bool nearPlayer = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;

        if (!nearPlayer || !HasEnoughInputs())
        {
            if (promptShown)
            {
                InteractionPromptUI.Instance.Hide();
                promptShown = false;
            }

            return;
        }

        if (!promptShown)
        {
            InteractionPromptUI.Instance.Show(
                stationTitle,
                () => StartCoroutine(CraftWithSilhouetteAndMinigames()),
                () => ApplyCraft(0.5f, 0, out _));
            promptShown = true;
        }
    }

    private bool HasEnoughInputs()
    {
        return ResourceBank.Get(oreType) >= oreAmount && ResourceBank.Get(woodType) >= woodAmount;
    }

    // Public read of the same eligibility check Update() uses for the interaction prompt -
    // lets other systems (e.g. DevAutoPlayController) know whether a craft would succeed right
    // now without duplicating the recipe check.
    public bool CanCraft => !isCrafting && HasEnoughInputs();

    // Which input the recipe is still short on - lets DevAutoPlayController target gathering by
    // actual need instead of "whichever resource node happens to be physically closer", which
    // can get stuck looping on one resource type forever if its field is nearer to the player's
    // usual path than the other.
    public bool NeedsOre => ResourceBank.Get(oreType) < oreAmount;
    public bool NeedsWood => ResourceBank.Get(woodType) < woodAmount;

    // Bypasses the silhouette/minigame flow and applies the same fixed-quality result as the
    // QUICK CRAFT button. Used by DevAutoPlayController for automated loop testing.
    public bool TryDevQuickCraft(out CraftGrade grade, out int amount)
    {
        grade = CraftGrade.Rough;
        amount = 0;

        if (!CanCraft)
        {
            return false;
        }

        grade = ApplyCraft(0.5f, 0, out amount);
        return true;
    }

    private IEnumerator CraftWithSilhouetteAndMinigames()
    {
        isCrafting = true;
        InteractionPromptUI.Instance.Hide();
        promptShown = false;

        bool loaded = false;
        int manaSpent = 0;
        yield return CraftingSilhouetteUI.Instance.RunSilhouette(stationTitle, (started, mana) =>
        {
            loaded = started;
            manaSpent = mana;
        });

        if (!loaded)
        {
            isCrafting = false;
            yield break;
        }

        float meltQuality = 0.5f;
        yield return CraftingMinigameUI.Instance.RunTemperature(
            "Melting", temperatureDuration, sweetMin, sweetMax, strokeBump, coolRate, overheatPenaltyMultiplier,
            q => meltQuality = q);

        float hammerQuality = 0.5f;
        yield return CraftingMinigameUI.Instance.RunHammering(
            "Forging", hammerRounds, chargeDuration, perfectTolerancePercent, goodTolerancePercent,
            q => hammerQuality = q);

        float quality = (meltQuality + hammerQuality) * 0.5f;
        CraftGrade grade = ApplyCraft(quality, manaSpent, out int amount);
        yield return CraftingMinigameUI.Instance.ShowGradeResult(grade, amount);
        isCrafting = false;
    }

    private CraftGrade ApplyCraft(float quality, int manaSpent, out int amount)
    {
        amount = 0;

        if (!HasEnoughInputs())
        {
            return CraftGrade.Rough;
        }

        ResourceBank.TrySpend(oreType, oreAmount);
        ResourceBank.TrySpend(woodType, woodAmount);

        if (manaSpent > 0)
        {
            int actualMana = Mathf.Min(manaSpent, ResourceBank.Get(manaStoneType));
            ResourceBank.TrySpend(manaStoneType, actualMana);
        }

        CraftGrade grade = CraftGradeUtility.GradeFor(quality);
        amount = outputAmount + CraftGradeUtility.BonusAmount(grade);
        ResourceBank.Add(outputType, amount);
        return grade;
    }

    private void UpdatePulse()
    {
        SetPulse(furnacePulseVisual, ref furnacePulseBaseScale);
        SetPulse(anvilPulseVisual, ref anvilPulseBaseScale);
    }

    private void SetPulse(Transform visual, ref Vector3 baseScale)
    {
        if (visual == null)
        {
            return;
        }

        if (!isCrafting)
        {
            visual.localScale = baseScale;
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength;
        visual.localScale = baseScale * pulse;
    }
}
