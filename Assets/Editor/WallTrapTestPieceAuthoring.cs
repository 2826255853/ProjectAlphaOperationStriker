using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Creates the first wall-mounted test pieces used to verify selection, placement,
/// multi-cell occupancy, damage and removal. These deliberately have no attack script:
/// they are authoring/placement fixtures, not a final weapon implementation.
/// </summary>
public static class WallTrapTestPieceAuthoring
{
    private const string ResourceDirectory = "Assets/Resources/WallTrapTest";
    private const string OneByOneName = "WallTrapTest_1x1";
    private const string TwoByTwoName = "WallTrapTest_2x2";

    [MenuItem("Tools/塔防/生成墙面陷阱测试件")]
    public static void CreateAssets()
    {
        EnsureFolders();
        Material oneByOneMaterial = CreateOrUpdateMaterial(OneByOneName + "_Material", new Color(0.12f, 0.55f, 0.95f));
        Material twoByTwoMaterial = CreateOrUpdateMaterial(TwoByTwoName + "_Material", new Color(0.95f, 0.35f, 0.12f));

        GameObject oneByOnePrefab = BuildPrefab(OneByOneName, 1, 1, oneByOneMaterial);
        GameObject twoByTwoPrefab = BuildPrefab(TwoByTwoName, 2, 2, twoByTwoMaterial);
        CreateOrUpdateDefinition(OneByOneName, "墙面测试件 1×1", oneByOnePrefab, 1, 1,
            new Vector3(0.8f, 0.8f, 0.18f));
        CreateOrUpdateDefinition(TwoByTwoName, "墙面测试件 2×2", twoByTwoPrefab, 2, 2,
            new Vector3(1.8f, 1.8f, 0.18f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Wall trap test pieces generated: " + ResourceDirectory);
    }

    public static void CreateAssetsBatch() => CreateAssets();

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(ResourceDirectory))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder("Assets/Resources", "WallTrapTest");
        }
    }

    private static Material CreateOrUpdateMaterial(string name, Color color)
    {
        string path = ResourceDirectory + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject BuildPrefab(string name, int width, int height, Material material)
    {
        string prefabPath = ResourceDirectory + "/" + name + ".prefab";
        GameObject oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        GameObject root = new GameObject(name);
        root.AddComponent<TrapInstance>();

        GameObject plate = CreatePrimitive(PrimitiveType.Cube, "Wall Backplate", root.transform, material);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.06f);
        plate.transform.localScale = new Vector3(width * 0.86f, height * 0.86f, 0.12f);

        GameObject shaft = CreatePrimitive(PrimitiveType.Cube, "Outward Direction Shaft", root.transform, material);
        shaft.transform.localPosition = new Vector3(0f, 0f, 0.24f);
        shaft.transform.localScale = new Vector3(0.08f, 0.08f, 0.34f);

        GameObject arrowHead = CreatePrimitive(PrimitiveType.Cylinder, "Outward Direction Arrow", root.transform, material);
        arrowHead.transform.localPosition = new Vector3(0f, 0f, 0.45f);
        arrowHead.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        arrowHead.transform.localScale = new Vector3(0.16f, 0.12f, 0.16f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
        Object.DestroyImmediate(root);
        if (!saved || prefab == null)
            throw new System.InvalidOperationException("Could not save wall trap test prefab: " + prefabPath);
        if (oldPrefab != null) EditorUtility.SetDirty(prefab);
        return prefab;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) collider.isTrigger = false;
        return go;
    }

    private static void CreateOrUpdateDefinition(string assetName, string displayName, GameObject prefab,
        int width, int height, Vector3 installBoundsSize)
    {
        string path = ResourceDirectory + "/" + assetName + ".asset";
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        SerializedObject settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = assetName.ToLowerInvariant().Replace('_', '-');
        settings.FindProperty("displayName").stringValue = displayName;
        settings.FindProperty("prefab").objectReferenceValue = prefab;
        settings.FindProperty("cost").intValue = 0;
        settings.FindProperty("footprintWidth").intValue = width;
        settings.FindProperty("footprintHeight").intValue = height;
        settings.FindProperty("localRotation").vector3Value = Vector3.zero;
        settings.FindProperty("walkableFloorTrap").boolValue = false;
        settings.FindProperty("worldFootprint").vector2Value = Vector2.zero;
        settings.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Wall;
        settings.FindProperty("installBoundsCenter").vector3Value = new Vector3(0f, 0f, 0.12f);
        settings.FindProperty("installBoundsSize").vector3Value = installBoundsSize;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }
}
