using UnityEngine;

/// <summary>Art rig for the SM-04 telescoping stand. Authored transforms store its deployed pose.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-20)]
public sealed class SentryDeployment : MonoBehaviour
{
    [SerializeField] private Transform mast;
    [SerializeField] private Transform carriage;
    [SerializeField] private Transform aim;
    [SerializeField] private Transform[] legs;
    [SerializeField] private Transform[] shoes;

    private Vector3[] legRest;
    private Vector3[] shoeRest;
    private TrapInstance trap;
    private bool deploying;

    private void Awake()
    {
        legRest = new Vector3[legs.Length];
        shoeRest = new Vector3[shoes.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            legRest[i] = legs[i].localPosition;
            shoeRest[i] = shoes[i].localPosition;
        }
    }

    private void OnEnable()
    {
        trap = GetComponentInParent<TrapInstance>();
        if (trap == null) return;
        trap.DeploymentStarted += BeginDeployment;
        if (trap.DeploymentProgress < 1f) BeginDeployment();
    }

    private void OnDisable()
    {
        if (trap != null) trap.DeploymentStarted -= BeginDeployment;
        if (deploying) ApplyPose(1f);
        deploying = false;
    }

    public void HoldPlacementPose()
    {
        deploying = false;
        ApplyPose(0f);
    }

    private void BeginDeployment()
    {
        deploying = true;
        ApplyPose(trap.DeploymentProgress);
    }

    private void Update()
    {
        if (!deploying) return;
        float progress = trap.DeploymentProgress;
        ApplyPose(progress);
        deploying = progress < 1f;
    }

    private void LateUpdate()
    {
        // The fork swivels with the head while the receiver alone pitches and recoils.
        carriage.localRotation = Quaternion.Euler(0f, aim.localEulerAngles.y, 0f);
    }

    private void ApplyPose(float progress)
    {
        for (int i = 0; i < legs.Length; i++)
        {
            float delay = i / 2 * 0.075f;
            float extend = Stage(progress, delay, delay + 0.50f);
            float plant = Stage(progress, delay + 0.42f, delay + 0.68f);
            Vector3 radial = Vector3.ProjectOnPlane(legRest[i], Vector3.up).normalized;
            legs[i].localPosition = legRest[i] - radial * (0.34f * (1f - extend));
            shoes[i].localPosition = shoeRest[i] + Vector3.up * (0.057f * (1f - plant));
        }

        mast.localPosition = Vector3.down * (0.095f * (1f - Stage(progress, 0.48f, 0.89f)));
        aim.localRotation = Quaternion.Euler(12f * (1f - Stage(progress, 0.63f, 1f)), 0f, 0f);
    }

    private static float Stage(float progress, float start, float end) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, progress));
}
