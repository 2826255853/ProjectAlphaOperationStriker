#if UNITY_INCLUDE_TESTS
#if ENABLE_DRONE_CODE
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

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
    GameObject pivotParentGo;
    GameObject pivotGo;
    DroneCameraController controller;
    CursorLockMode cursorLockStateBefore;
    bool cursorVisibleBefore;

    // ──────────────────────────────────────────────
    //  准备 / 清理
    // ──────────────────────────────────────────────

    [SetUp]
    public void Setup()
    {
        cursorLockStateBefore = Cursor.lockState;
        cursorVisibleBefore = Cursor.visible;

        // 添加虚拟鼠标（替换 Mouse.current）
        mouse = InputSystem.AddDevice<Mouse>();

        // 无人机根对象 + 摄像头枢轴子对象
        droneGo = new GameObject("DroneTest");
        pivotParentGo = new GameObject("CameraMount");
        pivotParentGo.transform.SetParent(droneGo.transform, worldPositionStays: false);
        pivotGo = new GameObject("Camera_Gimbal");
        pivotGo.transform.SetParent(pivotParentGo.transform, worldPositionStays: false);

        // EditMode 中手动驱动 Awake；应找到嵌套的 Camera_Gimbal 并读取初始 pitch
        controller = droneGo.AddComponent<DroneCameraController>();
        StepAwake(controller);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(droneGo);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        Cursor.lockState = cursorLockStateBefore;
        Cursor.visible = cursorVisibleBefore;
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

    static void StepAwake(DroneCameraController c)
    {
        typeof(DroneCameraController)
            .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(c, null);
    }

    static void StepUpdate(DroneCameraController c)
    {
        typeof(DroneCameraController)
            .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(c, null);
    }

    // ──────────────────────────────────────────────
    //  测试用例
    // ──────────────────────────────────────────────

    /// <summary>首次接管无人机时，摄像机应朝地面而不是仰向地平线以上。</summary>
    [Test]
    public void Awake_StartsWithCameraPointingDownward()
    {
        Assert.That(controller.CurrentPitch, Is.EqualTo(-30f).Within(0.01f),
            "默认姿态应为向下 30°");
        Assert.That(pivotGo.transform.forward.y, Is.LessThan(-0.49f),
            "相机视线应明显朝向地面");
    }

    /// <summary>Awake 应优先使用独立运行时云台，避免继承模型节点的错误轴向。</summary>
    [Test]
    public void Awake_PrefersDedicatedCameraPivot()
    {
        var dedicatedPivot = new GameObject("DroneCameraPivot");
        dedicatedPivot.transform.SetParent(droneGo.transform, worldPositionStays: false);
        pivotGo.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);
        typeof(DroneCameraController)
            .GetField("cameraPivot", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(controller, null);
        StepAwake(controller);

        var cameraPivotField = typeof(DroneCameraController)
            .GetField("cameraPivot", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(cameraPivotField.GetValue(controller), Is.EqualTo(dedicatedPivot.transform));
        Assert.That(dedicatedPivot.transform.forward.y, Is.LessThan(-0.49f));
    }

    /// <summary>Awake 应从子树中找到约定的 Camera_Gimbal 节点。</summary>
    [Test]
    public void Awake_FindsNestedCameraGimbal()
    {
        var cameraPivotField = typeof(DroneCameraController)
            .GetField("cameraPivot", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(cameraPivotField, Is.Not.Null, "应存在序列化的 cameraPivot 字段");
        Assert.That(cameraPivotField.GetValue(controller), Is.EqualTo(pivotGo.transform),
            "Awake 应找到嵌套的 Camera_Gimbal 节点");
    }

    /// <summary>已存在的无人机资源仍使用 Camera_Lens，需在新名称未找到时兼容它。</summary>
    [Test]
    public void Awake_FallsBackToLegacyCameraLens()
    {
        Object.DestroyImmediate(droneGo);
        droneGo = new GameObject("DroneLegacyPivotTest");
        pivotGo = new GameObject("Camera_Lens");
        pivotGo.transform.SetParent(droneGo.transform, worldPositionStays: false);
        controller = droneGo.AddComponent<DroneCameraController>();
        StepAwake(controller);

        var cameraPivotField = typeof(DroneCameraController)
            .GetField("cameraPivot", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(cameraPivotField.GetValue(controller), Is.EqualTo(pivotGo.transform),
            "现有 Camera_Lens 资产应保持可用");
    }

    /// <summary>两种约定节点都不存在时，Awake 应输出可供 CI 捕获的错误日志。</summary>
    [Test]
    public void Awake_LogsErrorWhenCameraPivotIsMissing()
    {
        Object.DestroyImmediate(droneGo);
        droneGo = new GameObject("DroneMissingPivotTest");
        controller = droneGo.AddComponent<DroneCameraController>();

        LogAssert.Expect(LogType.Error,
            "[DroneCameraController] 未找到 DroneCameraPivot、Camera_Gimbal 或 Camera_Lens 节点，请在 Inspector 里手动赋值 Camera Pivot。");
        StepAwake(controller);
    }

    /// <summary>SetActive 只切换组件启用状态，不接管全局光标。</summary>
    [Test]
    public void SetActive_DoesNotChangeGlobalCursor()
    {
        var lockState = Cursor.lockState;
        var visible = Cursor.visible;

        controller.SetActive(false);

        Assert.That(controller.enabled, Is.False, "SetActive(false) 应停用摄像头控制器");
        Assert.That(Cursor.lockState, Is.EqualTo(lockState), "SetActive 不应修改全局光标锁定状态");
        Assert.That(Cursor.visible, Is.EqualTo(visible), "SetActive 不应修改全局光标可见性");
    }

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

    /// <summary>鼠标向上推 100 单位，摄像头应从默认俯视姿态抬高约 12°。</summary>
    [Test]
    public void Pitch_IncreasesByMouseYTimesSensitivity()
    {
        SetDelta(new Vector2(0f, 100f));
        StepUpdate(controller);

        Assert.That(controller.CurrentPitch, Is.EqualTo(-18f).Within(0.5f),
            "鼠标向上移应让俯视角减少 12°");
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

    /// <summary>超大负 delta.y：CurrentPitch 不得低于 -maxPitchDown（默认 -85°）。</summary>
    [Test]
    public void Pitch_ClampsAtMaxPitchDown()
    {
        SetDelta(new Vector2(0f, -10_000f));
        StepUpdate(controller);

        Assert.That(controller.CurrentPitch, Is.EqualTo(-85f).Within(0.01f),
            "Pitch 下限应被夹到 -maxPitchDown = -85°");
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
#endif
