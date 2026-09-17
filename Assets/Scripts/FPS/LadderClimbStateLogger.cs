using UnityEngine;

/// <summary>
/// Logs once whenever the player enters the ladder-climbing state.
/// Add this component to the same GameObject as LadderClimbing.
/// </summary>
[AddComponentMenu("Gameplay/Debug/Ladder Climb State Logger")]
[RequireComponent(typeof(LadderClimbing))]
public sealed class LadderClimbStateLogger : MonoBehaviour
{
    private LadderClimbing ladderClimbing;
    private bool wasClimbing;

    private void Awake()
    {
        ladderClimbing = GetComponent<LadderClimbing>();
    }

    private void OnEnable()
    {
        // Treat enabling the component as a fresh observation period.
        wasClimbing = false;
    }

    private void Update()
    {
        if (ladderClimbing == null)
        {
            return;
        }

        bool isClimbing = ladderClimbing.IsClimbing;
        if (isClimbing && !wasClimbing)
        {
            Debug.Log($"[{nameof(LadderClimbStateLogger)}] Entered ladder-climbing state.", this);
        }

        wasClimbing = isClimbing;
    }
}
