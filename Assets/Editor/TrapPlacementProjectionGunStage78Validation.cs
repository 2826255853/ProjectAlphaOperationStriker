#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-safe acceptance checks for projection-gun stages 7-8.
/// This deliberately does not replace PlayMode or real-input acceptance.
/// </summary>
public static class TrapPlacementProjectionGunStage78Validation
{
    private const string PassPrefix = "PROJECTION_GUN_STAGE78_PASS";

    [MenuItem("Tools/塔防/投影枪/验收阶段七八契约")]
    public static void Run()
    {
        int checks = 0;
        GameObject host = null;
        GameObject prefab = null;
        try
        {
            SetStatic(typeof(TrapSelectionMenu), "IsOpen", false);
            SetStatic(typeof(GameStartMenu), "CursorOwned", false);

            host = new GameObject("Projection gun stage 7-8 validation");
            var placement = host.AddComponent<TrapPlacementController>();
            var gun = host.AddComponent<TrapPlacementProjectionGunController>();
            prefab = new GameObject("Projection gun validation prefab");
            Set(gun, "placement", placement);
            Set(gun, "projectionGunPrefab", prefab);
            Invoke(gun, "TryBindPlacement");

            Check(Get(gun, "projectionGunInstance") == null,
                "projection gun instance is lazy before first entry", ref checks);

            placement.EnterPlacementMode();
            var instance = (GameObject)Get(gun, "projectionGunInstance");
            Check(instance != null, "projection gun instance created on entry", ref checks);
            Check(placement.PlacementMode, "placement mode enters", ref checks);
            Check(instance.activeSelf, "projection gun shown in placement mode", ref checks);
            placement.ExitPlacementMode();
            Check(!instance.activeSelf, "projection gun hidden after exit", ref checks);

            UnityEngine.Object.DestroyImmediate(instance);
            Set(gun, "projectionGunInstance", null);
            Set(gun, "projectionGunPrefab", null);
            Set(gun, "missingPrefabWarningIssued", false);
            Invoke(gun, "WarnMissingPrefabOnce");
            Invoke(gun, "WarnMissingPrefabOnce");
            Check(Get(gun, "projectionGunInstance") == null, "missing prefab creates no replacement", ref checks);
            Check((bool)Get(gun, "missingPrefabWarningIssued"), "missing prefab warning is latched", ref checks);

            placement.EnterPlacementMode();
            int secondary = 0;
            placement.ProjectionGunSecondaryAction += () => secondary++;
            placement.OnProjectionGunSecondaryAction();
            Check(secondary == 1, "right click reservation event fires", ref checks);
            Check(placement.PlacementMode, "right click keeps placement mode", ref checks);
            SetStatic(typeof(TrapSelectionMenu), "IsOpen", true);
            placement.OnProjectionGunSecondaryAction();
            Check(secondary == 1, "trap menu blocks forwarded right click", ref checks);
            Check(!placement.IsProjectionGunInputActive, "trap menu owns projection input", ref checks);

            var shooterHost = new GameObject("Projection gun hitscan boundary validation");
            try
            {
                var shooter = shooterHost.AddComponent<FPSHitscanShooter>();
                Invoke(shooter, "FireHitscan");
                Check(true, "hitscan exits safely while placement mode is active", ref checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(shooterHost); }

            Debug.Log($"{PassPrefix}: {checks} assertions");
        }
        finally
        {
            SetStatic(typeof(TrapSelectionMenu), "IsOpen", false);
            SetStatic(typeof(GameStartMenu), "CursorOwned", false);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
        }
    }

    private static void Check(bool condition, string label, ref int checks)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Projection gun validation failed: " + label);
    }

    private static object Get(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void Invoke(object target, string name, params object[] args)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }

    private static void SetStatic(Type type, string name, object value)
    {
        type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .GetSetMethod(true).Invoke(null, new[] { value });
    }
}
#endif
