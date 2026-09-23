#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class TrapPurchaseTests
{
    private readonly List<Object> objects = new List<Object>();
    private TrapPlacementGrid grid;
    private TrapDefinition definition;
    private EconomyManager wallet;

    [SetUp]
    public void SetUp()
    {
        grid = Track(new GameObject("Purchase test grid")).AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(6, 1, 1f, 0f, new[] { true, true, true, true, true, true });
        definition = Track(ScriptableObject.CreateInstance<TrapDefinition>());
        SetField(definition, "cost", 40);
        wallet = EconomyManager.GetOrCreate();
        Track(wallet.gameObject);
        wallet.BeginRun(100);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [TestCase(100, 60)]
    [TestCase(40, 0)]
    public void SuccessfulPurchaseChargesOnceAfterInstanceAndOccupancyExist(int money, int remaining)
    {
        wallet.BeginRun(money);
        int events = 0;
        wallet.BalanceChanged += balance =>
        {
            events++;
            Assert.That(balance, Is.EqualTo(remaining));
            Assert.That(grid.TryGetTrapAtCell(Vector2Int.zero, out TrapInstance trap), Is.True);
            Assert.That(trap.Definition, Is.SameAs(definition));
        };
        Assert.That(Buy(0, out TrapInstance instance), Is.True);
        Assert.That(instance, Is.Not.Null);
        Assert.That(wallet.Balance, Is.EqualTo(remaining));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(remaining));
        Assert.That(events, Is.EqualTo(1));
    }

    [Test]
    public void InsufficientBalanceHasNoSideEffects()
    {
        wallet.BeginRun(39);
        Assert.That(Buy(0, out TrapInstance instance), Is.False);
        Assert.That(instance, Is.Null);
        AssertEmpty(39);
    }

    [TestCase("AutoSentryTurret", "TrapPlacementGrid_Road")]
    [TestCase("DualMissileLauncherLoaded", "TrapPlacementGrid_Road")]
    [TestCase("AutoSentryTurret", "TrapPlacementGrid_Ground")]
    [TestCase("DualMissileLauncherLoaded", "TrapPlacementGrid_Ground")]
    [TestCase("AutoSentryTurret", "TrapPlacementGrid_Platform")]
    [TestCase("DualMissileLauncherLoaded", "TrapPlacementGrid_Platform")]
    public void ShippedTurretsHaveAffordablePreviewOnSupportedSurfaces(string resource, string gridName)
    {
        definition = Resources.Load<TrapDefinition>(resource);
        Assert.That(definition, Is.Not.Null, resource);
        grid.name = gridName;
        bool[] mask = new bool[64];
        Array.Fill(mask, true);
        grid.ConfigureLayout(8, 8, 1f, 0f, mask);
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, definition, out string failure), Is.True, failure);
        GameObject host = Track(new GameObject("Road preview controller"));
        host.SetActive(false);
        var controller = host.AddComponent<TrapPlacementController>();
        SetField(controller, "selectedTrap", definition);
        SetField(controller, "activeGrid", grid);
        SetField(controller, "placementMode", true);
        SetField(controller, "hasHoveredCell", true);
        SetField(controller, "hoveredCell", Vector2Int.zero);
        Assert.That(controller.HasValidPreview, Is.True);
        wallet.BeginRun(definition.Cost - 1);
        Assert.That(controller.HasValidPreview, Is.False, "Road compatibility must not bypass the price.");
    }

    [Test]
    public void RoadLauncherPurchaseRemainsPassableAndChargesOnlyOnce()
    {
        definition = Resources.Load<TrapDefinition>("DualMissileLauncherLoaded");
        Assert.That(definition, Is.Not.Null);
        grid.name = "TrapPlacementGrid_Road";
        bool[] mask = new bool[64];
        Array.Fill(mask, true);
        grid.ConfigureLayout(8, 8, 1f, 0f, mask);
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, definition,
            out TrapInstance instance, out string failure), Is.True, failure);
        Assert.That(wallet.Balance, Is.EqualTo(100 - definition.Cost));
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        Assert.That(colliders, Is.Not.Empty);
        foreach (Collider collider in colliders) Assert.That(collider.isTrigger, Is.True);
        Assert.That(Buy(0, out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(100 - definition.Cost));
        // Older saved launchers may have the solid targeting collider.
        foreach (Collider collider in colliders) collider.isTrigger = false;
        grid.RebuildOccupancy();
        foreach (Collider collider in colliders) Assert.That(collider.isTrigger, Is.True);
        Assert.That(grid.IsOccupied(Vector2Int.zero), Is.True);
    }

    [Test]
    public void NewOrdinaryTrapsCanUseRoadWithoutPrefabWhitelist()
    {
        grid.name = "TrapPlacementGrid_Road";
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, definition,
            out TrapInstance instance, out string failure), Is.True, failure);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            Assert.That(collider.isTrigger, Is.True);
    }

    [TestCase(TrapPlacementGrid.SurfaceKind.Ground, "Ground", true)]
    [TestCase(TrapPlacementGrid.SurfaceKind.Road, "Road", true)]
    [TestCase(TrapPlacementGrid.SurfaceKind.Platform, "Renamed raised deck", false)]
    [TestCase(TrapPlacementGrid.SurfaceKind.Legacy, "TrapPlacementGrid_Platform", false)]
    public void FloorTrapsOnlyAllowGroundEvenWhenPlatformHasFlatSupport(
        TrapPlacementGrid.SurfaceKind surface, string gridName, bool allowed)
    {
        SetField(definition, "walkableFloorTrap", true);
        grid.name = gridName;
        // Both surfaces have the same valid support; height alone is not a surface rule.
        grid.transform.position = Vector3.up * 3f;
        grid.ConfigureLayout(6, 1, 1f, 0.02f, new[] { true, true, true, true, true, true }, surface);
        GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floor.transform.position = new Vector3(2f, 2.5f, 0f);
        floor.transform.localScale = new Vector3(10f, 1f, 4f);
        Physics.SyncTransforms();
        Assert.That(grid.IsOpenForTrap(Vector2Int.zero, definition), Is.EqualTo(allowed));
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, definition, out _), Is.EqualTo(allowed));
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, definition,
            out _, out string failure), Is.EqualTo(allowed), failure);
        if (allowed) Assert.That(wallet.Balance, Is.EqualTo(60));
        else
        {
            Assert.That(failure, Does.Contain("不能放在高台"));
            AssertEmpty(100);
        }
    }

    [Test]
    public void InvalidLocationsAndPreviewDoNotSpend()
    {
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, definition, out _), Is.True);
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(Buy(-1, out _), Is.False);
        grid.SetOpen(Vector2Int.zero, false);
        Assert.That(Buy(0, out _), Is.False);
        AssertEmpty(100);
        Assert.That(Buy(1, out _), Is.True);
        Assert.That(Buy(1, out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(1));
    }

    [TestCase(0, true)]
    [TestCase(-1, false)]
    public void ZeroPriceIsExplicitlyAllowedButNegativePriceIsRejected(int cost, bool succeeds)
    {
        SetField(definition, "cost", cost);
        wallet.BeginRun(0);
        Assert.That(Buy(0, out _), Is.EqualTo(succeeds));
        Assert.That(wallet.Balance, Is.Zero);
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(succeeds ? 1 : 0));
    }

    [Test]
    public void MissingOrUninitializedWalletNeverAllowsFreePlacement()
    {
        typeof(EconomyManager).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        Assert.That(Buy(0, out _), Is.False);
        typeof(EconomyManager).GetProperty("Instance").SetValue(null, wallet);
        Assert.That(Buy(0, out _), Is.False);
        Assert.That(grid.PlacedTraps, Is.Empty);
    }

    [Test]
    public void CreationExceptionReleasesReservationAndLeavesNoObject()
    {
        // A scene object is not a prefab asset: the editor factory must reject it.
        GameObject invalidPrefab = Track(new GameObject("Not a prefab asset"));
        SetField(definition, "prefab", invalidPrefab);
        int events = 0;
        wallet.BalanceChanged += _ => events++;
        Assert.That(Buy(0, out _), Is.False);
        AssertEmpty(100);
        Assert.That(events, Is.Zero);
        SetField(definition, "prefab", null);
        Assert.That(Buy(0, out _), Is.True, "Failed creation must release reserved funds.");
    }

    [Test]
    public void ReentrantPurchaseCannotSpendReservedMoney()
    {
        wallet.BeginRun(40);
        bool? nestedPurchase = null;
        var probe = grid.gameObject.AddComponent<TrapPurchaseCallbackProbe>();
        probe.Callback = () =>
        {
            Assert.That(wallet.Balance, Is.EqualTo(40), "No temporary debit is published.");
            Assert.That(wallet.AvailableBalance, Is.Zero);
            nestedPurchase = Buy(1, out _);
            Assert.That(wallet.TrySpend(1), Is.False);
        };
        Assert.That(Buy(0, out _), Is.True);
        Assert.That(nestedPurchase, Is.False);
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.Zero);
    }

    [Test]
    public void ReentrantSameCellPlacementRollsBackOuterObjectAndReservation()
    {
        bool? nestedPurchase = null;
        var probe = grid.gameObject.AddComponent<TrapPurchaseCallbackProbe>();
        probe.Callback = () => nestedPurchase = Buy(0, out _);
        Assert.That(Buy(0, out TrapInstance outer), Is.False);
        Assert.That(nestedPurchase, Is.True);
        Assert.That(outer, Is.Null);
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(1));
        Assert.That(grid.transform.childCount, Is.EqualTo(1), "Partial outer root must be removed.");
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(60));
    }

    [Test]
    public void RunChangeDuringCreationRemovesInitializedTrapWithoutChargingNewRun()
    {
        var probe = grid.gameObject.AddComponent<TrapPurchaseCallbackProbe>();
        probe.Callback = () => wallet.BeginRun(25);
        Assert.That(Buy(0, out TrapInstance instance), Is.False);
        Assert.That(instance, Is.Null);
        AssertEmpty(25);
    }

    [Test]
    public void FailingBalanceListenerCannotUndoCommittedPurchaseOrBlockOtherListeners()
    {
        int observed = -1;
        wallet.BalanceChanged += _ => throw new InvalidOperationException("Purchase listener test failure");
        wallet.BalanceChanged += balance => observed = balance;
        LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Purchase listener test failure"));
        Assert.That(Buy(0, out _), Is.True);
        Assert.That(observed, Is.EqualTo(60));
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.EqualTo(60));
    }

    [Test]
    public void DragPlacementChargesEachSuccessAndStopsWhenFundsRunOut()
    {
        GameObject host = Track(new GameObject("Purchase controller"));
        host.SetActive(false);
        var controller = host.AddComponent<TrapPlacementController>();
        SetField(controller, "selectedTrap", definition);
        SetField(controller, "activeGrid", grid);
        SetField(controller, "dragPlacement", true);
        SetField(controller, "hasLastPlacedCell", true);
        SetField(controller, "lastPlacedCell", Vector2Int.zero);
        Invoke(controller, "PlaceAt", Vector2Int.zero);
        Invoke(controller, "PlaceAlongLine", Vector2Int.zero, new Vector2Int(5, 0));
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(2));
        Assert.That(wallet.Balance, Is.EqualTo(20));
        Assert.That(GetField(controller, "dragPlacement"), Is.False);
        Assert.That(GetField(controller, "hasLastPlacedCell"), Is.False);
        Assert.That(controller.LastPlacementFailure, Does.Contain("金币不足"));
    }

    [Test]
    public void EditorBatchPlacementIsFreeAndAtomicFailureDoesNotCreateAnything()
    {
        Assert.That(grid.TryPlaceTraps(new[] { Vector2Int.zero, Vector2Int.zero }, definition,
            out var failed, out _, true), Is.Zero);
        Assert.That(failed, Is.Empty);
        AssertEmpty(100);
        Object.DestroyImmediate(wallet.gameObject);
        Assert.That(grid.TryPlaceTraps(new[] { Vector2Int.zero, Vector2Int.right }, definition,
            out var placed, out _, true), Is.EqualTo(2));
        Assert.That(placed.Count, Is.EqualTo(2));
        Assert.That(EconomyManager.Instance == null, Is.True);
    }

    [Test]
    public void RemovalDoesNotRefundPurchase()
    {
        Assert.That(Buy(0, out TrapInstance instance), Is.True);
        Assert.That(grid.RemoveTrap(instance), Is.True);
        AssertEmpty(60);
    }

    private bool Buy(int x, out TrapInstance instance) =>
        TrapPurchaseService.TryPurchase(grid, new Vector2Int(x, 0), definition, out instance, out _);

    private void AssertEmpty(int balance)
    {
        Assert.That(wallet.Balance, Is.EqualTo(balance));
        Assert.That(wallet.AvailableBalance, Is.EqualTo(balance));
        Assert.That(grid.PlacedTraps, Is.Empty);
        Assert.That(grid.transform.childCount, Is.Zero);
        for (int x = 0; x < grid.Columns; x++)
            Assert.That(grid.IsOccupied(new Vector2Int(x, 0)), Is.False);
    }

    private T Track<T>(T value) where T : Object { objects.Add(value); return value; }
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
#endif
