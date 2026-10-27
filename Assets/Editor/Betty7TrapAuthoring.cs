using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the Betty No.7 (贝蒂7号) trap assets from the final FBX, and renders
/// previews into the desktop material folder. Mirrors the other trap authoring
/// scripts: the FBX is the source of truth, everything else is generated.
/// </summary>
public static class Betty7TrapAuthoring
{
    public const string ModelPath = "Assets/Models/Betty7/Betty7.fbx";
    public const string PrefabPath = "Assets/Prefabs/Betty7.prefab";
    public const string DefinitionPath = "Assets/Resources/Betty7.asset";
    public const string MaterialDirectory = "Assets/Models/Betty7/Materials";
    public const string BasePartName = "Betty7_Base";
    public const string DomePartName = "Detector_Dome";
    public const string MinePartPrefix = "Mine_";
    public const string MineGroupName = "Mines";

    public static readonly string OutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "陷阱素材文件夹", "Betty7");

    /// <summary>Blender material name -> URP Lit settings (kept in sync with build_betty7.py).</summary>
    private static readonly (string Name, string Hex, float Metallic, float Smoothness, float Emission)[] MaterialSpecs =
    {
        ("ArmorPlate", "#4B5461", 0.72f, 0.54f, 0f),
        ("ArmorDark", "#22262D", 0.66f, 0.42f, 0f),
        ("SafetyTrim", "#E2701C", 0.30f, 0.62f, 0.55f),
        ("DetectorGlass", "#39DCEF", 0.00f, 0.88f, 2.6f),
        ("MineBody", "#A2501D", 0.58f, 0.60f, 0f),
        ("Steel", "#98A1A9", 0.88f, 0.72f, 0f),
    };

    [MenuItem("Tools/塔防/接入贝蒂7号陷阱预制体")]
    public static void CreateAssets()
    {
        Directory.CreateDirectory(OutputDirectory);
        string backup = Path.Combine(OutputDirectory, "Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { PrefabPath, DefinitionPath })
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));

        AssetDatabase.Refresh();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException("Import the final Betty7 FBX first: " + ModelPath);

        Dictionary<string, Material> materials = EnsureMaterials();
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject root = Object.Instantiate(model);
        root.name = "Betty7";
        try
        {
            Transform basePart = FindPart(root.transform, BasePartName, required: true);
            Transform dome = FindPart(root.transform, DomePartName, required: true);
            List<Transform> mines = CollectMines(root.transform);
            if (mines.Count != 4)
                throw new InvalidOperationException("Expected exactly four mine parts, found " + mines.Count + ".");

            // Keep the canisters under one owned group so the runtime can move
            // them without touching the plate hierarchy.
            var minesGroup = new GameObject(MineGroupName);
            minesGroup.transform.SetParent(basePart.parent != null ? basePart.parent : root.transform, false);
            foreach (Transform mine in mines)
            {
                Vector3 world = mine.position;
                Quaternion rotation = mine.rotation;
                mine.SetParent(minesGroup.transform, true);
                mine.position = world;
                mine.rotation = rotation;
            }

            ApplyMaterials(root, materials);

            TrapInstance instance = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
            Betty7JumpMineTrap logic = root.GetComponent<Betty7JumpMineTrap>() ?? root.AddComponent<Betty7JumpMineTrap>();
            // Rebuilding the art must preserve Inspector tuning from an older prefab.
            if (existing != null)
            {
                if (existing.TryGetComponent(out TrapInstance previousInstance)) EditorUtility.CopySerialized(previousInstance, instance);
                if (existing.TryGetComponent(out Betty7JumpMineTrap previousLogic)) EditorUtility.CopySerialized(previousLogic, logic);
            }

            var settings = new SerializedObject(logic);
            SerializedProperty mineArray = settings.FindProperty("mineRoots");
            mineArray.arraySize = mines.Count;
            for (int i = 0; i < mines.Count; i++) mineArray.GetArrayElementAtIndex(i).objectReferenceValue = mines[i];
            settings.FindProperty("detectorDome").objectReferenceValue = dome;
            settings.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Betty7 prefab could not be saved.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        WireDefinition();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Betty7 assets wired: " + PrefabPath + " / " + DefinitionPath + " (backup: " + backup + ")");
    }

    /// <summary>Batch entry point for <c>-executeMethod</c>.</summary>
    public static void CreateAssetsBatch() => CreateAssets();

    public static void BuildAndPreview()
    {
        CreateAssets();
        RenderPreview();
    }

    private static void WireDefinition()
    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (definition == null)
        {
            // A fresh definition inherits TrapDefinition.DefaultCost; rewiring an
            // existing asset below keeps whatever price was authored.
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        var settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = "betty7";
        settings.FindProperty("displayName").stringValue = "贝蒂7号";
        settings.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        settings.FindProperty("walkableFloorTrap").boolValue = true;
        settings.FindProperty("mountType").intValue = (int)TrapMountType.Ground;
        settings.FindProperty("worldFootprint").vector2Value = new Vector2(2f, 2f);
        settings.FindProperty("footprintWidth").intValue = 2;
        settings.FindProperty("footprintHeight").intValue = 2;
        SerializedProperty cost = settings.FindProperty("cost");
        // TODO(确认): 60 是首版平衡值。新建的定义会带着 TrapDefinition.DefaultCost(=40)，
        // 所以只在“仍是默认价”时才写入，避免覆盖后续手调的价格。
        if (cost.intValue == TrapDefinition.DefaultCost) cost.intValue = 60;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }

    private static Dictionary<string, Material> EnsureMaterials()
    {
        Directory.CreateDirectory(MaterialDirectory);
        AssetDatabase.Refresh();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (lit == null) throw new InvalidOperationException("No usable lit shader found for Betty7 materials.");

        var result = new Dictionary<string, Material>();
        foreach (var spec in MaterialSpecs)
        {
            string path = MaterialDirectory + "/" + spec.Name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = spec.Name };
                AssetDatabase.CreateAsset(material, path);
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            if (material.shader != lit) material.shader = lit;

            Color baseColor = ParseHex(spec.Hex);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", spec.Metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", spec.Smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", spec.Smoothness);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", baseColor * Mathf.Max(0f, spec.Emission));
                material.globalIlluminationFlags = spec.Emission > 0f
                    ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                    : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(material);
            result[spec.Name] = material;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    private static void ApplyMaterials(GameObject root, Dictionary<string, Material> materials)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                string key = slots[i] != null ? slots[i].name : "ArmorPlate";
                // FBX import appends suffixes or a Material name in some cases.
                foreach (var spec in MaterialSpecs)
                {
                    if (key.StartsWith(spec.Name, StringComparison.Ordinal)) { key = spec.Name; break; }
                }
                if (!materials.TryGetValue(key, out Material material))
                    throw new InvalidOperationException("No Betty7 material generated for FBX slot: " + key);
                slots[i] = material;
            }
            renderer.sharedMaterials = slots;
        }
    }

    private static Transform FindPart(Transform root, string name, bool required)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child != root && child.name.StartsWith(name, StringComparison.Ordinal)) return child;
        if (required) throw new InvalidOperationException("Missing Betty7 model part: " + name);
        return null;
    }

    private static List<Transform> CollectMines(Transform root)
    {
        var mines = new List<Transform>();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == root) continue;
            if (!child.name.StartsWith(MinePartPrefix, StringComparison.Ordinal)) continue;
            // The grouped empties also start with the prefix; only keep meshes.
            if (child.GetComponent<MeshFilter>() == null) continue;
            mines.Add(child);
        }
        mines.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return mines;
    }

    private static Color ParseHex(string hex)
    {
        string value = hex.TrimStart('#');
        byte r = Convert.ToByte(value.Substring(0, 2), 16);
        byte g = Convert.ToByte(value.Substring(2, 2), 16);
        byte b = Convert.ToByte(value.Substring(4, 2), 16);
        return new Color32(r, g, b, 255);
    }

    /// <summary>Dumps the prefab hierarchy, materials and bounds for verification.</summary>
    [MenuItem("Tools/塔防/检查贝蒂7号预制体")]
    public static void ProbePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Betty7 prefab is missing: " + PrefabPath);
        foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            Debug.Log($"Betty7 part: {child.name}, parent={child.parent?.name}, " +
                      $"localPosition={child.localPosition}, localScale={child.localScale}");
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"Betty7 renderer: {renderer.name}, material={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "<none>")}, " +
                      $"shader={(renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "<none>")}");
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            Debug.Log($"Betty7 collider: {collider.name}, isTrigger={collider.isTrigger}");
        var logic = prefab.GetComponent<Betty7JumpMineTrap>();
        Debug.Log($"Betty7 wiring: TrapInstance={prefab.GetComponent<TrapInstance>() != null}, Betty7JumpMineTrap={logic != null}");
    }

    [MenuItem("Tools/塔防/预览贝蒂7号陷阱")]
    public static void RenderPreview()
    {
        Directory.CreateDirectory(OutputDirectory);
        // -nographics renders an empty frame; keep the previous PNGs instead of
        // overwriting them with blanks.
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Debug.LogWarning("Betty7 preview skipped: no graphics device in this run.");
            return;
        }

        var preview = new PreviewRenderUtility();
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        try
        {
            preview.AddSingleGO(root);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(4f, 0.1f, 4f);
            preview.AddSingleGO(ground);

            preview.camera.orthographic = false;
            preview.camera.fieldOfView = 38f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 40f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.70f, 0.72f, 0.74f);
            preview.lights[0].intensity = 1.6f;
            preview.lights[0].transform.rotation = Quaternion.Euler(48f, 32f, 0f);
            preview.lights[1].intensity = 0.7f;
            preview.lights[1].transform.rotation = Quaternion.Euler(20f, -140f, 0f);
            preview.ambientColor = new Color(0.42f, 0.44f, 0.46f);

            Transform minesGroup = root.transform.Find(MineGroupName);
            var restPositions = new List<Vector3>();
            var mines = new List<Transform>();
            if (minesGroup != null)
                foreach (Transform child in minesGroup) { mines.Add(child); restPositions.Add(child.localPosition); }

            var logicSettings = new SerializedObject(root.GetComponent<Betty7JumpMineTrap>());
            float launchHeight = logicSettings.FindProperty("launchHeight").floatValue;

            RenderPose(preview, root, "preview_unity_hero", new Vector3(2.6f, 2.1f, -2.9f), new Vector3(0f, 0.22f, 0f), false);
            RenderPose(preview, root, "preview_unity_top", new Vector3(0f, 4.4f, 0f), Vector3.zero, true);

            for (int i = 0; i < mines.Count; i++)
                mines[i].localPosition = restPositions[i] + Vector3.up * launchHeight;
            RenderPose(preview, root, "preview_unity_launch", new Vector3(3.2f, 3.0f, -3.6f), new Vector3(0f, 0.9f, 0f), false);
        }
        finally
        {
            preview.Cleanup();
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            Object.DestroyImmediate(root);
        }
        Debug.Log("Betty7 previews saved to: " + OutputDirectory);
    }

    private static void RenderPose(PreviewRenderUtility preview, GameObject root, string fileName,
        Vector3 cameraPosition, Vector3 lookAt, bool orthographic)
    {
        preview.camera.orthographic = orthographic;
        if (orthographic) preview.camera.orthographicSize = 1.35f;
        preview.camera.transform.position = cameraPosition;
        preview.camera.transform.LookAt(lookAt);

        // Warm-up frame keeps the first saved image at the same material state.
        preview.BeginStaticPreview(new Rect(0f, 0f, 1000f, 700f));
        preview.Render(true);
        Texture2D warmup = preview.EndStaticPreview();
        Object.DestroyImmediate(warmup);

        preview.BeginStaticPreview(new Rect(0f, 0f, 1000f, 700f));
        preview.Render(true);
        Texture2D image = preview.EndStaticPreview();
        File.WriteAllBytes(Path.Combine(OutputDirectory, fileName + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
        Debug.Log("Betty7 preview written: " + fileName + ".png");
    }
}
