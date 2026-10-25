#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CeilingTrapPlacementTests
{
    private readonly List<Object> owned = new List<Object>();
    [UnityTearDown] public IEnumerator LeaveRuntime()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
    [TearDown] public void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }
    private GameObject New(string name) { var go = new GameObject(name); owned.Add(go); return go; }
    private TrapDefinition Definition(TrapMountType type, bool collider = false, int width = 1)
    {
        var d = ScriptableObject.CreateInstance<TrapDefinition>(); owned.Add(d);
        var s = new SerializedObject(d);
        s.FindProperty("mountType").enumValueIndex = (int)type;
        s.FindProperty("footprintWidth").intValue = width;
        s.FindProperty("installBoundsCenter").vector3Value = new Vector3(0f, 0f, .15f);
        s.FindProperty("installBoundsSize").vector3Value = new Vector3(.8f, .8f, .3f);
        s.FindProperty("cost").intValue = 40;
        var prefab = New("test prefab"); prefab.transform.position = new Vector3(1000, 1000, 1000);
        if (collider) { var box = prefab.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0, .15f); box.size = new Vector3(.8f, .8f, .3f); }
        s.FindProperty("prefab").objectReferenceValue = prefab;
        s.ApplyModifiedPropertiesWithoutUndo(); return d;
    }
    private TrapPlacementGrid Ceiling(float height = 4f)
    {
        var support = GameObject.CreatePrimitive(PrimitiveType.Cube); support.name = "ceiling support"; owned.Add(support); support.isStatic = true;
        support.transform.position = new Vector3(2f, height + .1f, 2f);
        var box = support.GetComponent<BoxCollider>(); box.size = new Vector3(4f, .2f, 4f);
        var supportMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.38f, .48f, .62f) };
        owned.Add(supportMaterial); support.GetComponent<Renderer>().sharedMaterial = supportMaterial;
        var grid = New("ceiling grid").AddComponent<TrapPlacementGrid>();
        grid.transform.SetParent(support.transform, false);
        CeilingTrapGridAuthoring.Configure(grid, box, new MapForgeSceneOrganization.CeilingTrapGridMap { id = "bottom" });
        return grid;
    }

    [Test] public void CategoryValuesAndMatchingMatrixRemainStable()
    {
        Assert.That((int)TrapMountType.Ground, Is.EqualTo(0)); Assert.That((int)TrapMountType.Wall, Is.EqualTo(1));
        Assert.That((int)TrapMountType.Ceiling, Is.EqualTo(2));
        foreach (TrapMountType target in Enum.GetValues(typeof(TrapMountType)))
        {
            var d = Definition(target);
            foreach (TrapMountType surface in Enum.GetValues(typeof(TrapMountType)))
                Assert.That(surface.SupportsDefinition(d), Is.EqualTo(surface == target));
            Assert.That(TrapSelectionMenu.MatchesMountFilter(d, (int)target + 1), Is.True);
        }
    }
    [Test] public void CeilingUsesCentredXYPlaneAndDownwardPose()
    {
        var grid = Ceiling(); var d = Definition(TrapMountType.Ceiling, false, 2);
        Assert.That(Vector3.Distance(grid.CellToWorld(Vector2Int.zero), new Vector3(.5f, 3.99f, .5f)), Is.LessThan(.001f));
        Assert.That(grid.TryWorldToCell(grid.CellToWorld(new Vector2Int(2, 3)), out var cell), Is.True);
        Assert.That(cell, Is.EqualTo(new Vector2Int(2, 3)));
        grid.GetPlacementPose(Vector2Int.zero, d, out var p, out var r);
        Assert.That(Vector3.Distance(p, new Vector3(1, 3.99f, .5f)), Is.LessThan(.001f));
        Assert.That(Vector3.Dot(r * Vector3.forward, Vector3.down), Is.GreaterThan(.999f));
    }
    [Test] public void MissingSupportHoleAndOutOfBoundsAreRejected()
    {
        var grid = Ceiling(); var d = Definition(TrapMountType.Ceiling);
        grid.SupportCollider.GetComponent<BoxCollider>().size = new Vector3(1, .2f, 1);
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, d, out var failure), Is.False); StringAssert.Contains("支撑", failure);
        Assert.That(grid.CanPlaceTrap(new Vector2Int(4, 0), d, out _), Is.False);
        Object.DestroyImmediate(grid.SupportCollider);
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, d, out _), Is.False);
    }
    [Test] public void SameCategoryRejectsOverlapAndRemovalReleasesFootprint()
    {
        var a = Ceiling(); var b = Ceiling(); var d = Definition(TrapMountType.Ceiling, true, 2);
        Assert.That(a.TryPlaceTrap(Vector2Int.zero, d, out var instance, out var failure), Is.True, failure);
        owned.Add(instance.gameObject);
        Assert.That(a.CanPlaceTrap(Vector2Int.zero, d, out _), Is.False);
        Assert.That(b.CanPlaceTrap(Vector2Int.zero, d, out _), Is.False);
        Assert.That(a.RemoveTrap(instance), Is.True);
        Assert.That(a.IsOccupied(new Vector2Int(1, 0)), Is.False);
        Assert.That(b.CanPlaceTrap(Vector2Int.zero, d, out failure), Is.True, failure);
    }
    [TestCase(TrapMountType.Ground, false, false)] [TestCase(TrapMountType.Ground, true, false)]
    [TestCase(TrapMountType.Wall, false, false)] [TestCase(TrapMountType.Wall, true, false)]
    [TestCase(TrapMountType.Ground, false, true)] [TestCase(TrapMountType.Ground, true, true)]
    [TestCase(TrapMountType.Wall, false, true)] [TestCase(TrapMountType.Wall, true, true)]
    public void OtherCategoriesHaveIndependentFootprintsAndBodies(TrapMountType otherType, bool collider, bool reverse)
    {
        var ceiling = Ceiling(); var d = Definition(TrapMountType.Ceiling, collider);
        // Use a coincident, independently owned existing instance so both the
        // cross-grid footprint and solid-body filters are exercised in either direction.
        var otherGrid = New("other grid").AddComponent<TrapPlacementGrid>();
        otherGrid.transform.SetPositionAndRotation(ceiling.transform.position, ceiling.transform.rotation);
        otherGrid.ConfigureLayout(4, 4, 1f, 0f, new bool[16], TrapPlacementGrid.SurfaceKind.Ground, otherType);
        var other = Definition(otherType, collider);
        var instanceGo = New("other instance"); instanceGo.transform.position = ceiling.CellToWorld(Vector2Int.zero);
        instanceGo.transform.rotation = ceiling.transform.rotation;
        if (collider) { var box = instanceGo.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0, .15f); box.size = new Vector3(.8f, .8f, .3f); }
        var instance = instanceGo.AddComponent<TrapInstance>();
        var state = new SerializedObject(instance);
        state.FindProperty("definition").objectReferenceValue = other;
        state.FindProperty("originCell").vector2IntValue = Vector2Int.zero;
        state.ApplyModifiedPropertiesWithoutUndo();
        typeof(TrapInstance).GetProperty("OwnerGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, otherGrid);
        Assert.That(ceiling.CanPlaceTrap(Vector2Int.zero, d, out var failure), Is.True, failure);
        if (reverse)
        {
            Assert.That(ceiling.TryPlaceTrap(Vector2Int.zero, d, out var placed, out failure), Is.True, failure); owned.Add(placed.gameObject);
            Assert.That(otherGrid.ConflictsWithSolidEntity(Vector2Int.zero, Vector2Int.one, other, instance, instanceGo, out failure), Is.False, failure);
        }
    }
    [Test] public void SolidPropsAndPlayerAreStillRejected()
    {
        var grid = Ceiling(); var d = Definition(TrapMountType.Ceiling);
        var prop = New("prop"); prop.transform.position = new Vector3(.5f, 3.8f, .5f); prop.AddComponent<BoxCollider>();
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, d, out var failure), Is.False); StringAssert.Contains("实体冲突", failure);
    }
    [Test] public void RaySelectsVisibleFrontAndRespectsOcclusion()
    {
        var grid = Ceiling(); var controller = New("controller").AddComponent<TrapPlacementController>();
        var t = typeof(TrapPlacementController);
        t.GetField("selectedTrap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, Definition(TrapMountType.Ceiling));
        t.GetMethod("RefreshGrids", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        var resolver = t.GetMethod("ResolveGridForRay", BindingFlags.Instance | BindingFlags.NonPublic);
        object[] args = { new Ray(new Vector3(.5f, 1, .5f), Vector3.up), Vector2Int.zero };
        Assert.That(resolver.Invoke(controller, args), Is.EqualTo(grid));
        var blocker = New("blocker"); blocker.transform.position = new Vector3(.5f, 2, .5f); blocker.AddComponent<BoxCollider>(); Physics.SyncTransforms();
        Assert.That(resolver.Invoke(controller, args), Is.Null);
    }
    [Test] public void AutoFitSurvivesScaleYawAndRepeatedConfiguration()
    {
        var grid = Ceiling(); var support = (BoxCollider)grid.SupportCollider;
        support.transform.localScale = new Vector3(2, 1, 1.5f); support.transform.rotation = Quaternion.Euler(0, 37, 0);
        var mapping = new MapForgeSceneOrganization.CeilingTrapGridMap { id = "bottom" };
        CeilingTrapGridAuthoring.Configure(grid, support, mapping); CeilingTrapGridAuthoring.Configure(grid, support, mapping);
        Assert.That(grid.Columns, Is.EqualTo(8)); Assert.That(grid.Rows, Is.EqualTo(6));
        Assert.That(Vector3.Distance(grid.transform.lossyScale, Vector3.one), Is.LessThan(.001f));
        Assert.That(grid.CanPlaceTrap(Vector2Int.zero, Definition(TrapMountType.Ceiling), out var failure), Is.True, failure);
    }
    [Test] public void ImportMappingUpdatesAndDeletesStableIdentity()
    {
        var node = new MapForgeSceneOrganization.Node { id = "ceiling", type = "cube", @static = true, collision = true,
            unity = new MapForgeSceneOrganization.UnityMapping { ceilingTrapGrids = new[] { new MapForgeSceneOrganization.CeilingTrapGridMap { id = "bottom" } } } };
        var owner = New("owner"); owner.isStatic = true; owner.transform.localScale = new Vector3(4, .2f, 4);
        var geometry = New("geometry"); geometry.transform.SetParent(owner.transform, false); geometry.isStatic = true; geometry.AddComponent<BoxCollider>();
        var cache = new Dictionary<string, GameObject>();
        MapForgeSceneOrganization.ApplyUnityMapping(node, owner, geometry, null, cache);
        var marker = owner.GetComponentInChildren<MapForgeCeilingTrapGrid>(); Assert.That(marker, Is.Not.Null);
        var identity = marker;
        MapForgeSceneOrganization.ApplyUnityMapping(node, owner, geometry, null, cache);
        Assert.That(owner.GetComponentsInChildren<MapForgeCeilingTrapGrid>().Length, Is.EqualTo(1));
        Assert.That(owner.GetComponentInChildren<MapForgeCeilingTrapGrid>(), Is.SameAs(identity));
        node.unity.ceilingTrapGrids = null;
        MapForgeSceneOrganization.ApplyUnityMapping(node, owner, geometry, null, cache);
        Assert.That(owner.GetComponentInChildren<MapForgeCeilingTrapGrid>(), Is.Null);
    }

    [UnityTest] public IEnumerator RuntimePurchaseAndSaleUseTheExistingWalletAndReleaseCells()
    {
        yield return new EnterPlayMode();
        Assert.That(Application.isPlaying, Is.True);
        var grid = Ceiling(14f);
        var d = AssetDatabase.LoadAssetAtPath<TrapDefinition>(CeilingTrapTestPieceAuthoring.DefinitionPath);
        Assert.That(d, Is.Not.Null, "Generate the ceiling graybox definition before running placement acceptance.");
        Assert.That(grid.HasSurfaceSupport(Vector2Int.zero, Vector2Int.one, d, out var supportFailure), Is.True, supportFailure);
        Assert.That(grid.IsOpen(Vector2Int.zero), Is.True, "The support sampler should leave a supported cell open.");
        var wallet = EconomyManager.GetOrCreate(); wallet.BeginRun(100);
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, d, out var instance, out var failure), Is.True, failure);
        owned.Add(instance.gameObject); Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(grid.IsOccupied(Vector2Int.zero), Is.True);
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, d, out _, out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            var camera = New("Runtime ceiling camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .08f, .12f);
            camera.transform.position = new Vector3(5, 10, -5); camera.transform.LookAt(new Vector3(2, 14, 2));
            CeilingTrapSystemValidation.Capture(camera, "ceiling-runtime-preview.png");
        }
        Assert.That(TrapSaleService.TrySell(grid, instance, out var refund, out failure), Is.True, failure);
        Assert.That(refund, Is.EqualTo(20)); Assert.That(wallet.Balance, Is.EqualTo(80));
        Assert.That(grid.IsOccupied(Vector2Int.zero), Is.False);
        Cleanup();
        yield return new ExitPlayMode();
    }
}
#endif
