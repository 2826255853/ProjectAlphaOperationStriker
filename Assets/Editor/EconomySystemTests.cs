#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class EconomySystemTests
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
    public void StartupUsesOneWalletAndDoesNotRefillOnRepeatedAccess()
    {
        EconomyManager wallet = EconomyManager.GetOrCreate();
        objects.Add(wallet.gameObject);
        Guid run = wallet.RunId;
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(wallet.TrySpend(40), Is.True);

        Assert.That(EconomyManager.GetOrCreate(), Is.SameAs(wallet));
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(wallet.RunId, Is.EqualTo(run));
    }

    [Test]
    public void AuthoredWalletUsesItsConfiguredStartingMoney()
    {
        EconomyManager wallet = CreateWallet();
        typeof(EconomyManager).GetField("startingMoney", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(wallet, 25);
        Invoke(wallet, "Awake");
        Assert.That(wallet.IsInitialized, Is.True);
        Assert.That(wallet.Balance, Is.EqualTo(25));
        Assert.That(EconomyManager.Instance, Is.SameAs(wallet));
    }

    [Test]
    public void UninitializedWalletRejectsTransactions()
    {
        EconomyManager wallet = CreateWallet();
        Assert.That(wallet.CanAfford(0), Is.False);
        Assert.That(wallet.TrySpend(0), Is.False);
        Assert.Throws<InvalidOperationException>(() => wallet.Grant(10));
        Assert.That(wallet.Balance, Is.Zero);
    }

    [Test]
    public void BeginRunResetsBalanceIdentityAndNotifiesSubscribers()
    {
        EconomyManager wallet = CreateWallet();
        var balances = new List<int>();
        wallet.BalanceChanged += balances.Add;
        wallet.BeginRun(100);
        Guid firstRun = wallet.RunId;
        wallet.TrySpend(40);
        wallet.BeginRun(0);

        Assert.That(wallet.RunId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(wallet.RunId, Is.Not.EqualTo(firstRun));
        Assert.That(wallet.Balance, Is.Zero);
        Assert.That(balances, Is.EqualTo(new[] { 100, 60, 0 }));
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void NegativeAmountsCannotAlterTheWallet(int amount)
    {
        EconomyManager wallet = CreateWallet();
        wallet.BeginRun(100);
        Guid run = wallet.RunId;
        int notifications = 0;
        wallet.BalanceChanged += _ => notifications++;

        Assert.That(wallet.CanAfford(amount), Is.False);
        Assert.That(wallet.TrySpend(amount), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Grant(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.BeginRun(amount));
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(wallet.RunId, Is.EqualTo(run));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void PurchasesAndRewardsPublishOnlyCommittedBalances()
    {
        EconomyManager wallet = CreateWallet();
        wallet.BeginRun(40);
        var balances = new List<int>();
        wallet.BalanceChanged += value =>
        {
            Assert.That(wallet.Balance, Is.EqualTo(value));
            balances.Add(value);
        };

        Assert.That(wallet.TrySpend(41), Is.False);
        Assert.That(wallet.CanAfford(40), Is.True);
        Assert.That(wallet.TrySpend(40), Is.True);
        Assert.That(wallet.TrySpend(1), Is.False);
        wallet.Grant(10);
        Assert.That(wallet.TrySpend(0), Is.True);
        wallet.Grant(0);

        Assert.That(wallet.Balance, Is.EqualTo(10));
        Assert.That(balances, Is.EqualTo(new[] { 0, 10 }));
    }

    [Test]
    public void OverflowIsRejectedWithoutChangingBalanceOrSendingEvents()
    {
        EconomyManager wallet = CreateWallet();
        wallet.BeginRun(int.MaxValue - 1);
        var balances = new List<int>();
        wallet.BalanceChanged += balances.Add;
        wallet.Grant(1);
        Assert.Throws<OverflowException>(() => wallet.Grant(1));
        Assert.That(wallet.Balance, Is.EqualTo(int.MaxValue));
        Assert.That(balances, Is.EqualTo(new[] { int.MaxValue }));
        Assert.That(wallet.TrySpend(int.MaxValue), Is.True);
        Assert.That(wallet.Balance, Is.Zero);
    }

    [Test]
    public void RuntimeResetClearsOldRunAndSubscriptionsWithoutDomainReload()
    {
        EconomyManager wallet = CreateWallet();
        Invoke(wallet, "Awake");
        Guid oldRun = wallet.RunId;
        int oldNotifications = 0;
        wallet.BalanceChanged += _ => oldNotifications++;
        typeof(EconomyManager).GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);

        Assert.That(EconomyManager.Instance, Is.Null);
        Assert.That(wallet.IsInitialized, Is.False);
        Assert.That(wallet.RunId, Is.EqualTo(Guid.Empty));
        Assert.That(wallet.TrySpend(0), Is.False);
        Invoke(wallet, "Awake");
        Assert.That(wallet.Balance, Is.EqualTo(100));
        Assert.That(wallet.RunId, Is.Not.EqualTo(oldRun));
        Assert.That(oldNotifications, Is.Zero);
    }

    private EconomyManager CreateWallet()
    {
        var host = new GameObject("Economy test wallet");
        host.SetActive(false);
        objects.Add(host);
        return host.AddComponent<EconomyManager>();
    }

    [UnityTest]
    public IEnumerator EnemyRewardsUseRealDeathAndArrivalLifecycles()
    {
        yield return new EnterPlayMode();
        // Do not retain the EditMode fixture across the domain reload.
        yield return VerifyEnemyRewardLifecycles();
    }

    private static IEnumerator VerifyEnemyRewardLifecycles()
    {
        var spawnedObjects = new List<GameObject>();
        GameObject Track(GameObject host)
        {
            spawnedObjects.Add(host);
            return host;
        }
        EnemyInstance SpawnTracked(EnemySpawnPoint entrance)
        {
            EnemyInstance enemy = entrance.SpawnEnemy();
            Track(enemy.gameObject);
            return enemy;
        }

        try
        {
            EconomyManager wallet = EconomyManager.GetOrCreate();
            wallet.BeginRun(100);
            EnemyCore core = Track(new GameObject("Reward test core")).AddComponent<EnemyCore>();
            core.transform.position = new Vector3(10000f, 0f, 10000f);
            EnemySpawnPoint spawn = Track(new GameObject("Reward test entrance")).AddComponent<EnemySpawnPoint>();
            spawn.SpawningEnabled = false;
            spawn.Core = core;
            spawn.PathGrid = null;
            spawn.transform.position = core.transform.position + Vector3.left * 20f;

            // Use the real spawn -> EnemyHealth.Died -> wallet chain. A second hit
            // occurs before deferred Destroy, reproducing same-frame projectiles.
            EnemyInstance killed = SpawnTracked(spawn);
            Assert.That(killed.KillReward, Is.EqualTo(10));
            EnemyHealth killedHealth = killed.GetComponent<EnemyHealth>();
            killedHealth.TakeDamage(killedHealth.MaxHealth);
            killedHealth.TakeDamage(killedHealth.MaxHealth);
            Assert.That(wallet.Balance, Is.EqualTo(110));
            Assert.That(killed.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Killed));
            Assert.That(killed.PathFollower.enabled, Is.False);
            ArriveAtCore(killed, core);
            Assert.That(core.CurrentHealth, Is.EqualTo(core.MaxHealth), "Killed enemies cannot leak later in the frame.");

            EnemyInstance leaked = SpawnTracked(spawn);
            ArriveAtCore(leaked, core);
            leaked.GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(leaked.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Leaked));
            Assert.That(wallet.Balance, Is.EqualTo(110), "Leaked enemies cannot reward delayed damage.");
            Assert.That(core.CurrentHealth, Is.EqualTo(core.MaxHealth - 2f));

            spawn.MonsterType = MonsterType.Flying;
            EnemyInstance flying = SpawnTracked(spawn);
            Assert.That(flying.KillReward, Is.EqualTo(15));
            Assert.That(flying.PathFollower.IsFlying, Is.True);
            flying.GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(125));
            EnemyInstance flyingLeak = SpawnTracked(spawn);
            ArriveAtCore(flyingLeak, core);
            flyingLeak.GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(125));
            Assert.That(core.CurrentHealth, Is.EqualTo(core.MaxHealth - 3f));

            // Overrides are captured at birth, including legitimate zero rewards.
            ConfigureReward(spawn, true, 27);
            EnemyInstance custom = SpawnTracked(spawn);
            ConfigureReward(spawn, true, 0);
            custom.GetComponent<EnemyHealth>().TakeDamage(1000f);
            SpawnTracked(spawn).GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(152));
            ConfigureReward(spawn, true, -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => spawn.SpawnEnemy());
            ConfigureReward(spawn, false, 0);

            // Arrival can happen synchronously inside follower initialization.
            spawn.transform.position = core.transform.position;
            EnemyInstance immediateLeak = SpawnTracked(spawn);
            Assert.That(immediateLeak.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Leaked));
            immediateLeak.GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(152));
            Assert.That(core.CurrentHealth, Is.EqualTo(core.MaxHealth - 4f));
            spawn.transform.position += Vector3.left * 20f;

            // A new run invalidates old kills and arrivals without refilling per entrance.
            EnemyInstance oldKill = SpawnTracked(spawn);
            EnemyInstance oldLeak = SpawnTracked(spawn);
            wallet.BeginRun(100);
            oldKill.GetComponent<EnemyHealth>().TakeDamage(1000f);
            float coreBeforeOldArrival = core.CurrentHealth;
            ArriveAtCore(oldLeak, core);
            Assert.That(wallet.Balance, Is.EqualTo(100));
            Assert.That(core.CurrentHealth, Is.EqualTo(coreBeforeOldArrival));

            EnemyInstance pooled = SpawnTracked(spawn);
            EnemyHealth pooledHealth = pooled.GetComponent<EnemyHealth>();
            pooledHealth.TakeDamage(25f);
            pooled.gameObject.SetActive(false);
            Assert.That(pooled.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Despawned));
            Assert.That(wallet.Balance, Is.EqualTo(100));
            pooled.gameObject.SetActive(true);
            typeof(EnemyInstance).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pooled, new object[] { spawn, 99, 1, 1 });
            Assert.That(pooled.Resolution, Is.EqualTo(EnemyInstance.ResolutionState.Alive));
            Assert.That(pooledHealth.CurrentHealth, Is.EqualTo(pooledHealth.MaxHealth));
            pooledHealth.TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(115), "Reuse starts one new reward lifetime.");

            EnemyInstance cleaned = SpawnTracked(spawn);
            Object.DestroyImmediate(cleaned.gameObject);
            Assert.That(wallet.Balance, Is.EqualTo(115), "Cleanup is not a kill.");

            EnemyInstance overflow = SpawnTracked(spawn);
            wallet.Grant(int.MaxValue - wallet.Balance);
            LogAssert.Expect(LogType.Warning, "击杀奖励超过金币余额上限，本次奖励未发放。");
            overflow.GetComponent<EnemyHealth>().TakeDamage(1000f);
            Assert.That(wallet.Balance, Is.EqualTo(int.MaxValue));
            yield return null;
            Assert.That(overflow == null, Is.True, "Overflow must not interrupt enemy destruction.");
        }
        finally
        {
            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
                if (spawnedObjects[i] != null) Object.DestroyImmediate(spawnedObjects[i]);
        }
    }

    [UnityTearDown]
    public IEnumerator LeavePlayModeAfterLifecycleTest()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private static void ArriveAtCore(EnemyInstance enemy, EnemyCore core)
    {
        enemy.transform.position = core.transform.position;
        enemy.PathFollower.RebuildPath();
    }

    private static void ConfigureReward(EnemySpawnPoint spawn, bool enabled, int amount)
    {
        var serialized = new UnityEditor.SerializedObject(spawn);
        serialized.FindProperty("overrideKillReward").boolValue = enabled;
        serialized.FindProperty("killRewardOverride").intValue = amount;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Invoke(EconomyManager wallet, string method) =>
        typeof(EconomyManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(wallet, null);
}
#endif
