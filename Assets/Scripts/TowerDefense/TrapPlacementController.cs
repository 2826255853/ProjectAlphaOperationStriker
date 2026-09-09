using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runtime trap placement (4), placement confirmation (left click), and dismantling (E).</summary>
public sealed class TrapPlacementController : MonoBehaviour
{
    [SerializeField] private TrapPlacementGrid grid;
    [SerializeField] private Camera placementCamera;
    [SerializeField] private TrapDefinition selectedTrap;
    [SerializeField] private KeyCode togglePlacementKey = KeyCode.Alpha4;
    [SerializeField] private KeyCode dismantleKey = KeyCode.E;
    [SerializeField] private bool allowRemoveWithRightClick = true;
    [SerializeField] private bool allowDragPlacement = true;
    [SerializeField] private bool placementMode;

    private GameObject preview;
    private TrapInstance hoveredTrap;
    private Vector2Int hoveredCell;
    private bool hasHoveredCell;
    private bool dragPlacement;
    private Vector2Int lastPlacedCell;
    private bool hasLastPlacedCell;
    private readonly List<GameObject> gridMarkers = new List<GameObject>();
    private readonly List<InputAction> suppressedWeaponActions = new List<InputAction>();
    private Material openMaterial, blockedMaterial, footprintMaterial, invalidFootprintMaterial;
    private Material hoveredMaterial;

    public TrapDefinition SelectedTrap { get => selectedTrap; set { selectedTrap = value; RebuildPreview(); } }
    public bool PlacementMode => placementMode;
    public bool HasValidPreview => placementMode && hasHoveredCell && selectedTrap != null && CanPlace(hoveredCell);
    public static bool IsPlacementModeActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        if (FindFirstObjectByType<TrapPlacementController>() != null) return;
        GameObject host = new GameObject("Trap Placement Controller");
        DontDestroyOnLoad(host);
        host.AddComponent<TrapPlacementController>();
    }

    private void Awake()
    {
        if (placementCamera == null) placementCamera = Camera.main;
        if (grid == null) grid = FindFirstObjectByType<TrapPlacementGrid>();
        if (selectedTrap == null) selectedTrap = Resources.Load<TrapDefinition>("AutoSentryTurret");
        SetPlacementMode(placementMode);
    }

    private void Update()
    {
        // The project uses the new Input System, so read the number key from
        // Keyboard.current instead of relying only on the legacy Input API.
        if (WasTogglePlacementKeyPressed()) SetPlacementMode(!placementMode);
        if (placementMode) SetWeaponInputSuppressed(true);
        if (placementCamera == null) placementCamera = Camera.main;
        if (grid == null) grid = FindFirstObjectByType<TrapPlacementGrid>();
        if (grid == null || placementCamera == null) return;
        UpdateHover();
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (hoveredTrap == null)
                hoveredTrap = FindTrapFromRay(placementCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)));
            DismantleHoveredTrap();
        }
        if (!placementMode) return;
        if (WasMouseButtonPressed(0) && HasValidPreview)
        {
            PlaceAt(hoveredCell);
            dragPlacement = allowDragPlacement;
            lastPlacedCell = hoveredCell;
            hasLastPlacedCell = true;
        }
        if (allowDragPlacement && dragPlacement && IsMouseButtonHeld(0) && hasHoveredCell && hasLastPlacedCell)
            PlaceAlongLine(lastPlacedCell, hoveredCell);
        if (WasMouseButtonReleased(0)) { dragPlacement = false; hasLastPlacedCell = false; }
        if (allowRemoveWithRightClick && WasMouseButtonPressed(1)) DismantleHoveredTrap();
    }

    private bool WasTogglePlacementKeyPressed()
    {
        if (Keyboard.current == null) return false;
        if (togglePlacementKey == KeyCode.Keypad4)
            return Keyboard.current[Key.Numpad4].wasPressedThisFrame;
        if (togglePlacementKey == KeyCode.Alpha4)
            return Keyboard.current[Key.Digit4].wasPressedThisFrame;
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
        Plane plane = new Plane(grid.transform.up, grid.CellToWorld(Vector2Int.zero));
        bool previousHover = hasHoveredCell;
        Vector2Int previousCell = hoveredCell;
        hasHoveredCell = plane.Raycast(ray, out float distance) && grid.TryWorldToCell(ray.GetPoint(distance), out hoveredCell);
        hoveredTrap = hasHoveredCell ? FindTrapAt(hoveredCell) : FindTrapFromRay(ray);
        if (!placementMode || selectedTrap == null || !hasHoveredCell)
        {
            SetPreviewVisible(false);
            return;
        }
        EnsurePreview();
        preview.transform.SetPositionAndRotation(grid.CellToWorld(hoveredCell), selectedTrap.LocalRotation);
        SetPreviewVisible(true);
        if (!previousHover || !hasHoveredCell || previousCell != hoveredCell) RefreshGridMarkers();
    }

    private void DismantleHoveredTrap()
    {
        if (hoveredTrap == null) return;
        grid.RemoveTrap(hoveredTrap);
        hoveredTrap = null;
        RefreshGridMarkers();
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
        foreach (TrapInstance trap in grid.PlacedTraps)
        {
            if (trap == null) continue;
            Vector3 toTrap = trap.transform.position - ray.origin;
            float alongRay = Vector3.Dot(toTrap, ray.direction);
            if (alongRay < 0f) continue;
            float distance = Vector3.Cross(ray.direction, toTrap).magnitude;
            if (distance < closestDistance) { closestDistance = distance; closest = trap; }
        }
        return closest;
    }

    private bool CanPlace(Vector2Int cell)
    {
        if (selectedTrap == null) return false;
        Vector2Int size = selectedTrap.Footprint;
        for (int y = 0; y < size.y; y++) for (int x = 0; x < size.x; x++)
        {
            Vector2Int c = cell + new Vector2Int(x, y);
            if (!grid.IsInside(c) || !grid.IsOpen(c) || grid.IsOccupied(c)) return false;
        }
        return true;
    }

    private void PlaceAt(Vector2Int cell)
    {
        if (selectedTrap != null) grid.TryPlaceTrap(cell, selectedTrap, out _, out _);
        RefreshGridMarkers();
    }

    private void PlaceAlongLine(Vector2Int from, Vector2Int to)
    {
        int steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
        for (int i = 1; i <= steps; i++)
        {
            Vector2Int cell = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, i / (float)steps)));
            if (cell == lastPlacedCell) continue;
            PlaceAt(cell);
            lastPlacedCell = cell;
        }
    }

    private TrapInstance FindTrapAt(Vector2Int cell) => grid.TryGetTrapAtCell(cell, out TrapInstance trap) ? trap : null;

    private void SetPlacementMode(bool enabled)
    {
        bool wasPlacementMode = placementMode;
        placementMode = enabled;
        IsPlacementModeActive = enabled;
        dragPlacement = false;
        hasLastPlacedCell = false;
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

        foreach (PlayerInput input in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
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
        if (preview != null) Destroy(preview);
        preview = null;
        if (!placementMode || selectedTrap == null) return;
        preview = selectedTrap.Prefab != null ? Instantiate(selectedTrap.Prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        preview.name = "Trap Preview";
        AutoSentryTurret turret = preview.GetComponent<AutoSentryTurret>();
        if (turret != null) turret.enabled = false;
        foreach (Collider collider in preview.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>()) renderer.material.color = new Color(0.2f, 1f, 0.3f, 0.45f);
        SetPreviewVisible(false);
    }

    private void SetPreviewVisible(bool visible) { if (preview != null) preview.SetActive(visible); }

    private void EnsureMaterials()
    {
        if (openMaterial != null) return;
        openMaterial = CreateOverlayMaterial(new Color(0.1f, 1f, 0.25f, 0.22f));
        blockedMaterial = CreateOverlayMaterial(new Color(1f, 0.15f, 0.1f, 0.12f));
        footprintMaterial = CreateOverlayMaterial(new Color(1f, 0.85f, 0.1f, 0.45f));
        invalidFootprintMaterial = CreateOverlayMaterial(new Color(1f, 0.05f, 0.05f, 0.5f));
        hoveredMaterial = CreateOverlayMaterial(new Color(0.15f, 0.9f, 1f, 0.75f));
    }

    private void RefreshGridMarkers()
    {
        if (!placementMode || grid == null) return;
        EnsureMaterials();
        ClearGridMarkers();
        for (int y = 0; y < grid.Rows; y++) for (int x = 0; x < grid.Columns; x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            Material material = grid.IsOpen(cell) && !grid.IsOccupied(cell) ? openMaterial : blockedMaterial;
            if (hasHoveredCell && selectedTrap != null && IsFootprintCell(hoveredCell, cell))
                material = CanPlace(hoveredCell) ? footprintMaterial : invalidFootprintMaterial;
            if (hasHoveredCell && cell == hoveredCell)
                material = CanPlace(hoveredCell) ? hoveredMaterial : invalidFootprintMaterial;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Trap Grid Marker";
            marker.transform.SetParent(transform, true);
            marker.transform.SetPositionAndRotation(grid.CellToWorld(cell) + grid.transform.up * 0.015f, grid.transform.rotation);
            marker.transform.localScale = new Vector3(grid.CellSize * 0.92f, 0.018f, grid.CellSize * 0.92f);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(marker.GetComponent<Collider>());
            gridMarkers.Add(marker);
        }
    }

    private bool IsFootprintCell(Vector2Int origin, Vector2Int cell)
    {
        Vector2Int size = selectedTrap.Footprint;
        return cell.x >= origin.x && cell.x < origin.x + size.x && cell.y >= origin.y && cell.y < origin.y + size.y;
    }

    private void ClearGridMarkers()
    {
        for (int i = 0; i < gridMarkers.Count; i++) if (gridMarkers[i] != null) Destroy(gridMarkers[i]);
        gridMarkers.Clear();
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
        if (!placementMode) return;
        GUI.Label(new Rect(16f, 16f, 420f, 24f), "陷阱放置模式：左键放置　E 拆卸　4 关闭");
        if (selectedTrap == null) GUI.Label(new Rect(16f, 40f, 420f, 24f), "请在 TrapPlacementController 中选择陷阱类型");
    }

    private void OnDisable()
    {
        IsPlacementModeActive = false;
        SetWeaponInputSuppressed(false);
        if (preview != null) Destroy(preview);
        preview = null;
        ClearGridMarkers();
    }
}
