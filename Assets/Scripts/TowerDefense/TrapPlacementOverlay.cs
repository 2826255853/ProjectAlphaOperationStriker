using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One reusable placement-mode mesh per surface, in the grid's local coordinates.</summary>
internal sealed class TrapPlacementOverlay
{
    private readonly TrapPlacementGrid grid;
    private readonly GameObject visual;
    private readonly Mesh mesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int>[] indices = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
    private enum Tint { Open, Occupied, Footprint, Invalid, Hover }
    private int revision;
    private bool built;
    private TrapDefinition definition;
    private RectInt highlight;
    private bool valid;

    public TrapPlacementOverlay(TrapPlacementGrid target, Material[] materials)
    {
        grid = target;
        visual = new GameObject("Trap placement overlay") { hideFlags = HideFlags.DontSave };
        visual.transform.SetParent(grid.transform, false);
        mesh = new Mesh { name = "Trap placement overlay", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public void Refresh(TrapDefinition selected, RectInt footprint, bool canPlace)
    {
        if (!visual.activeSelf) visual.SetActive(true);
        if (built && revision == grid.VisualRevision && selected == definition &&
            footprint.Equals(highlight) && valid == canPlace) return;
        built = true;
        definition = selected;
        highlight = footprint;
        valid = canPlace;
        revision = grid.VisualRevision;
        vertices.Clear();
        foreach (var group in indices) group.Clear();

        if (grid.AllowsOpenCell(selected))
        {
            var runs = grid.OpenRuns;
            for (int i = 0; i < runs.Count; i++)
            {
                RectInt run = runs[i];
                for (int x = run.xMin; x < run.xMax; x++)
                {
                    var cell = new Vector2Int(x, run.y);
                    if (!grid.IsOccupied(cell) && !highlight.Contains(cell)) AddCell(cell, Tint.Open);
                }
            }
        }
        foreach (var cell in grid.OccupiedCells)
            if (!highlight.Contains(cell)) AddCell(cell, Tint.Occupied);
        for (int y = Mathf.Max(0, highlight.yMin); y < Mathf.Min(grid.Rows, highlight.yMax); y++)
        for (int x = Mathf.Max(0, highlight.xMin); x < Mathf.Min(grid.Columns, highlight.xMax); x++)
        {
            var cell = new Vector2Int(x, y);
            Vector2 offset = (Vector2)cell + Vector2.one * 0.5f - highlight.center;
            bool central = Mathf.Abs(offset.x) <= 0.5f && Mathf.Abs(offset.y) <= 0.5f;
            AddCell(cell, !valid ? Tint.Invalid : central ? Tint.Hover : Tint.Footprint);
        }

        mesh.Clear();
        var format = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        if (mesh.indexFormat != format) mesh.indexFormat = format;
        mesh.SetVertices(vertices);
        mesh.subMeshCount = indices.Length;
        for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i, false);
        mesh.RecalculateBounds();
    }

    private void AddCell(Vector2Int cell, Tint tint)
    {
        bool mounted = grid.IsMountedSurface;
        var center = grid.CellToLocal(cell) + (mounted ? Vector3.forward : Vector3.up) * .015f;
        float half = grid.CellSize * .46f;
        var right = Vector3.right * half;
        var up = (mounted ? Vector3.up : Vector3.back) * half;
        int first = vertices.Count;
        vertices.Add(center - right - up);
        vertices.Add(center + right - up);
        vertices.Add(center + right + up);
        vertices.Add(center - right + up);
        var triangles = indices[(int)tint];
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    public void Hide() { if (visual != null) visual.SetActive(false); }

    public void Dispose()
    {
        Hide();
        if (Application.isPlaying) { Object.Destroy(visual); Object.Destroy(mesh); }
        else { Object.DestroyImmediate(visual); Object.DestroyImmediate(mesh); }
    }
}
