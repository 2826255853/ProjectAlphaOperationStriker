#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>蒙面士兵挥刀攻击姿态的编辑期检查（姿态由 MonsterWalkAnimator 在同一趟骨骼写入里叠加）。</summary>
public sealed class MonsterAttackPoseTests
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
    public void ContactPoseSwingsTheKnifeArmFarFromRest()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            Assert.IsNotNull(animator);
            animator.EnsureInitialized();

            var rightArm = FindBone(instance, "UpperArm.R", "UpperArm_R", "UpperArmR");
            var rightForearm = FindBone(instance, "LowerArm.R", "LowerArm_R", "LowerArmR");
            Assert.IsNotNull(rightArm, "缺少右上臂骨骼");
            Assert.IsNotNull(rightForearm, "缺少右前臂骨骼");

            var restArm = rightArm.localRotation;
            var restForearm = rightForearm.localRotation;

            // 接触帧：上臂大幅前挥，肘部几乎伸直（前臂接近导入姿态）。
            animator.ApplyPose(0f, 0f, 0.44f);
            Assert.Greater(Quaternion.Angle(restArm, rightArm.localRotation), 25f,
                "下劈帧右臂应明显离开静止姿态");

            // 举刀帧：收肘把刀带到身后，前臂姿态变化明显。
            animator.ApplyPose(0f, 0f, 0.30f);
            Assert.Greater(Quaternion.Angle(restForearm, rightForearm.localRotation), 20f,
                "举刀帧右前臂应随收肘动作变化");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void WindupAndContactSwingTheArmInOppositeDirections()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var rightArm = FindBone(instance, "UpperArm.R", "UpperArm_R", "UpperArmR");
            Assert.IsNotNull(rightArm);

            animator.ApplyPose(0f, 0f, 0.30f);
            var windup = rightArm.localRotation;
            animator.ApplyPose(0f, 0f, 0.44f);
            var contact = rightArm.localRotation;

            Assert.Greater(Quaternion.Angle(windup, contact), 60f,
                "举刀与下劈是两个相反的极端姿态，夹角应足够大");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void AttackOnlyMovesTheKnifeSide()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var leftArm = FindBone(instance, "UpperArm.L", "UpperArm_L", "UpperArmL");
            var leftLeg = FindBone(instance, "UpperLeg.L", "UpperLeg_L", "UpperLegL");
            Assert.IsNotNull(leftArm);
            Assert.IsNotNull(leftLeg);

            var restLeftArm = leftArm.localRotation;
            var restLeftLeg = leftLeg.localRotation;

            animator.ApplyPose(0f, 0f, 0.44f);

            Assert.Less(Quaternion.Angle(restLeftArm, leftArm.localRotation), 1f,
                "挥刀不应带动左臂（单手持刀）");
            Assert.Less(Quaternion.Angle(restLeftLeg, leftLeg.localRotation), 1f,
                "挥刀不应摆动腿部");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PoseEndsExactlyAtRestWhenTheSwingFinishes()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var rightArm = FindBone(instance, "UpperArm.R", "UpperArm_R", "UpperArmR");
            Assert.IsNotNull(rightArm);
            var rest = rightArm.localRotation;

            animator.ApplyPose(0f, 0f, 1f);

            Assert.Less(Quaternion.Angle(rest, rightArm.localRotation), 0.01f,
                "动作结束帧必须精确回到导入姿态，不能残留上一帧");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void NoAttackProgressKeepsTheIdlePose()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            var rightArm = FindBone(instance, "UpperArm.R", "UpperArm_R", "UpperArmR");
            Assert.IsNotNull(rightArm);
            var rest = rightArm.localRotation;

            animator.ApplyPose(0f, 0f, -1f);

            Assert.Less(Quaternion.Angle(rest, rightArm.localRotation), 1f,
                "不传挥刀进度时不应出现攻击姿态");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void TriggerAttackStartsAndFinishesOnItsOwn()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();

            Assert.IsFalse(animator.IsSwinging, "初始状态不应在挥刀");

            animator.TriggerAttack();
            Assert.IsTrue(animator.IsSwinging, "TriggerAttack 后应立即进入挥刀");
            Assert.AreEqual(0f, animator.AttackProgress, 1e-3f);

            animator.CancelAttack();
            Assert.IsFalse(animator.IsSwinging, "CancelAttack 后应立即退出挥刀");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void RestoreRestPoseAlsoClearsTheSwing()
    {
        var instance = InstantiatePrefab();
        try
        {
            var animator = instance.GetComponent<MonsterWalkAnimator>();
            animator.EnsureInitialized();
            animator.TriggerAttack();
            animator.ApplyPose(0f, 0f, 0.44f);

            animator.RestoreRestPose();

            Assert.IsFalse(animator.IsSwinging, "恢复静止姿态应同时结束挥刀状态");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
