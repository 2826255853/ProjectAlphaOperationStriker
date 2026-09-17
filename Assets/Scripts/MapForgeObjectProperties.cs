using UnityEngine;

/// <summary>Authored MapForge metadata. Gameplay systems may interpret prefabType and parametersJson.</summary>
[DisallowMultipleComponent]
public sealed class MapForgeObjectProperties : MonoBehaviour
{
    public string objectId;
    public string objectType;
    public string[] tags;
    public string prefabType;
    [TextArea(3, 12)] public string parametersJson = "{}";
    public string category;
    public bool visible = true;
    public bool locked;
    public bool collision = true;
    public bool collapsed;
}
