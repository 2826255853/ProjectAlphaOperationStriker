using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the in-game flamethrower prefab from the authored FBX and keeps the
/// existing gameplay wiring (TrapInstance + FlamethrowerTrap + feedback mounts).
/// The FBX stays visual-only: no colliders and no rigidbodies are added.
/// </summary>
public static class FlamethrowerModelAuthoring
{
    public const string ModelPath = "Assets/Models/Flamethrower/Flamethrower.fbx";
    public const string MaterialFolder = "Assets/Models/Flamethrower/Materials";
    public const string ModelRootName = "Model";

    /// <summary>
    /// The modelled concrete pad sits behind the mounting plane, so the whole
    /// model is pushed forward by this much to keep the pad visible instead of
    /// buried inside the map wall.
    /// </summary>
    public const float WallOffset = 0.08f;

    /// <summary>Width the model is normalised to, in metres (2x1 m trap cells).</summary>
    public const float TargetWidth = 1.82f;

    private readonly struct MaterialSpec
    {
        public readonly Color BaseColor;
        public readonly float Metallic;
        public readonly float Smoothness;

        public MaterialSpec(float r, float g, float b, float metallic, float smoothness)
        {
            BaseColor = new Color(r, g, b, 1f);
            Metallic = metallic;
            Smoothness = smoothness;
        }
    }

    // Keys are the Blender material names carried by the FBX. The FBX only ships
    // built-in Standard materials, which render magenta under URP, so every slot
    // is replaced with a generated URP Lit material.
    private static readonly Dictionary<string, MaterialSpec> Specs =
        new Dictionary<string, MaterialSpec>
        {
            { "Concrete", new MaterialSpec(0.300f, 0.298f, 0.285f, 0.00f, 0.08f) },
            { "SteelPlate", new MaterialSpec(0.360f, 0.370f, 0.378f, 0.25f, 0.40f) },
            { "DarkFrame", new MaterialSpec(0.040f, 0.043f, 0.048f, 0.30f, 0.45f) },
            { "WhiteArmor", new MaterialSpec(0.800f, 0.798f, 0.775f, 0.05f, 0.58f) },
            { "WhiteArmorWorn", new MaterialSpec(0.610f, 0.608f, 0.582f, 0.05f, 0.40f) },
            { "HazardOrange", new MaterialSpec(0.900f, 0.290f, 0.015f, 0.00f, 0.56f) },
            { "GunMetal", new MaterialSpec(0.105f, 0.110f, 0.118f, 0.55f, 0.60f) },
            { "BlackRubber", new MaterialSpec(0.018f, 0.018f, 0.020f, 0.00f, 0.12f) },
            { "NozzleBore", new MaterialSpec(0.004f, 0.004f, 0.005f, 0.20f, 0.40f) },
            { "VentSlat", new MaterialSpec(0.185f, 0.190f, 0.198f, 0.45f, 0.55f) },
        };

    [MenuItem("Tools/塔防/生成火焰喷射器模型预制体")]
    public static void CreateModelPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
            throw new System.InvalidOperationException("找不到模型：" + ModelPath + "（先跑 Blender 脚本导出 FBX）");

        // Repair the definition first so a missing prefab gets created before we
        // open its contents for editing.
        FlamethrowerTrapAuthoring.CreateAssets();
        GameObject root = PrefabUtility.LoadPrefabContents(FlamethrowerTrapAuthoring.PrefabPath);
        try
        {
            if (root.GetComponent<TrapInstance>() == null) root.AddComponent<TrapInstance>();
            if (root.GetComponent<FlamethrowerTrap>() == null) root.AddComponent<FlamethrowerTrap>();
            if (root.GetComponent<FlamethrowerFeedback>() == null) root.AddComponent<FlamethrowerFeedback>();

            RemoveGrayboxVisuals(root.transform);

            Transform container = root.transform.Find(ModelRootName);
            if (container == null)
            {
                container = new GameObject(ModelRootName).transform;
                container.SetParent(root.transform, false);
            }
            for (int i = container.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(container.GetChild(i).gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            if (instance == null)
                instance = Object.Instantiate(modelAsset);
            instance.name = "Flamethrower Mesh";
            instance.transform.SetParent(container, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            FitToInstallBounds(container);
            AssignMaterials(container);
            RemoveColliders(container);

            Bounds bounds = MeasureBounds(container);
            Transform flame = EnsureMount(root.transform, "Flame Origin",
                new Vector3(0f, 0f, bounds.max.z + 0.02f));
            EnsureMount(root.transform, "Attack Origin", Vector3.zero);
            foreach (string fxName in new[] { "Preheat Fx", "Spray Fx", "Cooldown Fx" })
            {
                Transform fx = EnsureMount(flame, fxName, Vector3.zero);
                fx.localRotation = Quaternion.identity;
                fx.localScale = Vector3.one;
            }
            // A stale graybox arrow must never sit inside the finished model.
            Transform staleArrow = root.transform.Find(FlamethrowerTrapAuthoring.DirectionName);
            if (staleArrow != null) Object.DestroyImmediate(staleArrow.gameObject);

            PrefabUtility.SaveAsPrefabAsset(root, FlamethrowerTrapAuthoring.PrefabPath, out bool saved);
            if (!saved) throw new System.InvalidOperationException("保存火焰喷射器预制体失败");

            TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(
                FlamethrowerTrapAuthoring.DefinitionPath);
            FlamethrowerTrapAuthoring.ConfigureDefinition(definition, rootAsset());
            UpdateInstallBounds(definition, bounds);
            AssetDatabase.SaveAssets();
            Debug.Log($"FLAMETHROWER_MODEL: flame={flame.localPosition} bounds={bounds.size} min={bounds.min} max={bounds.max}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.Refresh();
    }

    public static void CreateModelPrefabBatch() => CreateModelPrefab();

    /// <summary>
    /// Renders the generated prefab with the real URP materials so the model can
    /// be reviewed without opening the map scene.
    /// </summary>
    [MenuItem("Tools/塔防/预览火焰喷射器模型")]
    public static void RenderModelPreview()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new System.InvalidOperationException("请在带图形界面的批处理编辑器里运行。");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoring.PrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("请先生成火焰喷射器模型预制体。");

        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        var root = new GameObject("Flamethrower preview");
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetParent(root.transform, false);

        Material wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMaterial.color = new Color(0.13f, 0.14f, 0.16f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Preview wall";
        Object.DestroyImmediate(wall.GetComponent<Collider>());
        wall.transform.SetParent(root.transform, false);
        wall.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        wall.transform.localScale = new Vector3(3.4f, 3.4f, 0.10f);
        wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.26f, 0.28f, 0.33f);
        RenderSettings.fog = false;

        Camera camera = new GameObject("Preview camera").AddComponent<Camera>();
        camera.transform.SetParent(root.transform, false);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60f;

        Light key = new GameObject("Preview key").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 2.1f;
        key.color = new Color(1f, 0.97f, 0.92f);
        key.transform.rotation = Quaternion.Euler(24f, 152f, 0f);

        Light fill = new GameObject("Preview fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.7f;
        fill.color = new Color(0.70f, 0.78f, 0.92f);
        fill.transform.rotation = Quaternion.Euler(8f, -35f, 0f);

        Light rim = new GameObject("Preview rim").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.intensity = 1.1f;
        rim.color = new Color(0.95f, 0.92f, 1f);
        rim.transform.rotation = Quaternion.Euler(12f, 12f, 0f);

        string folder = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "Flamethrower");
        Directory.CreateDirectory(folder);

        // URP needs one render to upload light data; without this warm-up the
        // first saved view comes out flat and desaturated.
        string warmup = Path.Combine(folder, "_warmup.png");
        RenderView(camera, warmup, new Vector3(2.35f, 1.30f, 3.65f), new Vector3(0f, 0.02f, 0.85f));
        if (File.Exists(warmup)) File.Delete(warmup);

        RenderView(camera, Path.Combine(folder, "flamethrower_unity_three_quarter.png"),
            new Vector3(2.35f, 1.30f, 3.65f), new Vector3(0f, 0.02f, 0.85f));
        RenderView(camera, Path.Combine(folder, "flamethrower_unity_front.png"),
            new Vector3(0.05f, 0.02f, 4.05f), new Vector3(0f, 0.02f, 0.90f));
        RenderView(camera, Path.Combine(folder, "flamethrower_unity_side.png"),
            new Vector3(3.65f, 0.10f, 0.55f), new Vector3(0f, 0.02f, 0.55f));
        Debug.Log("FLAMETHROWER_PREVIEW: " + folder);
    }

    private static void RenderView(Camera camera, string path, Vector3 position, Vector3 target)
    {
        camera.transform.position = position;
        camera.transform.LookAt(target);
        var targetTexture = new RenderTexture(1200, 1200, 24);
        var texture = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
        camera.targetTexture = targetTexture;
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.Render();
            RenderTexture.active = targetTexture;
            texture.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(targetTexture);
        }
        Debug.Log("FLAMETHROWER_PREVIEW_SAVED: " + path);
    }

    private static GameObject rootAsset()
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(FlamethrowerTrapAuthoring.PrefabPath);
    }

    /// <summary>Orients, centres and scales the imported mesh so barrels face +Z.</summary>
    private static void FitToInstallBounds(Transform container)
    {
        // Regeneration must start from a clean transform or the previous run's
        // rotation/scale compounds and the facing check flips at random.
        container.localPosition = Vector3.zero;
        container.localRotation = Quaternion.identity;
        container.localScale = Vector3.one;

        Bounds raw = MeasureBounds(container);
        Debug.Log($"FLAMETHROWER_MODEL_RAW: size={raw.size} center={raw.center}");

        // A Z-up import would land the 1.8 m width on Y; stand it back up.
        if (raw.size.y < raw.size.x && raw.size.y < raw.size.z)
        {
            container.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            raw = MeasureBounds(container);
        }

        // Barrels must point forward: flip until they sit in front of the
        // concrete pad, rather than trusting FBX node order or axis conventions.
        if (!FacesForward(container))
        {
            container.localRotation = container.localRotation * Quaternion.Euler(0f, 180f, 0f);
            raw = MeasureBounds(container);
        }

        float scale = TargetWidth / Mathf.Max(0.0001f, raw.size.x);
        container.localScale = Vector3.one * scale;

        Bounds scaled = MeasureBounds(container);
        container.position += new Vector3(
            -scaled.center.x,
            -scaled.center.y,
            WallOffset - scaled.min.z);

        Debug.Log($"FLAMETHROWER_ORIENT: barrelsZ={ZOf(container, "Barrel")} slabZ={ZOf(container, "Slab")} facesForward={FacesForward(container)}");
    }

    /// <summary>True when the barrel group is in front of (+Z) the concrete pad.</summary>
    private static bool FacesForward(Transform container)
    {
        Renderer slab = FindRenderer(container, "Slab") ?? LargestFlatRenderer(container);
        Renderer barrel = FindRenderer(container, "Barrel");
        if (slab == null || barrel == null) return true;
        return barrel.bounds.center.z > slab.bounds.center.z;
    }

    private static float ZOf(Transform container, string token)
    {
        Renderer r = FindRenderer(container, token);
        return r == null ? 0f : Mathf.Round(r.bounds.center.z * 100f) / 100f;
    }

    private static Renderer FindRenderer(Transform root, string token)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
        }
        return null;
    }

    private static Bounds MeasureBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer || !renderer.gameObject.activeInHierarchy) continue;
            if (!has)
            {
                bounds = renderer.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return has ? bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    /// <summary>The widest, tallest renderer is the concrete mounting pad.</summary>
    private static Renderer LargestFlatRenderer(Transform root)
    {
        Renderer best = null;
        float bestArea = 0f;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || !renderer.gameObject.activeInHierarchy) continue;
            Vector3 size = renderer.bounds.size;
            float area = size.x * size.y;
            if (area <= bestArea) continue;
            bestArea = area;
            best = renderer;
        }
        return best;
    }

    private static void AssignMaterials(Transform container)
    {
        Dictionary<string, Material> cache = new Dictionary<string, Material>();
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                string key = slots[i].name.Replace(" (Instance)", string.Empty);
                if (!Specs.TryGetValue(key, out MaterialSpec spec)) continue;
                if (!cache.TryGetValue(key, out Material material))
                {
                    material = EnsureMaterial(key, spec);
                    cache[key] = material;
                }
                slots[i] = material;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = slots;
        }
    }

    private static Material EnsureMaterial(string name, MaterialSpec spec)
    {
        Directory.CreateDirectory(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) throw new System.InvalidOperationException("找不到 URP Lit 着色器");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.shader != shader) material.shader = shader;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", spec.BaseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", spec.BaseColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", spec.Metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", spec.Smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", spec.Smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Wall traps get their solid collider from placement; visuals stay physics-free.</summary>
    private static void RemoveColliders(Transform container)
    {
        foreach (Collider collider in container.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
        foreach (Rigidbody body in container.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(body);
    }

    /// <summary>Removes the placeholder cube/cylinder visuals from the graybox stage.</summary>
    private static void RemoveGrayboxVisuals(Transform root)
    {
        foreach (string name in new[] { "Wall Backplate", "Flame Nozzle" })
        {
            Transform stale = root.Find(name);
            if (stale != null) Object.DestroyImmediate(stale.gameObject);
        }
    }

    private static Transform EnsureMount(Transform parent, string name, Vector3 position)
    {
        Transform mount = parent.Find(name);
        if (mount == null)
        {
            mount = new GameObject(name).transform;
            mount.SetParent(parent, false);
        }
        mount.localPosition = position;
        return mount;
    }

    private static Transform FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    /// <summary>Install box matches the finished mesh so placement never clips the model.</summary>
    private static void UpdateInstallBounds(TrapDefinition definition, Bounds bounds)
    {
        if (definition == null) return;
        var settings = new SerializedObject(definition);
        settings.FindProperty("installBoundsCenter").vector3Value =
            new Vector3(0f, 0f, bounds.center.z);
        settings.FindProperty("installBoundsSize").vector3Value = new Vector3(
            Mathf.Max(1.6f, bounds.size.x),
            Mathf.Max(1.6f, bounds.size.y),
            bounds.size.z);
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }
}
