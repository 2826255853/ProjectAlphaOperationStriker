using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How a missile re-evaluates its intercept point while flying.</summary>
public enum MissileGuidanceMode
{
    /// <summary>Default: keep re-solving the intercept point during flight.</summary>
    ContinuousLeadRefine = 0,
    /// <summary>Freeze the intercept point computed at launch.</summary>
    FrozenLeadShot = 1
}

/// <summary>
/// Guided anti-air missile fired by <see cref="MissileLauncherWeapon"/>.
///
/// The missile leaves the tube along the barrel direction - the launcher has
/// already turned to the lead point - and then only makes small corrections, so
/// it needs far less turning than a projectile chasing the enemy's current
/// position. It ends its flight on proximity, on contact, or when it has flown
/// past its maximum range (rule R5); the range detonation still deals splash
/// damage instead of silently vanishing.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Missile Projectile")]
public sealed class MissileProjectile : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 45f;
    [SerializeField, Min(0f)] private float splashRadius = 2.5f;
    [SerializeField, Range(0f, 1f)] private float splashDamageRatio = 0.5f;

    [Header("Flight")]
    [SerializeField, Min(0.01f)] private float speed = 60f;
    [SerializeField, Min(0f)] private float turnRateDegrees = 180f;
    [SerializeField, Min(0.01f)] private float proximityFuseRadius = 1.2f;
    [SerializeField, Min(0.01f)] private float maxFlightRange = 18f;
    [Tooltip("Safety net only: max range detonation is the primary end-of-flight path.")]
    [SerializeField, Min(0.01f)] private float maxLifetime = 8f;

    [Header("Guidance")]
    [SerializeField] private MissileGuidanceMode guidanceMode = MissileGuidanceMode.ContinuousLeadRefine;
    [SerializeField, Min(0.01f)] private float leadRefreshSeconds = 0.1f;
    [Tooltip("Exponential smoothing applied to the sampled target velocity during flight.")]
    [SerializeField, Min(0f)] private float velocitySmoothingSeconds = 0.2f;
    [Tooltip("Range detonation follows the owning mount's detection range when enabled.")]
    [SerializeField] private bool useTurretRangeAsMaxFlightRange = true;
    [Tooltip("Mirrors the muzzle transforms so the tube-to-target geometry is visible in the editor.")]
    [SerializeField] private bool drawDebugTracer = true;
    [SerializeField] private MissileLauncherTurret turret;

    private EnemyInstance target;
    private Vector3 fallbackPoint;
    private Vector3 interceptPoint;
    private float travelledDistance;
    private float age;
    private float leadRefreshTimer;
    private bool detonated;
    private MissileVelocityTracker velocityTracker;

    /// <summary>Raised once when the missile ends its flight, so the weapon can decrement its in-flight count.</summary>
    public event Action<MissileProjectile> Destroyed;

    public EnemyInstance Target => target;
    public Vector3 InterceptPoint => interceptPoint;
    public float TravelledDistance => travelledDistance;
    public float Age => age;
    public bool IsDetonated => detonated;
    public float DamagePerMissile => damage;
    public float SplashRadius => splashRadius;
    public float SplashDamageRatio => splashDamageRatio;
    public float Speed => speed;
    public float TurnRateDegrees => turnRateDegrees;
    public float ProximityFuseRadius => proximityFuseRadius;
    public MissileGuidanceMode GuidanceMode => guidanceMode;
    public bool UseTurretRangeAsMaxFlightRange => useTurretRangeAsMaxFlightRange;

    /// <summary>Effective end-of-flight range: the mount's detection range when enabled, otherwise the authored value.</summary>
    public float MaxFlightRange =>
        useTurretRangeAsMaxFlightRange && turret != null ? turret.DetectionRange : maxFlightRange;

    private void OnValidate()
    {
        damage = Mathf.Max(0f, damage);
        splashRadius = Mathf.Max(0f, splashRadius);
        speed = Mathf.Max(0.01f, speed);
        turnRateDegrees = Mathf.Max(0f, turnRateDegrees);
        proximityFuseRadius = Mathf.Max(0.01f, proximityFuseRadius);
        maxFlightRange = Mathf.Max(0.01f, maxFlightRange);
        maxLifetime = Mathf.Max(0.01f, maxLifetime);
        leadRefreshSeconds = Mathf.Max(0.01f, leadRefreshSeconds);
        velocitySmoothingSeconds = Mathf.Max(0f, velocitySmoothingSeconds);
    }

    /// <summary>
    /// Copies the weapon's tuning onto this projectile, so the prefab does not
    /// have to duplicate the values the trap definition authors.
    /// </summary>
    public void ConfigureTuning(
        float damagePerMissile,
        float splashRadiusMetres,
        float splashRatio,
        float flightSpeed,
        float turnRate,
        float flightRange,
        MissileGuidanceMode mode,
        float leadRefresh)
    {
        damage = Mathf.Max(0f, damagePerMissile);
        splashRadius = Mathf.Max(0f, splashRadiusMetres);
        splashDamageRatio = Mathf.Clamp01(splashRatio);
        speed = Mathf.Max(0.01f, flightSpeed);
        turnRateDegrees = Mathf.Max(0f, turnRate);
        maxFlightRange = Mathf.Max(0.01f, flightRange);
        guidanceMode = mode;
        leadRefreshSeconds = Mathf.Max(0.01f, leadRefresh);
    }

    /// <summary>
    /// Arms the missile: records the tracked enemy, the lead point to fly to when
    /// the enemy dies before impact, and the launch direction (the barrel's world
    /// direction) the missile leaves the tube with.
    /// </summary>
    public void Initialize(
        MissileLauncherTurret owner,
        EnemyInstance trackedTarget,
        Vector3 fallbackInterceptPoint,
        Vector3 initialDirection)
    {
        turret = owner;
        target = trackedTarget;
        fallbackPoint = fallbackInterceptPoint;
        interceptPoint = fallbackInterceptPoint;
        velocityTracker = new MissileVelocityTracker();

        Vector3 direction = initialDirection.sqrMagnitude > 0.000001f ? initialDirection.normalized : Vector3.forward;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    /// <summary>
    /// Steps the flight with an explicit delta, so the edit-mode tests drive the
    /// same guidance, fuse and range math the runtime uses instead of duplicating it.
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (detonated) return;
        if (deltaTime <= 0f) return;
        age += deltaTime;

        if (!UpdateAimPoint(deltaTime)) return;

        Vector3 muzzle = transform.position;
        Vector3 forward = transform.forward;
        float step = speed * deltaTime;

        // Contact check first, so a fast missile cannot tunnel through its target
        // between two frames.
        if (TryGetContactTarget(muzzle, forward, step, out EnemyInstance contact))
        {
            Explode(contact);
            return;
        }

        // Proximity fuse: the primary hit test, because flying monsters may have no
        // collider at all (EnemyInstance only adds one when a renderer exists).
        if (IsWithinFuse(transform.position, step))
        {
            Explode(target);
            return;
        }

        transform.position += forward * step;
        travelledDistance += step;

        if (drawDebugTracer) Debug.DrawLine(muzzle, transform.position, new Color(1f, 0.55f, 0.1f), 0.05f);

        // Rule R5: past the engagement range the missile self-destructs, and that
        // detonation still damages whatever is nearby.
        if (travelledDistance >= MaxFlightRange || age >= maxLifetime)
        {
            Explode(NearestTargetWithinFuse());
        }
    }

    /// <summary>
    /// Refreshes the point the missile steers at: the tracked enemy's lead point
    /// when guidance is continuous, or the frozen launch solution otherwise.
    /// Returns false only when there is nothing left to steer at.
    /// </summary>
    private bool UpdateAimPoint(float deltaTime)
    {
        if (target == null)
        {
            // The enemy died mid-flight: keep flying to the last known point, then
            // detonate rather than hanging in the air.
            interceptPoint = fallbackPoint;
            Vector3 toFallback = fallbackPoint - transform.position;
            bool withinFuse = toFallback.sqrMagnitude <= proximityFuseRadius * proximityFuseRadius;
            bool overshot = Vector3.Dot(toFallback, transform.forward) <= 0f;
            if (withinFuse || overshot)
            {
                Explode(NearestTargetWithinFuse());
                return false;
            }
            return true;
        }

        if (guidanceMode == MissileGuidanceMode.FrozenLeadShot) return true;

        leadRefreshTimer -= deltaTime;
        if (leadRefreshTimer > 0f) return true;
        leadRefreshTimer = leadRefreshSeconds;

        Vector3 sampled = velocityTracker.Sample(target.transform.position, Time.time, velocitySmoothingSeconds);
        Vector3 velocity = sampled;
        MonsterPathFollower follower = target.PathFollower != null
            ? target.PathFollower
            : target.GetComponent<MonsterPathFollower>();
        if (follower != null && follower.IsFlying)
        {
            Vector3 toDestination = follower.DestinationPosition - target.transform.position;
            toDestination.y = 0f;
            if (toDestination.sqrMagnitude > 0.0001f)
            {
                Vector3 analytic = toDestination.normalized * follower.MoveSpeed;
                velocity = sampled.sqrMagnitude > 0.0001f ? Vector3.Lerp(analytic, sampled, 0.5f) : analytic;
            }
        }

        Vector3 solved;
        float timeToImpact;
        if (MissileAimSolver.TrySolveIntercept(transform.position, target.transform.position, velocity, speed,
                out solved, out timeToImpact))
        {
            interceptPoint = solved;
        }
        return true;
    }

    private void LateUpdate()
    {
        if (detonated) return;

        Vector3 toPoint = interceptPoint - transform.position;
        if (toPoint.sqrMagnitude < 0.000001f) return;
        Quaternion desired = Quaternion.LookRotation(toPoint.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnRateDegrees * Time.deltaTime);
    }

    /// <summary>True when the enemy is inside the proximity fuse, measured from a given position.</summary>
    private bool IsWithinFuse(Vector3 from, float lookAhead)
    {
        if (target == null) return false;
        float fuse = proximityFuseRadius + lookAhead * 0.5f;
        float sqrFuse = fuse * fuse;

        // Test where the missile is and where it is about to be, so a fast missile
        // cannot step over the fuse entirely.
        Vector3 next = from + transform.forward * lookAhead;
        return (target.transform.position - from).sqrMagnitude <= sqrFuse
            || (target.transform.position - next).sqrMagnitude <= sqrFuse;
    }

    /// <summary>The nearest enemy inside the fuse, used by the range detonation.</summary>
    private EnemyInstance NearestTargetWithinFuse()
    {
        float bestSqr = proximityFuseRadius * proximityFuseRadius;
        EnemyInstance best = null;
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>();
        for (int i = 0; i < enemies.Length; i++)
        {
            float sqr = (enemies[i].transform.position - transform.position).sqrMagnitude;
            if (sqr > bestSqr) continue;
            bestSqr = sqr;
            best = enemies[i];
        }
        return best;
    }

    /// <summary>
    /// Secondary hit test: catches colliders the proximity fuse missed. Skips our
    /// own colliders, because an authored projectile prefab may carry one.
    /// </summary>
    private bool TryGetContactTarget(Vector3 origin, Vector3 direction, float distance, out EnemyInstance contact)
    {
        contact = null;
        if (distance <= 0f) return false;

        RaycastHit[] hits = Physics.SphereCastAll(origin, proximityFuseRadius * 0.5f, direction, distance,
            ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;

            EnemyInstance found = hits[i].collider.GetComponentInParent<EnemyInstance>();
            if (found == null) continue;
            if (hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            contact = found;
        }
        return contact != null;
    }

    /// <summary>
    /// Ends the flight. Direct contact deals full damage and every other enemy in
    /// the blast radius takes the splash share, so a range detonation (rule R5) is
    /// never a dud.
    /// </summary>
    private void Explode(EnemyInstance directHit)
    {
        if (detonated) return;
        detonated = true;

        Vector3 epicentre = transform.position;
        var damaged = new HashSet<EnemyHealth>();

        if (directHit != null)
        {
            EnemyHealth health = directHit.GetComponent<EnemyHealth>();
            if (health != null && !health.IsDead && damaged.Add(health)) health.TakeDamage(damage);
        }

        if (splashRadius > 0f && splashDamageRatio > 0f)
        {
            float splashDamage = damage * splashDamageRatio;

            Collider[] hits = Physics.OverlapSphere(epicentre, splashRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                EnemyHealth health = hits[i].GetComponentInParent<EnemyHealth>();
                if (health == null || health.IsDead) continue;
                if (!damaged.Add(health)) continue;
                health.TakeDamage(splashDamage);
            }

            // OverlapSphere cannot see monsters without a collider, so sweep the
            // live instances as well: flying monsters often have renderers only.
            EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>();
            for (int i = 0; i < enemies.Length; i++)
            {
                Vector3 delta = enemies[i].transform.position - epicentre;
                if (delta.sqrMagnitude > splashRadius * splashRadius) continue;
                EnemyHealth health = enemies[i].GetComponent<EnemyHealth>();
                if (health == null || health.IsDead) continue;
                if (!damaged.Add(health)) continue;
                health.TakeDamage(splashDamage);
            }
        }

        if (drawDebugTracer)
        {
            const int spokes = 8;
            for (int i = 0; i < spokes; i++)
            {
                float angle = i * 360f / spokes;
                Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * splashRadius;
                Debug.DrawLine(epicentre, epicentre + offset, Color.yellow, 0.4f);
            }
        }

        Destroyed?.Invoke(this);
        DestroyNow(gameObject);
    }

    /// <summary>
    /// Removes the missile. Edit mode forbids the deferred <c>Destroy</c>, so the
    /// tests need the immediate variant.
    /// </summary>
    private static void DestroyNow(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>Detonates immediately; exposed so tests and the weapon can force it.</summary>
    public void Detonate()
    {
        Explode(target);
    }
}
