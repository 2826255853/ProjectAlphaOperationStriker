using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TrapPlacementGrid))]
public sealed class TrapPlacementGridEditor : Editor
{
    // The trap grid anchors traps to grid-line intersections while the monster
    // grid describes cell centres, so the generated grid is shifted half a cell
    // toward positive X and Z. A full metre on Z is required to cover the
    // authored monster corridor, otherwise traps sit half a cell short of the
    // line monsters actually walk along.
    // Expressed in the monster grid's local space so a rotated grid stays aligned.
    private static readonly Vector3 MonsterGridToTrapGridOffset = new Vector3(0.5f, 0f, 0.5f);

    private SerializedProperty columns;
    private SerializedProperty surfaceKind;
    private SerializedProperty rows;
    private SerializedProperty cellSize;
    private SerializedProperty placementHeight;
    private SerializedProperty trapParent;
    private SerializedProperty openCells;
    private bool scenePaintMode;
    private int brushRadius = 0;
    private bool paintOpen = true;
    private bool painting;
    private bool strokeUndoRecorded;
    private TrapDefinition trapBrush;
    private bool trapPaintMode;
    private bool trapEraseMode;
    private Vector2Int lastTrapCell;
    private bool hasLastTrapCell;

    private void OnEnable()
    {
        columns = serializedObject.FindProperty("columns");
        surfaceKind = serializedObject.FindProperty("surfaceKind");
        rows = serializedObject.FindProperty("rows");
        cellSize = serializedObject.FindProperty("cellSize");
        placementHeight = serializedObject.FindProperty("placementHeight");
        trapParent = serializedObject.FindProperty("trapParent");
        openCells = serializedObject.FindProperty("openCells");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(surfaceKind, new GUIContent("放置表面"));
        EditorGUILayout.HelpBox("普通炮塔类：地面、道路和高台均可放置。地刺等地面陷阱：只能放在地面或道路。", MessageType.Info);
        EditorGUILayout.PropertyField(columns);
        EditorGUILayout.PropertyField(rows);
        EditorGUILayout.PropertyField(cellSize);
        EditorGUILayout.PropertyField(placementHeight);
        EditorGUILayout.PropertyField(trapParent);
        serializedObject.ApplyModifiedProperties();

        var grid = (TrapPlacementGrid)target;
        grid.EnsureCellData();
        serializedObject.Update();
        openCells = serializedObject.FindProperty("openCells");

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Open cells (green in Scene view)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open all")) SetAll(true);
        if (GUILayout.Button("Close all")) SetAll(false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        scenePaintMode = EditorGUILayout.ToggleLeft("Scene paint mode (drag to paint cells)", scenePaintMode);
        using (new EditorGUI.DisabledScope(!scenePaintMode))
        {
            brushRadius = EditorGUILayout.IntSlider(new GUIContent("Brush radius"), brushRadius, 0, 4);
            paintOpen = EditorGUILayout.ToggleLeft("Paint cells open", paintOpen);
            EditorGUILayout.HelpBox("Left-drag paints; right-drag erases. Shift-click floods a contiguous area.", MessageType.Info);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("快速铺设陷阱", EditorStyles.boldLabel);
        trapBrush = (TrapDefinition)EditorGUILayout.ObjectField("陷阱类型", trapBrush, typeof(TrapDefinition), false);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = trapPaintMode && !trapEraseMode ? new Color(0.45f, 1f, 0.45f) : Color.white;
        if (GUILayout.Button("绘制陷阱", GUILayout.Height(24f))) { trapPaintMode = true; trapEraseMode = false; }
        GUI.backgroundColor = trapPaintMode && trapEraseMode ? new Color(1f, 0.65f, 0.3f) : Color.white;
        if (GUILayout.Button("擦除陷阱", GUILayout.Height(24f))) { trapPaintMode = true; trapEraseMode = true; }
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("停止", GUILayout.Height(24f))) trapPaintMode = false;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("在 Scene 视图按住左键拖动连续铺设；Shift 可临时擦除。陷阱占用范围会自动检查。", MessageType.Info);

        for (int y = grid.Rows - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < grid.Columns; x++)
            {
                int index = x + y * grid.Columns;
                var cell = openCells.GetArrayElementAtIndex(index);
                GUI.backgroundColor = cell.boolValue
                    ? new Color(0.45f, 1f, 0.45f)
                    : new Color(1f, 0.5f, 0.5f);
                string label = cell.boolValue ? "O" : ".";
                if (GUILayout.Button(label, GUILayout.Width(24f), GUILayout.Height(22f)))
                    cell.boolValue = !cell.boolValue;
            }
            EditorGUILayout.EndHorizontal();
        }

        GUI.backgroundColor = Color.white;
        serializedObject.ApplyModifiedProperties();
        if (scenePaintMode || trapPaintMode) SceneView.RepaintAll();
    }

    [MenuItem("Tools/Tower Defense/Create Trap Grid From Monster Grid")]
    private static void CreateFromMonsterGrid()
    {
        MonsterPathGrid monsterGrid = Object.FindAnyObjectByType<MonsterPathGrid>();
        if (monsterGrid == null)
        {
            EditorUtility.DisplayDialog("创建陷阱网格", "当前场景没有 MonsterPathGrid。", "确定");
            return;
        }

        TrapPlacementGrid trapGrid = Object.FindAnyObjectByType<TrapPlacementGrid>();
        if (trapGrid == null)
        {
            GameObject go = new GameObject("TrapPlacementGrid");
            Undo.RegisterCreatedObjectUndo(go, "Create trap placement grid");
            trapGrid = go.AddComponent<TrapPlacementGrid>();
        }

        Undo.RecordObject(trapGrid, "Configure trap placement grid");
        Vector3 trapGridPosition = monsterGrid.transform.position +
            monsterGrid.transform.TransformVector(MonsterGridToTrapGridOffset);
        trapGrid.transform.SetPositionAndRotation(trapGridPosition, monsterGrid.transform.rotation);
        trapGrid.ConfigureLayout(monsterGrid.Columns, monsterGrid.Rows, monsterGrid.CellSize,
            TrapGridAuthoring.RoadPlacementHeight(monsterGrid), monsterGrid.CreateOpenCellSnapshot(),
            TrapPlacementGrid.SurfaceKind.Road);
        EditorUtility.SetDirty(trapGrid);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(trapGrid.gameObject.scene);
        Selection.activeGameObject = trapGrid.gameObject;
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Tower Defense/Validate Trap Grid Overlap")]
    private static void ValidateOverlap()
    {
        MonsterPathGrid monsterGrid = Object.FindAnyObjectByType<MonsterPathGrid>();
        TrapPlacementGrid trapGrid = Object.FindAnyObjectByType<TrapPlacementGrid>();
        if (monsterGrid == null || trapGrid == null)
        {
            Debug.LogWarning("Trap grid overlap: MonsterPathGrid 或 TrapPlacementGrid 不存在。", trapGrid);
            return;
        }
        Vector3 expectedTrapGridPosition = monsterGrid.transform.position +
            monsterGrid.transform.TransformVector(MonsterGridToTrapGridOffset);
        bool geometry = trapGrid.Columns == monsterGrid.Columns && trapGrid.Rows == monsterGrid.Rows &&
            Mathf.Abs(trapGrid.CellSize - monsterGrid.CellSize) < 0.0001f &&
            Vector3.Distance(trapGrid.transform.position, expectedTrapGridPosition) < 0.0001f &&
            Quaternion.Angle(trapGrid.transform.rotation, monsterGrid.transform.rotation) < 0.001f;
        bool mask = trapGrid.HasSameOpenCells(monsterGrid.CreateOpenCellSnapshot());
        Debug.Log($"Trap grid overlap: geometry={geometry}, openCells={mask}.", trapGrid);
    }

    // ---------------------------------------------------------------------
    // Ground + platform authoring
    // ---------------------------------------------------------------------

    private const float TrapBaseTolerance = 0.35f;
    private const float LatticeTolerance = 0.001f;

    /// <summary>
    /// Creates (or refreshes) one trap grid per walkable surface: the ground
    /// level that monsters walk on, and the raised platform tops beside it.
    /// Both grids share the walkable grid's lattice, and no platform cell is
    /// ever placed on the monster lane.
    /// </summary>
    [MenuItem("Tools/Tower Defense/Create Ground And Platform Trap Grids")]
    private static void CreateGroundAndPlatformGrids()
    {
        MonsterPathGrid monsterGrid = Object.FindAnyObjectByType<MonsterPathGrid>();
        if (monsterGrid == null)
        {
            EditorUtility.DisplayDialog("创建陷阱网格", "当前场景没有 MonsterPathGrid。", "确定");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create ground and platform trap grids");

        Vector3 anchorPosition = TrapGridAuthoring.GroundAnchor(monsterGrid);
        Quaternion anchorRotation = monsterGrid.transform.rotation;
        List<Collider> platformColliders = TrapGridAuthoring.CollectPlatformColliders();
        List<Collider> surfaceColliders = TrapGridAuthoring.CollectSurfaceColliders();

        // The walkway follows the physical road surface, open ground sits
        // on the terrain surface, and the platform level sits on the platform
        // tops. Three surfaces, three grids, every cell on exactly one level.
        TrapPlacementGrid roadGrid = FindOrCreateGrid("TrapPlacementGrid_Road",
            "TrapPlacementGrid_Ground", "TrapPlacementGrid");
        ConfigureGrid(roadGrid, monsterGrid, anchorPosition, anchorRotation,
            monsterGrid.CreateOpenCellSnapshot(), TrapGridAuthoring.RoadPlacementHeight(monsterGrid), TrapPlacementGrid.SurfaceKind.Road);

        int reservedCells;
        bool[] groundCells = TrapGridAuthoring.BuildGroundMask(monsterGrid, anchorPosition, anchorRotation,
            platformColliders, out reservedCells);
        if (groundCells == null) groundCells = monsterGrid.CreateOpenCellSnapshot();
        float groundTop = TrapGridAuthoring.DominantSurfaceTop(monsterGrid, anchorPosition, anchorRotation,
            surfaceColliders, groundCells, 0f);
        TrapPlacementGrid groundGrid = FindOrCreateGrid("TrapPlacementGrid_Ground");
        ConfigureGrid(groundGrid, monsterGrid, anchorPosition, anchorRotation,
            groundCells, groundTop + TrapGridAuthoring.SurfaceOffset, TrapPlacementGrid.SurfaceKind.Ground);

        bool[] platformCells = TrapGridAuthoring.BuildPlatformMask(monsterGrid, anchorPosition, anchorRotation,
            platformColliders, out float platformTop, out int skippedHeights);
        if (skippedHeights > 0)
            Debug.LogWarning($"高台陷阱网格：有 {skippedHeights} 格位于 {platformTop:0.##} 米以外的高台高度，已跳过（单一网格只能使用一个高度）。");
        TrapPlacementGrid platformGrid = null;
        if (platformCells != null)
        {
            platformGrid = FindOrCreateGrid("TrapPlacementGrid_Platform");
            ConfigureGrid(platformGrid, monsterGrid, anchorPosition, anchorRotation,
                platformCells, platformTop + TrapGridAuthoring.SurfaceOffset, TrapPlacementGrid.SurfaceKind.Platform);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = (platformGrid != null ? platformGrid : groundGrid).gameObject;
        SceneView.RepaintAll();

        string message = $"陷阱网格已生成：走道 {CountOpen(roadGrid)} 格（高度 {roadGrid.PlacementHeight:0.##} 米）" +
            $"，走道以外地面 {CountOpen(groundGrid)} 格（高度 {groundGrid.PlacementHeight:0.##} 米，" +
            $"另有 {reservedCells} 格由走道/高台网格负责）";
        message += platformGrid != null
            ? $"，高台 {CountOpen(platformGrid)} 格（高度 {platformGrid.PlacementHeight:0.##} 米）。"
            : "，场景中没有找到高台（Platform_*）。";
        Debug.Log(message, platformGrid != null ? platformGrid : groundGrid);
        ValidateTrapGrids();
    }

    private static TrapPlacementGrid FindOrCreateGrid(string preferredName, params string[] legacyNames)
    {
        TrapPlacementGrid[] existing = Object.FindObjectsByType<TrapPlacementGrid>();
        for (int i = 0; i < existing.Length; i++)
            if (existing[i] != null && existing[i].gameObject.name == preferredName) return existing[i];
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] == null) continue;
            for (int n = 0; n < legacyNames.Length; n++)
                if (existing[i].gameObject.name == legacyNames[n])
                {
                    Undo.RecordObject(existing[i].gameObject, "Rename trap placement grid");
                    existing[i].gameObject.name = preferredName;
                    return existing[i];
                }
        }
        GameObject created = new GameObject(preferredName);
        Undo.RegisterCreatedObjectUndo(created, "Create trap placement grid");
        return created.AddComponent<TrapPlacementGrid>();
    }

    private static TrapPlacementGrid ConfigureGrid(TrapPlacementGrid grid, MonsterPathGrid monsterGrid,
        Vector3 anchorPosition, Quaternion anchorRotation, bool[] openCells, float placementHeight,
        TrapPlacementGrid.SurfaceKind surface)
    {
        Undo.RecordObject(grid, "Configure trap placement grid");
        grid.transform.SetPositionAndRotation(anchorPosition, anchorRotation);
        grid.ConfigureLayout(monsterGrid.Columns, monsterGrid.Rows, monsterGrid.CellSize, placementHeight, openCells, surface);
        EditorUtility.SetDirty(grid);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        return grid;
    }

    private static int CountOpen(TrapPlacementGrid grid)
    {
        int count = 0;
        for (int y = 0; y < grid.Rows; y++)
        for (int x = 0; x < grid.Columns; x++)
            if (grid.IsOpen(new Vector2Int(x, y))) count++;
        return count;
    }

    /// <summary>
    /// Reports whether every authored trap grid really lines up with the monster
    /// grid and with the surface it claims to sit on. This is the "没有错位"
    /// check to run after editing a map.
    /// </summary>
    [MenuItem("Tools/Tower Defense/Validate Trap Grids")]
    private static void ValidateTrapGrids()
    {
        MonsterPathGrid monsterGrid = Object.FindAnyObjectByType<MonsterPathGrid>();
        TrapPlacementGrid[] gridList = Object.FindObjectsByType<TrapPlacementGrid>();
        if (gridList.Length == 0)
        {
            Debug.LogWarning("陷阱网格校验：场景中没有 TrapPlacementGrid。");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"陷阱网格校验：{gridList.Length} 套陷阱网格" +
            (monsterGrid != null ? $"，怪物网格 {monsterGrid.Columns}x{monsterGrid.Rows}。" : "，场景缺少 MonsterPathGrid。"));
        var holes = new List<string>();
        for (int i = 0; i < gridList.Length; i++)
        {
            TrapPlacementGrid grid = gridList[i];
            int open = 0, occupied = 0, laneOverlap = 0, tooHigh = 0, tooLow = 0, noSurface = 0;
            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                bool isOpen = grid.IsOpen(cell);
                if (grid.IsOccupied(cell)) occupied++;
                if (!isOpen) continue;
                open++;
                if (monsterGrid != null && monsterGrid.IsOpen(cell) && !IsGroundGrid(grid, monsterGrid))
                    laneOverlap++;
                Vector3 center = grid.CellToWorld(cell);
                if (!Physics.Raycast(center + grid.transform.up * 2f, -grid.transform.up, out RaycastHit hit, 8f))
                {
                    noSurface++;
                    if (holes.Count < 6) holes.Add($"{grid.name} 格{cell} 下方没有地面");
                    continue;
                }
                float delta = Vector3.Dot(hit.point - center, grid.transform.up);
                if (delta > 0.05f)
                {
                    tooHigh++;
                    if (holes.Count < 6) holes.Add($"{grid.name} 格{cell} 被地形埋住（高出 {delta:0.00} 米）");
                }
                else if (delta < -TrapBaseTolerance)
                {
                    tooLow++;
                    if (holes.Count < 6) holes.Add($"{grid.name} 格{cell} 悬空（低 {delta:0.00} 米）");
                }
            }

            bool aligned = true;
            if (monsterGrid != null)
            {
                if (grid.Columns != monsterGrid.Columns || grid.Rows != monsterGrid.Rows ||
                    Mathf.Abs(grid.CellSize - monsterGrid.CellSize) > LatticeTolerance)
                    aligned = false;
                else
                    for (int y = 0; y < grid.Rows && aligned; y++)
                    for (int x = 0; x < grid.Columns; x++)
                    {
                        Vector3 expected = monsterGrid.CellToWorld(new Vector2Int(x, y));
                        Vector3 actual = grid.CellToWorld(new Vector2Int(x, y));
                        if (Vector3.Distance(new Vector3(expected.x, 0f, expected.z), new Vector3(actual.x, 0f, actual.z)) > 0.02f)
                        {
                            aligned = false;
                            if (holes.Count < 6) holes.Add($"{grid.name} 格({x},{y}) 与怪物网格错位 " +
                                $"{Vector3.Distance(expected, actual):0.00} 米");
                            break;
                        }
                    }
            }

            report.AppendLine($"- {grid.name}：高度 {grid.PlacementHeight:0.##} 米，开格 {open}，已放置 {occupied}，" +
                $"与怪物网格对齐 {(aligned ? "是" : "否")}" +
                (monsterGrid != null && !IsGroundGrid(grid, monsterGrid) ? $"，占用怪物走道 {laneOverlap} 格" : string.Empty) +
                $"，埋入地形 {tooHigh}，悬空 {tooLow}，无地面 {noSurface}。");
        }
        for (int i = 0; i < holes.Count; i++) report.AppendLine("  · " + holes[i]);
        if (monsterGrid != null)
        {
            int totalCells = monsterGrid.Columns * monsterGrid.Rows;
            int covered = 0, doubled = 0;
            for (int y = 0; y < monsterGrid.Rows; y++)
            for (int x = 0; x < monsterGrid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                int levels = 0;
                for (int i = 0; i < gridList.Length; i++)
                    if (gridList[i].IsInside(cell) && gridList[i].IsOpen(cell)) levels++;
                if (levels > 0) covered++;
                if (levels > 1) doubled++;
            }
            report.AppendLine($"覆盖：{covered}/{totalCells} 格可放陷阱，重叠 {doubled} 格。");
        }
        Debug.Log(report.ToString(), gridList[0]);
    }

    private static bool IsGroundGrid(TrapPlacementGrid grid, MonsterPathGrid monsterGrid)
    {
        return grid.name == "TrapPlacementGrid_Road"
            || grid.name == "TrapPlacementGrid"
            || Mathf.Abs(grid.PlacementHeight - (monsterGrid.PathHeight + TrapGridAuthoring.SurfaceOffset)) <= 0.05f;
    }

    private void OnSceneGUI()
    {
        if (!scenePaintMode && !trapPaintMode) return;
        var grid = (TrapPlacementGrid)target;
        Event evt = Event.current;
        // Capture the mouse while painting so selection/manipulation tools do not steal the drag.
        if ((evt.type == EventType.Layout || evt.type == EventType.MouseDown || painting) && !evt.alt)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        Plane plane = new Plane(grid.transform.up, grid.transform.position + grid.transform.up * grid.CellSize * 0.01f);
        if (!plane.Raycast(ray, out float distance)) return;
        Vector3 world = ray.GetPoint(distance);
        if (!grid.TryWorldToCell(world, out Vector2Int center)) return;

        if (trapPaintMode)
        {
            HandleTrapPainting(grid, center, evt);
            return;
        }

        // Draw brush preview directly in the scene view.
        Handles.color = paintOpen ? new Color(0.2f, 1f, 0.35f, 0.3f) : new Color(1f, 0.2f, 0.2f, 0.3f);
        for (int y = -brushRadius; y <= brushRadius; y++)
        for (int x = -brushRadius; x <= brushRadius; x++)
        {
            Vector2Int c = center + new Vector2Int(x, y);
            if (!grid.IsInside(c)) continue;
            Vector3 p = grid.CellToWorld(c);
            Handles.DrawSolidRectangleWithOutline(new[] {
                p + grid.transform.TransformVector(new Vector3(-grid.CellSize * .48f, 0, -grid.CellSize * .48f)),
                p + grid.transform.TransformVector(new Vector3(-grid.CellSize * .48f, 0, grid.CellSize * .48f)),
                p + grid.transform.TransformVector(new Vector3(grid.CellSize * .48f, 0, grid.CellSize * .48f)),
                p + grid.transform.TransformVector(new Vector3(grid.CellSize * .48f, 0, -grid.CellSize * .48f))
            }, Handles.color, Color.clear);
        }

        if (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1) && !evt.alt)
        {
            painting = true;
            strokeUndoRecorded = false;
            bool value = evt.button == 0 ? paintOpen : !paintOpen;
            if (evt.shift) FloodFill(grid, center, value);
            else PaintBrush(grid, center, value);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && painting && (evt.button == 0 || evt.button == 1) && !evt.alt)
        {
            bool value = evt.button == 0 ? paintOpen : !paintOpen;
            PaintBrush(grid, center, value);
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && painting)
        {
            painting = false;
            strokeUndoRecorded = false;
            evt.Use();
        }
        SceneView.RepaintAll();
    }

    private void HandleTrapPainting(TrapPlacementGrid grid, Vector2Int cell, Event evt)
    {
        bool erase = trapEraseMode || evt.shift;
        Handles.color = erase ? new Color(1f, 0.25f, 0.1f, 0.8f) : new Color(0.2f, 1f, 0.35f, 0.8f);
        Handles.DrawWireCube(grid.CellToWorld(cell), new Vector3(grid.CellSize * 0.96f, 0.04f, grid.CellSize * 0.96f));
        if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
        {
            painting = true; hasLastTrapCell = false; StampTrap(grid, cell, erase); evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && painting && evt.button == 0 && !evt.alt)
        {
            StampTrapLine(grid, cell, erase); evt.Use();
        }
        else if (evt.type == EventType.MouseUp && painting)
        {
            painting = false; hasLastTrapCell = false; evt.Use();
        }
        SceneView.RepaintAll();
    }

    private void StampTrap(TrapPlacementGrid grid, Vector2Int cell, bool erase)
    {
        if (hasLastTrapCell && lastTrapCell == cell) return;
        hasLastTrapCell = true; lastTrapCell = cell;
        if (erase)
        {
            if (grid.TryGetTrapAtCell(cell, out TrapInstance existing))
            {
                Undo.RegisterFullObjectHierarchyUndo(grid.gameObject, "Erase trap");
                grid.RemoveTrap(existing);
                EditorUtility.SetDirty(grid);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            }
            return;
        }
        if (trapBrush == null) return;
        Undo.RegisterFullObjectHierarchyUndo(grid.gameObject, "Paint trap");
        if (grid.TryPlaceTrap(cell, trapBrush, out TrapInstance placed, out _))
        {
            if (placed != null) Undo.RegisterCreatedObjectUndo(placed.gameObject, "Paint trap");
            EditorUtility.SetDirty(grid);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }
    }

    // Interpolate between Scene-view mouse events so a fast drag never leaves
    // unpainted gaps between cells.
    private void StampTrapLine(TrapPlacementGrid grid, Vector2Int cell, bool erase)
    {
        if (!hasLastTrapCell) { StampTrap(grid, cell, erase); return; }
        int steps = Mathf.Max(Mathf.Abs(cell.x - lastTrapCell.x), Mathf.Abs(cell.y - lastTrapCell.y));
        for (int i = 1; i <= steps; i++)
        {
            Vector2Int sample = new Vector2Int(
                Mathf.RoundToInt(Mathf.Lerp(lastTrapCell.x, cell.x, i / (float)steps)),
                Mathf.RoundToInt(Mathf.Lerp(lastTrapCell.y, cell.y, i / (float)steps)));
            StampTrap(grid, sample, erase);
        }
    }

    private void PaintBrush(TrapPlacementGrid grid, Vector2Int center, bool value)
    {
        if (!strokeUndoRecorded)
        {
            Undo.RecordObject(grid, "Paint trap grid cells");
            strokeUndoRecorded = true;
        }
        for (int y = -brushRadius; y <= brushRadius; y++)
        for (int x = -brushRadius; x <= brushRadius; x++)
        {
            Vector2Int c = center + new Vector2Int(x, y);
            if (grid.IsInside(c)) grid.SetOpen(c, value);
        }
        EditorUtility.SetDirty(grid);
    }

    private void FloodFill(TrapPlacementGrid grid, Vector2Int start, bool value)
    {
        bool source = grid.IsOpen(start);
        if (source == value) return;
        Undo.RecordObject(grid, "Flood fill trap grid cells");
        var queue = new System.Collections.Generic.Queue<Vector2Int>();
        var visited = new System.Collections.Generic.HashSet<Vector2Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (!grid.IsInside(c) || !visited.Add(c) || grid.IsOpen(c) != source) continue;
            grid.SetOpen(c, value);
            queue.Enqueue(c + Vector2Int.right); queue.Enqueue(c + Vector2Int.left);
            queue.Enqueue(c + Vector2Int.up); queue.Enqueue(c + Vector2Int.down);
        }
        EditorUtility.SetDirty(grid);
    }

    private void SetAll(bool value)
    {
        Undo.RecordObject(target, value ? "Open all trap grid cells" : "Close all trap grid cells");
        serializedObject.Update();
        for (int i = 0; i < openCells.arraySize; i++)
            openCells.GetArrayElementAtIndex(i).boolValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        SceneView.RepaintAll();
    }
}
