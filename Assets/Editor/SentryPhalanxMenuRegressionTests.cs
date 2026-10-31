#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class SentryPhalanxMenuRegressionTests
{
    private const string SentryId = "auto_sentry_turret";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private EconomyManager wallet;
    private TrapUpgradeService service;
    private GameObject menuHost;

    [SetUp]
    public void SetUp()
    {
        wallet = EconomyManager.GetOrCreate();
        wallet.BeginRun(10000);
        service = TrapUpgradeRuntime.GetOrCreate();
    }

    [TearDown]
    public void TearDown()
    {
        if (menuHost != null) Object.DestroyImmediate(menuHost);
        if (wallet != null) Object.DestroyImmediate(wallet.gameObject);
        foreach (EventSystem system in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            Object.DestroyImmediate(system.gameObject);
    }

    [Test]
    public void ProductionCatalogRetainsIntegratedSentryPhalanxBranch()
    {
        TrapUpgradeCatalog catalog = Resources.Load<TrapUpgradeCatalog>(TrapUpgradeCatalog.ResourcePath);
        Assert.That(catalog, Is.Not.Null);
        TrapUpgradeCatalog.Branch branch = catalog.GetBranch(SentryId, SentryPhalanxBranchAdapter.BranchId);
        Assert.That(branch, Is.Not.Null, "Hiding a standalone trap must not remove its upgrade branch.");
        Assert.That(branch.integrated, Is.True);
    }

    [Test]
    public void HiddenStandalonePhalanxStillAppearsAndUpgradesThroughSentryMenu()
    {
        TrapSelectionMenuView view = OpenSentryDirections(out TrapDefinition definition);
        TrapSelectionMenu owner = menuHost.GetComponent<TrapSelectionMenu>();
        List<TrapDefinition> available = Field<List<TrapDefinition>>(owner, "available");
        Assert.That(available.Any(item => item.TrapId == SentryPhalanxBranchAdapter.BranchId), Is.False);
        Assert.That(available.Contains(definition), Is.True);
        Button branch = PhalanxButton(view);
        Assert.That(branch.gameObject.activeInHierarchy, Is.True);
        Assert.That(branch.IsInteractable(), Is.True);
        int balance = wallet.Balance;
        branch.onClick.Invoke();
        var session = Field<TrapUpgradeMenuSession>(view, "upgradeSession");
        Assert.That(session.Step, Is.EqualTo(TrapUpgradeMenuSession.DialogStep.Branch));
        Assert.That(session.SelectedBranchAvailable, Is.True, session.SelectedBranchUnavailableReason);
        Assert.That(wallet.Balance, Is.EqualTo(balance));
        int cost = session.Quote.Cost;
        view.ConfirmUpgrade();
        Assert.That(service.GetState(SentryId).Level, Is.EqualTo(4));
        Assert.That(service.GetState(SentryId).BranchId, Is.EqualTo(SentryPhalanxBranchAdapter.BranchId));
        Assert.That(wallet.Balance, Is.EqualTo(balance - cost));
    }

    [Test]
    public void SentryPhalanxOptionIsInsideVisibleDirectionViewport()
    {
        TrapSelectionMenuView view = OpenSentryDirections(out _);
        Canvas.ForceUpdateCanvases();
        var viewport = (RectTransform)Field<GameObject>(view, "directionOptionsPanel").transform;
        var button = (RectTransform)PhalanxButton(view).transform;
        var corners = new Vector3[4];
        button.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector3 local = viewport.InverseTransformPoint(corner);
            Assert.That(local.x, Is.InRange(viewport.rect.xMin - 1f, viewport.rect.xMax + 1f));
            Assert.That(local.y, Is.InRange(viewport.rect.yMin - 1f, viewport.rect.yMax + 1f),
                "The branch button must be visible without scrolling past the normal direction.");
        }
    }

    [Test]
    public void ReusedCanvasRestoresPhalanxWhenTransientDirectionCachesAreLost()
    {
        TrapSelectionMenuView view = OpenSentryDirections(out _);
        var panel = Field<GameObject>(view, "directionOptionsPanel");
        Button originalBranch = PhalanxButton(view);
        Assert.That(view.BackFromUpgrade(), Is.True);
        typeof(TrapSelectionMenuView).GetField("directionOptionsContent", Private).SetValue(view, null);
        ((System.Collections.IList)Field<object>(view, "directionOptionControls")).Clear();
        // Existing Canvas children can outlive the nonserialized control cache.
        originalBranch.gameObject.SetActive(false);
        view.ConfirmUpgrade();
        Button restoredBranch = PhalanxButton(view);
        Assert.That(restoredBranch, Is.SameAs(originalBranch), "Reuse existing authored controls.");
        Assert.That(restoredBranch.gameObject.activeInHierarchy, Is.True);
        Assert.That(panel.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(2));
        restoredBranch.onClick.Invoke();
        Assert.That(Field<TrapUpgradeMenuSession>(view, "upgradeSession").SelectedBranchAvailable, Is.True);
    }

    private TrapSelectionMenuView OpenSentryDirections(out TrapDefinition definition)
    {
        definition = Resources.LoadAll<TrapDefinition>("").First(item => item.TrapId == SentryId);
        for (int i = 0; i < 2; i++)
            Assert.That(service.TryUpgrade(service.GetQuote(definition), out _), Is.True);
        menuHost = new GameObject("Sentry Phalanx menu regression");
        var owner = menuHost.AddComponent<TrapSelectionMenu>();
        Invoke(owner, "Awake");
        Invoke(owner, "OnEnable");
        Invoke(owner, "SetOpen", true);
        var view = Field<TrapSelectionMenuView>(owner, "view");
        view.SetUpgradePage(true);
        view.OpenUpgrade(definition);
        view.ConfirmUpgrade();
        Assert.That(Field<TrapUpgradeMenuSession>(view, "upgradeSession").Step,
            Is.EqualTo(TrapUpgradeMenuSession.DialogStep.Direction));
        return view;
    }

    private static Button PhalanxButton(TrapSelectionMenuView view) =>
        Field<GameObject>(view, "directionOptionsPanel").GetComponentsInChildren<Button>(true)
            .Single(button => button.GetComponentInChildren<TextMeshProUGUI>(true).text.Contains("密集阵"));

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);
}
#endif
