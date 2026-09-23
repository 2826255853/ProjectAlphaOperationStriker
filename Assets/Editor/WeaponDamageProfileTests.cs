#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode coverage for per-weapon damage differentiation (W1-W5).
///
/// The shooter itself needs a KINEMATION prefab, so these cases drive the pure
/// damage maths on <see cref="WeaponDamageStats"/> and the profile component.
/// </summary>
public sealed class WeaponDamageProfileTests
{
    private readonly System.Collections.Generic.List<GameObject> objects =
        new System.Collections.Generic.List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    // ---------------------------------------------------------------- W1 ----

    [Test]
    public void DifferentWeaponNamesProduceDifferentDefaultDamage()
    {
        float rifle = WeaponDamageStats.DefaultsFor("AK-47").BaseDamage;
        float smg = WeaponDamageStats.DefaultsFor("MP5_SMG").BaseDamage;
        float pistol = WeaponDamageStats.DefaultsFor("Glock_Pistol").BaseDamage;
        float shotgun = WeaponDamageStats.DefaultsFor("Pump_Shotgun").BaseDamage;
        float sniper = WeaponDamageStats.DefaultsFor("AWP_Sniper").BaseDamage;

        Assert.That(rifle, Is.EqualTo(25f), "W1: 步枪默认 25 伤害。");
        Assert.That(smg, Is.LessThan(rifle), "W1: SMG 单发伤害低于步枪。");
        Assert.That(pistol, Is.GreaterThan(rifle), "W1: 手枪单发伤害高于步枪。");
        Assert.That(shotgun, Is.GreaterThan(pistol), "W1: 霰弹枪单发伤害最高档。");
        Assert.That(sniper, Is.GreaterThan(shotgun), "W1: 狙击枪单发伤害最高。");
    }

    [Test]
    public void ShippedWeaponPrefabsLandInDistinctClasses()
    {
        // Exact asset names from Assets/KINEMATION/FPSAnimationPack/Prefabs.
        Assert.That(WeaponDamageStats.DefaultsFor("AK").WeaponClass, Is.EqualTo(WeaponClass.Rifle));
        Assert.That(WeaponDamageStats.DefaultsFor("ASVal").WeaponClass, Is.EqualTo(WeaponClass.Rifle));
        Assert.That(WeaponDamageStats.DefaultsFor("MGX5").WeaponClass, Is.EqualTo(WeaponClass.Lmg));
        Assert.That(WeaponDamageStats.DefaultsFor("MPS5").WeaponClass, Is.EqualTo(WeaponClass.Smg));
        Assert.That(WeaponDamageStats.DefaultsFor("PDW90").WeaponClass, Is.EqualTo(WeaponClass.Smg));
        Assert.That(WeaponDamageStats.DefaultsFor("Drake-12").WeaponClass, Is.EqualTo(WeaponClass.Shotgun));
        Assert.That(WeaponDamageStats.DefaultsFor("Striker-V").WeaponClass, Is.EqualTo(WeaponClass.Shotgun));
        Assert.That(WeaponDamageStats.DefaultsFor("KXG12").WeaponClass, Is.EqualTo(WeaponClass.Shotgun));
        Assert.That(WeaponDamageStats.DefaultsFor("Kar98k").WeaponClass, Is.EqualTo(WeaponClass.Sniper));
        Assert.That(WeaponDamageStats.DefaultsFor("L96X").WeaponClass, Is.EqualTo(WeaponClass.Sniper));
        Assert.That(WeaponDamageStats.DefaultsFor("SVD").WeaponClass, Is.EqualTo(WeaponClass.Sniper));
        Assert.That(WeaponDamageStats.DefaultsFor("M1911").WeaponClass, Is.EqualTo(WeaponClass.Pistol));
        Assert.That(WeaponDamageStats.DefaultsFor("Viper-357").WeaponClass, Is.EqualTo(WeaponClass.Pistol));
        Assert.That(WeaponDamageStats.DefaultsFor("Kolibri").WeaponClass, Is.EqualTo(WeaponClass.Pistol));
        Assert.That(WeaponDamageStats.DefaultsFor("RPG").WeaponClass, Is.EqualTo(WeaponClass.Launcher));

        Assert.That(WeaponDamageStats.DefaultsFor("AK.prefab").BaseDamage, Is.EqualTo(25f),
            "W1: 名字带 .prefab 后缀也能识别。");
    }

    [Test]
    public void UnknownWeaponNameFallsBackToTheShooterDamage()
    {
        WeaponDamageStats stats = WeaponDamageStats.DefaultsFor("Weapon_03", 17f);
        Assert.That(stats.BaseDamage, Is.EqualTo(17f),
            "W1: 无法识别的武器名应退回 FPSHitscanShooter 的 damage 回退值。");
        Assert.That(stats.WeaponClass, Is.EqualTo(WeaponClass.Unknown));
    }

    // ---------------------------------------------------------------- W2 ----

    [Test]
    public void ShotgunLosesMoreDamageOverDistanceThanSniper()
    {
        WeaponDamageStats shotgun = WeaponDamageStats.DefaultsFor("Pump_Shotgun");
        WeaponDamageStats sniper = WeaponDamageStats.DefaultsFor("AWP_Sniper");

        Assert.That(shotgun.DamageScaleAtDistance(shotgun.FalloffStart),
            Is.EqualTo(1f).Within(1e-4f), "W2: 近距离满伤害。");
        Assert.That(sniper.DamageScaleAtDistance(300f), Is.GreaterThan(0.8f),
            "W2: 狙击枪远距离几乎不衰减。");

        float shotgunFar = shotgun.Evaluate(40f, 1f, MonsterType.Ground, true);
        float shotgunNear = shotgun.Evaluate(2f, 1f, MonsterType.Ground, true);
        Assert.That(shotgunFar, Is.LessThan(shotgunNear * 0.5f),
            "W2: 霰弹枪在 40m 处伤害掉到近距的一半以下。");
    }

    [Test]
    public void DistanceBeyondFalloffEndKeepsTheMinimumScale()
    {
        WeaponDamageStats smg = WeaponDamageStats.DefaultsFor("MP5_SMG");
        float floor = smg.BaseDamage * smg.MinDamageScale;
        Assert.That(smg.Evaluate(1000f, 1f, MonsterType.Ground, true),
            Is.EqualTo(floor).Within(1e-3f), "W2: 超出衰减末端后保留最小伤害比例。");
    }

    // ---------------------------------------------------------------- W3 ----

    [Test]
    public void ShotgunHitsFlyingTargetsSofterThanGround()
    {
        WeaponDamageStats shotgun = WeaponDamageStats.DefaultsFor("Pump_Shotgun");
        float ground = shotgun.Evaluate(2f, 1f, MonsterType.Ground, false);
        float flying = shotgun.Evaluate(2f, 1f, MonsterType.Flying, false);

        Assert.That(flying, Is.LessThan(ground), "W3: 霰弹枪对空中目标伤害降低。");
        Assert.That(flying, Is.EqualTo(ground * shotgun.FlyingDamageScale).Within(1e-3f));
    }

    // ---------------------------------------------------------------- W4 ----

    [Test]
    public void WeakPointMultiplierUsesThePerWeaponValue()
    {
        WeaponDamageStats sniper = WeaponDamageStats.DefaultsFor("AWP_Sniper");
        float body = sniper.Evaluate(50f, 1f, MonsterType.Ground, false);
        float weak = sniper.Evaluate(50f, sniper.WeakPointMultiplier, MonsterType.Ground, false);

        Assert.That(weak, Is.EqualTo(body * sniper.WeakPointMultiplier).Within(1e-2f),
            "W4: 弱点伤害 = 基础伤害 x 该武器的弱点倍率。");
    }

    [Test]
    public void WeakPointMultiplierNeverDropsBelowOne()
    {
        WeaponDamageStats stats = new WeaponDamageStats(WeaponClass.Rifle, 10f, 0f, 0f, 1f, 0.25f, 1f, 1f);
        Assert.That(stats.WeakPointMultiplier, Is.EqualTo(1f),
            "W4: 弱点倍率被钳制到至少 1，避免打弱点反而掉伤害。");
    }

    // ---------------------------------------------------------------- W5 ----

    [Test]
    public void ProfileOnTheWeaponOverridesNameDefaults()
    {
        GameObject weapon = new GameObject("AK-47");
        objects.Add(weapon);
        WeaponDamageProfile profile = weapon.AddComponent<WeaponDamageProfile>();
        profile.Configure(new WeaponDamageStats(WeaponClass.Rifle, 7f, 10f, 20f, 0.5f, 1.5f, 1f, 1f));

        Assert.That(profile.Stats.BaseDamage, Is.EqualTo(7f),
            "W5: 手工配置的档案覆盖按名字推断的默认值。");
        Assert.That(profile.DefaultStatsForThisWeapon().BaseDamage, Is.EqualTo(25f),
            "W5: 右键菜单仍可按照名字回填默认值。");
    }

    [Test]
    public void ShooterExposesTheResolvedPerWeaponStats()
    {
        // The shooter's resolver is private by design; assert the public seam it
        // exposes for HUD / tests instead of reaching into KINEMATION objects.
        PropertyInfo stats = typeof(FPSHitscanShooter).GetProperty("CurrentWeaponStats");
        PropertyInfo baseDamage = typeof(FPSHitscanShooter).GetProperty("CurrentWeaponBaseDamage");
        FieldInfo toggle = typeof(FPSHitscanShooter).GetField("usePerWeaponDamage",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(stats, Is.Not.Null, "W5: 暴露当前武器伤害档案供 HUD / 测试读取。");
        Assert.That(baseDamage, Is.Not.Null, "W5: 暴露当前武器基础伤害。");
        Assert.That(toggle, Is.Not.Null, "W5: 提供 usePerWeaponDamage 开关，可退回统一伤害。");
    }
}
#endif
