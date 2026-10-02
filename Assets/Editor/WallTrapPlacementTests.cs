#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regression coverage for the first wall-trap placement slice. These tests keep the
/// wall-specific invariants executable without requiring a scene or a generated prefab.
/// </summary>
public sealed class WallTrapPlacementTests
{
    private readonly List<Object> owned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    [Test]
    public void WallGridUsesLocalXYAndOutwardPose()
    {
        TrapPlacementGrid grid = CreateWallGrid("Wall grid", 4, 3);
        TrapDefinition definition = CreateDefinition("wall_1x1", TrapMountType.Wall, 1, 1);

        Assert.That(grid.CellToWorld(new Vector2Int(2, 1)), Is.EqualTo(new Vector3(2f, 1f, 0.01f)));
        grid.GetPlacementPose(new Vector2Int(1, 1), definition, out Vector3 position, out Quaternion rotation);
        Assert.That(position, Is.EqualTo(new Vector3(1.5f, 1.5f, 0.01f)));
        Assert.That(Vector3.Dot(rotation * Vector3.forward, grid.SurfaceNormal), Is.GreaterThan(0.999f));
        Assert.That(Vector3.Dot(rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));
    }

    [Test]
    public void WallGridRejectsGroundAndReleasesMultiCellOccupancy()
    {
        TrapPlacementGrid grid = CreateWallGrid("Wall grid", 4, 4);
        TrapDefinition ground = CreateDefinition("ground", TrapMountType.Ground, 1, 1);
        TrapDefinition wall = CreateDefinition("wall_2x2", TrapMountType.Wall, 2, 2);

        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, ground, out string mismatch), Is.False);
        StringAssert.Contains("地面", mismatch);
        Assert.That(grid.TryPlaceTrap(Vector2Int.zero, wall, out TrapInstance instance, out string failure), Is.True, failure);
        Assert.That(grid.IsOccupied(new Vector2Int(1, 1)), Is.True);
        Assert.That(grid.RemoveTrap(instance), Is.True);
        Assert.That(grid.IsOccupied(Vector2Int.zero), Is.False);
        Assert.That(grid.IsOccupied(new Vector2Int(1, 1)), Is.False);
    }

    [Test]
    public void WallGridRebuildRestoresPoseAndFootprint()
    {
        TrapPlacementGrid grid = CreateWallGrid("Wall grid", 4, 3);
        TrapDefinition definition = CreateDefinition("wall_2x1", TrapMountType.Wall, 2, 1);
        Assert.That(grid.TryPlaceTrap(new Vector2Int(1, 1), definition, out TrapInstance instance, out string failure), Is.True, failure);
        grid.GetPlacementPose(new Vector2Int(1, 1), definition, out Vector3 expectedPosition, out Quaternion expectedRotation);

        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        grid.RebuildOccupancy();

        Assert.That(instance.transform.position, Is.EqualTo(expectedPosition));
        Assert.That(Quaternion.Angle(instance.transform.rotation, expectedRotation), Is.LessThan(0.01f));
        Assert.That(grid.OwnsTrap(instance), Is.True);
        Assert.That(grid.IsOccupied(new Vector2Int(2, 1)), Is.True);
    }

    [Test]
    public void WallGridRejectsUnsupportedHole()
    {
        GameObject root = NewObject("Partial wall");
        BoxCollider support = root.AddComponent<BoxCollider>();
        support.center = new Vector3(0.5f, 1.5f, -0.5f);
        support.size = new Vector3(1f, 3f, 1f);
        TrapPlacementGrid grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(4, 3, 1f, 0f, AllOpen(12), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, support, 0.01f);
        TrapDefinition definition = CreateDefinition("wall", TrapMountType.Wall, 1, 1);

        Assert.That(grid.CanPlaceTrap(new Vector2Int(3, 0), definition, out string failure), Is.False);
        StringAssert.Contains("支撑", failure);
    }

    [Test]
    public void MapForgeSyncValidationCountsAndReportsBrokenWallGrid()
    {
        GameObject root = NewObject("MapForgeWorld");
        GameObject validObject = NewObject("Valid wall grid", root.transform);
        BoxCollider support = validObject.AddComponent<BoxCollider>();
        support.center = new Vector3(1f, 1f, -0.5f);
        support.size = new Vector3(2f, 2f, 1f);
        TrapPlacementGrid valid = validObject.AddComponent<TrapPlacementGrid>();
        valid.ConfigureLayout(2, 2, 1f, 0f, AllOpen(4), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, support, 0.01f);

        GameObject brokenObject = NewObject("Broken wall grid", root.transform);
        TrapPlacementGrid broken = brokenObject.AddComponent<TrapPlacementGrid>();
        broken.ConfigureLayout(2, 2, 1f, 0f, AllOpen(4), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, null, 0.01f);

        var issues = new List<string>();
        int count = MapForgeLiveSync.ValidateWallTrapGrids(root.transform, issues);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(issues.Count, Is.EqualTo(1));
        StringAssert.Contains("Broken wall grid", issues[0]);
    }

    [Test]
    public void MapForgeRebuildKeepsAuthoredWallGrid()
    {
        GameObject root = NewObject("MapForgeWorld");
        MonsterPathGrid pathGrid = root.AddComponent<MonsterPathGrid>();
        pathGrid.EnsureCellData();

        GameObject wallObject = NewObject("Authored wall grid", root.transform);
        BoxCollider support = wallObject.AddComponent<BoxCollider>();
        support.center = new Vector3(1f, 1f, -0.5f);
        support.size = new Vector3(2f, 2f, 1f);
        TrapPlacementGrid wall = wallObject.AddComponent<TrapPlacementGrid>();
        wall.ConfigureLayout(2, 2, 1f, 0f, AllOpen(4), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, support, 0.01f);

        int rebuiltCount = MapForgeWorldImporter.RebuildTrapPlacementGrids(root.transform);

        Assert.That(rebuiltCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(wallObject != null, Is.True);
        Assert.That(root.GetComponentInChildren<TrapPlacementGrid>(true), Is.Not.Null);
        Assert.That(wallObject.GetComponent<TrapPlacementGrid>(), Is.SameAs(wall));
    }

    [Test]
    public void MapForgeMappingCreatesAndUpdatesStableWallGridRegions()
    {
        GameObject owner = NewObject("Wall");
        owner.AddComponent<MapForgeObjectProperties>();
        GameObject geometry = NewObject("wall_geometry", owner.transform);
        geometry.AddComponent<BoxCollider>();
        var node = new MapForgeSceneOrganization.Node
        {
            id = "wall_01",
            type = "cube",
            unity = new MapForgeSceneOrganization.UnityMapping
            {
                wallTrapGrids = new[]
                {
                    new MapForgeSceneOrganization.WallTrapGridMap
                    {
                        id = "north",
                        origin = new[] { 1f, 2f, 0f },
                        rotation = new[] { 0f, 1.5707964f, 0f },
                        columns = 2,
                        rows = 2,
                        cellSize = 1f,
                        surfaceOffset = 0.02f,
                        openCells = AllOpen(4),
                    },
                },
            },
        };

        MapForgeSceneOrganization.ApplyUnityMapping(node, owner, geometry, null,
            new Dictionary<string, GameObject>());
        var first = owner.GetComponentInChildren<MapForgeWallTrapGrid>(true);
        Assert.That(first, Is.Not.Null);
        Assert.That(first.gridId, Is.EqualTo("north"));
        Assert.That(first.GetComponent<TrapPlacementGrid>().SurfaceType, Is.EqualTo(TrapMountType.Wall));
        Assert.That(first.transform.localPosition, Is.EqualTo(new Vector3(1f, 2f, 0f)));

        node.unity.wallTrapGrids = new[]
        {
            new MapForgeSceneOrganization.WallTrapGridMap
            {
                id = "south",
                origin = new[] { 0f, 0f, 0f },
                rotation = new[] { 0f, 0f, 0f },
                columns = 1,
                rows = 1,
                cellSize = 1f,
                surfaceOffset = 0.01f,
                openCells = AllOpen(1),
            },
        };
        MapForgeSceneOrganization.ApplyUnityMapping(node, owner, geometry, null,
            new Dictionary<string, GameObject>());
        Assert.That(owner.GetComponentsInChildren<MapForgeWallTrapGrid>(true), Has.Length.EqualTo(1));
        Assert.That(owner.GetComponentInChildren<MapForgeWallTrapGrid>(true).gridId, Is.EqualTo("south"));
    }

    private GameObject NewObject(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        owned.Add(go);
        return go;
    }

    private TrapPlacementGrid CreateWallGrid(string name, int columns, int rows)
    {
        GameObject root = NewObject(name);
        BoxCollider support = root.AddComponent<BoxCollider>();
        support.center = new Vector3(columns * 0.5f, rows * 0.5f, -0.5f);
        support.size = new Vector3(columns, rows, 1f);
        TrapPlacementGrid grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(columns, rows, 1f, 0f, AllOpen(columns * rows),
            TrapPlacementGrid.SurfaceKind.Ground, TrapMountType.Wall, support, 0.01f);
        return grid;
    }

    private TrapDefinition CreateDefinition(string id, TrapMountType mountType, int width, int height)
    {
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        owned.Add(definition);
        SerializedObject serialized = new SerializedObject(definition);
        serialized.FindProperty("trapId").stringValue = id;
        serialized.FindProperty("displayName").stringValue = id;
        serialized.FindProperty("footprintWidth").intValue = width;
        serialized.FindProperty("footprintHeight").intValue = height;
        serialized.FindProperty("mountType").enumValueIndex = (int)mountType;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static bool[] AllOpen(int count)
    {
        var cells = new bool[count];
        for (int i = 0; i < cells.Length; i++) cells[i] = true;
        return cells;
    }
}
#endif
