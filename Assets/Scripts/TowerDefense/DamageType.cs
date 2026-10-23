/// <summary>
/// Single authoritative damage category shared by player weapons, traps, status effects and enemy resistances.
/// Every damage event carries exactly one category; composite attacks pick their primary category at the source.
/// Numeric values are part of the serialized contract, so append new entries instead of reordering.
/// </summary>
public enum DamageType
{
    Fire = 0,           // direct flame and burning damage over time
    Electric = 1,       // electrical damage; no control effect is implied
    Frost = 2,          // frost damage; no slow or freeze effect is implied
    Light = 3,          // light damage; blinding is a separate status, not a resistance
    RangedPhysical = 4, // guns, crossbows and other ranged physical attacks
    MeleePhysical = 5,  // melee, ground spikes and other close-range physical attacks
    Explosive = 6       // explosions and blast area damage
}

/// <summary>Read-only helpers for enumerating and labelling <see cref="DamageType"/> values.</summary>
public static class DamageTypeCatalog
{
    /// <summary>All seven categories in declaration order; do not mutate.</summary>
    public static readonly DamageType[] All =
    {
        DamageType.Fire,
        DamageType.Electric,
        DamageType.Frost,
        DamageType.Light,
        DamageType.RangedPhysical,
        DamageType.MeleePhysical,
        DamageType.Explosive
    };

    public const int Count = 7;

    public static bool IsDefined(DamageType type)
    {
        return (int)type >= 0 && (int)type < Count;
    }

    /// <summary>Player-facing label used by resistance configuration and future combat feedback.</summary>
    public static string GetDisplayName(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire: return "火";
            case DamageType.Electric: return "电";
            case DamageType.Frost: return "冰冻";
            case DamageType.Light: return "光";
            case DamageType.RangedPhysical: return "远程物理";
            case DamageType.MeleePhysical: return "近程物理";
            case DamageType.Explosive: return "爆炸";
            default: return type.ToString();
        }
    }
}
