using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Builds the stage-five visual prefab; runtime input and mounting belong to stage six.</summary>
public static class TrapPlacementProjectionGunAuthoring
{
    public const string ModelPath = "Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun_ScreenUpdated.fbx";
    public const string PrefabPath = "Assets/Resources/TrapPlacementProjectionGun.prefab";
    public const string MaterialDirectory = "Assets/Models/TrapPlacementProjectionGun/Materials";
    public const string MountName = "FirstPersonMount";
    public const int VisualLayer = 2; // Built-in Ignore Raycast; no project layer configuration is required.
    public static readonly Vector3 FirstPersonOffset = new Vector3(0.140f, -0.105f, 0.420f);
    public static readonly string[] MaterialNames = { "PG_WhiteShell", "PG_BlueArmor", "PG_DarkStructure",
        "PG_GripRubber", "PG_ProjectionLens", "PG_CyanEmission", "PG_DisplayGlass", "PG_RearDisplayCyan" };

    public static string OutputDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "TrapPlacementProjectionGun", "stage5-unity");

    [MenuItem("Tools/塔防/投影枪/生成阶段五材质和 Prefab")]
    public static void CreateAssetsBatch()
    {
        CreateAssets();
        Debug.Log("PROJECTION_GUN_STAGE5_BUILD_PASS: " + PrefabPath);
    }

    public static GameObject CreateAssets()
    {
        // Resolve every dependency before modifying a persistent asset.
        GameObject model = RequireModel();
        ValidateSource(model);
        foreach (string path in MaterialNames.Select(MaterialPath).Append(PrefabPath))
        {
            if (!File.Exists(path)) continue;
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (path == PrefabPath ? !(existing is GameObject) : !(existing is Material))
                throw new InvalidOperationException("Unexpected asset at projection gun output: " + path);
        }
        string backup = Path.Combine(OutputDirectory, "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(backup);
        var snapshots = MaterialNames.Select(MaterialPath).Append(PrefabPath)
            .Select(path => new AssetSnapshot(path, backup)).ToArray();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            EnsureFolder(MaterialDirectory);
            EnsureFolder("Assets/Resources");
            var materials = MaterialNames.ToDictionary(name => name, SaveMaterial);
            root = new GameObject("TrapPlacementProjectionGun");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var mount = new GameObject(MountName).transform;
            mount.SetParent(root.transform, false);
            mount.localPosition = FirstPersonOffset;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = "Model";
            instance.transform.SetParent(mount, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                node.gameObject.layer = VisualLayer;
                node.gameObject.isStatic = false;
            }
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => materials[m.name]).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            // Validate the in-memory hierarchy before publishing a prefab reference.
            TrapPlacementProjectionGunPrefabValidation.ValidateStructure(root, false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null) throw new InvalidOperationException("Could not save projection gun prefab.");
            foreach (Material material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            AssetDatabase.SaveAssetIfDirty(prefab);
            // Finish first-import material normalization before returning stable serialized assets.
            foreach (string path in MaterialNames.Select(MaterialPath).Append(PrefabPath))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            TrapPlacementProjectionGunPrefabValidation.ValidateStructure(prefab, true);
            Debug.Log("[ProjectionGunAuthoring] Saved " + PrefabPath + "; rollback backup: " + backup);
            return prefab;
        }
        catch
        {
            foreach (AssetSnapshot snapshot in snapshots.Reverse()) snapshot.Restore();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            throw;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static GameObject RequireModel()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Projection gun model is missing: " + ModelPath);
        if (Shader.Find("Universal Render Pipeline/Lit") == null)
            throw new InvalidOperationException("Projection gun requires the URP/Lit shader.");
        return model;
    }

    private static void ValidateSource(GameObject model)
    {
        var nodes = model.GetComponentsInChildren<Transform>(true);
        foreach (string name in new[] { "Muzzle", "GripAnchor", "ProjectionWindow" })
            if (nodes.Count(t => t.name == name) != 1)
                throw new InvalidOperationException("Expected a unique source node: " + name);
        if (model.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshFilter || c is MeshRenderer)))
            throw new InvalidOperationException("Projection gun source must contain static meshes and transforms only.");
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0 || renderers.Any(r => r.sharedMaterials.Any(m => m == null)
            || r.GetComponent<MeshFilter>()?.sharedMesh == null
            || r.sharedMaterials.Length != r.GetComponent<MeshFilter>().sharedMesh.subMeshCount))
            throw new InvalidOperationException("Projection gun source contains missing meshes or material slots.");
        var names = renderers.SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct().OrderBy(n => n);
        if (!names.SequenceEqual(MaterialNames.OrderBy(n => n)))
            throw new InvalidOperationException("Projection gun source must expose the eight stage-four material slots.");
    }

    public static string MaterialPath(string name) => MaterialDirectory + "/" + name + ".mat";

    // Shared with isolated previews so asset generation and validation use the same palette.
    public static Material CreateMaterial(string name)
    {
        string hex;
        float roughness, metallic, emission = 0f;
        switch (name)
        {
            case "PG_WhiteShell": hex = "#E5E9ED"; roughness = 0.29f; metallic = 0.12f; break;
            case "PG_BlueArmor": hex = "#127ED5"; roughness = 0.25f; metallic = 0.25f; break;
            case "PG_DarkStructure": hex = "#303B46"; roughness = 0.43f; metallic = 0.20f; break;
            case "PG_GripRubber": hex = "#28343D"; roughness = 0.73f; metallic = 0f; break;
            case "PG_ProjectionLens": hex = "#078DAA"; roughness = 0.21f; metallic = 0.35f; emission = 0.35f; break;
            case "PG_CyanEmission": hex = "#28E5EF"; roughness = 0.24f; metallic = 0.05f; emission = 2.8f; break;
            case "PG_RearDisplayCyan": hex = "#28E5EF"; roughness = 0.24f; metallic = 0.05f; emission = 1.1f; break;
            case "PG_DisplayGlass": hex = "#102F40"; roughness = 0.23f; metallic = 0.30f; break;
            default: throw new InvalidOperationException("Unknown projection gun material: " + name);
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Projection gun requires the URP/Lit shader.");
        ColorUtility.TryParseHtmlString(hex, out Color color);
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", 1f - roughness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_ZWrite", 1f);
        if (emission > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color.linear * emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return material;
    }

    private static Material SaveMaterial(string name)
    {
        string path = MaterialPath(name);
        Material definition = CreateMaterial(name);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }
        try
        {
            // Avoid dirtying already-correct assets: Unity's material importer also owns version metadata.
            if (!SameMaterial(existing, definition))
            {
                existing.shader = definition.shader;
                existing.CopyPropertiesFromMaterial(definition);
                existing.name = name;
                EditorUtility.SetDirty(existing);
            }
        }
        finally { Object.DestroyImmediate(definition); }
        return existing;
    }

    private static bool SameMaterial(Material actual, Material expected)
    {
        if (actual.shader != expected.shader || actual.name != expected.name || actual.renderQueue != expected.renderQueue
            || actual.globalIlluminationFlags != expected.globalIlluminationFlags
            || actual.enableInstancing != expected.enableInstancing || actual.doubleSidedGI != expected.doubleSidedGI
            || !actual.shaderKeywords.OrderBy(k => k).SequenceEqual(expected.shaderKeywords.OrderBy(k => k))) return false;
        Shader shader = expected.shader;
        for (int i = 0; i < shader.GetPropertyCount(); ++i)
        {
            string property = shader.GetPropertyName(i);
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color:
                    if (actual.GetColor(property) != expected.GetColor(property)) return false;
                    break;
                case ShaderPropertyType.Vector:
                    if (actual.GetVector(property) != expected.GetVector(property)) return false;
                    break;
                case ShaderPropertyType.Texture:
                    if (actual.GetTexture(property) != expected.GetTexture(property)
                        || actual.GetTextureOffset(property) != expected.GetTextureOffset(property)
                        || actual.GetTextureScale(property) != expected.GetTextureScale(property)) return false;
                    break;
                case ShaderPropertyType.Int:
                    if (actual.GetInteger(property) != expected.GetInteger(property)) return false;
                    break;
                default:
                    if (!Mathf.Approximately(actual.GetFloat(property), expected.GetFloat(property))) return false;
                    break;
            }
        }
        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException("Could not create asset directory: " + path);
    }

    private sealed class AssetSnapshot
    {
        private readonly string path, backupPath;
        private readonly bool existed;
        public AssetSnapshot(string path, string directory)
        {
            this.path = path;
            backupPath = Path.Combine(directory, Path.GetFileName(path));
            existed = File.Exists(path);
            if (!existed) return;
            File.Copy(path, backupPath);
            if (File.Exists(path + ".meta")) File.Copy(path + ".meta", backupPath + ".meta");
        }
        public void Restore()
        {
            if (!existed) { AssetDatabase.DeleteAsset(path); return; }
            File.Copy(backupPath, path, true);
            if (File.Exists(backupPath + ".meta")) File.Copy(backupPath + ".meta", path + ".meta", true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
