using UnityEditor;
using UnityEngine;

/// <summary>Scene-view authoring tool for explicitly marked wall trap placement grids.</summary>
public sealed class WallTrapGridAuthoring : EditorWindow
{
    private Collider supportCollider;
    private int columns = 4;
    private int rows = 3;
    private float cellSize = 1f;
    private float surfaceOffset = 0.01f;
    private Vector2 regionOffset;
    private bool authoring;
    private string status = "Select a static wall collider, then click Create / Pick Face.";

    [MenuItem("Tools/Tower Defense/Create Wall Trap Grid")]
    private static void Open()
    {
        GetWindow<WallTrapGridAuthoring>("Wall Trap Grid");
    }

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(status, status.StartsWith("Invalid:") ? MessageType.Warning : MessageType.Info);
        supportCollider = (Collider)EditorGUILayout.ObjectField("Static Wall Collider", supportCollider, typeof(Collider), true);
        columns = EditorGUILayout.IntField("Columns", Mathf.Max(1, columns));
        rows = EditorGUILayout.IntField("Rows", Mathf.Max(1, rows));
        cellSize = EditorGUILayout.FloatField("Cell Size (m)", Mathf.Max(0.01f, cellSize));
        surfaceOffset = EditorGUILayout.FloatField("Surface Offset (m)", Mathf.Max(0f, surfaceOffset));
        regionOffset = EditorGUILayout.Vector2Field("Region Origin Offset (m)", regionOffset);
        if (supportCollider != null && !IsEligibleSupport(supportCollider, out string supportProblem))
            EditorGUILayout.HelpBox("Invalid: " + supportProblem, MessageType.Error);
        bool supportValid = supportCollider != null && IsEligibleSupport(supportCollider, out _);
        using (new EditorGUI.DisabledScope(!supportValid))
        {
            if (GUILayout.Button(authoring ? "Cancel Face Pick" : "Create / Pick Face"))
            {
                authoring = !authoring;
                status = authoring ? "Click the front face of the selected wall in Scene view." : "Face picking cancelled.";
                SceneView.RepaintAll();
            }
        }
        if (authoring && supportCollider != null)
            EditorGUILayout.HelpBox("Click a vertical face. The created grid starts fully closed; paint allowed cells with the Trap Placement Grid inspector.", MessageType.None);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        DrawExistingGridWarnings();
        if (!authoring || supportCollider == null) return;
        if (!IsEligibleSupport(supportCollider, out string eligibilityProblem))
        {
            status = "Invalid: " + eligibilityProblem;
            Repaint();
            return;
        }

        Event current = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        if (TryGetBoundWallHit(ray, out RaycastHit hit, out string problem))
        {
            if (IsVerticalWallNormal(hit.normal))
            {
                Vector3 origin = GetRegionOrigin(hit.point, hit.normal);
                DrawGridPreview(origin, hit.normal);
                Handles.color = Color.cyan;
                Handles.ArrowHandleCap(0, hit.point, Quaternion.LookRotation(hit.normal), 0.6f, EventType.Repaint);
                status = "Face valid. Click to create a closed " + columns + " × " + rows + " grid.";
                if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
                {
                    if (CreateGrid(origin, hit.normal))
                    {
                        authoring = false;
                        current.Use();
                    }
                }
            }
            else
            {
                status = "Invalid: clicked face is not vertical (normal must be horizontal).";
                Handles.color = Color.red;
                Handles.ArrowHandleCap(0, hit.point, Quaternion.LookRotation(hit.normal), 0.6f, EventType.Repaint);
            }
        }
        else status = string.IsNullOrEmpty(problem) ? "Move the pointer over the selected wall." : "Invalid: " + problem;

        if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Repaint();
    }

    private bool TryGetBoundWallHit(Ray ray, out RaycastHit selected, out string problem)
    {
        selected = default;
        problem = string.Empty;
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null) continue;
            if (!IsBoundCollider(candidate))
            {
                problem = "another collider occludes the selected wall.";
                return false;
            }
            selected = hits[i];
            return true;
        }
        return false;
    }

    private bool IsBoundCollider(Collider candidate)
    {
        Transform wall = supportCollider.transform;
        Transform other = candidate.transform;
        return candidate == supportCollider || other.IsChildOf(wall) || wall.IsChildOf(other);
    }

    private static bool IsEligibleSupport(Collider collider, out string problem)
    {
        problem = string.Empty;
        if (collider == null) { problem = "Select a wall collider."; return false; }
        if (!collider.gameObject.isStatic) { problem = "support wall must be marked Static."; return false; }
        Vector3 scale = collider.transform.lossyScale;
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
        { problem = "negative or zero wall scale is unsupported."; return false; }
        return true;
    }

    private static bool IsVerticalWallNormal(Vector3 normal) => Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) <= 0.0872f;

    private Vector3 GetRegionOrigin(Vector3 hitPoint, Vector3 normal)
    {
        Quaternion rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
        return hitPoint + rotation * new Vector3(regionOffset.x, regionOffset.y, 0f);
    }

    private void DrawGridPreview(Vector3 origin, Vector3 normal)
    {
        Quaternion rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
        Vector3 right = rotation * Vector3.right * cellSize;
        Vector3 up = rotation * Vector3.up * cellSize;
        Vector3 offset = normal.normalized * surfaceOffset;
        Handles.color = new Color(0.15f, 0.85f, 1f, 0.9f);
        for (int x = 0; x <= columns; x++) Handles.DrawLine(origin + offset + right * x, origin + offset + right * x + up * rows);
        for (int y = 0; y <= rows; y++) Handles.DrawLine(origin + offset + up * y, origin + offset + up * y + right * columns);
        Handles.DrawWireDisc(origin + offset, normal, 0.06f);
    }

    private bool CreateGrid(Vector3 origin, Vector3 normal)
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create wall trap grid");
        GameObject root = GameObject.Find("WallTrapPlacementGrids");
        if (root == null)
        {
            root = new GameObject("WallTrapPlacementGrids");
            Undo.RegisterCreatedObjectUndo(root, "Create wall grid authoring root");
        }
        if (root.transform.parent != null || root.transform.position.sqrMagnitude > 0.000001f
            || Quaternion.Angle(root.transform.rotation, Quaternion.identity) > 0.001f
            || (root.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
        {
            status = "Invalid: WallTrapPlacementGrids authoring root must remain at world origin with identity rotation and unit scale.";
            return false;
        }
        GameObject gridObject = new GameObject("WallTrapGrid_" + supportCollider.name);
        Undo.RegisterCreatedObjectUndo(gridObject, "Create wall trap grid");
        gridObject.transform.SetParent(root.transform, true);
        gridObject.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(normal.normalized, Vector3.up));
        gridObject.transform.localScale = Vector3.one;
        TrapPlacementGrid grid = Undo.AddComponent<TrapPlacementGrid>(gridObject);
        grid.ConfigureLayout(Mathf.Max(1, columns), Mathf.Max(1, rows), Mathf.Max(0.01f, cellSize), 0f,
            new bool[Mathf.Max(1, columns) * Mathf.Max(1, rows)], TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, supportCollider, Mathf.Max(0f, surfaceOffset));
        EditorUtility.SetDirty(grid);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gridObject.scene);
        Selection.activeGameObject = gridObject;
        Undo.CollapseUndoOperations(group);
        status = "Created closed wall grid bound to " + supportCollider.name + ". Undo is available.";
        SceneView.RepaintAll();
        return true;
    }

    private static void DrawExistingGridWarnings()
    {
        TrapPlacementGrid[] grids = Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None);
        for (int i = 0; i < grids.Length; i++)
        {
            TrapPlacementGrid grid = grids[i];
            if (grid == null || !grid.IsWallSurface) continue;
            bool valid = grid.SupportCollider != null && grid.SupportCollider.gameObject.isStatic
                && (grid.transform.lossyScale - Vector3.one).sqrMagnitude <= 0.000001f
                && IsStillAttachedToSupport(grid);
            if (valid) continue;
            Vector3 labelPosition = grid.transform.position;
            Handles.color = Color.red;
            Handles.Label(labelPosition, grid.SupportCollider == null
                ? "Wall grid INVALID: missing support collider"
                : "Wall grid INVALID: support moved, is not static, or grid scale is not unit");
        }
    }

    private static bool IsStillAttachedToSupport(TrapPlacementGrid grid)
    {
        Collider support = grid.SupportCollider;
        if (support == null) return false;
        Vector3 normal = grid.SurfaceNormal.normalized;
        float width = grid.Columns * grid.CellSize;
        float height = grid.Rows * grid.CellSize;
        Vector3[] points =
        {
            grid.CellToWorld(Vector2Int.zero),
            grid.transform.TransformPoint(new Vector3(width, 0f, grid.SurfaceOffset)),
            grid.transform.TransformPoint(new Vector3(0f, height, grid.SurfaceOffset)),
            grid.transform.TransformPoint(new Vector3(width, height, grid.SurfaceOffset)),
            grid.transform.TransformPoint(new Vector3(width * 0.5f, height * 0.5f, grid.SurfaceOffset)),
        };
        for (int i = 0; i < points.Length; i++)
        {
            Ray ray = new Ray(points[i] + normal * 0.02f, -normal);
            if (!support.Raycast(ray, out RaycastHit hit, grid.WallSupportProbeDistance + grid.SurfaceOffset + 0.05f)
                || Vector3.Dot(hit.normal, normal) < Mathf.Cos(grid.WallSupportNormalToleranceDegrees * Mathf.Deg2Rad))
                return false;
        }
        return true;
    }
}
