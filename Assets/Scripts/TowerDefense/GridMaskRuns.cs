using System.Collections.Generic;
using UnityEngine;

/// <summary>Derived horizontal strips of open cells in a row-major mask.</summary>
internal sealed class GridMaskRuns
{
    private readonly List<RectInt> runs = new List<RectInt>();
    private bool dirty = true;
    public int Revision { get; private set; }

    public void Invalidate()
    {
        dirty = true;
        Revision++;
#if UNITY_EDITOR
        meshDirty = true;
#endif
    }

    public IReadOnlyList<RectInt> Get(bool[] mask, int columns)
    {
        if (!dirty) return runs;
        runs.Clear();
        for (int rowStart = 0; rowStart < mask.Length; rowStart += columns)
        {
            int x = 0;
            while (x < columns)
            {
                if (!mask[rowStart + x]) { x++; continue; }
                int start = x++;
                while (x < columns && mask[rowStart + x]) x++;
                runs.Add(new RectInt(start, rowStart / columns, x - start, 1));
            }
        }
        dirty = false;
        return runs;
    }

#if UNITY_EDITOR
    private static readonly int[] QuadIndices = { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
    // Ordinary MonoBehaviours can draw edit-mode gizmos without receiving OnDisable.
    // Release their native meshes even when the component or the managed domain disappears.
    private static readonly List<GridMaskRuns> meshOwners = new List<GridMaskRuns>();
    private Component owner;
    private Mesh mesh;
    private bool meshDirty = true;

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterMeshCleanup()
    {
        UnityEditor.EditorApplication.hierarchyChanged += () => ReleaseClosedMeshes(default);
        UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += ReleaseClosedMeshes;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseAllMeshes;
        UnityEditor.EditorApplication.quitting += ReleaseAllMeshes;
    }

    private static void ReleaseClosedMeshes(UnityEngine.SceneManagement.Scene closed)
    {
        for (int i = meshOwners.Count - 1; i >= 0; i--)
        {
            var cache = meshOwners[i];
            if (cache.owner == null || cache.owner.gameObject.scene == closed)
                cache.ReleaseMesh();
        }
    }

    private static void ReleaseAllMeshes()
    {
        while (meshOwners.Count > 0) meshOwners[meshOwners.Count - 1].ReleaseMesh();
    }

    // Canonical XY plane, with the first cell centred on (0,0). Both sides
    // are visible when inspecting ceilings or looking through a floor.
    public void Draw(Component source, bool[] mask, int columns, Matrix4x4 matrix, Color color)
    {
        Get(mask, columns);
        if (meshDirty || mesh == null)
        {
            if (mesh == null)
            {
                owner = source;
                meshOwners.Add(this);
                mesh = new Mesh { name = "Grid mask gizmo", hideFlags = HideFlags.HideAndDontSave };
            }
            mesh.Clear();
            var vertices = new Vector3[runs.Count * 4];
            var normals = new Vector3[vertices.Length];
            System.Array.Fill(normals, Vector3.forward);
            var triangles = new int[runs.Count * 12];
            for (int i = 0; i < runs.Count; i++)
            {
                RectInt run = runs[i];
                int v = i * 4, t = i * 12;
                float left = run.xMin - .48f, right = run.xMax - .52f;
                float bottom = run.y - .48f, top = run.y + .48f;
                vertices[v] = new Vector3(left, bottom);
                vertices[v + 1] = new Vector3(right, bottom);
                vertices[v + 2] = new Vector3(right, top);
                vertices[v + 3] = new Vector3(left, top);
                for (int j = 0; j < QuadIndices.Length; j++)
                    triangles[t + j] = v + QuadIndices[j];
            }
            mesh.indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            meshDirty = false;
        }
        var previousMatrix = Gizmos.matrix;
        var previousColor = Gizmos.color;
        Gizmos.matrix = matrix;
        Gizmos.color = color;
        if (runs.Count > 0) Gizmos.DrawMesh(mesh);
        if (UnityEditor.Selection.Contains(source.gameObject))
        {
            int rows = mask.Length / columns;
            Gizmos.color = new Color(.3f, .3f, .3f, .45f);
            for (int x = 0; x <= columns; x++)
                Gizmos.DrawLine(new Vector3(x - .5f, -.5f), new Vector3(x - .5f, rows - .5f));
            for (int y = 0; y <= rows; y++)
                Gizmos.DrawLine(new Vector3(-.5f, y - .5f), new Vector3(columns - .5f, y - .5f));
        }
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    public void ReleaseMesh()
    {
        meshOwners.Remove(this);
        if (mesh != null) Object.DestroyImmediate(mesh);
        mesh = null;
        owner = null;
    }
#endif
}
