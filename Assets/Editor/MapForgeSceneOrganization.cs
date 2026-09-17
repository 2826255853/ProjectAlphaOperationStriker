using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
    }
    [Serializable] public sealed class TransformData { public float[] position, rotation, scale; }
    [Serializable] public sealed class MaterialData { public string type; public float roughness, metalness; }

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
        return document;
    }

    public static void Create(Document document, Transform parent, string sceneName)
    {
        var instances = new Dictionary<string, GameObject>();
        var meshes = new Dictionary<string, GameObject>();
        var nodes = new Dictionary<string, Node>();
        var materialFolder = "Assets/MapForgeMaterials/" + Hash(sceneName);
        Directory.CreateDirectory(materialFolder); AssetDatabase.Refresh();
        foreach (var node in document.objects)
        {
            var go = new GameObject(string.IsNullOrEmpty(node.name) ? node.id : node.name);
            go.transform.SetParent(parent, false); go.layer = node.layer; go.isStatic = node.@static;
            instances.Add(node.id, go); nodes.Add(node.id, node);
            var metadata = go.AddComponent<MapForgeObjectProperties>();
            metadata.objectId = node.id; metadata.objectType = node.type; metadata.tags = node.tags ?? Array.Empty<string>();
            metadata.prefabType = node.prefabType; metadata.parametersJson = string.IsNullOrEmpty(node.parametersJson) ? "{}" : node.parametersJson;
            metadata.category = node.category; metadata.visible = node.visible; metadata.locked = node.locked; metadata.collision = node.collision; metadata.collapsed = node.collapsed;
            if (node.tags != null && node.tags.Length > 0 && Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, node.tags[0]) >= 0) go.tag = node.tags[0];
            if (node.type == "folder" || node.type == "group") continue;
            var primitive = node.type == "sphere" ? PrimitiveType.Sphere : node.type == "cylinder" ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            // Keep the stable ID on the geometry for existing Platform_*/Marker authoring rules.
            var mesh = GameObject.CreatePrimitive(primitive); mesh.name = node.id; mesh.transform.SetParent(go.transform, false);
            meshes.Add(node.id, mesh);
            mesh.transform.localScale = node.type == "plane" ? new Vector3(3f, .12f, 3f) : node.type == "sphere" ? Vector3.one * 1.2f : node.type == "cylinder" ? new Vector3(1f, .7f, 1f) : Vector3.one;
            mesh.layer = node.layer; mesh.isStatic = node.@static; mesh.tag = go.tag;
            mesh.GetComponent<Collider>().enabled = node.collision && !string.Equals(node.id, "Junction_Marker", StringComparison.OrdinalIgnoreCase);
            mesh.GetComponent<Renderer>().sharedMaterial = CreateMaterial(node, materialFolder);
        }
        foreach (var node in document.objects)
        {
            var go = instances[node.id];
            go.transform.SetParent(string.IsNullOrEmpty(node.parentId) ? parent : instances[node.parentId].transform, false);
            go.transform.localPosition = Vector(node.transform.position); go.transform.localScale = Vector(node.transform.scale);
            // Three.js uses intrinsic XYZ Euler rotations, stored in radians. Unity's Euler() uses a different order.
            var r = Vector(node.transform.rotation) * Mathf.Rad2Deg;
            go.transform.localRotation = Quaternion.AngleAxis(r.x, Vector3.right) * Quaternion.AngleAxis(r.y, Vector3.up) * Quaternion.AngleAxis(r.z, Vector3.forward);
            var visible = node.visible; var locked = node.locked; var ancestorId = node.parentId;
            while (!string.IsNullOrEmpty(ancestorId)) { var ancestor = nodes[ancestorId]; visible &= ancestor.visible; locked |= ancestor.locked; ancestorId = ancestor.parentId; }
            // Editor visibility and renderer visibility do not change authored collision participation.
            if (meshes.TryGetValue(node.id, out var geometry)) geometry.GetComponent<Renderer>().enabled = visible;
            if (!visible) SceneVisibilityManager.instance.Hide(go, false);
            if (locked) SceneVisibilityManager.instance.DisablePicking(go, true);
        }
        AssetDatabase.SaveAssets();
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
    private static void ValidateVector(float[] values, bool scale)
    {
        if (values == null || values.Length != 3) throw new ArgumentException("MapForge transforms require three numbers.");
        foreach (var value in values) if (float.IsNaN(value) || float.IsInfinity(value) || (scale && value < .01f)) throw new ArgumentException("Invalid MapForge transform value.");
    }
}
