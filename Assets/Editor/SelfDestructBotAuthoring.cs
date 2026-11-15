using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 自爆机器人：导入设置 / URP 材质 / 动画控制器 / 怪物预制体 的生成入口。
///
/// 模型与动画由桌面素材目录的 Blender 脚本产出：
///   C:\Users\ASUS\Desktop\陷阱素材文件夹\SelfDestructBot\build_selfdestruct_bot.py
/// 重新导出 FBX 后可以重复运行本工具（幂等）：材质按名字补齐、导入设置按名字回填、预制体整体重建。
/// <b>手工改预制体会在下次重建时丢失</b>，要改请改这里或改 Blender 脚本。
///
/// 菜单：Tools/塔防/生成自爆机器人预制体
/// 批处理：SelfDestructBotAuthoring.CreateAll
/// 校验：SelfDestructBotAuthoring.ProbeBatch → SELF_DESTRUCT_BOT_PASS / ..._FAIL
/// </summary>
public static class SelfDestructBotAuthoring
{
    public const string ModelPath = "Assets/Models/SelfDestructBot/SelfDestructBot.fbx";
    public const string MaterialDir = "Assets/Models/SelfDestructBot/Materials";
    public const string AnimDir = "Assets/Animations/SelfDestructBot";
    public const string ControllerPath = AnimDir + "/SelfDestructBot.controller";

    /// <summary>
    /// 可接入出怪口的地面怪物预制体：表现 + EnemyHealth + MonsterPathFollower(地面) +
    /// SelfDestructBotUnit(动画) + SelfDestructBotEnemy(挥臂/自爆)。
    /// 放在 Resources 下，出怪口没手填预制体时也能被 Resources.Load 取到。
    /// </summary>
    public const string EnemyPrefabPath = "Assets/Resources/SelfDestructBotEnemy.prefab";
    public const string EnemyPrefabResourceName = "SelfDestructBotEnemy";

    /// <summary>血量：比小怪硬，但远低于飞行怪物（无人机 260）。</summary>
    public const float EnemyMaxHealth = 130f;
    /// <summary>移动速度：负重炸弹的双足机器人，比普通小怪慢。</summary>
    public const float EnemyMoveSpeed = 1.8f;

    public const string HandAnchorName = "SDB_Hand";
    public const string BombAnchorName = "SDB_Bomb";

    const float ClipFps = 30f;

    // 循环片段（攻击是单次播放）
    static readonly string[] LoopClips = { "SDB_Idle", "SDB_Walk", "SDB_Prime" };

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

    /// <summary>
    /// 与 Blender 脚本里的 M_SDB_* 一一对应（导入时按材质名重映射）。
    /// 金属度一律 ≤ 0.55：地图大多是室内且没有反射探针，高金属度会让部件发黑。
    /// </summary>
    static readonly MatDef[] Materials =
    {
        new MatDef("M_SDB_Plate", new Color(0.265f, 0.275f, 0.285f), 0.45f, 0.50f),
        new MatDef("M_SDB_PlateDark", new Color(0.128f, 0.134f, 0.145f), 0.42f, 0.55f),
        new MatDef("M_SDB_DarkMetal", new Color(0.055f, 0.060f, 0.068f), 0.55f, 0.50f),
        new MatDef("M_SDB_Steel", new Color(0.340f, 0.352f, 0.368f), 0.55f, 0.66f),
        new MatDef("M_SDB_Joint", new Color(0.038f, 0.040f, 0.044f), 0.10f, 0.20f),
        new MatDef("M_SDB_Cable", new Color(0.048f, 0.049f, 0.053f), 0.05f, 0.16f),
        new MatDef("M_SDB_Copper", new Color(0.380f, 0.220f, 0.110f), 0.55f, 0.55f),
        new MatDef("M_SDB_WarnYellow", new Color(0.560f, 0.375f, 0.042f), 0.08f, 0.38f),
        new MatDef("M_SDB_WarnRed", new Color(0.420f, 0.055f, 0.040f), 0.08f, 0.38f),
        new MatDef("M_SDB_BombShell", new Color(0.148f, 0.163f, 0.120f), 0.38f, 0.36f),
        new MatDef("M_SDB_EyeGlow", new Color(0.08f, 0.62f, 0.80f), 0.0f, 0.35f,
                   new Color(0.16f, 0.75f, 1.0f), 5.0f),
        new MatDef("M_SDB_BombGlow", new Color(0.85f, 0.24f, 0.05f), 0.0f, 0.35f,
                   new Color(1.0f, 0.30f, 0.04f), 7.0f),
        new MatDef("M_SDB_Screen", new Color(0.05f, 0.22f, 0.10f), 0.0f, 0.40f,
                   new Color(0.20f, 0.95f, 0.35f), 3.0f),
    };

    [MenuItem("Tools/塔防/生成自爆机器人预制体")]
    public static void CreateAllFromMenu()
    {
        CreateAll();
    }

    /// <summary>批处理入口：材质 + 导入设置 + 控制器 + 怪物预制体。</summary>
    public static void CreateAll()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            AssetDatabase.Refresh();
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            Debug.LogError($"[SelfDestructBot] 找不到模型：{ModelPath}");
            return;
        }

        var materials = EnsureMaterials();
        ConfigureImporter(materials);
        var clips = LoadClips();
        if (clips.Count == 0)
        {
            Debug.LogError("[SelfDestructBot] 没有解析到 SDB_* 动画片段，请检查 FBX 的 NLA 轨道名。");
            return;
        }

        var controller = BuildController(clips);
        BuildEnemyPrefab(controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SelfDestructBot] 完成：材质 {materials.Count} 个，动画 {clips.Count} 段，怪物预制体 {EnemyPrefabPath}");
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
            Debug.LogWarning("[SelfDestructBot] 取不到 ModelImporter，跳过导入设置。");
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
        foreach (var mat in materials)
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), mat);
        }

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
            // 显式裁到设计好的帧区间：Blender 的 NLA 烘焙偶尔会给出多余尾巴。
            c.firstFrame = 0;
            c.lastFrame = LastFrameFor(c.name);
            c.events = c.name != null && c.name.Contains("Attack") ? BuildArmSwingEvents() : new AnimationEvent[0];
            clipSettings[i] = c;
        }
        importer.clipAnimations = clipSettings;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    /// <summary>每段动画在 FBX 里应裁到的最后帧（30 fps）。</summary>
    static int LastFrameFor(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return 0;
        if (clipName.Contains("SDB_Idle")) return 59;     // 60 帧 = 1.97 s
        if (clipName.Contains("SDB_Walk")) return 39;     // 40 帧 = 1.30 s
        if (clipName.Contains("SDB_Attack")) return 47;   // 48 帧 = 1.57 s
        if (clipName.Contains("SDB_Prime")) return 29;    // 30 帧 = 0.97 s
        return 0;
    }

    static bool IsLoopClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return false;
        foreach (var name in LoopClips)
        {
            if (clipName.Contains(name)) return true;
        }
        return false;
    }

    /// <summary>
    /// 挥臂命中时机做成动画事件（函数名 OnArmSwing），由 SelfDestructBotUnit 接收后广播。
    /// 导入的 FBX 片段不能直接 AnimationUtility.SetAnimationEvents，所以事件写在导入设置里。
    /// 帧号必须与 Blender 脚本 SDB_Attack 的扫击关键帧一致（第 22 帧）。
    /// </summary>
    static AnimationEvent[] BuildArmSwingEvents()
    {
        return new[]
        {
            new AnimationEvent
            {
                functionName = SelfDestructBotUnit.ArmSwingEventName,
                time = SelfDestructBotUnit.ArmSwingFrame / ClipFps,
                messageOptions = SendMessageOptions.DontRequireReceiver
            }
        };
    }

    // ------------------------------------------------------------ 动画片段
    static List<AnimationClip> LoadClips()
    {
        var found = new List<AnimationClip>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            var clip = asset as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__")) continue;
            found.Add(clip);
        }
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found;
    }

    static AnimationClip FindClip(List<AnimationClip> clips, string key)
    {
        foreach (var clip in clips)
        {
            if (clip.name.Contains(key)) return clip;
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

        var entries = new (string key, string state)[]
        {
            ("SDB_Idle", SelfDestructBotUnit.StateIdle),
            ("SDB_Walk", SelfDestructBotUnit.StateWalk),
            ("SDB_Attack", SelfDestructBotUnit.StateAttack),
            ("SDB_Prime", SelfDestructBotUnit.StatePrime),
        };

        AnimatorState defaultState = null;
        foreach (var entry in entries)
        {
            var clip = FindClip(clips, entry.key);
            if (clip == null)
            {
                Debug.LogWarning($"[SelfDestructBot] 缺少动画片段 {entry.key}");
                continue;
            }
            var state = machine.AddState(entry.state);
            state.motion = clip;
            state.speed = 1f;
            state.writeDefaultValues = true;
            if (defaultState == null) defaultState = state;
        }
        if (defaultState != null) machine.defaultState = defaultState;
        return controller;
    }

    // ------------------------------------------------------------ 预制体
    static GameObject CreateBotInstance(AnimatorController controller)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "SelfDestructBot";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        var animator = instance.GetComponent<Animator>();
        if (animator == null) animator = instance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Transform hand = EnsureAnchor(instance.transform, "Forearm_R", HandAnchorName, new Vector3(0f, 0.32f, 0f));
        Transform bomb = EnsureAnchor(instance.transform, "Bomb", BombAnchorName, new Vector3(0f, 0.10f, 0f));

        var unit = instance.GetComponent<SelfDestructBotUnit>() ?? instance.AddComponent<SelfDestructBotUnit>();
        unit.animator = animator;
        unit.hand = hand;
        unit.bomb = bomb;
        return instance;
    }

    /// <summary>在指定骨骼下挂一个空锚点（骨骼名找不到时退回根节点，位置仍然可用）。</summary>
    static Transform EnsureAnchor(Transform root, string boneName, string anchorName, Vector3 localPosition)
    {
        Transform bone = FindDeep(root, boneName);
        var existing = FindDeep(root, anchorName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject(anchorName);
        Transform anchor = go.transform;
        if (bone != null)
        {
            anchor.SetParent(bone, false);
        }
        else
        {
            Debug.LogWarning($"[SelfDestructBot] 找不到骨骼 {boneName}，锚点 {anchorName} 挂到根节点。");
            anchor.SetParent(root, false);
        }
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    /// <summary>
    /// 生成可接入出怪口的地面怪物预制体。
    /// 组件组合：SelfDestructBotUnit（动画）+ EnemyHealth（血量）+ MonsterPathFollower（地面分支）
    /// + SelfDestructBotEnemy（挥臂攻击 / 靠陷阱自爆）。
    /// <b>刻意不加 GroundEnemyCombat</b>：那套近战会与本组件的路径/移动暂停互抢，
    /// EnemySpawnPoint 对自爆机器人也跳过了自动添加。
    /// </summary>
    static GameObject BuildEnemyPrefab(AnimatorController controller)
    {
        Directory.CreateDirectory("Assets/Resources");
        var instance = CreateBotInstance(controller);
        instance.name = EnemyPrefabResourceName;

        var health = instance.GetComponent<EnemyHealth>() ?? instance.AddComponent<EnemyHealth>();
        var follower = instance.GetComponent<MonsterPathFollower>() ?? instance.AddComponent<MonsterPathFollower>();
        var enemy = instance.GetComponent<SelfDestructBotEnemy>() ?? instance.AddComponent<SelfDestructBotEnemy>();

        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("maxHealth").floatValue = EnemyMaxHealth;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        var followerSo = new SerializedObject(follower);
        followerSo.FindProperty("moveSpeed").floatValue = EnemyMoveSpeed;
        followerSo.FindProperty("turnSpeed").floatValue = 420f;
        followerSo.FindProperty("stoppingDistance").floatValue = 0.05f;
        followerSo.FindProperty("flying").boolValue = false;
        followerSo.ApplyModifiedPropertiesWithoutUndo();

        var enemySo = new SerializedObject(enemy);
        enemySo.FindProperty("follower").objectReferenceValue = follower;
        enemySo.FindProperty("health").objectReferenceValue = health;
        enemySo.FindProperty("unit").objectReferenceValue = instance.GetComponent<SelfDestructBotUnit>();
        enemySo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(instance, EnemyPrefabPath);
        Object.DestroyImmediate(instance);
        return AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
    }

    internal static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    // ------------------------------------------------------------ 校验
    [MenuItem("Tools/塔防/校验自爆机器人预制体")]
    public static void Probe()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[SelfDestructBot] 预制体不存在：{EnemyPrefabPath}，先跑 CreateAll。");
            return;
        }

        var sb = new System.Text.StringBuilder();
        var failures = new List<string>();
        sb.AppendLine("[SelfDestructBot] 预制体校验");

        Check(prefab.GetComponent<EnemyInstance>() == null, "EnemyInstance 由出怪口运行时添加", sb, failures);

        var health = prefab.GetComponent<EnemyHealth>();
        Check(health != null, "EnemyHealth", sb, failures);
        if (health != null) sb.AppendLine($"  血量: {health.MaxHealth}");

        var follower = prefab.GetComponent<MonsterPathFollower>();
        Check(follower != null, "MonsterPathFollower", sb, failures);
        if (follower != null)
        {
            sb.AppendLine($"  速度: {follower.MoveSpeed}，IsFlying: {follower.IsFlying}");
            Check(!follower.IsFlying, "地面分支（IsFlying = false）", sb, failures);
        }

        var unit = prefab.GetComponent<SelfDestructBotUnit>();
        Check(unit != null, "SelfDestructBotUnit", sb, failures);
        var enemy = prefab.GetComponent<SelfDestructBotEnemy>();
        Check(enemy != null, "SelfDestructBotEnemy", sb, failures);
        if (unit != null)
        {
            Check(unit.animator != null && unit.animator.runtimeAnimatorController != null, "Animator + Controller", sb, failures);
            Check(unit.hand != null, HandAnchorName + " 挂点", sb, failures);
            Check(unit.bomb != null, BombAnchorName + " 挂点", sb, failures);
        }

        Check(prefab.GetComponent<GroundEnemyCombat>() == null,
            "没有 GroundEnemyCombat（否则会和自爆逻辑抢移动）", sb, failures);

        foreach (var bone in new[] { "Root", "Hips", "Chest", "Head", "Bomb", "Arm_R", "Forearm_R", "Thigh_R", "Shin_R", "Foot_R" })
        {
            Check(FindDeep(prefab.transform, bone) != null, $"骨骼 {bone}", sb, failures);
        }

        var clips = LoadClips();
        foreach (var name in new[] { "SDB_Idle", "SDB_Walk", "SDB_Attack", "SDB_Prime" })
        {
            Check(FindClip(clips, name) != null, $"动画 {name}", sb, failures);
        }

        var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine($"  蒙皮网格: {renderers.Length}，材质槽: {(renderers.Length > 0 ? renderers[0].sharedMaterials.Length : 0)}");
        if (renderers.Length > 0)
        {
            int unbound = 0;
            foreach (var mat in renderers[0].sharedMaterials)
            {
                if (mat == null || mat.name.Contains("No Name") || mat.shader.name == "Hidden/InternalErrorShader") unbound++;
            }
            Check(unbound == 0, $"材质全部绑定到 M_SDB_*（未绑定 {unbound}）", sb, failures);
        }

        Check(Resources.Load<GameObject>(EnemyPrefabResourceName) != null,
            $"Resources.Load(\"{EnemyPrefabResourceName}\") 可取到", sb, failures);

        sb.AppendLine(failures.Count == 0 ? "SELF_DESTRUCT_BOT_PASS" : $"SELF_DESTRUCT_BOT_FAIL: {string.Join(" / ", failures)}");
        Debug.Log(sb.ToString());
    }

    public static void ProbeBatch()
    {
        Probe();
    }

    static void Check(bool condition, string label, System.Text.StringBuilder sb, List<string> failures)
    {
        sb.AppendLine($"  [{(condition ? "OK" : "FAIL")}] {label}");
        if (!condition) failures.Add(label);
    }
}
