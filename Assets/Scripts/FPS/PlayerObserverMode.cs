using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Input passed to an observer driver such as a future drone controller.</summary>
public readonly struct ObserverModeInput
{
    public readonly Vector2 Move;
    public readonly Vector2 Look;
    public readonly float Vertical;
    public readonly bool Boost;

    public ObserverModeInput(Vector2 move, Vector2 look, float vertical, bool boost)
    {
        Move = move;
        Look = look;
        Vertical = vertical;
        Boost = boost;
    }
}

/// <summary>Implement this on a MonoBehaviour to drive a drone or another observer vehicle.</summary>
public interface IObserverModeDriver
{
    bool CanEnter(PlayerHealth player);
    void Enter(PlayerHealth player, Transform viewTransform);
    void Tick(ObserverModeInput input, float deltaTime);
    void Exit();
}

/// <summary>Free-flight observer controls with an optional replaceable vehicle driver.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerObserverMode : MonoBehaviour
{
    [Header("Observer movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 6f;
    [SerializeField, Min(0f)] private float boostMultiplier = 2.5f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
    [SerializeField, Range(1f, 89f)] private float maximumLookAngle = 85f;
    [SerializeField] private MonoBehaviour driverComponent;

    private PlayerHealth health;
    private Transform viewTransform;
    private IObserverModeDriver driver;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction verticalAction;
    private InputAction boostAction;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private Quaternion savedViewLocalRotation;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private float pitch;

    public bool IsActive { get; private set; }
    public MonoBehaviour DriverComponent => driverComponent;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        Camera viewCamera = GetComponentInChildren<Camera>(true);
        viewTransform = viewCamera != null ? viewCamera.transform : transform;
        driver = driverComponent as IObserverModeDriver;
        CreateInputActions();
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        lookAction?.Dispose();
        verticalAction?.Dispose();
        boostAction?.Dispose();
    }

    private void Update()
    {
        if (!IsActive) return;
        UpdateCursor();

        Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        Vector2 look = Cursor.lockState == CursorLockMode.Locked
            ? lookAction.ReadValue<Vector2>() : Vector2.zero;
        float vertical = verticalAction.ReadValue<float>();
        bool boost = boostAction.IsPressed();
        ObserverModeInput input = new ObserverModeInput(move, look, vertical, boost);
        if (driver != null) driver.Tick(input, Time.deltaTime);
        else ApplyFreeFlight(input, Time.deltaTime);
    }

    public bool SetDriver(MonoBehaviour component)
    {
        if (IsActive) return false;
        if (component != null && component is not IObserverModeDriver) return false;
        driverComponent = component;
        driver = component as IObserverModeDriver;
        return true;
    }

    public bool IsDriverComponent(Behaviour behaviour) => behaviour == driverComponent;

    public bool TryEnterObserverMode()
    {
        if (IsActive) return true;
        if (driver != null && !driver.CanEnter(health)) return false;

        savedPosition = transform.position;
        savedRotation = transform.rotation;
        savedViewLocalRotation = viewTransform.localRotation;
        pitch = NormalizeAngle(viewTransform.localEulerAngles.x);
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        moveAction.Enable();
        lookAction.Enable();
        verticalAction.Enable();
        boostAction.Enable();
        IsActive = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        driver?.Enter(health, viewTransform);
        return true;
    }

    public bool ExitObserverMode()
    {
        if (!IsActive) return false;
        driver?.Exit();
        moveAction.Disable();
        lookAction.Disable();
        verticalAction.Disable();
        boostAction.Disable();
        transform.SetPositionAndRotation(savedPosition, savedRotation);
        if (viewTransform != transform) viewTransform.localRotation = savedViewLocalRotation;
        IsActive = false;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        return true;
    }

    private void CreateInputActions()
    {
        moveAction = new InputAction("Observer Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddBinding("<Gamepad>/leftStick");

        lookAction = new InputAction("Observer Look", InputActionType.Value, "<Mouse>/delta");

        verticalAction = new InputAction("Observer Vertical", InputActionType.Value);
        verticalAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftCtrl")
            .With("Positive", "<Keyboard>/space");
        verticalAction.AddBinding("<Gamepad>/rightStick/y");

        boostAction = new InputAction("Observer Boost", InputActionType.Button, "<Keyboard>/leftShift");
        boostAction.AddBinding("<Gamepad>/leftStickPress");
    }

    private void ApplyFreeFlight(ObserverModeInput input, float deltaTime)
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            transform.Rotate(Vector3.up, input.Look.x * mouseSensitivity, Space.World);
            pitch = Mathf.Clamp(pitch - input.Look.y * mouseSensitivity,
                -maximumLookAngle, maximumLookAngle);
            if (viewTransform != transform)
                viewTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector3 direction = transform.right * input.Move.x
                            + transform.forward * input.Move.y
                            + Vector3.up * input.Vertical;
        if (direction.sqrMagnitude > 1f) direction.Normalize();
        float speed = moveSpeed * (input.Boost ? boostMultiplier : 1f);
        transform.position += direction * speed * deltaTime;
    }

    private static void UpdateCursor()
    {
        if (TrapSelectionMenu.CursorOwned) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null
                 && (Mouse.current.leftButton.wasPressedThisFrame
                     || Mouse.current.rightButton.wasPressedThisFrame))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
}
