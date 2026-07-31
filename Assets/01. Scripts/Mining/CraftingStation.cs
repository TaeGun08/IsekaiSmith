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

    [Header("Temperature Minigame (Melt)")]
    [SerializeField] private float temperatureDuration = 4f;
    [SerializeField] private float sweetMin = 0.55f;
    [SerializeField] private float sweetMax = 0.75f;
    [SerializeField] private float pumpRate = 0.7f;
    [SerializeField] private float coolRate = 0.35f;

    [Header("Hammering Minigame (Forge)")]
    [SerializeField] private int hammerRounds = 4;
    [SerializeField] private float hammerRoundDuration = 1.1f;
    [SerializeField] private float perfectMin = 0.35f;
    [SerializeField] private float perfectMax = 0.5f;
    [SerializeField] private float goodMin = 0.18f;
    [SerializeField] private float goodMax = 0.65f;

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
                () => ApplyCraft(0.5f, 0));
            promptShown = true;
        }
    }

    private bool HasEnoughInputs()
    {
        return ResourceBank.Get(oreType) >= oreAmount && ResourceBank.Get(woodType) >= woodAmount;
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
            "Melting", temperatureDuration, sweetMin, sweetMax, pumpRate, coolRate,
            q => meltQuality = q);

        float hammerQuality = 0.5f;
        yield return CraftingMinigameUI.Instance.RunHammering(
            "Forging", hammerRounds, hammerRoundDuration, perfectMin, perfectMax, goodMin, goodMax,
            q => hammerQuality = q);

        float quality = (meltQuality + hammerQuality) * 0.5f;
        ApplyCraft(quality, manaSpent);
        isCrafting = false;
    }

    private void ApplyCraft(float quality, int manaSpent)
    {
        if (!HasEnoughInputs())
        {
            return;
        }

        ResourceBank.TrySpend(oreType, oreAmount);
        ResourceBank.TrySpend(woodType, woodAmount);

        if (manaSpent > 0)
        {
            int actualMana = Mathf.Min(manaSpent, ResourceBank.Get(manaStoneType));
            ResourceBank.TrySpend(manaStoneType, actualMana);
        }

        int amount = outputAmount;

        if (quality >= 0.85f)
        {
            amount += 1;
        }
        else if (quality >= 0.5f && Random.value < 0.5f)
        {
            amount += 1;
        }

        ResourceBank.Add(outputType, amount);
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
