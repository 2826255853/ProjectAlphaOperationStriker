#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step five: live damage and repair settlements read the owning trap's current multipliers.</summary>
public sealed class TrapUpgradeCombatIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> owned = new List<Object>();
    private readonly List<TrapInstance> traps = new List<TrapInstance>();
    private readonly Dictionary<string, TrapDefinition> definitions = new Dictionary<string, TrapDefinition>();
    private EconomyManager wallet;
    private TrapUpgradeService service;

    [SetUp]
    public void SetUp()
    {
        wallet = Own(new GameObject("Step five upgrade wallet")).AddComponent<EconomyManager>();
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
            ?.Invoke(null, null);
        for (int i = owned.Count - 1; i >= 0; --i)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        traps.Clear();
        owned.Clear();
        definitions.Clear();
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);

    private TrapDefinition Definition(string id)
    {
        if (definitions.TryGetValue(id, out TrapDefinition existing)) return existing;
        var definition = Own(ScriptableObject.CreateInstance<TrapDefinition>());
        Set(definition, "trapId", id);
        Set(definition, "cost", 40);
        definitions.Add(id, definition);
        return definition;
    }

    private TrapInstance Trap(TrapDefinition definition, float baseHealth = 100f)
    {
        var root = Own(new GameObject("Step five " + definition.TrapId));
        TrapInstance trap = root.AddComponent<TrapInstance>();
        traps.Add(trap);
        Set(trap, "maxHealth", baseHealth);
        Invoke(trap, "Awake");
        Invoke(trap, "Initialize", definition, Vector2Int.zero);
        Invoke(trap, "OnEnable");
        return trap;
    }

    private void UpgradeTo(TrapDefinition definition, int level)
    {
        while (service.GetState(definition.TrapId).Level < level)
        {
            TrapUpgradeDirection direction = service.GetState(definition.TrapId).Level == 3
                ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None;
            TrapUpgradeQuote quote = service.GetQuote(definition, direction);
            Assert.That(quote.CanUpgrade, Is.True, quote.Message);
            Assert.That(service.TryUpgrade(quote, out TrapUpgradeFailure failure), Is.True, failure.ToString());
        }
    }

    private static EnemyHealth Enemy(Vector3 position, bool withIdentity)
    {
        var root = new GameObject("Step five damage target");
        root.transform.position = position;
        if (withIdentity) root.AddComponent<EnemyInstance>();
        var health = root.AddComponent<EnemyHealth>();
        typeof(EnemyHealth).GetField("maxHealth", Private).SetValue(health, 1000f);
        Invoke(health, "Awake");
        health.ResetHealth();
        return health;
    }

    [TestCase(1, 1f)]
    [TestCase(2, 1.25f)]
    [TestCase(3, 1.5f)]
    [TestCase(4, 2f)]
    public void MachineGunAndSpikesUseCurrentDamageMultiplier(int level, float multiplier)
    {
        TrapDefinition sentryDefinition = Definition("auto_sentry_turret");
        TrapDefinition spikeDefinition = Definition("ground-spike");
        TrapInstance sentry = Trap(sentryDefinition);
        var turret = sentry.gameObject.AddComponent<AutoSentryTurret>();
        TrapInstance spikesInstance = Trap(spikeDefinition);
        var spikes = spikesInstance.gameObject.AddComponent<GroundSpikeTrap>();
        Invoke(spikes, "Awake");
        Invoke(spikes, "OnEnable");

        UpgradeTo(sentryDefinition, level);
        UpgradeTo(spikeDefinition, level);

        var sentryTarget = Enemy(Vector3.forward, true);
        owned.Add(sentryTarget.gameObject);
        float sentryHealth = sentryTarget.CurrentHealth;
        DamageType? sentryDamageType = null;
        sentryTarget.TypedDamaged += (_, __, type) => sentryDamageType = type;
        Invoke(turret, "FireAtTarget", sentryTarget.GetComponent<EnemyInstance>());
        Assert.That(sentryHealth - sentryTarget.CurrentHealth, Is.EqualTo(12f * multiplier).Within(0.001f));
        Assert.That(sentryDamageType, Is.EqualTo(DamageType.RangedPhysical));

        var spikeTarget = Enemy(Vector3.zero, false);
        owned.Add(spikeTarget.gameObject);
        spikeTarget.gameObject.AddComponent<MonsterPathFollower>();
        DamageType? spikeDamageType = null;
        spikeTarget.TypedDamaged += (_, __, type) => spikeDamageType = type;
        float spikeHealth = spikeTarget.CurrentHealth;
        spikes.Tick(0.1f);
        Assert.That(spikeHealth - spikeTarget.CurrentHealth, Is.EqualTo(40f * multiplier).Within(0.001f));
        Assert.That(spikeDamageType, Is.EqualTo(DamageType.MeleePhysical));
        float afterFirstHit = spikeTarget.CurrentHealth;
        spikes.Tick(0.1f);
        Assert.That(spikeTarget.CurrentHealth, Is.EqualTo(afterFirstHit), "one attack cannot hit the same target twice");

        var duplicateDefinition = Own(ScriptableObject.CreateInstance<TrapDefinition>());
        Set(duplicateDefinition, "trapId", sentryDefinition.TrapId);
        Set(duplicateDefinition, "cost", sentryDefinition.Cost);
        TrapInstance sameType = Trap(duplicateDefinition);
        TrapInstance isolatedType = Trap(Definition("step5-isolated-trap"));
        Assert.That(sameType.UpgradeLevel, Is.EqualTo(level), "new instances inherit their TrapId's committed level");
        Assert.That(isolatedType.UpgradeLevel, Is.EqualTo(1), "a different TrapId stays at level one");
        Assert.That(sameType.GetEffectiveDamage(12f), Is.EqualTo(12f * multiplier).Within(0.001f));
        Assert.That(isolatedType.GetEffectiveDamage(12f), Is.EqualTo(12f));

        Invoke(sentry, "OnDisable");
        Invoke(sentry, "OnEnable");
        var afterToggleTarget = Enemy(Vector3.forward, true);
        owned.Add(afterToggleTarget.gameObject);
        float beforeToggleShot = afterToggleTarget.CurrentHealth;
        Invoke(turret, "FireAtTarget", afterToggleTarget.GetComponent<EnemyInstance>());
        Assert.That(beforeToggleShot - afterToggleTarget.CurrentHealth,
            Is.EqualTo(12f * multiplier).Within(0.001f), "enable cycles never stack the damage multiplier");
    }

    [Test]
    public void MachineGunWithoutTrapInstanceKeepsItsBaseDamage()
    {
        var root = Own(new GameObject("Unowned sentry"));
        var turret = root.AddComponent<AutoSentryTurret>();
        var target = Enemy(Vector3.forward, true);
        owned.Add(target.gameObject);
        float before = target.CurrentHealth;
        Invoke(turret, "FireAtTarget", target.GetComponent<EnemyInstance>());
        Assert.That(before - target.CurrentHealth, Is.EqualTo(12f).Within(0.001f));
    }

    private TrapDefinition RepairDefinition => AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/RepairStation.asset");

    private TrapPlacementGrid RepairGrid()
    {
        var root = Own(new GameObject("Step five repair grid"));
        root.transform.position = new Vector3(10000f, 0f, 10000f);
        var grid = root.AddComponent<TrapPlacementGrid>();
        var open = new bool[16 * 16];
        for (int i = 0; i < open.Length; i++) open[i] = true;
        grid.ConfigureLayout(16, 16, 1f, 0f, open, TrapPlacementGrid.SurfaceKind.Ground);
        return grid;
    }

    private TrapInstance Place(TrapPlacementGrid grid, Vector2Int cell)
    {
        Assert.That(grid.TryPlaceTrap(cell, RepairDefinition, out TrapInstance trap, out string failure), Is.True, failure);
        traps.Add(trap);
        if (trap.IsDestroyed) Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");
        return trap;
    }

    [TestCase(1, 25f)]
    [TestCase(2, 31.25f)]
    [TestCase(3, 37.5f)]
    [TestCase(4, 50f)]
    public void RepairStationUsesCurrentRepairMultiplierAtEachLevel(int level, float expectedRepair)
    {
        Assert.That(RepairDefinition, Is.Not.Null);
        TrapPlacementGrid grid = RepairGrid();
        TrapInstance source = Place(grid, new Vector2Int(4, 4));
        TrapInstance target = Place(grid, new Vector2Int(4, 6));
        target.TakeDamage(80f);
        UpgradeTo(RepairDefinition, level);

        RepairStationTrap station = source.GetComponent<RepairStationTrap>();
        float before = target.CurrentHealth;
        station.Tick(0f);
        Assert.That(target.CurrentHealth - before, Is.EqualTo(expectedRepair).Within(0.001f));
        Assert.That(station.RepairAmount, Is.EqualTo(25f), "the authored level-one amount remains unchanged");
        Assert.That(station.GetRepairCount(0), Is.EqualTo(1));
    }

    [Test]
    public void RepairUpgradePreservesArmCooldownAndNextRepairUsesNewAmount()
    {
        TrapDefinition definition = RepairDefinition;
        TrapPlacementGrid grid = RepairGrid();
        TrapInstance source = Place(grid, new Vector2Int(4, 4));
        TrapInstance target = Place(grid, new Vector2Int(4, 6));
        target.TakeDamage(80f);
        RepairStationTrap station = source.GetComponent<RepairStationTrap>();
        station.Tick(0f);
        Assert.That(target.CurrentHealth, Is.EqualTo(45f));

        float[] readyAt = (float[])typeof(RepairStationTrap).GetField("readyAt", Private).GetValue(station);
        float[] animationAt = (float[])typeof(RepairStationTrap).GetField("animationAt", Private).GetValue(station);
        UpgradeTo(definition, 2);
        Assert.That(readyAt[0], Is.EqualTo(4f));
        Assert.That(animationAt[0], Is.EqualTo(0f));

        float duringCooldown = target.CurrentHealth;
        station.Tick(3.999f);
        Assert.That(target.CurrentHealth, Is.EqualTo(duringCooldown), "upgrade cannot finish an arm cooldown early");
        station.Tick(4f);
        Assert.That(target.CurrentHealth - duringCooldown, Is.EqualTo(31.25f).Within(0.001f));
        Assert.That(station.GetRepairCount(0), Is.EqualTo(2));
    }

    [Test]
    public void FourArmsRemainIndependentAndRepairTargetsShareUpgradeLevel()
    {
        TrapDefinition definition = RepairDefinition;
        TrapPlacementGrid grid = RepairGrid();
        TrapInstance source = Place(grid, new Vector2Int(4, 4));
        var cells = new[]
        {
            new Vector2Int(4, 6), new Vector2Int(6, 4),
            new Vector2Int(4, 2), new Vector2Int(2, 4)
        };
        var targets = new TrapInstance[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            targets[i] = Place(grid, cells[i]);
            targets[i].TakeDamage(80f);
        }
        UpgradeTo(definition, 4);

        RepairStationTrap station = source.GetComponent<RepairStationTrap>();
        station.Tick(0f);
        for (int i = 0; i < targets.Length; i++)
        {
            Assert.That(targets[i].UpgradeLevel, Is.EqualTo(4));
            Assert.That(targets[i].CurrentHealth, Is.EqualTo(90f).Within(0.001f));
            Assert.That(station.GetRepairCount(i), Is.EqualTo(1));
        }
    }

    [Test]
    public void RepairCapsAtMaximumAndNeverRevivesDestroyedTargets()
    {
        TrapDefinition definition = RepairDefinition;
        TrapPlacementGrid grid = RepairGrid();
        TrapInstance source = Place(grid, new Vector2Int(4, 4));
        TrapInstance almostFull = Place(grid, new Vector2Int(4, 6));
        TrapInstance dead = Place(grid, new Vector2Int(6, 4));
        almostFull.TakeDamage(10f);
        typeof(TrapInstance).GetProperty("CurrentHealth").SetValue(dead, 0f);

        source.GetComponent<RepairStationTrap>().Tick(0f);
        Assert.That(almostFull.CurrentHealth, Is.EqualTo(almostFull.MaxHealth));
        Assert.That(dead.CurrentHealth, Is.Zero);
    }

    [Test]
    public void RepairOnlyOverrideDoesNotChangeDamageOrHealthMultipliers()
    {
        TrapDefinition definition = RepairDefinition;
        service.Dispose();
        var catalog = Own(ScriptableObject.CreateInstance<TrapUpgradeCatalog>());
        var entry = new TrapUpgradeCatalog.Entry { trapId = definition.TrapId };
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride
        {
            level = 2, overrideRepair = true, repair = 1.8f
        });
        Set(catalog, "entries", new List<TrapUpgradeCatalog.Entry> { entry });
        service = TrapUpgradeService.ForWallet(wallet, catalog);

        TrapPlacementGrid grid = RepairGrid();
        TrapInstance source = Place(grid, new Vector2Int(4, 4));
        TrapInstance target = Place(grid, new Vector2Int(4, 6));
        target.TakeDamage(80f);
        UpgradeTo(definition, 2);

        Assert.That(source.RepairMultiplier, Is.EqualTo(1.8f));
        Assert.That(source.DamageMultiplier, Is.EqualTo(1.25f));
        Assert.That(source.MaxHealthMultiplier, Is.EqualTo(1.25f));
        float before = target.CurrentHealth;
        source.GetComponent<RepairStationTrap>().Tick(0f);
        Assert.That(target.CurrentHealth - before, Is.EqualTo(45f).Within(0.001f));
    }
}
#endif
