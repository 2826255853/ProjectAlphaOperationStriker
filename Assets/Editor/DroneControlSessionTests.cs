#if UNITY_INCLUDE_TESTS && ENABLE_DRONE_CODE
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Tests for DroneControlSession coordinator.</summary>
public sealed class DroneControlSessionTests
{
    private GameObject playerRoot;
    private GameObject droneRoot;
    private DroneControlSession session;

    [SetUp]
    public void Setup()
    {
        // Create minimal player hierarchy
        playerRoot = new GameObject("Player");
        playerRoot.AddComponent<PlayerHealth>();
        playerRoot.AddComponent<PlayerObserverMode>();
        var playerCam = new GameObject("PlayerCamera");
        playerCam.transform.SetParent(playerRoot.transform);
        playerCam.AddComponent<Camera>();

        // Create minimal drone hierarchy
        droneRoot = new GameObject("Drone");
        droneRoot.AddComponent<QuadcopterFlightController>();
        var droneRb = droneRoot.AddComponent<Rigidbody>();
        droneRb.isKinematic = true;

        var droneCamObj = new GameObject("DroneCamera");
        droneCamObj.transform.SetParent(droneRoot.transform);
        droneCamObj.AddComponent<Camera>();
        droneCamObj.AddComponent<DroneCameraController>();

        // Create session
        session = playerRoot.AddComponent<DroneControlSession>();
        session.Bind(playerRoot, droneRoot);
    }

    [TearDown]
    public void Teardown()
    {
        if (playerRoot != null) Object.DestroyImmediate(playerRoot);
        if (droneRoot != null) Object.DestroyImmediate(droneRoot);
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
        typeof(GameStartMenu).GetProperty("CursorOwned").SetValue(null, true);

        session.RequestToggle();

        Assert.AreEqual(DroneControlState.Player, session.State, "Should remain in Player state when menu owns cursor");

        // Cleanup
        typeof(GameStartMenu).GetProperty("CursorOwned").SetValue(null, false);
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
        var droneRb = droneRoot.GetComponent<Rigidbody>();
        droneRb.linearVelocity = new Vector3(5, 0, 0); // Simulate movement

        session.RequestToggle(); // Exit

        Assert.AreEqual(DroneControlState.Player, session.State, "Should return to Player state");
        Assert.IsTrue(droneRb.isKinematic, "Rigidbody should be frozen after exit");
        Assert.AreEqual(Vector3.zero, droneRb.linearVelocity, "Velocity should be cleared");
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
