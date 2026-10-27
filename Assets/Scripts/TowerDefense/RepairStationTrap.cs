using System;
using UnityEngine;

/// <summary>Four independent repair arms. Only real, grid-owned ground traps can operate.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrapInstance))]
public sealed class RepairStationTrap : MonoBehaviour
{
    [Serializable]
    public sealed class ArmRig
    {
        public Transform yaw, pitch, elbow, wrist;
        public Renderer torchRenderer;
        public int emissiveMaterialIndex = -1;
        public Vector3 yawWorkEuler, pitchWorkEuler, elbowWorkEuler, wristWorkEuler;
        [NonSerialized] public Quaternion yawRest, pitchRest, elbowRest, wristRest;
        [NonSerialized] private MaterialPropertyBlock emissionBlock;

        public bool IsBound => yaw != null && pitch != null && elbow != null && wrist != null;
        public void CaptureRest()
        {
            if (!IsBound) return;
            yawRest = yaw.localRotation; pitchRest = pitch.localRotation;
            elbowRest = elbow.localRotation; wristRest = wrist.localRotation;
        }
        public void Pose(float blend)
        {
            if (!IsBound) return;
            yaw.localRotation = Quaternion.Slerp(yawRest, yawRest * Quaternion.Euler(yawWorkEuler), blend);
            pitch.localRotation = Quaternion.Slerp(pitchRest, pitchRest * Quaternion.Euler(pitchWorkEuler), blend);
            elbow.localRotation = Quaternion.Slerp(elbowRest, elbowRest * Quaternion.Euler(elbowWorkEuler), blend);
            wrist.localRotation = Quaternion.Slerp(wristRest, wristRest * Quaternion.Euler(wristWorkEuler), blend);
            if (torchRenderer != null && emissiveMaterialIndex >= 0)
            {
                if (emissionBlock == null) emissionBlock = new MaterialPropertyBlock();
                torchRenderer.GetPropertyBlock(emissionBlock, emissiveMaterialIndex);
                emissionBlock.SetColor("_EmissionColor", new Color(0.1f, 0.85f, 1f) * (6f * blend));
                torchRenderer.SetPropertyBlock(emissionBlock, emissiveMaterialIndex);
            }
        }
    }

    [SerializeField, Min(0.01f)] private float repairAmount = 25f;
    [SerializeField, Min(0.01f)] private float cooldownSeconds = 4f;
    [SerializeField, Min(0.01f)] private float deploySeconds = 0.3f;
    [SerializeField, Min(0f)] private float holdSeconds = 0.4f;
    [SerializeField, Min(0.01f)] private float retractSeconds = 0.3f;
    [SerializeField] private ArmRig[] arms = new ArmRig[4];

    private TrapInstance self;
    private readonly TrapInstance[] targets = new TrapInstance[4];
    private readonly TrapInstance[] animatingTargets = new TrapInstance[4];
    private readonly float[] readyAt = new float[4];
    private readonly float[] animationAt = { float.NegativeInfinity, float.NegativeInfinity,
        float.NegativeInfinity, float.NegativeInfinity };
    private readonly int[] repairCounts = new int[4];
    private readonly Vector3[] sourceCorners = new Vector3[4], targetCorners = new Vector3[4];
    private bool restCaptured;

    public float RepairAmount => repairAmount;
    public float CooldownSeconds => cooldownSeconds;
    public int GetRepairCount(int arm) => repairCounts[arm];
    public TrapInstance GetTarget(int arm) => targets[arm];
    public float GetCooldownRemaining(int arm) => Mathf.Max(0f, readyAt[arm] - Time.time);
    public bool HasBoundArms => arms != null && arms.Length == 4 && Array.TrueForAll(arms, a => a != null && a.IsBound);

    private void Awake()
    {
        self = GetComponent<TrapInstance>();
        CaptureRest();
    }

    public void ConfigureArms(ArmRig[] bindings)
    {
        if (bindings == null || bindings.Length != 4) throw new ArgumentException("Four arm rigs required.");
        arms = bindings;
        restCaptured = false;
        CaptureRest();
    }

    private void CaptureRest()
    {
        if (restCaptured || !HasBoundArms) return;
        foreach (ArmRig arm in arms) arm.CaptureRest();
        restCaptured = true;
        foreach (ArmRig arm in arms) arm.Pose(0f);
    }

    private void Update()
    {
        // Scaled game time pauses both cooldown and pose, including the first repair.
        if (Time.deltaTime <= 0f) return;
        Tick(Time.time);
    }

    // Explicit time input keeps deterministic editor acceptance on the same runtime path.
    public void Tick(float now)
    {
        if (self == null) self = GetComponent<TrapInstance>();
        CaptureRest();
        if (!isActiveAndEnabled || !TrapPlacementGeometry.TryGetGroundFootprint(self, sourceCorners))
        {
            ClearTargetsAndPose();
            return;
        }
        Array.Clear(targets, 0, targets.Length);
        foreach (TrapInstance candidate in TrapInstance.ActiveTraps)
        {
            if (candidate.CurrentHealth >= candidate.MaxHealth) continue;
            int side = TrapPlacementGeometry.FindSharedGroundSide(self, candidate, sourceCorners, targetCorners);
            if (side >= 0 && IsBetter(candidate, targets[side])) targets[side] = candidate;
        }
        for (int i = 0; i < 4; i++)
        {
            TrapInstance target = targets[i];
            if (now >= readyAt[i] && target != null && target.isActiveAndEnabled
                && TrapPlacementGeometry.FindSharedGroundSide(self, target, sourceCorners, targetCorners) == i
                && target.Repair(repairAmount) > 0f)
            {
                readyAt[i] = now + Mathf.Max(0.01f, cooldownSeconds);
                animationAt[i] = now;
                animatingTargets[i] = target;
                repairCounts[i]++;
            }
            // A fully repaired target may finish its action; removed/displaced targets may not.
            if (animatingTargets[i] != null
                && TrapPlacementGeometry.FindSharedGroundSide(self, animatingTargets[i], sourceCorners, targetCorners) != i)
                animationAt[i] = float.NegativeInfinity;
            else if (animatingTargets[i] == null) animationAt[i] = float.NegativeInfinity;
            float elapsed = now - animationAt[i];
            float blend = elapsed < deploySeconds ? Mathf.Clamp01(elapsed / Mathf.Max(0.01f, deploySeconds))
                : elapsed < deploySeconds + holdSeconds ? 1f
                : 1f - Mathf.Clamp01((elapsed - deploySeconds - holdSeconds) / Mathf.Max(0.01f, retractSeconds));
            if (restCaptured) arms[i].Pose(Mathf.SmoothStep(0f, 1f, blend));
        }
    }

    private bool IsBetter(TrapInstance candidate, TrapInstance previous)
    {
        if (previous == null) return true;
        float difference = candidate.CurrentHealth / candidate.MaxHealth - previous.CurrentHealth / previous.MaxHealth;
        if (Mathf.Abs(difference) > 0.00001f) return difference < 0f;
        float distance = (candidate.transform.position - transform.position).sqrMagnitude;
        float oldDistance = (previous.transform.position - transform.position).sqrMagnitude;
        return Mathf.Abs(distance - oldDistance) > 0.00001f ? distance < oldDistance
            : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(candidate)
                < System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(previous);
    }

    private void OnDisable() => ClearTargetsAndPose();
    private void ClearTargetsAndPose()
    {
        Array.Clear(targets, 0, targets.Length);
        Array.Clear(animatingTargets, 0, animatingTargets.Length);
        for (int i = 0; i < 4; i++)
        {
            animationAt[i] = float.NegativeInfinity;
            if (restCaptured) arms[i].Pose(0f);
        }
        // Deliberately preserve readyAt: toggling a component cannot bypass cooldown.
    }
}
