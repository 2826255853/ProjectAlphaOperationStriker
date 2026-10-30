using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class TrapUpgradeHealthBarTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Ids = { "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded", "flamethrower", "betty7", "throat-lamp", "repair-station" };
    private readonly List<Object> owned = new List<Object>();
    private EconomyManager wallet;
    private TrapUpgradeService service;
    private bool wasOpen;
    private int releasedFrame;

    [SetUp] public void SetUp()
    {
        wasOpen = TrapSelectionMenu.IsOpen;
        releasedFrame = (int)typeof(TrapSelectionMenu).GetField("cursorReleasedFrame", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        MenuOpen(false);
        ReleasedFrame(-1);
        wallet = Own(new GameObject("Health bar test wallet")).AddComponent<EconomyManager>();
        Call(wallet, "Awake");
        wallet.BeginRun(100000);
        service = TrapUpgradeService.ForWallet(wallet);
    }

    [TearDown] public void TearDown()
    {
        service?.Dispose();
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] is GameObject host && host != null)
                foreach (TrapInstance trap in host.GetComponentsInChildren<TrapInstance>()) Call(trap, "OnDisable");
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
        owned.Clear();
        MenuOpen(wasOpen);
        ReleasedFrame(releasedFrame);
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryOfficialTrapReadsLiveLevelsAndNormalDirection(string id)
    {
        var definition = Definition(id);
        TrapInstance trap = Trap(definition);
        for (int level = 1; level <= 4; level++)
        {
            Assert.That(TrapHealthBarUI.GetLevelLabel(trap), Is.EqualTo(level < 4 ? $"等级 {level}" : "等级 4 · 普通"));
            if (level < 4) Upgrade(definition, level == 3 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);
        }
        TrapInstance newTrap = Trap(definition);
        Assert.That(TrapHealthBarUI.GetLevelLabel(newTrap), Is.EqualTo("等级 4 · 普通"));
        wallet.BeginRun(100000);
        Assert.That(TrapHealthBarUI.GetLevelLabel(trap), Is.EqualTo("等级 1"));
        Assert.That(TrapHealthBarUI.GetLevelLabel(newTrap), Is.EqualTo("等级 1"));
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryOfficialTrapUsesItsOwnFutureBranchDisplayName(string id)
    {
        var definition = Definition(id);
        service.Dispose();
        var catalog = Own(ScriptableObject.CreateInstance<TrapUpgradeCatalog>());
        var entry = new TrapUpgradeCatalog.Entry { trapId = id };
        entry.branches.Add(new TrapUpgradeCatalog.Branch { branchId = "test-only", displayName = "测试分支 · " + definition.DisplayName, integrated = true });
        Set(catalog, "entries", new List<TrapUpgradeCatalog.Entry> { entry });
        service = TrapUpgradeService.ForWallet(wallet, catalog, (trapId, branchId) => true);
        TrapInstance trap = Trap(definition);
        Upgrade(definition); Upgrade(definition); Upgrade(definition, TrapUpgradeDirection.Branch, "test-only");
        Assert.That(TrapHealthBarUI.GetLevelLabel(trap), Is.EqualTo("等级 4 · 测试分支 · " + definition.DisplayName));
        entry.branches.Clear();
        Assert.That(TrapHealthBarUI.GetLevelLabel(trap), Is.EqualTo("等级 4 · test-only"), "Missing branch metadata retains the committed branch identity.");
    }

    [TestCaseSource(nameof(Ids))]
    public void ColliderFreeAimLingersAndMenuImmediatelyHidesThenReacquires(string id)
    {
        TrapInstance trap = Trap(Definition(id));
        var camera = Own(new GameObject("Inspection camera")).AddComponent<Camera>();
        camera.transform.position = trap.transform.position - Vector3.forward * 10;
        var hud = Own(new GameObject("Inspection HUD")).AddComponent<TrapHealthBarUI>();
        Set(hud, "viewCamera", camera);
        Physics.SyncTransforms();
        Call(hud, "Update");
        Assert.That(Field<TrapInstance>(hud, "shownTrap"), Is.SameAs(trap));
        Assert.That(CanDraw(hud), Is.True);
        Assert.That(trap.GetComponent<Collider>(), Is.Null);
        camera.transform.rotation = Quaternion.Euler(0, 10, 0);
        Call(hud, "Update");
        Assert.That(Field<TrapInstance>(hud, "aimedTrap"), Is.Null);
        Assert.That(CanDraw(hud), Is.True, "Normal aim loss retains the existing linger.");
        MenuOpen(true);
        Assert.That(CanDraw(hud), Is.False, "Menu opened after HUD.Update must hide in the same frame.");
        Call(hud, "Update");
        Assert.That(Field<TrapInstance>(hud, "aimedTrap"), Is.Null);
        MenuOpen(false);
        ReleasedFrame(Time.frameCount);
        Assert.That(CanDraw(hud), Is.False, "Closing frame still owns the cursor.");
        ReleasedFrame(-1);
        camera.transform.rotation = Quaternion.identity;
        Call(hud, "Update");
        Assert.That(CanDraw(hud), Is.True);
        camera.transform.rotation = Quaternion.Euler(0, 10, 0);
        Set(hud, "hideAtTime", Time.unscaledTime - 1);
        Call(hud, "Update");
        Assert.That(Field<TrapInstance>(hud, "shownTrap"), Is.Null);
        Assert.That(CanDraw(hud), Is.False);
    }

    [Test]
    public void SolidOccluderBlocksColliderFreeTrapAndColliderHitStillResolvesTrap()
    {
        TrapInstance trap = Trap(Definition(Ids[0]));
        var camera = Own(new GameObject("Ray camera")).AddComponent<Camera>();
        camera.transform.position = trap.transform.position - Vector3.forward * 10;
        var hud = Own(new GameObject("Ray HUD")).AddComponent<TrapHealthBarUI>();
        Set(hud, "viewCamera", camera);
        var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = trap.transform.position - Vector3.forward * 5;
        Physics.SyncTransforms();
        Assert.That(Call(hud, "FindTrapAtCrosshair"), Is.Null);
        wall.SetActive(false);
        trap.gameObject.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        Assert.That(Call(hud, "FindTrapAtCrosshair"), Is.SameAs(trap));
    }

    [Test] public void LabelHandlesMissingTrapWithoutCreatingWallet()
    {
        service.Dispose();
        Object.DestroyImmediate(wallet.gameObject);
        Assert.That(TrapHealthBarUI.GetLevelLabel(null), Is.Empty);
        TrapInstance trap = Own(new GameObject("No definition")).AddComponent<TrapInstance>();
        Assert.That(TrapHealthBarUI.GetLevelLabel(trap), Is.EqualTo("等级 1"));
        Assert.That(EconomyManager.Instance == null, Is.True);
    }

    private static TrapDefinition Definition(string id) => Resources.LoadAll<TrapDefinition>("").First(d => d.TrapId == id);
    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    private TrapInstance Trap(TrapDefinition definition)
    {
        var trap = Own(new GameObject("HUD trap")).AddComponent<TrapInstance>();
        trap.transform.position = new Vector3(1234, 100, 1234);
        Call(trap, "Initialize", definition, Vector2Int.zero);
        Call(trap, "OnEnable");
        return trap;
    }
    private void Upgrade(TrapDefinition definition, TrapUpgradeDirection direction = TrapUpgradeDirection.None, string branch = null)
        => Assert.That(service.TryUpgrade(service.GetQuote(definition, direction, branch), out var failure), Is.True, failure.ToString());
    private static void MenuOpen(bool value) => typeof(TrapSelectionMenu).GetProperty("IsOpen").SetValue(null, value);
    private static void ReleasedFrame(int value) => typeof(TrapSelectionMenu).GetField("cursorReleasedFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private static bool CanDraw(TrapHealthBarUI hud) => (bool)typeof(TrapHealthBarUI).GetProperty("CanDrawPanel", Private).GetValue(hud);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static T Field<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
}
