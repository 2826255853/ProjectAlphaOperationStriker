#if ENABLE_DRONE_CODE
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
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
    public static DroneControlSession ActiveSession { get; private set; }
    public static event Action<DroneControlSession> ControlModeChanged;
    public Camera ControlCamera => State == DroneControlState.Drone ? droneCamera_cam : originalPlayerCamera;
    public bool IsDroneControlled => State == DroneControlState.Drone;
    internal Transform DroneTransform => droneRoot != null ? droneRoot.transform : null;

    private GameObject playerRoot;
    private GameObject droneRoot;
    private DroneCameraController droneCamera;
    private QuadcopterFlightController droneController;
    private Rigidbody droneRigidbody;
    private PlayerObserverMode observerMode;
    private Camera originalPlayerCamera;
    private Camera droneCamera_cam;
    private PlayerInput playerInput;
    private AudioListener playerAudioListener;
    private AudioListener droneAudioListener;
    private InputAction toggleAction;
    private bool playerInputWasEnabled;
    private bool playerAudioListenerWasEnabled;
    private MonoBehaviour[] playerInputBehaviours;
    private bool[] playerInputBehavioursWereEnabled;
    private GameObject engineerBody;
    private readonly List<Renderer> hiddenViewMeshes = new List<Renderer>();

    private static readonly string[] PlayerInputComponentNames =
    {
        "PlayerSprintAimBridge",
        "PlayerWeaponCategorySwitcher",
        "FPSHitscanShooter",
        "LadderClimbing"
    };

    // Player state restoration
    private FirstPersonController fpsController;
    private MonoBehaviour fpsPackageMotion; // FPSPackagePlayerMotion
    private bool fpsControllerWasEnabled;
    private bool fpsPackageMotionWasEnabled;
    private bool playerCameraWasEnabled;

    private void OnEnable()
    {
        toggleAction = new InputAction("Toggle Drone Control", InputActionType.Button);
        toggleAction.AddBinding("<Keyboard>/v");
        toggleAction.performed += OnTogglePerformed;
        toggleAction.Enable();
    }

    private void OnDisable()
    {
        toggleAction?.Disable();
        if (toggleAction != null) toggleAction.performed -= OnTogglePerformed;
        toggleAction?.Dispose();
        toggleAction = null;

        if (State == DroneControlState.Drone) ExitDroneMode();
    }

    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        RequestToggle();
    }

    public bool IsBoundTo(GameObject player)
    {
        return playerRoot == player && droneRoot != null;
    }

    /// <summary>Connects the player and the single drone instance owned by this session.</summary>
    public void Bind(GameObject playerRoot, GameObject droneRoot)
    {
        if (State == DroneControlState.Drone) ExitDroneMode();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (this.playerRoot != playerRoot) ReleaseEngineerBody();
        if (this.playerRoot != null)
        {
            PlayerHealth oldHealth = this.playerRoot.GetComponent<PlayerHealth>();
            if (oldHealth != null) oldHealth.Died -= OnPlayerDied;
        }

        this.playerRoot = playerRoot;
        this.droneRoot = droneRoot;
        State = DroneControlState.Player;

        if (droneRoot != null)
        {
            droneCamera = droneRoot.GetComponentInChildren<DroneCameraController>();
            droneController = droneRoot.GetComponent<QuadcopterFlightController>();
            droneRigidbody = droneRoot.GetComponent<Rigidbody>();
            droneCamera_cam = droneRoot.GetComponentInChildren<Camera>();
            droneAudioListener = droneRoot.GetComponentInChildren<AudioListener>(true);

            // Initialize drone in frozen state
            if (droneController != null) droneController.enabled = false;
            if (droneCamera != null) droneCamera.SetActive(false);
            if (droneCamera_cam != null) droneCamera_cam.enabled = false;
            if (droneAudioListener != null) droneAudioListener.enabled = false;
            if (droneRigidbody != null) droneRigidbody.isKinematic = true;
        }

        if (playerRoot != null)
        {
            observerMode = playerRoot.GetComponent<PlayerObserverMode>();
            originalPlayerCamera = playerRoot.GetComponentInChildren<Camera>(true);
            playerAudioListener = playerRoot.GetComponentInChildren<AudioListener>(true);
            fpsController = playerRoot.GetComponent<FirstPersonController>();
            playerInput = playerRoot.GetComponentInChildren<PlayerInput>(true);
            CapturePlayerInputBehaviours(playerRoot);

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
        ActiveSession = this;
        ControlModeChanged?.Invoke(this);
    }

    /// <summary>Requests a player/drone mode toggle.</summary>
    public void RequestToggle()
    {
        if (GameStartMenu.BlocksPlayerCursor || TrapSelectionMenu.CursorOwned) return;

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
        if (droneCamera == null || droneController == null || droneCamera_cam == null
            || droneRigidbody == null) return false;
        if (observerMode == null) return false;

        // Guard: reject if menu has cursor ownership
        if (GameStartMenu.BlocksPlayerCursor)
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
        ShowEngineerBody();

        if (playerInput != null)
        {
            playerInputWasEnabled = playerInput.enabled;
            playerInput.enabled = false;
        }

        if (playerInputBehaviours != null)
        {
            for (int i = 0; i < playerInputBehaviours.Length; i++)
            {
                MonoBehaviour behaviour = playerInputBehaviours[i];
                if (behaviour == null) continue;
                playerInputBehavioursWereEnabled[i] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

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
            MatchPlayerCameraRendering();
            playerCameraWasEnabled = originalPlayerCamera.enabled;
            originalPlayerCamera.enabled = false;
        }

        if (playerAudioListener != null)
        {
            playerAudioListenerWasEnabled = playerAudioListener.enabled;
            playerAudioListener.enabled = false;
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

        if (droneAudioListener != null) droneAudioListener.enabled = true;

        ControlModeChanged?.Invoke(this);

        // 3. Lock cursor if no menu owns it
        if (!GameStartMenu.CursorOwned && !TrapSelectionMenu.CursorOwned)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log("Entered Drone mode");
    }

    private void MatchPlayerCameraRendering()
    {
        if (droneCamera_cam == null) return;

        droneCamera_cam.clearFlags = originalPlayerCamera.clearFlags;
        droneCamera_cam.backgroundColor = originalPlayerCamera.backgroundColor;
        droneCamera_cam.allowHDR = originalPlayerCamera.allowHDR;

        var droneData = droneCamera_cam.GetUniversalAdditionalCameraData();
        originalPlayerCamera.TryGetComponent<UniversalAdditionalCameraData>(out var playerData);
        droneData.renderPostProcessing = playerData != null && playerData.renderPostProcessing;
        droneData.volumeLayerMask = playerData != null ? playerData.volumeLayerMask : (LayerMask)1;
        // Sample local volumes from the same position as the player camera.
        droneData.volumeTrigger = playerData != null && playerData.volumeTrigger != null
            ? playerData.volumeTrigger
            : originalPlayerCamera.transform;
    }

    private void ExitDroneMode()
    {
        // A placement session belongs to the view that started it. Returning to
        // the player clears its preview and restores the normal weapon.
        TrapPlacementController placement = FindAnyObjectByType<TrapPlacementController>();
        if (placement != null && placement.PlacementMode) placement.ExitPlacementMode();

        State = DroneControlState.Player;
        HideEngineerBody();

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

        if (droneAudioListener != null) droneAudioListener.enabled = false;

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

        if (playerAudioListener != null)
            playerAudioListener.enabled = playerAudioListenerWasEnabled;

        if (playerInputBehaviours != null)
        {
            for (int i = 0; i < playerInputBehaviours.Length; i++)
            {
                if (playerInputBehaviours[i] != null)
                    playerInputBehaviours[i].enabled = playerInputBehavioursWereEnabled[i];
            }
        }

        if (playerInput != null) playerInput.enabled = playerInputWasEnabled;

        ControlModeChanged?.Invoke(this);

        // 4. Return cursor control to player/menu system
        // Don't directly unlock here; let player controller handle it
        // based on current menu state

        Debug.Log("Exited Drone mode");
    }

    private void ShowEngineerBody()
    {
        if (playerRoot == null) return;
        if (engineerBody == null)
        {
            var prefab = Resources.Load<GameObject>("FieldEngineer");
            if (prefab == null) return;
            engineerBody = Instantiate(prefab, playerRoot.transform, false);
        }

        var capsule = playerRoot.GetComponent<CharacterController>();
        if (capsule != null)
            engineerBody.transform.localPosition = capsule.center - Vector3.up * (capsule.height * 0.5f);
        engineerBody.SetActive(true);
        if (engineerBody.TryGetComponent<DroneOperatorPose>(out var pose))
            pose.Bind(droneController);
        foreach (Renderer renderer in playerRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)
                || renderer.forceRenderingOff || renderer.transform.IsChildOf(engineerBody.transform)) continue;
            renderer.forceRenderingOff = true;
            hiddenViewMeshes.Add(renderer);
        }
    }

    private void HideEngineerBody()
    {
        if (engineerBody != null) engineerBody.SetActive(false);
        foreach (Renderer renderer in hiddenViewMeshes)
            if (renderer != null) renderer.forceRenderingOff = false;
        hiddenViewMeshes.Clear();
    }

    private void ReleaseEngineerBody()
    {
        if (engineerBody == null) return;
        HideEngineerBody();
        if (Application.isPlaying) Destroy(engineerBody);
        else DestroyImmediate(engineerBody);
        engineerBody = null;
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

        ReleaseEngineerBody();
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

        if (ActiveSession == this)
        {
            ActiveSession = null;
            ControlModeChanged?.Invoke(null);
        }

        if (droneRoot != null)
        {
            if (Application.isPlaying) Destroy(droneRoot);
            else DestroyImmediate(droneRoot);
        }
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
        if (droneRoot != null && droneRoot.scene != scene) return;
        if (State == DroneControlState.Drone) ExitDroneMode();

        // Clean up references without calling Exit (objects may already be destroyed)
        State = DroneControlState.Player;
        if (playerRoot != null)
        {
            PlayerHealth health = playerRoot.GetComponent<PlayerHealth>();
            if (health != null) health.Died -= OnPlayerDied;
        }
        playerRoot = null;
        droneRoot = null;
        droneCamera = null;
        droneController = null;
        droneRigidbody = null;
        droneCamera_cam = null;
        observerMode = null;
        originalPlayerCamera = null;
        playerAudioListener = null;
        droneAudioListener = null;
        fpsController = null;
        fpsPackageMotion = null;
        playerInput = null;
        playerInputBehaviours = null;
        playerInputBehavioursWereEnabled = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (ActiveSession == this)
        {
            ActiveSession = null;
            ControlModeChanged?.Invoke(null);
        }
    }

    private void CapturePlayerInputBehaviours(GameObject root)
    {
        var components = root.GetComponentsInChildren<MonoBehaviour>(true);
        var controls = new List<MonoBehaviour>();
        foreach (MonoBehaviour component in components)
        {
            if (component == null) continue;
            string typeName = component.GetType().Name;
            for (int i = 0; i < PlayerInputComponentNames.Length; i++)
            {
                if (typeName == PlayerInputComponentNames[i])
                {
                    controls.Add(component);
                    break;
                }
            }
        }

        playerInputBehaviours = controls.ToArray();
        playerInputBehavioursWereEnabled = new bool[playerInputBehaviours.Length];
    }

    private void Update()
    {
        // Monitor drone destruction
        if (droneRoot == null && State == DroneControlState.Drone)
        {
            Debug.LogWarning("[DroneControlSession] Drone was destroyed. Exiting to Player state.");
            ExitDroneMode();
        }

        // Stop drone controls while a menu owns the cursor.
        if (State == DroneControlState.Drone)
        {
            bool menuOwnsCursor = GameStartMenu.BlocksPlayerCursor || TrapSelectionMenu.CursorOwned;
            if (menuOwnsCursor)
            {
                if (droneController != null && droneController.enabled)
                {
                    droneController.ResetVelocity();
                    droneController.enabled = false;
                }

                if (droneCamera != null) droneCamera.enabled = false;
            }
            else
            {
                if (droneController != null && !droneController.enabled)
                {
                    droneController.enabled = true;
                }

                if (droneCamera != null && !droneCamera.enabled) droneCamera.enabled = true;
            }
        }
    }
}
#endif
