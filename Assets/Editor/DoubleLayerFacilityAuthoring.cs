using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Geometry-only facility scene. Keep the original MapForge source for later gameplay authoring.</summary>
public static partial class DoubleLayerFacilityAuthoring
{
    public const string SourcePath = "Assets/Editor/MapSources/double-layer-facility.json";
    public const string ScenePath = "Assets/Scenes/DOUBLE_LAYER_FACILITY.unity";
    private static readonly Vector3 SpawnCameraPosition = new Vector3(0f, 1.7f, -32f);
    private const float UpperFloorHeight = 6.4f;
    private const float HalfStairHeight = UpperFloorHeight * .5f;
    private const float StairFlightOffset = 1.25f;
    private const float StairFlightWidth = 2.2f;
    private static readonly float[] StairCenters = { -14.5f, 14.5f };

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
            if (File.Exists(ScenePath + ".meta"))
                File.Copy(ScenePath + ".meta", Path.Combine(backup, "DOUBLE_LAYER_FACILITY.unity.meta"));
            foreach (string preview in Directory.Exists(OutputDirectory)
                         ? Directory.GetFiles(OutputDirectory, "*.png", SearchOption.TopDirectoryOnly) : Array.Empty<string>())
                File.Copy(preview, Path.Combine(backup, Path.GetFileName(preview)));
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = document.name;
        var root = new GameObject(MapForgeWorldImporter.RootName);
        MapForgeSceneOrganization.Create(document, root.transform, document.name);
        foreach (float x in StairCenters) AddStairRamps(root.transform, x);
        ConfigurePresentation();
        Validate(scene, document.objects.Length);
        ValidateStairMovement();
        ValidateCargoRoutes();

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

    public static void BuildAndPreview()
    {
        Build();
        RenderPreview();
    }

    // Each return stair has two 18-riser flights. Smooth collision follows the
    // tread noses, while visible steps, landings and handrails live in the map source.
    private static void AddStairRamps(Transform parent, float x)
    {
        AddStairRamp(parent, new Vector3(x - StairFlightOffset, 0f, 20.7f),
            new Vector3(x - StairFlightOffset, HalfStairHeight, 26.1f), $"Stair {x} Lower Walk Surface");
        AddStairRamp(parent, new Vector3(x + StairFlightOffset, HalfStairHeight, 26.7f),
            new Vector3(x + StairFlightOffset, UpperFloorHeight, 21.3f), $"Stair {x} Upper Walk Surface");
    }

    private static void AddStairRamp(Transform parent, Vector3 bottom, Vector3 top, string name)
    {
        Quaternion rotation = Quaternion.LookRotation(top - bottom, Vector3.up);
        var ramp = new GameObject(name);
        ramp.transform.SetParent(parent, false);
        ramp.transform.SetPositionAndRotation((bottom + top) * .5f - rotation * Vector3.up * .05f, rotation);
        ramp.isStatic = true;
        ramp.AddComponent<BoxCollider>().size = new Vector3(StairFlightWidth, .1f, Vector3.Distance(bottom, top));
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
        foreach (float x in new[] { -24f, 0f, 24f })
        foreach (float z in new[] { -24f, -4f, 16f, 36f, 50f })
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
        ValidateCargoRoutes();
        if (!EditorBuildSettings.scenes.Any(entry => entry.path == ScenePath && entry.enabled))
            throw new InvalidOperationException("Facility is missing from map selection.");
        Debug.Log("DOUBLE_LAYER_FACILITY_VALIDATION_PASS");
    }

    private static void ValidateStairMovement()
    {
        var prefab = Resources.Load<GameObject>("FPSPlayer");
        var source = prefab != null ? prefab.GetComponentInChildren<CharacterController>(true) : null;
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
            foreach (float x in StairCenters)
            {
                var route = new[]
                {
                    new Vector3(x - StairFlightOffset, 0f, 19f),
                    new Vector3(x - StairFlightOffset, HalfStairHeight, 27.8f),
                    new Vector3(x + StairFlightOffset, HalfStairHeight, 27.8f),
                    new Vector3(x + StairFlightOffset, UpperFloorHeight, 19f)
                };
                controller.enabled = false;
                probe.transform.position = route[0] + Vector3.up * .05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                for (int i = 1; i < route.Length; i++) WalkTo(controller, route[i], $"stair {x} ascent {i}");
                for (int i = route.Length - 2; i >= 0; i--) WalkTo(controller, route[i], $"stair {x} descent {i}");
            }
            Debug.Log("FACILITY_STAIRS_PASS: actual FPS capsule walked both return stairs, including landing turns, in both directions.");

            // The old three straight lines now meet the cargo ramp. Their large-body
            // clearance is checked separately; the narrow employee stairs stay player-only.
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }

    private static void WalkTo(CharacterController controller, Vector3 target, string label)
    {
        for (int i = 0; i < 2000; i++)
        {
            Vector3 delta = target - controller.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < .0025f) break;
            controller.Move(Vector3.ClampMagnitude(delta, .05f) + Vector3.down * .08f);
        }
        for (int i = 0; i < 12; i++) controller.Move(Vector3.down * .08f);
        Vector3 actual = controller.transform.position;
        if (Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(target.x, target.z)) > .12f ||
            Mathf.Abs(actual.y - target.y) > .3f)
            throw new InvalidOperationException($"Blocked {label}: reached {actual}, expected {target}.");
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
            RenderShot(camera, target, new Vector3(68f, 85f, -89f), new Vector3(0f, 3f, 8f), "01-overview.png");
            camera.fieldOfView = fieldOfView;
            RenderShot(camera, target, SpawnCameraPosition, new Vector3(0f, 1.7f, 20f), "02-ground-floor.png");
            RenderShot(camera, target, new Vector3(-27f, 8.1f, 40f), new Vector3(-27f, 8f, -20f), "03-upper-floor.png");
            RenderShot(camera, target, new Vector3(-10f, 8.8f, 17f), new Vector3(-14.5f, 3.5f, 25f), "04-stairs.png");
            RenderShot(camera, target, new Vector3(-24f, 2.2f, -26f), new Vector3(-18f, 2.5f, 3f), "05-workshop.png");
            RenderGroundCutaway(camera, target);
            RenderExpansionPreviews(camera, target);
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

    private static void RenderGroundCutaway(Camera camera, RenderTexture target)
    {
        // Preview only: reveal the lower floor without changing geometry, collision or the saved scene.
        var hidden = UnityEngine.Object.FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None)
            .Where(item => item.objectId == "2f" || item.objectId == "side-wall-r" ||
                item.objectId.StartsWith("front-wall-") || item.objectId.StartsWith("entrance-lintel-"))
            .SelectMany(item => item.GetComponentsInChildren<Renderer>()).Where(renderer => renderer.enabled)
            .Distinct().ToArray();
        float fov = camera.fieldOfView;
        try
        {
            foreach (var renderer in hidden) renderer.enabled = false;
            camera.fieldOfView = 45f;
            RenderShot(camera, target, new Vector3(68f, 85f, -89f), new Vector3(0f, 1f, 8f), "06-ground-cutaway.png");
        }
        finally
        {
            foreach (var renderer in hidden) renderer.enabled = true;
            camera.fieldOfView = fov;
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
        foreach (float x in StairCenters)
        {
            for (int i = 0; i <= 18; i++)
            {
                float t = i / 18f;
                CheckFloor(new Vector3(x - StairFlightOffset, 8f, Mathf.Lerp(20.7f, 26.1f, t)),
                    Mathf.Lerp(0f, HalfStairHeight, t));
                CheckFloor(new Vector3(x + StairFlightOffset, 8f, Mathf.Lerp(26.7f, 21.3f, t)),
                    Mathf.Lerp(HalfStairHeight, UpperFloorHeight, t));
            }
            CheckFloor(new Vector3(x, 8f, 27.8f), HalfStairHeight);
            CheckFloor(new Vector3(x + StairFlightOffset, 8f, 20.5f), UpperFloorHeight);
        }
        // Sample the open circulation areas, away from the central core and equipment.
        // Check render bounds as well: a decorative canopy can lack collision entirely.
        var skySamples = new[] { new Vector2(-27f, -12f), new Vector2(27f, -12f),
            new Vector2(-27f, 12f), new Vector2(27f, 12f), new Vector2(0f, -3f), new Vector2(0f, 16f) };
        var visibleGeometry = components.OfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        foreach (Vector2 sample in skySamples)
        {
            var ray = new Ray(new Vector3(sample.x, UpperFloorHeight + .15f, sample.y), Vector3.up);
            if (Physics.Raycast(ray, 30f, ~0, QueryTriggerInteraction.Ignore) ||
                visibleGeometry.Any(r => r.bounds.IntersectRay(ray, out float distance) && distance <= 30f))
                throw new InvalidOperationException($"Roof approach is covered at {sample}.");
        }
        Debug.Log($"Facility geometry verified: {metadata.Length} source objects, {components.OfType<Renderer>().Count()} renderers, " +
                  $"{components.OfType<Collider>().Count()} colliders; spawn clear; return stairs connect both floors; roof remains open; no gameplay grids or spawners.");
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
