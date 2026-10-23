#if ENABLE_DRONE_CODE
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 无人机摄像头基本控制器：鼠标控制水平 Yaw（旋转机身）与垂直 Pitch（俯仰摄像头）。
/// 暂不接入游戏系统——挂到无人机根对象，把 CameraPivot 指向摄像头挂点即可独立运行。
///
/// 层级约定（与 QuadcopterDroneAuthoring 一致）：
///   QuadcopterDrone（此组件所在根）
///     └─ Camera_Lens  ← 摄像头俯仰枢轴，赋给 cameraPivot
///         └─ Camera（可选，挂 Unity Camera 组件）
///
/// TODO(确认): 接入游戏时改为尊重 TrapSelectionMenu.CursorOwned / GameStartMenu.CursorOwned，
///             与 FirstPersonController.UpdateCursor 保持一致的光标所有权逻辑。
/// </summary>
[DisallowMultipleComponent]
public sealed class DroneCameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("摄像头枢轴节点，绕本地 X 轴俯仰。通常是模型里的 Camera_Lens 节点。")]
    [SerializeField] private Transform cameraPivot;

    [Header("Sensitivity")]
    [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.12f;

    [Header("Pitch Limits")]
    [SerializeField, Range(1f, 89f)] private float maxPitchUp   = 60f;
    [SerializeField, Range(1f, 89f)] private float maxPitchDown = 45f;

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
            cameraPivot = FindDeep(transform, "Camera_Lens");
            if (cameraPivot == null)
            {
                Debug.LogWarning("[DroneCameraController] 未找到 Camera_Lens 节点，" +
                                 "请在 Inspector 里手动赋值 Camera Pivot。", this);
            }
        }

        // 从当前枢轴旋转恢复初始 pitch，防止启动时跳变
        if (cameraPivot != null)
        {
            pitch = WrapAngle(cameraPivot.localEulerAngles.x);
        }
    }

    private void OnEnable()
    {
        LockCursor();
    }

    private void OnDisable()
    {
        UnlockCursor();
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

    static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    /// <summary>把 Unity 的 0–360 欧拉角转成 -180–180，方便与 Clamp 配合。</summary>
    static float WrapAngle(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

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
