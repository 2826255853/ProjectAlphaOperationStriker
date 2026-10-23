using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ejects a brass casing from the <c>Ejection Port</c> node every time the sentry
/// fires. Casings are pooled under the art root (never under the yaw pivot), so the
/// spent brass keeps flying in the direction it was thrown even while the gun turns.
///
/// Motion is integrated by hand - no Rigidbody - so the arc is deterministic and the
/// same <see cref="SamplePosition"/> / <see cref="SampleRotation"/> helpers can drive
/// EditMode assertions and offline previews.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Shell Ejector")]
public sealed class SentryShellEjector : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform ejectionPort;
    [SerializeField] private GameObject casingPrefab;

    [Header("Pool")]
    [SerializeField, Min(1)] private int poolSize = 32;

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
        var turret = GetComponentInParent<AutoSentryTurret>();
        if (turret != null)
        {
            turret.ShotFired -= HandleShotFired;
            turret.ShotFired += HandleShotFired;
        }
    }

    private void OnDisable()
    {
        var turret = GetComponentInParent<AutoSentryTurret>();
        if (turret != null) turret.ShotFired -= HandleShotFired;
        RecycleAll();
    }

    private void Update()
    {
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
        if (ejectionPort == null || casing.Transform == null)
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

    private void EnsurePool()
    {
        if (casings.Count >= poolSize) return;
        if (casingPrefab == null) return;

        if (poolRoot == null)
        {
            var rootObject = new GameObject("Shell Casings");
            rootObject.transform.SetParent(transform, false);
            poolRoot = rootObject.transform;
        }

        int wanted = Mathf.Max(1, poolSize);
        while (casings.Count < wanted)
        {
            GameObject instance = Instantiate(casingPrefab, poolRoot, false);
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
