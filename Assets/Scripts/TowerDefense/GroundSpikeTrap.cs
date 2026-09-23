using System.Collections.Generic;
using UnityEngine;

/// <summary>A walkable, two metre floor trap. Swept tests also catch frame-to-frame crossings.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
[DefaultExecutionOrder(1000)]
public sealed class GroundSpikeTrap : MonoBehaviour
{
    public enum AttackState { Ready, Extending, Holding, Retracting, Cooldown }

    [SerializeField] private Transform spikeGroup;
    [SerializeField, Min(0f)] private float damage = 40f;
    [SerializeField, Min(0.001f)] private float extendSeconds = 0.12f;
    [SerializeField, Min(0.001f)] private float holdSeconds = 0.18f;
    [SerializeField, Min(0.001f)] private float retractSeconds = 0.20f;
    [SerializeField, Min(0.001f)] private float cooldownSeconds = 3f;
    [SerializeField, Min(0f)] private float extensionHeight = 0.8f;
    [SerializeField, Min(0f)] private float floorTolerance = 0.2f;

    private readonly Dictionary<EnemyHealth, Vector3> previousPositions = new Dictionary<EnemyHealth, Vector3>();
    private readonly HashSet<EnemyHealth> hitThisAttack = new HashSet<EnemyHealth>();
    private readonly List<EnemyHealth> staleTargets = new List<EnemyHealth>();
    private TrapInstance trap;
    private Vector3 restPosition;
    private float elapsed;
    public AttackState State { get; private set; }

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        if (spikeGroup == null) spikeGroup = transform.Find("Spikes");
        if (spikeGroup != null) restPosition = spikeGroup.localPosition;
    }

    private void OnEnable()
    {
        if (trap == null) trap = GetComponent<TrapInstance>();
        trap.Destroyed += StopAttack;
    }

    private void OnDisable()
    {
        if (trap != null) trap.Destroyed -= StopAttack;
        StopAttack(trap);
    }

    private void StopAttack(TrapInstance unused)
    {
        State = AttackState.Ready;
        elapsed = 0f;
        previousPositions.Clear();
        hitThisAttack.Clear();
        SetExtension(0f);
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    // Explicit simulation entry point allows deterministic editor verification.
    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || trap == null || trap.IsDestroyed) return;
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        float remaining = Mathf.Max(0f, deltaTime);
        float offset = 0f;
        // Subdivide at state boundaries so long frames never extend the damage window into cooldown.
        do
        {
            if (State == AttackState.Ready)
            {
                if (!HasContact(enemies, deltaTime, offset, remaining, false, out float delay)) break;
                offset += delay;
                remaining -= delay;
                hitThisAttack.Clear();
                State = AttackState.Extending;
                elapsed = 0f;
            }
            float duration = Duration();
            float step = Mathf.Min(remaining, Mathf.Max(0f, duration - elapsed));
            if (State == AttackState.Extending || State == AttackState.Holding)
                HasContact(enemies, deltaTime, offset, step, true, out _);
            if (trap.IsDestroyed || !isActiveAndEnabled) return;
            elapsed += step;
            offset += step;
            remaining -= step;
            UpdatePose(duration);
            if (elapsed < duration) break;
            State = State == AttackState.Cooldown ? AttackState.Ready : (AttackState)((int)State + 1);
            elapsed = 0f;
        } while (remaining > 0f || State == AttackState.Ready);

        staleTargets.Clear();
        foreach (var pair in previousPositions)
            if (pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.IsDead) staleTargets.Add(pair.Key);
        foreach (EnemyHealth enemy in staleTargets) previousPositions.Remove(enemy);
        foreach (EnemyHealth enemy in enemies)
            if (IsGroundTarget(enemy)) previousPositions[enemy] = FeetPosition(enemy);
    }

    private bool HasContact(EnemyHealth[] enemies, float frameTime, float offset, float step, bool applyDamage, out float delay)
    {
        bool found = false;
        delay = step;
        foreach (EnemyHealth enemy in enemies)
        {
            if (trap.IsDestroyed || !isActiveAndEnabled) break;
            if (!IsGroundTarget(enemy)) continue;
            Vector3 current = FeetPosition(enemy);
            if (!previousPositions.TryGetValue(enemy, out Vector3 previous)) previous = current;
            float start = frameTime > 0f ? offset / frameTime : 1f;
            float end = frameTime > 0f ? (offset + step) / frameTime : 1f;
            Vector3 a = transform.InverseTransformPoint(Vector3.Lerp(previous, current, start));
            Vector3 b = transform.InverseTransformPoint(Vector3.Lerp(previous, current, end));
            // Feet must remain on this floor; tall bounds cannot reach down from another storey.
            Bounds volume = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(2f, floorTolerance * 2f, 2f));
            Vector3 segment = b - a;
            float fraction = 0f;
            bool contact = volume.Contains(a);
            if (!contact && segment.sqrMagnitude > 0.000001f
                && volume.IntersectRay(new Ray(a, segment.normalized), out float distance)
                && distance <= segment.magnitude)
            {
                contact = true;
                fraction = distance / segment.magnitude;
            }
            if (!contact && volume.Contains(b)) { contact = true; fraction = 1f; }
            if (!contact) continue;
            found = true;
            delay = Mathf.Min(delay, fraction * step);
            if (applyDamage && hitThisAttack.Add(enemy)) enemy.TakeDamage(damage);
        }
        return found;
    }

    private static bool IsGroundTarget(EnemyHealth enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) return false;
        EnemyInstance identity = enemy.GetComponentInParent<EnemyInstance>();
        MonsterPathFollower follower = enemy.GetComponentInParent<MonsterPathFollower>();
        return (identity != null || follower != null)
            && (identity == null || identity.MonsterType != MonsterType.Flying)
            && (follower == null || !follower.IsFlying);
    }

    private static Vector3 FeetPosition(EnemyHealth enemy)
    {
        Collider body = enemy.GetComponentInChildren<Collider>();
        if (body != null && body.enabled)
            return new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z);
        return enemy.transform.position;
    }

    private float Duration()
    {
        switch (State)
        {
            case AttackState.Extending: return Mathf.Max(0.001f, extendSeconds);
            case AttackState.Holding: return Mathf.Max(0.001f, holdSeconds);
            case AttackState.Retracting: return Mathf.Max(0.001f, retractSeconds);
            default: return Mathf.Max(0.001f, cooldownSeconds);
        }
    }

    private void UpdatePose(float duration)
    {
        float fraction = Mathf.Clamp01(elapsed / duration);
        SetExtension(State == AttackState.Extending ? fraction : State == AttackState.Holding ? 1f
            : State == AttackState.Retracting ? 1f - fraction : 0f);
    }

    private void SetExtension(float fraction)
    {
        if (spikeGroup != null) spikeGroup.localPosition = restPosition + Vector3.up * (extensionHeight * fraction);
    }
}
