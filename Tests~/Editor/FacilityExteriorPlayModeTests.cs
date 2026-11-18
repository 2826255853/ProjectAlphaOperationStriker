#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class FacilityExteriorPlayModeTests
{
    private string previousScenePath;
    private float previousTimeScale = 1f;

    [UnitySetUp]
    public IEnumerator EnterFacility()
    {
        previousScenePath = SceneManager.GetActiveScene().path;
        previousTimeScale = Time.timeScale;
        EditorSceneManager.OpenScene("Assets/Scenes/DOUBLE_LAYER_FACILITY.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        foreach (var spawn in Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None))
            spawn.enabled = false;
        var menu = Object.FindAnyObjectByType<GameStartMenu>();
        if (menu != null) menu.BeginOrResume();
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator LeaveFacility()
    {
        if (!Application.isPlaying) yield break;
        Time.timeScale = 1f;
        yield return new ExitPlayMode();
        Time.timeScale = previousTimeScale;
        if (!string.IsNullOrEmpty(previousScenePath))
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
    }

    [UnityTest]
    public IEnumerator ExteriorDefenseAndFlightUseLiveFacility()
    {
        Assert.That(Application.isPlaying, Is.True);
        var core = Object.FindAnyObjectByType<EnemyCore>();
        Assert.That(core, Is.Not.Null);
        var grids = Object.FindObjectsByType<TrapPlacementGrid>(FindObjectsSortMode.None)
            .Where(grid => grid.name.Contains("exterior-")).ToArray();
        Assert.That(grids.Length, Is.EqualTo(10), "Eight exterior ground pads and two AA pads must be usable at runtime.");
        var launcher = Resources.Load<TrapDefinition>("DualMissileLauncherLoaded");
        Assert.That(launcher, Is.Not.Null, "The actual missile definition must be available in the player Resources.");
        var sentry = Resources.Load<TrapDefinition>("AutoSentryTurret");
        Assert.That(sentry, Is.Not.Null);
        foreach (var grid in grids)
        {
            bool antiAir = grid.name.Contains("exterior-aa-");
            TrapDefinition definition = antiAir ? launcher : sentry;
            Vector2Int origin = FindPlaceableOrigin(grid, definition);
            grid.GetPlacementPose(origin, definition, out Vector3 expected, out Quaternion rotation);
            Assert.That(Physics.RaycastAll(expected + Vector3.up * .6f, Vector3.down, 1f,
                ~0, QueryTriggerInteraction.Ignore).Any(hit => hit.normal.y > .9f &&
                Mathf.Abs(hit.point.y - expected.y) < .12f &&
                hit.collider.GetComponentInParent<MapForgeObjectProperties>() != null), Is.True,
                grid.name + " must have real authored floor support.");
            Assert.That(grid.TryPlaceTrap(origin, definition, out var trap, out string failure), Is.True,
                grid.name + ": " + failure);
            if (antiAir)
                Assert.That(trap.GetComponentInChildren<MissileLauncherWeapon>(true), Is.Not.Null,
                    "AA platform placement must instantiate the real missile launcher.");
            Assert.That(trap.Definition, Is.EqualTo(definition));
            Assert.That(grid.OwnsTrap(trap), Is.True);
            Assert.That(Vector3.Distance(trap.transform.position, expected), Is.LessThan(.01f));
            Assert.That(grid.RemoveTrap(trap), Is.True);
        }
        yield return null;

        var ground = Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None)
            .Where(spawn => !spawn.IsFlyingEntrance && spawn.GetComponent<AuthoredMonsterRoute>() != null)
            .OrderBy(spawn => spawn.transform.position.x).ToArray();
        Assert.That(ground.Length, Is.EqualTo(3));

        // Exercise the real placed weapons in the generated map, including their
        // target selection, aiming and damage. The short-range sentry placement
        // above alone does not prove the ground pads cover the cargo lanes.
        var artillery = Resources.Load<TrapDefinition>("XM777");
        Assert.That(artillery, Is.Not.Null);
        var artilleryGrid = grids.Single(grid => grid.name.EndsWith("exterior-defense-left-0"));
        Assert.That(artilleryGrid.TryPlaceTrap(FindPlaceableOrigin(artilleryGrid, artillery), artillery,
            out var placedArtillery, out string artilleryFailure), Is.True, artilleryFailure);
        Assert.That(placedArtillery.GetComponentInChildren<XM777Trap>(true), Is.Not.Null);
        var groundTarget = ground[1].SpawnEnemy();
        groundTarget.PathFollower.enabled = false;
        var targetCombat = groundTarget.GetComponent<GroundEnemyCombat>();
        if (targetCombat != null) targetCombat.enabled = false;
        groundTarget.transform.position = new Vector3(0f, .08f, -78f);
        Physics.SyncTransforms();
        float artilleryDamage = 0f;
        groundTarget.GetComponent<EnemyHealth>().TrapDamaged += (_, amount, type) => artilleryDamage += amount;
        float combatDeadline = Time.realtimeSinceStartup + 6f;
        while (artilleryDamage <= 0f && Time.realtimeSinceStartup < combatDeadline) yield return null;
        Assert.That(artilleryDamage, Is.GreaterThan(0f),
            "A real XM777 placed on the exterior ground pad must damage the central-lane target.");
        artilleryGrid.RemoveTrap(placedArtillery);
        if (groundTarget != null) Object.Destroy(groundTarget.gameObject);
        yield return null;

        var combatAirEntrance = Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None)
            .Single(spawn => spawn.IsFlyingEntrance);
        var airTarget = combatAirEntrance.SpawnEnemy();
        airTarget.PathFollower.enabled = false;
        airTarget.transform.position = new Vector3(0f, 16f, -64f);
        Physics.SyncTransforms();
        var aaGrid = grids.First(grid => grid.name.Contains("exterior-aa-left-"));
        Vector2Int aaOrigin = FindPlaceableOrigin(aaGrid, launcher, airTarget.transform.position);
        Assert.That(aaGrid.TryPlaceTrap(aaOrigin, launcher, out var placedLauncher, out string aaFailure), Is.True, aaFailure);
        float missileDamage = 0f;
        airTarget.GetComponent<EnemyHealth>().TrapDamaged += (_, amount, type) => missileDamage += amount;
        combatDeadline = Time.realtimeSinceStartup + 6f;
        while (missileDamage <= 0f && Time.realtimeSinceStartup < combatDeadline) yield return null;
        Assert.That(missileDamage, Is.GreaterThan(0f),
            "An inward AA launcher on the real platform must hit an airborne target over the central lane.");
        aaGrid.RemoveTrap(placedLauncher);
        if (airTarget != null) Object.Destroy(airTarget.gameObject);
        Debug.Log($"FACILITY_COMBAT_ACCEPTANCE_PASS: real exterior XM777 dealt {artilleryDamage:F2} damage to the central ground lane; " +
            $"real platform missile launcher dealt {missileDamage:F2} damage to the central air approach.");
        yield return null;

        var followers = new MonsterPathFollower[6];
        var arrived = new bool[6];
        var reachedUpperFloor = new bool[6];
        float initialCoreHealth = core.CurrentHealth;
        for (int lane = 0; lane < ground.Length; lane++)
        for (int branch = 0; branch < 2; branch++)
        {
            int index = lane * 2 + branch;
            var enemy = ground[lane].SpawnEnemy();
            Assert.That(enemy, Is.Not.Null);
            followers[index] = enemy.PathFollower;
            var combat = enemy.GetComponent<GroundEnemyCombat>();
            if (combat != null) combat.enabled = false;
            followers[index].ResumeMissionPath();
            Assert.That(followers[index].Waypoints.Any(point => point.y > 6.3f), Is.True,
                "Spawned enemies must receive the source's multi-storey route.");
            followers[index].Arrived += follower =>
            {
                arrived[index] = true;
                reachedUpperFloor[index] |= follower.transform.position.y > 6.1f;
            };
        }

        Time.timeScale = 20f;
        float timeout = Time.realtimeSinceStartup + 45f;
        while (arrived.Any(value => !value) && Time.realtimeSinceStartup < timeout)
        {
            for (int i = 0; i < followers.Length; i++)
                if (followers[i] != null)
                    reachedUpperFloor[i] |= followers[i].transform.position.y > 6.1f;
            yield return null;
        }
        Assert.That(arrived.All(value => value), Is.True, "All six authored cargo branches must reach the core in runtime Update.");
        Assert.That(reachedUpperFloor.All(value => value), Is.True, "Every ground enemy must actually climb to the second floor.");
        Assert.That(core.CurrentHealth, Is.EqualTo(initialCoreHealth - 12f).Within(.01f),
            "Six live ground leaks must each damage the real core.");

        var airEntrance = Object.FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None)
            .Single(spawn => spawn.IsFlyingEntrance);
        Assert.That(airEntrance.transform.position.z, Is.EqualTo(-136f).Within(.01f));
        Assert.That(airEntrance.MoveSpeed, Is.EqualTo(2.2f).Within(.01f));
        Assert.That(core.Position.y + airEntrance.FlightHeight, Is.EqualTo(16f).Within(.01f));
        Vector3 airPosition = airEntrance.transform.position;
        airPosition.y = Mathf.Max(airPosition.y, airEntrance.EntranceAltitude);
        Assert.That(airPosition.y, Is.EqualTo(16f).Within(.01f));
        var oldFlight = CreateFlightProbe("Old flight baseline", new Vector3(0f, 16f, 48f), core,
            airEntrance.MoveSpeed, airEntrance.FlightHeight);
        var newFlight = CreateFlightProbe("Exterior flight approach", airPosition, core,
            airEntrance.MoveSpeed, airEntrance.FlightHeight);
        float started = Time.time;
        float oldSeconds = -1f, newSeconds = -1f;
        oldFlight.Arrived += _ => oldSeconds = Time.time - started;
        newFlight.Arrived += _ => newSeconds = Time.time - started;
        timeout = Time.realtimeSinceStartup + 30f;
        while ((oldSeconds < 0f || newSeconds < 0f) && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(oldSeconds, Is.GreaterThan(0f));
        Assert.That(newSeconds, Is.GreaterThan(0f));
        Assert.That(oldSeconds, Is.EqualTo(30f).Within(1.5f));
        Assert.That(newSeconds, Is.EqualTo(118f / 2.2f).Within(1.5f));
        Assert.That(newSeconds / oldSeconds, Is.InRange(1.7f, 1.9f));
        Debug.Log($"FACILITY_RUNTIME_ACCEPTANCE_PASS: placed real defenses on {grids.Length} exterior grids, including both AA missile launchers; " +
            $"six live ground routes climbed to 2F and damaged the core; old flight {oldSeconds:F2}s, " +
            $"new flight {newSeconds:F2}s, ratio {newSeconds / oldSeconds:F3} at 2.2m/s.");
        Object.Destroy(oldFlight.gameObject);
        Object.Destroy(newFlight.gameObject);
        Time.timeScale = 1f;
    }

    private static Vector2Int FindPlaceableOrigin(TrapPlacementGrid grid, TrapDefinition definition,
        Vector3? nearestTo = null)
    {
        Vector2Int best = default;
        float bestDistance = float.PositiveInfinity;
        for (int y = 0; y < grid.Rows; y++)
        for (int x = 0; x < grid.Columns; x++)
        {
            var cell = new Vector2Int(x, y);
            if (!grid.CanPlaceTrap(cell, definition, out _)) continue;
            if (!nearestTo.HasValue) return cell;
            grid.GetPlacementPose(cell, definition, out Vector3 position, out Quaternion rotation);
            float distance = (position - nearestTo.Value).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = cell;
        }
        if (!float.IsPositiveInfinity(bestDistance)) return best;
        Assert.Fail("No real " + definition.DisplayName + " footprint fits " + grid.name);
        return default;
    }

    private static MonsterPathFollower CreateFlightProbe(string name, Vector3 position, EnemyCore core,
        float speed, float flightHeight)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var follower = go.AddComponent<MonsterPathFollower>();
        follower.InitializeFlying(core.transform, core.Position, speed, flightHeight, core);
        return follower;
    }
}
#endif
