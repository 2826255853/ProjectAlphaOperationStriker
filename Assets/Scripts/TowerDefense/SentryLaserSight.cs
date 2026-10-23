using UnityEngine;

/// <summary>
/// Two-point LineRenderer laser under the <c>Laser Emitter</c> node. The beam is
/// truncated by a raycast (triggers ignored) instead of drawing an infinite line,
/// and it only exists while the owning turret is actually enabled - placement
/// ghost copies never light up.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Laser Sight")]
public sealed class SentryLaserSight : MonoBehaviour
{
    public const float TargetingBrightDuration = 0.1f;
    public const float TargetingDarkDuration = 0.1f;
    public const float TargetingPulseDuration = 0.3f;

    [Header("Rig")]
    [SerializeField] private Transform laserEmitter;
    [SerializeField] private Material beamMaterial;

    [Header("Beam")]
    [SerializeField, Min(0.1f)] private float maxDistance = 8f;
    [SerializeField, Min(0.001f)] private float beamWidth = 0.012f;
    [SerializeField] private Color beamStartColor = new Color(1f, 0.22f, 0.13f, 0.85f);
    [SerializeField] private Color beamEndColor = new Color(1f, 0.22f, 0.13f, 0f);

    private LineRenderer line;
    private AutoSentryTurret turret;
    private float currentLength;
    private float targetingPulseElapsed = -1f;

    public Transform LaserEmitter => laserEmitter;
    public Material BeamMaterial => beamMaterial;
    public float MaxDistance => maxDistance;
    public bool IsBeamVisible => line != null && line.enabled;
    public float CurrentBeamLength => currentLength;
    public bool IsTargetingPulseActive => targetingPulseElapsed >= 0f;
    public float TargetingPulseElapsed => Mathf.Max(0f, targetingPulseElapsed);

    private void OnEnable()
    {
        // The LineRenderer is runtime-only. Building it in the editor would leak a
        // "Laser Beam" child into the generated prefab, so edit-mode tests call
        // EnsureBeam / UpdateBeam explicitly instead.
        if (Application.isPlaying) EnsureBeam();
        turret = GetComponentInParent<AutoSentryTurret>();
        Subscribe();
        if (Application.isPlaying) UpdateBeam();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        TickTargetingPulse(Time.deltaTime);
        UpdateBeam();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (line != null) line.enabled = false;
    }

    /// <summary>Starts the fixed 0.1 s bright / 0.1 s dark / 0.1 s bright sequence.</summary>
    public void BeginTargetingPulse()
    {
        if (turret == null) turret = GetComponentInParent<AutoSentryTurret>();
        if (turret != null && !turret.TargetingFeedbackEnabled) return;
        targetingPulseElapsed = 0f;
        UpdateBeam();
    }

    /// <summary>Advances the targeting pulse deterministically for tests and previews.</summary>
    public void TickTargetingPulse(float deltaTime)
    {
        if (targetingPulseElapsed < 0f) return;
        if (deltaTime < 0f) deltaTime = 0f;
        targetingPulseElapsed += deltaTime;
        if (targetingPulseElapsed >= TargetingPulseDuration)
            targetingPulseElapsed = -1f;
    }

    /// <summary>Lazily builds the runtime-only LineRenderer (there is no serialized line in the prefab).</summary>
    public void EnsureBeam()
    {
        if (line != null) return;

        var lineObject = new GameObject("Laser Beam");
        lineObject.transform.SetParent(transform, false);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = beamWidth;
        line.numCapVertices = 2;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.textureMode = LineTextureMode.Stretch;
        if (beamMaterial != null) line.sharedMaterial = beamMaterial;
        line.startColor = beamStartColor;
        line.endColor = beamEndColor;

        // A runtime laser must not cost physics queries per frame.
        line.startWidth = beamWidth;
        line.endWidth = beamWidth * 0.35f;
        line.enabled = false;
    }

    /// <summary>Recomputes the beam endpoints; hidden unless the turret is live.</summary>
    public void UpdateBeam()
    {
        EnsureBeam();
        if (line == null) return;

        // Resolve lazily as well: the art root can be instantiated before the turret
        // component finishes enabling, and EditMode acceptance calls this directly
        // without running OnEnable.
        if (turret == null) turret = GetComponentInParent<AutoSentryTurret>();

        if (!enabled || laserEmitter == null || turret == null || !turret.isActiveAndEnabled)
        {
            line.enabled = false;
            currentLength = 0f;
            return;
        }

        Vector3 origin = laserEmitter.position;
        Vector3 direction = laserEmitter.forward;
        Vector3 end = origin + direction * maxDistance;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, ~0,
                QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
        }

        currentLength = Vector3.Distance(origin, end);
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        line.enabled = ShouldShowBeam();
    }

    private bool ShouldShowBeam()
    {
        if (targetingPulseElapsed < 0f) return true;
        return targetingPulseElapsed < TargetingBrightDuration
            || targetingPulseElapsed >= TargetingBrightDuration + TargetingDarkDuration;
    }

    private void Subscribe()
    {
        if (turret == null) return;
        turret.TargetAcquired -= HandleTargetAcquired;
        turret.TargetAcquired += HandleTargetAcquired;
    }

    private void Unsubscribe()
    {
        if (turret == null) return;
        turret.TargetAcquired -= HandleTargetAcquired;
    }

    private void HandleTargetAcquired(EnemyInstance enemy)
    {
        BeginTargetingPulse();
    }
}
