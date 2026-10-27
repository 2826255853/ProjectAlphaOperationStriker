#if ENABLE_DRONE_CODE
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 无人机摄像头基本控制器：鼠标控制水平 Yaw（旋转机身）与垂直 Pitch（俯仰摄像头）。
/// 挂到无人机根对象，把 CameraPivot 指向摄像头云台节点即可运行。
///
/// 层级约定（与 QuadcopterDroneAuthoring 一致）：
///   QuadcopterDrone（此组件所在根）
///     └─ DroneCameraPivot（旧资源兼容 Camera_Gimbal / Camera_Lens）← 独立的摄像头俯仰枢轴
///         └─ Camera（可选，挂 Unity Camera 组件）
///
/// </summary>
[DisallowMultipleComponent]
public sealed class DroneCameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("摄像头枢轴节点，绕本地 X 轴俯仰。优先使用 DroneCameraPivot。")]
    [SerializeField] private Transform cameraPivot;

    [Header("Sensitivity")]
    [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.12f;

    [Header("Pitch Limits")]
    [SerializeField, Range(1f, 89f)] private float maxPitchUp   = 60f;
    [SerializeField, Range(1f, 89f)] private float maxPitchDown = 85f;

    [Tooltip("无人机进入控制模式时的默认俯角（正值为向下）。")]
    [SerializeField, Range(0f, 89f)] private float initialPitchDown = 30f;

    // 当前 pitch 积累值（度），向上为负（绕 +X 轴）
    private float pitch;

    // ──────────────────────────────────────────────
    //  生命周期
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // 若 Inspector 未赋值，尝试按约定名称在子树中自动找
        if (cameraPivot == null)
        {
            cameraPivot = FindDeep(transform, "DroneCameraPivot")
                          ?? FindDeep(transform, "Camera_Gimbal")
                          ?? FindDeep(transform, "Camera_Lens");
            if (cameraPivot == null)
            {
            Debug.LogError("[DroneCameraController] 未找到 DroneCameraPivot、Camera_Gimbal 或 Camera_Lens 节点，" +
                               "请在 Inspector 里手动赋值 Camera Pivot。", this);
            }
        }

        // 模型的 Camera_Lens 可能带有向上的导入姿态。相机视线应由云台
        // 的运行时俯仰控制，而不是继承这个会把镜头抬过地平线的姿态。
        if (cameraPivot != null)
        {
            pitch = Mathf.Clamp(initialPitchDown, -maxPitchUp, maxPitchDown);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        var delta = Mouse.current.delta.ReadValue();

        // Yaw：水平输入旋转整个机身（绕世界 Y 轴）
        var yawDelta = delta.x * mouseSensitivity;
        transform.Rotate(Vector3.up, yawDelta, Space.World);

        // Pitch：垂直输入俯仰摄像头枢轴（绕本地 X 轴，向上移鼠标 = 向上看 = 负 pitch）
        if (cameraPivot != null)
        {
            pitch -= delta.y * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, -maxPitchUp, maxPitchDown);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    // ──────────────────────────────────────────────
    //  公开接口（供将来游戏接入层调用）
    // ──────────────────────────────────────────────

    /// <summary>由无人机控制协调器调用，启用或停用本摄像头控制器。</summary>
    public void SetActive(bool active)
    {
        enabled = active;
    }

    /// <summary>返回摄像头当前俯仰角（度，向上为正，范围 [-maxPitchUp, maxPitchDown]）。</summary>
    public float CurrentPitch => -pitch;

    /// <summary>返回机身当前 yaw 角（度，绕世界 Y）。</summary>
    public float CurrentYaw => transform.eulerAngles.y;

    /// <summary>
    /// 强制重置摄像头俯仰到 0（水平）并清除内部状态，不影响机身朝向。
    /// </summary>
    public void ResetPitch()
    {
        pitch = 0f;
        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.identity;
        }
    }

    // ──────────────────────────────────────────────
    //  工具方法
    // ──────────────────────────────────────────────

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root.name == targetName)
        {
            return root;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}

#endif
