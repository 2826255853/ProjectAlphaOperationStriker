using UnityEngine;

/// <summary>
/// Twin launch plumes for the dual missile launcher trap.
///
/// The plume meshes are authored in Blender (desktop 陷阱素材文件夹\DualMissileExhaust)
/// and imported as Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx. The
/// authoring helper hangs one plume stack off the mount's Pitch Pivot per rail and
/// points them back along the barrel; this component only drives them:
///
/// * while <see cref="MissileLauncherWeapon"/> reports <see cref="MissileLauncherState.Firing"/>
///   the plumes burn at full scale,
/// * they linger <see cref="lingerAfterLaunchSeconds"/> after the last round leaves the
///   tube, then fade back to <see cref="idleScale"/> (0 hides them, which is the rest look).
///
/// The effect is pure scale + renderer toggling, so it looks identical in edit mode, in
/// the batch preview renderer and at runtime, and it saves no per-frame allocation.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Missile Exhaust Fx")]
public sealed class MissileExhaustFx : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Launcher weapon that reports the firing state. Found on this object when empty.")]
    [SerializeField] private MissileLauncherWeapon weapon;
    [Tooltip("Plume roots hung off the pitch pivot, one per rail.")]
    [SerializeField] private Transform[] plumes = new Transform[0];

    [Header("Timing")]
    [Tooltip("Seconds the plume takes to reach full scale once the salvo starts.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.06f;
    [Tooltip("Seconds the plume takes to die down after the linger time elapses.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.4f;
    [Tooltip("Plumes keep burning this long after the last missile leaves the tube.")]
    [SerializeField, Min(0f)] private float lingerAfterLaunchSeconds = 0.5f;

    [Header("Look")]
    [Tooltip("Extra scale multiplier on top of the authored plume scale.")]
    [SerializeField, Min(0f)] private float plumeScale = 1f;
    [Tooltip("Sinusoidal breathing applied on top of the plume scale. 0 freezes the shape.")]
    [SerializeField, Min(0f)] private float flickerAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float flickerHz = 24f;
    [Tooltip("Plume multiplier while the launcher is idle. 0 hides the plumes completely.")]
    [SerializeField, Min(0f)] private float idleScale = 0f;

    private float intensity;
    private float lastFiringTime = float.NegativeInfinity;
    private float previewIntensity = -1f;
    private Vector3[] authoredScales = new Vector3[0];
    private Renderer[][] plumeRenderers = new Renderer[0][];
    private bool cached;

    /// <summary>Current plume strength, 0 = idle, 1 = full burn.</summary>
    public float Intensity => intensity;

    public MissileLauncherWeapon Weapon => weapon;

    public Transform[] Plumes => plumes;

    private void Awake()
    {
        CachePlumes();
        // At rest the plumes are cold: start from the idle look instead of the
        // authored (full burn) scale the prefab is saved with.
        intensity = 0f;
        if (Application.isPlaying) ApplyIntensity(idleScale, false);
    }

    private void OnValidate()
    {
        fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
        fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
        lingerAfterLaunchSeconds = Mathf.Max(0f, lingerAfterLaunchSeconds);
        plumeScale = Mathf.Max(0f, plumeScale);
        flickerAmplitude = Mathf.Max(0f, flickerAmplitude);
        flickerHz = Mathf.Max(0f, flickerHz);
        idleScale = Mathf.Max(0f, idleScale);
        cached = false;
    }

    private void Update()
    {
        if (!cached) CachePlumes();

        if (previewIntensity >= 0f)
        {
            intensity = previewIntensity;
            ApplyIntensity(previewIntensity, false);
            return;
        }

        float target = TargetIntensity();
        float rate = target > intensity ? fadeInSeconds : fadeOutSeconds;
        intensity = rate <= 0f ? target : Mathf.MoveTowards(intensity, target, Time.deltaTime / rate);
        ApplyIntensity(intensity, true);
    }

    /// <summary>
    /// Drives the plumes from the weapon state: full burn during a salvo, held for
    /// <see cref="lingerAfterLaunchSeconds"/> afterwards, idle otherwise.
    /// </summary>
    private float TargetIntensity()
    {
        if (weapon != null && weapon.State == MissileLauncherState.Firing)
        {
            lastFiringTime = Time.time;
            return 1f;
        }

        return Time.time - lastFiringTime <= lingerAfterLaunchSeconds ? 1f : idleScale;
    }

    /// <summary>
    /// Editor/batch preview hook: forces a plume strength and applies it immediately
    /// (Update does not run in edit mode). Pass a negative value to hand control back
    /// to the weapon state.
    /// </summary>
    public void SetPreviewIntensity(float value)
    {
        if (!cached) CachePlumes();
        previewIntensity = value;
        if (value < 0f) return;
        intensity = Mathf.Max(0f, value);
        ApplyIntensity(value, false);
    }

    /// <summary>Replaces the wired plumes; used by the authoring helper and by tests.</summary>
    public void SetPlumes(Transform[] newPlumes)
    {
        plumes = newPlumes ?? new Transform[0];
        cached = false;
        CachePlumes();
    }

    /// <summary>Replaces the weapon that drives the plumes.</summary>
    public void SetWeapon(MissileLauncherWeapon newWeapon)
    {
        weapon = newWeapon;
    }

    private void CachePlumes()
    {
        cached = true;
        if (weapon == null) weapon = GetComponent<MissileLauncherWeapon>();

        if (plumes == null) plumes = new Transform[0];
        if (authoredScales.Length != plumes.Length) authoredScales = new Vector3[plumes.Length];
        if (plumeRenderers.Length != plumes.Length) plumeRenderers = new Renderer[plumes.Length][];

        for (int i = 0; i < plumes.Length; i++)
        {
            Transform plume = plumes[i];
            if (plume == null)
            {
                authoredScales[i] = Vector3.one;
                plumeRenderers[i] = new Renderer[0];
                continue;
            }

            if (authoredScales[i] == Vector3.zero) authoredScales[i] = plume.localScale;
            plumeRenderers[i] = plume.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ApplyIntensity(float value, bool flicker)
    {
        if (!cached) CachePlumes();
        float clamped = Mathf.Max(0f, value);
        for (int i = 0; i < plumes.Length; i++)
        {
            Transform plume = plumes[i];
            if (plume == null) continue;

            float factor = plumeScale * clamped;
            if (flicker && factor > 0f && flickerAmplitude > 0f && flickerHz > 0f)
                factor *= 1f + flickerAmplitude * Mathf.Sin(Time.time * flickerHz * Mathf.PI * 2f + i * 1.7f);

            plume.localScale = authoredScales[i] * factor;

            Renderer[] renderers = plumeRenderers[i];
            bool visible = factor > 0.001f;
            for (int r = 0; r < renderers.Length; r++)
                if (renderers[r] != null) renderers[r].enabled = visible;
        }
    }
}

