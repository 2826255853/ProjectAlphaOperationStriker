#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode coverage for the missile launcher rotation rig.
/// Verifies that yaw wraps a full 360 degrees while pitch is clamped to the
/// 0..75 degree band the launcher rails allow.
/// </summary>
public sealed class MissileLauncherTurretTests
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
    public void YawRotatesFullCircleWhilePitchStopsAtSeventyFiveDegrees()
    {
        MissileLauncherTurret turret = CreateTurret(new Vector3(0f, 2f, 0f));

        turret.SnapAimAt(turret.PitchPivot.position + Vector3.forward * 10f);
        Assert.That(turret.CurrentElevationDegrees, Is.EqualTo(0f).Within(0.5f), "水平目标应保持 0 度。");

        turret.SnapAimAt(turret.PitchPivot.position + new Vector3(0f, 40f, 0.0001f));
        Assert.That(turret.CurrentElevationDegrees, Is.EqualTo(MissileLauncherTurret.MaxPitchDegrees).Within(0.5f),
            "高于 75 度的目标必须被夹到 75 度。");

        turret.SnapAimAt(turret.PitchPivot.position + new Vector3(0f, -30f, 10f));
        Assert.That(turret.CurrentElevationDegrees, Is.EqualTo(0f).Within(0.5f), "低于地平线的目标不能把炮口压到 0 度以下。");

        // Walk a full circle and confirm the yaw wraps instead of clamping.
        var seen = new List<float>();
        for (int i = 0; i < 8; i++)
        {
            float yaw = i * 45f;
            Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            turret.SnapAimAt(turret.PitchPivot.position + direction * 10f);
            seen.Add(Mathf.DeltaAngle(0f, turret.CurrentYawDegrees));
        }

        Assert.That(seen[2], Is.EqualTo(90f).Within(1f), "90 度方位角应可达。");
        Assert.That(seen[4], Is.EqualTo(180f).Within(1f), "180 度方位角应可达（证明水平旋转是 360 度而不是 180 度夹紧）。");
        Assert.That(seen[6], Is.EqualTo(-90f).Within(1f), "270 度方位角应可达。");
    }

    [Test]
    public void ClampPitchDegreesAlwaysStaysInsideTheAllowedBand()
    {
        Assert.That(MissileLauncherTurret.ClampPitchDegrees(-45f), Is.EqualTo(0f));
        Assert.That(MissileLauncherTurret.ClampPitchDegrees(30f), Is.EqualTo(30f));
        Assert.That(MissileLauncherTurret.ClampPitchDegrees(400f), Is.EqualTo(75f));
    }

    [Test]
    public void ConfigurePitchLimitsNeverLeavesTheHardBand()
    {
        MissileLauncherTurret turret = CreateTurret(Vector3.zero);
        turret.ConfigurePitchLimits(-20f, 120f);
        Assert.That(turret.MinPitchLimit, Is.EqualTo(0f));
        Assert.That(turret.MaxPitchLimit, Is.EqualTo(75f));
    }

    [Test]
    public void AuthoredPrefabHasARotatingRigWiredToThePitchPivot()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            MissileLauncherTurretAuthoring.LauncherPrefabPath);
        Assert.That(prefab, Is.Not.Null, "应先运行 Tools/塔防/生成导弹发射器预制体。");

        var instance = (GameObject)Object.Instantiate(prefab);
        objects.Add(instance);
        MissileLauncherTurret turret = instance.GetComponent<MissileLauncherTurret>();
        Assert.That(turret, Is.Not.Null);
        Assert.That(turret.HasRig, Is.True, "预制体里的 Yaw/Pitch 枢轴引用必须已连线。");
        Assert.That(turret.PitchPivot.parent, Is.EqualTo(turret.YawPivot), "Pitch Pivot 必须是 Yaw Pivot 的子级。");
        Assert.That(turret.RestBarrelDirectionWorld.y, Is.EqualTo(0f).Within(1f), "静止时炮口应保持水平。");

        turret.SnapAimAt(turret.PitchPivot.position + new Vector3(0.0001f, 50f, 0f));
        Assert.That(turret.CurrentElevationDegrees,
            Is.EqualTo(MissileLauncherTurret.MaxPitchDegrees).Within(1f));

        // Aim exactly opposite to the rest direction: only a full 360 degree yaw
        // range can reach this. A 180 degree clamp would stop 90 degrees short.
        turret.SnapAimAt(turret.PitchPivot.position - turret.RestBarrelDirectionWorld * 10f);
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, turret.CurrentYawDegrees)), Is.EqualTo(180f).Within(2f),
            "水平方向应能转到静止朝向的反方向，证明是 360 度旋转。");
    }

    [Test]
    public void RawFbxModelCanBuildItsOwnRigAtRuntime()
    {
        GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            MissileLauncherTurretAuthoring.LauncherModelPath);
        Assert.That(model, Is.Not.Null, "找不到发射器模型 FBX。");

        var instance = (GameObject)Object.Instantiate(model);
        objects.Add(instance);
        MissileLauncherTurret turret = instance.AddComponent<MissileLauncherTurret>();
        Assert.That(turret.BuildRig(), Is.True, "原始 FBX 也应能自动搭出 Yaw/Pitch 机座。");

        Transform pylon = turret.YawPivot.Find("Launcher_Rotating_Pylon");
        Assert.That(pylon, Is.Not.Null, "机座圆柱应被挂到 Yaw Pivot 下。");
        Assert.That(instance.transform.Find("Trap_Base_Disc"), Is.Not.Null, "底座必须留在原地不跟着转。");

        Transform bridge = turret.PitchPivot.Find("Dual_Missile_Launcher_Body");
        Assert.That(bridge, Is.Not.Null, "发射箱应被挂到 Pitch Pivot 下。");

        turret.SnapAimAt(turret.PitchPivot.position + new Vector3(15f, -15f, 0f));
        Assert.That(turret.CurrentElevationDegrees, Is.EqualTo(0f).Within(1f), "俯仰不得低于 0 度。");
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, turret.CurrentYawDegrees)), Is.GreaterThan(45f), "应能水平转向 90 度方位。");
    }

    [Test]
    public void AirFilterIgnoresGroundMonstersAndKeepsFlyingOnes()
    {
        Assert.That(MissileLauncherTurret.IsAirTarget(null), Is.False);

        var ground = new GameObject("Ground Monster");
        objects.Add(ground);
        EnemyInstance groundInstance = ground.AddComponent<EnemyInstance>();
        ground.AddComponent<MonsterPathFollower>();
        Assert.That(MissileLauncherTurret.IsAirTarget(groundInstance), Is.False,
            "地面怪物不应被双联导弹发射器当作目标。");

        var flyer = new GameObject("Flying Monster");
        objects.Add(flyer);
        EnemyInstance flyerInstance = flyer.AddComponent<EnemyInstance>();
        flyer.AddComponent<MonsterPathFollower>().InitializeFlying(null, new Vector3(0f, 12f, 0f), 2f, 3f, null);
        Assert.That(MissileLauncherTurret.IsAirTarget(flyerInstance), Is.True,
            "处在飞行移动模式的怪物必须仍被识别为空中目标。");

        Assert.That(MissileLauncherTurret.IsAirTarget(groundInstance), Is.False, "空中判定不得污染地面怪物。");
    }
    private MissileLauncherTurret CreateTurret(Vector3 position)
    {
        var root = new GameObject("Dual Missile Launcher");
        objects.Add(root);
        root.transform.position = position;

        // Mirror the authored prefab: model wrapper -> pivots -> meshes.
        var model = new GameObject("Launcher Model");
        model.transform.SetParent(root.transform, false);
        CreatePart("Dual_Missile_Launcher_Body", model.transform, new Vector3(0f, 1f, 0f));
        CreatePart("Missile_01_Body", model.transform, new Vector3(0f, 1f, -0.5f));
        CreatePart("Missile_01_Rounded_Nose", model.transform, new Vector3(0f, 1f, 0.5f));
        CreatePart("Launcher_Rotating_Pylon", model.transform, new Vector3(0f, 0.5f, 0f));
        CreatePart("Trap_Base_Disc", model.transform, Vector3.zero);

        MissileLauncherTurret turret = root.AddComponent<MissileLauncherTurret>();
        Assert.That(turret.BuildRig(), Is.True, "应从模型网格识别出机座与俯仰摇篮。");
        Assert.That(turret.HasRig, Is.True);
        turret.CaptureRestPose();
        return turret;
    }

    private static void CreatePart(string name, Transform parent, Vector3 localPosition)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
    }
}
#endif
