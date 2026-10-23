#if UNITY_INCLUDE_TESTS && ENABLE_DRONE_CODE
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// DroneCameraController EditMode 测试。
/// 自行添加虚拟鼠标设备，并用 delta 状态事件模拟输入。
/// 不继承 InputTestFixture：它属于 Input System 的测试框架程序集
/// Unity.InputSystem.TestFramework，本工程的 Assembly-CSharp-Editor 没有引用该程序集。
///
/// 运行：Unity -runTests -testPlatform EditMode -testFilter DroneCameraControllerTests
/// </summary>
[TestFixture]
public class DroneCameraControllerTests
{
    Mouse mouse;
    GameObject droneGo;
    GameObject pivotGo;
    DroneCameraController controller;

    // ──────────────────────────────────────────────
    //  准备 / 清理
    // ──────────────────────────────────────────────

    [SetUp]
    public void Setup()
    {
        // 添加虚拟鼠标（替换 Mouse.current）
        mouse = InputSystem.AddDevice<Mouse>();

        // 无人机根对象 + 摄像头枢轴子对象
        droneGo = new GameObject("DroneTest");
        pivotGo = new GameObject("Camera_Lens");
        pivotGo.transform.SetParent(droneGo.transform, worldPositionStays: false);

        // AddComponent 触发 Awake；Awake 会找到 Camera_Lens 并读取初始 pitch
        controller = droneGo.AddComponent<DroneCameraController>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(droneGo);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
    }

    /// <summary>等价于 InputTestFixture.Set：写入鼠标 delta 并让输入系统分发一次。</summary>
    void SetDelta(Vector2 value)
    {
        InputSystem.QueueDeltaStateEvent(mouse.delta, value);
        InputSystem.Update();
    }

    // ──────────────────────────────────────────────
    //  辅助：通过反射调用私有 Update
    // ──────────────────────────────────────────────

    static void StepUpdate(DroneCameraController c)
    {
        typeof(DroneCameraController)
            .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(c, null);
    }

    // ──────────────────────────────────────────────
    //  测试用例
    // ──────────────────────────────────────────────

    /// <summary>鼠标向右移动 100 单位，机身 Yaw 应增加约 12°（sensitivity 0.12 默认值）。</summary>
    [Test]
    public void Yaw_IncreasesByMouseXTimesSensitivity()
    {
        var yawBefore = controller.CurrentYaw;
        SetDelta(new Vector2(100f, 0f));
        StepUpdate(controller);

        // 期望值：100 * 0.12 = 12°；允许 0.5° 浮点误差
        var expected = (yawBefore + 12f) % 360f;
        Assert.That(controller.CurrentYaw, Is.EqualTo(expected).Within(0.5f),
            "Yaw 应随鼠标 X delta 增加");
    }

    /// <summary>鼠标向上推 100 单位，摄像头 Pitch 应升高约 12°。</summary>
    [Test]
    public void Pitch_IncreasesByMouseYTimesSensitivity()
    {
        SetDelta(new Vector2(0f, 100f));
        StepUpdate(controller);

        Assert.That(controller.CurrentPitch, Is.EqualTo(12f).Within(0.5f),
            "鼠标向上移 Pitch 应为正（摄像头抬头）");
    }

    /// <summary>超大正 delta.y：Pitch 不得超过 maxPitchUp（默认 60°）。</summary>
    [Test]
    public void Pitch_ClampsAtMaxPitchUp()
    {
        SetDelta(new Vector2(0f, 10_000f));
        StepUpdate(controller);

        Assert.That(controller.CurrentPitch, Is.EqualTo(60f).Within(0.01f),
            "Pitch 上限应被夹到 maxPitchUp = 60°");
    }

    /// <summary>超大负 delta.y：CurrentPitch 不得低于 -maxPitchDown（默认 -45°）。</summary>
    [Test]
    public void Pitch_ClampsAtMaxPitchDown()
    {
        SetDelta(new Vector2(0f, -10_000f));
        StepUpdate(controller);

        Assert.That(controller.CurrentPitch, Is.EqualTo(-45f).Within(0.01f),
            "Pitch 下限应被夹到 -maxPitchDown = -45°");
    }

    /// <summary>ResetPitch 应把 Pitch 精确归零并重置枢轴旋转。</summary>
    [Test]
    public void ResetPitch_SetsPitchToZero()
    {
        // 先积累一些 pitch
        SetDelta(new Vector2(0f, 500f));
        StepUpdate(controller);
        Assert.That(controller.CurrentPitch, Is.GreaterThan(1f),
            "前置条件：Pitch 应已偏离 0");

        controller.ResetPitch();

        Assert.That(controller.CurrentPitch, Is.EqualTo(0f).Within(0.001f),
            "ResetPitch 后 Pitch 应回到 0");
        Assert.That(pivotGo.transform.localRotation,
            Is.EqualTo(Quaternion.identity).Using<Quaternion>(
                (a, b) => Quaternion.Angle(a, b) < 0.01f ? 0 : 1),
            "ResetPitch 后枢轴 localRotation 应为 identity");
    }
}
#endif
