using UnityEngine;

public class CraftingStation : MonoBehaviour
{
    [SerializeField] private ResourceType[] inputTypes;
    [SerializeField] private int[] inputAmounts;
    [SerializeField] private ResourceType outputType;
    [SerializeField] private int outputAmount = 1;
    [SerializeField] private float craftDuration = 3f;
    [SerializeField] private float interactRadius = 2f;
    [SerializeField] private float abandonTimeout = 3f;
    [SerializeField] private Transform pulseVisual;
    [SerializeField] private float pulseStrength = 0.12f;
    [SerializeField] private float pulseSpeed = 10f;

    private float progress;
    private float lastActiveTime;
    private Vector3 pulseBaseScale;

    public float Progress01 => craftDuration > 0f ? Mathf.Clamp01(progress / craftDuration) : 0f;

    private void Awake()
    {
        if (pulseVisual != null)
        {
            pulseBaseScale = pulseVisual.localScale;
        }
    }

    private void Update()
    {
        bool nearPlayer = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;
        bool hasInputs = HasEnoughInputs();

        if (nearPlayer && hasInputs)
        {
            progress += Time.deltaTime;
            lastActiveTime = Time.time;

            if (progress >= craftDuration)
            {
                progress = 0f;
                Craft();
            }
        }
        else if (progress > 0f && Time.time - lastActiveTime > abandonTimeout)
        {
            progress = 0f;
        }

        UpdatePulse();
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

    private void Craft()
    {
        for (int i = 0; i < inputTypes.Length; i++)
        {
            ResourceBank.TrySpend(inputTypes[i], inputAmounts[i]);
        }

        ResourceBank.Add(outputType, outputAmount);
    }

    private void UpdatePulse()
    {
        if (pulseVisual == null)
        {
            return;
        }

        if (progress <= 0f)
        {
            pulseVisual.localScale = pulseBaseScale;
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength;
        pulseVisual.localScale = pulseBaseScale * pulse;
    }
}
