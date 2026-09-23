using UnityEngine;

/// <summary>
/// Installation surface category shared by trap definitions and placement grids.
/// A trap may only be placed on a grid whose mount type matches its own; keeping
/// a single enum avoids maintaining two parallel type mappings.
/// </summary>
public enum TrapMountType
{
    /// <summary>Ground, road and platform surfaces. Default for all legacy assets.</summary>
    Ground = 0,
    /// <summary>Developer-marked, flat, vertical wall surfaces.</summary>
    Wall = 1,
}

public static class TrapMountTypeExtensions
{
    /// <summary>
    /// True when the placement grid may host the given definition. Wall grids
    /// never accept a <see cref="TrapDefinition.WalkableFloorTrap"/> (ground
    /// spike) definition: wall plus walkable-floor support is an invalid pairing.
    /// </summary>
    public static bool SupportsDefinition(this TrapMountType surfaceType, TrapDefinition definition)
    {
        if (definition == null) return false;
        if (definition.MountType != surfaceType) return false;
        return !(surfaceType == TrapMountType.Wall && definition.WalkableFloorTrap);
    }

    /// <summary>Reason text used by placement previews and editor validation.</summary>
    public static string DescribeMismatch(this TrapMountType surfaceType)
    {
        return surfaceType == TrapMountType.Wall
            ? "仅限墙面：该陷阱只能安装在标记过的墙面网格上。"
            : "仅限地面：该陷阱只能安装在地面、道路或高台网格上。";
    }
}
