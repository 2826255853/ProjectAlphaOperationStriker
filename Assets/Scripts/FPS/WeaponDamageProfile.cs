using System;
using UnityEngine;

/// <summary>
/// Coarse weapon family. It drives the built-in damage defaults so a weapon
/// that ships without a hand-tuned <see cref="WeaponDamageProfile"/> still
/// behaves differently from its neighbours.
/// </summary>
public enum WeaponClass
{
    Unknown = 0,
    Rifle = 1,
    Pistol = 2,
    Smg = 3,
    Shotgun = 4,
    Sniper = 5,
    Launcher = 6,
    Lmg = 7
}

/// <summary>
/// Pure value type that resolves a single hit into a damage number.
///
/// Keeping the maths in a struct (instead of inside the shooter) makes the
/// per-weapon rules unit-testable without spawning a KINEMATION prefab.
/// </summary>
[Serializable]
public struct WeaponDamageStats
{
    [SerializeField] private WeaponClass weaponClass;
    [SerializeField, Min(0f)] private float baseDamage;
    [SerializeField, Min(0f)] private float falloffStart;
    [SerializeField, Min(0f)] private float falloffEnd;
    [SerializeField, Range(0f, 1f)] private float minDamageScale;
    [SerializeField, Min(1f)] private float weakPointMultiplier;
    [SerializeField, Min(0f)] private float flyingDamageScale;
    [SerializeField, Min(0f)] private float groundDamageScale;

    public WeaponClass WeaponClass => weaponClass;
    public float BaseDamage => baseDamage;
    public float FalloffStart => falloffStart;
    public float FalloffEnd => falloffEnd;
    public float MinDamageScale => minDamageScale;
    public float WeakPointMultiplier => weakPointMultiplier;
    public float FlyingDamageScale => flyingDamageScale;
    public float GroundDamageScale => groundDamageScale;

    public WeaponDamageStats(WeaponClass weaponClass, float baseDamage, float falloffStart, float falloffEnd,
        float minDamageScale, float weakPointMultiplier, float flyingDamageScale, float groundDamageScale)
    {
        this.weaponClass = weaponClass;
        this.baseDamage = Mathf.Max(0f, baseDamage);
        this.falloffStart = Mathf.Max(0f, falloffStart);
        this.falloffEnd = Mathf.Max(this.falloffStart, falloffEnd);
        this.minDamageScale = Mathf.Clamp01(minDamageScale);
        this.weakPointMultiplier = Mathf.Max(1f, weakPointMultiplier);
        this.flyingDamageScale = Mathf.Max(0f, flyingDamageScale);
        this.groundDamageScale = Mathf.Max(0f, groundDamageScale);
    }

    /// <summary>Normalised copy so hand-authored inspector values stay inside sane ranges.</summary>
    public WeaponDamageStats Sanitized()
    {
        return new WeaponDamageStats(weaponClass, baseDamage, falloffStart, falloffEnd, minDamageScale,
            weakPointMultiplier, flyingDamageScale, groundDamageScale);
    }

    /// <summary>
    /// Damage falloff over distance. Inside <see cref="FalloffStart"/> the weapon
    /// deals full damage; past <see cref="FalloffEnd"/> it keeps
    /// <see cref="MinDamageScale"/> of it (shotguns drop hard, snipers barely).
    /// </summary>
    public float DamageScaleAtDistance(float distance)
    {
        if (falloffEnd <= falloffStart || distance <= falloffStart)
        {
            return 1f;
        }

        float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
        return Mathf.Lerp(1f, minDamageScale, t);
    }

    /// <summary>Per-archetype scaling: sniper rifles punish ground targets, shotguns hit fliers softer.</summary>
    public float DamageScaleAgainst(MonsterType targetType)
    {
        return targetType == MonsterType.Flying ? flyingDamageScale : groundDamageScale;
    }

    /// <summary>
    /// Full damage resolution for one bullet: base damage, distance falloff,
    /// target archetype and weak point multiplier, in that order.
    /// </summary>
    public float Evaluate(float distance, float hitWeakPointMultiplier, MonsterType targetType, bool useFalloff)
    {
        float result = baseDamage;
        if (useFalloff)
        {
            result *= DamageScaleAtDistance(distance);
        }

        result *= DamageScaleAgainst(targetType);
        result *= Mathf.Max(1f, hitWeakPointMultiplier);
        return Mathf.Max(0f, result);
    }

    /// <summary>
    /// Canonical numbers per weapon family. Every lookup funnels through here so
    /// the known-asset table and the fuzzy name heuristics can never drift apart.
    /// </summary>
    public static WeaponDamageStats ForClass(WeaponClass weaponClass)
    {
        switch (weaponClass)
        {
            case WeaponClass.Sniper:
                return new WeaponDamageStats(WeaponClass.Sniper, 120f, 120f, 400f, 0.85f, 3f, 1f, 1f);
            case WeaponClass.Shotgun:
                return new WeaponDamageStats(WeaponClass.Shotgun, 90f, 8f, 25f, 0.25f, 1.5f, 0.6f, 1f);
            case WeaponClass.Launcher:
                return new WeaponDamageStats(WeaponClass.Launcher, 110f, 60f, 150f, 0.7f, 1f, 1f, 0.8f);
            case WeaponClass.Lmg:
                return new WeaponDamageStats(WeaponClass.Lmg, 22f, 30f, 90f, 0.6f, 1.5f, 1f, 1f);
            case WeaponClass.Pistol:
                return new WeaponDamageStats(WeaponClass.Pistol, 34f, 25f, 70f, 0.5f, 2.5f, 0.85f, 1f);
            case WeaponClass.Smg:
                return new WeaponDamageStats(WeaponClass.Smg, 14f, 20f, 55f, 0.45f, 1.75f, 0.75f, 1f);
            case WeaponClass.Rifle:
                return new WeaponDamageStats(WeaponClass.Rifle, 25f, 45f, 130f, 0.55f, 2f, 1f, 1f);
            default:
                return new WeaponDamageStats(WeaponClass.Unknown, 25f, 40f, 120f, 0.6f, 2f, 1f, 1f);
        }
    }

    /// <summary>
    /// Explicit class for the weapons that actually ship under
    /// <c>Assets/KINEMATION/FPSAnimationPack/Prefabs</c>. Exact-name mapping beats
    /// substring guessing for short names such as "G3" or "AK".
    /// </summary>
    private static bool TryGetKnownClass(string name, out WeaponClass weaponClass)
    {
        switch (name)
        {
            // Assault / battle rifles
            case "ak": case "ak47": case "ak-47": case "asval": case "as-val":
            case "g3": case "g3a3": case "mx16a4": case "m16": case "m16a4":
                weaponClass = WeaponClass.Rifle;
                return true;

            // Light machine guns
            case "mgx5": case "mg5": case "m249": case "pkm":
                weaponClass = WeaponClass.Lmg;
                return true;

            // Submachine guns
            case "mps5": case "mp5": case "pdw90": case "p90":
                weaponClass = WeaponClass.Smg;
                return true;

            // Shotguns
            case "drake-12": case "drake12": case "striker-v": case "striker":
            case "kxg12": case "saiga12": case "saiga-12":
                weaponClass = WeaponClass.Shotgun;
                return true;

            // Sniper / marksman rifles
            case "kar98k": case "kar98": case "l96x": case "l96": case "awp":
            case "svd": case "mk14ebr": case "mk14": case "m14":
                weaponClass = WeaponClass.Sniper;
                return true;

            // Sidearms
            case "m1911": case "1911": case "kolibri": case "viper-357": case "viper357":
            // TODO(确认): Kolibri 是 2.7mm 微型手枪，现在和 M1911 同档 34 伤害，
            // 若需要手感差异，给它单独降到 8~12 并调高弱点倍率。
            case "python": case "x18": case "gsh18": case "gsh-18":
                weaponClass = WeaponClass.Pistol;
                return true;

            // Launchers
            case "rpg": case "rpg7": case "rpg-7": case "dgl50": case "dgl-50":
            case "mgl": case "gm94":
                weaponClass = WeaponClass.Launcher;
                return true;

            default:
                weaponClass = WeaponClass.Unknown;
                return false;
        }
    }

    /// <summary>
    /// Class defaults keyed off the weapon's object name: an exact hit against the
    /// shipped weapon list wins, then a fuzzy substring heuristics pass so renamed
    /// or imported prefabs still land in a sensible family.
    /// </summary>
    public static WeaponDamageStats DefaultsFor(string weaponName, float fallbackDamage = 25f)
    {
        string name = string.IsNullOrEmpty(weaponName)
            ? string.Empty
            : weaponName.Trim().ToLowerInvariant();

        if (name.EndsWith(".prefab"))
        {
            name = name.Substring(0, name.Length - ".prefab".Length);
        }

        if (TryGetKnownClass(name, out WeaponClass knownClass))
        {
            return ForClass(knownClass);
        }

        if (Contains(name, "sniper", "awp", "bolt", "dmr", "marksman", "kar98", "svd", "l96"))
        {
            return ForClass(WeaponClass.Sniper);
        }

        if (Contains(name, "shotgun", "benelli", "pump", "drake", "striker"))
        {
            return ForClass(WeaponClass.Shotgun);
        }

        if (Contains(name, "launcher", "rocket", "rpg", "grenade"))
        {
            return ForClass(WeaponClass.Launcher);
        }

        if (Contains(name, "lmg", "machinegun", "machine_gun"))
        {
            return ForClass(WeaponClass.Lmg);
        }

        if (Contains(name, "pistol", "glock", "usp", "deagle", "revolver"))
        {
            return ForClass(WeaponClass.Pistol);
        }

        if (Contains(name, "smg", "uzi", "vector", "pdw"))
        {
            return ForClass(WeaponClass.Smg);
        }

        if (Contains(name, "rifle", "carbine", "aug", "famas", "scar"))
        {
            return ForClass(WeaponClass.Rifle);
        }

        // Unknown: keep the caller's fallback damage so a custom prefab is not
        // silently rebalanced, but still give it a sane falloff curve.
        WeaponDamageStats fallback = ForClass(WeaponClass.Unknown);
        return new WeaponDamageStats(WeaponClass.Unknown, Mathf.Max(0f, fallbackDamage),
            fallback.FalloffStart, fallback.FalloffEnd, fallback.MinDamageScale,
            fallback.WeakPointMultiplier, fallback.FlyingDamageScale, fallback.GroundDamageScale);
    }
    private static bool Contains(string haystack, params string[] needles)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return false;
        }

        foreach (string needle in needles)
        {
            if (haystack.Contains(needle))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Optional per-weapon damage tuning component.
///
/// When it is present on the active KINEMATION weapon it fully replaces the
/// name-based defaults, so a designer can hand-tune a specific prefab. When it
/// is absent, <see cref="FPSHitscanShooter"/> falls back to
/// <see cref="WeaponDamageStats.DefaultsFor(string, float)"/>, which already
/// gives rifles / pistols / SMGs / shotguns / snipers distinct damage curves.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponDamageProfile : MonoBehaviour
{
    [SerializeField] private WeaponDamageStats stats = new WeaponDamageStats(
        WeaponClass.Rifle, 25f, 45f, 130f, 0.55f, 2f, 1f, 1f);

    /// <summary>Effective stats after clamping the serialized values.</summary>
    public WeaponDamageStats Stats => stats.Sanitized();

    /// <summary>Object-name defaults, used by the context menu and by tests.</summary>
    public WeaponDamageStats DefaultStatsForThisWeapon(float fallbackDamage = 25f)
    {
        return WeaponDamageStats.DefaultsFor(gameObject.name, fallbackDamage);
    }

    /// <summary>Seeds the inspector values from this weapon's name (editor convenience).</summary>
    [ContextMenu("Apply Class Defaults From Name")]
    private void ApplyClassDefaultsFromName()
    {
        stats = DefaultStatsForThisWeapon();
    }

    /// <summary>Runtime/test seam: force a known set of stats.</summary>
    public void Configure(WeaponDamageStats newStats)
    {
        stats = newStats.Sanitized();
    }

    private void OnValidate()
    {
        stats = stats.Sanitized();
    }
}
