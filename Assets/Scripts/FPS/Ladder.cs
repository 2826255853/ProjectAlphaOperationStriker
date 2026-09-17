using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("Gameplay/Ladder")]
public sealed class Ladder : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float height = 3f;
    [SerializeField, Min(0.2f)] private float width = 1f;
    [SerializeField, Min(0.2f)] private float depth = 0.8f;
    private BoxCollider trigger;
    public float Bottom => transform.position.y;
    public float Top => transform.position.y + height;
    private void Awake() => EnsureTrigger();
    private void OnValidate() => EnsureTrigger();
    private void EnsureTrigger()
    {
        trigger = GetComponent<BoxCollider>();
        if (trigger == null) trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = Vector3.up * (height * 0.5f);
        trigger.size = new Vector3(width, height, depth);
    }
}

[AddComponentMenu("Gameplay/Ladder Climbing")]
[RequireComponent(typeof(CharacterController))]
public sealed class LadderClimbing : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float climbSpeed = 3f;
    private CharacterController controller;
    private Ladder ladder;
    private int ladderContacts;
    private float detachLockUntil;
    public bool IsClimbing { get; private set; }
    private void Awake() => controller = GetComponent<CharacterController>();
    private void Update()
    {
        if (ladder == null || ladderContacts <= 0)
        {
            IsClimbing = false;
            return;
        }

        // Climbing is entered deliberately with vertical input. This prevents
        // a player spawned inside/near a trigger from being put into the state
        // before they interact with the ladder.
        if (!IsClimbing && Time.time >= detachLockUntil && Mathf.Abs(ReadVerticalInput()) > 0.01f)
        {
            IsClimbing = true;
        }

        if (!IsClimbing) return;

        // Horizontal movement and jump are the conventional ways to leave a
        // ladder. Release the climb immediately and let the normal controller
        // move the player out of the trigger volume.
        if (ReadHorizontalInput().sqrMagnitude > 0.04f || WasJumpPressed())
        {
            ExitClimb();
        }
    }
    private void LateUpdate()
    {
        if (!IsClimbing || ladder == null || controller == null) return;
        Vector3 position = transform.position;
        float targetY = Mathf.Clamp(position.y + ReadVerticalInput() * climbSpeed * Time.deltaTime, ladder.Bottom, ladder.Top);
        controller.Move(Vector3.up * (targetY - position.y));
    }
    private static float ReadVerticalInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return 0f;
        float value = 0f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value -= 1f;
        return value;
    }

    private static Vector2 ReadHorizontalInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return Vector2.zero;
        return new Vector2((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), 0f);
    }

    private static bool WasJumpPressed()
    {
        return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
               || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
    }

    public void ExitClimb()
    {
        IsClimbing = false;
        // Prevent the same held key from reattaching on the next frame.
        detachLockUntil = Time.time + 0.25f;
    }
    private void OnTriggerEnter(Collider other)
    {
        Ladder found = other.GetComponentInParent<Ladder>();
        if (found == null) return;
        ladder = found; ladderContacts++;
    }
    private void OnTriggerExit(Collider other)
    {
        Ladder found = other.GetComponentInParent<Ladder>();
        if (found == null || found != ladder) return;
        ladderContacts = Mathf.Max(0, ladderContacts - 1);
        if (ladderContacts == 0) { ladder = null; IsClimbing = false; }
    }
}
