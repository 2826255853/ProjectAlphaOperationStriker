#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode coverage for the dual missile launcher air-attack rules (R1-R7).
///
/// Every case drives the components through their explicit <c>Advance</c> /
/// <c>AimAt(pos, delta)</c> seams, so the assertions do not depend on
/// <see cref="Time.deltaTime"/> (which is zero in edit mode) or on the order in
/// which Unity would run the real <c>Update</c> methods.
/// </summary>
public sealed class MissileLauncherWeaponTests
{
    private const float PivotHeight = 1f;
    private const float LeftMuzzleX = -0.3f;
    private const float RightMuzzleX = 0.3f;

    private readonly List<GameObject> objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        // Missiles are spawned at the scene root, so sweep them up as well.
        foreach (MissileProjectile missile in Object.FindObjectsByType<MissileProjectile>(FindObjectsInactive.Include))
            if (missile != null) objects.Add(missile.gameObject);

        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    // ---------------------------------------------------------------- R2 ----

    [Test]
    public void NearestEnemyIsTargetedByDefault()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetEnum(turret, "targetPriority", MissileTargetPriority.ClosestToTurret);
        SetObject(turret, "core", CreateCore(new Vector3(0f, 0f, 20f)));

        EnemyInstance near = CreateFlyingEnemy(new Vector3(0f, 1f, 6f), new Vector3(0f, 1f, 6f), 0f);
        EnemyInstance far = CreateFlyingEnemy(new Vector3(0f, 1f, 20f), new Vector3(0f, 1f, 20f), 0f);

        weapon.Advance(1f / 60f);

        Assert.That(turret.CurrentTarget, Is.EqualTo(near),
            "R2: 默认离炮塔最近的敌人优先，即使更远的那只离核心更近。");
        Assert.That(turret.CurrentTarget, Is.Not.EqualTo(far));
    }

    [Test]
    public void TiesAreBrokenByDistanceToTheCore()
    {
        // Both candidates sit ~10 m from the mount, so the primary key ties and the
        // core distance decides (R2 tie-break).
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetFloat(turret, "tieBreakDistanceEpsilon", 0.25f);
        SetEnum(turret, "targetPriority", MissileTargetPriority.ClosestToTurret);
        SetObject(turret, "core", CreateCore(new Vector3(10f, 0f, 0f)));

        EnemyInstance onCoreSide = CreateFlyingEnemy(new Vector3(10f, 1f, 0f), new Vector3(10f, 1f, 0f), 0f);
        EnemyInstance awayFromCore = CreateFlyingEnemy(new Vector3(-10f, 1f, 0f), new Vector3(-10f, 1f, 0f), 0f);

        weapon.Advance(1f / 60f);

        Assert.That(turret.CurrentTarget, Is.EqualTo(onCoreSide),
            "R2 破平：到炮塔距离接近时，取更靠近核心的那只。");
        Assert.That(turret.CurrentTarget, Is.Not.EqualTo(awayFromCore));
    }

    [Test]
    public void MissingCoreStillPicksATargetAndDoesNotThrow()
    {
        // No EnemyCore exists anywhere in this test, so the tie-break degrades.
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetObject(turret, "core", null);
        CreateFlyingEnemy(new Vector3(0f, 1f, 8f), new Vector3(0f, 1f, 8f), 0f);

        Assert.That(Object.FindAnyObjectByType<EnemyCore>(), Is.Null, "本用例必须没有核心。");
        Assert.DoesNotThrow(() => weapon.Advance(1f / 60f));
        Assert.That(turret.CurrentTarget, Is.Not.Null, "无核心时仍必须选中一个目标。");
    }

    // ---------------------------------------------------------------- R3 ----

    [Test]
    public void TurretAimsAtTheInterceptPoint()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 60f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        // A fast crossing target puts the lead point ~15 degrees off the enemy, so
        // "barrel is on the lead point" and "barrel is on the enemy" stay clearly
        // distinguishable outside the 8 degree firing tolerance.
        EnemyInstance enemy = CreateFlyingEnemy(new Vector3(0f, 1f, 10f), new Vector3(60f, 1f, 10f), 15f);

        for (int i = 0; i < 90; i++) weapon.Advance(1f / 60f);

        Assert.That(weapon.HasInterceptPoint, Is.True);
        Assert.That(weapon.IsUsingLead, Is.True, "直线飞行的目标必须能解出提前量。");
        Assert.That(turret.TryGetCurrentAimPoint(out Vector3 aim), Is.True,
            "炮塔必须记录到武器写入的提前量瞄准点。");
        Assert.That(Vector3.Distance(aim, weapon.InterceptPoint), Is.LessThan(0.05f),
            "R3: 记录给炮塔的瞄准点必须就是求解器给出的拦截点。");
        Assert.That(Vector3.Distance(aim, enemy.transform.position), Is.GreaterThan(0.05f),
            "R3: 瞄准点必须与敌人当前位置分离，证明打的是提前量而不是敌人本身。");

        // The mount itself must have swung onto the lead point, not onto the enemy:
        // this is what actually makes the missile leave efficiently.
        Vector3 muzzle = MuzzlePosition(turret);
        Vector3 barrel = turret.BarrelDirectionWorld;
        Assert.That(Vector3.Angle(barrel, weapon.InterceptPoint - muzzle), Is.LessThan(4f),
            "R3: 炮管必须真的指向拦截点。");
        Assert.That(Vector3.Angle(barrel, enemy.transform.position - muzzle), Is.GreaterThan(8f),
            "R3: 炮管不能只是瞄着敌人当前位置。");
    }

    [Test]
    public void InterceptPointLeadsTheTargetInsteadOfTrackingIt()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        EnemyInstance enemy = CreateFlyingEnemy(new Vector3(0f, 1f, 10f), new Vector3(0f, 1f, 30f), 2f);
        Vector3 velocity = new Vector3(0f, 0f, 2f);

        weapon.Advance(1f / 60f);

        Assert.That(weapon.HasInterceptPoint, Is.True);
        Vector3 lead = weapon.InterceptPoint;
        Assert.That(Vector3.Dot(lead - enemy.transform.position, velocity), Is.GreaterThan(0f),
            "R3: 拦截点必须在目标运动方向的前方。");
        Assert.That(Vector3.Distance(lead, enemy.transform.position), Is.GreaterThan(0.05f));

        Assert.That(weapon.TryGetInterceptPoint(enemy, out Vector3 solved, out float timeToImpact), Is.True);
        Assert.That(Vector3.Distance(solved, lead), Is.LessThan(0.05f), "两次求解必须一致。");
        Assert.That(timeToImpact, Is.GreaterThan(0f));
        Assert.That(timeToImpact, Is.EqualTo(Vector3.Distance(lead, turret.PitchPivot.position) / weapon.ProjectileSpeed)
            .Within(0.05f), "飞行时间应与「拦截点距离 / 弹速」一致。");
    }

    [Test]
    public void MissileLeavesAlongTheBarrelDirection()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        CreateFlyingEnemy(new Vector3(0f, 1f, 10f), new Vector3(0f, 1f, 10f), 0f);

        List<MissileProjectile> launched = AdvanceUntilMissiles(weapon, 1, 200);
        Assert.That(launched.Count, Is.GreaterThanOrEqualTo(1), "炮管对齐后必须发射导弹。");

        float alignment = Vector3.Dot(launched[0].transform.forward, turret.BarrelDirectionWorld.normalized);
        Assert.That(alignment, Is.GreaterThan(0.99f),
            "R3: 出膛方向必须沿发射瞬间的炮管方向（此时炮管已指向提前量）。");
    }

    [Test]
    public void LeadFallbackKeepsFiringWhenElevationExceedsPitchLimit()
    {
        // A fast head-on target pushes the intercept point above the 75 degree rail
        // limit while the enemy itself stays inside it (height 20, horizontal 6 ->
        // 72.5 degrees). The launcher must fall back to the enemy position and fire,
        // instead of freezing on a clamped pose and never shooting.
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 60f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        EnemyInstance enemy = CreateFlyingEnemy(new Vector3(0f, 20f, 6f), new Vector3(0f, 20f, -40f), 20f);
        Assert.That(enemy.GetComponent<MonsterPathFollower>().DestinationPosition.z, Is.LessThan(0f),
            "目标必须朝炮塔方向俯冲。");

        // Pre-elevate the rail to the enemy so the case tests the aim decision, not
        // the rotation time budget.
        turret.SnapAimAt(enemy.transform.position);

        List<MissileProjectile> launched = AdvanceUntilMissiles(weapon, 1, 400);

        Assert.That(weapon.HasInterceptPoint, Is.True);
        Assert.That(weapon.IsUsingLead, Is.False, "拦截点超仰角时必须退化瞄本体。");
        Assert.That(launched.Count, Is.GreaterThanOrEqualTo(1),
            "退化后仍必须能对齐并开火，不能出现永远不开火的死锁。");
        Assert.That(turret.CurrentElevationDegrees, Is.LessThanOrEqualTo(MissileLauncherTurret.MaxPitchDegrees + 0.5f),
            "退化后不得越界仰角。");
    }

    // ---------------------------------------------------------------- R1 ----

    [Test]
    public void AirOnlyTargetIsTheOnlyThingThatTriggersFiring()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        SetEnum(turret, "targetFilter", MissileTargetFilter.AirOnly);

        CreateGroundEnemy(new Vector3(0f, 0f, 8f));
        for (int i = 0; i < 120; i++) weapon.Advance(1f / 60f);

        Assert.That(turret.CurrentTarget, Is.Null, "R1: 地面怪不得被选为目标。");
        Assert.That(weapon.State, Is.EqualTo(MissileLauncherState.Idle));
        Assert.That(CountMissiles(), Is.Zero, "R1: 只有地面怪时不得发射任何导弹。");

        CreateFlyingEnemy(new Vector3(0f, 1f, 8f), new Vector3(0f, 1f, 8f), 0f);
        List<MissileProjectile> launched = AdvanceUntilMissiles(weapon, 1, 200);
        Assert.That(launched.Count, Is.GreaterThanOrEqualTo(1), "空中怪出现后必须开火。");
    }

    // ---------------------------------------------------------------- R4 ----

    [Test]
    public void SalvoLaunchesOneMissilePerTube()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetFloat(weapon, "launchInterval", 0.25f);
        SetFloat(weapon, "reloadTime", 5f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        CreateFlyingEnemy(new Vector3(0f, 1f, 10f), new Vector3(0f, 1f, 10f), 0f);

        Assert.That(weapon.MissilesPerSalvo, Is.EqualTo(2));

        List<MissileProjectile> launched = AdvanceUntilMissiles(weapon, 2, 200);
        Assert.That(launched.Count, Is.GreaterThanOrEqualTo(2), "齐射必须打满两管。");

        // The tubes sit on opposite rails, so the two missiles must leave from
        // different X offsets (they fly down +Z, X never changes).
        float firstX = launched[0].transform.position.x;
        float secondX = launched[1].transform.position.x;
        Assert.That(Mathf.Abs(firstX - secondX), Is.GreaterThan(0.1f),
            "R4: 左右两管必须各发一发，而不是两根管子共用一个发射点。");
        Assert.That(Mathf.Min(firstX, secondX), Is.EqualTo(LeftMuzzleX).Within(0.05f));
        Assert.That(Mathf.Max(firstX, secondX), Is.EqualTo(RightMuzzleX).Within(0.05f));
    }

    [Test]
    public void ReloadBlocksFurtherSalvos()
    {
        MissileLauncherTurret turret = CreateLauncher(Vector3.zero, out MissileLauncherWeapon weapon);
        SetFloat(turret, "detectionRange", 40f);
        SetFloat(weapon, "launchInterval", 0.1f);
        SetFloat(weapon, "reloadTime", 1.5f);
        SetFloat(weapon, "damagePerMissile", 1f);
        SetFloat(weapon, "splashDamageRatio", 0f);
        CreateFlyingEnemy(new Vector3(0f, 1f, 10f), new Vector3(0f, 1f, 10f), 0f);

        List<MissileProjectile> launched = AdvanceUntilMissiles(weapon, 2, 200);
        Assert.That(launched.Count, Is.GreaterThanOrEqualTo(2));

        Assert.That(weapon.IsReloading, Is.True, "R4: 打满齐射后必须进入装填。");
        Assert.That(weapon.State, Is.EqualTo(MissileLauncherState.Reloading));

        int beforeReloadTick = CountMissiles();
        for (int i = 0; i < 25; i++) weapon.Advance(1f / 60f);
        Assert.That(weapon.IsReloading, Is.True, "1.5 秒装填在 0.42 秒后必须还没结束。");
        Assert.That(CountMissiles(), Is.LessThanOrEqualTo(beforeReloadTick),
            "R4: 装填期内不得再发射新导弹。");

        List<MissileProjectile> afterReload = AdvanceUntilMissiles(weapon, launched.Count + 1, 600);
        Assert.That(afterReload.Count, Is.GreaterThan(launched.Count), "装填结束后必须能再次齐射。");
    }

    // ------------------------------------------------------------- R5/R6 ----

    [Test]
    public void MissileDetonatesPastMaxRangeAndStillDealsDamage()
    {
        EnemyInstance target = CreateFlyingEnemy(new Vector3(30f, 0f, 0f), new Vector3(30f, 0f, 0f), 0f);
        EnemyHealth health = target.GetComponent<EnemyHealth>();

        MissileProjectile missile = CreateMissileAtOrigin(Vector3.right);
        // Range 28 stops the missile 2 m short of the target: outside the 1.2 m
        // proximity fuse but inside the 2.5 m blast, so only the range detonation
        // can explain the damage.
        missile.ConfigureTuning(45f, 2.5f, 0.5f, 10f, 0f, 28f, MissileGuidanceMode.FrozenLeadShot, 0.1f);
        missile.Initialize(null, target, target.transform.position, Vector3.right);

        for (int i = 0; i < 200 && !missile.IsDetonated; i++) missile.Advance(0.05f);

        Assert.That(missile.IsDetonated, Is.True, "R5: 超出射程必须自爆，不能无声消失。");
        Assert.That(missile.TravelledDistance, Is.EqualTo(28f).Within(0.2f), "R5: 自爆点应落在射程处。");
        Assert.That(health.CurrentHealth, Is.EqualTo(100f - 45f * 0.5f).Within(0.01f),
            "R5: 自爆必须对范围内的敌人造成溅射伤害。");
    }

    [Test]
    public void ProjectileAppliesDamageWithoutACollider()
    {
        EnemyInstance target = CreateFlyingEnemy(new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 0f), 0f);
        Assert.That(target.GetComponentInChildren<Collider>(), Is.Null, "R6: 场景必须没有碰撞体才有效。");
        EnemyHealth health = target.GetComponent<EnemyHealth>();

        MissileProjectile missile = CreateMissileAtOrigin(Vector3.right);
        missile.ConfigureTuning(45f, 0f, 0f, 10f, 0f, 18f, MissileGuidanceMode.FrozenLeadShot, 0.1f);
        missile.Initialize(null, target, target.transform.position, Vector3.right);

        for (int i = 0; i < 200 && !missile.IsDetonated; i++) missile.Advance(0.05f);

        Assert.That(missile.IsDetonated, Is.True);
        Assert.That(missile.TravelledDistance, Is.LessThan(18f), "R6: 应靠引信在到达射程前起爆。");
        Assert.That(health.CurrentHealth, Is.EqualTo(100f - 45f).Within(0.01f),
            "R6: 无碰撞体的飞行怪必须仍被距离引信命中。");
    }

    // ---------------------------------------------------------------- R7 ----

    [Test]
    public void AuthoredPrefabCarriesWeaponAndMuzzles()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            MissileLauncherTurretAuthoring.LauncherPrefabPath);
        Assert.That(prefab, Is.Not.Null, "应先运行 Tools/塔防/生成导弹发射器预制体。");

        var instance = (GameObject)Object.Instantiate(prefab);
        objects.Add(instance);

        MissileLauncherWeapon weapon = instance.GetComponent<MissileLauncherWeapon>();
        Assert.That(weapon, Is.Not.Null, "生成的预制体必须带 MissileLauncherWeapon。");
        Assert.That(weapon.Turret, Is.EqualTo(instance.GetComponent<MissileLauncherTurret>()),
            "武器必须连到同一物体的机座。");

        Transform muzzleA = GetObject<Transform>(weapon, "muzzleA");
        Transform muzzleB = GetObject<Transform>(weapon, "muzzleB");
        Assert.That(muzzleA, Is.Not.Null, "muzzleA 必须已接线。");
        Assert.That(muzzleB, Is.Not.Null, "muzzleB 必须已接线。");
        Assert.That(muzzleA, Is.Not.SameAs(muzzleB), "左右两管必须是不同节点。");
    }

    // ------------------------------------------------------------ helpers ---

    private MissileLauncherTurret CreateLauncher(Vector3 position, out MissileLauncherWeapon weapon)
    {
        var root = new GameObject("Dual Missile Launcher");
        objects.Add(root);
        root.transform.position = position;

        // Mirror the authored prefab: model wrapper -> pivots -> meshes.
        var model = new GameObject("Launcher Model");
        model.transform.SetParent(root.transform, false);
        CreatePart("Dual_Missile_Launcher_Body", model.transform, new Vector3(0f, PivotHeight, 0f));
        CreateTube("Missile_01", model.transform, LeftMuzzleX);
        CreateTube("Missile_02", model.transform, RightMuzzleX);
        CreatePart("Launcher_Rotating_Pylon", model.transform, new Vector3(0f, 0.5f, 0f));
        CreatePart("Trap_Base_Disc", model.transform, Vector3.zero);

        MissileLauncherTurret turret = root.AddComponent<MissileLauncherTurret>();
        Assert.That(turret.BuildRig(), Is.True, "应从模型网格识别出机座与俯仰摇篮。");
        turret.CaptureRestPose();

        weapon = root.AddComponent<MissileLauncherWeapon>();
        SetObject(weapon, "turret", turret);
        SetObject(weapon, "muzzleA", turret.PitchPivot.Find("Missile_01"));
        SetObject(weapon, "muzzleB", turret.PitchPivot.Find("Missile_02"));
        Assert.That(GetObject<Transform>(weapon, "muzzleA"), Is.Not.Null, "造桩必须给出左发射口。");
        Assert.That(GetObject<Transform>(weapon, "muzzleB"), Is.Not.Null, "造桩必须给出右发射口。");
        InvokeLifecycle(weapon, "Awake");
        InvokeLifecycle(weapon, "OnEnable");
        return turret;
    }

    private static void CreateTube(string name, Transform parent, float x)
    {
        var tube = new GameObject(name);
        tube.transform.SetParent(parent, false);
        tube.transform.localPosition = new Vector3(x, PivotHeight, -0.5f);
        CreatePart(name + "_Body", tube.transform, Vector3.zero);
        CreatePart(name + "_Rounded_Nose", tube.transform, new Vector3(0f, 0f, 1f));
    }

    private static void CreatePart(string name, Transform parent, Vector3 localPosition)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
    }

    private EnemyInstance CreateFlyingEnemy(Vector3 position, Vector3 destination, float speed)
    {
        var go = new GameObject("Flying Monster");
        objects.Add(go);
        go.transform.position = position;
        EnemyInstance instance = go.AddComponent<EnemyInstance>();
        go.AddComponent<MonsterPathFollower>().InitializeFlying(null, destination, speed, 0f, null);
        EnsureHealth(go);
        return instance;
    }

    private EnemyInstance CreateGroundEnemy(Vector3 position)
    {
        var go = new GameObject("Ground Monster");
        objects.Add(go);
        go.transform.position = position;
        EnemyInstance instance = go.AddComponent<EnemyInstance>();
        go.AddComponent<MonsterPathFollower>();
        EnsureHealth(go);
        return instance;
    }

    private EnemyCore CreateCore(Vector3 position)
    {
        var go = new GameObject("Core");
        objects.Add(go);
        go.transform.position = position;
        return go.AddComponent<EnemyCore>();
    }

    private MissileProjectile CreateMissileAtOrigin(Vector3 direction)
    {
        var go = new GameObject("Test Missile");
        objects.Add(go);
        go.transform.position = Vector3.zero;
        MissileProjectile missile = go.AddComponent<MissileProjectile>();
        SetBool(missile, "drawDebugTracer", false);
        return missile;
    }

    private static void EnsureHealth(GameObject go)
    {
        EnemyHealth health = go.AddComponent<EnemyHealth>();
        InvokeLifecycle(health, "Awake");
    }

    /// <summary>Steps the weapon until it has launched <paramref name="count"/> missiles or the step budget runs out.</summary>
    private List<MissileProjectile> AdvanceUntilMissiles(MissileLauncherWeapon weapon, int count, int maxSteps)
    {
        var launched = new List<MissileProjectile>();
        var seen = new HashSet<MissileProjectile>();
        for (int step = 0; step < maxSteps && launched.Count < count; step++)
        {
            weapon.Advance(1f / 60f);
            foreach (MissileProjectile missile in Object.FindObjectsByType<MissileProjectile>(FindObjectsInactive.Include))
            {
                if (missile == null || !seen.Add(missile)) continue;
                objects.Add(missile.gameObject);
                launched.Add(missile);
            }
        }
        return launched;
    }

    private static Vector3 MuzzlePosition(MissileLauncherTurret turret) =>
        turret.PitchPivot != null ? turret.PitchPivot.position : turret.transform.position;

    private static int CountMissiles() =>
        Object.FindObjectsByType<MissileProjectile>(FindObjectsInactive.Include).Length;

    private static void InvokeLifecycle(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"缺少方法 {methodName}");
        method.Invoke(target, null);
    }

    private static void SetObject(Object target, string name, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Assert.That(property, Is.Not.Null, $"缺少序列化字段 {name}");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(Object target, string name, float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Assert.That(property, Is.Not.Null, $"缺少序列化字段 {name}");
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(Object target, string name, bool value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Assert.That(property, Is.Not.Null, $"缺少序列化字段 {name}");
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEnum(Object target, string name, System.Enum value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Assert.That(property, Is.Not.Null, $"缺少序列化字段 {name}");
        property.intValue = System.Convert.ToInt32(value);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T GetObject<T>(Object target, string name) where T : Object
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        return property != null ? property.objectReferenceValue as T : null;
    }
}
#endif