using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// XM777 ground artillery. Shells are represented only by timed impact records;
/// no projectile GameObject, Rigidbody, or projectile prefab is created.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
[DefaultExecutionOrder(1000)]
public sealed class XM777Trap : MonoBehaviour
{
    public const float ShellSpeed = 500f;
    public const float RG777ShellSpeed = 1000f;
    public const float MinimumRange = 5f;
    public const float MaximumRange = 95f;
    public const float MinimumFiringElevationDegrees = -30f;
    public const float MaximumFiringElevationDegrees = 30f;
    /// <summary>Seconds between the start of one shot and the next (3 s = one shell per 3 s).</summary>
    public const float FireInterval = 3f;
    public const float BaseExplosionDamage = 45f;
    public const float ExplosionRadius = 4f;
    public const string RG777BranchId = "rg777";
    public const float RG777MinimumRange = 5f;
    public const float RG777MaximumRange = 200f;
    public const float RG777FireInterval = 2f;
    public const float RG777ExplosionDamage = BaseExplosionDamage * 5f;

    private struct PendingImpact
    {
        public Vector3 Point;
        public float Remaining;
        public float Damage;
    }

    [Header("Mount")]
    [SerializeField] private Transform yawPivot;
    [SerializeField] private Transform pitchPivot;
    [SerializeField] private Transform muzzle;
    [Tooltip("Barrel forward direction in yawPivot local space at the authored rest pose.")]
    [SerializeField] private Vector3 restBarrelForwardLocal = Vector3.forward;

    [Header("Effects")]
    [SerializeField] private GameObject muzzleEffectPrefab;
    [SerializeField] private GameObject impactEffectPrefab;

    [Header("Targeting")]
    [SerializeField, Min(0f)] private float yawSpeedDegrees = 240f;
    [SerializeField, Min(0f)] private float pitchSpeedDegrees = 240f;
    [SerializeField, Range(0.1f, 10f)] private float aimToleranceDegrees = 3f;
    [SerializeField, Min(0f)] private float velocitySmoothingSeconds = 0.12f;

    private readonly List<PendingImpact> pendingImpacts = new List<PendingImpact>();
    private TrapInstance trap;
    private EnemyInstance currentTarget;
    private bool selectTargetAfterShot;
    private MissileVelocityTracker velocityTracker;
    private Quaternion restYawRotation;
    private Quaternion restPitchRotation;
    private Vector3 pitchAxisLocal;
    private Vector3 restBarrelForwardWorld;
    private float restElevationDegrees;
    private Vector3 yawAxisWorld = Vector3.up;
    private bool restCaptured;
    private Transform authoredYawPivot;
    private Transform authoredPitchPivot;
    private Transform authoredMuzzle;
    private Quaternion authoredYawRotation;
    private Quaternion authoredPitchRotation;
    private bool authoredRigCaptured;
    private bool hasObservedBranchState;
    private bool observedRG777State;
    private bool hasFired;
    private float lastShotTime;
    private float clock;
    [SerializeField] private float nextShotTime;

    public EnemyInstance CurrentTarget => currentTarget;
    public Vector3 InterceptPoint { get; private set; }
    public bool HasInterceptPoint { get; private set; }
    public float TimeToImpact { get; private set; }
    public int ShotCount { get; private set; }
    public int ExplosionCount { get; private set; }
    public int PendingImpactCount => pendingImpacts.Count;
    public float NextShotTime => nextShotTime;
    public Transform Muzzle => muzzle;
    public Transform YawPivot => yawPivot;
    public Transform PitchPivot => pitchPivot;
    public GameObject MuzzleEffectPrefab => muzzleEffectPrefab;
    public GameObject ImpactEffectPrefab => impactEffectPrefab;
    public float YawSpeedDegrees => yawSpeedDegrees;
    public bool IsRG777Active => trap != null && trap.UpgradeState.Level == 4
        && trap.UpgradeState.Direction == TrapUpgradeDirection.Branch
        && trap.UpgradeState.BranchId == RG777BranchId;
    public float EffectiveMinimumRange => IsRG777Active ? RG777MinimumRange : MinimumRange;
    public float EffectiveMaximumRange => IsRG777Active ? RG777MaximumRange : MaximumRange;
    public float EffectiveShellSpeed => IsRG777Active ? RG777ShellSpeed : ShellSpeed;
    public float EffectiveFireInterval => IsRG777Active ? RG777FireInterval : FireInterval;
    public float EffectiveExplosionDamage => IsRG777Active ? RG777ExplosionDamage
        : trap != null ? trap.GetEffectiveDamage(BaseExplosionDamage) : BaseExplosionDamage;
    public float EffectiveElevationForDistance(float distance) => IsRG777Active
        ? RG777ElevationForDistance(distance)
        : ElevationForDistance(distance);

    public static float RG777ElevationForDistance(float distance) =>
        Mathf.InverseLerp(RG777MinimumRange, RG777MaximumRange, distance) * 30f;
    /// <summary>Vertical axis the turret traverses around (world up for grounded mounts).</summary>
    public Vector3 YawAxisWorld => yawAxisWorld;
    /// <summary>Current world-space barrel direction, read from the muzzle node.</summary>
    public Vector3 BarrelDirectionWorld => muzzle != null ? muzzle.forward : transform.forward;

    /// <summary>Barrel elevation above the horizon in degrees (0 = level, 90 = straight up).</summary>
    public float BarrelElevationDegrees
    {
        get
        {
            Vector3 barrel = BarrelDirectionWorld;
            Vector3 flat = Vector3.ProjectOnPlane(barrel, yawAxisWorld);
            return Mathf.Atan2(Vector3.Dot(barrel, yawAxisWorld), flat.magnitude) * Mathf.Rad2Deg;
        }
    }

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        EnsureRestPose();
        CaptureAuthoredRig();
    }

    /// <summary>Switches the active model rig while keeping this weapon's targets and pending shells.</summary>
    public void ConfigureAimRig(Transform nextYawPivot, Transform nextPitchPivot, Transform nextMuzzle)
    {
        EnsureRestPose();
        CaptureAuthoredRig();
        yawPivot = nextYawPivot != null ? nextYawPivot : transform;
        pitchPivot = nextPitchPivot != null ? nextPitchPivot : yawPivot;
        muzzle = nextMuzzle != null ? nextMuzzle : yawPivot;
        restCaptured = false;
        EnsureRestPose();
    }

    /// <summary>Returns to the authored XM777 rig and its original rest pose.</summary>
    public void RestoreAuthoredAimRig()
    {
        EnsureRestPose();
        CaptureAuthoredRig();
        if (authoredYawPivot != null) authoredYawPivot.rotation = authoredYawRotation;
        if (authoredPitchPivot != null) authoredPitchPivot.localRotation = authoredPitchRotation;
        ConfigureAimRig(authoredYawPivot, authoredPitchPivot, authoredMuzzle);
    }

    private void CaptureAuthoredRig()
    {
        if (authoredRigCaptured) return;
        authoredYawPivot = yawPivot;
        authoredPitchPivot = pitchPivot;
        authoredMuzzle = muzzle;
        authoredYawRotation = authoredYawPivot != null ? authoredYawPivot.rotation : transform.rotation;
        authoredPitchRotation = authoredPitchPivot != null ? authoredPitchPivot.localRotation : Quaternion.identity;
        authoredRigCaptured = true;
    }

    /// <summary>
    /// Captures the authored rest pose (and resolves the rig references) exactly
    /// once. Tools and tests can drive <see cref="AimAt"/> without Awake having
    /// run, which matters because a never captured rest rotation is the zero
    /// quaternion and would silently make every aim request a no-op.
    /// </summary>
    private void EnsureRestPose()
    {
        if (restCaptured) return;
        if (yawPivot == null) yawPivot = transform.Find("Yaw Pivot");
        if (yawPivot == null) yawPivot = transform;
        if (muzzle == null) muzzle = FindChild(transform, "Muzzle");
        if (muzzle == null) muzzle = yawPivot;
        if (pitchPivot == null) pitchPivot = FindChild(transform, "XM777_Pitch_Root");
        if (restBarrelForwardLocal.sqrMagnitude < 0.0001f)
            restBarrelForwardLocal = Vector3.forward;
        restBarrelForwardLocal.Normalize();
        restYawRotation = yawPivot.rotation;
        restBarrelForwardWorld = muzzle != null ? muzzle.forward : restYawRotation * restBarrelForwardLocal;
        yawAxisWorld = ResolveYawAxis();
        if (pitchPivot != null && pitchPivot != yawPivot)
        {
            restPitchRotation = pitchPivot.localRotation;
            restElevationDegrees = BarrelElevationDegrees;
            Vector3 flatForward = Vector3.ProjectOnPlane(muzzle.forward, yawAxisWorld).normalized;
            Vector3 axis = Vector3.Cross(flatForward, yawAxisWorld).normalized;
            pitchAxisLocal = pitchPivot.parent != null
                ? pitchPivot.parent.InverseTransformDirection(axis).normalized : axis;
        }
        restCaptured = true;
    }

    /// <summary>
    /// Vertical axis the turret turns around.
    ///
    /// The imported FBX keeps Blender's axis frame inside the rig: the pivot's
    /// parent ("XM777_Base_Root") carries a -90 degree X rotation, so its local up
    /// is a *horizontal* world axis. Rotating around that axis rolls the gun
    /// sideways instead of traversing it, which is the bug this resolves. A
    /// ground-mounted trap therefore traverses around the trap root's own up axis
    /// (world up after placement), falling back to world up for rotated roots.
    /// </summary>
    private Vector3 ResolveYawAxis()
    {
        Vector3 up = transform.up;
        if (up.sqrMagnitude < 0.000001f) return Vector3.up;
        up.Normalize();
        return Vector3.Dot(up, Vector3.up) > 0.9f ? up : Vector3.up;
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    private void OnEnable()
    {
        clock = 0f;
        nextShotTime = 0f;
        ShotCount = 0;
        ExplosionCount = 0;
        currentTarget = null;
        selectTargetAfterShot = false;
        HasInterceptPoint = false;
        InterceptPoint = default;
        TimeToImpact = 0f;
        hasObservedBranchState = false;
        observedRG777State = false;
        hasFired = false;
        lastShotTime = 0f;
        velocityTracker.Reset();
        pendingImpacts.Clear();
    }

    /// <summary>Deterministic simulation entry point, also used by EditMode tests.</summary>
    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || trap == null || trap.IsDestroyed
            || deltaTime < 0f || GetComponentInParent<TrapPlacementPreview>() != null)
            return;
        EnsureRestPose();

        float step = Mathf.Max(0f, deltaTime);
        clock += step;
        SynchronizeFireCadence();
        AdvanceImpacts(step);

        // Keep one living target through aiming and cooldown. A closer enemy
        // must not restart aim before this shot, but a target inside the
        // minimum range must be replaced so it cannot stall the artillery.
        if (selectTargetAfterShot || !IsLivingGroundEnemy(currentTarget)
            || IsTooCloseForArtillery(currentTarget))
        {
            EnemyInstance selected = FindNearestVisibleGroundTarget();
            if (selected != currentTarget)
            {
                currentTarget = selected;
                velocityTracker.Reset();
            }
            selectTargetAfterShot = false;
        }

        HasInterceptPoint = false;
        TimeToImpact = 0f;
        if (currentTarget == null) return;

        Vector3 targetPosition = PlasmaProjectile.AimPosition(currentTarget);
        Vector3 velocity = velocityTracker.Sample(targetPosition, clock, velocitySmoothingSeconds);
        float shellSpeed = EffectiveShellSpeed;
        if (!IsInRange(HorizontalDistance(targetPosition))) return;
        if (!MissileAimSolver.TrySolveIntercept(muzzle.position, targetPosition, velocity,
                shellSpeed, out Vector3 predictedPoint, out _,
                EffectiveMaximumRange / shellSpeed * 2f))
            return;

        if (!IsInRange(HorizontalDistance(predictedPoint))) return;
        if (!HasClearTargetPath(muzzle.position, predictedPoint, currentTarget)) return;

        InterceptPoint = predictedPoint;
        HasInterceptPoint = true;
        float targetDistance = HorizontalDistance(targetPosition);
        AimAt(predictedPoint, targetDistance, step);
        TimeToImpact = Vector3.Distance(muzzle.position, predictedPoint) / shellSpeed;

        if (clock + 0.0001f < nextShotTime || !IsYawAligned(predictedPoint)
            || Mathf.Abs(BarrelElevationDegrees - EffectiveElevationForDistance(targetDistance)) > aimToleranceDegrees
            || !IsWithinFiringElevation(predictedPoint)) return;
        FireAt(predictedPoint, TimeToImpact);
    }

    private EnemyInstance FindNearestVisibleGroundTarget()
    {
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None);
        EnemyInstance nearest = null;
        float nearestSqrDistance = float.PositiveInfinity;
        Vector3 origin = muzzle.position;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance candidate = enemies[i];
            if (!IsLivingGroundEnemy(candidate)) continue;

            Vector3 point = PlasmaProjectile.AimPosition(candidate);
            float sqrDistance = Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude;
            if (sqrDistance > EffectiveMaximumRange * EffectiveMaximumRange
                || sqrDistance < EffectiveMinimumRange * EffectiveMinimumRange)
                continue;
            if (sqrDistance >= nearestSqrDistance || !HasClearTargetPath(origin, point, candidate))
                continue;

            nearest = candidate;
            nearestSqrDistance = sqrDistance;
        }

        return nearest;
    }

    private static bool IsLivingGroundEnemy(EnemyInstance candidate)
    {
        if (candidate == null || !candidate.isActiveAndEnabled || candidate.MonsterType != MonsterType.Ground
            || MissileLauncherTurret.IsAirTarget(candidate))
            return false;
        EnemyHealth health = candidate.GetComponent<EnemyHealth>();
        return health != null && health.isActiveAndEnabled && !health.IsDead;
    }

    private bool IsTooCloseForArtillery(EnemyInstance candidate)
    {
        if (!IsLivingGroundEnemy(candidate)) return false;
        Vector3 point = PlasmaProjectile.AimPosition(candidate);
        float horizontalDistance = HorizontalDistance(point);
        return horizontalDistance < EffectiveMinimumRange;
    }

    private bool HasClearTargetPath(Vector3 origin, Vector3 destination, EnemyInstance candidate)
    {
        Vector3 flatDirection = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, flatDirection) * 0.75f;
        return HasClearRay(origin, destination, candidate)
            || HasClearRay(origin, destination + Vector3.up * 1.5f, candidate)
            || HasClearRay(origin, destination + side, candidate)
            || HasClearRay(origin, destination - side, candidate);
    }

    private bool HasClearRay(Vector3 origin, Vector3 destination, EnemyInstance candidate)
    {
        Vector3 delta = destination - origin;
        float distance = delta.magnitude;
        if (distance <= 0.001f) return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, delta / distance, distance,
            ~0, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.transform == transform || collider.transform.IsChildOf(transform))
                continue;
            if (hits[i].distance >= nearestDistance) continue;
            nearestDistance = hits[i].distance;
            nearestCollider = collider;
        }

        // Some enemy prefabs intentionally have no collider. In that case an
        // unobstructed ray is sufficient; when a collider is hit, it must belong
        // to the selected enemy and be the first non-self hit.
        return nearestCollider == null || nearestCollider.GetComponentInParent<EnemyInstance>() == candidate;
    }

    /// <summary>
    /// Turns the mount toward a world point by one step, setting elevation from
    /// base-to-point horizontal distance. Runtime uses the current enemy's
    /// distance for elevation and the predicted impact point for traverse.
    /// </summary>
    public void AimAt(Vector3 point, float deltaTime)
    {
        EnsureRestPose();
        AimAt(point, HorizontalDistance(point), deltaTime);
    }

    private float HorizontalDistance(Vector3 point) =>
        Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;

    private bool IsInRange(float distance) =>
        distance >= EffectiveMinimumRange && distance <= EffectiveMaximumRange;

    private void SynchronizeFireCadence()
    {
        bool isRG777 = IsRG777Active;
        if (!hasObservedBranchState)
        {
            observedRG777State = isRG777;
            hasObservedBranchState = true;
            return;
        }
        if (observedRG777State == isRG777) return;
        observedRG777State = isRG777;
        if (hasFired) nextShotTime = Mathf.Max(clock, lastShotTime + EffectiveFireInterval);
    }

    /// <summary>Linear 5..95 metre horizontal range to 0..45 degree elevation, clamped.</summary>
    public static float ElevationForDistance(float distance) =>
        Mathf.InverseLerp(MinimumRange, MaximumRange, distance) * 45f;

    /// <summary>Whether a target's required ground-relative firing elevation is inside the real firing arc.</summary>
    public static bool IsWithinFiringElevation(float elevationDegrees) =>
        elevationDegrees >= MinimumFiringElevationDegrees - 0.001f
        && elevationDegrees <= MaximumFiringElevationDegrees + 0.001f;

    private bool IsWithinFiringElevation(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(direction, yawAxisWorld);
        float requiredElevation = Mathf.Atan2(Vector3.Dot(direction, yawAxisWorld), flat.magnitude) * Mathf.Rad2Deg;
        return IsWithinFiringElevation(requiredElevation);
    }

    private void AimAt(Vector3 point, float targetDistance, float deltaTime)
    {
        AimYawAt(point, deltaTime);
        if (pitchPivot == null || pitchPivot == yawPivot) return;
        Quaternion desired = Quaternion.AngleAxis(EffectiveElevationForDistance(targetDistance) - restElevationDegrees,
            pitchAxisLocal) * restPitchRotation;
        pitchPivot.localRotation = Quaternion.RotateTowards(pitchPivot.localRotation, desired,
            Mathf.Max(0f, pitchSpeedDegrees) * Mathf.Max(0f, deltaTime));
    }

    private void AimYawAt(Vector3 point, float deltaTime)
    {
        if (yawPivot == null) return;
        Vector3 up = yawAxisWorld;
        Vector3 restForward = restBarrelForwardWorld;
        Vector3 currentAim = Vector3.ProjectOnPlane(point - muzzle.position, up);
        Vector3 flatRestForward = Vector3.ProjectOnPlane(restForward, up);
        if (currentAim.sqrMagnitude < 0.0001f || flatRestForward.sqrMagnitude < 0.0001f) return;

        float yaw = Vector3.SignedAngle(flatRestForward, currentAim, up);
        Quaternion desired = Quaternion.AngleAxis(yaw, up) * restYawRotation;
        yawPivot.rotation = Quaternion.RotateTowards(yawPivot.rotation, desired,
            Mathf.Max(0f, yawSpeedDegrees) * Mathf.Max(0f, deltaTime));
    }

    private bool IsYawAligned(Vector3 point)
    {
        Vector3 up = yawAxisWorld;
        Vector3 barrel = Vector3.ProjectOnPlane(muzzle.forward, up);
        Vector3 direction = Vector3.ProjectOnPlane(point - muzzle.position, up);
        return barrel.sqrMagnitude > 0.0001f && direction.sqrMagnitude > 0.0001f
            && Vector3.Angle(barrel, direction) <= aimToleranceDegrees;
    }

    private void FireAt(Vector3 impactPoint, float flightTime)
    {
        SpawnEffect(muzzleEffectPrefab, muzzle != null ? muzzle.position : transform.position,
            muzzle != null ? muzzle.rotation : transform.rotation);
        float damage = EffectiveExplosionDamage;
        pendingImpacts.Add(new PendingImpact
        {
            Point = impactPoint,
            Remaining = Mathf.Max(0f, flightTime),
            Damage = damage
        });
        ShotCount++;
        lastShotTime = clock;
        hasFired = true;
        nextShotTime = clock + EffectiveFireInterval;
        selectTargetAfterShot = true;
    }

    private void AdvanceImpacts(float deltaTime)
    {
        for (int i = pendingImpacts.Count - 1; i >= 0; i--)
        {
            PendingImpact impact = pendingImpacts[i];
            impact.Remaining -= deltaTime;
            if (impact.Remaining > 0f)
            {
                pendingImpacts[i] = impact;
                continue;
            }

            pendingImpacts.RemoveAt(i);
            Detonate(impact.Point, impact.Damage);
        }
    }

    private void Detonate(Vector3 point, float damage)
    {
        SpawnEffect(impactEffectPrefab, point, Quaternion.identity);
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None);
        var damaged = new HashSet<EnemyHealth>();
        float radiusSqr = ExplosionRadius * ExplosionRadius;
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance enemy = enemies[i];
            if (!IsLivingGroundEnemy(enemy)) continue;
            if ((PlasmaProjectile.AimPosition(enemy) - point).sqrMagnitude > radiusSqr) continue;

            EnemyHealth health = enemy.GetComponent<EnemyHealth>();
            if (health == null || !damaged.Add(health)) continue;
            // Blast damage intentionally ignores walls and other line-of-sight blockers.
            health.TakeDamage(damage, TrapDamageType.Explosive);
        }

        ExplosionCount++;
    }

    private static void SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return;
        GameObject instance = Instantiate(prefab, position, rotation);
        XM777VfxPlayback playback = instance.GetComponent<XM777VfxPlayback>();
        if (playback != null) playback.Play();
        else
        {
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            float lifetime = 0f;
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem particle = particles[i];
                if (particle == null) continue;
                particle.Play(true);
                ParticleSystem.MainModule main = particle.main;
                lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax);
            }
            Destroy(instance, particles.Length == 0 ? 2f : Mathf.Max(0.25f, lifetime + 0.25f));
        }
    }

    private void OnDisable()
    {
        currentTarget = null;
        selectTargetAfterShot = false;
        HasInterceptPoint = false;
        velocityTracker.Reset();
        pendingImpacts.Clear();
    }

    private static Transform FindChild(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName) return child;
            Transform nested = FindChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }
}
