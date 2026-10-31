using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ejects a brass casing from the <c>Ejection Port</c> node every time the gun fires.
/// Casings are pooled under the art root (never under the yaw pivot), so the spent
/// brass keeps flying in the direction it was thrown even while the gun turns.
///
/// Motion is integrated by hand - no Rigidbody - so the arc is deterministic and the
/// same <see cref="SamplePosition"/> / <see cref="SampleRotation"/> helpers can drive
/// EditMode assertions and offline previews.
///
/// It listens to whichever weapon owns the art: <see cref="AutoSentryTurret"/> first,
/// then the Phalanx branch's <see cref="PhalanxAutoCannon"/>. Both raise
/// <c>ShotFired(Vector3 muzzleWorldPosition)</c>, which is the only contract needed here.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Shell Ejector")]
public sealed class SentryShellEjector : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform ejectionPort;
    [SerializeField] private GameObject casingPrefab;
    [Tooltip("Optional. Used to synthesise a port and casing when none are authored.")]
    [SerializeField] private Transform muzzle;

    [Header("Pool")]
    [SerializeField, Min(1)] private int poolSize = 32;

    [Header("Fallback (used only when the port/casing are not authored)")]
    [Tooltip("Port offset from the muzzle, in muzzle local space, when no port node exists.")]
    [SerializeField] private Vector3 fallbackPortOffset = new Vector3(0.11f, -0.06f, 0.02f);

    [Header("Flight")]
    [Tooltip("Spawn velocity in ejection-port local space (+X is the gun's right flank).")]
    [SerializeField] private Vector3 localVelocity = new Vector3(1.5f, 2.2f, -0.4f);
    [SerializeField] private float gravity = -9.81f;
    [SerializeField, Min(0f)] private float spinDegreesPerSecond = 900f;
    [SerializeField, Min(0.05f)] private float lifetime = 1.6f;
    [Tooltip("Casing is recycled once it drops this far below the turret's floor line.")]
    [SerializeField, Min(0f)] private float groundClearance = 0.02f;

    private sealed class Casing
    {
        public Transform Transform;
        public GameObject GameObject;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
        public Vector3 Velocity;
        public Vector3 SpinAxis;
        public float SpinDegrees;
        public float Age;
        public bool Active;
    }

    private readonly List<Casing> casings = new List<Casing>();
    private Transform poolRoot;
    private float groundY;
    private AutoSentryTurret sentry;
    private PhalanxAutoCannon cannon;
    private GameObject fallbackCasing;
    private Material fallbackCasingMaterial;

    public Transform EjectionPort => ejectionPort;
    public GameObject CasingPrefab => casingPrefab;
    public int PoolSize => poolSize;
    public int ActiveCasingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < casings.Count; i++)
                if (casings[i].Active) count++;
            return count;
        }
    }

    private void Awake()
    {
        groundY = transform.position.y;
    }

    private void OnEnable()
    {
        BindWeapon();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        RecycleAll();
    }

    private void OnDestroy()
    {
        if (fallbackCasingMaterial != null) Destroy(fallbackCasingMaterial);
        if (fallbackCasing != null) Destroy(fallbackCasing);
    }

    /// <summary>Resolves the firing weapon; sentry first, then the Phalanx branch cannon.</summary>
    public void BindWeapon()
    {
        if (sentry == null) sentry = GetComponentInParent<AutoSentryTurret>();
        if (cannon == null && sentry == null) cannon = GetComponentInParent<PhalanxAutoCannon>();
    }

    private void Subscribe()
    {
        if (sentry != null)
        {
            sentry.ShotFired -= HandleShotFired;
            sentry.ShotFired += HandleShotFired;
            return;
        }
        if (cannon != null)
        {
            cannon.ShotFired -= HandleShotFired;
            cannon.ShotFired += HandleShotFired;
        }
    }

    private void Unsubscribe()
    {
        if (sentry != null) sentry.ShotFired -= HandleShotFired;
        if (cannon != null) cannon.ShotFired -= HandleShotFired;
    }

    private void Update()
    {
        // Same late-binding concern as the FX component: the weapon may only become a
        // parent after the visual is instantiated under the sentry root.
        if (sentry == null && cannon == null)
        {
            BindWeapon();
            Subscribe();
        }
        if (Application.isPlaying) Step(Time.deltaTime);
    }

    /// <summary>Advances every live casing. Public so tests/previews can scrub time.</summary>
    public void Step(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        groundY = transform.position.y;
        for (int i = 0; i < casings.Count; i++)
        {
            Casing casing = casings[i];
            if (!casing.Active) continue;

            casing.Age += deltaTime;

            // Integrate in closed form from the launch state. Stepping from the current
            // position with the constant launch velocity would ignore gravity entirely
            // (the velocity never accumulates), which made casings rise in a straight line.
            Vector3 position = SamplePosition(casing.SpawnPosition, casing.Velocity, gravity, casing.Age);
            casing.Transform.position = position;
            casing.Transform.rotation = SampleRotation(casing.SpawnRotation, casing.SpinAxis,
                casing.SpinDegrees, casing.Age);

            if (casing.Age >= lifetime || position.y <= groundY + groundClearance)
                Recycle(casing);
        }
    }

    /// <summary>Throws one casing out of the ejection port.</summary>
    public void Eject()
    {
        EnsurePool();
        Casing casing = TakeAvailable();
        if (casing == null) return;
        if (EnsureEjectionPort() == null || casing.Transform == null)
        {
            Recycle(casing);
            return;
        }

        casing.Age = 0f;
        casing.Active = true;
        casing.GameObject.SetActive(true);
        casing.SpawnPosition = ejectionPort.position;
        casing.SpawnRotation = ejectionPort.rotation;
        casing.Transform.position = casing.SpawnPosition;
        casing.Transform.rotation = casing.SpawnRotation;
        casing.Velocity = ejectionPort.TransformDirection(localVelocity);

        // A little per-shot variation keeps the stream from looking like a conveyor belt,
        // while the seeded axes stay deterministic for previews.
        float jitter = ((casings.IndexOf(casing) * 37) % 11) / 11f;
        casing.SpinAxis = (ejectionPort.up + ejectionPort.right * (jitter - 0.5f)).normalized;
        casing.SpinDegrees = spinDegreesPerSecond * (0.75f + 0.5f * jitter);
    }

    /// <summary>Collects the world positions of every live casing (for previews and tests).</summary>
    public void CollectActiveCasingPositions(List<Vector3> buffer)
    {
        if (buffer == null) return;
        buffer.Clear();
        for (int i = 0; i < casings.Count; i++)
        {
            Casing casing = casings[i];
            if (!casing.Active || casing.Transform == null) continue;
            buffer.Add(casing.Transform.position);
        }
    }

    /// <summary>Deactivates and returns every casing to the pool.</summary>
    public void RecycleAll()
    {
        for (int i = 0; i < casings.Count; i++)
            if (casings[i].Active) Recycle(casings[i]);
    }

    /// <summary>Ballistic position of a casing after <paramref name="time"/> seconds.</summary>
    public static Vector3 SamplePosition(Vector3 origin, Vector3 velocity, float gravity, float time) =>
        origin + velocity * time + new Vector3(0f, 0.5f * gravity * time * time, 0f);

    /// <summary>Spin applied to the starting orientation over <paramref name="time"/> seconds.</summary>
    public static Quaternion SampleRotation(Quaternion start, Vector3 spinAxis, float degreesPerSecond,
        float time) => start * Quaternion.AngleAxis(degreesPerSecond * time, spinAxis);

    private void HandleShotFired(Vector3 muzzleWorldPosition) => Eject();

    /// <summary>Returns the authored casing prefab, or a procedurally built brass cylinder.</summary>
    private GameObject EnsureCasingPrefab()
    {
        if (casingPrefab != null) return casingPrefab;
        if (fallbackCasing != null) return fallbackCasing;

        fallbackCasing = new GameObject("Procedural Casing");
        fallbackCasing.SetActive(false);

        // Built from a mesh instead of GameObject.CreatePrimitive so the casing never carries
        // a Collider: Instantiate() would otherwise copy a not-yet-destroyed collider onto
        // every pooled casing and hand the physics engine 96 stray bodies.
        var body = new GameObject("Brass");
        body.transform.SetParent(fallbackCasing.transform, false);
        // Lay the cylinder along the local +X throw axis so the spin reads as tumbling.
        body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        body.transform.localScale = new Vector3(0.021f, 0.031f, 0.021f);

        var filter = body.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildCasingMesh();
        var renderer = body.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = EnsureFallbackCasingMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return fallbackCasing;
    }

    /// <summary>Unit cylinder (radius 0.5, length 1 along +Y) with flat caps.</summary>
    private static Mesh BuildCasingMesh()
    {
        const int segments = 10;
        var vertices = new Vector3[segments * 2 + 2];
        float radius = 0.5f;
        float halfHeight = 0.5f;

        for (int i = 0; i < segments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segments;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[i] = new Vector3(x, -halfHeight, z);
            vertices[i + segments] = new Vector3(x, halfHeight, z);
        }
        int bottomCenter = segments * 2;
        int topCenter = bottomCenter + 1;
        vertices[bottomCenter] = new Vector3(0f, -halfHeight, 0f);
        vertices[topCenter] = new Vector3(0f, halfHeight, 0f);

        var triangles = new List<int>(segments * 12);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int bottomA = i, bottomB = next, topA = i + segments, topB = next + segments;
            triangles.Add(bottomA); triangles.Add(topA); triangles.Add(topB);
            triangles.Add(bottomA); triangles.Add(topB); triangles.Add(bottomB);
            triangles.Add(bottomCenter); triangles.Add(bottomB); triangles.Add(bottomA);
            triangles.Add(topCenter); triangles.Add(topA); triangles.Add(topB);
        }

        var mesh = new Mesh { name = "Casing Cylinder" };
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material EnsureFallbackCasingMaterial()
    {
        if (fallbackCasingMaterial != null) return fallbackCasingMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        fallbackCasingMaterial = new Material(shader) { name = "Phalanx Brass" };
        Color brass = new Color(0.78f, 0.60f, 0.20f, 1f);
        if (fallbackCasingMaterial.HasProperty("_BaseColor"))
            fallbackCasingMaterial.SetColor("_BaseColor", brass);
        if (fallbackCasingMaterial.HasProperty("_Color"))
            fallbackCasingMaterial.SetColor("_Color", brass);
        if (fallbackCasingMaterial.HasProperty("_Metallic"))
            fallbackCasingMaterial.SetFloat("_Metallic", 0.85f);
        if (fallbackCasingMaterial.HasProperty("_Smoothness"))
            fallbackCasingMaterial.SetFloat("_Smoothness", 0.5f);
        return fallbackCasingMaterial;
    }

    /// <summary>
    /// Returns the authored port, or synthesises one next to the muzzle. The synthetic port
    /// is parented to the pitch pivot (not the spinning barrel cluster) so casings keep a
    /// stable clock position while the six barrels rotate.
    /// </summary>
    private Transform EnsureEjectionPort()
    {
        if (ejectionPort != null) return ejectionPort;
        PhalanxTurretRig rig = GetComponentInParent<PhalanxTurretRig>();
        if (muzzle == null && rig != null) muzzle = rig.Muzzle;
        if (muzzle == null) return null;

        var portObject = new GameObject("Ejection Port");
        ejectionPort = portObject.transform;
        // The imported Phalanx node keeps Blender's axis frame, so the muzzle's local axes are
        // NOT world up/right. Build a clean frame instead: +Z = barrel, +Y = world up, +X = right,
        // which is exactly what localVelocity documents.
        Vector3 barrel = rig != null ? rig.BarrelDirection : muzzle.forward;
        if (barrel.sqrMagnitude < 0.0001f) barrel = muzzle.forward;
        Quaternion worldRotation = Quaternion.LookRotation(barrel.normalized, Vector3.up);
        Vector3 worldPosition = muzzle.position + worldRotation * fallbackPortOffset;
        Transform anchor = rig != null ? rig.PitchPivot : null;
        if (anchor != null) ejectionPort.SetParent(anchor, true);
        else ejectionPort.SetParent(muzzle, true);
        ejectionPort.position = worldPosition;
        ejectionPort.rotation = worldRotation;
        return ejectionPort;
    }

    private void EnsurePool()
    {
        if (casings.Count >= poolSize) return;
        GameObject prefab = EnsureCasingPrefab();
        if (prefab == null) return;

        if (poolRoot == null)
        {
            var rootObject = new GameObject("Shell Casings");
            rootObject.transform.SetParent(transform, false);
            poolRoot = rootObject.transform;
        }

        int wanted = Mathf.Max(1, poolSize);
        while (casings.Count < wanted)
        {
            GameObject instance = Instantiate(prefab, poolRoot, false);
            instance.name = "Shell Casing " + casings.Count.ToString("00");
            instance.SetActive(false);
            casings.Add(new Casing
            {
                Transform = instance.transform,
                GameObject = instance,
                Active = false,
            });
        }
    }

    private Casing TakeAvailable()
    {
        for (int i = 0; i < casings.Count; i++)
            if (!casings[i].Active) return casings[i];
        return null;
    }

    private void Recycle(Casing casing)
    {
        casing.Active = false;
        casing.Age = 0f;
        if (casing.GameObject != null && casing.GameObject.activeSelf) casing.GameObject.SetActive(false);
    }
}
