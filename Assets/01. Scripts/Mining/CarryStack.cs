using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CarryLayer
{
    Ore,
    Wood
}

public class CarryStack : MonoBehaviour
{
    [SerializeField] private Transform stackAnchor;
    [SerializeField] private int oreCapacity = 8;
    [SerializeField] private int woodCapacity = 8;
    [SerializeField] private float itemHeight = 0.5f;
    [SerializeField] private float woodItemHeight = 0.4f;
    [SerializeField] private float woodBackOffset = 0.35f;
    [SerializeField] private float swayAmplitude = 6f;
    [SerializeField] private float swaySpeed = 6f;
    [SerializeField] private float flightDuration = 0.4f;
    [SerializeField] private float flightArcHeight = 1.5f;
    [SerializeField] private float woodDropDuration = 0.3f;
    [SerializeField] private float woodDropArcHeight = 0.4f;
    [SerializeField] private float woodDropScatterRadius = 0.6f;
    [SerializeField] private float woodPickupDelay = 0.5f;

    private readonly List<Transform> oreItems = new List<Transform>();
    private readonly List<Transform> woodItems = new List<Transform>();
    private int reservedOre;
    private int reservedWood;
    private Rigidbody body;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
    }

    public bool IsFull(CarryLayer layer)
    {
        return layer == CarryLayer.Ore ? reservedOre >= oreCapacity : reservedWood >= woodCapacity;
    }

    public bool TryAdd(GameObject itemPrefab, Vector3 worldStartPosition, CarryLayer layer)
    {
        if (itemPrefab == null || stackAnchor == null || IsFull(layer))
        {
            return false;
        }

        int index;
        float height;
        float zOffset;

        if (layer == CarryLayer.Ore)
        {
            index = reservedOre;
            reservedOre++;
            height = itemHeight;
            zOffset = 0f;
        }
        else
        {
            index = reservedWood;
            reservedWood++;
            height = woodItemHeight;
            zOffset = -woodBackOffset;
        }

        Vector3 targetLocalPosition = new Vector3(0f, index * height, zOffset);

        GameObject instance = Instantiate(itemPrefab, worldStartPosition, Quaternion.identity);
        StartCoroutine(FlyToStack(instance.transform, worldStartPosition, targetLocalPosition, layer));
        return true;
    }

    public void Clear(CarryLayer layer)
    {
        List<Transform> items = layer == CarryLayer.Ore ? oreItems : woodItems;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                Destroy(items[i].gameObject);
            }
        }

        items.Clear();

        if (layer == CarryLayer.Ore)
        {
            reservedOre = 0;
        }
        else
        {
            reservedWood = 0;
        }
    }

    private IEnumerator FlyToStack(Transform item, Vector3 startWorldPosition, Vector3 targetLocalPosition, CarryLayer layer)
    {
        Vector3 flightStart = startWorldPosition;

        if (layer == CarryLayer.Wood)
        {
            Vector2 scatter = Random.insideUnitCircle * woodDropScatterRadius;
            Vector3 groundPosition = startWorldPosition + new Vector3(scatter.x, 0f, scatter.y);

            float dropElapsed = 0f;
            while (dropElapsed < woodDropDuration && item != null)
            {
                dropElapsed += Time.deltaTime;
                float dropT = Mathf.Clamp01(dropElapsed / woodDropDuration);
                Vector3 dropPosition = Vector3.Lerp(startWorldPosition, groundPosition, dropT);
                dropPosition.y += woodDropArcHeight * Mathf.Sin(dropT * Mathf.PI);
                item.position = dropPosition;
                yield return null;
            }

            if (item == null)
            {
                yield break;
            }

            item.position = groundPosition;
            flightStart = groundPosition;

            yield return new WaitForSeconds(woodPickupDelay);

            if (item == null)
            {
                yield break;
            }
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
            Destroy(item.gameObject);
            yield break;
        }

        item.SetParent(stackAnchor, false);
        item.localPosition = targetLocalPosition;
        item.localRotation = Quaternion.identity;

        if (layer == CarryLayer.Ore)
        {
            oreItems.Add(item);
        }
        else
        {
            woodItems.Add(item);
        }
    }

    private void Update()
    {
        if (stackAnchor == null)
        {
            return;
        }

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float sway = Mathf.Sin(Time.time * swaySpeed) * swayAmplitude * Mathf.Clamp01(speed / 5f);

        ApplySway(oreItems, sway);
        ApplySway(woodItems, sway);
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
