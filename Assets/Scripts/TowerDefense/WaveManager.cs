using System;
using UnityEngine;

/// <summary>
/// Scene-level wave configuration shared by every enemy spawn point.
/// Place one in a scene and edit <see cref="TotalWaves"/> to change the
/// number of waves for all entrances at once.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class WaveManager : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("全局总波次。场景中的所有出怪口共用此值。")]
    private int totalWaves = 3;

    private bool autoCreated;

    public static WaveManager Instance { get; private set; }
    public event Action<int> TotalWavesChanged;

    public int TotalWaves
    {
        get => Mathf.Max(1, totalWaves);
        set
        {
            int clamped = Mathf.Max(1, value);
            if (totalWaves == clamped) return;
            totalWaves = clamped;
            TotalWavesChanged?.Invoke(totalWaves);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        totalWaves = Mathf.Max(1, totalWaves);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Finds the scene manager or creates a temporary one at runtime.</summary>
    public static WaveManager GetOrCreate()
    {
        if (Instance != null) return Instance;

        WaveManager existing = FindAnyObjectByType<WaveManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var managerObject = new GameObject("Wave Manager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<WaveManager>();
        manager.autoCreated = true;
        managerObject.SetActive(true);
        return manager;
    }

    /// <summary>
    /// Migrates scenes made before the scene-level manager existed. The largest
    /// legacy value wins, so existing scenes retain their previous difficulty.
    /// Explicitly configured WaveManager components are never overwritten.
    /// </summary>
    internal void RegisterLegacyTotalWaves(int legacyValue)
    {
        if (!autoCreated || legacyValue <= 0)
            return;

        if (legacyValue > TotalWaves) TotalWaves = legacyValue;
    }
}
