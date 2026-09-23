using System.IO;
using UnityEditor;
using UnityEngine;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;

/// <summary>
/// Authoring menu that stamps a hand-tuned <see cref="WeaponDamageProfile"/>
/// onto the project's KINEMATION weapon prefabs.
///
/// The shooter already falls back to name-based class defaults, so this menu is
/// only needed when a designer wants an explicit, inspectable per-weapon value
/// instead of the implicit lookup.
/// </summary>
public static class WeaponDamageAuthoring
{
    private const string PrefabFolder = "Assets/KINEMATION/FPSAnimationPack/Prefabs";

    [MenuItem("Tools/塔防/给武器预制体写入伤害档案")]
    public static void ApplyProfilesToWeaponPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[WeaponDamageAuthoring] 在 {PrefabFolder} 下没找到武器预制体。");
            return;
        }

        int applied = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                continue;
            }

            try
            {
                if (root.GetComponentInChildren<FPSWeapon>(true) == null)
                {
                    continue;
                }

                WeaponDamageProfile profile = root.GetComponent<WeaponDamageProfile>();
                if (profile == null)
                {
                    profile = root.AddComponent<WeaponDamageProfile>();
                }

                profile.Configure(profile.DefaultStatsForThisWeapon());
                PrefabUtility.SaveAsPrefabAsset(root, path);
                applied++;
                Debug.Log($"[WeaponDamageAuthoring] {Path.GetFileName(path)} -> " +
                          $"{profile.Stats.WeaponClass} 基础伤害 {profile.Stats.BaseDamage:0.##}", root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[WeaponDamageAuthoring] 已为 {applied} 个武器预制体写入伤害档案。");
    }
}
