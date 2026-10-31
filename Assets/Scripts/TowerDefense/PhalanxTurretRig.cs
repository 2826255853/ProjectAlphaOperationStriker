using UnityEngine;

/// <summary>
/// Motion scaffolding for the Phalanx CIWS (密集阵) trap model.
///
/// This component only turns the authored rig: traverse (yaw), elevation
/// (pitch) and the six-barrel rotary cluster. It deliberately contains NO
/// target selection, burst timing, ammo, damage or upgrade logic — those are
/// the follow-up gameplay tasks described in Assets/Editor/PHALANX_HANDOFF.md.
///
/// The yaw -> pitch -> barrel -> muzzle hierarchy is wired by
/// PhalanxTrapAuthoring so a gameplay component can drive the same rig through
/// AimAt / SetBarrelSpinActive without touching the model.
/// </summary>
[DisallowMultipleComponent]
public sealed class PhalanxTurretRig : MonoBehaviour
{
    [Header("Rig (wired by PhalanxTrapAuthoring)")]
    [SerializeField] private Transform yawPivot;
    [SerializeField] private Transform pitchPivot;
    [SerializeField] private Transform barrelCluster;
    [SerializeField] private Transform muzzle;

    [Header("Traverse")]
    [SerializeField, Min(0f)] private float yawSpeedDegreesPerSecond = 240f;
    [SerializeField, Min(0f)] private float pitchSpeedDegreesPerSecond = 180f;
    [SerializeField] private float minPitchDegrees = -8f;
    [SerializeField] private float maxPitchDegrees = 85f;

    [Header("Barrel cluster")]
    [SerializeField, Min(0f)] private float barrelSpinDegreesPerSecond = 1500f;
    [SerializeField, Min(0.01f)] private float barrelSpinBlendSeconds = 0.25f;

    private Quaternion yawRest = Quaternion.identity;
    private Quaternion pitchRest = Quaternion.identity;
    private Quaternion barrelRest = Quaternion.identity;
    private Vector3 barrelAxisLocal = Vector3.forward;
    // The imported model node keeps Blender's axis frame (Base_Root carries an
    // X=-90 node rotation), so parent-local Vector3.up/right are NOT the world
    // up/right. Both axes are resolved once from the world axes at rest, the
    // same lesson as the XM777 yaw fix — never hard-code Vector3.up here.
    private Vector3 yawAxisLocal = Vector3.up;
    private Vector3 pitchAxisLocal = Vector3.right;
    private bool restCaptured;
    private bool spinActive;
    private float currentSpinRate;
    private float barrelAngle;

    public Transform YawPivot => yawPivot;
    public Transform PitchPivot => pitchPivot;
    public Transform BarrelCluster => barrelCluster;
    public Transform Muzzle => muzzle;

    /// <summary>Current traverse angle in degrees, measured from the rest pose.</summary>
    public float YawDegrees { get; private set; }

    /// <summary>Current elevation angle in degrees above the rest pose.</summary>
    public float PitchDegrees { get; private set; }

    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

    /// <summary>Barrel axis in world space, derived from the rig (never hard-coded).</summary>
    public Vector3 BarrelDirection =>
        muzzle != null && barrelCluster != null
            ? (muzzle.position - barrelCluster.position).normalized
            : transform.forward;

    public bool BarrelsSpinning => spinActive || currentSpinRate > 1f;

    private void Awake()
    {
        EnsureRestPose();
    }

    private void Update()
    {
        AdvanceVisuals(Time.deltaTime);
    }

    /// <summary>Captures the authored rest pose once; safe to call from tools and tests.</summary>
    public void EnsureRestPose()
    {
        if (restCaptured || yawPivot == null || pitchPivot == null) return;
        yawRest = yawPivot.localRotation;
        pitchRest = pitchPivot.localRotation;
        barrelRest = barrelCluster != null ? barrelCluster.localRotation : Quaternion.identity;
        if (barrelCluster != null && muzzle != null)
        {
            Vector3 axis = barrelCluster.InverseTransformDirection(
                muzzle.position - barrelCluster.position);
            if (axis.sqrMagnitude > 0.0001f) barrelAxisLocal = axis.normalized;
            else barrelAxisLocal = Vector3.forward;
        }
        // World up expressed in the yaw pivot's parent space (the imported
        // model node), and the yaw-frame right axis for elevation.
        if (yawPivot.parent != null)
            yawAxisLocal = Quaternion.Inverse(yawPivot.parent.rotation) * Vector3.up;
        if (pitchPivot.parent != null)
            pitchAxisLocal = Quaternion.Inverse(pitchPivot.parent.rotation) * Vector3.right;
        restCaptured = true;
    }

    /// <summary>
    /// Turns the turret toward a world-space point. Stops inside the authored
    /// pitch limits instead of snapping, so gameplay can gate firing on IsAimedAt.
    /// </summary>
    public void AimAt(Vector3 worldTarget, float deltaTime)
    {
        EnsureRestPose();
        if (yawPivot == null || pitchPivot == null) return;

        Vector3 pivot = yawPivot.position;
        Vector3 flat = worldTarget - pivot;
        flat.y = 0f;

        float desiredYaw = YawDegrees;
        if (flat.sqrMagnitude > 0.0001f)
        {
            Vector3 restForward = yawRest * Vector3.forward;
            desiredYaw = Vector3.SignedAngle(restForward, flat.normalized, Vector3.up);
        }
        YawDegrees = Mathf.MoveTowardsAngle(YawDegrees, desiredYaw,
            Mathf.Max(0f, yawSpeedDegreesPerSecond) * Mathf.Max(0f, deltaTime));
        yawPivot.localRotation = Quaternion.AngleAxis(YawDegrees, yawAxisLocal) * yawRest;

        Vector3 toTarget = worldTarget - pitchPivot.position;
        float desiredPitch = PitchDegrees;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Vector3 horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
            float horizontalDistance = horizontal.magnitude;
            if (horizontalDistance > 0.0001f)
                desiredPitch = Mathf.Atan2(toTarget.y, horizontalDistance) * Mathf.Rad2Deg;
        }
        desiredPitch = Mathf.Clamp(desiredPitch, minPitchDegrees, maxPitchDegrees);
        PitchDegrees = Mathf.MoveTowards(PitchDegrees, desiredPitch,
            Mathf.Max(0f, pitchSpeedDegreesPerSecond) * Mathf.Max(0f, deltaTime));
        pitchPivot.localRotation = Quaternion.AngleAxis(-PitchDegrees, pitchAxisLocal) * pitchRest;
    }

    /// <summary>True when the rig already points close enough to fire.</summary>
    public bool IsAimedAt(Vector3 worldTarget, float toleranceDegrees)
    {
        if (yawPivot == null || pitchPivot == null) return false;
        Vector3 toTarget = worldTarget - pitchPivot.position;
        if (toTarget.sqrMagnitude < 0.0001f) return true;
        float angle = Vector3.Angle(BarrelDirection, toTarget.normalized);
        return angle <= Mathf.Max(0f, toleranceDegrees);
    }

    /// <summary>Starts or stops the rotary spin; the transition is smoothed.</summary>
    public void SetBarrelSpinActive(bool active)
    {
        spinActive = active;
    }

    /// <summary>Immediately returns the rig to its authored rest pose.</summary>
    public void ResetToRestPose()
    {
        EnsureRestPose();
        if (yawPivot != null) yawPivot.localRotation = yawRest;
        if (pitchPivot != null) pitchPivot.localRotation = pitchRest;
        if (barrelCluster != null) barrelCluster.localRotation = barrelRest;
        YawDegrees = 0f;
        PitchDegrees = 0f;
        barrelAngle = 0f;
        currentSpinRate = 0f;
        spinActive = false;
    }

    /// <summary>Advances the decorative spin animation; gameplay never needs this.</summary>
    public void AdvanceVisuals(float deltaTime)
    {
        EnsureRestPose();
        if (barrelCluster == null || deltaTime <= 0f) return;

        float target = spinActive ? Mathf.Max(0f, barrelSpinDegreesPerSecond) : 0f;
        float blend = Mathf.Max(0.01f, barrelSpinBlendSeconds);
        currentSpinRate = Mathf.MoveTowards(currentSpinRate, target,
            (Mathf.Max(1f, barrelSpinDegreesPerSecond) / blend) * deltaTime);
        barrelAngle = Mathf.Repeat(barrelAngle + currentSpinRate * deltaTime, 360f);
        barrelCluster.localRotation = Quaternion.AngleAxis(barrelAngle, barrelAxisLocal) * barrelRest;
    }

    /// <summary>Current effective spin rate in degrees/second (read-only, for tools/tests).</summary>
    public float CurrentSpinRate => currentSpinRate;
}
