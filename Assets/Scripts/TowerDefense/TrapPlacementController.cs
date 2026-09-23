using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime trap placement, placement confirmation (left click), and
/// dismantling. Outside placement mode the E key dismantles whatever trap the
/// player is aiming at; inside placement mode that key is ignored and the right
/// mouse button removes the hovered trap instead.
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
    [SerializeField] private TrapDefinition[] trapSlots = new TrapDefinition[4];
    [SerializeField, Range(0, 3)] private int selectedSlot;
    [SerializeField] private KeyCode dismantleKey = KeyCode.E;
    [SerializeField] private bool allowRemoveWithRightClick = true;
    [SerializeField] private bool allowDragPlacement = true;
    [SerializeField] private bool placementMode;

    private GameObject preview;
    private Vector3 previewBaseScale = Vector3.one;
    private TrapInstance hoveredTrap;
    private TrapInstance aimedTrap;
    private Vector2Int hoveredCell;
    private bool hasHoveredCell;
    private readonly List<TrapPlacementGrid> grids = new List<TrapPlacementGrid>();
    private TrapPlacementGrid activeGrid;
    private bool dragPlacement;
    private Vector2Int lastPlacedCell;
    private bool hasLastPlacedCell;
    private readonly List<GameObject> gridMarkers = new List<GameObject>();
    private readonly List<InputAction> suppressedWeaponActions = new List<InputAction>();
    private Material openMaterial, blockedMaterial, footprintMaterial, invalidFootprintMaterial;
    private Material hoveredMaterial;
    private readonly EconomyUIState economy = new EconomyUIState();
    private readonly List<Material> previewMaterials = new List<Material>();
    private float failureVisibleUntil;
    private GUIStyle placementHintStyle;

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
    public bool PlacementMode => placementMode;
    public bool HasValidPreview => placementMode && hasHoveredCell && activeGrid != null &&
        selectedTrap != null && CanPurchaseAt(activeGrid, hoveredCell);
    public TrapPlacementGrid ActiveGrid => activeGrid;
    public string LastPlacementFailure { get; private set; } = string.Empty;
    public string PurchaseHint => economy.PurchaseHint(selectedTrap);
    public static bool IsPlacementModeActive { get; private set; }

    public TrapDefinition GetTrapSlot(int slot)
    {
        return trapSlots != null && slot >= 0 && slot < trapSlots.Length ? trapSlots[slot] : null;
    }

    /// <summary>Selects one of the four hotbar slots without changing placement mode.</summary>
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
        if (trapSlots == null || trapSlots.Length != 4)
        {
            TrapDefinition[] previous = trapSlots;
            trapSlots = new TrapDefinition[4];
            if (previous != null)
                for (int i = 0; i < Mathf.Min(previous.Length, trapSlots.Length); i++) trapSlots[i] = previous[i];
        }

        bool hasAssignedSlot = false;
        for (int i = 0; i < trapSlots.Length; i++) hasAssignedSlot |= trapSlots[i] != null;
        if (!hasAssignedSlot && selectedTrap != null) trapSlots[0] = selectedTrap;
        if (!hasAssignedSlot && selectedTrap == null) trapSlots[0] = Resources.Load<TrapDefinition>("AutoSentryTurret");

        // Fill only empty slots so Inspector assignments remain authoritative.
        TrapDefinition[] available = Resources.LoadAll<TrapDefinition>("");
        System.Array.Sort(available, (a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.Ordinal));
        for (int i = 0; i < trapSlots.Length; i++)
        {
            if (trapSlots[i] != null) continue;
            for (int j = 0; j < available.Length; j++)
            {
                if (available[j] == null) continue;
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
        if (placementCamera == null) placementCamera = Camera.main;
        InitializeTrapSlots();
        RefreshGrids();
        SelectTrapSlot(selectedSlot);
        SetPlacementMode(placementMode);
    }

    private void OnEnable()
    {
        economy.Changed += RefreshPurchaseFeedback;
        economy.Enable();
    }

    private void Update()
    {
        economy.Refresh();
        if (TrapSelectionMenu.CursorOwned) return;
        // The project uses the new Input System, so read the number keys from
        // Keyboard.current instead of relying only on the legacy Input API.
        if (TryGetTrapSlotPressed(out int slot))
        {
            SelectTrapSlot(slot);
            if (!placementMode) SetPlacementMode(true);
        }
        if (WasEscapePressed()) SetPlacementMode(false);
        if (placementMode) SetWeaponInputSuppressed(true);
        if (placementCamera == null) placementCamera = Camera.main;
        if (grids.Count == 0) RefreshGrids();
        if (grids.Count == 0 || placementCamera == null) return;
        UpdateHover();
        // E disassembles the trap under the crosshair, but only while trap
        // placement mode is off: the placement flow owns its own remove input
        // (right mouse button), so E stays reserved for the weapon layer there.
        aimedTrap = placementMode ? null : FindTrapFromRay(AimRay());
        if (!placementMode && WasDismantleKeyPressed())
            DismantleAimedTrap();
        if (!placementMode) return;
        // Report the actual placement failure even when the preview is red.
        if (WasMouseButtonPressed(0) && hasHoveredCell)
        {
            dragPlacement = PlaceAt(hoveredCell) && allowDragPlacement;
            lastPlacedCell = hoveredCell;
            hasLastPlacedCell = true;
        }
        if (allowDragPlacement && dragPlacement && IsMouseButtonHeld(0) && hasHoveredCell && hasLastPlacedCell)
            PlaceAlongLine(lastPlacedCell, hoveredCell);
        if (WasMouseButtonReleased(0)) { dragPlacement = false; hasLastPlacedCell = false; }
        if (allowRemoveWithRightClick && WasMouseButtonPressed(1)) DismantleHoveredTrap();
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
        TrapPlacementGrid bestUsable = null, bestOccupied = null, bestAny = null;
        float usableDistance = float.PositiveInfinity, occupiedDistance = float.PositiveInfinity, anyDistance = float.PositiveInfinity;
        Vector2Int usableCell = default, occupiedCell = default, anyCell = default;
        for (int i = 0; i < grids.Count; i++)
        {
            TrapPlacementGrid candidate = grids[i];
            if (candidate == null) continue;
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
            else if (!occupied && CanPlace(candidate, candidateCell) && distance < usableDistance)
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

    private bool TryGetTrapSlotPressed(out int slot)
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
        bool previousHover = hasHoveredCell;
        Vector2Int previousCell = hoveredCell;
        TrapPlacementGrid previousGrid = activeGrid;
        TrapPlacementGrid resolved = ResolveGridForRay(ray, out Vector2Int resolvedCell);
        hasHoveredCell = resolved != null;
        if (resolved != null)
        {
            activeGrid = resolved;
            hoveredCell = resolvedCell;
        }
        hoveredTrap = hasHoveredCell ? FindTrapAt(activeGrid, hoveredCell) : FindTrapFromRay(ray);
        if (!placementMode || selectedTrap == null || !hasHoveredCell || activeGrid == null)
        {
            SetPreviewVisible(false);
            return;
        }
        EnsurePreview();
        if (!activeGrid.TryGetFootprint(selectedTrap, out Vector2Int footprint))
        {
            SetPreviewVisible(false);
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
            : new Vector3(previewBaseScale.x * footprint.x, previewBaseScale.y, previewBaseScale.z * footprint.y);
        SetPreviewVisible(true);
        UpdatePreviewColor();
        if (!previousHover || !hasHoveredCell || previousCell != hoveredCell || previousGrid != activeGrid)
            RefreshGridMarkers();
    }

    private void DismantleHoveredTrap()
    {
        if (hoveredTrap == null) return;
        TrapPlacementGrid owner = FindOwningGrid(hoveredTrap);
        if (owner != null) owner.RemoveTrap(hoveredTrap);
        hoveredTrap = null;
        RefreshGridMarkers();
    }

    /// <summary>
    /// The crosshair ray. Placement mode aims with the mouse pointer, while
    /// freeroam aims with the locked cursor, which sits at the viewport centre.
    /// </summary>
    private Ray AimRay() => placementCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

    /// <summary>
    /// Removes the trap the player is aiming at. Only reachable while trap
    /// placement mode is off; placement mode dismantles with the right mouse
    /// button so the E key stays free for gameplay input.
    /// </summary>
    private void DismantleAimedTrap()
    {
        TrapInstance target = aimedTrap;
        if (target == null) return;
        aimedTrap = null;
        TrapPlacementGrid owner = FindOwningGrid(target);
        if (owner != null) owner.RemoveTrap(target);
        if (hoveredTrap == target) hoveredTrap = null;
    }

    private TrapPlacementGrid FindOwningGrid(TrapInstance trap)
    {
        for (int i = 0; i < grids.Count; i++)
            if (grids[i] != null && grids[i].OwnsTrap(trap)) return grids[i];
        return null;
    }

    private TrapInstance FindTrapFromRay(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            TrapInstance hitTrap = hit.collider.GetComponentInParent<TrapInstance>();
            if (hitTrap != null) return hitTrap;
        }

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

    private void UpdatePreviewColor()
    {
        Color color = HasValidPreview ? new Color(0.2f, 1f, 0.3f, 0.45f)
            : new Color(1f, 0.18f, 0.12f, 0.6f);
        foreach (Material material in previewMaterials)
            if (material != null) material.color = color;
    }

    private bool PlaceAt(Vector2Int cell)
    {
        bool purchased = TrapPurchaseService.TryPurchase(activeGrid, cell, selectedTrap, out _, out string failure);
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

    private TrapInstance FindTrapAt(TrapPlacementGrid target, Vector2Int cell)
    {
        if (target == null) return null;
        return target.TryGetTrapAtCell(cell, out TrapInstance trap) ? trap : null;
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
        else { EnsureMaterials(); RefreshGridMarkers(); }
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

        foreach (PlayerInput input in FindObjectsByType<PlayerInput>())
        {
            if (input.actions == null) continue;
            foreach (InputAction action in input.actions)
            {
                string actionName = action.name.ToLowerInvariant();
                if (!actionName.Contains("attack") && !actionName.Contains("fire") &&
                    !actionName.Contains("shoot") && !actionName.Contains("aim") &&
                    !actionName.Contains("reload")) continue;
                if (action.enabled)
                {
                    action.Disable();
                    suppressedWeaponActions.Add(action);
                }
            }
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
        previewBaseScale = preview.transform.localScale;
        AutoSentryTurret turret = preview.GetComponent<AutoSentryTurret>();
        if (turret != null) turret.enabled = false;
        // The launcher mount would otherwise swivel the ghost toward enemies.
        MissileLauncherTurret launcher = preview.GetComponent<MissileLauncherTurret>();
        if (launcher != null) launcher.enabled = false;
        // And the firing half would otherwise launch real missiles from a ghost.
        MissileLauncherWeapon launcherWeapon = preview.GetComponent<MissileLauncherWeapon>();
        if (launcherWeapon != null) launcherWeapon.enabled = false;
        foreach (GroundSpikeTrap spikes in preview.GetComponentsInChildren<GroundSpikeTrap>(true)) spikes.enabled = false;
        foreach (TrapInstance trap in preview.GetComponentsInChildren<TrapInstance>(true)) trap.enabled = false;
        foreach (Collider collider in preview.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
            previewMaterials.AddRange(renderer.materials);
        UpdatePreviewColor();
        SetPreviewVisible(false);
    }

    private void SetPreviewVisible(bool visible) { if (preview != null) preview.SetActive(visible); }

    private void ReleasePreviewMaterials()
    {
        foreach (Material material in previewMaterials)
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
        previewMaterials.Clear();
    }

    private void EnsureMaterials()
    {
        if (openMaterial != null) return;
        openMaterial = CreateOverlayMaterial(new Color(0.1f, 1f, 0.25f, 0.75f));
        blockedMaterial = CreateOverlayMaterial(new Color(1f, 0.15f, 0.1f, 0.12f));
        footprintMaterial = CreateOverlayMaterial(new Color(1f, 0.85f, 0.1f, 0.45f));
        invalidFootprintMaterial = CreateOverlayMaterial(new Color(1f, 0.05f, 0.05f, 0.5f));
        hoveredMaterial = CreateOverlayMaterial(new Color(0.15f, 0.9f, 1f, 0.75f));
    }

    private void RefreshGridMarkers()
    {
        if (!placementMode || grids.Count == 0) return;
        EnsureMaterials();
        int used = 0;
        // Every authored grid is drawn, so the player sees the usable cells of
        // both the ground level and the raised platform level at the same time.
        for (int g = 0; g < grids.Count; g++)
        {
            TrapPlacementGrid target = grids[g];
            if (target == null) continue;
            bool hoveringThisGrid = hasHoveredCell && target == activeGrid;
            for (int y = 0; y < target.Rows; y++) for (int x = 0; x < target.Columns; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                bool occupied = target.IsOccupied(cell);
                bool open = target.IsOpenForTrap(cell, selectedTrap);
                bool inFootprint = hoveringThisGrid && selectedTrap != null && IsFootprintCell(hoveredCell, cell);
                // Only usable cells are drawn, plus the footprint the player is
                // about to occupy. Drawing every blocked cell of every grid would
                // spawn thousands of extra overlays for no added information.
                if (!open && !occupied && !inFootprint) continue;
                Material material = open && !occupied ? openMaterial : blockedMaterial;
                if (inFootprint && cell != hoveredCell)
                    material = CanPurchaseAt(target, hoveredCell) ? footprintMaterial : invalidFootprintMaterial;
                if (hoveringThisGrid && cell == hoveredCell)
                    material = CanPurchaseAt(target, hoveredCell) ? hoveredMaterial : invalidFootprintMaterial;
                GameObject marker = RentMarker(used++);
                marker.transform.SetParent(transform, true);
                marker.transform.SetPositionAndRotation(target.CellToWorld(cell) + target.transform.up * 0.015f, target.transform.rotation);
                marker.transform.localScale = new Vector3(target.CellSize * 0.92f, 0.018f, target.CellSize * 0.92f);
                marker.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
        for (int i = used; i < gridMarkers.Count; i++) if (gridMarkers[i] != null) gridMarkers[i].SetActive(false);
    }

    /// <summary>
    /// Reuses marker objects between rebuilds. A hovered cell change repaints
    /// the overlay, and recreating thousands of primitives each move is far more
    /// expensive than re-positioning them.
    /// </summary>
    private GameObject RentMarker(int index)
    {
        while (gridMarkers.Count <= index)
        {
            GameObject created = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.name = "Trap Grid Marker";
            created.transform.SetParent(transform, true);
            Destroy(created.GetComponent<Collider>());
            gridMarkers.Add(created);
        }
        GameObject marker = gridMarkers[index];
        if (!marker.activeSelf) marker.SetActive(true);
        return marker;
    }

    private bool IsFootprintCell(Vector2Int origin, Vector2Int cell)
    {
        if (activeGrid == null || !activeGrid.TryGetFootprint(selectedTrap, out Vector2Int size)) return false;
        return cell.x >= origin.x && cell.x < origin.x + size.x && cell.y >= origin.y && cell.y < origin.y + size.y;
    }

    private void ClearGridMarkers()
    {
        for (int i = 0; i < gridMarkers.Count; i++) if (gridMarkers[i] != null) gridMarkers[i].SetActive(false);
    }

    private static Material CreateOverlayMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };
        material.SetFloat("_Surface", 1f);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
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
                GUI.Label(new Rect(18f, hintTop, 460f, 24f), $"按 E 拆除：{DescribeTrap(aimedTrap)}");
            return;
        }
        GUI.Label(new Rect(18f, hintTop, 620f, 24f), "陷阱放置模式：左键放置　右键拆除　Esc 退出");
        string slotText = "槽位 ";
        for (int i = 0; i < 4; i++)
        {
            TrapDefinition definition = GetTrapSlot(i);
            string name = definition != null ? definition.DisplayName : "空";
            slotText += $"{i + 4}:{name}" + (i == selectedSlot ? " [当前]" : "") + (i < 3 ? "   " : "");
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
        economy.Changed -= RefreshPurchaseFeedback;
        economy.Dispose();
        ReleasePreviewMaterials();
        IsPlacementModeActive = false;
        SetWeaponInputSuppressed(false);
        if (preview != null) Destroy(preview);
        preview = null;
        ClearGridMarkers();
    }
}
