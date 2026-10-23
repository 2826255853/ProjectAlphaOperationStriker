#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>蒙面士兵走路/待机程序化骨骼动画的编辑期检查。</summary>
public sealed class MonsterWalkAnimatorTests
{
    const string PrefabPath = "Assets/Prefabs/MaskedSoldier.prefab";

    static GameObject InstantiatePrefab()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "找不到预制体: " + PrefabPath);
        var instance = Object.Instantiate(prefab);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        return instance;
    }

    static Transform FindBone(GameObject instance, params string[] names)
    {
        var wanted = names.Select(Normalize).ToArray();
        return instance.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => wanted.Contains(Normalize(t.name)));
    }

    static string Normalize(string raw)
    {
        return new string(raw.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    [Test]
    public void MaskedSoldierPrefabCarriesWalkAnimator()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);
        Assert.IsNotNull(prefab.GetComponent<MonsterWalkAnimator>(), "预制体根节点缺少 MonsterWalkAnimator");
    }

    [Test]
    public void AnimatorFindsAllCoreBones()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            Assert.IsNotNull(animator);
            animator.EnsureInitialized();
            Assert.IsEmpty(animator.MissingBones, "缺少骨骼: " + string.Join(",", animator.MissingBones));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void WalkPoseSwingsLegsAndArms()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();

            var left = FindBone(instance, "UpperLeg.L", "UpperLeg_L", "UpperLegL", "Thigh.L");
            var right = FindBone(instance, "UpperLeg.R", "UpperLeg_R", "UpperLegR", "Thigh.R");
            var leftArm = FindBone(instance, "UpperArm.L", "UpperArm_L", "UpperArmL");
            Assert.IsNotNull(left, "缺少左大腿骨骼");
            Assert.IsNotNull(right, "缺少右大腿骨骼");
            Assert.IsNotNull(leftArm, "缺少左上臂骨骼");

            var leftRest = left.localRotation;
            var rightRest = right.localRotation;
            var leftArmRest = leftArm.localRotation;

            animator.ApplyPose(Mathf.PI * 0.5f, 1f);

            Assert.Greater(Quaternion.Angle(leftRest, left.localRotation), 10f, "走路姿态应明显摆动左大腿");
            Assert.Greater(Quaternion.Angle(leftArmRest, leftArm.localRotation), 5f, "走路姿态应摆动左上臂");

            // 左右腿相位相反：一个向前一个向后。
            var leftDelta = Quaternion.Angle(leftRest, left.localRotation);
            var rightDelta = Quaternion.Angle(rightRest, right.localRotation);
            Assert.Greater(leftDelta, 10f);
            Assert.Greater(rightDelta, 10f);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void IdlePoseKeepsLegsNearRest()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var leg = FindBone(instance, "UpperLeg.L", "UpperLeg_L", "UpperLegL", "Thigh.L");
            Assert.IsNotNull(leg);
            var rest = leg.localRotation;

            animator.ApplyPose(0f, 0f);

            Assert.Less(Quaternion.Angle(rest, leg.localRotation), 1f, "待机姿态不应摆动大腿");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void RestoreRestPoseReturnsExactImportedPose()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var leg = FindBone(instance, "UpperLeg.L", "UpperLeg_L", "UpperLegL", "Thigh.L");
            var rest = leg.localRotation;

            animator.ApplyPose(Mathf.PI * 0.5f, 1f);
            animator.RestoreRestPose();

            Assert.Less(Quaternion.Angle(rest, leg.localRotation), 0.01f, "RestoreRestPose 应精确恢复导入姿态");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void AnimationNeverMovesTheCharacterRoot()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var position = instance.transform.position;
            var rotation = instance.transform.rotation;

            animator.ApplyPose(Mathf.PI * 0.75f, 1f);

            Assert.AreEqual(position, instance.transform.position);
            Assert.AreEqual(rotation, instance.transform.rotation);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
