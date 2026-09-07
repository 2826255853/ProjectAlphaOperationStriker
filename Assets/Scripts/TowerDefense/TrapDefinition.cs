using UnityEngine;

[CreateAssetMenu(menuName = "Tower Defense/Trap Definition", fileName = "TrapDefinition")]
public sealed class TrapDefinition : ScriptableObject
{
    [SerializeField] private string trapId = "trap";
    [SerializeField] private string displayName = "Trap";
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0)] private int cost;
    [SerializeField, Min(1)] private int footprintWidth = 1;
    [SerializeField, Min(1)] private int footprintHeight = 1;
    [SerializeField] private Vector3 localRotation;

    public string TrapId => trapId;
    public string DisplayName => displayName;
    public GameObject Prefab => prefab;
    public int Cost => cost;
    public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprintWidth), Mathf.Max(1, footprintHeight));
    public Quaternion LocalRotation => Quaternion.Euler(localRotation);
}
