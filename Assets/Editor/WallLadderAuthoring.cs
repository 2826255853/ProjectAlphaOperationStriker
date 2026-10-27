using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 贴墙垂直梯子（WallLadder）的资产生成、数值校验与预览入口。
///
/// 模型约定（Blender 源在桌面「陷阱素材文件夹\WallLadder」）：
/// - 长轴竖直，脚底在本地 Y = 0，可攀爬高度 = <see cref="ModelHeight"/>；
/// - 背面（安装面）在本地 Z = 0，梯身朝 +Z 伸出，进深只有 7 cm 左右，因此能贴平墙面；
/// - 前进方向 +Z 背离墙面，顶部下梯方向是本地 -Z（朝墙、也就是朝上层平台的内侧）。
///
/// 菜单：Tools/塔防/贴墙梯子/…
/// 批处理：CreateAssetsBatch / ValidateBatch / RenderPreviewBatch(需要 -WithGraphics)
/// </summary>
public static class WallLadderAuthoring
{
    public const string ModelPath = "Assets/Models/WallLadder/WallLadder.fbx";
    public const string MaterialDir = "Assets/Models/WallLadder/Materials";
    public const string PrefabPath = "Assets/Prefabs/WallLadder.prefab";

    /// <summary>可攀爬高度，与模型包围盒一致；被 <see cref="Ladder.height"/> 引用。</summary>
    public const float ModelHeight = 3.20f;
    /// <summary>含两侧墙面安装法兰的总宽（导轨外缘之间是 0.60 m）。</summary>
    public const float ModelWidth = 0.66f;
    /// <summary>贴墙允许的最大进深；超过说明模型不再贴墙。</summary>
    public const float MaxDepth = 0.12f;

    // 攀爬触发体积：模型本身只有 7 cm 厚，触发区必须向玩家一侧留出余量。
    public const float TriggerWidth = 0.80f;
    public const float TriggerDepth = 0.60f;
    public const float TopDismountThreshold = 0.30f;

    private static readonly Dictionary<string, MaterialSpec> Palette = new Dictionary<string, MaterialSpec>
    {
        ["WL_SafetyYellow"] = new MaterialSpec(new Color(0.82f, 0.59f, 0.055f), 0.10f, 0.42f),
        ["WL_SteelDark"] = new MaterialSpec(new Color(0.105f, 0.115f, 0.130f), 0.72f, 0.50f),
        ["WL_ZincBolt"] = new MaterialSpec(new Color(0.64f, 0.66f, 0.69f), 0.90f, 0.62f),
    };

    private readonly struct MaterialSpec
    {
        public readonly Color BaseColor;
        public readonly float Metallic;
        public readonly float Smoothness;

        public MaterialSpec(Color baseColor, float metallic, float smoothness)
        {
            BaseColor = baseColor;
            Metallic = metallic;
            Smoothness = smoothness;
        }
    }

    // ------------------------------------------------------------------ 生成

    [MenuItem("Tools/塔防/贴墙梯子/生成预制体")]
    public static void CreateAssets() => CreateAssetsBatch();

    public static void CreateAssetsBatch()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException($"找不到贴墙梯子模型：{ModelPath}");

        EnsureFolder(MaterialDir);
        EnsureFolder("Assets/Prefabs");
        Dictionary<string, Material> materials = EnsureMaterialLibrary();

        var root = new GameObject("WallLadder");
        try
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
            instance.name = "WallLadder_Model";

            StandUpright(instance.transform);
            Bounds bounds = LocalBounds(root.transform, instance);
            if (bounds.size.y < 0.01f || bounds.size.y < bounds.size.x || bounds.size.y < bounds.size.z)
                throw new InvalidOperationException($"模型长轴不是竖直的：size={bounds.size}");

            // 脚底落到 Y = 0、安装面落到 Z = 0、左右居中，玩法根节点因此正好在墙脚。
            visual.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.min.z);

            AssignMaterials(instance, materials);

            Ladder ladder = root.AddComponent<Ladder>();
            var data = new SerializedObject(ladder);
            data.FindProperty("height").floatValue = ModelHeight;
            data.FindProperty("width").floatValue = TriggerWidth;
            data.FindProperty("depth").floatValue = TriggerDepth;
            data.FindProperty("topDismountThreshold").floatValue = TopDismountThreshold;
            // 贴墙梯子顶部要把玩家推向墙内的上层平台，而不是推离墙面（默认的 transform.forward）。
            data.FindProperty("topDismountDirection").vector3Value = new Vector3(0f, 0f, -1f);
            data.ApplyModifiedPropertiesWithoutUndo();

            var trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, ModelHeight * 0.5f, 0f);
            trigger.size = new Vector3(TriggerWidth, ModelHeight, TriggerDepth);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null) throw new InvalidOperationException($"保存预制体失败：{PrefabPath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[WallLadder] 已生成 {PrefabPath}，来源包围盒={bounds.size}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void StandUpright(Transform instance)
    {
        Bounds bounds = WorldBounds(instance.gameObject);
        if (bounds.size.z > bounds.size.y && bounds.size.z > bounds.size.x)
        {
            // 导出轴异常时把长轴转成竖直，避免再次踩到「模型躺着」的老坑。
            instance.localRotation = Quaternion.Euler(-90f, 0f, 0f) * instance.localRotation;
        }
    }

    private static Dictionary<string, Material> EnsureMaterialLibrary()
    {
        var result = new Dictionary<string, Material>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) throw new InvalidOperationException("找不到 URP/Lit 着色器");

        foreach (KeyValuePair<string, MaterialSpec> entry in Palette)
        {
            string path = $"{MaterialDir}/M_{entry.Key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = $"M_{entry.Key}" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != lit)
            {
                material.shader = lit;
            }

            material.SetColor("_BaseColor", entry.Value.BaseColor);
            material.SetFloat("_Metallic", entry.Value.Metallic);
            material.SetFloat("_Smoothness", entry.Value.Smoothness);
            EditorUtility.SetDirty(material);
            result[entry.Key] = material;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return result;
    }

    private static void AssignMaterials(GameObject instance, Dictionary<string, Material> materials)
    {
        var missing = new List<string>();
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                string name = slots[i] != null ? slots[i].name : string.Empty;
                if (materials.TryGetValue(name, out Material replacement))
                {
                    slots[i] = replacement;
                }
                else if (!string.IsNullOrEmpty(name) && !missing.Contains(name))
                {
                    missing.Add(name);
                }
            }
            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        if (missing.Count > 0)
            Debug.LogWarning($"[WallLadder] 以下 FBX 材质没有对应的 URP 材质：{string.Join(", ", missing)}");
    }

    // ------------------------------------------------------------------ 校验

    [MenuItem("Tools/塔防/贴墙梯子/校验预制体")]
    public static void Validate() => ValidateBatch();

    public static void ValidateBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException($"找不到预制体：{PrefabPath}，请先生成");

        var checks = new List<string>();
        int failed = 0;
        void Check(string name, bool ok, string detail)
        {
            checks.Add($"{(ok ? "PASS" : "FAIL")}\t{name}\t{detail}");
            if (!ok) failed++;
            Debug.Log($"[WallLadder][{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        }

        Ladder ladder = prefab.GetComponent<Ladder>();
        var trigger = prefab.GetComponent<BoxCollider>();
        Bounds bounds = LocalBounds(prefab.transform, prefab);

        Check("有 Ladder 组件", ladder != null, ladder == null ? "null" : "ok");
        Check("长轴竖直", bounds.size.y > bounds.size.x && bounds.size.y > bounds.size.z,
            $"size={Fmt(bounds.size)}");
        Check($"高度 {ModelHeight:F2} m", Near(bounds.size.y, ModelHeight, 0.01f), bounds.size.y.ToString("F4"));
        Check($"脚底在 Y=0", Near(bounds.min.y, 0f, 0.01f), bounds.min.y.ToString("F4"));
        Check($"安装面贴墙 Z=0", Near(bounds.min.z, 0f, 0.01f), bounds.min.z.ToString("F4"));
        Check($"贴墙进深 < {MaxDepth:F2} m", bounds.size.z <= MaxDepth, bounds.size.z.ToString("F4"));
        Check($"总宽 {ModelWidth:F2} m", Near(bounds.size.x, ModelWidth, 0.02f), bounds.size.x.ToString("F4"));
        Check("左右居中", Near(bounds.center.x, 0f, 0.01f), bounds.center.x.ToString("F4"));

        if (ladder != null)
        {
            var data = new SerializedObject(ladder);
            Check("攀爬高度与模型一致",
                Near(data.FindProperty("height").floatValue, ModelHeight, 0.01f),
                data.FindProperty("height").floatValue.ToString("F4"));
            Check("顶部下梯朝墙内",
                Vector3.Dot(data.FindProperty("topDismountDirection").vector3Value.normalized, Vector3.back) > 0.99f,
                data.FindProperty("topDismountDirection").vector3Value.ToString("F3"));
        }

        if (trigger != null)
        {
            Check("触发区是 trigger", trigger.isTrigger, trigger.isTrigger.ToString());
            Check("触发区覆盖攀爬高度",
                Near(trigger.center.y - trigger.size.y * 0.5f, 0f, 0.01f) &&
                Near(trigger.center.y + trigger.size.y * 0.5f, ModelHeight, 0.01f),
                $"center={trigger.center.ToString("F3")} size={trigger.size.ToString("F3")}");
            Check("触发区宽于模型", trigger.size.x > bounds.size.x, trigger.size.x.ToString("F3"));
        }

        Check("只有一个碰撞体且无需物理体",
            prefab.GetComponentsInChildren<Collider>().Length == 1 && prefab.GetComponent<Rigidbody>() == null,
            $"colliders={prefab.GetComponentsInChildren<Collider>().Length}");

        var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
        Check("有网格渲染器", renderers.Length > 0, renderers.Length.ToString());

        int nonUrp = 0;
        foreach (MeshRenderer renderer in renderers)
            foreach (Material material in renderer.sharedMaterials)
                if (material == null || material.shader == null ||
                    !material.shader.name.Contains("Universal Render Pipeline"))
                    nonUrp++;
        Check("材质全部为 URP", nonUrp == 0, $"非 URP 槽位={nonUrp}");

        string report = string.Join(Environment.NewLine, checks) +
                        $"{Environment.NewLine}结果: {(failed == 0 ? "PASS" : "FAIL")} " +
                        $"{checks.Count - failed}/{checks.Count}";
        string output = PreviewRoot("validation");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "validation.txt"), report, Encoding.UTF8);

        if (failed > 0) throw new InvalidOperationException($"贴墙梯子校验失败 {failed}/{checks.Count}：{output}");
        Debug.Log($"[WallLadder] 校验通过 {checks.Count}/{checks.Count}：{output}");
    }

    // ------------------------------------------------------------------ 预览

    [MenuItem("Tools/塔防/贴墙梯子/渲染预览")]
    public static void RenderPreview() => RenderPreviewBatch();

    public static void RenderPreviewBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException($"找不到预制体：{PrefabPath}，请先生成");

        string output = PreviewRoot("preview");
        Directory.CreateDirectory(output);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cleanup = new List<Object>();
        Material wallMaterial = MakeMaterial(new Color(0.32f, 0.32f, 0.31f), 0f, 0.15f);
        Material floorMaterial = MakeMaterial(new Color(0.17f, 0.18f, 0.19f), 0f, 0.20f);
        cleanup.Add(wallMaterial);
        cleanup.Add(floorMaterial);

        // 墙面正面正好在 Z = 0，与梯子的安装面共面，用来检查「是否真的贴墙」。
        // 墙体必须向 -Z 延伸：梯身朝 +Z 伸出，墙在它背后。
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Preview Wall";
        wall.transform.localScale = new Vector3(8f, 4.2f, 0.4f);
        wall.transform.position = new Vector3(0f, 2.1f, -0.2f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
        Object.DestroyImmediate(wall.GetComponent<Collider>());

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Preview Floor";
        floor.transform.localScale = new Vector3(8f, 0.1f, 8f);
        floor.transform.position = new Vector3(0f, -0.05f, 4f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var lightObject = new GameObject("Preview Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.96f, 0.90f);
        // 主光必须从玩家所站的 +Z 一侧照过来，否则梯子正面永远是背光的。
        lightObject.transform.rotation = Quaternion.LookRotation(new Vector3(-0.45f, -0.70f, -1f));

        var fillObject = new GameObject("Preview Fill");
        var fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.6f;
        fill.color = new Color(0.72f, 0.80f, 0.95f);
        fillObject.transform.rotation = Quaternion.LookRotation(new Vector3(0.75f, -0.25f, -0.6f));

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.52f);
        RenderSettings.ambientIntensity = 1f;

        var cameraObject = new GameObject("Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.12f, 0.15f);
        camera.fieldOfView = 42f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200f;
        camera.allowHDR = false;

        try
        {
            // 梯身朝 +Z 伸出，玩家也站在 +Z 一侧，所以相机必须在正 Z 方向回看。
            RenderView(camera, "01_front", new Vector3(0f, 1.55f, 7.4f), new Vector3(0f, 1.60f, 0f), output);
            RenderView(camera, "02_three_quarter", new Vector3(-3.6f, 2.5f, 6.2f), new Vector3(0f, 1.55f, 0f), output);
            RenderView(camera, "03_side_against_wall", new Vector3(6.4f, 1.7f, 2.4f), new Vector3(0f, 1.60f, 0.02f), output);
            RenderView(camera, "04_top_plate_closeup", new Vector3(-0.85f, 3.15f, 1.25f), new Vector3(0f, 3.02f, 0f), output,
                fieldOfView: 34f);

            Debug.Log($"[WallLadder] 预览已输出：{output}");
        }
        finally
        {
            foreach (Object item in cleanup) Object.DestroyImmediate(item);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(fillObject);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(instance);
        }
    }

    private static void RenderView(Camera camera, string name, Vector3 position, Vector3 lookAt,
        string output, float fieldOfView = 42f)
    {
        const int width = 1280;
        const int height = 900;

        camera.fieldOfView = fieldOfView;
        camera.transform.position = position;
        camera.transform.LookAt(lookAt);

        var target = new RenderTexture(width, height, 24);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(output, $"{name}.png"), texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture);
        }
    }

    // ------------------------------------------------------------------ 工具

    private static string PreviewRoot(string kind) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "WallLadder", $"unity-{kind}",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    private static Material MakeMaterial(Color color, float metallic, float smoothness)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static bool Near(float a, float b, float tolerance) => Mathf.Abs(a - b) <= tolerance;

    private static string Fmt(Vector3 value) =>
        string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", value.x, value.y, value.z);

    /// <summary>把 <paramref name="target"/> 的渲染器包围盒换算到 <paramref name="root"/> 的本地空间。</summary>
    public static Bounds LocalBounds(Transform root, GameObject target)
    {
        Bounds world = WorldBounds(target);
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? world.min.x : world.max.x,
                (i & 2) == 0 ? world.min.y : world.max.y,
                (i & 4) == 0 ? world.min.z : world.max.z);
            Vector3 local = root.InverseTransformPoint(corner);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    public static Bounds WorldBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException($"{target.name} 没有渲染器");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}