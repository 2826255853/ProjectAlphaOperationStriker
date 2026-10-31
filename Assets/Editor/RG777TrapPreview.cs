using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RG777「雷锤」电磁炮的批处理视觉验证：
///   1. 结构：XM777_Base_Root / XM777_Turret_Root / XM777_Pitch_Root / Muzzle 是否都在；
///      炮管顶脊上 8 段 ChargeSegment_0..7 是否都在（充能灯带的数据源）。
///   2. 材质：每个 Renderer 的材质是否都换成了 URP 着色器（没有残留内置 Standard，否则是品红）。
///   3. 出图：轴测 / 侧视 / 炮口 / 俯视四张 PNG，供肉眼确认配色与灯带。
///
/// 菜单：Tools/塔防/预览 RG777 电磁炮
/// 批处理：RG777TrapPreview.RenderPreviewBatch -WithGraphics
/// </summary>
public static class RG777TrapPreview
{
    private const int Width = 1280;
    private const int Height = 960;
    private const float FieldOfView = 34f;   // 约等于 Blender 里 44mm 镜头在 4:3 画幅下的竖向 FOV
    private const string OutputDirectory = "C:/Users/ASUS/Desktop/陷阱素材文件夹/RG777/integration/preview";

    private sealed class Shot
    {
        public string Name;
        public Vector3 CameraPosition;
        public Vector3 LookAt;
    }

    private static readonly Shot[] Shots =
    {
        new Shot { Name = "01-main",  CameraPosition = new Vector3(5.9f, 3.5f, -6.5f), LookAt = new Vector3(0f, 1.40f, 0.35f) },
        new Shot { Name = "02-side",  CameraPosition = new Vector3(8.8f, 2.2f, 0.6f),  LookAt = new Vector3(0f, 1.55f, 0.35f) },
        new Shot { Name = "03-muzzle", CameraPosition = new Vector3(2.9f, 2.9f, 7.6f), LookAt = new Vector3(0f, 1.75f, 0.35f) },
        new Shot { Name = "04-top",   CameraPosition = new Vector3(0f, 9.0f, -0.4f),   LookAt = new Vector3(0f, 0.35f, 0.20f) },
    };

    [MenuItem("Tools/塔防/预览 RG777 电磁炮")]
    public static void RenderPreview()
    {
        string outputDirectory = OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RG777BranchAuthoring.VisualPrefabPath);
        if (model == null)
            model = AssetDatabase.LoadAssetAtPath<GameObject>(RG777MaterialAuthoring.ModelPath);
        if (model == null)
        {
            Debug.LogError("找不到模型：" + RG777MaterialAuthoring.ModelPath);
            return;
        }

        var root = (GameObject)Object.Instantiate(model);
        root.transform.position = Vector3.zero;
        var cameraObject = new GameObject("Preview Camera");
        var lightObject = new GameObject("Preview Light");
        var fillObject = new GameObject("Preview Fill");
        RenderTexture renderTexture = null;
        try
        {
            bool structureOk = CheckStructure(root);
            bool materialsOk = CheckMaterials(root);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = FieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.23f, 0.28f);

            // 批处理渲染时场景会继承天空盒环境光，把配色洗成一片浅灰；
            // 这里锁定为弱平面环境光，让白/灰/蓝的对比由主光和自发光自己讲。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.13f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            lightObject.transform.rotation = Quaternion.Euler(45f, 35f, 0f);

            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.30f;
            fillObject.transform.rotation = Quaternion.Euler(20f, -140f, 0f);

            renderTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = renderTexture;

            foreach (Shot shot in Shots)
            {
                camera.transform.position = shot.CameraPosition;
                camera.transform.LookAt(shot.LookAt);

                camera.Render();
                RenderTexture.active = renderTexture;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(outputDirectory, shot.Name + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = null;
            }

            Debug.Log("预览图已输出：" + outputDirectory);
            if (structureOk && materialsOk)
                Debug.Log("RG777_PREVIEW_PASS: pivots, charge segments and URP materials are all in place; "
                          + Shots.Length + " preview images written to " + outputDirectory + ".");
            else
                Debug.LogError("RG777_PREVIEW_FAIL: structure=" + structureOk + " materials=" + materialsOk);
        }
        finally
        {
            if (renderTexture != null)
            {
                if (cameraObject.GetComponent<Camera>() != null)
                    cameraObject.GetComponent<Camera>().targetTexture = null;
                Object.DestroyImmediate(renderTexture);
            }
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(fillObject);
        }
    }

    public static void RenderPreviewBatch()
    {
        RenderPreview();
    }

    private static bool CheckStructure(GameObject root)
    {
        string[] pivots = { "XM777_Base_Root", "XM777_Turret_Root", "XM777_Pitch_Root", "Muzzle" };
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        bool ok = true;
        foreach (string pivot in pivots)
        {
            bool found = all.Any(t => t.name == pivot);
            if (!found)
            {
                ok = false;
                Debug.LogError("[RG777] 缺少枢轴：" + pivot);
            }
        }

        int segments = all.Count(t => t.name.StartsWith("ChargeSegment_"));
        int coils = all.Count(t => t.name.EndsWith("_Ring") && t.name.StartsWith("Coil_"));
        Debug.Log("[RG777] 结构：枢轴齐备=" + ok + "，充能段=" + segments + "，加速线圈=" + coils);
        if (segments != 8)
        {
            ok = false;
            Debug.LogError("[RG777] 充能灯带应为 8 段，实际 " + segments + " 段。");
        }
        return ok;
    }

    private static bool CheckMaterials(GameObject root)
    {
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        int urp = 0;
        int nonUrp = 0;
        var offenders = new System.Collections.Generic.HashSet<string>();
        foreach (MeshRenderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null)
                {
                    nonUrp++;
                    offenders.Add("<null>");
                    continue;
                }
                if (material.shader.name.StartsWith("Universal Render Pipeline"))
                    urp++;
                else
                {
                    nonUrp++;
                    offenders.Add(material.shader.name + " / " + material.name);
                }
            }
        }

        Debug.Log("[RG777] 材质：URP 槽位=" + urp + "，非 URP 槽位=" + nonUrp
                  + (nonUrp > 0 ? "（" + string.Join(", ", offenders) + "）" : ""));
        ProbeKeyParts(root);
        return nonUrp == 0 && renderers.Length > 0;
    }

    /// <summary>打印关键部件实际绑定的材质与 BaseColor，用于肉眼核对配色是否真的生效。</summary>
    private static void ProbeKeyParts(GameObject root)
    {
        string[] probes = { "Base_Chassis", "Base_TurnRingGlow", "Yaw_Deck", "Coil_0_Ring",
                            "Coil_0_Glow", "ChargeSegment_0", "ChargeSegment_7", "Barrel_Housing" };
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (string probe in probes)
        {
            Transform found = System.Array.Find(all, t => t.name == probe);
            if (found == null)
            {
                Debug.LogWarning("[RG777] 探针找不到对象：" + probe);
                continue;
            }
            var renderer = found.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning("[RG777] " + probe + " 上没有 MeshRenderer。");
                continue;
            }
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
                Color emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.clear;
                Debug.Log("[RG777] 探针 " + probe + " → " + material.name + " (shader=" + material.shader.name
                          + ", base=" + baseColor + ", emission=" + emission + ")");
            }
        }
    }
}
