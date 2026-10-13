#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Focused editor validation for the step 7 feedback contract.</summary>
public static class FlamethrowerFeedbackValidation
{
    private const string PrefabPath = "Assets/Resources/Flamethrower.prefab";
    private static int assertions;

    public static void Run()
    {
        assertions = 0;
        GameObject prefabContents = null;
        GameObject probe = null;
        GameObject burnProbe = null;
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab != null, "正式火焰喷射器预制体存在");
            Check(prefab.GetComponent<FlamethrowerTrap>() != null, "正式预制体保留 FlamethrowerTrap");

            prefabContents = PrefabUtility.LoadPrefabContents(PrefabPath);
            Check(FindChild(prefabContents.transform, "Flame Origin") != null, "存在 Flame Origin 挂点");
            Check(FindChild(prefabContents.transform, "Preheat Fx") != null, "存在 Preheat Fx 挂点");
            Check(FindChild(prefabContents.transform, "Spray Fx") != null, "存在 Spray Fx 挂点");
            Check(FindChild(prefabContents.transform, "Cooldown Fx") != null, "存在 Cooldown Fx 挂点");

            probe = new GameObject("FlamethrowerFeedbackValidationProbe");
            FlamethrowerFeedback feedback = probe.AddComponent<FlamethrowerFeedback>();
            feedback.Bind(null);
            Check(feedback.IsInitialized, "反馈组件可在缺少最终资源时初始化");

            feedback.SetState(FlamethrowerTrap.AttackState.Delay);
            Check(feedback.CurrentState == FlamethrowerTrap.AttackState.Delay, "Delay 回调可用");
            feedback.SetState(FlamethrowerTrap.AttackState.Spraying);
            Check(feedback.CurrentState == FlamethrowerTrap.AttackState.Spraying, "Spraying 回调可用");
            feedback.SetState(FlamethrowerTrap.AttackState.Cooldown);
            Check(feedback.CurrentState == FlamethrowerTrap.AttackState.Cooldown, "Cooldown 回调可用");
            feedback.SetState(FlamethrowerTrap.AttackState.Ready);
            Check(feedback.CurrentState == FlamethrowerTrap.AttackState.Ready, "Ready 回调可用");
            feedback.ResetFeedback();
            Check(feedback.CurrentState == FlamethrowerTrap.AttackState.Ready, "重置后回到 Ready");

            burnProbe = new GameObject("FireStatusEffectVisualValidationProbe");
            EnemyHealth health = burnProbe.AddComponent<EnemyHealth>();
            health.ResetHealth();
            FireStatusEffect.Ignite(health, 1f, 5f);
            FireStatusEffectVisual burnVisual = burnProbe.GetComponent<FireStatusEffectVisual>();
            Check(burnVisual != null, "点燃时自动创建敌人燃烧表现");
            Check(burnVisual.IsBurning, "敌人燃烧表现随状态点燃");
            burnProbe.GetComponent<FireStatusEffect>().Clear();
            Check(!burnVisual.IsBurning, "燃烧清理时关闭敌人表现");

            Debug.Log($"FLAMETHROWER_STEP7_PASS: {assertions} assertions");
        }
        finally
        {
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            if (burnProbe != null) UnityEngine.Object.DestroyImmediate(burnProbe);
            if (prefabContents != null) PrefabUtility.UnloadPrefabContents(prefabContents);
        }
    }

    private static Transform FindChild(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName) return children[i];
        }
        return null;
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("步骤7断言失败：" + message);
    }
}
#endif
