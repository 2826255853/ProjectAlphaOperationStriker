using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Betty No.7 (贝蒂7号): a two metre walkable floor trap. The centre detector
/// watches for ground monsters, works out which side the nearest one is on, and
/// launches the loaded corner canister that sits closest to that direction.
/// Each of the four canisters is its own slot with its own reload timer, so the
/// trap keeps firing the canister that is loaded and facing the threat instead
/// of dumping all four at once. Flying monsters are ignored on purpose — this
/// trap's whole design is a ground ambush, matching <see cref="GroundSpikeTrap"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
[DefaultExecutionOrder(1000)]
public sealed class Betty7JumpMineTrap : MonoBehaviour
{
    public enum AttackState { Ready, Arming, Launching, Detonating, Recovering, Cooldown }

    private const string MineNamePrefix = "Mine_";
    private const string DomeName = "Detector_Dome";
    private const float StateEpsilon = 1e-5f;
    private const float AlignmentEpsilon = 0.01f;

    [Header("Wiring")]
    [SerializeField] private Transform[] mineRoots = new Transform[0];
    [SerializeField] private Transform detectorDome;

    [Header("Detection")]
    [SerializeField, Min(0f)] private float detectRadius = 3.2f;
    [SerializeField, Min(0.001f)] private float armingSeconds = 0.30f;
    [Tooltip("怪物脚下偏离本陷阱基座的高度容差；用来排除站在另一层平台上的怪。")]
    [SerializeField, Min(0f)] private float detectHeightTolerance = 2.2f;

    [Header("Launch")]
    [SerializeField, Min(0.001f)] private float launchSeconds = 0.45f;
    [SerializeField, Min(0f)] private float launchHeight = 1.15f;
    [SerializeField, Min(0.001f)] private float detonateHoldSeconds = 0.22f;
    [SerializeField, Min(0.001f)] private float recoverSeconds = 0.45f;

    [Header("Blast")]
    [Tooltip("单颗跳雷起爆的伤害；一次只起爆一颗。")]
    [SerializeField, Min(0f)] private float damage = 55f;
    [SerializeField, Min(0f)] private float blastRadius = 2.5f;
    [Tooltip("起爆点相对基座的高度；伤害取水平距离，配一个垂直容差。")]
    [SerializeField, Min(0f)] private float blastHeightTolerance = 2.6f;
    [Tooltip("每个槽位单独装填：只有刚起爆的那颗跳雷进入这段装填时间，其余槽位各自按自己的计时器恢复。")]
    [SerializeField, Min(0.001f)] private float cooldownSeconds = 6f;

    [Header("Feedback")]
    [SerializeField, Min(0f)] private float flashRadius = 0.55f;
    [SerializeField, Min(0f)] private float flashLightIntensity = 6f;
    [SerializeField, Min(0f)] private float domePulseScale = 0.12f;

    private readonly List<BurstFlash> flashes = new List<BurstFlash>();
    private readonly List<MineSlot> slots = new List<MineSlot>();
    private TrapInstance trap;
    private Vector3 domeRestScale = Vector3.one;

    /// <summary>
    /// Aggregate of the per-slot states, most active first. A trap with one
    /// canister in the air reports <see cref="AttackState.Launching"/> even
    /// though the three other canisters are still loaded.
    /// </summary>
    public AttackState State => AggregateState();
    /// <summary>Number of completed detonations across all slots; editor tests assert this.</summary>
    public int DetonationCount { get; private set; }
    /// <summary>Working launch height, exposed for verification tooling.</summary>
    public float LaunchHeight => launchHeight;
    /// <summary>Slot that the detector committed most recently, or -1 before the first launch.</summary>
    public int LastLaunchedMineIndex { get; private set; } = -1;
    /// <summary>Flat direction from the base to the monster the last launch was aimed at.</summary>
    public Vector3 LastLaunchDirection { get; private set; } = Vector3.forward;
    public int MineSlotCount => slots.Count;

    public AttackState GetMineState(int index)
        => IsValidSlot(index) ? slots[index].State : AttackState.Ready;

    public bool IsMineReady(int index)
        => IsValidSlot(index) && slots[index].State == AttackState.Ready;

    /// <summary>Seconds left before the given slot is loaded again; 0 when it is not reloading.</summary>
    public float GetMineReloadRemaining(int index)
    {
        if (!IsValidSlot(index)) return 0f;
        MineSlot slot = slots[index];
        return slot.State == AttackState.Cooldown
            ? Mathf.Max(0f, Duration(AttackState.Cooldown) - slot.Elapsed)
            : 0f;
    }

    public int GetMineDetonationCount(int index)
        => IsValidSlot(index) ? slots[index].DetonationCount : 0;

    private bool IsValidSlot(int index) => index >= 0 && index < slots.Count;

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        CacheWiring();
    }

    private void OnEnable()
    {
        if (trap == null) trap = GetComponent<TrapInstance>();
        trap.Destroyed += HandleDestroyed;
    }

    private void OnDisable()
    {
        if (trap != null) trap.Destroyed -= HandleDestroyed;
        ResetToReady();
    }

    private void CacheWiring()
    {
        if (detectorDome == null)
        {
            Transform dome = transform.Find(DomeName);
            if (dome != null) detectorDome = dome;
        }
        if (detectorDome != null) domeRestScale = detectorDome.localScale;

        if (mineRoots == null || mineRoots.Length == 0) mineRoots = CollectMineRoots();
        slots.Clear();
        foreach (Transform mine in mineRoots)
        {
            if (mine == null) continue;
            slots.Add(new MineSlot(slots.Count, mine));
        }
    }

    /// <summary>Falls back to the authored child names so a rebuilt prefab needs no rewiring.</summary>
    private Transform[] CollectMineRoots()
    {
        var found = new List<Transform>();
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform) continue;
            if (child.name.StartsWith(MineNamePrefix, StringComparison.Ordinal)) found.Add(child);
        }
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found.ToArray();
    }

    private void HandleDestroyed(TrapInstance unused) => ResetToReady();

    private void LateUpdate() => Tick(Time.deltaTime);

    /// <summary>Explicit simulation entry point so editor tests need no frame timing.</summary>
    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || trap == null || trap.IsDestroyed) return;
        float dt = Mathf.Max(0f, deltaTime);

        EnemyHealth target = FindNearestGroundTarget(detectRadius);
        // One canister in the air at a time: the trap sends the nearest loaded
        // one toward the threat, then waits for it to clear the base.
        int launchSlot = target != null && !IsAnyMineEngaged() ? SelectMineForTarget(target) : -1;

        for (int i = 0; i < slots.Count; i++)
            TickSlot(slots[i], i == launchSlot ? target : null, dt);

        UpdateDomePulse();
        UpdateFlashes(deltaTime);
    }

    /// <summary>
    /// Drives one slot through its own state machine. The slot only leaves
    /// <see cref="AttackState.Ready"/> when the detector handed it a target.
    /// </summary>
    private void TickSlot(MineSlot slot, EnemyHealth launchTarget, float deltaTime)
    {
        float remaining = deltaTime;
        // Step across state boundaries so a long frame cannot skip a detonation.
        for (int guard = 0; guard < 64; guard++)
        {
            if (slot.State == AttackState.Ready)
            {
                if (launchTarget == null || !BeginArming(slot, launchTarget))
                {
                    ApplyPose(slot);
                    return;
                }
            }

            float duration = Duration(slot.State);
            if (slot.Elapsed + StateEpsilon < duration)
            {
                float step = Mathf.Min(remaining, duration - slot.Elapsed);
                slot.Elapsed += step;
                remaining -= step;
                ApplyPose(slot);
                if (slot.Elapsed + StateEpsilon < duration) return;
            }

            AdvanceState(slot);
            ApplyPose(slot);
            if (remaining <= StateEpsilon) return;
        }
    }

    private float Duration(AttackState state)
    {
        switch (state)
        {
            case AttackState.Arming: return Mathf.Max(0.001f, armingSeconds);
            case AttackState.Launching: return Mathf.Max(0.001f, launchSeconds);
            case AttackState.Detonating: return Mathf.Max(0.001f, detonateHoldSeconds);
            case AttackState.Recovering: return Mathf.Max(0.001f, recoverSeconds);
            case AttackState.Cooldown: return Mathf.Max(0.001f, cooldownSeconds);
            default: return 0.001f;
        }
    }

    private bool BeginArming(MineSlot slot, EnemyHealth target)
    {
        if (slot.Root == null) return false;
        slot.State = AttackState.Arming;
        slot.Elapsed = 0f;
        LastLaunchedMineIndex = slot.Index;
        LastLaunchDirection = FlatDirectionTo(target);
        return true;
    }

    private void AdvanceState(MineSlot slot)
    {
        slot.Elapsed = 0f;
        switch (slot.State)
        {
            case AttackState.Arming:
                slot.State = AttackState.Launching;
                break;
            case AttackState.Launching:
                slot.State = AttackState.Detonating;
                Detonate(slot);
                break;
            case AttackState.Detonating:
                slot.State = AttackState.Recovering;
                SetMineVisible(slot, true);
                break;
            case AttackState.Recovering:
                // Only this slot starts reloading; the other canisters keep
                // their own timers untouched.
                slot.State = AttackState.Cooldown;
                break;
            case AttackState.Cooldown:
                slot.State = AttackState.Ready;
                SetMineVisible(slot, true);
                break;
            default:
                slot.State = AttackState.Ready;
                break;
        }
    }

    // ------------------------------------------------------------------- posing
    private void ApplyPose(MineSlot slot)
    {
        float fraction = Mathf.Clamp01(slot.Elapsed / Duration(slot.State));
        switch (slot.State)
        {
            case AttackState.Launching:
                SetMineRise(slot, EaseOutCubic(fraction));
                break;
            case AttackState.Detonating:
                SetMineRise(slot, 1f);
                break;
            case AttackState.Recovering:
                SetMineRise(slot, 1f - fraction);
                break;
            default:
                // Ready / Arming / Cooldown all sit down in the slot.
                SetMineRise(slot, 0f);
                break;
        }
    }

    private void SetMineRise(MineSlot slot, float fraction)
    {
        if (slot.Root == null) return;
        slot.Root.localPosition = slot.RestLocalPosition + Vector3.up * (launchHeight * Mathf.Clamp01(fraction));
    }

    private void SetMineVisible(MineSlot slot, bool visible)
    {
        if (slot.Root == null) return;
        if (slot.Root.gameObject.activeSelf != visible) slot.Root.gameObject.SetActive(visible);
    }

    private void SetDomePulse(float amount)
    {
        if (detectorDome == null) return;
        detectorDome.localScale = domeRestScale * (1f + domePulseScale * Mathf.Clamp01(amount));
    }

    /// <summary>Detector brightness follows whichever canister is currently locking on.</summary>
    private void UpdateDomePulse()
    {
        float amount = 0f;
        foreach (MineSlot slot in slots)
        {
            if (slot.State != AttackState.Arming && slot.State != AttackState.Launching) continue;
            float fraction = Mathf.Clamp01(slot.Elapsed / Duration(slot.State));
            amount = slot.State == AttackState.Arming
                ? Mathf.Max(amount, Mathf.Lerp(0.35f, 1f, fraction))
                : Mathf.Max(amount, 1f - fraction);
        }
        // Breathing pulse makes the lock-on read as "detector is tracking".
        if (amount > 0.35f) amount *= 0.75f + 0.25f * Mathf.Sin(Time.time * 18f);
        SetDomePulse(amount);
    }

    // ----------------------------------------------------------------- selecting
    /// <summary>
    /// Direction pick: the loaded canister whose bearing from the base best
    /// matches the monster's side, with the flat distance to that monster as the
    /// tie-breaker. On the symmetric four-corner layout this is also simply the
    /// closest usable canister.
    /// </summary>
    private int SelectMineForTarget(EnemyHealth target)
    {
        if (slots.Count == 0) return -1;
        Vector3 origin = transform.position;
        Vector3 feet = FeetPosition(target);
        Vector2 toTarget = new Vector2(feet.x - origin.x, feet.z - origin.z);

        int bestIndex = -1;
        float bestAlignment = float.NegativeInfinity;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < slots.Count; i++)
        {
            MineSlot slot = slots[i];
            if (slot.Root == null || slot.State != AttackState.Ready) continue;

            Vector3 mineWorld = transform.TransformPoint(slot.RestLocalPosition);
            Vector2 toMine = new Vector2(mineWorld.x - origin.x, mineWorld.z - origin.z);
            Vector2 mineToTarget = new Vector2(feet.x - mineWorld.x, feet.z - mineWorld.z);
            float distance = mineToTarget.magnitude;

            float alignment = 0f;
            if (toTarget.sqrMagnitude > 1e-6f && toMine.sqrMagnitude > 1e-6f)
                alignment = Vector2.Dot(toTarget.normalized, toMine.normalized);

            bool betterAlignment = alignment > bestAlignment + AlignmentEpsilon;
            bool sameSideCloser = Mathf.Abs(alignment - bestAlignment) <= AlignmentEpsilon
                                  && distance < bestDistance - AlignmentEpsilon;
            if (!betterAlignment && !sameSideCloser) continue;

            bestIndex = i;
            bestAlignment = alignment;
            bestDistance = distance;
        }

        return bestIndex;
    }

    private bool IsAnyMineEngaged()
    {
        foreach (MineSlot slot in slots)
        {
            if (slot.State == AttackState.Arming || slot.State == AttackState.Launching
                || slot.State == AttackState.Detonating || slot.State == AttackState.Recovering)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ damaging
    private void Detonate(MineSlot slot)
    {
        slot.DetonationCount++;
        DetonationCount++;
        if (!slot.DamageApplied)
        {
            slot.DamageApplied = true;
            DamageAround(MineBlastCenter(slot));
        }
        SetMineVisible(slot, false);
        SpawnFlash(MineBlastCenter(slot));
    }

    private Vector3 MineBlastCenter(MineSlot slot)
    {
        if (slot.Root == null) return transform.position + Vector3.up * launchHeight;
        return transform.TransformPoint(slot.RestLocalPosition + Vector3.up * launchHeight);
    }

    /// <summary>
    /// Ground monsters inside the horizontal blast ring. Height is only a
    /// tolerance filter so monsters on an upper platform are not hit; the damage
    /// never depends on a collider, mirroring MissileProjectile.
    /// </summary>
    private void DamageAround(Vector3 center)
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) continue;
            if (IsFlying(enemy)) continue;
            if (!IsInsideBlast(center, enemy)) continue;
            enemy.TakeDamage(damage, TrapDamageType.Explosive);
        }
    }

    private bool IsInsideBlast(Vector3 center, EnemyHealth enemy)
    {
        Vector3 feet = FeetPosition(enemy);
        Vector2 flat = new Vector2(feet.x - center.x, feet.z - center.z);
        if (flat.magnitude > blastRadius) return false;
        return Mathf.Abs(feet.y - center.y) <= blastHeightTolerance;
    }

    private EnemyHealth FindNearestGroundTarget(float radius)
    {
        Vector3 origin = transform.position;
        EnemyHealth nearest = null;
        float nearestDistance = float.MaxValue;
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) continue;
            if (IsFlying(enemy)) continue;
            Vector3 feet = FeetPosition(enemy);
            Vector2 flat = new Vector2(feet.x - origin.x, feet.z - origin.z);
            float distance = flat.magnitude;
            if (distance > radius) continue;
            if (Mathf.Abs(feet.y - origin.y) > detectHeightTolerance) continue;
            if (distance >= nearestDistance) continue;
            nearest = enemy;
            nearestDistance = distance;
        }
        return nearest;
    }

    private Vector3 FlatDirectionTo(EnemyHealth target)
    {
        Vector3 delta = FeetPosition(target) - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude > 1e-6f ? delta.normalized : transform.forward;
    }

    /// <summary>Single flying check: <see cref="MonsterType"/> first, follower as fallback.</summary>
    private static bool IsFlying(EnemyHealth enemy)
    {
        EnemyInstance identity = enemy.GetComponentInParent<EnemyInstance>();
        if (identity != null && identity.MonsterType == MonsterType.Flying) return true;
        MonsterPathFollower follower = enemy.GetComponentInParent<MonsterPathFollower>();
        return follower != null && follower.IsFlying;
    }

    private static Vector3 FeetPosition(EnemyHealth enemy)
    {
        Collider body = enemy.GetComponentInChildren<Collider>();
        if (body != null && body.enabled)
            return new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z);
        return enemy.transform.position;
    }

    private static float EaseOutCubic(float t)
    {
        float inv = 1f - Mathf.Clamp01(t);
        return 1f - inv * inv * inv;
    }

    private AttackState AggregateState()
    {
        AttackState result = AttackState.Ready;
        int bestPriority = int.MaxValue;
        foreach (MineSlot slot in slots)
        {
            int priority = StatePriority(slot.State);
            if (priority >= bestPriority) continue;
            bestPriority = priority;
            result = slot.State;
        }
        return result;
    }

    private static int StatePriority(AttackState state)
    {
        switch (state)
        {
            case AttackState.Arming: return 0;
            case AttackState.Launching: return 1;
            case AttackState.Detonating: return 2;
            case AttackState.Recovering: return 3;
            case AttackState.Cooldown: return 4;
            default: return 5;
        }
    }

    // ------------------------------------------------------------------ burst fx
    private void SpawnFlash(Vector3 worldPosition)
    {
        BurstFlash flash = BurstFlash.Create(worldPosition, flashRadius, flashLightIntensity, detonateHoldSeconds);
        if (flash != null) flashes.Add(flash);
    }

    private void UpdateFlashes(float deltaTime)
    {
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            BurstFlash flash = flashes[i];
            if (flash == null || !flash.Tick(deltaTime))
            {
                if (flash != null) flash.Destroy();
                flashes.RemoveAt(i);
            }
        }
    }

    private void ResetToReady()
    {
        foreach (MineSlot slot in slots)
        {
            slot.State = AttackState.Ready;
            slot.Elapsed = 0f;
            slot.DamageApplied = false;
            SetMineRise(slot, 0f);
            SetMineVisible(slot, true);
        }
        SetDomePulse(0f);
        for (int i = flashes.Count - 1; i >= 0; i--)
            if (flashes[i] != null) flashes[i].Destroy();
        flashes.Clear();
    }

    /// <summary>One corner canister plus its own reload timer.</summary>
    private sealed class MineSlot
    {
        public readonly int Index;
        public readonly Transform Root;
        public readonly Vector3 RestLocalPosition;

        public AttackState State = AttackState.Ready;
        public float Elapsed;
        public bool DamageApplied;
        public int DetonationCount;

        public MineSlot(int index, Transform root)
        {
            Index = index;
            Root = root;
            RestLocalPosition = root.localPosition;
        }
    }

    /// <summary>
    /// A pooled, additive-looking flash sphere plus a short light. Deliberately
    /// collider-free and shader-light so it works before any VFX pipeline exists.
    /// </summary>
    private sealed class BurstFlash
    {
        private static Mesh sphereMesh;
        private static Material flashMaterial;

        private GameObject root;
        private Transform sphere;
        private Light light;
        private float lifetime;
        private float age;
        private float radius;

        public static BurstFlash Create(Vector3 position, float radius, float lightIntensity, float lifetime)
        {
            Material material = ResolveMaterial();
            if (material == null) return null;
            var flash = new BurstFlash
            {
                lifetime = Mathf.Max(0.02f, lifetime),
                radius = Mathf.Max(0.05f, radius)
            };
            flash.root = new GameObject("Betty7 Burst Flash");
            flash.root.transform.position = position;

            var sphereObject = new GameObject("Flash");
            sphereObject.transform.SetParent(flash.root.transform, false);
            sphereObject.AddComponent<MeshFilter>().sharedMesh = ResolveMesh();
            var renderer = sphereObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            flash.sphere = sphereObject.transform;

            var lightObject = new GameObject("Flash Light");
            lightObject.transform.SetParent(flash.root.transform, false);
            flash.light = lightObject.AddComponent<Light>();
            flash.light.type = LightType.Point;
            flash.light.color = new Color(1f, 0.62f, 0.22f);
            flash.light.range = flash.radius * 6f;
            flash.light.intensity = lightIntensity;
            flash.light.shadows = LightShadows.None;

            flash.Apply(0f);
            return flash;
        }

        /// <summary>Returns false once the flash has expired.</summary>
        public bool Tick(float deltaTime)
        {
            age += Mathf.Max(0f, deltaTime);
            float t = Mathf.Clamp01(age / lifetime);
            Apply(t);
            return t < 1f;
        }

        private void Apply(float t)
        {
            // Grow then collapse so the sphere never pops out at full size.
            float grow = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
            float scale = radius * (0.35f + 0.9f * grow);
            if (sphere != null) sphere.localScale = Vector3.one * scale;
            if (light != null) light.intensity = Mathf.Max(0f, 6f) * (1f - t) * (1f - t);
        }

        public void Destroy()
        {
            // EditMode previews and editor tests also drive this trap; Destroy
            // itself logs an error outside play mode.
            if (root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
            root = null;
        }

        private static Mesh ResolveMesh()
        {
            if (sphereMesh != null) return sphereMesh;
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = probe.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) UnityEngine.Object.Destroy(probe);
            else UnityEngine.Object.DestroyImmediate(probe);
            return sphereMesh;
        }

        private static Material ResolveMaterial()
        {
            if (flashMaterial != null) return flashMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            flashMaterial = new Material(shader) { name = "Betty7 Burst Flash (runtime)" };
            if (flashMaterial.HasProperty("_BaseColor"))
                flashMaterial.SetColor("_BaseColor", new Color(4.5f, 2.1f, 0.5f, 1f));
            if (flashMaterial.HasProperty("_Color"))
                flashMaterial.SetColor("_Color", new Color(4.5f, 2.1f, 0.5f, 1f));
            return flashMaterial;
        }
    }
}
