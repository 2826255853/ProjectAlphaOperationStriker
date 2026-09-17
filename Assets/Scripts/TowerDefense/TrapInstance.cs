using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TrapInstance : MonoBehaviour
{
    // Serialized so editor-authored traps retain their identity after a domain
    // reload or when the scene is reopened. The grid rebuilds its occupancy map
    // from these values on enable.
    [SerializeField] private TrapDefinition definition;
    [SerializeField] private Vector2Int originCell;
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    private static readonly HashSet<TrapInstance> activeTraps = new HashSet<TrapInstance>();
    public static IReadOnlyCollection<TrapInstance> ActiveTraps => activeTraps;
    internal TrapPlacementGrid OwnerGrid { get; set; }
    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDestroyed => CurrentHealth <= 0f;
    public event Action<TrapInstance> Damaged;
    public event Action<TrapInstance> Destroyed;

    public TrapDefinition Definition => definition;
    public Vector2Int OriginCell => originCell;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => activeTraps.Clear();

    private void Awake() => CurrentHealth = Mathf.Max(1f, maxHealth);
    private void OnEnable() => activeTraps.Add(this);
    private void OnDisable() => activeTraps.Remove(this);
    private void OnDestroy()
    {
        if (OwnerGrid != null) OwnerGrid.ForgetTrap(this);
    }

    public void TakeDamage(float amount)
    {
        if (IsDestroyed || amount <= 0f) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Damaged?.Invoke(this);
        if (!IsDestroyed) return;
        activeTraps.Remove(this);
        Destroyed?.Invoke(this);
        if (OwnerGrid == null || !OwnerGrid.RemoveTrap(this)) Destroy(gameObject);
    }

    internal void Initialize(TrapDefinition definition, Vector2Int originCell)
    {
        this.definition = definition;
        this.originCell = originCell;
        gameObject.name = definition == null ? "Trap" : definition.DisplayName;
    }
}
