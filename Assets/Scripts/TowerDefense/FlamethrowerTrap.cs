using System.Collections.Generic;
using UnityEngine;

/// <summary>2x2 wall trap: delayed forward cone/box burst, then a short recharge.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
[DefaultExecutionOrder(1000)]
public sealed class FlamethrowerTrap : MonoBehaviour
{
    public enum AttackState { Ready, Delay, Spraying, Cooldown }

    [SerializeField, Min(0f)] private float directDamage = 20f;
    [SerializeField, Min(0.001f)] private float triggerDelay = 0.1f;
    [SerializeField, Min(0.001f)] private float sprayDuration = 2f;
    [SerializeField, Min(0.001f)] private float cooldownDuration = 1.9f;
    [SerializeField, Min(0.01f)] private float range = 4f;
    [SerializeField, Min(0.01f)] private float width = 2f;
    [SerializeField, Min(0.01f)] private float height = 2f;
    [SerializeField, Min(0.01f)] private float burnDuration = 1f;
    [SerializeField, Min(0f)] private float burnDamagePerSecond = 5f;

    private TrapInstance trap;
    private AttackState state;
    private float stateTime;
    private readonly HashSet<EnemyHealth> hitThisBurst = new HashSet<EnemyHealth>();

    public AttackState State => state;
    public float Range => range;
    public float SprayDuration => sprayDuration;
    public float CooldownDuration => cooldownDuration;

    private void Awake() => trap = GetComponent<TrapInstance>();
    private void OnEnable() { if (trap == null) trap = GetComponent<TrapInstance>(); state = AttackState.Ready; }
    private void OnDisable() { state = AttackState.Ready; stateTime = 0f; hitThisBurst.Clear(); }
    private void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || trap == null || trap.IsDestroyed) return;
        float remaining = Mathf.Max(0f, deltaTime);
        while (remaining > 0f)
        {
            if (state == AttackState.Ready)
            {
                if (!HasTargetInRange()) return;
                state = AttackState.Delay; stateTime = 0f; hitThisBurst.Clear();
            }
            float duration = StateDuration();
            float step = Mathf.Min(remaining, duration - stateTime);
            if (state == AttackState.Spraying) ApplyBurstDamage();
            stateTime += step; remaining -= step;
            if (stateTime < duration) continue;
            stateTime = 0f;
            if (state == AttackState.Delay) state = AttackState.Spraying;
            else if (state == AttackState.Spraying) state = AttackState.Cooldown;
            else if (state == AttackState.Cooldown) state = AttackState.Ready;
        }
    }

    private float StateDuration() => state == AttackState.Delay ? triggerDelay
        : state == AttackState.Spraying ? sprayDuration : cooldownDuration;

    private bool HasTargetInRange()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++) if (IsInAttackVolume(enemies[i])) return true;
        return false;
    }

    private void ApplyBurstDamage()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyHealth enemy = enemies[i];
            if (!IsInAttackVolume(enemy) || !hitThisBurst.Add(enemy)) continue;
            enemy.TakeDamage(directDamage, TrapDamageType.Fire);
            FireStatusEffect.Ignite(enemy, burnDuration, burnDamagePerSecond);
        }
    }

    private bool IsInAttackVolume(EnemyHealth enemy)
    {
        if (enemy == null || enemy.IsDead) return false;
        EnemyInstance identity = enemy.GetComponentInParent<EnemyInstance>();
        if (identity == null) return false;
        Vector3 local = transform.InverseTransformPoint(enemy.transform.position);
        return local.z >= 0f && local.z <= range && Mathf.Abs(local.x) <= width * 0.5f
            && Mathf.Abs(local.y) <= height * 0.5f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.25f, 0.05f, 0.25f);
        Vector3 center = transform.position + transform.forward * range * 0.5f;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, range));
    }
}
