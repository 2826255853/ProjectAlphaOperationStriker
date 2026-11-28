using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime trap placement, placement confirmation (left click), and
/// dismantling. Outside placement mode the E key dismantles whatever trap the
/// player is aiming at; inside placement mode that key is ignored and the right
/// mouse button only raises the reserved projection-gun action.
/// </summary>
public sealed class TrapPlacementController : MonoBehaviour
{
    // Optional explicit primary grid. When left empty every TrapPlacementGrid in
    // the scene is discovered automatically, which is what allows ground level
    // and raised platforms to share one placement controller.
    [SerializeField] private TrapPlacementGrid grid;
    [SerializeField] private bool autoDiscoverGrids = true;
    [SerializeField] private Camera placementCamera;
    [SerializeField] private TrapDefinition selectedTrap;
    [SerializeField] private TrapDefinition[] trapSlots = new TrapDefinition[5];
    [SerializeField, Range(0, 4)] private int selectedSlot;
    [SerializeField] private KeyCode dismantleKey = KeyCode.E;
    [SerializeField] private bool allowDragPlacement = true;
    [SerializeField] private bool placementMode;
    [SerializeField, Min(0.1f), Tooltip("墙面陷阱从正面可交互的最大距离（米）。")]
    private float wallPlacementDistance = 6f;

    private GameObject preview;
    private Vector3 previewBaseScale = Vector3.one;
    private RaycastHit[] aimHits = new RaycastHit[16];
    private TrapInstance aimedTrap;
    private Vector2Int hoveredCell;
    private bool hasHoveredCell;
    private readonly List<TrapPlacementGrid> grids = new List<TrapPlacementGrid>();
    private TrapPlacementGrid activeGrid;
    private bool dragPlacement;
    private Vector2Int lastPlacedCell;
    private bool hasLastPlacedCell;
    private readonly Dictionary<TrapPlacementGrid, TrapPlacementOverlay> gridOverlays = new Dictionary<TrapPlacementGrid, TrapPlacementOverlay>();
    private readonly List<InputAction> suppressedWeaponActions = new List<InputAction>();
    private Material[] overlayMaterials;
    private readonly EconomyUIState economy = new EconomyUIState();
    private readonly List<Material> previewMaterials = new List<Material>();
    private float failureVisibleUntil;
    private GUIStyle placementHintStyle;
#if ENABLE_DRONE_CODE
    private Camera playerPlacementCamera;
    private bool droneControlSubscribed;
    private bool placementCameraWasConfigured;
#endif

    public TrapDefinition SelectedTrap
    {
        get => selectedTrap;
        set
        {
            selectedTrap = value;
            if (trapSlots != null && selectedSlot >= 0 && selectedSlot < trapSlots.Length) trapSlots[selectedSlot] = value;
            RebuildPreview();
        }
    }
    public int SelectedSlot => selectedSlot;
    public int TrapSlotCount => trapSlots != null ? trapSlots.Length : 0;
    /// <summary>Read-only placement state; a purchase or slot switch keeps this mode active.</summary>
    public bool PlacementMode => placementMode;
    /// <summary>True when projection-gun input may be forwarded this frame.</summary>
    public bool IsProjectionGunInputActive => isActiveAndEnabled && placementMode
        && !TrapSelectionMenu.CursorOwned
        && !GameStartMenu.CursorOwned;
    public bool HasValidPreview => placementMode && hasHoveredCell && activeGrid != null &&
        selectedTrap != null && CanPurchaseAt(activeGrid, hoveredCell);
    public TrapPlacementGrid ActiveGrid => activeGrid;
    public Camera PlacementCamera => placementCamera;
    public string LastPlacementFailure { get; private set; } = string.Empty;
    public string PurchaseHint => economy.PurchaseHint(selectedTrap);
    public static bool IsPlacementModeActive { get; private set; }

    /// <summary>Raised for a secondary projection-gun action in placement mode.</summary>
    public event Action ProjectionGunSecondaryAction;

    /// <summary>Raised after the placement-mode state changes.</summary>
    public event Action<bool> PlacementModeChanged;

    /// <summary>
    /// Attempts the existing hovered-cell purchase. Update owns hover and drag
    /// input until the dedicated projection-gun controller takes over input polling.
    /// </summary>
    public bool OnProjectionGunPrimaryAction()
    {
        if (!IsProjectionGunInputActive || !hasHoveredCell) return false;
        bool purchased = PlaceAt(hoveredCell);
        dragPlacement = purchased && allowDragPlacement;
        lastPlacedCell = hoveredCell;
        hasLastPlacedCell = true;
        return purchased;
    }

    /// <summary>Forwards the projection-gun secondary action without gameplay side effects.</summary>
    public void OnProjectionGunSecondaryAction()
    {
        if (!IsProjectionGunInputActive) return;
        ProjectionGunSecondaryAction?.Invoke();
    }

    public void OnProjectionGunPrimaryHeld()
    {
        if (!IsProjectionGunInputActive || !allowDragPlacement || !dragPlacement || !hasHoveredCell || !hasLastPlacedCell) return;
        PlaceAlongLine(lastPlacedCell, hoveredCell);
    }

    public void OnProjectionGunPrimaryReleased()
    {
        if (!IsProjectionGunInputActive) return;
        dragPlacement = false;
        hasLastPlacedCell = false;
    }

    public void ExitPlacementMode()
    {
        SetPlacementMode(false);
    }

    public void EnterPlacementMode()
    {
        SetPlacementMode(true);
    }

    public TrapDefinition GetTrapSlot(int slot)
    {
        return trapSlots != null && slot >= 0 && slot < trapSlots.Length ? trapSlots[slot] : null;
    }

    /// <summary>Selects one of the five hotbar slots without changing placement mode.</summary>
    public bool SelectTrapSlot(int slot)
    {
        if (trapSlots == null || slot < 0 || slot >= trapSlots.Length) return false;
        selectedSlot = slot;
        selectedTrap = trapSlots[slot];
        LastPlacementFailure = string.Empty;
        RebuildPreview();
        if (placementMode) RefreshGridMarkers();
        return selectedTrap != null;
    }

    /// <summary>Assigns a definition to a hotbar slot and refreshes the active preview.</summary>
    public bool AssignTrapToSlot(int slot, TrapDefinition definition)
    {
        if (trapSlots == null || slot < 0 || slot >= trapSlots.Length) return false;
        trapSlots[slot] = definition;
        if (selectedSlot == slot) SelectTrapSlot(slot);
        return true;
    }

    private void InitializeTrapSlots()
    {
        if (trapSlots == null || trapSlots.Length != 5)
        {
            TrapDefinition[] previous = trapSlots;
            trapSlots = new TrapDefinition[5];
            if (previous != null)
                for (int i = 0; i < Mathf.Min(previous.Length, trapSlots.Length); i++) trapSlots[i] = previous[i];
        }

        // Discard stale defaults for support assets before filling empty slots.
        if (!IsStandaloneTrap(selectedTrap)) selectedTrap = null;
        for (int i = 0; i < trapSlots.Length; i++)
            if (!IsStandaloneTrap(trapSlots[i])) trapSlots[i] = null;

        bool hasAssignedSlot = false;
        for (int i = 0; i < trapSlots.Length; i++) hasAssignedSlot |= trapSlots[i] != null;
        if (!hasAssignedSlot && selectedTrap != null) trapSlots[0] = selectedTrap;
        if (!hasAssignedSlot && selectedTrap == null) trapSlots[0] = Resources.Load<TrapDefinition>("AutoSentryTurret");

        // Fill only empty slots so Inspector assignments remain authoritative.
        TrapDefinition[] available = Resources.LoadAll<TrapDefinition>("");
        System.Array.Sort(available, (a, b) => string.Compare(a != null ? a.DisplayName : string.Empty,
            b != null ? b.DisplayName : string.Empty, System.StringComparison.Ordinal));
        for (int i = 0; i < trapSlots.Length; i++)
        {
            if (trapSlots[i] != null) continue;
            for (int j = 0; j < available.Length; j++)
            {
                if (!IsStandaloneTrap(available[j])) continue;
                bool alreadyUsed = false;
                for (int k = 0; k < i; k++) alreadyUsed |= trapSlots[k] == available[j];
                if (!alreadyUsed) { trapSlots[i] = available[j]; break; }
            }
        }
        selectedSlot = Mathf.Clamp(selectedSlot, 0, trapSlots.Length - 1);
        if (trapSlots[selectedSlot] == null)
            for (int i = 0; i < trapSlots.Length; i++)
                if (trapSlots[i] != null) { selectedSlot = i; break; }
    }

    internal static bool IsStandaloneTrap(TrapDefinition definition)
    {
        return definition != null &&
            !string.Equals(definition.TrapId, "ceiling_test", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(definition.TrapId, SentryPhalanxBranchAdapter.BranchId, StringComparison.Ordinal);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        if (FindAnyObjectByType<TrapPlacementController>() != null) return;
        GameObject host = new GameObject("Trap Placement Controller");
        DontDestroyOnLoad(host);
        host.AddComponent<TrapPlacementController>();
    }

    private void Awake()
    {
#if ENABLE_DRONE_CODE
        placementCameraWasConfigured = placementCamera != null;
        if (placementCamera == null)
        {
            DroneControlSession activeSession = DroneControlSession.ActiveSession;
            placementCamera = activeSession != null && activeSession.ControlCamera != null
                ? activeSession.ControlCamera : Camera.main;
        }
        playerPlacementCamera = placementCamera;
#else
        if (placementCamera == null) placementCamera = Camera.main;
#endif
        InitializeTrapSlots();
        RefreshGrids();
        SelectTrapSlot(selectedSlot);
        SetPlacementMode(placementMode);
    }

    private void OnEnable()
    {
        IsPlacementModeActive = placementMode;
        economy.Changed += RefreshPurchaseFeedback;
        economy.Enable();
#if ENABLE_DRONE_CODE
        if (!droneControlSubscribed)
        {
            DroneControlSession.ControlModeChanged += OnDroneControlModeChanged;
            droneControlSubscribed = true;
        }
        DroneControlSession activeSession = DroneControlSession.ActiveSession;
        if (activeSession != null && activeSession.ControlCamera != null)
        {
            if (activeSession.IsDroneControlled)
                placementCamera = activeSession.ControlCamera;
            else if (!placementCameraWasConfigured || playerPlacementCamera == null)
            {
                placementCamera = activeSession.ControlCamera;
                playerPlacementCamera = placementCamera;
            }
        }
#endif
    }

    private void Update()
    {
        economy.Refresh();
        if (TrapSelectionMenu.CursorOwned || GameStartMenu.CursorOwned) return;
        if (placementMode) SetWeaponInputSuppressed(true);
#if ENABLE_DRONE_CODE
        DroneControlSession activeSession = DroneControlSession.ActiveSession;
        if (activeSession != null && activeSession.IsDroneControlled && activeSession.ControlCamera != null)
            placementCamera = activeSession.ControlCamera;
        else if (placementCamera == null)
            placementCamera = Camera.main;
#else
        if (placementCamera == null) placementCamera = Camera.main;
#endif
        if (grids.Count == 0) RefreshGrids();
        if (grids.Count == 0 || placementCamera == null) return;
        UpdateHover();
        // E disassembles the aimed trap only outside placement mode.
        aimedTrap = placementMode ? null : FindTrapFromRay(AimRay());
        if (!placementMode && WasDismantleKeyPressed())
            DismantleAimedTrap(WasSellModifierHeld());
        if (!placementMode) return;
    }

    /// <summary>
    /// Rebuilds the list of placement grids. A scene may author one grid per
    /// surface height (ground level, raised platform top, ...), so the
    /// controller resolves which grid the cursor points at every frame instead
    /// of assuming a single grid.
    /// </summary>
    private void RefreshGrids()
    {
        grids.Clear();
        if (grid != null) grids.Add(grid);
        if (autoDiscoverGrids || grids.Count == 0)
        {
            TrapPlacementGrid[] found = FindObjectsByType<TrapPlacementGrid>();
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null && !grids.Contains(found[i])) grids.Add(found[i]);
        }
        // A persistent controller can enter placement again after an additive scene unload.
        if (gridOverlays.Count > 0)
        {
            var stale = new List<TrapPlacementGrid>();
            foreach (var entry in gridOverlays)
                if (entry.Key == null || !grids.Contains(entry.Key)) stale.Add(entry.Key);
            foreach (var removed in stale)
            {
                gridOverlays[removed].Dispose();
                gridOverlays.Remove(removed);
            }
        }
        if (grids.Count == 0) { activeGrid = null; return; }
        if (activeGrid == null || !grids.Contains(activeGrid)) activeGrid = grids[0];
    }

    /// <summary>
    /// Picks the grid the aim ray points at. A cell the player can actually use
    /// wins over a nearer surface that has nothing to offer, so hovering the
    /// road still targets the ground grid even though the platform plane is
    /// crossed first.
    /// </summary>
    private TrapPlacementGrid ResolveGridForRay(Ray ray, out Vector2Int cell)
    {
        // Wall grids are finite, front-facing surfaces. Resolve the nearest
        // visible wall first and never skip it merely because its hovered cell
        // is blocked: the player must see the actual failure on that wall.
        if (selectedTrap != null && selectedTrap.MountType != TrapMountType.Ground)
        {
            TrapPlacementGrid nearestWall = null;
            Vector2Int nearestWallCell = default;
            float nearestWallDistance = float.PositiveInfinity;
            for (int i = 0; i < grids.Count; i++)
            {
                TrapPlacementGrid candidate = grids[i];
                if (candidate == null || !candidate.IsMountedSurface || !candidate.SupportsDefinition(selectedTrap)) continue;
                Vector3 normal = candidate.SurfaceNormal;
                if (Vector3.Dot(ray.direction, normal) >= -0.001f) continue;
                if (!candidate.TryRaycastPlane(ray, out Vector3 point, out float distance)) continue;
                if (distance > Mathf.Max(0.1f, wallPlacementDistance)) continue;
                if (!candidate.TryWorldToCell(point, out Vector2Int candidateCell)) continue;
                if (distance >= nearestWallDistance) continue;
                if (IsWallRayOccluded(candidate, ray, distance)) continue;
                nearestWall = candidate;
                nearestWallCell = candidateCell;
                nearestWallDistance = distance;
            }
            cell = nearestWallCell;
            return nearestWall;
        }

        TrapPlacementGrid bestUsable = null, bestOccupied = null, bestAny = null;
        float usableDistance = float.PositiveInfinity, occupiedDistance = float.PositiveInfinity, anyDistance = float.PositiveInfinity;
        Vector2Int usableCell = default, occupiedCell = default, anyCell = default;
        for (int i = 0; i < grids.Count; i++)
        {
            TrapPlacementGrid candidate = grids[i];
            if (candidate == null || candidate.IsMountedSurface) continue;
            if (!candidate.TryRaycastPlane(ray, out Vector3 point, out float distance)) continue;
            if (!candidate.TryWorldToCell(point, out Vector2Int candidateCell)) continue;
            if (distance < anyDistance) { anyDistance = distance; bestAny = candidate; anyCell = candidateCell; }
            bool occupied = candidate.IsOccupied(candidateCell);
            if (occupied && distance < occupiedDistance)
            {
                occupiedDistance = distance;
                bestOccupied = candidate;
                occupiedCell = candidateCell;
            }
            else if (!occupied && distance < usableDistance && candidate.IsOpen(candidateCell) && CanPlace(candidate, candidateCell))
            {
                usableDistance = distance;
                bestUsable = candidate;
                usableCell = candidateCell;
            }
        }

        if (bestUsable != null) { cell = usableCell; return bestUsable; }
        if (bestOccupied != null) { cell = occupiedCell; return bestOccupied; }
        cell = anyCell;
        return bestAny;
    }

    /// <summary>
    /// Returns true when a physical object blocks the selected wall. The
    /// support collider itself is allowed to sit just behind the grid plane;
    /// triggers, preview ghosts and overlay markers never count as blockers.
    /// </summary>
    private bool IsWallRayOccluded(TrapPlacementGrid candidate, Ray ray, float wallDistance)
    {
        int hitCount = RaycastAim(ray, Mathf.Max(0.1f, wallDistance + 0.25f));
        float supportTolerance = Mathf.Max(0.05f, candidate.SurfaceOffset + 0.1f);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = aimHits[i].collider;
            if (collider == null || collider.isTrigger) continue;
            if (collider.GetComponentInParent<TrapPlacementPreview>() != null) continue;
            // Existing trap bodies sit on the same finite plane. They should
            // remain selectable as occupied cells instead of hiding the grid.
            if (collider.GetComponentInParent<TrapInstance>() != null) continue;
            if (candidate.SupportCollider != null && collider == candidate.SupportCollider &&
                Mathf.Abs(aimHits[i].distance - wallDistance) <= supportTolerance) continue;
            if (aimHits[i].distance < wallDistance - 0.02f) return true;
        }
        return false;
    }

    private bool TryGetTrapSlotPressed(out int slot)
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

    /// <summary>
    /// Reads the dismantle key through the new Input System so the serialized
    /// setting keeps working instead of being hard-coded to E.
    /// </summary>
    private bool WasDismantleKeyPressed()
    {
        if (Keyboard.current == null) return false;
        if (dismantleKey == KeyCode.E)
            return Keyboard.current.eKey.wasPressedThisFrame;
        return false;
    }

    /// <summary>
    /// Shift turns a dismantle into a sell (partial refund). Read through the
    /// new Input System so it works regardless of the legacy input setting.
    /// </summary>
    private static bool WasSellModifierHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
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

    private void UpdateHover()
    {
        Vector2 mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Ray ray = placementCamera.ScreenPointToRay(mousePosition);
        TrapPlacementGrid previousGrid = activeGrid;
        TrapPlacementGrid resolved = ResolveGridForRay(ray, out Vector2Int resolvedCell);
        hasHoveredCell = resolved != null;
        if (resolved != null)
        {
            activeGrid = resolved;
            hoveredCell = resolvedCell;
        }
        // A drag is one continuous stroke on one surface. Crossing to another
        // wall (or from a wall to the ground) must not draw a line in the
        // second grid or accidentally bridge unrelated coordinate systems.
        if (dragPlacement && (resolved == null || previousGrid != resolved))
        {
            dragPlacement = false;
            hasLastPlacedCell = false;
        }
        if (!placementMode || selectedTrap == null || !hasHoveredCell || activeGrid == null)
        {
            SetPreviewVisible(false);
            if (placementMode) RefreshGridMarkers(false);
            return;
        }
        EnsurePreview();
        if (!activeGrid.TryGetFootprint(selectedTrap, out Vector2Int footprint))
        {
            SetPreviewVisible(false);
            if (placementMode) RefreshGridMarkers(false);
            return;
        }
        // Keep the ghost anchored and sized exactly like the instance that
        // TrapPlacementGrid will create (including multi-cell footprints).
        preview.transform.SetPositionAndRotation(
            activeGrid.GetTrapWorldPosition(hoveredCell, footprint),
            activeGrid.GetTrapWorldRotation(selectedTrap));
        // Authored prefabs (including the 2x2m missile launcher) already carry
        // their world scale. Only the primitive fallback needs footprint scaling.
        preview.transform.localScale = selectedTrap.Prefab != null
            ? previewBaseScale
            : activeGrid.IsMountedSurface
            ? new Vector3(footprint.x * activeGrid.CellWorldSize.x * 0.8f, footprint.y * activeGrid.CellWorldSize.y * 0.8f, 0.15f)
            : new Vector3(previewBaseScale.x * footprint.x, previewBaseScale.y, previewBaseScale.z * footprint.y);
        ShowHoveredPreview();
        bool valid = HasValidPreview;
        ApplyPreviewColor(valid);
        RefreshGridMarkers(valid);
    }

    /// <summary>
    /// The crosshair ray. Placement mode aims with the mouse pointer, while
    /// freeroam aims with the locked cursor, which sits at the viewport centre.
    /// </summary>
    private Ray AimRay() => placementCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

    /// <summary>
    /// Removes the trap the player is aiming at. Only reachable while trap
    /// placement mode is off, so the E key stays free for gameplay input in
    /// placement mode. Holding Shift sells
    /// instead, which pays part of the purchase price back.
    /// </summary>
    private void DismantleAimedTrap(bool sell)
    {
        TrapInstance target = aimedTrap;
        if (target == null) return;
        aimedTrap = null;
        RemoveTrap(target, sell);
    }

    /// <summary>
    /// The single removal path. sell routes through TrapSaleService (partial
    /// refund at the configured rate); otherwise the trap is dismantled
    /// outright, which intentionally pays nothing back.
    /// </summary>
    private void RemoveTrap(TrapInstance trap, bool sell)
    {
        if (trap == null) return;
        TrapPlacementGrid owner = FindOwningGrid(trap);
        if (owner == null) return;
        if (!sell)
        {
            owner.RemoveTrap(trap);
            return;
        }
        bool sold = TrapSaleService.TrySell(owner, trap, out int refund, out string failure);
        LastPlacementFailure = sold
            ? (refund > 0 ? $"已出售，回收 {refund} 金币" : "已出售，该陷阱无回收款")
            : failure;
        failureVisibleUntil = Time.unscaledTime + 2.5f;
    }

    private TrapPlacementGrid FindOwningGrid(TrapInstance trap)
    {
        for (int i = 0; i < grids.Count; i++)
            if (grids[i] != null && grids[i].OwnsTrap(trap)) return grids[i];
        return null;
    }

    private TrapInstance FindTrapFromRay(Ray ray)
    {
        float nearestPhysicalBlock = float.PositiveInfinity;
        TrapInstance nearestHitTrap = null;
        float nearestHitTrapDistance = float.PositiveInfinity;
        int hitCount = RaycastAim(ray, 1000f);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = aimHits[i].collider;
            if (collider == null || collider.isTrigger) continue;
            if (collider.GetComponentInParent<TrapPlacementPreview>() != null) continue;
            TrapInstance hitTrap = collider.GetComponentInParent<TrapInstance>();
            if (hitTrap != null)
            {
                if (aimHits[i].distance < nearestHitTrapDistance)
                {
                    nearestHitTrap = hitTrap;
                    nearestHitTrapDistance = aimHits[i].distance;
                }
                continue;
            }
            nearestPhysicalBlock = Mathf.Min(nearestPhysicalBlock, aimHits[i].distance);
        }
        if (nearestHitTrap != null && nearestHitTrapDistance <= nearestPhysicalBlock + 0.05f)
            return nearestHitTrap;

        // Turret visuals intentionally have no colliders, so also resolve the
        // trap closest to the aim ray. This keeps E usable with the generated
        // machine-gun model and other collider-free trap prefabs.
        TrapInstance closest = null;
        float closestDistance = 0.35f;
        for (int g = 0; g < grids.Count; g++)
        {
            if (grids[g] == null) continue;
            foreach (TrapInstance trap in grids[g].PlacedTraps)
            {
                if (trap == null) continue;
                Vector3 toTrap = trap.transform.position - ray.origin;
                float alongRay = Vector3.Dot(toTrap, ray.direction);
                if (alongRay < 0f) continue;
                if (alongRay > nearestPhysicalBlock + 0.05f) continue;
                float distance = Vector3.Cross(ray.direction, toTrap).magnitude;
                if (distance < closestDistance) { closestDistance = distance; closest = trap; }
            }
        }
        return closest;
    }

    private bool CanPlace(TrapPlacementGrid target, Vector2Int cell)
    {
        return target != null && target.CanPlaceTrap(cell, selectedTrap, out _);
    }

    private bool CanPurchaseAt(TrapPlacementGrid target, Vector2Int cell) =>
        CanPlace(target, cell) && EconomyManager.Instance != null
        && EconomyManager.Instance.CanAfford(selectedTrap.Cost);

    private void RefreshPurchaseFeedback()
    {
        // Committed wallet changes invalidate a failure message from the previous balance.
        LastPlacementFailure = string.Empty;
        UpdatePreviewColor();
        RefreshGridMarkers();
    }

    private void UpdatePreviewColor() => ApplyPreviewColor(HasValidPreview);

    private void ApplyPreviewColor(bool valid)
    {
        Color color = valid ? new Color(0.2f, 1f, 0.3f, 0.45f)
            : new Color(1f, 0.18f, 0.12f, 0.6f);
        foreach (Material material in previewMaterials)
            if (material != null) material.color = color;
    }

    private bool PlaceAt(Vector2Int cell)
    {
        bool purchased = TrapPurchaseService.TryPurchase(activeGrid, cell, selectedTrap, out _, out string failure);
        if (purchased && hasHoveredCell && cell == hoveredCell) ShowHoveredPreview();
        LastPlacementFailure = failure;
        failureVisibleUntil = Time.unscaledTime + 3f;
        UpdatePreviewColor();
        RefreshGridMarkers();
        return purchased;
    }

    private void PlaceAlongLine(Vector2Int from, Vector2Int to)
    {
        int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
        for (int i = 1; i <= steps; i++)
        {
            Vector2Int cell = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, i / (float)steps)));
            if (cell == lastPlacedCell) continue;
            EconomyManager wallet = EconomyManager.Instance;
            if (selectedTrap == null || wallet == null || !wallet.CanAfford(selectedTrap.Cost))
            {
                economy.Refresh();
                LastPlacementFailure = wallet == null || !wallet.IsInitialized ? "金币暂不可用，连续放置已停止。"
                    : selectedTrap == null || selectedTrap.Cost < 0 ? "陷阱配置无效，连续放置已停止。"
                    : $"金币不足，还差 {Mathf.Max(0, selectedTrap.Cost - wallet.AvailableBalance)}，连续放置已停止。";
                failureVisibleUntil = Time.unscaledTime + 3f;
                dragPlacement = false;
                hasLastPlacedCell = false;
                break;
            }
            PlaceAt(cell);
            lastPlacedCell = cell;
        }
    }

    private int RaycastAim(Ray ray, float distance)
    {
        // A full NonAlloc buffer can omit the nearest blocker. Grow it once
        // and retry so dense scenes keep the same occlusion result.
        int count;
        while ((count = Physics.RaycastNonAlloc(ray, aimHits, distance, Physics.DefaultRaycastLayers,
                   QueryTriggerInteraction.Ignore)) == aimHits.Length)
            Array.Resize(ref aimHits, aimHits.Length * 2);
        return count;
    }

    private void SetPlacementMode(bool enabled)
    {
        bool wasPlacementMode = placementMode;
        placementMode = enabled;
        IsPlacementModeActive = enabled;
        dragPlacement = false;
        hasLastPlacedCell = false;
        LastPlacementFailure = string.Empty;
        if (enabled) RefreshGrids();
        if (enabled && !wasPlacementMode)
            Debug.Log("进入陷阱放置状态");
        SetWeaponInputSuppressed(enabled);
        if (!enabled) { SetPreviewVisible(false); ClearGridMarkers(); }
        else RefreshGridMarkers();
        if (wasPlacementMode != enabled) PlacementModeChanged?.Invoke(enabled);
    }

    private void SetWeaponInputSuppressed(bool suppressed)
    {
        if (!suppressed)
        {
            for (int i = 0; i < suppressedWeaponActions.Count; i++)
                suppressedWeaponActions[i]?.Enable();
            suppressedWeaponActions.Clear();
            return;
        }

        if (!Application.isPlaying)
        {
            foreach (var input in FindObjectsByType<PlayerInput>()) SuppressActions(input);
            return;
        }
        foreach (var input in PlayerInput.all) SuppressActions(input);
    }

    private void SuppressActions(PlayerInput input)
    {
        if (input.actions == null) return;
        foreach (var map in input.actions.actionMaps)
        foreach (var action in map.actions)
        {
            if (!action.enabled) continue;
            string name = action.name;
            if (name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("fire", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("shoot", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("aim", StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("reload", StringComparison.OrdinalIgnoreCase) < 0) continue;
            action.Disable();
            suppressedWeaponActions.Add(action);
        }
    }

    private void EnsurePreview()
    {
        if (preview != null && preview.name == "Trap Preview") return;
        RebuildPreview();
    }

    private void RebuildPreview()
    {
        ReleasePreviewMaterials();
        if (preview != null) Destroy(preview);
        preview = null;
        if (!placementMode || selectedTrap == null) return;
        preview = selectedTrap.Prefab != null ? Instantiate(selectedTrap.Prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        preview.name = "Trap Preview";
        // Imported model cameras must never render over the player/drone view
        // when the ghost is activated over a placement grid. Include inactive
        // children so showing the preview cannot reactivate a staging camera.
        foreach (Camera camera in preview.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in preview.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (Light light in preview.GetComponentsInChildren<Light>(true)) light.enabled = false;
        // Ghost bodies are never solid entities, damageable targets or valid
        // backing geometry; the marker keeps validation and support probes honest.
        if (preview.GetComponent<TrapPlacementPreview>() == null) preview.AddComponent<TrapPlacementPreview>();
        previewBaseScale = preview.transform.localScale;
        AutoSentryTurret turret = preview.GetComponent<AutoSentryTurret>();
        if (turret != null) turret.enabled = false;
        // The launcher mount would otherwise swivel the ghost toward enemies.
        MissileLauncherTurret launcher = preview.GetComponent<MissileLauncherTurret>();
        if (launcher != null) launcher.enabled = false;
        if (preview.TryGetComponent(out MissileLauncherDeployment missileDeployment))
            missileDeployment.HoldPlacementPose();
        // And the firing half would otherwise launch real missiles from a ghost.
        MissileLauncherWeapon launcherWeapon = preview.GetComponent<MissileLauncherWeapon>();
        if (launcherWeapon != null) launcherWeapon.enabled = false;
        foreach (GroundSpikeTrap spikes in preview.GetComponentsInChildren<GroundSpikeTrap>(true)) spikes.enabled = false;
        foreach (Betty7JumpMineTrap mines in preview.GetComponentsInChildren<Betty7JumpMineTrap>(true)) mines.enabled = false;
        foreach (RepairStationTrap station in preview.GetComponentsInChildren<RepairStationTrap>(true)) station.enabled = false;
        foreach (ThroatLampTrap lamp in preview.GetComponentsInChildren<ThroatLampTrap>(true))
        {
            lamp.enabled = false;
            lamp.HoldPlacementPose();
        }
        foreach (XM777Trap artillery in preview.GetComponentsInChildren<XM777Trap>(true)) artillery.enabled = false;
        foreach (XM777DeploymentAnimation deployment in preview.GetComponentsInChildren<XM777DeploymentAnimation>(true))
            deployment.enabled = false; // Keep the folded pose initialized by Awake while hovering.
        foreach (RG777DeploymentAnimation deployment in preview.GetComponentsInChildren<RG777DeploymentAnimation>(true))
            deployment.Begin(true);
        foreach (TrapInstance trap in preview.GetComponentsInChildren<TrapInstance>(true)) trap.enabled = false;
        foreach (Collider collider in preview.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
            previewMaterials.AddRange(renderer.materials);
        UpdatePreviewColor();
        SetPreviewVisible(false);
    }

    private void SetPreviewVisible(bool visible) { if (preview != null) preview.SetActive(visible); }

    private void ShowHoveredPreview()
    {
        // Keep the stowed preview from covering the mechanism at its placement anchor.
        bool coversDeployingTrap = activeGrid.TryGetTrapAtCell(hoveredCell, out TrapInstance placed)
            && placed.OriginCell == hoveredCell && placed.Definition == selectedTrap
            && (placed.TryGetComponent<XM777DeploymentAnimation>(out _) || placed.TryGetComponent<ThroatLampTrap>(out _)
                || placed.TryGetComponent<MissileLauncherDeployment>(out _));
        SetPreviewVisible(!coversDeployingTrap);
    }

    private void ReleasePreviewMaterials()
    {
        foreach (Material material in previewMaterials)
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
        previewMaterials.Clear();
    }

    private void RefreshGridMarkers() => RefreshGridMarkers(HasValidPreview);

    private void RefreshGridMarkers(bool valid)
    {
        if (!placementMode || grids.Count == 0) return;
        if (overlayMaterials == null)
            overlayMaterials = new[] {
                CreateOverlayMaterial(new Color(.1f, 1f, .25f, .75f)),
                CreateOverlayMaterial(new Color(1f, .15f, .1f, .12f)),
                CreateOverlayMaterial(new Color(1f, .85f, .1f, .45f)),
                CreateOverlayMaterial(new Color(1f, .05f, .05f, .5f)),
                CreateOverlayMaterial(new Color(.15f, .9f, 1f, .75f))
            };
        RectInt highlight = default;
        if (hasHoveredCell && activeGrid != null && activeGrid.TryGetFootprint(selectedTrap, out var size))
            highlight = new RectInt(hoveredCell, size);
        foreach (var target in grids)
        {
            if (target == null) continue;
            if (!gridOverlays.TryGetValue(target, out var overlay))
            {
                overlay = new TrapPlacementOverlay(target, overlayMaterials);
                gridOverlays.Add(target, overlay);
            }
            bool hovered = target == activeGrid && highlight.width > 0;
            overlay.Refresh(selectedTrap, hovered ? highlight : default, hovered && valid);
        }
    }

    private void ClearGridMarkers()
    {
        foreach (var overlay in gridOverlays.Values) overlay.Hide();
    }

    private void ReleaseGridOverlays()
    {
        foreach (var overlay in gridOverlays.Values) overlay.Dispose();
        gridOverlays.Clear();
        if (overlayMaterials == null) return;
        foreach (var material in overlayMaterials)
        {
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
        overlayMaterials = null;
    }

    private static Material CreateOverlayMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };
        material.SetFloat("_Surface", 1f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.renderQueue = 3000;
        return material;
    }

    private void OnGUI()
    {
        if (TrapSelectionMenu.IsOpen) return;
        economy.Refresh();
        // Keep placement hints below the wave and economy HUDs.
        const float hintTop = 218f;
        if (!placementMode)
        {
            // Freeroam hint: name the trap the E key would dismantle right now.
            if (aimedTrap != null)
                GUI.Label(new Rect(18f, hintTop, 620f, 24f),
                    $"按 E 拆除：{DescribeTrap(aimedTrap)}　Shift+E 出售（回收 ~{TrapSaleService.RefundFor(aimedTrap)} 金币）");
            return;
        }
        GUI.Label(new Rect(18f, hintTop, 760f, 24f), "陷阱放置模式：左键放置　Esc 退出");
        string slotText = "槽位 ";
        for (int i = 0; i < 5; i++)
        {
            TrapDefinition definition = GetTrapSlot(i);
            string name = definition != null ? definition.DisplayName : "空";
            slotText += $"{i + 4}:{name}" + (i == selectedSlot ? " [当前]" : "") + (i < 4 ? "   " : "");
        }
        GUI.Label(new Rect(18f, hintTop + 24f, Screen.width - 36f, 24f), slotText);
        if (placementHintStyle == null)
            placementHintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true,
                normal = { textColor = new Color(1f, 0.82f, 0.28f) } };
        string hint = selectedTrap == null ? "当前槽位未配置陷阱，可按 N 分配"
            : $"{selectedTrap.DisplayName}  花费 {selectedTrap.Cost}  "
                + (selectedTrap.AllowsPlatformPlacement ? "可放置：地面 / 高台" : "仅限地面（含道路）")
                + $"  {PurchaseHint}";
        GUI.Label(new Rect(18f, hintTop + 50f, Screen.width - 36f, 48f), hint, placementHintStyle);
        if (Time.unscaledTime < failureVisibleUntil && !string.IsNullOrEmpty(LastPlacementFailure))
            GUI.Label(new Rect(18f, hintTop + 98f, Screen.width - 36f, 48f), LastPlacementFailure, placementHintStyle);
    }

    private static string DescribeTrap(TrapInstance trap)
    {
        if (trap == null) return string.Empty;
        return trap.Definition != null ? trap.Definition.DisplayName : trap.gameObject.name;
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
        SetPlacementMode(false);
        economy.Changed -= RefreshPurchaseFeedback;
        economy.Dispose();
        ReleasePreviewMaterials();
        if (preview != null) Destroy(preview);
        preview = null;
        ReleaseGridOverlays();
    }

#if ENABLE_DRONE_CODE
    private void OnDroneControlModeChanged(DroneControlSession session)
    {
        if (session != null && session.IsDroneControlled && session.ControlCamera != null)
        {
            if (placementCamera != null && placementCamera != session.ControlCamera)
                playerPlacementCamera = placementCamera;
            placementCamera = session.ControlCamera;
            return;
        }

        if (session != null && session.ControlCamera != null
            && (!placementCameraWasConfigured || playerPlacementCamera == null))
            playerPlacementCamera = session.ControlCamera;
        placementCamera = playerPlacementCamera != null ? playerPlacementCamera
            : session != null && session.ControlCamera != null ? session.ControlCamera : Camera.main;
    }
#endif
}
