using UnityEngine;

public sealed class TrapInstance : MonoBehaviour
{
    // Serialized so editor-authored traps retain their identity after a domain
    // reload or when the scene is reopened. The grid rebuilds its occupancy map
    // from these values on enable.
    [SerializeField] private TrapDefinition definition;
    [SerializeField] private Vector2Int originCell;

    public TrapDefinition Definition => definition;
    public Vector2Int OriginCell => originCell;

    internal void Initialize(TrapDefinition definition, Vector2Int originCell)
    {
        this.definition = definition;
        this.originCell = originCell;
        gameObject.name = definition == null ? "Trap" : definition.DisplayName;
    }
}
