using System.Collections.Generic;
using UnityEngine;

/// <summary>2x2 wall trap: delayed forward box burst, then a short recharge.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
[DefaultExecutionOrder(1000)]
public sealed class FlamethrowerTrap : MonoBehaviour
{
    public float BaseDirectDamage => directDamage;
    public float BaseBurnDps => burnDamagePerSecond;
    public enum AttackState { Ready, Delay, Spraying, Cooldown }

    // 0.01 mm absorbs floating-point roundoff at closed faces after wall rotation.
    private const float VolumeBoundaryTolerance = 0.00001f;
    // Sub-microsecond tolerance keeps split frames on the same state boundary.
    private const double StateBoundaryTolerance = 0.0000001;

    [SerializeField, Min(0f)] private float directDamage = 20f;
    [SerializeField, Min(0.001f)] private float triggerDelay = 0.1f;
    [SerializeField, Min(0.001f)] private float sprayDuration = 2f;
    [SerializeField, Min(0.001f)] private float cooldownDuration = 1.9f;
    [SerializeField, Min(0.01f)] private float range = 8f;
    [SerializeField, Min(0.01f)] private float width = 2f;
    [SerializeField, Min(0.01f)] private float height = 2f;
    [SerializeField, Min(0.01f)] private float burnDuration = 1f;
    [SerializeField, Min(0f)] private float burnDamagePerSecond = 5f;

    private TrapInstance trap;
    private FlamethrowerFeedback feedback;
    private AttackState state;
    private double stateTime;
    private readonly HashSet<EnemyHealth> hitThisBurst = new HashSet<EnemyHealth>();
    private readonly List<EnemyHealth> attackTargets = new List<EnemyHealth>();

    public AttackState State => state;
    public float Range => range;
    public Vector3 AttackVolumeSize => new Vector3(width, height, range);
    // Attack dimensions are metres, independent of model or parent scale.
    public Matrix4x4 AttackVolumeMatrix => Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
    public float TriggerDelay => triggerDelay;
    public float SprayDuration => sprayDuration;
    public float CooldownDuration => cooldownDuration;

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        feedback = GetComponent<FlamethrowerFeedback>();
        if (feedback == null) feedback = gameObject.AddComponent<FlamethrowerFeedback>();
        feedback.Bind(this);
    }

    private void OnEnable()
    {
        if (trap == null) trap = GetComponent<TrapInstance>();
        if (feedback == null)
        {
            feedback = GetComponent<FlamethrowerFeedback>();
            if (feedback == null) feedback = gameObject.AddComponent<FlamethrowerFeedback>();
        }
        feedback.Bind(this);
        state = AttackState.Ready;
        stateTime = 0f;
        hitThisBurst.Clear();
        attackTargets.Clear();
        feedback.ResetFeedback();
    }
    private void OnDisable()
    {
        state = AttackState.Ready;
        stateTime = 0f;
        hitThisBurst.Clear();
        feedback?.ResetFeedback();
    }
    private void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || trap == null || trap.IsDestroyed
            || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        double remaining = deltaTime;
        while (remaining > StateBoundaryTolerance)
        {
            if (state == AttackState.Ready)
            {
                if (!HasTargetInRange()) return;
                // Commit this burst: losing the trigger target never cancels preheat.
                EnterState(AttackState.Delay);
            }
            double duration = StateDuration();
            double step = System.Math.Min(remaining, System.Math.Max(0d, duration - stateTime));
            if (state == AttackState.Spraying) ApplyBurstDamage();
            if (!isActiveAndEnabled || trap == null || trap.IsDestroyed) return;
            stateTime += step; remaining -= step;
            if (stateTime + StateBoundaryTolerance < duration) continue;
            if (state == AttackState.Delay) EnterState(AttackState.Spraying);
            else if (state == AttackState.Spraying) EnterState(AttackState.Cooldown);
            else if (state == AttackState.Cooldown) EnterState(AttackState.Ready);
            if (!isActiveAndEnabled || trap == null || trap.IsDestroyed) return;
        }
    }

    private void EnterState(AttackState next)
    {
        state = next;
        stateTime = 0d;
        feedback?.SetState(next);
        if (next == AttackState.Delay) hitThisBurst.Clear();
        // Hit at the opening boundary, even when no frame time remains after Delay.
        if (next == AttackState.Spraying) ApplyBurstDamage();
    }

    private float StateDuration() => Mathf.Max(0.001f, state == AttackState.Delay ? triggerDelay
        : state == AttackState.Spraying ? sprayDuration : cooldownDuration);

    private bool HasTargetInRange()
    {
        return CollectAttackTargets(attackTargets) > 0;
    }

    private void ApplyBurstDamage()
    {
        CollectAttackTargets(attackTargets);
        for (int i = 0; i < attackTargets.Count; i++)
        {
            EnemyHealth enemy = attackTargets[i];
            if (!IsInAttackVolume(enemy) || !hitThisBurst.Add(enemy)) continue;
            // Sample on application; the independent burn retains this DPS after upgrades.
            float effectiveDirectDamage = trap.GetEffectiveDamage(directDamage);
            float effectiveBurnDps = trap.GetEffectiveDamage(burnDamagePerSecond);
            enemy.TakeDamage(effectiveDirectDamage, TrapDamageType.Fire);
            FireStatusEffect.Ignite(enemy, burnDuration, effectiveBurnDps);
        }
    }

    /// <summary>Replace results with unique living targets, without requiring any Collider.</summary>
    public int CollectAttackTargets(List<EnemyHealth> results)
    {
        if (results == null) throw new System.ArgumentNullException(nameof(results));
        results.Clear();
        // Query health components once rather than colliders: each health is one damage target,
        // regardless of how many child hitboxes or visual meshes the enemy has.
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (IsInAttackVolume(enemies[i])) results.Add(enemies[i]);
        }
        return results.Count;
    }

    /// <summary>Inclusive forward box bounds, measured from the enemy body/root.</summary>
    public bool IsInAttackVolume(EnemyHealth enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) return false;
        EnemyInstance identity = enemy.GetComponentInParent<EnemyInstance>();
        Vector3 position = identity != null ? identity.transform.position : enemy.transform.position;
        Vector3 local = AttackVolumeMatrix.inverse.MultiplyPoint3x4(position);
        return local.z >= -VolumeBoundaryTolerance && local.z <= range + VolumeBoundaryTolerance
            && Mathf.Abs(local.x) <= width * 0.5f + VolumeBoundaryTolerance
            && Mathf.Abs(local.y) <= height * 0.5f + VolumeBoundaryTolerance;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = AttackVolumeMatrix;
        Gizmos.color = new Color(1f, 0.25f, 0.05f, 0.8f);
        Gizmos.DrawWireCube(Vector3.forward * range * 0.5f, AttackVolumeSize);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * range);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
