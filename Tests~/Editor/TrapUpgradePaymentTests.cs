using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Staged only while running Unity tests; never imported by a normal project session.</summary>
public sealed class TrapUpgradePaymentTests
{
    private readonly List<Object> objects = new List<Object>();
    private readonly List<TrapInstance> traps = new List<TrapInstance>();
    private EconomyManager wallet;
    private TrapDefinition definition;
    private TrapUpgradeService service;

    [SetUp]
    public void SetUp()
    {
        var host = new GameObject("Upgrade payment wallet");
        objects.Add(host);
        wallet = host.AddComponent<EconomyManager>();
        wallet.BeginRun(500);
        definition = Definition("test-trap");
        service = TrapUpgradeService.ForWallet(wallet);
    }

    [TearDown]
    public void TearDown()
    {
        service.Dispose();
        foreach (TrapInstance trap in traps)
            if (trap != null) Invoke(trap, "OnDisable");
        traps.Clear();
        for (int i = objects.Count - 1; i >= 0; --i)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    private TrapDefinition Definition(string id)
    {
        var value = ScriptableObject.CreateInstance<TrapDefinition>();
        objects.Add(value);
        Set(value, "trapId", id);
        Set(value, "cost", 40);
        return value;
    }

    private TrapInstance Trap(TrapDefinition value)
    {
        var host = new GameObject("Upgrade refresh receiver");
        objects.Add(host);
        var trap = host.AddComponent<TrapInstance>();
        Invoke(trap, "Initialize", value, Vector2Int.zero);
        Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");
        traps.Add(trap);
        return trap;
    }

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void ResetRuntime() => typeof(EconomyManager)
        .GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    private void Upgrade(TrapUpgradeDirection direction = TrapUpgradeDirection.None, string branch = null)
    {
        Assert.That(service.TryUpgrade(service.GetQuote(definition, direction, branch), out var failure),
            Is.True, failure.ToString());
    }

    [Test]
    public void NormalPathChargesExactlyOncePerLevelAndNeverChangesPlacementPrice()
    {
        var balances = new List<int>();
        wallet.BalanceChanged += balances.Add;
        Upgrade();
        Upgrade();
        Upgrade(TrapUpgradeDirection.Normal);
        Assert.That(wallet.Balance, Is.EqualTo(380));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(380));
        Assert.That(balances, Is.EqualTo(new[] { 480, 440, 380 }));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(4));
        Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(380));
        Assert.That(definition.Cost, Is.EqualTo(40));
    }

    [Test]
    public void InsufficientFundsAndExactBalanceUseAvailableMoney()
    {
        wallet.BeginRun(19);
        var quote = service.GetQuote(definition);
        Assert.That(service.TryUpgrade(quote, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.InsufficientFunds));
        Assert.That(wallet.Balance, Is.EqualTo(19));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
        wallet.Grant(1);
        Assert.That(service.TryUpgrade(quote, out _), Is.True);
        Assert.That(wallet.Balance, Is.Zero);
    }

    [Test]
    public void ReservationHasNoVisibleCommitAndDisposalCancelsIt()
    {
        int notifications = 0;
        wallet.BalanceChanged += _ => notifications++;
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        Assert.That(wallet.Balance, Is.EqualTo(500));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(480));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
        pending.Dispose();
        pending.Dispose();
        Assert.That(pending.Commit(out _), Is.False);
        Assert.That(wallet.AvailableBalance, Is.EqualTo(500));
        Assert.That(wallet.Balance, Is.EqualTo(500));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void ReservedUpgradeCannotBeSpentByAnotherPurchase()
    {
        wallet.BeginRun(40);
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        using (pending)
        {
            Assert.That(wallet.TrySpend(21), Is.False);
            Assert.That(wallet.TrySpend(20), Is.True);
            Assert.That(pending.Commit(out _), Is.True);
            Assert.That(wallet.Balance, Is.Zero);
        }
    }

    [Test]
    public void RepeatedQuoteAndReservationCommitCannotDebitTwice()
    {
        var quote = service.GetQuote(definition);
        Assert.That(service.TryReserveUpgrade(quote, out var pending, out _), Is.True);
        using (pending)
        {
            Assert.That(pending.Commit(out _), Is.True);
            Assert.That(pending.Commit(out _), Is.False);
            Assert.That(service.TryUpgrade(quote, out _), Is.False);
        }
        Assert.That(wallet.Balance, Is.EqualTo(480));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(480));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(2));
    }

    [Test]
    public void TwoReservationsForSameLevelOnlyOneCommitsAndLoserReleasesFunds()
    {
        var quote = service.GetQuote(definition);
        Assert.That(service.TryReserveUpgrade(quote, out var first, out _), Is.True);
        Assert.That(service.TryReserveUpgrade(quote, out var second, out _), Is.True);
        using (first) using (second)
        {
            Assert.That(first.Commit(out _), Is.True);
            Assert.That(second.Commit(out _), Is.False);
        }
        Assert.That(wallet.Balance, Is.EqualTo(480));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(480));
    }

    [Test]
    public void OtherReservationsCanPreventUpgradeWithoutChangingState()
    {
        wallet.BeginRun(30);
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var first, out _), Is.True);
        using (first)
        {
            Assert.That(service.TryUpgrade(service.GetQuote(Definition("other")), out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.InsufficientFunds));
        }
        Assert.That(wallet.Balance, Is.EqualTo(30));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(30));
        Assert.That(service.GetState("other").Level, Is.EqualTo(1));
    }

    [Test]
    public void NewRunCancelsReservationResetsLevelsAndRejectsOldQuotesImmediately()
    {
        Upgrade();
        var old = service.GetQuote(definition);
        Assert.That(service.TryReserveUpgrade(old, out var pending, out _), Is.True);
        int resets = 0;
        service.RunReset += id =>
        {
            resets++;
            Assert.That(id, Is.EqualTo(wallet.RunId));
            Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
            Assert.That(wallet.AvailableBalance, Is.EqualTo(100));
        };
        wallet.BeginRun(100);
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(pending.Commit(out _), Is.False);
        Assert.That(service.TryUpgrade(old, out _), Is.False);
        pending.Dispose();
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(100));
    }

    [Test]
    public void StateAndAllSameTypeInstancesRefreshBeforeUIAndOtherTypesStayUntouched()
    {
        var order = new List<string>();
        TrapInstance first = Trap(definition);
        TrapInstance duplicate = Trap(Definition(definition.TrapId));
        TrapInstance other = Trap(Definition("other"));
        int refreshed = 0;
        foreach (var trap in new[] { first, duplicate })
            trap.UpgradeStateChanged += (_, state) =>
            {
                Assert.That(state.Level, Is.EqualTo(2));
                Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(2));
                Assert.That(wallet.Balance, Is.EqualTo(480));
                refreshed++;
                order.Add("instance");
            };
        other.UpgradeStateChanged += (_, __) => Assert.Fail("Unrelated TrapId refreshed on upgrade.");
        wallet.BalanceChanged += _ => { Assert.That(refreshed, Is.EqualTo(2)); order.Add("balance"); };
        service.Upgraded += (_, __) => order.Add("upgrade");
        Upgrade();
        Assert.That(order, Is.EqualTo(new[] { "instance", "instance", "balance", "upgrade" }));
    }

    [Test]
    public void NewRunRefreshesSurvivingTrapsBeforeResetAndBalanceUI()
    {
        Upgrade();
        TrapInstance first = Trap(definition);
        TrapInstance other = Trap(Definition("other"));
        var order = new List<string>();
        foreach (var trap in new[] { first, other })
            trap.UpgradeStateChanged += (_, state) => { Assert.That(state.Level, Is.EqualTo(1)); order.Add("instance"); };
        service.RunReset += _ => order.Add("reset");
        wallet.BalanceChanged += _ => order.Add("balance");
        wallet.BeginRun(100);
        Assert.That(order, Is.EqualTo(new[] { "instance", "instance", "reset", "balance" }));
        Assert.That(first.CurrentHealth, Is.EqualTo(100), "Step three must not alter durability.");
    }

    [TestCase("instance")]
    [TestCase("balance")]
    [TestCase("upgrade")]
    [TestCase("reset")]
    public void CallbackReentryCannotStartAnotherPayment(string source)
    {
        int calls = 0;
        Action reenter = () =>
        {
            calls++;
            Assert.That(service.TryUpgrade(service.GetQuote(definition), out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.Busy));
        };
        if (source == "instance") Trap(definition).UpgradeStateChanged += (_, __) => reenter();
        if (source == "balance") wallet.BalanceChanged += _ => reenter();
        if (source == "upgrade") service.Upgraded += (_, __) => reenter();
        if (source == "reset")
        {
            service.RunReset += _ => reenter();
            wallet.BeginRun(500);
            Assert.That(wallet.Balance, Is.EqualTo(500));
        }
        else
        {
            Upgrade();
            Assert.That(wallet.Balance, Is.EqualTo(480));
        }
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EntireNewRunNotificationSequenceBlocksPaidSubmission(bool beforeService)
    {
        service.Dispose();
        int calls = 0;
        Action<Guid> callback = _ =>
        {
            calls++;
            Assert.That(service.TryUpgrade(service.GetQuote(definition), out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.Busy));
        };
        if (beforeService) wallet.RunStarted += callback;
        service = TrapUpgradeService.ForWallet(wallet);
        if (!beforeService) wallet.RunStarted += callback;
        wallet.BalanceChanged += _ =>
        {
            Assert.That(service.TryUpgrade(service.GetQuote(definition), out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(TrapUpgradeFailure.Busy));
        };
        wallet.BeginRun(100);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangedBranchEffectAdapterOrRunInsideValidationCannotDebit(bool changeRun)
    {
        service.Dispose();
        var catalog = ScriptableObject.CreateInstance<TrapUpgradeCatalog>();
        objects.Add(catalog);
        var entry = new TrapUpgradeCatalog.Entry { trapId = definition.TrapId };
        entry.branches.Add(new TrapUpgradeCatalog.Branch { branchId = "future", displayName = "Future", integrated = true });
        Set(catalog, "entries", new List<TrapUpgradeCatalog.Entry> { entry });
        bool changed = false;
        service = TrapUpgradeService.ForWallet(wallet, catalog, (_, __) =>
        {
            if (changed && changeRun) wallet.BeginRun(100);
            return !changed || changeRun;
        });
        Upgrade();
        Upgrade();
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition, TrapUpgradeDirection.Branch, "future"),
            out var pending, out _), Is.True);
        changed = true;
        using (pending) Assert.That(pending.Commit(out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(changeRun ? 100 : 440));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(wallet.Balance));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(changeRun ? 1 : 3));
    }

    [Test]
    public void ConfigAndPriceChangesAfterReservationRejectWithoutDebit()
    {
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        Set(definition, "cost", 80);
        using (pending) Assert.That(pending.Commit(out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(500));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(500));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
    }

    private TrapUpgradeCatalog.Branch BranchService()
    {
        service.Dispose();
        var catalog = ScriptableObject.CreateInstance<TrapUpgradeCatalog>();
        objects.Add(catalog);
        var branch = new TrapUpgradeCatalog.Branch { branchId = "future", displayName = "Future", integrated = true };
        var entry = new TrapUpgradeCatalog.Entry { trapId = definition.TrapId };
        entry.branches.Add(branch);
        Set(catalog, "entries", new List<TrapUpgradeCatalog.Entry> { entry });
        service = TrapUpgradeService.ForWallet(wallet, catalog, (_, __) => true);
        Upgrade();
        Upgrade();
        return branch;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BranchAvailabilityIsRecheckedAtCommit(bool disable)
    {
        var branch = BranchService();
        var quote = service.GetQuote(definition, TrapUpgradeDirection.Branch, "future");
        Assert.That(service.TryReserveUpgrade(quote, out var pending, out _), Is.True);
        branch.integrated = !disable;
        using (pending) Assert.That(pending.Commit(out _), Is.EqualTo(!disable));
        Assert.That(wallet.Balance, Is.EqualTo(disable ? 440 : 380));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(wallet.Balance));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(disable ? 3 : 4));
    }

    [Test]
    public void CatalogPriceChangeInvalidatesReservedQuote()
    {
        var branch = BranchService();
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition, TrapUpgradeDirection.Branch, "future"),
            out var pending, out _), Is.True);
        branch.overrideFixedCost = true;
        branch.fixedCost = 99;
        using (pending) Assert.That(pending.Commit(out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(440));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(440));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(3));
    }

    [Test]
    public void ForeignQuotesAndMissingBranchNeverCharge()
    {
        var rules = new TrapUpgradeService(() => wallet.RunId);
        Assert.That(service.TryUpgrade(rules.GetQuote(definition), out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(500));
        Upgrade();
        Upgrade();
        Assert.That(service.TryUpgrade(service.GetQuote(definition, TrapUpgradeDirection.Branch, "missing"), out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(440));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(3));
    }

    [Test]
    public void ListenerExceptionsDoNotUndoPaymentOrSkipOtherNotifications()
    {
        int calls = 0;
        Trap(definition).UpgradeStateChanged += (_, __) => throw new InvalidOperationException("instance-test");
        wallet.BalanceChanged += _ => throw new InvalidOperationException("balance-test");
        service.Upgraded += (_, __) => throw new InvalidOperationException("upgrade-test");
        service.Upgraded += (_, __) => calls++;
        foreach (string message in new[] { "instance-test", "balance-test", "upgrade-test" })
            LogAssert.Expect(LogType.Exception, new Regex(message));
        Upgrade();
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.EqualTo(480));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(2));
    }

    [TestCase("instance")]
    [TestCase("balance")]
    public void NewRunInsideCommittedCallbacksSuppressesOldUpgradeNotifications(string source)
    {
        int upgraded = 0;
        service.Upgraded += (_, __) => upgraded++;
        if (source == "instance")
            Trap(definition).UpgradeStateChanged += (_, state) => { if (state.Level == 2) wallet.BeginRun(100); };
        else
        {
            bool once = false;
            wallet.BalanceChanged += _ => { if (!once) { once = true; wallet.BeginRun(100); } };
        }
        Upgrade();
        Assert.That(upgraded, Is.Zero);
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
    }

    [Test]
    public void NewRunInsideInstanceCallbackStopsRemainingObsoleteRefreshListeners()
    {
        TrapInstance trap = Trap(definition);
        var levels = new List<int>();
        trap.UpgradeStateChanged += (_, state) => { if (state.Level == 2) wallet.BeginRun(100); };
        trap.UpgradeStateChanged += (_, state) => levels.Add(state.Level);
        Upgrade();
        Assert.That(levels, Is.EqualTo(new[] { 1 }));
        Assert.That(wallet.Balance, Is.EqualTo(100));
    }

    [Test]
    public void OnePaidServicePerWalletAndDisposalReleasesPendingFunds()
    {
        Assert.That(TrapUpgradeService.ForWallet(wallet), Is.SameAs(service));
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        service.Dispose();
        service.Dispose();
        Assert.That(wallet.AvailableBalance, Is.EqualTo(500));
        Assert.That(pending.Commit(out _), Is.False);
        Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.False);
        service = TrapUpgradeService.ForWallet(wallet);
        Upgrade();
        Assert.That(wallet.Balance, Is.EqualTo(480));
    }

    [Test]
    public void WalletDestructionDisposesServiceAndInvalidatesPendingPayment()
    {
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        // EditMode does not dispatch OnDestroy for a component that has never run Awake.
        Invoke(wallet, "OnDestroy");
        Object.DestroyImmediate(wallet.gameObject);
        Assert.That(service.IsDisposed, Is.True);
        Assert.That(pending.Commit(out _), Is.False);
    }

    [Test]
    public void NewRunRefreshesInactiveTrapsEvenWhenActiveRegistryWasReset()
    {
        TrapInstance trap = Trap(definition);
        trap.gameObject.SetActive(false);
        typeof(TrapInstance).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        int refreshes = 0;
        trap.UpgradeStateChanged += (_, state) => { Assert.That(state.Level, Is.EqualTo(1)); refreshes++; };
        wallet.BeginRun(100);
        Assert.That(refreshes, Is.EqualTo(1));
    }

    [Test]
    public void DomainReloadDisabledResetDisposesOldServiceAndReinitializesWallet()
    {
        Invoke(wallet, "Awake");
        service.Dispose();
        service = TrapUpgradeRuntime.GetOrCreate();
        Upgrade();
        var old = service;
        Assert.That(service.TryReserveUpgrade(service.GetQuote(definition), out var pending, out _), Is.True);
        ResetRuntime();
        Assert.That(old.IsDisposed, Is.True);
        Assert.That(wallet.RunId, Is.EqualTo(Guid.Empty));
        Assert.That(pending.Commit(out _), Is.False);
        service = TrapUpgradeRuntime.GetOrCreate();
        Assert.That(service, Is.Not.SameAs(old));
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(100));
        typeof(TrapUpgradeRuntime).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    [Test]
    public void UpgradeAppliesIndependentEffectiveValuesAndPreservesHealthRatio()
    {
        TrapInstance trap = Trap(definition);
        Set(trap, "<CurrentHealth>k__BackingField", 50f);

        Upgrade();

        Assert.That(trap.UpgradeLevel, Is.EqualTo(2));
        Assert.That(trap.UpgradeDirection, Is.EqualTo(TrapUpgradeDirection.None));
        Assert.That(trap.BaseMaxHealth, Is.EqualTo(100f));
        Assert.That(trap.MaxHealth, Is.EqualTo(125f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(62.5f).Within(0.001f));
        Assert.That(trap.GetEffectiveDamage(12f), Is.EqualTo(15f));
        Assert.That(trap.GetEffectiveRepair(25f), Is.EqualTo(31.25f));

        Set(trap, "<CurrentHealth>k__BackingField", 0f);
        Upgrade();
        Assert.That(trap.CurrentHealth, Is.Zero);
        Assert.That(trap.MaxHealth, Is.EqualTo(150f));
    }

    [Test]
    public void PlacedAndReenabledInstancesSynchronizeWithoutStackingMaxHealth()
    {
        Upgrade();
        TrapInstance placed = Trap(definition);
        Assert.That(placed.UpgradeLevel, Is.EqualTo(2));
        Assert.That(placed.MaxHealth, Is.EqualTo(125f));
        Assert.That(placed.CurrentHealth, Is.EqualTo(125f));

        Set(placed, "<CurrentHealth>k__BackingField", 62.5f);
        placed.gameObject.SetActive(false);
        Upgrade();
        Assert.That(placed.MaxHealth, Is.EqualTo(150f));
        Assert.That(placed.CurrentHealth, Is.EqualTo(75f).Within(0.001f));
        placed.gameObject.SetActive(true);
        Invoke(placed, "OnEnable");
        Assert.That(placed.MaxHealth, Is.EqualTo(150f));
        Assert.That(placed.CurrentHealth, Is.EqualTo(75f).Within(0.001f));
    }

    [Test]
    public void SceneAuthoredInstanceReadsRunUpgradeAfterAwake()
    {
        var host = new GameObject("Scene-authored upgrade trap");
        objects.Add(host);
        TrapInstance trap = host.AddComponent<TrapInstance>();
        Set(trap, "definition", definition);
        traps.Add(trap);
        Upgrade();

        Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");

        Assert.That(trap.UpgradeLevel, Is.EqualTo(2));
        Assert.That(trap.MaxHealth, Is.EqualTo(125f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(125f));
    }
}
