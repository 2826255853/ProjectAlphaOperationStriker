using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Version 2 hierarchy importer; primitive dimensions live on a mesh child, never on the authored parent.</summary>
public static class MapForgeSceneOrganization
{
    [Serializable] public sealed class Document
    {
        public int version;
        public string name;
        public string rotationUnit;
        public SettingsData settings;
        public Node[] objects;
        public GroupData[] groups;
        public UnitySceneData unity;
    }
    [Serializable] public sealed class SettingsData { public float gridSize = 1f; public string unit = "meter"; }
    [Serializable] public sealed class GroupData { public string id, name, parentId; }
    [Serializable] public sealed class Node
    {
        public string id, name, type, parentId, color, prefabType, category, parametersJson;
        public string[] tags;
        public int layer;
        public bool visible, locked, collision, collapsed;
        public bool @static;
        public TransformData transform;
        public MaterialData material;
        public UnityMapping unity;
    }
    [Serializable] public sealed class TransformData { public float[] position, rotation, scale; }
    [Serializable] public sealed class MaterialData { public string type; public float roughness, metalness; }

    // ----- Scene-wide Unity component mapping (the "unity" block of the map file). -----
    [Serializable] public sealed class UnitySceneData
    {
        public PrefabData[] prefabs;
        public WaveData waves;
        public BakeData settings;
    }
    [Serializable] public sealed class PrefabData { public string id, name, kind, assetPath, guid; }
    [Serializable] public sealed class WaveData
    {
        public int totalWaves = 3;
        public float initialDelay = 2f;
        public int enemiesPerWave = 10;
        public float spawnInterval = 1f;
        public float[] transitionDelays;
    }
    [Serializable] public sealed class BakeData { public bool buildNavMeshOnImport; public string agentType = "Humanoid"; public string collectObjects = "all"; }

    // ----- Per-object Unity component mapping. -----
    [Serializable] public sealed class UnityMapping
    {
        public PrefabRef prefab;
        public ColliderMap collider;
        public NavMeshMap navMesh;
        public SpawnPointMap spawnPoint;
        public CoreMap enemyCore;
        public PathNodeMap pathNode;
        public TriggerMap trigger;
    }
    [Serializable] public sealed class PrefabRef { public string id, guid, assetPath; }
    [Serializable] public sealed class ColliderMap
    {
        public string type = "auto";
        public bool isTrigger;
        public float[] center, size;
        public float radius = .5f, height = 2f;
        public int direction = 1;
    }
    [Serializable] public sealed class NavMeshMap
    {
        public string role = "none", agentType = "Humanoid";
        public int area;
        public bool ignoreFromBuild, applyToChildren, bidirectional = true;
        public float[] linkStart, linkEnd;
        public float linkWidth = 1f;
    }
    [Serializable] public sealed class SpawnPointMap
    {
        public bool enabled;
        public float spawnInterval = 1f, initialDelay = 2f;
        public int enemiesPerWave = 10;
        public float moveSpeed = 2f;
        public float[] travelDirection;
        public string monsterType = "Ground";
        public float flightHeight = 3f;
        public bool flyingEntrance;
        public float entranceAltitude = 12f;
        public string laneId, enemyPrefabId;
        public WaveOverride[] waves;
    }
    [Serializable] public sealed class WaveOverride { public bool enabled = true; public int enemyCount = 10; public string monsterType = ""; public float flightHeight = 3f; }
    [Serializable] public sealed class CoreMap { public bool enabled; public float maxHealth = 30f, groundDamage = 2f, flyingDamage = 1f, arrivalRadius = .35f; }
    [Serializable] public sealed class PathNodeMap { public bool enabled; public string role = "node"; public float waitTime; public string[] links; }
    [Serializable] public sealed class TriggerMap
    {
        public bool enabled;
        public string shape = "box";
        public bool isTrigger = true, once;
        public float[] center, size;
        public float radius = 1f;
        public TriggerEventMap[] events;
    }
    [Serializable] public sealed class TriggerEventMap { public string when = "enter", action = "message", target, message; public float amount = 1f, delay; }

    private static readonly string[] ColliderTypes = { "auto", "box", "sphere", "capsule", "mesh", "none" };
    private static readonly string[] NavRoles = { "none", "walkable", "obstacle", "notWalkable", "link", "source" };
    private static readonly string[] PathRoles = { "node", "waypoint", "junction", "end" };
    private static readonly string[] TriggerShapes = { "box", "sphere" };
    private static readonly string[] TriggerWhens = { "enter", "exit" };
    private static readonly string[] TriggerActions = { "spawn", "damage", "goal", "message", "enable", "disable" };
    private static readonly string[] MonsterTypes = { "Ground", "Flying" };
    private static readonly string[] WaveMonsterTypes = { "", "Ground", "Flying" };
    private static readonly string[] PrefabKinds = { "object", "enemy", "tower", "trap", "prop" };
    private static readonly string[] CollectObjectModes = { "all", "volume", "children", "markedWithModifier" };

    /// <summary>NavMesh area index used when a mapping marks an object as an obstacle.</summary>
    private const int NotWalkableArea = 1;

    public static Document Parse(string json)
    {
        var document = JsonUtility.FromJson<Document>(json);
        if (document == null || document.version != 2 || document.objects == null)
            throw new ArgumentException("MapForge v2 requires version=2 and an objects array.");
        if (!string.IsNullOrEmpty(document.rotationUnit) && document.rotationUnit != "radians")
            throw new ArgumentException("MapForge rotationUnit must be radians.");
        if (document.settings != null && (document.settings.gridSize <= 0f || document.settings.unit != "meter"))
            throw new ArgumentException("MapForge settings require a positive gridSize and unit=meter.");
        var nodes = new Dictionary<string, Node>();
        foreach (var node in document.objects)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id) || nodes.ContainsKey(node.id)) throw new ArgumentException("Missing or duplicate MapForge object ID.");
            if (node.type != "cube" && node.type != "sphere" && node.type != "cylinder" && node.type != "plane" && node.type != "folder" && node.type != "group") throw new ArgumentException("Unsupported MapForge type: " + node.type);
            if (node.layer < 0 || node.layer > 31 || node.transform == null) throw new ArgumentException("Invalid layer or transform: " + node.id);
            ValidateVector(node.transform.position, false); ValidateVector(node.transform.rotation, false); ValidateVector(node.transform.scale, true);
            nodes.Add(node.id, node);
        }
        foreach (var node in document.objects)
        {
            var visited = new HashSet<string> { node.id };
            var parent = node.parentId;
            while (!string.IsNullOrEmpty(parent))
            {
                if (!nodes.TryGetValue(parent, out var ancestor) || !visited.Add(parent) || visited.Count > 256) throw new ArgumentException("Invalid MapForge parent hierarchy: " + node.id);
                parent = ancestor.parentId;
            }
        }
        ValidateUnityScene(document.unity);
        foreach (var node in document.objects) ValidateUnityMapping(node.unity, node.id, nodes);
        return document;
    }

    /// <summary>Validates the scene-wide Unity mapping: prefab library, wave data and bake options.</summary>
    private static void ValidateUnityScene(UnitySceneData unity)
    {
        if (unity == null) return;
        var ids = new HashSet<string>();
        if (unity.prefabs != null)
            foreach (var prefab in unity.prefabs)
            {
                if (prefab == null || string.IsNullOrWhiteSpace(prefab.id)) throw new ArgumentException("MapForge unity.prefabs entries need a Prefab ID.");
                if (!ids.Add(prefab.id)) throw new ArgumentException("Duplicate MapForge Prefab ID: " + prefab.id);
                if (!string.IsNullOrEmpty(prefab.kind) && Array.IndexOf(PrefabKinds, prefab.kind) < 0) throw new ArgumentException("Unsupported MapForge Prefab kind: " + prefab.kind);
            }
        if (unity.waves != null)
        {
            var waves = unity.waves;
            if (waves.totalWaves < 1 || waves.totalWaves > 99) throw new ArgumentException("MapForge unity.waves.totalWaves must be 1-99.");
            if (waves.enemiesPerWave < 0 || waves.enemiesPerWave > 999) throw new ArgumentException("MapForge unity.waves.enemiesPerWave must be 0-999.");
            if (!Range(waves.initialDelay, 0f, 600f)) throw new ArgumentException("MapForge unity.waves.initialDelay must be 0-600.");
            if (!Range(waves.spawnInterval, .01f, 600f)) throw new ArgumentException("MapForge unity.waves.spawnInterval must be 0.01-600.");
            if (waves.transitionDelays != null)
            {
                if (waves.transitionDelays.Length > 32) throw new ArgumentException("MapForge unity.waves.transitionDelays accepts at most 32 values.");
                foreach (var delay in waves.transitionDelays) if (!Range(delay, 0f, float.MaxValue)) throw new ArgumentException("MapForge unity.waves.transitionDelays must be non-negative.");
            }
        }
        if (unity.settings != null && !string.IsNullOrEmpty(unity.settings.collectObjects) && Array.IndexOf(CollectObjectModes, unity.settings.collectObjects) < 0)
            throw new ArgumentException("Unsupported MapForge unity.settings.collectObjects: " + unity.settings.collectObjects);
    }

    /// <summary>Validates one object mapping. Path links are checked against the authored IDs.</summary>
    private static void ValidateUnityMapping(UnityMapping unity, string id, Dictionary<string, Node> nodes)
    {
        if (unity == null) return;
        if (unity.collider != null)
        {
            var collider = unity.collider;
            if (!string.IsNullOrEmpty(collider.type) && Array.IndexOf(ColliderTypes, collider.type) < 0) throw new ArgumentException(id + " has an unsupported collider type: " + collider.type);
            ValidateVector(collider.center, false); ValidateVector(collider.size, true);
            if (!Range(collider.radius, .01f, float.MaxValue) || !Range(collider.height, .01f, float.MaxValue)) throw new ArgumentException(id + " has an invalid collider radius or height.");
            if (collider.direction < 0 || collider.direction > 2) throw new ArgumentException(id + " has an invalid collider direction.");
        }
        if (unity.navMesh != null)
        {
            var navMesh = unity.navMesh;
            if (!string.IsNullOrEmpty(navMesh.role) && Array.IndexOf(NavRoles, navMesh.role) < 0) throw new ArgumentException(id + " has an unsupported NavMesh role: " + navMesh.role);
            if (navMesh.area < 0 || navMesh.area > 31) throw new ArgumentException(id + " has a NavMesh area outside 0-31.");
            if (!Range(navMesh.linkWidth, .01f, float.MaxValue)) throw new ArgumentException(id + " has an invalid NavMesh link width.");
            ValidateVector(navMesh.linkStart, false); ValidateVector(navMesh.linkEnd, false);
        }
        if (unity.spawnPoint != null)
        {
            var spawn = unity.spawnPoint;
            if (!Range(spawn.spawnInterval, .01f, 600f)) throw new ArgumentException(id + " has a spawn interval outside 0.01-600.");
            if (!Range(spawn.initialDelay, 0f, 600f)) throw new ArgumentException(id + " has an initial wave delay outside 0-600.");
            if (spawn.enemiesPerWave < 0 || spawn.enemiesPerWave > 999) throw new ArgumentException(id + " has an enemies-per-wave value outside 0-999.");
            if (!Range(spawn.moveSpeed, 0f, 100f)) throw new ArgumentException(id + " has a move speed outside 0-100.");
            if (!Range(spawn.flightHeight, 0f, 200f)) throw new ArgumentException(id + " has a flight height outside 0-200.");
            if (!Range(spawn.entranceAltitude, 0f, 500f)) throw new ArgumentException(id + " has an entrance altitude outside 0-500.");
            if (!string.IsNullOrEmpty(spawn.monsterType) && Array.IndexOf(MonsterTypes, spawn.monsterType) < 0) throw new ArgumentException(id + " has an unsupported monster type: " + spawn.monsterType);
            ValidateVector(spawn.travelDirection, false);
            if (spawn.waves != null)
                foreach (var wave in spawn.waves)
                {
                    if (wave == null) continue;
                    if (wave.enemyCount < 0 || wave.enemyCount > 999) throw new ArgumentException(id + " has a wave enemy count outside 0-999.");
                    if (!Range(wave.flightHeight, 0f, 200f)) throw new ArgumentException(id + " has a wave flight height outside 0-200.");
                    if (!string.IsNullOrEmpty(wave.monsterType) && Array.IndexOf(WaveMonsterTypes, wave.monsterType) < 0) throw new ArgumentException(id + " has an unsupported wave monster type: " + wave.monsterType);
                }
        }
        if (unity.enemyCore != null)
        {
            var core = unity.enemyCore;
            if (!Range(core.maxHealth, 1f, 1e6f)) throw new ArgumentException(id + " has a core health outside 1-1000000.");
            if (!Range(core.groundDamage, 0f, 1e6f) || !Range(core.flyingDamage, 0f, 1e6f)) throw new ArgumentException(id + " has core damage outside 0-1000000.");
            if (!Range(core.arrivalRadius, .01f, float.MaxValue)) throw new ArgumentException(id + " has an invalid core arrival radius.");
        }
        if (unity.pathNode != null)
        {
            var path = unity.pathNode;
            if (!string.IsNullOrEmpty(path.role) && Array.IndexOf(PathRoles, path.role) < 0) throw new ArgumentException(id + " has an unsupported path role: " + path.role);
            if (!Range(path.waitTime, 0f, 600f)) throw new ArgumentException(id + " has a path wait time outside 0-600.");
            if (path.links != null)
                foreach (var link in path.links)
                    if (!string.IsNullOrWhiteSpace(link) && !nodes.ContainsKey(link)) throw new ArgumentException(id + " links to an unknown path node: " + link);
        }
        if (unity.trigger != null)
        {
            var trigger = unity.trigger;
            if (!string.IsNullOrEmpty(trigger.shape) && Array.IndexOf(TriggerShapes, trigger.shape) < 0) throw new ArgumentException(id + " has an unsupported trigger shape: " + trigger.shape);
            if (!Range(trigger.radius, .01f, float.MaxValue)) throw new ArgumentException(id + " has an invalid trigger radius.");
            ValidateVector(trigger.center, false); ValidateVector(trigger.size, true);
            if (trigger.events != null)
                foreach (var entry in trigger.events)
                {
                    if (entry == null) continue;
                    if (!string.IsNullOrEmpty(entry.when) && Array.IndexOf(TriggerWhens, entry.when) < 0) throw new ArgumentException(id + " has an unsupported trigger timing: " + entry.when);
                    if (!string.IsNullOrEmpty(entry.action) && Array.IndexOf(TriggerActions, entry.action) < 0) throw new ArgumentException(id + " has an unsupported trigger action: " + entry.action);
                    if (!Range(entry.amount, 0f, 1e9f)) throw new ArgumentException(id + " has a trigger amount outside 0-1000000000.");
                    if (!Range(entry.delay, 0f, 600f)) throw new ArgumentException(id + " has a trigger delay outside 0-600.");
                }
        }
    }

    public static void Create(Document document, Transform parent, string sceneName)
    {
        var instances = new Dictionary<string, GameObject>();
        var meshes = new Dictionary<string, GameObject>();
        var nodes = new Dictionary<string, Node>();
        var prefabCache = new Dictionary<string, GameObject>();
        foreach (var node in document.objects)
        {
            var go = CreateHierarchyNode(node, parent, sceneName, out var geometry);
            instances.Add(node.id, go); nodes.Add(node.id, node);
            if (geometry != null) meshes.Add(node.id, geometry);
        }
        foreach (var node in document.objects)
        {
            var go = instances[node.id];
            go.transform.SetParent(string.IsNullOrEmpty(node.parentId) ? parent : instances[node.parentId].transform, false);
            ApplyNodeTransform(node, go, meshes.TryGetValue(node.id, out var geometry) ? geometry : null, nodes);
        }
        // Unity mapping runs after the whole hierarchy exists, so prefabs, links and
        // spawn points can reference objects that appear later in the file.
        ApplyUnityMappings(document, instances, meshes, prefabCache);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Runs the per-object Unity mapping for every authored mapping, in document order.</summary>
    public static void ApplyUnityMappings(Document document, Dictionary<string, GameObject> instances, Dictionary<string, GameObject> meshes, Dictionary<string, GameObject> prefabCache)
    {
        foreach (var node in document.objects)
        {
            if (node.unity == null) continue;
            if (!instances.TryGetValue(node.id, out var go)) continue;
            meshes.TryGetValue(node.id, out var geometry);
            ApplyUnityMapping(node, go, geometry, document.unity, prefabCache);
        }
    }

    /// <summary>
    /// Creates the MapForge object for one node: its metadata, and the primitive geometry
    /// child that carries the authored footprint. The caller is responsible for parenting,
    /// so the full import and the incremental sync share exactly one creation path.
    /// </summary>
    public static GameObject CreateHierarchyNode(Node node, Transform parent, string sceneName, out GameObject geometry)
    {
        var go = new GameObject(string.IsNullOrEmpty(node.name) ? node.id : node.name);
        go.transform.SetParent(parent, false); go.layer = node.layer; go.isStatic = node.@static;
        var metadata = go.AddComponent<MapForgeObjectProperties>();
        ApplyMetadata(node, metadata);
        if (node.tags != null && node.tags.Length > 0 && Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, node.tags[0]) >= 0) go.tag = node.tags[0];
        EnsureGeometryChild(node, go, null, sceneName, out geometry);
        return go;
    }

    /// <summary>
    /// Makes the primitive child that carries an object's authored footprint and returns
    /// the object. A created object passes no reusable child; the incremental sync passes
    /// the one already in the scene, so an updated object keeps the primitive named after
    /// its object ID that the project's Platform_*/Marker authoring rules look up.
    ///
    /// Folders and groups have no geometry, so a child left over from an earlier type is
    /// removed. A reusable child of the wrong primitive is replaced rather than reworked:
    /// a box collider on a sphere mesh would otherwise keep the old footprint while the
    /// scene looked correct.
    /// </summary>
    public static GameObject EnsureGeometryChild(Node node, GameObject go, GameObject reusable, string sceneName, out GameObject geometry)
    {
        geometry = null;
        if (node.type == "folder" || node.type == "group")
        {
            if (reusable != null) UnityEngine.Object.DestroyImmediate(reusable);
            return go;
        }
        if (reusable != null && !MatchesPrimitive(reusable, node.type))
        {
            UnityEngine.Object.DestroyImmediate(reusable);
            reusable = null;
        }
        var mesh = reusable != null ? reusable : GameObject.CreatePrimitive(PrimitiveFor(node.type));
        geometry = mesh;
        mesh.name = node.id;
        mesh.transform.SetParent(go.transform, false);
        return ApplyGeometry(node, go, mesh, sceneName);
    }

    /// <summary>
    /// Rebuilds the primitive look of a geometry child: authored mesh scale (the editor's
    /// plane/sphere/cylinder primitives are not Unity's size), layer, tag, collider state
    /// and the generated material. The incremental sync reuses the same child object, so
    /// this runs for every object it touches.
    /// </summary>
    public static GameObject ApplyGeometry(Node node, GameObject go, GameObject mesh, string sceneName)
    {
        if (mesh == null) return go;
        mesh.name = node.id;
        mesh.transform.localScale = MeshScale(node.type);
        mesh.layer = go.layer; mesh.isStatic = go.isStatic; mesh.tag = go.tag;
        var renderer = mesh.GetComponent<Renderer>();
        if (renderer != null) { renderer.sharedMaterial = CreateMaterial(node, MaterialFolder(sceneName)); renderer.enabled = node.visible; }
        var collider = mesh.GetComponent<Collider>();
        if (collider != null) collider.enabled = node.collision && !IsJunction(node);
        return go;
    }

    /// <summary>
    /// Copies the authored block metadata onto a component. The incremental sync reuses this
    /// so an updated object keeps its GameObject identity and its stable geometry child name.
    /// </summary>
    public static void ApplyMetadata(Node node, MapForgeObjectProperties metadata)
    {
        if (metadata == null) return;
        metadata.objectId = node.id; metadata.objectType = node.type; metadata.tags = node.tags ?? Array.Empty<string>();
        metadata.prefabType = node.prefabType; metadata.parametersJson = string.IsNullOrEmpty(node.parametersJson) ? "{}" : node.parametersJson;
        metadata.category = node.category; metadata.visible = node.visible; metadata.locked = node.locked; metadata.collision = node.collision; metadata.collapsed = node.collapsed;
    }

    /// <summary>Adds the primitive collider Unity would have put on a fresh primitive of this type.</summary>
    public static void AddPrimitiveCollider(GameObject mesh, PrimitiveType primitive, bool enabled)
    {
        if (mesh == null || mesh.GetComponent<Collider>() != null) return;
        Collider collider = primitive == PrimitiveType.Sphere ? (Collider)mesh.AddComponent<SphereCollider>()
            : primitive == PrimitiveType.Cylinder ? mesh.AddComponent<CapsuleCollider>()
            : mesh.AddComponent<BoxCollider>();
        collider.enabled = enabled;
    }

    /// <summary>
    /// True when a reusable geometry child already carries the authored primitive. The
    /// check compares mesh names rather than references, because a child that came through
    /// a scene file is deserialized and no longer reference-equal to the builtin mesh.
    /// </summary>
    private static bool MatchesPrimitive(GameObject mesh, string type)
    {
        var filter = mesh.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;
        return string.Equals(filter.sharedMesh.name, PrimitiveMeshName(PrimitiveFor(type)), StringComparison.Ordinal);
    }

    private static bool IsJunction(Node node) => string.Equals(node.id, "Junction_Marker", StringComparison.OrdinalIgnoreCase);

    private static PrimitiveType PrimitiveFor(string type) =>
        type == "sphere" ? PrimitiveType.Sphere : type == "cylinder" ? PrimitiveType.Cylinder : PrimitiveType.Cube;

    /// <summary>Name of the builtin mesh Unity's primitive of this type uses.</summary>
    private static string PrimitiveMeshName(PrimitiveType primitive) =>
        primitive == PrimitiveType.Sphere ? "Sphere" : primitive == PrimitiveType.Cylinder ? "Cylinder" : primitive == PrimitiveType.Capsule ? "Capsule" : "Cube";

    /// <summary>Mesh scale that preserves the map editor's authored bounds.</summary>
    private static Vector3 MeshScale(string type) =>
        type == "plane" ? new Vector3(3f, .12f, 3f) : type == "sphere" ? Vector3.one * 1.2f : type == "cylinder" ? new Vector3(1f, .7f, 1f) : Vector3.one;
    /// <summary>
    /// Authored folder for this scene's generated materials. Created on demand, because
    /// AssetDatabase.CreateAsset fails when the parent folder does not exist yet: the
    /// incremental sync recreates materials in a scene that has none on disk.
    /// </summary>
    public static string MaterialFolder(string sceneName)
    {
        var folder = "Assets/MapForgeMaterials/" + Hash(sceneName);
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/MapForgeMaterials"))
            {
                Directory.CreateDirectory("Assets/MapForgeMaterials");
                AssetDatabase.Refresh();
            }
            AssetDatabase.CreateFolder("Assets/MapForgeMaterials", Hash(sceneName));
        }
        return folder;
    }

    /// <summary>Writes one node's local transform plus the inherited visibility and picking state.</summary>
    public static void ApplyNodeTransform(Node node, GameObject go, GameObject geometry, Dictionary<string, Node> nodes)
    {
        go.transform.localPosition = Vector(node.transform.position); go.transform.localScale = Vector(node.transform.scale);
        // Three.js uses intrinsic XYZ Euler rotations, stored in radians. Unity's Euler() uses a different order.
        var r = Vector(node.transform.rotation) * Mathf.Rad2Deg;
        go.transform.localRotation = Quaternion.AngleAxis(r.x, Vector3.right) * Quaternion.AngleAxis(r.y, Vector3.up) * Quaternion.AngleAxis(r.z, Vector3.forward);
        var visible = node.visible; var locked = node.locked; var ancestorId = node.parentId;
        while (!string.IsNullOrEmpty(ancestorId))
        {
            if (nodes == null || !nodes.TryGetValue(ancestorId, out var ancestor)) break;
            visible &= ancestor.visible; locked |= ancestor.locked; ancestorId = ancestor.parentId;
        }
        // Editor visibility and renderer visibility do not change authored collision participation.
        if (geometry != null)
        {
            var renderer = geometry.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null) renderer.enabled = visible;
        }
        SceneVisibilityManager.instance.Show(go, false);
        SceneVisibilityManager.instance.EnablePicking(go, true);
        if (!visible) SceneVisibilityManager.instance.Hide(go, false);
        if (locked) SceneVisibilityManager.instance.DisablePicking(go, true);
    }

    /// <summary>
    /// Strips everything a previous import generated under one object - prefab instance,
    /// trigger volume, gameplay components and any collider the old mapping added - so the
    /// incremental sync can rebuild that object from the new authored mapping without
    /// duplicates. The object itself, its metadata and its geometry child are kept, which
    /// is what lets the sync update an object in place instead of recreating it.
    /// </summary>
    public static void ClearGeneratedContent(GameObject go, GameObject geometry, Node node)
    {
        if (go == null) return;
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
        {
            if (child == go.transform) continue;
            if (child.name.StartsWith("Prefab ", StringComparison.Ordinal) ||
                child.name.StartsWith("Trigger Volume", StringComparison.Ordinal)) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        foreach (var component in go.GetComponents<Component>())
        {
            if (component is MapForgeObjectProperties) continue;
            if (component is Transform) continue;
            if (component is NavMeshModifier || component is NavMeshLink || component is NavMeshSurface ||
                component is EnemySpawnPoint || component is EnemyCore || component is MapForgePathNode || component is MapForgeTrigger)
                UnityEngine.Object.DestroyImmediate(component);
        }
        if (geometry == null || node == null) return;
        var renderer = geometry.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = node.visible;
        // A typed or "none" mapping replaced the primitive collider, so every collider
        // that is not the primitive one for the authored type is dropped here; the
        // mapping recreates what it needs. An "auto" mapping then reuses the survivor,
        // which is why the primitive collider is kept rather than rebuilt.
        var expected = UnityColliderType(PrimitiveFor(node.type));
        bool keptPrimitive = false;
        foreach (var collider in geometry.GetComponents<Collider>())
        {
            if (!keptPrimitive && collider.GetType() == expected) { keptPrimitive = true; continue; }
            UnityEngine.Object.DestroyImmediate(collider);
        }
        if (!keptPrimitive) AddPrimitiveCollider(geometry, PrimitiveFor(node.type), node.collision && !IsJunction(node));
    }

    /// <summary>Component type of the collider Unity puts on a primitive mesh.</summary>
    private static Type UnityColliderType(PrimitiveType primitive) =>
        primitive == PrimitiveType.Sphere ? typeof(SphereCollider) : primitive == PrimitiveType.Cylinder ? typeof(CapsuleCollider) : typeof(BoxCollider);
    /// <summary>Creates the generated Unity components for one authored object.</summary>
    public static void ApplyUnityMapping(Node node, GameObject go, GameObject geometry, UnitySceneData scene, Dictionary<string, GameObject> prefabCache)
    {
        var unity = node.unity;
        var metadata = go.GetComponent<MapForgeObjectProperties>();
        metadata.prefabId = unity.prefab != null ? unity.prefab.id ?? string.Empty : string.Empty;
        metadata.prefabGuid = unity.prefab != null ? unity.prefab.guid ?? string.Empty : string.Empty;
        metadata.prefabAssetPath = unity.prefab != null ? unity.prefab.assetPath ?? string.Empty : string.Empty;
        metadata.colliderType = ColliderType(unity.collider, "auto");
        metadata.colliderIsTrigger = unity.collider != null && unity.collider.isTrigger;
        metadata.colliderCenter = unity.collider != null ? Vector(unity.collider.center, Vector3.zero) : Vector3.zero;
        metadata.colliderSize = unity.collider != null ? Vector(unity.collider.size, Vector3.one) : Vector3.one;
        metadata.colliderRadius = unity.collider != null ? Mathf.Max(.01f, unity.collider.radius) : .5f;
        metadata.colliderHeight = unity.collider != null ? Mathf.Max(.01f, unity.collider.height) : 2f;
        metadata.colliderDirection = unity.collider != null ? Mathf.Clamp(unity.collider.direction, 0, 2) : 1;
        metadata.navRole = NavRole(unity.navMesh, "none");
        metadata.navArea = unity.navMesh != null ? Mathf.Clamp(unity.navMesh.area, 0, 31) : 0;
        metadata.navAgentType = Text(unity.navMesh != null ? unity.navMesh.agentType : null, "Humanoid");
        metadata.navIgnoreFromBuild = unity.navMesh != null && unity.navMesh.ignoreFromBuild;
        metadata.navApplyToChildren = unity.navMesh != null && unity.navMesh.applyToChildren;
        metadata.navLinkStart = unity.navMesh != null ? Vector(unity.navMesh.linkStart, Vector3.zero) : Vector3.zero;
        metadata.navLinkEnd = unity.navMesh != null ? Vector(unity.navMesh.linkEnd, Vector3.zero) : Vector3.zero;
        metadata.navLinkWidth = unity.navMesh != null ? Mathf.Max(.01f, unity.navMesh.linkWidth) : 1f;
        metadata.navBidirectional = unity.navMesh == null || unity.navMesh.bidirectional;

        var spawn = unity.spawnPoint;
        metadata.spawnPointEnabled = spawn != null && spawn.enabled;
        metadata.spawnInterval = spawn != null ? Mathf.Clamp(spawn.spawnInterval, .01f, 600f) : 1f;
        metadata.spawnInitialDelay = spawn != null ? Mathf.Clamp(spawn.initialDelay, 0f, 600f) : 2f;
        metadata.spawnEnemiesPerWave = spawn != null ? Mathf.Clamp(spawn.enemiesPerWave, 1, 999) : 10;
        metadata.spawnMoveSpeed = spawn != null ? Mathf.Clamp(spawn.moveSpeed, 0f, 100f) : 2f;
        metadata.spawnTravelDirection = spawn != null ? Vector(spawn.travelDirection, Vector3.forward) : Vector3.forward;
        metadata.spawnMonsterType = spawn != null ? Text(spawn.monsterType, "Ground") : "Ground";
        metadata.spawnFlightHeight = spawn != null ? Mathf.Clamp(spawn.flightHeight, 0f, 200f) : 3f;
        metadata.spawnFlyingEntrance = spawn != null && spawn.flyingEntrance;
        metadata.spawnEntranceAltitude = spawn != null ? Mathf.Clamp(spawn.entranceAltitude, 0f, 500f) : 12f;
        metadata.spawnLaneId = spawn != null ? Text(spawn.laneId, string.Empty) : string.Empty;
        metadata.spawnEnemyPrefabId = spawn != null ? Text(spawn.enemyPrefabId, string.Empty) : string.Empty;
        metadata.spawnWaves = CopyWaves(spawn != null ? spawn.waves : null);

        var core = unity.enemyCore;
        metadata.enemyCoreEnabled = core != null && core.enabled;
        metadata.enemyCoreMaxHealth = core != null ? Mathf.Max(1f, core.maxHealth) : 30f;
        metadata.enemyCoreGroundDamage = core != null ? Mathf.Max(0f, core.groundDamage) : 2f;
        metadata.enemyCoreFlyingDamage = core != null ? Mathf.Max(0f, core.flyingDamage) : 1f;
        metadata.enemyCoreArrivalRadius = core != null ? Mathf.Max(.01f, core.arrivalRadius) : .35f;

        var path = unity.pathNode;
        metadata.pathNodeEnabled = path != null && path.enabled;
        metadata.pathNodeRole = path != null ? Text(path.role, "node") : "node";
        metadata.pathNodeWaitTime = path != null ? Mathf.Max(0f, path.waitTime) : 0f;
        metadata.pathNodeLinks = path != null && path.links != null ? (string[])path.links.Clone() : Array.Empty<string>();

        var trigger = unity.trigger;
        metadata.triggerEnabled = trigger != null && trigger.enabled;
        metadata.triggerShape = trigger != null ? Text(trigger.shape, "box") : "box";
        metadata.triggerIsTrigger = trigger == null || trigger.isTrigger;
        metadata.triggerOnce = trigger != null && trigger.once;
        metadata.triggerCenter = trigger != null ? Vector(trigger.center, Vector3.zero) : Vector3.zero;
        metadata.triggerSize = trigger != null ? Vector(trigger.size, Vector3.one) : Vector3.one;
        metadata.triggerRadius = trigger != null ? Mathf.Max(.01f, trigger.radius) : 1f;
        metadata.triggerEvents = CopyEvents(trigger != null ? trigger.events : null);

        if (!string.IsNullOrEmpty(metadata.prefabId))
        {
            var prefab = ResolvePrefab(metadata, scene, prefabCache);
            if (prefab != null) AttachPrefabInstance(go, geometry, prefab, metadata.prefabId);
        }
        if (geometry != null) ApplyCollider(geometry, node, unity.collider);
        if (unity.navMesh != null) ApplyNavMesh(go, unity.navMesh);
        if (trigger != null && trigger.enabled) CreateTriggerVolume(go, trigger);
        if (path != null && path.enabled)
        {
            var pathNode = go.AddComponent<MapForgePathNode>();
            pathNode.role = PathRole(metadata.pathNodeRole);
            pathNode.waitTime = metadata.pathNodeWaitTime;
            pathNode.links = metadata.pathNodeLinks;
        }
        if (spawn != null && spawn.enabled) ApplySpawnPoint(go, spawn, scene);
        if (core != null && core.enabled) ApplyEnemyCore(go, core);
    }

    /// <summary>Resolves a Prefab ID to a real prefab asset through the scene prefab library.</summary>
    private static GameObject ResolvePrefab(MapForgeObjectProperties metadata, UnitySceneData scene, Dictionary<string, GameObject> cache)
    {
        if (cache.TryGetValue(metadata.prefabId, out var cached)) return cached;
        string assetPath = metadata.prefabAssetPath;
        if (string.IsNullOrEmpty(assetPath) && scene != null && scene.prefabs != null)
            foreach (var entry in scene.prefabs)
                if (entry != null && entry.id == metadata.prefabId) { assetPath = entry.assetPath; break; }
        GameObject prefab = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
            Debug.LogWarning("MapForge: Prefab ID '" + metadata.prefabId + "' has no resolvable asset path" +
                (string.IsNullOrEmpty(assetPath) ? "." : " (" + assetPath + ")."));
        cache[metadata.prefabId] = prefab;
        return prefab;
    }

    /// <summary>
    /// Instantiates the mapped prefab under the authored object and hides the blockout
    /// renderer, so the scene shows the real prefab while the authored footprint and
    /// its collider stay exactly where the map editor placed them.
    /// </summary>
    private static void AttachPrefabInstance(GameObject go, GameObject geometry, GameObject prefab, string prefabId)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
        instance.name = "Prefab " + prefabId;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        if (geometry == null) return;
        var renderer = geometry.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;
    }

    /// <summary>
    /// Creates or reconfigures the authored collider on the blockout geometry.
    /// "auto" keeps the Unity primitive collider untouched (the default), "none"
    /// removes it, and the typed entries replace it. Sizes are authored in map
    /// units, so they are divided by the primitive mesh scale to stay identical
    /// to what the map editor previews. The junction marker keeps no collider.
    /// </summary>
    private static void ApplyCollider(GameObject geometry, Node node, ColliderMap mapping)
    {
        var type = ColliderType(mapping, "auto");
        var existing = geometry.GetComponent<Collider>();
        // The junction marker is a landmark inside the road crossing and must never
        // block enemy movement or trap placement, whatever the mapping says.
        bool junction = string.Equals(node.id, "Junction_Marker", StringComparison.OrdinalIgnoreCase);
        if (type == "auto")
        {
            if (existing == null) return;
            existing.enabled = node.collision && !junction;
            if (mapping != null && !junction) existing.isTrigger = mapping.isTrigger;
            return;
        }
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        if (type == "none" || junction) return;
        var meshScale = geometry.transform.localScale;
        float Axis(int axis) => Mathf.Max(.0001f, Mathf.Abs(meshScale[axis]));
        float Radial() => Mathf.Max(Axis(0), Axis(2));
        var center = mapping != null ? Vector(mapping.center, Vector3.zero) : Vector3.zero;
        if (type == "box")
        {
            var authored = mapping != null ? Vector(mapping.size, Vector3.one) : Vector3.one;
            var box = geometry.AddComponent<BoxCollider>();
            box.center = center;
            box.size = new Vector3(authored.x / Axis(0), authored.y / Axis(1), authored.z / Axis(2));
        }
        else if (type == "sphere")
        {
            var sphere = geometry.AddComponent<SphereCollider>();
            sphere.center = center;
            sphere.radius = Mathf.Max(.01f, (mapping != null ? mapping.radius : .5f) / Radial());
        }
        else if (type == "capsule")
        {
            int direction = Mathf.Clamp(mapping != null ? mapping.direction : 1, 0, 2);
            var capsule = geometry.AddComponent<CapsuleCollider>();
            capsule.center = center;
            capsule.direction = direction;
            capsule.radius = Mathf.Max(.01f, (mapping != null ? mapping.radius : .5f) / Radial());
            capsule.height = Mathf.Max(capsule.radius * 2f, (mapping != null ? mapping.height : 2f) / Axis(direction));
        }
        else if (type == "mesh")
        {
            var meshCollider = geometry.AddComponent<MeshCollider>();
            var filter = geometry.GetComponent<MeshFilter>();
            if (filter != null) meshCollider.sharedMesh = filter.sharedMesh;
        }
        var collider = geometry.GetComponent<Collider>();
        if (collider != null) collider.isTrigger = mapping != null && mapping.isTrigger;
    }

    /// <summary>Adds the NavMeshModifier/NavMeshLink/NavMeshSurface a mapping role asks for.</summary>
    private static void ApplyNavMesh(GameObject go, NavMeshMap mapping)
    {
        var role = NavRole(mapping, "none");
        if (role == "none") return;
        int agentTypeID = ResolveAgentType(mapping.agentType);
        if (role == "link")
        {
            var link = go.AddComponent<NavMeshLink>();
            var serialized = new SerializedObject(link);
            // NavMeshLink setters call UpdateLink, so write the serialized fields
            // first and refresh the link once at the end.
            serialized.FindProperty("m_AgentTypeID").intValue = agentTypeID;
            serialized.FindProperty("m_StartPoint").vector3Value = Vector(mapping.linkStart, Vector3.zero);
            serialized.FindProperty("m_EndPoint").vector3Value = Vector(mapping.linkEnd, Vector3.zero);
            serialized.FindProperty("m_Width").floatValue = Mathf.Max(.01f, mapping.linkWidth);
            serialized.FindProperty("m_Bidirectional").boolValue = mapping.bidirectional;
            serialized.FindProperty("m_Area").intValue = Mathf.Clamp(mapping.area, 0, 31);
            serialized.FindProperty("m_Activated").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            link.UpdateLink();
            return;
        }
        if (role == "source")
        {
            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentTypeID;
            surface.collectObjects = SceneCollectObjects(mapping.applyToChildren ? "children" : "all");
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            return;
        }
        var modifier = go.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = role == "walkable" ? Mathf.Clamp(mapping.area, 0, 31) : NotWalkableArea;
        modifier.ignoreFromBuild = mapping.ignoreFromBuild;
        modifier.applyToChildren = mapping.applyToChildren;
    }

    /// <summary>
    /// Creates the authored trigger volume as a child object with its own trigger collider,
    /// a kinematic Rigidbody (so trigger messages reach gameplay code) and MapForgeTrigger.
    /// </summary>
    private static void CreateTriggerVolume(GameObject go, TriggerMap mapping)
    {
        var volume = new GameObject(mapping.shape == "sphere" ? "Trigger Volume (Sphere)" : "Trigger Volume (Box)");
        volume.transform.SetParent(go.transform, false);
        volume.transform.localPosition = Vector(mapping.center, Vector3.zero);
        if (mapping.shape == "sphere") volume.AddComponent<SphereCollider>().radius = Mathf.Max(.01f, mapping.radius);
        else volume.AddComponent<BoxCollider>().size = Vector(mapping.size, Vector3.one);
        var collider = volume.GetComponent<Collider>();
        collider.isTrigger = mapping.isTrigger;
        // A trigger body only needs to exist so Unity sends OnTrigger events.
        var body = volume.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        if (!mapping.isTrigger)
            Debug.LogWarning("MapForge: trigger volume under '" + go.name + "' is not marked as a trigger and can block the monster route.");
        var trigger = volume.AddComponent<MapForgeTrigger>();
        trigger.once = mapping.once;
        trigger.events = CopyTriggerEvents(mapping.events);
    }

    /// <summary>Writes the authored spawn point into an EnemySpawnPoint component.</summary>
    private static void ApplySpawnPoint(GameObject go, SpawnPointMap mapping, UnitySceneData scene)
    {
        var spawnPoint = go.AddComponent<EnemySpawnPoint>();
        int totalWaves = scene != null && scene.waves != null ? Mathf.Clamp(scene.waves.totalWaves, 1, 99) : 3;
        var serialized = new SerializedObject(spawnPoint);
        serialized.FindProperty("spawningEnabled").boolValue = true;
        serialized.FindProperty("spawnInterval").floatValue = Mathf.Clamp(mapping.spawnInterval, .01f, 600f);
        serialized.FindProperty("enemiesPerWave").intValue = Mathf.Clamp(mapping.enemiesPerWave, 1, 999);
        serialized.FindProperty("initialWaveDelay").floatValue = Mathf.Clamp(mapping.initialDelay, 0f, 600f);
        serialized.FindProperty("travelDirection").vector3Value = Vector(mapping.travelDirection, Vector3.forward);
        serialized.FindProperty("moveSpeed").floatValue = Mathf.Clamp(mapping.moveSpeed, 0f, 100f);
        serialized.FindProperty("monsterType").enumValueIndex = Text(mapping.monsterType, "Ground") == "Flying" ? 1 : 0;
        serialized.FindProperty("flightHeight").floatValue = Mathf.Clamp(mapping.flightHeight, 0f, 200f);
        serialized.FindProperty("flyingEntrance").boolValue = mapping.flyingEntrance;
        serialized.FindProperty("entranceAltitude").floatValue = Mathf.Clamp(mapping.entranceAltitude, 0f, 500f);
        // The wave list has to match the scene WaveManager's total wave count, so
        // each spawned wave always has authored data behind it.
        var authoredDelays = scene != null && scene.waves != null ? scene.waves.transitionDelays : null;
        var delays = serialized.FindProperty("waveTransitionDelays");
        delays.arraySize = Mathf.Max(0, totalWaves - 1);
        for (int i = 0; i < delays.arraySize; i++)
            delays.GetArrayElementAtIndex(i).floatValue = authoredDelays != null && i < authoredDelays.Length ? Mathf.Max(0f, authoredDelays[i]) : 5f;
        var waves = serialized.FindProperty("waveSpawnSettings");
        waves.arraySize = Mathf.Max(0, totalWaves);
        for (int i = 0; i < waves.arraySize; i++)
        {
            var element = waves.GetArrayElementAtIndex(i);
            var authored = mapping.waves != null && i < mapping.waves.Length ? mapping.waves[i] : null;
            element.FindPropertyRelative("enabled").boolValue = authored == null || authored.enabled;
            element.FindPropertyRelative("enemyCount").intValue = Mathf.Clamp(authored != null ? authored.enemyCount : mapping.enemiesPerWave, 0, 999);
            element.FindPropertyRelative("monsterType").enumValueIndex = authored != null && authored.monsterType == "Flying" ? 1 : 0;
            element.FindPropertyRelative("flightHeight").floatValue = Mathf.Clamp(authored != null ? authored.flightHeight : mapping.flightHeight, 0f, 200f);
            element.FindPropertyRelative("enemyPrefab").objectReferenceValue = null;
        }
        if (!string.IsNullOrEmpty(mapping.enemyPrefabId))
        {
            var enemyPrefab = ResolveScenePrefab(mapping.enemyPrefabId, scene);
            if (enemyPrefab != null) serialized.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Writes the authored core statistics into an EnemyCore component.</summary>
    private static void ApplyEnemyCore(GameObject go, CoreMap mapping)
    {
        var core = go.AddComponent<EnemyCore>();
        var serialized = new SerializedObject(core);
        serialized.FindProperty("maxHealth").floatValue = Mathf.Max(1f, mapping.maxHealth);
        serialized.FindProperty("groundDamage").floatValue = Mathf.Max(0f, mapping.groundDamage);
        serialized.FindProperty("flyingDamage").floatValue = Mathf.Max(0f, mapping.flyingDamage);
        serialized.FindProperty("arrivalRadius").floatValue = Mathf.Max(.01f, mapping.arrivalRadius);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Wires the generated EnemySpawnPoint and EnemyCore components to the scene's
    /// MonsterPathGrid and core. The gameplay block creates those, so this runs after
    /// it and only fills references the mapping left empty.
    /// </summary>
    public static void LinkGameplayReferences(Transform parent)
    {
        if (parent == null) return;
        var core = parent.GetComponentInChildren<EnemyCore>(true);
        var grid = parent.GetComponentInChildren<MonsterPathGrid>(true);
        foreach (var spawnPoint in parent.GetComponentsInChildren<EnemySpawnPoint>(true))
        {
            var serialized = new SerializedObject(spawnPoint);
            var gridProperty = serialized.FindProperty("pathGrid");
            var coreProperty = serialized.FindProperty("core");
            if (grid != null && gridProperty.objectReferenceValue == null) gridProperty.objectReferenceValue = grid;
            if (core != null && coreProperty.objectReferenceValue == null) coreProperty.objectReferenceValue = core;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (core == null) return;
        var coreSerialized = new SerializedObject(core);
        var coreGrid = coreSerialized.FindProperty("pathGrid");
        if (grid == null || coreGrid.objectReferenceValue != null) return;
        coreGrid.objectReferenceValue = grid;
        coreSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Maps a MapForge NavMesh agent type name to a Unity agent type ID.</summary>
    public static int ResolveAgentType(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "Humanoid") return 0;
        try
        {
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                if (string.Equals(NavMesh.GetSettingsNameFromID(settings.agentTypeID), name, StringComparison.OrdinalIgnoreCase)) return settings.agentTypeID;
            }
        }
        catch (Exception ex) { Debug.LogWarning("MapForge: could not read the NavMesh agent types (" + ex.Message + ")."); }
        Debug.LogWarning("MapForge: NavMesh agent type '" + name + "' was not found; using the default agent instead.");
        return 0;
    }

    /// <summary>Creates the scene-wide NavMeshSurface the scene settings ask for.</summary>
    public static NavMeshSurface CreateNavMeshSurface(Transform parent, UnitySceneData scene)
    {
        var settings = scene != null ? scene.settings : null;
        var surfaceObject = new GameObject("NavMesh Surface");
        surfaceObject.transform.SetParent(parent, false);
        var surface = surfaceObject.AddComponent<NavMeshSurface>();
        surface.agentTypeID = ResolveAgentType(settings != null ? settings.agentType : null);
        surface.collectObjects = SceneCollectObjects(settings != null ? settings.collectObjects : null);
        // MapForge blockouts are primitives with matching colliders, so physics
        // geometry keeps the baked surface tied to the authored collision footprint.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        return surface;
    }

    /// <summary>Parses an authored collect mode; unknown values fall back to the whole scene.</summary>
    public static CollectObjects SceneCollectObjects(string value)
    {
        if (value == "volume") return CollectObjects.Volume;
        if (value == "children") return CollectObjects.Children;
        if (value == "markedWithModifier") return CollectObjects.MarkedWithModifier;
        return CollectObjects.All;
    }

    private static GameObject ResolveScenePrefab(string prefabId, UnitySceneData scene)
    {
        if (scene == null || scene.prefabs == null) return null;
        foreach (var entry in scene.prefabs)
        {
            if (entry == null || entry.id != prefabId) continue;
            if (string.IsNullOrEmpty(entry.assetPath)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.assetPath);
            if (prefab == null) Debug.LogWarning("MapForge: Prefab ID '" + prefabId + "' points at a missing asset: " + entry.assetPath);
            return prefab;
        }
        Debug.LogWarning("MapForge: Prefab ID '" + prefabId + "' is not in the scene prefab library.");
        return null;
    }

    private static MapForgePathNode.Role PathRole(string role)
    {
        if (role == "waypoint") return MapForgePathNode.Role.Waypoint;
        if (role == "junction") return MapForgePathNode.Role.Junction;
        if (role == "end") return MapForgePathNode.Role.End;
        return MapForgePathNode.Role.Node;
    }

    private static MapForgeWaveData[] CopyWaves(WaveOverride[] waves)
    {
        if (waves == null) return Array.Empty<MapForgeWaveData>();
        var result = new MapForgeWaveData[waves.Length];
        for (int i = 0; i < waves.Length; i++)
        {
            var wave = waves[i];
            result[i] = wave == null ? new MapForgeWaveData() : new MapForgeWaveData
            {
                enabled = wave.enabled,
                enemyCount = Mathf.Clamp(wave.enemyCount, 0, 999),
                monsterType = wave.monsterType ?? string.Empty,
                flightHeight = Mathf.Clamp(wave.flightHeight, 0f, 200f),
            };
        }
        return result;
    }

    /// <summary>Converts the authored trigger events into the runtime MapForgeTrigger payload.</summary>
    private static MapForgeTriggerEvent[] CopyTriggerEvents(TriggerEventMap[] events)
    {
        if (events == null) return Array.Empty<MapForgeTriggerEvent>();
        var result = new MapForgeTriggerEvent[events.Length];
        for (int i = 0; i < events.Length; i++)
        {
            var entry = events[i];
            result[i] = entry == null ? new MapForgeTriggerEvent() : new MapForgeTriggerEvent
            {
                when = Text(entry.when, "enter") == "exit" ? MapForgeTriggerEvent.When.Exit : MapForgeTriggerEvent.When.Enter,
                action = TriggerAction(entry.action),
                target = entry.target ?? string.Empty,
                message = entry.message ?? string.Empty,
                amount = Mathf.Clamp(entry.amount, 0f, 1e9f),
                delay = Mathf.Clamp(entry.delay, 0f, 600f),
            };
        }
        return result;
    }

    private static MapForgeTriggerEvent.Action TriggerAction(string action)
    {
        if (action == "spawn") return MapForgeTriggerEvent.Action.Spawn;
        if (action == "damage") return MapForgeTriggerEvent.Action.Damage;
        if (action == "goal") return MapForgeTriggerEvent.Action.Goal;
        if (action == "enable") return MapForgeTriggerEvent.Action.Enable;
        if (action == "disable") return MapForgeTriggerEvent.Action.Disable;
        return MapForgeTriggerEvent.Action.Message;
    }

    private static MapForgeTriggerEventData[] CopyEvents(TriggerEventMap[] events)
    {
        if (events == null) return Array.Empty<MapForgeTriggerEventData>();
        var result = new MapForgeTriggerEventData[events.Length];
        for (int i = 0; i < events.Length; i++)
        {
            var entry = events[i];
            result[i] = entry == null ? new MapForgeTriggerEventData() : new MapForgeTriggerEventData
            {
                when = Text(entry.when, "enter"),
                action = Text(entry.action, "message"),
                target = entry.target ?? string.Empty,
                message = entry.message ?? string.Empty,
                amount = Mathf.Clamp(entry.amount, 0f, 1e9f),
                delay = Mathf.Clamp(entry.delay, 0f, 600f),
            };
        }
        return result;
    }

    private static Material CreateMaterial(Node node, string folder)
    {
        bool unlit = node.material != null && node.material.type == "unlit";
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit") ?? Shader.Find(unlit ? "Unlit/Color" : "Standard");
        if (shader == null) throw new InvalidOperationException("No compatible MapForge material shader found.");
        var path = folder + "/" + Hash(node.id) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); } else material.shader = shader;
        material.name = "MapForge " + node.name;
        if (ColorUtility.TryParseHtmlString(node.color, out var color)) { if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color); if (material.HasProperty("_Color")) material.SetColor("_Color", color); }
        if (node.material != null)
        {
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", node.material.metalness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f - node.material.roughness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1f - node.material.roughness);
        }
        EditorUtility.SetDirty(material); return material;
    }

    private static string Hash(string text)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 24);
    }

    private static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);

    /// <summary>Reads an authored vector, falling back when the map omitted it.</summary>
    private static Vector3 Vector(float[] values, Vector3 fallback) =>
        values != null && values.Length == 3 ? new Vector3(values[0], values[1], values[2]) : fallback;

    private static string Text(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ColliderType(ColliderMap mapping, string fallback) => mapping == null ? fallback : Text(mapping.type, fallback);
    private static string NavRole(NavMeshMap mapping, string fallback) => mapping == null ? fallback : Text(mapping.role, fallback);

    /// <summary>Authored ranges must be finite, matching the browser and API validation.</summary>
    private static bool Range(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

    private static void ValidateVector(float[] values, bool scale)
    {
        if (values == null) return;
        if (values.Length != 3) throw new ArgumentException("MapForge vectors require three numbers.");
        foreach (var value in values) if (float.IsNaN(value) || float.IsInfinity(value) || (scale && value < .01f)) throw new ArgumentException("Invalid MapForge vector value.");
    }
}
