using UnityEngine;

/// <summary>The FBX stores the supported pose. Only a ghost or a new placement retracts it.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance), typeof(MissileLauncherTurret))]
[DefaultExecutionOrder(-20)]
public sealed class MissileLauncherDeployment : MonoBehaviour
{
    [SerializeField] private Transform[] outriggers;
    [SerializeField] private Transform[] shoes;

    private TrapInstance trap;
    private MissileLauncherTurret turret;
    private Vector3[] legRest, shoeRest, retractOffset, shoeLift;
    private Vector3 mastRest, mastDrop;
    private bool deploying;

    private void Awake()
    {
        trap = GetComponent<TrapInstance>();
        turret = GetComponent<MissileLauncherTurret>();
        int count = outriggers.Length;
        legRest = new Vector3[count];
        shoeRest = new Vector3[count];
        retractOffset = new Vector3[count];
        shoeLift = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Transform leg = outriggers[i];
            Transform shoe = shoes[i];
            legRest[i] = leg.localPosition;
            shoeRest[i] = shoe.localPosition;
            Vector3 radial = Vector3.ProjectOnPlane(transform.InverseTransformPoint(leg.position), Vector3.up);
            retractOffset[i] = ToParentVector(leg, -radial.normalized * 0.25f);
            shoeLift[i] = ToParentVector(shoe, Vector3.up * 0.072f);
        }
        mastRest = turret.YawPivot.localPosition;
        mastDrop = ToParentVector(turret.YawPivot, Vector3.down * 0.15f);
    }

    private Vector3 ToParentVector(Transform part, Vector3 rootLocalOffset)
    {
        return part.parent.InverseTransformVector(transform.TransformVector(rootLocalOffset));
    }

    private void OnEnable()
    {
        trap.DeploymentStarted += BeginDeployment;
    }

    private void OnDisable()
    {
        trap.DeploymentStarted -= BeginDeployment;
    }

    public void HoldPlacementPose()
    {
        deploying = false;
        ApplyPose(0f);
    }

    private void BeginDeployment()
    {
        deploying = true;
        ApplyPose(0f);
    }

    private void Update()
    {
        if (!deploying) return;
        float progress = trap.DeploymentProgress;
        ApplyPose(progress);
        deploying = progress < 1f;
    }

    private void ApplyPose(float progress)
    {
        for (int i = 0; i < outriggers.Length; i++)
        {
            float stagger = (i / 2) * 0.065f;
            float extend = Stage(progress, stagger, 0.4f + stagger);
            float settle = Stage(progress, 0.38f + stagger, 0.62f + stagger);
            outriggers[i].localPosition = legRest[i] + retractOffset[i] * (1f - extend);
            shoes[i].localPosition = shoeRest[i] + shoeLift[i] * (1f - settle);
        }
        turret.YawPivot.localPosition = mastRest + mastDrop * (1f - Stage(progress, 0.38f, 0.86f));
        float pitch = turret.RestPitchDegrees * Stage(progress, 0.58f, 1f) * Mathf.Deg2Rad;
        Vector3 direction = turret.RestBarrelDirectionWorld * Mathf.Cos(pitch) + transform.up * Mathf.Sin(pitch);
        turret.SnapAimAt(turret.PitchPivot.position + direction);
    }

    private static float Stage(float progress, float start, float end) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, progress));
}
