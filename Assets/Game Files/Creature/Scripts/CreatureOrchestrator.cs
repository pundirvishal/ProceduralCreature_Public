using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CreatureOrchestrator : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private CreatureController creatureController;
    [SerializeField] private TentacleBatchMaker tentacleBatchMaker;

    [Header("Child Prefabs")]
    [SerializeField] private GameObject visualTentaclePrefab;

    public Transform target { get; set; }

    private Rigidbody2D rootRigidbody;

    public CreatureController GetCreatureController() => creatureController;

    private void Awake()
    {
        rootRigidbody = GetComponent<Rigidbody2D>();
        if (creatureController == null) Debug.LogError("CreatureController is not assigned in the inspector!", this);
        if (tentacleBatchMaker == null) Debug.LogError("TentacleBatchMaker is not assigned in the inspector!", this);
    }

    // REMOVED: The OnEnable() method is gone.

    // NEW: This method will be called by the manager AFTER the target is set.
    public void BeginInitialization()
    {
        StartCoroutine(InitializeCreature());
    }

    private IEnumerator InitializeCreature()
    {
        creatureController.target = this.target;

        yield return StartCoroutine(creatureController.InitializeAsync(rootRigidbody));

        List<LogicalTentacle> logicalTentacles = creatureController.GetTentacles();
        List<TentacleRendererBatched> allVisualRenderers = new List<TentacleRendererBatched>();

        Transform visualParent = tentacleBatchMaker.transform;

        // Destroy any old visualizers from a previous life in the pool
        foreach (Transform child in visualParent)
        {
            Destroy(child.gameObject);
        }

        foreach (LogicalTentacle logic in logicalTentacles)
        {
            GameObject visualInstance = Instantiate(visualTentaclePrefab, visualParent.position, visualParent.rotation, visualParent);
            TentacleRendererBatched renderer = visualInstance.GetComponent<TentacleRendererBatched>();

            if (renderer != null)
            {
                renderer.Initialize(logic, tentacleBatchMaker);
                allVisualRenderers.Add(renderer);
            }
        }

        tentacleBatchMaker.RefreshTentacleList(allVisualRenderers);
    }
}