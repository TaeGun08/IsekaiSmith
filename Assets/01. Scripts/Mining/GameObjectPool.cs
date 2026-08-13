using System.Collections.Generic;
using UnityEngine;

// Generic reusable pool for prefab instances - avoids repeated Instantiate/Destroy churn for
// short-lived effects (mining/woodcutting hit fragments, carried pickup items, hit sparks, etc.),
// which were previously allocated and destroyed on every single hit tick during active gathering.
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
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    // Every instance this pool owns (active or waiting to be reused) lives under here instead of
    // scattered at scene root - user report: "풀링되는 오브젝트나 아이템들은 풀링을 모아두는
    // 오브젝트 자식으로 정리가 될 수 있게". Reparenting doesn't affect any of this project's
    // pooled-item logic - callers always set world position explicitly (transform.position, not
    // localPosition) both during flight and on final placement.
    private Transform pooledInstancesRoot;

    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, GameObject> instanceToPrefab = new Dictionary<GameObject, GameObject>();

    private void Awake()
    {
        pooledInstancesRoot = new GameObject("Pooled Instances").transform;
        pooledInstancesRoot.SetParent(transform, false);
    }

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
                pooled.transform.SetParent(pooledInstancesRoot, true);
                pooled.transform.SetPositionAndRotation(position, rotation);
                pooled.transform.localScale = prefab.transform.localScale;
                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject instance = Instantiate(prefab, position, rotation, pooledInstancesRoot);
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

        // Whatever reparented it while carried/in-flight (e.g. CarryStack's stackAnchor) gets
        // undone here - every despawned instance rejoins the pool's own container, not wherever it
        // last happened to be parented.
        instance.transform.SetParent(pooledInstancesRoot, true);

        if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            pools[prefab] = queue;
        }

        queue.Enqueue(instance);
    }
}
