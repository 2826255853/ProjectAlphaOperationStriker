using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional multi-storey mission routes attached to an enemy entrance.</summary>
[DisallowMultipleComponent]
public sealed class AuthoredMonsterRoute : MonoBehaviour
{
    [Serializable]
    public sealed class Route
    {
        public Transform[] points = Array.Empty<Transform>();
    }

    [SerializeField] private Route[] routes = Array.Empty<Route>();

    public IReadOnlyList<Route> Routes => routes;

    public void Configure(Transform[][] branches)
    {
        if (branches == null || branches.Length == 0)
            throw new ArgumentException("At least one authored route is required.", nameof(branches));
        routes = new Route[branches.Length];
        for (int i = 0; i < branches.Length; i++)
            routes[i] = new Route { points = (Transform[])branches[i].Clone() };
    }

    public List<Vector3> GetRoute(int spawnIndex, EnemyCore core)
    {
        if (routes == null || routes.Length == 0)
            throw new InvalidOperationException("Enemy entrance has no authored routes.");
        Route route = routes[Mathf.Abs(spawnIndex - 1) % routes.Length];
        var positions = new List<Vector3>();
        foreach (Transform point in route.points)
        {
            if (point == null) throw new InvalidOperationException("An authored enemy route point is missing.");
            positions.Add(point.position);
        }
        if (positions.Count == 0) throw new InvalidOperationException("An authored enemy route is empty.");
        if (core != null)
        {
            // The decorative core is elevated above its platform; monsters finish
            // at its horizontal location while keeping their final walking height.
            Vector3 end = core.Position;
            end.y = positions[positions.Count - 1].y;
            positions.Add(end);
        }
        return positions;
    }
}
