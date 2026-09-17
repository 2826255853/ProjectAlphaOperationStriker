#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GroundEnemyCombatTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private MonsterPathGrid grid;
    private MonsterPathFollower follower;
    private GroundEnemyCombat combat;
    private PlayerHealth player;
    private TrapInstance trap;

    [SetUp]
    public void SetUp()
    {
        grid = Create("Road", Vector3.zero).AddComponent<MonsterPathGrid>();
        for (int x = 1; x < 15; x++) grid.SetOpen(new Vector2Int(x, 1), true);
        GameObject enemy = Create("Enemy", new Vector3(1.5f, 0f, 1.5f));
        follower = enemy.AddComponent<MonsterPathFollower>();
        combat = enemy.AddComponent<GroundEnemyCombat>();
        Invoke(enemy.GetComponent<EnemyHealth>(), "Awake");
        Invoke(combat, "Awake");
        follower.Initialize(grid, null, new Vector3(14.5f, 0f, 1.5f), Vector3.right, 2f);
        player = Create("Player", new Vector3(5.5f, 0f, 1.5f)).AddComponent<PlayerHealth>();
        Invoke(player, "Awake");
        Invoke(player, "OnEnable");
        trap = Create("Trap", new Vector3(2.5f, 0f, 1.5f)).AddComponent<TrapInstance>();
        Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject obj in objects)
        {
            if (obj == null) continue;
            foreach (TrapInstance instance in obj.GetComponentsInChildren<TrapInstance>()) Invoke(instance, "OnDisable");
            PlayerHealth health = obj.GetComponent<PlayerHealth>();
            if (health != null) Invoke(health, "OnDisable");
        }
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void NearbyReachablePlayerOverridesCloserTrapAndReceivesMeleeDamage()
    {
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(player.transform));
        Assert.That(follower.IsFollowingCombatPath, Is.True);
        Assert.That(player.CurrentHealth, Is.EqualTo(100f), "No damage outside melee range.");
        player.transform.position = new Vector3(2.5f, 0f, 1.5f);
        Tick();
        Assert.That(player.CurrentHealth, Is.EqualTo(90f));
        Assert.That(trap.CurrentHealth, Is.EqualTo(100f));
        Assert.That(follower.CombatMovementPaused, Is.True);
        Tick();
        Assert.That(player.CurrentHealth, Is.EqualTo(90f), "Target refresh must not reset the attack cooldown.");
    }

    [Test]
    public void DetectionAndDisengageRangesHaveHysteresis()
    {
        player.transform.position = new Vector3(10.5f, 0f, 1.5f);
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
        player.transform.position = new Vector3(5.5f, 0f, 1.5f);
        Tick();
        player.transform.position = new Vector3(10.5f, 0f, 1.5f);
        Tick();
        Assert.That(combat.IsTargetingPlayer, Is.True, "A pursued player remains a target outside detection range.");
        player.transform.position = new Vector3(14.5f, 0f, 1.5f);
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
    }

    [TestCase(5.5f, 0f, 2.5f)]
    [TestCase(5.5f, 2f, 1.5f)]
    [TestCase(20f, 0f, 1.5f)]
    public void PlayerOnClosedCellHighPlatformOrOutsideGridIsRejected(float x, float y, float z)
    {
        Tick();
        player.transform.position = new Vector3(x, y, z);
        Tick(false); // Invalidation must work before the next periodic scan.
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void DisconnectedOpenRoadDoesNotQualifyForPlayerPursuit()
    {
        grid.SetOpen(new Vector2Int(3, 1), false);
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
        Assert.That(player.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void RoadsideTrapCanBeAttackedFromOpenRoad()
    {
        player.transform.position = new Vector3(20f, 0f, 1.5f);
        trap.transform.position = new Vector3(1.5f, 0f, 2.5f);
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
        Assert.That(trap.CurrentHealth, Is.EqualTo(90f));
        foreach (Vector3 point in follower.Waypoints)
        {
            Assert.That(grid.TryWorldToCell(point, out Vector2Int cell), Is.True);
            Assert.That(grid.IsOpen(cell), Is.True);
        }
    }

    [Test]
    public void DeadOrDisabledTargetsAreDropped()
    {
        Tick();
        player.TakeDamage(100f);
        Tick(false);
        Assert.That(combat.CurrentTarget, Is.EqualTo(trap.transform));
        trap.enabled = false;
        Tick(false);
        Assert.That(combat.CurrentTarget, Is.Null);
        Assert.That(follower.IsFollowingCombatPath, Is.False);
        Assert.That(follower.DestinationPosition, Is.EqualTo(new Vector3(14.5f, 0f, 1.5f)));
    }

    [Test]
    public void CombatDoesNotCompleteRequiredWaypointOrDamageCoreAndResumesMission()
    {
        EnemyCore core = Create("Core", new Vector3(14.5f, 0f, 1.5f)).AddComponent<EnemyCore>();
        Invoke(core, "Awake");
        Vector3 waypoint = new Vector3(8.5f, 0f, 1.5f);
        follower.InitializeViaWaypoint(grid, null, waypoint, Vector3.right, 2f, core);
        int arrivals = 0;
        follower.Arrived += _ => arrivals++;
        Tick();
        follower.CombatMovementPaused = false;
        List<Vector3> combatPath = new List<Vector3>(follower.Waypoints);
        foreach (Vector3 point in combatPath)
        {
            follower.transform.position = point;
            Invoke(follower, "Update");
        }
        Assert.That(arrivals, Is.Zero);
        Assert.That(follower.HasArrived, Is.False);
        Assert.That(follower.IsTravellingToRequiredWaypoint, Is.True);
        Assert.That(core.CurrentHealth, Is.EqualTo(core.MaxHealth));

        player.transform.position = new Vector3(30f, 0f, 1.5f);
        trap.enabled = false;
        Tick();
        Assert.That(follower.IsFollowingCombatPath, Is.False);
        Assert.That(follower.IsTravellingToRequiredWaypoint, Is.True);
        Assert.That(follower.DestinationPosition, Is.EqualTo(waypoint));
        List<Vector3> missionPath = new List<Vector3>(follower.Waypoints);
        foreach (Vector3 point in missionPath)
        {
            follower.transform.position = point;
            Invoke(follower, "Update");
        }
        Assert.That(follower.IsTravellingToRequiredWaypoint, Is.False);
        Assert.That(follower.DestinationPosition, Is.EqualTo(core.Position));
    }

    [Test]
    public void FlyingEnemyIgnoresGroundCombat()
    {
        follower.InitializeFlying(null, new Vector3(14.5f, 0f, 1.5f), 2f, 3f, null);
        Tick();
        Assert.That(combat.CurrentTarget, Is.Null);
        Assert.That(follower.IsFollowingCombatPath, Is.False);
        Assert.That(follower.IsFlying, Is.True);
    }

    [Test]
    public void SpawnPointAutomaticallyAddsCombatOnlyForGroundEnemies()
    {
        EnemySpawnPoint spawn = Create("Spawn", follower.transform.position).AddComponent<EnemySpawnPoint>();
        spawn.PathGrid = grid;
        spawn.TargetPosition = new Vector3(14.5f, 0f, 1.5f);
        spawn.MonsterType = MonsterType.Ground;
        EnemyInstance ground = spawn.SpawnEnemy();
        objects.Add(ground.gameObject);
        Assert.That(ground.GetComponent<GroundEnemyCombat>(), Is.Not.Null);
        Assert.That(ground.PathFollower.IsInitialized, Is.True);
        Assert.That(ground.PathFollower.IsFlying, Is.False);
        spawn.MonsterType = MonsterType.Flying;
        EnemyInstance flying = spawn.SpawnEnemy();
        objects.Add(flying.gameObject);
        Assert.That(flying.GetComponent<GroundEnemyCombat>(), Is.Null);
        Assert.That(flying.PathFollower.IsFlying, Is.True);
    }

    [Test]
    public void PlayerRoadCheckUsesCharacterControllerFeetInsteadOfElevatedRoot()
    {
        GameObject playerRoot = Create("Player with centered pivot", new Vector3(2.5f, 0.9f, 1.5f));
        CharacterController controller = playerRoot.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.center = Vector3.zero;
        PlayerHealth centeredPlayer = playerRoot.AddComponent<PlayerHealth>();
        Invoke(centeredPlayer, "Awake");
        Invoke(centeredPlayer, "OnEnable");
        Physics.SyncTransforms();
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(centeredPlayer.transform));
        Assert.That(centeredPlayer.CurrentHealth, Is.EqualTo(90f));
    }

    [Test]
    public void SameCellChaseMovesTowardPlayerWithoutDetouringToCellCenter()
    {
        follower.transform.position = new Vector3(1.1f, 0f, 1.1f);
        player.transform.position = new Vector3(1.2f, 0f, 1.1f);
        Tick();
        Assert.That(follower.Waypoints.Count, Is.EqualTo(1));
        Assert.That(follower.Waypoints[0], Is.EqualTo(player.FeetPosition));
    }

    [Test]
    public void DestroyedTrapReleasesPlacementForReplacement()
    {
        TrapPlacementGrid placement = Create("Placement", Vector3.zero).AddComponent<TrapPlacementGrid>();
        placement.ConfigureLayout(2, 2, 1f, 0f, new[] { true, true, true, true });
        TrapDefinition definition = ScriptableObject.CreateInstance<TrapDefinition>();
        try
        {
            Assert.That(placement.TryPlaceTrap(Vector2Int.zero, definition, out TrapInstance placed, out _), Is.True);
            Invoke(placed, "Awake");
            placed.TakeDamage(100f);
            Assert.That(placement.IsOccupied(Vector2Int.zero), Is.False);
            Assert.That(placement.PlacedTraps.Count, Is.Zero);
            Assert.That(placement.TryPlaceTrap(Vector2Int.zero, definition, out _, out _), Is.True);
        }
        finally { Object.DestroyImmediate(definition); }
    }

    private void Tick(bool refresh = true)
    {
        if (refresh) SetField(combat, "nextRepathTime", 0f);
        Invoke(combat, "Update");
    }

    private GameObject Create(string name, Vector3 position)
    {
        GameObject obj = new GameObject(name);
        obj.transform.position = position;
        objects.Add(obj);
        return obj;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
#endif
