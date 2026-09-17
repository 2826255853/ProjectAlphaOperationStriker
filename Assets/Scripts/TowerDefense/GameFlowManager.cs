using UnityEngine;

/// <summary>Resolves tower-defense victory and defeat conditions for the scene.</summary>
[DefaultExecutionOrder(-900)]
public sealed class GameFlowManager : MonoBehaviour
{
    public enum Result { Playing, Victory, Defeat }
    public static GameFlowManager Instance { get; private set; }
    public Result CurrentResult { get; private set; }
    public event System.Action<Result> ResultChanged;

    private EnemyCore core;
    private EnemySpawnPoint[] spawnPoints = System.Array.Empty<EnemySpawnPoint>();
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<GameFlowManager>() != null) return;
        new GameObject("Game Flow Manager").AddComponent<GameFlowManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        CurrentResult = Result.Playing;
    }

    private void Update()
    {
        if (CurrentResult != Result.Playing) return;
        if (core == null)
        {
            core = FindAnyObjectByType<EnemyCore>();
            if (core != null) core.Destroyed += HandleCoreDestroyed;
        }
        if (core != null && core.IsDestroyed) { SetResult(Result.Defeat); return; }
        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.2f;
        spawnPoints = FindObjectsByType<EnemySpawnPoint>();
        if (spawnPoints.Length == 0) return;
        for (int i = 0; i < spawnPoints.Length; i++)
            if (spawnPoints[i] == null || !spawnPoints[i].AllWavesAreCompleted) return;
        if (FindAnyObjectByType<EnemyInstance>() == null && (core == null || !core.IsDestroyed))
            SetResult(Result.Victory);
    }

    private void HandleCoreDestroyed(EnemyCore _) => SetResult(Result.Defeat);

    private void SetResult(Result result)
    {
        if (CurrentResult != Result.Playing) return;
        CurrentResult = result;
        if (result == Result.Defeat)
        {
            EnemySpawnPoint[] points = FindObjectsByType<EnemySpawnPoint>();
            for (int i = 0; i < points.Length; i++)
                if (points[i] != null) points[i].SpawningEnabled = false;
        }
        ResultChanged?.Invoke(result);
        Debug.Log(result == Result.Victory ? "游戏胜利" : "游戏失败");
    }

    private void OnDestroy()
    {
        if (core != null) core.Destroyed -= HandleCoreDestroyed;
        if (Instance == this) Instance = null;
    }
}
