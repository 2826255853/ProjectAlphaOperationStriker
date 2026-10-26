#if UNITY_INCLUDE_TESTS
#if ENABLE_DRONE_CODE
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TowerDefense;

/// <summary>Tests for DroneControlSession coordinator.</summary>
public sealed class DroneControlSessionTests
{
    private GameObject playerRoot;
    private GameObject droneRoot;
    private DroneControlSession session;
    private bool gameStartMenuCursorOwned;
    private CursorLockMode cursorLockState;
    private bool cursorVisible;

    private static readonly PropertyInfo GameStartMenuCursorOwnedProperty =
        typeof(GameStartMenu).GetProperty("CursorOwned", BindingFlags.Static | BindingFlags.Public);

    [SetUp]
    public void Setup()
    {
        gameStartMenuCursorOwned = GameStartMenu.CursorOwned;
        cursorLockState = Cursor.lockState;
        cursorVisible = Cursor.visible;
        try
        {
            SetGameStartMenuCursorOwned(false);
            CreateFixture();
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    private void CreateFixture()
    {
        // Create minimal player hierarchy
        playerRoot = new GameObject("Player");
        var playerHealth = playerRoot.AddComponent<PlayerHealth>();
        // EditMode does not invoke Awake automatically for newly added components.
        InitializeInEditMode(playerHealth);
        playerRoot.AddComponent<PlayerObserverMode>();
        var playerCam = new GameObject("PlayerCamera");
        playerCam.transform.SetParent(playerRoot.transform);
        playerCam.AddComponent<Camera>();

        // Create minimal drone hierarchy
        droneRoot = new GameObject("Drone");
        CreateChild(droneRoot.transform, "Rotor_FL");
        CreateChild(droneRoot.transform, "Rotor_FR");
        CreateChild(droneRoot.transform, "Rotor_RL");
        CreateChild(droneRoot.transform, "Rotor_RR");
        var flightController = droneRoot.AddComponent<QuadcopterFlightController>();
        // These MonoBehaviours do not run Awake automatically in EditMode.
        InitializeInEditMode(flightController);
        var droneRb = droneRoot.GetComponent<Rigidbody>();
        Assert.IsNotNull(droneRb, "QuadcopterFlightController should provide its required Rigidbody");
        droneRb.isKinematic = true;

        var gimbal = new GameObject("Camera_Gimbal");
        gimbal.transform.SetParent(droneRoot.transform);
        var droneCamObj = new GameObject("DroneCamera");
        droneCamObj.transform.SetParent(gimbal.transform);
        droneCamObj.AddComponent<Camera>();
        var cameraController = droneRoot.AddComponent<DroneCameraController>();
        InitializeInEditMode(cameraController);

        // Create session
        session = playerRoot.AddComponent<DroneControlSession>();
        session.Bind(playerRoot, droneRoot);
    }

    [TearDown]
    public void Teardown()
    {
        try
        {
            try
            {
                if (playerRoot != null) Object.DestroyImmediate(playerRoot);
            }
            finally
            {
                if (droneRoot != null) Object.DestroyImmediate(droneRoot);
            }
        }
        finally
        {
            try
            {
                SetGameStartMenuCursorOwned(gameStartMenuCursorOwned);
            }
            finally
            {
                Cursor.lockState = cursorLockState;
                Cursor.visible = cursorVisible;
            }
        }
    }

    private static void InitializeInEditMode(MonoBehaviour component)
    {
        var awake = component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(awake, "Fixture component must expose its Awake initialization method");
        awake.Invoke(component, null);
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent);
        return child;
    }

    private static void SetGameStartMenuCursorOwned(bool value)
    {
        var setter = GameStartMenuCursorOwnedProperty?.GetSetMethod(true);
        Assert.IsNotNull(setter, "GameStartMenu.CursorOwned must expose a non-public setter for test injection");
        setter.Invoke(null, new object[] { value });
    }

    [Test]
    public void InitialState_IsPlayer()
    {
        Assert.AreEqual(DroneControlState.Player, session.State);
    }

    [Test]
    public void RequestToggle_WhenMenuOwned_Rejects()
    {
        // Simulate menu taking cursor
        SetGameStartMenuCursorOwned(true);
        try
        {
            session.RequestToggle();
            Assert.AreEqual(DroneControlState.Player, session.State, "Should remain in Player state when menu owns cursor");
        }
        finally
        {
            SetGameStartMenuCursorOwned(gameStartMenuCursorOwned);
        }
    }

    [Test]
    public void RequestToggle_WhenPlayerDead_Rejects()
    {
        var health = playerRoot.GetComponent<PlayerHealth>();
        // Simulate player death
        health.TakeDamage(9999f);

        session.RequestToggle();

        Assert.AreEqual(DroneControlState.Player, session.State, "Should remain in Player state when player is dead");
    }

    [Test]
    public void RequestToggle_ValidConditions_EntersDroneMode()
    {
        session.RequestToggle();

        Assert.AreEqual(DroneControlState.Drone, session.State, "Should enter Drone state");

        var droneRb = droneRoot.GetComponent<Rigidbody>();
        Assert.IsFalse(droneRb.isKinematic, "Drone Rigidbody should be non-kinematic in flight mode");
    }

    [Test]
    public void ExitDroneMode_FreezesRigidbody()
    {
        session.RequestToggle(); // Enter
        Assert.AreEqual(DroneControlState.Drone, session.State, "Fixture must enter Drone state before exit assertions");
        var droneRb = droneRoot.GetComponent<Rigidbody>();
        droneRb.linearVelocity = new Vector3(5, 0, 0); // Simulate movement
        droneRb.angularVelocity = new Vector3(0, 2, 0); // Simulate rotation

        session.RequestToggle(); // Exit

        Assert.AreEqual(DroneControlState.Player, session.State, "Should return to Player state");
        Assert.IsTrue(droneRb.isKinematic, "Rigidbody should be frozen after exit");
        Assert.AreEqual(Vector3.zero, droneRb.linearVelocity, "Velocity should be cleared");
        Assert.AreEqual(Vector3.zero, droneRb.angularVelocity, "Angular velocity should be cleared");
    }

    [Test]
    public void Exit_RestoresPlayerCamera()
    {
        var playerCam = playerRoot.GetComponentInChildren<Camera>();
        bool originalState = playerCam.enabled;

        session.RequestToggle(); // Enter drone
        Assert.IsFalse(playerCam.enabled, "Player camera should be disabled in drone mode");

        session.RequestToggle(); // Exit drone
        Assert.AreEqual(originalState, playerCam.enabled, "Player camera should be restored to original state");
    }

    [Test]
    public void IObserverModeDriver_CanEnter_RespectsGuards()
    {
        var health = playerRoot.GetComponent<PlayerHealth>();

        // Valid conditions
        Assert.IsTrue(session.CanEnter(health), "Should allow entry when conditions are valid");

        // Invalid: dead player
        health.TakeDamage(9999f);
        Assert.IsFalse(session.CanEnter(health), "Should reject when player is dead");
    }
}
#endif
#endif
