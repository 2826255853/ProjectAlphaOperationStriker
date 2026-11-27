using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Animates the XM777 outriggers and spades over its placement cooldown.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
public sealed class XM777DeploymentAnimation : MonoBehaviour
{
    [SerializeField] private Transform[] legPivots = new Transform[4];
    [SerializeField] private Transform[] spadePivots = new Transform[4];
    [FormerlySerializedAs("legDropDegrees")]
    [SerializeField, Range(0f, 100f)] private float legFoldDegrees = 76f;
    [SerializeField, Range(0f, 60f)] private float legDeployDegrees = 44.85f;
    [SerializeField, Range(0f, 90f)] private float spadeSetDegrees = 28f;
    [SerializeField, Min(0f)] private float legStaggerSeconds = 0.12f;

    private TrapInstance trap;
    private Quaternion[] legRest;
    private Quaternion[] spadeRest;

    public Transform[] LegPivots => legPivots;
    public Transform[] SpadePivots => spadePivots;

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        legRest = Capture(legPivots);
        spadeRest = Capture(spadePivots);
        ApplyPose(0f);
    }

    private void LateUpdate()
    {
        ApplyPose(trap.DeploymentProgress);
    }

    private void ApplyPose(float progress)
    {
        for (int i = 0; i < legPivots.Length; i++)
        {
            if (legPivots[i] == null || spadePivots[i] == null) continue;
            float start = Mathf.Min(0.28f, i * legStaggerSeconds / TrapInstance.DeploymentCooldownSeconds);
            float t = Ease(Mathf.InverseLerp(start, 1f, progress));
            // On the imported hinges positive local Y folds the legs toward the chassis.
            float legAngle = Mathf.Lerp(legFoldDegrees, -legDeployDegrees, t);
            float spadeAngle = Mathf.Lerp(-spadeSetDegrees, spadeSetDegrees, t);
            legPivots[i].localRotation = legRest[i] * Quaternion.AngleAxis(legAngle, Vector3.up);
            spadePivots[i].localRotation = spadeRest[i] * Quaternion.AngleAxis(spadeAngle, Vector3.up);
        }
    }

    private static Quaternion[] Capture(Transform[] pivots)
    {
        var rotations = new Quaternion[pivots.Length];
        for (int i = 0; i < pivots.Length; i++)
            rotations[i] = pivots[i] != null ? pivots[i].localRotation : Quaternion.identity;
        return rotations;
    }

    private static float Ease(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
