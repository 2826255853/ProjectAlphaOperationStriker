using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 快速小怪（防暴警棍兵）的模型导入工具。
/// 从 <see cref="ModelPath"/> 的 FBX 生成 URP 材质与可直接出怪的地面怪预制体
/// <see cref="PrefabPath"/>，数值来自 <see cref="MonsterSpeciesCatalog.FastRunner"/>，
/// 因此改平衡只改数值表，再重跑本工具即可，不需要手改预制体。
///
/// 预制体结构：FastRunner（根，带碰撞体/血量/近战/程序化步态） → FastRunner_Model（FBX 实例）。
/// 警棍是独立蒙皮网格 FastRunner_Baton，权重全在 Baton 骨骼上，可单独隐藏或替换。
/// </summary>
public static class FastRunnerAuthoring
{
    public const string ModelPath = "Assets/Models/FastRunner/FastRunner.fbx";
    public const string MaterialDir = "Assets/Models/FastRunner/Materials";
    public const string PrefabPath = "Assets/Prefabs/FastRunner.prefab";
    public const string SpeciesId = MonsterSpeciesCatalog.FastRunnerId;

    /// <summary>预制体根缩放后仍然使用的碰撞体尺寸（未乘缩放，随 Transform 自动缩放）。</summary>
    public const float ColliderHeight = 1.78f;
    public const float ColliderRadius = 0.30f;

    sealed class MatSpec
    {
        public string Name;
        public Color BaseColor;
        public float Metallic;
        public float Smoothness;
        public bool Transparent;
    }

    // 与 build_fast_runner.py 的 MAT 表一一对应：颜色是 sRGB 近似值，透明面罩单独处理。
    static readonly MatSpec[] Palette =
    {
        new MatSpec { Name = "FR_Helmet_Shell", BaseColor = new Color(0.030f, 0.032f, 0.036f), Metallic = 0.12f, Smoothness = 0.74f },
        new MatSpec { Name = "FR_Visor_Glass", BaseColor = new Color(0.020f, 0.026f, 0.032f, 0.22f), Metallic = 0f, Smoothness = 0.95f, Transparent = true },
        new MatSpec { Name = "FR_Face_Skin", BaseColor = new Color(0.55f, 0.41f, 0.32f), Metallic = 0f, Smoothness = 0.42f },
        new MatSpec { Name = "FR_Eye_Dark", BaseColor = new Color(0.014f, 0.011f, 0.009f), Metallic = 0f, Smoothness = 0.65f },
        new MatSpec { Name = "FR_Uniform_Cloth", BaseColor = new Color(0.024f, 0.025f, 0.028f), Metallic = 0f, Smoothness = 0.14f },
        new MatSpec { Name = "FR_Armor_Plate", BaseColor = new Color(0.020f, 0.021f, 0.024f), Metallic = 0.08f, Smoothness = 0.66f },
        new MatSpec { Name = "FR_Webbing_Nylon", BaseColor = new Color(0.016f, 0.017f, 0.019f), Metallic = 0f, Smoothness = 0.24f },
        new MatSpec { Name = "FR_Glove_Leather", BaseColor = new Color(0.014f, 0.014f, 0.016f), Metallic = 0f, Smoothness = 0.50f },
        new MatSpec { Name = "FR_Boot_Leather", BaseColor = new Color(0.017f, 0.017f, 0.019f), Metallic = 0f, Smoothness = 0.60f },
        new MatSpec { Name = "FR_Boot_Sole", BaseColor = new Color(0.008f, 0.008f, 0.009f), Metallic = 0f, Smoothness = 0.12f },
        new MatSpec { Name = "FR_Metal_Fitting", BaseColor = new Color(0.080f, 0.085f, 0.095f), Metallic = 0.88f, Smoothness = 0.70f },
        new MatSpec { Name = "FR_Baton_Rubber", BaseColor = new Color(0.011f, 0.011f, 0.013f), Metallic = 0f, Smoothness = 0.20f },
        new MatSpec { Name = "FR_Baton_Grip", BaseColor = new Color(0.006f, 0.006f, 0.007f), Metallic = 0f, Smoothness = 0.08f },
        new MatSpec { Name = "FR_Face_Mask", BaseColor = new Color(0.012f, 0.012f, 0.014f), Metallic = 0f, Smoothness = 0.08f },
        new MatSpec { Name = "FR_Shoulder_Patch", BaseColor = new Color(0.040f, 0.052f, 0.072f), Metallic = 0f, Smoothness = 0.38f },
    };

    [MenuItem("Tools/塔防/快速小怪/生成材质与预制体")]
    public static void CreateAssetsMenu()
    {
        CreateAssetsBatch();
        EditorUtility.DisplayDialog("快速小怪", "材质与预制体已生成：\n" + PrefabPath, "好");
    }

    [MenuItem("Tools/塔防/快速小怪/探针：模型与骨骼导入检查")]
    public static void ProbeMenu()
    {
        var report = ProbeModel();
        EditorUtility.DisplayDialog("快速小怪", "探针完成，见 Console 与临时目录。\n骨骼 " + report.boneCount + " 根", "好");
    }

    [MenuItem("Tools/塔防/快速小怪/渲染预览图")]
    public static void RenderPreviewMenu()
    {
        RenderPreview();
        EditorUtility.DisplayDialog("快速小怪", "预览图已输出到桌面素材目录。", "好");
    }

    /// <summary>批处理入口：-executeMethod FastRunnerAuthoring.CreateAssetsBatch</summary>
    public static void CreateAssetsBatch()
    {
        ConfigureImporter();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            throw new FileNotFoundException("找不到快速小怪模型: " + ModelPath);
        }

        var species = MonsterSpeciesCatalog.FastRunner;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            Debug.LogWarning("[FastRunner] 未找到 URP/Lit，退回 Standard");
        }

        if (!Directory.Exists(MaterialDir))
        {
            Directory.CreateDirectory(MaterialDir);
        }

        var materials = new Dictionary<string, Material>();
        foreach (var spec in Palette)
        {
            materials[spec.Name] = GetOrCreateMaterial(spec, shader);
        }

        AssetDatabase.SaveAssets();

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "FastRunner_Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var remapped = 0;
        var unmatched = new List<string>();
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var source = renderer.sharedMaterials;
            var target = new Material[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var sourceName = source[i] != null ? source[i].name : string.Empty;
                var spec = Palette.FirstOrDefault(p => string.Equals(p.Name, sourceName, StringComparison.Ordinal))
                           ?? Palette.FirstOrDefault(p => sourceName.Contains(p.Name) || p.Name.Contains(sourceName));
                if (spec != null)
                {
                    target[i] = materials[spec.Name];
                    remapped++;
                }
                else
                {
                    target[i] = source[i];
                    unmatched.Add(sourceName);
                }
            }

            renderer.sharedMaterials = target;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        var root = new GameObject("FastRunner");
        instance.transform.SetParent(root.transform, false);
        root.transform.localScale = Vector3.one * species.ModelScale;

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, ColliderHeight * 0.5f, 0f);
        capsule.height = ColliderHeight;
        capsule.radius = ColliderRadius;
        capsule.direction = 1;

        // 血量与抗性：数值唯一来源是 MonsterSpeciesCatalog.FastRunner。
        var health = root.AddComponent<EnemyHealth>();
        SetSerializedFloat(health, "maxHealth", species.MaxHealth);
        for (var i = 0; i < DamageTypeCatalog.Count; i++)
        {
            var type = DamageTypeCatalog.All[i];
            health.SetResistancePercent(type, species.GetResistancePercent(type));
        }

        // 近战：攻击 8、间隔 0.8 秒，比普通小怪更频繁。
        var combat = root.AddComponent<GroundEnemyCombat>();
        SetSerializedFloat(combat, "attackDamage", species.AttackDamage);
        SetSerializedFloat(combat, "attackInterval", species.AttackInterval);
        SetSerializedFloat(combat, "attackRange", 1.35f);
        SetSerializedFloat(combat, "playerDetectionDistance", 9f);
        SetSerializedFloat(combat, "trapDetectionDistance", 7f);

        // 程序化步态：快速小怪步频更高、摆幅更大，看起来像在快走/小跑。
        var gait = root.AddComponent<MonsterWalkAnimator>();
        SetSerializedFloat(gait, "fullWalkSpeed", 3.2f);
        SetSerializedFloat(gait, "strideFrequency", 2.45f);
        SetSerializedFloat(gait, "legSwingDegrees", 34f);
        SetSerializedFloat(gait, "kneeBendDegrees", 48f);
        SetSerializedFloat(gait, "armSwingDegrees", 26f);
        SetSerializedFloat(gait, "hipBobMeters", 0.042f);
        SetSerializedFloat(gait, "walkLeanDegrees", 8f);
        SetSerializedFloat(gait, "attackDuration", 0.52f);

        var dir = Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[FastRunner] prefab=" + PrefabPath
                  + " species=" + species.Id
                  + " hp=" + species.MaxHealth
                  + " attack=" + species.AttackDamage
                  + " scale=" + species.ModelScale
                  + " materialsRemapped=" + remapped
                  + (unmatched.Count > 0 ? " unmatchedMaterials=" + string.Join(",", unmatched.Distinct()) : string.Empty)
                  + " saved=" + (prefab != null));
    }

    /// <summary>只读探针：骨骼、网格、材质、包围盒与警棍权重检查。</summary>
    public static (int boneCount, int renderers, float height) ProbeModel()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            throw new FileNotFoundException("找不到快速小怪模型: " + ModelPath);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var bounds = new Bounds(instance.transform.position, Vector3.zero);
        var first = true;
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (first)
            {
                bounds = r.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        var smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"model\": \"" + ModelPath + "\",");
        sb.AppendLine("  \"bounds_size\": [" + bounds.size.x.ToString("F4") + ", " + bounds.size.y.ToString("F4") + ", " + bounds.size.z.ToString("F4") + "],");
        sb.AppendLine("  \"bounds_min\": [" + bounds.min.x.ToString("F4") + ", " + bounds.min.y.ToString("F4") + ", " + bounds.min.z.ToString("F4") + "],");
        sb.AppendLine("  \"renderers\": [");
        var boneNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < smrs.Length; i++)
        {
            var smr = smrs[i];
            foreach (var bone in smr.bones)
            {
                if (bone != null) boneNames.Add(bone.name);
            }

            var isBaton = smr.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0;
            var batonWeightViolations = 0;
            if (isBaton && smr.sharedMesh != null)
            {
                foreach (var w in smr.sharedMesh.boneWeights)
                {
                    var indices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    var weights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (var k = 0; k < 4; k++)
                    {
                        if (weights[k] <= 1e-4f) continue;
                        var bn = indices[k] >= 0 && indices[k] < smr.bones.Length && smr.bones[indices[k]] != null
                            ? smr.bones[indices[k]].name
                            : "(missing)";
                        if (bn != "Baton") batonWeightViolations++;
                    }
                }
            }

            sb.AppendLine("    {");
            sb.AppendLine("      \"name\": \"" + smr.name + "\",");
            sb.AppendLine("      \"is_baton\": " + (isBaton ? "true" : "false") + ",");
            sb.AppendLine("      \"vertices\": " + (smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0) + ",");
            sb.AppendLine("      \"bones\": " + smr.bones.Count(b => b != null) + ",");
            sb.AppendLine("      \"baton_weight_violations\": " + batonWeightViolations + ",");
            sb.AppendLine("      \"materials\": [" + string.Join(", ", smr.sharedMaterials.Select(m => "\"" + (m != null ? m.name : "null") + "\"")) + "]");
            sb.Append("    }");
            sb.AppendLine(i == smrs.Length - 1 ? string.Empty : ",");
        }

        sb.AppendLine("  ],");
        sb.AppendLine("  \"bones_total\": " + boneNames.Count);
        sb.AppendLine("}");

        var outDir = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "fast-runner");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "probe.json");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));

        Debug.Log("[FastRunner] probe " + outPath + " size=" + bounds.size + " smrs=" + smrs.Length
                  + " bones=" + boneNames.Count
                  + " baton=" + string.Join(",", smrs.Where(s => s.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0).Select(s => s.name)));

        UnityEngine.Object.DestroyImmediate(instance);
        return (boneNames.Count, smrs.Length, bounds.size.y);
    }

    /// <summary>
    /// 校验预制体与数值表一致，并把报告写到 %TEMP%/alpha-striker-unity/fast-runner/validation.json。
    /// 返回失败项数量；0 表示全部通过。批处理入口 FastRunnerAuthoring.ValidateBatch。
    /// </summary>
    public static int Validate()
    {
        var failures = new List<string>();
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            checks.Add((condition ? "PASS " : "FAIL ") + message);
            if (!condition) failures.Add(message);
        }

        var species = MonsterSpeciesCatalog.FastRunner;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "预制体存在: " + PrefabPath);
        if (prefab != null)
        {
            var health = prefab.GetComponent<EnemyHealth>();
            var combat = prefab.GetComponent<GroundEnemyCombat>();
            var gait = prefab.GetComponent<MonsterWalkAnimator>();
            var capsule = prefab.GetComponent<CapsuleCollider>();
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var batonRenderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(r => r.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0);

            Check(health != null && Mathf.Approximately(health.MaxHealth, species.MaxHealth),
                "血量 = " + species.MaxHealth + "（实际 " + (health != null ? health.MaxHealth : -1f) + "）");
            Check(health != null && Mathf.Approximately(health.GetResistancePercent(DamageType.Light), species.GetResistancePercent(DamageType.Light)),
                "光属性抗性 = " + species.GetResistancePercent(DamageType.Light) + "%");
            Check(combat != null, "挂载 GroundEnemyCombat");
            Check(gait != null, "挂载 MonsterWalkAnimator");
            Check(capsule != null && capsule.height < 1.8f && capsule.radius <= 0.32f,
                "碰撞体小于普通小怪（高 " + (capsule != null ? capsule.height : -1f) + "，半径 " + (capsule != null ? capsule.radius : -1f) + "）");
            Check(animator == null, "不依赖 Animator（程序化步态）");
            Check(batonRenderer != null, "警棍是独立蒙皮网格（右手）");
            Check(prefab.transform.localScale.x < 1f, "预制体缩放小于 1（体型更小）");

            if (gait != null)
            {
                gait.EnsureInitialized();
                Check(gait.MissingBones.Count == 0, "骨骼齐全，缺失: " + string.Join(",", gait.MissingBones));
            }

            if (combat != null)
            {
                var so = new SerializedObject(combat);
                var damage = so.FindProperty("attackDamage");
                var interval = so.FindProperty("attackInterval");
                Check(damage != null && Mathf.Approximately(damage.floatValue, species.AttackDamage),
                    "攻击力 = " + species.AttackDamage + "（实际 " + (damage != null ? damage.floatValue : -1f) + "）");
                Check(interval != null && Mathf.Approximately(interval.floatValue, species.AttackInterval),
                    "攻击间隔 = " + species.AttackInterval + "s");
            }
        }

        var report = new StringBuilder();
        report.AppendLine("{");
        report.AppendLine("  \"prefab\": \"" + PrefabPath + "\",");
        report.AppendLine("  \"species\": \"" + species.Id + "\",");
        report.AppendLine("  \"checks\": [");
        for (var i = 0; i < checks.Count; i++)
        {
            report.AppendLine("    \"" + checks[i].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" + (i == checks.Count - 1 ? string.Empty : ","));
        }

        report.AppendLine("  ],");
        report.AppendLine("  \"failures\": " + failures.Count);
        report.AppendLine("}");

        var outDir = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "fast-runner");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "validation.json");
        File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));

        Debug.Log("[FastRunner] validation " + (failures.Count == 0 ? "PASS" : "FAIL")
                  + " checks=" + checks.Count + " failures=" + failures.Count + " report=" + outPath);
        foreach (var failure in failures)
        {
            Debug.LogError("[FastRunner] 校验失败: " + failure);
        }

        return failures.Count;
    }

    /// <summary>批处理入口：-executeMethod FastRunnerAuthoring.ValidateBatch</summary>
    public static void ValidateBatch()
    {
        var failures = Validate();
        if (failures > 0)
        {
            throw new InvalidOperationException("快速小怪预制体校验失败：" + failures + " 项");
        }
    }

    /// <summary>快速小怪在地面波次中的默认占比：一半普通小怪、一半快速小怪。</summary>
    public const float DefaultFastShare = 0.5f;

    /// <summary>
    /// 把快速小怪按占比混进指定地面出怪口的每一波（默认 50% 普通 + 50% 快速）。
    /// 只写「第二种预制体 / 占比 / 速度」，不动数量与主预制体，因此波次节奏保持原样。
    /// </summary>
    [MenuItem("Tools/塔防/快速小怪/写入选中出怪口的 50% 快速小怪")]
    public static void AssignToSelectedEntrance()
    {
        var spawn = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<EnemySpawnPoint>()
            : null;
        if (spawn == null)
        {
            EditorUtility.DisplayDialog("快速小怪", "请先在 Hierarchy 中选中一个带 EnemySpawnPoint 的出怪口。", "好");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new FileNotFoundException("请先生成预制体: " + PrefabPath);
        }

        int changed = ApplyMixedWaves(spawn, prefab, DefaultFastShare);
        Debug.Log("[FastRunner] 已把 " + spawn.name + " 的 " + changed + " 个波次改为 "
                  + Mathf.RoundToInt(DefaultFastShare * 100f) + "% 快速小怪");
    }

    /// <summary>
    /// 把主场景（构建场景列表第一个）里所有地面出怪口的每一波改成 50% 普通 + 50% 快速。
    /// 会打开并保存场景，所以先备份场景再执行。
    /// </summary>
    [MenuItem("Tools/塔防/快速小怪/把主场景地面出怪口改为 50% 普通 + 50% 快速")]
    public static void ApplyMixedWavesToMainSceneMenu()
    {
        int entrances = ApplyMixedWavesToMainScene();
        EditorUtility.DisplayDialog("快速小怪", "已改写 " + entrances + " 个地面出怪口（50% 普通 + 50% 快速）并保存场景。", "好");
    }

    /// <summary>批处理入口：-executeMethod FastRunnerAuthoring.ApplyMixedWavesToMainScene</summary>
    public static int ApplyMixedWavesToMainScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new FileNotFoundException("请先生成预制体: " + PrefabPath);
        }

        string scenePath = ResolveMainScenePath();
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, scenePath, StringComparison.Ordinal))
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        var entrances = UnityEngine.Object
            .FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include)
            .Where(s => s.gameObject.scene == scene && !s.IsFlyingEntrance)
            .OrderBy(s => s.name, StringComparer.Ordinal)
            .ToArray();

        if (entrances.Length == 0)
        {
            throw new InvalidOperationException("场景中没有地面出怪口: " + scenePath);
        }

        int totalWaves = 0;
        foreach (var entrance in entrances)
        {
            totalWaves += ApplyMixedWaves(entrance, prefab, DefaultFastShare);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[FastRunner] 主场景 " + scenePath + " 已改写 " + entrances.Length
                  + " 个地面出怪口、共 " + totalWaves + " 个波次（50% 普通 + 50% 快速）");
        return entrances.Length;
    }

    /// <summary>
    /// 把快速小怪按占比混进一个出怪口的每一波，返回改写的波次数。
    /// 主预制体保持不变（空则运行时回落到出怪口默认预制体）；若某波原本整波都是快速小怪，
    /// 则把主预制体还原成出怪口默认预制体，避免变成 100% 快速。
    /// </summary>
    public static int ApplyMixedWaves(EnemySpawnPoint spawn, GameObject fastPrefab, float share)
    {
        if (spawn == null) throw new ArgumentNullException(nameof(spawn));
        if (fastPrefab == null) throw new ArgumentNullException(nameof(fastPrefab));

        if (spawn.IsFlyingEntrance)
        {
            Debug.LogWarning("[FastRunner] " + spawn.name + " 是飞行出怪口，跳过混刷");
            return 0;
        }

        var so = new SerializedObject(spawn);
        var waves = so.FindProperty("waveSpawnSettings");
        if (waves == null || !waves.isArray)
        {
            throw new InvalidOperationException("出怪口缺少 waveSpawnSettings");
        }

        var defaultPrefab = so.FindProperty("enemyPrefab");
        float fastSpeed = MonsterSpeciesCatalog.FastRunner.MoveSpeed;
        float clampedShare = Mathf.Clamp01(share);
        int changed = 0;

        Undo.RecordObject(spawn, "Mix Fast Runner Waves");
        for (var i = 0; i < waves.arraySize; i++)
        {
            var element = waves.GetArrayElementAtIndex(i);
            if (element == null) continue;

            var typeProperty = element.FindPropertyRelative("monsterType");
            if (typeProperty != null && typeProperty.enumValueIndex == (int)MonsterType.Flying) continue;

            var alternate = element.FindPropertyRelative("alternateEnemyPrefab");
            var alternateShare = element.FindPropertyRelative("alternateShare");
            var alternateSpeed = element.FindPropertyRelative("alternateMoveSpeed");
            if (alternate == null || alternateShare == null || alternateSpeed == null) continue;

            // 旧配置若把整波写成快速小怪，先把主预制体还原成出怪口默认值，否则会变成 100% 快速。
            var primary = element.FindPropertyRelative("enemyPrefab");
            if (primary != null && primary.objectReferenceValue == fastPrefab)
            {
                primary.objectReferenceValue = defaultPrefab != null ? defaultPrefab.objectReferenceValue : null;
            }

            alternate.objectReferenceValue = fastPrefab;
            alternateShare.floatValue = clampedShare;
            alternateSpeed.floatValue = fastSpeed;
            changed++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(spawn);
        return changed;
    }

    private static string ResolveMainScenePath()
    {
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene != null && scene.enabled && !string.IsNullOrEmpty(scene.path)) return scene.path;
        }

        throw new InvalidOperationException("构建场景列表为空，无法定位主场景");
    }

    /// <summary>批处理入口（需要 -WithGraphics）：渲染预制体的正面/侧面/走路姿态预览。</summary>
    public static void RenderPreview()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outDir = Path.Combine(desktop, "陷阱素材文件夹", "FastRunner", "unity-previews");
        Directory.CreateDirectory(outDir);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new FileNotFoundException("请先生成预制体: " + PrefabPath);
        }

        var created = new List<GameObject>();
        var savedAmbient = RenderSettings.ambientLight;
        var savedAmbientMode = RenderSettings.ambientMode;
        var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        try
        {
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            created.Add(instance);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "FastRunnerPreviewGround";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
            groundMat.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.33f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            created.Add(ground);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.37f);

            var keyGo = new GameObject("FastRunnerPreviewKey");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2.0f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.Soft;
            keyGo.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            created.Add(keyGo);

            var rimGo = new GameObject("FastRunnerPreviewRim");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.8f;
            rim.color = new Color(0.75f, 0.85f, 1f);
            rimGo.transform.rotation = Quaternion.Euler(25f, 20f, 0f);
            created.Add(rimGo);

            var camGo = new GameObject("FastRunnerPreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.34f, 0.36f, 0.39f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            created.Add(camGo);

            var rt = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;

            void SaveShot(string fileName)
            {
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 1200, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 1200), 0, 0);
                tex.Apply();
                var path = Path.Combine(outDir, fileName);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                RenderTexture.active = null;
                Debug.Log("[FastRunner] preview " + path);
            }

            var shots = new (string name, Vector3 pos, Vector3 target)[]
            {
                ("unity_preview_front.png", new Vector3(0f, 1.02f, 3.3f), new Vector3(0f, 0.92f, 0f)),
                ("unity_preview_side.png", new Vector3(3.3f, 1.02f, 0f), new Vector3(0f, 0.92f, 0f)),
                ("unity_preview_back.png", new Vector3(0f, 1.02f, -3.3f), new Vector3(0f, 0.92f, 0f)),
                ("unity_preview_three_quarter.png", new Vector3(1.9f, 1.50f, 2.9f), new Vector3(0f, 0.92f, 0f)),
            };

            // URP 在 batch 里首次 Camera.Render 会预热着色器，先空渲染两帧丢弃。
            camGo.transform.position = new Vector3(0f, 1.02f, 3.3f);
            camGo.transform.LookAt(new Vector3(0f, 0.92f, 0f));
            cam.Render();
            cam.Render();

            foreach (var shot in shots)
            {
                camGo.transform.position = shot.pos;
                camGo.transform.LookAt(shot.target);
                SaveShot(shot.name);
            }

            var walkAnimator = instance.GetComponent<MonsterWalkAnimator>();
            if (walkAnimator != null)
            {
                walkAnimator.EnsureInitialized();
                camGo.transform.position = new Vector3(1.9f, 1.50f, 2.9f);
                camGo.transform.LookAt(new Vector3(0f, 0.92f, 0f));
                var walkShots = new (string name, float phase, float blend)[]
                {
                    ("walk_pose_contact.png", Mathf.PI * 0.5f, 1f),
                    ("walk_pose_swing.png", Mathf.PI * 1.5f, 1f),
                };
                foreach (var shot in walkShots)
                {
                    walkAnimator.ApplyPose(shot.phase, shot.blend);
                    SaveShot(shot.name);
                }

                walkAnimator.RestoreRestPose();
                Debug.Log("[FastRunner] walk previews rendered, missingBones=" + walkAnimator.MissingBones.Count);
            }

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
        finally
        {
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientLight = savedAmbient;
            foreach (var go in created)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            UnityEngine.Object.DestroyImmediate(groundMat);
        }
    }

    static void ConfigureImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.addCollider = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.optimizeMeshPolygons = true;
        importer.optimizeMeshVertices = true;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    static void SetSerializedFloat(UnityEngine.Object target, string fieldName, float value)
    {
        var so = new SerializedObject(target);
        var property = so.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning("[FastRunner] 找不到字段 " + fieldName + " on " + target.GetType().Name);
            return;
        }

        property.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Material GetOrCreateMaterial(MatSpec spec, Shader shader)
    {
        var path = MaterialDir + "/" + spec.Name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetColor("_BaseColor", spec.BaseColor);
        mat.color = spec.BaseColor;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", spec.Metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", spec.Smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", spec.Smoothness);

        if (spec.Transparent)
        {
            // 面罩要真的透出脸部：URP Lit 的透明模式由 _Surface/_Blend/混合状态决定。
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = -1;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
