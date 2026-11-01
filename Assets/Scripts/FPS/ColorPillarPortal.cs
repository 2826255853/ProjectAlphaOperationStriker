using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends a player who remains inside a colored pillar to another active pillar
/// with the same color identifier.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class ColorPillarPortal : MonoBehaviour
{
    [SerializeField] private string colorId = "Blue";
    [SerializeField, Min(0.1f)] private float activationSeconds = 3f;
    [SerializeField] private Transform arrivalPoint;

    private static readonly List<ColorPillarPortal> ActivePortals = new List<ColorPillarPortal>();
    private readonly Dictionary<Transform, float> DwellTimes = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, int> LastAccumulatedFrame = new Dictionary<Transform, int>();
    private readonly HashSet<Transform> MustExitBeforeRearming = new HashSet<Transform>();
    private BoxCollider triggerCollider;

    public string ColorId => colorId;
    public float ActivationSeconds => activationSeconds;

    private void Awake()
    {
        triggerCollider = GetComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
    }

    private void OnEnable()
    {
        if (!ActivePortals.Contains(this))
            ActivePortals.Add(this);
    }

    private void OnDisable()
    {
        ActivePortals.Remove(this);
        DwellTimes.Clear();
        LastAccumulatedFrame.Clear();
        MustExitBeforeRearming.Clear();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!TryGetPlayerRoot(other, out Transform playerRoot))
            return;

        if (MustExitBeforeRearming.Contains(playerRoot))
            return;

        if (LastAccumulatedFrame.TryGetValue(playerRoot, out int lastFrame) && lastFrame == Time.frameCount)
            return;

        LastAccumulatedFrame[playerRoot] = Time.frameCount;
        DwellTimes.TryGetValue(playerRoot, out float elapsed);
        elapsed += Time.deltaTime;
        if (elapsed < activationSeconds)
        {
            DwellTimes[playerRoot] = elapsed;
            return;
        }

        DwellTimes.Remove(playerRoot);
        ColorPillarPortal destination = FindDestination();
        if (destination != null)
        {
            destination.MustExitBeforeRearming.Add(playerRoot);
            Teleport(playerRoot, destination);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (TryGetPlayerRoot(other, out Transform playerRoot))
        {
            DwellTimes.Remove(playerRoot);
            LastAccumulatedFrame.Remove(playerRoot);
            MustExitBeforeRearming.Remove(playerRoot);
        }
    }

    private bool TryGetPlayerRoot(Collider other, out Transform playerRoot)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller != null)
        {
            playerRoot = controller.transform;
            return true;
        }

        Transform candidate = other.transform.root;
        if (candidate.CompareTag("Player"))
        {
            playerRoot = candidate;
            return true;
        }

        playerRoot = null;
        return false;
    }

    private ColorPillarPortal FindDestination()
    {
        ColorPillarPortal nearest = null;
        float nearestSqrDistance = float.PositiveInfinity;
        for (int i = 0; i < ActivePortals.Count; i++)
        {
            ColorPillarPortal candidate = ActivePortals[i];
            if (candidate != null && candidate != this && candidate.isActiveAndEnabled &&
                candidate.colorId == colorId)
            {
                float sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearest = candidate;
                    nearestSqrDistance = sqrDistance;
                }
            }
        }

        return nearest;
    }

    private static void Teleport(Transform playerRoot, ColorPillarPortal destination)
    {
        Transform exit = destination.arrivalPoint != null
            ? destination.arrivalPoint
            : destination.transform;

        CharacterController controller = playerRoot.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;

        playerRoot.SetPositionAndRotation(exit.position, exit.rotation);

        if (controller != null)
            controller.enabled = true;
    }
}
