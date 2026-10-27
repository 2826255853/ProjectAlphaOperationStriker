using UnityEngine;

/// <summary>Fixed-direction plasma with swept contacts and categorized electric damage.</summary>
[DisallowMultipleComponent]
public sealed class PlasmaProjectile : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float hitRadius = 0.1f;
    private Vector3 direction;
    private float speed, maxRange, directDamage, splashDamage, splashRadius;
    private Transform owner;
    private bool initialized;
    public float TravelledDistance { get; private set; }
    public bool HasImpacted { get; private set; }
    public Vector3 Direction => direction;

    public void Initialize(Vector3 flightDirection, float flightSpeed, float range,
        float direct, float splash, float radius, Transform source)
    {
        direction = flightDirection.normalized;
        speed = Mathf.Max(0.01f, flightSpeed);
        maxRange = Mathf.Max(0.01f, range);
        directDamage = Mathf.Max(0f, direct);
        splashDamage = Mathf.Max(0f, splash);
        splashRadius = Mathf.Max(0f, radius);
        owner = source;
        TravelledDistance = 0f;
        HasImpacted = false;
        initialized = true;
    }

    private void Update() => Advance(Time.deltaTime);

    public void Advance(float deltaTime)
    {
        if (!initialized || HasImpacted || deltaTime <= 0f) return;
        float step = Mathf.Min(speed * deltaTime, maxRange - TravelledDistance);
        Vector3 start = transform.position;
        float nearest = step;
        bool hit = false;
        EnemyInstance direct = null;
        foreach (RaycastHit contact in Physics.SphereCastAll(start, hitRadius, direction, step, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IgnoreObstacle(contact.collider, owner) || contact.distance > nearest) continue;
            nearest = contact.distance;
            hit = true;
        }
        var enemies = FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None);
        foreach (EnemyInstance enemy in enemies)
        {
            if (!IsEligible(enemy)) continue;
            Bounds bounds = BodyBounds(enemy);
            bounds.Expand(hitRadius * 2f);
            float distance = 0f;
            if (!bounds.Contains(start) && !bounds.IntersectRay(new Ray(start, direction), out distance)) continue;
            if (distance > nearest) continue;
            nearest = distance;
            direct = enemy;
            hit = true;
        }
        transform.position = start + direction * nearest;
        TravelledDistance += nearest;
        if (hit) Impact(direct, enemies);
        else if (TravelledDistance + 0.0001f >= maxRange) Remove();
    }

    private void Impact(EnemyInstance direct, EnemyInstance[] enemies)
    {
        HasImpacted = true;
        // Snapshot before applying damage: a kill may disable or destroy its instance.
        foreach (EnemyInstance enemy in enemies)
        {
            if (!IsEligible(enemy)) continue;
            bool primary = enemy == direct;
            if (!primary && (AimPosition(enemy) - transform.position).sqrMagnitude > splashRadius * splashRadius) continue;
            enemy.GetComponent<EnemyHealth>().TakeDamage(primary ? directDamage : splashDamage, DamageType.Electric);
        }
        Remove();
    }

    public static bool IsEligible(EnemyInstance enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.MonsterType != MonsterType.Ground
            || enemy.Resolution != EnemyInstance.ResolutionState.Alive) return false;
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        return health != null && !health.IsDead;
    }

    public static Vector3 AimPosition(EnemyInstance enemy) => BodyBounds(enemy).center;

    private static Bounds BodyBounds(EnemyInstance enemy)
    {
        Collider body = enemy.GetComponentInChildren<Collider>();
        if (body != null) return body.bounds;
        Renderer renderer = enemy.GetComponentInChildren<Renderer>();
        if (renderer != null) return renderer.bounds;
        return new Bounds(enemy.transform.position, Vector3.one * 0.6f);
    }

    private static bool IgnoreObstacle(Collider collider, Transform source)
    {
        return collider.GetComponentInParent<EnemyInstance>() != null
            || (source != null && collider.transform.IsChildOf(source));
    }

    public static bool HasClearPath(Vector3 from, Vector3 to, Transform source)
    {
        Vector3 delta = to - from;
        foreach (RaycastHit hit in Physics.SphereCastAll(from, 0.1f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreObstacle(hit.collider, source)) return false;
        return true;
    }

    private void Remove()
    {
        initialized = false;
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}
