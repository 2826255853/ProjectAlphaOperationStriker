using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Builds the XM777 gameplay prefab from its finished FBX model.</summary>
public static class XM777TrapAuthoring
{
    public const string ModelPath = "Assets/Models/XM777/XM777.fbx";
    public const string PrefabPath = "Assets/Prefabs/XM777.prefab";
    public const string DefinitionPath = "Assets/Resources/XM777.asset";
    public const string TrapId = "xm777";
    private const float FootprintMeters = 4f;

    [MenuItem("Tools/塔防/生成 XM777 火炮陷阱")]
    public static void CreateAssets()
    {
        AssetDatabase.Refresh();
        XM777VfxAuthoring.CreateEffects();
        // Blender staging cameras/lights are not part of the gameplay model.
        // Keep this in the generator as well as the FBX import settings so a
        // rebuilt prefab cannot regain a camera that renders over the player.
        if (AssetImporter.GetAtPath(ModelPath) is ModelImporter importer
            && (importer.importCameras || importer.importLights))
        {
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到 XM777 模型：{ModelPath}");
            return;
        }

        Directory.CreateDirectory("Assets/Prefabs");
        Directory.CreateDirectory("Assets/Resources");
        TrapDefinition definition = LoadOrCreateDefinition();

        GameObject root = UnityEngine.Object.Instantiate(model);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "XM777";
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // The FBX has independent base and turret roots. Reparent the turret to a
        // yaw pivot while preserving its authored 45-degree rest pose. Runtime
        // elevation is applied relative to this pose on the independent cradle.
        Transform baseRoot = FindDeepChild(root.transform, "XM777_Base_Root");
        Transform turretRoot = FindDeepChild(root.transform, "XM777_Turret_Root");
        Transform pitchRoot = FindDeepChild(root.transform, "XM777_Pitch_Root");
        Transform muzzleMesh = FindDeepChild(root.transform, "Muzzle_Bore");
        if (baseRoot == null || turretRoot == null || pitchRoot == null || muzzleMesh == null)
        {
            UnityEngine.Object.DestroyImmediate(root);
            Debug.LogError("XM777 模型层级不完整：需要 Base_Root、Turret_Root、Pitch_Root 与 Muzzle_Bore。");
            return;
        }

        Vector3 muzzleDirection = muzzleMesh.position - pitchRoot.position;
        if (muzzleDirection.sqrMagnitude < 0.0001f) muzzleDirection = Vector3.forward;
        muzzleDirection.Normalize();

        // The model is authored as two independent top-level roots; make its
        // elevated cradle follow the turning turret without changing its pose.
        pitchRoot.SetParent(turretRoot, true);
        turretRoot.SetParent(baseRoot, true);
        GameObject yawPivotObject = new GameObject("Yaw Pivot");
        yawPivotObject.transform.SetParent(baseRoot, false);
        yawPivotObject.transform.SetPositionAndRotation(turretRoot.position, turretRoot.rotation);
        turretRoot.SetParent(yawPivotObject.transform, false);
        turretRoot.localPosition = Vector3.zero;
        turretRoot.localRotation = Quaternion.identity;

        GameObject muzzleObject = new GameObject("Muzzle");
        muzzleObject.transform.SetParent(pitchRoot, true);
        muzzleObject.transform.position = muzzleMesh.position + muzzleDirection * 0.08f;
        muzzleObject.transform.rotation = Quaternion.LookRotation(muzzleDirection, Vector3.up);

        FitToFootprint(root, FootprintMeters);

        TrapInstance trapInstance = root.GetComponent<TrapInstance>();
        if (trapInstance == null) trapInstance = root.AddComponent<TrapInstance>();
        SetObjectReference(trapInstance, "definition", definition);

        XM777Trap weapon = root.GetComponent<XM777Trap>();
        if (weapon == null) weapon = root.AddComponent<XM777Trap>();
        var weaponSerialized = new SerializedObject(weapon);
        weaponSerialized.FindProperty("yawPivot").objectReferenceValue = yawPivotObject.transform;
        weaponSerialized.FindProperty("pitchPivot").objectReferenceValue = pitchRoot;
        weaponSerialized.FindProperty("muzzle").objectReferenceValue = muzzleObject.transform;
        weaponSerialized.FindProperty("muzzleEffectPrefab").objectReferenceValue = XM777VfxAuthoring.LoadMuzzlePrefab();
        weaponSerialized.FindProperty("impactEffectPrefab").objectReferenceValue = XM777VfxAuthoring.LoadImpactPrefab();
        weaponSerialized.FindProperty("restBarrelForwardLocal").vector3Value =
            yawPivotObject.transform.InverseTransformDirection(muzzleDirection).normalized;
        weaponSerialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null)
        {
            Debug.LogError($"生成 XM777 预制体失败：{PrefabPath}");
            return;
        }

        var definitionSerialized = new SerializedObject(definition);
        definitionSerialized.FindProperty("prefab").objectReferenceValue = prefab;
        definitionSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"XM777 预制体和游戏陷阱定义已生成：{PrefabPath} / {DefinitionPath}");
    }

    public static void CreateAssetsBatch()
    {
        CreateAssets();
    }

    public static void ValidatePresentationBatch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (prefab == null || importer == null || importer.importCameras || importer.importLights)
            throw new InvalidOperationException("XM777 model must exclude staging cameras and lights.");
        if (prefab.GetComponentsInChildren<Camera>(true).Length != 0
            || prefab.GetComponentsInChildren<Light>(true).Length != 0
            || prefab.GetComponentsInChildren<AudioListener>(true).Length != 0)
            throw new InvalidOperationException("XM777 prefab contains staging presentation components; rebuild it.");
        XM777Trap trap = prefab.GetComponent<XM777Trap>();
        if (trap == null || trap.PitchPivot == null || trap.Muzzle == null || trap.YawPivot == null
            || !trap.Muzzle.IsChildOf(trap.PitchPivot) || !trap.PitchPivot.IsChildOf(trap.YawPivot))
            throw new InvalidOperationException("XM777 prefab must wire yaw -> pitch -> muzzle.");
        // Resolve the impact through the same helper the generator uses, so a stale
        // reference (for example XM777_Impact left on the prefab while the gameplay
        // impact is Explosion1) is reported instead of silently accepted.
        GameObject muzzleFx = AssetDatabase.LoadAssetAtPath<GameObject>(XM777VfxAuthoring.MuzzlePrefabPath);
        GameObject impactFx = XM777VfxAuthoring.LoadImpactPrefab();
        if (muzzleFx == null || impactFx == null || trap.MuzzleEffectPrefab != muzzleFx
            || trap.ImpactEffectPrefab != impactFx)
            throw new InvalidOperationException(
                $"XM777 prefab effect references are out of date (muzzle={NameOf(trap.MuzzleEffectPrefab)}, "
                + $"impact={NameOf(trap.ImpactEffectPrefab)}; expected impact={NameOf(impactFx)}). "
                + "Rebuild with Tools/塔防/生成 XM777 火炮陷阱.");
        if (muzzleFx.GetComponent<XM777VfxPlayback>() == null
            || muzzleFx.GetComponentsInChildren<ParticleSystem>(true).Length < 2
            || impactFx.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
            throw new InvalidOperationException("XM777 effect prefabs are missing particle playback or required emitters.");
        GameObject generatedImpact = AssetDatabase.LoadAssetAtPath<GameObject>(XM777VfxAuthoring.ImpactPrefabPath);
        if (impactFx == generatedImpact
            && (impactFx.GetComponent<XM777VfxPlayback>() == null
                || impactFx.GetComponentsInChildren<ParticleSystem>(true).Length < 4
                || impactFx.transform.Find("Ground Shockwave") == null))
            throw new InvalidOperationException("Generated XM777_Impact fallback is missing playback, emitters or the ground shockwave.");
        Debug.Log("XM777_PRESENTATION_PASS: yaw/pitch/muzzle and both particle prefabs wired; no cameras, lights or audio listeners.");
    }

    private static string NameOf(UnityEngine.Object asset) => asset != null ? asset.name : "<none>";

    private static TrapDefinition LoadOrCreateDefinition()
    {
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        bool created = definition == null;
        if (created)
        {
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("trapId").stringValue = TrapId;
        serialized.FindProperty("displayName").stringValue = "XM777 重型火炮";
        if (created) serialized.FindProperty("cost").intValue = 40;
        serialized.FindProperty("footprintWidth").intValue = 1;
        serialized.FindProperty("footprintHeight").intValue = 1;
        serialized.FindProperty("worldFootprint").vector2Value = new Vector2(FootprintMeters, FootprintMeters);
        serialized.FindProperty("localRotation").vector3Value = Vector3.zero;
        serialized.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Ground;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null) throw new MissingFieldException(target.GetType().Name, propertyName);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void FitToFootprint(GameObject root, float targetMeters)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (footprint > 0.0001f) root.transform.localScale *= targetMeters / footprint;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName) return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }
}
