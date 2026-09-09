using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TrapPlacementGrid))]
public sealed class TrapPlacementGridEditor : Editor
{
    private SerializedProperty columns;
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
        rows = serializedObject.FindProperty("rows");
        cellSize = serializedObject.FindProperty("cellSize");
        placementHeight = serializedObject.FindProperty("placementHeight");
        trapParent = serializedObject.FindProperty("trapParent");
        openCells = serializedObject.FindProperty("openCells");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
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
        trapGrid.transform.SetPositionAndRotation(monsterGrid.transform.position, monsterGrid.transform.rotation);
        trapGrid.ConfigureLayout(monsterGrid.Columns, monsterGrid.Rows, monsterGrid.CellSize,
            monsterGrid.PathHeight + 0.02f, monsterGrid.CreateOpenCellSnapshot());
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
        bool geometry = trapGrid.Columns == monsterGrid.Columns && trapGrid.Rows == monsterGrid.Rows &&
            Mathf.Abs(trapGrid.CellSize - monsterGrid.CellSize) < 0.0001f &&
            Vector3.Distance(trapGrid.transform.position, monsterGrid.transform.position) < 0.0001f &&
            Quaternion.Angle(trapGrid.transform.rotation, monsterGrid.transform.rotation) < 0.001f;
        bool mask = trapGrid.HasSameOpenCells(monsterGrid.CreateOpenCellSnapshot());
        Debug.Log($"Trap grid overlap: geometry={geometry}, openCells={mask}.", trapGrid);
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
