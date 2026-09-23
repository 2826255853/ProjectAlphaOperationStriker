using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Deterministic batch smoke checks for the wall trap grid coordinates, pose,
/// corners and occupancy rebuild (implementation unit 2). Mirrors the style of
/// GroundSpikeTrapValidation so it can run without the optional test assemblies.
/// Run with: -executeMethod WallTrapGridValidation.Run
/// </summary>
public static class WallTrapGridValidation
{
    private static readonly List<Object> owned = new List<Object>();
    private static int checks;

    private const float Tolerance = 0.0001f;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run validation in a batch editor, outside Play mode.");
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        checks = 0;
        try
        {
            ValidateWallCoordinates();
            Cleanup();
            ValidateWallInstancePose();
            Cleanup();
            ValidateRotatedWallPose();
            Cleanup();
            ValidateReloadKeepsWallPose();
            Cleanup();
            ValidateGroundUnchanged();
            Cleanup();
            ValidateCategoryInterception();
            Cleanup();
            ValidateWallSupportAndClearance();
            Cleanup();
            Debug.Log($"WALL_TRAP_GRID_VALIDATION_PASS: {checks} assertions");
        }
        finally { Cleanup(); }
    }

    private static void ValidateWallCoordinates()
    {
        TrapPlacementGrid wall = CreateGrid("Wall grid", new Vector3(10f, 0f, 5f), Quaternion.identity,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.01f);

        Check(wall.IsWallSurface && wall.SurfaceType == TrapMountType.Wall, "wall grid reports itself as a wall surface");
        Check(Almost(wall.SurfaceNormal, Vector3.forward), "wall surface normal is the grid's +Z");
        Check(Almost(wall.CellWorldSize, new Vector2(1f, 1f)), "wall cells measure 1x1m on the local X/Y plane");

        // Wall cells sit on the local X/Y plane; the rectangle origin is lower-left.
        Check(Almost(wall.CellToWorld(Vector2Int.zero), new Vector3(10f, 0f, 5.01f)),
            "wall cell (0,0) sits at the authored lower-left corner, pushed out by surfaceOffset");
        Check(Almost(wall.CellToWorld(new Vector2Int(2, 1)), new Vector3(12f, 1f, 5.01f)),
            "wall cell (2,1) advances along local +X and +Y, never along +Z");
        Check(Almost(wall.SurfaceOffsetWorld, new Vector3(0f, 0f, 0.01f)),
            "surfaceOffsetWorld pushes along the wall normal only");

        Check(wall.TryWorldToCell(new Vector3(12.5f, 1.5f, 5.01f), out Vector2Int roundTrip) && roundTrip == new Vector2Int(2, 1),
            "wall world->cell conversion uses the local Y axis for the second index");
        Check(wall.TryWorldToCell(new Vector3(12.5f, 1.5f, 40f), out Vector2Int projected) && projected == new Vector2Int(2, 1),
            "wall world->cell ignores depth (the third axis) and projects on the X/Y plane");

        var corners = new Vector3[4];
        wall.GetCellCorners(new Vector2Int(1, 0), corners);
        Check(Almost(corners[0], new Vector3(11f, 0f, 5.01f)) && Almost(corners[1], new Vector3(12f, 0f, 5.01f))
            && Almost(corners[2], new Vector3(12f, 1f, 5.01f)) && Almost(corners[3], new Vector3(11f, 1f, 5.01f)),
            "wall cell corners are ordered lower-left, lower-right, upper-right, upper-left");

        Check(wall.TryRaycastPlane(new Ray(new Vector3(11.5f, 1.5f, -10f), Vector3.forward), out Vector3 hit, out float distance)
            && Almost(hit.z, 5.01f) && distance > 0f,
            "wall plane raycast intersects the backplate plane offset outward by surfaceOffset");
    }

    private static void ValidateWallInstancePose()
    {
        TrapPlacementGrid wall = CreateGrid("Wall grid", new Vector3(10f, 0f, 5f), Quaternion.identity,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.01f);
        TrapDefinition small = CreateDefinition("wall_small", TrapMountType.Wall, 1, 1);
        TrapDefinition large = CreateDefinition("wall_large", TrapMountType.Wall, 2, 2);

        wall.GetPlacementPose(new Vector2Int(1, 1), small, out Vector3 smallPosition, out Quaternion smallRotation);
        Check(Almost(smallPosition, new Vector3(11.5f, 1.5f, 5.01f)),
            "1x1 wall trap hangs at the center of its single cell, off the wall");
        Check(Almost(smallRotation * Vector3.forward, wall.SurfaceNormal),
            "wall trap faces away from the wall (+Z aligned with the surface normal)");
        Check(Almost(smallRotation * Vector3.up, Vector3.up), "wall trap keeps world up for its model");

        wall.GetPlacementPose(Vector2Int.zero, large, out Vector3 largePosition, out _);
        Check(Almost(largePosition, new Vector3(11f, 1f, 5.01f)),
            "2x2 wall trap hangs at the center of the area it covers");

        Check(wall.TryPlaceTrap(new Vector2Int(1, 1), small, out TrapInstance instance, out string failure),
            "wall grid accepts a wall trap: " + failure);
        Check(Almost(instance.transform.position, smallPosition), "spawned wall trap uses the shared placement pose");
        Check(Quaternion.Angle(instance.transform.rotation, smallRotation) < 0.01f,
            "spawned wall trap keeps the shared placement rotation");
        Check(Mathf.Abs(instance.transform.localScale.z - 0.15f) < 0.001f && instance.transform.localScale.y > 0.5f,
            "prefab-less wall stand-in is a flat backplate, not a ground slab");

        Check(!wall.TryPlaceTrap(new Vector2Int(1, 1), large, out _, out _),
            "wall footprint occupancy is honoured after the first placement");
        Check(wall.IsOccupied(new Vector2Int(1, 1)) && !wall.IsOccupied(Vector2Int.zero),
            "a 1x1 wall trap only reserves its own single cell");
    }

    private static void ValidateRotatedWallPose()
    {
        Quaternion rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0f), Vector3.up);
        TrapPlacementGrid wall = CreateGrid("Rotated wall grid", new Vector3(4f, 2f, -3f), rotation,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.02f);
        TrapDefinition definition = CreateDefinition("wall_rotated", TrapMountType.Wall, 1, 1);

        Check(Almost(wall.SurfaceNormal, new Vector3(-1f, 0f, 0f)), "rotated wall normal follows the grid's +Z");

        wall.GetPlacementPose(Vector2Int.zero, definition, out Vector3 position, out Quaternion poseRotation);
        Vector3 expected = wall.transform.TransformPoint(new Vector3(0.5f, 0.5f, 0.02f));
        Check(Almost(position, expected), "rotated wall pose is expressed in the grid's own frame");
        Check(Vector3.Dot(position - wall.transform.position, wall.SurfaceNormal) > 0f,
            "rotated wall pose is pushed out along the wall normal, not along world +Z");
        Check(Almost(poseRotation * Vector3.forward, wall.SurfaceNormal) && Almost(poseRotation * Vector3.up, Vector3.up),
            "rotated wall trap faces off the wall while staying upright");

        Check(wall.TryWorldToCell(wall.CellToWorld(new Vector2Int(3, 2)) + wall.SurfaceNormal * 0.005f, out Vector2Int cell)
            && cell == new Vector2Int(3, 2), "rotated wall world->cell conversion round-trips");
    }

    private static void ValidateReloadKeepsWallPose()
    {
        TrapPlacementGrid wall = CreateGrid("Wall grid", new Vector3(10f, 0f, 5f), Quaternion.identity,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.01f);
        TrapDefinition definition = CreateDefinition("wall_reload", TrapMountType.Wall, 2, 1);
        Check(wall.TryPlaceTrap(new Vector2Int(0, 1), definition, out TrapInstance instance, out string failure),
            "wall trap placed for the reload check: " + failure);
        wall.GetPlacementPose(new Vector2Int(0, 1), definition, out Vector3 expectedPosition, out Quaternion expectedRotation);

        // Simulate a stale saved transform (old cell-center authoring), then let
        // the grid rebuild its occupancy exactly like a scene reopen would.
        instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0f), Quaternion.identity);
        wall.RebuildOccupancy();

        Check(Almost(instance.transform.position, expectedPosition),
            "occupancy rebuild puts a wall trap back on the wall, not on a ground anchor");
        Check(Quaternion.Angle(instance.transform.rotation, expectedRotation) < 0.01f,
            "occupancy rebuild restores the wall rotation");
        Check(wall.IsOccupied(new Vector2Int(0, 1)) && wall.IsOccupied(new Vector2Int(1, 1)),
            "occupancy rebuild re-reserves the whole 2x1 footprint");
        Check(!wall.IsOccupied(new Vector2Int(0, 0)) && !wall.IsOccupied(new Vector2Int(2, 1)),
            "occupancy rebuild does not leak cells outside the footprint");
        Check(wall.OwnsTrap(instance), "rebuilt wall trap stays owned by its grid");
    }

    private static void ValidateGroundUnchanged()
    {
        TrapPlacementGrid ground = CreateGridLegacy("Ground grid", new Vector3(0f, 0f, 0f), 6, 4, 1f, 0.02f);
        Check(ground.SurfaceType == TrapMountType.Ground && !ground.IsWallSurface,
            "the legacy ConfigureLayout overload still yields a ground surface");
        Check(Almost(ground.CellToWorld(new Vector2Int(2, 1)), new Vector3(2f, 0.02f, 1f)),
            "ground CellToWorld still uses the local X/Z plane and placementHeight");
        Check(Almost(ground.CellWorldSize, new Vector2(1f, 1f)), "ground cells still measure 1x1m");

        TrapDefinition definition = CreateDefinition("ground_turret", TrapMountType.Ground, 2, 2);
        ground.GetPlacementPose(Vector2Int.zero, definition, out Vector3 position, out Quaternion rotation);
        Check(Almost(position, new Vector3(0.5f, 0.02f, 0.5f)),
            "ground 2x2 pose keeps the historical half-cell intersection compensation");
        Check(Quaternion.Angle(rotation, Quaternion.identity) < 0.01f,
            "ground pose keeps using the definition's own rotation, not the grid's");

        var corners = new Vector3[4];
        ground.GetCellCorners(new Vector2Int(1, 1), corners);
        Check(Almost(corners[0], new Vector3(1f, 0.02f, 1f)) && Almost(corners[1], new Vector3(2f, 0.02f, 1f))
            && Almost(corners[2], new Vector3(2f, 0.02f, 2f)) && Almost(corners[3], new Vector3(1f, 0.02f, 2f)),
            "ground cell corners keep their historical X/Z layout");

        Check(ground.TryPlaceTrap(new Vector2Int(0, 0), definition, out TrapInstance instance, out string failure),
            "ground grid still places a ground trap: " + failure);
        Check(Almost(instance.transform.position, new Vector3(0.5f, 0.02f, 0.5f)),
            "spawned ground trap keeps its historical position");
    }

    private static void ValidateCategoryInterception()
    {
        TrapPlacementGrid wall = CreateGrid("Wall grid", new Vector3(10f, 0f, 5f), Quaternion.identity,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.01f);
        TrapPlacementGrid ground = CreateGridLegacy("Ground grid", Vector3.zero, 6, 4, 1f, 0.02f);
        TrapDefinition groundTrap = CreateDefinition("ground_turret", TrapMountType.Ground, 1, 1);
        TrapDefinition wallTrap = CreateDefinition("wall_turret", TrapMountType.Wall, 1, 1);
        TrapDefinition invalidWallTrap = CreateDefinition("wall_spike", TrapMountType.Wall, 1, 1, walkableFloorTrap: true);

        Check(!wall.CanPlaceTrap(Vector2Int.zero, groundTrap, out string wallFailure)
            && wallFailure.Contains("墙面"), "a wall grid refuses ground traps with an explicit reason");
        Check(!ground.CanPlaceTrap(Vector2Int.zero, wallTrap, out string groundFailure)
            && groundFailure.Contains("地面"), "a ground grid refuses wall traps with an explicit reason");
        Check(!wall.CanPlaceTrap(Vector2Int.zero, invalidWallTrap, out _),
            "wall + walkable-floor support is rejected as an invalid pairing");
        Check(wall.CanPlaceTrap(Vector2Int.zero, wallTrap, out string okFailure) && okFailure.Length == 0,
            "matching categories still place cleanly: " + okFailure);
    }

    /// <summary>
    /// Unit 3: support sampling, solid-entity conflicts and monster clearance.
    /// Every case builds its own plain colliders so the checks stay deterministic
    /// and independent of scene content.
    /// </summary>
    private static void ValidateWallSupportAndClearance()
    {
        // A wall grid whose backing wall only covers the lower-left column must
        // reject cells that hang over a hole, even though the mask is open.
        TrapPlacementGrid partial = CreateGridWithPartialWall("Partial wall grid", Vector3.zero, 4, 3);
        TrapDefinition wallTrap = CreateDefinition("wall_turret", TrapMountType.Wall, 1, 1);
        Check(partial.HasWallSupport(Vector2Int.zero, Vector2Int.one, wallTrap, out string firstFailure)
            && firstFailure.Length == 0, "a cell over the backing wall is supported: " + firstFailure);
        Check(!partial.HasWallSupport(new Vector2Int(3, 0), Vector2Int.one, wallTrap, out string gapFailure)
            && gapFailure.Contains("支撑不足"), "a cell beyond the wall is rejected as unsupported: " + gapFailure);
        Check(!partial.CanPlaceTrap(new Vector2Int(3, 0), wallTrap, out string maskedFailure)
            && maskedFailure.Contains("支撑不足"), "placement refuses an unsupported wall cell: " + maskedFailure);
        Check(partial.CanPlaceTrap(Vector2Int.zero, wallTrap, out string okFailure) && okFailure.Length == 0,
            "placement accepts a supported wall cell: " + okFailure);
        Cleanup();

        // An unbound grid can never support a wall trap.
        var unbound = new GameObject("Unbound wall grid");
        unbound.transform.position = Vector3.zero;
        owned.Add(unbound);
        var unboundGrid = unbound.AddComponent<TrapPlacementGrid>();
        unboundGrid.ConfigureLayout(2, 2, 1f, 0f, AllOpen(4), TrapPlacementGrid.SurfaceKind.Ground,
            TrapMountType.Wall, null, 0.01f);
        TrapDefinition unboundTrap = CreateDefinition("wall_unbound", TrapMountType.Wall, 1, 1);
        Check(!unboundGrid.CanPlaceTrap(Vector2Int.zero, unboundTrap, out string unboundFailure)
            && unboundFailure.Contains("支撑"), "an unbound wall grid refuses placement: " + unboundFailure);
        Cleanup();

        // Solid-entity conflicts: a protruding prop blocks the install box, a
        // ghost preview never does, and the bound wall itself is never a conflict.
        TrapPlacementGrid wall = CreateGrid("Wall grid", new Vector3(10f, 0f, 5f), Quaternion.identity,
            TrapMountType.Wall, 4, 3, 1f, 0f, 0.01f);
        TrapDefinition oneByOne = CreateDefinition("wall_solid", TrapMountType.Wall, 1, 1,
            installBoundsSize: new Vector3(0.8f, 0.8f, 0.3f));
        Check(wall.CanPlaceTrap(Vector2Int.zero, oneByOne, out string cleanFailure) && cleanFailure.Length == 0,
            "an empty wall spot has no entity conflict: " + cleanFailure);
        wall.GetPlacementPose(Vector2Int.zero, oneByOne, out Vector3 anchor, out _);

        var ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghost.name = "Trap Preview";
        ghost.AddComponent<TrapPlacementPreview>();
        ghost.transform.position = anchor;
        ghost.transform.localScale = Vector3.one;
        owned.Add(ghost);
        Check(wall.CanPlaceTrap(Vector2Int.zero, oneByOne, out string ghostFailure) && ghostFailure.Length == 0,
            "a ghost preview is not a solid entity conflict: " + ghostFailure);

        var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prop.name = "Wall corner prop";
        prop.transform.position = anchor;
        prop.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        owned.Add(prop);
        Check(!wall.CanPlaceTrap(Vector2Int.zero, oneByOne, out string propFailure)
            && propFailure.Contains("实体冲突"), "a protruding prop blocks the install box: " + propFailure);
        Object.DestroyImmediate(prop);
        Object.DestroyImmediate(ghost);

        // An existing wall trap blocks a later trap whose install box reaches into
        // it, while the same trap still validates itself after a reload.
        TrapDefinition plated = CreateDefinition("wall_plated", TrapMountType.Wall, 2, 2,
            installBoundsSize: new Vector3(1.6f, 1.6f, 0.3f));
        Check(wall.TryPlaceTrap(Vector2Int.zero, plated, out TrapInstance placed, out string placeFailure),
            "2x2 wall trap placed for the conflict check: " + placeFailure);
        TrapDefinition wide = CreateDefinition("wall_wide", TrapMountType.Wall, 1, 1,
            installBoundsSize: new Vector3(3f, 1f, 0.3f));
        Check(!wall.CanPlaceTrap(new Vector2Int(2, 0), wide, out string trapFailure)
            && trapFailure.Contains("实体冲突"),
            "an install box reaching an existing trap is rejected: " + trapFailure);
        // Occupancy intentionally rejects a duplicate before entity checks, so the
        // self-ignore path is asserted on the entity check the reload/creation flow
        // uses: a trap validating its own install box must not conflict with itself.
        Check(!wall.ConflictsWithSolidEntity(Vector2Int.zero, new Vector2Int(2, 2), plated, placed, null, out string selfFailure)
            && selfFailure.Length == 0, "a trap ignores its own body during reload validation: " + selfFailure);
        Check(wall.ConflictsWithSolidEntity(Vector2Int.zero, new Vector2Int(2, 2), plated, null, null, out string otherFailure)
            && otherFailure.Contains("实体冲突"),
            "the same install box does conflict with the placed trap it belongs to: " + otherFailure);
        wall.RebuildOccupancy();
        Check(wall.OwnsTrap(placed), "wall trap survives an occupancy rebuild with the new checks");
        Cleanup();

        // Monster clearance: the same trap is rejected when its body intrudes into
        // the walking band of an open road cell beneath it, and accepted higher up.
        TrapPlacementGrid clearanceGrid = CreateGrid("Clearance wall grid", Vector3.zero, Quaternion.identity,
            TrapMountType.Wall, 2, 6, 1f, 0f, 0.01f);
        var road = new GameObject("Clearance road");
        owned.Add(road);
        var pathGrid = road.AddComponent<MonsterPathGrid>();
        for (int y = 0; y < 6; y++) for (int x = 0; x < 2; x++) pathGrid.SetOpen(new Vector2Int(x, y), true);
        clearanceGrid.PathGridOverride = pathGrid;
        TrapDefinition low = CreateDefinition("wall_low", TrapMountType.Wall, 1, 1);
        Check(clearanceGrid.ConflictsWithMonsterClearance(Vector2Int.zero, Vector2Int.one, low, out string bandFailure)
            && bandFailure.Contains("通道"), "the clearance check names the intruded corridor: " + bandFailure);
        Check(!clearanceGrid.CanPlaceTrap(Vector2Int.zero, low, out string lowFailure),
            "a wall body inside the walking band is rejected");
        Check(lowFailure.Contains("通道"), "the rejection names the walking corridor: " + lowFailure);
        Check(clearanceGrid.CanPlaceTrap(new Vector2Int(0, 3), low, out string highFailure) && highFailure.Length == 0,
            "an equivalent trap above the walking band is accepted: " + highFailure);
        clearanceGrid.PathGridOverride = null;
        Cleanup();
    }

    private static TrapPlacementGrid CreateGrid(string name, Vector3 position, Quaternion rotation,
        TrapMountType mountType, int columns, int rows, float cellSize, float placementHeight, float surfaceOffset)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, rotation);
        owned.Add(root);
        // Support validation (unit 3) samples inward along the wall normal, so the
        // backing wall must actually cover the authored rectangle: local X/Y span
        // the grid, local Z sits behind the placement plane.
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(columns * cellSize * 0.5f, rows * cellSize * 0.5f, -0.5f);
        collider.size = new Vector3(columns * cellSize, rows * cellSize, 1f);
        var grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(columns, rows, cellSize, placementHeight, AllOpen(columns * rows),
            TrapPlacementGrid.SurfaceKind.Ground, mountType, collider, surfaceOffset);
        return grid;
    }

    /// <summary>
    /// Wall grid whose backing wall deliberately covers only the lower-left
    /// column, so support sampling must fail for cells outside it.
    /// </summary>
    private static TrapPlacementGrid CreateGridWithPartialWall(string name, Vector3 position, int columns, int rows)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        owned.Add(root);
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0.5f, rows * 0.5f, -0.5f);
        collider.size = new Vector3(1f, rows, 1f);
        var grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(columns, rows, 1f, 0f, AllOpen(columns * rows),
            TrapPlacementGrid.SurfaceKind.Ground, TrapMountType.Wall, collider, 0.01f);
        return grid;
    }

    private static TrapPlacementGrid CreateGridLegacy(string name, Vector3 position, int columns, int rows,
        float cellSize, float placementHeight)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        owned.Add(root);
        var grid = root.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(columns, rows, cellSize, placementHeight, AllOpen(columns * rows));
        return grid;
    }

    private static bool[] AllOpen(int count)
    {
        var mask = new bool[count];
        for (int i = 0; i < count; i++) mask[i] = true;
        return mask;
    }

    private static TrapDefinition CreateDefinition(string id, TrapMountType mountType, int width, int height,
        bool walkableFloorTrap = false, Vector3 installBoundsSize = default)
    {
        var definition = ScriptableObject.CreateInstance<TrapDefinition>();
        definition.name = id;
        owned.Add(definition);
        var settings = new SerializedObject(definition);
        settings.FindProperty("trapId").stringValue = id;
        settings.FindProperty("displayName").stringValue = id;
        settings.FindProperty("footprintWidth").intValue = width;
        settings.FindProperty("footprintHeight").intValue = height;
        settings.FindProperty("mountType").enumValueIndex = (int)mountType;
        settings.FindProperty("walkableFloorTrap").boolValue = walkableFloorTrap;
        if (installBoundsSize != Vector3.zero)
            settings.FindProperty("installBoundsSize").vector3Value = installBoundsSize;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static bool Almost(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= Tolerance * Tolerance;
    private static bool Almost(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= Tolerance * Tolerance;
    private static bool Almost(float a, float b) => Mathf.Abs(a - b) <= Tolerance;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Wall trap grid validation failed: " + message);
        checks++;
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }
}
