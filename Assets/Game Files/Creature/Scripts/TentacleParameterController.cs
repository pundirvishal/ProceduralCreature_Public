using UnityEngine;

public class TentacleParameterController : MonoBehaviour
{
    public static TentacleParameterController Instance { get; private set; }

    [Header("Tentacle Settings")]
    public int segments = 26;
    public float segmentLength = 0.26f;
    public int constraintIterations = 15;
    public float gravityMultiplier = 0.4f;
    public float maxStretchMultiplier = 1.3f;

    [Header("Movement & Reaching")]
    public float reachSpeed = 45f;
    public float idleReachTimeout = 1f;
    public float movingReachTimeout = 1f;
    public float hangDuration = 0.5f;
    public float hangingGravityMultiplier = 8f;

    [Header("Damping")]
    [Range(0f, 1f)] public float idleDamping = 0.1f;
    [Range(0f, 1f)] public float grippingDamping = 0.09f;
    [Range(0f, 1f)] public float reachingDamping = 0.09f;
    [Range(0f, 1f)] public float attackingDamping = 0.038f;
    [Range(0f, 1f)] public float hangingDamping = 0.43f;

    [Header("Grip Finding")]
    public LayerMask terrainLayer = -1;
    public float reachDistance = 10f;
    public float searchRadius = 6f;
    public int searchAttemptsPerFrame = 10;
    public int searchDurationFrames = 2;
    public float minimumGripDistance = 4f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Set this to the layer your obstacles are on.")]
    public LayerMask obstacleLayer;
    [Tooltip("How far away from obstacles should tentacles try to stay?")]
    public float obstacleAvoidanceRadius = 1f;

    [Header("Attacking")]
    public float attackRange = 8f;
    public float attackSpeed = 60f;
    public float attackOvershootDistance = 2f;
    public float attackReachBonus = 2f;
    public float attackReachTimeout = 0.4f;
    public float attackSearchTimeout = 0.2f;
    public float attackSearchRadius = 10f;
    public float maxAttackStretchMultiplier = 4f;
    public float attackImpactForce = 5f;
    public LayerMask attackableLayer = -1;
    public float attackImpactRadius = 0.01f;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }
}