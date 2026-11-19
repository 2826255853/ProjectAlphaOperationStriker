using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

public static partial class DoubleLayerFacilityAuthoring
{
    private const string PumpSpawnId = "enemy-spawn-pump";
    private const string WorkshopSpawnId = "enemy-spawn-workshop";

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

        var groundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaskedSoldierAuthoring.PrefabPath);
        var flyingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlyingSpawnPointAuthoring.FlyingPrefabPath);
        if (groundPrefab == null || flyingPrefab == null)
            throw new InvalidOperationException("Facility requires the existing MaskedSoldier and FlyingMonster prefabs.");
        var authored = document.objects.Where(node => node.id.StartsWith("route-cargo-", StringComparison.Ordinal))
            .ToDictionary(node => node.id);

        foreach (var node in document.objects.Where(node => node.unity?.spawnPoint != null && node.unity.spawnPoint.enabled))
        {
            var mapping = node.unity.spawnPoint;
            bool flying = mapping.flyingEntrance;
            var entrance = instances[node.id].gameObject.AddComponent<EnemySpawnPoint>();
            entrance.Core = core;
            entrance.PathGrid = flying ? null : lanes;
            entrance.EnemyPrefab = flying ? flyingPrefab : groundPrefab;
            entrance.MonsterType = flying ? MonsterType.Flying : MonsterType.Ground;
            entrance.IsFlyingEntrance = flying;
            entrance.EntranceAltitude = mapping.entranceAltitude;
            entrance.FlightHeight = mapping.flightHeight;
            entrance.FlyingSpawnSpread = 2f;
            entrance.MoveSpeed = mapping.moveSpeed;
            entrance.SpawnInterval = mapping.spawnInterval;
            // TODO(确认): 首版外场给30秒布防、波间45秒；后续按实战调整源图波次配置。
            entrance.InitialWaveDelay = Mathf.Max(30f, mapping.initialDelay);
            var direction = mapping.travelDirection;
            entrance.TravelDirection = direction == null ? Vector3.forward
                : new Vector3(direction[0], direction[1], direction[2]);
            var serialized = new SerializedObject(entrance);
            var waves = serialized.FindProperty("waveSpawnSettings");
            waves.arraySize = 3;
            int[] counts = flying ? new[] { 2, 3, 4 } : new[] { 3, 4, 2 };
            for (int i = 0; i < 3; i++)
            {
                var wave = waves.GetArrayElementAtIndex(i);
                bool hasSource = mapping.waves != null && i < mapping.waves.Length;
                wave.FindPropertyRelative("enabled").boolValue = !hasSource || mapping.waves[i].enabled;
                wave.FindPropertyRelative("enemyCount").intValue = hasSource ? mapping.waves[i].enemyCount : counts[i];
                wave.FindPropertyRelative("enemyPrefab").objectReferenceValue = entrance.EnemyPrefab;
                wave.FindPropertyRelative("monsterType").enumValueIndex = (int)entrance.MonsterType;
                wave.FindPropertyRelative("flightHeight").floatValue = mapping.flightHeight;
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
        var pads = world.GetComponentsInChildren<MapForgeObjectProperties>(true)
            .Where(item => item.tags != null && item.tags.Contains("defense-reservation")).ToArray();
        if (cores.Length != 1 || entrances.Length != 8 || entrances.Count(item => item.IsFlyingEntrance) != 1)
            throw new InvalidOperationException("Facility requires one core, seven ground entrances and one flying entrance.");
        if (grids.Length != pads.Length || grids.Length < 27)
            throw new InvalidOperationException("Every facility defense reservation requires a usable placement grid.");
        int exteriorCells = 0, rearCells = 0, wingCells = 0, allCells = 0;
        var lanes = world.GetComponentInChildren<MonsterPathGrid>();
        foreach (var grid in grids)
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
        var air = entrances.Single(item => item.IsFlyingEntrance);
        float distance = Vector2.Distance(new Vector2(air.transform.position.x, air.transform.position.z),
            new Vector2(cores[0].Position.x, cores[0].Position.z));
        if (distance < 115f || air.MoveSpeed <= 0f)
            throw new InvalidOperationException("Flying entrance must provide at least 115 m of approach.");
        var launcherDefinition = Resources.Load<TrapDefinition>("DualMissileLauncherLoaded");
        var launcher = launcherDefinition.Prefab.GetComponentInChildren<MissileLauncherTurret>(true);
        float flightAltitude = cores[0].Position.y + air.FlightHeight;
        foreach (var pad in pads.Where(item => item.objectId.StartsWith("exterior-aa-", StringComparison.Ordinal)))
        {
            Bounds bounds = pad.GetComponentInChildren<Renderer>().bounds;
            Vector3 installation = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 crossing = new Vector3(cores[0].Position.x, flightAltitude, bounds.center.z);
            if (launcher == null || Vector3.Distance(installation, crossing) >= launcher.DetectionRange)
                throw new InvalidOperationException("AA platform center is outside the real launcher's flight coverage: " + pad.objectId);
        }
        var sentry = Resources.Load<TrapDefinition>("AutoSentryTurret").Prefab.GetComponentInChildren<AutoSentryTurret>(true);
        Debug.Log($"FACILITY_WEAPON_COVERAGE_PASS: platform centers cover the flight corridor with the real launcher range " +
            $"{launcher.DetectionRange:F1}m; current sentry prefab range {sentry.DetectionRange:F1}m.");
        Debug.Log($"FACILITY_GAMEPLAY_PASS: {entrances.Length} entrances, {grids.Length} placement grids, " +
            $"{exteriorCells} front yard / {rearCells} rear yard / {wingCells} side wings / {allCells} total usable 1m cells; flying horizontal approach {distance:F2}m, " +
            $"{distance / air.MoveSpeed:F2}s at {air.MoveSpeed:F2}m/s. Ground missions use authored ramp heights.");
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
        bool[] mask;
        if (floor < .1f)
            mask = TrapGridAuthoring.BuildGroundMask(lanes, anchor, Quaternion.identity, null, out _);
        else
            mask = TrapGridAuthoring.BuildPlatformMask(lanes, anchor, Quaternion.identity,
                new List<Collider> { hits[0].collider }, out _, out _);
        if (mask == null) throw new InvalidOperationException("No placement cells on defense pad " + pad.objectId);
        for (int y = 0; y < lanes.Rows; y++)
        for (int x = 0; x < lanes.Columns; x++)
        {
            Vector3 center = TrapGridAuthoring.CellCenter(lanes, anchor, Quaternion.identity, x, y);
            mask[x + y * lanes.Columns] &= center.x - .5f >= bounds.min.x - .001f && center.x + .5f <= bounds.max.x + .001f
                && center.z - .5f >= bounds.min.z - .001f && center.z + .5f <= bounds.max.z + .001f;
        }
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
