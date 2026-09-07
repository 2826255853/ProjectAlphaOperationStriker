using UnityEngine;

/// <summary>
/// The developer-authored destination that enemies are trying to reach.
/// Move this GameObject in the scene to choose the core's position.
/// </summary>
[AddComponentMenu("Tower Defense/Enemy Core")]
public sealed class EnemyCore : MonoBehaviour
{
    [SerializeField, Tooltip("Optional grid used by enemies travelling to this core. If empty, the spawn point's grid is used.")]
    private MonsterPathGrid pathGrid;

    [SerializeField, Min(0.01f), Tooltip("Maximum horizontal distance from the core at which an enemy is considered to have arrived.")]
    private float arrivalRadius = 0.35f;

    public MonsterPathGrid PathGrid => pathGrid;
    public float ArrivalRadius => arrivalRadius;
    public Vector3 Position => transform.position;

    public bool IsWithinArrivalRange(Vector3 worldPosition)
    {
        Vector3 delta = worldPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= arrivalRadius * arrivalRadius;
    }

    private void OnValidate()
    {
        arrivalRadius = Mathf.Max(0.01f, arrivalRadius);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.85f, 0.15f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, arrivalRadius);
        Gizmos.DrawRay(transform.position, transform.up * 0.75f);
    }
}
