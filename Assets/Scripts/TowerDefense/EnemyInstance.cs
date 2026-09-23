using System;
using UnityEngine;

/// <summary>Runtime identity for an enemy created by an <see cref="EnemySpawnPoint"/>.</summary>
public sealed class EnemyInstance : MonoBehaviour
{
    public enum ResolutionState { Alive, Killed, Leaked, Despawned }

    public ResolutionState Resolution { get; private set; } = ResolutionState.Despawned;
    public int KillReward { get; private set; }
    public Guid RunId { get; private set; }
    public EnemySpawnPoint SpawnPoint { get; private set; }
    public int SpawnSequence { get; private set; }
    public float SpawnTime { get; private set; }
    public int WaveNumber { get; private set; }
    public int SpawnIndexInWave { get; private set; }
    public MonsterType MonsterType { get; internal set; }
    private MonsterPathFollower pathFollower;
    private EnemyHealth health;
    private EconomyManager economy;

    public MonsterPathFollower PathFollower
    {
        get => pathFollower;
        internal set
        {
            if (pathFollower == value) return;
            if (pathFollower != null) pathFollower.Arrived -= HandleArrived;
            pathFollower = value;
            if (pathFollower != null)
            {
                pathFollower.Arrived += HandleArrived;
                // A follower can complete synchronously while it builds its
                // initial path (for example, when spawning inside the core).
                if (pathFollower.HasArrived) HandleArrived(pathFollower);
            }
        }
    }

    internal void Initialize(EnemySpawnPoint spawnPoint, int spawnSequence, int waveNumber, int spawnIndexInWave)
    {
        DetachCallbacks();
        pathFollower = null;
        SpawnPoint = spawnPoint;
        SpawnSequence = spawnSequence;
        SpawnTime = Time.time;
        WaveNumber = waveNumber;
        SpawnIndexInWave = spawnIndexInWave;

        EnsureDamageReceiver();
        health = GetComponent<EnemyHealth>();
        health.ResetHealth();
        economy = EconomyManager.GetOrCreate();
        RunId = economy.RunId;
        KillReward = spawnPoint != null ? spawnPoint.GetKillReward(MonsterType) : 0;
        Resolution = ResolutionState.Alive;
        health.Died += HandleDied;
    }

    private void EnsureDamageReceiver()
    {
        if (GetComponent<EnemyHealth>() == null)
        {
            gameObject.AddComponent<EnemyHealth>();
        }

        // Some imported test models contain only renderers. Add a simple body
        // hit volume so a hitscan weapon has a physical target to intersect.
        if (GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        CapsuleCollider hitCollider = gameObject.AddComponent<CapsuleCollider>();
        Vector3 lossyScale = transform.lossyScale;
        float scaleX = Mathf.Max(0.001f, Mathf.Abs(lossyScale.x));
        float scaleY = Mathf.Max(0.001f, Mathf.Abs(lossyScale.y));
        float scaleZ = Mathf.Max(0.001f, Mathf.Abs(lossyScale.z));
        hitCollider.center = transform.InverseTransformPoint(bounds.center);
        hitCollider.height = Mathf.Max(0.2f, bounds.size.y / scaleY);
        hitCollider.radius = Mathf.Max(0.1f, Mathf.Max(bounds.size.x / scaleX, bounds.size.z / scaleZ) * 0.5f);
        hitCollider.direction = 1;
    }

    private void HandleArrived(MonsterPathFollower follower)
    {
        if (follower.HasCoreTarget && TryResolve(ResolutionState.Leaked))
        {
            if (IsCurrentRun && SpawnPoint != null && SpawnPoint.Core != null)
                SpawnPoint.Core.TakeDamage(MonsterType);
            Destroy(gameObject);
        }
    }

    private bool IsCurrentRun => economy != null && economy == EconomyManager.Instance
        && economy.IsInitialized && economy.RunId == RunId;

    private void HandleDied(EnemyHealth sender)
    {
        if (!TryResolve(ResolutionState.Killed) || !IsCurrentRun) return;
        try
        {
            economy.Grant(KillReward);
        }
        catch (OverflowException)
        {
            // A rejected reward must not interrupt EnemyHealth's destruction.
            Debug.LogWarning("击杀奖励超过金币余额上限，本次奖励未发放。", this);
        }
    }

    private bool TryResolve(ResolutionState result)
    {
        if (Resolution != ResolutionState.Alive) return false;
        Resolution = result;
        DetachCallbacks();
        if (pathFollower != null) pathFollower.enabled = false;
        return true;
    }

    private void DetachCallbacks()
    {
        if (health != null) health.Died -= HandleDied;
        if (pathFollower != null) pathFollower.Arrived -= HandleArrived;
    }

    private void OnDisable()
    {
        TryResolve(ResolutionState.Despawned);
        DetachCallbacks();
    }

    private void OnDestroy()
    {
        TryResolve(ResolutionState.Despawned);
        DetachCallbacks();
    }
}
