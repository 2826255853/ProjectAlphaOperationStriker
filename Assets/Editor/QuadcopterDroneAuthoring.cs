#if ENABLE_DRONE_CODE
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 四轴无人机模型（Assets/Models/QuadcopterDrone/QuadcopterDrone.fbx）的导入校验与预制体生成。
/// 模型由 Blender 脚本生成，中间件在桌面「陷阱素材文件夹\QuadcopterDrone」。
/// 本类只负责「资产是否正确接进 Unity」，不包含任何飞行/玩法逻辑。
/// </summary>
public static class QuadcopterDroneAuthoring
{
    public const string ModelPath = "Assets/Models/QuadcopterDrone/QuadcopterDrone.fbx";
    public const string PrefabPath = "Assets/Prefabs/QuadcopterDrone.prefab";

    /// <summary>旋翼枢轴节点名，飞行逻辑应绕这些节点的本地 up 轴自转。</summary>
    public static readonly string[] RotorNames = { "Rotor_FL", "Rotor_FR", "Rotor_RL", "Rotor_RR" };

    /// <summary>期望的枢轴位置（Unity 局部米制，Y 向上，+Z 为机头）。</summary>
    static readonly Vector3[] RotorExpectations =
    {
        new Vector3(-0.200f, 0.030f, 0.200f), // FL 前左
        new Vector3(0.200f, 0.030f, 0.200f),  // FR 前右
        new Vector3(-0.200f, 0.030f, -0.200f), // RL 后左
        new Vector3(0.200f, 0.030f, -0.200f), // RR 后右
    };

    [MenuItem("Tools/塔防/探针：无人机模型导入检查")]
    public static void ProbeModelMenu()
    {
        ProbeModel();
    }

    [MenuItem("Tools/塔防/探针：无人机预制体检查")]
    public static void ProbePrefabMenu()
    {
        ProbePrefab();
    }

    /// <summary>导入模型并核对朝向、枢轴与包围盒，报告写到 %TEMP%/alpha-striker-unity/quadcopter-drone/。</summary>
    public static void ProbeModel()
    {
        Probe(ModelPath, false, "model");
    }

    /// <summary>核对预制体：同一套枢轴契约，外加 URP 材质检查。</summary>
    public static void ProbePrefab()
    {
        Probe(PrefabPath, true, "prefab");
    }

    static void Probe(string assetPath, bool requireUrpMaterials, string label)
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
        {
            throw new FileNotFoundException("找不到资产: " + assetPath);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (instance == null)
        {
            instance = Object.Instantiate(asset);
        }

        var report = new StringBuilder();
        var ok = true;

        void Line(string text)
        {
            report.AppendLine(text);
            Debug.Log("[drone-probe] " + text);
        }

        void Check(string name, bool passed, string detail)
        {
            ok &= passed;
            Line((passed ? "PASS " : "FAIL ") + name + " :: " + detail);
        }

        try
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Check("renderer count", renderers.Length > 100, renderers.Length.ToString());

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            var size = bounds.size;
            Line("bounds size (x,y,z) = " + F(size.x) + ", " + F(size.y) + ", " + F(size.z));
            Check("span ~0.626 m",
                Mathf.Abs(size.x - 0.6264f) < 0.02f && Mathf.Abs(size.z - 0.6264f) < 0.02f,
                F(size.x) + " x " + F(size.z));
            Check("height ~0.192 m on Y (upright)", Mathf.Abs(size.y - 0.1923f) < 0.02f, F(size.y));
            Check("Y is the smallest axis", size.y < Mathf.Min(size.x, size.z),
                "size.y=" + F(size.y));

            var lens = FindDeep(instance.transform, "Camera_Lens");
            Check("camera lens exists", lens != null, lens != null ? lens.name : "missing");
            if (lens != null)
            {
                var localLens = instance.transform.InverseTransformPoint(lens.position);
                Check("nose points to +Z", localLens.z > 0.15f, "lens local z=" + F(localLens.z));
            }

            for (var i = 0; i < RotorNames.Length; i++)
            {
                var name = RotorNames[i];
                var rotor = FindDeep(instance.transform, name);
                if (rotor == null)
                {
                    Check(name + " pivot exists", false, "missing");
                    continue;
                }

                var local = instance.transform.InverseTransformPoint(rotor.position);
                var expected = RotorExpectations[i];
                Check(name + " pivot position",
                    Vector3.Distance(local, expected) < 0.003f,
                    "local=" + V(local) + " expected=" + V(expected));

                // 约定的自转轴：模型上方（世界 up）在枢轴局部空间里的方向。
                // 飞行代码应使用 rotor.Rotate(spinLocal, degrees, Space.Self) 或等价实现。
                var spinLocal = rotor.InverseTransformDirection(instance.transform.up);
                Check(name + " spin axis is model up",
                    Vector3.Dot(spinLocal, Vector3.up) > 0.999f,
                    "local axis=" + V(spinLocal));

                var blades = rotor.GetComponentsInChildren<Renderer>(true);
                var maxRadius = 0f;
                foreach (var blade in blades)
                {
                    foreach (var corner in BladeCorners(blade.bounds))
                    {
                        var localPoint = instance.transform.InverseTransformPoint(corner) - local;
                        maxRadius = Mathf.Max(maxRadius, new Vector2(localPoint.x, localPoint.z).magnitude);
                    }
                }

                Check(name + " blades inside guard ring", maxRadius < 0.108f,
                    "max radius=" + F(maxRadius));
                Check(name + " pivot has blades", blades.Length >= 6, blades.Length.ToString());

                // 真实自转测试：绕约定轴转 30°，桨盘中心不应平移
                var before = BladeCentre(blades, instance.transform);
                var savedRotation = rotor.localRotation;
                rotor.Rotate(spinLocal, 30f, Space.Self);
                var after = BladeCentre(blades, instance.transform);
                rotor.localRotation = savedRotation;
                Check(name + " spins in place", Vector3.Distance(before, after) < 0.01f,
                    "drift=" + F(Vector3.Distance(before, after)) + " m");
            }

            var tri = 0;
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                {
                    tri += mf.sharedMesh.triangles.Length / 3;
                }
            }

            Line("triangles = " + tri);

            var materials = instance.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Select(m => m.name + "@" + (m.shader != null ? m.shader.name : "null"))
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            Line("materials = " + string.Join(", ", materials));
            if (requireUrpMaterials)
            {
                Check("materials use URP shaders",
                    materials.All(m => m.Contains("Universal Render Pipeline")),
                    materials.Length + " distinct");
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        var dir = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "quadcopter-drone");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "probe-" + label + ".json");
        var json = "{\n  \"result\": \"" + (ok ? "PASS" : "FAIL") + "\",\n"
                   + "  \"asset\": \"" + assetPath + "\",\n"
                   + "  \"report\": [" + string.Join(", ", report.ToString()
                       .Split('\n')
                       .Where(l => !string.IsNullOrWhiteSpace(l))
                       .Select(l => "\"" + l.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]\n}";
        File.WriteAllText(path, json);
        Debug.Log("[drone-probe] report written to " + path + " result=" + (ok ? "PASS" : "FAIL"));

        if (ok)
        {
            Debug.Log("DRONE_PROBE_OK");
        }
        else
        {
            Debug.LogError("DRONE_PROBE_FAILED");
        }
    }

    [MenuItem("Tools/塔防/生成无人机预制体")]
    public static void CreateDronePrefabMenu()
    {
        CreateMaterialsAndPrefab();
    }

    /// <summary>FBX 自带内置 Standard 材质，URP 下是品红，所以预制体要换成下面生成的 URP 材质。</summary>
    public const string MaterialDir = "Assets/Models/QuadcopterDrone/Materials";

    struct MatSpec
    {
        public string Name;
        public Color BaseColor;
        public float Metallic;
        public float Smoothness;
        public Color Emission;
        public float EmissionIntensity;
    }

    static readonly MatSpec[] Palette =
    {
        new MatSpec
        {
            Name = "DR_WhiteShell", BaseColor = new Color(0.855f, 0.870f, 0.888f),
            Metallic = 0.05f, Smoothness = 0.72f, Emission = Color.black, EmissionIntensity = 0f,
        },
        new MatSpec
        {
            Name = "DR_OffWhite", BaseColor = new Color(0.480f, 0.500f, 0.522f),
            Metallic = 0.10f, Smoothness = 0.58f, Emission = Color.black, EmissionIntensity = 0f,
        },
        new MatSpec
        {
            Name = "DR_BlackCarbon", BaseColor = new Color(0.010f, 0.010f, 0.012f),
            Metallic = 0.08f, Smoothness = 0.60f, Emission = Color.black, EmissionIntensity = 0f,
        },
        new MatSpec
        {
            Name = "DR_DarkMetal", BaseColor = new Color(0.055f, 0.060f, 0.070f),
            Metallic = 0.95f, Smoothness = 0.70f, Emission = Color.black, EmissionIntensity = 0f,
        },
        new MatSpec
        {
            Name = "DR_CyanEmission", BaseColor = new Color(0.040f, 0.400f, 0.520f),
            Metallic = 0f, Smoothness = 0.75f,
            Emission = new Color(0.070f, 0.760f, 1.000f), EmissionIntensity = 6.5f,
        },
        new MatSpec
        {
            Name = "DR_LensGlass", BaseColor = new Color(0.004f, 0.010f, 0.018f),
            Metallic = 0.65f, Smoothness = 0.96f,
            Emission = new Color(0.030f, 0.180f, 0.240f), EmissionIntensity = 0.12f,
        },
    };

    /// <summary>生成 URP 材质，并把它们挂到模型预制体上（旋翼枢轴保持原样）。</summary>
    public static void CreateMaterialsAndPrefab()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (asset == null)
        {
            throw new FileNotFoundException("找不到无人机模型: " + ModelPath);
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[drone] 未找到 URP/Lit，退回内置 Standard 着色器");
            shader = Shader.Find("Standard");
        }

        Directory.CreateDirectory(MaterialDir);
        var materials = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var spec in Palette)
        {
            var path = MaterialDir + "/M_" + spec.Name + ".mat";
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

            if (spec.EmissionIntensity > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", spec.Emission * spec.EmissionIntensity);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            materials[spec.Name] = mat;
        }

        AssetDatabase.SaveAssets();

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        instance.name = "QuadcopterDrone";

        var remapped = 0;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var source = renderer.sharedMaterials;
            var target = new Material[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var sourceName = source[i] != null ? source[i].name : string.Empty;
                var key = materials.Keys.FirstOrDefault(k => sourceName.StartsWith(k))
                          ?? materials.Keys.FirstOrDefault(k => sourceName.Contains(k));
                if (key != null)
                {
                    target[i] = materials[key];
                    remapped++;
                }
                else
                {
                    target[i] = source[i];
                }
            }

            renderer.sharedMaterials = target;
        }

        var dir = Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[drone] prefab saved: " + PrefabPath + " materials remapped=" + remapped
                  + " shader=" + shader.name + " ok=" + (prefab != null));
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    static Vector3 BladeCentre(Renderer[] blades, Transform modelRoot)
    {
        if (blades.Length == 0)
        {
            return Vector3.zero;
        }

        var sum = Vector3.zero;
        foreach (var blade in blades)
        {
            sum += modelRoot.InverseTransformPoint(blade.bounds.center);
        }

        return sum / blades.Length;
    }

    static Vector3[] BladeCorners(Bounds b)
    {
        var c = b.center;
        var e = b.extents;
        return new[]
        {
            c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(-e.x, -e.y, e.z),
            c + new Vector3(-e.x, e.y, -e.z), c + new Vector3(-e.x, e.y, e.z),
            c + new Vector3(e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(e.x, e.y, -e.z), c + new Vector3(e.x, e.y, e.z),
        };
    }

    static string F(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
}

#endif
