using UnityEngine;

/// <summary>
/// Runtime weapon used by the sentry's Phalanx four-level branch.
/// It keeps the authored Phalanx rig for aiming and visual barrel spin while
/// sharing the owning TrapInstance with the original sentry.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Phalanx Auto Cannon")]
public sealed class PhalanxAutoCannon : MonoBehaviour
{
    public const float DefaultDetectionRange = 50f;
    public const float DefaultRoundsPerSecond = 50f;
    public const float DefaultDamagePerShot = 0.5f;

    [SerializeField, Min(0.1f)] private float detectionRange = DefaultDetectionRange;
    [SerializeField, Min(0.01f)] private float roundsPerSecond = DefaultRoundsPerSecond;
    [SerializeField, Min(0f)] private float damagePerShot = DefaultDamagePerShot;
    [SerializeField, Min(0f)] private float aimToleranceDegrees = 5f;
    [SerializeField] private PhalanxTurretRig rig;

    private EnemyInstance target;
    private TrapInstance trap;
    private float shotAccumulator;

    public float DetectionRange => detectionRange;
    public float RoundsPerSecond => roundsPerSecond;
    public float BaseDamagePerShot => damagePerShot;
    public EnemyInstance CurrentTarget => target;
    public PhalanxTurretRig Rig => rig;

    /// <summary>Raised once per round with the muzzle world position (flash / casing feed).</summary>
    public event System.Action<Vector3> ShotFired;

    /// <summary>
    /// Raised once per round with the muzzle world position and the point the round was
    /// aimed at. Tracer effects need both ends, so they listen here instead of ShotFired.
    /// </summary>
    public event System.Action<Vector3, Vector3> ShotTraced;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        detectionRange = Mathf.Max(0.1f, detectionRange);
        roundsPerSecond = Mathf.Max(0.01f, roundsPerSecond);
        damagePerShot = Mathf.Max(0f, damagePerShot);
        aimToleranceDegrees = Mathf.Max(0f, aimToleranceDegrees);
    }

    private void OnEnable()
    {
        ResolveReferences();
        rig?.SetBarrelSpinActive(true);
    }

    private void OnDisable()
    {
        target = null;
        shotAccumulator = 0f;
        rig?.SetBarrelSpinActive(false);
    }

    private void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (deltaTime < 0f) deltaTime = 0f;
        ResolveReferences();
        AcquireTarget();
        if (target == null)
        {
            shotAccumulator = 0f;
            rig?.SetBarrelSpinActive(false);
            return;
        }

        bool aimed = true;
        if (rig != null)
        {
            rig.AimAt(target.transform.position, deltaTime);
            aimed = rig.IsAimedAt(target.transform.position, aimToleranceDegrees);
            rig.SetBarrelSpinActive(aimed);
        }
        if (!aimed) return;

        shotAccumulator += deltaTime * roundsPerSecond;
        int safety = 0;
        while (shotAccumulator >= 1f && target != null && safety++ < 256)
        {
            shotAccumulator -= 1f;
            FireAtTarget(target);
        }
    }

    public void AcquireTarget()
    {
        // Re-evaluate every tick so a newly entering aircraft immediately takes
        // priority over the ground target currently under fire.
        EnemyInstance[] enemies = FindObjectsByType<EnemyInstance>();
        EnemyInstance bestAir = null;
        EnemyInstance bestGround = null;
        float bestAirSqr = detectionRange * detectionRange;
        float bestGroundSqr = detectionRange * detectionRange;
        Vector3 origin = transform.position;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyInstance candidate = enemies[i];
            if (!IsValidTarget(candidate)) continue;
            float sqrDistance = (candidate.transform.position - origin).sqrMagnitude;
            if (MissileLauncherTurret.IsAirTarget(candidate))
            {
                if (sqrDistance <= bestAirSqr)
                {
                    bestAirSqr = sqrDistance;
                    bestAir = candidate;
                }
            }
            else if (sqrDistance <= bestGroundSqr)
            {
                bestGroundSqr = sqrDistance;
                bestGround = candidate;
            }
        }

        target = bestAir != null ? bestAir : bestGround;
    }

    public bool IsValidTarget(EnemyInstance candidate)
    {
        if (candidate == null) return false;
        EnemyHealth health = candidate.GetComponent<EnemyHealth>();
        if (health == null || health.IsDead) return false;
        Vector3 delta = candidate.transform.position - transform.position;
        return delta.sqrMagnitude <= detectionRange * detectionRange;
    }

    private void FireAtTarget(EnemyInstance enemy)
    {
        if (!IsValidTarget(enemy))
        {
            target = null;
            return;
        }

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        float effectiveDamage = trap != null
            ? trap.GetEffectiveDamage(damagePerShot)
            : damagePerShot;
        health.TakeDamage(effectiveDamage, TrapDamageType.Bullet);
        Vector3 muzzle = rig != null ? rig.MuzzlePosition : transform.position;
        Vector3 aimPoint = enemy.transform.position;
        ShotFired?.Invoke(muzzle);
        ShotTraced?.Invoke(muzzle, aimPoint);
        Debug.DrawLine(muzzle, aimPoint, Color.yellow, 0.04f);
    }

    private void ResolveReferences()
    {
        if (rig == null) rig = GetComponentInChildren<PhalanxTurretRig>(true);
        if (trap == null) trap = GetComponent<TrapInstance>();
    }
}
