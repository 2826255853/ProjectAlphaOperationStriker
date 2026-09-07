using UnityEngine;

/// <summary>
/// Automatic sentry machine gun trap. It acquires the nearest live enemy in
/// range, turns its gun toward that enemy and applies hitscan damage. Ammo is
/// unlimited; the only firing limitation is a three-second burst followed by
/// a one-second cooldown.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[AddComponentMenu("Tower Defense/Auto Sentry Turret")]
public sealed class AutoSentryTurret : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float detectionRange = 5f;
    [SerializeField, Min(0f)] private float rotationSpeed = 360f;

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

    public float DetectionRange => detectionRange;
    public float BurstDuration => burstDuration;
    public float CooldownDuration => cooldownDuration;
    public bool IsCoolingDown => cooldownTime > 0f;
    public EnemyInstance CurrentTarget => target;

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
    }

    private void Update()
    {
        if (cooldownTime > 0f)
            cooldownTime = Mathf.Max(0f, cooldownTime - Time.deltaTime);

        AcquireTarget();
        if (target == null)
        {
            // A burst is continuous only while an enemy is actually present.
            // Losing all targets pauses the weapon and starts a fresh burst on
            // the next acquisition.
            burstTime = 0f;
            shotTimer = 0f;
            return;
        }

        bool aligned = AimAtTarget(Time.deltaTime);
        if (!aligned) return;
        if (cooldownTime > 0f) return;

        burstTime += Time.deltaTime;
        shotTimer -= Time.deltaTime;
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
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None);
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

    private bool AimAtTarget(float deltaTime)
    {
        if (aimPivot == null) return true;
        Vector3 direction = target.transform.position - aimPivot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return true;
        Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
        aimPivot.rotation = Quaternion.RotateTowards(aimPivot.rotation, desired, rotationSpeed * deltaTime);
        return Quaternion.Angle(aimPivot.rotation, desired) <= 5f;
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
        health.TakeDamage(damagePerShot);

        // A short debug tracer makes the weapon readable in a gray-box scene
        // without requiring an additional projectile asset.
        if (muzzle != null)
            Debug.DrawLine(muzzle.position, enemy.transform.position, Color.red, 0.08f);
    }

    /// <summary>Creates a small machine-gun silhouette when no art prefab is supplied.</summary>
    private void BuildFallbackVisuals()
    {
        if (visualsBuilt) return;
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
