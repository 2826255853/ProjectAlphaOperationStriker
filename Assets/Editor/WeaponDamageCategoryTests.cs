#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Step three checks for docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md: existing damage sources
/// declare exactly one authoritative <see cref="DamageType"/> and settle it once.
/// Covers weapon-family mapping, profile overrides, the shooter's resolution seam,
/// burn damage over time and the "no category is guessed" fallbacks.
/// </summary>
public sealed class WeaponDamageCategoryTests
{
    private const float Tolerance = 0.0001f;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private GameObject enemyObject;
    private EnemyHealth health;

    [SetUp]
    public void SetUp()
    {
        enemyObject = new GameObject("Weapon damage category test enemy");
        spawned.Add(enemyObject);
        health = enemyObject.AddComponent<EnemyHealth>();
        // Ordinary MonoBehaviour.Awake is not dispatched by EditMode creation.
        health.ResetHealth();
    }

    [TearDown]
    public void TearDown()
    {
        LogAssert.ignoreFailingMessages = false;
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
            {
                Object.DestroyImmediate(spawned[i]);
            }
        }

        spawned.Clear();
        enemyObject = null;
        health = null;
    }

    // ------------------------------------------------------ weapon families ----

    [TestCase(WeaponClass.Rifle)]
    [TestCase(WeaponClass.Pistol)]
    [TestCase(WeaponClass.Smg)]
    [TestCase(WeaponClass.Shotgun)]
    [TestCase(WeaponClass.Sniper)]
    [TestCase(WeaponClass.Lmg)]
    public void GunFamiliesMapToRangedPhysical(WeaponClass weaponClass)
    {
        Assert.That(WeaponDamageTypeMapping.TryGetForClass(weaponClass, out DamageType type), Is.True);
        Assert.That(type, Is.EqualTo(DamageType.RangedPhysical), weaponClass.ToString());
    }

    [Test]
    public void LauncherFamilyMapsToExplosive()
    {
        Assert.That(WeaponDamageTypeMapping.TryGetForClass(WeaponClass.Launcher, out DamageType type), Is.True);
        Assert.That(type, Is.EqualTo(DamageType.Explosive));
    }

    [Test]
    public void UnknownFamilyStaysUncategorizedInsteadOfGuessing()
    {
        Assert.That(WeaponDamageTypeMapping.TryGetForClass(WeaponClass.Unknown, out _), Is.False,
            "未知武器家族不得被猜测成任一物理子类。");

        // The shooter resolves an un-profiled weapon through this name lookup, so an
        // unrecognisable prefab must still end up uncategorized.
        Assert.That(WeaponDamageStats.DefaultsFor("Weapon_03", 25f).WeaponClass,
            Is.EqualTo(WeaponClass.Unknown));
    }

    [Test]
    public void ProfileDerivesItsCategoryFromTheWeaponFamily()
    {
        WeaponDamageProfile gunProfile = CreateWeapon("AK-47");
        Assert.That(gunProfile.TryGetDamageType(out DamageType gunType), Is.True);
        Assert.That(gunType, Is.EqualTo(DamageType.RangedPhysical), "枪械 = 远程物理。");

        WeaponDamageProfile launcherProfile = CreateWeapon("RPG");
        launcherProfile.Configure(WeaponDamageStats.ForClass(WeaponClass.Launcher));
        Assert.That(launcherProfile.TryGetDamageType(out DamageType launcherType), Is.True);
        Assert.That(launcherType, Is.EqualTo(DamageType.Explosive), "RPG / 手雷 = 爆炸。");
    }

    [Test]
    public void ExplicitProfileCategoryOverridesTheFamilyDefault()
    {
        WeaponDamageProfile profile = CreateWeapon("CombatKnife");

        profile.ConfigureDamageType(WeaponDamageTypeMode.MeleePhysical);
        Assert.That(profile.TryGetDamageType(out DamageType type), Is.True);
        Assert.That(type, Is.EqualTo(DamageType.MeleePhysical), "近战武器 = 近程物理。");

        profile.ConfigureDamageType(WeaponDamageTypeMode.Auto);
        Assert.That(profile.TryGetDamageType(out DamageType restored), Is.True);
        Assert.That(restored, Is.EqualTo(DamageType.RangedPhysical), "Auto 回到家族默认值。");
    }

    // -------------------------------------------------------- shooter seam ----

    [Test]
    public void ShooterReportsNoCategoryWithoutAWeapon()
    {
        Assert.That(FPSHitscanShooter.ResolveDamageTypeFor(null, true, out _), Is.False,
            "没有武器时不得凭空给出类别。");
    }

    [Test]
    public void ShooterExposesItsResolvedCategoryForHudAndTests()
    {
        // Same reflection seam style as WeaponDamageProfileTests: the shooter itself needs
        // a KINEMATION prefab, so only the public read-only surface is asserted here.
        Assert.That(typeof(FPSHitscanShooter).GetProperty("CurrentWeaponDamageType"), Is.Not.Null,
            "暴露当前武器的伤害类别供 HUD / 测试读取。");
    }

    // ------------------------------------------------- settlement of sources ----

    [Test]
    public void GunHitSettlesRangedResistanceAndIgnoresExplosiveResistance()
    {
        health.SetResistancePercent(DamageType.RangedPhysical, 40f);
        health.SetResistancePercent(DamageType.Explosive, 90f);

        // Mirrors exactly what FPSHitscanShooter does for a categorized gun hit.
        health.TakeDamage(25f, DamageType.RangedPhysical);

        Assert.That(health.CurrentHealth, Is.EqualTo(85f).Within(Tolerance),
            "枪械只按远程物理结算一次：25 * (1 - 0.4) = 15。");
    }

    [Test]
    public void LauncherHitSettlesExplosiveResistanceOnly()
    {
        health.SetResistancePercent(DamageType.Explosive, 50f);
        health.SetResistancePercent(DamageType.RangedPhysical, 90f);

        // Mirrors the launcher (RPG / grenade) category resolved by the profile.
        health.TakeDamage(40f, DamageType.Explosive);

        Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(Tolerance),
            "爆炸只按爆炸结算一次，不再叠加远程物理抗性。");
    }

    [Test]
    public void UncategorizedWeaponKeepsRawDamage()
    {
        foreach (DamageType type in DamageTypeCatalog.All)
        {
            health.SetResistancePercent(type, 90f);
        }

        // Mirrors the shooter's fallback branch for weapons with no authoritative category.
        health.TakeDamage(25f);

        Assert.That(health.CurrentHealth, Is.EqualTo(75f).Within(Tolerance),
            "无类别来源保持原始伤害，不套用任何抗性。");
    }

    // ------------------------------------------------------------ burn DoT ----

    [Test]
    public void BurnTickSettlesFireResistanceExactlyOnce()
    {
        health.SetResistancePercent(DamageType.Fire, 50f);
        FireStatusEffect.Ignite(health, 1f, 10f);
        FireStatusEffect burn = enemyObject.GetComponent<FireStatusEffect>();
        Assert.That(burn, Is.Not.Null);

        float lastFireAmount = -1f;
        int fireEvents = 0;
        health.TrapDamaged += (_, amount, type) =>
        {
            if (type == TrapDamageType.Fire)
            {
                lastFireAmount = amount;
                fireEvents++;
            }
        };

        burn.Tick(1f);

        // 10 requested over the second, halved by 50% fire resistance, settled once.
        Assert.That(health.CurrentHealth, Is.EqualTo(95f).Within(Tolerance));
        Assert.That(fireEvents, Is.EqualTo(1), "燃烧不得同时经过旧、新入口重复扣血。");
        Assert.That(lastFireAmount, Is.EqualTo(5f).Within(Tolerance));
    }

    [Test]
    public void BurnIgnoresNonFireResistances()
    {
        health.SetResistancePercent(DamageType.Explosive, 100f);
        health.SetResistancePercent(DamageType.RangedPhysical, 100f);
        FireStatusEffect.Ignite(health, 1f, 10f);
        FireStatusEffect burn = enemyObject.GetComponent<FireStatusEffect>();
        Assert.That(burn, Is.Not.Null);

        burn.Tick(1f);

        Assert.That(health.CurrentHealth, Is.EqualTo(90f).Within(Tolerance),
            "爆炸/物理抗性不影响火类别持续伤害。");
    }

    // ------------------------------------------------------- trap call sites ----

    [Test]
    public void LegacyTrapCategoriesSettleTheirMappedResistanceOnce()
    {
        // Sentry = Bullet, spike = Melee, missile = Explosive, flame = Fire:
        // every existing trap call site still goes through the compatibility mapping.
        TrapDamageType[] legacySources =
        {
            TrapDamageType.Bullet,
            TrapDamageType.Melee,
            TrapDamageType.Explosive,
            TrapDamageType.Fire
        };

        foreach (TrapDamageType legacy in legacySources)
        {
            Assert.That(legacy.TryGetDamageType(out DamageType expected), Is.True, legacy.ToString());

            health.ResetHealth();
            health.SetResistancePercent(expected, 50f);
            health.TakeDamage(20f, legacy);

            Assert.That(health.CurrentHealth, Is.EqualTo(90f).Within(Tolerance),
                $"{legacy} 只结算一次抗性。");
            health.SetResistancePercent(expected, 0f);
        }
    }

    private WeaponDamageProfile CreateWeapon(string name)
    {
        GameObject weapon = new GameObject(name);
        spawned.Add(weapon);
        return weapon.AddComponent<WeaponDamageProfile>();
    }
}
#endif
