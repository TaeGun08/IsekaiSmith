using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarryStack : MonoBehaviour
{
    [SerializeField] private Transform stackAnchor;
    [SerializeField] private int capacity = 8;
    [SerializeField] private float itemHeight = 0.3f;
    [SerializeField] private float swayAmplitude = 6f;
    [SerializeField] private float swaySpeed = 6f;
    [SerializeField] private float flightDuration = 0.4f;
    [SerializeField] private float flightArcHeight = 1.5f;

    private readonly List<Transform> items = new List<Transform>();
    private Rigidbody body;
    private int reservedCount;

    public int Count => reservedCount;
    public int Capacity => capacity;
    public bool IsFull => reservedCount >= capacity;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
    }

    public bool TryAdd(GameObject itemPrefab, Vector3 worldStartPosition)
    {
        if (IsFull || itemPrefab == null || stackAnchor == null)
        {
            return false;
        }

        int index = reservedCount;
        reservedCount++;

        GameObject instance = Instantiate(itemPrefab, worldStartPosition, Quaternion.identity);
        Vector3 targetLocalPosition = new Vector3(0f, index * itemHeight, 0f);
        StartCoroutine(FlyToStack(instance.transform, worldStartPosition, targetLocalPosition));
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                Destroy(items[i].gameObject);
            }
        }
        items.Clear();
        reservedCount = 0;
    }

    private IEnumerator FlyToStack(Transform item, Vector3 startWorldPosition, Vector3 targetLocalPosition)
    {
        float elapsed = 0f;

        while (elapsed < flightDuration && item != null && stackAnchor != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);

            Vector3 targetWorldPosition = stackAnchor.TransformPoint(targetLocalPosition);
            Vector3 flatPosition = Vector3.Lerp(startWorldPosition, targetWorldPosition, t);
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
        items.Add(item);
    }

    private void Update()
    {
        if (items.Count == 0 || stackAnchor == null)
        {
            return;
        }

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float sway = Mathf.Sin(Time.time * swaySpeed) * swayAmplitude * Mathf.Clamp01(speed / 5f);

        for (int i = 0; i < items.Count; i++)
        {
            float weight = (i + 1f) / items.Count;
            items[i].localRotation = Quaternion.Euler(0f, 0f, sway * weight);
        }
    }
}
