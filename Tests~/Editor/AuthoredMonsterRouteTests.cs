#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class AuthoredMonsterRouteTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private static readonly MethodInfo TickMethod = typeof(MonsterPathFollower)
        .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void StartingBelowCoreStillRequiresTheCargoRoute()
    {
        var core = Core(new Vector3(0f, 6.4f, 0f));
        var follower = Follower(Vector3.zero);
        follower.InitializeAuthoredRoute(new[] { new Vector3(0f, 0f, 10f),
            new Vector3(0f, 6.4f, 10f), core.Position }, 0f, core);

        Tick(follower);

        Assert.That(follower.IsInitialized, Is.True);
        Assert.That(follower.HasCoreTarget, Is.True);
        Assert.That(follower.IsFlying, Is.False);
        Assert.That(follower.HasArrived, Is.False,
            "The ground entrance passes beneath the upper core before taking the cargo ramp.");
    }

    [Test]
    public void CrossingBelowCoreMidRouteDoesNotLeak()
    {
        var core = Core(new Vector3(0f, 6.4f, 0f));
        var follower = Follower(new Vector3(0f, 0f, -10f));
        follower.InitializeAuthoredRoute(new[] { Vector3.zero, new Vector3(0f, 0f, 10f),
            new Vector3(0f, 6.4f, 10f), core.Position }, 0f, core);

        follower.transform.position = Vector3.zero;
        Tick(follower);

        Assert.That(follower.HasArrived, Is.False);
        Assert.That(follower.RemainingPathDistance, Is.GreaterThan(20f));
    }

    [Test]
    public void RemainingDistanceIncludesTheRampRise()
    {
        var core = Core(new Vector3(0f, 3f, 10f));
        var follower = Follower(Vector3.zero);
        follower.InitializeAuthoredRoute(new[] { new Vector3(0f, 3f, 0f), core.Position }, 0f, core);

        Assert.That(follower.RemainingPathDistance, Is.EqualTo(13f).Within(.001f));
    }

    [Test]
    public void VerticalWaypointCannotBeSkippedByHorizontalArrival()
    {
        var core = Core(new Vector3(0f, 3f, 10f));
        var follower = Follower(Vector3.zero);
        follower.InitializeAuthoredRoute(new[] { new Vector3(0f, 3f, 0f), core.Position }, 0f, core);
        Tick(follower);
        follower.transform.position = core.Position;

        Tick(follower);

        Assert.That(follower.HasArrived, Is.False,
            "The route must still visit its first elevated waypoint before reaching the core.");
    }

    [Test]
    public void ArrivalIsRaisedOnceAfterEveryAuthoredWaypoint()
    {
        var core = Core(new Vector3(0f, 6.4f, 0f));
        var follower = Follower(new Vector3(0f, 0f, -10f));
        var route = new[] { new Vector3(0f, 0f, 10f), new Vector3(0f, 6.4f, 10f), core.Position };
        int arrivals = 0;
        follower.Arrived += _ => arrivals++;
        follower.InitializeAuthoredRoute(route, 0f, core);
        for (int i = 0; i < route.Length; i++)
        {
            follower.transform.position = route[i];
            Tick(follower);
            Assert.That(follower.HasArrived, Is.EqualTo(i == route.Length - 1));
        }
        Tick(follower);
        follower.RebuildPath();
        Tick(follower);

        Assert.That(arrivals, Is.EqualTo(1));
    }

    [Test]
    public void ResumingAfterCombatKeepsUnvisitedUpperRoute()
    {
        var core = Core(new Vector3(0f, 6.4f, 0f));
        var follower = Follower(new Vector3(0f, 0f, -10f));
        var rampBottom = new Vector3(0f, 0f, 10f);
        var rampTop = new Vector3(0f, 6.4f, 20f);
        follower.InitializeAuthoredRoute(new[] { rampBottom, rampTop, core.Position }, 0f, core);
        follower.transform.position = rampBottom;
        Tick(follower);
        var combatPoint = new Vector3(2f, 0f, 10f);
        follower.FollowCombatPath(new[] { combatPoint });
        follower.transform.position = combatPoint;
        follower.ResumeMissionPath();

        Assert.That(follower.IsFollowingCombatPath, Is.False);
        Assert.That(follower.HasArrived, Is.False);
        float remaining = Vector3.Distance(combatPoint, rampTop) + Vector3.Distance(rampTop, core.Position);
        Assert.That(follower.RemainingPathDistance, Is.EqualTo(remaining).Within(.001f),
            "An enemy returning from combat should continue up the ramp rather than restart the route.");
    }

    [Test]
    public void ExistingDirectGroundModeKeepsItsHorizontalContract()
    {
        var core = Core(new Vector3(0f, 6.4f, 0f));
        var follower = Follower(Vector3.zero);

        follower.Initialize(null, core.transform, core.Position, Vector3.forward, 0f, core);

        Assert.That(follower.HasArrived, Is.True,
            "Authored cargo movement is opt-in and must preserve existing map arrival behavior.");
    }

    private MonsterPathFollower Follower(Vector3 position)
    {
        var go = Create("Route follower");
        go.transform.position = position;
        return go.AddComponent<MonsterPathFollower>();
    }

    private EnemyCore Core(Vector3 position)
    {
        var go = Create("Route core");
        go.transform.position = position;
        return go.AddComponent<EnemyCore>();
    }

    private GameObject Create(string name)
    {
        var go = new GameObject(name);
        objects.Add(go);
        return go;
    }

    private static void Tick(MonsterPathFollower follower) => TickMethod.Invoke(follower, null);
}
#endif
