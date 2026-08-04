using System.Collections.Generic;
using UnityEngine;

// Generic reusable pool for prefab instances - avoids repeated Instantiate/Destroy churn for
// short-lived effects (mining/woodcutting hit fragments, carried pickup items, etc.), which were
// previously allocated and destroyed on every single hit tick during active gathering.
public class GameObjectPool : MonoBehaviour
{
    private static GameObjectPool instance;

    public static GameObjectPool Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GameObjectPool");
                instance = go.AddComponent<GameObjectPool>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, GameObject> instanceToPrefab = new Dictionary<GameObject, GameObject>();

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            pools[prefab] = queue;
        }

        while (queue.Count > 0)
        {
            GameObject pooled = queue.Dequeue();

            if (pooled != null)
            {
                pooled.transform.SetPositionAndRotation(position, rotation);
                pooled.transform.localScale = prefab.transform.localScale;
                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject instance = Instantiate(prefab, position, rotation);
        instanceToPrefab[instance] = prefab;
        return instance;
    }

    public void Despawn(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        instance.SetActive(false);

        if (!instanceToPrefab.TryGetValue(instance, out GameObject prefab))
        {
            // Wasn't spawned through this pool (e.g. created before pooling was added) - just
            // destroy it rather than pretending to own its lifecycle.
            Destroy(instance);
            return;
        }

        if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            pools[prefab] = queue;
        }

        queue.Enqueue(instance);
    }
}
