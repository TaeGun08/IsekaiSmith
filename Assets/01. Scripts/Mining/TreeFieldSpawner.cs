using System.Collections.Generic;
using UnityEngine;

public class TreeFieldSpawner : MonoBehaviour
{
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private int treeCount = 12;
    [SerializeField] private float areaWidth = 10f;
    [SerializeField] private float areaDepth = 10f;
    [SerializeField] private float minSpacing = 2.5f;
    [SerializeField] private int maxAttemptsPerTree = 30;

    private readonly List<Vector3> placedPositions = new List<Vector3>();

    private void Awake()
    {
        if (treePrefab == null)
        {
            return;
        }

        Vector3 origin = transform.position;
        placedPositions.Clear();

        for (int i = 0; i < treeCount; i++)
        {
            if (TryFindValidPosition(origin, out Vector3 position))
            {
                placedPositions.Add(position);
                Instantiate(treePrefab, position, Quaternion.identity, transform);
            }
        }
    }

    private bool TryFindValidPosition(Vector3 origin, out Vector3 result)
    {
        for (int attempt = 0; attempt < maxAttemptsPerTree; attempt++)
        {
            float x = Random.Range(-areaWidth * 0.5f, areaWidth * 0.5f);
            float z = Random.Range(-areaDepth * 0.5f, areaDepth * 0.5f);
            Vector3 candidate = origin + new Vector3(x, 0f, z);

            if (IsFarEnoughFromExisting(candidate))
            {
                result = candidate;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    private bool IsFarEnoughFromExisting(Vector3 candidate)
    {
        for (int i = 0; i < placedPositions.Count; i++)
        {
            if (Vector3.Distance(candidate, placedPositions[i]) < minSpacing)
            {
                return false;
            }
        }

        return true;
    }
}
