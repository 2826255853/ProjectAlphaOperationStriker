using UnityEngine;

/// <summary>Stable MapForge identity. Only generated ceiling grids carry this marker.</summary>
[DisallowMultipleComponent]
public sealed class MapForgeCeilingTrapGrid : MonoBehaviour
{
    public string objectId;
    public string gridId;
    [HideInInspector] public string mappingJson;
}
