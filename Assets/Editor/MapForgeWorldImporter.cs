using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Imports MapForge world JSON files as Unity scenes.
/// The automatic import runs after every domain reload, but only rewrites a
/// scene when mapforge-world.json changed or that scene is missing, so routine
/// script recompiles no longer replace the scene or leave numbered copies.
/// </summary>
[InitializeOnLoad]
public static partial class MapForgeWorldImporter
{
    static MapForgeWorldImporter()
    {
        // Import automatically once when the file is first detected by the editor.
        EditorApplication.delayCall += AutoImportIfNeeded;
    }

    private static void AutoImportIfNeeded()
    {
        EditorApplication.delayCall -= AutoImportIfNeeded;
        // delayCall can run after the editor has entered Play Mode (for
        // example when a domain reload completes while Play is starting).
        // Scene creation through EditorSceneManager is editor-only.
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var source = Path.Combine(Application.dataPath, "mapforge-world.json");
        if (!File.Exists(source)) return;

        // The imported scene is named after world.name, so the "already
        // imported" check has to resolve that name. The previous check looked
        // for a fixed UNTITLED_WORLD.unity file that the importer never wrote,
        // so the guard was always true and every domain reload imported again.
        World world;
        try { world = JsonUtility.FromJson<World>(File.ReadAllText(source)); }
        catch (Exception ex) { Debug.LogWarning("MapForge auto import skipped: " + ex.Message); return; }
        if (world == null) { Debug.LogWarning("MapForge auto import skipped: the world JSON is empty or invalid."); return; }
        var sceneName = ResolveSceneName(world, source);

        // Import only when the world JSON changed since the last import, or
        // when its scene is missing. Unchanged JSON leaves the existing scene
        // alone, so scene edits and play-mode tuning survive script recompiles.
        bool sceneMissing = !File.Exists(ScenePath(sceneName));
        if (!sceneMissing && EditorPrefs.GetString(StampKey(sceneName), string.Empty) == ContentStamp(source)) return;
        Import(source);
    }

    /// <summary>Absolute path of the scene asset a world is imported into.</summary>
    private static string ScenePath(string sceneName) =>
        Path.Combine(Application.dataPath, "Scenes", sceneName + ".unity");

    /// <summary>
    /// Machine-local record of the world file that produced a scene. EditorPrefs
    /// keeps it out of version control; the project path keeps projects apart.
    /// </summary>
    private static string StampKey(string sceneName) =>
        "MapForgeWorldImporter.LastImportStamp:" + Application.dataPath.Replace('\\', '/') + ":" + sceneName;

    private static string ContentStamp(string jsonPath)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(jsonPath))).Replace("-", string.Empty);
    }

    /// <summary>Scene name for a world, matching what Import writes to disk.</summary>
    private static string ResolveSceneName(World world, string jsonPath)
    {
        var sceneName = world != null && !string.IsNullOrWhiteSpace(world.name)
            ? world.name
            : Path.GetFileNameWithoutExtension(jsonPath);
        foreach (var c in Path.GetInvalidFileNameChars()) sceneName = sceneName.Replace(c, '_');
        return sceneName;
    }

    // Reuses the scene-organization DTOs so the importer and the live sync always read
    // the same "unity" shape: prefab library, wave configuration and bake options. The
    // object hierarchy itself is parsed by MapForgeSceneOrganization, which insists on the
    // current format version, so this envelope carries only what that parser does not.
    /// <summary>Envelope shared with the live sync, which reuses Import(jsonPath).</summary>
    [Serializable] public class World { public int version; public string name; public RoadsideStepData[] roadsideSteps; public GameplayData gameplay; public MapForgeSceneOrganization.UnitySceneData unity; }
    [Serializable] public class RoadsideStepData
    {
        public string platformId;
        public Vector3 position;
        public string axis = "z";
        public int nearSideSign = 1;
        public float length = 1f;
        public float width = 0.5f;
        public float height = 0.5f;
    }
    [Serializable] public class GameplayData
    {
        public PathGridData pathGrid;
        public CoreData core;
        public int totalWaves;
        public SpawnData[] spawns;
    }
    [Serializable] public class PathGridData
    {
        public Vector3 origin;
        public int columns = 1;
        public int rows = 1;
        public float cellSize = 1f;
        public float pathHeight;
        public bool[] openCells;
        public PathSegmentData[] segments;
    }
    [Serializable] public class PathSegmentData { public Vector3 start; public Vector3 end; }
    [Serializable] public class CoreData { public Vector3 position; public float arrivalRadius = 0.35f; }
    [Serializable] public class SpawnData
    {
        public string id;
        public Vector3 position;
        public Vector3 travelDirection = Vector3.forward;
        public float moveSpeed = 2f;
        // Kept on the gameplay spawn so an older world file still yields the
        // right scene-level wave count; migrated to the WaveManager during import.
        public int totalWaves = 3;
        public int enemiesPerWave = 10;
        public float initialWaveDelay = 2f;
        public float[] waveTransitionDelays = { 5f, 5f };
    }

    [MenuItem("MapForge/Import mapforge-world.json", priority = 0)]
    public static void ImportFromMenu()
    {
        var path = EditorUtility.OpenFilePanel("Select MapForge world", Application.dataPath, "json");
        if (!string.IsNullOrEmpty(path)) Import(path);
    }

    [MenuItem("MapForge/Import bundled world", priority = 1)]
    public static void ImportBundledWorld() => ImportDefault();

    // Allows Unity batch mode (and scripts) to invoke the importer.
    public static void ImportDefault()
    {
        var path = Path.Combine(Application.dataPath, "mapforge-world.json");
        if (File.Exists(path)) Import(path);
        else Debug.LogError("MapForge world not found: " + path);
    }

    public static void Import(string jsonPath)
    {
        // This importer creates and saves an editor scene. Unity rejects
        // EditorSceneManager.NewScene while Play Mode is active, so fail
        // cleanly instead of throwing from the delayed callback.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("MapForge import skipped because Unity is in or entering Play Mode. Stop Play Mode and run the import again.");
            return;
        }
        // Importing opens a generated scene, which throws away whatever is
        // currently open. Let an interactive user save first; batch mode has
        // nobody to answer the dialog.
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("MapForge import cancelled because the current scene was not saved.");
            return;
        }
        if (!File.Exists(jsonPath)) { Debug.LogError("MapForge file not found: " + jsonPath); return; }
        World world;
        try { world = JsonUtility.FromJson<World>(File.ReadAllText(jsonPath)); }
        catch (Exception ex) { Debug.LogError("Could not parse MapForge JSON: " + ex.Message); return; }
        if (world == null) { Debug.LogError("MapForge JSON is empty or invalid."); return; }

        // Only the current format is importable. An older export is refused here
        // instead of migrated on load, so everything below has one hierarchy path.
        if (world.version != 2)
        {
            Debug.LogError("MapForge world '" + jsonPath + "' has format version " + world.version +
                "; only version 2 is supported. Re-export it from MapForge.");
            return;
        }
        MapForgeSceneOrganization.Document organized;
        try { organized = MapForgeSceneOrganization.Parse(File.ReadAllText(jsonPath)); }
        catch (Exception ex) { Debug.LogError("Invalid MapForge hierarchy: " + ex.Message); return; }
        var sceneName = ResolveSceneName(world, jsonPath);
        var sceneDir = Path.Combine(Application.dataPath, "Scenes");
        Directory.CreateDirectory(sceneDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = sceneName;
        int globalTotalWaves = ResolveTotalWaves(world);
        var root = Build(world, organized, sceneName, globalTotalWaves);
        // Overwrite the canonical scene for this world instead of asking Unity
        // for a unique path. A unique path turns every import into a numbered
        // copy ("Map 1", "Map 2", ...); saving over the same path keeps one
        // scene asset and preserves its GUID, so existing references to it
        // (build settings, other scenes) stay valid.
        var outPath = "Assets/Scenes/" + sceneName + ".unity";
        bool replacingExisting = File.Exists(ScenePath(sceneName));
        EditorSceneManager.SaveScene(scene, outPath);
        AssetDatabase.Refresh();
        // Remember which world revision produced this scene, so the auto import
        // can tell an unchanged JSON from a new map export.
        EditorPrefs.SetString(StampKey(sceneName), ContentStamp(jsonPath));
        Debug.Log($"Imported MapForge world '{sceneName}' with {root.transform.childCount} objects into {outPath}" +
            (replacingExisting ? " (replaced the existing scene)" : " (created a new scene)"));
    }

    /// <summary>
    /// Rebuilds the map inside its existing scene asset instead of creating a new scene.
    /// The live sync uses this for the cases that cannot be patched object by object
    /// (renamed map, gameplay or roadside changes): the scene keeps its path and GUID,
    /// and anything the project added to the scene next to the map stays untouched.
    /// </summary>
    public static void RebuildInPlace(string jsonPath)
    {
        var world = ReadWorld(jsonPath);
        if (world == null) return;
        // Only the current format is importable. An older export is refused here
        // instead of migrated on load, so everything below has one hierarchy path.
        if (world.version != 2)
        {
            Debug.LogError("MapForge world '" + jsonPath + "' has format version " + world.version +
                "; only version 2 is supported. Re-export it from MapForge.");
            return;
        }
        MapForgeSceneOrganization.Document organized;
        try { organized = MapForgeSceneOrganization.Parse(File.ReadAllText(jsonPath)); }
        catch (Exception ex) { Debug.LogError("Invalid MapForge hierarchy: " + ex.Message); return; }
        var sceneName = ResolveSceneName(world, jsonPath);
        var scene = OpenTargetScene(sceneName);
        if (!scene.IsValid()) return;
        // Objects the project authored under the generated root are lifted out first, so
        // rebuilding the map does not delete them, and are put back afterwards when the new
        // hierarchy does not have an object of that name.
        var authored = DetachAuthoredChildren(FindRoot(scene)?.transform, world, organized, sceneName);
        ClearGeneratedRoot(scene);
        int globalTotalWaves = ResolveTotalWaves(world);
        var root = Build(world, organized, sceneName, globalTotalWaves);
        int restored = RestoreAuthoredChildren(authored, root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (restored > 0) Debug.Log($"MapForge: kept {restored} project-authored object(s) under {RootName}.");
        EditorPrefs.SetString(StampKey(sceneName), ContentStamp(jsonPath));
        Debug.Log($"Rebuilt MapForge world '{sceneName}' in place with {root.transform.childCount} objects.");
    }

    /// <summary>Parses a world file, reporting the same way the menu import does.</summary>
    public static World ReadWorld(string jsonPath)
    {
        if (!File.Exists(jsonPath)) { Debug.LogError("MapForge file not found: " + jsonPath); return null; }
        try
        {
            var world = JsonUtility.FromJson<World>(File.ReadAllText(jsonPath));
            if (world == null) Debug.LogError("MapForge JSON is empty or invalid.");
            return world;
        }
        catch (Exception ex) { Debug.LogError("Could not parse MapForge JSON: " + ex.Message); return null; }
    }

    /// <summary>
    /// Scene this map belongs to: the loaded one when it is already open, otherwise the
    /// file on disk. Returns an invalid scene when neither exists, so the caller can fall
    /// back to a fresh import.
    /// </summary>
    public static Scene OpenTargetScene(string sceneName)
    {
        var path = "Assets/Scenes/" + sceneName + ".unity";
        var loaded = SceneManager.GetSceneByPath(path);
        if (loaded.IsValid() && loaded.isLoaded) return loaded;
        if (!File.Exists(ScenePath(sceneName))) return default;
        try { return EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
        catch (Exception ex) { Debug.LogError("MapForge could not open " + path + ": " + ex.Message); return default; }
    }

    /// <summary>Removes everything the previous import put into a scene.</summary>
    public static void ClearGeneratedRoot(Scene scene)
    {
        foreach (var rootObject in scene.GetRootGameObjects())
        {
            if (!string.Equals(rootObject.name, RootName, StringComparison.Ordinal)) continue;
            UnityEngine.Object.DestroyImmediate(rootObject);
            return;
        }
    }

    /// <summary>Name of the generated root object; the live sync patches this object's children.</summary>
    public const string RootName = "MapForgeWorld";

    /// <summary>
    /// Rebuilds only the generated hierarchy for an already-parsed world and returns its
    /// root. The live sync calls this after destroying the old root, so a full rebuild
    /// keeps the scene asset, its GUID and everything the project authored beside the map.
    /// </summary>
    public static GameObject BuildRoot(World world, MapForgeSceneOrganization.Document organized, string sceneName) =>
        Build(world, organized, sceneName, ResolveTotalWaves(world));

    /// <summary>The generated root of an open scene, or null when the map was never imported.</summary>
    public static GameObject FindRoot(Scene scene)
    {
        if (!scene.IsValid()) return null;
        foreach (var rootObject in scene.GetRootGameObjects())
            if (string.Equals(rootObject.name, RootName, StringComparison.Ordinal)) return rootObject;
        return null;
    }

    /// <summary>
    /// Authored object IDs currently present under a generated root, so the live sync can
    /// report what a full rebuild created and deleted instead of guessing.
    /// </summary>
    public static Dictionary<string, MapForgeObjectProperties> IndexObjects(GameObject root)
    {
        var index = new Dictionary<string, MapForgeObjectProperties>(StringComparer.Ordinal);
        if (root == null) return index;
        foreach (var metadata in root.GetComponentsInChildren<MapForgeObjectProperties>(true))
            if (metadata != null && !string.IsNullOrEmpty(metadata.objectId)) index[metadata.objectId] = metadata;
        return index;
    }

    /// <summary>Builds the generated hierarchy for a parsed world and returns its root.</summary>
    private static GameObject Build(World world, MapForgeSceneOrganization.Document organized, string sceneName, int globalTotalWaves)
    {
        var root = new GameObject(RootName);
        var waveManagerObject = new GameObject("Wave Manager");
        waveManagerObject.transform.SetParent(root.transform, false);
        var waveManager = waveManagerObject.AddComponent<WaveManager>();
        waveManager.TotalWaves = Mathf.Max(1, globalTotalWaves);
        MapForgeSceneOrganization.Create(organized, root.transform, sceneName);
        ApplyRoadsideSteps(world.roadsideSteps, root.transform);
        CreateGameplayObjects(world.gameplay, root.transform, globalTotalWaves);
        // The Unity mapping creates its own spawn points and cores, so the shared
        // path grid and core references are filled once both passes have finished.
        MapForgeSceneOrganization.LinkGameplayReferences(root.transform);
        if (world.unity != null && world.unity.settings != null && world.unity.settings.buildNavMeshOnImport)
            BakeNavMesh(root.transform, world.unity, sceneName);
        LogUnitySummary(root.transform, globalTotalWaves, world.unity);
        RecordGeneratedChildren(root.transform, sceneName);
        return root;
    }

    /// <summary>
    /// Rebuilds the authoring trap grids from the scene's monster grid. The live sync
    /// calls this after geometry changed, because the masks are derived from the
    /// platform and ground colliders that the sync just rewrote.
    /// </summary>
    public static int RebuildTrapPlacementGrids(Transform parent)
    {
        if (parent == null) return 0;
        var pathGrid = parent.GetComponentInChildren<MonsterPathGrid>(true);
        if (pathGrid == null)
        {
            Debug.LogWarning("MapForge: no MonsterPathGrid found, so the trap grids were not rebuilt.");
            return 0;
        }
        foreach (var grid in parent.GetComponentsInChildren<TrapPlacementGrid>(true))
            UnityEngine.Object.DestroyImmediate(grid.gameObject);
        CreateTrapPlacementGrids(pathGrid, parent);
        return parent.GetComponentsInChildren<TrapPlacementGrid>(true).Length;
    }

    /// <summary>
    /// Total waves for the scene: the authored Unity wave configuration wins, then the
    /// gameplay block, then the largest per-object value on a gameplay spawn.
    /// </summary>
    private static int ResolveTotalWaves(World world)
    {
        if (world.unity != null && world.unity.waves != null && world.unity.waves.totalWaves > 0)
            return Mathf.Clamp(world.unity.waves.totalWaves, 1, 99);
        int total = world.gameplay != null ? world.gameplay.totalWaves : 0;
        if (total <= 0 && world.gameplay != null && world.gameplay.spawns != null)
        {
            for (int i = 0; i < world.gameplay.spawns.Length; i++)
                if (world.gameplay.spawns[i] != null)
                    total = Mathf.Max(total, world.gameplay.spawns[i].totalWaves);
        }
        return total;
    }

    /// <summary>
    /// Bakes the NavMesh the mapping asked for and stores it as an asset, which is what
    /// the NavMeshSurface inspector does. A failed bake only warns: the scene is still
    /// valid and the surface can be baked by hand.
    /// </summary>
    private static void BakeNavMesh(Transform parent, MapForgeSceneOrganization.UnitySceneData unity, string sceneName)
    {
        try
        {
            var surface = MapForgeSceneOrganization.CreateNavMeshSurface(parent, unity);
            surface.BuildNavMesh();
            var data = surface.navMeshData;
            if (data == null)
            {
                Debug.LogWarning("MapForge: the NavMesh bake produced no data for '" + sceneName + "'.");
                return;
            }
            const string folder = "Assets/NavMesh";
            Directory.CreateDirectory(folder);
            var assetPath = folder + "/" + sceneName + ".asset";
            if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("MapForge: baked the NavMesh for '" + sceneName + "' into " + assetPath + ".");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("MapForge: the NavMesh bake failed (" + ex.Message + "). Bake it from the NavMeshSurface inspector.");
        }
    }

    /// <summary>Counts the generated Unity components so the import log matches the editor summary.</summary>
    private static void LogUnitySummary(Transform parent, int totalWaves, MapForgeSceneOrganization.UnitySceneData unity)
    {
        int prefabs = 0, colliders = 0, navMesh = 0, spawnPoints = 0, cores = 0, pathNodes = 0, triggers = 0;
        foreach (var metadata in parent.GetComponentsInChildren<MapForgeObjectProperties>(true))
        {
            if (metadata == null) continue;
            if (!string.IsNullOrEmpty(metadata.prefabId)) prefabs++;
            if (metadata.colliderType != "auto" || metadata.colliderIsTrigger) colliders++;
            if (metadata.navRole != "none") navMesh++;
            if (metadata.spawnPointEnabled) spawnPoints++;
            if (metadata.enemyCoreEnabled) cores++;
            if (metadata.pathNodeEnabled) pathNodes++;
            if (metadata.triggerEnabled) triggers++;
        }
        int library = unity != null && unity.prefabs != null ? unity.prefabs.Length : 0;
        Debug.Log("MapForge Unity 组件映射：Prefab 库 " + library + " / Prefab 实例 " + prefabs + " / Collider " + colliders +
            " / NavMesh 标记 " + navMesh + " / SpawnPoint " + spawnPoints + " / EnemyCore " + cores +
            " / 路径节点 " + pathNodes + " / 触发器 " + triggers + " / 总波次 " + Mathf.Max(1, totalWaves));
    }

    private static void ApplyRoadsideSteps(RoadsideStepData[] steps, Transform parent)
    {
        if (steps == null) return;
        foreach (var step in steps)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.platformId)) continue;
            var platform = parent.Find(step.platformId);
            Transform authoredParent = null;
            foreach (var metadata in parent.GetComponentsInChildren<MapForgeObjectProperties>(true))
            {
                if (metadata.objectId != step.platformId) continue;
                authoredParent = metadata.transform;
                platform = authoredParent.Find(step.platformId);
                break;
            }
            if (platform == null) { Debug.LogWarning("Roadside step platform not found: " + step.platformId); continue; }
            // Roadside step coordinates are world-aligned. Stage only the
            // geometry under the scene root, leaving the authored hierarchy intact.
            if (authoredParent != null && Quaternion.Angle(platform.rotation, Quaternion.identity) > .01f)
            {
                Debug.LogWarning("Roadside steps require an unrotated platform: " + step.platformId);
                continue;
            }
            if (authoredParent != null) platform.SetParent(parent, true);
            try
            {
            bool alongZ = !string.Equals(step.axis, "x", StringComparison.OrdinalIgnoreCase);
            float travelSize = alongZ ? platform.localScale.z : platform.localScale.x;
            float sideSize = alongZ ? platform.localScale.x : platform.localScale.z;
            float stepWidth = Mathf.Clamp(step.width, 0.01f, sideSize - 0.01f);
            float stepLength = Mathf.Clamp(step.length, 0.01f, travelSize - 0.01f);
            float outerSideSize = sideSize - stepWidth;
            float travelMin = (alongZ ? platform.localPosition.z : platform.localPosition.x) - travelSize * 0.5f;
            float travelMax = travelMin + travelSize;
            float stepMin = alongZ ? step.position.z - stepLength * 0.5f : step.position.x - stepLength * 0.5f;
            float stepMax = stepMin + stepLength;
            if (stepMin <= travelMin || stepMax >= travelMax) continue;

            var sourceRenderer = platform.GetComponent<MeshRenderer>();
            var sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : null;
            int sign = step.nearSideSign >= 0 ? 1 : -1;
            var platformPos = platform.localPosition;
            var outerPos = platformPos;
            if (alongZ) outerPos.x -= sign * stepWidth * 0.5f;
            else outerPos.z -= sign * stepWidth * 0.5f;
            platform.localPosition = outerPos;
            if (alongZ) platform.localScale = new Vector3(outerSideSize, platform.localScale.y, travelSize);
            else platform.localScale = new Vector3(travelSize, platform.localScale.y, outerSideSize);

            var nearPos = platformPos;
            if (alongZ) nearPos.x += sign * (sideSize - stepWidth) * 0.5f;
            else nearPos.z += sign * (sideSize - stepWidth) * 0.5f;
            float beforeLength = stepMin - travelMin;
            float afterLength = travelMax - stepMax;
            if (beforeLength > 0.01f) CreateStepCube(platform, platform.name + "_HighBefore", nearPos, alongZ, stepWidth, beforeLength, stepMin - beforeLength * 0.5f, 1f, sourceMaterials, authoredParent);
            if (afterLength > 0.01f) CreateStepCube(platform, platform.name + "_HighAfter", nearPos, alongZ, stepWidth, afterLength, stepMax + afterLength * 0.5f, 1f, sourceMaterials, authoredParent);
            CreateStepCube(platform, platform.name + "_Step_0.5m", nearPos, alongZ, stepWidth, stepLength, alongZ ? step.position.z : step.position.x, Mathf.Max(0.01f, step.height), sourceMaterials, authoredParent);
            }
            finally { if (authoredParent != null) platform.SetParent(authoredParent, true); }
        }
    }

    private static void CreateStepCube(Transform source, string name, Vector3 localPosition, bool alongZ, float sideWidth, float travelLength, float travelCenter, float height, Material[] materials, Transform authoredParent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(source.parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localPosition = alongZ
            ? new Vector3(localPosition.x, height * 0.5f, travelCenter)
            : new Vector3(travelCenter, height * 0.5f, localPosition.z);
        go.transform.localScale = alongZ
            ? new Vector3(sideWidth, height, travelLength)
            : new Vector3(travelLength, height, sideWidth);
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && materials != null) renderer.sharedMaterials = materials;
        if (authoredParent != null)
        {
            go.transform.SetParent(authoredParent, true);
            go.layer = source.gameObject.layer; go.isStatic = source.gameObject.isStatic;
            var sourceCollider = source.GetComponent<Collider>();
            if (sourceCollider != null) go.GetComponent<Collider>().enabled = sourceCollider.enabled;
            var sourceRenderer = source.GetComponent<Renderer>();
            if (sourceRenderer != null) renderer.enabled = sourceRenderer.enabled;
        }
    }

    private static void CreateGameplayObjects(GameplayData data, Transform parent, int globalTotalWaves)
    {
        if (data == null) return;

        MonsterPathGrid pathGrid = null;
        if (data.pathGrid != null)
        {
            var gridObject = new GameObject("MonsterPathGrid");
            gridObject.transform.SetParent(parent, false);
            gridObject.transform.position = data.pathGrid.origin;
            pathGrid = gridObject.AddComponent<MonsterPathGrid>();
            var gridSerialized = new SerializedObject(pathGrid);
            gridSerialized.FindProperty("columns").intValue = Mathf.Max(1, data.pathGrid.columns);
            gridSerialized.FindProperty("rows").intValue = Mathf.Max(1, data.pathGrid.rows);
            gridSerialized.FindProperty("cellSize").floatValue = Mathf.Max(1f, data.pathGrid.cellSize);
            gridSerialized.FindProperty("pathHeight").floatValue = data.pathGrid.pathHeight;
            gridSerialized.ApplyModifiedPropertiesWithoutUndo();
            pathGrid.EnsureCellData();
            if (data.pathGrid.openCells != null)
            {
                var serializedGrid = new SerializedObject(pathGrid);
                var openCells = serializedGrid.FindProperty("openCells");
                int count = Mathf.Min(openCells.arraySize, data.pathGrid.openCells.Length);
                for (int i = 0; i < count; i++) openCells.GetArrayElementAtIndex(i).boolValue = data.pathGrid.openCells[i];
                serializedGrid.ApplyModifiedPropertiesWithoutUndo();
            }
            else if (data.pathGrid.segments != null)
            {
                foreach (var segment in data.pathGrid.segments) OpenSegment(pathGrid, segment);
            }
        }

        EnemyCore core = null;
        if (data.core != null)
        {
            var coreObject = new GameObject("Enemy Core");
            coreObject.transform.SetParent(parent, false);
            coreObject.transform.position = data.core.position;
            core = coreObject.AddComponent<EnemyCore>();
            var coreSerialized = new SerializedObject(core);
            coreSerialized.FindProperty("arrivalRadius").floatValue = Mathf.Max(0.01f, data.core.arrivalRadius);
            if (pathGrid != null) coreSerialized.FindProperty("pathGrid").objectReferenceValue = pathGrid;
            coreSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Trap placement travels with the map: every imported scene gets one
        // grid for the ground level and one for the raised platform level.
        CreateTrapPlacementGrids(pathGrid, parent);

        if (data.spawns == null) return;
        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TestMonster.prefab");
        for (int i = 0; i < data.spawns.Length; i++)
        {
            var spawn = data.spawns[i];
            if (spawn == null) continue;
            var spawnObject = new GameObject(string.IsNullOrWhiteSpace(spawn.id) ? $"Enemy Spawn {i + 1}" : spawn.id);
            spawnObject.transform.SetParent(parent, false);
            spawnObject.transform.position = spawn.position;
            var component = spawnObject.AddComponent<EnemySpawnPoint>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("spawningEnabled").boolValue = true;
            serialized.FindProperty("spawnInterval").floatValue = 1f;
            serialized.FindProperty("enemiesPerWave").intValue = Mathf.Max(1, spawn.enemiesPerWave);
            serialized.FindProperty("initialWaveDelay").floatValue = Mathf.Max(0f, spawn.initialWaveDelay);
            var delays = serialized.FindProperty("waveTransitionDelays");
            delays.arraySize = Mathf.Max(0, globalTotalWaves - 1);
            for (int d = 0; d < delays.arraySize; d++)
            {
                float delay = spawn.waveTransitionDelays != null && d < spawn.waveTransitionDelays.Length ? spawn.waveTransitionDelays[d] : 5f;
                delays.GetArrayElementAtIndex(d).floatValue = Mathf.Max(0f, delay);
            }
            serialized.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
            serialized.FindProperty("pathGrid").objectReferenceValue = pathGrid;
            serialized.FindProperty("core").objectReferenceValue = core;
            serialized.FindProperty("travelDirection").vector3Value = spawn.travelDirection.sqrMagnitude > 0.001f ? spawn.travelDirection : Vector3.forward;
            serialized.FindProperty("moveSpeed").floatValue = Mathf.Max(0f, spawn.moveSpeed);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Creates the trap placement grids for a freshly imported map. Both grids
    /// share the monster grid's lattice, so a trap always lands on the exact
    /// centre of the cell it covers, and the platform grid never opens a cell
    /// that belongs to the monster lane.
    /// </summary>
    private static void CreateTrapPlacementGrids(MonsterPathGrid pathGrid, Transform parent)
    {
        if (pathGrid == null) return;
        Vector3 anchor = TrapGridAuthoring.GroundAnchor(pathGrid);
        Quaternion rotation = pathGrid.transform.rotation;

        var platforms = TrapGridAuthoring.CollectPlatformColliders();
        var surfaces = TrapGridAuthoring.CollectSurfaceColliders();

        CreateTrapPlacementGrid("TrapPlacementGrid_Road", parent, pathGrid, anchor, rotation,
            pathGrid.CreateOpenCellSnapshot(), TrapGridAuthoring.RoadPlacementHeight(pathGrid), TrapPlacementGrid.SurfaceKind.Road);

        bool[] groundCells = TrapGridAuthoring.BuildGroundMask(pathGrid, anchor, rotation, platforms, out _);
        float groundTop = TrapGridAuthoring.DominantSurfaceTop(pathGrid, anchor, rotation, surfaces, groundCells, 0f);
        CreateTrapPlacementGrid("TrapPlacementGrid_Ground", parent, pathGrid, anchor, rotation,
            groundCells, groundTop + TrapGridAuthoring.SurfaceOffset, TrapPlacementGrid.SurfaceKind.Ground);

        if (platforms.Count == 0) return;
        bool[] platformCells = TrapGridAuthoring.BuildPlatformMask(pathGrid, anchor, rotation, platforms,
            out float platformTop, out int skippedCells);
        if (skippedCells > 0)
            Debug.LogWarning($"Trap grid: skipped {skippedCells} platform cells that are not at {platformTop:0.##}m, " +
                "because a single grid can only describe one height.");
        if (platformCells == null) return;
        CreateTrapPlacementGrid("TrapPlacementGrid_Platform", parent, pathGrid, anchor, rotation,
            platformCells, platformTop + TrapGridAuthoring.SurfaceOffset, TrapPlacementGrid.SurfaceKind.Platform);
    }

    private static void CreateTrapPlacementGrid(string name, Transform parent, MonsterPathGrid pathGrid,
        Vector3 anchor, Quaternion rotation, bool[] openCells, float placementHeight, TrapPlacementGrid.SurfaceKind surface)
    {
        var gridObject = new GameObject(name);
        gridObject.transform.SetParent(parent, true);
        gridObject.transform.SetPositionAndRotation(anchor, rotation);
        TrapPlacementGrid grid = gridObject.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(pathGrid.Columns, pathGrid.Rows, pathGrid.CellSize, placementHeight, openCells, surface);
    }

    private static void OpenSegment(MonsterPathGrid grid, PathSegmentData segment)
    {
        if (grid == null || segment == null) return;
        if (!grid.TryWorldToCell(segment.start, out var a) || !grid.TryWorldToCell(segment.end, out var b)) return;
        int dx = Math.Sign(b.x - a.x), dy = Math.Sign(b.y - a.y);
        var cell = a;
        grid.SetOpen(cell, true);
        while (cell != b)
        {
            if (Math.Abs(b.x - cell.x) >= Math.Abs(b.y - cell.y)) cell.x += dx;
            else cell.y += dy;
            grid.SetOpen(cell, true);
        }
    }
}
