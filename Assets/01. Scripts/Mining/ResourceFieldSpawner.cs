using System.Collections.Generic;
using UnityEngine;

// Scatters ore nodes randomly around the quarry, same algorithm as TreeFieldSpawner (user
// request: "채석장도 벌목장의 나무처럼 무작위로 배치해줬으면 좋겠어") - was a fixed
// columns x rows grid before, which read as mechanically different from the organic-looking
// forest right next to it.
public class ResourceFieldSpawner : MonoBehaviour
{
    [SerializeField] private GameObject nodePrefab;
    [SerializeField] private int nodeCount = 9; // matches the old 3x3 grid's total
    [SerializeField] private float areaWidth = 14f;
    [SerializeField] private float areaDepth = 14f;
    [SerializeField] private float minSpacing = 2.3f;
    [SerializeField] private int maxAttemptsPerNode = 50;

    private readonly List<Vector3> placedPositions = new List<Vector3>();

    private void Awake()
    {
        if (nodePrefab == null)
        {
            return;
        }

        Vector3 origin = transform.position;
        placedPositions.Clear();

        for (int i = 0; i < nodeCount; i++)
        {
            if (TryFindValidPosition(origin, out Vector3 position))
            {
                placedPositions.Add(position);
                Instantiate(nodePrefab, position, Quaternion.identity, transform);
            }
        }
    }

    private bool TryFindValidPosition(Vector3 origin, out Vector3 result)
    {
        for (int attempt = 0; attempt < maxAttemptsPerNode; attempt++)
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
