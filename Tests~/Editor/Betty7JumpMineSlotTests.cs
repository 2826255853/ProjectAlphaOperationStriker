#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 贝蒂7号跳雷陷阱：方向择雷 + 每个槽位独立装填 的 EditMode 回归用例。
/// 运行：Unity -runTests -testPlatform EditMode -testFilter Betty7JumpMineSlotTests
/// （仓库存放于 Tests~，运行时临时复制进 Assets/Editor，跑完移出。）
/// </summary>
public sealed class Betty7JumpMineSlotTests
{
    private const float ArmingSeconds = 0.30f;
    private const float LaunchSeconds = 0.45f;
    private const float HoldSeconds = 0.22f;
    private const float RecoverSeconds = 0.45f;
    private const float CycleSeconds = ArmingSeconds + LaunchSeconds + HoldSeconds + RecoverSeconds;

    private GameObject trapObject;
    private readonly System.Collections.Generic.List<GameObject> enemies = new System.Collections.Generic.List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject enemy in enemies)
            if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy);
        enemies.Clear();
        if (trapObject != null) UnityEngine.Object.DestroyImmediate(trapObject);
        foreach (GameObject sceneObject in UnityEngine.Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string name;
            try { name = sceneObject != null ? sceneObject.name : string.Empty; }
            catch (MissingReferenceException) { continue; }
            if (name.StartsWith("Betty7 Burst Flash")) UnityEngine.Object.DestroyImmediate(sceneObject);
        }
    }

    private static void InvokeLifecycle(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(info, "missing lifecycle method " + method + " on " + target.GetType().Name);
        info.Invoke(target, null);
    }

    /// <summary>四角跳雷按预制体布局摆放：前后左右各一颗，半径 0.61 m。</summary>
    private Betty7JumpMineTrap BuildTrap()
    {
        trapObject = new GameObject("Betty7Trap");
        var mines = new GameObject("Mines");
        mines.transform.SetParent(trapObject.transform, false);
        foreach (var (name, x, z) in new[]
                 {
                     ("Mine_01", 0.61f, 0.61f), ("Mine_02", -0.61f, 0.61f),
                     ("Mine_03", -0.61f, -0.61f), ("Mine_04", 0.61f, -0.61f)
                 })
        {
            var mine = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mine.name = name;
            mine.transform.SetParent(mines.transform, false);
            mine.transform.localPosition = new Vector3(x, 0.255f, z);
            UnityEngine.Object.DestroyImmediate(mine.GetComponent<Collider>());
        }
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Detector_Dome";
        dome.transform.SetParent(trapObject.transform, false);
        dome.transform.localPosition = new Vector3(0f, 0.30f, 0f);
        UnityEngine.Object.DestroyImmediate(dome.GetComponent<Collider>());

        var instance = trapObject.AddComponent<TrapInstance>();
        var trap = trapObject.AddComponent<Betty7JumpMineTrap>();
        InvokeLifecycle(instance, "Awake");
        InvokeLifecycle(trap, "Awake");
        InvokeLifecycle(trap, "OnEnable");
        return trap;
    }

    private EnemyHealth BuildEnemy(Vector3 position, bool flying)
    {
        var enemyObject = new GameObject(flying ? "FlyingMonster" : "Monster");
        enemyObject.transform.position = position;
        var health = enemyObject.AddComponent<EnemyHealth>();
        // 高血量夹具：EnemyHealth 死亡时会调用 Destroy，编辑期会报错并留下
        // 待销毁对象干扰后续用例；这里只验证伤害是否结算。
        FieldInfo maxHealth = typeof(EnemyHealth).GetField("maxHealth",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(maxHealth, "EnemyHealth.maxHealth field not found");
        maxHealth.SetValue(health, 100000f);
        InvokeLifecycle(health, "Awake");
        var instance = enemyObject.AddComponent<EnemyInstance>();
        var field = typeof(EnemyInstance).GetField("<MonsterType>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "EnemyInstance.MonsterType backing field not found");
        field.SetValue(instance, flying ? MonsterType.Flying : MonsterType.Ground);
        enemies.Add(enemyObject);
        return health;
    }

    private static void Run(Betty7JumpMineTrap trap, float seconds, float step = 0.02f)
    {
        for (float t = 0f; t < seconds; t += step) trap.Tick(step);
    }

    /// <summary>推进到条件成立为止，返回实际耗时。</summary>
    private static float RunUntil(Betty7JumpMineTrap trap, Func<bool> condition, float maxSeconds, float step = 0.02f)
    {
        float elapsed = 0f;
        while (!condition() && elapsed < maxSeconds)
        {
            trap.Tick(step);
            elapsed += step;
        }
        return elapsed;
    }

    private Transform MineAt(Betty7JumpMineTrap trap, int index)
    {
        Transform mines = trapObject.transform.Find("Mines");
        Assert.IsNotNull(mines, "Mines group missing");
        return mines.GetChild(index);
    }

    private static int ReadySlotCount(Betty7JumpMineTrap trap)
    {
        int count = 0;
        for (int i = 0; i < trap.MineSlotCount; i++)
            if (trap.IsMineReady(i)) count++;
        return count;
    }

    // ──────────────────────────────────────────────
    //  方向择雷：只启动敌人所在那一侧的跳雷
    // ──────────────────────────────────────────────

    [Test]
    public void PlacementPreviewNeverAttacksAfterBeingShownAgain()
    {
        var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/Betty7.asset");
        Assert.IsNotNull(definition);
        Assert.IsNotNull(definition.Prefab);
        var controllerObject = new GameObject("Betty7 Preview Test Controller");
        GameObject preview = null;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo previewField = typeof(TrapPlacementController).GetField("preview", fields);
        try
        {
            var controller = controllerObject.AddComponent<TrapPlacementController>();
            typeof(TrapPlacementController).GetField("selectedTrap", fields).SetValue(controller, definition);
            typeof(TrapPlacementController).GetField("placementMode", fields).SetValue(controller, true);
            // Runtime previews intentionally clone renderer materials. In EditMode,
            // Unity logs this warning as an error; cleanup below releases the clones.
            foreach (Renderer renderer in definition.Prefab.GetComponentsInChildren<Renderer>())
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null)
                        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                            "Instantiating material due to calling renderer.material during edit mode. " +
                            "This will leak materials into the scene. You most likely want to use renderer.sharedMaterial instead.");
            InvokeLifecycle(controller, "RebuildPreview");
            preview = (GameObject)previewField.GetValue(controller);
            Assert.IsNotNull(preview);
            Assert.IsNotNull(preview.GetComponent<TrapPlacementPreview>());
            var trap = preview.GetComponent<Betty7JumpMineTrap>();
            Assert.IsNotNull(trap);
            // EditMode does not automatically initialize these runtime components.
            InvokeLifecycle(preview.GetComponent<TrapInstance>(), "Awake");
            InvokeLifecycle(trap, "Awake");
            EnemyHealth health = BuildEnemy(preview.transform.position + Vector3.forward * 1.2f, flying: false);
            float before = health.CurrentHealth;

            for (int show = 0; show < 2; show++)
            {
                preview.SetActive(true);
                Assert.IsFalse(trap.enabled, "showing a placement ghost must not enable its attack component");
                Run(trap, 3f);
                Assert.AreEqual(Betty7JumpMineTrap.AttackState.Ready, trap.State);
                Assert.AreEqual(-1, trap.LastLaunchedMineIndex);
                Assert.AreEqual(0, trap.DetonationCount);
                Assert.AreEqual(before, health.CurrentHealth, 0.001f);
                preview.SetActive(false);
            }
            Assert.IsTrue(definition.Prefab.GetComponent<Betty7JumpMineTrap>().enabled,
                "disabling the preview must leave the actual prefab ready to attack");
        }
        finally
        {
            var controller = controllerObject.GetComponent<TrapPlacementController>();
            if (controller != null)
            {
                // Avoid runtime Destroy calls when cleaning up an EditMode preview.
                previewField.SetValue(controller, null);
                InvokeLifecycle(controller, "ReleasePreviewMaterials");
            }
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    [Test]
    public void TargetInFrontLaunchesTheCanisterOnThatSide()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);

        Run(trap, 0.10f);

        Assert.GreaterOrEqual(trap.LastLaunchedMineIndex, 0, "detector should commit a canister");
        Transform launched = MineAt(trap, trap.LastLaunchedMineIndex);
        Assert.Greater(launched.localPosition.z, 0f, "monster on +Z must use a +Z canister");
    }

    [Test]
    public void TargetBehindLaunchesTheCanisterOnThatSide()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, -1.2f), flying: false);

        Run(trap, 0.10f);

        Assert.GreaterOrEqual(trap.LastLaunchedMineIndex, 0, "detector should commit a canister");
        Transform launched = MineAt(trap, trap.LastLaunchedMineIndex);
        Assert.Less(launched.localPosition.z, 0f, "monster on -Z must use a -Z canister");
    }

    [Test]
    public void TargetOnTheRightLaunchesTheCanisterOnThatSide()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(1.2f, 0f, 0f), flying: false);

        Run(trap, 0.10f);

        Assert.GreaterOrEqual(trap.LastLaunchedMineIndex, 0);
        Transform launched = MineAt(trap, trap.LastLaunchedMineIndex);
        Assert.Greater(launched.localPosition.x, 0f, "monster on +X must use a +X canister");
    }

    // ──────────────────────────────────────────────
    //  一次只启动一颗
    // ──────────────────────────────────────────────

    [Test]
    public void OnlyOneCanisterIsEngagedAtATime()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);

        Run(trap, 0.10f);

        Assert.AreEqual(3, ReadySlotCount(trap), "three canisters must stay loaded while one arms");
        Assert.AreEqual(0, trap.DetonationCount, "nothing has detonated yet");
        Assert.AreEqual(Betty7JumpMineTrap.AttackState.Arming, trap.State);
    }

    [Test]
    public void SingleDetonationPerLaunch()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        EnemyHealth health = BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);
        float before = health.CurrentHealth;

        Run(trap, CycleSeconds - 0.4f);

        Assert.AreEqual(1, trap.DetonationCount, "one launch must detonate exactly once");
        Assert.Less(health.CurrentHealth, before, "the monster inside the ring takes blast damage");
        int total = 0;
        for (int i = 0; i < trap.MineSlotCount; i++) total += trap.GetMineDetonationCount(i);
        Assert.AreEqual(trap.DetonationCount, total);
    }

    // ──────────────────────────────────────────────
    //  每个槽位单独装填
    // ──────────────────────────────────────────────

    [Test]
    public void OnlyTheFiredSlotReloads()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);

        // 起爆后立刻清场，避免第二颗紧接着开始启用。
        RunUntil(trap, () => trap.DetonationCount > 0, CycleSeconds);
        int fired = trap.LastLaunchedMineIndex;
        foreach (GameObject enemy in enemies) UnityEngine.Object.DestroyImmediate(enemy);
        enemies.Clear();

        Run(trap, CycleSeconds);

        Assert.AreEqual(Betty7JumpMineTrap.AttackState.Cooldown, trap.GetMineState(fired),
            "the fired canister must be reloading");
        Assert.Greater(trap.GetMineReloadRemaining(fired), 4f, "reload timer should be running");
        Assert.AreEqual(3, ReadySlotCount(trap), "the other three canisters keep their load");
    }

    [Test]
    public void ReloadTimersArePerSlot()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);

        // 两颗跳雷先后起爆，间隔就是一个完整周期。
        RunUntil(trap, () => trap.DetonationCount >= 2, CycleSeconds * 3f);
        foreach (GameObject enemy in enemies) UnityEngine.Object.DestroyImmediate(enemy);
        enemies.Clear();
        // 等第二颗走完起爆/回收，两个槽位同时处于装填，才能比较各自的剩余时间。
        Run(trap, HoldSeconds + RecoverSeconds + 0.2f);

        var reloading = new System.Collections.Generic.List<int>();
        for (int i = 0; i < trap.MineSlotCount; i++)
            if (trap.GetMineReloadRemaining(i) > 0f) reloading.Add(i);

        Assert.AreEqual(2, reloading.Count, "two canisters fired, two reloading");
        float first = Mathf.Max(trap.GetMineReloadRemaining(reloading[0]), trap.GetMineReloadRemaining(reloading[1]));
        float second = Mathf.Min(trap.GetMineReloadRemaining(reloading[0]), trap.GetMineReloadRemaining(reloading[1]));
        Assert.Greater(first - second, CycleSeconds * 0.6f,
            "each slot keeps its own timer, so the earlier launch is closer to being loaded");
    }

    [Test]
    public void ReloadedSlotFiresAgain()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);

        RunUntil(trap, () => trap.DetonationCount > 0, CycleSeconds);
        foreach (GameObject enemy in enemies) UnityEngine.Object.DestroyImmediate(enemy);
        enemies.Clear();

        // 装填完整走完（起爆后一个周期再等待冷却）。
        Run(trap, CycleSeconds + 6.2f);
        Assert.AreEqual(4, ReadySlotCount(trap), "every slot is loaded again after its own reload");

        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);
        RunUntil(trap, () => trap.DetonationCount > 1, CycleSeconds + 0.5f);
        Assert.AreEqual(2, trap.DetonationCount, "a reloaded canister must be able to fire again");
    }

    // ──────────────────────────────────────────────
    //  真实预制体接线
    // ──────────────────────────────────────────────

    [Test]
    public void PrefabWiresAllFourCanisters()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Betty7.prefab");
        Assert.IsNotNull(prefab, "Betty7 prefab missing");
        trapObject = (GameObject)UnityEngine.Object.Instantiate(prefab);
        var trap = trapObject.GetComponent<Betty7JumpMineTrap>();
        Assert.IsNotNull(trap, "prefab root must carry Betty7JumpMineTrap");

        InvokeLifecycle(trapObject.GetComponent<TrapInstance>(), "Awake");
        InvokeLifecycle(trap, "Awake");
        InvokeLifecycle(trap, "OnEnable");

        Assert.AreEqual(4, trap.MineSlotCount, "prefab must wire all four canisters");
        for (int i = 0; i < trap.MineSlotCount; i++)
            Assert.IsTrue(trap.IsMineReady(i), "every slot starts loaded");

        Vector3 origin = trapObject.transform.position;
        BuildEnemy(new Vector3(origin.x, origin.y, origin.z + 1.2f), flying: false);
        Run(trap, 0.10f);
        Assert.GreaterOrEqual(trap.LastLaunchedMineIndex, 0, "the prefab must be able to fire a canister");
    }

    // ──────────────────────────────────────────────
    //  目标筛选：只打地面怪
    // ──────────────────────────────────────────────

    [Test]
    public void FlyingMonsterIsIgnored()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        EnemyHealth health = BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: true);
        float before = health.CurrentHealth;

        Run(trap, 3f);

        Assert.AreEqual(0, trap.DetonationCount, "flying monsters must not arm the trap");
        Assert.AreEqual(-1, trap.LastLaunchedMineIndex);
        Assert.AreEqual(before, health.CurrentHealth, 0.001f);
    }

    [Test]
    public void DetonationBeyondBlastRadiusDoesNoDamage()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        // 起爆点跟着选中的那颗跳雷走，所以取一个连最近的角雷都够不到的 3.15 m。
        EnemyHealth health = BuildEnemy(new Vector3(0f, 0f, 3.15f), flying: false);
        float before = health.CurrentHealth;

        Run(trap, CycleSeconds + 0.5f);

        Assert.AreEqual(1, trap.DetonationCount, "3.15 m is still inside the 3.2 m detection radius");
        Assert.AreEqual(before, health.CurrentHealth, 0.001f, "3.15 m is outside the 2.5 m blast radius");
    }

    [Test]
    public void LaunchedCanisterReturnsToItsSocket()
    {
        Betty7JumpMineTrap trap = BuildTrap();
        BuildEnemy(new Vector3(0f, 0f, 1.2f), flying: false);
        Transform mine = MineAt(trap, 0);
        Vector3 rest = mine.localPosition;

        Run(trap, 0.75f);
        Assert.Greater(mine.localPosition.y, rest.y + 0.5f, "mine should be airborne mid-cycle");

        foreach (GameObject enemy in enemies) UnityEngine.Object.DestroyImmediate(enemy);
        enemies.Clear();
        Run(trap, 12f, 0.05f);
        Assert.AreEqual(rest.y, mine.localPosition.y, 0.01f, "mine should be back in its socket");
    }
}
#endif
