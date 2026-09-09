using UnityEngine;
using UnityEngine.InputSystem;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;

[RequireComponent(typeof(CharacterController))]
public sealed class FirstPersonController : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
    [SerializeField, Range(1f, 89f)] private float maximumLookAngle = 85f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
    [SerializeField, Min(0f)] private float sprintSpeed = 7.5f;
    [SerializeField, Min(0f)] private float quietWalkSpeed = 2f;
    [SerializeField, Min(0f)] private float crouchSpeed = 2.2f;
    [SerializeField, Min(0f)] private float acceleration = 28f;

    [Header("Stance")]
    [SerializeField, Min(0.1f)] private float standingHeight = 1.8f;
    [SerializeField, Min(0.1f)] private float crouchingHeight = 1.15f;
    [SerializeField, Min(0f)] private float stanceChangeSpeed = 8f;
    [SerializeField, Min(0f)] private float standingCameraHeight = 1.65f;
    [SerializeField, Min(0f)] private float crouchingCameraHeight = 1f;

    [Header("Jump")]
    [Tooltip("The ballistic apex above take-off height.")]
    [SerializeField, Min(0.01f)] private float jumpHeight = 1.1f;
    [SerializeField, Min(0.01f)] private float gravity = 20f;
    [SerializeField, Min(0f)] private float groundedForce = 2f;

    private CharacterController characterController;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction sprintAction;
    private InputAction quietWalkAction;
    private InputAction crouchAction;
    private InputAction jumpAction;
    private Vector3 horizontalVelocity;
    private readonly Collider[] ceilingOverlaps = new Collider[8];
    private float verticalVelocity;
    private float pitch;

    public bool IsCrouching { get; private set; }
    public bool IsQuietWalking => quietWalkAction != null && quietWalkAction.IsPressed();
    public float JumpHeight => jumpHeight;

    public void SetCameraPivot(Transform pivot)
    {
        cameraPivot = pivot;
        if (cameraPivot != null)
        {
            cameraPivot.localPosition = Vector3.up * standingCameraHeight;
            cameraPivot.localRotation = Quaternion.identity;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        ConfigureController();
        CreateInputActions();

        if (cameraPivot == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            cameraPivot = childCamera != null ? childCamera.transform : null;
        }

        LockCursor();
    }

    private void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        sprintAction.Enable();
        quietWalkAction.Enable();
        crouchAction.Enable();
        jumpAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        lookAction.Disable();
        sprintAction.Disable();
        quietWalkAction.Disable();
        crouchAction.Disable();
        jumpAction.Disable();
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        lookAction?.Dispose();
        sprintAction?.Dispose();
        quietWalkAction?.Dispose();
        crouchAction?.Dispose();
        jumpAction?.Dispose();
    }

    private void Update()
    {
        UpdateCursor();
        UpdateView();
        UpdateStance();
        UpdateMovement();
    }

    private void ConfigureController()
    {
        characterController.height = standingHeight;
        characterController.radius = Mathf.Min(0.35f, standingHeight * 0.45f);
        characterController.center = Vector3.up * (standingHeight * 0.5f);
        characterController.stepOffset = 0.3f;
        characterController.slopeLimit = 50f;
        characterController.skinWidth = 0.03f;
        characterController.minMoveDistance = 0f;
    }

    private void CreateInputActions()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddBinding("<Gamepad>/leftStick");

        lookAction = new InputAction("Look", InputActionType.Value);
        lookAction.AddBinding("<Mouse>/delta");

        sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");
        quietWalkAction = new InputAction("Quiet Walk", InputActionType.Button, "<Keyboard>/leftAlt");
        crouchAction = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
        crouchAction.AddBinding("<Keyboard>/c");
        crouchAction.AddBinding("<Gamepad>/rightStickPress");
        jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void UpdateView()
    {
        if (cameraPivot == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 look = lookAction.ReadValue<Vector2>() * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - look.y, -maximumLookAngle, maximumLookAngle);
        transform.Rotate(Vector3.up, look.x, Space.Self);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void UpdateStance()
    {
        bool wantsToCrouch = crouchAction.IsPressed();
        IsCrouching = wantsToCrouch || !CanStandUp();

        float targetHeight = IsCrouching ? crouchingHeight : standingHeight;
        float newHeight = Mathf.MoveTowards(characterController.height, targetHeight, stanceChangeSpeed * Time.deltaTime);
        characterController.height = newHeight;
        characterController.center = Vector3.up * (newHeight * 0.5f);

        if (cameraPivot != null)
        {
            float targetCameraHeight = IsCrouching ? crouchingCameraHeight : standingCameraHeight;
            Vector3 target = Vector3.up * targetCameraHeight;
            cameraPivot.localPosition = Vector3.MoveTowards(
                cameraPivot.localPosition, target, stanceChangeSpeed * Time.deltaTime);
        }
    }

    private bool CanStandUp()
    {
        if (characterController.height >= standingHeight - 0.001f)
        {
            return true;
        }

        float radius = characterController.radius * 0.95f;
        Vector3 bottom = transform.position + Vector3.up * radius;
        Vector3 top = transform.position + Vector3.up * (standingHeight - radius);
        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom, top, radius, ceilingOverlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = ceilingOverlaps[i];
            if (overlap != null && overlap.transform != transform && !overlap.transform.IsChildOf(transform))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateMovement()
    {
        Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        Vector3 desiredDirection = transform.right * input.x + transform.forward * input.y;
        float targetSpeed = SelectMovementSpeed();
        Vector3 desiredVelocity = desiredDirection * targetSpeed;
        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity, desiredVelocity, acceleration * Time.deltaTime);

        bool grounded = characterController.isGrounded;
        bool jumped = grounded && jumpAction.WasPressedThisFrame() && !IsCrouching;
        if (jumped)
        {
            verticalVelocity = CalculateJumpSpeed(jumpHeight, gravity);
        }
        else if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -groundedForce;
        }

        float verticalDisplacement;
        if (!grounded || jumped)
        {
            // Exact constant-acceleration integration keeps the configured ballistic apex.
            verticalDisplacement = verticalVelocity * Time.deltaTime
                                   - 0.5f * gravity * Time.deltaTime * Time.deltaTime;
            verticalVelocity -= gravity * Time.deltaTime;
        }
        else
        {
            verticalDisplacement = verticalVelocity * Time.deltaTime;
        }

        CollisionFlags flags = characterController.Move(
            horizontalVelocity * Time.deltaTime + Vector3.up * verticalDisplacement);

        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }
    }

    private float SelectMovementSpeed()
    {
        if (IsCrouching)
        {
            return crouchSpeed;
        }

        if (quietWalkAction.IsPressed())
        {
            return quietWalkSpeed;
        }

        return sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
    }

    public static float CalculateJumpSpeed(float height, float gravityMagnitude)
    {
        return Mathf.Sqrt(2f * Mathf.Max(0f, gravityMagnitude) * Mathf.Max(0f, height));
    }

    private static void UpdateCursor()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}

/// <summary>
/// Adds gravity and a simple jump to the KINEMATION FPS prefab. The package's
/// FPSPlayer continues to own horizontal movement, look, ADS and weapon input.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class FPSPackagePlayerMotion : MonoBehaviour
{
    [SerializeField, Min(0f)] private float gravity = 20f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.1f;
    [SerializeField, Min(0f)] private float groundedForce = 2f;

    private CharacterController characterController;
    private FPSPlayer fpsPlayer;
    private float verticalVelocity;
    private bool jumpRequested;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        fpsPlayer = GetComponentInChildren<FPSPlayer>(true);
    }

    private void Update()
    {
        if (characterController == null || !characterController.enabled)
        {
            return;
        }

        UpdateCursor();

        if (WasJumpPressed())
        {
            jumpRequested = true;
        }
    }

    private void LateUpdate()
    {
        if (characterController == null || !characterController.enabled)
        {
            return;
        }

        // FPSPlayer also moves this CharacterController in Update(). Applying
        // vertical motion in LateUpdate guarantees that the package movement
        // pass cannot overwrite the jump displacement in the same frame.
        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -groundedForce;
        }

        if (grounded && jumpRequested)
        {
            verticalVelocity = FirstPersonController.CalculateJumpSpeed(jumpHeight, gravity);
            fpsPlayer?.OnJump();
            jumpRequested = false;
        }

        verticalVelocity -= gravity * Time.deltaTime;
        characterController.Move(Vector3.up * (verticalVelocity * Time.deltaTime));
    }

    private static void UpdateCursor()
    {
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

    private static bool WasJumpPressed()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        return (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
               || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
    }
}

/// <summary>
/// Turns each KINEMATION weapon shot into a lightweight hitscan query. The
/// package remains responsible for fire cadence, ammo, animation, sound and
/// recoil; this bridge only supplies gameplay hit detection for scene targets
/// that implement TakeDamage(float) (or a compatible SendMessage receiver).
/// </summary>
public sealed class FPSHitscanShooter : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float range = 250f;
    [SerializeField, Min(0f)] private float damage = 25f;
    [SerializeField] private LayerMask hitMask = ~0;

    private FPSPlayer player;
    private FPSWeapon observedWeapon;
    private Camera playerCamera;
    private int previousAmmo = -1;

    private void Awake()
    {
        // The bootstrap adds this bridge to the prefab root, while the package
        // player lives on its character child (alongside the weapon animator).
        player = GetComponentInChildren<FPSPlayer>(true);
        playerCamera = GetComponentInChildren<Camera>(true);
    }

    private void Update()
    {
        if (TrapPlacementController.IsPlacementModeActive)
        {
            // Trap placement owns the mouse while active; discard any weapon
            // state changes so a shot cannot damage gameplay targets.
            previousAmmo = -1;
            observedWeapon = null;
            return;
        }
        if (player == null || playerCamera == null)
        {
            return;
        }

        FPSWeapon activeWeapon;
        try
        {
            activeWeapon = player.GetActiveWeapon();
        }
        catch (System.ArgumentOutOfRangeException)
        {
            // FPSPlayer populates its weapon list in Start; wait one frame.
            return;
        }

        if (activeWeapon != observedWeapon)
        {
            observedWeapon = activeWeapon;
            previousAmmo = activeWeapon.GetActiveAmmo();
            return;
        }

        int ammo = activeWeapon.GetActiveAmmo();
        if (previousAmmo >= 0 && ammo < previousAmmo)
        {
            FireHitscan();
        }

        previousAmmo = ammo;
    }

    private void FireHitscan()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (damage <= 0f)
        {
            return;
        }

        // A model can contain several colliders, and the collider that gets
        // hit is often a child of the object that owns EnemyHealth.  Resolve
        // the complete hit list so a self-collider does not consume the shot
        // and so damage is applied to the first damageable object in the line
        // of fire.  Once a solid non-target collider is reached it still
        // blocks the shot like a normal hitscan weapon.
        RaycastHit[] hits = Physics.RaycastAll(ray, range, hitMask,
            QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
        {
            return;
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.TakeDamage(damage);
                return;
            }

            // Preserve compatibility with other gameplay targets that expose
            // TakeDamage(float), including receivers on a parent object.
            hitTransform.SendMessageUpwards("TakeDamage", damage,
                SendMessageOptions.DontRequireReceiver);
            return;
        }
    }
}
