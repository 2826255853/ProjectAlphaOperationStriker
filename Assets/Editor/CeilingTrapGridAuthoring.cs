using System;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;

/// <summary>Shared full import, incremental sync and Unity authoring implementation.</summary>
public static class CeilingTrapGridAuthoring
{
    public static bool TryFit(BoxCollider support, float cellSize, out Vector3 origin,
        out Quaternion rotation, out int columns, out int rows, out string failure)
    {
        origin = Vector3.zero; rotation = Quaternion.identity; columns = rows = 0;
        failure = "天花板需要水平、静态、正缩放的盒体底面支撑。";
        if (support == null || !support.enabled || support.isTrigger || !support.gameObject.isStatic
            || !support.gameObject.activeInHierarchy || cellSize < .01f) return false;
        Transform t = support.transform;
        Vector3 scale = t.lossyScale;
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f
            || Vector3.Dot(t.up, Vector3.up) < .999f) return false;
        columns = Mathf.FloorToInt(support.size.x * scale.x / cellSize + .001f);
        rows = Mathf.FloorToInt(support.size.z * scale.z / cellSize + .001f);
        if (columns < 1 || rows < 1 || columns > 256 || rows > 256)
        { failure = "天花板底面必须能容纳 1–256 行/列完整格。"; return false; }
        rotation = Quaternion.LookRotation(-t.up, t.forward);
        Vector3 bottom = t.TransformPoint(support.center - Vector3.up * support.size.y * .5f);
        origin = bottom - t.right * (columns * cellSize * .5f) - t.forward * (rows * cellSize * .5f);
        failure = string.Empty;
        return true;
    }

    public static void Configure(TrapPlacementGrid grid, BoxCollider support,
        MapForgeSceneOrganization.CeilingTrapGridMap mapping)
    {
        if (!TryFit(support, mapping.cellSize, out Vector3 origin, out Quaternion rotation,
            out int columns, out int rows, out string failure)) throw new ArgumentException(failure);
        if (!mapping.autoFit)
        {
            origin = grid.transform.parent.TransformPoint(Vector(mapping.origin));
            rotation = grid.transform.parent.rotation * Quaternion.Euler(Vector(mapping.rotation) * Mathf.Rad2Deg);
            columns = mapping.columns; rows = mapping.rows;
        }
        grid.transform.localScale = Vector3.one;
        grid.transform.SetPositionAndRotation(origin, rotation);
        // Mounted local XY correspond to the support's XZ axes. Cancel parent
        // scale so cellSize, authored solid bounds and offsets stay in metres.
        Vector3 scale = grid.transform.lossyScale;
        grid.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        var open = new bool[columns * rows];
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            open[x + y * columns] = mapping.openCells == null || x >= mapping.columns || y >= mapping.rows
                || mapping.openCells[x + y * mapping.columns];
        grid.ConfigureLayout(columns, rows, mapping.cellSize, 0f, open,
            TrapPlacementGrid.SurfaceKind.Ground, TrapMountType.Ceiling, support, mapping.surfaceOffset);
        // A grid can be created beside its support and sampled in the same
        // frame (including Play Mode authoring/tests). Push transforms before
        // permanently closing cells whose probes miss the support.
        Physics.SyncTransforms();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            var cell = new Vector2Int(x, y);
            if (!grid.HasSurfaceSupport(cell, Vector2Int.one, null, out _)) grid.SetOpen(cell, false);
        }
    }

    private static Vector3 Vector(float[] v) => v != null && v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;

    [MenuItem("Tools/塔防/为选中盒体底面创建天花板网格")]
    public static void CreateForSelection()
    {
        var support = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInChildren<BoxCollider>() : null;
        if (!TryFit(support, 1f, out _, out _, out _, out _, out string failure))
        { Debug.LogWarning(failure); return; }
        Transform existing = support.transform.Find("CeilingTrapGrid_Authored");
        GameObject go = existing != null ? existing.gameObject : new GameObject("CeilingTrapGrid_Authored");
        if (existing == null) { Undo.RegisterCreatedObjectUndo(go, "Create ceiling grid"); go.transform.SetParent(support.transform, false); }
        Undo.RecordObject(go.transform, "Fit ceiling grid");
        var grid = go.GetComponent<TrapPlacementGrid>() ?? Undo.AddComponent<TrapPlacementGrid>(go);
        Undo.RecordObject(grid, "Configure ceiling grid");
        Configure(grid, support, new MapForgeSceneOrganization.CeilingTrapGridMap());
        EditorUtility.SetDirty(grid);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
    }

    /// <summary>Adds the MapForge test panel to the current main scene without reimporting the map.</summary>
    public static void AddTestPanelToMainScene()
    {
        const string scenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
        const string objectId = "ceiling_test_panel_20261025";
        const string materialPath = "Assets/Resources/CeilingTrapTest/CeilingPanel.mat";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var world = GameObject.Find("MapForgeWorld");
        if (world == null) throw new InvalidOperationException("当前场景缺少 MapForgeWorld 根对象。");

        GameObject panel = null;
        foreach (var properties in world.GetComponentsInChildren<MapForgeObjectProperties>(true))
            if (properties.objectId == objectId) { panel = properties.gameObject; break; }
        if (panel == null)
        {
            panel = new GameObject(objectId);
            panel.transform.SetParent(world.transform, false);
        }
        panel.transform.localPosition = new Vector3(0f, 4.1f, 0f);
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(4f, .2f, 4f);
        panel.isStatic = true;
        var propertiesComponent = panel.GetComponent<MapForgeObjectProperties>()
            ?? panel.AddComponent<MapForgeObjectProperties>();
        propertiesComponent.objectId = objectId;
        propertiesComponent.objectType = "cube";
        propertiesComponent.tags = new[] { "ceiling-trap-test" };
        propertiesComponent.parametersJson = "{}";
        propertiesComponent.visible = true;
        propertiesComponent.collision = true;
        propertiesComponent.colliderType = "auto";

        Transform geometry = panel.transform.Find("Geometry");
        GameObject geometryObject;
        if (geometry == null)
        {
            geometryObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            geometryObject.name = "Geometry";
            geometryObject.transform.SetParent(panel.transform, false);
        }
        else geometryObject = geometry.gameObject;
        geometryObject.transform.localPosition = Vector3.zero;
        geometryObject.transform.localRotation = Quaternion.identity;
        geometryObject.transform.localScale = Vector3.one;
        geometryObject.isStatic = true;
        var support = geometryObject.GetComponent<BoxCollider>() ?? geometryObject.AddComponent<BoxCollider>();
        support.isTrigger = false;

        if (!AssetDatabase.IsValidFolder(CeilingTrapTestPieceAuthoring.Directory))
            AssetDatabase.CreateFolder("Assets/Resources", "CeilingTrapTest");
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { name = "Ceiling Test Panel", color = new Color(.47f, .58f, .68f) };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .32f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var renderer = geometryObject.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = material;

        const string gridName = "CeilingTrapGrid_bottom";
        Transform gridTransform = panel.transform.Find(gridName);
        GameObject gridObject = gridTransform != null ? gridTransform.gameObject : new GameObject(gridName);
        if (gridTransform == null) gridObject.transform.SetParent(panel.transform, false);
        var grid = gridObject.GetComponent<TrapPlacementGrid>() ?? gridObject.AddComponent<TrapPlacementGrid>();
        var marker = gridObject.GetComponent<MapForgeCeilingTrapGrid>() ?? gridObject.AddComponent<MapForgeCeilingTrapGrid>();
        var mapping = new MapForgeSceneOrganization.CeilingTrapGridMap
        {
            id = "bottom", columns = 4, rows = 4, cellSize = 1f, surfaceOffset = .01f,
            autoFit = true, openCells = new[] { true, true, true, true, true, true, true, true,
                true, true, true, true, true, true, true, true }
        };
        marker.objectId = objectId;
        marker.gridId = mapping.id;
        marker.mappingJson = JsonUtility.ToJson(mapping);
        Configure(grid, support, mapping);
        EditorUtility.SetDirty(panel);
        EditorUtility.SetDirty(propertiesComponent);
        EditorUtility.SetDirty(grid);
        EditorUtility.SetDirty(marker);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new InvalidOperationException("保存主场景失败：" + scenePath);
        Debug.Log("CEILING_MAIN_MAP_PANEL_PASS: " + objectId + " / " + gridName);
    }
}
