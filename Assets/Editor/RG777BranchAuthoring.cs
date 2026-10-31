using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Builds the RG777 branch visual and wires it into the existing XM777 trap.</summary>
public static class RG777BranchAuthoring
{
    public const string VisualPrefabPath = "Assets/Resources/RG777Visual.prefab";
    public const string XM777PrefabPath = "Assets/Prefabs/XM777.prefab";
    private const string CatalogPath = "Assets/Resources/TrapUpgradeCatalog.asset";
    private const float FootprintMeters = 4f;

    [MenuItem("Tools/塔防/生成 XM777 的 RG777 四级分支")]
    public static void CreateAssets()
    {
        AssetDatabase.Refresh();
        RG777MaterialAuthoring.CreateMaterials();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(RG777MaterialAuthoring.ModelPath);
        if (model == null) throw new InvalidOperationException("找不到 RG777 模型：" + RG777MaterialAuthoring.ModelPath);

        GameObject visual = BuildVisual(model);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(visual, VisualPrefabPath);
        UnityEngine.Object.DestroyImmediate(visual);
        if (prefab == null) throw new InvalidOperationException("无法生成 RG777 分支视觉预制体：" + VisualPrefabPath);

        EnsureBranchCatalogEntry();
        WireBranchAdapter(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("RG777_BRANCH_PASS: URP materials, RG777 visual, XM777 branch adapter and upgrade catalog are wired.");
    }

    public static void CreateAssetsBatch() => CreateAssets();

    public static void ValidateIntegrationBatch()
    {
        TrapUpgradeCatalogAuthoring.ValidateAll();
        XM777TrapAuthoring.ValidatePresentationBatch();
        ValidateBatch();
    }

    public static void WireBranchAdapterIfAvailable()
    {
        GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        if (visual != null) WireBranchAdapter(visual);
    }

    public static void ValidateBatch()
    {
        GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        GameObject xm777 = AssetDatabase.LoadAssetAtPath<GameObject>(XM777PrefabPath);
        TrapUpgradeCatalog catalog = AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>(CatalogPath);
        if (visual == null || xm777 == null || catalog == null)
            throw new InvalidOperationException("RG777 validation requires both prefabs and the upgrade catalog.");

        RG777BranchAdapter adapter = xm777.GetComponent<RG777BranchAdapter>();
        XM777Trap weapon = xm777.GetComponent<XM777Trap>();
        if (adapter == null || weapon == null || !RG777BranchAdapter.IsAvailable("xm777", "rg777"))
            throw new InvalidOperationException("XM777 prefab or RG777 branch adapter is missing.");
        TrapUpgradeCatalog.Branch branch = catalog.GetBranch("xm777", XM777Trap.RG777BranchId);
        if (branch == null || !branch.integrated || !branch.overrideDamage || !Mathf.Approximately(branch.damage, 5f))
            throw new InvalidOperationException("RG777 upgrade branch is not integrated with the five-times damage definition.");
        if (FindDeepChild(visual.transform, "Yaw Pivot") == null
            || FindDeepChild(visual.transform, "XM777_Pitch_Root") == null
            || FindDeepChild(visual.transform, "Muzzle") == null)
            throw new InvalidOperationException("RG777 visual is missing its yaw/pitch/muzzle rig.");

        int materialSlots = 0;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null
                    || !material.shader.name.StartsWith("Universal Render Pipeline", StringComparison.Ordinal))
                    throw new InvalidOperationException("RG777 visual contains a non-URP material slot.");
                materialSlots++;
            }
        if (materialSlots == 0) throw new InvalidOperationException("RG777 visual has no material slots.");
        ValidateRuntimeSwitch(xm777);
        Debug.Log("RG777_BRANCH_VALIDATION_PASS: branch catalog, adapter, yaw/pitch/muzzle rig and "
            + materialSlots + " URP material slots verified.");
    }

    private static void ValidateRuntimeSwitch(GameObject xm777Prefab)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(xm777Prefab) as GameObject;
        if (instance == null) throw new InvalidOperationException("Cannot instantiate the XM777 prefab for branch validation.");
        try
        {
            TrapInstance trap = instance.GetComponent<TrapInstance>();
            XM777Trap weapon = instance.GetComponent<XM777Trap>();
            RG777BranchAdapter adapter = instance.GetComponent<RG777BranchAdapter>();
            if (trap == null || weapon == null || adapter == null)
                throw new InvalidOperationException("XM777 branch validation instance is missing a required component.");

            InvokePrivate(weapon, "Awake");
            InvokePrivate(adapter, "Awake");
            InvokePrivate(adapter, "OnEnable");
            float xm777Footprint = GetFootprint(weapon.GetComponentsInChildren<Renderer>(true));

            TrapUpgradeState branchState = new TrapUpgradeState(4, TrapUpgradeDirection.Branch, XM777Trap.RG777BranchId);
            typeof(TrapInstance).GetField("upgradeState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trap, branchState);
            InvokePrivate(adapter, "HandleUpgradeStateChanged", trap, branchState);

            if (!weapon.IsRG777Active || !adapter.IsBranchActive || !adapter.RG777Visual.activeSelf
                || weapon.YawPivot != FindDeepChild(adapter.RG777Visual.transform, "Yaw Pivot")
                || !Mathf.Approximately(weapon.EffectiveMinimumRange, 5f)
                || !Mathf.Approximately(weapon.EffectiveMaximumRange, 200f)
                || !Mathf.Approximately(weapon.EffectiveShellSpeed, 1000f)
                || !Mathf.Approximately(weapon.EffectiveFireInterval, 2f)
                || !Mathf.Approximately(weapon.EffectiveExplosionDamage, XM777Trap.BaseExplosionDamage * 5f)
                || !Mathf.Approximately(weapon.EffectiveElevationForDistance(5f), 0f)
                || !Mathf.Approximately(weapon.EffectiveElevationForDistance(102.5f), 15f)
                || !Mathf.Approximately(weapon.EffectiveElevationForDistance(200f), 30f))
                throw new InvalidOperationException("RG777 branch runtime tuning or visual switch did not match its specification.");
            float rg777Footprint = GetFootprint(adapter.RG777Visual.GetComponentsInChildren<Renderer>(true));
            if (Mathf.Abs(rg777Footprint - xm777Footprint) > Mathf.Max(0.1f, xm777Footprint * 0.1f))
                throw new InvalidOperationException($"RG777 visual footprint ({rg777Footprint:0.##} m) does not match "
                    + $"the XM777 base footprint ({xm777Footprint:0.##} m).");

            TrapUpgradeState initial = TrapUpgradeState.Initial;
            typeof(TrapInstance).GetField("upgradeState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trap, initial);
            InvokePrivate(adapter, "HandleUpgradeStateChanged", trap, initial);
            if (weapon.IsRG777Active || adapter.IsBranchActive || adapter.RG777Visual.activeSelf)
                throw new InvalidOperationException("XM777 base rig and visuals did not restore after leaving RG777.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        Type[] types = Array.ConvertAll(arguments, value => value.GetType());
        MethodInfo method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic, null, types, null);
        if (method == null) throw new MissingMethodException(target.GetType().Name, methodName);
        method.Invoke(target, arguments);
    }

    private static float GetFootprint(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            throw new InvalidOperationException("Cannot measure a visual prefab without renderers.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Max(bounds.size.x, bounds.size.z);
    }

    private static GameObject BuildVisual(GameObject model)
    {
        var root = UnityEngine.Object.Instantiate(model);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "RG777Visual";
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Transform baseRoot = FindDeepChild(root.transform, "XM777_Base_Root");
        Transform turretRoot = FindDeepChild(root.transform, "XM777_Turret_Root");
        Transform pitchRoot = FindDeepChild(root.transform, "XM777_Pitch_Root");
        Transform muzzleBore = FindDeepChild(root.transform, "Muzzle_Bore");
        if (baseRoot == null || turretRoot == null || pitchRoot == null || muzzleBore == null)
        {
            UnityEngine.Object.DestroyImmediate(root);
            throw new InvalidOperationException("RG777 FBX must contain XM777_Base_Root, XM777_Turret_Root, "
                + "XM777_Pitch_Root and Muzzle_Bore.");
        }

        Vector3 muzzleDirection = muzzleBore.position - pitchRoot.position;
        if (muzzleDirection.sqrMagnitude < 0.0001f) muzzleDirection = Vector3.forward;
        muzzleDirection.Normalize();
        pitchRoot.SetParent(turretRoot, true);
        turretRoot.SetParent(baseRoot, true);

        var yawObject = new GameObject("Yaw Pivot");
        yawObject.transform.SetParent(baseRoot, false);
        yawObject.transform.SetPositionAndRotation(turretRoot.position, turretRoot.rotation);
        turretRoot.SetParent(yawObject.transform, false);
        turretRoot.localPosition = Vector3.zero;
        turretRoot.localRotation = Quaternion.identity;

        Transform muzzle = FindDeepChild(root.transform, "Muzzle");
        if (muzzle == null)
        {
            var muzzleObject = new GameObject("Muzzle");
            muzzle = muzzleObject.transform;
        }
        muzzle.SetParent(pitchRoot, true);
        muzzle.position = muzzleBore.position + muzzleDirection * 0.08f;
        muzzle.rotation = Quaternion.LookRotation(muzzleDirection, Vector3.up);
        FitToFootprint(root, FootprintMeters);
        return root;
    }

    private static void EnsureBranchCatalogEntry()
    {
        TrapUpgradeCatalog catalog = AssetDatabase.LoadAssetAtPath<TrapUpgradeCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Missing upgrade catalog: " + CatalogPath);
        var serialized = new SerializedObject(catalog);
        SerializedProperty entries = serialized.FindProperty("entries");
        SerializedProperty xmEntry = null;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("trapId").stringValue == "xm777")
            {
                xmEntry = entry;
                break;
            }
        }
        if (xmEntry == null) throw new InvalidOperationException("Upgrade catalog has no xm777 entry.");

        SerializedProperty branches = xmEntry.FindPropertyRelative("branches");
        SerializedProperty rgBranch = null;
        for (int i = 0; i < branches.arraySize; i++)
            if (branches.GetArrayElementAtIndex(i).FindPropertyRelative("branchId").stringValue == XM777Trap.RG777BranchId)
            {
                rgBranch = branches.GetArrayElementAtIndex(i);
                break;
            }
        if (rgBranch == null)
        {
            int index = branches.arraySize++;
            rgBranch = branches.GetArrayElementAtIndex(index);
        }

        rgBranch.FindPropertyRelative("branchId").stringValue = XM777Trap.RG777BranchId;
        rgBranch.FindPropertyRelative("displayName").stringValue = "RG777 雷锤电磁炮";
        rgBranch.FindPropertyRelative("description").stringValue =
            "射程 5–200 米，炮管仰角随敌距在线性 0°–30° 间调整；2 秒一发，伤害为 XM777 一级状态的 5 倍。";
        rgBranch.FindPropertyRelative("integrated").boolValue = true;
        rgBranch.FindPropertyRelative("overrideCostMultiplier").boolValue = false;
        rgBranch.FindPropertyRelative("costMultiplier").doubleValue = 1.5d;
        rgBranch.FindPropertyRelative("overrideFixedCost").boolValue = false;
        rgBranch.FindPropertyRelative("fixedCost").intValue = 0;
        rgBranch.FindPropertyRelative("overrideDamage").boolValue = true;
        rgBranch.FindPropertyRelative("damage").floatValue = 5f;
        rgBranch.FindPropertyRelative("overrideRepair").boolValue = false;
        rgBranch.FindPropertyRelative("repair").floatValue = 2f;
        rgBranch.FindPropertyRelative("overrideMaxHealth").boolValue = false;
        rgBranch.FindPropertyRelative("maxHealth").floatValue = 2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    private static void WireBranchAdapter(GameObject visualPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(XM777PrefabPath);
        try
        {
            XM777Trap weapon = root.GetComponent<XM777Trap>();
            if (weapon == null) throw new InvalidOperationException("XM777 prefab has no XM777Trap component.");
            RG777BranchAdapter adapter = root.GetComponent<RG777BranchAdapter>();
            if (adapter == null) adapter = root.AddComponent<RG777BranchAdapter>();
            var serialized = new SerializedObject(adapter);
            serialized.FindProperty("visualPrefab").objectReferenceValue = visualPrefab;
            serialized.FindProperty("weapon").objectReferenceValue = weapon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, XM777PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void FitToFootprint(GameObject root, float targetMeters)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("RG777 model has no renderers.");
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
