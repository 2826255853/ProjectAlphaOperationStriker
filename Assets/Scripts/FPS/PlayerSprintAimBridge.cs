using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using KINEMATION.FPSAnimationPack.Scripts.Player;

/// <summary>
/// 设定：奔跑时按下右键，自动进入瞄准并解除奔跑。右键松开后恢复正常输入。
///
/// 实现说明：KINEMATION FPS 包的 <see cref="FPSPlayer"/> 没有公开的
/// “写入瞄准状态”接口（只有 InputValue 版 <c>OnAim</c>），而该包不在仓库里
/// （.gitignore 排除了 Assets/KINEMATION），所以这里用反射桥接，不改第三方包：
///   * 优先调用 FPSPlayer 上任何接受 <c>bool</c> 的瞄准写入方法（若日后包提供）；
///   * 否则退回写私有字段 <c>_isAiming</c>（并同步 <c>_recoilAnimation.isAiming</c>）；
///   * 右键按下时临时禁用 PlayerInput 上的 “Sprint” 动作，避免物理上的 Shift
///     仍按着时又立刻恢复奔跑；右键松开后再恢复该动作。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-100)]
public sealed class PlayerSprintAimBridge : MonoBehaviour
{
    private const string SprintActionName = "Sprint";

    [SerializeField] private FPSPlayer fpsPlayer;

    private PlayerInput playerInput;
    private InputAction sprintAction;
    private MethodInfo reflectAimSetter;
    private FieldInfo aimStateField;
    private FieldInfo recoilAnimationField;
    private FieldInfo recoilAimingField;
    private bool reflectionResolved;
    private bool warnedAboutFallback;
    private bool sprintOverrideActive;
    private bool sprintActionWasEnabled;

    /// <summary>Whether the bridge currently sees the player in aim (ADS) state.</summary>
    public bool IsAiming => GetAimState();

    /// <summary>True while the sprint key is held and sprint is not blocked by crouching.</summary>
    public bool IsSprintRequested { get; private set; }

    private void Awake()
    {
        if (fpsPlayer == null)
        {
            fpsPlayer = GetComponentInChildren<FPSPlayer>(true);
        }
    }

    private void OnDisable()
    {
        IsSprintRequested = false;
        EndAimOverride();
    }

    private void Update()
    {
        if (!EnsurePlayerResolved())
        {
            return;
        }

        ResolveInputActions();
        IsSprintRequested = SprintKeyHeld();

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame && SprintKeyHeld())
        {
            BeginAimOverride();
        }
        else if (IsAiming && SprintKeyPressedThisFrame())
        {
            // Sprint is disabled during the right-click override, so read the
            // physical key to allow Shift to leave ADS reliably.
            CancelAim();
        }
        else if (sprintOverrideActive && (mouse == null || !mouse.rightButton.isPressed))
        {
            EndAimOverride();
        }
    }

    private void BeginAimOverride()
    {
        if (sprintOverrideActive)
        {
            return;
        }

        sprintOverrideActive = true;
        sprintActionWasEnabled = sprintAction != null && sprintAction.enabled;
        sprintAction?.Disable();
        GetComponent<FirstPersonController>()?.SetSprintSuppressedByAim(true);
    }

    private void EndAimOverride()
    {
        sprintOverrideActive = false;
        if (sprintActionWasEnabled)
        {
            sprintAction?.Enable();
        }

        GetComponent<FirstPersonController>()?.SetSprintSuppressedByAim(false);
        sprintActionWasEnabled = false;
    }

    /// <summary>
    /// Force the player out of aim. The optional parameter is retained for the
    /// existing jump caller and does not affect right-click sprint override.
    /// </summary>
    public void CancelAim(bool hardCancel = false)
    {
        if (!EnsurePlayerResolved() || !IsAiming)
        {
            return;
        }

        if (!TrySetAimState(false))
        {
            WarnAboutFallback();
        }
    }

    private bool TrySetAimState(bool isAiming)
    {
        ResolveReflection();

        if (reflectAimSetter != null)
        {
            reflectAimSetter.Invoke(fpsPlayer, new object[] { isAiming });
            return true;
        }

        bool changed = false;
        if (aimStateField != null)
        {
            aimStateField.SetValue(fpsPlayer, isAiming);
            changed = true;
        }

        if (recoilAnimationField != null && recoilAimingField != null)
        {
            object recoilAnimation = recoilAnimationField.GetValue(fpsPlayer);
            if (recoilAnimation != null)
            {
                recoilAimingField.SetValue(recoilAnimation, isAiming);
            }
        }

        if (changed)
        {
            return true;
        }

        WarnAboutFallback();
        return false;
    }

    /// <summary>
    /// 预制体里 FPSPlayer 挂在子物体上，且本组件的 Awake 可能早于子物体就绪
    /// （AddComponent / 运行时引导都会出现），所以每次用之前都做一次惰性解析。
    /// </summary>
    private bool EnsurePlayerResolved()
    {
        if (fpsPlayer == null)
        {
            fpsPlayer = GetComponentInChildren<FPSPlayer>(true);
        }

        return fpsPlayer != null || GetComponent<FirstPersonController>() != null;
    }

    private bool GetAimState()
    {
        if (!EnsurePlayerResolved())
        {
            return false;
        }

        ResolveReflection();
        if (aimStateField == null)
        {
            return false;
        }

        object value = aimStateField.GetValue(fpsPlayer);
        return value is bool aiming && aiming;
    }

    /// <summary>
    /// Reads the sprint action for the package player, or the shared sprint input
    /// exposed by the legacy controller.
    /// </summary>
    private bool SprintKeyHeld()
    {
        FirstPersonController controller = GetComponent<FirstPersonController>();
        if (controller != null)
        {
            return controller.IsSprintRequested || controller.IsSprinting;
        }

        if (sprintAction != null)
        {
            return sprintAction.IsPressed();
        }

        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.leftShiftKey.isPressed)
        {
            return true;
        }

        UnityEngine.InputSystem.Gamepad gamepad = UnityEngine.InputSystem.Gamepad.current;
        return gamepad != null && gamepad.leftStickButton.isPressed;
    }

    private bool SprintKeyPressedThisFrame()
    {
        if (sprintAction != null && sprintAction.enabled && sprintAction.WasPressedThisFrame())
        {
            return true;
        }

        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.leftShiftKey.wasPressedThisFrame)
        {
            return true;
        }

        UnityEngine.InputSystem.Gamepad gamepad = UnityEngine.InputSystem.Gamepad.current;
        return gamepad != null && gamepad.leftStickButton.wasPressedThisFrame;
    }

    private void ResolveReflection()
    {
        if (reflectionResolved || fpsPlayer == null)
        {
            return;
        }

        reflectionResolved = true;

        // 1) 优先找包内可能提供的 bool 版瞄准写入方法。
        foreach (MethodInfo method in fpsPlayer.GetType().GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!method.Name.Contains("Aim") || method.Name.Contains("Legacy"))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
            {
                reflectAimSetter = method;
                break;
            }
        }

        // 2) 退回字段：_isAiming 与 _recoilAnimation.isAiming。
        aimStateField = fpsPlayer.GetType().GetField(
            "_isAiming", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        recoilAnimationField = fpsPlayer.GetType().GetField(
            "_recoilAnimation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (recoilAnimationField != null)
        {
            recoilAimingField = recoilAnimationField.FieldType.GetField(
                "isAiming", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        ResolveInputActions();
    }

    private void ResolveInputActions()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInChildren<PlayerInput>(true);
        }

        if (playerInput == null || playerInput.actions == null || sprintAction != null)
        {
            return;
        }

        sprintAction = playerInput.actions.FindAction(SprintActionName, false);
    }

    private void WarnAboutFallback()
    {
        if (warnedAboutFallback)
        {
            return;
        }

        warnedAboutFallback = true;
        Debug.LogWarning(
            "[PlayerSprintAimBridge] 无法写入 FPSPlayer 的瞄准状态（当前包版本没有 bool 版瞄准接口），" +
            "跳跃时将无法强制退出 ADS；右键按下时仍会通过禁用 Sprint 动作来解除奔跑。", this);
    }

}
