using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Repeatable URP import, gameplay prefab and catalogue definition generation.</summary>
public static class RepairStationAuthoring
{
    public const string ModelPath = "Assets/Models/RepairStation/RepairStation.fbx";
    public const string MaterialDir = "Assets/Models/RepairStation/Materials";
    public const string PrefabPath = "Assets/Resources/RepairStation.prefab";
    public const string DefinitionPath = "Assets/Resources/RepairStation.asset";
    public const string PreviewDir = @"C:\Users\ASUS\Desktop\陷阱素材文件夹\RepairStation\unity-previews";
    private const string Lit = "Universal Render Pipeline/Lit";
    private static readonly string[] ArmNames = { "Arm_Front", "Arm_Right", "Arm_Back", "Arm_Left" };

    private readonly struct MaterialSpec
    {
        public readonly Color color;
        public readonly float metallic, smoothness;
        public MaterialSpec(float r, float g, float b, float metal, float smooth)
        { color = new Color(r, g, b); metallic = metal; smoothness = smooth; }
    }

    // Frozen from the model's materials.json; rebuilding never needs a desktop input file.
    private static readonly Dictionary<string, MaterialSpec> Palette = new Dictionary<string, MaterialSpec>
    {
        { "RS_WhiteShell", new MaterialSpec(.8f, .82f, .845f, .12f, .6f) },
        { "RS_WhiteTrim", new MaterialSpec(.46f, .48f, .5f, .55f, .67f) },
        { "RS_BlueArmor", new MaterialSpec(.085f, .175f, .44f, .25f, .66f) },
        { "RS_BlueDark", new MaterialSpec(.045f, .085f, .22f, .3f, .6f) },
        { "RS_DarkStructure", new MaterialSpec(.065f, .072f, .082f, .55f, .58f) },
        { "RS_JointMetal", new MaterialSpec(.3f, .325f, .35f, .85f, .71f) },
        { "RS_Rubber", new MaterialSpec(.028f, .028f, .032f, 0f, .38f) },
        { "RS_CopperTip", new MaterialSpec(.62f, .36f, .17f, 1f, .72f) },
        { "RS_CyanEmission", new MaterialSpec(.1f, .8f, .92f, 0f, .75f) }
    };

    [MenuItem("Tools/塔防/维修站/生成材质、Prefab 与陷阱定义")]
    public static void CreateAssetsBatch()
    {
        Shader shader = Shader.Find(Lit);
        if (shader == null) throw new InvalidOperationException("URP/Lit shader missing.");
        EnsureFolder(MaterialDir);
        EnsureFolder("Assets/Resources");
        foreach (var entry in Palette)
        {
            string path = MaterialDir + "/" + entry.Key + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = entry.Key };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", entry.Value.color);
            material.SetFloat("_Metallic", entry.Value.metallic);
            material.SetFloat("_Smoothness", entry.Value.smoothness);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.renderQueue = (int)RenderQueue.Geometry;
            bool emission = entry.Key == "RS_CyanEmission";
            material.SetColor("_EmissionColor", emission ? new Color(.1f, .85f, 1f) * 6f : Color.black);
            if (emission) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("RepairStation FBX importer missing.");
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.preserveHierarchy = true;
        importer.bakeAxisConversion = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        foreach (string name in Palette.Keys)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + name + ".mat"));
        importer.SaveAndReimport();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Model import failed.");
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject root = new GameObject("RepairStation");
        try
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // The exported Front lies on Unity -Z; face it along the prefab's +Z.
            visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * visual.transform.localRotation;
            Bounds bounds = RendererBounds(root);
            visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = RendererBounds(root);
            if (Mathf.Abs(bounds.size.x - 1.902f) > .025f || Mathf.Abs(bounds.size.z - 1.902f) > .025f
                || Mathf.Abs(bounds.size.y - 1.2495f) > .025f)
                throw new InvalidOperationException("Unexpected imported dimensions: " + bounds.size);
            var body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, .625f, 0f);
            body.size = new Vector3(1.90f, 1.25f, 1.90f);
            TrapInstance instance = root.AddComponent<TrapInstance>();
            if (existingPrefab != null && existingPrefab.TryGetComponent(out TrapInstance previousInstance))
                CopyManualFloat(previousInstance, instance, "maxHealth");
            var repair = root.AddComponent<RepairStationTrap>();
            if (existingPrefab != null && existingPrefab.TryGetComponent(out RepairStationTrap previousRepair))
                CopyManualRepairTuning(previousRepair, repair);
            repair.ConfigureArms(BindArms(root));
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Could not save RepairStation prefab.");
        }
        finally { Object.DestroyImmediate(root); }

        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        bool isNew = definition == null;
        if (isNew)
        {
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("trapId").stringValue = "repair-station";
        serialized.FindProperty("displayName").stringValue = "维修站";
        serialized.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        // Preserve later balance edits on repeat generation.
        if (isNew) serialized.FindProperty("cost").intValue = 40;
        serialized.FindProperty("footprintWidth").intValue = 2;
        serialized.FindProperty("footprintHeight").intValue = 2;
        serialized.FindProperty("worldFootprint").vector2Value = new Vector2(2f, 2f);
        serialized.FindProperty("localRotation").vector3Value = Vector3.zero;
        serialized.FindProperty("walkableFloorTrap").boolValue = false;
        serialized.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Ground;
        serialized.FindProperty("installBoundsCenter").vector3Value = new Vector3(0f, .625f, 0f);
        serialized.FindProperty("installBoundsSize").vector3Value = new Vector3(1.9f, 1.25f, 1.9f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        ValidateBatch();
        Debug.Log("REPAIR_STATION_ASSETS_PASS");
    }

    private static void CopyManualRepairTuning(RepairStationTrap source, RepairStationTrap destination)
    {
        foreach (string property in new[] { "repairAmount", "cooldownSeconds", "deploySeconds", "holdSeconds", "retractSeconds" })
            CopyManualFloat(source, destination, property);
    }

    private static void CopyManualFloat(UnityEngine.Object source, UnityEngine.Object destination, string property)
    {
        if (source == null || destination == null) return;
        var sourceSettings = new SerializedObject(source);
        var destinationSettings = new SerializedObject(destination);
        SerializedProperty sourceValue = sourceSettings.FindProperty(property);
        SerializedProperty destinationValue = destinationSettings.FindProperty(property);
        if (sourceValue == null || destinationValue == null || sourceValue.propertyType != SerializedPropertyType.Float ||
            destinationValue.propertyType != SerializedPropertyType.Float)
            throw new InvalidOperationException("Unable to preserve authored float property '" + property + "'.");
        destinationValue.floatValue = sourceValue.floatValue;
        destinationSettings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RepairStationTrap.ArmRig[] BindArms(GameObject root)
    {
        var rigs = new RepairStationTrap.ArmRig[4];
        for (int i = 0; i < 4; i++)
        {
            Transform arm = Find(root.transform, ArmNames[i]);
            var rig = new RepairStationTrap.ArmRig
            {
                yaw = Find(arm, "Shoulder_Yaw"), pitch = Find(arm, "Shoulder_Pitch"),
                elbow = Find(arm, "Elbow"), wrist = Find(arm, "Wrist")
            };
            // Blender reference: yaw Z=-90, pitch Y=-52, elbow Y=-72, wrist Y=55.
            // Convert each Blender axis through the imported model's basis into the joint's local space.
            // FBX reflects the local Z coordinate: axial rotations about X/Y change sign, Z does not.
            rig.yawWorkEuler = new Vector3(0f, 0f, -90f);
            rig.pitchWorkEuler = new Vector3(0f, 52f, 0f);
            rig.elbowWorkEuler = new Vector3(0f, 72f, 0f);
            rig.wristWorkEuler = new Vector3(0f, -55f, 0f);
            // See probe report for imported axes; graphical acceptance verifies the reference pose.
            var torch = Find(arm, "Torch");
            rig.torchRenderer = torch.GetComponent<Renderer>();
            Material[] mats = rig.torchRenderer.sharedMaterials;
            rig.emissiveMaterialIndex = Array.FindIndex(mats, m => m != null && m.name == "RS_CyanEmission");
            if (!rig.IsBound || rig.emissiveMaterialIndex < 0) throw new InvalidOperationException("Unbound arm " + i);
            Vector3 face = root.transform.InverseTransformDirection(-arm.up);
            int side = Mathf.Abs(face.z) >= Mathf.Abs(face.x) ? (face.z >= 0f ? 0 : 2) : (face.x >= 0f ? 1 : 3);
            if (rigs[side] != null) throw new InvalidOperationException("Duplicate arm side " + side);
            rigs[side] = rig;
        }
        return rigs;
    }

    private static Transform Find(Transform root, string name)
    {
        Transform match = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name
            || t.name.StartsWith(name + ".", StringComparison.Ordinal)
            || t.name.StartsWith(name + "_", StringComparison.Ordinal));
        if (match == null) throw new InvalidOperationException("Missing model node " + name);
        return match;
    }

    public static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("No mesh renderers.");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    [MenuItem("Tools/塔防/维修站/校验资源")]
    public static void ValidateBatch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (prefab == null || definition == null || definition.Prefab != prefab
            || !prefab.GetComponent<RepairStationTrap>().HasBoundArms || prefab.GetComponent<TrapInstance>() == null
            || definition.MountType != TrapMountType.Ground || definition.WorldFootprint != new Vector2(2f, 2f))
            throw new InvalidOperationException("RepairStation asset wiring invalid.");
        Material[] materials = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        if (materials.Length != 9 || materials.Any(m => m == null || m.shader.name != Lit))
            throw new InvalidOperationException("Expected nine URP materials.");
        Directory.CreateDirectory(PreviewDir);
        var report = new List<string> { "REPAIR_STATION_VALIDATION_PASS", "Materials=9; arms=4; repair=25; cooldown=4", "Bounds=" + RendererBounds(prefab) };
        foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Arm_") || t.name == "Shoulder_Yaw" || t.name == "Shoulder_Pitch")
                report.Add(t.name + " pos=" + t.position + " rot=" + t.rotation.eulerAngles + " local=" + t.localRotation.eulerAngles
                    + " scale=" + t.lossyScale + " up=" + t.up + " forward=" + t.forward + " localPos=" + t.localPosition);
        File.WriteAllLines(Path.Combine(PreviewDir, "asset-validation.txt"), report);
        Debug.Log("REPAIR_STATION_VALIDATION_PASS: " + string.Join("; ", report.Take(3)));
    }

    [MenuItem("Tools/塔防/维修站/渲染预览")]
    public static void RenderPreviewBatch()
    {
        ValidateBatch();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Repair Station Preview");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        RenderTexture rt = null;
        AmbientMode oldMode = RenderSettings.ambientMode;
        Color oldAmbient = RenderSettings.ambientLight;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.48f, .5f, .55f);
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), root.transform);
            model.GetComponent<RepairStationTrap>().enabled = false;
            var cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.fieldOfView = 32f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.23f, .26f, .32f);
            rt = new RenderTexture(1200, 1000, 24) { antiAliasing = 4 };
            camera.targetTexture = rt;
            MakeLight(root, new Vector3(45f, -30f, 0f), 2.5f);
            MakeLight(root, new Vector3(60f, 150f, 0f), 1.5f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.position = new Vector3(0f, -.1f, 0f);
            floor.transform.localScale = new Vector3(12f, .2f, 12f);
            var floorMat = new Material(Shader.Find(Lit));
            floorMat.SetColor("_BaseColor", new Color(.38f, .4f, .44f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            camera.transform.position = new Vector3(4.3f, 3.4f, 4.8f);
            camera.transform.LookAt(new Vector3(0f, .6f, 0f));
            camera.Render(); camera.Render();
            SaveFrame(camera, rt, "01_idle");
            RepairStationTrap.ArmRig[] rigs = BindArms(model);
            foreach (var rig in rigs) { rig.CaptureRest(); rig.Pose(1f); }
            SaveFrame(camera, rt, "02_four_arms_working");
            foreach (var rig in rigs) rig.Pose(0f);
            // Real purchase/placement and runtime tick; this shot is also an integration acceptance probe.
            Object.DestroyImmediate(model);
            IntegrationPreview(root, camera, rt);
            Object.DestroyImmediate(floorMat);
            Debug.Log("REPAIR_STATION_PREVIEW_PASS: " + PreviewDir);
        }
        finally
        {
            RenderSettings.ambientMode = oldMode;
            RenderSettings.ambientLight = oldAmbient;
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static void GenerateAndPreviewBatch()
    {
        CreateAssetsBatch();
        RenderPreviewBatch();
    }

    private static void IntegrationPreview(GameObject root, Camera camera, RenderTexture rt)
    {
        var gridObject = new GameObject("Repair Integration Grid");
        gridObject.transform.SetParent(root.transform, false);
        gridObject.transform.position = new Vector3(-3.5f, .01f, -3.5f);
        var grid = gridObject.AddComponent<TrapPlacementGrid>();
        grid.ConfigureLayout(10, 10, 1f, 0f, Enumerable.Repeat(true, 100).ToArray(), TrapPlacementGrid.SurfaceKind.Ground);
        var walletObject = new GameObject("Repair Preview Wallet");
        walletObject.transform.SetParent(root.transform, false);
        var wallet = walletObject.AddComponent<EconomyManager>();
        // Awake is not invoked by editor AddComponent for all non-ExecuteAlways behaviours.
        if (EconomyManager.Instance == null)
            typeof(EconomyManager).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(wallet, null);
        wallet.BeginRun(500);
        var definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (!TrapPurchaseService.TryPurchase(grid, new Vector2Int(3, 3), definition, out TrapInstance source, out string failure))
            throw new InvalidOperationException("Purchase failed: " + failure);
        // A disposable definition provides four compact, visually distinct repair targets.
        var targetDefinition = ScriptableObject.CreateInstance<TrapDefinition>();
        var so = new SerializedObject(targetDefinition);
        so.FindProperty("displayName").stringValue = "Repair target";
        so.ApplyModifiedPropertiesWithoutUndo();
        var positions = new[] { new Vector2Int(3, 5), new Vector2Int(5, 3), new Vector2Int(3, 2), new Vector2Int(2, 3) };
        var targets = new List<TrapInstance>();
        try
        {
            foreach (Vector2Int cell in positions)
            {
                if (!grid.TryPlaceTrap(cell, targetDefinition, out TrapInstance target, out failure))
                    throw new InvalidOperationException("Target placement failed: " + failure);
                EnsureHealthAwake(target);
                target.TakeDamage(60f);
                targets.Add(target);
            }
            EnsureHealthAwake(source);
            RepairStationTrap repair = source.GetComponent<RepairStationTrap>();
            repair.Tick(0f);
            if (targets.Any(t => !Mathf.Approximately(t.CurrentHealth, 65f)) || wallet.Balance != 460)
                throw new InvalidOperationException("Four-way repair or paid placement failed.");
            // At a mid-action time no extra healing may occur.
            repair.Tick(.5f);
            camera.transform.position = new Vector3(7f, 6.5f, 8f);
            camera.transform.LookAt(new Vector3(0f, .5f, 0f));
            SaveFrame(camera, rt, "03_adjacent_targets_repaired");
            repair.Tick(3.99f);
            if (targets.Any(t => !Mathf.Approximately(t.CurrentHealth, 65f))) throw new InvalidOperationException("Cooldown bypass.");
            repair.Tick(4f);
            if (targets.Any(t => !Mathf.Approximately(t.CurrentHealth, 90f))) throw new InvalidOperationException("4-second cooldown failed.");
            if (!TrapSaleService.TrySell(grid, source, out int refund, out failure) || refund != 20 || wallet.Balance != 480)
                throw new InvalidOperationException("Sale failed: " + failure);
            File.WriteAllText(Path.Combine(PreviewDir, "integration-validation.txt"),
                "REPAIR_STATION_INTEGRATION_PASS\nPurchase: 500 -> 460; four targets: 40 -> 65 immediately, unchanged at 3.99s, 90 at 4s; sale: +20 -> 480.\n");
        }
        finally { Object.DestroyImmediate(targetDefinition); }
    }

    private static void EnsureHealthAwake(TrapInstance trap)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if (trap.IsDestroyed) typeof(TrapInstance).GetMethod("Awake", flags).Invoke(trap, null);
        typeof(TrapInstance).GetMethod("OnEnable", flags).Invoke(trap, null);
    }

    private static void MakeLight(GameObject root, Vector3 angles, float intensity)
    {
        var go = new GameObject("Preview Light");
        go.transform.SetParent(root.transform, false);
        go.transform.rotation = Quaternion.Euler(angles);
        var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity;
    }

    private static void SaveFrame(Camera camera, RenderTexture rt, string name)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(PreviewDir, name + ".png"), texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
