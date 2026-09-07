using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer-positioned enemy entrance. While active, it creates a finite number of enemy waves.
/// If no prefab is assigned, an empty placeholder is created so models can be added later.
/// </summary>
public sealed class EnemySpawnPoint : MonoBehaviour
{
    [Serializable]
    public sealed class WaveSpawnSettings
    {
        [Tooltip("是否在这一波从该出怪口生成敌人。")]
        public bool enabled = true;

        [Min(0), Tooltip("这一波生成的数量。设为 0 等同于关闭本波。")]
        public int enemyCount = 10;

        [Tooltip("这一波使用的怪物预制体。留空则使用出怪口的默认预制体。")]
        public GameObject enemyPrefab;
    }

    [Header("Spawn Settings")]
    [SerializeField, Tooltip("Whether this entrance is currently producing enemies.")]
    private bool spawningEnabled = true;

    [SerializeField, Min(0.01f), Tooltip("Seconds between enemy spawns.")]
    private float spawnInterval = 1f;

    [Header("Wave Settings")]
    // Kept hidden for migration. New scenes use WaveManager.TotalWaves globally.
    [SerializeField, HideInInspector, Min(1), Tooltip("Legacy per-entrance value; migrated to WaveManager.")]
    private int totalWaves = 3;

    [SerializeField, Min(1), Tooltip("Number of enemies produced during each wave.")]
    private int enemiesPerWave = 10;

    [SerializeField, Tooltip("逐波覆盖设置。列表元素对应第 1 波、第 2 波……；未填写的波次使用默认数量和预制体。")]
    private List<WaveSpawnSettings> waveSpawnSettings = new List<WaveSpawnSettings>();

    [SerializeField, Min(0f), Tooltip("Seconds from game start (or restart) to the first enemy of wave one.")]
    private float initialWaveDelay = 5f;

    [SerializeField, Tooltip("One wait time per wave transition. Element 0 is wave 1 to 2, element 1 is wave 2 to 3, and so on.")]
    private float[] waveTransitionDelays = { 15f, 30f };

    // Retains and migrates scenes created before per-transition delays were introduced.
    [SerializeField, HideInInspector]
    private float timeBetweenWaves = -1f;

    [Header("Optional References")]
    [SerializeField, Tooltip("Leave empty to create a model-free enemy placeholder.")]
    private GameObject enemyPrefab;

    [SerializeField, Tooltip("Optional parent for spawned enemies. Leave empty to use the scene root.")]
    private Transform enemyParent;

    [Header("Monster Path")]
    [SerializeField, Tooltip("Grid whose open cells monsters may traverse. If empty, monsters move directly to the target.")]
    private MonsterPathGrid pathGrid;

    [SerializeField, Tooltip("Scene core that all newly spawned monsters will pursue. Leave empty to find the first EnemyCore in the scene.")]
    private EnemyCore core;

    [SerializeField, Tooltip("When enabled, monsters must reach the target waypoint before continuing to the core.")]
    private bool useTargetWaypoint;

    [SerializeField, Tooltip("Optional required waypoint transform. When empty, Target Position is used.")]
    private Transform targetPoint;

    [SerializeField, Tooltip("Required waypoint used when no Target Point transform is assigned.")]
    private Vector3 targetPosition;

    [SerializeField, Tooltip("Preferred initial travel direction; also breaks pathfinding ties.")]
    private Vector3 travelDirection = Vector3.forward;

    [SerializeField, Min(0f), Tooltip("Monster movement speed in world units per second.")]
    private float moveSpeed = 2f;

    private float timeUntilNextSpawn;
    private int spawnSequence;
    private int currentWave;
    private int spawnedInCurrentWave;
    private bool allWavesCompleted;
    private WaveManager waveManager;

    /// <summary>Raised immediately after the final enemy of the final wave is created.</summary>
    public event Action<EnemySpawnPoint> AllWavesCompleted;

    public bool SpawningEnabled
    {
        get => spawningEnabled;
        set
        {
            if (spawningEnabled == value) return;

            if (value && allWavesCompleted)
            {
                RestartWaves();
                return;
            }

            spawningEnabled = value;
        }
    }

    public float SpawnInterval
    {
        get => spawnInterval;
        set => spawnInterval = Mathf.Max(0.01f, value);
    }

    public GameObject EnemyPrefab { get => enemyPrefab; set => enemyPrefab = value; }
    public MonsterPathGrid PathGrid { get => pathGrid; set => pathGrid = value; }
    public EnemyCore Core { get => core; set => core = value; }
    public bool UseTargetWaypoint { get => useTargetWaypoint; set => useTargetWaypoint = value; }
    public Transform TargetPoint { get => targetPoint; set => targetPoint = value; }
    public Vector3 TargetPosition { get => targetPosition; set => targetPosition = value; }
    public Vector3 TravelDirection { get => travelDirection; set => travelDirection = value; }
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Max(0f, value); }
    /// <summary>Total wave count shared by every spawn point in the scene.</summary>
    public int TotalWaves => waveManager != null ? waveManager.TotalWaves : Mathf.Max(1, totalWaves);
    public int EnemiesPerWave => GetEnemiesPerWave(currentWave);
    public int GetEnemiesPerWave(int waveNumber)
    {
        WaveSpawnSettings settings = GetWaveSettings(waveNumber);
        return settings != null ? Mathf.Max(0, settings.enemyCount) : Mathf.Max(0, enemiesPerWave);
    }
    public bool IsWaveEnabled(int waveNumber)
    {
        WaveSpawnSettings settings = GetWaveSettings(waveNumber);
        return settings == null || (settings.enabled && settings.enemyCount > 0);
    }
    public float InitialWaveDelay
    {
        get => initialWaveDelay;
        set => initialWaveDelay = Mathf.Max(0f, value);
    }
    public int CurrentWave => currentWave;
    public int SpawnedInCurrentWave => spawnedInCurrentWave;
    public bool AllWavesAreCompleted => allWavesCompleted;
    public int WaveTransitionCount => waveTransitionDelays?.Length ?? 0;

    /// <summary>
    /// True while this spawn point is waiting before the first enemy of a wave.
    /// Spawn intervals between enemies are intentionally not reported as wave waits.
    /// </summary>
    public bool IsWaitingForWave => spawningEnabled && !allWavesCompleted && spawnedInCurrentWave == 0 && timeUntilNextSpawn > 0f;

    /// <summary>Seconds remaining before this spawn point creates the next wave's first enemy.</summary>
    public float RemainingWaitTime => IsWaitingForWave ? Mathf.Max(0f, timeUntilNextSpawn) : 0f;

    /// <summary>Immediately ends the current pre-wave wait, if one is active.</summary>
    public void SkipWait()
    {
        if (IsWaitingForWave) timeUntilNextSpawn = 0f;
    }

    private void Awake()
    {
        waveManager = WaveManager.GetOrCreate();
        waveManager.RegisterLegacyTotalWaves(totalWaves);
        waveManager.TotalWavesChanged += HandleTotalWavesChanged;
        if (core == null) core = FindAnyObjectByType<EnemyCore>();
        if (pathGrid == null && core != null) pathGrid = core.PathGrid;
        if (pathGrid == null) pathGrid = FindAnyObjectByType<MonsterPathGrid>();
        EnsureWaveTransitionData();
        ResetWaveProgress();
    }

    private void OnDestroy()
    {
        if (waveManager != null) waveManager.TotalWavesChanged -= HandleTotalWavesChanged;
    }

    private void HandleTotalWavesChanged(int value)
    {
        EnsureWaveTransitionData();
        EnsureWaveSpawnData();
        if (currentWave > value)
        {
            currentWave = value;
            spawnedInCurrentWave = 0;
            allWavesCompleted = false;
        }
    }

    private void OnValidate()
    {
        spawnInterval = Mathf.Max(0.01f, spawnInterval);
        totalWaves = Mathf.Max(1, totalWaves);
        enemiesPerWave = Mathf.Max(1, enemiesPerWave);
        initialWaveDelay = Mathf.Max(0f, initialWaveDelay);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        EnsureWaveTransitionData();
        EnsureWaveSpawnData();
    }

    private void Update()
    {
        if (allWavesCompleted)
        {
            // Serialized fields can be changed directly from the Inspector without using the property setter.
            if (!spawningEnabled) return;
            ResetWaveProgress();
        }

        if (!spawningEnabled) return;

        timeUntilNextSpawn -= Time.deltaTime;
        if (timeUntilNextSpawn > 0f) return;

        SpawnNextWaveEnemy();
    }

    /// <summary>Creates one enemy immediately, independently of the automatic timer.</summary>
    public EnemyInstance SpawnEnemy()
    {
        return CreateEnemy(0, 0);
    }

    /// <summary>Clears wave progress and starts again from wave one.</summary>
    public void RestartWaves()
    {
        ResetWaveProgress();
        spawningEnabled = true;
    }

    private void SpawnNextWaveEnemy()
    {
        // Disabled/empty waves are advanced without creating an enemy.
        while (!IsWaveEnabled(currentWave))
        {
            if (currentWave >= TotalWaves)
            {
                CompleteAllWaves();
                return;
            }

            currentWave++;
            spawnedInCurrentWave = 0;
            timeUntilNextSpawn = GetWaveTransitionDelay(currentWave - 1);
            if (timeUntilNextSpawn > 0f) return;
        }

        int indexInWave = spawnedInCurrentWave + 1;
        CreateEnemy(currentWave, indexInWave);
        spawnedInCurrentWave = indexInWave;

        int waveCount = GetEnemiesPerWave(currentWave);
        if (spawnedInCurrentWave < waveCount)
        {
            timeUntilNextSpawn = spawnInterval;
            return;
        }

        if (currentWave >= TotalWaves)
        {
            CompleteAllWaves();
            return;
        }

        float transitionDelay = GetWaveTransitionDelay(currentWave);
        currentWave++;
        spawnedInCurrentWave = 0;
        timeUntilNextSpawn = transitionDelay;
    }

    private EnemyInstance CreateEnemy(int waveNumber, int indexInWave)
    {
        spawnSequence++;
        GameObject selectedPrefab = GetWaveSettings(waveNumber)?.enemyPrefab;
        if (selectedPrefab == null) selectedPrefab = enemyPrefab;
        GameObject enemy = selectedPrefab != null
            ? Instantiate(selectedPrefab, transform.position, transform.rotation, enemyParent)
            : CreatePlaceholderEnemy();

        EnemyInstance instance = enemy.GetComponent<EnemyInstance>() ?? enemy.AddComponent<EnemyInstance>();
        instance.Initialize(this, spawnSequence, waveNumber, indexInWave);
        MonsterPathFollower follower = enemy.GetComponent<MonsterPathFollower>() ?? enemy.AddComponent<MonsterPathFollower>();
        Vector3 direction = travelDirection.sqrMagnitude > 0.001f ? travelDirection : transform.forward;
        if (core != null && useTargetWaypoint)
        {
            follower.InitializeViaWaypoint(pathGrid, targetPoint, targetPosition, direction, moveSpeed, core);
        }
        else
        {
            Transform destinationPoint = core != null ? core.transform : targetPoint;
            Vector3 destination = destinationPoint != null ? destinationPoint.position : targetPosition;
            follower.Initialize(pathGrid, destinationPoint, destination, direction, moveSpeed, core);
        }
        instance.PathFollower = follower;
        return instance;
    }

    private void ResetWaveProgress()
    {
        currentWave = 1;
        spawnedInCurrentWave = 0;
        allWavesCompleted = false;
        timeUntilNextSpawn = initialWaveDelay;
    }

    private WaveSpawnSettings GetWaveSettings(int waveNumber)
    {
        int index = waveNumber - 1;
        return waveSpawnSettings != null && index >= 0 && index < waveSpawnSettings.Count
            ? waveSpawnSettings[index]
            : null;
    }

    private void CompleteAllWaves()
    {
        allWavesCompleted = true;
        spawningEnabled = false;
        timeUntilNextSpawn = 0f;
        AllWavesCompleted?.Invoke(this);
    }

    private void EnsureWaveSpawnData()
    {
        int requiredCount = Mathf.Max(0, TotalWaves);
        if (waveSpawnSettings == null) waveSpawnSettings = new List<WaveSpawnSettings>();
        while (waveSpawnSettings.Count < requiredCount)
            waveSpawnSettings.Add(new WaveSpawnSettings { enabled = true, enemyCount = enemiesPerWave });
        if (waveSpawnSettings.Count > requiredCount)
            waveSpawnSettings.RemoveRange(requiredCount, waveSpawnSettings.Count - requiredCount);
        for (int i = 0; i < waveSpawnSettings.Count; i++)
        {
            if (waveSpawnSettings[i] == null) waveSpawnSettings[i] = new WaveSpawnSettings();
            waveSpawnSettings[i].enemyCount = Mathf.Max(0, waveSpawnSettings[i].enemyCount);
        }
    }

    /// <summary>Gets the wait after the specified wave. Wave numbers are one-based.</summary>
    public float GetWaveTransitionDelay(int completedWaveNumber)
    {
        int index = completedWaveNumber - 1;
        return waveTransitionDelays != null && index >= 0 && index < waveTransitionDelays.Length
            ? Mathf.Max(0f, waveTransitionDelays[index])
            : 0f;
    }

    /// <summary>Changes the wait after the specified wave. Wave numbers are one-based.</summary>
    public bool SetWaveTransitionDelay(int completedWaveNumber, float seconds)
    {
        EnsureWaveTransitionData();
        int index = completedWaveNumber - 1;
        if (index < 0 || index >= waveTransitionDelays.Length) return false;

        waveTransitionDelays[index] = Mathf.Max(0f, seconds);
        return true;
    }

    private void EnsureWaveTransitionData()
    {
        int requiredCount = Mathf.Max(0, TotalWaves - 1);
        if (timeBetweenWaves >= 0f)
        {
            waveTransitionDelays = new float[requiredCount];
            for (int i = 0; i < requiredCount; i++) waveTransitionDelays[i] = timeBetweenWaves;
            timeBetweenWaves = -1f;
            return;
        }

        if (waveTransitionDelays != null && waveTransitionDelays.Length == requiredCount)
        {
            for (int i = 0; i < waveTransitionDelays.Length; i++)
                waveTransitionDelays[i] = Mathf.Max(0f, waveTransitionDelays[i]);
            return;
        }

        float[] previous = waveTransitionDelays;
        waveTransitionDelays = new float[requiredCount];
        for (int i = 0; i < requiredCount; i++) waveTransitionDelays[i] = 5f;
        if (previous != null)
            Array.Copy(previous, waveTransitionDelays, Mathf.Min(previous.Length, waveTransitionDelays.Length));
    }

    private GameObject CreatePlaceholderEnemy()
    {
        var placeholder = new GameObject($"Enemy Placeholder {spawnSequence:000}");
        placeholder.transform.SetPositionAndRotation(transform.position, transform.rotation);
        if (enemyParent != null) placeholder.transform.SetParent(enemyParent, true);
        return placeholder;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = spawningEnabled
            ? new Color(1f, 0.2f, 0.1f, 0.9f)
            : new Color(0.45f, 0.45f, 0.45f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawRay(transform.position, transform.forward);
        Vector3 direction = travelDirection.sqrMagnitude > 0.001f ? travelDirection.normalized : transform.forward;
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.9f);
        Gizmos.DrawRay(transform.position, direction);
        Vector3 corePosition = core != null ? core.Position : (targetPoint != null ? targetPoint.position : targetPosition);
        if (core != null && useTargetWaypoint)
        {
            Vector3 waypoint = targetPoint != null ? targetPoint.position : targetPosition;
            Gizmos.DrawLine(transform.position, waypoint);
            Gizmos.color = new Color(0.85f, 0.15f, 1f, 0.9f);
            Gizmos.DrawWireSphere(waypoint, 0.2f);
            Gizmos.DrawLine(waypoint, corePosition);
        }
        else
        {
            Gizmos.DrawLine(transform.position, corePosition);
        }
    }
}
