using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 蒙面士兵（普通小怪）模型导入工具。
/// 生成 URP 材质并把模型封装成可直接作为地面怪预制体使用的 Assets/Prefabs/MaskedSoldier.prefab。
/// 警棍是独立的蒙皮网格对象（MaskedSoldier_Baton，沿用右手的 Knife 挂点），
/// 后续可以单独隐藏/替换/掉落，不会和身体网格混在一起。
/// </summary>
public static class MaskedSoldierAuthoring
{
    public const string ModelPath = "Assets/Models/MaskedSoldier/MaskedSoldier.fbx";
    public const string TexturePath = "Assets/Models/MaskedSoldier/Textures/MaskedSoldier_Camo.png";
    const string WeavePath = "Assets/Models/MaskedSoldier/Textures/MaskedSoldier_Weave.png";
    public const string MaterialDir = "Assets/Models/MaskedSoldier/Materials";
    public const string PrefabPath = "Assets/Prefabs/MaskedSoldier.prefab";

    sealed class MatSpec
    {
        public string Name;
        public Color BaseColor;
        public float Metallic;
        public float Smoothness;
        public bool UseCamo;
        public bool Woven;
    }

    static readonly MatSpec[] Palette =
    {
        new MatSpec { Name = "MS_Camo_Uniform", BaseColor = new Color32(201, 196, 174, 255), Metallic = 0f, Smoothness = 0.15f, UseCamo = true, Woven = true },
        new MatSpec { Name = "MS_Camo_Helmet", BaseColor = new Color32(170, 169, 148, 255), Metallic = 0f, Smoothness = 0.26f, UseCamo = true },
        new MatSpec { Name = "MS_Gear_Vest", BaseColor = new Color32(73, 78, 66, 255), Metallic = 0f, Smoothness = 0.12f, Woven = true },
        new MatSpec { Name = "MS_Gear_Webbing", BaseColor = new Color32(51, 56, 49, 255), Metallic = 0f, Smoothness = 0.14f, Woven = true },
        new MatSpec { Name = "MS_Gear_Glove", BaseColor = new Color32(57, 59, 53, 255), Metallic = 0f, Smoothness = 0.32f },
        new MatSpec { Name = "MS_Boot_Leather", BaseColor = new Color32(59, 57, 52, 255), Metallic = 0f, Smoothness = 0.4f },
        new MatSpec { Name = "MS_Boot_Sole", BaseColor = new Color32(38, 39, 37, 255), Metallic = 0f, Smoothness = 0.19f },
        new MatSpec { Name = "MS_Fabric_Mask", BaseColor = new Color32(65, 70, 65, 255), Metallic = 0f, Smoothness = 0.08f, Woven = true },
        new MatSpec { Name = "MS_Metal_Buckle", BaseColor = new Color32(83, 89, 85, 255), Metallic = 0.5f, Smoothness = 0.46f },
        new MatSpec { Name = "MS_Patch", BaseColor = new Color32(179, 168, 146, 255), Metallic = 0f, Smoothness = 0.19f, Woven = true },
        new MatSpec { Name = "MS_Goggle_Lens", BaseColor = new Color32(71, 79, 71, 255), Metallic = 0.42f, Smoothness = 0.82f },
        new MatSpec { Name = "MS_Baton_Finish", BaseColor = new Color32(67, 73, 74, 255), Metallic = 0.52f, Smoothness = 0.52f },
        new MatSpec { Name = "MS_Baton_Grip", BaseColor = new Color32(41, 44, 44, 255), Metallic = 0f, Smoothness = 0.23f },
    };

    [MenuItem("Tools/塔防/蒙面士兵/生成材质与预制体")]
    public static void CreateAssetsMenu()
    {
        CreateAssetsBatch();
        EditorUtility.DisplayDialog("蒙面士兵", "材质与预制体已生成：\n" + PrefabPath, "好");
    }

    [MenuItem("Tools/塔防/蒙面士兵/探针：模型与骨骼导入检查")]
    public static void ProbeMenu()
    {
        ProbeModel();
        EditorUtility.DisplayDialog("蒙面士兵", "检查结果已写入日志与临时目录。", "好");
    }

    [MenuItem("Tools/塔防/蒙面士兵/渲染预览图")]
    public static void RenderPreviewMenu()
    {
        RenderPreview();
        EditorUtility.DisplayDialog("蒙面士兵", "预览图已输出到桌面素材目录。", "好");
    }

    /// <summary>批处理入口（需要 -WithGraphics）：渲染前后 3/4 视角的 Unity 实机材质预览。</summary>
    public static void RenderPreview()
    {
        var desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        var outDir = Path.Combine(desktop, "陷阱素材文件夹", "MaskedSoldier", "previews");
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
            // 普通 Instantiate：嵌套 FBX 预制体的骨骼是可写的场景对象，
            // PrefabUtility.InstantiatePrefab 产生的实例在批处理预览里不会反映程序化骨骼姿态。
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            created.Add(instance);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "MaskedSoldierPreviewGround";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
            groundMat.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.33f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            created.Add(ground);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.37f);

            var keyGo = new GameObject("MaskedSoldierPreviewKey");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2.0f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.Soft;
            keyGo.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            created.Add(keyGo);

            var rimGo = new GameObject("MaskedSoldierPreviewRim");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.7f;
            rim.color = new Color(0.75f, 0.85f, 1f);
            rimGo.transform.rotation = Quaternion.Euler(25f, 20f, 0f);
            created.Add(rimGo);

            var camGo = new GameObject("MaskedSoldierPreviewCam");
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

            var shots = new (string name, Vector3 pos, Vector3 target)[]
            {
                ("unity_preview_front.png", new Vector3(0f, 1.05f, 3.4f), new Vector3(0f, 0.95f, 0f)),
                ("unity_preview_three_quarter.png", new Vector3(1.9f, 1.55f, 3.0f), new Vector3(0f, 0.95f, 0f)),
                ("unity_preview_head.png", new Vector3(0.39f, 1.67f, 0.83f), new Vector3(0f, 1.55f, 0f)),
                ("unity_preview_back.png", new Vector3(0f, 1.05f, -3.4f), new Vector3(0f, 0.95f, 0f)),
            };

            // URP 在 batch 里首次 Camera.Render 会先编译/预热着色器，第一张图可能拿到默认材质；
            // 先在同一机位空渲染两次丢弃，保证正式输出的第一张就是正确材质。
            camGo.transform.position = new Vector3(0f, 1.05f, 3.4f);
            camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));
            cam.Render();
            cam.Render();

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
                Debug.Log("[MaskedSoldier] preview " + path);
            }

            foreach (var shot in shots)
            {
                camGo.transform.position = shot.pos;
                camGo.transform.LookAt(shot.target);
                SaveShot(shot.name);
            }

            // 走路/待机姿态预览：直接采样程序化动画，便于肉眼确认腿臂摆动方向与膝盖弯曲。
            var walkAnimator = instance.GetComponent<MonsterWalkAnimator>();
            if (walkAnimator != null)
            {
                walkAnimator.EnsureInitialized();
                camGo.transform.position = new Vector3(1.9f, 1.55f, 3.0f);
                camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));
                var walkShots = new (string name, float phase, float blend)[]
                {
                    ("walk_pose_idle.png", 0f, 0f),
                    ("walk_pose_contact.png", Mathf.PI * 0.5f, 1f),
                    ("walk_pose_passing.png", Mathf.PI, 1f),
                    ("walk_pose_swing.png", Mathf.PI * 1.5f, 1f),
                };
                foreach (var shot in walkShots)
                {
                    walkAnimator.ApplyPose(shot.phase, shot.blend);
                    SaveShot(shot.name);
                }

                walkAnimator.RestoreRestPose();
                Debug.Log("[MaskedSoldier] walk previews rendered, missingBones=" + walkAnimator.MissingBones.Count);

                // 挥棍姿态预览：起手 / 接触 / 收招三个关键帧，
                // 再补一张斜前方机位检查警棍与胸前装备的间距。
                var attackShots = new (string name, float progress, Vector3 pos)[]
                {
                    ("attack_pose_windup.png", 0.30f, new Vector3(0.45f, 1.30f, 3.1f)),
                    ("attack_pose_contact.png", 0.44f, new Vector3(0.45f, 1.30f, 3.1f)),
                    ("attack_pose_recover.png", 0.62f, new Vector3(0.45f, 1.30f, 3.1f)),
                    ("attack_pose_contact_three_quarter.png", 0.44f, new Vector3(2.4f, 1.5f, 2.5f)),
                };
                foreach (var shot in attackShots)
                {
                    camGo.transform.position = shot.pos;
                    camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));
                    walkAnimator.ApplyPose(0f, 0f, shot.progress);
                    SaveShot(shot.name);
                }

                walkAnimator.RestoreRestPose();
                Debug.Log("[MaskedSoldier] attack previews rendered");
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

    /// <summary>批处理入口：-executeMethod MaskedSoldierAuthoring.CreateAssetsBatch</summary>
    public static void CreateAssetsBatch()
    {
        ConfigureImporter();
        var weaveImporter = AssetImporter.GetAtPath(WeavePath) as TextureImporter;
        if (weaveImporter != null && weaveImporter.textureType != TextureImporterType.NormalMap)
        {
            weaveImporter.textureType = TextureImporterType.NormalMap;
            weaveImporter.SaveAndReimport();
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            throw new FileNotFoundException("找不到蒙面士兵模型: " + ModelPath);
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            Debug.LogWarning("[MaskedSoldier] 未找到 URP/Lit，退回 Standard");
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
        instance.name = "MaskedSoldier_Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var remapped = 0;
        var batonObjects = new List<string>();
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
                }
            }

            renderer.sharedMaterials = target;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                batonObjects.Add(smr.name);
            }
        }

        var root = new GameObject("MaskedSoldier");
        instance.transform.SetParent(root.transform, false);

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.32f;
        capsule.direction = 1;

        // 走路 / 待机程序化骨骼动画：运行时按实际位移自动混合，不需要 Animator 资源。
        root.AddComponent<MonsterWalkAnimator>();

        var dir = Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[MaskedSoldier] prefab=" + PrefabPath
                  + " materialsRemapped=" + remapped
                  + " batonObjects=" + string.Join(",", batonObjects)
                  + " shader=" + shader.name
                  + " saved=" + (prefab != null));
    }

    /// <summary>只读探针：把 FBX 导入结果写到 %TEMP%/alpha-striker-unity/masked-soldier/probe.json。</summary>
    public static void ProbeModel()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            throw new FileNotFoundException("找不到蒙面士兵模型: " + ModelPath);
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

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"model\": \"" + ModelPath + "\",");
        sb.AppendLine("  \"bounds_size\": [" + bounds.size.x.ToString("F4") + ", " + bounds.size.y.ToString("F4") + ", " + bounds.size.z.ToString("F4") + "],");
        sb.AppendLine("  \"bounds_min\": [" + bounds.min.x.ToString("F4") + ", " + bounds.min.y.ToString("F4") + ", " + bounds.min.z.ToString("F4") + "],");
        sb.AppendLine("  \"renderers\": [");

        var smrs = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (var i = 0; i < smrs.Length; i++)
        {
            var smr = smrs[i];
            var bones = smr.bones.Where(b => b != null).Select(b => b.name).ToArray();
            var isBaton = smr.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0;
            var batonWeightViolations = 0;
            if (isBaton && smr.sharedMesh != null)
            {
                var bw = smr.sharedMesh.boneWeights;
                foreach (var w in bw)
                {
                    var indices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    var weights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (var k = 0; k < 4; k++)
                    {
                        if (weights[k] <= 1e-4f)
                        {
                            continue;
                        }

                        var bn = indices[k] >= 0 && indices[k] < smr.bones.Length && smr.bones[indices[k]] != null
                            ? smr.bones[indices[k]].name
                            : "(missing)";
                        if (bn != "Knife")
                        {
                            batonWeightViolations++;
                        }
                    }
                }
            }

            sb.AppendLine("    {");
            sb.AppendLine("      \"name\": \"" + smr.name + "\",");
            sb.AppendLine("      \"is_baton\": " + (isBaton ? "true" : "false") + ",");
            sb.AppendLine("      \"vertices\": " + (smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0) + ",");
            sb.AppendLine("      \"lossy_scale\": [" + smr.transform.lossyScale.x.ToString("F4") + ", " + smr.transform.lossyScale.y.ToString("F4") + ", " + smr.transform.lossyScale.z.ToString("F4") + "],");
            var parentChain = new List<string>();
            for (var t = smr.transform; t != null && parentChain.Count < 6; t = t.parent)
            {
                parentChain.Add(t.name + " scale=" + t.localScale.x.ToString("F3"));
            }

            sb.AppendLine("      \"transform_chain\": \"" + string.Join(" > ", parentChain) + "\",");
            if (smr.sharedMesh != null)
            {
                var mb = smr.sharedMesh.bounds;
                sb.AppendLine("      \"mesh_bounds_center\": [" + mb.center.x.ToString("F4") + ", " + mb.center.y.ToString("F4") + ", " + mb.center.z.ToString("F4") + "],");
                sb.AppendLine("      \"mesh_bounds_size\": [" + mb.size.x.ToString("F4") + ", " + mb.size.y.ToString("F4") + ", " + mb.size.z.ToString("F4") + "],");
            }

            sb.AppendLine("      \"bones\": " + bones.Length + ",");
            sb.AppendLine("      \"bone_names\": [" + string.Join(", ", bones.Select(b => "\"" + b + "\"")) + "],");
            sb.AppendLine("      \"baton_weight_violations\": " + batonWeightViolations + ",");
            sb.AppendLine("      \"materials\": [" + string.Join(", ", smr.sharedMaterials.Select(m => "\"" + (m != null ? m.name : "null") + "\"")) + "]");
            sb.Append("    }");
            sb.AppendLine(i == smrs.Length - 1 ? string.Empty : ",");
        }

        sb.AppendLine("  ],");
        sb.AppendLine("  \"bones_total\": " + smrs.SelectMany(s => s.bones).Where(b => b != null).Select(b => b.name).Distinct().Count());
        sb.AppendLine("}");

        var outDir = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "masked-soldier");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "probe.json");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));

        Debug.Log("[MaskedSoldier] probe " + outPath + " size=" + bounds.size + " smrs=" + smrs.Length
                  + " baton=" + string.Join(",", smrs.Where(s => s.name.IndexOf("Baton", StringComparison.OrdinalIgnoreCase) >= 0).Select(s => s.name)));

        UnityEngine.Object.DestroyImmediate(instance);
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
        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", spec.Metallic);
        }

        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", spec.Smoothness);
        }

        if (mat.HasProperty("_Glossiness"))
        {
            mat.SetFloat("_Glossiness", spec.Smoothness);
        }

        if (spec.UseCamo)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
            }
            else
            {
                Debug.LogWarning("[MaskedSoldier] camo texture missing: " + TexturePath);
            }
        }

        if (spec.Woven && mat.HasProperty("_DetailNormalMap"))
        {
            mat.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(WeavePath));
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(7f, 7f));
            mat.SetFloat("_DetailNormalMapScale", 0.55f);
            mat.EnableKeyword("_DETAIL_MULX2");
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
