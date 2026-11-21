using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class DoubleLayerFacilityAuthoring
{
    private const string PumpSpawnId = "enemy-spawn-pump";
    private const string WorkshopSpawnId = "enemy-spawn-workshop";
    private const string FrontLeftAirSpawnId = "enemy-spawn-flying-front-left";
    private const string FrontRightAirSpawnId = "enemy-spawn-flying-front-right";
    private const string RearLeftAirSpawnId = "enemy-spawn-flying-rear-left";
    private const string RearRightAirSpawnId = "enemy-spawn-flying-rear-right";
    private const string FacilityCeilingPanelMaterialPath = "Assets/Resources/FacilityCeilingPanel.mat";
    private static readonly (string suffix, string name, Vector3 position)[] FacilityCeilingPanels =
    {
        ("Front_Beam_West", "Facility Ceiling Trap Panel Front Beam West", new Vector3(-15.5f, 5.34f, -25f)),
        ("Front_Beam_East", "Facility Ceiling Trap Panel Front Beam East", new Vector3(15.5f, 5.34f, -25f)),
        ("Workshop_Beam_West", "Facility Ceiling Trap Panel Workshop Beam West", new Vector3(-15.5f, 5.34f, -12f)),
        ("Workshop_Beam_East", "Facility Ceiling Trap Panel Workshop Beam East", new Vector3(15.5f, 5.34f, -12f))
    };

    // Gameplay is applied after the geometry import so the facility keeps its
    // authored multi-storey cargo routes instead of a flat imported path grid.
    private static void ConfigureFacilityGameplay(GameObject world)
    {
        var document = MapForgeSceneOrganization.Parse(File.ReadAllText(SourcePath));
        var instances = world.GetComponentsInChildren<MapForgeObjectProperties>(true)
            .ToDictionary(item => item.objectId);
        var gameplay = new GameObject("Facility Gameplay");
        gameplay.transform.SetParent(world.transform, false);
        var manager = gameplay.AddComponent<WaveManager>();
        manager.TotalWaves = 3;

        EnemyCore core = instances["energy-core"].gameObject.AddComponent<EnemyCore>();
        var sourceCore = document.objects.First(node => node.id == "energy-core").unity.enemyCore;
        var serializedCore = new SerializedObject(core);
        serializedCore.FindProperty("maxHealth").floatValue = sourceCore.maxHealth;
        serializedCore.FindProperty("groundDamage").floatValue = sourceCore.groundDamage;
        serializedCore.FindProperty("flyingDamage").floatValue = sourceCore.flyingDamage;
        serializedCore.FindProperty("arrivalRadius").floatValue = sourceCore.arrivalRadius;
        serializedCore.ApplyModifiedPropertiesWithoutUndo();

        MonsterPathGrid lanes = CreateFacilityLaneGrid(gameplay.transform, instances);
        foreach (var pad in instances.Values.Where(item => item.tags != null && item.tags.Contains("defense-reservation")))
            CreateFacilityDefenseGrid(gameplay.transform, lanes, pad);
        CreateFacilityCeilingTrapPanels(world.transform);

        var groundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaskedSoldierAuthoring.PrefabPath);
        var flyingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HurricaneDroneAuthoring.EnemyPrefabPath);
        var shortSpearPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShortSpearDroneAuthoring.PrefabPath);
        var houndPrefab = Resources.Load<GameObject>("CrackjawHound");
        if (groundPrefab == null || flyingPrefab == null || shortSpearPrefab == null || houndPrefab == null)
            throw new InvalidOperationException("Facility requires MaskedSoldier, HurricaneDroneEnemy, ShortSpearDroneEnemy and CrackjawHound prefabs.");
        var authored = document.objects.Where(node => node.id.StartsWith("route-cargo-", StringComparison.Ordinal))
            .ToDictionary(node => node.id);

        foreach (var node in document.objects.Where(node => node.unity?.spawnPoint != null && node.unity.spawnPoint.enabled))
        {
            var mapping = node.unity.spawnPoint;
            bool flying = mapping.flyingEntrance;
            bool shortSpear = flying && (node.id == RearLeftAirSpawnId || node.id == RearRightAirSpawnId);
            var entrance = instances[node.id].gameObject.AddComponent<EnemySpawnPoint>();
            entrance.Core = core;
            entrance.PathGrid = flying ? null : lanes;
            entrance.EnemyPrefab = shortSpear ? shortSpearPrefab : flying ? flyingPrefab : groundPrefab;
            entrance.MonsterType = flying ? MonsterType.Flying : MonsterType.Ground;
            entrance.IsFlyingEntrance = flying;
            entrance.EntranceAltitude = mapping.entranceAltitude;
            entrance.FlightHeight = mapping.flightHeight;
            entrance.FlyingSpawnSpread = 2f;
            entrance.MoveSpeed = shortSpear ? ShortSpearDroneAuthoring.MoveSpeed : mapping.moveSpeed;
            entrance.SpawnInterval = mapping.spawnInterval;
            // TODO(确认): 首版外场给30秒布防、波间45秒；后续按实战调整源图波次配置。
            entrance.InitialWaveDelay = Mathf.Max(30f, mapping.initialDelay);
            var direction = mapping.travelDirection;
            entrance.TravelDirection = direction == null ? Vector3.forward
                : new Vector3(direction[0], direction[1], direction[2]);
            if (flying) entrance.transform.rotation = Quaternion.LookRotation(entrance.TravelDirection, Vector3.up);
            var serialized = new SerializedObject(entrance);
            var waves = serialized.FindProperty("waveSpawnSettings");
            waves.arraySize = 3;
            if (flying && mapping.waves?.Length != waves.arraySize)
                throw new InvalidOperationException("An air route requires a count for every wave: " + node.id);
            int[] groundCounts = { 3, 4, 2 };
            for (int i = 0; i < 3; i++)
            {
                var wave = waves.GetArrayElementAtIndex(i);
                bool hasSource = mapping.waves != null && i < mapping.waves.Length;
                wave.FindPropertyRelative("enabled").boolValue = !hasSource || mapping.waves[i].enabled;
                wave.FindPropertyRelative("enemyCount").intValue = hasSource ? mapping.waves[i].enemyCount : groundCounts[i];
                wave.FindPropertyRelative("enemyPrefab").objectReferenceValue = entrance.EnemyPrefab;
                wave.FindPropertyRelative("monsterType").enumValueIndex = (int)entrance.MonsterType;
                wave.FindPropertyRelative("flightHeight").floatValue = mapping.flightHeight;
                bool mixesHound = (i == 1 && (node.id == "enemy-spawn-left" || node.id == "enemy-spawn-right"))
                    || (i == 2 && (node.id == PumpSpawnId || node.id == WorkshopSpawnId));
                if (mixesHound)
                {
                    wave.FindPropertyRelative("alternateEnemyPrefab").objectReferenceValue = houndPrefab;
                    wave.FindPropertyRelative("alternateShare").floatValue = 1f / wave.FindPropertyRelative("enemyCount").intValue;
                    wave.FindPropertyRelative("alternateMoveSpeed").floatValue = 4f;
                }
            }
            var delays = serialized.FindProperty("waveTransitionDelays");
            delays.arraySize = 2;
            for (int i = 0; i < 2; i++) delays.GetArrayElementAtIndex(i).floatValue = 45f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!flying)
            {
                var branches = new List<Transform[]>();
                CollectFacilityRoute("route-cargo-" + mapping.laneId + "-start", new List<string>(), authored, instances, branches);
                entrance.gameObject.AddComponent<AuthoredMonsterRoute>().Configure(branches.ToArray());
            }
        }
        ConfigureWingIntake(instances, PumpSpawnId, "wing-pump-", "enemy-spawn-upper-left");
        ConfigureWingIntake(instances, WorkshopSpawnId, "wing-workshop-", "enemy-spawn-upper-right");
        gameplay.AddComponent<FacilityAirWaveRotation>().Configure(
            instances[FrontLeftAirSpawnId].GetComponent<EnemySpawnPoint>(),
            instances[FrontRightAirSpawnId].GetComponent<EnemySpawnPoint>());
        Physics.SyncTransforms();
    }

    private static void ConfigureWingIntake(Dictionary<string, MapForgeObjectProperties> instances,
        string spawnId, string gatePrefix, string rearSpawnId)
    {
        var entrance = instances[spawnId].GetComponent<EnemySpawnPoint>();
        var rear = instances[rearSpawnId].GetComponent<EnemySpawnPoint>();
        var shutter = instances[gatePrefix + "shutter"].transform;
        var panel = instances[gatePrefix + "shutter-panel"].GetComponentInChildren<Renderer>();
        var beacon = instances[gatePrefix + "beacon"].GetComponentInChildren<Renderer>();
        var warning = beacon.transform.parent.gameObject.AddComponent<AudioSource>();
        warning.playOnAwake = false;
        warning.spatialBlend = 1f;
        warning.minDistance = 12f;
        warning.maxDistance = 70f;
        warning.rolloffMode = AudioRolloffMode.Linear;
        entrance.gameObject.AddComponent<FacilityPumpIntake>().Configure(entrance, rear, shutter, panel, beacon, warning);
    }

    private static void ValidateFacilityGameplay(GameObject world)
    {
        var entrances = world.GetComponentsInChildren<EnemySpawnPoint>(true);
        var cores = world.GetComponentsInChildren<EnemyCore>(true);
        var grids = world.GetComponentsInChildren<TrapPlacementGrid>(true);
        var ceilingGrids = grids.Where(grid => grid.IsCeilingSurface).ToArray();
        var wallGrids = grids.Where(grid => grid.IsWallSurface).ToArray();
        var groundGrids = grids.Where(grid => !grid.IsMountedSurface).ToArray();
        var pads = world.GetComponentsInChildren<MapForgeObjectProperties>(true)
            .Where(item => item.tags != null && item.tags.Contains("defense-reservation")).ToArray();
        if (cores.Length != 1 || entrances.Length != 11 || entrances.Count(item => item.IsFlyingEntrance) != 4)
            throw new InvalidOperationException("Facility requires one core, seven ground entrances and four flying entrances.");
        if (groundGrids.Length != pads.Length || groundGrids.Length < 27)
            throw new InvalidOperationException("Every facility defense reservation requires a usable placement grid.");
        int exteriorCells = 0, rearCells = 0, wingCells = 0, allCells = 0;
        var lanes = world.GetComponentInChildren<MonsterPathGrid>();
        foreach (var grid in groundGrids)
        {
            int count = 0;
            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsOpen(cell)) continue;
                count++;
                Vector3 point = grid.CellToWorld(cell);
                if (lanes.IsOpen(cell))
                    throw new InvalidOperationException("Facility placement cell overlaps an enemy lane: " + grid.name);
                if (!Physics.Raycast(point + Vector3.up * .5f, Vector3.down, out var hit, 1f, ~0, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(hit.point.y - (point.y - TrapGridAuthoring.SurfaceOffset)) > .03f)
                    throw new InvalidOperationException("Facility placement cell lacks aligned support: " + grid.name + cell);
            }
            if (count == 0) throw new InvalidOperationException("Empty facility placement grid: " + grid.name);
            bool rearPad = grid.name.Contains("rear-defense-");
            bool outerWingPad = grid.name.Contains("wing-defense-") && grid.name.EndsWith("-apron", StringComparison.Ordinal);
            var definition = Resources.Load<TrapDefinition>(grid.name.Contains("exterior-aa-")
                ? "DualMissileLauncherLoaded"
                : (grid.name.Contains("exterior-defense-") || rearPad || outerWingPad) ? "XM777" : "AutoSentryTurret");
            if (definition == null) throw new InvalidOperationException("Facility defense trap definition is missing.");
            bool canPlace = false;
            for (int y = 0; y < grid.Rows && !canPlace; y++)
            for (int x = 0; x < grid.Columns && !canPlace; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.IsOpen(cell) && grid.CanPlaceTrap(cell, definition, out _)) canPlace = true;
            }
            if (!canPlace) throw new InvalidOperationException("No valid " + definition.DisplayName + " footprint on " + grid.name);
            allCells += count;
            if (grid.name.Contains("exterior-")) exteriorCells += count;
            if (rearPad) rearCells += count;
            if (grid.name.Contains("wing-defense-")) wingCells += count;
        }
        foreach (var entrance in entrances)
        {
            if (!entrance.SpawningEnabled || entrance.Core != cores[0] || entrance.EnemyPrefab == null)
                throw new InvalidOperationException("Facility entrance is not configured: " + entrance.name);
            if (entrance.IsFlyingEntrance) continue;
            string id = entrance.GetComponent<MapForgeObjectProperties>().objectId;
            bool rear = id.StartsWith("enemy-spawn-upper-", StringComparison.Ordinal);
            var mission = entrance.GetComponent<AuthoredMonsterRoute>();
            if (mission == null || mission.Routes.Count != (rear ? 1 : 2))
                throw new InvalidOperationException("Ground entrance has an unexpected number of cargo branches: " + entrance.name);
            if (rear && (entrance.TravelDirection != Vector3.back || entrance.IsWaveEnabled(1) ||
                !entrance.IsWaveEnabled(2) || entrance.GetEnemiesPerWave(2) != 1 ||
                !entrance.IsWaveEnabled(3) || entrance.GetEnemiesPerWave(3) != 2))
                throw new InvalidOperationException("Rear entrance must face the factory and join in waves two and three: " + entrance.name);
            if ((id == PumpSpawnId || id == WorkshopSpawnId) &&
                (entrance.TravelDirection != (id == PumpSpawnId ? Vector3.right : Vector3.left) || entrance.IsWaveEnabled(1) ||
                entrance.IsWaveEnabled(2) || !entrance.IsWaveEnabled(3) || entrance.GetEnemiesPerWave(3) != 2 ||
                entrance.GetComponent<FacilityPumpIntake>() == null))
                throw new InvalidOperationException("Invalid third-wave wing entrance: " + id);
            if ((id == "enemy-spawn-left" || id == "enemy-spawn-right") && entrance.IsWaveEnabled(3))
                throw new InvalidOperationException("The side entrances replace the front flanks in wave three.");
            for (int i = 1; i <= mission.Routes.Count; i++)
            {
                var route = mission.GetRoute(i, cores[0]);
                if (Vector3.Distance(route[0], entrance.transform.position) > .2f ||
                    Mathf.Abs(route[route.Count - 1].y - UpperFloorHeight) > .01f)
                    throw new InvalidOperationException("Ground mission is detached from entrance/floor: " + entrance.name);
            }
        }
        if (wallGrids.Length != 0)
            throw new InvalidOperationException("Facility should not contain the removed legacy wall trap grids.");
        ValidateFacilityCeilingTrapPanels(ceilingGrids);
        ValidateFacilityAirRoutes(world, cores[0], entrances, groundGrids);
        var sentry = Resources.Load<TrapDefinition>("AutoSentryTurret").Prefab.GetComponentInChildren<AutoSentryTurret>(true);
        Debug.Log($"FACILITY_GAMEPLAY_PASS: {entrances.Length} entrances, {groundGrids.Length} ground/platform grids and " +
            $"{ceilingGrids.Length} ceiling grids ({ceilingGrids.Length * 64} cells), " +
            $"{exteriorCells} front yard / {rearCells} rear yard / {wingCells} side wings / {allCells} total usable 1m cells; " +
            $"current sentry prefab range {sentry.DetectionRange:F1}m. Ground missions use authored ramp heights.");
    }

    private static void ValidateFacilityCeilingTrapPanels(TrapPlacementGrid[] grids)
    {
        var trap = Resources.Load<TrapDefinition>("ThroatLamp");
        if (grids.Length != 4 || trap == null || trap.MountType != TrapMountType.Ceiling)
            throw new InvalidOperationException("Facility requires four ceiling panels and a ceiling trap definition.");

        foreach (var grid in grids)
        {
            if (grid.Columns != 8 || grid.Rows != 8 || Mathf.Abs(grid.CellSize - 1f) > .001f
                || grid.SupportCollider == null || !grid.SupportsDefinition(trap))
                throw new InvalidOperationException("Invalid 8m ceiling trap grid: " + grid.name);

            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsOpen(cell))
                    throw new InvalidOperationException("Ceiling panel contains a closed trap cell: " + grid.name + cell);
                if (!grid.HasSurfaceSupport(cell, Vector2Int.one, trap, out string failure))
                    throw new InvalidOperationException("Ceiling panel cell lacks trap support: " + grid.name + cell + " " + failure);
            }

            bool canPlace = false;
            for (int y = 0; y < grid.Rows && !canPlace; y++)
            for (int x = 0; x < grid.Columns && !canPlace; x++)
                canPlace = grid.CanPlaceTrap(new Vector2Int(x, y), trap, out _);
            if (!canPlace) throw new InvalidOperationException("No valid ceiling trap footprint on " + grid.name);
        }
    }

    private static void ValidateFacilityAirRoutes(GameObject world, EnemyCore core, EnemySpawnPoint[] entrances,
        TrapPlacementGrid[] grids)
    {
        var air = entrances.Where(item => item.IsFlyingEntrance)
            .ToDictionary(item => item.GetComponent<MapForgeObjectProperties>().objectId);
        var rotation = world.GetComponentInChildren<FacilityAirWaveRotation>();
        if (rotation == null || rotation.FrontLeft != air[FrontLeftAirSpawnId] ||
            rotation.FrontRight != air[FrontRightAirSpawnId] ||
            rotation.FrontLeft.GetEnemiesPerWave(2) != 1 || rotation.FrontRight.GetEnemiesPerWave(2) != 1 ||
            rotation.FrontLeft.IsWaveEnabled(2) == rotation.FrontRight.IsWaveEnabled(2))
            throw new InvalidOperationException("Exactly one front route must start enabled for the second air wave.");

        var launcherDefinition = Resources.Load<TrapDefinition>("DualMissileLauncherLoaded");
        var launcher = launcherDefinition?.Prefab?.GetComponentInChildren<MissileLauncherTurret>(true);
        if (launcher == null || launcher.DetectionRange <= 0f)
            throw new InvalidOperationException("The facility air route check requires the dual missile launcher.");
        var routes = new[]
        {
            (FrontLeftAirSpawnId, false, false, "exterior-aa-left-reservation"),
            (FrontRightAirSpawnId, false, true, "exterior-aa-right-reservation"),
            (RearLeftAirSpawnId, true, false, "rear-defense-upper-left"),
            (RearRightAirSpawnId, true, true, "rear-defense-upper-right")
        };
        Vector3 reference = rotation.FrontLeft.transform.position - core.Position;
        float referenceSpeed = rotation.FrontLeft.MoveSpeed;
        var hurricane = AssetDatabase.LoadAssetAtPath<GameObject>(HurricaneDroneAuthoring.EnemyPrefabPath);
        var shortSpear = AssetDatabase.LoadAssetAtPath<GameObject>(ShortSpearDroneAuthoring.PrefabPath);
        if (hurricane == null || shortSpear == null || !hurricane.TryGetComponent<HurricaneDroneEnemy>(out _))
            throw new InvalidOperationException("The facility air routes require both drone enemy prefabs.");

        foreach (var (id, rear, right, padId) in routes)
        {
            var prefab = rear ? shortSpear : hurricane;
            float expectedSpeed = rear ? ShortSpearDroneAuthoring.MoveSpeed : referenceSpeed;
            var entrance = air[id];
            var waves = new SerializedObject(entrance).FindProperty("waveSpawnSettings");
            if (entrance.EnemyPrefab != prefab || waves.arraySize != entrance.TotalWaves ||
                Enumerable.Range(0, waves.arraySize).Any(index => waves.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("enemyPrefab").objectReferenceValue != prefab))
                throw new InvalidOperationException("Invalid drone prefab configuration: " + id);

            Vector3 start = entrance.transform.position;
            Vector3 expectedOffset = new Vector3(right ? -reference.x : reference.x, reference.y,
                rear ? -reference.z : reference.z);
            Vector3 end = core.Position + Vector3.up * entrance.FlightHeight;
            Vector3 route = end - start;
            float distance = route.magnitude;
            Vector3 direction = route.normalized;
            if (reference.x >= 0f || reference.z >= 0f ||
                Vector3.Distance(start - core.Position, expectedOffset) > .01f ||
                Mathf.Abs(end.y - start.y) > .01f || Mathf.Abs(entrance.EntranceAltitude - start.y) > .01f ||
                distance < 115f || expectedSpeed <= 0f || !Mathf.Approximately(entrance.MoveSpeed, expectedSpeed) ||
                Vector3.Dot(entrance.transform.forward, direction) < .999f ||
                entrance.IsWaveEnabled(1) == rear || (rear && !entrance.IsWaveEnabled(2)) ||
                !entrance.IsWaveEnabled(3))
                throw new InvalidOperationException("Invalid facility air route: " + id);

            Bounds bounds = new Bounds(start, Vector3.zero);
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                probe.transform.SetPositionAndRotation(start, entrance.transform.rotation);
                foreach (var renderer in probe.GetComponentsInChildren<Renderer>())
                    if (renderer is not TrailRenderer) bounds.Encapsulate(renderer.bounds);
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            Physics.SyncTransforms();
            float spreadDiameter = entrance.FlyingSpawnSpread * 2f;
            bounds.Expand(new Vector3(spreadDiameter, 0f, spreadDiameter));
            if (Physics.CheckBox(bounds.center, bounds.extents, Quaternion.identity, ~0,
                    QueryTriggerInteraction.Ignore) ||
                Physics.BoxCast(bounds.center, bounds.extents, direction, out _, Quaternion.identity, distance, ~0,
                    QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Flying monster plus spawn spread intersects scenery: " + id);

            var grid = grids.Single(item => item.name == "TrapPlacementGrid_" + padId);
            ValidateAirDefensePlacement(grid, launcherDefinition, launcher.DetectionRange, start, end);
            Debug.Log($"FACILITY_AIR_ROUTE_PASS: {id}, {distance:F2} m / {distance / entrance.MoveSpeed:F2} s, " +
                $"clear for {prefab.name} and covered by {padId}.");
        }

        int[] expectedCounts = { 2, 3, 4 };
        for (int wave = 1; wave <= expectedCounts.Length; wave++)
        {
            int count = air.Values.Where(item => item.IsWaveEnabled(wave)).Sum(item => item.GetEnemiesPerWave(wave));
            if (count != expectedCounts[wave - 1])
                throw new InvalidOperationException($"Wave {wave} must spawn {expectedCounts[wave - 1]} flying monsters.");
        }
    }

    private static void ValidateAirDefensePlacement(TrapPlacementGrid grid, TrapDefinition launcher, float range,
        Vector3 start, Vector3 end)
    {
        if (grid.TryGetFootprint(launcher, out Vector2Int footprint))
        {
            Vector3 route = end - start;
            for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.CanPlaceTrap(cell, launcher, out _)) continue;
                Vector3 site = grid.GetTrapWorldPosition(cell, footprint);
                Vector3 crossing = start + route * Mathf.Clamp01(Vector3.Dot(site - start, route) / route.sqrMagnitude);
                if (Vector3.Distance(site, crossing) < range &&
                    !Physics.Linecast(site + Vector3.up, crossing, ~0, QueryTriggerInteraction.Ignore)) return;
            }
        }
        throw new InvalidOperationException("No exposed missile launcher placement covers the flying approach: " + grid.name);
    }

    private static MonsterPathGrid CreateFacilityLaneGrid(Transform parent,
        Dictionary<string, MapForgeObjectProperties> instances)
    {
        var slab = instances["ground-slab"].GetComponentInChildren<Renderer>().bounds;
        var exterior = instances.Values.First(item => item.objectId == "exterior-yard-slab")
            .GetComponentInChildren<Renderer>().bounds;
        Bounds worldBounds = exterior;
        worldBounds.Encapsulate(slab);
        worldBounds.Encapsulate(instances["rear-service-foundation"].GetComponentInChildren<Renderer>().bounds);
        foreach (string side in new[] { "left", "right" })
            worldBounds.Encapsulate(instances["wing-" + side + "-slab"].GetComponentInChildren<Renderer>().bounds);
        var go = new GameObject("Facility Exterior Monster Lanes");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(worldBounds.min.x, 0f, worldBounds.min.z);
        var lanes = go.AddComponent<MonsterPathGrid>();
        var serialized = new SerializedObject(lanes);
        serialized.FindProperty("columns").intValue = Mathf.RoundToInt(worldBounds.size.x);
        serialized.FindProperty("rows").intValue = Mathf.RoundToInt(worldBounds.size.z);
        serialized.FindProperty("cellSize").floatValue = 1f;
        serialized.FindProperty("pathHeight").floatValue = 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        lanes.EnsureCellData();
        float entranceZ = instances["front-wall-0"].transform.position.z;
        float[] centers = { instances["enemy-spawn-left"].transform.position.x,
            instances["enemy-spawn-middle"].transform.position.x, instances["enemy-spawn-right"].transform.position.x };
        var rearApproaches = new[] { "left", "right" }.Select(side =>
            instances["route-cargo-upper-" + side + "-ramp-toe"].transform.position).ToArray();
        Vector3[] Approach(string wing) => new[] { "start", "shutter", "apron", "door" }.Select(node =>
            instances["route-cargo-" + wing + "-" + node].transform.position).ToArray();
        var westApproach = Approach("pump");
        var eastApproach = Approach("workshop");
        for (int y = 0; y < lanes.Rows; y++)
        for (int x = 0; x < lanes.Columns; x++)
        {
            var cell = new Vector2Int(x, y);
            Vector3 center = lanes.CellToWorld(cell);
            bool frontLane = center.z <= entranceZ && centers.Any(lane => Mathf.Abs(center.x - lane) < 6f);
            bool rearLane = rearApproaches.Any(toe => center.z >= toe.z && Mathf.Abs(center.x - toe.x) < 6f);
            bool wingLane = (center.x < slab.min.x && InWingApproach(center, westApproach)) ||
                (center.x > slab.max.x && InWingApproach(center, eastApproach));
            lanes.SetOpen(cell, frontLane || rearLane || wingLane);
        }
        return lanes;
    }

    private static bool InWingApproach(Vector3 point, Vector3[] approach)
    {
        const float halfWidth = 4f;
        for (int i = 1; i < approach.Length; i++)
        {
            Vector3 start = approach[i - 1];
            Vector3 delta = approach[i] - start;
            float along = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            if ((point - (start + along * delta)).sqrMagnitude < halfWidth * halfWidth) return true;
        }
        return false;
    }

    private static void CreateFacilityDefenseGrid(Transform parent, MonsterPathGrid lanes, MapForgeObjectProperties pad)
    {
        Bounds bounds = pad.GetComponentInChildren<Renderer>().bounds;
        float floor = bounds.min.y;
        var hits = Physics.RaycastAll(bounds.center + Vector3.up, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.normal.y > .99f && Mathf.Abs(hit.point.y - floor) < .04f).OrderBy(hit => hit.distance).ToArray();
        if (hits.Length == 0) throw new InvalidOperationException("No support under defense pad " + pad.objectId);
        Vector3 anchor = TrapGridAuthoring.GroundAnchor(lanes);
        Bounds? support = floor < .1f ? (Bounds?)null : hits[0].collider.bounds;
        bool[] mask = TrapGridAuthoring.BuildDefensePadMask(lanes, anchor, bounds, support);
        var go = new GameObject("TrapPlacementGrid_" + pad.objectId);
        go.transform.SetParent(parent, false);
        go.transform.position = anchor;
        var grid = go.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(lanes.Columns, lanes.Rows, lanes.CellSize, floor + TrapGridAuthoring.SurfaceOffset, mask,
            floor < .1f ? TrapPlacementGrid.SurfaceKind.Ground : TrapPlacementGrid.SurfaceKind.Platform);
        grid.PathGridOverride = lanes;
        // Reloaded grids discover the scene's single lane grid via ResolvePathGrid.
        if (floor > .1f) go.AddComponent<PlatformTrapGridLines>();
    }

    private static void CreateFacilityCeilingTrapPanels(Transform parent)
    {
        foreach (var item in FacilityCeilingPanels)
        {
            Transform existing = parent.Find(item.name);
            var panel = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = item.name;
            ConfigureFacilityCeilingPanel(panel, item.suffix, item.position, parent);
        }
    }

    [MenuItem("MapForge/Clean Legacy Facility Trap Grids")]
    public static void CleanLegacyFacilityTrapGrids()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before changing facility ceiling panels.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject world = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MapForgeWorldImporter.RootName);
        if (world == null) throw new InvalidOperationException("Facility scene has no MapForgeWorld root.");

        string backupDirectory = Path.Combine(OutputDirectory, "ceiling-trap-boards-20261121", "backup");
        Directory.CreateDirectory(backupDirectory);
        string sceneBackup = Path.Combine(backupDirectory, "DOUBLE_LAYER_FACILITY.before-ceiling-grid.unity");
        if (!File.Exists(sceneBackup)) File.Copy(ScenePath, sceneBackup);
        string metaBackup = sceneBackup + ".meta";
        if (File.Exists(ScenePath + ".meta") && !File.Exists(metaBackup)) File.Copy(ScenePath + ".meta", metaBackup);

        CreateFacilityCeilingTrapPanels(world.transform);

        var newPanelTransforms = FacilityCeilingPanels
            .Select(item => world.transform.Find(item.name))
            .Where(transform => transform != null).ToArray();
        var legacyGrids = world.GetComponentsInChildren<TrapPlacementGrid>(true)
            .Where(grid => grid.IsWallSurface || (grid.IsCeilingSurface
                && !newPanelTransforms.Any(panel => grid.transform.IsChildOf(panel))))
            .ToArray();
        var legacySupports = new HashSet<GameObject>();
        foreach (var grid in legacyGrids)
        {
            Collider support = grid.SupportCollider;
            if (support != null && IsLegacyTrapMountSupport(support.gameObject))
                legacySupports.Add(support.gameObject);
            UnityEngine.Object.DestroyImmediate(grid.gameObject);
        }

        foreach (Transform item in world.GetComponentsInChildren<Transform>(true))
        {
            if (newPanelTransforms.Any(panel => item == panel || item.IsChildOf(panel))) continue;
            if (item != world.transform && IsLegacyTrapMountSupport(item.gameObject))
                legacySupports.Add(item.gameObject);
        }
        foreach (var support in legacySupports)
            if (support != null) UnityEngine.Object.DestroyImmediate(support);

        foreach (var item in FacilityCeilingPanels)
        {
            var panel = world.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == item.name)?.gameObject;
            if (panel == null) throw new InvalidOperationException("Missing facility ceiling panel: " + item.name);
            ConfigureFacilityCeilingPanel(panel, item.suffix, item.position, world.transform);
        }

        Physics.SyncTransforms();
        var remainingGrids = world.GetComponentsInChildren<TrapPlacementGrid>(true);
        if (remainingGrids.Any(grid => grid.IsWallSurface))
            throw new InvalidOperationException("Legacy wall trap grids remain in the facility.");
        ValidateFacilityCeilingTrapPanels(remainingGrids.Where(grid => grid.IsCeilingSurface).ToArray());
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Could not save facility ceiling trap panels.");
        Debug.Log("FACILITY_LEGACY_GRIDS_CLEAN_PASS: removed " + legacyGrids.Length
            + " legacy wall/ceiling grids and " + legacySupports.Count + " support objects, retained four 8x8 ceiling boards.");
    }

    private static bool IsLegacyTrapMountSupport(GameObject candidate)
    {
        return candidate.name.StartsWith("Wall Trap Mount Plate", StringComparison.Ordinal)
            || candidate.name.StartsWith("Ceiling Trap Mount Plate", StringComparison.Ordinal)
            || candidate.name.StartsWith("Ceiling Panel Suspension", StringComparison.Ordinal);
    }

    private static void ConfigureFacilityCeilingPanel(GameObject panel, string suffix, Vector3 position,
        Transform suspensionParent)
    {
        float panelHeight = position.y;
        const float panelThickness = 0.12f;
        const float suspensionTop = 6.1f;
        panel.transform.SetParent(suspensionParent, false);
        panel.transform.position = position;
        panel.transform.rotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(8f, panelThickness, 8f);
        panel.SetActive(true);
        panel.isStatic = true;
        var support = panel.GetComponent<BoxCollider>() ?? panel.AddComponent<BoxCollider>();
        support.enabled = true;
        support.isTrigger = false;
        Material panelMaterial = LoadFacilityCeilingPanelMaterial();
        var panelRenderer = panel.GetComponent<Renderer>();
        if (panelRenderer != null) panelRenderer.sharedMaterial = panelMaterial;

        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            string hangerName = "Facility Ceiling Panel Suspension " + suffix + " " + x + " " + z;
            Transform existingHanger = suspensionParent.Find(hangerName);
            GameObject hanger = existingHanger != null ? existingHanger.gameObject
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            hanger.name = hangerName;
            hanger.transform.SetParent(suspensionParent, false);
            hanger.transform.position = new Vector3(panel.transform.position.x + x * 4.08f,
                (panelHeight + suspensionTop) * 0.5f, panel.transform.position.z + z * 4.08f);
            hanger.transform.rotation = Quaternion.identity;
            hanger.transform.localScale = new Vector3(0.12f, suspensionTop - panelHeight, 0.12f);
            hanger.isStatic = true;
            var hangerRenderer = hanger.GetComponent<Renderer>();
            if (hangerRenderer != null) hangerRenderer.sharedMaterial = panelMaterial;
        }

        string gridName = "FacilityCeilingTrapPlacementGrid_" + suffix;
        var grid = panel.GetComponentsInChildren<TrapPlacementGrid>(true).FirstOrDefault();
        if (grid == null)
        {
            var gridObject = new GameObject(gridName);
            gridObject.transform.SetParent(panel.transform, false);
            grid = gridObject.AddComponent<TrapPlacementGrid>();
        }
        grid.gameObject.name = gridName;
        var mapping = new MapForgeSceneOrganization.CeilingTrapGridMap
        {
            columns = 8,
            rows = 8,
            cellSize = 1f,
            surfaceOffset = 0.01f,
            autoFit = true,
            openCells = Enumerable.Repeat(true, 64).ToArray()
        };
        CeilingTrapGridAuthoring.Configure(grid, support, mapping);
    }

    private static Material LoadFacilityCeilingPanelMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FacilityCeilingPanelMaterialPath);
        if (material != null) return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP/Lit shader is unavailable for facility ceiling panels.");
        material = new Material(shader)
        {
            name = "Facility Ceiling Panel",
            color = new Color(0.34f, 0.39f, 0.42f)
        };
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.28f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.42f);
        AssetDatabase.CreateAsset(material, FacilityCeilingPanelMaterialPath);
        return material;
    }

    public static void RenderFacilityCeilingPanelPreview()
    {
        ValidateSavedScene();
        string previewDirectory = Path.Combine(OutputDirectory, "ceiling-trap-boards-20261121", "preview");
        Directory.CreateDirectory(previewDirectory);
        Camera camera = Camera.main;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        float fieldOfView = camera.fieldOfView;
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(1600, 1000, 24);
        try
        {
            camera.targetTexture = target;
            camera.fieldOfView = 72f;
            RenderShot(camera, target, new Vector3(-24f, 2.2f, -26f),
                new Vector3(-15.5f, 5.2f, -25f), "ceiling-panel-view.png", Vector3.up, previewDirectory);
            Debug.Log("FACILITY_CEILING_PREVIEW_PASS: " + previewDirectory);
        }
        finally
        {
            camera.targetTexture = null;
            camera.fieldOfView = fieldOfView;
            camera.transform.SetPositionAndRotation(position, rotation);
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void CollectFacilityRoute(string id, List<string> visiting,
        Dictionary<string, MapForgeSceneOrganization.Node> authored,
        Dictionary<string, MapForgeObjectProperties> instances, List<Transform[]> branches)
    {
        if (visiting.Contains(id) || !authored.TryGetValue(id, out var node) || !instances.ContainsKey(id))
            throw new InvalidOperationException("Invalid facility cargo route at " + id);
        visiting.Add(id);
        var links = node.unity.pathNode.links;
        if (links == null || links.Length == 0)
        {
            if (node.unity.pathNode.role != "end") throw new InvalidOperationException("Unterminated cargo route " + id);
            branches.Add(visiting.Select(key => instances[key].transform).ToArray());
        }
        else foreach (string next in links) CollectFacilityRoute(next, new List<string>(visiting), authored, instances, branches);
    }
}
