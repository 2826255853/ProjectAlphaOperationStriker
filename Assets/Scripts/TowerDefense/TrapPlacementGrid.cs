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
    public enum SurfaceKind { Legacy = 0, Ground = 1, Road = 2, Platform = 3 }

    [Header("Grid layout")]
    [SerializeField, Tooltip("放置表面类型；Legacy 按旧网格名称识别，兼容已有场景。")]
    private SurfaceKind surfaceKind;
    [SerializeField, Min(1)] private int columns = 16;
    [SerializeField, Min(1)] private int rows = 10;
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField] private float placementHeight;
    [SerializeField] private Transform trapParent;

    [Header("Surface category")]
    [SerializeField, Tooltip("表面类别：Ground=地面/道路/高台（旧网格默认值），Wall=指定墙面网格。显式配置，不靠对象名或旋转角推断。")]
    private TrapMountType surfaceType = TrapMountType.Ground;
    [SerializeField, Tooltip("墙面网格绑定的静态墙体碰撞体；墙面支撑校验与遮挡判定使用。")]
    private Collider supportCollider;
    [SerializeField, Min(0f), Tooltip("墙面贴合与网格绘制偏移（米）；不影响地面 placementHeight 语义。")]
    private float surfaceOffset = 0.01f;

    [Header("Developer-authored availability")]
    [Tooltip("One entry per cell, row-major (x + y * columns). True means a trap may be placed there.")]
    [SerializeField] private bool[] openCells;
    [SerializeField, Tooltip("兼容旧道路网格的标记；道路上的陷阱碰撞体设为触发器，避免阻挡移动。")]
    private bool walkableTrapsOnly;

    private readonly Dictionary<Vector2Int, TrapInstance> occupiedCells = new Dictionary<Vector2Int, TrapInstance>();
    private readonly HashSet<TrapInstance> placedTraps = new HashSet<TrapInstance>();

    public int Columns => columns;
    public int Rows => rows;
    public float CellSize => cellSize;
    public float PlacementHeight => placementHeight;
    public SurfaceKind Surface => surfaceKind != SurfaceKind.Legacy ? surfaceKind
        : gameObject.name == "TrapPlacementGrid_Platform" ? SurfaceKind.Platform
        : walkableTrapsOnly || gameObject.name == "TrapPlacementGrid_Road" ? SurfaceKind.Road
        : SurfaceKind.Ground;
    private bool RequiresWalkableTrap => SurfaceType == TrapMountType.Ground && Surface == SurfaceKind.Road;

    /// <summary>Installation surface category. Wall grids only accept Wall traps.</summary>
    public TrapMountType SurfaceType => surfaceType;
    public bool IsWallSurface => surfaceType == TrapMountType.Wall;
    public Collider SupportCollider => supportCollider;
    public float SurfaceOffset => Mathf.Max(0f, surfaceOffset);

    /// <summary>Outward-facing normal of the placement surface (+Z of the grid).</summary>
    public Vector3 SurfaceNormal => transform.forward;

    /// <summary>Unified category matching used by placement, painting and validation.</summary>
    public bool SupportsDefinition(TrapDefinition definition) => surfaceType.SupportsDefinition(definition);
    public IReadOnlyCollection<TrapInstance> PlacedTraps => placedTraps;

    /// <summary>True when this grid created/manages the given trap instance.</summary>
    public bool OwnsTrap(TrapInstance trap) => trap != null && placedTraps.Contains(trap);

    /// <summary>
    /// Initializes an independent placement mask. The caller supplies a snapshot,
    /// so this grid never needs to reference the monster path grid at runtime.
    /// </summary>
    public void ConfigureLayout(int newColumns, int newRows, float newCellSize, float newHeight, bool[] openCellSnapshot,
        SurfaceKind newSurfaceKind = SurfaceKind.Legacy)
    {
        ConfigureLayout(newColumns, newRows, newCellSize, newHeight, openCellSnapshot, newSurfaceKind,
            TrapMountType.Ground);
    }

    /// <summary>
    /// Wall-aware layout entry point. Ground callers keep using the legacy
    /// overload above; nothing changes for existing grids.
    /// </summary>
    public void ConfigureLayout(int newColumns, int newRows, float newCellSize, float newHeight, bool[] openCellSnapshot,
        SurfaceKind newSurfaceKind, TrapMountType newSurfaceType, Collider newSupportCollider = null,
        float newSurfaceOffset = 0.01f)
    {
        surfaceType = newSurfaceType;
        supportCollider = newSupportCollider;
        surfaceOffset = Mathf.Max(0f, newSurfaceOffset);
        surfaceKind = newSurfaceKind;
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
        cellSize = Mathf.Max(0.01f, cellSize);
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
            if (!TryGetFootprint(trap.Definition, out Vector2Int footprint)) continue;
            // One shared pose for spawn, preview, rebuild and painting keeps a
            // reloaded wall trap on the same wall spot instead of dropping it
            // back to a ground anchor.
            GetPlacementPose(trap.OriginCell, trap.Definition, out Vector3 posePosition, out Quaternion poseRotation);
            trap.transform.SetPositionAndRotation(posePosition, poseRotation);
            MakeTrapPassableIfRequired(trap.gameObject, trap.Definition);
            if (trap.Definition.WalkableFloorTrap && !CanPlaceTrap(trap.OriginCell, trap.Definition, out _)) continue;
            bool valid = true;
            for (int y = 0; y < footprint.y && valid; y++)
            for (int x = 0; x < footprint.x; x++)
            {
                Vector2Int cell = trap.OriginCell + new Vector2Int(x, y);
                if (!IsInside(cell) || occupiedCells.ContainsKey(cell)) { valid = false; break; }
            }
            if (!valid) continue;
            placedTraps.Add(trap);
            trap.OwnerGrid = this;
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

    public bool IsOpenForTrap(Vector2Int cell, TrapDefinition definition) => IsOpen(cell)
        && SupportsDefinition(definition)
        && (SurfaceType == TrapMountType.Wall || Surface != SurfaceKind.Platform || definition.AllowsPlatformPlacement);

    public bool TryGetFootprint(TrapDefinition definition, out Vector2Int footprint)
    {
        footprint = Vector2Int.one;
        if (definition == null) return false;
        Vector3 scale = transform.lossyScale;
        if (definition.WalkableFloorTrap && (Vector3.Dot(transform.up, Vector3.up) < 0.999f
            || Mathf.Abs(Mathf.Abs(scale.x) - Mathf.Abs(scale.z)) > 0.001f)) return false;
        return definition.TryGetFootprint(cellSize * Mathf.Abs(scale.x), out footprint);
    }

    public bool CanPlaceTrap(Vector2Int origin, TrapDefinition definition, out string failure)
    {
        failure = string.Empty;
        if (definition == null)
        {
            failure = "No trap definition selected.";
            return false;
        }
        // Category interception runs before any geometry work so wall traps can
        // never land on ground grids (and vice versa) through a stale mask.
        if (!SupportsDefinition(definition))
        {
            failure = SurfaceType.DescribeMismatch();
            return false;
        }
        if (IsWallSurface && supportCollider == null)
        {
            failure = "墙面网格未绑定支撑墙体。";
            return false;
        }
        if (definition != null && SurfaceType == TrapMountType.Ground
            && Surface == SurfaceKind.Platform && !definition.AllowsPlatformPlacement)
        {
            failure = "地刺等地面陷阱只能放置在地面或道路上，不能放在高台。";
            return false;
        }
        if (!TryGetFootprint(definition, out Vector2Int footprint))
        {
            failure = "Trap dimensions do not fit this grid.";
            return false;
        }
        for (int y = 0; y < footprint.y; y++)
        for (int x = 0; x < footprint.x; x++)
        {
            Vector2Int cell = origin + new Vector2Int(x, y);
            if (!IsOpenForTrap(cell, definition)) { failure = "Trap footprint is outside the available floor."; return false; }
            if (IsOccupied(cell)) { failure = "Trap footprint is occupied."; return false; }
        }
        if (definition.WalkableFloorTrap && !HasFloorSupport(origin, footprint, definition))
        {
            failure = "Ground spikes require a flat supporting floor.";
            return false;
        }
        if (OverlapsAnotherGrid(origin, footprint, definition))
        {
            failure = "Trap overlaps an occupied footprint on another grid.";
            return false;
        }
        return true;
    }

    public Quaternion GetTrapWorldRotation(TrapDefinition definition)
    {
        // Wall traps inherit the grid orientation so they face away from the
        // wall; LocalRotation then only corrects the authored model axes.
        // Ground traps keep the historical behavior exactly as before.
        if (IsWallSurface) return transform.rotation * definition.LocalRotation;
        return definition.WalkableFloorTrap
            ? transform.rotation * definition.LocalRotation : definition.LocalRotation;
    }

    private bool OverlapsAnotherGrid(Vector2Int origin, Vector2Int footprint, TrapDefinition definition)
    {
        Vector3 center = TrapWorldPosition(origin, footprint);
        SurfaceAxes(out Vector3 right, out Vector3 forward);
        Vector2 half = new Vector2(footprint.x * CellWorldSize.x, footprint.y * CellWorldSize.y) * 0.5f;
        foreach (TrapInstance other in FindObjectsByType<TrapInstance>(FindObjectsSortMode.None))
        {
            if (!other.isActiveAndEnabled || other.Definition == null) continue;
            TrapPlacementGrid owner = other.OwnerGrid != null ? other.OwnerGrid : other.GetComponentInParent<TrapPlacementGrid>();
            if (owner == null || owner == this || (!definition.WalkableFloorTrap && !other.Definition.WalkableFloorTrap)) continue;
            if (!owner.TryGetFootprint(other.Definition, out Vector2Int otherFootprint)) continue;
            Vector3 delta = other.transform.position - center;
            if (Mathf.Abs(delta.y) > 0.15f) continue;
            owner.SurfaceAxes(out Vector3 otherRight, out Vector3 otherForward);
            Vector2 otherHalf = new Vector2(otherFootprint.x * owner.CellWorldSize.x,
                otherFootprint.y * owner.CellWorldSize.y) * 0.5f;
            bool separated = false;
            foreach (Vector3 axis in new[] { right, forward, otherRight, otherForward })
            {
                float extent = Mathf.Abs(Vector3.Dot(right, axis)) * half.x + Mathf.Abs(Vector3.Dot(forward, axis)) * half.y
                    + Mathf.Abs(Vector3.Dot(otherRight, axis)) * otherHalf.x + Mathf.Abs(Vector3.Dot(otherForward, axis)) * otherHalf.y;
                if (Mathf.Abs(Vector3.Dot(delta, axis)) >= extent - 0.001f) { separated = true; break; }
            }
            if (!separated) return true;
        }
        return false;
    }

    private bool HasFloorSupport(Vector2Int origin, Vector2Int footprint, TrapDefinition definition)
    {
        Vector3 center = TrapWorldPosition(origin, footprint);
        // Sample the entire 2m plate at 0.5m intervals, including its edges.
        Vector2 physicalSize = definition.WorldFootprint != Vector2.zero ? definition.WorldFootprint
            : new Vector2(footprint.x * cellSize * Mathf.Abs(transform.lossyScale.x),
                footprint.y * cellSize * Mathf.Abs(transform.lossyScale.z));
        float halfX = physicalSize.x * 0.5f - 0.01f;
        float halfZ = physicalSize.y * 0.5f - 0.01f;
        Quaternion rotation = GetTrapWorldRotation(definition);
        for (int z = 0; z <= 4; z++)
        for (int x = 0; x <= 4; x++)
        {
            Vector3 point = center + rotation * new Vector3(
                Mathf.Lerp(-halfX, halfX, x / 4f), 0f, Mathf.Lerp(-halfZ, halfZ, z / 4f));
            bool supported = false;
            foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 0.1f, Vector3.down,
                0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<TrapInstance>() != null
                    || hit.collider.GetComponentInParent<EnemyHealth>() != null
                    || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (Vector3.Dot(hit.normal, Vector3.up) >= 0.98f
                    && Mathf.Abs(hit.point.y - point.y) <= 0.035f) supported = true;
            }
            if (!supported) return false;
        }
        return true;
    }

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
        // the same area for availability and occupancy checks. Wall cells keep
        // the same corner convention, but on the grid's local X/Y plane.
        Vector3 local = IsWallSurface
            ? new Vector3(cell.x * cellSize, cell.y * cellSize, SurfaceOffset)
            : new Vector3(cell.x * cellSize, placementHeight, cell.y * cellSize);
        return transform.TransformPoint(local);
    }

    /// <summary>World-space push that lifts a trap off the wall along its normal.</summary>
    public Vector3 SurfaceOffsetWorld => transform.TransformVector(new Vector3(0f, 0f, SurfaceOffset));

    /// <summary>World size of one cell along the grid's two in-plane axes.</summary>
    public Vector2 CellWorldSize => IsWallSurface
        ? new Vector2(cellSize * Mathf.Abs(transform.lossyScale.x), cellSize * Mathf.Abs(transform.lossyScale.y))
        : new Vector2(cellSize * Mathf.Abs(transform.lossyScale.x), cellSize * Mathf.Abs(transform.lossyScale.z));

    /// <summary>In-plane axes: right is local +X, "up" is local +Y (wall) or +Z (ground).</summary>
    private void SurfaceAxes(out Vector3 right, out Vector3 up)
    {
        right = transform.right;
        up = IsWallSurface ? transform.up : transform.forward;
    }

    /// <summary>Returns the same footprint anchor used by real trap instances.</summary>
    public Vector3 GetTrapWorldPosition(Vector2Int origin, Vector2Int footprint)
    {
        return TrapWorldPosition(origin, footprint);
    }

    /// <summary>
    /// Single source of truth for spawn, preview, occupancy rebuild and painting.
    /// Ground grids return their historical position/rotation unchanged; wall
    /// grids hang a trap at the center of its covered area, facing off the wall.
    /// </summary>
    public void GetPlacementPose(Vector2Int origin, TrapDefinition definition,
        out Vector3 position, out Quaternion rotation)
    {
        if (!TryGetFootprint(definition, out Vector2Int footprint)) footprint = Vector2Int.one;
        position = TrapWorldPosition(origin, footprint);
        rotation = definition != null ? GetTrapWorldRotation(definition) : transform.rotation;
    }

    /// <summary>
    /// Four world-space corners of one cell, ordered lower-left, lower-right,
    /// upper-right, upper-left in the grid's own placement plane.
    /// </summary>
    public void GetCellCorners(Vector2Int cell, Vector3[] corners)
    {
        SurfaceAxes(out Vector3 right, out Vector3 up);
        Vector3 gridOrigin = CellToWorld(cell);
        right *= cellSize;
        up *= cellSize;
        corners[0] = gridOrigin;
        corners[1] = gridOrigin + right;
        corners[2] = gridOrigin + right + up;
        corners[3] = gridOrigin + up;
    }

    /// <summary>
    /// Intersects a world ray with this grid's placement plane. Used by the
    /// runtime controller to decide whether the cursor is over the ground grid,
    /// the platform grid, or neither.
    /// </summary>
    public bool TryRaycastPlane(Ray ray, out Vector3 point, out float distance)
    {
        // Ground grids intersect on their horizontal plane; wall grids intersect
        // on the backplate plane pushed out by surfaceOffset, so the cursor lands
        // where the trap will actually hang.
        Plane plane = IsWallSurface
            ? new Plane(SurfaceNormal, transform.position + SurfaceOffsetWorld)
            : new Plane(transform.up, CellToWorld(Vector2Int.zero));
        if (plane.Raycast(ray, out distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = Vector3.zero;
        distance = 0f;
        return false;
    }

    public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float second = IsWallSurface ? local.y : local.z;
        cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(second / cellSize));
        return IsInside(cell);
    }

    public bool TryPlaceTrap(Vector2Int origin, TrapDefinition definition, out TrapInstance instance, out string failure)
    {
        instance = null;
        failure = string.Empty;
        if (!CanPlaceTrap(origin, definition, out failure)) return false;
        TryGetFootprint(definition, out Vector2Int footprint);
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

        GameObject root = null;
        try
        {
            CreateTrapObject(definition, origin, footprint, out root);
            // Imported art assets do not necessarily carry colliders. Keep every
            // placed trap selectable/targetable by providing a footprint collider
            // when the authored prefab has none (the launcher FBX is visual-only).
            if (root.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = definition.WalkableFloorTrap || RequiresWalkableTrap;
                if (IsWallSurface)
                {
                    // Wall traps get a backplate box (width x height x thickness).
                    // The ground turret's tall vertical box would poke straight
                    // through the wall it is mounted on.
                    Vector2 cell = CellWorldSize;
                    collider.center = new Vector3(0f, 0f, 0.05f);
                    collider.size = new Vector3(Mathf.Max(0.25f, footprint.x * cell.x * 0.8f),
                        Mathf.Max(0.25f, footprint.y * cell.y * 0.8f), 0.2f);
                }
                else
                {
                    collider.center = new Vector3(0f, 0.65f, 0f);
                    collider.size = new Vector3(Mathf.Max(0.5f, footprint.x * cellSize * 0.8f), 1.3f,
                        Mathf.Max(0.5f, footprint.y * cellSize * 0.8f));
                }
            }
            MakeTrapPassableIfRequired(root, definition);
            root.transform.SetParent(trapParent != null ? trapParent : transform, true);
            // Prefab/parent callbacks can place another trap while this object is created.
            if (!CanPlaceTrap(origin, definition, out failure))
                throw new System.InvalidOperationException(failure);
            instance = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
            instance.Initialize(definition, origin);
            // Keep the root authoritative for both visuals and colliders. This is
            // also applied after parenting because trapParent may have a transform.
            GetPlacementPose(origin, definition, out Vector3 finalPosition, out Quaternion finalRotation);
            root.transform.SetPositionAndRotation(finalPosition, finalRotation);
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
            instance.OwnerGrid = this;
            foreach (var cell in cells) occupiedCells[cell] = instance;
            return true;
        }
        catch
        {
            // The purchase layer owns money; this layer owns partial objects and cells.
            if (instance != null) ForgetTrap(instance);
            instance = null;
            if (root != null)
            {
                root.SetActive(false);
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            }
            throw;
        }
    }

    private void MakeTrapPassableIfRequired(GameObject root, TrapDefinition definition)
    {
        if (!definition.WalkableFloorTrap && !RequiresWalkableTrap) return;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
    }

    private void CreateTrapObject(TrapDefinition definition, Vector2Int origin, Vector2Int footprint, out GameObject root)
    {
        root = null;
        if (definition.Prefab != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // PrefabUtility preserves a proper prefab instance in the
                // scene, including added/generated child meshes.
                root = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, gameObject.scene);
                GetPlacementPose(origin, definition, out Vector3 posePosition, out Quaternion poseRotation);
                root.transform.SetPositionAndRotation(posePosition, poseRotation);
                return;
            }
#endif
            GetPlacementPose(origin, definition, out Vector3 runtimePosition, out Quaternion runtimeRotation);
            root = Instantiate(definition.Prefab, runtimePosition, runtimeRotation);
            return;
        }
        CreateFallbackTrap(definition, origin, footprint, out root);
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
        if (!TryGetFootprint(definition, out Vector2Int footprint))
        { failure = "Trap dimensions do not fit this grid."; return 0; }
        for (int i = 0; i < origins.Count; i++)
        {
            Vector2Int origin = origins[i];
            bool valid = CanPlaceTrap(origin, definition, out failure);
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
        if (!TryGetFootprint(definition, out Vector2Int footprint))
        { failure = "Trap dimensions do not fit this grid."; return 0; }
        var origins = new List<Vector2Int>(size.x * size.y);
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
            origins.Add(origin + new Vector2Int(x * footprint.x, y * footprint.y));
        return TryPlaceTraps(origins, definition, out instances, out failure, atomic);
    }

    public bool RemoveTrap(TrapInstance instance)
    {
        if (!ForgetTrap(instance)) return false;
        // Destroy is deferred in Play Mode; disable immediately, including on rollback.
        instance.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(instance.gameObject); else DestroyImmediate(instance.gameObject);
        return true;
    }

    internal bool ForgetTrap(TrapInstance instance)
    {
        if (instance == null || !placedTraps.Remove(instance)) return false;
        var toRemove = new List<Vector2Int>();
        foreach (var pair in occupiedCells) if (pair.Value == instance) toRemove.Add(pair.Key);
        foreach (var cell in toRemove) occupiedCells.Remove(cell);
        instance.OwnerGrid = null;
        return true;
    }

    private void CreateFallbackTrap(TrapDefinition definition, Vector2Int origin, Vector2Int footprint, out GameObject root)
    {
        root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GetPlacementPose(origin, definition, out Vector3 posePosition, out Quaternion poseRotation);
        root.transform.position = posePosition;
        root.transform.rotation = poseRotation;
        // The cube is a placeholder for the authored model, so match the layout
        // of whichever plane the grid uses: thin plate for ground, flat backplate
        // for a wall.
        Vector2 cell = CellWorldSize;
        Vector2 plate = new Vector2(footprint.x * cell.x * 0.8f, footprint.y * cell.y * 0.8f);
        root.transform.localScale = IsWallSurface
            ? new Vector3(plate.x, plate.y, 0.15f)
            : new Vector3(plate.x, 0.25f, plate.y);
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
    }

    private Vector3 TrapWorldPosition(Vector2Int origin, Vector2Int footprint)
    {
        if (IsWallSurface)
        {
            // Wall cells are addressed from the authored rectangle's lower-left
            // corner on the local X/Y plane. A multi-cell trap hangs at the center
            // of its covered area, pushed outward along +Z by surfaceOffset.
            Vector3 wallLocal = new Vector3((origin.x + footprint.x * 0.5f) * cellSize,
                (origin.y + footprint.y * 0.5f) * cellSize, SurfaceOffset);
            return transform.TransformPoint(wallLocal);
        }
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
            // Ground marks lie flat in X/Z; wall marks lie in the X/Y plane so the
            // Scene view shows where a trap would actually hang.
            if (IsWallSurface)
            {
                Gizmos.DrawCube(Vector3.zero, new Vector3(cellSize * 0.96f, cellSize * 0.96f, 0.02f));
                Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(cellSize, cellSize, 0.03f));
            }
            else
            {
                Gizmos.DrawCube(Vector3.zero, new Vector3(cellSize * 0.96f, 0.025f, cellSize * 0.96f));
                Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(cellSize, 0.03f, cellSize));
            }
            Gizmos.matrix = old;
        }
    }
}

