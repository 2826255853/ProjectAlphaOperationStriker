using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class TrapCrosshairGridTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly Vector3 centre = new Vector3(2000f, 0f, 2000f);
    private GameObject root;
    private TrapPlacementController controller;
    private TrapDefinition trap;
    private TrapPlacementGrid lower;
    private TrapPlacementGrid upper;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Crosshair test");
        var host = new GameObject("Placement controller");
        host.transform.SetParent(root.transform);
        host.SetActive(false);
        controller = host.AddComponent<TrapPlacementController>();
        trap = ScriptableObject.CreateInstance<TrapDefinition>();
        var definition = new SerializedObject(trap);
        definition.FindProperty("footprintWidth").intValue = 2;
        definition.FindProperty("footprintHeight").intValue = 1;
        definition.ApplyModifiedPropertiesWithoutUndo();
        typeof(TrapPlacementController).GetField("selectedTrap", Private).SetValue(controller, trap);
        lower = MakeGrid(0f);
        upper = MakeGrid(2f);
    }

    private TrapPlacementGrid MakeGrid(float height)
    {
        var go = new GameObject("Grid");
        go.transform.SetParent(root.transform);
        go.transform.position = centre + Vector3.up * height;
        var grid = go.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(2, 1, 1f, 0f, new[] { true, true });
        var grids = (List<TrapPlacementGrid>)typeof(TrapPlacementController).GetField("grids", Private).GetValue(controller);
        grids.Add(grid);
        return grid;
    }

    private TrapPlacementGrid Aim(float x = 0f)
    {
        var ray = new Ray(centre + new Vector3(x, 5f, 0f), Vector3.down);
        object[] args = { ray, Vector2Int.zero };
        return (TrapPlacementGrid)typeof(TrapPlacementController).GetMethod("ResolveGridForRay", Private).Invoke(controller, args);
    }

    [Test]
    public void NearestGridWinsUntilItsLastOpenCellCloses()
    {
        Assert.That(Aim(0.5f), Is.SameAs(upper));
        upper.SetOpen(Vector2Int.right, false);
        Assert.That(Aim(0.5f), Is.SameAs(upper));
        upper.SetOpen(Vector2Int.zero, false);
        Assert.That(Aim(0.5f), Is.SameAs(lower));
        lower.SetOpen(Vector2Int.right, false);
        lower.SetOpen(Vector2Int.zero, false);
        Assert.That(Aim(0.5f), Is.Null);
    }

    [Test]
    public void OverhangPreviewsUntilTheLastCellLeavesTheGrid()
    {
        Assert.That(Aim(-0.51f), Is.SameAs(upper));
        Assert.That(Aim(1.51f), Is.SameAs(upper));
        Assert.That(Aim(-1.01f), Is.Null);
        Assert.That(Aim(2.01f), Is.Null);
    }

    [TestCase(TrapMountType.Ground, 3, 3)]
    [TestCase(TrapMountType.Ground, 2, 2)]
    [TestCase(TrapMountType.Wall, 3, 2)]
    [TestCase(TrapMountType.Ceiling, 4, 3)]
    public void CrosshairSnapsToCenterOfFootprintOnEachSurface(TrapMountType surface, int width, int height)
    {
        var definition = new SerializedObject(trap);
        definition.FindProperty("mountType").enumValueIndex = (int)surface;
        definition.FindProperty("footprintWidth").intValue = width;
        definition.FindProperty("footprintHeight").intValue = height;
        definition.ApplyModifiedPropertiesWithoutUndo();
        upper.ConfigureLayout(1, 1, 1f, 0f, new[] { true }, TrapPlacementGrid.SurfaceKind.Ground, surface);
        lower.gameObject.SetActive(false);
        upper.transform.rotation = surface == TrapMountType.Ceiling ? Quaternion.Euler(90f, 20f, 0f) : Quaternion.Euler(0f, 35f, 0f);
        var size = new Vector2Int(width, height);
        var expectedOrigin = Vector2Int.one - size; // Only the far corner (0, 0) is open.
        Vector3 normal = upper.IsMountedSurface ? upper.SurfaceNormal : upper.transform.up;
        Vector3 midpoint = (upper.CellToWorld(expectedOrigin) + upper.CellToWorld(Vector2Int.zero)) * 0.5f;
        var ray = new Ray(midpoint + normal * 2f, -normal);
        object[] args = { ray, default(Vector2Int) };
        Assert.That(typeof(TrapPlacementController).GetMethod("ResolveGridForRay", Private).Invoke(controller, args), Is.SameAs(upper));
        Assert.That(args[1], Is.EqualTo(expectedOrigin));
        Assert.That(Vector3.Distance(upper.GetTrapWorldPosition(expectedOrigin, size), midpoint), Is.LessThan(0.001f));
        Assert.That(upper.CanPlaceTrap(expectedOrigin, trap, out _), Is.False);
    }

    [Test]
    public void BlockerHidesBothGrids()
    {
        var blocker = new GameObject("Solid obstruction");
        blocker.transform.SetParent(root.transform);
        blocker.transform.position = centre + Vector3.up * 3f;
        blocker.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        Assert.That(Aim(), Is.Null);
    }

    [Test]
    public void HoverMovesToTheActiveCamerasCrosshairAndHidesOnMiss()
    {
        var ghost = new GameObject("Trap Preview");
        ghost.transform.SetParent(root.transform);
        var type = typeof(TrapPlacementController);
        type.GetField("preview", Private).SetValue(controller, ghost);
        type.GetField("placementMode", Private).SetValue(controller, true);
        var updateHover = type.GetMethod("UpdateHover", Private);
        foreach (float height in new[] { 1f, 5f })
        {
            var view = new GameObject("Control view");
            view.transform.SetParent(root.transform);
            view.transform.SetPositionAndRotation(centre + new Vector3(0.5f, height, 0f), Quaternion.Euler(90f, 0f, 0f));
            type.GetField("placementCamera", Private).SetValue(controller, view.AddComponent<Camera>());
            updateHover.Invoke(controller, null);
            var target = height < 2f ? lower : upper;
            Assert.That(ghost.activeSelf, Is.True);
            Assert.That(controller.ActiveGrid, Is.SameAs(target));
            Assert.That(ghost.transform.position, Is.EqualTo(target.GetTrapWorldPosition(Vector2Int.zero, new Vector2Int(2, 1))));
            view.transform.rotation = Quaternion.identity;
            updateHover.Invoke(controller, null);
            Assert.That(ghost.activeSelf, Is.False);
            Assert.That(controller.OnProjectionGunPrimaryAction(), Is.False);
        }
    }

    [TearDown]
    public void TearDown()
    {
        typeof(TrapPlacementController).GetMethod("ReleaseGridOverlays", Private).Invoke(controller, null);
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(trap);
    }
}
