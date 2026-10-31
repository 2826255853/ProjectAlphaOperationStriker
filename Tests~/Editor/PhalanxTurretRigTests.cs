#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regression net for the Phalanx CIWS rig. The imported FBX keeps Blender's
/// axis frame (PHX_Base_Root carries an X=-90 node rotation), so a rig that
/// rotates around parent-local Vector3.up traverses around a HORIZONTAL axis
/// and never converges on the target. These cases lock the world-axis
/// resolution in PhalanxTurretRig.
/// </summary>
public sealed class PhalanxTurretRigTests
{
    private const string PrefabPath = "Assets/Prefabs/Phalanx.prefab";

    [Test]
    public void GeneratedPrefabConvergesOnAWorldTarget()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "missing prefab: " + PrefabPath);
        GameObject root = Object.Instantiate(prefab);
        try
        {
            PhalanxTurretRig rig = root.GetComponent<PhalanxTurretRig>();
            Assert.IsNotNull(rig, "prefab is missing PhalanxTurretRig");
            Assert.IsNotNull(rig.YawPivot);
            Assert.IsNotNull(rig.PitchPivot);
            Assert.IsNotNull(rig.BarrelCluster);
            Assert.IsNotNull(rig.Muzzle);

            rig.ResetToRestPose();
            Vector3 target = new Vector3(5.5f, 6f, 8f);
            for (int i = 0; i < 120; i++) rig.AimAt(target, 1f / 30f);

            Vector3 toTarget = (target - rig.PitchPivot.position).normalized;
            float angle = Vector3.Angle(rig.BarrelDirection, toTarget);
            Assert.Less(angle, 1f, $"barrel did not converge (angle={angle:F2})");
            Assert.IsTrue(rig.IsAimedAt(target, 1f), "IsAimedAt should report the converged pose");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TraverseAxisStaysVerticalUnderAnAxisRotatedModelNode()
    {
        // Rebuilds the imported frame: the model node is rotated -90 deg about X,
        // so children live in Blender's Z-up coordinates.
        GameObject rigRoot = new GameObject("AxisRotatedRig");
        GameObject baseRoot = new GameObject("Base_Root");
        baseRoot.transform.SetParent(rigRoot.transform, false);
        baseRoot.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);

        Transform yaw = NewPivot("Yaw_Root", baseRoot.transform, new Vector3(0f, 0f, 0.46f));
        Transform pitch = NewPivot("Pitch_Root", yaw, new Vector3(0f, -0.30f, 1.06f));
        Transform barrel = NewPivot("Barrel_Root", pitch, new Vector3(0f, -0.74f, 1.06f));
        Transform muzzle = NewPivot("Muzzle", barrel, new Vector3(0f, -1.50f, 1.06f));

        var rigObject = new GameObject("Rig");
        rigObject.transform.SetParent(rigRoot.transform, false);
        PhalanxTurretRig rig = rigObject.AddComponent<PhalanxTurretRig>();
        var serialized = new SerializedObject(rig);
        serialized.FindProperty("yawPivot").objectReferenceValue = yaw;
        serialized.FindProperty("pitchPivot").objectReferenceValue = pitch;
        serialized.FindProperty("barrelCluster").objectReferenceValue = barrel;
        serialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        try
        {
            rig.EnsureRestPose();
            Quaternion restYaw = yaw.localRotation;
            rig.AimAt(new Vector3(6f, 3f, 2f), 0.5f);

            Quaternion delta = yaw.localRotation * Quaternion.Inverse(restYaw);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f) degrees = 360f - degrees;
            Vector3 worldAxis = yaw.parent.rotation * axis.normalized;
            Assert.Greater(degrees, 1f, "the yaw pivot should have turned");
            Assert.Greater(Mathf.Abs(Vector3.Dot(worldAxis, Vector3.up)), 0.98f,
                $"traverse axis must be world up, got {worldAxis:F3}");

            // Elevation must not leak into the traverse: a target 3 m up at
            // ~6.3 m range is about 25 deg, so the barrel has to lift, and the
            // yaw rotation itself must stay horizontal.
            float barrelElevation = Mathf.Asin(Mathf.Clamp(rig.BarrelDirection.y, -1f, 1f))
                * Mathf.Rad2Deg;
            Assert.Greater(barrelElevation, 5f, "barrel should elevate toward the raised target");
        }
        finally
        {
            Object.DestroyImmediate(rigRoot);
        }
    }

    private static Transform NewPivot(string name, Transform parent, Vector3 localPosition)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = localPosition;
        return pivot.transform;
    }
}
#endif
