using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step four only: durability and effective-value contracts, without combat adapters.</summary>
public sealed class TrapUpgradeDurabilityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> owned = new List<Object>();
    private readonly List<TrapInstance> traps = new List<TrapInstance>();
    private EconomyManager wallet;
    private TrapUpgradeService service;
    private TrapDefinition definition;

    [SetUp]
    public void SetUp()
    {
        wallet = Own(new GameObject("Durability test wallet")).AddComponent<EconomyManager>();
        Invoke(wallet, "Awake");
        wallet.BeginRun(10000);
        service = TrapUpgradeService.ForWallet(wallet);
        definition = Definition("durability-test");
    }

    [TearDown]
    public void TearDown()
    {
        service?.Dispose();
        typeof(TrapUpgradeRuntime).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        foreach (TrapInstance trap in traps)
            if (trap != null) Invoke(trap, "OnDisable");
        traps.Clear();
        for (int i = owned.Count - 1; i >= 0; --i)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);

    private TrapDefinition Definition(string id)
    {
        var value = Own(ScriptableObject.CreateInstance<TrapDefinition>());
        Set(value, "trapId", id);
        return value;
    }

    private TrapInstance Trap(TrapDefinition value = null, float baseHealth = 100f)
    {
        var trap = Own(new GameObject("Durability trap")).AddComponent<TrapInstance>();
        traps.Add(trap);
        Set(trap, "maxHealth", baseHealth);
        Invoke(trap, "Awake");
        Invoke(trap, "Initialize", value ?? definition, Vector2Int.zero);
        Invoke(trap, "OnEnable");
        return trap;
    }

    private void Upgrade(TrapDefinition value = null, TrapUpgradeDirection direction = TrapUpgradeDirection.None,
        string branchId = null)
    {
        Assert.That(service.TryUpgrade(service.GetQuote(value ?? definition, direction, branchId), out var failure),
            Is.True, failure.ToString());
    }

    private void UpgradeTo(int level)
    {
        while (service.GetState(definition.TrapId).Level < level)
            Upgrade(direction: service.GetState(definition.TrapId).Level == 3
                ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);
    }

    [TestCase("auto_sentry_turret")]
    [TestCase("ground-spike")]
    [TestCase("dual_missile_launcher_loaded")]
    [TestCase("flamethrower")]
    [TestCase("betty7")]
    [TestCase("throat-lamp")]
    [TestCase("repair-station")]
    public void SevenTrapIdsShareLevelsOnlyWithTheirOwnType(string id)
    {
        definition = Definition(id);
        TrapInstance first = Trap();
        TrapInstance duplicate = Trap(Definition(id), 240f);
        TrapInstance other = Trap(Definition("other"));
        foreach (int level in new[] { 2, 3, 4 })
        {
            Upgrade(direction: level == 4 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);
            float multiplier = TrapUpgradeCatalog.DefaultMultipliers(level).MaxHealth;
            Assert.That(first.MaxHealth, Is.EqualTo(100f * multiplier));
            Assert.That(first.CurrentHealth, Is.EqualTo(first.MaxHealth));
            Assert.That(duplicate.MaxHealth, Is.EqualTo(240f * multiplier));
            Assert.That(duplicate.CurrentHealth, Is.EqualTo(duplicate.MaxHealth));
            Assert.That(first.UpgradeLevel, Is.EqualTo(level));
            Assert.That(first.UpgradeDirection, Is.EqualTo(level == 4
                ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None));
            Assert.That(other.MaxHealth, Is.EqualTo(100f));
            Assert.That(other.UpgradeLevel, Is.EqualTo(1));
        }
        Assert.That(first.BaseMaxHealth, Is.EqualTo(100f));
        Assert.That(duplicate.BaseMaxHealth, Is.EqualTo(240f));
    }

    [TestCase(0f)]
    [TestCase(37f)]
    [TestCase(99.5f)]
    public void DamageTakenBeforeUpgradeRetainsRatioAtEveryLevel(float damage)
    {
        TrapInstance trap = Trap();
        trap.TakeDamage(damage);
        float ratio = trap.CurrentHealth / trap.MaxHealth;
        for (int level = 2; level <= 4; ++level)
        {
            UpgradeTo(level);
            Assert.That(trap.CurrentHealth / trap.MaxHealth, Is.EqualTo(ratio).Within(0.00001f));
        }
    }

    [Test]
    public void UpgradeAndRepairNeverResurrectDestroyedSurvivor()
    {
        TrapInstance trap = Trap();
        // Simulate the zero-health survivor during removal callbacks, before Unity destroys it.
        Set(trap, "<CurrentHealth>k__BackingField", 0f);
        UpgradeTo(4);
        Assert.That(trap.UpgradeLevel, Is.EqualTo(4));
        Assert.That(trap.MaxHealth, Is.EqualTo(200f));
        Invoke(trap, "OnDisable");
        Invoke(trap, "OnEnable");
        Assert.That(trap.Repair(500f), Is.Zero);
        Assert.That(trap.CurrentHealth, Is.Zero);
        Assert.That(trap.IsDestroyed, Is.True);
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void RepairCapsAtEffectiveMaximumWithoutDamageEvents(int level)
    {
        TrapInstance trap = Trap();
        UpgradeTo(level);
        trap.TakeDamage(40f);
        int events = 0;
        trap.Damaged += _ => events++;
        trap.Destroyed += _ => events++;
        Assert.That(trap.Repair(1000f), Is.EqualTo(40f).Within(0.001f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(trap.MaxHealth));
        Assert.That(trap.Repair(25f), Is.Zero);
        Assert.That(events, Is.Zero);
    }

    [Test]
    public void IndependentOverridesApplyOriginalBasesAndAreVisibleBeforeUiNotification()
    {
        service.Dispose();
        var catalog = Own(ScriptableObject.CreateInstance<TrapUpgradeCatalog>());
        var entry = new TrapUpgradeCatalog.Entry { trapId = definition.TrapId };
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride
        {
            level = 2, overrideDamage = true, damage = 1.8f,
            overrideRepair = true, repair = 2.4f, overrideMaxHealth = true, maxHealth = 3f
        });
        Set(catalog, "entries", new List<TrapUpgradeCatalog.Entry> { entry });
        service = TrapUpgradeService.ForWallet(wallet, catalog);
        TrapInstance trap = Trap(baseHealth: 80f);
        trap.TakeDamage(40f);
        bool notified = false;
        wallet.BalanceChanged += _ =>
        {
            Assert.That(trap.UpgradeLevel, Is.EqualTo(2));
            Assert.That(trap.MaxHealth, Is.EqualTo(240f));
            Assert.That(trap.CurrentHealth, Is.EqualTo(120f));
            notified = true;
        };
        Upgrade();
        Assert.That(notified, Is.True);
        Assert.That(trap.GetEffectiveDamage(10f), Is.EqualTo(18f).Within(0.001f));
        Assert.That(trap.GetEffectiveRepair(25f), Is.EqualTo(60f).Within(0.001f));
        Assert.That(trap.BaseMaxHealth, Is.EqualTo(80f));
        Assert.That(TrapUpgradeRuntime.GetOrCreate(), Is.SameAs(service));
    }

    [Test]
    public void RunResetShrinksEffectiveHealthByRatioAndDoesNotHeal()
    {
        TrapInstance trap = Trap();
        UpgradeTo(4);
        trap.TakeDamage(80f);
        trap.gameObject.SetActive(false);
        wallet.BeginRun(500);
        Assert.That(trap.UpgradeLevel, Is.EqualTo(1));
        Assert.That(trap.MaxHealth, Is.EqualTo(100f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(60f).Within(0.001f));
    }

    [Test]
    public void RepeatedEnableAndDisableNeverStackOrHeal()
    {
        TrapInstance trap = Trap();
        UpgradeTo(3);
        trap.TakeDamage(30f);
        for (int i = 0; i < 10; ++i)
        {
            Invoke(trap, "OnDisable");
            Invoke(trap, "OnEnable");
        }
        Assert.That(trap.MaxHealth, Is.EqualTo(150f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(120f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RealPlacementStartsFullWithOrWithoutPrefabInstance(bool prefabHasInstance)
    {
        var prefab = Own(new GameObject("Placement template"));
        if (prefabHasInstance) prefab.AddComponent<TrapInstance>();
        prefab.SetActive(false);
        Set(definition, "prefab", prefab);
        var grid = Own(new GameObject("Durability placement grid")).AddComponent<TrapPlacementGrid>();
        grid.transform.position = new Vector3(1000f, 0f, 1000f);
        grid.ConfigureLayout(2, 2, 1f, 0f, new[] { true, true, true, true },
            TrapPlacementGrid.SurfaceKind.Ground);
        UpgradeTo(3);
        Assert.That(grid.TryPlaceTrap(Vector2Int.zero, definition, out TrapInstance placed, out string failure),
            Is.True, failure);
        traps.Add(placed);
        Assert.That(placed.UpgradeLevel, Is.EqualTo(3));
        Assert.That(placed.MaxHealth, Is.EqualTo(150f));
        Assert.That(placed.CurrentHealth, Is.EqualTo(150f));
        placed.gameObject.SetActive(true);
        Invoke(placed, "Awake");
        Invoke(placed, "OnEnable");
        Assert.That(placed.CurrentHealth, Is.EqualTo(placed.MaxHealth));
    }
}
