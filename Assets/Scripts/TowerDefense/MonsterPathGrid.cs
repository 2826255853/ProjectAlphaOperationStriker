using System.Collections.Generic;
using UnityEngine;

/// <summary>Grid describing the cells that monsters may traverse.</summary>
public sealed class MonsterPathGrid : MonoBehaviour
{
    [Header("Grid layout")]
    [SerializeField, Min(1)] private int columns = 16;
    [SerializeField, Min(1)] private int rows = 10;
    [SerializeField, Min(1f)] private float cellSize = 1f;
    [SerializeField] private float pathHeight;

    [Header("Monster availability")]
    [Tooltip("Row-major (x + y * columns). True cells are traversable by monsters.")]
    [SerializeField] private bool[] openCells;

    public int Columns => columns;
    public int Rows => rows;
    public float CellSize => cellSize;
    public float PathHeight => pathHeight;

    /// <summary>Copies the authored walkable mask without exposing its backing array.</summary>
    public bool[] CreateOpenCellSnapshot()
    {
        EnsureCellData();
        return (bool[])openCells.Clone();
    }

    private void Awake() => EnsureCellData();

    private void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        cellSize = Mathf.Max(1f, cellSize);
        EnsureCellData();
    }

    public void EnsureCellData()
    {
        int count = columns * rows;
        if (openCells != null && openCells.Length == count) return;
        bool[] previous = openCells;
        openCells = new bool[count];
        if (previous != null) System.Array.Copy(previous, openCells, Mathf.Min(previous.Length, count));
    }

    public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows;

    public bool IsOpen(Vector2Int cell)
    {
        EnsureCellData();
        return IsInside(cell) && openCells[cell.x + cell.y * columns];
    }

    public void SetOpen(Vector2Int cell, bool open)
    {
        EnsureCellData();
        if (IsInside(cell)) openCells[cell.x + cell.y * columns] = open;
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        Vector3 local = new Vector3((cell.x + 0.5f) * cellSize, pathHeight, (cell.y + 0.5f) * cellSize);
        return transform.TransformPoint(local);
    }

    public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.z / cellSize));
        return IsInside(cell);
    }

    /// <summary>Builds a shortest path. Direction is used as a tie-breaker so routes follow authored flow.</summary>
    public bool TryFindPath(Vector3 startWorld, Vector3 targetWorld, Vector3 travelDirection, List<Vector3> result)
    {
        return TryFindPath(startWorld, targetWorld, travelDirection, result, out _);
    }

    /// <summary>
    /// Builds a path toward a target. If the target is unreachable, result contains
    /// the route to the closest reachable open cell and reachedTarget is false.
    /// </summary>
    public bool TryFindPath(Vector3 startWorld, Vector3 targetWorld, Vector3 travelDirection, List<Vector3> result, out bool reachedTarget)
    {
        reachedTarget = false;
        if (result == null) return false;
        result.Clear();
        Vector2Int start = WorldToCellUnclamped(startWorld);
        Vector2Int target = WorldToCellUnclamped(targetWorld);
        if (!IsOpen(start)) start = FindNearestOpen(start);
        if (!IsOpen(target)) target = FindNearestOpen(target);
        if (!IsOpen(start) || !IsOpen(target)) return false;
        Vector2Int requestedTarget = WorldToCellUnclamped(targetWorld);
        if (start == target)
        {
            reachedTarget = requestedTarget == target && IsOpen(requestedTarget);
            result.Add(reachedTarget ? targetWorld : CellToWorld(target));
            return true;
        }

        var frontier = new List<Vector2Int> { start };
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var cost = new Dictionary<Vector2Int, float> { [start] = 0f };
        Vector2Int closestCell = start;
        int closestDistance = Manhattan(start, target);
        Vector2 direction = new Vector2(travelDirection.x, travelDirection.z).normalized;
        Vector2Int[] neighbours = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        while (frontier.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < frontier.Count; i++)
            {
                Vector2Int c = frontier[i];
                float score = cost[c] + Manhattan(c, target);
                if (direction.sqrMagnitude > 0.001f)
                {
                    Vector2 step = new Vector2(c.x - start.x, c.y - start.y).normalized;
                    score -= Vector2.Dot(step, direction) * 0.001f;
                }
                if (score < bestScore) { bestScore = score; bestIndex = i; }
            }

            Vector2Int current = frontier[bestIndex];
            frontier.RemoveAt(bestIndex);
            int distanceToTarget = Manhattan(current, target);
            if (distanceToTarget < closestDistance)
            {
                closestCell = current;
                closestDistance = distanceToTarget;
            }
            if (current == target)
            {
                AddPathToResult(start, current, cameFrom, result);
                reachedTarget = requestedTarget == target && IsOpen(requestedTarget);
                if (reachedTarget) result.Add(targetWorld);
                return result.Count > 0;
            }

            foreach (Vector2Int offset in neighbours)
            {
                Vector2Int next = current + offset;
                if (!IsOpen(next)) continue;
                float directionalPenalty = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    Vector2 step = new Vector2(offset.x, offset.y).normalized;
                    // Keep the route shortest, while preferring cells that advance in the authored direction.
                    directionalPenalty = (1f - Vector2.Dot(step, direction)) * 0.05f;
                }
                float newCost = cost[current] + 1f + directionalPenalty;
                if (!cost.TryGetValue(next, out float oldCost) || newCost < oldCost)
                {
                    cost[next] = newCost;
                    cameFrom[next] = current;
                    if (!frontier.Contains(next)) frontier.Add(next);
                }
            }
        }

        // If the core is separated from the spawn area, walk to the closest
        // reachable open cell instead of incorrectly treating the enemy as arrived.
        if (closestCell != start)
            AddPathToResult(start, closestCell, cameFrom, result);
        return result.Count > 0;
    }

    private void AddPathToResult(Vector2Int start, Vector2Int end, Dictionary<Vector2Int, Vector2Int> cameFrom, List<Vector3> result)
    {
        var cells = new List<Vector2Int>();
        Vector2Int current = end;
        while (current != start)
        {
            cells.Add(current);
            if (!cameFrom.TryGetValue(current, out current)) return;
        }

        cells.Reverse();
        foreach (Vector2Int cell in cells) result.Add(CellToWorld(cell));
    }

    private Vector2Int FindNearestOpen(Vector2Int origin)
    {
        Vector2Int best = origin; int bestDistance = int.MaxValue;
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            Vector2Int c = new Vector2Int(x, y);
            if (!IsOpen(c)) continue;
            int distance = Mathf.Abs(c.x - origin.x) + Mathf.Abs(c.y - origin.y);
            if (distance < bestDistance) { bestDistance = distance; best = c; }
        }
        return bestDistance == int.MaxValue ? origin : best;
    }

    private Vector2Int WorldToCellUnclamped(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.z / cellSize));
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private void OnDrawGizmos()
    {
        EnsureCellData();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            Vector3 center = CellToWorld(new Vector2Int(x, y));
            Gizmos.color = IsOpen(new Vector2Int(x, y)) ? new Color(0.2f, 0.8f, 1f, 0.25f) : new Color(0.15f, 0.15f, 0.15f, 0.12f);
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(cellSize * 0.96f, 0.025f, cellSize * 0.96f));
            Gizmos.color = new Color(0f, 0f, 0f, 0.3f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(cellSize, 0.03f, cellSize));
            Gizmos.matrix = old;
        }
    }
}
