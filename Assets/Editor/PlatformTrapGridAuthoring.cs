using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Adds platform placement cells without rebuilding other map grids.</summary>
public static class PlatformTrapGridAuthoring
{
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    public static string OutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "TrapGridBackups", "PlatformGridOutput");

    [MenuItem("Tools/Tower Defense/Add Platform Trap Grid To Current Map")]
    public static void ApplyToCurrentMap()
    {
        Directory.CreateDirectory(OutputDirectory);
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        File.Copy(ScenePath, Path.Combine(OutputDirectory, "before-platform-grid-"
            + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity"));
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Physics.SyncTransforms();
        var monsterGrid = UnityEngine.Object.FindAnyObjectByType<MonsterPathGrid>();
        if (monsterGrid == null) throw new InvalidOperationException("Map has no monster grid.");
        var anchor = TrapGridAuthoring.GroundAnchor(monsterGrid);
        var rotation = monsterGrid.transform.rotation;
        var mask = TrapGridAuthoring.BuildPlatformMask(monsterGrid, anchor, rotation,
            TrapGridAuthoring.CollectPlatformColliders(), out float top, out int skipped);
        if (mask == null) throw new InvalidOperationException("Map has no usable platform cells.");
        if (skipped != 0) throw new InvalidOperationException("Platforms require separate height grids: " + skipped);

        var gridObject = GameObject.Find("TrapPlacementGrid_Platform");
        if (gridObject == null) gridObject = new GameObject("TrapPlacementGrid_Platform");
        var grid = gridObject.GetComponent<TrapPlacementGrid>();
        if (grid == null) grid = gridObject.AddComponent<TrapPlacementGrid>();
        Undo.RecordObject(grid, "Add platform trap placement cells");
        grid.transform.SetPositionAndRotation(anchor, rotation);
        grid.ConfigureLayout(monsterGrid.Columns, monsterGrid.Rows, monsterGrid.CellSize,
            top + TrapGridAuthoring.SurfaceOffset, mask, TrapPlacementGrid.SurfaceKind.Platform);
        var lines = grid.GetComponent<PlatformTrapGridLines>();
        if (lines == null) lines = Undo.AddComponent<PlatformTrapGridLines>(grid.gameObject);
        lines.Rebuild();
        Physics.SyncTransforms();
        int open = 0;
        for (int y = 0; y < grid.Rows; y++)
        for (int x = 0; x < grid.Columns; x++)
        {
            var cell = new Vector2Int(x, y);
            if (!grid.IsOpen(cell)) continue;
            open++;
            if (monsterGrid.IsOpen(cell)) throw new InvalidOperationException("Platform cell overlaps monster lane: " + cell);
            var center = grid.CellToWorld(cell);
            if (!Physics.Raycast(center + Vector3.up * .25f, Vector3.down, out var hit,
                .5f, ~0, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(hit.point.y + TrapGridAuthoring.SurfaceOffset - center.y) > .025f)
                throw new InvalidOperationException("Platform cell lacks level support: " + cell + " at " + center);
        }
        EditorUtility.SetDirty(grid);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save map.");
        var report = new StringBuilder();
        report.AppendLine("PLATFORM_GRID_PASS");
        report.AppendLine("Open cells: " + open + "; placement height: " + grid.PlacementHeight);
        report.AppendLine("Lane overlaps: 0; unsupported cells: 0");
        foreach (var support in TrapGridAuthoring.CollectPlatformColliders())
            report.AppendLine(support.name + ": " + support.bounds);
        File.WriteAllText(Path.Combine(OutputDirectory, "validation.txt"), report.ToString());
        typeof(TrapPlacementGridEditor).GetMethod("ValidateTrapGrids",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        Debug.Log(report.ToString());
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreview(grid);
    }

    private static void RenderPreview(TrapPlacementGrid grid)
    {
        var temporary = new List<GameObject>();
        var previous = RenderTexture.active;
        var target = new RenderTexture(1600, 1200, 24);
        var pixels = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
        try
        {
            var bounds = new Bounds();
            bool first = true;
            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsOpen(cell)) continue;
                var center = grid.CellToWorld(cell);
                if (first) { bounds = new Bounds(center, Vector3.zero); first = false; }
                bounds.Encapsulate(center);
            }
            var cameraHost = new GameObject("Platform grid preview camera"); temporary.Add(cameraHost);
            var camera = cameraHost.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.z * 1.15f, bounds.extents.x * .9f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .13f);
            camera.transform.position = bounds.center + new Vector3(0f, 100f, -15f);
            camera.transform.LookAt(bounds.center);
            camera.targetTexture = target;
            camera.Render(); camera.Render(); camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, "platform-grids.png"), pixels.EncodeToPNG());
            // Inspect a representative long platform at a playable viewing angle.
            camera.orthographicSize = 8f;
            var detailCenter = grid.CellToWorld(new Vector2Int(17, 65));
            camera.transform.position = detailCenter + new Vector3(9f, 14f, -12f);
            camera.transform.LookAt(detailCenter);
            camera.Render(); camera.Render(); camera.Render();
            pixels.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, "platform-grid-detail.png"), pixels.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            foreach (var go in temporary) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
