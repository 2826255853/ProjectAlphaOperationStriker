using System.Collections.Generic;
using UnityEngine;
using KINEMATION.FPSAnimationPack.Scripts.Player;

/// <summary>Keeps the existing first-person arms in a two-handed pistol grip.</summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class TrapPlacementProjectionGunGrip : MonoBehaviour
{
    // Calibrated local joint rotations for the player's SK_Arms_Mono skeleton.
    // Each row contains the proximal, middle and distal joints of one finger.
    private static readonly Vector3[] FingerAngles =
    {
        new Vector3(-14.81f, 34.17f, -22.20f), new Vector3(1.41f, 0.19f, -77.99f), new Vector3(0.68f, -0.65f, -35.75f),
        new Vector3(1.90f, 25.50f, -75.89f), new Vector3(-2.22f, -0.68f, -78.54f), new Vector3(-0.82f, 4.85f, -41.69f),
        new Vector3(-1.86f, 35.20f, -82.95f), new Vector3(0.68f, 1.56f, -75.69f), new Vector3(0.81f, -3.39f, -32.81f),
        new Vector3(-7.08f, 35.10f, -97.46f), new Vector3(1.30f, 1.08f, -45.15f), new Vector3(-0.02f, -3.33f, -59.20f),
        new Vector3(62.11f, -116.12f, -66.19f), new Vector3(-7.10f, 2.41f, -47.28f), new Vector3(2.48f, 0.05f, -13.83f)
    };

    private Transform model;
    private Transform gripAnchor;
    private Transform view;
    private Transform rightHand;
    private Transform leftHand;
    private Transform[] posedBones;
    private Quaternion[] animationRotations;
    private Transform[] rightFingers;
    private Transform[] leftFingers;
    private Quaternion[] fingerRotations;
    private bool poseApplied;

    public void Bind(FPSPlayer player, Transform gunModel, Transform cameraTransform)
    {
        RestoreAnimationPose();
        posedBones = null;
        if (player == null || gunModel == null || cameraTransform == null) return;
        var bones = new Dictionary<string, Transform>();
        foreach (Transform bone in player.GetComponentsInChildren<Transform>(true))
            bones.TryAdd(bone.name, bone);
        if (!bones.TryGetValue("hand_r", out rightHand) || !bones.TryGetValue("hand_l", out leftHand)) return;
        if (rightHand.parent?.name != "lowerarm_r" || rightHand.parent.parent?.name != "upperarm_r"
            || rightHand.parent.parent.parent?.name != "clavicle_r"
            || leftHand.parent?.name != "lowerarm_l" || leftHand.parent.parent?.name != "upperarm_l"
            || leftHand.parent.parent.parent?.name != "clavicle_l") return;

        model = gunModel;
        view = cameraTransform;
        foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            if (node.name == "GripAnchor") gripAnchor = node;
        if (gripAnchor == null) return;
        var modified = new List<Transform>
        {
            rightHand.parent.parent.parent, rightHand.parent.parent, rightHand.parent, rightHand,
            leftHand.parent.parent.parent, leftHand.parent.parent, leftHand.parent, leftHand
        };
        var right = new List<Transform>();
        var left = new List<Transform>();
        var rotations = new List<Quaternion>();
        int fingerIndex = 0;
        foreach (string finger in new[] { "index", "middle", "ring", "pinky", "thumb" })
        {
            for (int joint = 1; joint <= 3; joint++)
            {
                string name = finger + "_0" + joint;
                if (bones.TryGetValue(name + "_r", out Transform r)
                    && bones.TryGetValue(name + "_l", out Transform l))
                {
                    right.Add(r);
                    left.Add(l);
                    modified.Add(r);
                    modified.Add(l);
                    rotations.Add(Quaternion.Euler(FingerAngles[fingerIndex]));
                }
                fingerIndex++;
            }
        }
        rightFingers = right.ToArray();
        leftFingers = left.ToArray();
        fingerRotations = rotations.ToArray();
        posedBones = modified.ToArray();
        animationRotations = new Quaternion[posedBones.Length];
    }

    private void Update() => RestoreAnimationPose();

    private void LateUpdate()
    {
        if (model == null || gripAnchor == null || view == null || posedBones == null) return;
        for (int i = 0; i < posedBones.Length; i++)
        {
            if (posedBones[i] == null) return;
            animationRotations[i] = posedBones[i].localRotation;
        }
        poseApplied = true;

        // Both wrists sit behind the grip, with the support hand cupping its left side.
        SolveArm(rightHand, gripAnchor.position + model.TransformVector(new Vector3(0.028f, -0.020f, -0.103f)),
            model.rotation * Quaternion.LookRotation(Vector3.up, Vector3.right),
            view.TransformPoint(new Vector3(0.42f, -0.48f, 0.12f)));
        SolveArm(leftHand, gripAnchor.position + model.TransformVector(new Vector3(-0.038f, -0.055f, -0.103f)),
            model.rotation * Quaternion.LookRotation(Vector3.down, Vector3.right),
            view.TransformPoint(new Vector3(-0.38f, -0.46f, 0.18f)));

        // This rig uses matching local finger axes on its mirrored hands.
        for (int i = 0; i < leftFingers.Length; i++)
        {
            rightFingers[i].localRotation = fingerRotations[i];
            leftFingers[i].localRotation = fingerRotations[i];
        }
    }

    private static void SolveArm(Transform hand, Vector3 target, Quaternion rotation, Vector3 elbowHint)
    {
        Transform forearm = hand.parent;
        Transform upperArm = forearm.parent;
        Transform clavicle = upperArm.parent;
        float upperLength = Vector3.Distance(upperArm.position, forearm.position);
        float lowerLength = Vector3.Distance(forearm.position, hand.position);
        float comfortableReach = (upperLength + lowerLength) * 0.96f;
        if (Vector3.Distance(upperArm.position, target) > comfortableReach)
        {
            // Raise the shoulder when aiming steeply upward, keeping both arm segments unscaled.
            Vector3 toTarget = target - clavicle.position;
            Vector3 toShoulder = upperArm.position - clavicle.position;
            float shoulderLength = toShoulder.magnitude;
            float cosine = Mathf.Clamp((toTarget.sqrMagnitude + shoulderLength * shoulderLength
                - comfortableReach * comfortableReach) / (2f * toTarget.magnitude * shoulderLength), -1f, 1f);
            Vector3 side = Vector3.ProjectOnPlane(toShoulder, toTarget).normalized;
            Vector3 raised = toTarget.normalized * cosine + side * Mathf.Sqrt(1f - cosine * cosine);
            clavicle.rotation = Quaternion.FromToRotation(toShoulder, raised) * clavicle.rotation;
        }
        Vector3 shoulder = upperArm.position;
        Vector3 reach = target - shoulder;
        float distance = Mathf.Clamp(reach.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f,
            upperLength + lowerLength - 0.001f);
        Vector3 direction = reach.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(elbowHint - shoulder, direction).normalized;
        float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 elbow = shoulder + direction * along + bend * height;
        upperArm.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upperArm.rotation;
        forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position,
            shoulder + direction * distance - forearm.position) * forearm.rotation;
        hand.rotation = rotation;
    }

    private void RestoreAnimationPose()
    {
        if (!poseApplied) return;
        for (int i = 0; i < posedBones.Length; i++)
            if (posedBones[i] != null) posedBones[i].localRotation = animationRotations[i];
        poseApplied = false;
    }

    private void OnDisable() => RestoreAnimationPose();
}
