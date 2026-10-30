using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把飓风无人机作为飞行怪物接入主场景：创建/刷新空中出怪口 <see cref="SpawnName"/>。
///
/// 与 <see cref="FlyingSpawnPointAuthoring"/> 的分工：
///   * 那边管的是默认飞行怪（Resources/FlyingMonster，飞机模型）与固定入口名 "Enemy Spawn Flying"；
///   * 这里只新增一个**独立的**空中出怪口，专门放飓风无人机，不改动既有入口。
///
/// 出怪口的字段全部由脚本写入（EnemySpawnPoint 的字段是私有的序列化字段），
/// 所以重复运行只会刷新接线，不会重复建对象。
///
/// 菜单：Tools/塔防/添加飓风无人机空中出怪口
/// 批处理：HurricaneDroneSpawnAuthoring.CreateSpawnPointBatch / ValidateBatch
/// </summary>
public static class HurricaneDroneSpawnAuthoring
{
    public const string SpawnName = "Enemy Spawn Flying Drone";
    public const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";

    public const float EntranceAltitude = 16f;
    public const float SpawnSpread = 4f;
    public const float MoveSpeed = HurricaneDroneAuthoring.EnemyMoveSpeed;
    public const float FlightHeight = HurricaneDroneAuthoring.EnemyFlightHeight;
    public const float SpawnInterval = 2f;
    public const int EnemiesPerWave = 3;

    [MenuItem("Tools/塔防/添加飓风无人机空中出怪口")]
    public static void CreateSpawnPoint()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HurricaneDroneAuthoring.EnemyPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[HurricaneDroneSpawn] 缺少飞行怪物预制体：{HurricaneDroneAuthoring.EnemyPrefabPath}，" +
                           "先运行 Tools/塔防/生成飓风无人机预制体。");
            return;
        }

        var grid = Object.FindAnyObjectByType<MonsterPathGrid>();
        var core = Object.FindAnyObjectByType<EnemyCore>();
        var worldRoot = GameObject.Find("MapForgeWorld");

        var existing = GameObject.Find(SpawnName);
        bool created = existing == null;
        var spawnObject = created ? new GameObject(SpawnName) : existing;
        if (created)
        {
            if (worldRoot != null)
            {
                spawnObject.transform.SetParent(worldRoot.transform, true);
            }
            spawnObject.transform.position = DefaultPosition(core);
        }
        else if (worldRoot != null && spawnObject.transform.parent != worldRoot.transform)
        {
            spawnObject.transform.SetParent(worldRoot.transform, true);
        }

        var spawnPoint = spawnObject.GetComponent<EnemySpawnPoint>() ?? spawnObject.AddComponent<EnemySpawnPoint>();
        var serialized = new SerializedObject(spawnPoint);
        serialized.FindProperty("flyingEntrance").boolValue = true;
        serialized.FindProperty("entranceAltitude").floatValue = EntranceAltitude;
        serialized.FindProperty("flyingSpawnSpread").floatValue = SpawnSpread;
        serialized.FindProperty("monsterType").intValue = (int)MonsterType.Flying;
        serialized.FindProperty("enemyPrefab").objectReferenceValue = prefab;
        serialized.FindProperty("pathGrid").objectReferenceValue = grid;
        serialized.FindProperty("core").objectReferenceValue = core;
        serialized.FindProperty("travelDirection").vector3Value = Vector3.forward;
        serialized.FindProperty("moveSpeed").floatValue = MoveSpeed;
        serialized.FindProperty("flightHeight").floatValue = FlightHeight;
        serialized.FindProperty("spawnInterval").floatValue = SpawnInterval;
        serialized.FindProperty("enemiesPerWave").intValue = EnemiesPerWave;
        serialized.FindProperty("initialWaveDelay").floatValue = 12f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 注意：ApplyModifiedProperties 会触发 EnemySpawnPoint.OnValidate -> EnsureWaveSpawnData，
        // 它用当时的 enemiesPerWave 补出默认波次条目（实测是 10 只/波）。
        // 所以逐波设置必须在这之后再写一遍，否则默认值会盖掉上面的意图。
        ApplyWaveSettings(spawnPoint, prefab);

        if (created)
        {
            Undo.RegisterCreatedObjectUndo(spawnObject, "Add Hurricane Drone Spawn Point");
        }
        EditorSceneManager.MarkSceneDirty(spawnObject.scene);
        Selection.activeGameObject = spawnObject;
        Debug.Log($"[HurricaneDroneSpawn] {(created ? "已创建" : "已刷新")}空中出怪口 {SpawnName} @ {spawnObject.transform.position}");
    }

    /// <summary>逐波写死本出怪口的无人机配置（数量、预制体、飞行高度），避免被默认条目覆盖。</summary>
    static void ApplyWaveSettings(EnemySpawnPoint spawnPoint, GameObject prefab)
    {
        var serialized = new SerializedObject(spawnPoint);
        var legacyWaves = serialized.FindProperty("totalWaves");
        int waveCount = Mathf.Max(1, legacyWaves != null ? legacyWaves.intValue : 3);

        var waves = serialized.FindProperty("waveSpawnSettings");
        waves.arraySize = waveCount;
        for (int i = 0; i < waveCount; i++)
        {
            var entry = waves.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("enabled").boolValue = true;
            entry.FindPropertyRelative("enemyCount").intValue = EnemiesPerWave;
            entry.FindPropertyRelative("enemyPrefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("monsterType").intValue = (int)MonsterType.Flying;
            entry.FindPropertyRelative("flightHeight").floatValue = FlightHeight;
            entry.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue = null;
            entry.FindPropertyRelative("alternateShare").floatValue = 0f;
            entry.FindPropertyRelative("alternateMoveSpeed").floatValue = 0f;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>默认位置：避开既有空中出怪口，落在核心侧后方的空中。</summary>
    static Vector3 DefaultPosition(EnemyCore core)
    {
        Vector3 anchor = core != null ? core.transform.position : Vector3.zero;
        Vector3 position = new Vector3(anchor.x - 60f, EntranceAltitude, anchor.z - 80f);

        var other = GameObject.Find(FlyingSpawnPointAuthoring.FlyingSpawnName);
        if (other != null && (other.transform.position - position).sqrMagnitude < 400f)
        {
            position += new Vector3(90f, 0f, 0f);
        }
        return position;
    }

    /// <summary>批处理入口：建/刷新出怪口并保存场景。</summary>
    public static void CreateSpawnPointBatch()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        CreateSpawnPoint();
        EditorSceneManager.SaveScene(scene);
        ValidateSpawnPoint();
        Debug.Log("[HurricaneDroneSpawn] CreateSpawnPointBatch 完成");
    }

    [MenuItem("Tools/塔防/校验飓风无人机空中出怪口")]
    public static void ValidateSpawnPoint()
    {
        var sb = new System.Text.StringBuilder();
        int failures = 0;
        sb.AppendLine("[HurricaneDroneSpawn] 空中出怪口校验");

        var spawnObject = GameObject.Find(SpawnName);
        Check(spawnObject != null, $"场景中存在 {SpawnName}", sb, ref failures);
        if (spawnObject == null)
        {
            sb.AppendLine($"HURRICANE_DRONE_SPAWN_FAIL: 缺少出怪口");
            Debug.Log(sb.ToString());
            return;
        }

        var spawnPoint = spawnObject.GetComponent<EnemySpawnPoint>();
        Check(spawnPoint != null, "EnemySpawnPoint 组件", sb, ref failures);
        if (spawnPoint != null)
        {
            Check(spawnPoint.IsFlyingEntrance, "flyingEntrance = true", sb, ref failures);
            Check(spawnPoint.EntranceAltitude >= 10f, $"入口高度 {spawnPoint.EntranceAltitude:F1} >= 10", sb, ref failures);
            Check(spawnPoint.MonsterType == MonsterType.Flying, "monsterType = Flying", sb, ref failures);
            Check(spawnPoint.EnemyPrefab != null, "enemyPrefab 已赋值", sb, ref failures);
            if (spawnPoint.EnemyPrefab != null)
            {
                Check(spawnPoint.EnemyPrefab.GetComponent<HurricaneDroneEnemy>() != null, "预制体带 HurricaneDroneEnemy", sb, ref failures);
                Check(spawnPoint.EnemyPrefab.GetComponent<EnemyHealth>() != null, "预制体带 EnemyHealth", sb, ref failures);
                Check(spawnPoint.EnemyPrefab.GetComponent<MonsterPathFollower>() != null, "预制体带 MonsterPathFollower", sb, ref failures);
                Check(spawnPoint.EnemyPrefab.GetComponent<GroundEnemyCombat>() == null, "预制体不带 GroundEnemyCombat", sb, ref failures);
            }
            Check(spawnPoint.Core != null, "core 已接线", sb, ref failures);
            Check(spawnPoint.GetEnemiesPerWave(1) == EnemiesPerWave, $"每波数量 = {EnemiesPerWave}", sb, ref failures);

            var so = new SerializedObject(spawnPoint);
            var waves = so.FindProperty("waveSpawnSettings");
            int waveCount = spawnPoint.TotalWaves;
            Check(waves != null && waves.arraySize == waveCount,
                $"逐波设置条数 {waves?.arraySize ?? 0} = 总波数 {waveCount}", sb, ref failures);
            if (waves != null && waves.arraySize > 0)
            {
                // 「每波刷新 3 架」是本出怪口的硬要求：逐波全量校验，不能只看第 1 波。
                for (int i = 0; i < waves.arraySize; i++)
                {
                    var entry = waves.GetArrayElementAtIndex(i);
                    string waveLabel = $"第 {i + 1} 波";
                    Check(entry.FindPropertyRelative("enabled").boolValue, $"{waveLabel} 已启用", sb, ref failures);
                    Check(entry.FindPropertyRelative("enemyPrefab").objectReferenceValue == spawnPoint.EnemyPrefab,
                        $"{waveLabel} 预制体 = 无人机", sb, ref failures);
                    Check(entry.FindPropertyRelative("enemyCount").intValue == EnemiesPerWave,
                        $"{waveLabel} 数量 = {EnemiesPerWave}", sb, ref failures);
                    Check(entry.FindPropertyRelative("monsterType").intValue == (int)MonsterType.Flying,
                        $"{waveLabel} 类型 = Flying", sb, ref failures);
                    Check(Mathf.Approximately(entry.FindPropertyRelative("flightHeight").floatValue, FlightHeight),
                        $"{waveLabel} 飞行高度 = {FlightHeight}", sb, ref failures);
                    Check(spawnPoint.GetEnemiesPerWave(i + 1) == EnemiesPerWave,
                        $"{waveLabel} GetEnemiesPerWave = {EnemiesPerWave}", sb, ref failures);
                }
            }
        }

        sb.AppendLine(failures == 0 ? "HURRICANE_DRONE_SPAWN_PASS" : $"HURRICANE_DRONE_SPAWN_FAIL: {failures} 项");
        Debug.Log(sb.ToString());
    }

    public static void ValidateBatch()
    {
        // 批处理启动时是空场景；不先打开主场景会误报"缺少出怪口"。
        var scene = EditorSceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        ValidateSpawnPoint();
    }

    static void Check(bool condition, string label, System.Text.StringBuilder sb, ref int failures)
    {
        sb.AppendLine($"  [{(condition ? "OK" : "FAIL")}] {label}");
        if (!condition)
        {
            failures++;
        }
    }
}
