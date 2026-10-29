using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Append missing controls to the user's existing prefabs; never rebuild their Canvas.</summary>
public static class TrapUpgradeMenuAuthoring
{
    private const string Root = "Assets/Resources/UI/";
    [MenuItem("Tools/行动打击/陷阱菜单/增量添加升级界面")]
    public static void AddMissingControls()
    {
        string backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "TrapUpgradeSystem", "step8", "prefab-backup");
        Directory.CreateDirectory(backup);
        string[] paths = { Root + "TrapSelectionMenuCard.prefab", Root + "TrapSelectionMenu.prefab" };
        foreach (string path in paths)
        {
            string target = Path.Combine(backup, Path.GetFileName(path));
            if (!File.Exists(target)) File.Copy(path, target);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.TryGetComponent(out TrapSelectionMenuCardView card)) card.EnsureUpgradeUI();
                if (root.TryGetComponent(out TrapSelectionMenuView view)) view.EnsureUpgradeUI();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        TrapSelectionMenuAuthoring.Validate();
        TrapUpgradeCatalogAuthoring.ValidateAll();
        Debug.Log("TRAP_UPGRADE_MENU_MIGRATION_PASS; existing Canvas preserved; backup=" + backup);
    }

    public static void AddMissingControlsAndPreview()
    {
        AddMissingControls();
        string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "TrapUpgradeSystem", "step8");
        TrapSelectionMenuAuthoring.RenderPreviewBatch(output);
    }
}
