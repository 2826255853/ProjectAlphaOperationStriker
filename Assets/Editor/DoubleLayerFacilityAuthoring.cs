using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Geometry-only facility scene. Keep the original MapForge source for later gameplay authoring.</summary>
public static class DoubleLayerFacilityAuthoring
{
    public const string SourcePath = "Assets/Editor/MapSources/double-layer-facility.json";
    public const string ScenePath = "Assets/Scenes/DOUBLE_LAYER_FACILITY.unity";
    private static readonly Vector3 SpawnCameraPosition = new Vector3(0f, 1.7f, -32f);

    [MenuItem("MapForge/Build Double Layer Facility (Geometry Only)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before rebuilding the facility.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var document = MapForgeSceneOrganization.Parse(File.ReadAllText(SourcePath));
        // Never rewrite the source: its dormant spawn/path mappings are useful in a later gameplay pass.
        foreach (var node in document.objects)
        {
            var collider = node.unity != null ? node.unity.collider : null;
            node.unity = new MapForgeSceneOrganization.UnityMapping { collider = collider };
            if (node.id == "pathnodes" || node.id == "spawnpoints") node.visible = false;
        }
        document.unity = null;

        if (File.Exists(ScenePath))
        {
            string backup = Path.Combine(OutputDirectory, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, Path.Combine(backup, "DOUBLE_LAYER_FACILITY.unity"));
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = document.name;
        var root = new GameObject(MapForgeWorldImporter.RootName);
        MapForgeSceneOrganization.Create(document, root.transform, document.name);
        AddStairRamp(root.transform, -14.5f, "Left Stair Walk Surface");
        AddStairRamp(root.transform, 14.5f, "Right Stair Walk Surface");
        ConfigurePresentation();
        Validate(scene, document.objects.Length);

        Directory.CreateDirectory("Assets/Scenes");
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        var existing = scenes.FindIndex(entry => entry.path == ScenePath);
        if (existing < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        else scenes[existing].enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("DOUBLE_LAYER_FACILITY_PASS: saved geometry-only scene and enabled map selection: " + ScenePath);
    }

    // The 0.4 m risers exceed the player's normal step offset. The ramp follows the
    // leading tread edges; the last tread joins the upper floor at y=6.4 m.
    private static void AddStairRamp(Transform parent, float x, string name)
    {
        Vector3 bottom = new Vector3(x, 0f, 20.4375f);
        Vector3 top = new Vector3(x, 6.4f, 29.4375f);
        Quaternion rotation = Quaternion.LookRotation(top - bottom, Vector3.up);
        var ramp = new GameObject(name);
        ramp.transform.SetParent(parent, false);
        ramp.transform.SetPositionAndRotation((bottom + top) * .5f - rotation * Vector3.up * .05f, rotation);
        ramp.isStatic = true;
        ramp.AddComponent<BoxCollider>().size = new Vector3(4.9f, .1f, Vector3.Distance(bottom, top));
    }

    private static void ConfigurePresentation()
    {
        Camera camera = Camera.main;
        camera.transform.SetPositionAndRotation(SpawnCameraPosition, Quaternion.identity);
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 250f;
        camera.fieldOfView = 75f;
        // FirstPersonPlayerBootstrap uses this camera as the spawn marker, just like the main map.
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.5f, .55f, .62f);
        RenderSettings.ambientEquatorColor = new Color(.32f, .36f, .42f);
        RenderSettings.ambientGroundColor = new Color(.18f, .20f, .24f);
        var sun = UnityEngine.Object.FindAnyObjectByType<Light>();
        sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Soft;
        RenderSettings.sun = sun;

        var lights = new GameObject("Facility Interior Lighting");
        foreach (float x in new[] { -19f, 0f, 19f })
        foreach (float z in new[] { -24f, -4f, 16f, 33f })
        {
            var lamp = new GameObject($"Lower Floor Light {x} {z}");
            lamp.transform.SetParent(lights.transform, false);
            lamp.transform.position = new Vector3(x, 5.5f, z);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 2.5f;
            light.color = new Color(.8f, .88f, 1f);
            light.shadows = LightShadows.None;
        }
    }

    [MenuItem("MapForge/Validate Double Layer Facility")]
    public static void ValidateSavedScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Validate(scene, MapForgeSceneOrganization.Parse(File.ReadAllText(SourcePath)).objects.Length);
        ValidateStairMovement();
        if (!EditorBuildSettings.scenes.Any(entry => entry.path == ScenePath && entry.enabled))
            throw new InvalidOperationException("Facility is missing from map selection.");
        Debug.Log("DOUBLE_LAYER_FACILITY_VALIDATION_PASS");
    }

    private static void ValidateStairMovement()
    {
        var source = Resources.Load<GameObject>("FPSPlayer").GetComponentInChildren<CharacterController>(true);
        if (source == null) throw new InvalidOperationException("FPSPlayer has no CharacterController.");
        var probe = new GameObject("Temporary Stair Validation");
        try
        {
            var controller = probe.AddComponent<CharacterController>();
            controller.height = source.height;
            controller.radius = source.radius;
            controller.center = source.center;
            controller.stepOffset = source.stepOffset;
            controller.slopeLimit = source.slopeLimit;
            controller.skinWidth = source.skinWidth;
            controller.minMoveDistance = 0f;
            foreach (float x in new[] { -14.5f, 14.5f })
            {
                controller.enabled = false;
                probe.transform.position = new Vector3(x, .05f, 19f);
                controller.enabled = true;
                for (int i = 0; i < 300; i++) controller.Move(new Vector3(0f, -.08f, .05f));
                if (probe.transform.position.z < 31f || Mathf.Abs(probe.transform.position.y - 6.4f) > .3f)
                    throw new InvalidOperationException("Player cannot climb stair: " + probe.transform.position);
                for (int i = 0; i < 300; i++) controller.Move(new Vector3(0f, -.08f, -.05f));
                if (probe.transform.position.z > 20f || Mathf.Abs(probe.transform.position.y) > .3f)
                    throw new InvalidOperationException("Player cannot descend stair: " + probe.transform.position);
            }
            Debug.Log("FACILITY_STAIRS_PASS: actual FPS capsule settings walked up and down both staircases.");
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }

    [MenuItem("MapForge/Preview Double Layer Facility")]
    public static void RenderPreview()
    {
        ValidateSavedScene();
        Directory.CreateDirectory(OutputDirectory);
        Camera camera = Camera.main;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        float fieldOfView = camera.fieldOfView;
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(1600, 1000, 24);
        try
        {
            camera.targetTexture = target;
            camera.fieldOfView = 45f;
            RenderShot(camera, target, new Vector3(48f, 66f, -64f), new Vector3(0f, 3f, 0f), "01-overview.png");
            camera.fieldOfView = fieldOfView;
            RenderShot(camera, target, SpawnCameraPosition, new Vector3(0f, 1.7f, 20f), "02-ground-floor.png");
            RenderShot(camera, target, new Vector3(-10f, 8.1f, 32f), new Vector3(0f, 8f, 0f), "03-upper-floor.png");
            RenderShot(camera, target, new Vector3(-9f, 2.7f, 18f), new Vector3(-14.5f, 4f, 26f), "04-stairs.png");
            Debug.Log("FACILITY_PREVIEW_PASS: " + OutputDirectory);
        }
        finally
        {
            camera.targetTexture = null;
            camera.fieldOfView = fieldOfView;
            camera.transform.SetPositionAndRotation(position, rotation);
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void RenderShot(Camera camera, RenderTexture target, Vector3 position, Vector3 lookAt, string filename)
    {
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        // Warm the offscreen camera before reading its render target.
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, filename), texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static void Validate(Scene scene, int expectedObjects)
    {
        var components = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Component>(true)).ToArray();
        if (components.Any(c => c == null)) throw new InvalidOperationException("Missing script in facility scene.");
        if (components.Any(c => c is MonsterPathGrid || c is TrapPlacementGrid || c is TrapInstance ||
                                c is EnemySpawnPoint || c is WaveManager || c is EnemyCore || c is MapForgePathNode ||
                                c is MapForgeTrigger || c is Unity.AI.Navigation.NavMeshSurface))
            throw new InvalidOperationException("Geometry-only scene contains gameplay components.");
        var metadata = components.OfType<MapForgeObjectProperties>().ToArray();
        if (metadata.Length != expectedObjects) throw new InvalidOperationException("Map objects were lost during import.");
        foreach (var item in metadata.Where(item => item.objectId == "pathnodes" || item.objectId == "spawnpoints"))
            if (item.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled))
                throw new InvalidOperationException("Authoring markers are visible in the game.");
        if (Resources.Load<GameObject>("FPSPlayer") == null) throw new InvalidOperationException("FPSPlayer prefab is missing.");
        if (components.OfType<Camera>().Count(camera => camera.CompareTag("MainCamera")) != 1)
            throw new InvalidOperationException("Expected exactly one player spawn camera.");
        Physics.SyncTransforms();
        CheckFloor(SpawnCameraPosition + Vector3.up * 2f, 0f);
        if (Physics.CheckCapsule(new Vector3(0f, .4f, -32f), new Vector3(0f, 1.5f, -32f), .3f, ~0, QueryTriggerInteraction.Ignore))
            throw new InvalidOperationException("Player spawn is obstructed.");
        foreach (float x in new[] { -14.5f, 14.5f })
        {
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                CheckFloor(new Vector3(x, 8f, Mathf.Lerp(20.4375f, 29.4375f, t)), Mathf.Lerp(0f, 6.4f, t));
            }
            CheckFloor(new Vector3(x, 8f, 31f), 6.4f);
        }
        Debug.Log($"Facility geometry verified: {metadata.Length} source objects, {components.OfType<Renderer>().Count()} renderers, " +
                  $"{components.OfType<Collider>().Count()} colliders; spawn clear; both stair ramps connect to the upper floor; no gameplay grids or spawners.");
    }

    private static void CheckFloor(Vector3 origin, float expectedHeight)
    {
        if (!Physics.Raycast(origin, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore) ||
            Mathf.Abs(hit.point.y - expectedHeight) > .06f)
            throw new InvalidOperationException($"Unexpected walking surface at {origin}: {hit.point.y}, expected {expectedHeight}.");
    }

    private static string OutputDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "DoubleLayerFacility");
}
