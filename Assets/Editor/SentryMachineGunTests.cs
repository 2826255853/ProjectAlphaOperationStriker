#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Stage-six regression for the sentry machine gun: the shipped trap definition is
/// untouched, the generated art prefab fits its 2x2 cell and keeps the name contract,
/// and the procedural fire feedback / casing arc / laser sight behave the way the
/// offline acceptance run (SentryMachineGunStage5Validation) measured.
///
/// Run with:
///   Assets/Editor/RunSentryMachineGunTests.ps1
/// or -runTests -testPlatform EditMode -testFilter SentryMachineGunTests
/// plus -extraScriptingDefines=UNITY_INCLUDE_TESTS (the whole file is compiled out otherwise).
/// </summary>
public sealed class SentryMachineGunTests
{
    private const string ArtPrefabPath = "Assets/Resources/SentryMachineGun.prefab";
    private const string CasingPrefabPath = "Assets/Prefabs/SentryShellCasing.prefab";
    private const string ShippedTrapPrefabPath = "Assets/Prefabs/AutoSentryTurret.prefab";
    private const string DefinitionResourcesPath = "Assets/Resources/AutoSentryTurret.asset";
    private const string DefinitionTrapPath = "Assets/TrapDefinitions/AutoSentryTurret.asset";
    private const string UrpLit = "Universal Render Pipeline/Lit";
    private const string UrpUnlit = "Universal Render Pipeline/Unlit";
    private const float FixedStep = 1f / 60f;

    private readonly List<Object> owned = new List<Object>();
    private int shotCount;

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
        shotCount = 0;
    }

    // --- 1. 占格与定义不变 -------------------------------------------------

    [Test]
    public void ShippedDefinitionsKeepTheTwoByTwoFootprint()
    {
        var fromResources = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionResourcesPath);
        var fromTrapDefinitions = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionTrapPath);
        Assert.That(fromResources, Is.Not.Null, "Resources/AutoSentryTurret.asset 必须存在。");
        Assert.That(fromTrapDefinitions, Is.Not.Null, "TrapDefinitions/AutoSentryTurret.asset 必须存在。");

        var shippedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShippedTrapPrefabPath);
        Assert.That(shippedPrefab, Is.Not.Null, "出厂陷阱预制体必须能导入为 GameObject（见 SentryTrapPrefabRepair）。");

        foreach (TrapDefinition definition in new[] { fromResources, fromTrapDefinitions })
        {
            Assert.That(definition.TrapId, Is.EqualTo("auto_sentry_turret"), "trapId 不能改。");
            Assert.That(definition.Footprint, Is.EqualTo(new Vector2Int(2, 2)), "占格必须仍是 2x2。");
            Assert.That(definition.Prefab, Is.Not.Null, "定义必须解析到预制体。");
            Assert.That(definition.Prefab, Is.SameAs(shippedPrefab), "两份定义应指向同一个出厂预制体。");
        }
    }

    // --- 2. 模型尺寸 -------------------------------------------------------

    [Test]
    public void ArtPrefabFitsTheTwoByTwoCell()
    {
        GameObject instance = InstantiateArt();
        Bounds bounds = UnionBounds(instance);
        Vector3 size = bounds.size;

        Assert.That(size.y, Is.LessThanOrEqualTo(1.70f),
            $"机枪水平姿态总高必须 ≤ 1.70 m，实测 {size.y:0.000} m。");
        Assert.That(size.y, Is.GreaterThan(0.5f), "总高太小，可能只算到部分网格。");
        Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(-0.01f),
            $"脚底必须贴地（y ≥ -0.01），实测 {bounds.min.y:0.000} m。");
        Assert.That(bounds.min.y, Is.LessThanOrEqualTo(0.02f), "脚底不能悬空。");
        Assert.That(size.x, Is.LessThanOrEqualTo(2.00f), $"水平包围盒 X ≤ 2.00 m，实测 {size.x:0.000} m。");
        Assert.That(size.z, Is.LessThanOrEqualTo(2.00f), $"水平包围盒 Z ≤ 2.00 m，实测 {size.z:0.000} m。");
    }

    // --- 3. 层级契约 -------------------------------------------------------

    [Test]
    public void ArtPrefabKeepsTheContractHierarchy()
    {
        GameObject instance = InstantiateArt();

        foreach (string node in new[] { "Aim Pivot", "Muzzle", "Gun Recoil", "Ejection Port", "Laser Emitter" })
            Assert.That(CountNamed(instance.transform, node), Is.EqualTo(1),
                $"契约节点必须唯一存在：{node}。");

        Transform pivot = FindNamed(instance.transform, "Aim Pivot");
        Transform muzzle = FindNamed(instance.transform, "Muzzle");
        Transform recoil = FindNamed(instance.transform, "Gun Recoil");
        Transform port = FindNamed(instance.transform, "Ejection Port");
        Transform emitter = FindNamed(instance.transform, "Laser Emitter");

        Assert.That(pivot.localPosition.y, Is.EqualTo(0.62f).Within(0.02f), "Aim Pivot 应落在 (0, 0.62, 0)。");
        Assert.That(muzzle.IsChildOf(pivot), Is.True, "Muzzle 必须在 Aim Pivot 之下。");
        Assert.That(muzzle.IsChildOf(recoil), Is.True, "Muzzle 必须在 Gun Recoil 之下，才能跟着后坐后退。");
        Assert.That(port.IsChildOf(pivot), Is.True, "Ejection Port 必须跟着 yaw 一起转。");
        Assert.That(emitter.IsChildOf(pivot), Is.True, "Laser Emitter 必须跟着 yaw 一起转。");

        // Rest pose: pivot yaw is zero and the barrel points at +Z.
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.5f),
            "静止时 Aim Pivot 的 yaw 必须为 0。");
        Assert.That(Vector3.Dot(pivot.forward, Vector3.forward), Is.GreaterThan(0.99f),
            "静止时枪口必须朝 +Z。");

        Vector3 muzzleLocal = pivot.InverseTransformPoint(muzzle.position);
        Assert.That(muzzleLocal.z, Is.GreaterThan(0.5f), "Muzzle 必须在 pivot 前方（局部 +Z）。");
        Assert.That(muzzleLocal.y, Is.GreaterThan(0f), "Muzzle 应高于支点。");
        Assert.That(port.localPosition.x, Is.GreaterThan(0.05f), "Ejection Port 必须在右侧（+X）。");
        Assert.That(emitter.localPosition.y, Is.GreaterThan(muzzleLocal.y - 0.2f), "激光镜筒应在机匣上方。");

        foreach (string leg in new[] { "Leg_FL", "Leg_FR", "Leg_RL", "Leg_RR" })
        {
            Transform node = FindNamed(instance.transform, leg);
            Assert.That(node, Is.Not.Null, $"四脚支架缺件：{leg}。");
            Assert.That(node.childCount, Is.EqualTo(4), $"{leg} 应有 4 个零件（Leg / Foot / Foot_Sole / Brace）。");
        }
    }

    // --- 4. 材质与物理 -----------------------------------------------------

    [Test]
    public void ArtPrefabUsesUrpMaterialsAndCarriesNoPhysics()
    {
        GameObject instance = InstantiateArt();
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers.Length, Is.GreaterThan(10), "美术预制体应包含多个网格渲染器。");

        foreach (Renderer renderer in renderers)
        {
            Assert.That(renderer.sharedMaterial, Is.Not.Null,
                $"渲染器 {renderer.name} 没有材质（会渲染成品红）。");
            string shader = renderer.sharedMaterial.shader.name;
            Assert.That(shader == UrpLit || shader == UrpUnlit,
                $"渲染器 {renderer.name} 用了非 URP 着色器：{shader}。");
        }

        Assert.That(instance.GetComponentsInChildren<Collider>(true).Length, Is.Zero,
            "陷阱惯例：美术不加 Collider（血条走近射线兜底）。");
        Assert.That(instance.GetComponentsInChildren<Rigidbody>(true).Length, Is.Zero, "美术不应带刚体。");
        Assert.That(instance.GetComponentsInChildren<Animator>(true).Length, Is.Zero,
            "本轮不做 AnimationClip / Animator。");
    }

    [Test]
    public void CasingPrefabIsAPureVisual()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CasingPrefabPath);
        Assert.That(prefab, Is.Not.Null, "弹壳预制体必须存在。");

        var instance = (GameObject)Object.Instantiate(prefab);
        owned.Add(instance);

        // The casing may be a single mesh or a small hierarchy (body + rim), so the check is
        // "nothing but pure visual components anywhere", not "exactly three on the root".
        Assert.That(instance.GetComponentsInChildren<MeshFilter>(true).Length, Is.GreaterThan(0),
            "弹壳需要网格。");
        Assert.That(instance.GetComponentsInChildren<MeshRenderer>(true).Length, Is.GreaterThan(0),
            "弹壳需要渲染器。");
        foreach (Component component in instance.GetComponentsInChildren<Component>(true))
        {
            Assert.That(component, Is.InstanceOf<Transform>()
                                   .Or.InstanceOf<MeshFilter>()
                                   .Or.InstanceOf<MeshRenderer>(),
                $"弹壳预制体只应有 Transform / MeshFilter / MeshRenderer，实际出现 {component.GetType().Name}。");
        }
        Assert.That(instance.GetComponentsInChildren<Collider>(true).Length, Is.Zero,
            "弹壳不加 Collider，落地由脚本判定。");
    }

    // --- 5. 后坐与枪口焰 ---------------------------------------------------

    [Test]
    public void RecoilCurvePeaksEarlyAndReturnsExactlyHome()
    {
        GameObject instance = InstantiateArt();
        var feedback = instance.GetComponent<SentryFireFeedback>();
        Assert.That(feedback, Is.Not.Null, "美术根必须带 SentryFireFeedback。");
        feedback.EnsureInitialized();

        Assert.That(feedback.EvaluateRecoil01(0f), Is.EqualTo(0f), "进度 0 时后坐为 0。");
        Assert.That(feedback.EvaluateRecoil01(1f), Is.EqualTo(0f), "进度 1 时后坐必须精确归零。");

        float peak = 0f;
        float peakAt = -1f;
        for (int i = 0; i <= 200; i++)
        {
            float progress = i / 200f;
            float value = feedback.EvaluateRecoil01(progress);
            if (value <= peak) continue;
            peak = value;
            peakAt = progress;
        }

        Assert.That(peak, Is.GreaterThan(0.99f), "后坐曲线应达到满幅。");
        Assert.That(peakAt, Is.InRange(0.1f, 0.4f), $"后坐峰值应落在 0.1–0.4，实测 {peakAt:0.00}。");

        Transform recoil = feedback.RecoilRoot;
        Vector3 restPosition = recoil.localPosition;
        Quaternion restRotation = recoil.localRotation;
        Assert.That(Vector3.Distance(restPosition, Vector3.zero), Is.LessThan(1e-4f),
            "Gun Recoil 静止时必须零偏移。");

        feedback.PlayShot();
        Assert.That(feedback.IsRecoiling, Is.True, "PlayShot 应启动后坐。");
        Assert.That(feedback.MuzzleFlash.activeSelf, Is.True, "开火帧应显示枪口焰。");

        feedback.Tick(feedback.SingleShotDuration * 0.18f);
        Assert.That(Vector3.Distance(recoil.localPosition, restPosition + Vector3.back * feedback.RecoilDistance),
            Is.LessThan(1e-4f), "峰值时枪身应沿局部 -Z 后退 0.04 m。");
        Assert.That(Quaternion.Angle(recoil.localRotation, restRotation),
            Is.EqualTo(feedback.RecoilLiftDegrees).Within(0.05f), "峰值时应抬枪 3°。");

        feedback.Tick(0.05f);
        Assert.That(feedback.MuzzleFlash.activeSelf, Is.False, "枪口焰应在约 0.045 s 后消失。");

        feedback.Tick(feedback.SingleShotDuration);
        Assert.That(feedback.IsRecoiling, Is.False, "后坐周期应结束。");
        Assert.That(recoil.localPosition, Is.EqualTo(restPosition), "Tick 结束后必须精确复位位置。");
        Assert.That(Quaternion.Angle(recoil.localRotation, restRotation), Is.LessThan(1e-4f),
            "Tick 结束后必须精确复位旋转。");
    }

    // --- 6. 开火事件与弹壳池 -----------------------------------------------

    [Test]
    public void EachRoundRaisesShotFiredAndFeedsTheCasingPool()
    {
        AutoSentryTurret turret = CreateTurret();
        var ejector = PrepareEjector(turret);
        Assert.That(ejector, Is.Not.Null, "美术根必须带 SentryShellEjector。");

        CreateEnemy(new Vector3(0f, 0f, 2.5f), 100000f);
        int rounds = Advance(turret, 0.4f);

        Assert.That(rounds, Is.GreaterThan(0), "有目标时应开火。");
        Assert.That(shotCount, Is.EqualTo(rounds), $"ShotFired 必须每发触发一次（{shotCount} / {rounds}）。");
        Assert.That(ejector.ActiveCasingCount, Is.EqualTo(rounds),
            "每发应抛出一枚弹壳，且弹壳挂在炮塔根（不随 yaw）。");

        // Long burst + cooldown + resumed burst: the pool must cap, not grow.
        Advance(turret, 6f);
        Assert.That(ejector.ActiveCasingCount, Is.LessThanOrEqualTo(ejector.PoolSize),
            $"活跃弹壳不得超过池上限 {ejector.PoolSize}。");
        Transform pool = FindNamed(turret.transform, "Shell Casings");
        Assert.That(pool, Is.Not.Null, "弹壳池应挂在美术根下。");
        Assert.That(pool.childCount, Is.EqualTo(ejector.PoolSize), "池对象数应固定为池上限。");
    }

    // --- 7. 弹壳抛物线 -----------------------------------------------------

    [Test]
    public void CasingsFlyOutOnAnArcAndExpire()
    {
        AutoSentryTurret turret = CreateTurret();
        var ejector = PrepareEjector(turret);
        Assert.That(ejector.ActiveCasingCount, Is.Zero, "池初始为空。");
        Assert.That(ejector.EjectionPort.name, Is.EqualTo("Ejection Port"), "抛壳口必须是 Ejection Port。");

        ejector.Eject();
        Assert.That(ejector.ActiveCasingCount, Is.EqualTo(1), "一次 Eject 应产出一枚弹壳。");

        Transform pool = FindNamed(turret.transform, "Shell Casings");
        Transform casing = pool.GetChild(0);
        Assert.That(Vector3.Distance(casing.position, ejector.EjectionPort.position), Is.LessThan(1e-3f),
            "弹壳应从抛壳口出膛。");

        Vector3 start = casing.position;
        Quaternion startRotation = casing.rotation;
        float maxY = start.y;
        Vector3 last = start;
        bool spun = false;
        bool recycled = false;
        float elapsed = 0f;

        for (int i = 0; i < 240; i++)
        {
            ejector.Step(FixedStep);
            elapsed += FixedStep;
            if (ejector.ActiveCasingCount == 0)
            {
                recycled = true;
                break;
            }

            last = casing.position;
            maxY = Mathf.Max(maxY, last.y);
            if (!spun && Quaternion.Angle(casing.rotation, startRotation) > 5f) spun = true;
        }

        Assert.That(recycled, Is.True, "弹壳应被回收，不能一直存在。");
        Assert.That(elapsed, Is.InRange(0.4f, 1.0f), $"落地回收应在 0.4–1.0 s（实测 {elapsed:0.00} s）。");
        Assert.That(maxY, Is.GreaterThan(start.y + 0.15f),
            $"弹壳应先升后降（顶点 +{maxY - start.y:0.000} m）。");
        Assert.That(last.y, Is.LessThan(maxY - 0.05f), "顶点之后必须下降。");
        Assert.That(last.x - start.x, Is.GreaterThan(0.1f), "弹壳必须抛向右侧（+X）。");
        Assert.That(spun, Is.True, "弹壳飞行时应自旋。");
        Assert.That(pool.GetChild(0).gameObject.activeSelf, Is.False, "回收后的弹壳对象应停用。");

        // Timeout path: a casing that never reaches the floor still expires at 1.6 s.
        ejector.Eject();
        Assert.That(ejector.ActiveCasingCount, Is.EqualTo(1));
        ejector.Step(2f);
        Assert.That(ejector.ActiveCasingCount, Is.Zero, "超时也必须回收弹壳。");
    }

    // --- 8/9. 激光瞄准器 ---------------------------------------------------

    [Test]
    public void LaserBeamIsARuntimeTwoPointLine()
    {
        AutoSentryTurret turret = CreateTurret();
        var laser = turret.GetComponentInChildren<SentryLaserSight>(true);
        Assert.That(laser, Is.Not.Null, "美术根必须带 SentryLaserSight。");
        Assert.That(laser.LaserEmitter.name, Is.EqualTo("Laser Emitter"), "激光起点必须是 Laser Emitter。");

        laser.EnsureBeam();
        var line = laser.GetComponentInChildren<LineRenderer>(true);
        Assert.That(line, Is.Not.Null, "运行时才建 LineRenderer。");
        Assert.That(line.positionCount, Is.EqualTo(2), "激光必须是两点线段。");
        Assert.That(line.useWorldSpace, Is.True, "激光用世界坐标。");

        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.True, "炮塔启用时激光可见。");
        Assert.That(laser.CurrentBeamLength, Is.GreaterThan(0f), "激光应有长度。");
        Assert.That(laser.CurrentBeamLength, Is.LessThanOrEqualTo(laser.MaxDistance + 1e-3f),
            "激光长度必须受上限约束。");

        Vector3 origin = laser.LaserEmitter.position;
        Vector3 direction = laser.LaserEmitter.forward;
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Sentry Laser Test Blocker";
        blocker.transform.position = origin + direction * 3f;
        blocker.transform.localScale = Vector3.one * 0.2f;
        owned.Add(blocker);
        Physics.SyncTransforms();
        laser.UpdateBeam();
        Assert.That(laser.CurrentBeamLength, Is.LessThanOrEqualTo(3.25f),
            $"激光应被 3 m 处的方块截断（实测 {laser.CurrentBeamLength:0.00} m）。");
        Assert.That(laser.CurrentBeamLength, Is.LessThan(laser.MaxDistance),
            "被几何截断的激光必须短于上限，不能画穿方块。");
    }

    [Test]
    public void LaserBeamHidesWhileTheTurretIsDisabled()
    {
        AutoSentryTurret turret = CreateTurret();
        var laser = turret.GetComponentInChildren<SentryLaserSight>(true);
        laser.EnsureBeam();

        turret.enabled = false;
        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.False, "炮塔禁用时（放置预览的幽灵）激光必须隐藏。");
        Assert.That(laser.CurrentBeamLength, Is.EqualTo(0f), "隐藏的激光长度归零。");

        turret.enabled = true;
        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.True, "重新启用后激光恢复。");
    }

    // --- 10. 旧功能回归 ----------------------------------------------------

    [Test]
    public void TargetAcquisitionFeedbackUsesFixedPulseAndReacquiresAfterLoss()
    {
        AutoSentryTurret turret = CreateTurret();
        var laser = turret.GetComponentInChildren<SentryLaserSight>(true);
        var feedback = turret.GetComponentInChildren<SentryTargetingFeedback>(true);
        Assert.That(laser, Is.Not.Null);
        Assert.That(feedback, Is.Not.Null);
        Invoke(laser, "OnEnable");
        Invoke(feedback, "OnEnable");

        int acquisitions = 0;
        turret.TargetAcquired += _ => acquisitions++;
        EnemyHealth victim = CreateEnemy(new Vector3(0f, 0f, 2.5f), 100000f);
        turret.Tick(0.01f);
        Assert.That(acquisitions, Is.EqualTo(1));
        Assert.That(laser.IsTargetingPulseActive, Is.True, "目标获取事件应启动激光脉冲。");
        Assert.That(feedback.IsSequenceActive, Is.True, "目标获取事件应启动滴声序列。");
        Assert.That(feedback.BeepStartCount, Is.EqualTo(1));
        turret.Tick(0.1f);
        Assert.That(acquisitions, Is.EqualTo(1));

        laser.BeginTargetingPulse();
        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.True);
        laser.TickTargetingPulse(0.1f);
        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.False);
        laser.TickTargetingPulse(0.1f);
        laser.UpdateBeam();
        Assert.That(laser.IsBeamVisible, Is.True);
        laser.TickTargetingPulse(0.1f);
        laser.UpdateBeam();
        Assert.That(laser.IsTargetingPulseActive, Is.False);
        Assert.That(laser.IsBeamVisible, Is.True);

        feedback.Tick(0.1f);
        feedback.Tick(0.1f);
        Assert.That(feedback.BeepStartCount, Is.EqualTo(2));
        feedback.Tick(0.1f);
        Assert.That(feedback.IsSequenceActive, Is.False);

        Object.DestroyImmediate(victim.gameObject);
        turret.Tick(0.01f);
        Assert.That(turret.CurrentTarget, Is.Null);
        CreateEnemy(new Vector3(0f, 0f, 2.5f), 100000f);
        turret.Tick(0.01f);
        Assert.That(acquisitions, Is.EqualTo(2));
    }

    [Test]
    public void TargetAcquisitionFeedbackCanBeDisabled()
    {
        AutoSentryTurret turret = CreateTurret();
        var serialized = new SerializedObject(turret);
        serialized.FindProperty("targetingFeedbackEnabled").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        int acquisitions = 0;
        turret.TargetAcquired += _ => acquisitions++;
        CreateEnemy(new Vector3(0f, 0f, 2.5f), 100000f);
        turret.Tick(0.01f);
        Assert.That(acquisitions, Is.Zero);
    }

    [Test]
    public void TurretStillLocksOnAndDealsDamage()
    {
        AutoSentryTurret turret = CreateTurret();
        Assert.That(turret.AimPivot, Is.Not.Null, "Aim Pivot 应从美术解析出来。");
        Assert.That(turret.Muzzle, Is.Not.Null, "Muzzle 应从美术解析出来。");
        Assert.That(turret.Muzzle.IsChildOf(turret.AimPivot), Is.True, "Muzzle 在 Aim Pivot 下。");

        EnemyHealth victim = CreateEnemy(new Vector3(0f, 0f, 2.5f), 100000f);
        float startHealth = victim.CurrentHealth;
        int rounds = Advance(turret, 1f);

        Assert.That(rounds, Is.InRange(6, 10), $"1 s 内应约 8 发（实测 {rounds}）。");
        Assert.That(turret.CurrentTarget, Is.Not.Null, "炮塔应锁定目标。");
        Assert.That(startHealth - victim.CurrentHealth, Is.EqualTo(rounds * 12f).Within(0.01f),
            "每发 12 伤害，结算链路不变。");

        // Target moves to the right flank: yaw must follow.
        victim.transform.position = new Vector3(2.5f, 0f, 0f);
        Advance(turret, 0.8f);
        Assert.That(Vector3.Dot(turret.AimPivot.forward, Vector3.right), Is.GreaterThan(0.99f),
            "Aim Pivot 应转向右翼目标。");

        Object.DestroyImmediate(victim.gameObject);
        Advance(turret, 0.1f);
        Assert.That(turret.CurrentTarget, Is.Null, "目标消失后应清空锁定。");
    }

    // --- 工具 --------------------------------------------------------------

    private GameObject InstantiateArt()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabPath);
        Assert.That(prefab, Is.Not.Null, "缺少美术预制体：" + ArtPrefabPath + "（先跑 SentryMachineGunAuthoring.CreateAssetsBatch）。");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        owned.Add(instance);
        return instance;
    }

    /// <summary>
    /// Builds a live turret root. <see cref="AutoSentryTurret"/> is [ExecuteAlways], so adding
    /// the component already runs Awake and instantiates the art; calling it again is a no-op
    /// guarded by <c>visualsBuilt</c>, which keeps this identical to the shipped prefab path.
    /// </summary>
    private AutoSentryTurret CreateTurret()
    {
        var root = new GameObject("Sentry Test Turret");
        owned.Add(root);
        var turret = root.AddComponent<AutoSentryTurret>();
        turret.ShotFired += _ => shotCount++;
        Invoke(turret, "Awake");
        Assert.That(root.transform.Find("Sentry Machine Gun Art"), Is.Not.Null,
            "Awake 应从 Resources 实例化美术。");
        return turret;
    }

    /// <summary>
    /// EditMode does not dispatch OnEnable for plain MonoBehaviours, so the ejector is
    /// woken explicitly - the same thing the batch acceptance run does.
    /// </summary>
    private SentryShellEjector PrepareEjector(AutoSentryTurret turret)
    {
        var ejector = turret.GetComponentInChildren<SentryShellEjector>(true);
        if (ejector == null) return null;
        Invoke(ejector, "Awake");
        Invoke(ejector, "OnEnable");
        return ejector;
    }

    private EnemyHealth CreateEnemy(Vector3 position, float maxHealth)
    {
        var enemy = new GameObject("Sentry Test Victim");
        enemy.transform.position = position;
        owned.Add(enemy);
        enemy.AddComponent<EnemyInstance>();
        EnemyHealth health = enemy.AddComponent<EnemyHealth>();

        var serialized = new SerializedObject(health);
        SerializedProperty property = serialized.FindProperty("maxHealth");
        if (property != null)
        {
            property.floatValue = maxHealth;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        health.ResetHealth();
        return health;
    }

    /// <summary>Advances the turret with fixed steps and returns the rounds fired.</summary>
    private int Advance(AutoSentryTurret turret, float seconds)
    {
        int before = shotCount;
        int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / FixedStep));
        for (int i = 0; i < steps; i++) turret.Tick(FixedStep);
        return shotCount - before;
    }

    private static Bounds UnionBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        Assert.That(renderers.Length, Is.GreaterThan(0), "美术预制体没有任何渲染器。");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static int CountNamed(Transform root, string name)
    {
        int count = 0;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) count++;
        return count;
    }

    private static Transform FindNamed(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static void Invoke(object target, string methodName)
    {
        if (target == null) return;
        MethodInfo method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) return;
        method.Invoke(target, null);
    }
}
#endif
