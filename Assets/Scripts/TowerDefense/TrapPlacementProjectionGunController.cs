using UnityEngine;
using UnityEngine.InputSystem;

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
    private bool missingPrefabWarningIssued;
    private bool subscribed;

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
            if (projectionGunInstance != null) projectionGunInstance.SetActive(false);
            return;
        }

        if (projectionGunPrefab == null)
        {
            if (!missingPrefabWarningIssued)
            {
                Debug.LogWarning("[TrapPlacementProjectionGunController] 未配置投影枪 Prefab；放置模式仍可工作，但不会替换或隐藏普通武器。", this);
                missingPrefabWarningIssued = true;
            }
            return;
        }

        if (projectionGunInstance == null)
        {
            Transform anchor = projectionGunAnchor != null ? projectionGunAnchor : Camera.main != null ? Camera.main.transform : transform;
            projectionGunInstance = Instantiate(projectionGunPrefab, anchor);
            projectionGunInstance.name = projectionGunPrefab.name + " (Projection Gun)";
        }
        projectionGunInstance.SetActive(true);
    }

    private void OnDisable()
    {
        if (placement != null && subscribed) placement.PlacementModeChanged -= OnPlacementModeChanged;
        subscribed = false;
        if (projectionGunInstance != null) projectionGunInstance.SetActive(false);
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
        return slot >= 0;
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
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
