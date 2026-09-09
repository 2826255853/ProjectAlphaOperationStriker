using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Imports MapForge world JSON files as Unity scenes.</summary>
[InitializeOnLoad]
public static class MapForgeWorldImporter
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
        if (File.Exists(source) && !File.Exists(Path.Combine(Application.dataPath, "Scenes", "UNTITLED_WORLD.unity")))
            Import(source);
    }
    [Serializable] private class World { public string name; public MapObject[] objects; public RoadsideStepData[] roadsideSteps; public GameplayData gameplay; }
    [Serializable] private class RoadsideStepData
    {
        public string platformId;
        public Vector3 position;
        public string axis = "z";
        public int nearSideSign = 1;
        public float length = 1f;
        public float width = 0.5f;
        public float height = 0.5f;
    }
    [Serializable] private class GameplayData
    {
        public PathGridData pathGrid;
        public CoreData core;
        public int totalWaves;
        public SpawnData[] spawns;
    }
    [Serializable] private class PathGridData
    {
        public Vector3 origin;
        public int columns = 1;
        public int rows = 1;
        public float cellSize = 1f;
        public float pathHeight;
        public bool[] openCells;
        public PathSegmentData[] segments;
    }
    [Serializable] private class PathSegmentData { public Vector3 start; public Vector3 end; }
    [Serializable] private class CoreData { public Vector3 position; public float arrivalRadius = 0.35f; }
    [Serializable] private class SpawnData
    {
        public string id;
        public Vector3 position;
        public Vector3 travelDirection = Vector3.forward;
        public float moveSpeed = 2f;
        // Retained for backwards compatibility with older map files. The
        // value is migrated to the scene-level WaveManager during import.
        public int totalWaves = 3;
        public int enemiesPerWave = 10;
        public float initialWaveDelay = 2f;
        public float[] waveTransitionDelays = { 5f, 5f };
    }
    [Serializable] private class MapObject
    {
        public string id;
        public string type;
        public TransformData transform;
    }
    [Serializable] private class TransformData
    {
        public float[] position;
        public float[] rotation;
        public float[] scale;
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
        if (!File.Exists(jsonPath)) { Debug.LogError("MapForge file not found: " + jsonPath); return; }
        World world;
        try { world = JsonUtility.FromJson<World>(File.ReadAllText(jsonPath)); }
        catch (Exception ex) { Debug.LogError("Could not parse MapForge JSON: " + ex.Message); return; }
        if (world == null) { Debug.LogError("MapForge JSON is empty or invalid."); return; }

        var sceneName = string.IsNullOrWhiteSpace(world.name) ? Path.GetFileNameWithoutExtension(jsonPath) : world.name;
        foreach (var c in Path.GetInvalidFileNameChars()) sceneName = sceneName.Replace(c, '_');
        var sceneDir = Path.Combine(Application.dataPath, "Scenes");
        Directory.CreateDirectory(sceneDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = sceneName;
        var root = new GameObject("MapForgeWorld");
        SceneManager.MoveGameObjectToScene(root, scene);
        var waveManagerObject = new GameObject("Wave Manager");
        waveManagerObject.transform.SetParent(root.transform, false);
        var waveManager = waveManagerObject.AddComponent<WaveManager>();
        int globalTotalWaves = world.gameplay != null ? world.gameplay.totalWaves : 0;
        if (globalTotalWaves <= 0 && world.gameplay != null && world.gameplay.spawns != null)
        {
            for (int i = 0; i < world.gameplay.spawns.Length; i++)
                if (world.gameplay.spawns[i] != null)
                    globalTotalWaves = Mathf.Max(globalTotalWaves, world.gameplay.spawns[i].totalWaves);
        }
        waveManager.TotalWaves = Mathf.Max(1, globalTotalWaves);
        if (world.objects != null)
        {
            foreach (var item in world.objects)
            {
                if (item == null) continue;
                var primitive = PrimitiveType.Cube;
                if (!string.IsNullOrEmpty(item.type) && item.type.Equals("sphere", StringComparison.OrdinalIgnoreCase)) primitive = PrimitiveType.Sphere;
                else if (!string.IsNullOrEmpty(item.type) && item.type.Equals("capsule", StringComparison.OrdinalIgnoreCase)) primitive = PrimitiveType.Capsule;
                else if (!string.IsNullOrEmpty(item.type) && item.type.Equals("cylinder", StringComparison.OrdinalIgnoreCase)) primitive = PrimitiveType.Cylinder;
                var go = GameObject.CreatePrimitive(primitive);
                go.name = string.IsNullOrEmpty(item.id) ? item.type : item.id;
                go.transform.SetParent(root.transform);
                ApplyTransform(go.transform, item.type, item.transform);
                // Keep the junction marker as a visual landmark only. Its
                // footprint is inside the road crossing and must not block
                // enemy movement or player placement.
                if (string.Equals(item.id, "Junction_Marker", StringComparison.OrdinalIgnoreCase))
                {
                    var collider = go.GetComponent<Collider>();
                    if (collider != null) collider.enabled = false;
                }
            }
        }
        ApplyRoadsideSteps(world.roadsideSteps, root.transform);
        CreateGameplayObjects(world.gameplay, root.transform, globalTotalWaves);
        var outPath = "Assets/Scenes/" + sceneName + ".unity";
        outPath = AssetDatabase.GenerateUniqueAssetPath(outPath);
        EditorSceneManager.SaveScene(scene, outPath);
        AssetDatabase.Refresh();
        Debug.Log($"Imported MapForge world '{sceneName}' with {root.transform.childCount} objects into {outPath}");
    }

    private static void ApplyRoadsideSteps(RoadsideStepData[] steps, Transform parent)
    {
        if (steps == null) return;
        foreach (var step in steps)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.platformId)) continue;
            var platform = parent.Find(step.platformId);
            if (platform == null) { Debug.LogWarning("Roadside step platform not found: " + step.platformId); continue; }

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
            if (beforeLength > 0.01f) CreateStepCube(platform, platform.name + "_HighBefore", nearPos, alongZ, stepWidth, beforeLength, stepMin - beforeLength * 0.5f, 1f, sourceMaterials);
            if (afterLength > 0.01f) CreateStepCube(platform, platform.name + "_HighAfter", nearPos, alongZ, stepWidth, afterLength, stepMax + afterLength * 0.5f, 1f, sourceMaterials);
            CreateStepCube(platform, platform.name + "_Step_0.5m", nearPos, alongZ, stepWidth, stepLength, alongZ ? step.position.z : step.position.x, Mathf.Max(0.01f, step.height), sourceMaterials);
        }
    }

    private static void CreateStepCube(Transform source, string name, Vector3 localPosition, bool alongZ, float sideWidth, float travelLength, float travelCenter, float height, Material[] materials)
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

    // MapForge and Unity use the same world units, but their primitive meshes
    // have different native dimensions.  The editor's plane is a 3 x 0.12 x 3
    // thin box, while Unity's Cube is 1 x 1 x 1.  Convert the authored scale
    // to preserve the actual bounds (and therefore snapped edge-to-edge tiles).
    private static void ApplyTransform(Transform t, string type, TransformData d)
    {
        if (d == null) return;
        if (d.position != null && d.position.Length >= 3) t.localPosition = new Vector3(d.position[0], d.position[1], d.position[2]);
        if (d.rotation != null && d.rotation.Length >= 3) t.localEulerAngles = new Vector3(d.rotation[0], d.rotation[1], d.rotation[2]);
        if (d.scale != null && d.scale.Length >= 3)
        {
            var authoredScale = new Vector3(d.scale[0], d.scale[1], d.scale[2]);
            var meshSize = PrimitiveMeshSize(type);
            t.localScale = Vector3.Scale(authoredScale, meshSize);
        }
    }

    private static Vector3 PrimitiveMeshSize(string type)
    {
        if (string.Equals(type, "plane", StringComparison.OrdinalIgnoreCase))
            return new Vector3(3f, 0.12f, 3f);
        // Three.js SphereGeometry(.6) has a 1.2m diameter; Unity's Sphere is 1m.
        if (string.Equals(type, "sphere", StringComparison.OrdinalIgnoreCase))
            return new Vector3(1.2f, 1.2f, 1.2f);
        // Three.js CylinderGeometry(..., height 1.4) versus Unity's 2m cylinder.
        if (string.Equals(type, "cylinder", StringComparison.OrdinalIgnoreCase))
            return new Vector3(1f, 0.7f, 1f);
        return Vector3.one;
    }
}
