using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CarryLayer
{
    Ore,
    Wood,
    ManaStone
}

public class CarryStack : MonoBehaviour
{
    // Keep in sync with CarryLayer's member count - see LocalSlotPosition/Awake.
    private const int LayerCount = 3;

    [SerializeField] private Transform stackAnchor;
    [SerializeField] private int oreCapacity = 8;
    [SerializeField] private int woodCapacity = 8;
    // Starting capacity is intentionally small - field monster mana drops are meant to be a
    // trickle, not a farm. Also the first field meant to be raised later by a gold-cost carry
    // capacity upgrade (not built yet - see combat_design_v1.html follow-up notes).
    [SerializeField] private int manaCapacity = 6;
    [SerializeField] private float itemHeight = 0.5f;
    [SerializeField] private float woodItemHeight = 0.4f;
    [SerializeField] private float manaItemHeight = 0.22f;
    [SerializeField] private float woodBackOffset = 0.35f;
    [SerializeField] private float manaSideOffset = 0.4f;
    [SerializeField] private float swayAmplitude = 6f;
    [SerializeField] private float swaySpeed = 6f;
    [SerializeField] private float flightDuration = 0.4f;
    [SerializeField] private float flightArcHeight = 1.5f;
    [SerializeField] private float dropDuration = 0.3f;
    [SerializeField] private float dropArcHeight = 0.4f;
    [SerializeField] private float dropScatterRadius = 0.6f;
    [SerializeField] private float pickupDelay = 0.5f;
    [SerializeField] private float depositFlightDuration = 0.35f;
    [SerializeField] private float depositArcHeight = 1f;
    [SerializeField] private float depositStagger = 0.08f;

    // Indexed by (int)CarryLayer - one array instead of a field pair per layer, so a future 4th
    // carryable layer only needs a new capacity field + LayerCount bump, not new branches
    // scattered through every method here.
    private readonly List<Transform>[] itemsByLayer = new List<Transform>[LayerCount];
    private readonly int[] reservedByLayer = new int[LayerCount];
    private int[] capacities;
    private Rigidbody body;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
        capacities = new[] { oreCapacity, woodCapacity, manaCapacity };

        for (int i = 0; i < LayerCount; i++)
        {
            itemsByLayer[i] = new List<Transform>();
        }
    }

    public bool IsFull(CarryLayer layer)
    {
        return reservedByLayer[(int)layer] >= capacities[(int)layer];
    }

    public int GetCount(CarryLayer layer)
    {
        return reservedByLayer[(int)layer];
    }

    public bool TryAdd(GameObject itemPrefab, Vector3 worldStartPosition, CarryLayer layer)
    {
        if (itemPrefab == null || stackAnchor == null || IsFull(layer))
        {
            return false;
        }

        int index = reservedByLayer[(int)layer];
        reservedByLayer[(int)layer]++;

        Vector3 targetLocalPosition = LocalSlotPosition(layer, index);

        GameObject instance = GameObjectPool.Instance.Spawn(itemPrefab, worldStartPosition, Quaternion.identity);
        StartCoroutine(FlyToStack(instance.transform, worldStartPosition, targetLocalPosition, layer));
        return true;
    }

    // Each layer gets its own stacking column so piles never visually overlap: ore stacks
    // straight up at the anchor, wood stacks up behind it, mana shards stack up to the side.
    private Vector3 LocalSlotPosition(CarryLayer layer, int index)
    {
        switch (layer)
        {
            case CarryLayer.Wood:
                return new Vector3(0f, index * woodItemHeight, -woodBackOffset);
            case CarryLayer.ManaStone:
                return new Vector3(manaSideOffset, index * manaItemHeight, 0f);
            default:
                return new Vector3(0f, index * itemHeight, 0f);
        }
    }

    public void Clear(CarryLayer layer)
    {
        List<Transform> items = itemsByLayer[(int)layer];

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                items[i].SetParent(null, true);
                GameObjectPool.Instance.Despawn(items[i].gameObject);
            }
        }

        items.Clear();
        reservedByLayer[(int)layer] = 0;
    }

    // Drops everything carried, across every layer at once - used when the player dies (user
    // request: "죽게 되면, 플레이어 등에 있는 자원들은 다 사라지게"). Loops by index instead of
    // hardcoding each CarryLayer so a future 4th layer doesn't need a matching update here too.
    public void ClearAll()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            Clear((CarryLayer)i);
        }
    }

    public void Deposit(CarryLayer layer, Vector3 targetPosition)
    {
        List<Transform> items = itemsByLayer[(int)layer];

        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];

            if (item == null)
            {
                continue;
            }

            item.SetParent(null, true);
            StartCoroutine(DepositFlightRoutine(item, targetPosition, i * depositStagger));
        }

        items.Clear();
        reservedByLayer[(int)layer] = 0;
    }

    private IEnumerator DepositFlightRoutine(Transform item, Vector3 targetPosition, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (item == null)
        {
            yield break;
        }

        Vector3 startPosition = item.position;
        Vector3 startScale = item.localScale;
        float elapsed = 0f;

        while (elapsed < depositFlightDuration && item != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / depositFlightDuration);

            Vector3 flatPosition = Vector3.Lerp(startPosition, targetPosition, t);
            float arc = depositArcHeight * Mathf.Sin(t * Mathf.PI);

            item.position = flatPosition + Vector3.up * arc;
            item.localScale = startScale * Mathf.Lerp(1f, 0.1f, t * t);
            item.Rotate(Vector3.up, 540f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (item != null)
        {
            GameObjectPool.Instance.Despawn(item.gameObject);
        }
    }

    private IEnumerator FlyToStack(Transform item, Vector3 startWorldPosition, Vector3 targetLocalPosition, CarryLayer layer)
    {
        Vector2 scatter = Random.insideUnitCircle * dropScatterRadius;
        Vector3 groundPosition = startWorldPosition + new Vector3(scatter.x, 0f, scatter.y);

        float dropElapsed = 0f;
        while (dropElapsed < dropDuration && item != null)
        {
            dropElapsed += Time.deltaTime;
            float dropT = Mathf.Clamp01(dropElapsed / dropDuration);
            Vector3 dropPosition = Vector3.Lerp(startWorldPosition, groundPosition, dropT);
            dropPosition.y += dropArcHeight * Mathf.Sin(dropT * Mathf.PI);
            item.position = dropPosition;
            yield return null;
        }

        if (item == null)
        {
            yield break;
        }

        item.position = groundPosition;
        Vector3 flightStart = groundPosition;

        yield return new WaitForSeconds(pickupDelay);

        if (item == null)
        {
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < flightDuration && item != null && stackAnchor != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);

            Vector3 targetWorldPosition = stackAnchor.TransformPoint(targetLocalPosition);
            Vector3 flatPosition = Vector3.Lerp(flightStart, targetWorldPosition, t);
            float arc = flightArcHeight * Mathf.Sin(t * Mathf.PI);

            item.position = flatPosition + Vector3.up * arc;
            item.Rotate(Vector3.up, 480f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (item == null)
        {
            yield break;
        }

        if (stackAnchor == null)
        {
            GameObjectPool.Instance.Despawn(item.gameObject);
            yield break;
        }

        item.SetParent(stackAnchor, false);
        item.localPosition = targetLocalPosition;
        item.localRotation = Quaternion.identity;

        itemsByLayer[(int)layer].Add(item);
    }

    private void Update()
    {
        if (stackAnchor == null)
        {
            return;
        }

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float sway = Mathf.Sin(Time.time * swaySpeed) * swayAmplitude * Mathf.Clamp01(speed / 5f);

        for (int i = 0; i < LayerCount; i++)
        {
            ApplySway(itemsByLayer[i], sway);
        }
    }

    private static void ApplySway(List<Transform> items, float sway)
    {
        for (int i = 0; i < items.Count; i++)
        {
            float weight = (i + 1f) / items.Count;
            items[i].localRotation = Quaternion.Euler(0f, 0f, sway * weight);
        }
    }
}
