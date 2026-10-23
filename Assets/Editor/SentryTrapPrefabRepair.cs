using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 修复哨戒机枪的出厂陷阱预制体。
///
/// 背景：<c>Assets/Prefabs/AutoSentryTurret.prefab</c> 在 Unity 里被导入为
/// <c>BrokenPrefabAsset</c>（主资源类型不是 GameObject，只剩一个 ImportLog 子资源），
/// 于是两份 <c>AutoSentryTurret.asset</c> 里按 GUID 引用的 <c>prefab</c> 运行时解析成 null，
/// 放置哨戒机枪时一直走 <c>TrapPlacementGrid.CreateFallbackTrap</c> 的兜底分支。
///
/// 根因（由 <see cref="DiagnoseBatch"/> 从 ImportLog 读出）：
/// <c>Prefab file is corrupt: it contains an object using fileID 100100000, which is reserved
/// for the Prefab asset.</c> —— 这个 prefab 是手写 YAML，把根 GameObject 的 fileID 用成了 Unity
/// 保留给 prefab 资产本身的 <c>100100000</c>，现代 Unity 直接判为损坏。
///
/// 处理方式：不手工改 YAML，而是把原文件备份到桌面素材目录后，用 Unity 自己
/// <c>PrefabUtility.SaveAsPrefabAsset</c> 重建同路径预制体。路径不变 → `.meta` 不变 →
/// GUID 不变；重建会换掉根对象 fileID，所以两份 TrapDefinition 的 `prefab` 引用一并重指。
///
/// 菜单：Tools/塔防/哨戒机枪/诊断出厂陷阱预制体、Tools/塔防/哨戒机枪/修复出厂陷阱预制体
/// 批处理：SentryTrapPrefabRepair.DiagnoseBatch、SentryTrapPrefabRepair.RepairBatch
/// </summary>
public static class SentryTrapPrefabRepair
{
    public const string TrapPrefabPath = "Assets/Prefabs/AutoSentryTurret.prefab";
    private const string BackupRoot =
        @"C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\backups";

    [MenuItem("Tools/塔防/哨戒机枪/诊断出厂陷阱预制体")]
    public static void DiagnoseBatch()
    {
        AssetDatabase.ImportAsset(TrapPrefabPath, ImportAssetOptions.ForceUpdate);
        Object main = AssetDatabase.LoadMainAssetAtPath(TrapPrefabPath);
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(TrapPrefabPath);
        var report = new StringBuilder();
        report.AppendLine("[SentryTrapPrefabRepair] path=" + TrapPrefabPath);
        report.AppendLine("  guid=" + AssetDatabase.AssetPathToGUID(TrapPrefabPath));
        report.AppendLine("  main=" + Describe(main));
        report.AppendLine("  subAssets=" + all.Length);
        foreach (Object asset in all)
        {
            report.AppendLine("    - " + Describe(asset));
            DumpMembers(report, asset, "      ", 0);
        }
        GameObject loaded = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        report.AppendLine("  LoadAssetAtPath<GameObject>=" + (loaded == null ? "null" : loaded.name));
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/塔防/哨戒机枪/修复出厂陷阱预制体")]
    public static void RepairBatch()
    {
        string guidBefore = AssetDatabase.AssetPathToGUID(TrapPrefabPath);

        // Back up whatever currently sits at the path (asset + meta) before writing.
        string backup = Path.Combine(BackupRoot,
            "trapprefab-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        if (File.Exists(TrapPrefabPath))
            File.Copy(TrapPrefabPath, Path.Combine(backup, "AutoSentryTurret.prefab"), true);
        if (File.Exists(TrapPrefabPath + ".meta"))
            File.Copy(TrapPrefabPath + ".meta", Path.Combine(backup, "AutoSentryTurret.prefab.meta"), true);
        Debug.Log("[SentryTrapPrefabRepair] backup: " + backup);

        // Rebuild with Unity's own serializer so the result is a valid prefab asset.
        GameObject root = new GameObject("Auto Sentry Turret");
        try
        {
            root.AddComponent<TrapInstance>();
            AutoSentryTurret turret = root.AddComponent<AutoSentryTurret>();

            // AutoSentryTurret is [ExecuteAlways], so AddComponent already ran Awake and
            // instantiated the runtime art. Strip it: the prefab must stay a bare root and
            // let every placed instance build its own art, otherwise each trap shows two guns.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name == "Sentry Machine Gun Art") Object.DestroyImmediate(child.gameObject);
            }
            var serialized = new SerializedObject(turret);
            serialized.FindProperty("aimPivot").objectReferenceValue = null;
            serialized.FindProperty("muzzle").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TrapPrefabPath, out bool success);
            if (!success || saved == null)
                throw new InvalidOperationException("SaveAsPrefabAsset failed for " + TrapPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.ImportAsset(TrapPrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        GameObject repaired = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        if (repaired != null)
        {
            // The rebuilt root has a fresh local fileID, so the definitions' explicit
            // {fileID: 100100000, guid: ...} references must be re-pointed at it.
            RepointDefinition("Assets/Resources/AutoSentryTurret.asset", repaired);
            RepointDefinition("Assets/TrapDefinitions/AutoSentryTurret.asset", repaired);
            AssetDatabase.SaveAssets();
        }

        string guidAfter = AssetDatabase.AssetPathToGUID(TrapPrefabPath);
        Object main = AssetDatabase.LoadMainAssetAtPath(TrapPrefabPath);
        var definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/Resources/AutoSentryTurret.asset");
        var gridDefinition = AssetDatabase.LoadAssetAtPath<TrapDefinition>("Assets/TrapDefinitions/AutoSentryTurret.asset");

        int failures = 0;
        Check(main is GameObject, "main asset is a GameObject (was " + Describe(main) + ")", ref failures);
        Check(repaired != null, "LoadAssetAtPath<GameObject> succeeds", ref failures);
        Check(guidAfter == guidBefore && !string.IsNullOrEmpty(guidAfter),
            "GUID preserved (" + guidBefore + " -> " + guidAfter + ")", ref failures);
        Check(repaired != null && repaired.GetComponent<TrapInstance>() != null,
            "root carries TrapInstance", ref failures);
        Check(repaired != null && repaired.GetComponent<AutoSentryTurret>() != null,
            "root carries AutoSentryTurret", ref failures);
        Check(repaired != null && repaired.transform.childCount == 0,
            "root stays a bare trap root (art is built in Awake)", ref failures);
        Check(definition != null && definition.Prefab == repaired,
            "Resources/AutoSentryTurret.asset now resolves its prefab", ref failures);
        Check(gridDefinition != null && gridDefinition.Prefab == repaired,
            "TrapDefinitions/AutoSentryTurret.asset now resolves its prefab", ref failures);

        if (failures > 0)
            throw new InvalidOperationException("SENTRY_TRAP_PREFAB_REPAIR_FAIL: " + failures + " checks failed");

        Debug.Log("SENTRY_TRAP_PREFAB_REPAIR_PASS: 8 checks; backup=" + backup);
    }

    /// <summary>Read-only probe used by the stage 5 acceptance and by hand.</summary>
    public static bool IsLoadable(out GameObject prefab)
    {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
        return prefab != null;
    }

    private static void RepointDefinition(string definitionPath, GameObject prefab)
    {
        var definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(definitionPath);
        if (definition == null)
            throw new InvalidOperationException("Missing trap definition: " + definitionPath);
        var serialized = new SerializedObject(definition);
        SerializedProperty property = serialized.FindProperty("prefab");
        property.objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }

    private static void Check(bool condition, string message, ref int failures)
    {
        if (condition)
        {
            Debug.Log("[SentryTrapPrefabRepair] ok: " + message);
            return;
        }
        failures++;
        Debug.LogError("[SentryTrapPrefabRepair] FAILED: " + message);
    }

    private static string Describe(Object asset)
    {
        return asset == null ? "null" : asset.GetType().FullName + " '" + asset.name + "'";
    }

    /// <summary>Dumps whatever the ImportLog sub-asset exposes, so the reason is visible.</summary>
    private static void DumpMembers(StringBuilder report, object asset, string indent, int depth)
    {
        if (asset == null || depth > 2) return;
        Type type = asset.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                             BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
            object value;
            try { value = property.GetValue(asset); }
            catch (Exception) { continue; }
            AppendMember(report, indent, property.Name, value, depth);
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance))
        {
            object value;
            try { value = field.GetValue(asset); }
            catch (Exception) { continue; }
            AppendMember(report, indent, field.Name, value, depth);
        }
    }

    private static void AppendMember(StringBuilder report, string indent, string name, object value, int depth)
    {
        if (value is IEnumerable enumerable && !(value is string))
        {
            report.AppendLine(indent + name + ":");
            int count = 0;
            foreach (object item in enumerable)
            {
                if (count++ > 10) break;
                report.AppendLine(indent + "  [" + count + "] " + (item == null ? "null" : item.GetType().Name));
                DumpMembers(report, item, indent + "    ", depth + 1);
            }
            return;
        }
        report.AppendLine(indent + name + " = " + FormatValue(value));
    }

    private static string FormatValue(object value)
    {
        if (value == null) return "null";
        if (value is string text) return text;
        return value.ToString();
    }
}
