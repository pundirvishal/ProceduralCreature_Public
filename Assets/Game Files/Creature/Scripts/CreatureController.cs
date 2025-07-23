using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreatureController : MonoBehaviour
{
    public Transform target { get; set; }

    [Header("Spawning")]
    [SerializeField] private int numberOfTentacles = 8;
    [SerializeField] private GameObject logicalTentaclePrefab;
    [Tooltip("Controls the spacing of tentacle roots along the sprite's outline. 1 = use the full perimeter.")]
    [Range(0f, 1f)][SerializeField] private float tentacleSpread = 1f;
    [Tooltip("Controls how far inwards (positive) or outwards (negative) the tentacle roots are from the sprite outline.")]
    [Range(-1f, 1f)][SerializeField] private float tentacleInset = 0.1f;
    [Tooltip("Additional horizontal offset for the tentacle roots.")]
    [Range(-1f, 1f)][SerializeField] private float horizontalInset = 0f;
    [Tooltip("Additional vertical offset for the tentacle roots.")]
    [Range(-1f, 1f)][SerializeField] private float verticalInset = 0f;

    [Header("Rotation")]
    [Tooltip("How quickly the creature rotates to face its target.")]
    [SerializeField] private float rotationSpeed = 5f;
    [Tooltip("The base rotation of the sprite. 0 for right, -90 for up.")]
    [SerializeField] private float rotationOffset = 0f;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float minSpeed = 1f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float arrivalDistance = 1f;
    [Tooltip("How many times the creature will fail to reach a target before giving up.")]
    [Range(2, 5)]
    [SerializeField] private int maxMoveFailures = 3;

    [Header("Wavy Motion")]
    [SerializeField] private float waveFrequency = 2f;
    [SerializeField] private float waveAmplitude = 0.5f;

    [Header("Damping")]
    [SerializeField] private float movingBodyDamping = 0.5f;
    [SerializeField] private float idleBodyDamping = 2.0f;
    [SerializeField] private float brakingBodyDamping = 4.0f;

    [Header("Tentacle Control")]
    [SerializeField] private float movingGripInterval = 0.5f;
    [SerializeField] private float idleGripInterval = 2f;
    [SerializeField] private int minimumGripsForStability = 3;
    [SerializeField] private int movementTentacleCount = 4;
    [SerializeField] private float minimumSeparationAngle = 30f;
    [Tooltip("The angle of the cone in front of the creature where tentacles will search while moving.")]
    [SerializeField] private float forwardSearchAngle = 45f;
    [Tooltip("Controls which tentacle to release while moving. 0 = releases the one furthest from the target. 1 = releases the one pointing most directly backwards.")]
    [Range(0f, 1f)][SerializeField] private float rearwardReleasePriority = 0.8f;

    [Header("Attacking")]
    [SerializeField] private AttackerRole retractorRole = AttackerRole.Rear;
    [SerializeField] private int maxAttackers = 5;
    [SerializeField] private int maxRearAttackers = 2;
    [SerializeField] private int maxForwardAttackers = 3;
    [SerializeField] private float attackCooldown = 2f;

    [Header("Body Physics")]
    [SerializeField] private float bodyGravityMultiplier = 1.0f;

    [Header("Quadrant Forces")]
    [SerializeField] private float minQuadrantForce = 0.0f;
    [SerializeField] private float maxQuadrantForce = 0.5f;

    [Header("Optimization")]
    [SerializeField] private int tentaclesPerFrame = 2;

    private float pullBackSpeed = 40f;
    private float pullingForceTimeout = 1.5f;
    private float maxStretchDistance = 2f;
    public int GripCount => gripCount;
    public int MinimumGripsForStability => minimumGripsForStability;
    public bool IsGivingUp => isGivingUpOnMove;

    private readonly List<LogicalTentacle> tentacles = new List<LogicalTentacle>();
    private Rigidbody2D rb;
    private float originalGravityScale;
    private int gripCount;
    private float regripTimer;
    private bool isMovementHalted = false;
    private float movementHaltTimer;
    private float attackCooldownTimer = 0f;
    private int currentMoveFailures = 0;
    private bool isGivingUpOnMove = false;

    private Transform originalTarget;
    private Transform activeTarget;

    private Dictionary<int, List<LogicalTentacle>> quadrantTentacles = new Dictionary<int, List<LogicalTentacle>>();
    private bool isInitialised;
    private List<LogicalTentacle> grippingTentaclesCache = new List<LogicalTentacle>();
    private Vector2[] _attachmentPoints;
    private SpriteRenderer _spriteRenderer;
    private float arrivalDistanceSq;
    private Transform logicalParentTransform;

    public IEnumerator InitializeAsync(Rigidbody2D rootRigidbody)
    {
        isInitialised = false;
        this.rb = rootRigidbody;
        originalGravityScale = rb.gravityScale;
        regripTimer = idleGripInterval;
        _spriteRenderer = GetComponent<SpriteRenderer>();
        arrivalDistanceSq = arrivalDistance * arrivalDistance;

        for (int i = 0; i < 4; i++)
        {
            quadrantTentacles[i] = new List<LogicalTentacle>();
        }

        CalculateAttachmentPoints();
        yield return StartCoroutine(SpawnTentaclesAsync());
        AssignTentaclesToQuadrants();

        gripCount = 0;
        foreach (var tentacle in tentacles)
        {
            Vector2 randomDir = FindUncongestedSearchDirection(true, tentacle);
            tentacle.RequestNewGrip(randomDir, true);
        }

        if (target != null)
        {
            originalTarget = target;
            activeTarget = target;
        }

        isInitialised = true;
    }

    public void OnTargetMoved()
    {
        if (originalTarget != null && isGivingUpOnMove)
        {
            isGivingUpOnMove = false;
            currentMoveFailures = 0;
            movementHaltTimer = pullingForceTimeout;
            activeTarget = originalTarget;
        }
    }

    public void SetTarget(Transform newTarget)
    {
        originalTarget = newTarget;
        target = newTarget;
        currentMoveFailures = 0;
        isGivingUpOnMove = false;
        if (!isGivingUpOnMove)
        {
            activeTarget = newTarget;
        }
    }

    public void ResetState()
    {
        if (logicalParentTransform != null)
        {
            Destroy(logicalParentTransform.gameObject);
        }
        tentacles.Clear();
        isInitialised = false;
        gripCount = 0;
    }

    private void CalculateAttachmentPoints()
    {
        var spriteRenderer = _spriteRenderer != null ? _spriteRenderer : GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.GetPhysicsShapeCount() == 0)
        {
            _attachmentPoints = new Vector2[] { Vector2.zero };
            return;
        }

        List<Vector2> pathPoints = new List<Vector2>();
        spriteRenderer.sprite.GetPhysicsShape(0, pathPoints);

        if (pathPoints.Count < 2)
        {
            _attachmentPoints = new Vector2[] { pathPoints.Count > 0 ? pathPoints[0] : Vector2.zero };
            return;
        }

        Vector2 shapeCenter = Vector2.zero;
        foreach (var p in pathPoints) shapeCenter += p;
        shapeCenter /= pathPoints.Count;

        List<Vector2> finalPoints = new List<Vector2>();
        float perimeter = 0;
        for (int i = 0; i < pathPoints.Count; i++)
            perimeter += Vector2.Distance(pathPoints[i], pathPoints[(i + 1) % pathPoints.Count]);

        if (numberOfTentacles <= 0) return;
        float stepDistance = (perimeter * tentacleSpread) / numberOfTentacles;
        if (stepDistance <= 0)
        {
            _attachmentPoints = new Vector2[] { pathPoints[0] };
            return;
        }

        for (int i = 0; i < numberOfTentacles; i++)
        {
            float targetDistance = i * stepDistance;
            float traveled = 0;
            for (int j = 0; j < pathPoints.Count; j++)
            {
                Vector2 p1 = pathPoints[j];
                Vector2 p2 = pathPoints[(j + 1) % pathPoints.Count];
                float segmentLength = Vector2.Distance(p1, p2);

                if (traveled + segmentLength >= targetDistance)
                {
                    float distanceIntoSegment = targetDistance - traveled;
                    float t = segmentLength > 0 ? distanceIntoSegment / segmentLength : 0;
                    Vector2 pointOnEdge = Vector2.Lerp(p1, p2, t);
                    Vector2 directionToCenter = (shapeCenter - pointOnEdge).normalized;
                    Vector2 finalPoint = pointOnEdge + (directionToCenter * tentacleInset);
                    Vector2 directionFromCenter = (pointOnEdge - shapeCenter).normalized;
                    Vector2 directionalOffset = new Vector2(directionFromCenter.x * horizontalInset, directionFromCenter.y * verticalInset);
                    finalPoint += directionalOffset;
                    finalPoints.Add(finalPoint);
                    break;
                }
                traveled += segmentLength;
            }
        }
        _attachmentPoints = finalPoints.ToArray();
    }

    private IEnumerator SpawnTentaclesAsync()
    {
        if (logicalParentTransform != null)
        {
            Destroy(logicalParentTransform.gameObject);
        }

        logicalParentTransform = new GameObject("LogicalTentacles").transform;
        logicalParentTransform.SetParent(transform, false);
        logicalParentTransform.localPosition = Vector3.zero;

        for (int i = 0; i < numberOfTentacles; i++)
        {
            GameObject logicalInstance = Instantiate(logicalTentaclePrefab, transform.position, Quaternion.identity, logicalParentTransform);
            logicalInstance.name = $"LogicalTentacle_{i + 1}";
            logicalInstance.transform.localPosition = Vector3.zero;
            LogicalTentacle logicalScript = logicalInstance.GetComponent<LogicalTentacle>();

            if (_attachmentPoints != null && _attachmentPoints.Length > 0)
                logicalScript.AttachmentOffset = _attachmentPoints[i % _attachmentPoints.Length];

            logicalScript.Initialize(this, transform);
            tentacles.Add(logicalScript);

            if (i % tentaclesPerFrame == tentaclesPerFrame - 1)
                yield return null;
        }
    }

    private void Update()
    {
        if (!isInitialised) return;
        HandleAttackInput();
        UpdateAttackCooldown();
        HandleRegripTimer();
    }

    void FixedUpdate()
    {
        if (!isInitialised) return;
        RotateSprite();
        UpdateGripStatus();
        MoveBody();
        ApplyQuadrantForces();
    }

    private void RotateSprite()
    {
        if (activeTarget == null || _spriteRenderer == null) return;
        Vector2 directionToTarget = activeTarget.position - transform.position;

        if (directionToTarget.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(directionToTarget.y, directionToTarget.x) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
            _spriteRenderer.transform.rotation = Quaternion.Slerp(_spriteRenderer.transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            CalculateAttachmentPoints();
        }

        if (_attachmentPoints == null || _attachmentPoints.Length == 0) return;

        Gizmos.color = Color.cyan;
        foreach (var point in _attachmentPoints)
        {
            Vector3 worldPoint = transform.TransformPoint(point);
            Gizmos.DrawSphere(worldPoint, 0.1f);
        }

        if (activeTarget != null)
        {
            Gizmos.color = isGivingUpOnMove ? Color.red : Color.green;
            Gizmos.DrawLine(transform.position, activeTarget.position);
            Gizmos.DrawWireSphere(activeTarget.position, 0.2f);
        }
        else if (originalTarget != null)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(transform.position, originalTarget.position);
            Gizmos.DrawWireSphere(originalTarget.position, 0.1f);
        }
    }

    private void AssignTentaclesToQuadrants()
    {
        quadrantTentacles.Clear();
        for (int i = 0; i < 4; i++)
        {
            quadrantTentacles[i] = new List<LogicalTentacle>();
        }

        for (int i = 0; i < tentacles.Count; i++)
        {
            float angle = (360f / tentacles.Count) * i;
            if (angle < 90) quadrantTentacles[0].Add(tentacles[i]);
            else if (angle < 180) quadrantTentacles[1].Add(tentacles[i]);
            else if (angle < 270) quadrantTentacles[2].Add(tentacles[i]);
            else quadrantTentacles[3].Add(tentacles[i]);
        }
    }

    private void ApplyQuadrantForces()
    {
        foreach (var quadrant in quadrantTentacles)
        {
            if (quadrant.Value.Count > 0)
            {
                float forceDirection = Random.Range(0, 2) * 2 - 1;
                float forceMagnitude = Random.Range(minQuadrantForce, maxQuadrantForce);
                Vector2 quadrantCenter = Vector2.zero;
                foreach (var t in quadrant.Value)
                    quadrantCenter += t.Points[0].position;
                quadrantCenter /= quadrant.Value.Count;

                foreach (var t in quadrant.Value)
                {
                    Vector2 directionToCenter = (quadrantCenter - (Vector2)transform.position).normalized;
                    rb.AddForce(directionToCenter * forceMagnitude * forceDirection * Time.fixedDeltaTime, ForceMode2D.Impulse);
                }
            }
        }
    }

    public void NotifyGripStatusChanged(bool isGripping)
    {
        if (isGripping) gripCount++; else gripCount--;
        gripCount = Mathf.Max(0, gripCount);
    }

    public void TriggerAttack(Vector2 position)
    {
        List<LogicalTentacle> availableTentacles = new List<LogicalTentacle>();
        foreach (var t in tentacles)
        {
            if ((t.IsIdle || t.IsGripping) && t.IsAttackPointReachable(position))
                availableTentacles.Add(t);
        }
        if (availableTentacles.Count == 0) return;

        int potentialGrippingAttackers = 0;
        foreach (var t in availableTentacles)
            if (t.IsGripping) potentialGrippingAttackers++;

        int attackersToAssign = Mathf.Min(availableTentacles.Count, maxAttackers);
        int potentialGripsToLose = Mathf.Min(potentialGrippingAttackers, attackersToAssign);

        if (gripCount - potentialGripsToLose < minimumGripsForStability) return;

        Vector2 directionToAttack = (position - (Vector2)transform.position).normalized;
        List<LogicalTentacle> rearAttackers = new List<LogicalTentacle>();
        List<LogicalTentacle> forwardAttackers = new List<LogicalTentacle>();

        foreach (var t in availableTentacles)
        {
            Vector2 gripDir = (t.Points[t.Points.Length - 1].position - (Vector2)transform.position);
            if (Vector2.Dot(gripDir.normalized, directionToAttack) < 0) rearAttackers.Add(t);
            else forwardAttackers.Add(t);
        }

        rearAttackers.Sort((t1, t2) => Vector2.Dot((t1.Points[t1.Points.Length - 1].position - (Vector2)transform.position).normalized, directionToAttack).CompareTo(Vector2.Dot(((Vector2)t2.Points[t2.Points.Length - 1].position - (Vector2)transform.position).normalized, directionToAttack)));
        forwardAttackers.Sort((t1, t2) => Vector2.Dot(((Vector2)t2.Points[t2.Points.Length - 1].position - (Vector2)transform.position).normalized, directionToAttack).CompareTo(Vector2.Dot(((Vector2)t1.Points[t1.Points.Length - 1].position - (Vector2)transform.position).normalized, directionToAttack)));

        HashSet<LogicalTentacle> assignedTentacles = new HashSet<LogicalTentacle>();
        int currentAttackers = 0, currentRearAttackers = 0, currentForwardAttackers = 0;
        List<LogicalTentacle> firstGroup = (retractorRole == AttackerRole.Rear) ? rearAttackers : forwardAttackers;
        List<LogicalTentacle> secondGroup = (retractorRole == AttackerRole.Rear) ? forwardAttackers : rearAttackers;

        foreach (var tentacle in firstGroup)
        {
            if (currentAttackers >= maxAttackers) break;
            if (retractorRole == AttackerRole.Rear && currentRearAttackers >= maxRearAttackers) continue;
            if (retractorRole == AttackerRole.Forward && currentForwardAttackers >= maxForwardAttackers) continue;
            if (assignedTentacles.Contains(tentacle)) continue;
            tentacle.InitiateAttack(position, false);
            assignedTentacles.Add(tentacle);
            currentAttackers++;
            if (retractorRole == AttackerRole.Rear) currentRearAttackers++; else currentForwardAttackers++;
        }
        foreach (var tentacle in secondGroup)
        {
            if (currentAttackers >= maxAttackers) break;
            if (retractorRole == AttackerRole.Rear && currentForwardAttackers >= maxForwardAttackers) continue;
            if (retractorRole == AttackerRole.Forward && currentRearAttackers >= maxRearAttackers) continue;
            if (assignedTentacles.Contains(tentacle)) continue;
            tentacle.InitiateAttack(position, true);
            assignedTentacles.Add(tentacle);
            currentAttackers++;
            if (retractorRole == AttackerRole.Rear) currentForwardAttackers++; else currentRearAttackers++;
        }
        if (assignedTentacles.Count > 0) attackCooldownTimer = attackCooldown;
    }

    private void UpdateTentacleGrips()
    {
        if (tentacles.Count == 0) return;

        bool isIdle = (activeTarget == null) || Vector2.Distance(transform.position, activeTarget.position) < arrivalDistance;

        LogicalTentacle idleTentacle = null;
        foreach (var t in tentacles)
        {
            if (t.IsIdle)
            {
                idleTentacle = t;
                break;
            }
        }

        if (idleTentacle != null)
        {
            idleTentacle.RequestNewGrip(FindUncongestedSearchDirection(true, idleTentacle), true);
            return;
        }

        if (gripCount <= minimumGripsForStability) return;

        LogicalTentacle tentacleToRelease = null;
        if (isIdle)
        {
            grippingTentaclesCache.Clear();
            foreach (var t in tentacles)
            {
                if (t.IsGripping) grippingTentaclesCache.Add(t);
            }
            if (grippingTentaclesCache.Count > 0)
            {
                tentacleToRelease = grippingTentaclesCache[Random.Range(0, grippingTentaclesCache.Count)];
            }
        }
        else
        {
            grippingTentaclesCache.Clear();
            foreach (var t in tentacles)
            {
                if (t.IsGripping) grippingTentaclesCache.Add(t);
            }

            if (grippingTentaclesCache.Count > 0)
            {
                float maxDist = 0f;
                foreach (var t in grippingTentaclesCache)
                {
                    float dist = Vector2.Distance(t.Griptarget, activeTarget.position);
                    if (dist > maxDist) maxDist = dist;
                }
                if (maxDist == 0) maxDist = 1f;

                float bestScore = -1f;
                Vector2 moveDirection = (activeTarget.position - transform.position).normalized;

                foreach (var t in grippingTentaclesCache)
                {
                    float distScore = Vector2.Distance(t.Griptarget, activeTarget.position) / maxDist;
                    Vector2 gripDir = (t.Griptarget - (Vector2)transform.position).normalized;
                    float dot = Vector2.Dot(gripDir, moveDirection);
                    float angleScore = 1 - ((dot + 1) / 2f);
                    float finalScore = Mathf.Lerp(distScore, angleScore, rearwardReleasePriority);

                    if (finalScore > bestScore)
                    {
                        bestScore = finalScore;
                        tentacleToRelease = t;
                    }
                }
            }
        }

        if (tentacleToRelease != null)
        {
            tentacleToRelease.RequestNewGrip(FindUncongestedSearchDirection(isIdle, tentacleToRelease), isIdle);
        }
    }

    private void UpdateAttackCooldown()
    {
        if (attackCooldownTimer > 0) attackCooldownTimer -= Time.deltaTime;
    }

    private void MoveBody()
    {
        if (activeTarget == null)
        {
            rb.linearDamping = idleBodyDamping;
            float waveOffset = Mathf.Sin(Time.time * waveFrequency) * waveAmplitude;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.up * waveOffset, acceleration * Time.fixedDeltaTime);
            return;
        }

        if ((activeTarget.position - transform.position).sqrMagnitude <= arrivalDistanceSq)
        {
            rb.linearDamping = brakingBodyDamping;
            float waveOffset = Mathf.Sin(Time.time * waveFrequency) * waveAmplitude;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, new Vector2(0, 1) * waveOffset, acceleration * Time.fixedDeltaTime);
            return;
        }

        rb.linearDamping = movingBodyDamping;
        float distanceToTarget = Vector2.Distance(transform.position, activeTarget.position);
        bool canMove = gripCount >= (distanceToTarget > maxStretchDistance ? minimumGripsForStability + 2 : minimumGripsForStability);
        isMovementHalted = !canMove;

        if (isMovementHalted)
        {
            movementHaltTimer -= Time.fixedDeltaTime;
            Vector2 gripCenter = Vector2.zero;
            int grippingCount = 0;
            foreach (var tentacle in tentacles) if (tentacle.IsGripping) { gripCenter += tentacle.Griptarget; grippingCount++; }

            Vector2 moveDirection = (activeTarget.position - transform.position).normalized;
            Vector2 waveVelMove = new Vector2(-moveDirection.y, moveDirection.x) * (Mathf.Sin(Time.time * waveFrequency) * waveAmplitude);
            Vector2 pullDirection = (grippingCount > 0) ? ((gripCenter / grippingCount) - (Vector2)transform.position).normalized : -moveDirection;
            Vector2 desiredVel = pullDirection * pullBackSpeed + waveVelMove;
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, desiredVel, acceleration * Time.fixedDeltaTime);

            if (movementHaltTimer <= 0)
            {
                currentMoveFailures++;
                if (currentMoveFailures >= maxMoveFailures)
                {
                    isGivingUpOnMove = true;
                    activeTarget = null;
                    return;
                }
                movementHaltTimer = pullingForceTimeout;
            }
            return;
        }

        movementHaltTimer = pullingForceTimeout;
        float gripRatio = tentacles.Count > 0 ? (float)gripCount / tentacles.Count : 0;
        float currentSpeed = Mathf.Lerp(minSpeed, maxSpeed, gripRatio);
        Vector2 directionToTarget = (activeTarget.position - transform.position).normalized;
        Vector2 waveVelocityMove = new Vector2(-directionToTarget.y, directionToTarget.x) * (Mathf.Sin(Time.time * waveFrequency) * waveAmplitude);
        Vector2 desiredVelocity = (directionToTarget * currentSpeed) + waveVelocityMove;
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, desiredVelocity, acceleration * Time.fixedDeltaTime);
    }

    private void HandleAttackInput()
    {
        if (Input.GetMouseButtonDown(1) && attackCooldownTimer <= 0)
        {
            Vector2 attackPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            TriggerAttack(attackPosition);
        }
    }

    private void HandleRegripTimer()
    {
        regripTimer -= Time.deltaTime;
        if (regripTimer <= 0f)
        {
            UpdateTentacleGrips();
            bool isIdle = (activeTarget == null) || (activeTarget.position - transform.position).sqrMagnitude < arrivalDistanceSq;
            regripTimer = isIdle ? idleGripInterval : movingGripInterval;
        }
    }

    private void UpdateGripStatus()
    {
        if (tentacles.Count > 0)
        {
            float gripRatio = (float)gripCount / tentacles.Count;
            rb.gravityScale = Mathf.Lerp(originalGravityScale * bodyGravityMultiplier, 0f, gripRatio);
        }
    }

    private Vector2 FindUncongestedSearchDirection(bool isIdle, LogicalTentacle releasingTentacle)
    {
        Vector2 targetDir = (activeTarget != null) ? ((Vector2)(activeTarget.position - transform.position)).normalized : Random.insideUnitCircle.normalized;
        Vector2 candidateDirection;

        if (!isIdle)
        {
            int currentMovementTentacles = 0;
            foreach (var t in tentacles)
            {
                if (t != releasingTentacle && !t.IsIdle && Vector2.Dot(t.IdealSearchDirection, targetDir) > 0.5f)
                {
                    currentMovementTentacles++;
                }
            }

            if (currentMovementTentacles < movementTentacleCount)
            {
                candidateDirection = (Vector2)(Quaternion.Euler(0, 0, Random.Range(-forwardSearchAngle, forwardSearchAngle)) * targetDir);
            }
            else
            {
                candidateDirection = Random.insideUnitCircle.normalized;
            }
        }
        else
        {
            candidateDirection = Random.insideUnitCircle.normalized;
        }

        for (int i = 0; i < 5; i++)
        {
            bool isCongested = false;
            foreach (var other in tentacles)
            {
                if (other != releasingTentacle && other.IdealSearchDirection != Vector2.zero)
                {
                    if (Vector2.Angle(candidateDirection, other.IdealSearchDirection) < minimumSeparationAngle)
                    {
                        candidateDirection = Quaternion.Euler(0, 0, 45) * candidateDirection;
                        isCongested = true;
                        break;
                    }
                }
            }

            if (!isCongested)
            {
                return candidateDirection;
            }
        }

        return Random.insideUnitCircle.normalized;
    }

    public void ResetMovementFailures()
    {
        isGivingUpOnMove = false;
        currentMoveFailures = 0;
        movementHaltTimer = pullingForceTimeout;
        if (originalTarget != null)
        {
            activeTarget = originalTarget;
        }
    }

    public List<LogicalTentacle> GetTentacles() => tentacles;
}