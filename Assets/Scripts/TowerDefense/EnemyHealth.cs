using System;
using UnityEngine;

/// <summary>Runtime health for enemies that can be damaged by player weapons.</summary>
[DisallowMultipleComponent]
public sealed class EnemyHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("伤害抗性（%）：正数减伤，负数易伤，0 不变")]
    [SerializeField, Tooltip("火：火焰直击与燃烧持续伤害")] private float fireResistancePercent;
    [SerializeField, Tooltip("电：电属性伤害；不改变控制效果")] private float electricResistancePercent;
    [SerializeField, Tooltip("冰冻：冰冻类别伤害；不改变减速或冻结")] private float frostResistancePercent;
    [SerializeField, Tooltip("光：光属性伤害；不改变致盲")] private float lightResistancePercent;
    [SerializeField, Tooltip("远程物理：枪械、弓弩等远程实体攻击")] private float rangedPhysicalResistancePercent;
    [SerializeField, Tooltip("近程物理：近战、地刺等近距离实体攻击")] private float meleePhysicalResistancePercent;
    [SerializeField, Tooltip("爆炸：爆炸冲击与爆炸范围伤害")] private float explosiveResistancePercent;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<EnemyHealth> Damaged;
    /// <summary>
    /// Raised for trap damage, including the legacy uncategorized <see cref="TrapDamageType.Physical"/> value.
    /// The amount is the health actually deducted by that request (never the pre-resistance request amount).
    /// </summary>
    public event Action<EnemyHealth, float, TrapDamageType> TrapDamaged;
    /// <summary>
    /// Raised for damage that carries an authoritative <see cref="DamageType"/>.
    /// The amount is the health actually deducted after resistance, so subscribers never see the requested value.
    /// </summary>
    public event Action<EnemyHealth, float, DamageType> TypedDamaged;
    public event Action<EnemyHealth> Died;

    private bool deathRaised;

    private void Awake()
    {
        CurrentHealth = Mathf.Max(1f, maxHealth);
    }

    /// <summary>
    /// Applies uncategorized damage exactly as requested. Resistance is intentionally not applied here:
    /// legacy callers cannot declare a category, so no category is guessed on their behalf.
    /// </summary>
    public void TakeDamage(float amount)
    {
        ApplyDamage(amount, false, default, false, default);
    }

    /// <summary>
    /// Applies damage under the authoritative category: resistance is read from this instance,
    /// the final damage is <c>max(0, 1 - resistancePercent / 100)</c> scaled, then the existing
    /// health, death-deduplication and kill flow runs once.
    /// </summary>
    public void TakeDamage(float amount, DamageType type)
    {
        ApplyDamage(amount, true, type, false, default);
    }

    /// <summary>
    /// Compatibility entry for existing trap callers. Known categories forward through the fixed
    /// mapping in <see cref="TrapDamageTypeCompatibility"/> and settle resistance once;
    /// the legacy <see cref="TrapDamageType.Physical"/> value stays uncategorized raw damage.
    /// </summary>
    public void TakeDamage(float amount, TrapDamageType type)
    {
        if (type.TryGetDamageType(out DamageType mappedType))
        {
            ApplyDamage(amount, true, mappedType, true, type);
        }
        else
        {
            ApplyDamage(amount, false, default, true, type);
        }
    }

    /// <summary>
    /// Final damage this instance would take for a categorized request, without changing health or events.
    /// Returns zero for non-positive requests and for categories fully resisted or beyond.
    /// </summary>
    public float CalculateDamage(float requestedAmount, DamageType type)
    {
        if (requestedAmount <= 0f || float.IsNaN(requestedAmount))
        {
            return 0f;
        }

        float damage = requestedAmount * GetDamageMultiplier(type);
        return float.IsNaN(damage) ? 0f : Mathf.Max(0f, damage);
    }

    /// <summary>Restores this instance to full health when reused from a pool.</summary>
    public void ResetHealth()
    {
        deathRaised = false;
        CurrentHealth = Mathf.Max(1f, maxHealth);
        GetComponent<FireStatusEffect>()?.Clear();
        // Resistances are prototype configuration and deliberately survive a health reset.
    }

    /// <summary>
    /// Signed resistance percentage for one category: +30 means 70% damage taken, -30 means 130%.
    /// Unconfigured categories and undefined enum values return 0.
    /// </summary>
    public float GetResistancePercent(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire: return fireResistancePercent;
            case DamageType.Electric: return electricResistancePercent;
            case DamageType.Frost: return frostResistancePercent;
            case DamageType.Light: return lightResistancePercent;
            case DamageType.RangedPhysical: return rangedPhysicalResistancePercent;
            case DamageType.MeleePhysical: return meleePhysicalResistancePercent;
            case DamageType.Explosive: return explosiveResistancePercent;
            default: return 0f;
        }
    }

    /// <summary>
    /// Authoring and test hook for the per-instance resistance configuration.
    /// Does not modify health, events or the damage pipeline.
    /// </summary>
    public void SetResistancePercent(DamageType type, float percent)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent))
        {
            return;
        }

        switch (type)
        {
            case DamageType.Fire: fireResistancePercent = percent; break;
            case DamageType.Electric: electricResistancePercent = percent; break;
            case DamageType.Frost: frostResistancePercent = percent; break;
            case DamageType.Light: lightResistancePercent = percent; break;
            case DamageType.RangedPhysical: rangedPhysicalResistancePercent = percent; break;
            case DamageType.MeleePhysical: meleePhysicalResistancePercent = percent; break;
            case DamageType.Explosive: explosiveResistancePercent = percent; break;
        }
    }

    /// <summary>
    /// Damage multiplier implied by the resistance: max(0, 1 - resistancePercent / 100).
    /// </summary>
    public float GetDamageMultiplier(DamageType type)
    {
        return Mathf.Max(0f, 1f - GetResistancePercent(type) / 100f);
    }

    /// <summary>
    /// Single settlement path shared by every damage entry point.
    /// Applies resistance at most once, deducts health, publishes the actually deducted amount
    /// and runs the existing death-deduplication flow.
    /// </summary>
    /// <returns>Health actually deducted; zero when the request was ignored or fully resisted.</returns>
    private float ApplyDamage(float requestedAmount, bool categorized, DamageType damageType, bool legacyTyped, TrapDamageType legacyType)
    {
        if (deathRaised || requestedAmount <= 0f || float.IsNaN(requestedAmount))
        {
            return 0f;
        }

        float finalDamage = categorized ? requestedAmount * GetDamageMultiplier(damageType) : requestedAmount;
        if (float.IsNaN(finalDamage) || finalDamage <= 0f)
        {
            // Fully resisted (resistance >= 100%) or a non-finite scale: no health change, no events, no death.
            return 0f;
        }

        float previousHealth = CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - finalDamage);
        float appliedDamage = previousHealth - CurrentHealth;

        Debug.Log($"[EnemyHealth] {gameObject.name} 受到伤害: {appliedDamage:0.##}，剩余血量: {CurrentHealth:0.##}/{MaxHealth:0.##}", this);
        Damaged?.Invoke(this);

        if (legacyTyped)
        {
            TrapDamaged?.Invoke(this, appliedDamage, legacyType);
        }

        if (categorized)
        {
            TypedDamaged?.Invoke(this, appliedDamage, damageType);
        }

        if (CurrentHealth > 0f)
        {
            return appliedDamage;
        }

        deathRaised = true;
        GetComponent<FireStatusEffect>()?.Clear();
        Died?.Invoke(this);
        Destroy(gameObject);
        return appliedDamage;
    }
}
