#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerHealthTests
{
    private GameObject playerObject;
    private PlayerHealth health;
    private CharacterController characterController;
    private FPSPackagePlayerMotion playerMotion;
    private FPSHitscanShooter shooter;
    private PlayerObserverMode observerMode;
    private PlayerLifeStateController lifeStateController;
    private GameObject respawnMarker;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("Player health test");
        characterController = playerObject.AddComponent<CharacterController>();
        health = playerObject.AddComponent<PlayerHealth>();
        Invoke(health, "Awake");
        Invoke(health, "OnEnable");
        playerMotion = playerObject.AddComponent<FPSPackagePlayerMotion>();
        Invoke(playerMotion, "Awake");
        shooter = playerObject.AddComponent<FPSHitscanShooter>();
        Invoke(shooter, "Awake");
        observerMode = playerObject.AddComponent<PlayerObserverMode>();
        Invoke(observerMode, "Awake");
        lifeStateController = playerObject.AddComponent<PlayerLifeStateController>();
        Invoke(lifeStateController, "Awake");
        Invoke(lifeStateController, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        if (health != null && health.IsDead) health.ResetHealth();
        else if (lifeStateController != null && lifeStateController.IsObserverMode)
            lifeStateController.TryExitObserverMode();
        if (lifeStateController != null) Invoke(lifeStateController, "OnDisable");
        if (health != null) Invoke(health, "OnDisable");
        if (observerMode != null) Invoke(observerMode, "OnDestroy");
        if (respawnMarker != null) Object.DestroyImmediate(respawnMarker);
        if (playerObject != null) Object.DestroyImmediate(playerObject);
    }

    [Test]
    public void DamageReducesHealthAndRaisesDamagedEvent()
    {
        int damagedEvents = 0;
        health.Damaged += _ => damagedEvents++;

        health.TakeDamage(25f);

        Assert.That(health.CurrentHealth, Is.EqualTo(75f));
        Assert.That(health.IsDead, Is.False);
        Assert.That(damagedEvents, Is.EqualTo(1));
    }

    [Test]
    public void DeathIsRaisedOnceAndResetRaisesRevived()
    {
        int diedEvents = 0;
        int revivedEvents = 0;
        health.Died += _ => diedEvents++;
        health.Revived += _ => revivedEvents++;

        health.TakeDamage(health.MaxHealth);
        health.TakeDamage(health.MaxHealth);

        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(health.IsDead, Is.True);
        Assert.That(diedEvents, Is.EqualTo(1));
        Assert.That(characterController.enabled, Is.False);
        Assert.That(playerMotion.enabled, Is.False);
        Assert.That(shooter.enabled, Is.False);
        Assert.That(lifeStateController.IsObserverMode, Is.True);
        Assert.That(lifeStateController.TryExitObserverMode(), Is.False);

        health.ResetHealth();

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(health.IsDead, Is.False);
        Assert.That(revivedEvents, Is.EqualTo(1));
        Assert.That(characterController.enabled, Is.True);
        Assert.That(playerMotion.enabled, Is.True);
        Assert.That(shooter.enabled, Is.True);
        Assert.That(lifeStateController.IsObserverMode, Is.False);
    }

    [Test]
    public void LivingPlayerCanEnterAndExitObserverModeWithoutLosingHealth()
    {
        Assert.That(lifeStateController.TryEnterObserverMode(), Is.True);
        Assert.That(lifeStateController.IsObserverMode, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(characterController.enabled, Is.False);
        Assert.That(playerMotion.enabled, Is.False);
        Assert.That(shooter.enabled, Is.False);

        Assert.That(lifeStateController.TryExitObserverMode(), Is.True);
        Assert.That(lifeStateController.IsObserverMode, Is.False);
        Assert.That(health.IsDead, Is.False);
        Assert.That(characterController.enabled, Is.True);
        Assert.That(playerMotion.enabled, Is.True);
        Assert.That(shooter.enabled, Is.True);
    }

    [Test]
    public void DeathRespawnsAtConfiguredPointAfterDelayRoutineCompletes()
    {
        respawnMarker = new GameObject("Test respawn point");
        respawnMarker.transform.SetPositionAndRotation(new Vector3(4f, 2f, 6f), Quaternion.Euler(0f, 90f, 0f));
        lifeStateController.RespawnPoint = respawnMarker.transform;
        lifeStateController.RespawnDelay = 0f;

        health.TakeDamage(health.MaxHealth);
        Assert.That(health.IsDead, Is.True);
        Assert.That(lifeStateController.IsObserverMode, Is.True);

        IEnumerator respawn = (IEnumerator)InvokeResult(lifeStateController, "RespawnAfterDelay");
        Assert.That(respawn.MoveNext(), Is.True);
        Assert.That(respawn.MoveNext(), Is.False);

        Assert.That(health.IsDead, Is.False);
        Assert.That(lifeStateController.IsObserverMode, Is.False);
        Assert.That(playerObject.transform.position, Is.EqualTo(respawnMarker.transform.position));
        Assert.That(characterController.enabled, Is.True);
        Assert.That(playerMotion.enabled, Is.True);
        Assert.That(shooter.enabled, Is.True);
    }

    private static void Invoke(object target, string methodName) =>
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static object InvokeResult(object target, string methodName) =>
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
}
#endif
