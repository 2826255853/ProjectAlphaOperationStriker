#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class SentryPhalanxBranchTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> owned = new List<Object>();
    private TrapUpgradeService service;

    [SetUp]
    public void SetUp()
    {
        service = new TrapUpgradeService(() => new System.Guid("a8e8b201-17f0-4a16-bb75-0a8e4e7e1c44"),
            AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>("Assets/Resources/TrapUpgradeCatalog.asset"),
            SentryPhalanxBranchAdapter.IsAvailable);
    }

    [TearDown]
    public void TearDown()
    {
        service?.Dispose();
        for (int i = owned.Count - 1; i >= 0; --i)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    [Test]
    public void CatalogExposesIntegratedPhalanxBranchAtSixtyCoins()
    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/AutoSentryTurret.asset");
        Assert.IsNotNull(definition);
        while (service.GetState(definition.TrapId).Level < 3)
        {
            TrapUpgradeQuote quote = service.GetQuote(definition,
                service.GetState(definition.TrapId).Level == 3 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);
            Assert.IsTrue(quote.CanUpgrade, quote.Message);
            Assert.IsTrue(service.TryUpgrade(quote, out TrapUpgradeFailure failure), failure.ToString());
        }

        TrapUpgradeQuote branch = service.GetQuote(definition, TrapUpgradeDirection.Branch, "phalanx");
        Assert.IsTrue(branch.CanUpgrade, branch.Message);
        Assert.AreEqual(60, branch.Cost);
        Assert.AreEqual(2f, branch.TargetMultipliers.Damage);
    }

    [Test]
    public void CannonPrefersAirAndFiresFiftyShotsPerSecond()
    {
        GameObject root = Own(new GameObject("Phalanx branch"));
        PhalanxAutoCannon cannon = root.AddComponent<PhalanxAutoCannon>();
        Set(cannon, "detectionRange", 50f);
        Set(cannon, "roundsPerSecond", 50f);
        Set(cannon, "damagePerShot", 0.5f);

        GameObject airObject = Own(Enemy("Air", new Vector3(8f, 4f, 0f), MonsterType.Flying));
        GameObject groundObject = Own(Enemy("Ground", new Vector3(2f, 0f, 0f), MonsterType.Ground));
        EnemyInstance air = airObject.GetComponent<EnemyInstance>();
        EnemyInstance ground = groundObject.GetComponent<EnemyInstance>();
        Invoke(cannon, "ResolveReferences");
        cannon.AcquireTarget();
        Assert.AreSame(air, cannon.CurrentTarget);

        float before = air.GetComponent<EnemyHealth>().CurrentHealth;
        cannon.Tick(1f);
        Assert.AreEqual(25f, before - air.GetComponent<EnemyHealth>().CurrentHealth, 0.001f);
        Assert.AreEqual(100f, ground.GetComponent<EnemyHealth>().CurrentHealth, 0.001f);
    }

    [Test]
    public void BranchUpgradeKeepsTrapInstanceAndTogglesPhalanxVisual()
    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/AutoSentryTurret.asset");
        Assert.IsNotNull(definition);
        GameObject root = Own(new GameObject("Sentry branch owner"));
        TrapInstance trap = root.AddComponent<TrapInstance>();
        Set(trap, "definition", definition);
        Invoke(trap, "Awake");
        Invoke(trap, "OnEnable");
        SentryPhalanxBranchAdapter adapter = root.AddComponent<SentryPhalanxBranchAdapter>();
        Invoke(adapter, "Awake");
        Invoke(adapter, "OnEnable");
        Assert.IsFalse(adapter.IsPhalanxActive);

        while (service.GetState(definition.TrapId).Level < 3)
        {
            TrapUpgradeQuote quote = service.GetQuote(definition,
                service.GetState(definition.TrapId).Level == 3 ? TrapUpgradeDirection.Normal : TrapUpgradeDirection.None);
            Assert.IsTrue(service.TryUpgrade(quote, out TrapUpgradeFailure failure), failure.ToString());
        }
        TrapUpgradeState committed = new TrapUpgradeState(4, TrapUpgradeDirection.Branch, "phalanx");
        Invoke(adapter, "HandleUpgradeStateChanged", trap, committed);

        Assert.AreSame(trap, root.GetComponent<TrapInstance>());
        Assert.IsTrue(adapter.IsPhalanxActive);
        Assert.IsNotNull(adapter.PhalanxVisual);
        Assert.IsNull(adapter.PhalanxVisual.GetComponent<TrapInstance>());
        Assert.IsTrue(adapter.PhalanxVisual.activeSelf);
    }

    private GameObject Enemy(string name, Vector3 position, MonsterType type)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;
        EnemyInstance instance = root.AddComponent<EnemyInstance>();
        Set(instance, "<MonsterType>k__BackingField", type);
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        Set(health, "maxHealth", 100f);
        Invoke(health, "Awake");
        health.ResetHealth();
        return root;
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    private static void Set(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        Assert.IsNotNull(info, field);
        info.SetValue(target, value);
    }

    private static void Invoke(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private)?.Invoke(target, args);
}
#endif
