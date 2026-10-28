using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Persist outside Assets; temporarily stage for Unity Test Runner, then remove.</summary>
public sealed class TrapUpgradeRulesTests
{
    private readonly List<Object> objects = new List<Object>();
    private Guid runId;
    private TrapDefinition trap;
    private TrapUpgradeService service;

    [SetUp]
    public void SetUp()
    {
        runId = Guid.NewGuid();
        trap = Definition("test-trap", 40);
        service = new TrapUpgradeService(() => runId);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object value in objects) Object.DestroyImmediate(value);
        objects.Clear();
    }

    private TrapDefinition Definition(string id, int cost)
    {
        var value = ScriptableObject.CreateInstance<TrapDefinition>();
        objects.Add(value);
        typeof(TrapDefinition).GetField("trapId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, id);
        typeof(TrapDefinition).GetField("cost", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, cost);
        return value;
    }

    private TrapUpgradeCatalog Catalog(params TrapUpgradeCatalog.Entry[] entries)
    {
        var value = ScriptableObject.CreateInstance<TrapUpgradeCatalog>();
        objects.Add(value);
        typeof(TrapUpgradeCatalog).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(value, entries.ToList());
        return value;
    }

    private static void Advance(TrapUpgradeService rules, TrapDefinition definition)
    {
        TrapUpgradeQuote quote = rules.GetQuote(definition);
        Assert.That(quote.CanUpgrade, Is.True, quote.Message);
        Assert.That(rules.TryUpgrade(quote, out TrapUpgradeFailure failure), Is.True, failure.ToString());
    }

    private void ToLevelThree(TrapUpgradeService rules = null)
    {
        rules = rules ?? service;
        Advance(rules, trap);
        Advance(rules, trap);
    }

    [TestCase(1, 1f)]
    [TestCase(2, 1.25f)]
    [TestCase(3, 1.5f)]
    [TestCase(4, 2f)]
    public void DefaultGrowthUsesLevelOneValues(int level, float expected)
    {
        TrapUpgradeMultipliers value = TrapUpgradeCatalog.DefaultMultipliers(level);
        Assert.That(value.Damage, Is.EqualTo(expected));
        Assert.That(value.Repair * 25f, Is.EqualTo(expected * 25f));
        Assert.That(value.MaxHealth, Is.EqualTo(expected));
    }

    [TestCase(40, 20, 40, 60)]
    [TestCase(60, 30, 60, 90)]
    [TestCase(41, 21, 41, 62)]
    [TestCase(1, 1, 1, 2)]
    [TestCase(0, 1, 1, 1)]
    public void DefaultCostsCeilAndHaveMinimumOne(int price, int second, int third, int fourth)
    {
        TrapDefinition definition = Definition("priced", price);
        Assert.That(service.GetQuote(definition).Cost, Is.EqualTo(second));
        Advance(service, definition);
        Assert.That(service.GetQuote(definition).Cost, Is.EqualTo(third));
        Advance(service, definition);
        Assert.That(service.GetQuote(definition, TrapUpgradeDirection.Normal).Cost, Is.EqualTo(fourth));
    }

    [TestCase(100, 0.07, 7)]
    [TestCase(100, 0.14, 14)]
    [TestCase(41, 0.5, 21)]
    [TestCase(40, 0.0, 1)]
    public void CostRoundingDoesNotAddCoinForBinaryNoise(int price, double multiplier, int expected)
        => Assert.That(TrapUpgradeCatalog.CalculateCost(price, multiplier), Is.EqualTo(expected));

    [Test]
    public void CostRejectsInvalidValuesAndOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrapUpgradeCatalog.CalculateCost(-1, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrapUpgradeCatalog.CalculateCost(40, -0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrapUpgradeCatalog.CalculateCost(40, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrapUpgradeCatalog.CalculateCost(40, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrapUpgradeCatalog.CalculateCost(40, 1, -1));
        Assert.Throws<OverflowException>(() => TrapUpgradeCatalog.CalculateCost(int.MaxValue, 1.5));
        Assert.That(TrapUpgradeCatalog.CalculateCost(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void OrdinaryPathRequiresDirectionOnlyAtFourthAndCannotChangeAfterwards()
    {
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(1));
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Normal).Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDirection));
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Branch, "future").Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDirection));
        Advance(service, trap);
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Normal).CanUpgrade, Is.False);
        Advance(service, trap);
        Assert.That(service.GetQuote(trap).Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDirection));
        TrapUpgradeQuote quote = service.GetQuote(trap, TrapUpgradeDirection.Normal);
        Assert.That(service.TryUpgrade(quote, out _), Is.True);
        Assert.That(service.GetState(trap.TrapId).Equals(new TrapUpgradeState(4, TrapUpgradeDirection.Normal)), Is.True);
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Branch, "future").Failure, Is.EqualTo(TrapUpgradeFailure.MaxLevel));
        Assert.That(service.TryUpgrade(quote, out _), Is.False);
    }

    [TestCase(0, TrapUpgradeDirection.None)]
    [TestCase(5, TrapUpgradeDirection.None)]
    [TestCase(2, TrapUpgradeDirection.Normal)]
    [TestCase(4, TrapUpgradeDirection.None)]
    [TestCase(4, (TrapUpgradeDirection)9)]
    public void StateRejectsInvalidLevelAndDirection(int level, TrapUpgradeDirection direction)
        => Assert.Catch<ArgumentException>(() => new TrapUpgradeState(level, direction));

    [Test]
    public void StateRejectsMissingAndMisplacedBranchIds()
    {
        Assert.Throws<ArgumentException>(() => new TrapUpgradeState(4, TrapUpgradeDirection.Branch));
        Assert.Throws<ArgumentException>(() => new TrapUpgradeState(2, TrapUpgradeDirection.None, "future"));
        Assert.Throws<ArgumentException>(() => new TrapUpgradeState(4, TrapUpgradeDirection.Normal, "future"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" padded")]
    [TestCase("padded ")]
    public void InvalidTrapIdsAreRejected(string id)
    {
        Assert.Throws<ArgumentException>(() => service.GetState(id));
        Assert.That(service.GetQuote(Definition(id, 40)).Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDefinition));
    }

    [Test]
    public void InvalidDefinitionsAndUninitializedRunCannotQuote()
    {
        Assert.That(service.GetQuote(null).Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDefinition));
        Assert.That(service.GetQuote(Definition("negative", -1)).Failure, Is.EqualTo(TrapUpgradeFailure.InvalidDefinition));
        runId = Guid.Empty;
        Assert.That(service.GetQuote(trap).Failure, Is.EqualTo(TrapUpgradeFailure.RunUnavailable));
    }

    [Test]
    public void SameIdSharesLevelAndDifferentIdsStayIndependent()
    {
        TrapDefinition duplicate = Definition(trap.TrapId, trap.Cost);
        TrapDefinition other = Definition("other", trap.Cost);
        Advance(service, trap);
        Assert.That(service.GetQuote(duplicate).Current.Level, Is.EqualTo(2));
        Assert.That(service.GetState(other.TrapId).Level, Is.EqualTo(1));
        Advance(service, duplicate);
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(3));
    }

    [Test]
    public void DuplicateDefinitionPricesCannotSilentlyMerge()
    {
        TrapDefinition matching = Definition(trap.TrapId, 40);
        TrapDefinition conflicting = Definition(trap.TrapId, 41);
        TrapUpgradeCatalog.ValidateDefinitions(new[] { trap, matching });
        Assert.Throws<InvalidOperationException>(() => TrapUpgradeCatalog.ValidateDefinitions(new[] { trap, conflicting }));
        service.GetQuote(trap);
        Assert.That(service.GetQuote(conflicting).Failure, Is.EqualTo(TrapUpgradeFailure.ConflictingDefinition));
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(1));
    }

    [Test]
    public void IndependentRepairOverrideDoesNotBorrowDamageOrHealth()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride { level = 2, overrideRepair = true, repair = 3f });
        var rules = new TrapUpgradeService(() => runId, Catalog(entry));
        TrapUpgradeQuote quote = rules.GetQuote(trap);
        Assert.That(quote.TargetMultipliers.Repair, Is.EqualTo(3f));
        Assert.That(quote.TargetMultipliers.Damage, Is.EqualTo(1.25f));
        Assert.That(quote.TargetMultipliers.MaxHealth, Is.EqualTo(1.25f));
        Advance(rules, trap);
        Assert.That(rules.GetQuote(trap).TargetMultipliers.Repair, Is.EqualTo(1.5f));
    }

    [Test]
    public void GrowthAndCostOverridesUseAbsoluteBaseValues()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride { level = 2, overrideDamage = true, damage = 3f,
            overrideMaxHealth = true, maxHealth = 4f });
        entry.costs.Add(new TrapUpgradeCatalog.CostOverride { targetLevel = 2, overrideFixedCost = true, fixedCost = 7 });
        entry.costs.Add(new TrapUpgradeCatalog.CostOverride { targetLevel = 3, overrideMultiplier = true, multiplier = 0.07 });
        var rules = new TrapUpgradeService(() => runId, Catalog(entry));
        TrapUpgradeQuote quote = rules.GetQuote(trap);
        Assert.That(quote.Cost, Is.EqualTo(7));
        Assert.That(quote.TargetMultipliers.Damage, Is.EqualTo(3f));
        Assert.That(quote.TargetMultipliers.MaxHealth, Is.EqualTo(4f));
        Advance(rules, trap);
        quote = rules.GetQuote(trap);
        Assert.That(quote.Cost, Is.EqualTo(3));
        Assert.That(quote.TargetMultipliers.Damage, Is.EqualTo(1.5f));
    }

    [Test]
    public void DuplicateCatalogEntriesAndLevelsAreRejected()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        var catalog = Catalog(entry, new TrapUpgradeCatalog.Entry { trapId = trap.TrapId });
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        Assert.That(new TrapUpgradeService(() => runId, catalog).GetQuote(trap).Failure,
            Is.EqualTo(TrapUpgradeFailure.InvalidConfiguration));
        catalog = Catalog(entry);
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride { level = 2 });
        entry.growth.Add(new TrapUpgradeCatalog.GrowthOverride { level = 2 });
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        entry.growth.Clear();
        entry.costs.Add(new TrapUpgradeCatalog.CostOverride { targetLevel = 2 });
        entry.costs.Add(new TrapUpgradeCatalog.CostOverride { targetLevel = 2 });
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void EnabledInvalidGrowthIsRejected(float value)
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        var growth = new TrapUpgradeCatalog.GrowthOverride { overrideRepair = true, repair = value };
        entry.growth.Add(growth);
        var catalog = Catalog(entry);
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        growth.overrideRepair = false;
        catalog.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrapUpgradeMultipliers(1f, value, 1f));
    }

    [Test]
    public void InvalidConfigIdsLevelsAndCostModesAreRejected()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = " " };
        var catalog = Catalog(entry);
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        entry.trapId = trap.TrapId;
        var growth = new TrapUpgradeCatalog.GrowthOverride { level = 1 };
        entry.growth.Add(growth);
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        entry.growth.Clear();
        var cost = new TrapUpgradeCatalog.CostOverride { targetLevel = 5 };
        entry.costs.Add(cost);
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        cost.targetLevel = 2;
        cost.overrideMultiplier = cost.overrideFixedCost = true;
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        cost.overrideFixedCost = false;
        cost.multiplier = -1;
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void BranchNeedsBothConfigurationAndEffectAdapter(bool configured, bool effectReady)
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        entry.branches.Add(new TrapUpgradeCatalog.Branch { branchId = "future", displayName = "Future branch", integrated = configured,
            overrideRepair = true, repair = 4f });
        var rules = new TrapUpgradeService(() => runId, Catalog(entry), (id, branch) => effectReady);
        ToLevelThree(rules);
        TrapUpgradeQuote quote = rules.GetQuote(trap, TrapUpgradeDirection.Branch, "future");
        Assert.That(quote.CanUpgrade, Is.EqualTo(configured && effectReady));
        Assert.That(rules.TryUpgrade(quote, out _), Is.EqualTo(configured && effectReady));
        Assert.That(rules.GetState(trap.TrapId).Level, Is.EqualTo(configured && effectReady ? 4 : 3));
        if (quote.CanUpgrade)
        {
            Assert.That(quote.Cost, Is.EqualTo(60));
            Assert.That(quote.TargetMultipliers.Repair, Is.EqualTo(4f));
            Assert.That(rules.GetState(trap.TrapId).BranchId, Is.EqualTo("future"));
            Assert.That(rules.GetQuote(trap, TrapUpgradeDirection.Normal).Failure, Is.EqualTo(TrapUpgradeFailure.MaxLevel));
        }
    }

    [Test]
    public void MissingCatalogAndAdapterLeaveBranchesUnavailable()
    {
        ToLevelThree();
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Branch, "future").Failure,
            Is.EqualTo(TrapUpgradeFailure.BranchUnavailable));
        Assert.That(service.GetQuote(trap, TrapUpgradeDirection.Normal, "future").CanUpgrade, Is.False);
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        entry.branches.Add(new TrapUpgradeCatalog.Branch { branchId = "future", displayName = "Future", integrated = true });
        var rules = new TrapUpgradeService(() => runId, Catalog(entry));
        ToLevelThree(rules);
        Assert.That(rules.GetQuote(trap, TrapUpgradeDirection.Branch, "future").CanUpgrade, Is.False);
        Assert.That(rules.GetQuote(trap, TrapUpgradeDirection.Branch, "missing").CanUpgrade, Is.False);
    }

    [Test]
    public void BranchDuplicateIdsAndInvalidGrowthAreRejected()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        var branch = new TrapUpgradeCatalog.Branch { branchId = "future", displayName = "Future" };
        entry.branches.Add(branch);
        entry.branches.Add(branch);
        var catalog = Catalog(entry);
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
        entry.branches.RemoveAt(1);
        branch.overrideMaxHealth = true;
        branch.maxHealth = -1;
        Assert.Throws<InvalidOperationException>(() => catalog.Validate());
    }

    [Test]
    public void NewRunClearsLevelsAndRejectsOldQuotes()
    {
        Advance(service, trap);
        TrapUpgradeQuote old = service.GetQuote(trap);
        runId = Guid.NewGuid();
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(1));
        Assert.That(service.TryUpgrade(old, out TrapUpgradeFailure failure), Is.False);
        Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.StaleQuote));
        Assert.That(service.GetQuote(trap).Current.Level, Is.EqualTo(1));
    }

    [Test]
    public void ForeignAndRepeatedQuotesCannotAdvanceState()
    {
        TrapUpgradeQuote quote = service.GetQuote(trap);
        var other = new TrapUpgradeService(() => runId);
        Assert.That(other.TryUpgrade(quote, out _), Is.False);
        Assert.That(service.TryUpgrade(null, out _), Is.False);
        Assert.That(service.TryUpgrade(quote, out _), Is.True);
        Assert.That(service.TryUpgrade(quote, out _), Is.False);
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(2));
    }

    [Test]
    public void ChangedConfigurationInvalidatesQuote()
    {
        var entry = new TrapUpgradeCatalog.Entry { trapId = trap.TrapId };
        var growth = new TrapUpgradeCatalog.GrowthOverride { overrideRepair = true, repair = 2f };
        entry.growth.Add(growth);
        var rules = new TrapUpgradeService(() => runId, Catalog(entry));
        TrapUpgradeQuote quote = rules.GetQuote(trap);
        growth.repair = 3f;
        Assert.That(rules.TryUpgrade(quote, out _), Is.False);
        Assert.That(rules.GetState(trap.TrapId).Level, Is.EqualTo(1));
        Advance(rules, trap);
    }

    [Test]
    public void UpgradeEventObservesCommittedStateAndRejectsReentry()
    {
        int calls = 0;
        service.Upgraded += (id, state) =>
        {
            calls++;
            Assert.That(service.GetState(id).Equals(state), Is.True);
            Assert.That(service.TryUpgrade(service.GetQuote(trap), out TrapUpgradeFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.Busy));
        };
        Advance(service, trap);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(service.GetState(trap.TrapId).Level, Is.EqualTo(2));
    }

    [Test]
    public void WalletRunIdIsolationDoesNotSpendOrChangePlacementPrices()
    {
        var host = new GameObject("Upgrade rules wallet test");
        objects.Add(host);
        EconomyManager wallet = host.AddComponent<EconomyManager>();
        wallet.BeginRun(500);
        var rules = new TrapUpgradeService(() => wallet.RunId);
        Advance(rules, trap);
        Assert.That(wallet.Balance, Is.EqualTo(500), "Payment intentionally belongs to step three.");
        Assert.That(trap.Cost, Is.EqualTo(40));
        wallet.BeginRun(100);
        Assert.That(rules.GetState(trap.TrapId).Level, Is.EqualTo(1));
    }

    [Test]
    public void ProjectCatalogChecksBothDefinitionFoldersAndSevenOfficialTrapIds()
    {
        TrapUpgradeCatalogAuthoring.CreateDefaultIfMissing();
        var catalog = AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>(TrapUpgradeCatalogAuthoring.CatalogPath);
        Assert.That(catalog, Is.Not.Null);
        Assert.That(Resources.Load<TrapUpgradeCatalog>(TrapUpgradeCatalog.ResourcePath), Is.SameAs(catalog));
        string[] official = { "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded",
            "flamethrower", "betty7", "throat-lamp", "repair-station" };
        TrapDefinition[] definitions = AssetDatabase.FindAssets("t:TrapDefinition", new[] { "Assets/Resources", "Assets/TrapDefinitions" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<TrapDefinition>).ToArray();
        foreach (string id in official)
        {
            Assert.That(definitions.Any(value => value.TrapId == id), Is.True, id);
            Assert.That(service.GetQuote(definitions.First(value => value.TrapId == id)).CanUpgrade, Is.True, id);
        }
        Assert.That(definitions.Count(value => value.TrapId == "auto_sentry_turret"), Is.EqualTo(2));
        Assert.That(definitions.Count(value => value.TrapId == "dual_missile_launcher_loaded"), Is.EqualTo(2));
        // Running creation twice must preserve manually configured data and the asset GUID.
        string before = EditorJsonUtility.ToJson(catalog);
        string guid = AssetDatabase.AssetPathToGUID(TrapUpgradeCatalogAuthoring.CatalogPath);
        TrapUpgradeCatalogAuthoring.CreateDefaultIfMissing();
        Assert.That(EditorJsonUtility.ToJson(catalog), Is.EqualTo(before));
        Assert.That(AssetDatabase.AssetPathToGUID(TrapUpgradeCatalogAuthoring.CatalogPath), Is.EqualTo(guid));
    }
}
