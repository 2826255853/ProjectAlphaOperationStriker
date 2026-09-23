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

        public MonsterType monsterType = MonsterType.Ground;
        [Min(0f)] public float flightHeight = 3f;
    }

    [Header("Spawn Settings")]
    [SerializeField, Tooltip("Whether this entrance is currently producing enemies.")]
    private bool spawningEnabled = true;

    [SerializeField, Min(0.01f), Tooltip("Seconds between enemy spawns.")]
    private float spawnInterval = 1f;

    [Header("Kill Reward")]
    [SerializeField, Tooltip("覆盖该出怪口生成的所有怪物的击杀奖励；关闭时地面怪 10、飞行怪 15。")]
    private bool overrideKillReward;

    [SerializeField, Min(0), Tooltip("启用覆盖时每只怪物的击杀奖励；允许 0。生成后修改不会改变已生成怪物的奖励。")]
    private int killRewardOverride = 10;

    public int GetKillReward(MonsterType type)
    {
        if (overrideKillReward)
        {
            if (killRewardOverride < 0) throw new ArgumentOutOfRangeException(nameof(killRewardOverride));
            return killRewardOverride;
        }
        // TODO(确认): 暂用地面怪 10、飞行怪 15，后续按关卡平衡调整。
        return type == MonsterType.Flying ? 15 : 10;
    }

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

    [SerializeField, Tooltip("Default movement type for spawned monsters.")]
    private MonsterType monsterType = MonsterType.Ground;

    [SerializeField, Min(0f), Tooltip("Height above the target's Y position used by flying monsters.")]
    private float flightHeight = 3f;

    [Header("Flying Entrance")]
    [SerializeField, Tooltip("开启后本出怪口只生成飞行怪物，并把它自己放在空中。")]
    private bool flyingEntrance;

    [SerializeField, Min(0f), Tooltip("飞行出怪口所在的空中高度（世界 Y）。运行时进入场景会把出怪口抬到此高度。")]
    private float entranceAltitude = 12f;

    [SerializeField, Min(0f), Tooltip("飞行怪物生成时在水平方向的随机散布半径，避免全部叠在同一个点。")]
    private float flyingSpawnSpread = 2f;

    private const string FlyingMonsterResourcePath = "FlyingMonster";
    private static GameObject cachedFlyingMonsterPrefab;
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
    public MonsterType MonsterType { get => monsterType; set => monsterType = value; }
    public float FlightHeight { get => flightHeight; set => flightHeight = Mathf.Max(0f, value); }
    /// <summary>True when this entrance only produces airborne monsters.</summary>
    public bool IsFlyingEntrance { get => flyingEntrance; set => flyingEntrance = value; }
    public float EntranceAltitude { get => entranceAltitude; set => entranceAltitude = Mathf.Max(0f, value); }
    public float FlyingSpawnSpread { get => flyingSpawnSpread; set => flyingSpawnSpread = Mathf.Max(0f, value); }
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
        if (flyingEntrance) LiftEntranceIntoAir();
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
        flightHeight = Mathf.Max(0f, flightHeight);
        entranceAltitude = Mathf.Max(0f, entranceAltitude);
        flyingSpawnSpread = Mathf.Max(0f, flyingSpawnSpread);
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
        MonsterType selectedType = flyingEntrance
            ? MonsterType.Flying
            : (GetWaveSettings(waveNumber)?.monsterType ?? monsterType);
        // Reject invalid configuration before creating a partially initialized enemy.
        GetKillReward(selectedType);
        spawnSequence++;
        GameObject selectedPrefab = GetWaveSettings(waveNumber)?.enemyPrefab;
        if (selectedPrefab == null) selectedPrefab = enemyPrefab;
        if (selectedPrefab == null && flyingEntrance) selectedPrefab = GetFlyingMonsterPrefab();
        Vector3 spawnPosition = GetSpawnPosition();
        GameObject enemy = selectedPrefab != null
            ? Instantiate(selectedPrefab, spawnPosition, transform.rotation, enemyParent)
            : CreatePlaceholderEnemy(spawnPosition);

        EnemyInstance instance = enemy.GetComponent<EnemyInstance>() ?? enemy.AddComponent<EnemyInstance>();
        MonsterPathFollower follower = enemy.GetComponent<MonsterPathFollower>() ?? enemy.AddComponent<MonsterPathFollower>();
        instance.MonsterType = selectedType;
        instance.Initialize(this, spawnSequence, waveNumber, indexInWave);
        follower.enabled = true;
        float selectedHeight = GetWaveSettings(waveNumber)?.flightHeight ?? flightHeight;
        Vector3 direction = travelDirection.sqrMagnitude > 0.001f ? travelDirection : transform.forward;
        Transform destinationPoint = core != null ? core.transform : targetPoint;
        Vector3 destination = destinationPoint != null ? destinationPoint.position : targetPosition;
        if (selectedType == MonsterType.Flying)
        {
            follower.InitializeFlying(destinationPoint, destination, moveSpeed, selectedHeight, core);
        }
        else if (core != null && useTargetWaypoint)
        {
            follower.InitializeViaWaypoint(pathGrid, targetPoint, targetPosition, direction, moveSpeed, core);
        }
        else
        {
            follower.Initialize(pathGrid, destinationPoint, destination, direction, moveSpeed, core);
        }
        instance.PathFollower = follower;
        if (instance.Resolution == EnemyInstance.ResolutionState.Alive
            && selectedType == MonsterType.Ground && enemy.GetComponent<GroundEnemyCombat>() == null)
            enemy.AddComponent<GroundEnemyCombat>();
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
            waveSpawnSettings[i].flightHeight = Mathf.Max(0f, waveSpawnSettings[i].flightHeight);
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

    /// <summary>
    /// Airborne monster model used when no prefab is assigned. Loaded once from
    /// Resources so scenes do not need a hard reference to the flying model.
    /// </summary>
    public static GameObject GetFlyingMonsterPrefab()
    {
        if (cachedFlyingMonsterPrefab == null)
            cachedFlyingMonsterPrefab = Resources.Load<GameObject>(FlyingMonsterResourcePath);
        return cachedFlyingMonsterPrefab;
    }

    /// <summary>Origin used for one enemy. Flying entrances scatter monsters in the air.</summary>
    private Vector3 GetSpawnPosition()
    {
        Vector3 origin = transform.position;
        if (!flyingEntrance) return origin;

        origin.y = Mathf.Max(origin.y, entranceAltitude);
        if (flyingSpawnSpread > 0f)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * flyingSpawnSpread;
            origin.x += offset.x;
            origin.z += offset.y;
        }

        return origin;
    }

    /// <summary>Keeps an airborne entrance above the ground even if it was authored at Y = 0.</summary>
    private void LiftEntranceIntoAir()
    {
        Vector3 position = transform.position;
        if (position.y >= entranceAltitude) return;
        // TODO(确认): 是否需要让飞行出怪口在编辑器中就实时抬升（目前只在运行时生效）。
        position.y = entranceAltitude;
        transform.position = position;
    }

    private GameObject CreatePlaceholderEnemy(Vector3 spawnPosition)
    {
        var placeholder = new GameObject($"Enemy Placeholder {spawnSequence:000}");
        placeholder.transform.SetPositionAndRotation(spawnPosition, transform.rotation);
        if (enemyParent != null) placeholder.transform.SetParent(enemyParent, true);
        return placeholder;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = !spawningEnabled
            ? new Color(0.45f, 0.45f, 0.45f, 0.8f)
            : (flyingEntrance ? new Color(0.15f, 0.55f, 1f, 0.9f) : new Color(1f, 0.2f, 0.1f, 0.9f));
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

        if (flyingEntrance)
        {
            Vector3 ground = new Vector3(transform.position.x, 0f, transform.position.z);
            Gizmos.color = new Color(0.15f, 0.55f, 1f, 0.5f);
            Gizmos.DrawLine(ground, transform.position);
            Gizmos.DrawWireCube(transform.position, new Vector3(flyingSpawnSpread * 2f, 0.1f, flyingSpawnSpread * 2f));
        }
    }
}
