#if ENABLE_DRONE_CODE
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TowerDefense;

public enum DroneControlState
{
    Player,
    Drone
}

/// <summary>Owns the player's transition into and out of the local drone view.</summary>
[DisallowMultipleComponent]
public sealed class DroneControlSession : MonoBehaviour, IObserverModeDriver
{
    public DroneControlState State { get; private set; } = DroneControlState.Player;

    private GameObject playerRoot;
    private GameObject droneRoot;
    private DroneCameraController droneCamera;
    private QuadcopterFlightController droneController;
    private Rigidbody droneRigidbody;
    private PlayerObserverMode observerMode;
    private Camera originalPlayerCamera;
    private Camera droneCamera_cam;

    // Player state restoration
    private FirstPersonController fpsController;
    private MonoBehaviour fpsPackageMotion; // FPSPackagePlayerMotion
    private bool fpsControllerWasEnabled;
    private bool fpsPackageMotionWasEnabled;
    private bool playerCameraWasEnabled;

    /// <summary>Connects the player and the single drone instance owned by this session.</summary>
    public void Bind(GameObject playerRoot, GameObject droneRoot)
    {
        this.playerRoot = playerRoot;
        this.droneRoot = droneRoot;
        State = DroneControlState.Player;

        if (droneRoot != null)
        {
            droneCamera = droneRoot.GetComponentInChildren<DroneCameraController>();
            droneController = droneRoot.GetComponent<QuadcopterFlightController>();
            droneRigidbody = droneRoot.GetComponent<Rigidbody>();
            droneCamera_cam = droneRoot.GetComponentInChildren<Camera>();

            // Initialize drone in frozen state
            if (droneController != null) droneController.enabled = false;
            if (droneCamera != null) droneCamera.SetActive(false);
            if (droneCamera_cam != null) droneCamera_cam.enabled = false;
            if (droneRigidbody != null) droneRigidbody.isKinematic = true;
        }

        if (playerRoot != null)
        {
            observerMode = playerRoot.GetComponent<PlayerObserverMode>();
            originalPlayerCamera = playerRoot.GetComponentInChildren<Camera>();
            fpsController = playerRoot.GetComponent<FirstPersonController>();

            // Try to find FPSPackagePlayerMotion by name (avoid hard reference)
            var motion = playerRoot.GetComponent("FPSPackagePlayerMotion") as MonoBehaviour;
            fpsPackageMotion = motion;

            // Subscribe to player death event
            var playerHealth = playerRoot.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.Died += OnPlayerDied;
            }
        }

        // Subscribe to scene unload
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    /// <summary>Requests a player/drone mode toggle.</summary>
    public void RequestToggle()
    {
        if (State == DroneControlState.Player)
        {
            if (CanEnterDrone())
            {
                EnterDroneMode();
            }
            else
            {
                Debug.LogWarning("Cannot enter drone mode: requirements not met");
            }
        }
        else
        {
            ExitDroneMode();
        }
    }

    private bool CanEnterDrone()
    {
        // Check drone readiness
        if (droneRoot == null || !droneRoot.activeInHierarchy) return false;
        if (droneCamera == null || droneController == null) return false;
        if (observerMode == null) return false;

        // Guard: reject if menu has cursor ownership
        if (GameStartMenu.CursorOwned)
        {
            Debug.Log("Drone toggle blocked: GameStartMenu owns cursor");
            return false;
        }

        if (TrapSelectionMenu.CursorOwned)
        {
            Debug.Log("Drone toggle blocked: TrapSelectionMenu owns cursor");
            return false;
        }

        // Guard: reject if player is dead
        var playerHealth = playerRoot != null ? playerRoot.GetComponent<PlayerHealth>() : null;
        if (playerHealth != null && playerHealth.IsDead)
        {
            Debug.Log("Drone toggle blocked: player is dead");
            return false;
        }

        return true;
    }

    private void EnterDroneMode()
    {
        State = DroneControlState.Drone;

        // 1. Record player component states before disabling
        if (fpsController != null)
        {
            fpsControllerWasEnabled = fpsController.enabled;
            fpsController.enabled = false;
        }

        if (fpsPackageMotion != null)
        {
            fpsPackageMotionWasEnabled = fpsPackageMotion.enabled;
            fpsPackageMotion.enabled = false;
        }

        if (originalPlayerCamera != null)
        {
            playerCameraWasEnabled = originalPlayerCamera.enabled;
            originalPlayerCamera.enabled = false;
        }

        // 2. Enable drone flight and camera
        if (droneRigidbody != null)
        {
            droneRigidbody.isKinematic = false; // Allow physics
        }

        if (droneController != null)
        {
            droneController.enabled = true;
        }

        if (droneCamera != null)
        {
            droneCamera.SetActive(true);
        }

        if (droneCamera_cam != null)
        {
            droneCamera_cam.enabled = true;
        }

        // 3. Lock cursor if no menu owns it
        if (!GameStartMenu.CursorOwned && !TrapSelectionMenu.CursorOwned)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log("Entered Drone mode");
    }

    private void ExitDroneMode()
    {
        State = DroneControlState.Player;

        // 1. Reset drone velocity and freeze
        if (droneController != null)
        {
            droneController.ResetVelocity();
            droneController.enabled = false;
        }

        if (droneRigidbody != null)
        {
            droneRigidbody.isKinematic = true; // Freeze drone
        }

        // 2. Disable drone camera
        if (droneCamera != null)
        {
            droneCamera.SetActive(false);
        }

        if (droneCamera_cam != null)
        {
            droneCamera_cam.enabled = false;
        }

        // 3. Restore player components to recorded state
        if (fpsController != null)
        {
            fpsController.enabled = fpsControllerWasEnabled;
        }

        if (fpsPackageMotion != null)
        {
            fpsPackageMotion.enabled = fpsPackageMotionWasEnabled;
        }

        if (originalPlayerCamera != null)
        {
            originalPlayerCamera.enabled = playerCameraWasEnabled;
        }

        // 4. Return cursor control to player/menu system
        // Don't directly unlock here; let player controller handle it
        // based on current menu state

        Debug.Log("Exited Drone mode");
    }

    // IObserverModeDriver implementation
    public bool CanEnter(PlayerHealth health)
    {
        return CanEnterDrone();
    }

    public void Enter(PlayerHealth health, Transform view)
    {
        // Observer mode framework integration
        // State transition is already handled by EnterDroneMode
        if (State != DroneControlState.Drone)
        {
            EnterDroneMode();
        }
    }

    public void Tick(ObserverModeInput input, float dt)
    {
        // Drone controller handles its own input via QuadcopterFlightController
        // DroneCameraController handles camera input
        // This is called by PlayerObserverMode each frame while in drone mode
    }

    public void Exit()
    {
        if (State == DroneControlState.Drone)
        {
            ExitDroneMode();
        }
    }

    private void OnDestroy()
    {
        // Cleanup on session destruction
        if (State == DroneControlState.Drone)
        {
            ExitDroneMode();
        }

        // Unsubscribe from events
        if (playerRoot != null)
        {
            var playerHealth = playerRoot.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.Died -= OnPlayerDied;
            }
        }

        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    // ── Boundary Event Handlers ────────────────────────────────

    private void OnPlayerDied(PlayerHealth health)
    {
        if (State == DroneControlState.Drone)
        {
            Debug.Log("[DroneControlSession] Player died while in drone mode. Exiting to Player state.");
            ExitDroneMode();
        }
    }

    private void OnSceneUnloaded(Scene scene)
    {
        // Clean up references without calling Exit (objects may already be destroyed)
        State = DroneControlState.Player;
        playerRoot = null;
        droneRoot = null;
        droneCamera = null;
        droneController = null;
        droneRigidbody = null;
        observerMode = null;
        originalPlayerCamera = null;
        fpsController = null;
        fpsPackageMotion = null;
    }

    private void Update()
    {
        // Monitor drone destruction
        if (droneRoot == null && State == DroneControlState.Drone)
        {
            Debug.LogWarning("[DroneControlSession] Drone was destroyed. Exiting to Player state.");
            ExitDroneMode();
        }

        // Stop drone input if menu takes cursor ownership
        if (State == DroneControlState.Drone)
        {
            if (GameStartMenu.CursorOwned || TrapSelectionMenu.CursorOwned)
            {
                // Disable drone controller input temporarily
                // Controller will resume when menu closes
                if (droneController != null && droneController.enabled)
                {
                    droneController.enabled = false;
                }
            }
            else
            {
                // Re-enable if it was disabled
                if (droneController != null && !droneController.enabled)
                {
                    droneController.enabled = true;
                }
            }
        }
    }
}
#endif
