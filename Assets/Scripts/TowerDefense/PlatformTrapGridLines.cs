using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Visible platform cell boundaries, using the placement mask as their source.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TrapPlacementGrid))]
public sealed class PlatformTrapGridLines : MonoBehaviour
{
    [SerializeField] private Color lineColor = new Color(.035f, .22f, .29f, 1f);
    [SerializeField, Min(.005f)] private float lineWidth = .035f;
    [SerializeField, Min(.001f)] private float lift = .012f;
    private GameObject visual;
    private Mesh mesh;
    private Material material;
    private bool rebuild;

    private void OnEnable() => Rebuild();
    private void OnValidate() => rebuild = true;
    private void Update()
    {
        if (!rebuild) return;
        rebuild = false;
        Rebuild();
    }
    private void OnDisable() => Release();

    public void Rebuild()
    {
        Release();
        var grid = GetComponent<TrapPlacementGrid>();
        if (grid == null || grid.IsMountedSurface || grid.Surface != TrapPlacementGrid.SurfaceKind.Platform) return;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float half = grid.CellSize * .5f;
        for (int y = 0; y < grid.Rows; y++)
        for (int x = 0; x < grid.Columns; x++)
        {
            var cell = new Vector2Int(x, y);
            if (!grid.IsOpen(cell)) continue;
            // Ground cells are anchored at their centres. Keep the visible
            // boundaries half a cell away from that same placement centre.
            var center = transform.InverseTransformPoint(grid.CellToWorld(cell)) + Vector3.up * lift;
            var a = center + new Vector3(-half, 0f, -half);
            var b = center + new Vector3(half, 0f, -half);
            var c = center + new Vector3(half, 0f, half);
            var d = center + new Vector3(-half, 0f, half);
            AddLine(a, b, vertices, triangles);
            AddLine(a, d, vertices, triangles);
            if (!grid.IsOpen(cell + Vector2Int.right)) AddLine(b, c, vertices, triangles);
            if (!grid.IsOpen(cell + Vector2Int.up)) AddLine(d, c, vertices, triangles);
        }
        if (vertices.Count == 0) return;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return;
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = lineColor };
        mesh = new Mesh { name = "Platform placement cell lines", hideFlags = HideFlags.HideAndDontSave,
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        visual = new GameObject("Platform placement grid lines") { hideFlags = HideFlags.HideAndDontSave };
        visual.transform.SetParent(transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void AddLine(Vector3 start, Vector3 end, List<Vector3> vertices, List<int> triangles)
    {
        var side = Vector3.Cross(Vector3.up, (end - start).normalized) * (lineWidth * .5f);
        int i = vertices.Count;
        vertices.Add(start - side); vertices.Add(start + side);
        vertices.Add(end + side); vertices.Add(end - side);
        triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 1);
        triangles.Add(i); triangles.Add(i + 3); triangles.Add(i + 2);
    }

    private void Release()
    {
        Dispose(visual); Dispose(mesh); Dispose(material);
        visual = null; mesh = null; material = null;
    }

    private static void Dispose(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
