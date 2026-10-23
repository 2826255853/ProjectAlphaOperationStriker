using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Step 3 smoke checks, runnable without optional NUnit test assemblies.</summary>
public static class FlamethrowerPlacementValidation
{
    private static readonly List<Object> owned = new List<Object>();
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run this validation in a batch editor outside Play mode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        checks = 0;
        try
        {
            TrapDefinition definition = ValidateAssetAndMenu();
            Cleanup();
            ValidatePlacement(definition);
            Cleanup();
            ValidateSupportAndBounds(definition);
            Cleanup();
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f, 37f })
            {
                ValidateRotatedWall(definition, Quaternion.Euler(0f, yaw, 0f));
                Cleanup();
            }
            ValidateRotatedWall(definition, Quaternion.Euler(15f, 37f, 10f));
            Cleanup();
            ValidateRegeneration(definition);
            Debug.Log($"FLAMETHROWER_STEP3_PASS: {checks} assertions");
        }
        finally { Cleanup(); }
    }

    private static TrapDefinition ValidateAssetAndMenu()    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(FlamethrowerTrapAuthoring.DefinitionPath);
        Check(definition != null, "definition asset exists");
        Check(definition.TrapId == FlamethrowerTrapAuthoring.TrapId, "stable flamethrower ID");
        Check(definition.DisplayName == "火焰喷射器", "menu display name");
        Check(definition.MountType == TrapMountType.Wall && !definition.WalkableFloorTrap, "wall category only");
        Check(definition.Footprint == new Vector2Int(2, 2), "2x2 footprint");
        Check(definition.Cost >= 0, "valid configurable debug price");
        Check(definition.WorldFootprint == Vector2.zero, "reuse wall grid cell dimensions");
        Check(Quaternion.Angle(definition.LocalRotation, Quaternion.identity) < 0.001f, "no inward model rotation");
        Check(definition.Prefab != null, "wired prefab");
        Check(definition.Prefab.GetComponent<TrapInstance>() != null, "prefab has trap identity");
        Check(definition.Prefab.GetComponent<FlamethrowerTrap>() != null, "existing runtime component retained");
        Check(definition.Prefab.transform.localScale == Vector3.one, "unit prefab scale");
        Check(Quaternion.Angle(definition.Prefab.transform.rotation, Quaternion.identity) < 0.001f, "root +Z faces out");
        Check(definition.HasExplicitInstallBounds, "solid install bounds are explicit");
        definition.ResolveInstallBounds(definition.Footprint, Vector2.one, out Vector3 center, out Vector3 extents);
        // The install box is measured from the authored model when the prefab is
        // generated, so validate it against that model instead of the retired
        // graybox constants.
        Bounds authoredModel = MeasureModelBounds(definition.Prefab.transform);
        Check(authoredModel.size.x <= extents.x * 2f + 0.02f && authoredModel.size.y <= extents.y * 2f + 0.02f
            && authoredModel.max.z <= center.z + extents.z + 0.02f
            && authoredModel.min.z >= center.z - extents.z - 0.02f, "install bounds cover the authored model");
        // Well below the four-metre spray volume; 0.9 m leaves room for the
        // modelled housing and barrels to stand off the wall.
        Check(extents.x <= 1f && extents.y <= 1f && center.z - extents.z >= -0.0001f
            && center.z + extents.z < 0.9f, "body fits 2x2 wall area and excludes four-metre spray");
        var body = new Bounds(center, extents * 2f + Vector3.one * 0.001f);
        foreach (MeshFilter mesh in definition.Prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds meshBounds = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                corner = definition.Prefab.transform.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                Check(body.Contains(corner), mesh.name + " remains within solid installation bounds");
            }
        }
        int matches = 0;
        foreach (TrapDefinition resource in Resources.LoadAll<TrapDefinition>(""))
            if (resource.TrapId == definition.TrapId) matches++;
        Check(matches == 1, "Resources contains exactly one flamethrower definition");
        TrapSelectionMenu menu = NewObject("Menu loading probe").AddComponent<TrapSelectionMenu>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TrapSelectionMenu).GetMethod("Refresh", flags).Invoke(menu, null);
        var available = (List<TrapDefinition>)typeof(TrapSelectionMenu).GetField("available", flags).GetValue(menu);
        Check(available.Contains(definition), "actual menu Refresh loads the definition");
        return definition;
    }

    private static void ValidatePlacement(TrapDefinition definition)
    {
        TrapPlacementGrid grid = CreateWall(Quaternion.identity);
        var origin = new Vector2Int(1, 1);
        Check(grid.TryPlaceTrap(origin, definition, out TrapInstance trap, out string failure), "place 2x2: " + failure);
        Check(Almost(trap.transform.position, grid.transform.TransformPoint(new Vector3(2f, 2f, grid.SurfaceOffset))),
            "placement centered over four local X/Y cells");
        BoxCollider collider = trap.GetComponent<BoxCollider>();
        Check(collider != null && Almost(collider.center, definition.InstallBoundsCenter)
            && Almost(collider.size, definition.InstallBoundsSize), "generated collider uses solid definition bounds");
        TrapDefinition single = Own(ScriptableObject.CreateInstance<TrapDefinition>());
        var settings = new SerializedObject(single);
        settings.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Wall;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            var cell = new Vector2Int(x, y);
            bool covered = x >= 1 && x <= 2 && y >= 1 && y <= 2;
            Check(grid.IsOccupied(cell) == covered, "exact occupancy at " + cell);
            if (!covered) continue;
            Check(grid.TryGetTrapAtCell(cell, out TrapInstance owner) && owner == trap, "same owner for " + cell);
            Check(!grid.CanPlaceTrap(cell, single, out failure) && failure.Contains("occupied"), "occupied cell blocks another wall trap: " + cell);
        }
        grid.RebuildOccupancy();
        Check(grid.OwnsTrap(trap) && grid.IsOccupied(new Vector2Int(2, 2)), "rebuild preserves 2x2 ownership");
        Check(grid.RemoveTrap(trap), "remove flamethrower");
        for (int y = 1; y <= 2; y++)
        for (int x = 1; x <= 2; x++)
        {
            var cell = new Vector2Int(x, y);
            Check(!grid.IsOccupied(cell) && grid.CanPlaceTrap(cell, single, out failure), "release cell " + cell + ": " + failure);
        }
        Check(grid.CanPlaceTrap(origin, definition, out failure), "replace after removal: " + failure);
    }

    private static void ValidateSupportAndBounds(TrapDefinition definition)
    {
        TrapPlacementGrid grid = CreateWall(Quaternion.identity);
        Check(!grid.CanPlaceTrap(new Vector2Int(3, 0), definition, out _), "reject right edge overflow");
        Check(!grid.CanPlaceTrap(new Vector2Int(0, 3), definition, out _), "reject top edge overflow");
        grid.SetOpen(new Vector2Int(1, 1), false);
        Check(!grid.CanPlaceTrap(Vector2Int.zero, definition, out _), "reject one closed cell among four");
        grid.SetOpen(new Vector2Int(1, 1), true);
        BoxCollider support = (BoxCollider)grid.SupportCollider;
        support.center = new Vector3(0.5f, 2f, -0.5f);
        support.size = new Vector3(1f, 4f, 1f);
        Check(!grid.CanPlaceTrap(Vector2Int.zero, definition, out string failure) && failure.Contains("支撑"),
            "reject footprint with unsupported second column");
        support.center = new Vector3(2f, 2f, -0.5f);
        support.size = new Vector3(4f, 4f, 1f);
        grid.GetPlacementPose(Vector2Int.zero, definition, out Vector3 anchor, out Quaternion rotation);
        GameObject obstacle = NewObject("Spray-only obstacle");
        obstacle.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
        obstacle.transform.position = anchor + rotation * new Vector3(0f, 0f, 3f);
        Check(grid.CanPlaceTrap(Vector2Int.zero, definition, out failure), "spray-only obstacle does not block mounting: " + failure);
        obstacle.transform.position = anchor + rotation * definition.InstallBoundsCenter;
        Check(!grid.CanPlaceTrap(Vector2Int.zero, definition, out failure) && failure.Contains("实体冲突"), "solid body obstacle blocks mounting");
        obstacle.SetActive(false);
        TrapPlacementGrid ground = NewObject("Ground grid").AddComponent<TrapPlacementGrid>();
        ground.ConfigureLayout(4, 4, 1f, 0f, AllOpen(), TrapPlacementGrid.SurfaceKind.Ground);
        Check(!ground.CanPlaceTrap(Vector2Int.zero, definition, out _), "wall flamethrower rejected on ground");
    }

    private static void ValidateRotatedWall(TrapDefinition definition, Quaternion rotation)
    {
        TrapPlacementGrid grid = CreateWall(rotation);
        Check(grid.TryPlaceTrap(Vector2Int.zero, definition, out TrapInstance trap, out string failure), "rotated wall placement: " + failure);
        Check(Vector3.Dot(trap.transform.forward, grid.SurfaceNormal) > 0.9999f, "root +Z points out of rotated wall");
        Check(Vector3.Dot(trap.transform.up, grid.transform.up) > 0.9999f, "local Y follows rotated wall");
        Check(Almost(trap.transform.position, grid.transform.TransformPoint(new Vector3(1f, 1f, grid.SurfaceOffset))),
            "rotated wall retains shared 2x2 anchor");
        // The modelled prefab has no graybox nozzle; the flame mount marks the
        // muzzle plane and must stay outside the rotated wall.
        Transform nozzle = trap.transform.Find("Flame Origin");
        Check(nozzle != null && Vector3.Dot(nozzle.position - trap.transform.position, grid.SurfaceNormal) > 0f,
            "flame mount is outside rotated wall");
        Check(nozzle != null && Vector3.Dot(nozzle.forward, grid.SurfaceNormal) > 0.9999f, "flame mount forward and root +Z agree");
    }

    private static void ValidateRegeneration(TrapDefinition definition)
    {
        string guid = AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.DefinitionPath);
        GameObject prefab = definition.Prefab;
        int price = definition.Cost;
        FlamethrowerTrapAuthoring.CreateAssets();
        Check(AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.DefinitionPath) == guid, "regeneration preserves definition GUID");
        Check(definition.Prefab == prefab && definition.Cost == price, "regeneration preserves prefab and price");
        TrapDefinition copy = Own(Object.Instantiate(definition));
        var settings = new SerializedObject(copy);
        settings.FindProperty("cost").intValue = 73;
        settings.ApplyModifiedPropertiesWithoutUndo();
        FlamethrowerTrapAuthoring.ConfigureDefinition(copy, prefab);
        Check(copy.Cost == 73, "repair does not reset designer price");
    }

    private static TrapPlacementGrid CreateWall(Quaternion rotation)
    {
        GameObject root = NewObject("Flamethrower validation wall");
        root.transform.SetPositionAndRotation(new Vector3(20f, 10f, 20f), rotation);
        BoxCollider support = root.AddComponent<BoxCollider>();
        support.center = new Vector3(2f, 2f, -0.5f);
        support.size = new Vector3(4f, 4f, 1f);
        TrapPlacementGrid grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(4, 4, 1f, 0f, AllOpen(), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, support, 0.01f);
        return grid;
    }

    private static bool[] AllOpen()
    {
        var cells = new bool[16];
        for (int i = 0; i < cells.Length; i++) cells[i] = true;
        return cells;
    }

    private static GameObject NewObject(string name) => Own(new GameObject(name));
    private static T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    /// <summary>Renderer bounds of the authored model, expressed in prefab root space.</summary>
    private static Bounds MeasureModelBounds(Transform root)
    {
        bool has = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Bounds world = renderer.bounds;
            Vector3 min = root.InverseTransformPoint(world.min);
            Vector3 max = root.InverseTransformPoint(world.max);
            var local = new Bounds((min + max) * 0.5f, max - min);
            if (local.size.x < 0f) local.size = new Vector3(Mathf.Abs(local.size.x), local.size.y, local.size.z);
            if (!has) { bounds = local; has = true; }
            else bounds.Encapsulate(local);
        }
        return bounds;
    }

    private static bool Almost(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Flamethrower step 3: " + message);
        checks++;
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }
}
