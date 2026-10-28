using System;

/// <summary>Level-four direction. Levels one through three always use None.</summary>
public enum TrapUpgradeDirection
{
    None = 0,
    Normal = 1,
    Branch = 2
}

/// <summary>Immutable, per-TrapId state owned by one run's upgrade rules service.</summary>
public readonly struct TrapUpgradeState : IEquatable<TrapUpgradeState>
{
    public static TrapUpgradeState Initial => new TrapUpgradeState(1);

    public int Level { get; }
    public TrapUpgradeDirection Direction { get; }
    public string BranchId { get; }

    public TrapUpgradeState(int level, TrapUpgradeDirection direction = TrapUpgradeDirection.None,
        string branchId = null)
    {
        if (level < 1 || level > 4)
            throw new ArgumentOutOfRangeException(nameof(level), "Trap levels must be between 1 and 4.");
        if (level < 4 && direction != TrapUpgradeDirection.None ||
            level == 4 && direction != TrapUpgradeDirection.Normal && direction != TrapUpgradeDirection.Branch)
            throw new ArgumentException("Only level four has a direction, and its direction is required.");
        if (direction == TrapUpgradeDirection.Branch && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("A branch direction requires a branch ID.", nameof(branchId));
        if (direction != TrapUpgradeDirection.Branch && !string.IsNullOrEmpty(branchId))
            throw new ArgumentException("Only branch upgrades may carry a branch ID.", nameof(branchId));

        Level = level;
        Direction = direction;
        BranchId = branchId ?? string.Empty;
    }

    public bool Equals(TrapUpgradeState other) => Level == other.Level && Direction == other.Direction &&
        string.Equals(BranchId, other.BranchId, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is TrapUpgradeState other && Equals(other);
    public override int GetHashCode() => (Level * 397 ^ (int)Direction) * 397 ^
        (BranchId == null ? 0 : StringComparer.Ordinal.GetHashCode(BranchId));
}

/// <summary>Independent multipliers, always applied to the original level-one values.</summary>
public readonly struct TrapUpgradeMultipliers : IEquatable<TrapUpgradeMultipliers>
{
    public float Damage { get; }
    public float Repair { get; }
    public float MaxHealth { get; }

    public TrapUpgradeMultipliers(float damage, float repair, float maxHealth)
    {
        if (!IsPositiveFinite(damage) || !IsPositiveFinite(repair) || !IsPositiveFinite(maxHealth))
            throw new ArgumentOutOfRangeException(nameof(damage), "Growth multipliers must be positive and finite.");
        Damage = damage;
        Repair = repair;
        MaxHealth = maxHealth;
    }

    internal static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);
    public bool Equals(TrapUpgradeMultipliers other) => Damage.Equals(other.Damage) &&
        Repair.Equals(other.Repair) && MaxHealth.Equals(other.MaxHealth);
    public override bool Equals(object obj) => obj is TrapUpgradeMultipliers other && Equals(other);
    public override int GetHashCode() => (Damage.GetHashCode() * 397 ^ Repair.GetHashCode()) * 397 ^ MaxHealth.GetHashCode();
}
