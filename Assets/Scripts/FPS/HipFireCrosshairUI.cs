using UnityEngine;

/// <summary>Draws the hip-fire reticle and presents weapon kill feedback.</summary>
[DefaultExecutionOrder(1000)]
public sealed class HipFireCrosshairUI : MonoBehaviour
{
    private const float KillMarkerDuration = 0.18f;
    private const float KillMarkerExpandTime = 0.05f;
    private const float KillSoundInterval = 0.12f;

    [Header("Appearance")]
    [SerializeField] private Color color = Color.white;
    [SerializeField, Min(1f)] private float lineLength = 7f;
    [SerializeField, Min(1f)] private float lineThickness = 2f;
    [SerializeField, Min(0f)] private float centerGap = 4f;
    [SerializeField, Min(0f)] private float outlineThickness = 1f;
    [SerializeField] private Color outlineColor = Color.black;

    [Header("Weapon Kill")]
    [SerializeField] private Color killMarkerColor = new Color(1f, 0.91f, 0.83f);
    [SerializeField] private AudioClip killConfirmClip;
    [SerializeField, Range(0f, 1f)] private float killConfirmVolume = 0.65f;

    [Header("Movement Spread")]
    // TODO(确认): 根据实际手感调整普通跑动的扩散量和速度阈值。
    [SerializeField, Min(0f)] private float runSpread = 4f;
    [Tooltip("Grounded horizontal speed in m/s required to widen the reticle during regular movement.")]
    [SerializeField, Min(0.1f)] private float minimumRunSpeed = 2.5f;
    [SerializeField, Min(0f)] private float sprintSpread = 8f;
    [SerializeField, Min(0f)] private float airborneSpread = 12f;
    [SerializeField, Min(0f)] private float landingSpread = 10f;
    [SerializeField, Min(0f)] private float landingRecoverySpeed = 35f;
    [SerializeField, Min(0f)] private float spreadOutSpeed = 90f;
    [SerializeField, Min(0f)] private float spreadRecoverySpeed = 30f;
    // TODO(确认): 包内移动速度调整后，将此阈值保持在行走与奔跑速度之间。
    [Tooltip("Package-player speed threshold, above walking speed, to ignore sprint input while stationary or crouching.")]
    [SerializeField, Min(0f)] private float minimumSprintSpeed = 5f;

    private PlayerSprintAimBridge aimBridge;
    private CharacterController characterController;
    private FirstPersonController legacyController;
    private LadderClimbing ladderClimbing;
    private PlayerHealth playerHealth;
    private Texture2D pixel;
    private AudioSource killAudioSource;
    private Camera killViewCamera;
    private float killMarkerStarted = float.NegativeInfinity;
    private float lastKillSound = float.NegativeInfinity;
    private float movementSpread;
    private float landingImpulse;
    private Vector3 previousPosition;
    private bool hasGroundSample;
    private bool wasGrounded;

    private void Awake()
    {
        aimBridge = GetComponent<PlayerSprintAimBridge>();
        characterController = GetComponent<CharacterController>();
        legacyController = GetComponent<FirstPersonController>();
        ladderClimbing = GetComponent<LadderClimbing>();
        playerHealth = GetComponent<PlayerHealth>();
        pixel = Texture2D.whiteTexture;
    }

    private void Start()
    {
        if (killConfirmClip == null && GetComponent<FPSHitscanShooter>() != null)
            killConfirmClip = Resources.Load<AudioClip>("GunKillConfirm");
    }

    private void OnEnable()
    {
        ResetMovementSpread();
    }

    private void OnDisable()
    {
        ClearKillFeedback();
        lastKillSound = float.NegativeInfinity;
    }

    internal void ShowWeaponKill(Camera viewCamera)
    {
        if (!CanShowWeaponKill(viewCamera)) return;

        float now = Time.unscaledTime;
        killViewCamera = viewCamera;
        killMarkerStarted = now;
        if (killConfirmClip == null || now - lastKillSound < KillSoundInterval) return;

        if (killAudioSource == null)
        {
            GameObject audioObject = new GameObject("Gun Kill Confirm Audio");
            audioObject.transform.SetParent(transform, false);
            killAudioSource = audioObject.AddComponent<AudioSource>();
            killAudioSource.playOnAwake = false;
            killAudioSource.spatialBlend = 0f;
        }

        killAudioSource.PlayOneShot(killConfirmClip, killConfirmVolume);
        lastKillSound = now;
    }

    private bool CanShowWeaponKill(Camera viewCamera)
    {
        return isActiveAndEnabled && viewCamera != null && viewCamera.isActiveAndEnabled
            && (playerHealth == null || !playerHealth.IsDead)
            && !TrapSelectionMenu.CursorOwned && !GameStartMenu.CursorOwned
            && !TrapPlacementController.IsPlacementModeActive;
    }

    private void ClearKillFeedback()
    {
        killViewCamera = null;
        killMarkerStarted = float.NegativeInfinity;
        if (killAudioSource != null) killAudioSource.Stop();
    }

    private void ResetMovementSpread()
    {
        movementSpread = 0f;
        landingImpulse = 0f;
        hasGroundSample = false;
        previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (killViewCamera != null && !CanShowWeaponKill(killViewCamera)) ClearKillFeedback();

        // Both movement implementations (and ladders) have finished moving by now.
        if (characterController == null || !characterController.enabled
            || GameStartMenu.CursorOwned || TrapSelectionMenu.CursorOwned
            || (ladderClimbing != null && ladderClimbing.IsClimbing))
        {
            ResetMovementSpread();
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        // The package moves horizontally in Update and vertically in LateUpdate;
        // CharacterController.velocity here can contain only the vertical pass.
        Vector3 velocity = (transform.position - previousPosition) / deltaTime;
        previousPosition = transform.position;
        velocity.y = 0f;
        float horizontalSpeedSquared = velocity.sqrMagnitude;
        bool movingForward = Vector3.Dot(velocity, transform.forward) > 0.1f;
        bool sprinting = legacyController != null && legacyController.isActiveAndEnabled
            ? legacyController.IsSprinting && !legacyController.IsQuietWalking && movingForward
            : aimBridge != null && aimBridge.isActiveAndEnabled && aimBridge.IsSprintRequested
              && !aimBridge.IsAiming && movingForward
              && horizontalSpeedSquared >= minimumSprintSpeed * minimumSprintSpeed;

        bool grounded = characterController.isGrounded;
        bool running = grounded && horizontalSpeedSquared >= minimumRunSpeed * minimumRunSpeed;
        float horizontalSpread = running ? runSpread : 0f;
        if (sprinting) horizontalSpread = sprintSpread;
        float baseSpread = horizontalSpread + (grounded ? 0f : airborneSpread);
        if (hasGroundSample && !wasGrounded && grounded)
        {
            // Add a visible landing kick even when the airborne reticle was already wide.
            landingImpulse = Mathf.Max(0f, movementSpread - baseSpread) + landingSpread;
            movementSpread = baseSpread + landingImpulse;
        }
        else
        {
            landingImpulse = Mathf.MoveTowards(landingImpulse, 0f, landingRecoverySpeed * deltaTime);
        }

        float targetSpread = baseSpread + landingImpulse;
        float speed = targetSpread > movementSpread ? spreadOutSpeed : spreadRecoverySpeed;
        movementSpread = Mathf.MoveTowards(movementSpread, targetSpread, speed * deltaTime);
        wasGrounded = grounded;
        hasGroundSample = true;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float scale = Mathf.Max(0.5f, Screen.height / 1080f);
        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;
        if (ShouldDraw())
        {
            float length = lineLength * scale;
            float thickness = lineThickness * scale;
            float gap = (centerGap + movementSpread) * scale;

            DrawSegment(new Rect(x - gap - length, y - thickness * 0.5f, length, thickness), color, outlineColor, scale);
            DrawSegment(new Rect(x + gap, y - thickness * 0.5f, length, thickness), color, outlineColor, scale);
            DrawSegment(new Rect(x - thickness * 0.5f, y - gap - length, thickness, length), color, outlineColor, scale);
            DrawSegment(new Rect(x - thickness * 0.5f, y + gap, thickness, length), color, outlineColor, scale);
        }

        float killAge = Time.unscaledTime - killMarkerStarted;
        if (killAge < KillMarkerDuration && CanShowWeaponKill(killViewCamera))
            DrawKillMarker(new Vector2(x, y), scale, killAge);
    }

    private void DrawKillMarker(Vector2 center, float scale, float age)
    {
        float expansion = Mathf.SmoothStep(0f, 1f, age / KillMarkerExpandTime);
        float alpha = 1f - Mathf.SmoothStep(0f, 1f,
            (age - KillMarkerExpandTime) / (KillMarkerDuration - KillMarkerExpandTime));
        float gap = Mathf.Lerp(7f, 13f, expansion) * scale;
        float length = Mathf.Lerp(8f, 11f, expansion) * scale;
        float thickness = Mathf.Lerp(2.5f, 3.5f, expansion) * scale;
        Color tint = killMarkerColor;
        tint.a *= alpha;
        Color outline = outlineColor;
        outline.a *= alpha;

        Matrix4x4 originalMatrix = GUI.matrix;
        Color originalColor = GUI.color;
        int originalDepth = GUI.depth;
        GUI.depth = -1; // The marker sits over the ADS reticle as well as the hip-fire reticle.
        Rect segment = new Rect(center.x + gap, center.y - thickness * 0.5f, length, thickness);
        for (int angle = 45; angle < 360; angle += 90)
        {
            GUI.matrix = originalMatrix;
            GUIUtility.RotateAroundPivot(angle, center);
            DrawSegment(segment, tint, outline, scale);
        }
        GUI.matrix = originalMatrix;
        GUI.color = originalColor;
        GUI.depth = originalDepth;
    }

    private bool ShouldDraw()
    {
        return isActiveAndEnabled
               && (aimBridge == null || !aimBridge.IsAiming)
               && !TrapSelectionMenu.CursorOwned
               && !GameStartMenu.CursorOwned;
    }

    private void DrawSegment(Rect rect, Color tint, Color outlineTint, float scale)
    {
        float outline = outlineThickness * scale;
        if (outline > 0f)
        {
            GUI.color = outlineTint;
            GUI.DrawTexture(new Rect(rect.x - outline, rect.y - outline, rect.width + outline * 2f, rect.height + outline * 2f), pixel);
        }

        GUI.color = tint;
        GUI.DrawTexture(rect, pixel);
        GUI.color = Color.white;
    }
}
