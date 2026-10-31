using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RG777「雷锤」电磁炮的 URP 材质库生成器。
///
/// 背景：Blender 导出的 FBX 里带的是内置 Standard 材质，URP 工程里会渲染成品红。
/// 这里把 9 个材质槽（6 个不透明 PBR + 3 档蓝色自发光）生成为 URP 材质资产，
/// 再通过 ModelImporter.AddRemap 绑回 <see cref="ModelPath"/>，导入后不会再有品红面。
///
/// 绑定流程沿用工程里 SentryMachineGunAuthoring 的既有做法：
///   ImportStandard + AddRemap → 校验每个 Renderer 都拿到外部材质 → 失败时用
///   ImportViaMaterialDescription 重试一次。两条路都失败才抛异常，不留下半绑定的模型。
///
/// 菜单：Tools/塔防/生成 RG777 电磁炮 URP 材质
/// 批处理：RG777MaterialAuthoring.CreateMaterialsBatch
/// </summary>
public static class RG777MaterialAuthoring
{
    public const string ModelPath = "Assets/Models/RG777/RG777.fbx";
    public const string MaterialFolder = "Assets/Models/RG777/Materials";

    private const string UrpLit = "Universal Render Pipeline/Lit";

    // 三档蓝色自发光材质的固定名字：充能灯带按这三档切换。
    public const string GlowFullName = "RG777_GlowBlueFull";
    public const string GlowMidName = "RG777_GlowBlueMid";
    public const string GlowDimName = "RG777_GlowBlueDim";

    /// <summary>一个材质槽的规格：颜色取 Blender 里的线性值，smoothness = 1 - roughness。</summary>
    private struct Spec
    {
        public string Name;
        public Color BaseColor;
        public float Metallic;
        public float Smoothness;
        public Color Emission;      // alpha 为 0 表示不发光
        public float EmissionScale;

        public Spec(string name, Color baseColor, float metallic, float smoothness,
            Color emission = default, float emissionScale = 0f)
        {
            Name = name;
            BaseColor = baseColor;
            Metallic = metallic;
            Smoothness = smoothness;
            Emission = emission;
            EmissionScale = emissionScale;
        }

        public bool IsEmissive => EmissionScale > 0f;
    }

    private static readonly Spec[] Specs =
    {
        // ---- 不透明 PBR：白 / 灰 / 蓝 三主色 + 镀铬 + 橡胶 ----
        new Spec("RG777_WhiteArmor", new Color(0.52f, 0.55f, 0.59f), 0.12f, 0.45f),
        new Spec("RG777_PlateGray", new Color(0.155f, 0.170f, 0.195f), 0.70f, 0.55f),
        new Spec("RG777_DarkMetal", new Color(0.045f, 0.052f, 0.062f), 0.88f, 0.68f),
        new Spec("RG777_BlueMetal", new Color(0.028f, 0.105f, 0.300f), 0.85f, 0.74f),
        new Spec("RG777_ChromeRod", new Color(0.62f, 0.66f, 0.72f), 0.95f, 0.82f),
        new Spec("RG777_Rubber", new Color(0.018f, 0.020f, 0.024f), 0.10f, 0.18f),

        // ---- 蓝色自发光三档：充能灯带满亮 / 半亮 / 待机 ----
        // 发光强度沿用 Blender 里的 2.4 / 1.2 / 0.45，乘在 HDR 自发光色上。
        new Spec(GlowFullName, new Color(0.02f, 0.22f, 0.80f), 0f, 0.30f,
            new Color(0.03f, 0.32f, 1.00f), 2.4f),
        new Spec(GlowMidName, new Color(0.02f, 0.16f, 0.62f), 0f, 0.30f,
            new Color(0.02f, 0.24f, 0.85f), 1.2f),
        new Spec(GlowDimName, new Color(0.015f, 0.08f, 0.30f), 0f, 0.35f,
            new Color(0.02f, 0.12f, 0.45f), 0.45f),
    };

    [MenuItem("Tools/塔防/生成 RG777 电磁炮 URP 材质")]
    public static void CreateMaterialsFromMenu()
    {
        CreateMaterials();
        EditorUtility.DisplayDialog("RG777",
            "URP 材质已生成并绑定到 RG777.fbx。\n共 " + Specs.Length + " 个材质。", "好");
    }

    /// <summary>批处理入口：生成材质 + 绑定 + 校验，最后打印 RG777_MATERIALS_PASS / FAIL。</summary>
    public static void CreateMaterialsBatch()
    {
        try
        {
            CreateMaterials();
            Debug.Log("RG777_MATERIALS_PASS: " + Specs.Length + " URP materials generated in "
                      + MaterialFolder + " and bound to " + ModelPath + ".");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("RG777_MATERIALS_FAIL: " + ex);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ 生成

    public static void CreateMaterials()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            throw new InvalidOperationException("找不到模型：" + ModelPath);

        Shader shader = Shader.Find(UrpLit);
        if (shader == null)
            throw new InvalidOperationException(
                "找不到 URP Lit 着色器；工程是否切回了内置管线？");

        EnsureFolder(MaterialFolder);

        var materials = new Dictionary<string, Material>();
        foreach (Spec spec in Specs)
        {
            string path = MaterialFolder + "/" + spec.Name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = spec.Name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            Apply(material, spec);
            EditorUtility.SetDirty(material);
            materials[spec.Name] = material;
        }

        AssetDatabase.SaveAssets();

        ApplyImporter(materials.Keys.ToList(), materials);

        // 绑定后再校验一次，并顺带报一下每个材质用到的网格数量，便于回归时肉眼对照。
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        int rendererCount = CountRenderersUsing(model, materials.Values);
        Debug.Log("[RG777] 材质绑定完成：" + materials.Count + " 个材质，"
                  + rendererCount + " 个 Renderer 引用它们。");
    }

    private static void Apply(Material material, Spec spec)
    {
        material.SetColor("_BaseColor", spec.BaseColor);
        material.color = spec.BaseColor;               // 兼容少数只读 .color 的工具
        material.SetFloat("_Metallic", spec.Metallic);
        material.SetFloat("_Smoothness", spec.Smoothness);
        material.SetFloat("_Glossiness", spec.Smoothness);   // 内置管线回退时仍可用

        if (spec.IsEmissive)
        {
            Color emission = spec.Emission * spec.EmissionScale;
            emission.a = 1f;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // ------------------------------------------------------------------ 导入绑定

    private static void ApplyImporter(IList<string> slots, IDictionary<string, Material> materials)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("不是 FBX 模型：" + ModelPath);

        ApplyImporterSettings(importer, ModelImporterMaterialImportMode.ImportStandard);
        RemapMaterials(importer, slots, materials);
        importer.SaveAndReimport();

        // ImportStandard + AddRemap 是文档路径，但工程若切到材质描述导入会静默失效，
        // 所以再重试一次 ImportViaMaterialDescription，避免留下品红模型。
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (MaterialsBound(model, materials)) return;

        Debug.LogWarning("[RG777] 材质重映射未生效，改用 ImportViaMaterialDescription 重试。");
        var retry = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (retry == null) throw new InvalidOperationException("不是 FBX 模型：" + ModelPath);
        ApplyImporterSettings(retry, ModelImporterMaterialImportMode.ImportViaMaterialDescription);
        RemapMaterials(retry, slots, materials);
        retry.SaveAndReimport();

        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!MaterialsBound(model, materials))
            throw new InvalidOperationException("无法把 URP 材质库绑定到 " + ModelPath);
    }

    private static void ApplyImporterSettings(ModelImporter importer,
        ModelImporterMaterialImportMode materialMode)
    {
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = true;
        importer.globalScale = 1f;
        // 炮塔要靠 XM777_Base_Root / XM777_Turret_Root / XM777_Pitch_Root / Muzzle 这些空节点定位，
        // 必须保留层级，否则 XM777Trap.cs 的 FindChild 找不到枢轴。
        importer.preserveHierarchy = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.materialImportMode = materialMode;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialSearch = ModelImporterMaterialSearch.Local;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
    }

    private static void RemapMaterials(ModelImporter importer, IEnumerable<string> slots,
        IDictionary<string, Material> materials)
    {
        foreach (string slot in slots)
        {
            Material material;
            if (!materials.TryGetValue(slot, out material) || material == null) continue;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), material);
        }
    }

    private static bool MaterialsBound(GameObject model, IDictionary<string, Material> materials)
    {
        if (model == null) return false;
        var allowed = new HashSet<Material>(materials.Values.Where(m => m != null));
        MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) return false;
        foreach (MeshRenderer renderer in renderers)
        {
            Material[] assigned = renderer.sharedMaterials;
            if (assigned.Length == 0) return false;
            foreach (Material material in assigned)
                if (material == null || !allowed.Contains(material)) return false;
        }
        return true;
    }

    private static int CountRenderersUsing(GameObject model, IEnumerable<Material> materials)
    {
        if (model == null) return 0;
        var allowed = new HashSet<Material>(materials.Where(m => m != null));
        return model.GetComponentsInChildren<MeshRenderer>(true)
            .Count(r => r.sharedMaterials.Any(m => m != null && allowed.Contains(m)));
    }
}
