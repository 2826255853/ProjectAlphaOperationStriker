using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 飓风无人机：导入设置 / URP 材质 / 动画控制器 / 预制体 的生成入口。
///
/// 模型与动画由桌面素材目录的 Blender 脚本产出：
///   C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\build_drone_v3.py
/// 该脚本重新导出后，本工具可重复运行（幂等）：材质按名字补齐、导入设置按名字回填、
/// 预制体整体重建。手工改预制体会在下次重建时丢失，要改请改这里。
///
/// 菜单：Tools/塔防/生成飓风无人机预制体
/// 批处理：HurricaneDroneAuthoring.CreateAll
/// </summary>
public static class HurricaneDroneAuthoring
{
    public const string ModelPath = "Assets/Models/HurricaneDrone/HurricaneDrone.fbx";
    public const string MaterialDir = "Assets/Models/HurricaneDrone/Materials";
    public const string AnimDir = "Assets/Animations/HurricaneDrone";
    public const string ControllerPath = AnimDir + "/HurricaneDrone.controller";
    public const string PrefabPath = "Assets/Prefabs/HurricaneDrone.prefab";

    /// <summary>炮口在 Unity 坐标系下的本地位置（Blender 里机头朝 -Y，导出后朝 +Z）。</summary>
    public static readonly Vector3 MuzzleLocalPosition = new Vector3(0f, -1.18f, 2.42f);

    public const string MuzzleObjectName = "Muzzle";

    // 循环播放的空中位移类动画；攻击是单次播放。
    static readonly string[] LoopClips = { "HD_Hover", "HD_FlyForward", "HD_BankTurn" };
    static readonly string[] AllClips = { "HD_Hover", "HD_FlyForward", "HD_BankTurn", "HD_Attack" };

    // 攻击动画里 6 次开火的帧号（30 fps）。这里的值必须与 Blender 脚本 FIRE_FRAMES 一致。
    static readonly int[] FireFrames = { 22, 26, 30, 46, 50, 54 };
    const float ClipFps = 30f;

    struct MatDef
    {
        public string Name;
        public Color BaseColor;
        public float Metallic;
        public float Smoothness;
        public Color Emission;
        public float EmissionIntensity;

        public MatDef(string name, Color baseColor, float metallic, float smoothness,
                      Color emission = default, float emissionIntensity = 0f)
        {
            Name = name;
            BaseColor = baseColor;
            Metallic = metallic;
            Smoothness = smoothness;
            Emission = emission;
            EmissionIntensity = emissionIntensity;
        }
    }

    static readonly MatDef[] Materials =
    {
        new MatDef("M_HD_WhiteShell", new Color(0.86f, 0.86f, 0.85f), 0.10f, 0.42f),
        new MatDef("M_HD_DarkMetal", new Color(0.075f, 0.082f, 0.095f), 0.85f, 0.55f),
        new MatDef("M_HD_BlackCarbon", new Color(0.025f, 0.028f, 0.035f), 0.30f, 0.58f),
        new MatDef("M_HD_Steel", new Color(0.46f, 0.48f, 0.51f), 0.90f, 0.72f),
        new MatDef("M_HD_OrdnanceBrass", new Color(0.62f, 0.44f, 0.14f), 0.85f, 0.58f),
        new MatDef("M_HD_CoreGlow", new Color(0.10f, 0.38f, 1.0f), 0.0f, 0.55f,
                   new Color(0.16f, 0.45f, 1.0f), 6.0f),
        new MatDef("M_HD_LensGlass", new Color(0.02f, 0.05f, 0.18f), 0.30f, 0.92f),
        new MatDef("M_HD_MidGrey", new Color(0.34f, 0.35f, 0.37f), 0.55f, 0.60f),
    };

    [MenuItem("Tools/塔防/生成飓风无人机预制体")]
    public static void CreateAllFromMenu()
    {
        CreateAll();
    }

    /// <summary>批处理入口：材质 + 导入设置 + 控制器 + 预制体。</summary>
    public static void CreateAll()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            // 改过 FBX 后先刷新一次，保证能取到资源
            AssetDatabase.Refresh();
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            Debug.LogError($"[HurricaneDrone] 找不到模型：{ModelPath}");
            return;
        }

        var materials = EnsureMaterials();
        ConfigureImporter(materials);
        var clips = LoadClips();
        if (clips.Count == 0)
        {
            Debug.LogError("[HurricaneDrone] 没有解析到 HD_* 动画片段，请检查 FBX 的 NLA 轨道名。");
            return;
        }

        var controller = BuildController(clips);
        var prefab = BuildPrefab(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HurricaneDrone] 完成：材质 {materials.Count} 个，动画 {clips.Count} 段，预制体 {PrefabPath}");
    }

    // ---------------------------------------------------------------- 材质
    static List<Material> EnsureMaterials()
    {
        Directory.CreateDirectory(MaterialDir);
        var list = new List<Material>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        foreach (var def in Materials)
        {
            string path = $"{MaterialDir}/{def.Name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.name = def.Name;
            mat.SetColor("_BaseColor", def.BaseColor);
            mat.SetColor("_Color", def.BaseColor);
            mat.SetFloat("_Metallic", def.Metallic);
            mat.SetFloat("_Smoothness", def.Smoothness);
            mat.SetFloat("_Glossiness", def.Smoothness);

            if (def.EmissionIntensity > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", def.Emission * def.EmissionIntensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            list.Add(mat);
        }
        return list;
    }

    // ------------------------------------------------------------ 导入设置
    static void ConfigureImporter(List<Material> materials)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("[HurricaneDrone] 取不到 ModelImporter，跳过导入设置。");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.optimizeMeshPolygons = true;
        importer.optimizeMeshVertices = true;

        // 材质名 -> 工程内 .mat 的重映射（FBX 里带的是 Blender 内置材质）
        var byName = new Dictionary<string, Material>();
        foreach (var m in materials)
        {
            byName[m.name] = m;
        }
        foreach (var pair in byName)
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
        }

        // 片段循环设置 + 开火动画事件
        var clipSettings = importer.clipAnimations;
        if (clipSettings == null || clipSettings.Length == 0)
        {
            clipSettings = importer.defaultClipAnimations;
        }
        for (int i = 0; i < clipSettings.Length; i++)
        {
            var c = clipSettings[i];
            bool loop = IsLoopClip(c.name);
            c.loopTime = loop;
            c.wrapMode = loop ? WrapMode.Loop : WrapMode.Clamp;
            // 显式裁到设计好的帧区间：Blender 的 NLA 烘焙偶尔会给出多余尾巴
            // （HD_Attack 实测导成过 5.34s，正确值是 90 帧 = 2.97s），这里按名字兜底。
            c.firstFrame = 0;
            c.lastFrame = LastFrameFor(c.name);
            c.events = c.name != null && c.name.Contains("Attack") ? BuildFireEvents() : new AnimationEvent[0];
            clipSettings[i] = c;
        }
        importer.clipAnimations = clipSettings;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    /// <summary>每段动画应当在 FBX 里裁到的最后帧（30 fps，含首帧）。</summary>
    static int LastFrameFor(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
        {
            return 0;
        }
        if (clipName.Contains("HD_Attack")) return 89;      // 90 帧 = 2.97 s
        if (clipName.Contains("HD_Hover")) return 59;       // 60 帧 = 1.97 s
        if (clipName.Contains("HD_FlyForward")) return 39;  // 40 帧 = 1.30 s
        if (clipName.Contains("HD_BankTurn")) return 47;    // 48 帧 = 1.57 s
        return 0;
    }

    static bool IsLoopClip(string clipName)    {
        if (string.IsNullOrEmpty(clipName))
        {
            return false;
        }
        foreach (var name in LoopClips)
        {
            if (clipName.Contains(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 开火时机做成动画事件（函数名 OnGunFire），由 HurricaneDroneUnit 接收。
    /// 导入的 FBX 片段不能直接 AnimationUtility.SetAnimationEvents，所以事件写在导入设置里。
    /// </summary>
    static AnimationEvent[] BuildFireEvents()
    {
        var events = new AnimationEvent[FireFrames.Length];
        for (int i = 0; i < FireFrames.Length; i++)
        {
            events[i] = new AnimationEvent
            {
                functionName = HurricaneDroneUnit.GunFireEventName,
                time = FireFrames[i] / ClipFps,
                messageOptions = SendMessageOptions.DontRequireReceiver
            };
        }
        return events;
    }

    // ------------------------------------------------------------ 动画片段
    static List<AnimationClip> LoadClips()
    {
        var found = new List<AnimationClip>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            var clip = asset as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__"))
            {
                continue;
            }
            found.Add(clip);
        }
        // 按固定顺序排列，方便控制器状态命名
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found;
    }

    static AnimationClip FindClip(List<AnimationClip> clips, string key)
    {
        foreach (var clip in clips)
        {
            if (clip.name.Contains(key))
            {
                return clip;
            }
        }
        return null;
    }

    // ------------------------------------------------------- 动画控制器
    static AnimatorController BuildController(List<AnimationClip> clips)
    {
        Directory.CreateDirectory(AnimDir);
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;

        var entries = new (string key, string state, float speed)[]
        {
            ("HD_Hover", HurricaneDroneUnit.StateHover, 1f),
            ("HD_FlyForward", HurricaneDroneUnit.StateFlyForward, 1f),
            ("HD_BankTurn", HurricaneDroneUnit.StateBankTurn, 1f),
            ("HD_Attack", HurricaneDroneUnit.StateAttack, 1f),
        };

        AnimatorState defaultState = null;
        foreach (var entry in entries)
        {
            var clip = FindClip(clips, entry.key);
            if (clip == null)
            {
                Debug.LogWarning($"[HurricaneDrone] 缺少动画片段 {entry.key}");
                continue;
            }
            var state = machine.AddState(entry.state);
            state.motion = clip;
            state.speed = entry.speed;
            state.writeDefaultValues = true;
            if (defaultState == null)
            {
                defaultState = state;
            }
        }
        if (defaultState != null)
        {
            machine.defaultState = defaultState;
        }
        return controller;
    }

    // ------------------------------------------------------------ 预制体
    static GameObject BuildPrefab(AnimatorController controller)
    {
        Directory.CreateDirectory("Assets/Prefabs");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "HurricaneDrone";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        var animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 炮口挂点：挂在 Gun_Pitch 骨骼下，随俯仰一起动
        var muzzle = EnsureMuzzle(instance);

        var unit = instance.GetComponent<HurricaneDroneUnit>();
        if (unit == null)
        {
            unit = instance.AddComponent<HurricaneDroneUnit>();
        }
        unit.animator = animator;
        unit.muzzle = muzzle;

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    static Transform EnsureMuzzle(GameObject instance)
    {
        Transform gunPitch = null;
        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Gun_Pitch")
            {
                gunPitch = t;
                break;
            }
        }

        var existing = instance.transform.Find(MuzzleObjectName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        var go = new GameObject(MuzzleObjectName);
        var muzzle = go.transform;
        if (gunPitch != null)
        {
            muzzle.SetParent(gunPitch, false);
        }
        else
        {
            // 骨骼名找不到时退回挂在根节点，位置仍然正确（模型根在原点）
            Debug.LogWarning("[HurricaneDrone] 找不到 Gun_Pitch 骨骼，炮口挂到根节点。");
            muzzle.SetParent(instance.transform, false);
        }
        // 局部位置：相对 Gun_Pitch 骨骼原点
        muzzle.localPosition = gunPitch != null
            ? gunPitch.InverseTransformPoint(new Vector3(MuzzleLocalPosition.x, MuzzleLocalPosition.y, MuzzleLocalPosition.z))
            : MuzzleLocalPosition;
        muzzle.localRotation = Quaternion.identity;
        muzzle.localScale = Vector3.one;
        return muzzle;
    }

    /// <summary>探针：输出预制体的层级、骨骼、动画片段与朝向，供人工核对。</summary>
    [MenuItem("Tools/塔防/校验飓风无人机预制体")]
    public static void ProbePrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[HurricaneDrone] 预制体不存在：{PrefabPath}");
            return;
        }
        var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var muzzle = prefab.GetComponentInChildren<Transform>(true) != null ? FindDeep(prefab.transform, MuzzleObjectName) : null;
        var animator = prefab.GetComponent<Animator>();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[HurricaneDrone] 预制体校验");
        sb.AppendLine($"  蒙皮网格: {renderers.Length}，材质槽: {(renderers.Length > 0 ? renderers[0].sharedMaterials.Length : 0)}");
        sb.AppendLine($"  Animator: {(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "缺失")}");
        sb.AppendLine($"  炮口: {(muzzle != null ? muzzle.name + " @" + muzzle.position : "缺失")}");
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
        sb.AppendLine($"  包围盒尺寸: {bounds.size}（设计图 7.60 x 7.60 x 2.40，注意 Unity 的 Y/Z 与 Blender 互换）");
        foreach (var bone in new[] { "Root", "Body", "Fan_FL", "Fan_FR", "Fan_BL", "Fan_BR", "Gun_Yaw", "Gun_Pitch" })
        {
            sb.AppendLine($"  骨骼 {bone}: {(FindDeep(prefab.transform, bone) != null ? "OK" : "缺失")}");
        }
        Debug.Log(sb.ToString());
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }
        return null;
    }

    public static void ProbePrefabBatch()
    {
        ProbePrefab();
    }
}
