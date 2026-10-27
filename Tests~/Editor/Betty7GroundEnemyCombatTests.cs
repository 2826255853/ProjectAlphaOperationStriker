#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class Betty7GroundEnemyCombatTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private MonsterPathFollower follower;
    private GroundEnemyCombat combat;
    private TrapInstance betty;
    private static readonly Vector3 Destination = new Vector3(14.5f, 0f, 1.5f);

    [SetUp]
    public void SetUp()
    {
        var grid = Create("Road", Vector3.zero).AddComponent<MonsterPathGrid>();
        for (int x = 1; x < 15; x++) grid.SetOpen(new Vector2Int(x, 1), true);
        GameObject enemy = Create("Enemy", new Vector3(1.5f, 0f, 1.5f));
        follower = enemy.AddComponent<MonsterPathFollower>();
        combat = enemy.AddComponent<GroundEnemyCombat>();
        Invoke(enemy.GetComponent<EnemyHealth>(), "Awake");
        Invoke(combat, "Awake");
        follower.Initialize(grid, null, Destination, Vector3.right, 2f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Betty7TrapAuthoring.PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        GameObject root = Object.Instantiate(prefab);
        objects.Add(root);
        root.transform.position = new Vector3(2.5f, 0f, 1.5f);
        betty = root.GetComponent<TrapInstance>();
        Assert.That(root.GetComponent<Betty7JumpMineTrap>(), Is.Not.Null);
        Invoke(betty, "Awake");
        Invoke(betty, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject obj in objects)
            if (obj != null && obj.TryGetComponent(out TrapInstance trap)) Invoke(trap, "OnDisable");
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void MonsterIgnoresRealBettyPrefabEvenWhenStandingOnIt()
    {
        float health = betty.CurrentHealth;
        foreach (Vector3 position in new[] { follower.transform.position, betty.transform.position,
            betty.transform.position + Vector3.right })
        {
            follower.transform.position = position;
            Tick();
            Assert.That(combat.CurrentTarget, Is.Null);
            Assert.That(combat.IsAttacking, Is.False);
            Assert.That(follower.IsFollowingCombatPath, Is.False);
            Assert.That(follower.CombatMovementPaused, Is.False);
            Assert.That(follower.DestinationPosition, Is.EqualTo(Destination));
            Assert.That(betty.CurrentHealth, Is.EqualTo(health));
        }
    }

    [Test]
    public void IgnoringBettyStillAllowsAttackingOrdinaryTrap()
    {
        var ordinary = Create("Ordinary trap", new Vector3(2.7f, 0f, 1.5f)).AddComponent<TrapInstance>();
        Invoke(ordinary, "Awake");
        Invoke(ordinary, "OnEnable");
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(ordinary.transform));
        Assert.That(combat.IsAttacking, Is.True);
        Assert.That(ordinary.CurrentHealth, Is.EqualTo(90f));
        Assert.That(betty.CurrentHealth, Is.EqualTo(betty.MaxHealth));
    }

    [Test]
    public void ExistingBettyTargetIsDroppedBeforeNextScheduledScan()
    {
        SetField("allowAttackGroundSpikes", true);
        Tick();
        Assert.That(combat.CurrentTarget, Is.EqualTo(betty.transform));
        Assert.That(follower.CombatMovementPaused, Is.True);
        float health = betty.CurrentHealth;

        SetField("allowAttackGroundSpikes", false);
        SetField("nextRepathTime", float.PositiveInfinity);
        SetField("nextAttackTime", 0f);
        Invoke(combat, "Update");
        Assert.That(combat.CurrentTarget, Is.Null);
        Assert.That(combat.IsAttacking, Is.False);
        Assert.That(follower.IsFollowingCombatPath, Is.False);
        Assert.That(follower.CombatMovementPaused, Is.False);
        Assert.That(follower.DestinationPosition, Is.EqualTo(Destination));
        Assert.That(betty.CurrentHealth, Is.EqualTo(health));
    }

    private void Tick()
    {
        SetField("nextRepathTime", 0f);
        Invoke(combat, "Update");
    }

    private GameObject Create(string name, Vector3 position)
    {
        var obj = new GameObject(name);
        objects.Add(obj);
        obj.transform.position = position;
        return obj;
    }

    private void SetField(string name, object value) =>
        typeof(GroundEnemyCombat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(combat, value);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
#endif
