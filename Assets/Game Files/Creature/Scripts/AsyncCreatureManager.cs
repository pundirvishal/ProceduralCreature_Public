using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AsyncCreatureManager : MonoBehaviour
{
    [Header("Pool")]
    [Tooltip("Reference to the ObjectPool in the scene")]
    [SerializeField] private ObjectPool pool;
    [Tooltip("The tag used in the ObjectPool for the Creature_Master_Prefab")]
    [SerializeField] private string creatureTag = "Creature";

    [Header("Target Assignment")]
    [Tooltip("Default target for all spawned creatures (can be overridden by the spawner)")]
    public Transform defaultTarget;

    [Header("Target Movement Tracking")]
    [Tooltip("Minimum distance the target must move to notify creatures")]
    [SerializeField] private float targetMovementThreshold = 2.0f;
    [Tooltip("Minimum time between target movement notifications")]
    [SerializeField] private float targetMovementCooldown = 3.0f;

    private float lastNotificationTime;


    [Header("Spawning Cadence")]
    [Tooltip("The maximum number of creatures to spawn in a single frame when using SpawnMany")]
    [SerializeField] private int maxSpawnsPerFrame = 2;
    [Tooltip("The delay between spawn bursts when using SpawnMany")]
    [SerializeField] private float spawnBurstDelay = 0.1f;

    private WaitForSeconds _spawnWait;
    private Vector3 lastTargetPosition;
    private readonly List<CreatureController> activeCreatures = new List<CreatureController>();
    private float targetMovementThresholdSq;

    private Transform lastKnownTarget;

    private void Start()
    {
        _spawnWait = new WaitForSeconds(spawnBurstDelay);
        targetMovementThresholdSq = targetMovementThreshold * targetMovementThreshold;

        if (pool == null)
        {
            pool = FindFirstObjectByType<ObjectPool>();
            if (pool == null)
            {
                Debug.LogError("AsyncCreatureManager could not find an ObjectPool in the scene!", this);
            }
        }

        if (defaultTarget != null)
        {
            lastTargetPosition = defaultTarget.position;
        }

        lastKnownTarget = defaultTarget;
    }

    private void Update()
    {
        if (defaultTarget != lastKnownTarget)
        {
            BroadcastNewDefaultTarget();
        }

        if (defaultTarget != null)
        {
            float distanceMovedSq = (defaultTarget.position - lastTargetPosition).sqrMagnitude;
            if (distanceMovedSq > targetMovementThresholdSq && Time.time - lastNotificationTime > targetMovementCooldown)
            {
                NotifyCreaturesTargetMoved();
                lastTargetPosition = defaultTarget.position;
                lastNotificationTime = Time.time;
            }
        }
    }

    private void BroadcastNewDefaultTarget()
    {
        activeCreatures.RemoveAll(item => item == null);

        foreach (var creature in activeCreatures)
        {
            if (creature.target == lastKnownTarget)
            {
                creature.SetTarget(defaultTarget);
            }
        }

        lastKnownTarget = defaultTarget;
    }

    public void Spawn(Vector3 pos, Transform customTarget = null)
    {
        GameObject creatureInstance = pool.SpawnFromPool(creatureTag, pos, Quaternion.identity);
        if (creatureInstance == null)
        {
            Debug.LogWarning($"Could not spawn from pool with tag '{creatureTag}'. Check pool configuration.", this);
            return;
        }

        var orchestrator = creatureInstance.GetComponent<CreatureOrchestrator>();
        if (orchestrator == null)
        {
            Debug.LogError($"The spawned prefab with tag '{creatureTag}' is missing the CreatureOrchestrator script!", creatureInstance);
            pool.ReturnToPool(creatureTag, creatureInstance);
            return;
        }

        Transform targetToUse = customTarget != null ? customTarget : defaultTarget;
        orchestrator.target = targetToUse;

        var creatureController = orchestrator.GetCreatureController();
        if (creatureController != null)
        {
            if (!activeCreatures.Contains(creatureController))
            {
                activeCreatures.Add(creatureController);
            }
        }
        else
        {
            Debug.LogWarning("Spawned creature is missing its CreatureController reference!", creatureInstance);
        }

        orchestrator.BeginInitialization();
    }

    public void SpawnMany(Vector3[] positions, Transform customTarget = null)
    {
        StartCoroutine(SpawnRoutine(positions, customTarget));
    }

    private IEnumerator SpawnRoutine(IEnumerable<Vector3> positions, Transform customTarget = null)
    {
        int spawnedThisFrame = 0;
        foreach (var p in positions)
        {
            Spawn(p, customTarget);

            spawnedThisFrame++;
            if (spawnedThisFrame >= maxSpawnsPerFrame)
            {
                spawnedThisFrame = 0;
                yield return _spawnWait;
            }
        }
    }

    private void NotifyCreaturesTargetMoved()
    {
        activeCreatures.RemoveAll(item => item == null);

        foreach (var creature in activeCreatures)
        {
            creature.OnTargetMoved();
        }
    }

    public void ReturnCreatureToPool(GameObject creatureInstance)
    {
        var orchestrator = creatureInstance.GetComponent<CreatureOrchestrator>();
        if (orchestrator != null)
        {
            var creatureController = orchestrator.GetCreatureController();
            if (creatureController != null)
            {
                activeCreatures.Remove(creatureController);
                creatureController.ResetState();
            }
        }

        pool.ReturnToPool(creatureTag, creatureInstance);
    }

    public void ForceNotifyTargetMoved()
    {
        if (defaultTarget != null)
        {
            lastTargetPosition = defaultTarget.position;
            NotifyCreaturesTargetMoved();
        }
    }
}