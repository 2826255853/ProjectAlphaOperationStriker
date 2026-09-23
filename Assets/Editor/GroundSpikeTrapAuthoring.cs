using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Builds the ground spike prefab and renders its three operating poses.</summary>
public static class GroundSpikeTrapAuthoring
{
    public const string ModelPath = "Assets/Models/GroundSpike/GroundSpike.fbx";
    public const string PrefabPath = "Assets/Resources/GroundSpike.prefab";
    public const string DefinitionPath = "Assets/Resources/GroundSpike.asset";
    public static readonly string OutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "陷阱素材文件夹", "GroundSpike");

    public static void InspectExisting()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Existing ground spike prefab is missing.");
        foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            Debug.Log($"GroundSpike part: {child.name}, parent={child.parent?.name}, position={child.localPosition}, scale={child.localScale}");
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"GroundSpike bounds: {renderer.name} {renderer.bounds}");
    }

    [MenuItem("Tools/塔防/接入地刺陷阱预制体")]
    public static void CreateAssets()
    {
        Directory.CreateDirectory(OutputDirectory);
        string backup = Path.Combine(OutputDirectory, "Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { PrefabPath, DefinitionPath, "Assets/Models/GroundSpike/Spike.asset" })
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Import the final ground spike FBX before building: " + ModelPath);
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject root = Object.Instantiate(model);
        root.name = "GroundSpike";
        try
        {
            Transform group = FindSpikeGroup(root);
            AssignMaterials(root);
            group.localScale = Vector3.one;
            group.localPosition = Vector3.zero;
            float tip = 0f;
            foreach (MeshFilter mesh in group.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    tip = Mathf.Max(tip, root.transform.InverseTransformPoint(mesh.transform.TransformPoint(vertex)).y);
            if (tip <= 0f) throw new InvalidOperationException("Spike meshes have no positive height.");
            group.localScale = new Vector3(1f, 0.8f / tip, 1f);
            group.localPosition = new Vector3(0f, 0.07f - 0.8f, 0f);
            TrapInstance instance = root.GetComponent<TrapInstance>() ?? root.AddComponent<TrapInstance>();
            GroundSpikeTrap attack = root.GetComponent<GroundSpikeTrap>() ?? root.AddComponent<GroundSpikeTrap>();
            // Rebuilding the art must preserve Inspector tuning from the existing prefab.
            if (existing != null)
            {
                if (existing.TryGetComponent(out TrapInstance previousInstance)) EditorUtility.CopySerialized(previousInstance, instance);
                if (existing.TryGetComponent(out GroundSpikeTrap previousAttack)) EditorUtility.CopySerialized(previousAttack, attack);
            }
            var attackSettings = new SerializedObject(attack);
            attackSettings.FindProperty("spikeGroup").objectReferenceValue = group;
            attackSettings.ApplyModifiedPropertiesWithoutUndo();
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
            if (root.GetComponentInChildren<Collider>(true) == null)
            {
                BoxCollider trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0.1f, 0f);
                trigger.size = new Vector3(2f, 0.2f, 2f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Ground spike prefab could not be saved.");
        }
        finally { Object.DestroyImmediate(root); }

        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (definition == null)
        {
            // New definitions inherit TrapDefinition.DefaultCost. Rewiring an existing
            // asset below preserves its authored price, including an explicit free price.
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        var settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = "ground-spike";
        settings.FindProperty("displayName").stringValue = "地刺陷阱";
        settings.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        settings.FindProperty("walkableFloorTrap").boolValue = true;
        settings.FindProperty("worldFootprint").vector2Value = new Vector2(2f, 2f);
        settings.FindProperty("footprintWidth").intValue = 2;
        settings.FindProperty("footprintHeight").intValue = 2;
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("GroundSpike assets wired; backup: " + backup);
    }

    private static void AssignMaterials(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                string name = materials[i] != null ? materials[i].name : "Plate";
                Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/GroundSpike/" + name + ".mat");
                if (material == null) throw new InvalidOperationException("Missing ground spike material: " + name);
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    [MenuItem("Tools/塔防/预览地刺三态")]
    public static void RenderPreview()
    {
        Directory.CreateDirectory(OutputDirectory);
        // -nographics 批处理下 PreviewRenderUtility 只会渲染出纯色空图；这种情况下
        // 直接跳过，避免把上一次用显卡渲染好的三态预览覆盖成空白。
        // TODO(确认): 若以后需要在无显卡的 CI 里出预览，得改成离线渲染（如 Unity Recorder），
        //             目前选择保留上一次的有效 PNG，而不是产出空白图。
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Debug.LogWarning("GroundSpike preview skipped: no graphics device in this run, keeping the existing PNGs.");
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
            ground.transform.position = new Vector3(0f, -0.06f, 0f);
            ground.transform.localScale = new Vector3(3f, 0.1f, 3f);
            preview.AddSingleGO(ground);
            preview.camera.orthographic = false;
            preview.camera.fieldOfView = 38f;
            preview.camera.transform.position = new Vector3(4f, 3.4f, -4f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.2f, 0f));
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 30f;
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = new Color(0.13f, 0.16f, 0.2f);
            preview.lights[0].intensity = 1.5f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            preview.lights[1].intensity = 1f;
            preview.ambientColor = new Color(0.4f, 0.4f, 0.4f);
            Transform group = FindSpikeGroup(root);
            Vector3 rest = group.localPosition;
            string[] names = { "01-ready", "02-extended", "03-retracting" };
            var attackSettings = new SerializedObject(root.GetComponent<GroundSpikeTrap>());
            float height = attackSettings.FindProperty("extensionHeight").floatValue;
            float[] extensions = { 0f, height, height * 0.5f };
            // Warm up the preview scene once so the first saved frame has the same material state.
            for (int i = -1; i < names.Length; i++)
            {
                group.localPosition = rest + Vector3.up * extensions[Mathf.Max(0, i)];
                preview.BeginStaticPreview(new Rect(0f, 0f, 900f, 700f));
                preview.Render(true);
                Texture2D image = preview.EndStaticPreview();
                if (i >= 0) File.WriteAllBytes(Path.Combine(OutputDirectory, names[i] + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
        }
        finally
        {
            preview.Cleanup();
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
        }
        Debug.Log("GroundSpike previews saved: " + OutputDirectory);
    }

    public static void BuildAndPreview()
    {
        CreateAssets();
        RenderPreview();
    }

    private static Transform FindSpikeGroup(GameObject root)
    {
        Transform group = root.transform.Find("Spikes") ?? root.transform.Find("Spike Group");
        if (group == null) throw new InvalidOperationException("Missing independently movable spike group.");
        return group;
    }

    public static void BuildAndValidate()
    {
        BuildAndPreview();
        GroundSpikeTrapValidation.Run();
    }

    /// <summary>第五阶段验收：在既有校验之后追加跨系统回归（菜单、付费、通行、原有陷阱）。</summary>
    public static void BuildAndValidateStage5()
    {
        BuildAndValidate();
        GroundSpikeStage5Acceptance.Run();
    }
}