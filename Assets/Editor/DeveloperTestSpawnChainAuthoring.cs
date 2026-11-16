using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Writes a compact, repeatable enemy progression to the main scene's ground entrances.</summary>
public static class DeveloperTestSpawnChainAuthoring
{
    public const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    private const int WaveCount = 3;
    private const int FirstWaveCount = 3;
    private const int MixedWaveCount = 4;
    private const int FinalWaveCount = 2;
    private const float SpawnInterval = 1.5f;

    [MenuItem("Tools/塔防/开发测试/配置三段式出怪链")]
    public static void ApplyMainScene()
    {
        ApplyMainSceneInternal();
    }

    /// <summary>Batch entry point: DeveloperTestSpawnChainAuthoring.ApplyMainSceneBatch</summary>
    public static void ApplyMainSceneBatch()
    {
        ApplyMainSceneInternal();
    }

    [MenuItem("Tools/塔防/开发测试/校验三段式出怪链")]
    public static void ValidateMainSceneMenu()
    {
        int failures = ValidateMainScene();
        EditorUtility.DisplayDialog("出怪链校验", failures == 0 ? "配置通过。" : "发现 " + failures + " 项问题，详见 Console。", "好");
    }

    /// <summary>Batch entry point: DeveloperTestSpawnChainAuthoring.ValidateMainSceneBatch</summary>
    public static void ValidateMainSceneBatch()
    {
        int failures = ValidateMainScene();
        if (failures != 0) throw new InvalidOperationException("开发测试出怪链校验失败: " + failures);
        Debug.Log("DEVELOPER_TEST_SPAWN_CHAIN_PASS");
    }

    private static void ApplyMainSceneInternal()
    {
        GameObject masked = RequirePrefab(MaskedSoldierAuthoring.PrefabPath);
        GameObject fast = RequirePrefab(FastRunnerAuthoring.PrefabPath);
        GameObject selfDestruct = RequirePrefab("Assets/Resources/SelfDestructBotEnemy.prefab");
        if (!File.Exists(ScenePath)) throw new FileNotFoundException("找不到主场景", ScenePath);

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "DeveloperTestSpawnChain", "scene-backups", stamp);
        Directory.CreateDirectory(backupDir);
        string backupPath = Path.Combine(backupDir, Path.GetFileName(ScenePath));
        File.Copy(ScenePath, backupPath, true);
        if (File.Exists(ScenePath + ".meta")) File.Copy(ScenePath + ".meta", backupPath + ".meta", true);

        Scene scene = OpenMainScene();
        EnemySpawnPoint[] entrances = UnityEngine.Object.FindObjectsByType<EnemySpawnPoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        EnemySpawnPoint[] ground = entrances.Where(point => !point.IsFlyingEntrance).ToArray();
        if (ground.Length == 0) throw new InvalidOperationException("主场景没有地面 EnemySpawnPoint。");

        foreach (EnemySpawnPoint point in ground) Configure(point, masked, fast, selfDestruct);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("保存主场景失败: " + ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[DeveloperTestSpawnChain] configured=" + ground.Length + " ground entrances; " +
                  "per entrance=3+4+2 (9 total), flying entrances unchanged; backup=" + backupPath);
    }

    private static void Configure(EnemySpawnPoint point, GameObject masked, GameObject fast, GameObject selfDestruct)
    {
        var serialized = new SerializedObject(point);
        serialized.FindProperty("spawningEnabled").boolValue = true;
        serialized.FindProperty("spawnInterval").floatValue = SpawnInterval;
        serialized.FindProperty("initialWaveDelay").floatValue = 2f;
        serialized.FindProperty("enemiesPerWave").intValue = FirstWaveCount;
        serialized.FindProperty("enemyPrefab").objectReferenceValue = masked;
        SerializedProperty delays = serialized.FindProperty("waveTransitionDelays");
        delays.arraySize = WaveCount - 1;
        delays.GetArrayElementAtIndex(0).floatValue = 5f;
        delays.GetArrayElementAtIndex(1).floatValue = 5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Apply the wave list after base fields because OnValidate fills missing entries from enemiesPerWave.
        serialized.Update();
        SerializedProperty waves = serialized.FindProperty("waveSpawnSettings");
        waves.arraySize = WaveCount;
        SetWave(waves.GetArrayElementAtIndex(0), FirstWaveCount, masked, null, 0f);
        SetWave(waves.GetArrayElementAtIndex(1), MixedWaveCount, masked, fast, 0.5f);
        SetWave(waves.GetArrayElementAtIndex(2), FinalWaveCount, selfDestruct, null, 0f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(point);
    }

    private static void SetWave(SerializedProperty wave, int count, GameObject primary, GameObject alternate, float share)
    {
        wave.FindPropertyRelative("enabled").boolValue = true;
        wave.FindPropertyRelative("enemyCount").intValue = count;
        wave.FindPropertyRelative("enemyPrefab").objectReferenceValue = primary;
        wave.FindPropertyRelative("monsterType").intValue = (int)MonsterType.Ground;
        wave.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue = alternate;
        wave.FindPropertyRelative("alternateShare").floatValue = share;
        wave.FindPropertyRelative("alternateMoveSpeed").floatValue = alternate != null
            ? MonsterSpeciesCatalog.FastRunner.MoveSpeed : 0f;
    }

    private static int ValidateMainScene()
    {
        OpenMainScene();
        EnemySpawnPoint[] ground = UnityEngine.Object.FindObjectsByType<EnemySpawnPoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Where(point => !point.IsFlyingEntrance).ToArray();
        int failures = 0;
        if (ground.Length == 0) { Debug.LogError("[DeveloperTestSpawnChain] 未找到地面出怪口。"); return 1; }
        foreach (EnemySpawnPoint point in ground)
        {
            var serialized = new SerializedObject(point);
            SerializedProperty waves = serialized.FindProperty("waveSpawnSettings");
            string[] expectedPaths = { MaskedSoldierAuthoring.PrefabPath, MaskedSoldierAuthoring.PrefabPath,
                "Assets/Resources/SelfDestructBotEnemy.prefab" };
            int[] expectedCounts = { FirstWaveCount, MixedWaveCount, FinalWaveCount };
            bool valid = waves.arraySize == WaveCount;
            for (int i = 0; valid && i < WaveCount; i++)
            {
                SerializedProperty wave = waves.GetArrayElementAtIndex(i);
                valid = wave.FindPropertyRelative("enabled").boolValue
                    && wave.FindPropertyRelative("enemyCount").intValue == expectedCounts[i]
                    && AssetDatabase.GetAssetPath(wave.FindPropertyRelative("enemyPrefab").objectReferenceValue) == expectedPaths[i];
            }
            if (valid && waves.arraySize == WaveCount)
            {
                SerializedProperty mixed = waves.GetArrayElementAtIndex(1);
                valid = mixed.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue ==
                        AssetDatabase.LoadAssetAtPath<GameObject>(FastRunnerAuthoring.PrefabPath)
                    && Mathf.Approximately(mixed.FindPropertyRelative("alternateShare").floatValue, 0.5f)
                    && point.SpawnInterval <= SpawnInterval + 0.001f;
            }
            if (!valid) { failures++; Debug.LogError("[DeveloperTestSpawnChain] 配置不符: " + point.name, point); }
        }
        Debug.Log("[DeveloperTestSpawnChain] entrances=" + ground.Length + " failures=" + failures);
        return failures;
    }

    private static GameObject RequirePrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new FileNotFoundException("找不到怪物预制体", path);
        return prefab;
    }

    private static Scene OpenMainScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && string.Equals(active.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return active;
        if (active.IsValid() && active.isDirty) EditorSceneManager.SaveOpenScenes();
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
