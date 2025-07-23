using UnityEngine;

public class CreatureSpawner : MonoBehaviour
{
    [Header("Spawning Configuration")]
    [SerializeField] private AsyncCreatureManager creatureManager;

    [Header("Spawn Settings")]
    [SerializeField] private int spawnCount = 1;
    [SerializeField] private float spawnRadius = 0f; // Set to 0 for exact position
    [SerializeField] private bool spawnMultiple = false;

    void Start()
    {
        if (creatureManager == null)
        {
            creatureManager = Object.FindAnyObjectByType<AsyncCreatureManager>();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnCreature();
        }
    }

    private void SpawnCreature()
    {
        if (creatureManager == null)
        {
            Debug.LogError("CreatureManager not found! Please assign it in the inspector.");
            return;
        }

        if (spawnMultiple)
        {
            SpawnMultipleCreatures();
        }
        else
        {
            Vector3 spawnPosition = transform.position;
            if (spawnRadius > 0f)
            {
                Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
                spawnPosition += new Vector3(randomOffset.x, randomOffset.y, 0);
            }

            creatureManager.Spawn(spawnPosition);
        }
    }

    private void SpawnMultipleCreatures()
    {
        Vector3[] positions = new Vector3[spawnCount];
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 spawnPos = transform.position;
            if (spawnRadius > 0f)
            {
                Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
                spawnPos += new Vector3(randomOffset.x, randomOffset.y, 0);
            }
            positions[i] = spawnPos;
        }

        creatureManager.SpawnMany(positions);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.3f);

        if (spawnRadius > 0f)
        {
            Gizmos.color = Color.yellow;
            DrawWireCircle(transform.position, spawnRadius);
        }
    }

    private void DrawWireCircle(Vector3 position, float radius)
    {
        int segments = 36;
        float angle = 0f;
        Vector3 lastPoint = position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;

        for (int i = 1; i <= segments; i++)
        {
            angle = i * Mathf.PI * 2 / segments;
            Vector3 nextPoint = position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
            Gizmos.DrawLine(lastPoint, nextPoint);
            lastPoint = nextPoint;
        }
    }
}