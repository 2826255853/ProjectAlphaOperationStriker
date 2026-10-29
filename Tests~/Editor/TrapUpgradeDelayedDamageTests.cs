#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step six: attack-time damage snapshots survive upgrades without restarting attacks.</summary>
public sealed class TrapUpgradeDelayedDamageTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(1000f, 20f, 1000f);
    private readonly List<Object> owned = new List<Object>();
    private readonly List<TrapInstance> traps = new List<TrapInstance>();
    private readonly Dictionary<string, TrapDefinition> definitions = new Dictionary<string, TrapDefinition>();
    private EconomyManager wallet;
    private TrapUpgradeService service;

    [SetUp]
    public void SetUp()
    {
        wallet = New("Step six wallet").AddComponent<EconomyManager>();
        Invoke(wallet, "Awake");
        wallet.BeginRun(10000);
        service = TrapUpgradeService.ForWallet(wallet);
    }

    [TearDown]
    public void TearDown()
    {
        service?.Dispose();
        foreach (TrapInstance trap in traps)
            if (trap != null) Invoke(trap, "OnDisable");
        typeof(TrapUpgradeRuntime).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        foreach (PlasmaProjectile shot in Object.FindObjectsByType<PlasmaProjectile>(FindObjectsSortMode.None))
            if (shot != null && Vector3.Distance(shot.transform.position, Origin) < 100f)
                Object.DestroyImmediate(shot.gameObject);
        foreach (MissileProjectile shot in Object.FindObjectsByType<MissileProjectile>(FindObjectsSortMode.None))
            if (shot != null && Vector3.Distance(shot.transform.position, Origin) < 100f)
                Object.DestroyImmediate(shot.gameObject);
        for (int i = owned.Count - 1; i >= 0; --i)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
        traps.Clear();
        definitions.Clear();
    }

    private GameObject New(string name)
    {
        var go = new GameObject(name);
        go.transform.position = Origin;
        owned.Add(go);
        return go;
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Private).SetValue(target, value);
    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, Private).GetValue(target);
    private static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);

    private TrapDefinition Definition(string id)
    {
        if (definitions.TryGetValue(id, out TrapDefinition existing)) return existing;
        var definition = ScriptableObject.CreateInstance<TrapDefinition>();
        owned.Add(definition);
        Set(definition, "trapId", id);
        Set(definition, "cost", 40);
        definitions.Add(id, definition);
        return definition;
    }

    private TrapInstance Bind(GameObject root, string id)
    {
        var trap = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
        traps.Add(trap);
        Invoke(trap, "Awake");
        Invoke(trap, "Initialize", Definition(id), Vector2Int.zero);
        Invoke(trap, "OnEnable");
        return trap;
    }

    private void UpgradeTo(string id, int level)
    {
        while (service.GetState(id).Level < level)
        {
            var direction = service.GetState(id).Level == 3 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None;
            var quote = service.GetQuote(Definition(id), direction);
            Assert.That(service.TryUpgrade(quote, out TrapUpgradeFailure failure), Is.True, failure.ToString());
        }
    }

    private EnemyHealth Enemy(Vector3 offset, bool flying = false)
    {
        var root = New("Step six target");
        root.transform.position += offset;
        var health = root.AddComponent<EnemyHealth>();
        Set(health, "maxHealth", 10000f);
        Invoke(health, "Awake");
        var enemy = root.AddComponent<EnemyInstance>();
        typeof(EnemyInstance).GetProperty("MonsterType").SetValue(enemy, flying ? MonsterType.Flying : MonsterType.Ground);
        Invoke(enemy, "Initialize", null, 0, 1, 0);
        Assert.That(root.GetComponent<Collider>(), Is.Null, "these attacks must also work without colliders");
        return health;
    }

    private MissileLauncherWeapon Launcher(bool bind = true)
    {
        var root = New("Step six launcher");
        var weapon = root.AddComponent<MissileLauncherWeapon>();
        Invoke(weapon, "Awake");
        // Mirror placement's dynamic AddComponent after the launcher's Awake.
        if (bind) Bind(root, "dual_missile_launcher_loaded");
        var template = New("Step six missile template").AddComponent<MissileProjectile>();
        template.transform.position += Vector3.right * 90f;
        Set(weapon, "projectilePrefab", template);
        return weapon;
    }

    private MissileProjectile Launch(MissileLauncherWeapon weapon, EnemyHealth target)
    {
        Set(weapon, "interceptPoint", target.transform.position);
        var before = new HashSet<MissileProjectile>(Object.FindObjectsByType<MissileProjectile>(FindObjectsSortMode.None));
        Invoke(weapon, "LaunchOneMissile", target.GetComponent<EnemyInstance>());
        foreach (var shot in Object.FindObjectsByType<MissileProjectile>(FindObjectsSortMode.None))
            if (!before.Contains(shot)) return shot;
        Assert.Fail("the launcher must emit a projectile");
        return null;
    }

    private static void Impact(MissileProjectile shot, EnemyHealth target)
    {
        shot.transform.position = target.transform.position;
        shot.Advance(0.001f);
    }

    [TestCase(1, 1f)] [TestCase(2, 1.25f)] [TestCase(3, 1.5f)] [TestCase(4, 2f)]
    public void MissileSamplesAtLaunchAndSettlesDirectAndSplashResistanceOnce(int level, float multiplier)
    {
        var weapon = Launcher();
        UpgradeTo("dual_missile_launcher_loaded", level);
        var direct = Enemy(Vector3.forward * 6f, true);
        var splash = Enemy(new Vector3(1f, 0f, 6f), true);
        direct.SetResistancePercent(DamageType.Explosive, 25f);
        splash.SetResistancePercent(DamageType.Explosive, 50f);
        var shot = Launch(weapon, direct);
        Assert.That(shot.DamagePerMissile, Is.EqualTo(45f * multiplier));
        Assert.That(weapon.DamagePerMissile, Is.EqualTo(45f));
        Impact(shot, direct);
        Assert.That(10000f - direct.CurrentHealth, Is.EqualTo(45f * multiplier * 0.75f).Within(0.001f));
        Assert.That(10000f - splash.CurrentHealth, Is.EqualTo(22.5f * multiplier * 0.5f).Within(0.001f));
    }

    [Test]
    public void MissileInFlightKeepsOldDamageWhileNextLaunchUsesUpgrade()
    {
        var weapon = Launcher();
        var direct = Enemy(Vector3.forward * 6f, true);
        var splash = Enemy(new Vector3(1f, 0f, 6f), true);
        var old = Launch(weapon, direct);
        Set(weapon, "reloadRemaining", 2f);
        UpgradeTo("dual_missile_launcher_loaded", 4);
        Assert.That(weapon.ReloadRemaining, Is.EqualTo(2f));
        Assert.That(weapon.MissilesInFlight, Is.EqualTo(1));
        Assert.That(old.DamagePerMissile, Is.EqualTo(45f));
        Impact(old, direct);
        Assert.That(direct.CurrentHealth, Is.EqualTo(9955f));
        Assert.That(splash.CurrentHealth, Is.EqualTo(9977.5f));
        Impact(Launch(weapon, direct), direct);
        Assert.That(direct.CurrentHealth, Is.EqualTo(9865f));
        Assert.That(splash.CurrentHealth, Is.EqualTo(9932.5f));
    }

    [Test]
    public void LauncherWithoutTrapInstanceRetainsLevelOneDamage()
    {
        var weapon = Launcher(false);
        var target = Enemy(Vector3.forward * 6f, true);
        Impact(Launch(weapon, target), target);
        Assert.That(target.CurrentHealth, Is.EqualTo(9955f));
    }

    private FlamethrowerTrap Flame()
    {
        var root = New("Step six flamethrower");
        Bind(root, "flamethrower");
        var flame = root.AddComponent<FlamethrowerTrap>();
        Invoke(flame, "Awake");
        Invoke(flame, "OnEnable");
        return flame;
    }

    [TestCase(1, 1f)] [TestCase(2, 1.25f)] [TestCase(3, 1.5f)] [TestCase(4, 2f)]
    public void FlameDirectAndBurnUseAttackMultiplierAndFireResistanceOnce(int level, float multiplier)
    {
        var flame = Flame();
        UpgradeTo("flamethrower", level);
        var target = Enemy(Vector3.forward * 3f);
        target.SetResistancePercent(DamageType.Fire, 50f);
        flame.Tick(0.1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(10000f - 10f * multiplier).Within(0.001f));
        var burn = target.GetComponent<FireStatusEffect>();
        Assert.That(burn.DamagePerSecond, Is.EqualTo(5f * multiplier));
        burn.Tick(0.4f);
        burn.Tick(1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(10000f - 12.5f * multiplier).Within(0.001f));
        Assert.That(burn.IsBurning, Is.False);
    }

    [Test]
    public void FlameUpgradeDuringSprayPreservesExistingBurnAndHitDeduplication()
    {
        var flame = Flame();
        var old = Enemy(Vector3.forward * 3f);
        flame.Tick(0.1f);
        var burn = old.GetComponent<FireStatusEffect>();
        burn.Tick(0.25f);
        double elapsed = Get<double>(flame, "stateTime");
        UpgradeTo("flamethrower", 2);
        Assert.That(flame.State, Is.EqualTo(FlamethrowerTrap.AttackState.Spraying));
        Assert.That(Get<double>(flame, "stateTime"), Is.EqualTo(elapsed));
        Assert.That(burn.DamagePerSecond, Is.EqualTo(5f));
        var fresh = Enemy(new Vector3(0.5f, 0f, 3f));
        flame.Tick(0.2f);
        burn.Tick(0.75f);
        Assert.That(old.CurrentHealth, Is.EqualTo(9975f), "the old target takes only its original direct hit and burn");
        Assert.That(fresh.CurrentHealth, Is.EqualTo(9975f));
        Assert.That(fresh.GetComponent<FireStatusEffect>().DamagePerSecond, Is.EqualTo(6.25f));
        flame.Tick(3.8f);
        Assert.That(old.CurrentHealth, Is.EqualTo(9950f), "a later burst applies upgraded direct damage");
        Assert.That(burn.DamagePerSecond, Is.EqualTo(6.25f));
    }

    private Betty7JumpMineTrap Betty()
    {
        var root = New("Step six Betty");
        Bind(root, "betty7");
        foreach (var p in new[] { new Vector3(0.61f, 0f, 0.61f), new Vector3(-0.61f, 0f, 0.61f),
                     new Vector3(-0.61f, 0f, -0.61f), new Vector3(0.61f, 0f, -0.61f) })
        {
            var mine = new GameObject("Mine_" + root.transform.childCount);
            mine.transform.SetParent(root.transform, false);
            mine.transform.localPosition = p;
        }
        var betty = root.AddComponent<Betty7JumpMineTrap>();
        Invoke(betty, "Awake");
        Invoke(betty, "OnEnable");
        return betty;
    }

    [TestCase(1, 1f)] [TestCase(2, 1.25f)] [TestCase(3, 1.5f)] [TestCase(4, 2f)]
    public void BettySamplesDamageAtLiftOffAndPreservesGroundFiltering(int level, float multiplier)
    {
        var betty = Betty();
        var target = Enemy(new Vector3(1f, 0f, 1f));
        var flying = Enemy(new Vector3(1f, 0f, 1f), true);
        target.SetResistancePercent(DamageType.Explosive, 20f);
        betty.Tick(0.1f);
        Assert.That(betty.GetMineState(0), Is.EqualTo(Betty7JumpMineTrap.AttackState.Arming));
        UpgradeTo("betty7", level);
        betty.Tick(0.2f);
        Assert.That(betty.GetMineState(0), Is.EqualTo(Betty7JumpMineTrap.AttackState.Launching));
        betty.Tick(0.45f);
        Assert.That(10000f - target.CurrentHealth, Is.EqualTo(55f * multiplier * 0.8f).Within(0.001f));
        Assert.That(flying.CurrentHealth, Is.EqualTo(10000f));
        Assert.That(betty.GetMineDetonationCount(0), Is.EqualTo(1));
    }

    [Test]
    public void BettyAirborneSnapshotReloadsAndOtherSlotsSurviveUpgrade()
    {
        var betty = Betty();
        var target = Enemy(new Vector3(1f, 0f, 1f));
        betty.Tick(0.3f);
        UpgradeTo("betty7", 2);
        betty.Tick(0.45f);
        Assert.That(target.CurrentHealth, Is.EqualTo(9945f), "an airborne level-one mine keeps its damage");
        betty.Tick(0.22f + 0.45f);
        Assert.That(betty.GetMineState(0), Is.EqualTo(Betty7JumpMineTrap.AttackState.Cooldown));
        target.transform.position = Origin + new Vector3(-1f, 0f, 1f);
        betty.Tick(0.1f);
        float reload = betty.GetMineReloadRemaining(0);
        UpgradeTo("betty7", 4);
        Assert.That(betty.GetMineReloadRemaining(0), Is.EqualTo(reload));
        Assert.That(betty.GetMineState(1), Is.EqualTo(Betty7JumpMineTrap.AttackState.Arming));
        betty.Tick(0.2f);
        betty.Tick(0.45f);
        Assert.That(target.CurrentHealth, Is.EqualTo(9835f), "another slot samples the current level at lift-off");
        target.gameObject.SetActive(false);
        betty.Tick(7f);
        target.transform.position = Origin + new Vector3(1f, 0f, 1f);
        target.gameObject.SetActive(true);
        betty.Tick(0.75f);
        Assert.That(betty.GetMineDetonationCount(0), Is.EqualTo(2));
        Assert.That(target.CurrentHealth, Is.EqualTo(9725f), "the reloaded slot must deal damage again using a fresh snapshot");
    }

    private ThroatLampTrap Lamp()
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ThroatLampAuthoring.PrefabPath));
        owned.Add(root);
        root.transform.SetPositionAndRotation(Origin + Vector3.up * 4f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        Bind(root, "throat-lamp");
        root.GetComponent<Animator>().Rebind();
        root.GetComponent<Animator>().Update(0f);
        return root.GetComponent<ThroatLampTrap>();
    }

    private static PlasmaProjectile FireLamp(ThroatLampTrap lamp)
    {
        int previous = lamp.ShotCount;
        for (int i = 0; i < 300 && lamp.ShotCount == previous; i++) lamp.Advance(0.02f);
        Assert.That(lamp.ShotCount, Is.EqualTo(previous + 1));
        var shots = Object.FindObjectsByType<PlasmaProjectile>(FindObjectsSortMode.None);
        Assert.That(shots.Length, Is.EqualTo(1));
        return shots[0];
    }

    [TestCase(1, 1f)] [TestCase(2, 1.25f)] [TestCase(3, 1.5f)] [TestCase(4, 2f)]
    public void LampProjectileScalesDirectAndSplashOnceAndIgnoresFlyingTargets(int level, float multiplier)
    {
        var lamp = Lamp();
        UpgradeTo("throat-lamp", level);
        var target = Enemy(Vector3.zero);
        var splash = Enemy(Vector3.right);
        var flying = Enemy(Vector3.up * 2f, true);
        target.SetResistancePercent(DamageType.Electric, 25f);
        splash.SetResistancePercent(DamageType.Electric, 50f);
        FireLamp(lamp).Advance(1f);
        Assert.That(10000f - target.CurrentHealth, Is.EqualTo(80f * multiplier * 0.75f).Within(0.001f));
        Assert.That(10000f - splash.CurrentHealth, Is.EqualTo(40f * multiplier * 0.5f).Within(0.001f));
        Assert.That(flying.CurrentHealth, Is.EqualTo(10000f));
    }

    [Test]
    public void LampChargeSurvivesUpgradeAndEmittedProjectileKeepsItsSnapshot()
    {
        var lamp = Lamp();
        var target = Enemy(Vector3.zero);
        var splash = Enemy(Vector3.right);
        for (int i = 0; i < 25; i++) lamp.Advance(0.02f);
        Assert.That(lamp.State, Is.EqualTo(ThroatLampState.Charging));
        float charge = Get<float>(lamp, "chargeElapsed");
        UpgradeTo("throat-lamp", 2);
        Assert.That(Get<float>(lamp, "chargeElapsed"), Is.EqualTo(charge));
        Assert.That(lamp.State, Is.EqualTo(ThroatLampState.Charging));
        var old = FireLamp(lamp);
        UpgradeTo("throat-lamp", 4);
        Assert.That(Get<float>(old, "directDamage"), Is.EqualTo(100f));
        Assert.That(Get<float>(old, "splashDamage"), Is.EqualTo(50f));
        old.Advance(1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(9900f), "direct targets never also take plasma splash");
        Assert.That(splash.CurrentHealth, Is.EqualTo(9950f));
        FireLamp(lamp).Advance(1f);
        Assert.That(target.CurrentHealth, Is.EqualTo(9740f));
        Assert.That(splash.CurrentHealth, Is.EqualTo(9870f));
        Assert.That(Get<float>(lamp, "directDamage"), Is.EqualTo(80f));
        Assert.That(Get<float>(lamp, "splashDamage"), Is.EqualTo(40f));
    }
}
#endif
