#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;

/// <summary>
/// Runtime acceptance for the first-person projection-gun path. Stage 7 covers
/// isolated contracts; these checks send virtual device events through live Update loops.
/// </summary>
public sealed class TrapPlacementProjectionGunStage8Tests
{
    private const string MainScene = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";

    private Mouse testMouse;
    private Keyboard testKeyboard;
    private bool createdMouse;
    private bool createdKeyboard;
    private GameObject aimHost;
    private GameObject gridHost;
    private GameObject damageTarget;
    private TrapDefinition testDefinition;
    private TrapPlacementController placement;
    private TrapPlacementProjectionGunController projectionGun;
    private FPSHitscanShooter shooter;
    private LayerMask previousHitMask;
    private bool hitMaskChanged;

    [UnitySetUp]
    public IEnumerator EnterProjectPlayMode()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != MainScene)
            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);

        yield return new EnterPlayMode();
        for (int i = 0; i < 3; i++) yield return null;
        Assert.That(Application.isPlaying, Is.True);
    }

    [UnityTearDown]
    public IEnumerator LeaveProjectPlayMode()
    {
        if (Application.isPlaying)
        {
            if (placement != null) placement.ExitPlacementMode();
            TrapSelectionMenu trapMenu = Object.FindAnyObjectByType<TrapSelectionMenu>();
            if (trapMenu != null && TrapSelectionMenu.IsOpen) Invoke(trapMenu, "SetOpen", false);
            GameStartMenu startMenu = Object.FindAnyObjectByType<GameStartMenu>();
            if (startMenu != null && GameStartMenu.CursorOwned) Invoke(startMenu, "Close");
            InputSystem.onAfterUpdate -= PumpPolledControllers;
            if (shooter != null && hitMaskChanged) Set(shooter, "hitMask", previousHitMask);
            if (damageTarget != null) Object.Destroy(damageTarget);
            if (gridHost != null) Object.Destroy(gridHost);
            if (aimHost != null) Object.Destroy(aimHost);
            if (testDefinition != null) Object.Destroy(testDefinition);
            if (createdMouse && testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
            if (createdKeyboard && testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
            yield return null;
            yield return new ExitPlayMode();
        }
    }

    [UnityTest]
    public IEnumerator FirstPersonPlacementInputVisualsAndMenuOwnershipWorkTogether()
    {
        FPSPlayer player = null;
        for (int i = 0; i < 120 && player == null; i++)
        {
            player = Object.FindAnyObjectByType<FPSPlayer>();
            if (player == null) yield return null;
        }
        Assert.That(player, Is.Not.Null, "The main scene must bootstrap its real KINEMATION FPSPlayer.");

        FPSWeapon weapon = player.GetActiveWeapon();
        Assert.That(weapon, Is.Not.Null, "The real player must have an active normal weapon.");
        Renderer[] weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
        Assert.That(weaponRenderers.Length, Is.GreaterThan(0), "The active weapon needs renderers to verify hide/restore.");
        var originalRendererStates = new bool[weaponRenderers.Length];
        bool anyWeaponRendererVisible = false;
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            originalRendererStates[i] = weaponRenderers[i] != null && weaponRenderers[i].enabled;
            anyWeaponRendererVisible |= originalRendererStates[i];
        }
        Assert.That(anyWeaponRendererVisible, Is.True, "The baseline weapon must be visible for this check to be meaningful.");

        Camera playerCamera = Camera.main;
        Assert.That(playerCamera, Is.Not.Null, "The bootstrapped player must provide the active first-person MainCamera.");
        Assert.That(playerCamera.transform.root, Is.SameAs(player.transform.root),
            "The active MainCamera must belong to the bootstrapped FPS player hierarchy, not the disabled scene marker camera.");
        PlayerInput playerInput = player.GetComponentInChildren<PlayerInput>(true);
        Assert.That(playerInput, Is.Not.Null);

        GameStartMenu startMenu = Object.FindAnyObjectByType<GameStartMenu>();
        Assert.That(startMenu, Is.Not.Null);
        if (GameStartMenu.CursorOwned) Invoke(startMenu, "Close");
        yield return null;
        Assert.That(GameStartMenu.CursorOwned, Is.False, "The test must leave the start menu before exercising gameplay input.");

        testMouse = InputSystem.AddDevice<Mouse>();
        createdMouse = true;
        testKeyboard = InputSystem.AddDevice<Keyboard>();
        createdKeyboard = true;
        testMouse.MakeCurrent();
        testKeyboard.MakeCurrent();
        Assert.That(testMouse, Is.Not.Null);
        Assert.That(testKeyboard, Is.Not.Null);
        if (playerInput.user.valid)
        {
            InputUser.PerformPairingWithDevice(testMouse, playerInput.user);
            InputUser.PerformPairingWithDevice(testKeyboard, playerInput.user);
        }

        Vector2 pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        QueueMouse(new MouseState().WithPosition(pointer));
        yield return null;
        yield return null;

        // Use the scene's actual runtime controllers, redirecting only their
        // transient test-time references to an isolated floor grid and camera.
        placement = null;
        projectionGun = null;
        for (int i = 0; i < 120 && (placement == null || projectionGun == null); i++)
        {
            placement = Object.FindAnyObjectByType<TrapPlacementController>();
            projectionGun = Object.FindAnyObjectByType<TrapPlacementProjectionGunController>();
            if (placement == null || projectionGun == null) yield return null;
        }
        Assert.That(placement, Is.Not.Null, "The scene must bootstrap its real trap-placement controller.");
        Assert.That(projectionGun, Is.Not.Null, "The scene must bootstrap its real projection-gun controller.");

        // A tiny isolated floor grid gives the live polling loop a deterministic
        // ray target without changing the authored level or its trap assets.
        Vector3 target = new Vector3(10000f, 0f, 10000f);
        aimHost = new GameObject("Stage 8 aim camera");
        aimHost.transform.position = target + new Vector3(0f, 5f, -5f);
        aimHost.transform.LookAt(target);
        Camera aimCamera = aimHost.AddComponent<Camera>();
        aimCamera.pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
        aimCamera.enabled = false;

        gridHost = new GameObject("Stage 8 isolated placement grid");
        gridHost.transform.position = target - new Vector3(1.5f, 0f, 1.5f);
        TrapPlacementGrid grid = gridHost.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(3, 3, 1f, 0f,
            new[] { true, true, true, true, true, true, true, true, true },
            TrapPlacementGrid.SurfaceKind.Ground);

        testDefinition = ScriptableObject.CreateInstance<TrapDefinition>();
        Set(testDefinition, "cost", 10);
        Set(testDefinition, "trapId", "stage8_projection_gun_test");
        Set(testDefinition, "displayName", "Stage 8 Test Trap");
        EconomyManager wallet = EconomyManager.GetOrCreate();
        wallet.BeginRun(100);

        placement.ExitPlacementMode();
        Set(placement, "grid", grid);
        Set(placement, "autoDiscoverGrids", false);
        Set(placement, "placementCamera", aimCamera);
        Set(placement, "trapSlots", new[] { testDefinition, null, null, null });
        Set(placement, "selectedTrap", testDefinition);
        Invoke(placement, "RefreshGrids");
        Set(projectionGun, "placement", placement);
        Set(projectionGun, "projectionGunPrefab", Resources.Load<GameObject>("TrapPlacementProjectionGun"));
        Set(projectionGun, "projectionGunAnchor", playerCamera.transform);

        shooter = player.transform.root.GetComponent<FPSHitscanShooter>();
        Assert.That(shooter, Is.Not.Null, "The real player must have its gameplay hitscan bridge.");
        previousHitMask = (LayerMask)Get(shooter, "hitMask");
        hitMaskChanged = true;
        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        damageTarget = targetObject;
        targetObject.name = "Stage 8 isolated damage target";
        targetObject.layer = 31;
        targetObject.transform.SetPositionAndRotation(playerCamera.transform.position + playerCamera.transform.forward * 3f,
            Quaternion.identity);
        targetObject.transform.localScale = Vector3.one * 0.6f;
        EnemyHealth targetHealth = targetObject.AddComponent<EnemyHealth>();
        targetHealth.ResetHealth();
        Set(shooter, "hitMask", (LayerMask)(1 << 31));
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        int normalAmmoBeforePlacement = weapon.GetActiveAmmo();
        float targetHealthBeforePlacement = targetHealth.CurrentHealth;
        Assert.That(shooter.CurrentWeaponBaseDamage, Is.GreaterThan(0f));

        // Use the same number-key and mouse polling used by a player, not direct
        // calls to placement methods.
        Assert.That(GameStartMenu.CursorOwned, Is.False);
        Assert.That(TrapSelectionMenu.CursorOwned, Is.False);
        Assert.That(projectionGun.isActiveAndEnabled, Is.True);
        Assert.That(placement.isActiveAndEnabled, Is.True);
        QueueKeyboard(new KeyboardState(Key.Digit4));
        yield return null;
        yield return null;
        Assert.That(placement.PlacementMode, Is.True, "4 should enter placement mode in the live Update loop.");
        Assert.That(placement.HasValidPreview, Is.True,
            "The camera ray should resolve an open affordable cell before input is injected.");

        GameObject shownProjectionGun = (GameObject)Get(projectionGun, "projectionGunInstance");
        Assert.That(shownProjectionGun, Is.Not.Null);
        Assert.That(shownProjectionGun.activeInHierarchy, Is.True);
        for (int i = 0; i < weaponRenderers.Length; i++)
            if (weaponRenderers[i] != null) Assert.That(weaponRenderers[i].enabled, Is.False, weaponRenderers[i].name);

        int initialTrapCount = grid.PlacedTraps.Count;
        QueueMouse(new MouseState().WithPosition(pointer).WithButton(MouseButton.Left));
        yield return null;
        yield return null;
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(initialTrapCount + 1), "A real left-button event should purchase/place one trap.");
        Assert.That(wallet.Balance, Is.EqualTo(90));
        Assert.That(targetHealth.CurrentHealth, Is.EqualTo(targetHealthBeforePlacement),
            "A placement click must not leak into normal hitscan damage.");
        Assert.That(weapon.GetActiveAmmo(), Is.EqualTo(normalAmmoBeforePlacement),
            "A placement click must not consume ammunition from the normal weapon.");

        QueueMouse(new MouseState().WithPosition(pointer));
        yield return null;
        yield return null;
        int secondaryActions = 0;
        placement.ProjectionGunSecondaryAction += () => secondaryActions++;
        QueueMouse(new MouseState().WithPosition(pointer).WithButton(MouseButton.Right));
        yield return null;
        yield return null;
        Assert.That(secondaryActions, Is.EqualTo(1), "Right click should publish only the reserved secondary event.");
        Assert.That(grid.PlacedTraps.Count, Is.EqualTo(initialTrapCount + 1), "Right click must not sell or dismantle the placed trap.");
        Assert.That(wallet.Balance, Is.EqualTo(90), "Right click must not alter the wallet.");

        QueueMouse(new MouseState().WithPosition(pointer));
        QueueKeyboard(new KeyboardState());
        yield return null;
        QueueKeyboard(new KeyboardState(Key.Digit1));
        yield return null;
        yield return null;
        Assert.That(placement.PlacementMode, Is.False, "1 should return to the normal weapon in the live Update loop.");
        Assert.That(shownProjectionGun.activeInHierarchy, Is.False);
        for (int i = 0; i < weaponRenderers.Length; i++)
            if (weaponRenderers[i] != null)
                Assert.That(weaponRenderers[i].enabled, Is.EqualTo(originalRendererStates[i]), weaponRenderers[i].name);

        yield return null;
        int normalAmmoBeforeShot = weapon.GetActiveAmmo();
        float targetHealthBeforeShot = targetHealth.CurrentHealth;
        QueueMouse(new MouseState().WithPosition(pointer).WithButton(MouseButton.Left));
        for (int i = 0; i < 90 && weapon.GetActiveAmmo() >= normalAmmoBeforeShot; i++) yield return null;
        Assert.That(weapon.GetActiveAmmo(), Is.LessThan(normalAmmoBeforeShot),
            "After leaving placement, the same real left-button input must fire the normal weapon again.");
        Assert.That(targetHealth.CurrentHealth, Is.LessThan(targetHealthBeforeShot),
            "The restored normal shot must reach a real EnemyHealth target.");

        QueueMouse(new MouseState().WithPosition(pointer));
        QueueKeyboard(new KeyboardState());
        yield return null;
        TrapSelectionMenu trapMenu = Object.FindAnyObjectByType<TrapSelectionMenu>();
        Assert.That(trapMenu, Is.Not.Null);
        InputAction look = playerInput.actions.FindAction("Look", false);
        Assert.That(look, Is.Not.Null, "The actual player input map must expose its look action.");
        Assert.That(look.enabled, Is.True, "Look must be enabled before opening the trap menu.");
        Quaternion cameraBeforeMenu = playerCamera.transform.rotation;

        QueueKeyboard(new KeyboardState(Key.N));
        yield return null;
        yield return null;
        Assert.That(TrapSelectionMenu.CursorOwned, Is.True, "N should open the real trap-selection menu.");
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
        Assert.That(Cursor.visible, Is.True);
        Assert.That(look.enabled, Is.False, "The menu must suspend the player's look action.");

        QueueKeyboard(new KeyboardState());
        QueueMouse(new MouseState().WithPosition(pointer + new Vector2(80f, 45f)).WithDelta(new Vector2(80f, 45f)));
        yield return null;
        yield return null;
        Assert.That(Quaternion.Angle(cameraBeforeMenu, playerCamera.transform.rotation), Is.LessThan(0.01f),
            "Mouse movement while the menu owns the cursor must not rotate the first-person camera.");

        QueueMouse(new MouseState().WithPosition(pointer + new Vector2(80f, 45f)));
        QueueKeyboard(new KeyboardState(Key.N));
        yield return null;
        QueueKeyboard(new KeyboardState());
        yield return null;
        yield return null;
        Assert.That(TrapSelectionMenu.CursorOwned, Is.False);
        Assert.That(look.enabled, Is.True, "Closing the menu must restore the player's look action.");
        Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.Locked));
        Assert.That(Cursor.visible, Is.False);
    }

    private void QueueMouse(MouseState state)
    {
        InputSystem.QueueStateEvent(testMouse, state);
        SchedulePolledControllerPump();
    }

    private void QueueKeyboard(KeyboardState state)
    {
        InputSystem.QueueStateEvent(testKeyboard, state);
        SchedulePolledControllerPump();
    }

    private void SchedulePolledControllerPump()
    {
        // Run after the queued event has been processed but before MonoBehaviour
        // Update, so the production polling code sees the same-frame input edge.
        InputSystem.onAfterUpdate -= PumpPolledControllers;
        InputSystem.onAfterUpdate += PumpPolledControllers;
    }

    private void PumpPolledControllers()
    {
        InputSystem.onAfterUpdate -= PumpPolledControllers;
        TrapSelectionMenu trapMenu = Object.FindAnyObjectByType<TrapSelectionMenu>();
        if (trapMenu != null) Invoke(trapMenu, "Update");
        if (placement != null) Invoke(placement, "Update");
        if (projectionGun != null) Invoke(projectionGun, "Update");
    }

    private static object Get(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        return field.GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing field: " + name);
        field.SetValue(target, value);
    }

    private static void Invoke(object target, string name, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Missing method: " + name);
        method.Invoke(target, args);
    }

}

/// <summary>
/// Input System 的 MouseState 测试扩展（WithPosition / WithButton / WithDelta）
/// 原本由 Input System 的测试工具包提供，工程里没有该包，这里补一份最小实现。
/// </summary>
internal static class TrapPlacementProjectionGunMouseStateExtensions
{
    public static MouseState WithPosition(this MouseState state, Vector2 position)
    {
        state.position = position;
        return state;
    }

    public static MouseState WithDelta(this MouseState state, Vector2 delta)
    {
        state.delta = delta;
        return state;
    }

    public static MouseState WithButton(this MouseState state, MouseButton button, bool pressed = true)
    {
        ushort bit = (ushort)(1 << (int)button);
        if (pressed)
        {
            state.buttons |= bit;
        }
        else
        {
            state.buttons &= (ushort)~bit;
        }

        return state;
    }
}
#endif
