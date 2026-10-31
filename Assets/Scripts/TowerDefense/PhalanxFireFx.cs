using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Firing feedback for the Phalanx CIWS (密集阵) trap: muzzle flash and tracer rounds.
///
/// It is driven only by <see cref="PhalanxAutoCannon.ShotFired"/> (flash) and
/// <see cref="PhalanxAutoCannon.ShotTraced"/> (tracers), so the weapon stays the single
/// authority for when a round leaves the barrel. Nothing here is authored: the flash mesh,
/// the tracer line pool and their additive materials are built procedurally, which means a
/// regenerated <c>PhalanxVisual.prefab</c> only needs the component, not a hand-placed FX mesh.
///
/// The shell casings are NOT handled here — <see cref="SentryShellEjector"/> already owns
/// pooled, deterministic casing ballistics and listens to the same ShotFired event.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Phalanx Fire Fx")]
public sealed class PhalanxFireFx : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Muzzle node. Falls back to PhalanxTurretRig.Muzzle when left empty.")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private PhalanxAutoCannon cannon;

    [Header("Muzzle flash")]
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField, Min(0.005f)] private float flashDuration = 0.05f;
    [SerializeField, Min(0f)] private float flashScaleJitter = 0.28f;
    [SerializeField, Min(0.01f)] private float flashSize = 0.34f;
    [SerializeField, Min(0f)] private float flashForwardOffset = 0.08f;
    [SerializeField] private Color flashColor = new Color(1f, 0.62f, 0.18f, 1f);

    [Header("Tracers")]
    [SerializeField, Min(1f)] private float tracerSpeed = 240f;
    [SerializeField, Min(0.01f)] private float tracerLength = 2.4f;
    [SerializeField, Min(0.001f)] private float tracerWidth = 0.05f;
    [SerializeField, Min(1)] private int tracerPoolSize = 48;
    [Tooltip("Draw one tracer every N rounds. 1 = every round.")]
    [SerializeField, Min(1)] private int tracerEveryNthRound = 1;
    [SerializeField] private Color tracerColor = new Color(1f, 0.86f, 0.42f, 1f);

    private sealed class Tracer
    {
        public LineRenderer Line;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Distance;
        public float Travelled;
        public bool Active;
    }

    private readonly List<Tracer> tracers = new List<Tracer>();
    private Transform tracerRoot;
    private Transform flashRoot;
    private Material flashMaterial;
    private Material tracerMaterial;
    private Vector3 flashBaseScale = Vector3.one;
    private float flashTime;
    private int roundsSinceTracer;
    private uint randomState = 0x85EBCA6Bu;
    private bool initialized;

    /// <summary>True while the flash quad is still lit from the last round.</summary>
    public bool IsMuzzleFlashVisible => flashTime > 0f;

    /// <summary>Number of tracer streaks currently in flight (read-only, for tests/previews).</summary>
    public int ActiveTracerCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < tracers.Count; i++)
                if (tracers[i].Active) count++;
            return count;
        }
    }

    public Transform Muzzle => muzzle;
    public PhalanxAutoCannon Cannon => cannon;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        BindCannon();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        flashTime = 0f;
        if (muzzleFlash != null && muzzleFlash.activeSelf) muzzleFlash.SetActive(false);
        if (flashRoot != null && flashRoot.gameObject.activeSelf) flashRoot.gameObject.SetActive(false);
        for (int i = 0; i < tracers.Count; i++) Deactivate(tracers[i]);
    }

    private void OnDestroy()
    {
        if (flashMaterial != null) Destroy(flashMaterial);
        if (tracerMaterial != null) Destroy(tracerMaterial);
    }

    private void Update()
    {
        // The cannon lives on the sentry root, which is only the parent once the branch
        // visual has been instantiated under it; retry so a late parent still binds.
        if (cannon == null)
        {
            BindCannon();
            if (cannon != null) Subscribe();
        }
        Tick(Time.deltaTime);
        BillboardFlash();
    }

    /// <summary>Resolves the rig and builds the procedural flash/tracer objects once.</summary>
    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        BindCannon();
        ResolveMuzzle();
        BuildMuzzleFlash();
        EnsureTracerPool();
    }

    /// <summary>Advances the flash timer and every tracer streak. Public for tests and previews.</summary>
    public void Tick(float deltaTime)
    {
        EnsureInitialized();
        if (deltaTime < 0f) deltaTime = 0f;

        if (flashTime > 0f)
        {
            flashTime -= deltaTime;
            if (flashTime <= 0f) HideFlash();
        }

        if (deltaTime <= 0f) return;
        for (int i = 0; i < tracers.Count; i++)
        {
            Tracer tracer = tracers[i];
            if (!tracer.Active) continue;
            tracer.Travelled += Mathf.Max(1f, tracerSpeed) * deltaTime;
            if (tracer.Travelled >= tracer.Distance + tracerLength)
            {
                Deactivate(tracer);
                continue;
            }
            ApplyTracerPositions(tracer);
        }
    }

    /// <summary>One round left the barrel: flash, and (every Nth round) launch a tracer.</summary>
    public void PlayShot(Vector3 muzzleWorldPosition, Vector3 aimPoint)
    {
        EnsureInitialized();
        PlayFlash();

        roundsSinceTracer++;
        if (roundsSinceTracer < Mathf.Max(1, tracerEveryNthRound)) return;
        roundsSinceTracer = 0;
        SpawnTracer(muzzleWorldPosition, aimPoint);
    }

    /// <summary>Throws a tracer streak from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public void SpawnTracer(Vector3 from, Vector3 to)
    {
        EnsureInitialized();
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.05f) return;

        Tracer tracer = TakeAvailableTracer();
        if (tracer == null || tracer.Line == null) return;

        tracer.Active = true;
        tracer.Line.enabled = true;
        tracer.Origin = from;
        tracer.Direction = delta / distance;
        tracer.Distance = distance;
        tracer.Travelled = 0f;
        ApplyTracerPositions(tracer);
    }

    /// <summary>Hides the flash and recycles every tracer (used when the branch is torn down).</summary>
    public void ResetFx()
    {
        flashTime = 0f;
        HideFlash();
        for (int i = 0; i < tracers.Count; i++) Deactivate(tracers[i]);
    }

    /// <summary>Head position of a streak that has travelled <paramref name="travelled"/> metres.</summary>
    public static Vector3 SampleTracerHead(Vector3 origin, Vector3 direction, float travelled) =>
        origin + direction * Mathf.Max(0f, travelled);

    private void PlayFlash()
    {
        flashTime = Mathf.Max(0.005f, flashDuration);
        GameObject target = flashRoot != null ? flashRoot.gameObject : muzzleFlash;
        if (target == null) return;
        if (!target.activeSelf) target.SetActive(true);

        Transform jitterTarget = flashRoot != null ? flashRoot : target.transform;
        float jitter = 1f + NextFloat(-flashScaleJitter, flashScaleJitter);
        jitterTarget.localScale = flashBaseScale * Mathf.Max(0.05f, jitter);
        if (flashRoot != null)
            flashRoot.localRotation = Quaternion.Euler(0f, 0f, NextFloat(0f, 360f));
    }

    private void HideFlash()
    {
        if (flashRoot != null && flashRoot.gameObject.activeSelf) flashRoot.gameObject.SetActive(false);
        if (muzzleFlash != null && muzzleFlash.activeSelf) muzzleFlash.SetActive(false);
    }

    private void BillboardFlash()
    {
        if (flashRoot == null || !flashRoot.gameObject.activeSelf) return;
        Camera camera = Camera.main;
        if (camera == null) return;
        Vector3 toCamera = flashRoot.position - camera.transform.position;
        if (toCamera.sqrMagnitude < 0.0001f) return;
        flashRoot.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    private void ApplyTracerPositions(Tracer tracer)
    {
        float headTravelled = Mathf.Min(tracer.Travelled, tracer.Distance + tracerLength);
        float tailTravelled = Mathf.Max(0f, headTravelled - Mathf.Max(0.01f, tracerLength));
        tracer.Line.SetPosition(0, SampleTracerHead(tracer.Origin, tracer.Direction, headTravelled));
        tracer.Line.SetPosition(1, SampleTracerHead(tracer.Origin, tracer.Direction, tailTravelled));
    }

    private void BuildMuzzleFlash()
    {
        if (muzzleFlash != null || muzzle == null) return;

        var rootObject = new GameObject("Muzzle Flash");
        rootObject.transform.SetParent(muzzle, false);
        rootObject.transform.localPosition = Vector3.zero;
        flashRoot = rootObject.transform;
        flashBaseScale = Vector3.one * Mathf.Max(0.01f, flashSize);
        flashRoot.localScale = flashBaseScale;

        flashMaterial = CreateAdditiveMaterial("Phalanx Muzzle Flash", flashColor);
        Mesh quad = BuildQuadMesh();

        // Three crossed quads read as a volumetric star from any camera angle without
        // needing a texture, which keeps the effect asset-free.
        for (int i = 0; i < 3; i++)
        {
            var quadObject = new GameObject("Flash Quad " + i);
            quadObject.transform.SetParent(flashRoot, false);
            quadObject.transform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
            quadObject.transform.localPosition = Vector3.forward * (flashForwardOffset * i * 0.5f);
            var filter = quadObject.AddComponent<MeshFilter>();
            filter.sharedMesh = quad;
            var renderer = quadObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = flashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        rootObject.SetActive(false);
    }

    private void EnsureTracerPool()
    {
        if (tracers.Count > 0) return;
        if (tracerRoot == null)
        {
            var rootObject = new GameObject("Tracers");
            rootObject.transform.SetParent(transform, false);
            tracerRoot = rootObject.transform;
        }
        tracerMaterial = CreateAdditiveMaterial("Phalanx Tracer", tracerColor);

        int wanted = Mathf.Max(1, tracerPoolSize);
        for (int i = 0; i < wanted; i++)
        {
            var lineObject = new GameObject("Tracer " + i.ToString("00"));
            lineObject.transform.SetParent(tracerRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.widthMultiplier = Mathf.Max(0.001f, tracerWidth);
            line.sharedMaterial = tracerMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            tracers.Add(new Tracer { Line = line, Active = false });
        }
    }

    private Tracer TakeAvailableTracer()
    {
        for (int i = 0; i < tracers.Count; i++)
            if (!tracers[i].Active) return tracers[i];
        return null;
    }

    private static void Deactivate(Tracer tracer)
    {
        if (tracer == null) return;
        tracer.Active = false;
        tracer.Travelled = 0f;
        if (tracer.Line != null && tracer.Line.enabled) tracer.Line.enabled = false;
    }

    private void BindCannon()
    {
        if (cannon == null) cannon = GetComponentInParent<PhalanxAutoCannon>();
    }

    private void ResolveMuzzle()
    {
        if (muzzle != null) return;
        PhalanxTurretRig rig = GetComponentInParent<PhalanxTurretRig>();
        if (rig != null) muzzle = rig.Muzzle;
        else if (cannon != null && cannon.Rig != null) muzzle = cannon.Rig.Muzzle;
    }

    private void Subscribe()
    {
        if (cannon == null) return;
        cannon.ShotFired -= HandleShotFired;
        cannon.ShotFired += HandleShotFired;
        cannon.ShotTraced -= HandleShotTraced;
        cannon.ShotTraced += HandleShotTraced;
    }

    private void Unsubscribe()
    {
        if (cannon == null) return;
        cannon.ShotFired -= HandleShotFired;
        cannon.ShotTraced -= HandleShotTraced;
    }

    private void HandleShotFired(Vector3 muzzleWorldPosition)
    {
        PlayFlash();
    }

    private void HandleShotTraced(Vector3 muzzleWorldPosition, Vector3 aimPoint)
    {
        roundsSinceTracer++;
        if (roundsSinceTracer < Mathf.Max(1, tracerEveryNthRound)) return;
        roundsSinceTracer = 0;
        SpawnTracer(muzzleWorldPosition, aimPoint);
    }

    /// <summary>Unlit, additive, double-sided material so the FX never depend on scene lighting.</summary>
    private static Material CreateAdditiveMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = name };

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        if (shader != null && shader.name == "Universal Render Pipeline/Unlit")
        {
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 2f);   // additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return material;
    }

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "Phalanx Flash Quad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        mesh.normals = new[]
        {
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Deterministic small-range generator so previews and tests agree.</summary>
    private float NextFloat(float min, float max)
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return Mathf.Lerp(min, max, (randomState & 0xFFFFFF) / 16777215f);
    }
}
