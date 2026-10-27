using UnityEngine;
using UnityEngine.InputSystem;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;

/// <summary>
/// Owns projection-gun mouse input and display state while
/// TrapPlacementController owns placement state, hover resolution, preview
/// rendering, and purchases.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrapPlacementProjectionGunController : MonoBehaviour
{
    [SerializeField] private TrapPlacementController placement;
    [Tooltip("Optional first-person projection-gun placeholder or final prefab. It is never used as a replacement for the normal weapon.")]
    [SerializeField] private GameObject projectionGunPrefab;
    [Tooltip("Optional mount. When empty, the main camera is used while the projection gun is shown.")]
    [SerializeField] private Transform projectionGunAnchor;

    private GameObject projectionGunInstance;
    private Renderer[] hiddenWeaponRenderers;
    private bool[] previousRendererStates;
    private bool weaponVisualsSuppressed;
    private bool missingPrefabWarningIssued;
    private bool subscribed;
    private bool droneControlSubscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        if (FindAnyObjectByType<TrapPlacementProjectionGunController>() != null) return;
        GameObject host = new GameObject("Trap Placement Projection Gun Controller");
        DontDestroyOnLoad(host);
        host.AddComponent<TrapPlacementProjectionGunController>();
    }

    private void Awake()
    {
        TryBindPlacement();
    }

    private void OnEnable()
    {
        TryBindPlacement();
#if ENABLE_DRONE_CODE
        if (!droneControlSubscribed)
        {
            DroneControlSession.ControlModeChanged += OnDroneControlModeChanged;
            droneControlSubscribed = true;
        }
        if (DroneControlSession.ActiveSession != null && DroneControlSession.ActiveSession.IsDroneControlled)
            OnDroneControlModeChanged(DroneControlSession.ActiveSession);
#endif
    }

    private void Update()
    {
        TryBindPlacement();
        if (placement == null) return;
        if (TrapSelectionMenu.CursorOwned || GameStartMenu.CursorOwned) return;

        if (WasEscapePressed() && placement.PlacementMode)
        {
            placement.ExitPlacementMode();
            return;
        }

        // While placing traps, reserve the three weapon-category keys and wheel
        // for returning to the player's normal weapon. Exiting restores its
        // visuals and re-enables the weapon input actions.
        if (placement.PlacementMode && WasNormalWeaponSwitchRequested())
        {
            placement.ExitPlacementMode();
            return;
        }

        if (TryGetTrapSlotPressed(out int slot))
        {
            placement.SelectTrapSlot(slot);
            if (!placement.PlacementMode) placement.EnterPlacementMode();
            return;
        }

        if (!placement.IsProjectionGunInputActive) return;

        if (WasMouseButtonPressed(1)) placement.OnProjectionGunSecondaryAction();
        if (WasMouseButtonPressed(0)) placement.OnProjectionGunPrimaryAction();
        if (IsMouseButtonHeld(0)) placement.OnProjectionGunPrimaryHeld();
        if (WasMouseButtonReleased(0)) placement.OnProjectionGunPrimaryReleased();
    }

    private void TryBindPlacement()
    {
        if (placement == null) placement = FindAnyObjectByType<TrapPlacementController>();
        if (placement == null || subscribed) return;

        placement.PlacementModeChanged += OnPlacementModeChanged;
        subscribed = true;
        OnPlacementModeChanged(placement.PlacementMode);
    }

    private void OnPlacementModeChanged(bool enabled)
    {
        if (!enabled)
        {
            HideProjectionGunAndRestoreWeapon();
            return;
        }

        if (IsDroneModeActive())
        {
            HideProjectionGunAndRestoreWeapon();
            return;
        }

        if (projectionGunPrefab == null)
            projectionGunPrefab = Resources.Load<GameObject>("TrapPlacementProjectionGun");

        ShowResolvedProjectionGun(projectionGunPrefab);
    }

    private static bool IsDroneModeActive()
    {
#if ENABLE_DRONE_CODE
        return DroneControlSession.ActiveSession != null && DroneControlSession.ActiveSession.IsDroneControlled;
#else
        return false;
#endif
    }

    private void HideProjectionGunAndRestoreWeapon()
    {
        if (projectionGunInstance != null) projectionGunInstance.SetActive(false);
        SetNormalWeaponVisualsVisible(true);
    }

#if ENABLE_DRONE_CODE
    private void OnDroneControlModeChanged(DroneControlSession session)
    {
        if (placement == null) TryBindPlacement();
        if (placement != null && placement.PlacementMode)
            OnPlacementModeChanged(true);
    }
#endif

    // Keep resource lookup separate from presentation so the genuine missing-resource
    // fallback can be verified without moving or deleting the shipped Resources asset.
    private void ShowResolvedProjectionGun(GameObject prefab)
    {
        if (prefab == null)
        {
            WarnMissingPrefabOnce();
            return;
        }

        if (projectionGunInstance == null)
        {
            Transform anchor = projectionGunAnchor != null ? projectionGunAnchor : Camera.main != null ? Camera.main.transform : transform;
            projectionGunInstance = Instantiate(prefab, anchor);
            projectionGunInstance.name = prefab.name + " (Projection Gun)";
        }
        projectionGunInstance.SetActive(true);
        SetNormalWeaponVisualsVisible(false);
    }

    private void WarnMissingPrefabOnce()
    {
        if (missingPrefabWarningIssued) return;
        Debug.LogWarning("[TrapPlacementProjectionGunController] 未配置投影枪 Prefab；放置模式仍可工作，但不会替换或隐藏普通武器。", this);
        missingPrefabWarningIssued = true;
    }

    private void SetNormalWeaponVisualsVisible(bool visible)
    {
        if (visible)
        {
            if (!weaponVisualsSuppressed) return;
            for (int i = 0; i < hiddenWeaponRenderers.Length; i++)
                if (hiddenWeaponRenderers[i] != null) hiddenWeaponRenderers[i].enabled = previousRendererStates[i];
            hiddenWeaponRenderers = null;
            previousRendererStates = null;
            weaponVisualsSuppressed = false;
            return;
        }

        if (weaponVisualsSuppressed) return;
        FPSPlayer player = FindAnyObjectByType<FPSPlayer>();
        if (player == null) return;
        FPSWeapon weapon;
        try { weapon = player.GetActiveWeapon(); }
        catch (System.ArgumentOutOfRangeException) { return; }
        if (weapon == null) return;
        hiddenWeaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
        previousRendererStates = new bool[hiddenWeaponRenderers.Length];
        for (int i = 0; i < hiddenWeaponRenderers.Length; i++)
        {
            previousRendererStates[i] = hiddenWeaponRenderers[i] != null && hiddenWeaponRenderers[i].enabled;
            if (hiddenWeaponRenderers[i] != null) hiddenWeaponRenderers[i].enabled = false;
        }
        weaponVisualsSuppressed = true;
    }

    private void OnDisable()
    {
#if ENABLE_DRONE_CODE
        if (droneControlSubscribed)
        {
            DroneControlSession.ControlModeChanged -= OnDroneControlModeChanged;
            droneControlSubscribed = false;
        }
#endif
        if (placement != null && subscribed) placement.PlacementModeChanged -= OnPlacementModeChanged;
        subscribed = false;
        HideProjectionGunAndRestoreWeapon();
    }

    private static bool TryGetTrapSlotPressed(out int slot)
    {
        slot = -1;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) slot = 0;
        else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) slot = 1;
        else if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) slot = 2;
        else if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) slot = 3;
        else if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) slot = 4;
        return slot >= 0;
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private static bool WasNormalWeaponSwitchRequested()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame
            || keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame
            || keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame))
            return true;

        Mouse mouse = Mouse.current;
        return mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
    }

    private static bool WasMouseButtonPressed(int button)
    {
        if (Mouse.current == null) return false;
        return button == 0 ? Mouse.current.leftButton.wasPressedThisFrame : Mouse.current.rightButton.wasPressedThisFrame;
    }

    private static bool IsMouseButtonHeld(int button)
    {
        if (Mouse.current == null) return false;
        return button == 0 ? Mouse.current.leftButton.isPressed : Mouse.current.rightButton.isPressed;
    }

    private static bool WasMouseButtonReleased(int button)
    {
        if (Mouse.current == null) return false;
        return button == 0 ? Mouse.current.leftButton.wasReleasedThisFrame : Mouse.current.rightButton.wasReleasedThisFrame;
    }
}
