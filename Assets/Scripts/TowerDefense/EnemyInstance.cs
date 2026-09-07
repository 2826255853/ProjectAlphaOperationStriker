using UnityEngine;

/// <summary>Runtime identity for an enemy created by an <see cref="EnemySpawnPoint"/>.</summary>
public sealed class EnemyInstance : MonoBehaviour
{
    public EnemySpawnPoint SpawnPoint { get; private set; }
    public int SpawnSequence { get; private set; }
    public float SpawnTime { get; private set; }
    public int WaveNumber { get; private set; }
    public int SpawnIndexInWave { get; private set; }
    private MonsterPathFollower pathFollower;

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
        SpawnPoint = spawnPoint;
        SpawnSequence = spawnSequence;
        SpawnTime = Time.time;
        WaveNumber = waveNumber;
        SpawnIndexInWave = spawnIndexInWave;

        EnsureDamageReceiver();
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
        if (follower.HasCoreTarget) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (pathFollower != null) pathFollower.Arrived -= HandleArrived;
    }
}
