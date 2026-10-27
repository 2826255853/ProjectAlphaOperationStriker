#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ThroatLampTests
{
    private readonly List<Object> owned = new List<Object>();
    private static readonly Vector3 Origin = new Vector3(300f, 30f, 300f);
    private GameObject New(string name) { var go = new GameObject(name); owned.Add(go); return go; }
    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private EnemyInstance Enemy(Vector3 offset, bool flying = false)
    {
        GameObject go = New(flying ? "plasma flying target" : "plasma ground target");
        go.transform.position = Origin + offset;
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        Set(health, "maxHealth", 10000f);
        var enemy = go.AddComponent<EnemyInstance>();
        typeof(EnemyInstance).GetProperty("MonsterType").SetValue(enemy, flying ? MonsterType.Flying : MonsterType.Ground);
        typeof(EnemyInstance).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(enemy, new object[] { null, 0, 1, 0 });
        if (!owned.Contains(EconomyManager.Instance.gameObject)) owned.Add(EconomyManager.Instance.gameObject);
        return enemy;
    }

    private ThroatLampTrap Lamp(float yaw = 0f)
    {
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ThroatLampAuthoring.PrefabPath));
        owned.Add(go);
        go.transform.SetPositionAndRotation(Origin + Vector3.up * 4f,
            Quaternion.Euler(0f, yaw, 0f) * Quaternion.LookRotation(Vector3.down, Vector3.forward));
        var trap = go.GetComponent<ThroatLampTrap>();
        // EditMode does not call Awake; the serialized rig is already complete.
        go.GetComponent<Animator>().Rebind();
        go.GetComponent<Animator>().Update(0f);
        return trap;
    }

    private PlasmaProjectile Shot(Vector3 offset, Vector3 direction, float range = 18f)
    {
        var go = New("test plasma"); go.transform.position = Origin + offset;
        var shot = go.AddComponent<PlasmaProjectile>();
        shot.Initialize(direction, 20f, range, 80f, 40f, 2f, null);
        return shot;
    }

    [TearDown] public void Cleanup()
    {
        foreach (PlasmaProjectile shot in Object.FindObjectsByType<PlasmaProjectile>(FindObjectsSortMode.None))
            if (shot != null && Vector3.Distance(shot.transform.position, Origin) < 100f) Object.DestroyImmediate(shot.gameObject);
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
        Time.timeScale = 1f;
    }

    [UnityTearDown] public IEnumerator LeavePlayMode()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [Test] public void GeneratedAssetsArePurchasableCeilingTrapAndOnlyGameplayTriggersAttack()
    {
        var definition = Resources.Load<TrapDefinition>("ThroatLamp");
        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.MountType, Is.EqualTo(TrapMountType.Ceiling));
        Assert.That(definition.WorldFootprint, Is.EqualTo(new Vector2(2, 2)));
        Assert.That(definition.Cost, Is.EqualTo(40));
        Assert.That(definition.Prefab.GetComponent<ThroatLampTrap>().HasBoundRig, Is.True);
        Assert.That(definition.Prefab.GetComponent<TrapInstance>(), Is.Not.Null);
        Assert.That(TrapSelectionMenu.MatchesMountFilter(definition, 3), Is.True);
        foreach (float size in new[] { 0.5f, 1f, 2f })
        {
            Assert.That(definition.TryGetFootprint(size, out Vector2Int cells), Is.True);
            Assert.That(cells, Is.EqualTo(Vector2Int.one * Mathf.CeilToInt(2f / size)));
        }
        var controller = (AnimatorController)AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ThroatLampAuthoring.ControllerPath);
        foreach (var state in controller.layers[0].stateMachine.states)
            Assert.That(state.state.transitions.Length, Is.EqualTo(state.state.name == "Discharge" ? 1 : 0));
    }

    [Test] public void SweepDealsElectricDamageOnceAndIgnoresFlyingEnemies()
    {
        var direct = Enemy(new Vector3(0, 0, 4));
        var nearby = Enemy(new Vector3(1, 0, 4));
        var outside = Enemy(new Vector3(3, 0, 4));
        var flying = Enemy(new Vector3(0, 0, 2), true);
        nearby.GetComponent<EnemyHealth>().SetResistancePercent(DamageType.Electric, 50f);
        Shot(Vector3.zero, Vector3.forward).Advance(1f);
        Assert.That(direct.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9920));
        Assert.That(nearby.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9980));
        Assert.That(outside.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(10000));
        Assert.That(flying.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(10000));
    }

    [Test] public void SweepStopsAtWallAndAppliesOnlySplashWithoutTunnelling()
    {
        var enemy = Enemy(new Vector3(0, 0, 5));
        var wall = New("plasma blocker"); wall.transform.position = Origin + new Vector3(0, 0, 4);
        wall.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.1f);
        Physics.SyncTransforms();
        Shot(Vector3.zero, Vector3.forward).Advance(1f);
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9960));
    }

    [Test] public void SplashIncludesBoundaryAndExcludesOutsideAndDoesNotDoubleDamageDirectTarget()
    {
        var direct = Enemy(new Vector3(0, 0, 4));
        // Expanded fallback body begins at Z=3.6, the exact swept impact point.
        var boundary = Enemy(new Vector3(2, 0, 3.6f));
        var outside = Enemy(new Vector3(2.05f, 0, 3.6f));
        Shot(Vector3.zero, Vector3.forward).Advance(1f);
        Assert.That(direct.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9920));
        Assert.That(boundary.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(9960));
        Assert.That(outside.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(10000));
    }

    [Test] public void ExpiredShotDoesNotDetonateAndDirectionNeverTracksTarget()
    {
        var enemy = Enemy(new Vector3(0, 0, 4));
        var shot = Shot(Vector3.zero, Vector3.forward, 2f);
        enemy.transform.position += Vector3.right;
        Assert.That(shot.Direction, Is.EqualTo(Vector3.forward));
        shot.Advance(1f);
        Assert.That(shot == null, Is.True);
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.EqualTo(10000));
    }

    [TestCase(0f)] [TestCase(47f)] [TestCase(130f)]
    public void CeilingAimOverwritesAnimationAndShootsEveryFourSeconds(float yaw)
    {
        var target = Enemy(new Vector3(2, 0, 2));
        var lamp = Lamp(yaw);
        var animator = lamp.GetComponent<Animator>();
        Transform mount = lamp.transform.Find("Ceiling Mount");
        Quaternion mountRotation = mount.rotation;
        var times = new List<float>();
        int shots = 0;
        for (int i = 0; i < 520; i++)
        {
            animator.Update(0.02f);
            lamp.Advance(0.02f);
            if (lamp.ShotCount > shots) { times.Add(i * 0.02f); shots = lamp.ShotCount; }
        }
        Assert.That(lamp.CurrentTarget, Is.EqualTo(target));
        Assert.That(times.Count, Is.GreaterThanOrEqualTo(3));
        for (int i = 1; i < times.Count; i++) Assert.That(times[i] - times[i - 1], Is.EqualTo(4f).Within(0.04f));
        Assert.That(Vector3.Angle(lamp.Nozzle.forward, lamp.InterceptPoint - lamp.Nozzle.position), Is.LessThan(2f));
        Assert.That(Quaternion.Angle(mountRotation, mount.rotation), Is.LessThan(0.01f));
    }

    [Test] public void MovingGroundTargetIsLedThenPredictionSettlesWhenItStopsAndReverses()
    {
        var target = Enemy(new Vector3(0, 0, 3));
        var lamp = Lamp();
        for (int i = 0; i < 35; i++) { target.transform.position += Vector3.right * 0.04f; lamp.Advance(0.02f); }
        Assert.That(lamp.InterceptPoint.x, Is.GreaterThan(target.transform.position.x + 0.2f));
        for (int i = 0; i < 100; i++) lamp.Advance(0.02f);
        Assert.That(Vector3.Distance(lamp.InterceptPoint, target.transform.position), Is.LessThan(0.02f));
        for (int i = 0; i < 35; i++) { target.transform.position -= Vector3.right * 0.04f; lamp.Advance(0.02f); }
        Assert.That(lamp.InterceptPoint.x, Is.LessThan(target.transform.position.x - 0.2f));
    }

    [Test] public void PlacementGhostCannotAcquireTargetsOrFireEvenWhenAdvanced()
    {
        Enemy(Vector3.zero);
        var lamp = Lamp();
        lamp.gameObject.AddComponent<TrapPlacementPreview>();
        for (int i = 0; i < 400; i++) lamp.Advance(0.02f);
        Assert.That(lamp.CurrentTarget, Is.Null);
        Assert.That(lamp.ShotCount, Is.Zero);
    }

    [Test] public void InterceptBeyondProjectileRangeCancelsCharge()
    {
        var target = Enemy(new Vector3(0f, 0f, 15f));
        var lamp = Lamp();
        for (int i = 0; i < 10; i++) { target.transform.position += Vector3.forward * 0.1f; lamp.Advance(0.02f); }
        Assert.That(lamp.HasInterceptPoint, Is.False);
        Assert.That(lamp.ShotCount, Is.Zero);
    }

    [Test] public void FlyingOccludedAndDeadTargetsCannotTriggerShotsAndReacquisitionRecharges()
    {
        Enemy(new Vector3(0, 2, 0), true);
        var target = Enemy(Vector3.zero);
        var lamp = Lamp();
        for (int i = 0; i < 40; i++) lamp.Advance(0.02f);
        Assert.That(lamp.State, Is.EqualTo(ThroatLampState.Charging));
        var wall = New("line of sight blocker"); wall.transform.position = Origin + Vector3.up * 2;
        wall.AddComponent<BoxCollider>(); Physics.SyncTransforms();
        for (int i = 0; i < 200; i++) lamp.Advance(0.02f);
        Assert.That(lamp.ShotCount, Is.Zero);
        Assert.That(lamp.CurrentTarget, Is.Null);
        wall.SetActive(false); Physics.SyncTransforms();
        for (int i = 0; i < 45; i++) lamp.Advance(0.02f);
        Assert.That(lamp.ShotCount, Is.Zero);
        target.gameObject.SetActive(false);
        lamp.Advance(0.02f);
        Assert.That(lamp.CurrentTarget, Is.Null);
        var replacement = Enemy(Vector3.zero);
        for (int i = 0; i < 45; i++) lamp.Advance(0.02f);
        Assert.That(lamp.ShotCount, Is.Zero);
        for (int i = 0; i < 45; i++) lamp.Advance(0.02f);
        Assert.That(lamp.ShotCount, Is.EqualTo(1));
        Assert.That(lamp.CurrentTarget, Is.EqualTo(replacement));
    }

    private TrapPlacementGrid Ceiling(float yaw)
    {
        var support = New("plasma test ceiling"); support.isStatic = true;
        support.transform.SetPositionAndRotation(Origin + Vector3.up * 4.1f, Quaternion.Euler(0, yaw, 0));
        var box = support.AddComponent<BoxCollider>(); box.size = new Vector3(4f, 0.2f, 4f);
        var grid = New("plasma ceiling grid").AddComponent<TrapPlacementGrid>();
        grid.transform.SetParent(support.transform, false);
        CeilingTrapGridAuthoring.Configure(grid, box, new MapForgeSceneOrganization.CeilingTrapGridMap { id = "plasma" });
        return grid;
    }

    [UnityTest] public IEnumerator RuntimePurchaseAnimationCombatAndSale()
    {
        yield return new EnterPlayMode();
        Time.timeScale = 1f;
        // Test only the real generated trap in an isolated area of the running scene.
        var wallet = EconomyManager.GetOrCreate(); owned.Add(wallet.gameObject); wallet.BeginRun(100);
        var definition = Resources.Load<TrapDefinition>("ThroatLamp");
        var grid = Ceiling(37f);
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, definition, out var placed, out var failure), Is.True, failure);
        owned.Add(placed.gameObject);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(TrapPurchaseService.TryPurchase(grid, Vector2Int.zero, definition, out _, out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        Assert.That(TrapPurchaseService.TryPurchase(grid, new Vector2Int(3, 3), definition, out _, out _), Is.False);
        Assert.That(wallet.Balance, Is.EqualTo(60));
        var enemy = Enemy(Vector3.zero);
        enemy.transform.position = placed.transform.position + Vector3.down * 3f + Vector3.right;
        var lamp = placed.GetComponent<ThroatLampTrap>();
        float timeout = Time.realtimeSinceStartup + 10f;
        bool sawCharge = false, sawDischarge = false;
        while (lamp.ShotCount < 2 && Time.realtimeSinceStartup < timeout)
        {
            var state = lamp.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);
            sawCharge |= state.IsName("Charge"); sawDischarge |= state.IsName("Discharge");
            yield return null;
        }
        Assert.That(lamp.ShotCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(sawCharge && sawDischarge, Is.True, "Animator must really play Charge and Discharge.");
        Assert.That(enemy.GetComponent<EnemyHealth>().CurrentHealth, Is.LessThan(10000));
        Assert.That(placed.CurrentHealth, Is.GreaterThan(0));
        Assert.That(TrapInstance.ActiveTraps, Does.Contain(placed));
        Assert.That(TrapHealthBarUI.FindTrapNearRay(new Ray(placed.transform.position + Vector3.down * 2f,
            Vector3.up), 2.1f), Is.EqualTo(placed));
        Assert.That(TrapSaleService.TrySell(grid, placed, out int refund, out failure), Is.True, failure);
        Assert.That(refund, Is.EqualTo(20)); Assert.That(wallet.Balance, Is.EqualTo(80));
        yield return null;
        Assert.That(grid.IsOccupied(Vector2Int.zero), Is.False);
        Assert.That(grid.IsOccupied(Vector2Int.one), Is.False);
        yield return new ExitPlayMode();
    }
}
#endif
