using UnityEngine;

/// <summary>
/// Marks a collider as an enemy weak point. Attach this component to a child
/// object that has the collider used for the weak-point hit area.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EnemyWeakPoint : MonoBehaviour
{
    [SerializeField, Min(1f)] private float damageMultiplier = 2f;

    /// <summary>Damage multiplier applied when this collider is hit.</summary>
    public float DamageMultiplier => Mathf.Max(1f, damageMultiplier);

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(1f, damageMultiplier);
    }

    private void OnDrawGizmosSelected()
    {
        Collider hitCollider = GetComponent<Collider>();
        if (hitCollider == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.matrix = transform.localToWorldMatrix;
        if (hitCollider is SphereCollider sphere)
        {
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
        else if (hitCollider is BoxCollider box)
        {
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else
        {
            Gizmos.DrawWireCube(hitCollider.bounds.center, hitCollider.bounds.size);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
