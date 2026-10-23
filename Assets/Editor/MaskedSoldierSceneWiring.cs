using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把普通小怪预制体（蒙面士兵）接入主场景的地面出怪点。
/// 只改 EnemySpawnPoint 的 enemyPrefab / 逐波覆盖里的占位预制体，不碰飞行出怪口和路径、网格、经济数据。
/// 改动前把主场景复制到桌面素材目录，并输出一份 JSON 报告（改前/改后、跳过的对象）。
/// </summary>
public static class MaskedSoldierSceneWiring
{
    public const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    const string PlaceholderPrefabName = "TestMonster";

    [MenuItem("Tools/塔防/蒙面士兵/一键接入地面出怪点（含场景备份）")]
    public static void WireMenu()
    {
        var summary = WireGroundSpawners(true);
        EditorUtility.DisplayDialog("蒙面士兵", summary, "好");
    }

    /// <summary>批处理入口：-executeMethod MaskedSoldierSceneWiring.WireGroundSpawnersBatch</summary>
    public static void WireGroundSpawnersBatch()
    {
        WireGroundSpawners(false);
    }

    /// <summary>接入地面出怪点并保存场景；返回面向用户的摘要。</summary>
    public static string WireGroundSpawners(bool interactive)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaskedSoldierAuthoring.PrefabPath);
        if (prefab == null)
        {
            throw new FileNotFoundException("找不到蒙面士兵预制体: " + MaskedSoldierAuthoring.PrefabPath);
        }

        if (!File.Exists(ScenePath))
        {
            throw new FileNotFoundException("找不到主场景: " + ScenePath);
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var materialRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹",
            "MaskedSoldier");
        var backupDir = Path.Combine(materialRoot, "scene-backups", stamp);
        var reportDir = Path.Combine(materialRoot, "scene-wiring");
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(reportDir);

        // 备份必须在打开/保存场景之前完成。
        var backupPath = Path.Combine(backupDir, Path.GetFileName(ScenePath));
        File.Copy(ScenePath, backupPath, true);
        var sceneMeta = ScenePath + ".meta";
        if (File.Exists(sceneMeta))
        {
            File.Copy(sceneMeta, backupPath + ".meta", true);
        }

        var active = SceneManager.GetActiveScene();
        if (!active.IsValid() || !string.Equals(active.path, ScenePath, StringComparison.OrdinalIgnoreCase))
        {
            if (active.IsValid() && active.isDirty)
            {
                EditorSceneManager.SaveOpenScenes();
            }

            active = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var spawnPoints = UnityEngine.Object.FindObjectsByType<EnemySpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        var rows = new List<string>();
        var changedPoints = 0;
        var skippedFlying = 0;
        var waveOverrides = 0;
        var keptOtherPrefabs = new List<string>();

        foreach (var point in spawnPoints.OrderBy(p => p.name, StringComparer.Ordinal))
        {
            var so = new SerializedObject(point);
            var flying = so.FindProperty("flyingEntrance");
            if (flying != null && flying.boolValue)
            {
                skippedFlying++;
                rows.Add(Row(point, "skipped-flying", null, null, 0));
                continue;
            }

            var rootProp = so.FindProperty("enemyPrefab");
            if (rootProp == null)
            {
                continue;
            }

            var before = rootProp.objectReferenceValue as GameObject;
            var beforePath = before != null ? AssetDatabase.GetAssetPath(before) : "(none)";
            var rootChanged = false;
            if (IsPlaceholderOrEmpty(before))
            {
                rootProp.objectReferenceValue = prefab;
                rootChanged = true;
                changedPoints++;
            }
            else
            {
                keptOtherPrefabs.Add(point.name + " -> " + beforePath);
            }

            var wavesProp = so.FindProperty("waveSpawnSettings");
            var pointWaveChanges = 0;
            if (wavesProp != null && wavesProp.isArray)
            {
                for (var i = 0; i < wavesProp.arraySize; i++)
                {
                    var element = wavesProp.GetArrayElementAtIndex(i);
                    var typeProp = element.FindPropertyRelative("monsterType");
                    if (typeProp != null && typeProp.intValue == (int)MonsterType.Flying)
                    {
                        continue;
                    }

                    var wavePrefab = element.FindPropertyRelative("enemyPrefab");
                    if (wavePrefab == null)
                    {
                        continue;
                    }

                    var current = wavePrefab.objectReferenceValue as GameObject;
                    if (IsPlaceholderOrEmpty(current))
                    {
                        wavePrefab.objectReferenceValue = prefab;
                        pointWaveChanges++;
                        waveOverrides++;
                    }
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            rows.Add(Row(point, rootChanged ? "root->MaskedSoldier" : "root-kept",
                beforePath, MaskedSoldierAuthoring.PrefabPath, pointWaveChanges));
        }

        EditorSceneManager.MarkSceneDirty(active);
        if (!EditorSceneManager.SaveScene(active))
        {
            throw new InvalidOperationException("保存场景失败: " + ScenePath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var reportPath = Path.Combine(reportDir, "wiring-" + stamp + ".json");
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"timestamp\": \"" + stamp + "\",");
        json.AppendLine("  \"scene\": \"" + ScenePath + "\",");
        json.AppendLine("  \"prefab\": \"" + MaskedSoldierAuthoring.PrefabPath + "\",");
        json.AppendLine("  \"prefab_guid\": \"" + AssetDatabase.AssetPathToGUID(MaskedSoldierAuthoring.PrefabPath) + "\",");
        json.AppendLine("  \"backup\": \"" + backupPath.Replace("\\", "/") + "\",");
        json.AppendLine("  \"spawn_points_total\": " + spawnPoints.Length + ",");
        json.AppendLine("  \"spawn_points_changed\": " + changedPoints + ",");
        json.AppendLine("  \"flying_points_skipped\": " + skippedFlying + ",");
        json.AppendLine("  \"wave_overrides_changed\": " + waveOverrides + ",");
        json.AppendLine("  \"kept_other_prefabs\": [" + string.Join(", ", keptOtherPrefabs.Select(k => "\"" + k.Replace("\"", "'") + "\"")) + "],");
        json.AppendLine("  \"points\": [");
        json.AppendLine("    " + string.Join("," + Environment.NewLine + "    ", rows.Select(r => "\"" + r.Replace("\\", "/") + "\"")));
        json.AppendLine("  ]");
        json.AppendLine("}");

        File.WriteAllText(reportPath, json.ToString(), new UTF8Encoding(false));
        Debug.Log("[MaskedSoldier] 接入完成 points=" + spawnPoints.Length
                  + " changed=" + changedPoints
                  + " flyingSkipped=" + skippedFlying
                  + " waveOverrides=" + waveOverrides
                  + " backup=" + backupPath
                  + " report=" + reportPath);

        var summary = "场景已备份到：\n" + backupPath
                      + "\n\n出怪口总数：" + spawnPoints.Length
                      + "\n已接入：" + changedPoints
                      + "\n跳过飞行出怪口：" + skippedFlying
                      + "\n逐波覆盖改为蒙面士兵：" + waveOverrides
                      + "\n\n报告：" + reportPath;
        if (interactive)
        {
            Debug.Log("[MaskedSoldier] " + summary);
        }

        return summary;
    }

    private static bool IsPlaceholderOrEmpty(GameObject candidate)
    {
        if (candidate == null)
        {
            return true;
        }

        var path = AssetDatabase.GetAssetPath(candidate);
        var fileName = string.IsNullOrEmpty(path) ? candidate.name : Path.GetFileNameWithoutExtension(path);
        return fileName.IndexOf(PlaceholderPrefabName, StringComparison.OrdinalIgnoreCase) >= 0
               || fileName.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Row(EnemySpawnPoint point, string action, string before, string after, int waveChanges)
    {
        return action + " | " + point.name + " | " + (before ?? "-") + " -> " + (after ?? "-") + " | waves=" + waveChanges;
    }
}
