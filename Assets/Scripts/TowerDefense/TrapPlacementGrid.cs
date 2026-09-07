using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer-authored grid for trap placement. The local X/Z plane is the grid.
/// Availability is edited in the custom inspector (row-major openCells array) or
/// painted directly in the Scene view via TrapPlacementGridEditor.
/// </summary>
public sealed class TrapPlacementGrid : MonoBehaviour
{
    [Header("Grid layout")]
    [SerializeField, Min(1)] private int columns = 16;
    [SerializeField, Min(1)] private int rows = 10;
    [SerializeField, Min(1f)] private float cellSize = 1f;
    [SerializeField] private float placementHeight;
    [SerializeField] private Transform trapParent;

    [Header("Developer-authored availability")]
    [Tooltip("One entry per cell, row-major (x + y * columns). True means a trap may be placed there.")]
    [SerializeField] private bool[] openCells;

    private readonly Dictionary<Vector2Int, TrapInstance> occupiedCells = new Dictionary<Vector2Int, TrapInstance>();
    private readonly HashSet<TrapInstance> placedTraps = new HashSet<TrapInstance>();

    public int Columns => columns;
    public int Rows => rows;
    public float CellSize => cellSize;
    public IReadOnlyCollection<TrapInstance> PlacedTraps => placedTraps;

    private void Awake()
    {
        EnsureCellData();
        RebuildOccupancy();
    }

    private void OnEnable()
    {
        // Also runs in the editor, allowing scene-authored traps to be edited
        // immediately after a domain reload or scene reopen.
        EnsureCellData();
        RebuildOccupancy();
    }

    private void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        cellSize = Mathf.Max(1f, cellSize);
        EnsureCellData();
    }

    public void EnsureCellData()
    {
        int count = columns * rows;
        if (openCells != null && openCells.Length == count) return;
        var previous = openCells;
        openCells = new bool[count];
        if (previous != null) System.Array.Copy(previous, openCells, Mathf.Min(previous.Length, openCells.Length));
    }

    /// <summary>Reconstructs runtime/editor occupancy from serialized TrapInstance data.</summary>
    public void RebuildOccupancy()
    {
        occupiedCells.Clear();
        placedTraps.Clear();
        var traps = new List<TrapInstance>(GetComponentsInChildren<TrapInstance>(true));
        if (trapParent != null && trapParent != transform)
        {
            var externalTraps = trapParent.GetComponentsInChildren<TrapInstance>(true);
            for (int i = 0; i < externalTraps.Length; i++)
                if (!traps.Contains(externalTraps[i])) traps.Add(externalTraps[i]);
        }
        foreach (var trap in traps)
        {
            if (trap == null || trap.Definition == null || !IsInside(trap.OriginCell)) continue;
            Vector2Int footprint = trap.Definition.Footprint;
            bool valid = true;
            for (int y = 0; y < footprint.y && valid; y++)
            for (int x = 0; x < footprint.x; x++)
            {
                Vector2Int cell = trap.OriginCell + new Vector2Int(x, y);
                if (!IsInside(cell) || occupiedCells.ContainsKey(cell)) { valid = false; break; }
            }
            if (!valid) continue;
            placedTraps.Add(trap);
            for (int y = 0; y < footprint.y; y++)
            for (int x = 0; x < footprint.x; x++)
                occupiedCells[trap.OriginCell + new Vector2Int(x, y)] = trap;
        }
    }

    public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows;

    public bool IsOpen(Vector2Int cell)
    {
        EnsureCellData();
        return IsInside(cell) && openCells[cell.x + cell.y * columns];
    }

    public bool IsOccupied(Vector2Int cell) => occupiedCells.ContainsKey(cell);

    /// <summary>Returns the trap occupying a cell, if any.</summary>
    public bool TryGetTrapAtCell(Vector2Int cell, out TrapInstance instance)
    {
        return occupiedCells.TryGetValue(cell, out instance) && instance != null;
    }

    public void SetOpen(Vector2Int cell, bool open)
    {
        EnsureCellData();
        if (IsInside(cell)) openCells[cell.x + cell.y * columns] = open;
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        Vector3 local = new Vector3((cell.x + 0.5f) * cellSize, placementHeight, (cell.y + 0.5f) * cellSize);
        return transform.TransformPoint(local);
    }

    public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.z / cellSize));
        return IsInside(cell);
    }

    public bool TryPlaceTrap(Vector2Int origin, TrapDefinition definition, out TrapInstance instance, out string failure)
    {
        instance = null;
        failure = string.Empty;
        if (definition == null) { failure = "No trap definition selected."; return false; }
        Vector2Int footprint = definition.Footprint;
        var cells = new List<Vector2Int>(footprint.x * footprint.y);
        for (int y = 0; y < footprint.y; y++)
        for (int x = 0; x < footprint.x; x++)
        {
            var cell = origin + new Vector2Int(x, y);
            if (!IsInside(cell)) { failure = "Trap footprint is outside the grid."; return false; }
            if (!IsOpen(cell)) { failure = $"Cell {cell} is not open for trap placement."; return false; }
            if (IsOccupied(cell)) { failure = $"Cell {cell} is already occupied."; return false; }
            cells.Add(cell);
        }

        GameObject root = definition.Prefab != null
            ? Instantiate(definition.Prefab, CellToWorld(origin), definition.LocalRotation)
            : CreateFallbackTrap(definition, origin, footprint);
        root.transform.SetParent(trapParent != null ? trapParent : transform, true);
        instance = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
        instance.Initialize(definition, origin);
        placedTraps.Add(instance);
        foreach (var cell in cells) occupiedCells[cell] = instance;
        return true;
    }

    /// <summary>
    /// Places a batch of traps in one validation/placement pass. Origins are
    /// expressed in grid coordinates. When <paramref name="atomic"/> is true
    /// (the default), no trap is created if any origin is invalid; this makes
    /// editor tools and drag painting safe to use with a single undo action.
    /// </summary>
    public int TryPlaceTraps(IReadOnlyList<Vector2Int> origins, TrapDefinition definition,
        out List<TrapInstance> instances, out string failure, bool atomic = true)
    {
        instances = new List<TrapInstance>();
        failure = string.Empty;
        if (definition == null) { failure = "No trap definition selected."; return 0; }
        if (origins == null || origins.Count == 0) return 0;

        var planned = new HashSet<Vector2Int>();
        var footprint = definition.Footprint;
        for (int i = 0; i < origins.Count; i++)
        {
            Vector2Int origin = origins[i];
            bool valid = true;
            var candidateCells = new List<Vector2Int>(footprint.x * footprint.y);
            for (int y = 0; y < footprint.y && valid; y++)
            for (int x = 0; x < footprint.x; x++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                if (!IsInside(cell) || !IsOpen(cell) || IsOccupied(cell) || planned.Contains(cell))
                {
                    valid = false;
                    failure = !IsInside(cell) ? "Trap footprint is outside the grid."
                        : !IsOpen(cell) ? $"Cell {cell} is not open for trap placement."
                        : IsOccupied(cell) ? $"Cell {cell} is already occupied."
                        : $"Trap origins overlap at cell {cell}.";
                    break;
                }
                candidateCells.Add(cell);
            }
            if (!valid && atomic) return 0;
            if (valid)
                for (int c = 0; c < candidateCells.Count; c++) planned.Add(candidateCells[c]);
        }

        // In non-atomic mode, validation above only reserves cells. Re-check
        // each origin while placing so invalid entries can be skipped safely.
        for (int i = 0; i < origins.Count; i++)
        {
            if (TryPlaceTrap(origins[i], definition, out TrapInstance instance, out _))
                instances.Add(instance);
            else if (atomic)
            {
                // This should be unreachable unless an external caller changed
                // occupancy between validation and placement; clean up anyway.
                for (int j = 0; j < instances.Count; j++) RemoveTrap(instances[j]);
                instances.Clear();
                failure = "Grid changed while placing traps.";
                return 0;
            }
        }
        return instances.Count;
    }

    /// <summary>Convenience helper for quickly tiling a rectangular area.</summary>
    public int TryPlaceRectangle(Vector2Int origin, Vector2Int size, TrapDefinition definition,
        out List<TrapInstance> instances, out string failure, bool atomic = true)
    {
        instances = new List<TrapInstance>();
        failure = string.Empty;
        if (size.x <= 0 || size.y <= 0) return 0;
        Vector2Int footprint = definition != null ? definition.Footprint : Vector2Int.one;
        var origins = new List<Vector2Int>(size.x * size.y);
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
            origins.Add(origin + new Vector2Int(x * footprint.x, y * footprint.y));
        return TryPlaceTraps(origins, definition, out instances, out failure, atomic);
    }

    public bool RemoveTrap(TrapInstance instance)
    {
        if (instance == null || !placedTraps.Remove(instance)) return false;
        var toRemove = new List<Vector2Int>();
        foreach (var pair in occupiedCells) if (pair.Value == instance) toRemove.Add(pair.Key);
        foreach (var cell in toRemove) occupiedCells.Remove(cell);
        if (Application.isPlaying) Destroy(instance.gameObject); else DestroyImmediate(instance.gameObject);
        return true;
    }

    private GameObject CreateFallbackTrap(TrapDefinition definition, Vector2Int origin, Vector2Int footprint)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.transform.position = CellToWorld(origin);
        root.transform.rotation = definition.LocalRotation;
        root.transform.localScale = new Vector3(footprint.x * cellSize * 0.8f, 0.25f, footprint.y * cellSize * 0.8f);
        return root;
    }

    private void OnDrawGizmos()
    {
        EnsureCellData();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            var cell = new Vector2Int(x, y);
            Gizmos.color = IsOccupied(cell) ? new Color(1f, 0.65f, 0.1f, 0.75f) :
                (IsOpen(cell) ? new Color(0.2f, 1f, 0.35f, 0.28f) : new Color(1f, 0.15f, 0.15f, 0.12f));
            Vector3 center = CellToWorld(cell);
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(cellSize * 0.96f, 0.025f, cellSize * 0.96f));
            Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(cellSize, 0.03f, cellSize));
            Gizmos.matrix = old;
        }
    }
}
