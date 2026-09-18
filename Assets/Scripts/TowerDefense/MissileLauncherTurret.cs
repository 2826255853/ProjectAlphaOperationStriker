using System.Collections.Generic;
using UnityEngine;

/// <summary>Which monsters the dual missile launcher mount is allowed to track.</summary>
public enum MissileTargetFilter
{
    /// <summary>Only airborne monsters (flying entrances / grid-free flying movement).</summary>
    AirOnly = 0,
    /// <summary>Only monsters walking the ground path grid.</summary>
    GroundOnly = 1,
    /// <summary>Any live monster inside the detection range.</summary>
    Any = 2
}

/// <summary>How the mount chooses between several valid targets inside range.</summary>
public enum MissileTargetPriority
{
    /// <summary>Default: the enemy closest to the mount wins.</summary>
    ClosestToTurret = 0,
    /// <summary>Non-default debug/level option: the enemy closest to the core wins.</summary>
    ClosestToCore = 1
}

/// <summary>
/// Rotating mount for the dual missile launcher trap.
///
/// Yaw (horizontal) is unlimited and wraps a full 360 degrees. Pitch (elevation)
/// is hard clamped to 0..75 degrees, where 0 = the launcher resting horizontally
/// and 75 = the steepest upward elevation the rails allow. The launcher can never
/// aim below the horizon, no matter where the target sits.
///
/// The component drives an explicit rig built around the imported FBX:
/// Yaw Pivot (pedestal) -&gt; Pitch Pivot (missile cradle) -&gt; launcher meshes.
/// <see cref="BuildRig"/> builds that rig and also detects the barrel direction
/// from the missile meshes, so the raw model works without hand authored pivots.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Missile Launcher Turret")]
public sealed class MissileLauncherTurret : MonoBehaviour
{
    /// <summary>Hard lower bound: the launcher never aims below the horizon.</summary>
    public const float MinPitchDegrees = 0f;
    /// <summary>Hard upper bound: the launcher never aims past 75 degrees upward.</summary>
    public const float MaxPitchDegrees = 75f;

    /// <summary>Meshes that turn with the pedestal (yaw). Base/collar stay static.</summary>
    private static readonly string[] YawPartNames = { "Launcher_Rotating_Pylon", "Pylon_Top_Cap" };
    /// <summary>Model wrapper nodes that only group the imported art; never reparent them.</summary>
    private static readonly string[] ModelWrapperNames = { "Launcher Model", "DualMissileLauncher" };
    /// <summary>Meshes that elevate with the cradle (pitch).</summary>
    private static readonly string[] PitchPartNames =
    {
        "Dual_Missile_Launcher_Body", "Missile_Rail", "Launcher_End_Stop", "Missile_01", "Missile_02"
    };
    /// <summary>Base parts that stay bolted to the ground while the rig turns.</summary>
    private static readonly string[] StaticPartNames = { "Trap_Base_Disc", "Pylon_Base_Collar" };
    /// <summary>Fallback filters used when the exact names above are not present.</summary>
    private static readonly string[] YawFallbackFilters = { "Pylon" };
    private static readonly string[] PitchFallbackFilters = { "Missile", "Launcher", "Rail" };

    private const float DirectionEpsilon = 0.000001f;
    private const string YawPivotName = "Yaw Pivot";
    private const string PitchPivotName = "Pitch Pivot";

    [Header("Targeting")]
    [Tooltip("Auto aim at the nearest live enemy inside the detection range.")]
    [SerializeField] private bool trackNearestEnemy = true;
    [Tooltip("The launcher is an anti-air trap: it only swings at airborne monsters by default.")]
    [SerializeField] private MissileTargetFilter targetFilter = MissileTargetFilter.AirOnly;
    [Tooltip("Default keeps the original behaviour: the closest enemy to the mount wins.")]
    [SerializeField] private MissileTargetPriority targetPriority = MissileTargetPriority.ClosestToTurret;
    [Tooltip("When two candidates are this close to the mount (metres), the one nearer the core wins.")]
    [SerializeField, Min(0f)] private float tieBreakDistanceEpsilon = 0.25f;
    [Tooltip("Optional core used for the distance tie-break. Falls back to the target's spawn point core, then the scene core.")]
    [SerializeField] private EnemyCore core;
    [SerializeField, Min(0f)] private float detectionRange = 12f;

    [Header("Rotation Speed")]
    [SerializeField, Min(0f)] private float yawSpeedDegrees = 120f;
    [SerializeField, Min(0f)] private float pitchSpeedDegrees = 90f;

    [Header("Pitch Limits (0 = horizontal, 75 = up)")]
    [SerializeField, Range(MinPitchDegrees, MaxPitchDegrees)] private float minPitchDegrees = MinPitchDegrees;
    [SerializeField, Range(MinPitchDegrees, MaxPitchDegrees)] private float maxPitchDegrees = MaxPitchDegrees;
    [Tooltip("Snap the elevation back to the rest angle when no target is in range.")]
    [SerializeField] private bool returnToRestWhenIdle = true;
    [SerializeField, Range(MinPitchDegrees, MaxPitchDegrees)] private float restPitchDegrees = MinPitchDegrees;

    [Header("Rig")]
    [SerializeField] private Transform yawPivot;
    [SerializeField] private Transform pitchPivot;
    [Tooltip("Barrel direction in Yaw Pivot local space while the rig is at rest. Detected from the missile meshes.")]
    [SerializeField] private Vector3 barrelRestDirection = Vector3.right;
    [SerializeField] private bool autoBuildRigIfMissing = true;

    private EnemyInstance target;
    private Vector3 currentAimPoint;
    private bool hasAimPoint;
    private bool warnedMissingCore;
    private bool restCaptured;
    private Quaternion yawRestLocal = Quaternion.identity;
    private Quaternion pitchRestLocal = Quaternion.identity;

    public float DetectionRange => detectionRange;
    public bool TrackNearestEnemy => trackNearestEnemy;
    public MissileTargetFilter TargetFilter => targetFilter;
    public MissileTargetPriority TargetPriority => targetPriority;
    public float TieBreakDistanceEpsilon => tieBreakDistanceEpsilon;
    public EnemyCore Core => core;
    public float YawSpeedDegrees => yawSpeedDegrees;
    public float PitchSpeedDegrees => pitchSpeedDegrees;
    public float MinPitchLimit => minPitchDegrees;
    public float MaxPitchLimit => maxPitchDegrees;
    public float RestPitchDegrees => restPitchDegrees;
    public EnemyInstance CurrentTarget => target;
    public Transform YawPivot => yawPivot;
    public Transform PitchPivot => pitchPivot;
    public bool HasRig => yawPivot != null && pitchPivot != null;

    /// <summary>Yaw of the launcher in degrees, signed and wrapping over the full 360 range.</summary>
    public float CurrentYawDegrees
    {
        get
        {
            if (!HasRig) return 0f;
            Vector3 up = PedestalUp();
            Vector3 flat = Vector3.ProjectOnPlane(BarrelDirectionWorld, up);
            if (flat.sqrMagnitude < DirectionEpsilon) return 0f;
            return Vector3.SignedAngle(RestBarrelDirectionWorld, flat, up);
        }
    }

    /// <summary>Elevation of the launcher in degrees; always inside the 0..75 band.</summary>
    public float CurrentElevationDegrees
    {
        get
        {
            if (!HasRig) return 0f;
            Vector3 barrel = BarrelDirectionWorld;
            Vector3 flat = Vector3.ProjectOnPlane(barrel, PedestalUp());
            return Mathf.Atan2(Vector3.Dot(barrel, PedestalUp()), flat.magnitude) * Mathf.Rad2Deg;
        }
    }

    /// <summary>Clamps any requested elevation into the allowed 0..75 degree band.</summary>
    public static float ClampPitchDegrees(float pitchDegrees)
    {
        return Mathf.Clamp(pitchDegrees, MinPitchDegrees, MaxPitchDegrees);
    }

    private void Awake()
    {
        if (!HasRig && autoBuildRigIfMissing) BuildRig();
        if (core == null) core = FindAnyObjectByType<EnemyCore>();
        CaptureRestPose();
    }

    private void OnValidate()
    {
        detectionRange = Mathf.Max(0f, detectionRange);
        tieBreakDistanceEpsilon = Mathf.Max(0f, tieBreakDistanceEpsilon);
        yawSpeedDegrees = Mathf.Max(0f, yawSpeedDegrees);
        pitchSpeedDegrees = Mathf.Max(0f, pitchSpeedDegrees);
        minPitchDegrees = Mathf.Clamp(minPitchDegrees, MinPitchDegrees, MaxPitchDegrees);
        maxPitchDegrees = Mathf.Clamp(maxPitchDegrees, minPitchDegrees, MaxPitchDegrees);
        restPitchDegrees = Mathf.Clamp(restPitchDegrees, minPitchDegrees, maxPitchDegrees);
        if (barrelRestDirection.sqrMagnitude < 0.0001f) barrelRestDirection = Vector3.right;
    }

    private void Update()
    {
        if (!HasRig)
        {
            if (autoBuildRigIfMissing) BuildRig();
            return;
        }

        if (trackNearestEnemy)
        {
            AcquireTarget();
            if (target != null)
            {
                // When the weapon drives the mount it aims at the lead point in its
                // own Update; aiming here as well would overwrite it.
                if (!AimDrivenExternally) AimAt(target.transform.position);
                return;
            }
        }

        if (returnToRestWhenIdle) AimAtRest();
    }

    /// <summary>
    /// Re-runs target acquisition and reports whether a valid target exists now.
    /// The weapon calls this when it needs a target without waiting for the mount's
    /// own <c>Update</c> to run first (edit-mode tests and disabled mounts).
    /// </summary>
    public bool RefreshTarget()
    {
        AcquireTarget();
        return target != null;
    }

    /// <summary>
    /// Set by <see cref="MissileLauncherWeapon"/>: the weapon computes the lead
    /// point and calls <see cref="AimAt"/> itself, so the mount must stop aiming at
    /// the enemy's current position or the two would fight every frame.
    /// The mount still acquires targets, because the weapon reads
    /// <see cref="CurrentTarget"/>.
    /// </summary>
    public bool AimDrivenExternally { get; set; }

    /// <summary>True when a lead/intercept aim point has been recorded by the weapon.</summary>
    public bool HasAimPoint => hasAimPoint;

    /// <summary>
    /// World point the mount is currently aiming at, as recorded by the weapon's
    /// lead solver (<see cref="MissileAimSolver"/>). Returns false when nothing has
    /// been recorded yet, so callers can tell "no lead solution" from "lead at
    /// the enemy's own position".
    /// </summary>
    public bool TryGetCurrentAimPoint(out Vector3 aimPoint)
    {
        aimPoint = currentAimPoint;
        return hasAimPoint;
    }

    /// <summary>Records the lead/intercept point the mount is aiming at.</summary>
    public void SetCurrentAimPoint(Vector3 worldPoint)
    {
        currentAimPoint = worldPoint;
        hasAimPoint = true;
    }

    /// <summary>Forgets the recorded aim point, e.g. when the weapon stops tracking.</summary>
    public void ClearCurrentAimPoint()
    {
        hasAimPoint = false;
    }

    /// <summary>Overrides the pitch band at runtime; values are clamped into 0..75 degrees.</summary>
    public void ConfigurePitchLimits(float minPitch, float maxPitch)
    {
        minPitchDegrees = Mathf.Clamp(minPitch, MinPitchDegrees, MaxPitchDegrees);
        maxPitchDegrees = Mathf.Clamp(maxPitch, minPitchDegrees, MaxPitchDegrees);
        restPitchDegrees = Mathf.Clamp(restPitchDegrees, minPitchDegrees, maxPitchDegrees);
    }

    /// <summary>Turns the launcher toward a world position, respecting speed and the pitch limits.</summary>
    public void AimAt(Vector3 worldPosition)
    {
        AimAt(worldPosition, -1f);
    }

    /// <summary>
    /// Turns the launcher toward a world position with an explicit step, so the
    /// edit-mode tests do not depend on <see cref="Time.deltaTime"/> (which is zero
    /// in edit mode and would freeze the rig). A negative delta uses the frame time.
    /// </summary>
    public void AimAt(Vector3 worldPosition, float deltaTime)
    {
        if (!HasRig) return;
        float step = deltaTime >= 0f ? deltaTime : Mathf.Max(Time.deltaTime, 0.0001f);
        ApplyAim(worldPosition, false, Mathf.Max(step, 0.0001f));
    }

    /// <summary>Instantly points the launcher at a world position, still respecting the pitch limits.</summary>
    public void SnapAimAt(Vector3 worldPosition)
    {
        if (!HasRig) return;
        ApplyAim(worldPosition, true, 0f);
    }

    /// <summary>Brings the elevation back to the rest angle. Azimuth is left where it is.</summary>
    public void AimAtRest()
    {
        if (!HasRig) return;
        Quaternion yawWorld = yawPivot.rotation;
        Vector3 restAzimuthWorld = yawWorld * Vector3.ProjectOnPlane(barrelRestDirection, Vector3.up).normalized;
        Vector3 up = PedestalUp();
        Vector3 pitchAxisInYaw = Quaternion.Inverse(yawWorld) * Vector3.Cross(restAzimuthWorld, up);
        Quaternion desiredPitchWorld =
            yawWorld * Quaternion.AngleAxis(restPitchDegrees, pitchAxisInYaw) * pitchRestLocal;
        pitchPivot.rotation = Quaternion.RotateTowards(pitchPivot.rotation, desiredPitchWorld,
            pitchSpeedDegrees * Mathf.Max(Time.deltaTime, 0.0001f));
    }

    private void ApplyAim(Vector3 worldPosition, bool immediate, float deltaTime)
    {
        Quaternion yawRestWorld = PedestalParent().rotation * yawRestLocal;
        Vector3 up = PedestalUp();
        Vector3 restDirectionWorld = yawRestWorld * barrelRestDirection.normalized;

        Vector3 toTarget = worldPosition - pitchPivot.position;
        Vector3 flatToTarget = Vector3.ProjectOnPlane(toTarget, up);

        // ---- Yaw: free 360 degrees around the pedestal's vertical axis. ----
        float yawDelta = flatToTarget.sqrMagnitude > DirectionEpsilon
            ? Vector3.SignedAngle(restDirectionWorld, flatToTarget, up)
            : 0f;
        Quaternion desiredYawWorld = Quaternion.AngleAxis(yawDelta, up) * yawRestWorld;
        Vector3 azimuthWorld = Quaternion.AngleAxis(yawDelta, up) * Vector3.ProjectOnPlane(restDirectionWorld, up).normalized;

        // ---- Pitch: 0 degrees (horizontal) up to 75 degrees (upward). ----
        float requestedPitch = Mathf.Atan2(Vector3.Dot(toTarget, up), flatToTarget.magnitude) * Mathf.Rad2Deg;
        float clampedPitch = Mathf.Clamp(requestedPitch, minPitchDegrees, maxPitchDegrees);
        Vector3 pitchAxisWorld = azimuthWorld.sqrMagnitude > DirectionEpsilon
            ? Vector3.Cross(azimuthWorld, up)
            : Vector3.Cross(Vector3.ProjectOnPlane(restDirectionWorld, up).normalized, up);
        Quaternion desiredPitchWorld =
            Quaternion.AngleAxis(clampedPitch, pitchAxisWorld) * desiredYawWorld * pitchRestLocal;

        if (immediate)
        {
            yawPivot.rotation = desiredYawWorld;
            pitchPivot.rotation = desiredPitchWorld;
            return;
        }

        yawPivot.rotation = Quaternion.RotateTowards(yawPivot.rotation, desiredYawWorld, yawSpeedDegrees * deltaTime);
        pitchPivot.rotation = Quaternion.RotateTowards(pitchPivot.rotation, desiredPitchWorld, pitchSpeedDegrees * deltaTime);
    }

    private void AcquireTarget()
    {
        if (IsValidTarget(target)) return;

        EnemyInstance best = null;
        float bestSqrDistance = float.PositiveInfinity;
        float bestCoreSqrDistance = float.PositiveInfinity;
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>();
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance candidate = enemies[i];
            if (!IsValidTarget(candidate)) continue;

            float sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
            float coreSqrDistance = CoreSqrDistance(candidate);

            // ClosestToTurret (default) keys on the mount distance and only uses the
            // core distance to break ties; ClosestToCore swaps the two keys.
            bool primaryIsCore = targetPriority == MissileTargetPriority.ClosestToCore;
            bool better = primaryIsCore
                ? IsBetterByScore(coreSqrDistance, sqrDistance, bestCoreSqrDistance, bestSqrDistance)
                : IsBetterByScore(sqrDistance, coreSqrDistance, bestSqrDistance, bestCoreSqrDistance);
            if (!better) continue;

            best = candidate;
            bestSqrDistance = sqrDistance;
            bestCoreSqrDistance = coreSqrDistance;
        }

        target = best;
    }

    /// <summary>
    /// Compares two squared distances. The primary key decides unless the two
    /// candidates are within <see cref="tieBreakDistanceEpsilon"/> metres of each
    /// other, in which case the smaller secondary key wins. A missing core yields
    /// <see cref="float.PositiveInfinity"/> and therefore never breaks a tie.
    /// </summary>
    private bool IsBetterByScore(float primarySqr, float secondarySqr, float bestPrimarySqr, float bestSecondarySqr)
    {
        if (float.IsPositiveInfinity(bestPrimarySqr)) return true;

        float epsilon = Mathf.Max(0f, tieBreakDistanceEpsilon);
        float primaryDelta = Mathf.Abs(Mathf.Sqrt(primarySqr) - Mathf.Sqrt(bestPrimarySqr));
        // Exact tie on the primary key keeps the first candidate (deterministic),
        // because a missing core yields PositiveInfinity for the secondary key.
        if (primaryDelta > epsilon) return primarySqr < bestPrimarySqr;
        return secondarySqr < bestSecondarySqr;
    }

    /// <summary>
    /// Horizontal squared distance from a candidate to the core it is trying to
    /// reach, matching <see cref="EnemyCore.IsWithinArrivalRange"/> (Y is ignored).
    /// Returns <see cref="float.PositiveInfinity"/> when no core can be resolved.
    /// </summary>
    private float CoreSqrDistance(EnemyInstance candidate)
    {
        EnemyCore resolved = ResolveCore(candidate);
        if (resolved == null) return float.PositiveInfinity;
        Vector3 delta = candidate.transform.position - resolved.Position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    /// <summary>Per-candidate core lookup: spawn point core, then the authored core, then the scene core.</summary>
    private EnemyCore ResolveCore(EnemyInstance candidate)
    {
        EnemySpawnPoint spawnPoint = candidate != null ? candidate.SpawnPoint : null;
        if (spawnPoint != null && spawnPoint.Core != null) return spawnPoint.Core;
        if (core == null) core = FindAnyObjectByType<EnemyCore>();
        if (core == null && !warnedMissingCore)
        {
            warnedMissingCore = true;
            Debug.LogWarning(
                $"[{nameof(MissileLauncherTurret)}] 场景里找不到 {nameof(EnemyCore)}，" +
                "目标优先级只能退化为「先遍历到的那个」，破平规则失效。", this);
        }
        return core;
    }

    private bool IsValidTarget(EnemyInstance candidate)
    {
        if (candidate == null) return false;
        if (!MatchesTargetFilter(candidate)) return false;
        EnemyHealth health = candidate.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead) return false;
        if (detectionRange <= 0f) return true;
        return (candidate.transform.position - transform.position).sqrMagnitude <= detectionRange * detectionRange;
    }

    /// <summary>Applies <see cref="targetFilter"/> so the mount ignores the other monster layer.</summary>
    private bool MatchesTargetFilter(EnemyInstance candidate)
    {
        switch (targetFilter)
        {
            case MissileTargetFilter.AirOnly:
                return IsAirTarget(candidate);
            case MissileTargetFilter.GroundOnly:
                return !IsAirTarget(candidate);
            default:
                return true;
        }
    }

    /// <summary>
    /// True when the monster moves through the air: its spawn point is a flying
    /// entrance, the spawner tagged it as <see cref="MonsterType.Flying"/>, or its
    /// path follower is running the grid-free flying movement mode.
    /// </summary>
    public static bool IsAirTarget(EnemyInstance candidate)
    {
        if (candidate == null) return false;
        if (candidate.MonsterType == MonsterType.Flying) return true;
        MonsterPathFollower follower = candidate.PathFollower != null
            ? candidate.PathFollower
            : candidate.GetComponent<MonsterPathFollower>();
        if (follower != null && follower.IsFlying) return true;
        EnemySpawnPoint spawnPoint = candidate.SpawnPoint;
        return spawnPoint != null && spawnPoint.IsFlyingEntrance;
    }

    /// <summary>Captures the current pivot rotations as the rest pose used by all aim math.</summary>
    public void CaptureRestPose()
    {
        if (!HasRig) return;
        yawRestLocal = yawPivot.localRotation;
        pitchRestLocal = pitchPivot.localRotation;
        restCaptured = true;
    }

    /// <summary>Barrel direction of the launcher in world space, at rest.</summary>
    public Vector3 RestBarrelDirectionWorld => RestBarrelDirectionValue();

    /// <summary>Barrel direction of the launcher in world space, at its current aim.</summary>
    public Vector3 BarrelDirectionWorld => HasRig ? pitchPivot.rotation * Quaternion.Inverse(pitchRestLocal) * barrelRestDirection.normalized : transform.forward;

    private Vector3 RestBarrelDirectionValue()
    {
        if (!HasRig) return transform.rotation * barrelRestDirection.normalized;
        if (!restCaptured) return (yawPivot.parent != null ? yawPivot.parent.rotation : transform.rotation) * barrelRestDirection.normalized;
        Transform parent = PedestalParent();
        return (parent.rotation * yawRestLocal) * barrelRestDirection.normalized;
    }

    private Transform PedestalParent()
    {
        if (yawPivot != null && yawPivot.parent != null) return yawPivot.parent;
        return transform;
    }

    private Vector3 PedestalUp()
    {
        return PedestalParent().up;
    }

    /// <summary>
    /// Builds the Yaw Pivot / Pitch Pivot rig around the imported launcher model and
    /// reparents the pedestal and cradle meshes under them. Safe to call repeatedly:
    /// an existing rig is reused, and only the missing pivots are created.
    /// </summary>
    public bool BuildRig()
    {
        Transform existingYaw = FindDeepChild(transform, YawPivotName);
        Transform existingPitch = existingYaw != null ? FindDeepChild(existingYaw, PitchPivotName) : null;
        if (existingYaw != null && existingPitch != null)
        {
            yawPivot = existingYaw;
            pitchPivot = existingPitch;
            DetectBarrelDirection();
            CaptureRestPose();
            return true;
        }

        Transform pedestal = FindPart(transform, YawPartNames, YawFallbackFilters);
        Transform cradle = FindPart(transform, PitchPartNames, PitchFallbackFilters);
        if (pedestal == null && cradle == null) return false;

        Vector3 yawAnchor = pedestal != null ? RendererCenter(pedestal, transform) : RendererCenter(cradle, transform);
        if (cradle == null) return false;
        Vector3 pitchAnchor = RendererCenter(cradle, transform);

        GameObject yawObject = new GameObject(YawPivotName);
        yawObject.transform.SetParent(transform, false);
        yawObject.transform.localPosition = new Vector3(yawAnchor.x, yawAnchor.y, yawAnchor.z);
        yawObject.transform.localRotation = Quaternion.identity;
        yawPivot = yawObject.transform;

        GameObject pitchObject = new GameObject(PitchPivotName);
        pitchObject.transform.SetParent(yawPivot, false);
        pitchObject.transform.localPosition = pitchAnchor - yawObject.transform.localPosition;
        pitchObject.transform.localRotation = Quaternion.identity;
        pitchPivot = pitchObject.transform;

        MoveParts(transform, yawPivot, YawPartNames, YawFallbackFilters);
        MoveParts(transform, pitchPivot, PitchPartNames, PitchFallbackFilters);
        DetectBarrelDirection();
        CaptureRestPose();
        return true;
    }

    /// <summary>Resets the rig to its rest pose; used before rebuilding so math stays predictable.</summary>
    public void ResetRigToRestPose()
    {
        if (!HasRig) return;
        yawPivot.localRotation = yawRestLocal;
        pitchPivot.localRotation = pitchRestLocal;
    }

    /// <summary>Detects the barrel direction from the missile nose/body meshes.</summary>
    private void DetectBarrelDirection()
    {
        Transform nose = FindDeepChild(transform, "Missile_01_Rounded_Nose");
        Transform body = FindDeepChild(transform, "Missile_01_Body");
        if (nose == null || body == null || yawPivot == null) return;
        Vector3 direction = RendererCenter(nose, yawPivot) - RendererCenter(body, yawPivot);
        direction.y = 0f;
        if (direction.sqrMagnitude < DirectionEpsilon) return;
        barrelRestDirection = direction.normalized;
    }

    /// <summary>
    /// Reparents every top-most part belonging to one pivot. Only the highest
    /// matching node in each branch moves, so nested meshes (missile bodies under
    /// a Missile_01 group) travel with their parent instead of being split up.
    /// </summary>
    private static void MoveParts(Transform source, Transform destination, string[] exactNames, string[] fallbackFilters)
    {
        var matches = new List<Transform>();
        CollectTopMostMatches(source, destination, exactNames, fallbackFilters, false, matches);
        if (matches.Count == 0)
            CollectTopMostMatches(source, destination, exactNames, fallbackFilters, true, matches);
        for (int i = 0; i < matches.Count; i++)
            if (matches[i] != null) matches[i].SetParent(destination, true);
    }

    private static void CollectTopMostMatches(Transform root, Transform destination, string[] exactNames,
        string[] fallbackFilters, bool allowFuzzy, List<Transform> matches)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == destination || child.IsChildOf(destination)) continue;
            if (IsPart(child.name, exactNames, fallbackFilters, allowFuzzy))
            {
                matches.Add(child);
                continue;
            }
            CollectTopMostMatches(child, destination, exactNames, fallbackFilters, allowFuzzy, matches);
        }
    }

    private static bool IsPart(string name, string[] exactNames, string[] fallbackFilters, bool allowFuzzy)
    {
        if (IsExcludedName(name)) return false;
        if (!allowFuzzy)
        {
            for (int i = 0; i < exactNames.Length; i++)
                if (name == exactNames[i] || name.StartsWith(exactNames[i])) return true;
            return false;
        }
        for (int i = 0; i < fallbackFilters.Length; i++)
            if (name.Contains(fallbackFilters[i])) return true;
        return false;
    }

    /// <summary>Static base parts that must never follow the rotating rig.</summary>
    private static bool IsExcludedName(string name)
    {
        for (int i = 0; i < ModelWrapperNames.Length; i++)
            if (name == ModelWrapperNames[i]) return true;
        for (int i = 0; i < StaticPartNames.Length; i++)
            if (name == StaticPartNames[i] || name.StartsWith(StaticPartNames[i])) return true;
        return false;
    }

    private static Transform FindPart(Transform root, string[] exactNames, string[] fallbackFilters)
    {
        for (int i = 0; i < exactNames.Length; i++)
        {
            Transform exact = FindDeepChild(root, exactNames[i]);
            if (exact != null) return exact;
            Transform prefix = FindDeepChildStartingWith(root, exactNames[i]);
            if (prefix != null) return prefix;
        }
        for (int i = 0; i < fallbackFilters.Length; i++)
        {
            Transform fuzzy = FindDeepChildContaining(root, fallbackFilters[i]);
            if (fuzzy != null) return fuzzy;
        }
        return null;
    }

    private static Vector3 RendererCenter(Transform target, Transform space)
    {
        if (target == null) return Vector3.zero;
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null) return space.InverseTransformPoint(renderer.bounds.center);
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Renderer childRenderer in target.GetComponentsInChildren<Renderer>())
        {
            sum += childRenderer.bounds.center;
            count++;
        }
        return count > 0 ? space.InverseTransformPoint(sum / count) : space.InverseTransformPoint(target.position);
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

    private static Transform FindDeepChildStartingWith(Transform root, string prefix)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.StartsWith(prefix)) return child;
            Transform nested = FindDeepChildStartingWith(child, prefix);
            if (nested != null) return nested;
        }
        return null;
    }

    private static Transform FindDeepChildContaining(Transform root, string token)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.Contains(token)) return child;
            Transform nested = FindDeepChildContaining(child, token);
            if (nested != null) return nested;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!HasRig) return;
        Vector3 origin = pitchPivot.position;
        Vector3 barrel = BarrelDirectionWorld;
        Vector3 rest = RestBarrelDirectionWorld;
        Vector3 up = PedestalUp();

        Gizmos.color = new Color(0.35f, 0.85f, 1f);
        Gizmos.DrawLine(origin, origin + barrel * 2.5f);
        Gizmos.DrawWireSphere(origin, 0.1f);

        // The elevation fan shows the reachable 0..75 degree band.
        Vector3 pitchAxis = Vector3.Cross(rest, up);
        Gizmos.color = new Color(0.4f, 1f, 0.4f);
        Gizmos.DrawLine(origin, origin + rest * 2.5f);
        Gizmos.color = new Color(1f, 0.4f, 0.4f);
        Gizmos.DrawLine(origin, origin + Quaternion.AngleAxis(maxPitchDegrees, pitchAxis) * rest * 2.5f);

        if (target != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(target.transform.position, Vector3.one * 0.6f);
        }

        if (hasAimPoint)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(currentAimPoint, 0.35f);
            Gizmos.DrawLine(origin, currentAimPoint);
        }

        if (detectionRange <= 0f) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
