using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

    /// <summary>
    /// Initializes an independent placement mask. The caller supplies a snapshot,
    /// so this grid never needs to reference the monster path grid at runtime.
    /// </summary>
    public void ConfigureLayout(int newColumns, int newRows, float newCellSize, float newHeight, bool[] openCellSnapshot)
    {
        columns = Mathf.Max(1, newColumns);
        rows = Mathf.Max(1, newRows);
        cellSize = Mathf.Max(0.01f, newCellSize);
        placementHeight = newHeight;
        openCells = new bool[columns * rows];
        if (openCellSnapshot != null)
            System.Array.Copy(openCellSnapshot, openCells, Mathf.Min(openCellSnapshot.Length, openCells.Length));
        EnsureCellData();
        RebuildOccupancy();
    }

    public bool HasSameOpenCells(bool[] other)
    {
        EnsureCellData();
        if (other == null || other.Length != openCells.Length) return false;
        for (int i = 0; i < openCells.Length; i++) if (openCells[i] != other[i]) return false;
        return true;
    }

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
            // Scene-authored traps may have been saved with the old cell-center
            // placement. Re-align their root so the model and every collider
            // follow the grid intersection coordinate after loading.
            trap.transform.SetPositionAndRotation(TrapWorldPosition(trap.OriginCell, trap.Definition.Footprint), trap.Definition.LocalRotation);
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
        // Trap positions are anchored to grid-line intersections (as in Go),
        // rather than the visual center of a cell. Cell indices still describe
        // the same area for availability and occupancy checks.
        Vector3 local = new Vector3(cell.x * cellSize, placementHeight, cell.y * cellSize);
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

        GameObject root = CreateTrapObject(definition, origin, footprint);
        root.transform.SetParent(trapParent != null ? trapParent : transform, true);
        instance = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
        instance.Initialize(definition, origin);
        // Keep the root authoritative for both visuals and colliders. This is
        // also applied after parenting because trapParent may have a transform.
        root.transform.SetPositionAndRotation(TrapWorldPosition(origin, definition.Footprint), definition.LocalRotation);
#if UNITY_EDITOR
        // Objects created by the editor painting tool must be scene objects,
        // otherwise they can be discarded during a domain reload/reopen.
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(instance);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        }
#endif
        placedTraps.Add(instance);
        foreach (var cell in cells) occupiedCells[cell] = instance;
        return true;
    }

    private GameObject CreateTrapObject(TrapDefinition definition, Vector2Int origin, Vector2Int footprint)
    {
        if (definition.Prefab != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // PrefabUtility preserves a proper prefab instance in the
                // scene, including added/generated child meshes.
                GameObject editorInstance = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, gameObject.scene);
                editorInstance.transform.SetPositionAndRotation(TrapWorldPosition(origin, footprint), definition.LocalRotation);
                return editorInstance;
            }
#endif
            return Instantiate(definition.Prefab, TrapWorldPosition(origin, footprint), definition.LocalRotation);
        }
        return CreateFallbackTrap(definition, origin, footprint);
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
        root.transform.position = TrapWorldPosition(origin, footprint);
        root.transform.rotation = definition.LocalRotation;
        root.transform.localScale = new Vector3(footprint.x * cellSize * 0.8f, 0.25f, footprint.y * cellSize * 0.8f);
        // Keep the fallback usable when a definition's prefab reference is
        // unavailable at runtime. AutoSentryTurret builds its machine-gun
        // visuals in Awake, replacing the otherwise bare gray cube.
        if (definition.TrapId == "auto_sentry_turret")
        {
            root.AddComponent<AutoSentryTurret>();
            // The cube is only the generic fallback footprint; hide it once
            // the turret component has generated its visible machine gun.
            Renderer renderer = root.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            Collider collider = root.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }
        return root;
    }

    private Vector3 TrapWorldPosition(Vector2Int origin, Vector2Int footprint)
    {
        Vector3 intersection = CellToWorld(origin);
        // The imported turret mesh has its visual/pickup origin half a cell
        // toward the left/rear of its prefab root. Compensate that authored
        // pivot offset so the model and colliders sit on the intended crossing.
        Vector3 localOffset = new Vector3((footprint.x * 0.5f - 0.5f) * cellSize, 0f,
            (footprint.y * 0.5f - 0.5f) * cellSize);
        return intersection + transform.TransformVector(localOffset);
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
