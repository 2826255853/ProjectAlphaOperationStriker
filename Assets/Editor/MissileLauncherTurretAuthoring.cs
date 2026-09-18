using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Authoring helper for the dual missile launcher trap.
///
/// The imported FBX is visual-only, so this builds the rotatable rig
/// (Yaw Pivot -&gt; Pitch Pivot -&gt; launcher meshes) once, saves it as a prefab and
/// points the trap definition assets at it. Run it from
/// Tools/塔防/生成导弹发射器预制体 or in batch mode via
/// MissileLauncherTurretAuthoring.CreateLauncherPrefab.
/// </summary>
public static class MissileLauncherTurretAuthoring
{
    public const string LauncherModelPath = "Assets/Models/Trap_Base_Disc_Dual_Launcher_Loaded.fbx";
    public const string LauncherPrefabPath = "Assets/Prefabs/DualMissileLauncher.prefab";
    private const float LauncherFootprintMeters = 2f;

    private static readonly string[] TrapDefinitionPaths =
    {
        "Assets/Resources/DualMissileLauncherLoaded.asset",
        "Assets/TrapDefinitions/DualMissileLauncherLoaded.asset"
    };

    [MenuItem("Tools/塔防/生成导弹发射器预制体")]
    public static void CreateLauncherPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到导弹发射器模型：{LauncherModelPath}");
            return;
        }

        // A plain copy is used on purpose: reparenting meshes inside a nested
        // prefab instance is not persisted by SaveAsPrefabAsset, while a copy
        // produces a clean, baked Yaw Pivot / Pitch Pivot hierarchy.
        GameObject root = (GameObject)Object.Instantiate(model);
        root.name = "DualMissileLauncher";
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        MissileLauncherTurret turret = root.AddComponent<MissileLauncherTurret>();
        bool built = turret.BuildRig();
        if (!built)
            Debug.LogWarning($"未能从模型自动识别机座，已保存静态预制体，运行时会在 {nameof(MissileLauncherTurret)} 里重试。");

        FitLauncherToFootprint(root, LauncherFootprintMeters);

        // The firing half lives next to the mount: it owns the lead solution, the
        // salvo cadence and the in-flight bookkeeping (see the air-attack plan).
        MissileLauncherWeapon weapon = root.AddComponent<MissileLauncherWeapon>();
        WireWeapon(weapon, root, turret);

        EditorUtility.SetDirty(turret);
        EditorUtility.SetDirty(weapon);
        Directory.CreateDirectory(Path.GetDirectoryName(LauncherPrefabPath) ?? "Assets/Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LauncherPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        AssignPrefabToTrapDefinitions(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"已生成导弹发射器预制体：{LauncherPrefabPath}" +
            $"（旋转机座：{(built ? "已生成" : "运行时会重建")}）");
    }

    /// <summary>
    /// Points the weapon at its mount and its two tubes. The imported model names
    /// its missiles Missile_01 / Missile_02, so the launcher can fire one round per
    /// rail without any hand wiring; both fields stay editable when a model changes.
    /// </summary>
    private static void WireWeapon(MissileLauncherWeapon weapon, GameObject root, MissileLauncherTurret turret)
    {
        if (weapon == null) return;

        var serialized = new SerializedObject(weapon);
        SetObjectReference(serialized, "turret", turret);

        Transform muzzleA = FindDeepChild(root.transform, "Missile_01");
        Transform muzzleB = FindDeepChild(root.transform, "Missile_02");
        if (muzzleA == null) muzzleA = FindDeepChild(root.transform, "Missile_01_Body");
        if (muzzleB == null) muzzleB = FindDeepChild(root.transform, "Missile_02_Body");
        SetObjectReference(serialized, "muzzleA", muzzleA);
        SetObjectReference(serialized, "muzzleB", muzzleB);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (muzzleA == null || muzzleB == null)
            Debug.LogWarning("未能从模型识别出两个导弹发射口（Missile_01 / Missile_02），" +
                "运行时会在 MissileLauncherWeapon 里重试，届时可能两根管子共用一个发射点。");
    }

    private static void SetObjectReference(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform nested = FindDeepChild(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static void FitLauncherToFootprint(GameObject root, float targetMeters)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float currentFootprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (currentFootprint > 0.0001f)
            root.transform.localScale = Vector3.one * (targetMeters / currentFootprint);
    }

    /// <summary>Points every launcher trap definition at the generated prefab.</summary>
    public static void AssignPrefabToTrapDefinitions(GameObject prefab)
    {
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherPrefabPath);
        if (prefab == null) return;

        for (int i = 0; i < TrapDefinitionPaths.Length; i++)
        {
            TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(TrapDefinitionPaths[i]);
            if (definition == null) continue;
            var serialized = new SerializedObject(definition);
            SerializedProperty prefabProperty = serialized.FindProperty("prefab");
            if (prefabProperty == null) continue;
            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }

    /// <summary>Batch-mode entry point: regenerates the prefab and relinks the trap definitions.</summary>
    public static void CreateLauncherPrefabBatch()
    {
        CreateLauncherPrefab();
        EditorSceneManager.SaveOpenScenes();
    }
}
