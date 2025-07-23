using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    [System.Serializable]
    public class Pool
    {
        public string tag;
        public GameObject prefab;
        public int size;
        public bool expandable = true;
    }

    [Header("Pool Configuration")]
    public List<Pool> pools;
    public Transform poolParent;
    private Dictionary<string, Queue<GameObject>> poolDictionary;
    private Dictionary<string, Pool> poolSettings;

    private void Awake()
    {
        poolDictionary = new Dictionary<string, Queue<GameObject>>();
        poolSettings = new Dictionary<string, Pool>();
        if (poolParent == null)
            poolParent = new GameObject("ObjectPool").transform;

        // Start initialization and wait for it to complete before warming
        StartCoroutine(InitializePoolsSequence());
    }

    private IEnumerator InitializePoolsSequence()
    {
        yield return StartCoroutine(InitializePools());
        yield return StartCoroutine(WarmPool());
    }

    private IEnumerator InitializePools()
    {
        foreach (Pool pool in pools)
        {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            poolSettings.Add(pool.tag, pool);

            for (int i = 0; i < pool.size; i++)
            {
                GameObject obj = Instantiate(pool.prefab, poolParent);
                obj.SetActive(false);
                objectPool.Enqueue(obj);
                if (i % 5 == 0)
                    yield return null; // Yield to avoid frame spike
            }
            poolDictionary.Add(pool.tag, objectPool);
        }
    }

    // Pool warming: instantiate additional objects for better performance
    private IEnumerator WarmPool()
    {
        foreach (var pool in pools)
        {
            // Ensure the dictionary key exists before accessing it
            if (!poolDictionary.ContainsKey(pool.tag)) continue;

            int warmCount = Mathf.RoundToInt(pool.size * 0.8f);
            for (int i = 0; i < warmCount; i++)
            {
                GameObject obj = Instantiate(pool.prefab, poolParent);
                obj.SetActive(false);
                poolDictionary[pool.tag].Enqueue(obj);
                if (i % 3 == 0) yield return null;
            }
        }
    }

    public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(tag))
        {
            Debug.LogWarning($"Pool with tag '{tag}' doesn't exist.");
            return null;
        }

        GameObject objectToSpawn;
        if (poolDictionary[tag].Count > 0)
            objectToSpawn = poolDictionary[tag].Dequeue();
        else if (poolSettings[tag].expandable)
            objectToSpawn = Instantiate(poolSettings[tag].prefab, poolParent);
        else
        {
            Debug.LogWarning($"Pool '{tag}' is empty and not expandable.");
            return null;
        }

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.transform.SetParent(null);
        return objectToSpawn;
    }

    public void ReturnToPool(string tag, GameObject obj)
    {
        if (!poolDictionary.ContainsKey(tag))
        {
            Debug.LogWarning($"Pool with tag '{tag}' doesn't exist.");
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(poolParent);
        poolDictionary[tag].Enqueue(obj);
    }
}
