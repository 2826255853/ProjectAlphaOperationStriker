using UnityEngine;

/// <summary>
/// Pure geometry helpers shared by wall and ground placement validation.
/// Keeping the math in one place lets runtime placement, editor painting,
/// gizmos and tests agree on the same installation-box convention instead of
/// re-deriving basis vectors per caller.
/// </summary>
public static class TrapPlacementGeometry
{
    /// <summary>Occupied ground rectangle, using the placement lattice rather than visual bounds.</summary>
    public static bool TryGetGroundFootprint(TrapInstance trap, Vector3[] corners)
    {
        if (trap == null || !trap.isActiveAndEnabled || trap.IsDestroyed || trap.Definition == null
            || trap.Definition.MountType != TrapMountType.Ground
            || trap.GetComponentInParent<TrapPlacementPreview>() != null || corners == null || corners.Length < 4)
            return false;
        TrapPlacementGrid grid = trap.OwnerGrid;
        if (grid == null || !grid.isActiveAndEnabled || !grid.OwnsTrap(trap)
            || grid.SurfaceType != TrapMountType.Ground
            || !grid.TryGetFootprint(trap.Definition, out Vector2Int footprint)) return false;
        grid.GetPlacementPose(trap.OriginCell, trap.Definition, out Vector3 center, out Quaternion rotation);
        // Manually displaced traps no longer occupy their recorded cells for repair purposes.
        if ((trap.transform.position - center).sqrMagnitude > 0.0004f
            || Quaternion.Angle(trap.transform.rotation, rotation) > 0.1f) return false;
        Vector2 size = grid.CellWorldSize;
        Vector3 right = grid.transform.right * (footprint.x * size.x * 0.5f);
        Vector3 forward = grid.transform.forward * (footprint.y * size.y * 0.5f);
        corners[0] = center - right - forward;
        corners[1] = center + right - forward;
        corners[2] = center + right + forward;
        corners[3] = center - right + forward;
        return true;
    }

    /// <summary>
    /// Longest coplanar shared edge, assigned to front/right/back/left (0..3).
    /// Works across grids, including scaled and rotated grids; corner contact has zero length.
    /// Scratch arrays are caller-owned to avoid allocating in the target scan.
    /// </summary>
    public static int FindSharedGroundSide(TrapInstance source, TrapInstance target,
        Vector3[] sourceCorners, Vector3[] targetCorners)
    {
        if (source == target || !TryGetGroundFootprint(source, sourceCorners)
            || !TryGetGroundFootprint(target, targetCorners)) return -1;
        Vector3 up = source.OwnerGrid.transform.up;
        if (Vector3.Dot(up, target.OwnerGrid.transform.up) < 0.9999f
            || Mathf.Abs(Vector3.Dot(targetCorners[0] - sourceCorners[0], up)) > 0.02f) return -1;
        int bestSide = -1;
        float bestLength = 0.02f;
        for (int edge = 0; edge < 4; edge++)
        {
            Vector3 start = sourceCorners[edge];
            Vector3 delta = sourceCorners[(edge + 1) % 4] - start;
            float length = delta.magnitude;
            Vector3 axis = delta / length;
            Vector3 outward = Vector3.Cross(up, axis);
            Vector3 localNormal = source.transform.InverseTransformDirection(outward);
            int side = Mathf.Abs(localNormal.z) >= Mathf.Abs(localNormal.x)
                ? (localNormal.z >= 0f ? 0 : 2) : (localNormal.x >= 0f ? 1 : 3);
            for (int other = 0; other < 4; other++)
            {
                Vector3 a = targetCorners[other];
                Vector3 b = targetCorners[(other + 1) % 4];
                Vector3 otherAxis = (b - a).normalized;
                // Opposite directed edges mean the interiors are on opposite sides.
                if (Vector3.Dot(axis, otherAxis) > -0.9999f
                    || Mathf.Abs(Vector3.Dot(a - start, outward)) > 0.02f
                    || Mathf.Abs(Vector3.Dot(b - start, outward)) > 0.02f) continue;
                float pa = Vector3.Dot(a - start, axis), pb = Vector3.Dot(b - start, axis);
                float shared = Mathf.Min(length, Mathf.Max(pa, pb)) - Mathf.Max(0f, Mathf.Min(pa, pb));
                if (shared > bestLength + 0.0001f
                    || (shared > 0.02f && Mathf.Abs(shared - bestLength) <= 0.0001f
                        && (bestSide < 0 || side < bestSide)))
                {
                    bestLength = shared;
                    bestSide = side;
                }
            }
        }
        return bestSide;
    }

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
