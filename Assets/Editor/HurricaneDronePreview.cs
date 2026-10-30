using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 飓风无人机预制体预览渲染：在空场景里实例化预制体，用离屏相机出图。
///
/// 目的有两个：
///   1. 确认 FBX 导入后的 URP 材质不是品红（材质重映射成功）；
///   2. 确认骨骼动画真的在动（同一相机位置渲染不同动画状态）。
///
/// 菜单：Tools/塔防/预览飓风无人机
/// 批处理：HurricaneDronePreview.RenderPreviewBatch
/// 输出：%TEMP%\alpha-striker-unity\hurricane-drone-preview
/// 需要留档的图按规范复制到桌面「陷阱素材文件夹\HurricaneDrone」。
/// </summary>
public static class HurricaneDronePreview
{
    const int Size = 1024;

    static string OutputDir =>
        Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "hurricane-drone-preview");

    [MenuItem("Tools/塔防/预览飓风无人机")]
    public static void RenderPreview() => RenderPreviewBatch();

    public static void RenderPreviewBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HurricaneDroneAuthoring.PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[HurricaneDrone] 预制体不存在，请先运行 HurricaneDroneAuthoring.CreateAll：{HurricaneDroneAuthoring.PrefabPath}");
            return;
        }

        Directory.CreateDirectory(OutputDir);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;

        // 离屏渲染时蒙皮网格容易被视锥剔除/不更新，强制每帧重算包围盒并 CPU 蒙皮
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            smr.enabled = true;
            var m = smr.sharedMesh;
            Debug.Log($"[HurricaneDrone] 渲染器 {smr.name}: mesh={(m != null ? m.name : "null")} " +
                      $"verts={(m != null ? m.vertexCount : 0)} bounds={smr.bounds.size} " +
                      $"materials={smr.sharedMaterials.Length} layer={smr.gameObject.layer}");
            foreach (var mat in smr.sharedMaterials)
            {
                string shaderName = mat != null ? mat.shader.name : "null";
                bool bad = mat == null || shaderName.Contains("InternalErrorShader");
                if (bad)
                {
                    Debug.LogError($"[HurricaneDrone] 材质丢失或为错误着色器：{shaderName}");
                }
            }
        }

        var camGo = new GameObject("HurricanePreviewCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.58f, 0.60f, 0.64f, 1f);
        cam.fieldOfView = 42f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;

        var keyGo = new GameObject("PreviewKeyLight");
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.6f;
        key.color = new Color(1f, 0.97f, 0.92f);
        keyGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

        var fillGo = new GameObject("PreviewFillLight");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.6f;
        fill.color = new Color(0.75f, 0.82f, 1f);
        fillGo.transform.rotation = Quaternion.Euler(12f, 150f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;

        var animator = instance.GetComponent<Animator>();

        // 三个机位：3/4 俯视、正侧、特写
        var views = new (string name, Vector3 position, Vector3 lookAt, float fov)[]
        {
            ("persp", new Vector3(8.5f, 7.0f, -11.5f), new Vector3(0f, 0f, 0f), 40f),
            ("side", new Vector3(17f, 0.8f, 0f), new Vector3(0f, -0.1f, 0f), 26f),
            ("top", new Vector3(0.01f, 17f, 0f), new Vector3(0f, 0f, 0f), 26f),
            ("gun", new Vector3(4.5f, -3.4f, -6.5f), new Vector3(0f, -2.6f, 1.6f), 40f),
        };

        foreach (var view in views)
        {
            Pose(animator, HurricaneDroneUnit.StateHover, 0.6f);
            Render(cam, view.position, view.lookAt, view.fov, $"{OutputDir}/{view.name}.png");
        }

        // 攻击动画的两个关键帧（含后坐）
        Pose(animator, HurricaneDroneUnit.StateAttack, 0.9f);
        Render(cam, new Vector3(7.0f, 3.0f, -9.5f), new Vector3(0f, -1.2f, 0.6f), 38f,
               $"{OutputDir}/attack_aim.png");
        Pose(animator, HurricaneDroneUnit.StateAttack, 1.55f);
        Render(cam, new Vector3(4.5f, -3.4f, -6.5f), new Vector3(0f, -2.6f, 1.6f), 40f,
               $"{OutputDir}/attack_recoil.png");

        ReportClips();

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(instance);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(keyGo);
        Object.DestroyImmediate(fillGo);
        Debug.Log($"[HurricaneDrone] 预览图输出目录：{OutputDir}");
    }

    static void Pose(Animator animator, string stateName, float seconds)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            return;
        }
        animator.Play(hash, 0, 0f);
        animator.Update(0f);
        // 分批推进，避免采样跨度过大
        float t = 0f;
        while (t < seconds)
        {
            animator.Update(1f / 30f);
            t += 1f / 30f;
        }
    }

    static void Render(Camera cam, Vector3 position, Vector3 lookAt, float fov, string path)
    {
        cam.transform.position = position;
        cam.transform.LookAt(lookAt);
        cam.fieldOfView = fov;

        // URP 下直接 Camera.Render() 在 batchmode 里只出背景色（实测），
        // 正确做法是走 SRP 的 SubmitRenderRequest；不支持时再退回 Camera.Render()。
        var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
        }
        else
        {
            cam.Render();
        }

        var previous = RenderTexture.active;
        RenderTexture.active = cam.targetTexture;
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, tex.EncodeToPNG());

        int covered = CountNonBackground(tex);
        if (covered < Size * Size / 400)
        {
            Debug.LogWarning($"[HurricaneDrone] 预览图基本是空的（非背景像素 {covered}）：{path}");
        }
        else
        {
            Debug.Log($"[HurricaneDrone] 预览图：{path}（非背景像素 {covered}）");
        }
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// 用「画面四角」当作背景基准来数差异像素。
    /// 注意不能拿 cam.backgroundColor 直接比：渲染结果经过色彩空间转换，数值对不上。
    /// </summary>
    static int CountNonBackground(Texture2D tex)
    {
        Color bg = tex.GetPixel(2, 2);
        int count = 0;
        const int step = 4;
        for (int y = 0; y < Size; y += step)
        {
            for (int x = 0; x < Size; x += step)
            {
                Color c = tex.GetPixel(x, y);
                if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 0.05f)
                {
                    count++;
                }
            }
        }
        return count * step * step;
    }

    static void ReportClips()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HurricaneDroneAuthoring.ModelPath);
        if (model == null)
        {
            return;
        }
        var sb = new System.Text.StringBuilder("[HurricaneDrone] 动画片段\n");
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(HurricaneDroneAuthoring.ModelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                // HD_Attack 曾出现 length=5.34s（应为 2.97s），这里把真实曲线末端也打出来定位
                float lastKey = 0f;
                int curveCount = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.length > 0)
                    {
                        curveCount++;
                        lastKey = Mathf.Max(lastKey, curve.keys[curve.length - 1].time);
                    }
                }
                sb.AppendLine($"  {clip.name}: length={clip.length:F2}s, 曲线={curveCount}, 末关键帧={lastKey:F2}s, " +
                              $"sampleRate={clip.frameRate}, loop={clip.isLooping}, events={clip.events.Length}");
            }
        }
        Debug.Log(sb.ToString());
    }
}
