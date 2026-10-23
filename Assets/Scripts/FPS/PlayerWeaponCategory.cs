using KINEMATION.FPSAnimationPack.Scripts.Weapon;

public enum PlayerWeaponCategory
{
    Primary = 1,
    Secondary = 2,
    Launcher = 3
}

/// <summary>Equipment slots reuse the existing weapon family, independently of damage type.</summary>
public static class PlayerWeaponCategories
{
    public static PlayerWeaponCategory FromClass(WeaponClass weaponClass)
    {
        switch (weaponClass)
        {
            case WeaponClass.Pistol: return PlayerWeaponCategory.Secondary;
            case WeaponClass.Launcher: return PlayerWeaponCategory.Launcher;
            default: return PlayerWeaponCategory.Primary;
        }
    }

    public static PlayerWeaponCategory Resolve(FPSWeapon weapon)
    {
        if (weapon == null) return PlayerWeaponCategory.Primary;
        WeaponDamageProfile profile = weapon.GetComponentInChildren<WeaponDamageProfile>(true);
        if (profile != null) return FromClass(profile.Stats.WeaponClass);
        string weaponName = weapon.gameObject.name.Replace("(Clone)", string.Empty).Trim();
        return FromClass(WeaponDamageStats.DefaultsFor(weaponName).WeaponClass);
    }
}
