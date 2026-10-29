#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class TrapUpgradeMenuTests
{
    private EconomyManager wallet;
    private TrapUpgradeService service;
    private GameObject menuHost, placementHost;
    private static readonly string[] Ids = { "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded", "flamethrower", "betty7", "throat-lamp", "repair-station" };

    [SetUp] public void SetUp()
    {
        wallet = EconomyManager.GetOrCreate();
        wallet.BeginRun(10000);
        service = TrapUpgradeRuntime.GetOrCreate();
    }

    [TearDown] public void TearDown()
    {
        if (menuHost != null) Object.DestroyImmediate(menuHost);
        if (placementHost != null) Object.DestroyImmediate(placementHost);
        if (wallet != null) Object.DestroyImmediate(wallet.gameObject);
        foreach (var system in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(system.gameObject);
    }

    private static TrapDefinition Definition(string id) => Resources.LoadAll<TrapDefinition>("").First(d => d.TrapId == id);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static bool Confirm(TrapUpgradeMenuSession session)
    {
        var quote = session.Quote;
        return session.Confirm(quote.TrapId, quote.Current.Level, quote.Version);
    }

    [TestCaseSource(nameof(Ids))]
    public void SevenTrapsUpgradeThroughNormalFourWithoutPlacedInstances(string id)
    {
        var session = new TrapUpgradeMenuSession(service);
        var definition = Definition(id);
        int balance = wallet.Balance;
        for (int level = 1; level <= 3; level++)
        {
            session.Open(definition);
            var quote = session.Quote;
            Assert.That(quote.Current.Level, Is.EqualTo(level));
            Assert.That(quote.CanUpgrade, Is.True, quote.Message);
            if (level == 3)
            {
                Assert.That(Confirm(session), Is.False);
                Assert.That(session.Step, Is.EqualTo(TrapUpgradeMenuSession.DialogStep.Direction));
                Assert.That(wallet.Balance, Is.EqualTo(balance));
                session.ChooseNormal();
            }
            Assert.That(Confirm(session), Is.True);
            balance -= quote.Cost;
            Assert.That(wallet.Balance, Is.EqualTo(balance));
            Assert.That(service.GetState(id).Level, Is.EqualTo(level + 1));
            Assert.That(Confirm(session), Is.False, "Result dialog must not accept a repeated click");
            Assert.That(wallet.Balance, Is.EqualTo(balance));
        }
        Assert.That(service.GetState(id).Direction, Is.EqualTo(TrapUpgradeDirection.Normal));
        Assert.That(session.QuoteFor(definition).Failure, Is.EqualTo(TrapUpgradeFailure.MaxLevel));
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryTrapPreviewUsesAuthoredBaseAndQuotedMultipliers(string id)
    {
        var definition = Definition(id);
        var quote = new TrapUpgradeMenuSession(service).QuoteFor(definition);
        string preview = TrapUpgradeMenuSession.AttributePreview(definition, quote);
        var prefab = definition.Prefab;
        float baseHealth = prefab.TryGetComponent(out TrapInstance trap) ? trap.BaseMaxHealth : TrapInstance.DefaultBaseMaxHealth;
        StringAssert.Contains($"最大血量：{baseHealth * quote.CurrentMultipliers.MaxHealth:0.##} → {baseHealth * quote.TargetMultipliers.MaxHealth:0.##}", preview);
        float damage = 0;
        string name = "";
        if (prefab.TryGetComponent(out RepairStationTrap repair))
        { damage = repair.BaseRepairAmount; name = "每次维修量"; }
        if (prefab.TryGetComponent(out ThroatLampTrap lamp))
        {
            damage = lamp.BaseDirectDamage; name = "直击伤害";
            StringAssert.Contains($"溅射伤害：{lamp.BaseSplashDamage:0.##} → {lamp.BaseSplashDamage * quote.TargetMultipliers.Damage:0.##}", preview);
        }
        if (prefab.TryGetComponent(out Betty7JumpMineTrap betty)) { damage = betty.BaseDamage; name = "爆炸伤害"; }
        if (prefab.TryGetComponent(out AutoSentryTurret sentry)) { damage = sentry.BaseDamagePerShot; name = "每发伤害"; }
        if (prefab.TryGetComponent(out GroundSpikeTrap spike)) { damage = spike.BaseDamage; name = "刺击伤害"; }
        if (prefab.TryGetComponent(out MissileLauncherWeapon missile)) { damage = missile.DamagePerMissile; name = "导弹直击"; }
        if (prefab.TryGetComponent(out FlamethrowerTrap fire)) { damage = fire.BaseDirectDamage; name = "直击伤害"; }
        float multiplier = id == "repair-station" ? quote.TargetMultipliers.Repair : quote.TargetMultipliers.Damage;
        StringAssert.Contains($"{name}：{damage:0.##} → {damage * multiplier:0.##}", preview);
    }

    [Test] public void CancelNeverPaysOrChangesLevel()
    {
        var session = new TrapUpgradeMenuSession(service);
        session.Open(Definition("repair-station"));
        session.Cancel();
        Assert.That(session.IsOpen, Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(10000));
        Assert.That(service.GetState("repair-station").Level, Is.EqualTo(1));
    }

    [Test] public void FundsCanRunOutWhileDialogIsOpenAndRetryAfterGrantWorks()
    {
        var session = new TrapUpgradeMenuSession(service);
        session.Open(Definition("betty7"));
        wallet.TrySpend(wallet.Balance);
        Assert.That(Confirm(session), Is.False);
        StringAssert.Contains("金币不足", session.Feedback);
        Assert.That(service.GetState("betty7").Level, Is.EqualTo(1));
        wallet.Grant(1000);
        Assert.That(Confirm(session), Is.True);
    }

    [Test] public void ConcurrentUpgradeRejectsOldQuoteWithoutASecondPayment()
    {
        var session = new TrapUpgradeMenuSession(service);
        var definition = Definition("ground-spike");
        session.Open(definition);
        Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.True);
        int balance = wallet.Balance;
        Assert.That(Confirm(session), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(balance));
        StringAssert.Contains("过期", session.Feedback);
        Assert.That(session.Quote.Current.Level, Is.EqualTo(2));
    }

    [Test] public void NewRunRejectsPendingQuoteEvenWhenLevelAndVersionMatch()
    {
        var session = new TrapUpgradeMenuSession(service);
        session.Open(Definition("auto_sentry_turret"));
        wallet.BeginRun(1000);
        Assert.That(Confirm(session), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(1000));
    }

    [Test] public void WrongIdentityOrExpectedLevelOrVersionCannotPay()
    {
        var session = new TrapUpgradeMenuSession(service);
        session.Open(Definition("repair-station"));
        var quote = session.Quote;
        Assert.That(session.Confirm("betty7", 1, quote.Version), Is.False);
        Assert.That(session.Confirm(quote.TrapId, 2, quote.Version), Is.False);
        Assert.That(session.Confirm(quote.TrapId, 1, quote.Version + 1), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(10000));
    }

    [Test] public void DirectionBackAndCancelNeverPay()
    {
        var definition = Definition("throat-lamp");
        Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.True);
        Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.True);
        var session = new TrapUpgradeMenuSession(service);
        session.Open(definition);
        int balance = wallet.Balance;
        Confirm(session);
        session.ChooseNormal();
        session.Back();
        Assert.That(session.Step, Is.EqualTo(TrapUpgradeMenuSession.DialogStep.Direction));
        session.Back();
        Assert.That(session.Step, Is.EqualTo(TrapUpgradeMenuSession.DialogStep.Confirm));
        session.Back();
        Assert.That(session.IsOpen, Is.False);
        Assert.That(service.GetState(definition.TrapId).Level, Is.EqualTo(3));
        Assert.That(wallet.Balance, Is.EqualTo(balance));
    }

    private TrapSelectionMenuView MakeMenu()
    {
        placementHost = new GameObject("Test Placement");
        var placement = placementHost.AddComponent<TrapPlacementController>();
        Invoke(placement, "Awake");
        menuHost = new GameObject("Test Menu");
        var owner = menuHost.AddComponent<TrapSelectionMenu>();
        Invoke(owner, "Awake");
        Invoke(owner, "OnEnable");
        Invoke(owner, "SetOpen", true);
        return Field<TrapSelectionMenuView>(owner, "view");
    }

    [Test] public void UpgradeArrowDoesNotAssignSlotOrCloseMenuAndModalBlocksUnderlyingActions()
    {
        var view = MakeMenu();
        var owner = menuHost.GetComponent<TrapSelectionMenu>();
        var placement = placementHost.GetComponent<TrapPlacementController>();
        var previous = placement.GetTrapSlot(placement.SelectedSlot);
        view.SetUpgradePage(true);
        var card = Field<TrapSelectionMenuCardView[]>(view, "cards")[0];
        var arrow = Field<Button>(card, "upgradeButton");
        Assert.That(arrow.gameObject.activeSelf, Is.True);
        ExecuteEvents.Execute(arrow.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        Assert.That(view.UpgradeDialogOpen, Is.True);
        Assert.That(TrapSelectionMenu.IsOpen, Is.True);
        Assert.That(TrapSelectionMenu.CursorOwned, Is.True);
        Assert.That(placement.GetTrapSlot(placement.SelectedSlot), Is.SameAs(previous));
        Invoke(owner, "AssignTrap", Definition("betty7"));
        Invoke(owner, "ClearSelectedSlot");
        Assert.That(placement.GetTrapSlot(placement.SelectedSlot), Is.SameAs(previous));
        Assert.That(Field<CanvasGroup>(view, "upgradeBackgroundGroup").interactable, Is.False);
        Assert.That(view.BackFromUpgrade(), Is.True);
        Assert.That(view.UpgradeDialogOpen, Is.False);
        Assert.That(TrapSelectionMenu.IsOpen, Is.True);
    }

    [Test] public void SelectionPageStillAssignsAndClosesWithoutCharging()
    {
        var view = MakeMenu();
        var card = Field<TrapSelectionMenuCardView[]>(view, "cards")[0];
        var expected = Field<TrapDefinition>(card, "definition");
        Field<Button>(card, "button").onClick.Invoke();
        Assert.That(placementHost.GetComponent<TrapPlacementController>().GetTrapSlot(0), Is.SameAs(expected));
        Assert.That(TrapSelectionMenu.IsOpen, Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(10000));
    }

    [Test] public void BalanceAndUpgradeEventsRefreshDialogAndCardsWithoutPollingLocalLevels()
    {
        var view = MakeMenu();
        view.SetUpgradePage(true);
        view.OpenUpgrade(Definition("repair-station"));
        wallet.TrySpend(wallet.Balance);
        Assert.That(Field<TextMeshProUGUI>(view, "upgradeBalance").text, Is.EqualTo("金币：0"));
        view.ConfirmUpgrade();
        StringAssert.Contains("金币不足", Field<TextMeshProUGUI>(view, "upgradeFeedback").text);
        wallet.Grant(1000);
        view.ConfirmUpgrade();
        StringAssert.Contains("升级成功", Field<TextMeshProUGUI>(view, "upgradeFeedback").text);
        int balance = wallet.Balance;
        view.ConfirmUpgrade();
        Assert.That(wallet.Balance, Is.EqualTo(balance));
        wallet.BeginRun(500);
        Assert.That(view.UpgradeDialogOpen, Is.False);
        Assert.That(service.GetState("repair-station").Level, Is.EqualTo(1));
    }

    [Test] public void MaxLevelCardIsDisabledAndIncrementalControlsPreserveExistingLayout()
    {
        var view = MakeMenu();
        var existing = view.GetComponentsInChildren<TextMeshProUGUI>(true).ToDictionary(t => t, t => (t.font, t.rectTransform.anchoredPosition, t.rectTransform.sizeDelta));
        int controls = view.GetComponentsInChildren<RectTransform>(true).Length;
        view.EnsureUpgradeUI(); view.EnsureUpgradeUI();
        Assert.That(view.GetComponentsInChildren<RectTransform>(true).Length, Is.EqualTo(controls));
        foreach (var item in existing)
        {
            Assert.That(item.Key.font, Is.SameAs(item.Value.Item1));
            Assert.That(item.Key.rectTransform.anchoredPosition, Is.EqualTo(item.Value.Item2));
            Assert.That(item.Key.rectTransform.sizeDelta, Is.EqualTo(item.Value.Item3));
        }
        var definition = Definition("repair-station");
        service.TryUpgrade(service.GetQuote(definition), out _);
        service.TryUpgrade(service.GetQuote(definition), out _);
        service.TryUpgrade(service.GetQuote(definition, TrapUpgradeDirection.Normal), out _);
        view.SetUpgradePage(true);
        var card = Field<TrapSelectionMenuCardView[]>(view, "cards").First(c => Field<TrapDefinition>(c, "definition") == definition);
        Assert.That(Field<Button>(card, "upgradeButton").interactable, Is.False);
        Assert.That(Field<TextMeshProUGUI>(card, "upgradeLabel").text, Is.EqualTo("已满级"));
    }
}
#endif
