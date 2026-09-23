using UnityEngine;

[CreateAssetMenu(menuName = "Tower Defense/Trap Definition", fileName = "TrapDefinition")]
public sealed class TrapDefinition : ScriptableObject
{
    // TODO(确认): 首版基础调试价 40；各陷阱独立平衡后以资产中的 Cost 为准。
    public const int DefaultCost = 40;

    [SerializeField] private string trapId = "trap";
    [SerializeField] private string displayName = "Trap";
    [SerializeField] private GameObject prefab;
    [SerializeField, Min(0), Tooltip("放置价格（金币）；0 表示明确配置的免费陷阱。")]
    private int cost = DefaultCost;
    [SerializeField, Min(1)] private int footprintWidth = 1;
    [SerializeField, Min(1)] private int footprintHeight = 1;
    [SerializeField] private Vector3 localRotation;
    [SerializeField, Tooltip("地刺等地面陷阱：只能放在地面（含道路），不能放在高台。关闭时为可放在地面和高台的普通陷阱。")]
    private bool walkableFloorTrap;
    [SerializeField, Tooltip("固定世界尺寸（米）；零表示沿用网格占位尺寸。")]
    private Vector2 worldFootprint;
    [SerializeField, Tooltip("安装类别：Ground=地面/道路/高台，Wall=指定墙面网格。旧资产缺字段时默认为 Ground。")]
    private TrapMountType mountType = TrapMountType.Ground;

    public string TrapId => trapId;
    public string DisplayName => displayName;
    public GameObject Prefab => prefab;
    public int Cost => cost;
    public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprintWidth), Mathf.Max(1, footprintHeight));
    public Quaternion LocalRotation => Quaternion.Euler(localRotation);
    public bool WalkableFloorTrap => walkableFloorTrap;
    // Ground includes the monster road. Placement rules apply to categories,
    // not a whitelist of today's sentry and launcher prefabs.
    public bool AllowsPlatformPlacement => !walkableFloorTrap;
    public Vector2 WorldFootprint => worldFootprint;
    public TrapMountType MountType => mountType;

    /// <summary>True when this definition may be installed on the given surface category.</summary>
    public bool SupportsMountType(TrapMountType surfaceType) => surfaceType.SupportsDefinition(this);

    private void OnValidate()
    {
        // Wall-mounted traps hang on a vertical backplate; the walkable-floor
        // flag only describes ground spikes. Rejecting the pair keeps the
        // intent explicit instead of silently ignoring one of the two flags.
        if (mountType == TrapMountType.Wall && walkableFloorTrap)
            Debug.LogWarning($"[TrapDefinition] {name}: Wall 与 WalkableFloorTrap 不能同时开启，" +
                "该组合无效；请关闭 WalkableFloorTrap。", this);
    }

    public bool TryGetFootprint(float worldCellSize, out Vector2Int footprint)
    {
        footprint = Footprint;
        if (worldFootprint == Vector2.zero) return true;
        if (worldCellSize <= 0f) return false;
        Vector2 cells = worldFootprint / worldCellSize;
        if (cells.x <= 0f || cells.y <= 0f) return false;
        // Reserve every covered cell on a coarse lattice; never resize the authored model.
        footprint = new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(cells.x - 0.001f)),
            Mathf.Max(1, Mathf.CeilToInt(cells.y - 0.001f)));
        return true;
    }
}
