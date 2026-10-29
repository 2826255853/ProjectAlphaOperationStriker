using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One authoritative configuration per TrapId, independent of menu presentation.</summary>
[CreateAssetMenu(menuName = "Tower Defense/Trap Upgrade Catalog", fileName = "TrapUpgradeCatalog")]
public sealed class TrapUpgradeCatalog : ScriptableObject
{
    public const string ResourcePath = "TrapUpgradeCatalog";

    [Serializable]
    public sealed class GrowthOverride
    {
        [Range(2, 4)] public int level = 2;
        public bool overrideDamage;
        [Min(0.0001f)] public float damage = 1f;
        public bool overrideRepair;
        [Min(0.0001f)] public float repair = 1f;
        public bool overrideMaxHealth;
        [Min(0.0001f)] public float maxHealth = 1f;
    }

    [Serializable]
    public sealed class CostOverride
    {
        [Range(2, 4)] public int targetLevel = 2;
        public bool overrideMultiplier;
        [Min(0)] public double multiplier = 0.5d;
        public bool overrideFixedCost;
        [Min(0)] public int fixedCost;
    }

    [Serializable]
    public sealed class Branch
    {
        public string branchId;
        public string displayName;
        [TextArea] public string description;
        [Tooltip("Configuration readiness only. An available effect adapter is also required before selection.")]
        public bool integrated;
        public bool overrideCostMultiplier;
        [Min(0)] public double costMultiplier = 1.5d;
        public bool overrideFixedCost;
        [Min(0)] public int fixedCost;
        public bool overrideDamage;
        [Min(0.0001f)] public float damage = 2f;
        public bool overrideRepair;
        [Min(0.0001f)] public float repair = 2f;
        public bool overrideMaxHealth;
        [Min(0.0001f)] public float maxHealth = 2f;
    }

    [Serializable]
    public sealed class Entry
    {
        public string trapId;
        public List<GrowthOverride> growth = new List<GrowthOverride>();
        public List<CostOverride> costs = new List<CostOverride>();
        public List<Branch> branches = new List<Branch>();
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();
    public IReadOnlyList<Entry> Entries => entries.AsReadOnly();

    public static bool IsValidId(string id) => !string.IsNullOrWhiteSpace(id) &&
        string.Equals(id, id.Trim(), StringComparison.Ordinal);

    public static TrapUpgradeMultipliers DefaultMultipliers(int level)
    {
        if (level < 1 || level > 4) throw new ArgumentOutOfRangeException(nameof(level));
        float value = level == 1 ? 1f : level == 2 ? 1.25f : level == 3 ? 1.5f : 2f;
        return new TrapUpgradeMultipliers(value, value, value);
    }

    public static double DefaultCostMultiplier(int targetLevel)
    {
        if (targetLevel < 2 || targetLevel > 4) throw new ArgumentOutOfRangeException(nameof(targetLevel));
        return (targetLevel - 1) * 0.5;
    }

    /// <summary>Reject malformed authored data; never clamp or silently merge duplicate IDs.</summary>
    public void Validate()
    {
        if (entries == null) throw new InvalidOperationException("Upgrade catalog entries cannot be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Entry entry in entries)
        {
            if (entry == null || !IsValidId(entry.trapId))
                throw new InvalidOperationException("Upgrade catalog contains an empty or padded TrapId.");
            if (!ids.Add(entry.trapId))
                throw new InvalidOperationException($"Duplicate upgrade configuration for TrapId '{entry.trapId}'.");
            if (entry.growth == null || entry.costs == null || entry.branches == null)
                throw new InvalidOperationException($"Null configuration list for '{entry.trapId}'.");
            var levels = new HashSet<int>();
            foreach (GrowthOverride value in entry.growth)
            {
                if (value == null || value.level < 2 || value.level > 4 || !levels.Add(value.level))
                    throw new InvalidOperationException($"Invalid or duplicate growth level for '{entry.trapId}'.");
                ValidateGrowth(value.overrideDamage, value.damage);
                ValidateGrowth(value.overrideRepair, value.repair);
                ValidateGrowth(value.overrideMaxHealth, value.maxHealth);
            }
            levels.Clear();
            foreach (CostOverride value in entry.costs)
            {
                if (value == null || value.targetLevel < 2 || value.targetLevel > 4 || !levels.Add(value.targetLevel))
                    throw new InvalidOperationException($"Invalid or duplicate cost level for '{entry.trapId}'.");
                ValidateCost(value.overrideMultiplier, value.multiplier, value.overrideFixedCost, value.fixedCost);
            }
            var branches = new HashSet<string>(StringComparer.Ordinal);
            foreach (Branch branch in entry.branches)
            {
                if (branch == null || !IsValidId(branch.branchId) || !branches.Add(branch.branchId) ||
                    string.IsNullOrWhiteSpace(branch.displayName))
                    throw new InvalidOperationException($"Invalid or duplicate branch for '{entry.trapId}'.");
                ValidateCost(branch.overrideCostMultiplier, branch.costMultiplier, branch.overrideFixedCost, branch.fixedCost);
                ValidateGrowth(branch.overrideDamage, branch.damage);
                ValidateGrowth(branch.overrideRepair, branch.repair);
                ValidateGrowth(branch.overrideMaxHealth, branch.maxHealth);
            }
        }
    }

    private static void ValidateGrowth(bool enabled, float value)
    {
        if (enabled && !TrapUpgradeMultipliers.IsPositiveFinite(value))
            throw new InvalidOperationException("Enabled growth overrides must be positive and finite.");
    }

    private static void ValidateCost(bool scaled, double multiplier, bool fixedPrice, int price)
    {
        if (scaled && fixedPrice)
            throw new InvalidOperationException("Choose a fixed upgrade price or a multiplier, not both.");
        if (scaled && (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0d) ||
            fixedPrice && price < 0)
            throw new InvalidOperationException("Upgrade costs must be nonnegative and finite.");
    }

    /// <summary>Duplicates across Resources and TrapDefinitions must agree on the base price.</summary>
    public static void ValidateDefinitions(IEnumerable<TrapDefinition> definitions)
    {
        if (definitions == null) throw new ArgumentNullException(nameof(definitions));
        var prices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TrapDefinition definition in definitions)
        {
            if (definition == null || !IsValidId(definition.TrapId) || definition.Cost < 0)
                throw new InvalidOperationException("Trap definitions require a valid TrapId and a nonnegative base price.");
            if (prices.TryGetValue(definition.TrapId, out int price) && price != definition.Cost)
                throw new InvalidOperationException($"Conflicting base prices for TrapId '{definition.TrapId}': {price} / {definition.Cost}.");
            prices[definition.TrapId] = definition.Cost;
        }
    }

    private Entry FindEntry(string trapId)
    {
        if (!IsValidId(trapId)) throw new ArgumentException("A valid TrapId is required.", nameof(trapId));
        Validate();
        return entries.Find(value => string.Equals(value.trapId, trapId, StringComparison.Ordinal));
    }

    public TrapUpgradeMultipliers GetMultipliers(string trapId, TrapUpgradeState state)
    {
        // Reconstructing also rejects default(TrapUpgradeState) and invalid levels.
        _ = new TrapUpgradeState(state.Level, state.Direction, state.BranchId);
        Entry entry = FindEntry(trapId);
        if (state.Direction == TrapUpgradeDirection.Branch)
        {
            Branch branch = FindBranch(entry, state.BranchId);
            if (branch == null) throw new InvalidOperationException("Unknown branch direction.");
            return new TrapUpgradeMultipliers(branch.overrideDamage ? branch.damage : 2f,
                branch.overrideRepair ? branch.repair : 2f, branch.overrideMaxHealth ? branch.maxHealth : 2f);
        }
        TrapUpgradeMultipliers defaults = DefaultMultipliers(state.Level);
        GrowthOverride value = entry?.growth.Find(item => item.level == state.Level);
        return value == null ? defaults : new TrapUpgradeMultipliers(
            value.overrideDamage ? value.damage : defaults.Damage,
            value.overrideRepair ? value.repair : defaults.Repair,
            value.overrideMaxHealth ? value.maxHealth : defaults.MaxHealth);
    }

    public Branch GetBranch(string trapId, string branchId) => FindBranch(FindEntry(trapId), branchId);

    /// <summary>Returns the configured four-level branches for menu presentation.</summary>
    public IReadOnlyList<Branch> GetBranches(string trapId)
    {
        Entry entry = FindEntry(trapId);
        return entry == null ? Array.Empty<Branch>() : entry.branches.AsReadOnly();
    }

    private static Branch FindBranch(Entry entry, string branchId) =>
        entry?.branches.Find(value => string.Equals(value.branchId, branchId, StringComparison.Ordinal));

    public int GetCost(string trapId, int baseCost, TrapUpgradeState target)
    {
        if (baseCost < 0) throw new ArgumentOutOfRangeException(nameof(baseCost), "Negative placement prices are invalid.");
        _ = new TrapUpgradeState(target.Level, target.Direction, target.BranchId);
        double multiplier = DefaultCostMultiplier(target.Level);
        int? fixedCost = null;
        Entry entry = FindEntry(trapId);
        if (target.Direction == TrapUpgradeDirection.Branch)
        {
            Branch branch = FindBranch(entry, target.BranchId);
            if (branch == null) throw new InvalidOperationException("Unknown branch direction.");
            if (branch.overrideCostMultiplier) multiplier = branch.costMultiplier;
            if (branch.overrideFixedCost) fixedCost = branch.fixedCost;
        }
        else
        {
            CostOverride value = entry?.costs.Find(item => item.targetLevel == target.Level);
            if (value != null && value.overrideMultiplier) multiplier = value.multiplier;
            if (value != null && value.overrideFixedCost) fixedCost = value.fixedCost;
        }
        return CalculateCost(baseCost, multiplier, fixedCost);
    }

    public static int CalculateCost(int baseCost, double multiplier, int? fixedCost = null)
    {
        if (baseCost < 0 || double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0 || fixedCost < 0)
            throw new ArgumentOutOfRangeException(nameof(baseCost), "Invalid upgrade price configuration.");
        if (fixedCost.HasValue) return Math.Max(1, fixedCost.Value);
        if (baseCost == 0 || multiplier == 0d) return 1;
        // Decimal avoids rounding 100 * 0.07 up to 8 due to binary floating-point noise.
        if (multiplier > (double)int.MaxValue / baseCost)
            throw new OverflowException("Upgrade price exceeds Int32.MaxValue.");
        decimal cost = Math.Max(1m, decimal.Ceiling(baseCost * (decimal)multiplier));
        if (cost > int.MaxValue) throw new OverflowException("Upgrade price exceeds Int32.MaxValue.");
        return (int)cost;
    }
}
