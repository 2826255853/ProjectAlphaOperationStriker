using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Non-destructive authoring/validation; does not rebuild the user's trap menu.</summary>
public static class TrapUpgradeCatalogAuthoring
{
    public const string CatalogPath = "Assets/Resources/TrapUpgradeCatalog.asset";
    private static readonly string[] DefinitionFolders = { "Assets/Resources", "Assets/TrapDefinitions" };
    private static readonly string[] OfficialTrapIds = { "auto_sentry_turret", "ground-spike", "dual_missile_launcher_loaded",
        "flamethrower", "betty7", "throat-lamp", "repair-station" };

    [MenuItem("Tools/塔防/陷阱升级/创建缺失的默认配置")]
    public static void CreateDefaultIfMissing()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>(CatalogPath);
        if (catalog == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(CatalogPath) != null)
                throw new InvalidOperationException($"Another asset occupies {CatalogPath}.");
            catalog = ScriptableObject.CreateInstance<TrapUpgradeCatalog>();
            var serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = OfficialTrapIds.Length;
            for (int i = 0; i < OfficialTrapIds.Length; i++)
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("trapId").stringValue = OfficialTrapIds[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
        }
        ValidateAll();
    }

    [MenuItem("Tools/塔防/陷阱升级/校验配置与所有陷阱定义")]
    public static void ValidateAll()
    {
        TrapDefinition[] definitions = AssetDatabase.FindAssets("t:TrapDefinition", DefinitionFolders)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<TrapDefinition>).ToArray();
        TrapUpgradeCatalog.ValidateDefinitions(definitions);
        string[] catalogs = AssetDatabase.FindAssets("t:TrapUpgradeCatalog", DefinitionFolders);
        if (catalogs.Length != 1 || AssetDatabase.GUIDToAssetPath(catalogs[0]) != CatalogPath)
            throw new InvalidOperationException($"Use one authoritative upgrade catalog at {CatalogPath}; found {catalogs.Length}.");
        var catalog = AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>(CatalogPath);
        catalog.Validate();
        var ids = definitions.Select(value => value.TrapId).ToHashSet(StringComparer.Ordinal);
        foreach (TrapUpgradeCatalog.Entry entry in catalog.Entries)
            if (!ids.Contains(entry.trapId)) throw new InvalidOperationException($"Unknown catalog TrapId '{entry.trapId}'.");
        foreach (string officialId in OfficialTrapIds)
        {
            if (!ids.Contains(officialId))
                throw new InvalidOperationException($"Missing official TrapId definition '{officialId}'.");
            if (!catalog.Entries.Any(entry => string.Equals(entry.trapId, officialId, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Missing default upgrade configuration for official TrapId '{officialId}'.");
        }
        Debug.Log($"TRAP_UPGRADE_CATALOG_PASS: {definitions.Length} definitions, {ids.Count} TrapIds, one catalog; both definition folders checked.");
    }
}
