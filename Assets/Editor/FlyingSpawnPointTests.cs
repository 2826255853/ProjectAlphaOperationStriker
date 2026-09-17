#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>Covers the dedicated airborne entrance behaviour.</summary>
public sealed class FlyingSpawnPointTests
{
    private readonly List<GameObject> objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void FlyingEntranceSpawnsAirborneMonsters()
    {
        EnemySpawnPoint spawnPoint = CreateSpawnPoint(flyingEntrance: true, altitude: 14f, spread: 0f);
        EnemyInstance instance = spawnPoint.SpawnEnemy();

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.MonsterType, Is.EqualTo(MonsterType.Flying));
        Assert.That(instance.PathFollower, Is.Not.Null);
        Assert.That(instance.PathFollower.IsFlying, Is.True, "Flying entrance must ignore the ground grid.");
        Assert.That(instance.transform.position.y, Is.EqualTo(14f).Within(0.01f), "Monsters must spawn in the air.");
        Assert.That(instance.GetComponent<GroundEnemyCombat>(), Is.Null, "Airborne monsters must not use melee combat.");
    }

    [Test]
    public void FlyingEntranceLiftsItselfAboveGround()
    {
        EnemySpawnPoint spawnPoint = CreateSpawnPoint(flyingEntrance: true, altitude: 12f, spread: 0f);
        Assert.That(spawnPoint.transform.position.y, Is.EqualTo(12f).Within(0.01f));
        Assert.That(spawnPoint.IsFlyingEntrance, Is.True);
    }

    [Test]
    public void RegularEntranceKeepsGroundBehaviour()
    {
        EnemySpawnPoint spawnPoint = CreateSpawnPoint(flyingEntrance: false, altitude: 12f, spread: 0f);
        EnemyInstance instance = spawnPoint.SpawnEnemy();

        Assert.That(instance.MonsterType, Is.EqualTo(MonsterType.Ground));
        Assert.That(instance.PathFollower.IsFlying, Is.False);
        Assert.That(spawnPoint.transform.position.y, Is.EqualTo(0f).Within(0.01f), "Ground entrances must stay on the ground.");
    }

    private EnemySpawnPoint CreateSpawnPoint(bool flyingEntrance, float altitude, float spread)
    {
        var spawnObject = new GameObject(flyingEntrance ? "Flying Spawn Test" : "Ground Spawn Test");
        objects.Add(spawnObject);
        spawnObject.transform.position = Vector3.zero;
        EnemySpawnPoint spawnPoint = spawnObject.AddComponent<EnemySpawnPoint>();
        Invoke(spawnPoint, "Awake");

        var serialized = new UnityEditor.SerializedObject(spawnPoint);
        serialized.FindProperty("flyingEntrance").boolValue = flyingEntrance;
        serialized.FindProperty("entranceAltitude").floatValue = altitude;
        serialized.FindProperty("flyingSpawnSpread").floatValue = spread;
        serialized.FindProperty("spawnInterval").floatValue = 1f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // Awake already ran, so re-apply the authored altitude the same way Awake does.
        if (flyingEntrance) Invoke(spawnPoint, "LiftEntranceIntoAir");
        return spawnPoint;
    }

    private static void Invoke(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName}");
        method.Invoke(target, null);
    }
}
#endif
