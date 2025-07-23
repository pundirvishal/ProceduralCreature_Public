using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CreatureOrchestrator : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private CreatureController creatureController;

    public Transform target { get; set; }

    private Rigidbody2D rootRigidbody;

    public CreatureController GetCreatureController() => creatureController;

    private void Awake()
    {
        rootRigidbody = GetComponent<Rigidbody2D>();
        if (creatureController == null) Debug.LogError("CreatureController is not assigned in the inspector!", this);
    }

    public void BeginInitialization()
    {
        StartCoroutine(InitializeCreature());
    }

    private IEnumerator InitializeCreature()
    {
        // This method is now much simpler. It only initializes the logical part of the creature.
        // The visuals are now handled globally by the TentacleGpuRenderer.
        creatureController.target = this.target;
        yield return StartCoroutine(creatureController.InitializeAsync(rootRigidbody));
    }
}