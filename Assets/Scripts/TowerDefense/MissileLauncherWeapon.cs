using System.Collections.Generic;
using UnityEngine;

/// <summary>Firing state of the dual missile launcher.</summary>
public enum MissileLauncherState
{
    /// <summary>No valid target: nothing to do.</summary>
    Idle = 0,
    /// <summary>Tracking a target and waiting for the barrel to line up with the lead point.</summary>
    Aiming = 1,
    /// <summary>Launching the current salvo, one tube at a time.</summary>
    Firing = 2,
    /// <summary>Reloading after a salvo.</summary>
    Reloading = 3
}

/// <summary>
/// Firing half of the dual missile launcher trap: it tracks the mount's current
/// target, aims the barrel at the predicted intercept point (rule R3), launches a
/// two-missile salvo (one per tube), then reloads.
///
/// The mount (<see cref="MissileLauncherTurret"/>) owns the rotation rig; this
/// component owns the lead solution, the trigger, the salvo cadence and the
/// in-flight bookkeeping.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Missile Launcher Weapon")]
public sealed class MissileLauncherWeapon : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Mount that provides the target and the barrel direction. Found on this object when empty.")]
    [SerializeField] private MissileLauncherTurret turret;
    [Tooltip("Barrel must be within this many degrees of the intercept point before a missile is released.")]
    [SerializeField, Min(0f)] private float aimToleranceDegrees = 8f;

    [Header("Salvo")]
    [SerializeField, Min(1)] private int missilesPerSalvo = 2;
    [SerializeField, Min(0f)] private float launchInterval = 0.25f;
    [SerializeField, Min(0.01f)] private float reloadTime = 4f;

    [Header("Projectile")]
    [SerializeField] private MissileProjectile projectilePrefab;
    [SerializeField] private Transform muzzleA;
    [SerializeField] private Transform muzzleB;
    [Tooltip("Optional reload art: the tube's round disappears on launch and returns when the reload finishes. Added at runtime when missing.")]
    [SerializeField] private MissileLauncherReloadVisual reloadVisual;
    [SerializeField, Min(0f)] private float damagePerMissile = 45f;
    [SerializeField, Min(0f)] private float splashRadius = 2.5f;
    [SerializeField, Range(0f, 1f)] private float splashDamageRatio = 0.5f;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 100f;
    [SerializeField, Min(0f)] private float turnRateDegrees = 360f;
    [Tooltip("Flight range of one missile. It is independent of the mount's detection range by default.")]
    [SerializeField, Min(0.01f)] private float maxFlightRange = 50f;
    [Tooltip("True: the range detonation reuses the mount's detection range. False: the missile flies its own maxFlightRange, so it can still catch a target that left the detection sphere.")]
    [SerializeField] private bool flightRangeFollowsDetection;

    [Header("Lead")]
    [SerializeField] private MissileGuidanceMode guidanceMode = MissileGuidanceMode.ContinuousLeadRefine;
    [SerializeField, Min(0.01f)] private float leadRefreshSeconds = 0.1f;
    [Tooltip("Exponential smoothing applied to the sampled target velocity.")]
    [SerializeField, Min(0f)] private float velocitySmoothingSeconds = 0.2f;

    private MissileLauncherState state = MissileLauncherState.Idle;
    private float stateTimer;
    private float reloadRemaining;
    private int missilesLaunchedInSalvo;
    private int missilesInFlight;
    private EnemyInstance trackedTarget;
    private Vector3 interceptPoint;
    private bool hasIntercept;
    private bool usingLead;
    private float interceptRefreshTimer;
    private readonly Dictionary<EnemyInstance, MissileVelocityTracker> velocityTrackers =
        new Dictionary<EnemyInstance, MissileVelocityTracker>();

    public MissileLauncherState State => state;
    public float ReloadRemaining => reloadRemaining;
    public int MissilesInFlight => missilesInFlight;
    public bool IsReloading => reloadRemaining > 0f;
    public float AimToleranceDegrees => aimToleranceDegrees;
    public int MissilesPerSalvo => missilesPerSalvo;
    public float LaunchInterval => launchInterval;
    public float ReloadTime => reloadTime;
    public float DamagePerMissile => damagePerMissile;
    public float SplashRadius => splashRadius;
    public float SplashDamageRatio => splashDamageRatio;
    public float ProjectileSpeed => projectileSpeed;
    public float TurnRateDegrees => turnRateDegrees;
    public float MaxFlightRange => maxFlightRange;
    public bool FlightRangeFollowsDetection => flightRangeFollowsDetection;
    public MissileGuidanceMode GuidanceMode => guidanceMode;
    public float LeadRefreshSeconds => leadRefreshSeconds;
    public MissileLauncherTurret Turret => turret;
    /// <summary>Reload art driver: one loaded round per tube, empty while reloading.</summary>
    public MissileLauncherReloadVisual ReloadVisual => reloadVisual;
    /// <summary>True when this tube has no round on its rail right now.</summary>
    public bool IsTubeEmpty(int tubeIndex)
    {
        return reloadVisual != null && !reloadVisual.IsTubeLoaded(tubeIndex);
    }
    public EnemyInstance CurrentTarget => turret != null ? turret.CurrentTarget : trackedTarget;
    public Vector3 InterceptPoint => interceptPoint;
    /// <summary>True once a usable aim point exists (a lead solution or the enemy-position fallback).</summary>
    public bool HasInterceptPoint => hasIntercept;
    /// <summary>True when the current aim point is a real lead solution, false when it fell back to the enemy.</summary>
    public bool IsUsingLead => usingLead;

    private void Awake()
    {
        if (turret == null) turret = GetComponent<MissileLauncherTurret>();
        ResolveMuzzles();
        ResolveReloadVisual();
    }

    /// <summary>
    /// Wires the two tubes from the imported model when they were not authored on
    /// the prefab. The FBX names its missiles Missile_01 / Missile_02, which is the
    /// same convention <see cref="MissileLauncherTurret"/> uses for the rig.
    /// </summary>
    private void ResolveMuzzles()
    {
        if (muzzleA == null) muzzleA = FindDeepChild(transform, "Missile_01");
        if (muzzleB == null) muzzleB = FindDeepChild(transform, "Missile_02");
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform nested = FindDeepChild(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>
    /// Finds (or adds) the reload art driver and hands it the same two tube
    /// transforms the muzzles use, so "this tube is empty" and "fire from this
    /// tube" always refer to the same rail.
    /// </summary>
    private void ResolveReloadVisual()
    {
        if (reloadVisual == null) reloadVisual = GetComponent<MissileLauncherReloadVisual>();
        if (reloadVisual == null) reloadVisual = gameObject.AddComponent<MissileLauncherReloadVisual>();
        reloadVisual.SetTubes(muzzleA, muzzleB);
        reloadVisual.RestoreAll();
    }

    private void OnEnable()
    {
        // Rule R3: the weapon owns the aiming so the mount does not fight it by
        // tracking the enemy's current position in its own Update.
        if (turret != null) turret.AimDrivenExternally = true;
        // A freshly placed / re-enabled launcher starts with a full rack; an
        // interrupted reload must not leave the rails empty.
        if (reloadVisual != null) reloadVisual.RestoreAll();
    }

    private void OnDisable()
    {
        if (turret != null) turret.AimDrivenExternally = false;
        if (turret != null) turret.ClearCurrentAimPoint();
        // Outside the enclosing object being torn down, put the rounds back so
        // disabling the component never leaves a visibly empty rack.
        if (reloadVisual != null && gameObject.activeInHierarchy) reloadVisual.RestoreAll();
        velocityTrackers.Clear();
        hasIntercept = false;
        usingLead = false;
    }

    private void OnValidate()
    {
        aimToleranceDegrees = Mathf.Max(0f, aimToleranceDegrees);
        missilesPerSalvo = Mathf.Max(1, missilesPerSalvo);
        launchInterval = Mathf.Max(0f, launchInterval);
        reloadTime = Mathf.Max(0.01f, reloadTime);
        damagePerMissile = Mathf.Max(0f, damagePerMissile);
        splashRadius = Mathf.Max(0f, splashRadius);
        projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
        turnRateDegrees = Mathf.Max(0f, turnRateDegrees);
        maxFlightRange = Mathf.Max(0.01f, maxFlightRange);
        leadRefreshSeconds = Mathf.Max(0.01f, leadRefreshSeconds);
        velocitySmoothingSeconds = Mathf.Max(0f, velocitySmoothingSeconds);
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    /// <summary>
    /// Steps the fire control with an explicit delta, so the update loop and the
    /// edit-mode tests share one deterministic path instead of both reading
    /// <see cref="Time.deltaTime"/>.
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (turret == null) turret = GetComponent<MissileLauncherTurret>();
        if (deltaTime < 0f) deltaTime = 0f;

        if (reloadRemaining > 0f)
        {
            reloadRemaining = Mathf.Max(0f, reloadRemaining - deltaTime);
            // "Loaded again" happens exactly when the timer runs out: the empty
            // rails get their rounds back before anything else looks at them.
            if (reloadRemaining <= 0f) FinishReload();
        }

        EnemyInstance target = AcquireTarget();
        if (target == null)
        {
            state = reloadRemaining > 0f ? MissileLauncherState.Reloading : MissileLauncherState.Idle;
            if (turret != null) turret.ClearCurrentAimPoint();
            hasIntercept = false;
            usingLead = false;
            return;
        }

        if (!UpdateInterceptPoint(target, deltaTime))
        {
            state = MissileLauncherState.Aiming;
            return;
        }

        // Rule R3: the mount itself swings onto the lead point, not the enemy.
        if (turret != null) turret.AimAt(interceptPoint, deltaTime);

        state = reloadRemaining > 0f ? MissileLauncherState.Reloading : MissileLauncherState.Aiming;
        if (reloadRemaining > 0f)
        {
            stateTimer = 0f;
            missilesLaunchedInSalvo = 0;
            return;
        }
        if (!IsBarrelAligned(target)) return;

        state = MissileLauncherState.Firing;
        stateTimer -= deltaTime;
        if (stateTimer > 0f) return;

        LaunchOneMissile(target);
        missilesLaunchedInSalvo++;
        stateTimer = launchInterval;
        if (missilesLaunchedInSalvo >= missilesPerSalvo)
        {
            reloadRemaining = reloadTime;
            missilesLaunchedInSalvo = 0;
            stateTimer = 0f;
            state = MissileLauncherState.Reloading;
        }
    }

    /// <summary>Called once when the reload timer reaches zero: the rack is full again.</summary>
    private void FinishReload()
    {
        missilesLaunchedInSalvo = 0;
        if (reloadVisual != null) reloadVisual.RestoreAll();
    }

    /// <summary>The mount's live target, falling back to the last one we tracked.</summary>
    private EnemyInstance AcquireTarget()
    {
        // The mount may not have run its own Update yet (disabled mount, or an
        // edit-mode test driving this component alone), so ask it to refresh.
        EnemyInstance candidate = turret != null ? turret.CurrentTarget : null;
        if (candidate == null && turret != null && turret.RefreshTarget())
            candidate = turret.CurrentTarget;
        if (candidate != null)
        {
            trackedTarget = candidate;
            return candidate;
        }

        if (trackedTarget != null)
        {
            EnemyHealth health = trackedTarget.GetComponent<EnemyHealth>();
            if (health != null && !health.IsDead) return trackedTarget;
        }

        trackedTarget = null;
        return null;
    }

    /// <summary>
    /// Refreshes the lead solution. Returns false when no usable intercept point
    /// exists, so the caller keeps aiming without firing.
    /// </summary>
    private bool UpdateInterceptPoint(EnemyInstance target, float deltaTime)
    {
        interceptRefreshTimer -= deltaTime;
        if (hasIntercept && interceptRefreshTimer > 0f && guidanceMode == MissileGuidanceMode.ContinuousLeadRefine)
            return true;

        interceptRefreshTimer = leadRefreshSeconds;

        Vector3 solved;
        float timeToImpact;
        // Always yields a usable aim point: the lead solution when solvable, the
        // enemy's own position otherwise. That keeps the mount aiming instead of
        // stalling short of its tolerance (never-firing deadlock).
        hasIntercept = TryGetInterceptPoint(target, out solved, out timeToImpact);
        if (hasIntercept) interceptPoint = solved;

        if (turret != null)
        {
            if (hasIntercept) turret.SetCurrentAimPoint(interceptPoint);
            else turret.ClearCurrentAimPoint();
        }
        return hasIntercept;
    }

    /// <summary>
    /// Aim point for a candidate. Returns true whenever the candidate is valid and
    /// a usable aim point exists; <see cref="IsUsingLead"/> then says whether it is
    /// a real lead solution or the enemy-position fallback.
    ///
    /// Elevation guard (rule R3 against the 0..75 degree pitch band): a head-on
    /// target can push the lead point past the elevation limit. We then aim at the
    /// enemy itself, and if even that is above the limit the mount simply keeps
    /// turning - it never fires from a clamped pose.
    /// </summary>
    public bool TryGetInterceptPoint(EnemyInstance candidate, out Vector3 interceptPoint, out float timeToImpact)
    {
        interceptPoint = transform.position;
        timeToImpact = 0f;
        usingLead = false;
        if (candidate == null) return false;

        Vector3 shooter = turret != null && turret.PitchPivot != null ? turret.PitchPivot.position : transform.position;
        Vector3 targetPosition = candidate.transform.position;
        Vector3 velocity = EstimateVelocity(candidate);

        bool solved = MissileAimSolver.TrySolveIntercept(shooter, targetPosition, velocity, projectileSpeed,
            out Vector3 lead, out timeToImpact);

        if (solved && !ExceedsElevationLimit(shooter, lead))
        {
            interceptPoint = lead;
            usingLead = true;
            return true;
        }

        // Fallback: aim at the enemy itself so the mount keeps tracking instead of
        // stalling. If this is out of elevation too, the caller's alignment check
        // keeps the trigger safe and the launcher waits for the target to come down.
        interceptPoint = targetPosition;
        timeToImpact = projectileSpeed > 0f ? Vector3.Distance(shooter, targetPosition) / projectileSpeed : 0f;
        return true;
    }

    /// <summary>True when the mount cannot elevate enough to point at this world position.</summary>
    private bool ExceedsElevationLimit(Vector3 shooter, Vector3 worldPoint)
    {
        if (turret == null) return false;
        Vector3 up = turret.YawPivot != null && turret.YawPivot.parent != null
            ? turret.YawPivot.parent.up
            : Vector3.up;
        Vector3 toPoint = worldPoint - shooter;
        Vector3 flat = Vector3.ProjectOnPlane(toPoint, up);
        float elevation = Mathf.Atan2(Vector3.Dot(toPoint, up), flat.magnitude) * Mathf.Rad2Deg;
        return elevation > turret.MaxPitchLimit + 0.5f;
    }

    /// <summary>True when the barrel already points at the lead point within tolerance.</summary>
    public bool IsBarrelAligned(EnemyInstance target)
    {
        if (turret == null || !hasIntercept) return false;
        Vector3 barrel = turret.BarrelDirectionWorld;
        Vector3 toPoint = interceptPoint - MuzzlePosition();
        if (toPoint.sqrMagnitude < 0.000001f) return true;
        return Vector3.Angle(barrel, toPoint) <= aimToleranceDegrees;
    }

    /// <summary>Smoothed world-space velocity estimate for a tracked enemy, with an analytic fallback.</summary>
    public Vector3 EstimateVelocity(EnemyInstance candidate)
    {
        if (candidate == null) return Vector3.zero;

        MissileVelocityTracker tracker;
        if (!velocityTrackers.TryGetValue(candidate, out tracker))
        {
            tracker = new MissileVelocityTracker();
            velocityTrackers[candidate] = tracker;
        }

        Vector3 sampled = tracker.Sample(candidate.transform.position, Time.time, velocitySmoothingSeconds);
        velocityTrackers[candidate] = tracker;

        // The follower knows its speed and destination, so use it while the
        // sampled difference is still warming up (or has stalled).
        MonsterPathFollower follower = candidate.PathFollower != null
            ? candidate.PathFollower
            : candidate.GetComponent<MonsterPathFollower>();
        if (follower != null && follower.IsFlying)
        {
            Vector3 toDestination = follower.DestinationPosition - candidate.transform.position;
            toDestination.y = 0f;
            if (toDestination.sqrMagnitude > 0.0001f)
            {
                Vector3 analytic = toDestination.normalized * follower.MoveSpeed;
                return sampled.sqrMagnitude > 0.0001f ? Vector3.Lerp(analytic, sampled, 0.5f) : analytic;
            }
        }

        if (velocityTrackers.Count > 32) PruneVelocityTrackers();
        return sampled;
    }

    /// <summary>Drops trackers for enemies that no longer exist, so their pins do not leak.</summary>
    private void PruneVelocityTrackers()
    {
        var stale = new List<EnemyInstance>();
        foreach (KeyValuePair<EnemyInstance, MissileVelocityTracker> pair in velocityTrackers)
            if (pair.Key == null) stale.Add(pair.Key);
        for (int i = 0; i < stale.Count; i++) velocityTrackers.Remove(stale[i]);
    }

    private Vector3 MuzzlePosition()
    {
        Transform muzzle = FirstMuzzle();
        if (muzzle != null) return muzzle.position;
        return turret != null && turret.PitchPivot != null ? turret.PitchPivot.position : transform.position;
    }

    /// <summary>
    /// Fires one missile out of the next tube, alternating left/right so a salvo
    /// leaves one round per rail. The missile departs along the barrel direction
    /// (the mount has already turned to the lead point) and reports back through
    /// <see cref="MissileProjectile.Destroyed"/> so the in-flight count stays exact.
    /// </summary>
    private void LaunchOneMissile(EnemyInstance target)
    {
        if (target == null) return;

        EnemyHealth health = target.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead) return;

        int tubeIndex = NextTubeIndex();
        Transform muzzle = MuzzleAt(tubeIndex);
        Vector3 origin = muzzle != null ? muzzle.position : MuzzlePosition();
        // Rule R3: leave the tube along the barrel, which is already on the lead point.
        Vector3 direction = turret != null && turret.HasRig
            ? turret.BarrelDirectionWorld
            : (interceptPoint - origin);

        MissileProjectile missile = projectilePrefab != null
            ? Instantiate(projectilePrefab, origin, Quaternion.LookRotation(
                direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward, Vector3.up))
            : CreateFallbackMissile(origin, direction);

        if (missile == null) return;

        // Placement can add TrapInstance after Awake; resolve it at launch time.
        TrapInstance trap = GetComponent<TrapInstance>();
        float shotDamage = trap != null ? trap.GetEffectiveDamage(damagePerMissile) : damagePerMissile;
        missile.ConfigureTuning(shotDamage, splashRadius, splashDamageRatio, projectileSpeed,
            turnRateDegrees, maxFlightRange, guidanceMode, leadRefreshSeconds);
        // The mount's detection sphere and the missile's flight range are two
        // different limits: the missile may keep chasing a target that already
        // left the sphere, and detonates at its own range instead.
        missile.SetFollowMountRange(flightRangeFollowsDetection);
        missile.Initialize(turret, target, interceptPoint, direction);

        missile.Destroyed += HandleMissileDestroyed;
        missilesInFlight++;

        // The round physically left this rail, so the rail reads as empty from now
        // on - until FinishReload puts a fresh round back.
        if (tubeIndex >= 0 && reloadVisual != null) reloadVisual.ClearTube(tubeIndex);
    }

    /// <summary>Index of the tube that fires next (0 or 1); -1 when no tube is wired.</summary>
    private int NextTubeIndex()
    {
        bool hasA = muzzleA != null;
        bool hasB = muzzleB != null;
        if (!hasA && !hasB) return -1;
        if (!hasA) return 1;
        if (!hasB) return 0;
        return missilesLaunchedInSalvo % 2;
    }

    /// <summary>The muzzle of one tube, falling back to the other rail when it is missing.</summary>
    private Transform MuzzleAt(int tubeIndex)
    {
        if (tubeIndex == 1)
        {
            if (muzzleB != null) return muzzleB;
            return muzzleA;
        }
        if (muzzleA != null) return muzzleA;
        return muzzleB;
    }

    /// <summary>Builds a minimal missile when no prefab is assigned, mirroring the sentry gun's fallback art.</summary>
    private MissileProjectile CreateFallbackMissile(Vector3 origin, Vector3 direction)
    {
        var root = new GameObject("Missile Projectile");
        root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(
            direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward, Vector3.up));

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Missile Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.12f, 0.28f, 0.12f);
        Collider collider = body.GetComponent<Collider>();
        if (collider != null) DestroyNow(collider);

        Renderer renderer = body.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = CreateMarkerMaterial(new Color(0.9f, 0.35f, 0.1f));

        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.08f;
        trail.endWidth = 0f;
        trail.material = CreateMarkerMaterial(new Color(1f, 0.7f, 0.3f, 0.6f));

        return root.AddComponent<MissileProjectile>();
    }

    private static Material CreateMarkerMaterial(Color color)
    {
        // Shader.Find can return null in a stripped player or a bare edit-mode
        // context; fall back to the always-present internal error shader instead of
        // throwing a NullReferenceException.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")
            ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        return material;
    }

    /// <summary>
    /// Removes a helper object. Edit mode forbids the deferred Destroy, so a test
    /// that builds a fallback missile needs the immediate variant.
    /// </summary>
    private static void DestroyNow(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private void HandleMissileDestroyed(MissileProjectile missile)
    {
        if (missile != null) missile.Destroyed -= HandleMissileDestroyed;
        missilesInFlight = Mathf.Max(0, missilesInFlight - 1);
    }

    /// <summary>Falls back to the pitch pivot when the authored tubes are missing.</summary>
    private Transform FirstMuzzle()
    {
        if (muzzleA != null) return muzzleA;
        if (muzzleB != null) return muzzleB;
        return null;
    }
}
