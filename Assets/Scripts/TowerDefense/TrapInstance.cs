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
    // Keep this serialized field as the authored level-one value; never write upgraded health into it.
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    private TrapUpgradeState upgradeState = TrapUpgradeState.Initial;
    private TrapUpgradeMultipliers upgradeMultipliers = TrapUpgradeCatalog.DefaultMultipliers(1);
    private bool healthInitialized;

    private static readonly HashSet<TrapInstance> activeTraps = new HashSet<TrapInstance>();
    public static IReadOnlyCollection<TrapInstance> ActiveTraps => activeTraps;
    internal TrapPlacementGrid OwnerGrid { get; set; }
    public float BaseMaxHealth => maxHealth;
    public float MaxHealth => maxHealth * upgradeMultipliers.MaxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDestroyed => CurrentHealth <= 0f;
    public TrapUpgradeState UpgradeState => upgradeState;
    public int UpgradeLevel => upgradeState.Level;
    public TrapUpgradeDirection UpgradeDirection => upgradeState.Direction;
    public float DamageMultiplier => upgradeMultipliers.Damage;
    public float RepairMultiplier => upgradeMultipliers.Repair;
    public float MaxHealthMultiplier => upgradeMultipliers.MaxHealth;
    public TrapUpgradeMultipliers UpgradeMultipliers => upgradeMultipliers;
    public float GetEffectiveDamage(float baseDamage) => baseDamage * DamageMultiplier;
    public float GetEffectiveRepair(float baseRepair) => baseRepair * RepairMultiplier;
    public event Action<TrapInstance> Damaged;
    public event Action<TrapInstance> Destroyed;
    /// <summary>Refresh hook; durability/combat adapters read the service's committed state here.</summary>
    public event Action<TrapInstance, TrapUpgradeState> UpgradeStateChanged;

    internal void RefreshUpgradeState(TrapUpgradeState state, TrapUpgradeMultipliers multipliers,
        Func<bool> isCurrent)
    {
        if (this == null || !isCurrent()) return;
        ApplyUpgradeState(state, multipliers, false);
        if (UpgradeStateChanged == null) return;
        foreach (Action<TrapInstance, TrapUpgradeState> listener in UpgradeStateChanged.GetInvocationList())
        {
            if (this == null || !isCurrent()) break;
            try { listener(this, state); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    public TrapDefinition Definition => definition;
    public Vector2Int OriginCell => originCell;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => activeTraps.Clear();

    private void Awake()
    {
        if (!healthInitialized)
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            healthInitialized = true;
        }
        SynchronizeUpgradeState(false);
    }

    private void OnEnable()
    {
        activeTraps.Add(this);
        SynchronizeUpgradeState(false);
    }
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

    /// <summary>Restores a living trap's health without emitting damage/destruction events.</summary>
    public float Repair(float amount)
    {
        if (IsDestroyed || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
        float previous = CurrentHealth;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        return CurrentHealth - previous;
    }

    internal void Initialize(TrapDefinition definition, Vector2Int originCell)
    {
        this.definition = definition;
        this.originCell = originCell;
        gameObject.name = definition == null ? "Trap" : definition.DisplayName;
        SynchronizeUpgradeState(true);
    }

    private void SynchronizeUpgradeState(bool fillHealth)
    {
        if (definition == null || !TrapUpgradeCatalog.IsValidId(definition.TrapId))
        {
            upgradeState = TrapUpgradeState.Initial;
            upgradeMultipliers = TrapUpgradeCatalog.DefaultMultipliers(1);
            if (fillHealth || !healthInitialized) CurrentHealth = MaxHealth;
            healthInitialized = true;
            return;
        }

        EconomyManager wallet = EconomyManager.Instance;
        TrapUpgradeService service = wallet != null && wallet.UpgradeService != null &&
            !wallet.UpgradeService.IsDisposed ? wallet.UpgradeService : TrapUpgradeRuntime.GetOrCreate();
        ApplyUpgradeState(service.GetState(definition.TrapId), service.GetMultipliers(definition.TrapId), fillHealth);
    }

    private void ApplyUpgradeState(TrapUpgradeState nextState, TrapUpgradeMultipliers nextMultipliers,
        bool fillHealth)
    {
        float previousMaxHealth = MaxHealth;
        bool wasInitialized = healthInitialized;
        bool wasDestroyed = wasInitialized && CurrentHealth <= 0f;
        float healthRatio = !wasInitialized || previousMaxHealth <= 0f
            ? 1f : Mathf.Clamp01(CurrentHealth / previousMaxHealth);
        bool changed = !upgradeState.Equals(nextState) || !upgradeMultipliers.Equals(nextMultipliers);

        upgradeState = nextState;
        upgradeMultipliers = nextMultipliers;

        if (wasDestroyed) CurrentHealth = 0f;
        else if (fillHealth || !wasInitialized) CurrentHealth = MaxHealth;
        else if (changed) CurrentHealth = MaxHealth * healthRatio;
        else CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        healthInitialized = true;
    }
}
