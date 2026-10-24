using UnityEngine;

/// <summary>
/// Automatic sentry machine gun trap. It acquires the nearest live enemy in
/// range, turns its gun toward that enemy and applies hitscan damage. Ammo is
/// unlimited; the firing limitations are a three-second burst followed by a
/// one-second cooldown, plus a fixed vertical field of fire: the barrel only
/// traverses between <see cref="MinPitchDegrees"/> (30 degrees down) and
/// <see cref="MaxPitchDegrees"/> (60 degrees up), and the gun holds fire for
/// targets outside that band instead of shooting through the limit.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[AddComponentMenu("Tower Defense/Auto Sentry Turret")]
public sealed class AutoSentryTurret : MonoBehaviour
{
    /// <summary>Lowest elevation the sentry barrel can reach (pointing down).</summary>
    public const float MinPitchDegrees = -30f;

    /// <summary>Highest elevation the sentry barrel can reach (pointing up).</summary>
    public const float MaxPitchDegrees = 60f;

    private const float AimToleranceDegrees = 5f;
    private const float ElevationToleranceDegrees = 0.5f;

    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float detectionRange = 5f;
    [SerializeField, Min(0f)] private float rotationSpeed = 360f;
    [SerializeField] private bool targetingFeedbackEnabled = true;

    [Header("Vertical Field of Fire")]
    [SerializeField, Range(MinPitchDegrees, MaxPitchDegrees)] private float minPitchDegrees = MinPitchDegrees;
    [SerializeField, Range(MinPitchDegrees, MaxPitchDegrees)] private float maxPitchDegrees = MaxPitchDegrees;

    [Header("Weapon")]
    [SerializeField, Min(0.01f)] private float roundsPerSecond = 8f;
    [SerializeField, Min(0f)] private float damagePerShot = 12f;
    [SerializeField, Min(0.01f)] private float burstDuration = 3f;
    [SerializeField, Min(0.01f)] private float cooldownDuration = 1f;
    [SerializeField] private Transform aimPivot;
    [SerializeField] private Transform muzzle;

    private EnemyInstance target;
    private float burstTime;
    private float cooldownTime;
    private float shotTimer;
    private bool visualsBuilt;

    /// <summary>
    /// Raised once per fired round with the muzzle world position. Purely additive:
    /// aiming, damage, cooldown and the fallback geometry do not depend on listeners.
    /// </summary>
    public event System.Action<Vector3> ShotFired;

    /// <summary>Raised once when the turret transitions from no valid target to a valid target.</summary>
    public event System.Action<EnemyInstance> TargetAcquired;

    public float DetectionRange => detectionRange;
    public float BurstDuration => burstDuration;
    public float CooldownDuration => cooldownDuration;
    public bool IsCoolingDown => cooldownTime > 0f;
    public bool TargetingFeedbackEnabled => targetingFeedbackEnabled;
    public EnemyInstance CurrentTarget => target;
    public Transform AimPivot => aimPivot;
    public Transform Muzzle => muzzle;
    public float MinPitchLimit => minPitchDegrees;
    public float MaxPitchLimit => maxPitchDegrees;

    /// <summary>
    /// Clamps any requested elevation into the sentry's fixed -30..+60 degree
    /// band. Missile turrets expose the same helper shape, so both traps agree
    /// on what "the gun cannot go there" means.
    /// </summary>
    public static float ClampPitchDegrees(float pitchDegrees)
    {
        return Mathf.Clamp(pitchDegrees, MinPitchDegrees, MaxPitchDegrees);
    }

    /// <summary>Narrows the vertical field of fire, always inside the hard -30..+60 band.</summary>
    public void ApplyPitchLimits(float minPitch, float maxPitch)
    {
        minPitchDegrees = Mathf.Clamp(minPitch, MinPitchDegrees, MaxPitchDegrees);
        maxPitchDegrees = Mathf.Clamp(maxPitch, minPitchDegrees, MaxPitchDegrees);
    }

    private void Awake()
    {
        BuildFallbackVisuals();
    }

    private void OnValidate()
    {
        detectionRange = Mathf.Max(0.1f, detectionRange);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        roundsPerSecond = Mathf.Max(0.01f, roundsPerSecond);
        damagePerShot = Mathf.Max(0f, damagePerShot);
        burstDuration = Mathf.Max(0.01f, burstDuration);
        cooldownDuration = Mathf.Max(0.01f, cooldownDuration);
        minPitchDegrees = Mathf.Clamp(minPitchDegrees, MinPitchDegrees, MaxPitchDegrees);
        maxPitchDegrees = Mathf.Clamp(maxPitchDegrees, minPitchDegrees, MaxPitchDegrees);
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances cooldown, acquisition, aiming and the burst cadence by an explicit
    /// step. <see cref="Update"/> forwards <c>Time.deltaTime</c>; EditMode acceptance
    /// and offline previews call it directly so the burst rhythm is deterministic
    /// outside the player loop. The behavior itself is unchanged.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (deltaTime < 0f) deltaTime = 0f;

        if (cooldownTime > 0f)
            cooldownTime = Mathf.Max(0f, cooldownTime - deltaTime);

        bool hadValidTarget = IsValidTarget(target);
        AcquireTarget();
        if (targetingFeedbackEnabled && !hadValidTarget && target != null)
            TargetAcquired?.Invoke(target);
        if (target == null)
        {
            // A burst is continuous only while an enemy is actually present.
            // Losing all targets pauses the weapon and starts a fresh burst on
            // the next acquisition.
            burstTime = 0f;
            shotTimer = 0f;
            return;
        }

        bool aligned = AimAtTarget(deltaTime);
        if (!aligned) return;
        if (cooldownTime > 0f) return;

        burstTime += deltaTime;
        shotTimer -= deltaTime;
        float interval = 1f / roundsPerSecond;
        while (shotTimer <= 0f && burstTime < burstDuration && target != null)
        {
            FireAtTarget(target);
            shotTimer += interval;
        }

        if (burstTime >= burstDuration)
        {
            burstTime = 0f;
            cooldownTime = cooldownDuration;
            shotTimer = 0f;
        }
    }

    private void AcquireTarget()
    {
        if (IsValidTarget(target)) return;

        target = null;
        float bestSqrDistance = detectionRange * detectionRange;
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>();
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance candidate = enemies[i];
            if (!IsValidTarget(candidate)) continue;
            Vector3 delta = candidate.transform.position - transform.position;
            if (delta.sqrMagnitude > bestSqrDistance) continue;
            bestSqrDistance = delta.sqrMagnitude;
            target = candidate;
        }
    }

    private bool IsValidTarget(EnemyInstance candidate)
    {
        if (candidate == null) return false;
        EnemyHealth health = candidate.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead) return false;
        Vector3 delta = candidate.transform.position - transform.position;
        return delta.sqrMagnitude <= detectionRange * detectionRange;
    }

    /// <summary>
    /// Elevation in degrees the barrel would need to point at
    /// <paramref name="worldPosition"/>: negative means below the pivot, positive
    /// means above it.
    /// </summary>
    public float RequiredElevationDegrees(Vector3 worldPosition)
    {
        Vector3 origin = aimPivot != null ? aimPivot.position : transform.position;
        Vector3 direction = worldPosition - origin;
        float planar = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
        if (planar < 0.0001f) return direction.y >= 0f ? 90f : -90f;
        return Mathf.Atan2(direction.y, planar) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// True when <paramref name="worldPosition"/> sits inside the vertical field
    /// of fire, i.e. the barrel can actually point at it (-30..+60 by default).
    /// </summary>
    public bool IsWithinFieldOfFire(Vector3 worldPosition)
    {
        float elevation = RequiredElevationDegrees(worldPosition);
        return elevation >= minPitchDegrees - ElevationToleranceDegrees
            && elevation <= maxPitchDegrees + ElevationToleranceDegrees;
    }

    private bool AimAtTarget(float deltaTime)
    {
        if (aimPivot == null) return true;
        Vector3 direction = target.transform.position - aimPivot.position;
        if (direction.sqrMagnitude < 0.0001f) return true;

        // Aim in full 3D, then clamp the elevation to the field of fire. The
        // desired pose always stays inside -30..+60, so the barrel physically
        // stops at the limit instead of swinging past it.
        float requestedElevation = RequiredElevationDegrees(target.transform.position);
        float elevation = Mathf.Clamp(requestedElevation, minPitchDegrees, maxPitchDegrees);
        float planarSqr = direction.x * direction.x + direction.z * direction.z;

        // A target straight above/below the pivot leaves yaw undefined; keep the
        // current heading instead of snapping to an arbitrary one.
        float yaw = planarSqr < 0.000001f
            ? aimPivot.eulerAngles.y
            : Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        Quaternion desired = Quaternion.Euler(-elevation, yaw, 0f);
        aimPivot.rotation = Quaternion.RotateTowards(aimPivot.rotation, desired, rotationSpeed * deltaTime);
        if (Quaternion.Angle(aimPivot.rotation, desired) > AimToleranceDegrees) return false;

        // Reaching the clamped pose is not enough: a target outside the vertical
        // field of fire must never be shot at from the limit. Keep tracking it
        // (in case it descends into the arc) but hold fire.
        return IsWithinFieldOfFire(target.transform.position);
    }

    private void FireAtTarget(EnemyInstance enemy)
    {
        if (enemy == null) return;
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead)
        {
            target = null;
            return;
        }
        health.TakeDamage(damagePerShot, TrapDamageType.Bullet);
        ShotFired?.Invoke(muzzle != null ? muzzle.position : transform.position);

        // A short debug tracer makes the weapon readable in a gray-box scene
        // without requiring an additional projectile asset.
        if (muzzle != null)
            Debug.DrawLine(muzzle.position, enemy.transform.position, Color.red, 0.08f);
    }

    /// <summary>Creates a small machine-gun silhouette when no art prefab is supplied.</summary>
    private void BuildFallbackVisuals()
    {
        if (visualsBuilt) return;
        GameObject art = Resources.Load<GameObject>("SentryMachineGun");
        if (art != null)
        {
            GameObject instance = Instantiate(art, transform);
            instance.name = "Sentry Machine Gun Art";
            aimPivot = FindDeepChild(instance.transform, "Aim Pivot");
            muzzle = FindDeepChild(instance.transform, "Muzzle");
            if (aimPivot != null && muzzle != null)
            { visualsBuilt = true; return; }
            DestroyImmediate(instance);
        }
        Transform existingPivot = transform.Find("Aim Pivot");
        if (existingPivot != null)
        {
            aimPivot = existingPivot;
            muzzle = existingPivot.Find("Muzzle");
            visualsBuilt = true;
            return;
        }
        visualsBuilt = true;
        if (aimPivot == null)
        {
            GameObject pivotObject = new GameObject("Aim Pivot");
            pivotObject.transform.SetParent(transform, false);
            pivotObject.transform.localPosition = new Vector3(0f, 0.26f, 0f);
            aimPivot = pivotObject.transform;
        }

        Material dark = CreateMaterial(new Color(0.08f, 0.1f, 0.12f));
        Material metal = CreateMaterial(new Color(0.28f, 0.31f, 0.34f));
        CreatePrimitive(PrimitiveType.Cylinder, "Base", transform, Vector3.zero,
            new Vector3(0.55f, 0.16f, 0.55f), dark);
        CreatePrimitive(PrimitiveType.Cylinder, "Gun Housing", aimPivot, Vector3.zero,
            new Vector3(0.34f, 0.22f, 0.34f), metal);
        GameObject barrel = CreatePrimitive(PrimitiveType.Cube, "Machine Gun Barrel", aimPivot,
            new Vector3(0f, 0f, 0.34f), new Vector3(0.1f, 0.1f, 0.7f), dark);

        if (muzzle == null)
        {
            GameObject muzzleObject = new GameObject("Muzzle");
            muzzleObject.transform.SetParent(aimPivot, false);
            muzzleObject.transform.localPosition = new Vector3(0f, 0f, 0.69f);
            muzzle = muzzleObject.transform;
        }
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent.name == childName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindDeepChild(parent.GetChild(i), childName);
            if (result != null) return result;
        }
        return null;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        obj.transform.localScale = localScale;
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
        Collider collider = obj.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        return obj;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };
        return material;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}


