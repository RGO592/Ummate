using UnityEngine;

// Small pen wandering loop; existing feeding, product and save components remain independent.
[DisallowMultipleComponent]
public sealed class AnimalWander : MonoBehaviour
{
    [SerializeField] private AnimalMovementArea movementArea;
    [SerializeField, Min(0f)] private float moveSpeed = 0.65f;
    [SerializeField, Min(0f)] private float minWaitTime = 1f;
    [SerializeField, Min(0f)] private float maxWaitTime = 3f;
    [SerializeField, Min(0.01f)] private float stoppingDistance = 0.15f;
    [SerializeField] private float facingOffset = 90f;
    [SerializeField, Min(0f)] private float turnSpeed = 120f;
    [SerializeField] private LayerMask obstacleLayers = ~0;

    private readonly Collider[] overlaps = new Collider[64];
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private Quaternion authoredRotation;
    private GameDay gameDay;
    private Vector3 destination;
    private Vector3 queryHalfExtents;
    private float groundOffset, queryCenterOffset, footprintRadius, yaw, waitRemaining;
    private bool moving, placed;
    private bool hasActivated;

    public AnimalMovementArea MovementArea => movementArea;
    public bool IsMoving => moving;
    public float FootprintRadius => footprintRadius;

    private void Awake()
    {
        authoredRotation = transform.rotation;
        gameDay = FindFirstObjectByType<GameDay>();
        // A conservative rotation-independent footprint covers the model in every facing direction.
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
        foreach (Collider collider in GetComponentsInChildren<Collider>()) bounds.Encapsulate(collider.bounds);
        float x = Mathf.Max(Mathf.Abs(bounds.min.x - transform.position.x), Mathf.Abs(bounds.max.x - transform.position.x));
        float z = Mathf.Max(Mathf.Abs(bounds.min.z - transform.position.z), Mathf.Abs(bounds.max.z - transform.position.z));
        footprintRadius = Mathf.Max(0.1f, new Vector2(x, z).magnitude) + 0.05f;
        float bottom = bounds.min.y + 0.06f; // Keep the support ground out of obstacle queries.
        queryHalfExtents = new Vector3(footprintRadius, Mathf.Max(0.05f, (bounds.max.y - bottom) * 0.5f), footprintRadius);
        queryCenterOffset = bottom + queryHalfExtents.y - transform.position.y;
        groundOffset = movementArea != null ? transform.position.y - movementArea.transform.position.y : 0f;
    }

    private void OnEnable()
    {
        if (gameDay != null) gameDay.StateRestored += Restart;
        Restart();
        if (!hasActivated)
        {
            // Skip only the first activation wait; later waits stay unchanged.
            hasActivated = true;
            waitRemaining = 0f;
        }
    }

    private void OnDisable()
    {
        if (gameDay != null) gameDay.StateRestored -= Restart;
        moving = false;
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        minWaitTime = Mathf.Max(0f, minWaitTime);
        maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
        stoppingDistance = Mathf.Max(0.01f, stoppingDistance);
        turnSpeed = Mathf.Max(0f, turnSpeed);
    }

    private void Restart()
    {
        placed = false;
        Wait();
    }

    private void Wait()
    {
        moving = false;
        waitRemaining = Random.Range(minWaitTime, Mathf.Max(minWaitTime, maxWaitTime));
    }

    private bool Limits(out Vector2 half)
    {
        half = Vector2.zero;
        if (movementArea == null) return false;
        Vector3 scale = movementArea.transform.lossyScale;
        half = movementArea.Size * 0.5f - new Vector2(footprintRadius / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
            footprintRadius / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        return half.x > 0f && half.y > 0f;
    }

    private Vector3 AreaPoint(float x, float z)
    {
        Vector3 point = movementArea.transform.TransformPoint(new Vector3(x, 0f, z));
        point.y = movementArea.transform.position.y + groundOffset;
        return point;
    }

    private Vector3 ClampToArea(Vector3 position, Vector2 half)
    {
        Vector3 local = movementArea.transform.InverseTransformPoint(position);
        return AreaPoint(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.z, -half.y, half.y));
    }

    private bool Blocking(Collider collider)
    {
        // Interaction triggers and other animals don't cause pushing or flock behaviour.
        return collider != null && !collider.transform.IsChildOf(transform) &&
            collider.GetComponentInParent<AnimalWander>() == null && collider.GetComponentInParent<PlayerInteractor>() == null;
    }

    private Vector3 QueryCenter(Vector3 position) => position + Vector3.up * queryCenterOffset;

    private bool ClearAt(Vector3 position)
    {
        int count = Physics.OverlapBoxNonAlloc(QueryCenter(position), queryHalfExtents, overlaps,
            Quaternion.identity, obstacleLayers, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false; // Fail closed if the query buffer is full.
        for (int i = 0; i < count; i++) if (Blocking(overlaps[i])) return false;
        return true;
    }

    private bool ClearPath(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (!ClearAt(to)) return false;
        if (delta.sqrMagnitude < 0.000001f) return true;
        int count = Physics.BoxCastNonAlloc(QueryCenter(from), queryHalfExtents, delta.normalized, hits,
            Quaternion.identity, delta.magnitude, obstacleLayers, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++) if (Blocking(hits[i].collider)) return false;
        return true;
    }

    private void Update()
    {
        if (!Limits(out Vector2 half)) return;
        // Re-enabled/unlocked/loaded animals restart at a valid point without changing saved gameplay state.
        if (!placed)
        {
            Vector3 start = ClampToArea(transform.position, half);
            for (int attempt = 0; attempt < 32; attempt++)
            {
                if (ClearAt(start)) { transform.position = start; placed = true; break; }
                if (attempt < 24)
                {
                    float angle = (attempt % 8) * Mathf.PI / 4f;
                    float distance = (1 + attempt / 8) * 0.5f;
                    start = ClampToArea(transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance, half);
                }
                else start = AreaPoint(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            }
            if (!placed) { Wait(); return; }
        }
        // A swinging gate can enter the safety footprint after placement. Recover inside the pen.
        if (!ClearAt(transform.position)) { placed = false; Wait(); return; }
        if (!moving)
        {
            waitRemaining -= Time.deltaTime;
            if (waitRemaining > 0f) return;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Vector3 candidate = AreaPoint(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
                if (Vector3.Distance(transform.position, candidate) <= stoppingDistance || !ClearPath(transform.position, candidate)) continue;
                destination = candidate; moving = true; break;
            }
            if (!moving) { Wait(); return; }
        }
        Vector3 delta = destination - transform.position;
        if (delta.magnitude <= stoppingDistance) { Wait(); return; }
        Vector3 next = ClampToArea(Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.deltaTime), half);
        if (!ClearPath(transform.position, next)) { Wait(); return; }
        Vector3 actualMove = next - transform.position;
        if (actualMove.sqrMagnitude > 0f)
        {
            float targetYaw = Mathf.Atan2(actualMove.x, actualMove.z) * Mathf.Rad2Deg + facingOffset;
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnSpeed * Time.deltaTime);
            // Preserve the imported model's X/Z correction, including the chicken's -90 degree X tilt.
            transform.SetPositionAndRotation(next, Quaternion.AngleAxis(yaw, Vector3.up) * authoredRotation);
        }
    }
}


