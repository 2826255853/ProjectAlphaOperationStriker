using UnityEngine;

/// <summary>Enemy-owned continuous burn, independent of the trap that ignited it.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public sealed class FireStatusEffect : MonoBehaviour
{
    private EnemyHealth health;
    public float RemainingSeconds { get; private set; }
    public float DamagePerSecond { get; private set; }
    public bool IsBurning => RemainingSeconds > 0f;

    private void Awake() => health = GetComponent<EnemyHealth>();
    private void Update() => Tick(Time.deltaTime);
    private void OnDisable() => Clear();

    public void Tick(float deltaTime)
    {
        if (health == null) health = GetComponent<EnemyHealth>();
        if (!isActiveAndEnabled || health == null || !health.isActiveAndEnabled || health.IsDead)
        {
            Clear();
            return;
        }
        float step = Mathf.Min(Mathf.Max(0f, deltaTime), RemainingSeconds);
        RemainingSeconds = Mathf.Max(0f, RemainingSeconds - step);
        // Integrate partial seconds, but never deal damage beyond expiry on a long frame.
        if (step > 0f) health.TakeDamage(DamagePerSecond * step, TrapDamageType.Fire);
        if (!IsBurning) DamagePerSecond = 0f;
    }

    public void Clear() { RemainingSeconds = 0f; DamagePerSecond = 0f; }

    public static void Ignite(EnemyHealth target, float duration = 1f, float damagePerSecond = 5f)
    {
        if (target == null || !target.isActiveAndEnabled || target.IsDead
            || duration <= 0f || damagePerSecond <= 0f) return;
        FireStatusEffect burn = target.GetComponent<FireStatusEffect>()
            ?? target.gameObject.AddComponent<FireStatusEffect>();
        burn.health = target;
        // Re-ignition refreshes one enemy-owned burn; different sources never stack.
        burn.RemainingSeconds = duration;
        burn.DamagePerSecond = Mathf.Max(burn.DamagePerSecond, damagePerSecond);
    }
}
