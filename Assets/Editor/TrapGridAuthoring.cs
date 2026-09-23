using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared authoring math for trap placement grids: where a trap surface is,
/// which of its cells are usable, and at what height a trap must sit. Both the
/// trap grid inspector tools and the MapForge scene importer use this so a
/// freshly imported map already ships with a ground grid and a platform grid.
/// </summary>
public static class TrapGridAuthoring
{
    /// <summary>A trap sits this far above the surface it is placed on.</summary>
    public const float SurfaceOffset = 0.02f;

    /// <summary>Trap bases follow physical road geometry, not the elevated monster travel plane.</summary>
    public static float RoadPlacementHeight(MonsterPathGrid grid)
    {
        return DominantSurfaceTop(grid, GroundAnchor(grid), grid.transform.rotation,
            CollectSurfaceColliders(), grid.CreateOpenCellSnapshot(), grid.PathHeight) + SurfaceOffset;
    }

    /// <summary>
    /// The monster grid anchors cell (x, y) at its lower corner while trap grids
    /// anchor it at the cell centre, so the trap grid origin is shifted half a
    /// cell along the monster grid's local X and Z. With this offset a trap
    /// placed on cell (x, y) lands exactly on the centre of the same cell the
    /// monsters walk through - no half-cell drift.
    /// </summary>
    public static Vector3 GroundAnchor(MonsterPathGrid monsterGrid)
    {
        float half = monsterGrid.CellSize * 0.5f;
        return monsterGrid.transform.position +
            monsterGrid.transform.TransformVector(new Vector3(half, 0f, half));
    }

    /// <summary>World centre of a trap cell for the given lattice.</summary>
    public static Vector3 CellCenter(MonsterPathGrid monsterGrid, Vector3 anchor, Quaternion rotation, int x, int y)
    {
        return anchor + rotation * new Vector3(x * monsterGrid.CellSize, 0f, y * monsterGrid.CellSize);
    }

    /// <summary>
    /// Collects every raised surface that traps may stand on. MapForge names
    /// them Platform_*; anything else has to be clearly taller than the ground
    /// to qualify, and markers are excluded because they are decoration.
    /// </summary>
    public static List<Collider> CollectPlatformColliders()
    {
        var platforms = new List<Collider>();
        Collider[] all = Object.FindObjectsByType<Collider>();
        for (int i = 0; i < all.Length; i++)
            if (IsPlatformCollider(all[i])) platforms.Add(all[i]);
        return platforms;
    }

    /// <summary>
    /// Collects every walkable/visible surface collider (ground slabs, roads,
    /// platforms). Decorative markers and already placed traps are ignored.
    /// </summary>
    public static List<Collider> CollectSurfaceColliders()
    {
        var surfaces = new List<Collider>();
        Collider[] all = Object.FindObjectsByType<Collider>();
        for (int i = 0; i < all.Length; i++)
        {
            Collider collider = all[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (collider.gameObject.name.IndexOf("Marker", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (collider.GetComponentInParent<TrapInstance>() != null) continue;
            surfaces.Add(collider);
        }
        return surfaces;
    }

    /// <summary>
    /// Most common top surface height under the given cells. A grid uses this so
    /// its traps rest on the geometry it covers instead of floating above it or
    /// sinking into it.
    /// </summary>
    public static float DominantSurfaceTop(MonsterPathGrid monsterGrid, Vector3 anchor, Quaternion rotation,
        List<Collider> surfaces, bool[] cellMask, float fallback)
    {
        if (monsterGrid == null || surfaces == null || surfaces.Count == 0 || cellMask == null) return fallback;
        float half = monsterGrid.CellSize * 0.5f;
        Vector3 right = rotation * Vector3.right * half;
        Vector3 forward = rotation * Vector3.forward * half;
        var buckets = new Dictionary<int, int>();
        for (int y = 0; y < monsterGrid.Rows; y++)
        for (int x = 0; x < monsterGrid.Columns; x++)
        {
            int index = x + y * monsterGrid.Columns;
            if (index >= cellMask.Length || !cellMask[index]) continue;
            Vector3 center = CellCenter(monsterGrid, anchor, rotation, x, y);
            float top = float.NegativeInfinity;
            for (int i = 0; i < surfaces.Count; i++)
            {
                Bounds bounds = surfaces[i].bounds;
                if (!CellFitsBounds(center, right, forward, bounds)) continue;
                if (bounds.max.y > top) top = bounds.max.y;
            }
            if (top == float.NegativeInfinity) continue;
            int bucket = Mathf.RoundToInt(top * 100f);
            buckets.TryGetValue(bucket, out int count);
            buckets[bucket] = count + 1;
        }
        if (buckets.Count == 0) return fallback;
        int bestBucket = 0, bestCount = -1;
        foreach (var pair in buckets)
            if (pair.Value > bestCount) { bestCount = pair.Value; bestBucket = pair.Key; }
        return bestBucket / 100f;
    }

    public static bool IsPlatformCollider(Collider collider)
    {
        if (collider == null || !collider.enabled) return false;
        GameObject go = collider.gameObject;
        if (!go.activeInHierarchy) return false;
        if (go.name.IndexOf("Marker", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (go.name.StartsWith("Platform", System.StringComparison.OrdinalIgnoreCase)) return true;
        // Fallback for maps that do not follow the MapForge Platform_* naming.
        return collider.bounds.max.y >= 1.5f;
    }

    /// <summary>
    /// Builds the availability mask for the raised platform level. A cell is
    /// usable when a platform top covers it completely and the cell is not part
    /// of the monster lane, so platform traps can never block the enemy route.
    /// Returns null when the map has no usable platform surface.
    /// </summary>
    public static bool[] BuildPlatformMask(MonsterPathGrid monsterGrid, Vector3 anchor, Quaternion rotation,
        List<Collider> platforms, out float topHeight, out int skippedCells)
    {
        topHeight = 0f;
        skippedCells = 0;
        if (monsterGrid == null || platforms == null || platforms.Count == 0) return null;

        int columns = monsterGrid.Columns;
        int rows = monsterGrid.Rows;
        float half = monsterGrid.CellSize * 0.5f;
        Vector3 right = rotation * Vector3.right * half;
        Vector3 forward = rotation * Vector3.forward * half;
        var cellTops = new float[columns * rows];
        var hasTop = new bool[columns * rows];
        var buckets = new Dictionary<int, int>();

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            if (monsterGrid.IsOpen(new Vector2Int(x, y))) continue;
            Vector3 center = CellCenter(monsterGrid, anchor, rotation, x, y);
            float bestTop = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < platforms.Count; i++)
            {
                Bounds bounds = platforms[i].bounds;
                if (!CellFitsBounds(center, right, forward, bounds)) continue;
                if (bounds.max.y > bestTop) bestTop = bounds.max.y;
                found = true;
            }
            if (!found) continue;
            int index = x + y * columns;
            cellTops[index] = bestTop;
            hasTop[index] = true;
            int bucket = Mathf.RoundToInt(bestTop * 100f);
            buckets.TryGetValue(bucket, out int count);
            buckets[bucket] = count + 1;
        }
        if (buckets.Count == 0) return null;

        int dominantBucket = 0, dominantCount = -1;
        foreach (var pair in buckets)
            if (pair.Value > dominantCount) { dominantCount = pair.Value; dominantBucket = pair.Key; }
        topHeight = dominantBucket / 100f;

        var mask = new bool[columns * rows];
        int open = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!hasTop[i]) continue;
            if (Mathf.Abs(cellTops[i] - topHeight) > 0.02f) { skippedCells++; continue; }
            mask[i] = true;
            open++;
        }
        return open > 0 ? mask : null;
    }

    /// <summary>
    /// Builds the availability mask for open ground: every cell beside the
    /// monster lane whose ground surface is not buried under a raised platform.
    /// Road cells belong to the lane grid and platform cells to the platform
    /// grid, so every map cell is offered on exactly one level.
    /// </summary>
    public static bool[] BuildGroundMask(MonsterPathGrid monsterGrid, Vector3 anchor, Quaternion rotation,
        List<Collider> platforms, out int reservedCells)
    {
        reservedCells = 0;
        if (monsterGrid == null) return null;
        int columns = monsterGrid.Columns;
        int rows = monsterGrid.Rows;
        float half = monsterGrid.CellSize * 0.5f;
        Vector3 right = rotation * Vector3.right * half;
        Vector3 forward = rotation * Vector3.forward * half;
        var mask = new bool[columns * rows];

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            Vector3 center = CellCenter(monsterGrid, anchor, rotation, x, y);
            bool buried = false;
            if (platforms != null)
                for (int i = 0; i < platforms.Count; i++)
                {
                    if (!CellFitsBounds(center, right, forward, platforms[i].bounds)) continue;
                    buried = true;
                    break;
                }
            if (!buried && monsterGrid.IsOpen(new Vector2Int(x, y))) buried = true; // lane: covered by the road grid
            int index = x + y * columns;
            if (buried) { reservedCells++; continue; }
            mask[index] = true;
        }
        return mask;
    }

    private static bool CellFitsBounds(Vector3 center, Vector3 right, Vector3 forward, Bounds bounds)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 point = i == 4 ? center
                : center + (i % 2 == 0 ? right : -right) + (i < 2 ? forward : -forward);
            if (point.x < bounds.min.x || point.x > bounds.max.x) return false;
            if (point.z < bounds.min.z || point.z > bounds.max.z) return false;
        }
        return true;
    }
}
