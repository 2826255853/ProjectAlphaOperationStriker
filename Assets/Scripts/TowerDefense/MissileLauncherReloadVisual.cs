using UnityEngine;

/// <summary>
/// Visual half of the dual missile launcher's reload: each tube owns one loaded
/// missile mesh, and that mesh disappears the moment the tube fires. The tube
/// stays empty (a bare rail) until the launcher finishes reloading, then the
/// missile pops back in.
///
/// It only toggles <see cref="Renderer.enabled"/> on the missile meshes, never the
/// transforms: the tubes double as the muzzle points used by
/// <see cref="MissileLauncherWeapon"/> and by the exhaust mount, so their
/// positions must keep working while the round is away.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Missile Launcher Reload Visual")]
public sealed class MissileLauncherReloadVisual : MonoBehaviour
{
    [Tooltip("One entry per tube, in firing order. Empty slots resolve from tubeVisualNames at runtime.")]
    [SerializeField] private Transform[] tubeVisuals = new Transform[0];

    [Tooltip("Fallback node names used when a tube slot is empty (the imported model names its missiles Missile_01 / Missile_02).")]
    [SerializeField] private string[] tubeVisualNames = { "Missile_01", "Missile_02" };

    [Tooltip("True: the round vanishes as soon as the tube fires. False: keep it until the reload starts.")]
    [SerializeField] private bool hideOnLaunch = true;

    private Renderer[][] tubeRenderers = new Renderer[0][];
    private bool[] tubeLoaded = new bool[0];
    private bool resolved;

    /// <summary>Number of tubes this component can drive.</summary>
    public int TubeCount
    {
        get
        {
            EnsureResolved();
            return tubeLoaded.Length;
        }
    }

    /// <summary>True when the round at this tube is currently mounted on the rail.</summary>
    public bool IsTubeLoaded(int tubeIndex)
    {
        EnsureResolved();
        if (tubeIndex < 0 || tubeIndex >= tubeLoaded.Length) return false;
        return tubeLoaded[tubeIndex];
    }

    /// <summary>True when at least one tube is visibly empty.</summary>
    public bool HasEmptyTube
    {
        get
        {
            EnsureResolved();
            for (int i = 0; i < tubeLoaded.Length; i++)
                if (!tubeLoaded[i]) return true;
            return false;
        }
    }

    /// <summary>The missile node for a tube, or null when the tube is not wired.</summary>
    public Transform GetTube(int tubeIndex)
    {
        EnsureResolved();
        if (tubeIndex < 0 || tubeIndex >= tubeVisuals.Length) return null;
        return tubeVisuals[tubeIndex];
    }

    /// <summary>True when the round is removed the instant the tube fires.</summary>
    public bool HideOnLaunch => hideOnLaunch;

    /// <summary>
    /// Wires the tubes explicitly (generator / tests). A null slot keeps whatever
    /// the component already resolved by name.
    /// </summary>
    public void SetTubes(params Transform[] tubes)
    {
        if (tubes == null) return;
        if (tubeVisuals == null || tubeVisuals.Length < tubes.Length)
        {
            var grown = new Transform[tubes.Length];
            if (tubeVisuals != null)
                for (int i = 0; i < tubeVisuals.Length && i < grown.Length; i++) grown[i] = tubeVisuals[i];
            tubeVisuals = grown;
        }
        for (int i = 0; i < tubes.Length; i++)
            if (tubes[i] != null) tubeVisuals[i] = tubes[i];
        resolved = false;
    }

    /// <summary>Removes or restores the round on one tube. Returns true when the state changed.</summary>
    public bool SetTubeLoaded(int tubeIndex, bool loaded)
    {
        EnsureResolved();
        if (tubeIndex < 0 || tubeIndex >= tubeLoaded.Length) return false;
        if (tubeLoaded[tubeIndex] == loaded) return false;
        tubeLoaded[tubeIndex] = loaded;
        ApplyTube(tubeIndex);
        return true;
    }

    /// <summary>Removes the round from this tube; used right after a launch.</summary>
    public bool ClearTube(int tubeIndex)
    {
        return SetTubeLoaded(tubeIndex, false);
    }

    /// <summary>Restores every round: the launcher is fully loaded again.</summary>
    public void SetAllLoaded(bool loaded)
    {
        EnsureResolved();
        for (int i = 0; i < tubeLoaded.Length; i++) tubeLoaded[i] = loaded;
        for (int i = 0; i < tubeLoaded.Length; i++) ApplyTube(i);
    }

    /// <summary>Restores every round (reload finished, trap re-enabled, trap placed fresh).</summary>
    public void RestoreAll()
    {
        SetAllLoaded(true);
    }

    private void Awake()
    {
        EnsureResolved();
        ApplyAll();
    }

    private void OnEnable()
    {
        // A pooled / re-enabled trap always comes back with a full rack; the weapon
        // re-asserts this when it enables too, so order between the two is harmless.
        if (resolved) ApplyAll();
    }

    /// <summary>Resolves the tubes (explicit slots first, then fallback names) once.</summary>
    public void EnsureResolved()
    {
        if (resolved) return;

        if (tubeVisuals == null) tubeVisuals = new Transform[0];
        if (tubeVisualNames == null) tubeVisualNames = new string[0];

        int count = Mathf.Max(tubeVisuals.Length, tubeVisualNames.Length);
        if (count <= 0) count = 0;

        var tubes = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            if (i < tubeVisuals.Length && tubeVisuals[i] != null) tubes[i] = tubeVisuals[i];
            else if (i < tubeVisualNames.Length && !string.IsNullOrEmpty(tubeVisualNames[i]))
                tubes[i] = FindDeepChild(transform, tubeVisualNames[i]);
        }
        tubeVisuals = tubes;

        tubeRenderers = new Renderer[count][];
        tubeLoaded = new bool[count];
        for (int i = 0; i < count; i++)
        {
            tubeRenderers[i] = tubes[i] != null
                ? tubes[i].GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            tubeLoaded[i] = true;
        }
        resolved = true;
    }

    private void ApplyAll()
    {
        for (int i = 0; i < tubeLoaded.Length; i++) ApplyTube(i);
    }

    private void ApplyTube(int tubeIndex)
    {
        if (tubeRenderers == null || tubeIndex < 0 || tubeIndex >= tubeRenderers.Length) return;
        Renderer[] renderers = tubeRenderers[tubeIndex];
        if (renderers == null) return;
        bool visible = tubeLoaded[tubeIndex];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = visible;
        }
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
}
