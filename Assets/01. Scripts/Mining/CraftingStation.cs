using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum CraftingMinigameType
{
    Temperature,
    Hammering
}

public class CraftingStation : MonoBehaviour
{
    [SerializeField] private ResourceType[] inputTypes;
    [SerializeField] private int[] inputAmounts;
    [SerializeField] private ResourceType outputType;
    [SerializeField] private int outputAmount = 1;
    [SerializeField] private float interactRadius = 2f;
    [SerializeField] private string stationTitle = "Crafting";
    [SerializeField] private CraftingMinigameType minigameType = CraftingMinigameType.Temperature;
    [SerializeField] private Transform pulseVisual;
    [SerializeField] private float pulseStrength = 0.12f;
    [SerializeField] private float pulseSpeed = 10f;

    [Header("Temperature Minigame (Furnace)")]
    [SerializeField] private float temperatureDuration = 4f;
    [SerializeField] private float sweetMin = 0.55f;
    [SerializeField] private float sweetMax = 0.75f;
    [SerializeField] private float pumpRate = 0.7f;
    [SerializeField] private float coolRate = 0.35f;

    [Header("Hammering Minigame (Anvil)")]
    [SerializeField] private int hammerRounds = 4;
    [SerializeField] private float hammerRoundDuration = 1.1f;
    [SerializeField] private float perfectMin = 0.35f;
    [SerializeField] private float perfectMax = 0.5f;
    [SerializeField] private float goodMin = 0.18f;
    [SerializeField] private float goodMax = 0.65f;

    private Vector3 pulseBaseScale;
    private bool isCrafting;

    private void Awake()
    {
        if (pulseVisual != null)
        {
            pulseBaseScale = pulseVisual.localScale;
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

        if (!nearPlayer || Keyboard.current == null || !HasEnoughInputs())
        {
            return;
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            StartCoroutine(CraftWithMinigame());
        }
        else if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            ApplyCraft(0.5f);
        }
    }

    private bool HasEnoughInputs()
    {
        for (int i = 0; i < inputTypes.Length; i++)
        {
            if (ResourceBank.Get(inputTypes[i]) < inputAmounts[i])
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerator CraftWithMinigame()
    {
        isCrafting = true;
        float quality = 0.5f;

        if (minigameType == CraftingMinigameType.Temperature)
        {
            yield return CraftingMinigameUI.Instance.RunTemperature(
                stationTitle, temperatureDuration, sweetMin, sweetMax, pumpRate, coolRate,
                q => quality = q);
        }
        else
        {
            yield return CraftingMinigameUI.Instance.RunHammering(
                stationTitle, hammerRounds, hammerRoundDuration, perfectMin, perfectMax, goodMin, goodMax,
                q => quality = q);
        }

        ApplyCraft(quality);
        isCrafting = false;
    }

    private void ApplyCraft(float quality)
    {
        if (!HasEnoughInputs())
        {
            return;
        }

        for (int i = 0; i < inputTypes.Length; i++)
        {
            ResourceBank.TrySpend(inputTypes[i], inputAmounts[i]);
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
        if (pulseVisual == null)
        {
            return;
        }

        if (!isCrafting)
        {
            pulseVisual.localScale = pulseBaseScale;
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength;
        pulseVisual.localScale = pulseBaseScale * pulse;
    }
}
