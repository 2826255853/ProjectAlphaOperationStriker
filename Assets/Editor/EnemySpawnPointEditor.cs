using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemySpawnPoint))]
public sealed class EnemySpawnPointEditor : Editor
{
    private SerializedProperty spawningEnabled;
    private SerializedProperty spawnInterval;
    private SerializedProperty legacyTotalWaves;
    private SerializedProperty enemiesPerWave;
    private SerializedProperty waveSpawnSettings;
    private SerializedProperty initialWaveDelay;
    private SerializedProperty waveTransitionDelays;
    private SerializedProperty enemyPrefab;
    private SerializedProperty enemyParent;
    private SerializedProperty pathGrid;
    private SerializedProperty core;
    private SerializedProperty useTargetWaypoint;
    private SerializedProperty targetPoint;
    private SerializedProperty targetPosition;
    private SerializedProperty travelDirection;
    private SerializedProperty moveSpeed;

    private void OnEnable()
    {
        spawningEnabled = serializedObject.FindProperty("spawningEnabled");
        spawnInterval = serializedObject.FindProperty("spawnInterval");
        legacyTotalWaves = serializedObject.FindProperty("totalWaves");
        enemiesPerWave = serializedObject.FindProperty("enemiesPerWave");
        waveSpawnSettings = serializedObject.FindProperty("waveSpawnSettings");
        initialWaveDelay = serializedObject.FindProperty("initialWaveDelay");
        waveTransitionDelays = serializedObject.FindProperty("waveTransitionDelays");
        enemyPrefab = serializedObject.FindProperty("enemyPrefab");
        enemyParent = serializedObject.FindProperty("enemyParent");
        pathGrid = serializedObject.FindProperty("pathGrid");
        core = serializedObject.FindProperty("core");
        useTargetWaypoint = serializedObject.FindProperty("useTargetWaypoint");
        targetPoint = serializedObject.FindProperty("targetPoint");
        targetPosition = serializedObject.FindProperty("targetPosition");
        travelDirection = serializedObject.FindProperty("travelDirection");
        moveSpeed = serializedObject.FindProperty("moveSpeed");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("刷新设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(spawningEnabled, new GUIContent("是否启动"));
        EditorGUILayout.PropertyField(spawnInterval, new GUIContent("同波怪物生成间隔（秒）"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("波次设置", EditorStyles.boldLabel);
        WaveManager manager = FindAnyObjectByType<WaveManager>();
        if (manager != null)
        {
            EditorGUILayout.LabelField("全局总波次", manager.TotalWaves.ToString());
            if (GUILayout.Button("选择全局波次管理器")) Selection.activeObject = manager.gameObject;
        }
        else
        {
            EditorGUILayout.HelpBox("总波次由场景级 WaveManager 统一控制。当前场景尚未配置，运行时会自动创建并兼容旧出怪口配置。", MessageType.Info);
        }
        EditorGUILayout.PropertyField(enemiesPerWave, new GUIContent("每波怪物数量"));
        EditorGUILayout.LabelField("逐波出怪设置", EditorStyles.boldLabel);
        ResizeWaveSpawnData();
        for (int i = 0; i < waveSpawnSettings.arraySize; i++)
        {
            SerializedProperty settings = waveSpawnSettings.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"第 {i + 1} 波", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("enabled"), new GUIContent("出怪"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("enemyCount"), new GUIContent("数量"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("enemyPrefab"), new GUIContent("怪物类型（预制体）"));
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.PropertyField(initialWaveDelay, new GUIContent("游戏开始 → 第 1 波等待（秒）"));

        ResizeTransitionData();
        for (int i = 0; i < waveTransitionDelays.arraySize; i++)
        {
            string label = $"第 {i + 1} 波 → 第 {i + 2} 波等待（秒）";
            EditorGUILayout.PropertyField(waveTransitionDelays.GetArrayElementAtIndex(i), new GUIContent(label));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("可选引用", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(enemyPrefab, new GUIContent("怪物预制体"));
        EditorGUILayout.PropertyField(enemyParent, new GUIContent("怪物父节点"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("怪物路径", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(pathGrid, new GUIContent("怪物通行网格"));
        EditorGUILayout.PropertyField(core, new GUIContent("核心（怪物目标）"));
        if (core.objectReferenceValue == null)
            EditorGUILayout.HelpBox("未指定核心时会自动寻找场景中的第一个 EnemyCore。", MessageType.Info);

        EditorGUILayout.Space(3f);
        EditorGUILayout.PropertyField(useTargetWaypoint, new GUIContent("启用 Target 必经点"));
        if (useTargetWaypoint.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(targetPoint, new GUIContent("Target 必经点（可选）"));
            if (targetPoint.objectReferenceValue == null)
                EditorGUILayout.PropertyField(targetPosition, new GUIContent("Target 必经位置"));
            EditorGUILayout.HelpBox("路线顺序：刷新点 → Target 必经点 → 核心。请将必经点放在怪物开放区域内。", MessageType.Info);
            DrawWaypointValidation();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.PropertyField(travelDirection, new GUIContent("行进方向"));
        EditorGUILayout.PropertyField(moveSpeed, new GUIContent("移动速度"));

        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying && target is EnemySpawnPoint spawnPoint)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("当前波次", $"{spawnPoint.CurrentWave} / {spawnPoint.TotalWaves}");
            EditorGUILayout.LabelField("本波已生成", $"{spawnPoint.SpawnedInCurrentWave} / {spawnPoint.EnemiesPerWave}");
            EditorGUILayout.LabelField("全部完成", spawnPoint.AllWavesAreCompleted ? "是" : "否");
        }
    }

    private void ResizeTransitionData()
    {
        WaveManager manager = FindAnyObjectByType<WaveManager>();
        int globalTotalWaves = manager != null
            ? manager.TotalWaves
            : (legacyTotalWaves != null ? Mathf.Max(1, legacyTotalWaves.intValue) : 3);
        int requiredCount = Mathf.Max(0, globalTotalWaves - 1);
        int previousCount = waveTransitionDelays.arraySize;
        if (previousCount == requiredCount) return;

        waveTransitionDelays.arraySize = requiredCount;
        for (int i = previousCount; i < requiredCount; i++)
            waveTransitionDelays.GetArrayElementAtIndex(i).floatValue = 5f;
    }

    private void ResizeWaveSpawnData()
    {
        WaveManager manager = FindAnyObjectByType<WaveManager>();
        int totalWaves = manager != null ? manager.TotalWaves : (legacyTotalWaves != null ? Mathf.Max(1, legacyTotalWaves.intValue) : 3);
        int requiredCount = Mathf.Max(0, totalWaves);
        int previousCount = waveSpawnSettings.arraySize;
        if (previousCount == requiredCount) return;
        waveSpawnSettings.arraySize = requiredCount;
        for (int i = previousCount; i < requiredCount; i++)
        {
            SerializedProperty settings = waveSpawnSettings.GetArrayElementAtIndex(i);
            settings.FindPropertyRelative("enabled").boolValue = true;
            settings.FindPropertyRelative("enemyCount").intValue = enemiesPerWave.intValue;
            settings.FindPropertyRelative("enemyPrefab").objectReferenceValue = null;
        }
    }

    private void DrawWaypointValidation()
    {
        MonsterPathGrid activeGrid = pathGrid.objectReferenceValue as MonsterPathGrid;
        if (activeGrid == null && core.objectReferenceValue is EnemyCore activeCore)
            activeGrid = activeCore.PathGrid;
        if (activeGrid == null) return;

        Vector3 waypoint = targetPoint.objectReferenceValue is Transform waypointTransform
            ? waypointTransform.position
            : targetPosition.vector3Value;
        if (!activeGrid.TryWorldToCell(waypoint, out Vector2Int cell) || !activeGrid.IsOpen(cell))
            EditorGUILayout.HelpBox("当前 Target 必经点不在怪物开放格内。怪物不会跳过该点前往核心。", MessageType.Warning);
    }
}
