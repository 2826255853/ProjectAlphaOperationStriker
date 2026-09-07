using System.Collections.Generic;
using UnityEngine;

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
    private Material openMaterial, blockedMaterial, footprintMaterial, invalidFootprintMaterial;

    public TrapDefinition SelectedTrap { get => selectedTrap; set { selectedTrap = value; RebuildPreview(); } }
    public bool PlacementMode => placementMode;
    public bool HasValidPreview => placementMode && hasHoveredCell && selectedTrap != null && CanPlace(hoveredCell);

    private void Awake()
    {
        if (placementCamera == null) placementCamera = Camera.main;
        if (grid == null) grid = FindFirstObjectByType<TrapPlacementGrid>();
        if (selectedTrap == null) selectedTrap = Resources.Load<TrapDefinition>("AutoSentryTurret");
        SetPlacementMode(placementMode);
    }

    private void Update()
    {
        if (grid == null || placementCamera == null) return;
        if (Input.GetKeyDown(togglePlacementKey)) SetPlacementMode(!placementMode);
        UpdateHover();
        if (Input.GetKeyDown(dismantleKey))
        {
            if (hoveredTrap == null)
                hoveredTrap = FindTrapFromRay(placementCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)));
            DismantleHoveredTrap();
        }
        if (!placementMode) return;
        if (Input.GetMouseButtonDown(0) && HasValidPreview)
        {
            PlaceAt(hoveredCell);
            dragPlacement = allowDragPlacement;
            lastPlacedCell = hoveredCell;
            hasLastPlacedCell = true;
        }
        if (allowDragPlacement && dragPlacement && Input.GetMouseButton(0) && hasHoveredCell && hasLastPlacedCell)
            PlaceAlongLine(lastPlacedCell, hoveredCell);
        if (Input.GetMouseButtonUp(0)) { dragPlacement = false; hasLastPlacedCell = false; }
        if (allowRemoveWithRightClick && Input.GetMouseButtonDown(1)) DismantleHoveredTrap();
    }

    private void UpdateHover()
    {
        Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);
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
        placementMode = enabled;
        dragPlacement = false;
        hasLastPlacedCell = false;
        if (!enabled) { SetPreviewVisible(false); ClearGridMarkers(); }
        else { EnsureMaterials(); RefreshGridMarkers(); }
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
        if (preview != null) Destroy(preview);
        preview = null;
        ClearGridMarkers();
    }
}
