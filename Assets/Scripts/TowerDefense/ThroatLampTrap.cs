using UnityEngine;

public enum ThroatLampState { Idle, Charging, Discharging, CoolingDown }

/// <summary>Ceiling plasma trap. Gameplay owns aiming; the Animator owns light and recoil.</summary>
[DisallowMultipleComponent]
public sealed class ThroatLampTrap : MonoBehaviour
{
    [SerializeField] private Transform yawPivot;
    [SerializeField] private Transform pitchPivot;
    [SerializeField] private Transform nozzle;
    [SerializeField] private Animator animator;
    [SerializeField] private PlasmaProjectile projectilePrefab;
    [SerializeField, Min(0.1f)] private float detectionRange = 18f;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 20f;
    [SerializeField, Min(0.1f)] private float fireInterval = 4f;
    [SerializeField, Min(0f)] private float chargeSeconds = 1.4f;
    [SerializeField, Min(0f)] private float directDamage = 80f;
    [SerializeField, Min(0f)] private float splashDamage = 40f;
    [SerializeField, Min(0f)] private float splashRadius = 2f;
    [SerializeField, Min(1f)] private float aimSpeedDegrees = 240f;
    [SerializeField, Range(0.1f, 10f)] private float aimToleranceDegrees = 2f;
    private MissileVelocityTracker velocityTracker;
    private float chargeElapsed;
    private float nextShotTime;
    private float dischargeUntil;
    private float clock;
    public EnemyInstance CurrentTarget { get; private set; }
    public Vector3 InterceptPoint { get; private set; }
    public ThroatLampState State { get; private set; }
    public int ShotCount { get; private set; }
    public bool HasInterceptPoint { get; private set; }
    public bool HasBoundRig => yawPivot != null && pitchPivot != null && nozzle != null && animator != null && projectilePrefab != null;
    public float FireInterval => fireInterval;
    public float ProjectileSpeed => projectileSpeed;
    public float DetectionRange => detectionRange;
    public Transform Nozzle => nozzle;

    private void Awake()
    {
        if (yawPivot == null) yawPivot = transform.Find("Yaw Pivot");
        if (pitchPivot == null && yawPivot != null) pitchPivot = yawPivot.Find("Pitch Pivot");
        if (nozzle == null && pitchPivot != null) nozzle = pitchPivot.Find("Nozzle");
        if (animator == null) animator = GetComponent<Animator>();
        clock = Time.time;
    }

    // Run after Animator evaluation; both the alignment gate and the emitted shot
    // observe the final barrel pose, never the previous frame's animation pose.
    private void LateUpdate() => Advance(Time.deltaTime);

    public void Advance(float deltaTime)
    {
        if (!isActiveAndEnabled || !HasBoundRig || deltaTime <= 0f
            || GetComponentInParent<TrapPlacementPreview>() != null) return;
        clock += deltaTime;
        if (!CanTarget(CurrentTarget))
        {
            CancelCharge();
            CurrentTarget = null;
            float nearest = float.PositiveInfinity;
            foreach (EnemyInstance candidate in FindObjectsByType<EnemyInstance>(FindObjectsSortMode.None))
            {
                if (!CanTarget(candidate)) continue;
                float distance = (PlasmaProjectile.AimPosition(candidate) - nozzle.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                CurrentTarget = candidate;
            }
            velocityTracker.Reset();
        }

        HasInterceptPoint = false;
        if (CurrentTarget == null) { State = ThroatLampState.Idle; return; }
        Vector3 position = PlasmaProjectile.AimPosition(CurrentTarget);
        Vector3 velocity = velocityTracker.Sample(position, clock, 0.12f);
        HasInterceptPoint = MissileAimSolver.TrySolveIntercept(nozzle.position, position, velocity,
            projectileSpeed, out Vector3 point, out _, detectionRange / projectileSpeed * 2f);
        HasInterceptPoint &= (point - nozzle.position).sqrMagnitude <= detectionRange * detectionRange;
        InterceptPoint = point;
        if (!HasInterceptPoint) { CancelCharge(); return; }

        // +Z is the mounting normal. Rotate the yoke about Z, then tilt the head
        // about X. Restore our own pose because the Animator also writes pivots.
        AimAt(point, deltaTime);
        bool aligned = Vector3.Angle(nozzle.forward, point - nozzle.position) <= aimToleranceDegrees;
        bool clear = PlasmaProjectile.HasClearPath(nozzle.position, point, transform);
        if (clock < dischargeUntil) { State = ThroatLampState.Discharging; return; }
        if (clock < nextShotTime - chargeSeconds) { State = ThroatLampState.CoolingDown; return; }
        if (!aligned || !clear) { CancelCharge(); return; }

        if (State != ThroatLampState.Charging)
        {
            chargeElapsed = 0f;
            State = ThroatLampState.Charging;
            animator.ResetTrigger("Fire");
            animator.SetTrigger("Charge");
        }
        chargeElapsed += deltaTime;
        if (chargeElapsed + 0.0001f < chargeSeconds || clock + 0.0001f < nextShotTime) return;

        PlasmaProjectile shot = Instantiate(projectilePrefab, nozzle.position, nozzle.rotation);
        // Charging keeps its progress; only the emitted projectile samples the current level.
        TrapInstance trap = GetComponent<TrapInstance>();
        float shotDirect = trap != null ? trap.GetEffectiveDamage(directDamage) : directDamage;
        float shotSplash = trap != null ? trap.GetEffectiveDamage(splashDamage) : splashDamage;
        shot.Initialize(nozzle.forward, projectileSpeed, detectionRange, shotDirect, shotSplash, splashRadius, transform);
        ShotCount++;
        nextShotTime = clock + Mathf.Max(fireInterval, chargeSeconds);
        dischargeUntil = clock + 0.9f;
        State = ThroatLampState.Discharging;
        chargeElapsed = 0f;
        animator.ResetTrigger("Charge");
        animator.SetTrigger("Fire");
    }

    private Quaternion aimYaw = Quaternion.identity;
    private Quaternion aimPitch = Quaternion.identity;

    /// <summary>Applies the same two-axis pose for runtime and authoring previews.</summary>
    public void AimAt(Vector3 point, float deltaTime)
    {
        Vector3 direction = transform.InverseTransformDirection(point - pitchPivot.position);
        float tilt = Mathf.Atan2(new Vector2(direction.x, direction.y).magnitude, direction.z) * Mathf.Rad2Deg;
        float azimuth = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
        aimYaw = Quaternion.RotateTowards(aimYaw, Quaternion.AngleAxis(azimuth, Vector3.forward), aimSpeedDegrees * deltaTime);
        aimPitch = Quaternion.RotateTowards(aimPitch, Quaternion.AngleAxis(Mathf.Clamp(tilt, 0f, 85f), Vector3.right), aimSpeedDegrees * deltaTime);
        yawPivot.localRotation = aimYaw;
        pitchPivot.localRotation = aimPitch;
    }

    private bool CanTarget(EnemyInstance enemy)
    {
        if (!PlasmaProjectile.IsEligible(enemy)) return false;
        Vector3 point = PlasmaProjectile.AimPosition(enemy);
        Vector3 local = transform.InverseTransformDirection(point - transform.position);
        return local.z > 0f && Vector3.Angle(Vector3.forward, local) <= 85f
            && (point - nozzle.position).sqrMagnitude <= detectionRange * detectionRange
            && PlasmaProjectile.HasClearPath(nozzle.position, point, transform);
    }

    private void CancelCharge()
    {
        chargeElapsed = 0f;
        if (State == ThroatLampState.Charging && animator != null)
        {
            animator.ResetTrigger("Charge");
            animator.ResetTrigger("Fire");
            animator.Play("Idle", 0, 0f);
        }
        State = ThroatLampState.Idle;
    }

    private void OnDisable()
    {
        CancelCharge();
        CurrentTarget = null;
        HasInterceptPoint = false;
        velocityTracker.Reset();
    }
}
