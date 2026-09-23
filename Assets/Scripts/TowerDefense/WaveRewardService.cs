using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pays the player for surviving a wave. Kill rewards alone cannot fund the
/// second half of a run: ten ground kills are 100 gold, which is two and a half
/// 40-gold traps for a whole wave. This service grants a per-wave payout when a
/// spawn point finishes a wave, plus a small bonus for calling the next wave in
/// early instead of waiting the full intermission out.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-880)]
public sealed class WaveRewardService : MonoBehaviour
{
    [SerializeField, Min(0), Tooltip("第 1 波结算时发放的金币。")]
    private int firstWaveReward = 25;
    [SerializeField, Min(0), Tooltip("每多通过一波，奖励增加的金币。")]
    private int rewardGrowthPerWave = 15;
    [SerializeField, Min(0f), Tooltip("提前呼叫下一波时，每省下一秒等待额外发放的金币。")]
    private float earlyCallBonusPerSecond = 2f;
    [SerializeField, Min(0f), Tooltip("单次提前呼叫奖励的上限，防止长休息被刷成巨款。")]
    private float maxEarlyCallBonus = 40f;

    public static WaveRewardService Instance { get; private set; }

    private sealed class PointState
    {
        public int RewardedWave = 1;
        public float PeakWaitSeconds;
        public bool Completed;
    }

    private readonly Dictionary<EnemySpawnPoint, PointState> states = new Dictionary<EnemySpawnPoint, PointState>();
    private EnemySpawnPoint[] spawnPoints = Array.Empty<EnemySpawnPoint>();
    private float nextScanTime;

    /// <summary>Payout for finishing the given wave, before any early-call bonus.</summary>
    public int RewardForWave(int waveNumber)
        => RewardForWave(waveNumber, firstWaveReward, rewardGrowthPerWave);

    /// <summary>Pure payout curve so tests and HUD code never duplicate the formula.</summary>
    public static int RewardForWave(int waveNumber, int firstReward, int growthPerWave)
    {
        if (waveNumber < 1) return 0;
        long payout = Math.Max(0, firstReward) + (long)Math.Max(0, growthPerWave) * (waveNumber - 1);
        return payout > int.MaxValue ? int.MaxValue : (int)payout;
    }

    /// <summary>Early-call bonus for the intermission time the player skipped.</summary>
    public int EarlyCallBonus(float skippedSeconds)
    {
        if (skippedSeconds <= 0f || earlyCallBonusPerSecond <= 0f) return 0;
        float bonus = Mathf.Min(maxEarlyCallBonus, skippedSeconds * earlyCallBonusPerSecond);
        return Mathf.FloorToInt(bonus + 0.0001f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<WaveRewardService>() != null) return;
        var host = new GameObject("Wave Reward Service");
        host.AddComponent<WaveRewardService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        firstWaveReward = Mathf.Max(0, firstWaveReward);
        rewardGrowthPerWave = Mathf.Max(0, rewardGrowthPerWave);
        earlyCallBonusPerSecond = Mathf.Max(0f, earlyCallBonusPerSecond);
        maxEarlyCallBonus = Mathf.Max(0f, maxEarlyCallBonus);
    }

    private void Update()
    {
        if (Time.time >= nextScanTime)
        {
            spawnPoints = FindObjectsByType<EnemySpawnPoint>();
            nextScanTime = Time.time + 0.5f;
        }

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            EnemySpawnPoint point = spawnPoints[i];
            if (point == null) continue;
            if (!states.TryGetValue(point, out PointState state))
            {
                state = new PointState { RewardedWave = Mathf.Max(1, point.CurrentWave) };
                states[point] = state;
            }

            if (state.Completed) continue;

            // Held at its maximum so the seconds the player skipped by pressing
            // the skip key are still visible after the timer is zeroed.
            if (point.IsWaitingForWave)
                state.PeakWaitSeconds = Mathf.Max(state.PeakWaitSeconds, point.RemainingWaitTime);

            if (point.CurrentWave > state.RewardedWave)
            {
                PayForWave(state.RewardedWave, (int)state.PeakWaitSeconds);
                state.RewardedWave = point.CurrentWave;
                state.PeakWaitSeconds = 0f;
                continue;
            }

            if (point.AllWavesAreCompleted)
            {
                PayForWave(state.RewardedWave, (int)state.PeakWaitSeconds);
                state.Completed = true;
            }
        }
    }

    private void PayForWave(int waveNumber, int skippedWaitSeconds)
    {
        EconomyManager wallet = EconomyManager.Instance;
        if (wallet == null || !wallet.IsInitialized) return;

        int payout = RewardForWave(waveNumber) + EarlyCallBonus(skippedWaitSeconds);
        if (payout <= 0) return;
        try
        {
            wallet.Grant(payout);
        }
        catch (OverflowException)
        {
            Debug.LogWarning("波次奖励超过金币余额上限，本次奖励未发放。", this);
        }
        catch (InvalidOperationException exception)
        {
            Debug.LogWarning($"波次奖励未发放：{exception.Message}", this);
        }
    }
}

