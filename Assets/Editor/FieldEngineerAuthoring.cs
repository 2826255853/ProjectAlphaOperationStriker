using System;
using UnityEditor;
using UnityEngine;

public static class FieldEngineerAuthoring
{
    const string Directory = "Assets/Models/FieldEngineer/";
    const string Finish = "FE_ServiceFinish";
    const string PrefabPath = "Assets/Resources/FieldEngineer.prefab";

    [MenuItem("Tools/行动打击/工兵/重建第三人称角色")]
    public static void Build()
    {
        var albedo = ImportTexture("FE_Workwear", TextureImporterType.Default, true);
        var normal = ImportTexture("FE_Surface", TextureImporterType.NormalMap, false);
        var metalSmooth = ImportTexture("FE_MetalSmooth", TextureImporterType.Default, false);
        string materialPath = Directory + Finish + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("Missing URP Lit shader");
            material = new Material(shader) { name = Finish };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 0.55f);
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap", metalSmooth);
        material.SetFloat("_Smoothness", 1f);
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        var root = new GameObject("FieldEngineer");
        try
        {
            var asset = ImportModel("FieldEngineer", material);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!skin || skin.sharedMaterial != material || skin.bones.Length < 19)
                throw new InvalidOperationException("Incomplete field engineer skin, skeleton or material binding");

            Transform foot = Array.Find(skin.bones, b => b.name == "Foot.L");
            Transform toes = Array.Find(skin.bones, b => b.name == "Toes.L");
            if (!foot || !toes) throw new InvalidOperationException("Missing foot alignment landmarks");
            Vector3 forward = Vector3.ProjectOnPlane(toes.position - foot.position, Vector3.up);
            model.transform.localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward)
                * model.transform.localRotation;

            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                float floor = float.PositiveInfinity;
                float top = float.NegativeInfinity;
                foreach (var vertex in baked.vertices)
                {
                    float y = skin.transform.TransformPoint(vertex).y;
                    floor = Mathf.Min(floor, y);
                    top = Mathf.Max(top, y);
                }
                float height = top - floor;
                if (height < 1.80f || height > 1.90f)
                    throw new InvalidOperationException($"Unexpected imported height {height:F3} m");
                model.transform.localPosition -= Vector3.up * floor;
#if ENABLE_DRONE_CODE
                var operatorModel = (GameObject)PrefabUtility.InstantiatePrefab(
                    ImportModel("FieldEngineer_Operator", material), root.transform);
                operatorModel.transform.SetLocalPositionAndRotation(model.transform.localPosition, model.transform.localRotation);
                UnityEngine.Object.DestroyImmediate(model);
                var operatorSkin = operatorModel.GetComponentInChildren<SkinnedMeshRenderer>();
                if (!operatorSkin || operatorSkin.sharedMaterial != material)
                    throw new InvalidOperationException("Missing operator mesh or suit material");
                var pose = root.AddComponent<DroneOperatorPose>();
                pose.left = FindGrip(operatorSkin, ".L");
                pose.right = FindGrip(operatorSkin, ".R");
#endif
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save FieldEngineer prefab");
                Debug.Log($"FieldEngineer ready: standing reference {height:F3} m");
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static GameObject ImportModel(string name, Material material)
    {
        string path = Directory + name + ".fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.addCollider = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), Finish), material);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

#if ENABLE_DRONE_CODE
    static DroneOperatorPose.Grip FindGrip(SkinnedMeshRenderer skin, string suffix)
    {
        Transform Bone(string name) => Array.Find(skin.bones, b => b.name == name + suffix)
            ?? throw new InvalidOperationException($"Missing operator bone {name}{suffix}");
        return new DroneOperatorPose.Grip { stick = Bone("Stick"), cap = Bone("Cap"), thumb = Bone("Thumb") };
    }
#endif

    static Texture2D ImportTexture(string name, TextureImporterType type, bool sRGB)
    {
        string path = Directory + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = type;
        importer.sRGBTexture = sRGB;
        importer.alphaIsTransparency = false;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
