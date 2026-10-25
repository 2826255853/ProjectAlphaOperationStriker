using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repairs the main map's authored wall grids and produces inspectable evidence.</summary>
public static class WallTrapGridRestoration
{
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    public static string OutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "WallTrapGridRestore");

    [MenuItem("Tools/Tower Defense/Restore Main Map Wall Trap Grids")]
    public static void Restore()
    {
        Directory.CreateDirectory(OutputDirectory);
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string backup = Path.Combine(OutputDirectory, "THREE_ROUTE_MERGE_MAP_before_restore_"
            + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity");
        File.Copy(ScenePath, backup);
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
        int wallsBefore = 0;
        var otherGrids = new List<TrapPlacementGrid>();
        foreach (var grid in Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None))
            if (grid.IsWallSurface) wallsBefore++; else otherGrids.Add(grid);
        WallTrapGridAuthoring.EnsureStarterGridForCurrentMap();
        string signature = Validate(out TrapPlacementGrid detail);
        // A second run must keep the same objects, transforms, support and mask.
        WallTrapGridAuthoring.EnsureStarterGridForCurrentMap();
        if (Validate(out _) != signature) throw new InvalidOperationException("Wall grid repair is not idempotent.");
        foreach (var grid in otherGrids)
            if (grid == null) throw new InvalidOperationException("Repair removed a ground or ceiling grid.");
        File.WriteAllText(Path.Combine(OutputDirectory, "validation.txt"),
            "WALL_GRID_RESTORE_PASS\nWall grids before: " + wallsBefore + "\n" + signature
            + "\nGround/ceiling grids retained: " + otherGrids.Count + "\nBackup: " + backup);
        // Reuse the project's full grid validator, including its existing terrain diagnostics.
        typeof(TrapPlacementGridEditor).GetMethod("ValidateTrapGrids",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        Debug.Log("WALL_GRID_RESTORE_PASS: before=" + wallsBefore + "; " + signature);
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            RenderPreview(scene, detail);
    }

    private static string Validate(out TrapPlacementGrid detail)
    {
        Physics.SyncTransforms();
        detail = null;
        int count = 0, open = 0, closed = 0;
        var signature = new List<string>();
        foreach (var grid in Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None))
        {
            if (!grid.IsWallSurface) continue;
            count++;
            if (grid.SupportCollider == null || !grid.SupportCollider.enabled
                || !grid.SupportCollider.gameObject.isStatic)
                throw new InvalidOperationException(grid.name + ": missing/disabled/nonstatic support.");
            if (detail == null || grid.Columns > detail.Columns) detail = grid;
            var mask = new StringBuilder();
            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                mask.Append(grid.IsOpen(cell) ? '1' : '0');
                if (!grid.IsOpen(cell)) { closed++; continue; }
                open++;
                if (!grid.HasWallSupport(cell, Vector2Int.one, null, out string failure))
                    throw new InvalidOperationException(grid.name + " " + cell + ": " + failure);
                Vector3 point = grid.CellToWorld(cell);
                if (point.y < 0f || Mathf.Abs(grid.SurfaceNormal.y) > .001f)
                    throw new InvalidOperationException(grid.name + ": invalid height/normal.");
            }
            signature.Add(grid.name + ": " + grid.GetEntityId() + ", " + grid.transform.position
                + ", " + grid.transform.rotation + ", support=" + grid.SupportCollider.GetEntityId() + ", " + mask);
        }
        if (count == 0 || open == 0) throw new InvalidOperationException("No usable wall grids restored.");
        signature.Sort(StringComparer.Ordinal);
        return "Wall grids: " + count + "; open cells: " + open + "; closed cells: " + closed
            + "; support errors: 0\n" + string.Join("\n", signature);
    }

    private static void RenderPreview(Scene scene, TrapPlacementGrid detail)
    {
        var temporary = new List<GameObject>();
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = new Color(.12f, 1f, .35f);
        try
        {
            foreach (var grid in Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None))
            {
                if (!grid.IsWallSurface) continue;
                var corners = new Vector3[4];
                for (int y = 0; y < grid.Rows; y++)
                for (int x = 0; x < grid.Columns; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!grid.IsOpen(cell)) continue;
                    grid.GetCellCorners(cell, corners);
                    var host = new GameObject("Wall grid preview overlay"); temporary.Add(host);
                    SceneManager.MoveGameObjectToScene(host, scene);
                    var line = host.AddComponent<LineRenderer>();
                    line.sharedMaterial = material; line.startWidth = line.endWidth = .025f;
                    line.positionCount = 4; line.loop = true;
                    for (int i = 0; i < 4; i++) line.SetPosition(i, corners[i] + grid.SurfaceNormal * .04f);
                }
            }
            var cameraHost = new GameObject("Wall grid preview camera"); temporary.Add(cameraHost);
            SceneManager.MoveGameObjectToScene(cameraHost, scene);
            var camera = cameraHost.AddComponent<Camera>(); camera.fieldOfView = 55f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f);
            Vector3 center = detail.transform.TransformPoint(new Vector3(detail.Columns * .5f, detail.Rows * .5f, .1f));
            float distance = Mathf.Max(10f, detail.Columns * .85f);
            camera.transform.position = center + detail.SurfaceNormal * distance + Vector3.up * distance * .3f;
            camera.transform.LookAt(center);
            Capture(camera, Path.Combine(OutputDirectory, "wall-grids-restored.png"));
        }
        finally
        {
            foreach (var go in temporary) Object.DestroyImmediate(go);
            Object.DestroyImmediate(material);
        }
    }

    private static void Capture(Camera camera, string path)
    {
        var previous = RenderTexture.active;
        var target = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render(); camera.Render(); camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
        }
    }
}
