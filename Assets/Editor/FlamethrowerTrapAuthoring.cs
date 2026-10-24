using UnityEditor;
using UnityEngine;

/// <summary>Owns the wall definition and replaceable graybox visuals, not attack tuning.</summary>
public static class FlamethrowerTrapAuthoring
{
    public const string DefinitionPath = "Assets/Resources/Flamethrower.asset";
    public const string PrefabPath = "Assets/Resources/Flamethrower.prefab";
    public const string TrapId = "flamethrower";
    public const string DirectionName = "Graybox Direction";

    // Solid housing/nozzle only. The four-metre attack volume must never be
    // included in placement/support/overlap checks. These values are the
    // fallback for a definition repair; generating the model prefab re-measures
    // the real mesh and writes the result back into the definition.
    public static readonly Vector3 InstallBoundsCenter = new Vector3(0f, 0f, 0.452f);
    public static readonly Vector3 InstallBoundsSize = new Vector3(1.82f, 1.82f, 0.744f);

    [MenuItem("Tools/塔防/生成火焰喷射器墙面定义")]
    public static void CreateAssets()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        bool created = definition == null;
        if (created) definition = ScriptableObject.CreateInstance<TrapDefinition>();
        // Keep later authored prefabs and prices intact when repairing the definition.
        GameObject prefab = definition.Prefab != null ? definition.Prefab : EnsurePrefab();
        ConfigureDefinition(definition, prefab);
        if (created)
        {
            // TODO(确认): 沿用项目调试价 40 金币，灰盒平衡阶段再确定正式价格。
            var settings = new SerializedObject(definition);
            settings.FindProperty("cost").intValue = TrapDefinition.DefaultCost;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Flamethrower wall definition generated: " + DefinitionPath);
    }

    public static void CreateAssetsBatch() => CreateAssets();

    [MenuItem("Tools/塔防/生成火焰喷射器灰盒预制体")]
    public static void CreateGraybox()
    {
        CreateAssets();
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        // The canonical prefab now carries the modelled visuals; rebuilding the
        // placeholder over it would throw the finished art away.
        if (existing != null && existing.transform.Find("Model") != null)
            throw new System.InvalidOperationException(
                "当前预制体已使用火焰喷射器模型，灰盒生成不再覆盖它。要重做模型请用 Tools/塔防/生成火焰喷射器模型预制体。");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Keep authored runtime settings and additional children when rebuilding visuals.
            if (root.GetComponent<TrapInstance>() == null) root.AddComponent<TrapInstance>();
            if (root.GetComponent<FlamethrowerTrap>() == null) root.AddComponent<FlamethrowerTrap>();
            Material body = EnsureMaterial("FlamethrowerGrayboxBody", new Color(0.34f, 0.39f, 0.44f));
            Material nozzleMaterial = EnsureMaterial("FlamethrowerMaterial", new Color(0.95f, 0.16f, 0.03f));
            Material arrowMaterial = EnsureMaterial("FlamethrowerGrayboxDirection", new Color(1f, 0.8f, 0.08f));
            SetVisual(root.transform, PrimitiveType.Cube, "Wall Backplate", body,
                new Vector3(0f, 0f, 0.06f), new Vector3(1.8f, 1.8f, 0.12f), Quaternion.identity);
            SetVisual(root.transform, PrimitiveType.Cylinder, "Flame Nozzle", nozzleMaterial,
                new Vector3(0f, 0f, 0.22f), new Vector3(0.22f, 0.18f, 0.22f), Quaternion.Euler(90f, 0f, 0f));
            // The direction marker is visible geometry inside the solid mounting box,
            // not a four-metre beam that could affect placement or bounds inference.
            Transform arrow = EnsureMount(root.transform, DirectionName, new Vector3(0.55f, 0f, 0f));
            SetVisual(arrow, PrimitiveType.Cube, "Shaft", arrowMaterial,
                new Vector3(0f, 0f, 0.23f), new Vector3(0.035f, 0.035f, 0.2f), Quaternion.identity);
            SetVisual(arrow, PrimitiveType.Cube, "Tip Left", arrowMaterial,
                new Vector3(-0.04f, 0f, 0.34f), new Vector3(0.025f, 0.04f, 0.14f), Quaternion.Euler(0f, 45f, 0f));
            SetVisual(arrow, PrimitiveType.Cube, "Tip Right", arrowMaterial,
                new Vector3(0.04f, 0f, 0.34f), new Vector3(0.025f, 0.04f, 0.14f), Quaternion.Euler(0f, -45f, 0f));
            // Attack coordinates still start at the trap root. Future flame visuals
            // start at the nozzle exit but use the same local +Z direction.
            EnsureMount(root.transform, "Attack Origin", Vector3.zero);
            Transform flame = EnsureMount(root.transform, "Flame Origin", new Vector3(0f, 0f, 0.4f));
            EnsureMount(flame, "Preheat Fx", Vector3.zero);
            EnsureMount(flame, "Spray Fx", Vector3.zero);
            EnsureMount(flame, "Cooldown Fx", Vector3.zero);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new System.InvalidOperationException("Could not save flamethrower graybox.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("FLAMETHROWER_STEP4_GENERATED: " + PrefabPath);
    }

    private static Material EnsureMaterial(string name, Color color)
    {
        string path = "Assets/Resources/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) throw new System.InvalidOperationException("No shader for flamethrower graybox.");
        material = new Material(shader) { name = name, color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SetVisual(Transform parent, PrimitiveType type, string name, Material material,
        Vector3 position, Vector3 size, Quaternion rotation)
    {
        Transform visual = parent.Find(name);
        if (visual == null) visual = CreateVisual(type, name, parent, material);
        visual.localPosition = position;
        visual.localRotation = rotation;
        visual.localScale = size;
        visual.GetComponent<Renderer>().sharedMaterial = material;
        foreach (Collider collider in visual.GetComponents<Collider>()) Object.DestroyImmediate(collider);
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
        mount.localRotation = Quaternion.identity;
        mount.localScale = Vector3.one;
        return mount;
    }

    // Definition repair creates a minimal body only when missing. Graybox
    // regeneration is an explicit, separate entry point.
    private static GameObject EnsurePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null) return prefab;
        const string materialPath = "Assets/Resources/FlamethrowerMaterial.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new System.InvalidOperationException("No shader for flamethrower placeholder.");
            material = new Material(shader) { name = "Flamethrower", color = new Color(0.95f, 0.16f, 0.03f) };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var root = new GameObject("Flamethrower");
        try
        {
            root.AddComponent<TrapInstance>();
            root.AddComponent<FlamethrowerTrap>();
            Transform plate = CreateVisual(PrimitiveType.Cube, "Wall Backplate", root.transform, material);
            plate.localPosition = new Vector3(0f, 0f, 0.06f);
            plate.localScale = new Vector3(1.72f, 1.72f, 0.12f);
            Transform nozzle = CreateVisual(PrimitiveType.Cylinder, "Flame Nozzle", root.transform, material);
            nozzle.localPosition = new Vector3(0f, 0f, 0.22f);
            nozzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nozzle.localScale = new Vector3(0.22f, 0.18f, 0.22f);
            prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null) throw new System.InvalidOperationException("Could not save flamethrower prefab.");
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Transform CreateVisual(PrimitiveType type, string name, Transform parent, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(type);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.GetComponent<Renderer>().sharedMaterial = material;
        // Placement owns the solid installation collider; attack VFX never do.
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        return visual.transform;
    }

    public static void ConfigureDefinition(TrapDefinition definition, GameObject prefab)
    {
        if (definition == null) throw new System.ArgumentNullException(nameof(definition));
        if (prefab == null) throw new System.ArgumentNullException(nameof(prefab));

        // A model-built prefab measures its own solid housing; only the legacy
        // graybox falls back to the constants above.
        Vector3 center = InstallBoundsCenter;
        Vector3 size = InstallBoundsSize;
        Transform model = prefab.transform.Find(FlamethrowerModelAuthoring.ModelRootName);
        if (model != null && TryMeasureBounds(model, prefab.transform, out Bounds measured))
        {
            center = new Vector3(0f, 0f, measured.center.z);
            size = new Vector3(Mathf.Max(1.6f, measured.size.x), Mathf.Max(1.6f, measured.size.y),
                Mathf.Max(0.05f, measured.size.z));
        }

        var settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = TrapId;
        settings.FindProperty("displayName").stringValue = "火焰喷射器";
        settings.FindProperty("prefab").objectReferenceValue = prefab;
        settings.FindProperty("footprintWidth").intValue = 2;
        settings.FindProperty("footprintHeight").intValue = 2;
        settings.FindProperty("localRotation").vector3Value = Vector3.zero;
        settings.FindProperty("walkableFloorTrap").boolValue = false;
        // Wall footprint is expressed in the existing grid's local X/Y cells.
        settings.FindProperty("worldFootprint").vector2Value = Vector2.zero;
        settings.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Wall;
        settings.FindProperty("installBoundsCenter").vector3Value = center;
        settings.FindProperty("installBoundsSize").vector3Value = size;
        settings.FindProperty("skipMonsterClearanceCheck").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }

    /// <summary>Renderer bounds of a child hierarchy, expressed in the prefab root space.</summary>
    private static bool TryMeasureBounds(Transform model, Transform root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool has = false;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Bounds world = renderer.bounds;
            Vector3 min = root.InverseTransformPoint(world.min);
            Vector3 max = root.InverseTransformPoint(world.max);
            var local = new Bounds((min + max) * 0.5f, new Vector3(
                Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z)));
            if (!has)
            {
                bounds = local;
                has = true;
            }
            else
            {
                bounds.Encapsulate(local);
            }
        }
        return has;
    }
}
