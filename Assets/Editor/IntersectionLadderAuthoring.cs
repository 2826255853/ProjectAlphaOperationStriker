using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Places additional wall ladders around the central intersection.
public static class IntersectionLadderAuthoring
{
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    private const string GroupName = "IntersectionWallLadders_Symmetric";

    [MenuItem("Tools/Map/Place Intersection Ladders")]
    public static void Place()
    {
        string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "WallLadder", "intersection-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        File.Copy(ScenePath, Path.Combine(output, "scene-before.unity"));
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLadderAuthoring.PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Wall ladder prefab is missing.");

        var previous = GameObject.Find(GroupName);
        if (previous != null) Object.DestroyImmediate(previous);
        Physics.SyncTransforms();
        var group = new GameObject(GroupName);
        foreach (float x in new[] { -6f, 6f })
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 direction = Vector3.forward * side;
            Vector3 start = new Vector3(x, 0.9f, -16f);
            var hits = Physics.RaycastAll(start, direction, 6f, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(hit => hit.distance).ToArray();
            var wall = hits.FirstOrDefault(hit => hit.collider.name.StartsWith("Platform_Central_", StringComparison.Ordinal));
            if (wall.collider == null)
                throw new InvalidOperationException($"No central platform wall at {start}, direction {direction}; hits: {string.Join(", ", hits.Select(hit => hit.collider.name))}.");
            Vector3 outside = wall.point - direction * 0.15f;
            Vector3 inside = wall.point + direction * 0.6f;
            if (!Physics.Raycast(outside + Vector3.up * 4f, Vector3.down, out RaycastHit floor, 8f, ~0, QueryTriggerInteraction.Ignore) ||
                !Physics.Raycast(inside + Vector3.up * 4f, Vector3.down, out RaycastHit top, 8f, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Could not detect floor and platform top.");
            float height = top.point.y - floor.point.y;
            if (height < 0.9f || height > WallLadderAuthoring.ModelHeight || top.normal.y < 0.9f)
                throw new InvalidOperationException($"Invalid platform height: {height}.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            instance.name = $"IntersectionLadder_{(x < 0 ? "West" : "East")}_{(side < 0 ? "South" : "North")}";
            instance.transform.SetPositionAndRotation(new Vector3(wall.point.x, floor.point.y, wall.point.z) - direction * 0.005f,
                Quaternion.LookRotation(-direction));
            instance.transform.localScale = Vector3.one * (height / WallLadderAuthoring.ModelHeight);
            var ladder = instance.GetComponent<Ladder>();
            if (ladder == null || Mathf.Abs(ladder.Top - top.point.y) > 0.02f || Vector3.Dot(ladder.DismountDirection, direction) < 0.99f)
                throw new InvalidOperationException($"Invalid ladder placement: {instance.name}.");
            Debug.Log($"INTERSECTION_LADDER {instance.name}: {instance.transform.position}, floor={floor.point.y}, top={top.point.y}");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
        RenderPreview(output);
        Debug.Log("INTERSECTION_LADDERS_PASS: four ladders; backup and preview: " + output);
    }

    private static void RenderPreview(string output)
    {
        var cameraObject = new GameObject("Intersection Ladder Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1200, 900, 24);
        var image = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.transform.position = new Vector3(0f, 23f, -30f);
            camera.transform.LookAt(new Vector3(0f, 0f, -16f));
            camera.orthographic = true;
            camera.orthographicSize = 14f;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, "intersection-ladders.png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
