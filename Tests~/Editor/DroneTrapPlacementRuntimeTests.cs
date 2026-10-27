#if UNITY_INCLUDE_TESTS && ENABLE_DRONE_CODE
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TowerDefense;

public sealed class DroneTrapPlacementRuntimeTests
{
    private GameObject playerRoot;
    private GameObject droneRoot;
    private GameObject placementHost;
    private GameObject menuHost;
    private GameObject projectionPrefab;
    private DroneControlSession session;
    private TrapSelectionMenu menu;
    private Keyboard keyboard;
    private InputSettings.UpdateMode originalInputUpdateMode;
    private CursorLockMode cursorLockState;
    private bool cursorVisible;

    [SetUp]
    public void SaveCursorState()
    {
        cursorLockState = Cursor.lockState;
        cursorVisible = Cursor.visible;
        originalInputUpdateMode = InputSystem.settings.updateMode;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
    }

    [Test]
    public void DroneUsesItsCameraHidesProjectionGunAndReturnClearsPlacement()
    {
        CreateSession();
        placementHost = new GameObject("Drone trap placement test");
        TrapPlacementController placement = placementHost.AddComponent<TrapPlacementController>();
        Initialize(placement, "Awake");
        Initialize(placement, "OnEnable");

        projectionPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        TrapPlacementProjectionGunController projection = placementHost.AddComponent<TrapPlacementProjectionGunController>();
        typeof(TrapPlacementProjectionGunController).GetField("projectionGunPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(projection, projectionPrefab);
        Initialize(projection, "Awake");
        Initialize(projection, "OnEnable");

        PressProjectionKey(projection, Key.Digit4);
        Assert.That(placement.PlacementMode, Is.True, "4 should enter placement mode in the drone-capable polling loop.");
        Assert.That(placement.SelectedSlot, Is.EqualTo(0));
        GameObject shownGun = (GameObject)typeof(TrapPlacementProjectionGunController)
            .GetField("projectionGunInstance", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(projection);
        Assert.That(shownGun, Is.Not.Null);
        Assert.That(shownGun.activeSelf, Is.True);

        session.RequestToggle();
        Assert.That(session.IsDroneControlled, Is.True);
        Assert.That(placement.PlacementCamera, Is.SameAs(session.ControlCamera));
        Assert.That(shownGun.activeSelf, Is.False);
        Assert.That(placement.PlacementMode, Is.True);
        PressProjectionKey(projection, Key.Digit5);
        Assert.That(placement.SelectedSlot, Is.EqualTo(1), "5 should select the next trap slot while controlling the drone.");
        Assert.That(placement.IsProjectionGunInputActive, Is.True,
            "Placement mouse input remains active even though the projection-gun model is hidden.");

        menuHost = new GameObject("Drone trap selection menu test");
        menu = menuHost.AddComponent<TrapSelectionMenu>();
        Initialize(menu, "Awake");
        PressMenuKey(Key.N);
        Initialize(session, "Update");
        Assert.That(TrapSelectionMenu.CursorOwned, Is.True);
        Assert.That(droneRoot.GetComponent<QuadcopterFlightController>().enabled, Is.False,
            "The trap menu should pause drone flight controls.");
        session.RequestToggle();
        Assert.That(session.IsDroneControlled, Is.True,
            "The existing menu cursor guard should block returning to the player while the menu is open.");
        PressMenuKey(Key.Escape);
        typeof(TrapSelectionMenu).GetField("cursorReleasedFrame", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, -1);
        Initialize(session, "Update");
        Assert.That(droneRoot.GetComponent<QuadcopterFlightController>().enabled, Is.True,
            "Closing the menu should resume drone flight controls.");

        session.RequestToggle();
        Assert.That(session.IsDroneControlled, Is.False);
        Assert.That(placement.PlacementCamera, Is.SameAs(session.ControlCamera));
        Assert.That(placement.PlacementMode, Is.False);
        Assert.That(shownGun.activeSelf, Is.False);
    }

    private void CreateSession()
    {
        playerRoot = new GameObject("Player");
        PlayerHealth health = playerRoot.AddComponent<PlayerHealth>();
        Initialize(health, "Awake");
        playerRoot.AddComponent<PlayerObserverMode>();
        GameObject playerCamera = new GameObject("PlayerCamera");
        playerCamera.transform.SetParent(playerRoot.transform);
        playerCamera.AddComponent<Camera>();

        droneRoot = new GameObject("Drone");
        Rigidbody body = droneRoot.AddComponent<Rigidbody>();
        body.isKinematic = true;
        foreach (string rotor in new[] { "Rotor_FL", "Rotor_FR", "Rotor_RL", "Rotor_RR" })
        {
            GameObject rotorObject = new GameObject(rotor);
            rotorObject.transform.SetParent(droneRoot.transform);
        }
        QuadcopterFlightController flight = droneRoot.AddComponent<QuadcopterFlightController>();
        Initialize(flight, "Awake");
        GameObject gimbal = new GameObject("Camera_Gimbal");
        gimbal.transform.SetParent(droneRoot.transform);
        GameObject droneCamera = new GameObject("DroneCamera");
        droneCamera.transform.SetParent(gimbal.transform);
        droneCamera.AddComponent<Camera>();
        DroneCameraController cameraController = droneRoot.AddComponent<DroneCameraController>();
        Initialize(cameraController, "Awake");

        session = playerRoot.AddComponent<DroneControlSession>();
        session.Bind(playerRoot, droneRoot);
    }

    private static void Initialize(MonoBehaviour component, string method)
    {
        MethodInfo info = component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        if (info != null) info.Invoke(component, null);
    }

    private static void Call(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        info.Invoke(target, args);
    }

    private void PressProjectionKey(TrapPlacementProjectionGunController projection, Key key)
    {
        SendKeyboardState(new KeyboardState(key));
        Call(projection, "Update");
        SendKeyboardState(new KeyboardState());
        Call(projection, "Update");
    }

    private void PressMenuKey(Key key)
    {
        SendKeyboardState(new KeyboardState(key));
        Call(menu, "Update");
        SendKeyboardState(new KeyboardState());
        Call(menu, "Update");
    }

    private void SendKeyboardState(KeyboardState state)
    {
        InputSystem.QueueStateEvent(keyboard, state);
        InputSystem.Update();
    }

    [TearDown]
    public void Cleanup()
    {
        if (menu != null) Call(menu, "SetOpen", false);
        if (playerRoot != null) Object.DestroyImmediate(playerRoot);
        if (droneRoot != null) Object.DestroyImmediate(droneRoot);
        if (placementHost != null) Object.DestroyImmediate(placementHost);
        if (menuHost != null) Object.DestroyImmediate(menuHost);
        if (projectionPrefab != null) Object.DestroyImmediate(projectionPrefab);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.updateMode = originalInputUpdateMode;
        Cursor.lockState = cursorLockState;
        Cursor.visible = cursorVisible;
    }
}
#endif
