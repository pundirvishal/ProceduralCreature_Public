using UnityEngine;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;

public struct TentaclePoint
{
    public Vector2 position;
    public Vector2 prevPosition;
    public TentaclePoint(Vector2 pos) { this.position = pos; this.prevPosition = pos; }
}

public class LogicalTentacle : MonoBehaviour
{
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
    public LayerMask obstacleLayer;
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

    public TentaclePoint[] Points { get; private set; }
    public bool IsGripping { get; private set; }
    public bool isAttacking { get; private set; }
    public bool IsIdle => !IsGripping && !needsNewGrip && !isReaching && !isSearchingPostAttack && !isHanging;
    public Vector2 Griptarget { get; private set; }
    public Vector2 IdealSearchDirection { get; private set; }
    public JobHandle simulationJobHandle { get; private set; }

    // Stores the local offset from the creature's center for attachment.
    public Vector2 AttachmentOffset { get; set; }

    private bool needsNewGrip, isReaching, isSearchingPostAttack, shouldSearchPostAttack, isHanging;
    private float maxTentacleLength, reachTimer, idleTimer, attackSearchTimer, bestScore, activeReachTimeout, hangTimer;
    private CreatureController creatureController;
    private Transform body;
    private Vector2 gravity = new Vector2(0f, -9.81f);
    private int searchTimer;
    private Vector2 bestGripPoint, lastGripPoint;
    private int reachFailureCount = 0;
    private const int MAX_REACH_FAILURES = 3;
    private float originalMaxStretchMultiplier;

    // --- Member variables for optimized attack physics query ---
    private readonly Collider2D[] _hitCollidersCache = new Collider2D[10];
    private readonly HashSet<Rigidbody2D> hitObjectsThisAttack = new HashSet<Rigidbody2D>();
    private ContactFilter2D _attackFilter;


    public void Initialize(CreatureController controller, Transform bodyTransform)
    {
        this.creatureController = controller;
        this.body = bodyTransform;

        ApplyControllerParameters();

        // --- Initialize attack filter ---
        _attackFilter = new ContactFilter2D();
        _attackFilter.SetLayerMask(attackableLayer);
        _attackFilter.useTriggers = true;

        Points = new TentaclePoint[segments];
        // Use the offset to determine the world-space start position.
        Vector2 startPos = body.TransformPoint(AttachmentOffset);
        for (int i = 0; i < segments; i++)
            Points[i] = new TentaclePoint(startPos);

        maxTentacleLength = segments * segmentLength * maxStretchMultiplier;
        originalMaxStretchMultiplier = maxStretchMultiplier;
    }

    private void ApplyControllerParameters()
    {
        if (TentacleParameterController.Instance == null) return;
        var c = TentacleParameterController.Instance;
        this.segments = c.segments; this.segmentLength = c.segmentLength; this.constraintIterations = c.constraintIterations; this.gravityMultiplier = c.gravityMultiplier; this.maxStretchMultiplier = c.maxStretchMultiplier;
        this.reachSpeed = c.reachSpeed; this.idleReachTimeout = c.idleReachTimeout; this.movingReachTimeout = c.movingReachTimeout; this.hangDuration = c.hangDuration; this.hangingGravityMultiplier = c.hangingGravityMultiplier;
        this.idleDamping = c.idleDamping; this.grippingDamping = c.grippingDamping; this.reachingDamping = c.reachingDamping; this.attackingDamping = c.attackingDamping; this.hangingDamping = c.hangingDamping;
        this.terrainLayer = c.terrainLayer; this.reachDistance = c.reachDistance; this.searchRadius = c.searchRadius; this.searchAttemptsPerFrame = c.searchAttemptsPerFrame; this.searchDurationFrames = c.searchDurationFrames; this.minimumGripDistance = c.minimumGripDistance;
        this.obstacleLayer = c.obstacleLayer; this.obstacleAvoidanceRadius = c.obstacleAvoidanceRadius;
        this.attackRange = c.attackRange; this.attackSpeed = c.attackSpeed; this.attackOvershootDistance = c.attackOvershootDistance; this.attackReachBonus = c.attackReachBonus; this.attackReachTimeout = c.attackReachTimeout; this.attackSearchTimeout = c.attackSearchTimeout; this.attackSearchRadius = c.attackSearchRadius; this.maxAttackStretchMultiplier = c.maxAttackStretchMultiplier; this.attackImpactForce = c.attackImpactForce; this.attackableLayer = c.attackableLayer; this.attackImpactRadius = c.attackImpactRadius;
    }

    public void CompleteSimulation()
    {
        simulationJobHandle.Complete();
    }

    void FixedUpdate()
    {
        if (body == null || Points == null) return;
        CompleteSimulation();

        if (isHanging)
        {
            hangTimer -= Time.fixedDeltaTime;
            if (hangTimer <= 0) isHanging = false;
        }
        else if (IsIdle && !needsNewGrip)
        {
            RequestNewGrip(Random.insideUnitCircle.normalized, true);
        }

        if (isSearchingPostAttack) SearchForPostAttackGripPoint();
        else if (needsNewGrip) SearchForGripPoint();

        Simulate();
        CheckForOverstretch();
    }

    private void CheckForOverstretch()
    {
        if (!IsGripping) return;
        if (creatureController != null && creatureController.GripCount <= creatureController.MinimumGripsForStability) return;

        bool isOutsideOperatingRadius = Vector2.Distance(body.position, Griptarget) > (reachDistance + (idleTimer > 2f ? 2f : 0f));
        bool isPhysicallyOverstretched = Vector2.Distance(body.position, Points[Points.Length - 1].position) > (segments * segmentLength * maxStretchMultiplier);

        if (isOutsideOperatingRadius || isPhysicallyOverstretched)
        {
            SetGripping(false);
            reachFailureCount = 0;
            bool isCreatureIdle = (creatureController == null || creatureController.target == null);
            Vector2 searchDirection = isCreatureIdle ? Random.insideUnitCircle.normalized : ((Vector2)creatureController.target.position - (Vector2)body.position).normalized;
            RequestNewGrip(searchDirection, isCreatureIdle);
        }
    }

    private void SetGripping(bool gripping)
    {
        if (IsGripping != gripping)
        {
            IsGripping = gripping;
            creatureController?.NotifyGripStatusChanged(IsGripping);
        }
    }

    public void RequestNewGrip(Vector2 idealtargetDirection, bool isCreatureIdle)
    {
        if (needsNewGrip || isReaching || isHanging) return;

        bool wasGripping = this.IsGripping;
        Vector2 lastKnownGrip = this.Griptarget;

        SetGripping(false);
        isSearchingPostAttack = false;
        isReaching = false;
        needsNewGrip = false;
        idleTimer = 0f;
        maxStretchMultiplier = originalMaxStretchMultiplier;
        isAttacking = false;

        if (wasGripping)
        {
            Griptarget = lastKnownGrip;
            isReaching = true;
            reachTimer = idleReachTimeout;
        }
        else
        {
            this.activeReachTimeout = isCreatureIdle ? idleReachTimeout : movingReachTimeout;
            this.IdealSearchDirection = idealtargetDirection;
            needsNewGrip = true;
            searchTimer = searchDurationFrames;
            bestScore = float.MaxValue;
        }
    }

    private void Simulate()
    {
        Vector2 currentGravity = gravity * gravityMultiplier * (isHanging ? hangingGravityMultiplier : 1f);

        float currentDamping = idleDamping;
        if (isAttacking) currentDamping = attackingDamping;
        else if (isHanging) currentDamping = hangingDamping;
        else if (isReaching) currentDamping = reachingDamping;
        else if (IsGripping) currentDamping = grippingDamping;

        for (int i = 1; i < Points.Length; i++)
        {
            if (isReaching && i == Points.Length - 1) continue;
            TentaclePoint p = Points[i];
            Vector2 tempPos = p.position;
            Vector2 velocity = p.position - p.prevPosition;
            p.position += velocity * (1f - currentDamping) + (currentGravity * Time.fixedDeltaTime * Time.fixedDeltaTime);
            p.prevPosition = tempPos;
            Points[i] = p;
        }

        if (isReaching) HandleReachingState();
        for (int i = 0; i < constraintIterations; i++) ApplyConstraints();
    }

    private void ApplyConstraints()
    {
        if (Points == null || Points.Length == 0 || body == null) return;

        // Use the offset to find the world-space attachment point.
        Points[0].position = body.TransformPoint(AttachmentOffset);
        Points[0].prevPosition = Points[0].position;

        for (int i = 0; i < Points.Length - 1; i++)
        {
            ref var p1 = ref Points[i];
            ref var p2 = ref Points[i + 1];
            float dist = (p1.position - p2.position).magnitude;
            if (dist == 0) continue;
            float error = dist - segmentLength;
            Vector2 direction = (p1.position - p2.position) / dist;

            if (i == 0) p2.position += direction * error;
            else { p1.position -= direction * error * 0.5f; p2.position += direction * error * 0.5f; }
        }

        if (IsGripping && Points.Length > 0)
        {
            var tip = Points[Points.Length - 1];
            tip.position = Griptarget;
            Points[Points.Length - 1] = tip;
        }
    }

    private void HandleReachingState()
    {
        reachTimer -= Time.fixedDeltaTime;
        float currentSpeed = isAttacking ? attackSpeed : reachSpeed;
        Vector2 previousTipPosition = Points[Points.Length - 1].position;
        ref TentaclePoint tip = ref Points[Points.Length - 1];
        tip.position = Vector2.MoveTowards(tip.position, Griptarget, currentSpeed * Time.fixedDeltaTime);
        if (isAttacking) ApplyAttackImpact(tip.position, previousTipPosition);

        if (Vector2.Distance(tip.position, Griptarget) < 0.01f || reachTimer <= 0f)
        {
            isReaching = false;
            if (isAttacking)
            {
                ApplyAttackImpact(tip.position, previousTipPosition);
                reachFailureCount = 0;
                isAttacking = false;
                maxStretchMultiplier = originalMaxStretchMultiplier;

                bool hasPreviousGrip = lastGripPoint.x != float.PositiveInfinity;
                bool previousGripIsReachable = hasPreviousGrip && Vector2.Distance(body.position, lastGripPoint) <= maxTentacleLength;

                if (hasPreviousGrip && previousGripIsReachable)
                {
                    Griptarget = lastGripPoint;
                    isReaching = true;
                    reachTimer = movingReachTimeout;
                }
                else if (shouldSearchPostAttack)
                {
                    isSearchingPostAttack = true;
                    attackSearchTimer = attackSearchTimeout;
                    searchTimer = searchDurationFrames;
                    bestScore = float.MaxValue;
                }
                else
                {
                    isHanging = true;
                    hangTimer = hangDuration;
                }
            }
            else
            {
                if (reachTimer > 0f)
                {
                    reachFailureCount = 0;
                    SetGripping(true);
                    tip.position = Griptarget;
                }
                else
                {
                    reachFailureCount++;
                    if (reachFailureCount >= MAX_REACH_FAILURES)
                    {
                        isHanging = true;
                        hangTimer = hangDuration;
                        reachFailureCount = 0;
                    }
                    else { RequestNewGrip(Random.insideUnitCircle.normalized, true); }
                }
            }
        }
    }

    private void ApplyAttackImpact(Vector2 currentTipPosition, Vector2 previousTipPosition)
    {
        // Use the modern OverlapCircle overload with a filter and a results cache.
        int hitCount = Physics2D.OverlapCircle(currentTipPosition, attackImpactRadius, _attackFilter, _hitCollidersCache);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = _hitCollidersCache[i];
            Rigidbody2D targetRb = hitCollider.GetComponent<Rigidbody2D>();

            if (targetRb != null && !hitObjectsThisAttack.Contains(targetRb))
            {
                Vector2 impactDirection = (currentTipPosition - previousTipPosition).normalized;
                if (impactDirection.magnitude < 0.1f) impactDirection = (hitCollider.transform.position - body.position).normalized;
                float tentacleSpeed = Vector2.Distance(currentTipPosition, previousTipPosition) / Time.fixedDeltaTime;
                float speedMultiplier = Mathf.Clamp(tentacleSpeed / attackSpeed, 0.3f, 2.0f);
                float finalForce = attackImpactForce * speedMultiplier;
                targetRb.AddForce(impactDirection * finalForce, ForceMode2D.Impulse);
                hitObjectsThisAttack.Add(targetRb);
            }
        }
    }

    private void SearchForGripPoint()
    {
        if (searchTimer <= 0)
        {
            if (bestScore < float.MaxValue)
            {
                needsNewGrip = false;
                Griptarget = bestGripPoint;
                isReaching = true;
                reachTimer = activeReachTimeout;
            }
            else
            {
                needsNewGrip = true;
                searchTimer = searchDurationFrames;
                bestScore = float.MaxValue;
                IdealSearchDirection = Random.insideUnitCircle.normalized;
            }
            return;
        }

        searchTimer--;
        Vector2 idealPoint = (Vector2)body.position + IdealSearchDirection.normalized * Random.Range(segmentLength * 2, reachDistance + (idleTimer > 2f ? 2f : 0f));
        for (int i = 0; i < searchAttemptsPerFrame; i++)
        {
            Vector2 randomSearchPoint = idealPoint + Random.insideUnitCircle * searchRadius;
            RaycastHit2D hit = Physics2D.CircleCast(body.position, 0.1f, (randomSearchPoint - (Vector2)body.position).normalized, reachDistance + (idleTimer > 2f ? 2f : 0f), terrainLayer);
            if (hit.collider)
            {
                if (Vector2.Distance(body.position, hit.point) > (segments * segmentLength * maxStretchMultiplier)) continue;
                float score = CalculateGripScore(hit.point, idealPoint);
                if (score < bestScore) { bestScore = score; bestGripPoint = hit.point; }
            }
        }
    }

    private void SearchForPostAttackGripPoint()
    {
        attackSearchTimer -= Time.fixedDeltaTime;
        if (attackSearchTimer <= 0 || searchTimer <= 0)
        {
            isSearchingPostAttack = false;
            if (bestScore < float.MaxValue)
            {
                Griptarget = bestGripPoint;
                isReaching = true;
                reachTimer = activeReachTimeout;
            }
            else { isHanging = true; hangTimer = hangDuration; }
            return;
        }

        searchTimer--;
        for (int i = 0; i < searchAttemptsPerFrame; i++)
        {
            Vector2 randomPoint = Points[Points.Length - 1].position + Random.insideUnitCircle * attackSearchRadius;
            RaycastHit2D hit = Physics2D.CircleCast(body.position, 0.1f, (randomPoint - (Vector2)body.position).normalized, (segments * segmentLength * maxStretchMultiplier), terrainLayer);
            if (hit.collider)
            {
                if (Vector2.Distance(body.position, hit.point) > (segments * segmentLength * maxStretchMultiplier)) continue;
                float score = CalculateGripScore(hit.point, randomPoint);
                if (score < bestScore) { bestScore = score; bestGripPoint = hit.point; }
            }
        }
    }

    public bool IsAttackPointReachable(Vector2 point)
    {
        if (body == null) return false;
        float effectiveReach = attackRange + attackReachBonus;
        return Vector2.Distance(body.position, point) <= effectiveReach;
    }

    public void InitiateAttack(Vector2 attackPosition, bool shouldSearchAfter)
    {
        this.shouldSearchPostAttack = shouldSearchAfter;
        lastGripPoint = IsGripping ? this.Griptarget : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        SetGripping(false);

        Vector2 direction = (attackPosition - (Vector2)body.position).normalized;
        float currentBaseLength = segments * segmentLength;
        float distanceTotarget = Vector2.Distance(body.position, attackPosition);
        float requiredTotalLength = distanceTotarget + attackOvershootDistance;
        maxStretchMultiplier = Mathf.Min(requiredTotalLength / currentBaseLength, maxAttackStretchMultiplier);
        Vector2 overshoottarget = attackPosition + direction * attackOvershootDistance;

        IsGripping = false; needsNewGrip = false; isSearchingPostAttack = false; isHanging = false;
        Griptarget = overshoottarget;
        isReaching = true; isAttacking = true;
        reachTimer = attackReachTimeout;
        hitObjectsThisAttack.Clear();
    }

    private float CalculateGripScore(Vector2 candidatePoint, Vector2 idealPoint)
    {
        float score = Vector2.Distance(candidatePoint, idealPoint);
        if (Vector2.Distance(body.position, candidatePoint) < segmentLength * 2) score += 100f;

        if (creatureController != null)
        {
            var allTentacles = creatureController.GetTentacles();
            for (int i = 0; i < allTentacles.Count; i++)
            {
                LogicalTentacle otherTentacle = allTentacles[i];
                if (otherTentacle == this || !otherTentacle.IsGripping) continue;
                if (Vector2.Distance(candidatePoint, otherTentacle.Griptarget) < minimumGripDistance) score += 1000f;
            }
        }

        if (Physics2D.OverlapCircle(candidatePoint, obstacleAvoidanceRadius, obstacleLayer)) score += 10000f;
        return score;
    }

    void OnDrawGizmos()
    {
        if (body == null || Points == null || Points.Length == 0) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < Points.Length - 1; i++)
            Gizmos.DrawLine(Points[i].position, Points[i + 1].position);

        if (isReaching || IsGripping)
        {
            Gizmos.color = IsGripping ? Color.green : Color.cyan;
            Gizmos.DrawSphere(Griptarget, 0.2f);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(body.position, attackRange + attackReachBonus);
    }

    void OnDestroy()
    {
        CompleteSimulation();
    }
}