using UnityEngine;

/// <summary>
/// Stable identity for a wall-trap grid authored by MapForge.
/// The owning wall object ID and grid ID together form the sync key.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapForgeWallTrapGrid : MonoBehaviour
{
    [SerializeField] public string objectId;
    [SerializeField] public string gridId;
}
