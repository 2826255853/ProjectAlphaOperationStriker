using UnityEngine;

/// <summary>
/// The developer-authored destination that enemies are trying to reach.
/// Move this GameObject in the scene to choose the core's position.
/// </summary>
[AddComponentMenu("Tower Defense/Enemy Core")]
public sealed class EnemyCore : MonoBehaviour
{
    [SerializeField, Min(1f), Tooltip("核心初始血量。")]
    private float maxHealth = 30f;
    [SerializeField, Min(0f), Tooltip("地面怪物进入核心造成的伤害。")]
    private float groundDamage = 2f;
    [SerializeField, Min(0f), Tooltip("飞行怪物进入核心造成的伤害。")]
    private float flyingDamage = 1f;
    [SerializeField, Tooltip("Optional grid used by enemies travelling to this core. If empty, the spawn point's grid is used.")]
    private MonsterPathGrid pathGrid;

    [SerializeField, Min(0.01f), Tooltip("Maximum horizontal distance from the core at which an enemy is considered to have arrived.")]
    private float arrivalRadius = 0.35f;

    public MonsterPathGrid PathGrid => pathGrid;
    public float ArrivalRadius => arrivalRadius;
    public Vector3 Position => transform.position;
    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDestroyed => CurrentHealth <= 0f;
    public event System.Action<EnemyCore> HealthChanged;
    public event System.Action<EnemyCore> Destroyed;

    private void Awake() => CurrentHealth = Mathf.Max(1f, maxHealth);

    public void TakeDamage(MonsterType monsterType)
    {
        if (IsDestroyed) return;
        float damage = monsterType == MonsterType.Flying ? flyingDamage : groundDamage;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        HealthChanged?.Invoke(this);
        if (CurrentHealth <= 0f) Destroyed?.Invoke(this);
    }

    public bool IsWithinArrivalRange(Vector3 worldPosition)
    {
        Vector3 delta = worldPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= arrivalRadius * arrivalRadius;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        groundDamage = Mathf.Max(0f, groundDamage);
        flyingDamage = Mathf.Max(0f, flyingDamage);
        arrivalRadius = Mathf.Max(0.01f, arrivalRadius);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.85f, 0.15f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, arrivalRadius);
        Gizmos.DrawRay(transform.position, transform.up * 0.75f);
    }
}
