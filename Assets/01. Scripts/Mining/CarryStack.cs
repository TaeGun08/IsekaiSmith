using System.Collections.Generic;
using UnityEngine;

public class CarryStack : MonoBehaviour
{
    [SerializeField] private Transform stackAnchor;
    [SerializeField] private int capacity = 8;
    [SerializeField] private float itemHeight = 0.18f;
    [SerializeField] private float zigzagOffset = 0.08f;
    [SerializeField] private float swayAmplitude = 6f;
    [SerializeField] private float swaySpeed = 6f;

    private readonly List<Transform> items = new List<Transform>();
    private Rigidbody body;

    public int Count => items.Count;
    public int Capacity => capacity;
    public bool IsFull => items.Count >= capacity;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
    }

    public bool TryAdd(GameObject itemPrefab)
    {
        if (IsFull || itemPrefab == null || stackAnchor == null)
        {
            return false;
        }

        int index = items.Count;
        Transform item = Instantiate(itemPrefab, stackAnchor).transform;
        item.localPosition = new Vector3((index % 2 == 0 ? 1f : -1f) * zigzagOffset, index * itemHeight, 0f);
        item.localRotation = Quaternion.identity;
        items.Add(item);
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
