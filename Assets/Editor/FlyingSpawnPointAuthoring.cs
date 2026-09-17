using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Authoring helpers for the dedicated airborne enemy entrance.
/// Creates the flying monster prefab and places a flying-only spawn point in the air.
/// </summary>
public static class FlyingSpawnPointAuthoring
{
    public const string FlyingModelPath = "Assets/FlyingMonsterPlane.fbx";
    public const string FlyingPrefabPath = "Assets/Resources/FlyingMonster.prefab";
    public const string FlyingSpawnName = "Enemy Spawn Flying";
    public const float DefaultEntranceAltitude = 14f;

    [MenuItem("Tools/塔防/生成飞行怪物预制体")]
    public static void CreateFlyingPrefab()
    {
        Directory.CreateDirectory("Assets/Resources");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FlyingModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到飞行怪物模型：{FlyingModelPath}");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "FlyingMonster";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        PrefabUtility.SaveAsPrefabAsset(instance, FlyingPrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"已生成飞行怪物预制体：{FlyingPrefabPath}");
    }

    [MenuItem("Tools/塔防/在场景中添加飞行出怪口")]
    public static void AddFlyingSpawnPoint()
    {
        var existing = GameObject.Find(FlyingSpawnName);
        if (existing != null)
        {
            var existingRoot = GameObject.Find("MapForgeWorld");
            if (existingRoot != null && existing.transform.parent != existingRoot.transform)
                existing.transform.SetParent(existingRoot.transform, true);
            Debug.Log($"飞行出怪口已存在：{FlyingSpawnName}，跳过创建。");
            Selection.activeGameObject = existing;
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlyingPrefabPath);
        var grid = Object.FindAnyObjectByType<MonsterPathGrid>();
        var core = Object.FindAnyObjectByType<EnemyCore>();
        Vector3 position = core != null
            ? new Vector3(core.transform.position.x, DefaultEntranceAltitude, core.transform.position.z - 80f)
            : new Vector3(0f, DefaultEntranceAltitude, -40f);

        var spawnObject = new GameObject(FlyingSpawnName);
        var worldRoot = GameObject.Find("MapForgeWorld");
        if (worldRoot != null) spawnObject.transform.SetParent(worldRoot.transform, true);
        spawnObject.transform.position = position;
        var spawnPoint = spawnObject.AddComponent<EnemySpawnPoint>();
        var serialized = new SerializedObject(spawnPoint);
        serialized.FindProperty("flyingEntrance").boolValue = true;
        serialized.FindProperty("entranceAltitude").floatValue = DefaultEntranceAltitude;
        serialized.FindProperty("flyingSpawnSpread").floatValue = 3f;
        serialized.FindProperty("monsterType").intValue = (int)MonsterType.Flying;
        serialized.FindProperty("enemyPrefab").objectReferenceValue = prefab;
        serialized.FindProperty("pathGrid").objectReferenceValue = grid;
        serialized.FindProperty("core").objectReferenceValue = core;
        serialized.FindProperty("travelDirection").vector3Value = Vector3.forward;
        serialized.FindProperty("moveSpeed").floatValue = 2.5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(spawnObject, "Add Flying Spawn Point");
        EditorSceneManager.MarkSceneDirty(spawnObject.scene);
        Selection.activeGameObject = spawnObject;
        Debug.Log($"已在空中添加飞行出怪口：{FlyingSpawnName} @ {position}");
    }

    /// <summary>Batch entry point: creates the prefab, then places the airborne entrance and saves the scene.</summary>
    public static void CreateAssets()
    {
        const string scenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
        var scene = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        CreateFlyingPrefab();
        AddFlyingSpawnPoint();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("FlyingSpawnPointAuthoring.CreateAssets 完成");
    }
}
