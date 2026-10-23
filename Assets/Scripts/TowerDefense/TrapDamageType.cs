/// <summary>Type of trap-sourced damage; kept as a compatibility layer over the authoritative DamageType.</summary>
public enum TrapDamageType
{
    Physical = 0,  // legacy compatibility value; uncategorized, must not be guessed into a physical subtype
    Fire = 1,      // direct flame and burning
    Explosive = 2, // missile impact and blast
    Energy = 3,    // reserved for electrical/energy traps
    Bullet = 4,    // sentry bullets
    Melee = 5      // ground spikes and other contact attacks
}

/// <summary>
/// Fixed one-way mapping from the legacy trap enum to <see cref="DamageType"/>.
/// The legacy <see cref="TrapDamageType.Physical"/> value is intentionally uncategorized: it cannot tell
/// ranged from melee, so it keeps raw damage behaviour instead of being mapped to either physical subtype.
/// </summary>
public static class TrapDamageTypeCompatibility
{
    /// <summary>
    /// Resolves the authoritative damage category for a legacy trap category.
    /// Returns false for <see cref="TrapDamageType.Physical"/> and unknown values, meaning "no category".
    /// </summary>
    public static bool TryGetDamageType(this TrapDamageType legacyType, out DamageType damageType)
    {
        switch (legacyType)
        {
            case TrapDamageType.Fire:
                damageType = DamageType.Fire;
                return true;
            case TrapDamageType.Explosive:
                damageType = DamageType.Explosive;
                return true;
            case TrapDamageType.Energy:
                damageType = DamageType.Electric;
                return true;
            case TrapDamageType.Bullet:
                damageType = DamageType.RangedPhysical;
                return true;
            case TrapDamageType.Melee:
                damageType = DamageType.MeleePhysical;
                return true;
            default:
                damageType = default;
                return false;
        }
    }
}
