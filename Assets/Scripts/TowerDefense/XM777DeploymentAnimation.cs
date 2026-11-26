using UnityEngine;

/// <summary>Animates the XM777 outriggers and spades over its placement cooldown.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
public sealed class XM777DeploymentAnimation : MonoBehaviour
{
    [SerializeField] private Transform[] legPivots = new Transform[4];
    [SerializeField] private Transform[] spadePivots = new Transform[4];
    [SerializeField, Range(0f, 100f)] private float legDropDegrees = 76f;
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
    }

    private void LateUpdate()
    {
        if (trap == null || legRest == null || spadeRest == null) return;

        float duration = TrapInstance.DeploymentCooldownSeconds;
        for (int i = 0; i < legPivots.Length; i++)
        {
            if (legPivots[i] == null || spadePivots[i] == null) continue;
            float legStart = Mathf.Min(0.28f, i * legStaggerSeconds / duration);
            float legProgress = Ease(Mathf.InverseLerp(legStart, 0.79f, trap.DeploymentProgress));
            float spadeStart = Mathf.Min(0.65f, legStart + 0.24f);
            float spadeProgress = Ease(Mathf.InverseLerp(spadeStart, 1f, trap.DeploymentProgress));
            legPivots[i].localRotation = legRest[i]
                * Quaternion.AngleAxis(legDropDegrees * legProgress, Vector3.up);
            spadePivots[i].localRotation = spadeRest[i]
                * Quaternion.AngleAxis(-spadeSetDegrees * spadeProgress, Vector3.up);
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
