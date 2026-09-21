using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored MapForge path node: the role it plays in the monster route and the nodes it links to.
/// Gameplay code can read <see cref="All"/> or link <see cref="links"/> back to GameObjects.
/// </summary>
[AddComponentMenu("MapForge/Path Node")]
[DisallowMultipleComponent]
public sealed class MapForgePathNode : MonoBehaviour
{
    public enum Role
    {
        Node = 0,
        Waypoint = 1,
        Junction = 2,
        End = 3
    }

    [Tooltip("路径节点在路线中的角色。")]
    public Role role = Role.Node;

    [Min(0f), Tooltip("怪物到达后停留的秒数。")]
    public float waitTime;

    [Tooltip("后继节点的 MapForge 对象 ID。")]
    public string[] links = new string[0];

    private static readonly List<MapForgePathNode> registry = new List<MapForgePathNode>();

    /// <summary>Every enabled path node currently loaded in the scene.</summary>
    public static IReadOnlyList<MapForgePathNode> All => registry;

    /// <summary>Resolves a link ID against the loaded scene. Returns null when the target is missing.</summary>
    public static MapForgePathNode Find(string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return null;
        foreach (var node in registry)
        {
            var metadata = node.GetComponent<MapForgeObjectProperties>();
            if (metadata == null ? node.name == objectId : metadata.objectId == objectId) return node;
        }
        return null;
    }

    private void OnEnable() { if (!registry.Contains(this)) registry.Add(this); }
    private void OnDisable() { registry.Remove(this); }

    private void OnDrawGizmos()
    {
        Gizmos.color = role == Role.End ? new Color(1f, .61f, .39f) : new Color(.41f, .88f, .75f);
        Gizmos.DrawWireSphere(transform.position, role == Role.Junction ? .45f : .3f);
        if (links == null) return;
        foreach (var id in links)
        {
            var target = Find(id);
            if (target != null) Gizmos.DrawLine(transform.position, target.transform.position);
        }
    }
}
