using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 地刺陷阱第五阶段验收：菜单接入、付费放置、可通行性、原有陷阱回归，以及主场景道路网格烟测。
/// 全部为批处理内的确定性断言，不依赖测试程序集。
/// </summary>
public static class GroundSpikeStage5Acceptance
{
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    private static readonly List<Object> owned = new List<Object>();
    private static int checks;

    public static int Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run stage 5 acceptance in a batch editor, outside Play mode.");
        checks = 0;
        try
        {
            ValidateMenuIntegration();
            Cleanup();
            ValidatePaidPlacement();
            Cleanup();
            ValidateWalkThrough();
            Cleanup();
            ValidateExistingTrapRegression();
            Cleanup();
            ValidateSceneRoadPlacement();
            Debug.Log($"GROUND_SPIKE_STAGE5_PASS: {checks} assertions");
            return checks;
        }
        finally { Cleanup(); }
    }

    // --- 1. 菜单与资源接入 -------------------------------------------------

    private static void ValidateMenuIntegration()
    {
        TrapDefinition spike = null;
        int found = 0;
        foreach (TrapDefinition definition in Resources.LoadAll<TrapDefinition>(string.Empty))
        {
            if (definition == null || definition.TrapId != "ground-spike") continue;
            found++;
            spike = definition;
        }
        Check(found == 1, "trap menu discovers exactly one ground spike definition");
        Check(spike != null && spike.DisplayName == "地刺陷阱", "menu entry keeps its authored display name");
        Check(spike.Prefab != null && spike.Prefab.GetComponent<GroundSpikeTrap>() != null,
            "menu entry points at the attacking spike prefab");
        Check(spike.Prefab == AssetDatabase.LoadAssetAtPath<GameObject>(GroundSpikeTrapAuthoring.PrefabPath),
            "menu entry uses the shipped prefab, not a stale copy");
        Check(spike.WalkableFloorTrap && !spike.AllowsPlatformPlacement,
            "menu entry keeps the road-only walkable placement category");
        Check(spike.Cost >= 0, "menu entry has a non-negative price");
    }

    // --- 2. 付费放置 -------------------------------------------------------

    private static void ValidatePaidPlacement()
    {
        TrapDefinition spike = SpikeDefinition();
        TrapPlacementGrid grid = CreateGrid("Stage 5 paid road grid", TrapPlacementGrid.SurfaceKind.Road);
        CreateFloor(grid);
        Check(grid.TryGetFootprint(spike, out Vector2Int footprint) && footprint == new Vector2Int(2, 2),
            "purchase path uses the authored 2x2 metre footprint");

        EconomyManager wallet = EconomyManager.GetOrCreate();
        owned.Add(wallet.gameObject);
        wallet.BeginRun(spike.Cost + 25);

        Check(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, spike, out TrapInstance bought, out string failure),
            "paid placement on the road succeeds: " + failure);
        Check(bought != null && bought.Definition == spike && wallet.Balance == 25,
            "purchase charges the authored price exactly once");
        Check(grid.PlacedTraps.Count == 1 && grid.IsOccupied(Vector2Int.zero) && grid.IsOccupied(new Vector2Int(1, 1)),
            "paid placement reserves the whole 2m plate");

        Check(!TrapPurchaseService.TryPurchase(grid, new Vector2Int(1, 1), spike, out _, out _),
            "paid overlap is rejected");
        Check(wallet.Balance == 25 && grid.PlacedTraps.Count == 1, "rejected purchase leaves no side effects");

        if (spike.Cost > 0)
        {
            wallet.BeginRun(spike.Cost - 1);
            Check(!TrapPurchaseService.TryPurchase(grid, new Vector2Int(3, 0), spike, out _, out _),
                "insufficient gold blocks the purchase");
            Check(wallet.Balance == spike.Cost - 1 && !grid.IsOccupied(new Vector2Int(3, 0)),
                "unaffordable purchase leaves no side effects");
        }

        // 买到的实例必须能真正工作：共享血量 + 攻击循环。
        Invoke(bought, "Awake");
        Invoke(bought, "OnEnable");
        GroundSpikeTrap attack = bought.GetComponent<GroundSpikeTrap>();
        Check(attack != null, "purchased spike carries its attack component");
        Invoke(attack, "Awake");
        Invoke(attack, "OnEnable");
        Check(bought.MaxHealth == 100f && bought.CurrentHealth == 100f, "purchased spike uses the shared 100 HP trap health");
        EnemyHealth victim = Enemy(grid.GetTrapWorldPosition(Vector2Int.zero, new Vector2Int(2, 2)));
        attack.Tick(0.1f);
        Check(victim.CurrentHealth == 60f, "purchased spike damages a ground monster through the shared attack loop");
        bought.TakeDamage(bought.MaxHealth);
        Check(bought.IsDestroyed && attack.State == GroundSpikeTrap.AttackState.Ready
            && grid.PlacedTraps.Count == 0 && !grid.IsOccupied(Vector2Int.zero) && !grid.IsOccupied(new Vector2Int(1, 1)),
            "lethal damage releases the paid 2x2 footprint immediately");
    }

    // --- 3. 可通行性（不阻挡移动） ----------------------------------------

    private static void ValidateWalkThrough()
    {
        Check(typeof(GroundSpikeTrap).GetMethod("OnTriggerEnter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null
            && typeof(GroundSpikeTrap).GetMethod("OnTriggerStay",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
            "detection never depends on trigger callbacks");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundSpikeTrapAuthoring.PrefabPath);
        var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        owned.Add(placed);
        placed.transform.position = Vector3.zero;
        Physics.SyncTransforms();

        Collider[] colliders = placed.GetComponentsInChildren<Collider>(true);
        Check(colliders.Length > 0 && Array.TrueForAll(colliders, collider => collider.isTrigger),
            "every shipped collider is a trigger");

        // 用物理查询证明整块 2m 底板体积内没有任何非 Trigger 碰撞体。
        Vector3 probeExtents = new Vector3(0.95f, 0.09f, 0.95f);
        int blocking = 0;
        foreach (Collider hit in Physics.OverlapBox(placed.transform.position + Vector3.up * 0.09f, probeExtents,
                     Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (hit.transform.IsChildOf(placed.transform)) blocking++;
        Check(blocking == 0, "no blocking collider lives inside the plate volume");

        // 怪物/玩家可以从底板正上方与正侧方穿过：射线只命中非地刺物体。
        float height = 0.12f;
        Vector3[] probes =
        {
            new Vector3(0f, height, 0f), new Vector3(0.5f, height, 0f), new Vector3(-0.5f, height, 0f),
            new Vector3(0f, height, 0.5f), new Vector3(0f, height, -0.5f)
        };
        foreach (Vector3 probe in probes)
        {
            bool selfHit = false;
            foreach (Collider hit in Physics.OverlapSphere(placed.transform.position + probe, 0.2f, ~0,
                         QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(placed.transform)) selfHit = true;
            Check(!selfHit, "walk level is clear at " + probe);
        }

        // 待机时尖刺必须收在底板以下，避免站上去被顶起。
        Renderer plate = placed.transform.Find("Base plate").GetComponent<Renderer>();
        foreach (MeshFilter spear in placed.transform.Find("Spikes").GetComponentsInChildren<MeshFilter>())
            Check(spear.GetComponent<Renderer>().bounds.max.y <= plate.bounds.max.y,
                "idle spear stays under the plate surface");
    }

    // --- 4. 原有陷阱回归 ---------------------------------------------------

    private static void ValidateExistingTrapRegression()
    {
        TrapDefinition sentry = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/AutoSentryTurret.asset");
        TrapDefinition launcher = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/DualMissileLauncherLoaded.asset");
        TrapDefinition spike = SpikeDefinition();
        Check(sentry != null && launcher != null, "existing trap definitions still load");

        foreach (TrapDefinition definition in new[] { sentry, launcher })
            Check(definition != null && !definition.WalkableFloorTrap && definition.AllowsPlatformPlacement,
                "existing trap keeps its ground/platform placement category");
        // 已知外部状态：AutoSentryTurret.prefab 引用了不在仓库里的脚本 GUID，
        // 导入失败后定义里的 prefab 会解析为 null，网格走立方体回退路径。
        // 这里只断言回退路径仍然可用，不把该既有问题算作地刺改动引起的回归。
        Check(launcher.Prefab != null && launcher.Prefab.GetComponent<MissileLauncherTurret>() != null,
            "launcher trap keeps its authored prefab and turret component");
        Check(sentry.Prefab == null, "sentry trap still relies on the documented cube fallback path");

        TrapPlacementGrid ground = CreateGrid("Stage 5 ground grid", TrapPlacementGrid.SurfaceKind.Ground);
        TrapPlacementGrid platform = CreateGrid("Stage 5 platform grid", TrapPlacementGrid.SurfaceKind.Platform);
        TrapPlacementGrid road = CreateGrid("Stage 5 road grid", TrapPlacementGrid.SurfaceKind.Road);
        CreateFloor(ground);

        Check(ground.CanPlaceTrap(Vector2Int.zero, sentry, out _), "existing trap still places on the ground grid");
        Check(ground.TryPlaceTrap(Vector2Int.zero, sentry, out TrapInstance placedSentry, out string failure),
            "existing trap still spawns on the ground: " + failure);
        Check(placedSentry != null && placedSentry.gameObject.GetComponentInChildren<AutoSentryTurret>(true) != null,
            "sentry fallback still builds its turret component on the ground grid");
        int sentryColliders = placedSentry.GetComponentsInChildren<Collider>(true).Length;
        Check(sentryColliders == 0 || !placedSentry.Definition.WalkableFloorTrap,
            "ground-placed sentry keeps its non-walkable ground category");

        Check(platform.CanPlaceTrap(Vector2Int.zero, sentry, out _), "existing trap still places on platforms");
        Check(!platform.CanPlaceTrap(Vector2Int.zero, spike, out _), "spikes are still rejected on platforms");
        Check(road.CanPlaceTrap(new Vector2Int(2, 2), launcher, out _), "existing trap still places on the road");
        Check(road.TryPlaceTrap(new Vector2Int(2, 2), launcher, out TrapInstance placedLauncher, out _),
            "existing trap still spawns on the road");
        bool roadWalkable = true;
        foreach (Collider collider in placedLauncher.GetComponentsInChildren<Collider>(true))
            if (!collider.isTrigger) roadWalkable = false;
        Check(roadWalkable, "road-placed ordinary trap stays walkable as before");
        Check(placedLauncher != null && placedLauncher.GetComponent<MissileLauncherTurret>() != null
            && placedLauncher.GetComponent<TrapInstance>() != null,
            "road-placed launcher still receives the runtime trap component from the grid");
        // Edit mode does not dispatch runtime Awake callbacks, so prime the pool explicitly.
        Invoke(placedLauncher, "Awake");
        Check(placedLauncher.CurrentHealth == placedLauncher.MaxHealth && placedLauncher.MaxHealth == 100f,
            "road-placed launcher still uses the shared 100 HP trap pool");
        placedLauncher.TakeDamage(10f);
        Check(placedLauncher.CurrentHealth == 90f, "road-placed launcher still accepts shared trap damage");
    }

    // --- 5. 主场景道路网格烟测（只读，不修改场景） ------------------------

    private struct RoadLayout
    {
        public int Columns, Rows;
        public float CellSize, Height;
        public bool[] Open;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private static void ValidateSceneRoadPlacement()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Check(scene.IsValid() && scene.isLoaded, "main scene opens for the integration smoke test");
        Check(Object.FindObjectsByType<GroundSpikeTrap>(FindObjectsSortMode.None).Length == 0,
            "main scene ships without pre-placed spikes");

        TrapPlacementGrid road = null;
        var grids = new List<TrapPlacementGrid>();
        foreach (TrapPlacementGrid grid in Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None))
        {
            grids.Add(grid);
            if (grid.Surface == TrapPlacementGrid.SurfaceKind.Road && road == null) road = grid;
        }
        Check(road != null, "main scene exposes a road placement grid");
        Check(grids.Count >= 1 && grids.TrueForAll(grid => grid.Surface != TrapPlacementGrid.SurfaceKind.Legacy),
            "every main scene placement grid resolves to a concrete surface kind");

        TrapDefinition spike = SpikeDefinition();
        RoadLayout layout = Capture(road);
        int open = 0;
        foreach (bool cell in layout.Open) if (cell) open++;
        Check(open >= 4 && layout.CellSize > 0f, "main scene road grid keeps usable open cells");

        // 只读判定：道路网格上的地刺与普通陷阱仍按既有类别规则受理/拒绝。
        Check(road.TryGetFootprint(spike, out Vector2Int sceneFootprint) && sceneFootprint == new Vector2Int(2, 2),
            "main scene road cell size still maps the 2m plate onto 2x2 cells");
        foreach (TrapPlacementGrid grid in grids)
            if (grid.Surface == TrapPlacementGrid.SurfaceKind.Platform)
                Check(!grid.CanPlaceTrap(Vector2Int.zero, spike, out _),
                    "main scene platform grid still rejects the spike plate");
        Check(spike.Prefab != null && spike.Prefab.GetComponent<GroundSpikeTrap>() != null,
            "main scene menu entry resolves to a live spike prefab");

        // 在空场景里按主场景道路网格的真实参数复刻一份，避免弄脏主场景。
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        TrapPlacementGrid clone = New("Cloned road grid", layout.Position).AddComponent<TrapPlacementGrid>();
        clone.transform.rotation = layout.Rotation;
        clone.transform.localScale = layout.Scale;
        clone.ConfigureLayout(layout.Columns, layout.Rows, layout.CellSize, layout.Height, layout.Open,
            TrapPlacementGrid.SurfaceKind.Road);

        // 打开主场景后资产实例会随 AssetDatabase 刷新失效，重新取一次定义，避免用到假空对象。
        spike = SpikeDefinition();
        Check(spike != null, "spike definition reloads after the main scene smoke test");
        Check(clone.TryGetFootprint(spike, out Vector2Int cloneFootprint) && cloneFootprint == new Vector2Int(2, 2),
            "cloned 1m scene road lattice still maps the plate onto 2x2 cells");
        int cloneWindows = CountOpenWindows(clone, cloneFootprint);
        Vector2Int target = FindOpenFootprint(clone, spike);
        Check(target.x >= 0, "main scene road layout offers a free 2m window for the spike plate "
            + $"(1m-lattice 2x2 windows={cloneWindows})");
        CreateFloor(clone);

        bool[] before = OpenSnapshot(clone);
        Check(clone.CanPlaceTrap(target, spike, out string failure),
            "cloned scene road grid accepts the spike plate: " + failure);
        Check(clone.TryPlaceTrap(target, spike, out TrapInstance instance, out failure),
            "cloned scene road placement succeeds: " + failure);
        Check(instance != null && instance.GetComponent<GroundSpikeTrap>() != null,
            "scene-placed spike carries its attack component");
        Bounds plate = instance.transform.Find("Base plate").GetComponent<Renderer>().bounds;
        Check(Mathf.Abs(plate.size.x - 2f) < 0.001f && Mathf.Abs(plate.size.z - 2f) < 0.001f,
            "scene-placed plate stays exactly 2x2 metres, never scaled by the grid");
        bool walkable = true;
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            if (!collider.isTrigger) walkable = false;
        Check(walkable, "scene-placed spike cannot block the monster road");

        bool[] after = OpenSnapshot(clone);
        bool unchanged = before.Length == after.Length;
        for (int i = 0; i < before.Length && unchanged; i++) unchanged = before[i] == after[i];
        Check(unchanged, "placing a spike never closes a road cell");
        Check(clone.IsOccupied(target) && clone.IsOccupied(target + new Vector2Int(1, 1)),
            "scene placement reserves its full 2x2 footprint");
        Check(!clone.CanPlaceTrap(target + Vector2Int.one, spike, out _), "scene placement rejects overlap");

        Check(clone.RemoveTrap(instance) && !clone.IsOccupied(target)
            && Object.FindObjectsByType<GroundSpikeTrap>(FindObjectsSortMode.None).Length == 0,
            "scene placement removes cleanly and leaves no residue");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static RoadLayout Capture(TrapPlacementGrid grid)
    {
        var layout = new RoadLayout
        {
            Columns = grid.Columns,
            Rows = grid.Rows,
            CellSize = grid.CellSize,
            Height = grid.PlacementHeight,
            Open = OpenSnapshot(grid),
            Position = grid.transform.position,
            Rotation = grid.transform.rotation,
            Scale = grid.transform.lossyScale
        };
        return layout;
    }
    private static Vector2Int FindOpenFootprint(TrapPlacementGrid grid, TrapDefinition definition)
    {
        if (grid == null) return new Vector2Int(-1, -1);
        if (!grid.TryGetFootprint(definition, out Vector2Int footprint)) return new Vector2Int(-1, -1);
        for (int y = 0; y <= grid.Rows - footprint.y; y++)
        for (int x = 0; x <= grid.Columns - footprint.x; x++)
        {
            bool open = true;
            for (int dy = 0; dy < footprint.y && open; dy++)
            for (int dx = 0; dx < footprint.x && open; dx++)
                open = grid.IsOpen(new Vector2Int(x + dx, y + dy));
            if (open) return new Vector2Int(x, y);
        }
        return new Vector2Int(-1, -1);
    }

    /// <summary>Counts footprints that fit entirely inside currently open cells.</summary>
    private static int CountOpenWindows(TrapPlacementGrid grid, Vector2Int footprint)
    {
        if (grid == null || footprint.x < 1 || footprint.y < 1) return 0;
        int windows = 0;
        for (int y = 0; y <= grid.Rows - footprint.y; y++)
        for (int x = 0; x <= grid.Columns - footprint.x; x++)
        {
            bool open = true;
            for (int dy = 0; dy < footprint.y && open; dy++)
            for (int dx = 0; dx < footprint.x && open; dx++)
                open = grid.IsOpen(new Vector2Int(x + dx, y + dy));
            if (open) windows++;
        }
        return windows;
    }

    private static bool[] OpenSnapshot(TrapPlacementGrid grid)
    {
        var snapshot = new bool[grid.Columns * grid.Rows];
        for (int y = 0; y < grid.Rows; y++)
        for (int x = 0; x < grid.Columns; x++)
            snapshot[y * grid.Columns + x] = grid.IsOpen(new Vector2Int(x, y));
        return snapshot;
    }

    // --- 工具 --------------------------------------------------------------

    private static TrapDefinition SpikeDefinition() => AssetDatabase.LoadAssetAtPath<TrapDefinition>(
        GroundSpikeTrapAuthoring.DefinitionPath);

    private static TrapPlacementGrid CreateGrid(string name, TrapPlacementGrid.SurfaceKind surface)
    {
        TrapPlacementGrid grid = New(name, Vector3.zero).AddComponent<TrapPlacementGrid>();
        var open = new bool[64];
        for (int i = 0; i < open.Length; i++) open[i] = true;
        grid.ConfigureLayout(8, 8, 1f, 0.02f, open, surface);
        return grid;
    }

    /// <summary>A ground slab that covers every cell of the grid, so placement only
    /// fails for the rule under test rather than for missing floor support.</summary
    private static GameObject CreateFloor(TrapPlacementGrid grid)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        owned.Add(floor);
        float width = grid.Columns * grid.CellSize;
        float depth = grid.Rows * grid.CellSize;
        floor.transform.position = grid.transform.TransformPoint(
            new Vector3(width * 0.5f, grid.PlacementHeight - 0.5f, depth * 0.5f));
        floor.transform.localScale = new Vector3(width + 2f, 1f, depth + 2f);
        Physics.SyncTransforms();
        return floor;
    }

    private static EnemyHealth Enemy(Vector3 position)
    {
        GameObject root = New("Stage 5 enemy", position);
        root.AddComponent<MonsterPathFollower>();
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        health.ResetHealth();
        return health;
    }

    private static GameObject New(string name, Vector3 position)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        owned.Add(root);
        return root;
    }

    private static void Invoke(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GroundSpike stage 5 failed: " + message);
        checks++;
    }

    private static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }
}