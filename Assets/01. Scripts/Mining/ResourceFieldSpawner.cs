using UnityEngine;

public class ResourceFieldSpawner : MonoBehaviour
{
    [SerializeField] private GameObject nodePrefab;
    [SerializeField] private int columns = 3;
    [SerializeField] private int rows = 3;
    [SerializeField] private float spacing = 2.5f;

    private void Awake()
    {
        if (nodePrefab == null)
        {
            return;
        }

        Vector3 origin = transform.position;
        float offsetX = (columns - 1) * spacing * 0.5f;
        float offsetZ = (rows - 1) * spacing * 0.5f;

        for (int x = 0; x < columns; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                Vector3 position = origin + new Vector3(x * spacing - offsetX, 0f, z * spacing - offsetZ);
                Instantiate(nodePrefab, position, Quaternion.identity, transform);
            }
        }
    }
}
