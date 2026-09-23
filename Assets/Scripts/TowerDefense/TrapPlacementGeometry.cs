using UnityEngine;

/// <summary>
/// Pure geometry helpers shared by wall and ground placement validation.
/// Keeping the math in one place lets runtime placement, editor painting,
/// gizmos and tests agree on the same installation-box convention instead of
/// re-deriving basis vectors per caller.
/// </summary>
public static class TrapPlacementGeometry
{
    /// <summary>
    /// Fills the eight world-space corners of an oriented box. The sign triple
    /// iteration order is fixed so repeated calls produce comparable results.
    /// </summary>
    public static void FillBoxCorners(Vector3 center, Quaternion rotation, Vector3 halfExtents, Vector3[] corners)
    {
        if (corners == null || corners.Length < 8) return;
        int index = 0;
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        for (int x = -1; x <= 1; x += 2)
            corners[index++] = center + rotation * new Vector3(halfExtents.x * x, halfExtents.y * y, halfExtents.z * z);
    }

    /// <summary>True when a world point lies inside an oriented box (small tolerance by default).</summary>
    public static bool PointInOrientedBox(Vector3 point, Vector3 center, Quaternion rotation, Vector3 halfExtents,
        float tolerance = 0.001f)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - center);
        return Mathf.Abs(local.x) <= halfExtents.x + tolerance
            && Mathf.Abs(local.y) <= halfExtents.y + tolerance
            && Mathf.Abs(local.z) <= halfExtents.z + tolerance;
    }

    /// <summary>
    /// World vertical span of an oriented box. The monster-clearance test needs
    /// true world height extents, which differ from the local half sizes once a
    /// box is rotated.
    /// </summary>
    public static void GetWorldVerticalSpan(Vector3 center, Quaternion rotation, Vector3 halfExtents,
        out float bottom, out float top)
    {
        float extent = Mathf.Abs((rotation * Vector3.up).y) * halfExtents.y
            + Mathf.Abs((rotation * Vector3.right).y) * halfExtents.x
            + Mathf.Abs((rotation * Vector3.forward).y) * halfExtents.z;
        bottom = center.y - extent;
        top = center.y + extent;
    }

    /// <summary>
    /// In-plane probe pattern for one placement cell: center plus the four inset
    /// corners. <paramref name="right"/> and <paramref name="up"/> are unit
    /// in-plane axes; <paramref name="cellOrigin"/> is the cell's lower-left
    /// corner in world space.
    /// </summary>
    public static void FillCellProbePattern(Vector3 cellOrigin, Vector3 right, Vector3 up, float cellSize,
        float inset, Vector3[] probes)
    {
        if (probes == null || probes.Length < 5) return;
        float low = Mathf.Clamp(inset, 0f, cellSize * 0.45f);
        float high = cellSize - low;
        probes[0] = cellOrigin + right * (cellSize * 0.5f) + up * (cellSize * 0.5f);
        probes[1] = cellOrigin + right * low + up * low;
        probes[2] = cellOrigin + right * high + up * low;
        probes[3] = cellOrigin + right * high + up * high;
        probes[4] = cellOrigin + right * low + up * high;
    }
}
