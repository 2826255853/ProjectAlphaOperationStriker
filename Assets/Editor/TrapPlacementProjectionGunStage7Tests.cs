#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;

public sealed class TrapPlacementProjectionGunStage7Tests
{
    private GameObject host;
    private GameObject prefab;
    private TrapPlacementController placement;
    private TrapPlacementProjectionGunController gun;
    private readonly List<Object> owned = new List<Object>();
    private bool previousTrapMenuOpen;
    private bool previousStartMenuOpen;

    [SetUp]
    public void SetUp()
    {
        previousTrapMenuOpen = TrapSelectionMenu.IsOpen;
        previousStartMenuOpen = GameStartMenu.CursorOwned;
        SetStatic(typeof(TrapSelectionMenu), "IsOpen", false);
        SetStatic(typeof(GameStartMenu), "CursorOwned", false);
        host = new GameObject("Projection gun stage 7 test");
        placement = host.AddComponent<TrapPlacementController>();
        gun = host.AddComponent<TrapPlacementProjectionGunController>();
        prefab = new GameObject("Projection gun test prefab");
        Set(gun, "placement", placement);
        Set(gun, "projectionGunPrefab", prefab);
        Set(gun, "projectionGunAnchor", host.transform);
        Invoke(gun, "TryBindPlacement");
    }

    [TearDown]
    public void TearDown()
    {
        if (placement != null) placement.ExitPlacementMode();
        if (gun != null)
        {
            Invoke(gun, "OnDisable");
            var instance = (GameObject)Get(gun, "projectionGunInstance");
            if (instance != null) Object.DestroyImmediate(instance);
        }
        if (placement != null)
        {
            Invoke(placement, "OnDisable");
            // These transient materials are created by entering placement mode.
            foreach (string field in new[] { "openMaterial", "blockedMaterial", "footprintMaterial",
                "invalidFootprintMaterial", "hoveredMaterial" })
            {
                var material = (Material)Get(placement, field);
                if (material != null) Object.DestroyImmediate(material);
            }
        }
        if (host != null) Object.DestroyImmediate(host);
        if (prefab != null) Object.DestroyImmediate(prefab);
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
        SetStatic(typeof(TrapSelectionMenu), "IsOpen", previousTrapMenuOpen);
        SetStatic(typeof(GameStartMenu), "CursorOwned", previousStartMenuOpen);
    }

    [Test]
    public void DedicatedGunOnlyActivatesDuringPlacementModeAndDeactivatesOnExit()
    {
        Assert.That(Get(gun, "projectionGunInstance"), Is.Null,
            "The instance is created lazily on first entry, not while binding.");

        placement.EnterPlacementMode();
        Assert.That(placement.PlacementMode, Is.True);
        GameObject instance = (GameObject)Get(gun, "projectionGunInstance");
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.activeSelf, Is.True);

        placement.ExitPlacementMode();
        Assert.That(placement.PlacementMode, Is.False);
        Assert.That(instance.activeSelf, Is.False);
    }

    [Test]
    public void ReenteringPlacementReusesTheDedicatedGun()
    {
        placement.EnterPlacementMode();
        var instance = (GameObject)Get(gun, "projectionGunInstance");
        placement.ExitPlacementMode();
        placement.EnterPlacementMode();
        Assert.That(Get(gun, "projectionGunInstance"), Is.SameAs(instance));
        Assert.That(instance.activeSelf, Is.True);
    }

    [Test]
    public void DisablingControllerHidesGunAndRestoresRecordedRendererStates()
    {
        placement.EnterPlacementMode();
        var instance = (GameObject)Get(gun, "projectionGunInstance");
        var visible = host.AddComponent<MeshRenderer>();
        var hiddenObject = new GameObject("Originally hidden weapon part");
        hiddenObject.transform.SetParent(host.transform);
        var hidden = hiddenObject.AddComponent<MeshRenderer>();
        visible.enabled = false;
        hidden.enabled = false;
        Set(gun, "hiddenWeaponRenderers", new Renderer[] { visible, hidden });
        Set(gun, "previousRendererStates", new[] { true, false });
        Set(gun, "weaponVisualsSuppressed", true);

        // EditMode lifecycle delivery is not guaranteed for ordinary MonoBehaviours.
        Invoke(gun, "OnDisable");
        Assert.That(instance.activeSelf, Is.False);
        Assert.That(visible.enabled, Is.True);
        Assert.That(hidden.enabled, Is.False);
        Assert.That(Get(gun, "weaponVisualsSuppressed"), Is.False);
        Assert.That(Get(gun, "hiddenWeaponRenderers"), Is.Null);
    }

    [Test]
    public void MissingPrefabWarnsOnceAndDoesNotCreateReplacement()
    {
        // Bind while placement is already active, then exercise the real null-
        // resource presentation branch without moving the shipped Resources asset.
        Invoke(gun, "OnDisable");
        LogAssert.Expect(LogType.Log, "进入陷阱放置状态");
        placement.EnterPlacementMode();
        Set(gun, "missingPrefabWarningIssued", false);
        LogAssert.Expect(LogType.Warning, "[TrapPlacementProjectionGunController] 未配置投影枪 Prefab；放置模式仍可工作，但不会替换或隐藏普通武器。");
        Invoke(gun, "ShowResolvedProjectionGun", new object[] { null });
        Invoke(gun, "ShowResolvedProjectionGun", new object[] { null });
        Assert.That(Get(gun, "projectionGunInstance"), Is.Null);
        Assert.That(Get(gun, "weaponVisualsSuppressed"), Is.False);
        Assert.That(placement.PlacementMode, Is.True, "Missing visuals must not alter the placement state.");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void EmptyInspectorReferenceLoadsShippedPrefabWithoutDuplicatingMountOffset()
    {
        Set(gun, "projectionGunPrefab", null);
        var shipped = Resources.Load<GameObject>("TrapPlacementProjectionGun");
        Assert.That(shipped, Is.Not.Null);
        placement.EnterPlacementMode();
        var instance = (GameObject)Get(gun, "projectionGunInstance");
        Assert.That(Get(gun, "projectionGunPrefab"), Is.SameAs(shipped));
        Assert.That(instance.transform.parent, Is.SameAs(host.transform));
        Assert.That(instance.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(instance.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(instance.transform.Find("FirstPersonMount").localPosition,
            Is.EqualTo(shipped.transform.Find("FirstPersonMount").localPosition));
    }

    [Test]
    public void ReenablingGunWhilePlacingRebindsAndShowsExistingInstance()
    {
        placement.EnterPlacementMode();
        var instance = (GameObject)Get(gun, "projectionGunInstance");
        Invoke(gun, "OnDisable");
        Assert.That(instance.activeSelf, Is.False);
        Invoke(gun, "OnEnable");
        Assert.That(Get(gun, "projectionGunInstance"), Is.SameAs(instance));
        Assert.That(instance.activeSelf, Is.True);
        placement.ExitPlacementMode();
        Assert.That(instance.activeSelf, Is.False, "Rebinding must also restore the exit subscription.");
    }

    [Test]
    public void WeaponActionsAreSuppressedAndOnlyPreviouslyEnabledActionsAreRestored()
    {
        var actions = Track(ScriptableObject.CreateInstance<InputActionAsset>());
        var map = actions.AddActionMap("Player");
        foreach (string name in new[] { "Attack", "Fire", "Shoot", "Aim", "Reload", "Move", "Interact", "DisabledFire" })
            map.AddAction(name);
        var inputHost = Track(new GameObject("Projection gun input test"));
        var input = inputHost.AddComponent<PlayerInput>();
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        input.actions = actions;
        actions = input.actions;
        actions.Enable();
        actions.FindAction("DisabledFire").Disable();

        placement.EnterPlacementMode();
        foreach (string name in new[] { "Attack", "Fire", "Shoot", "Aim", "Reload" })
            Assert.That(actions.FindAction(name).enabled, Is.False, name);
        Assert.That(actions.FindAction("Move").enabled, Is.True);
        Assert.That(actions.FindAction("Interact").enabled, Is.True);
        placement.ExitPlacementMode();
        foreach (string name in new[] { "Attack", "Fire", "Shoot", "Aim", "Reload" })
            Assert.That(actions.FindAction(name).enabled, Is.True, name);
        Assert.That(actions.FindAction("DisabledFire").enabled, Is.False);
    }

    [Test]
    public void PrimaryActionPurchasesAndDragsWhileSecondaryCannotSellOrDismantle()
    {
        var wallet = EconomyManager.GetOrCreate();
        Track(wallet.gameObject);
        wallet.BeginRun(100);
        var definition = Track(ScriptableObject.CreateInstance<TrapDefinition>());
        Set(definition, "cost", 40);
        Set(placement, "selectedTrap", definition);
        placement.EnterPlacementMode();
        // Inject the purchase target after entry: grid-marker rendering belongs to
        // runtime presentation and uses deferred Destroy, not EditMode lifecycle.
        var grid = Track(new GameObject("Projection purchase grid")).AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(3, 1, 1f, 0f, new[] { true, true, true });
        Set(placement, "activeGrid", grid);
        Set(placement, "hasHoveredCell", true);
        Set(placement, "hoveredCell", Vector2Int.zero);
        Assert.That(placement.OnProjectionGunPrimaryAction(), Is.True, placement.LastPlacementFailure);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(grid.TryGetTrapAtCell(Vector2Int.zero, out TrapInstance trap), Is.True);
        Set(placement, "hoveredTrap", trap);
        int secondary = 0;
        placement.ProjectionGunSecondaryAction += () => secondary++;
        placement.OnProjectionGunSecondaryAction();
        Assert.That(secondary, Is.EqualTo(1));
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(grid.OwnsTrap(trap), Is.True);
        Set(placement, "hoveredCell", new Vector2Int(2, 0));
        placement.OnProjectionGunPrimaryHeld();
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(2));
        Assert.That(wallet.Balance, Is.EqualTo(20));
        Assert.That(placement.PlacementMode, Is.True);
        placement.OnProjectionGunPrimaryReleased();
        Assert.That(Get(placement, "dragPlacement"), Is.False);
        Assert.That(Get(placement, "hasLastPlacedCell"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BothMenusBlockPrimaryAndSecondaryWithoutExitingPlacement(bool startMenu)
    {
        placement.EnterPlacementMode();
        int secondary = 0;
        placement.ProjectionGunSecondaryAction += () => secondary++;
        SetStatic(startMenu ? typeof(GameStartMenu) : typeof(TrapSelectionMenu),
            startMenu ? "CursorOwned" : "IsOpen", true);
        Assert.That(placement.IsProjectionGunInputActive, Is.False);
        Assert.That(placement.OnProjectionGunPrimaryAction(), Is.False);
        placement.OnProjectionGunSecondaryAction();
        Assert.That(secondary, Is.Zero);
        Assert.That(placement.PlacementMode, Is.True);
    }

    [Test]
    public void PlacementInputKeepsLeftPlacementAndRightReservedEventSideEffectFree()
    {
        placement.EnterPlacementMode();
        int secondary = 0;
        placement.ProjectionGunSecondaryAction += () => secondary++;
        placement.OnProjectionGunSecondaryAction();
        Assert.That(secondary, Is.EqualTo(1));
        Assert.That(placement.PlacementMode, Is.True);

        SetStatic(typeof(TrapSelectionMenu), "IsOpen", true);
        placement.OnProjectionGunSecondaryAction();
        Assert.That(secondary, Is.EqualTo(1), "An open trap menu blocks forwarded input.");
        Assert.That(placement.IsProjectionGunInputActive, Is.False);
    }

    [Test]
    public void HitscanDamageBoundaryReturnsDuringPlacementMode()
    {
        placement.EnterPlacementMode();
        var shooterHost = new GameObject("Hitscan boundary test");
        try
        {
            var shooter = shooterHost.AddComponent<FPSHitscanShooter>();
            Assert.DoesNotThrow(() => Invoke(shooter, "FireHitscan"));
        }
        finally { Object.DestroyImmediate(shooterHost); }
    }

    [Test]
    public void HitscanCannotDamageEnemyWhilePlacingAndResumesAfterExit()
    {
        var cameraHost = Track(new GameObject("Projection damage camera"));
        cameraHost.transform.position = new Vector3(10000f, 500f, 10000f);
        var camera = cameraHost.AddComponent<Camera>();
        var target = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        target.transform.position = cameraHost.transform.position + Vector3.forward * 10f;
        var health = target.AddComponent<EnemyHealth>();
        health.ResetHealth();
        var shooter = host.AddComponent<FPSHitscanShooter>();
        Set(shooter, "playerCamera", camera);
        Set(shooter, "observedStats", new WeaponDamageStats(WeaponClass.Unknown, 25f, 0f, 100f, 1f, 1f, 1f, 1f));
        Physics.SyncTransforms();
        float before = health.CurrentHealth;
        placement.EnterPlacementMode();
        Invoke(shooter, "FireHitscan");
        Assert.That(health.CurrentHealth, Is.EqualTo(before));
        placement.ExitPlacementMode();
        Invoke(shooter, "FireHitscan");
        Assert.That(health.CurrentHealth, Is.EqualTo(before - 25f),
            "The same target must be damageable outside placement; otherwise the guard test is vacuous.");
    }

    private T Track<T>(T value) where T : Object
    {
        owned.Add(value);
        return value;
    }

    private static object Get(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        return field.GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        field.SetValue(target, value);
    }

    private static void Invoke(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Missing method: " + name);
        method.Invoke(target, args);
    }

    private static void SetStatic(System.Type type, string name, object value)
    {
        var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null, "Missing property: " + name);
        property.GetSetMethod(true).Invoke(null, new[] { value });
    }
}
#endif
