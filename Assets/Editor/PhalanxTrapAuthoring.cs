using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Phalanx CIWS (密集阵) trap prefab, materials and definition from
/// the finished FBX. The prefab is authored at the gameplay scale (2 m × 2 m
/// grid footprint) and wires the yaw -> pitch -> barrel -> muzzle rig that
/// PhalanxTurretRig drives.
///
/// Gameplay (targeting, burst timing, damage, upgrade branch, menu icon) is a
/// separate programmer task; see Assets/Editor/PHALANX_HANDOFF.md.
/// </summary>
public static class PhalanxTrapAuthoring
{
    public const string ModelPath = "Assets/Models/Phalanx/Phalanx.fbx";
    public const string PrefabPath = "Assets/Prefabs/Phalanx.prefab";
    public const string VisualPrefabPath = "Assets/Resources/PhalanxVisual.prefab";
    public const string DefinitionPath = "Assets/Resources/Phalanx.asset";
    public const string MaterialFolder = "Assets/Models/Phalanx/Materials";
    public const string TrapId = "phalanx";

    /// <summary>Grid occupancy stays at 2 m × 2 m for this trap.</summary>
    public const float FootprintMeters = 2f;

    private const string YawName = "PHX_Yaw_Root";
    private const string PitchName = "PHX_Pitch_Root";
    private const string BarrelName = "PHX_Barrel_Root";
    private const string MuzzleName = "PHX_Muzzle";
    private const string BaseName = "PHX_Base_Root";

    private struct PartStyle
    {
        public string MaterialName;
        public string BaseColor;
        public float Metallic;
        public float Smoothness;

        public PartStyle(string materialName, string baseColor, float metallic, float smoothness)
        {
            MaterialName = materialName;
            BaseColor = baseColor;
            Metallic = metallic;
            Smoothness = smoothness;
        }
    }

    // Palette mirrors phalanx_build.py. Metalness is kept moderate because the
    // map has no reflection probes; high metalness turns the body black.
    private static readonly Dictionary<string, PartStyle> Styles = new Dictionary<string, PartStyle>
    {
        { "Armor", new PartStyle("M_PHX_Armor", "#4F5947", 0.15f, 0.38f) },
        { "ArmorDark", new PartStyle("M_PHX_ArmorDark", "#363E31", 0.20f, 0.34f) },
        { "Metal", new PartStyle("M_PHX_Metal", "#565C5E", 0.50f, 0.55f) },
        { "MetalDark", new PartStyle("M_PHX_MetalDark", "#2F3336", 0.45f, 0.50f) },
        { "Barrel", new PartStyle("M_PHX_Barrel", "#3B4044", 0.55f, 0.62f) },
        { "Radome", new PartStyle("M_PHX_Radome", "#D9DAD3", 0.05f, 0.45f) },
        { "RadarPanel", new PartStyle("M_PHX_RadarPanel", "#9AA0A0", 0.25f, 0.50f) },
        { "Warning", new PartStyle("M_PHX_Warning", "#C8802A", 0.20f, 0.45f) },
        { "Glass", new PartStyle("M_PHX_Glass", "#1B2A2E", 0.10f, 0.80f) },
    };

    [MenuItem("Tools/塔防/生成密集阵近防炮陷阱")]
    public static void CreateAssets()
    {
        AssetDatabase.Refresh();
        ConfigureModelImporter();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"找不到密集阵模型：{ModelPath}");
            return;
        }

        Directory.CreateDirectory("Assets/Prefabs");
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory(MaterialFolder);

        Dictionary<string, Material> materials = EnsureMaterials();
        TrapDefinition definition = LoadOrCreateDefinition();

        GameObject root = UnityEngine.Object.Instantiate(model);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Transform baseRoot = FindDeepChild(root.transform, BaseName);
        if (baseRoot == null)
        {
            UnityEngine.Object.DestroyImmediate(root);
            Debug.LogError($"密集阵模型层级不完整：找不到 {BaseName}。");
            return;
        }

        // Promote the authored base root to be the prefab root; the generated
        // FBX wrapper only exists because of the import and adds no value.
        baseRoot.SetParent(null, true);
        UnityEngine.Object.DestroyImmediate(root);

        // The imported model node keeps the FBX axis rotation. It becomes a
        // child of a clean gameplay root so placement can own the root
        // transform without tipping the model over.
        GameObject prefabRoot = new GameObject("Phalanx");
        prefabRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        baseRoot.SetParent(prefabRoot.transform, true);

        Transform yaw = FindDeepChild(baseRoot, YawName);
        Transform pitch = FindDeepChild(baseRoot, PitchName);
        Transform barrel = FindDeepChild(baseRoot, BarrelName);
        Transform muzzle = FindDeepChild(baseRoot, MuzzleName);
        if (yaw == null || pitch == null || barrel == null || muzzle == null)
        {
            UnityEngine.Object.DestroyImmediate(prefabRoot);
            Debug.LogError("密集阵模型层级不完整：需要 Yaw/Pitch/Barrel/Muzzle 四个枢轴。");
            return;
        }

        AlignModelToGameplayAxes(baseRoot,
            FindDeepChild(baseRoot, "PHX_Base_Plate"), yaw, pitch, muzzle);
        AssignMaterials(prefabRoot, materials);
        EnsureGroundContact(baseRoot);

        TrapInstance trapInstance = prefabRoot.GetComponent<TrapInstance>();
        if (trapInstance == null) trapInstance = prefabRoot.AddComponent<TrapInstance>();
        SetObjectReference(trapInstance, "definition", definition);

        PhalanxTurretRig rig = prefabRoot.GetComponent<PhalanxTurretRig>();
        if (rig == null) rig = prefabRoot.AddComponent<PhalanxTurretRig>();
        var rigSerialized = new SerializedObject(rig);
        rigSerialized.FindProperty("yawPivot").objectReferenceValue = yaw;
        rigSerialized.FindProperty("pitchPivot").objectReferenceValue = pitch;
        rigSerialized.FindProperty("barrelCluster").objectReferenceValue = barrel;
        rigSerialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        rigSerialized.ApplyModifiedPropertiesWithoutUndo();

        // Firing feedback: muzzle flash + tracers live on PhalanxFireFx, the brass
        // casings on SentryShellEjector (shared with the sentry machine gun). Both are
        // driven by PhalanxAutoCannon.ShotFired / ShotTraced, which the branch adapter
        // adds to the sentry root at runtime, so nothing is wired to a weapon here.
        PhalanxFireFx fireFx = prefabRoot.GetComponent<PhalanxFireFx>();
        if (fireFx == null) fireFx = prefabRoot.AddComponent<PhalanxFireFx>();
        var fxSerialized = new SerializedObject(fireFx);
        fxSerialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        fxSerialized.ApplyModifiedPropertiesWithoutUndo();

        SentryShellEjector ejector = prefabRoot.GetComponent<SentryShellEjector>();
        if (ejector == null) ejector = prefabRoot.AddComponent<SentryShellEjector>();
        var ejectorSerialized = new SerializedObject(ejector);
        ejectorSerialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        // 50 rounds/second with a 1.2 s casing lifetime needs ~60 live casings.
        ejectorSerialized.FindProperty("poolSize").intValue = 96;
        ejectorSerialized.FindProperty("lifetime").floatValue = 1.2f;
        ejectorSerialized.FindProperty("localVelocity").vector3Value = new Vector3(1.7f, 2.1f, -0.35f);
        ejectorSerialized.ApplyModifiedPropertiesWithoutUndo();

        Bounds bounds = MeasureVertexBounds(prefabRoot.transform);
        Debug.Log($"[Phalanx] 预制体尺寸 {bounds.size.x:F2} × {bounds.size.y:F2} × {bounds.size.z:F2} m"
            + $"（占用格 2×2 m；炮管前伸按设计保留）。");

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        TrapInstance visualTrap = prefabRoot.GetComponent<TrapInstance>();
        if (visualTrap != null) UnityEngine.Object.DestroyImmediate(visualTrap);
        GameObject visualPrefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, VisualPrefabPath);
        UnityEngine.Object.DestroyImmediate(prefabRoot);
        if (prefab == null)
        {
            Debug.LogError($"生成密集阵预制体失败：{PrefabPath}");
            return;
        }
        if (visualPrefab == null)
        {
            Debug.LogError($"Failed to create Phalanx visual prefab: {VisualPrefabPath}");
            return;
        }

        var definitionSerialized = new SerializedObject(definition);
        definitionSerialized.FindProperty("prefab").objectReferenceValue = prefab;
        definitionSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"PHALANX_CREATED prefab={PrefabPath} definition={DefinitionPath} "
            + $"size={bounds.size.x:F2}x{bounds.size.y:F2}x{bounds.size.z:F2}m");
    }

    public static void CreateAssetsBatch()
    {
        CreateAssets();
    }

    /// <summary>Read-only probe: prints the raw FBX import so axis handling is fact-based.</summary>
    public static void ProbeImportBatch()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException($"Missing model: {ModelPath}");
        GameObject instance = UnityEngine.Object.Instantiate(model);
        Transform baseRoot = FindDeepChild(instance.transform, BaseName);
        Debug.Log($"PHX_IMPORT rootFound={baseRoot != null} prefabRoot={instance.transform.name} "
            + $"rot={instance.transform.localEulerAngles} scale={instance.transform.localScale}");
        if (baseRoot != null)
        {
            Debug.Log($"PHX_IMPORT {BaseName} localPos={baseRoot.localPosition} "
                + $"localRot={baseRoot.localEulerAngles} localScale={baseRoot.localScale} "
                + $"worldPos={baseRoot.position}");
            foreach (string node in new[] { "PHX_Base_Plate", "PHX_Base_Deck", "PHX_Yaw_Root",
                "PHX_Pitch_Root", "PHX_Barrel_Root", "PHX_Muzzle", "PHX_Radar_Frame" })
            {
                Transform t = FindDeepChild(baseRoot, node);
                Debug.Log(t != null
                    ? $"PHX_IMPORT {node} worldPos={t.position} localRot={t.localEulerAngles} lossyScale={t.lossyScale}"
                    : $"PHX_IMPORT {node} MISSING");
            }
            Bounds raw = MeasureVertexBounds(baseRoot);
            Debug.Log($"PHX_IMPORT bounds center={raw.center} size={raw.size} min={raw.min} max={raw.max}");
        }
        UnityEngine.Object.DestroyImmediate(instance);
    }

    /// <summary>Read-only probe: verifies the yaw/pitch/barrel grouping and aim math.</summary>
    public static void ProbeRigBatch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException($"Missing prefab: {PrefabPath}");
        GameObject root = UnityEngine.Object.Instantiate(prefab);
        try
        {
            PhalanxTurretRig rig = root.GetComponent<PhalanxTurretRig>();
            if (rig == null) throw new InvalidOperationException("Missing PhalanxTurretRig");
            foreach (string part in new[] { "PHX_Turret_Shell", "PHX_Radar_Frame", "PHX_Ammo_Box",
                "PHX_Gun_Cradle", "PHX_Barrel_1", "PHX_Radome_Dome", "PHX_Base_Plate" })
            {
                Transform t = FindDeepChild(root.transform, part);
                Debug.Log(t == null
                    ? $"PHX_RIG {part} MISSING"
                    : $"PHX_RIG {part} parent={t.parent.name} underYaw={t.IsChildOf(rig.YawPivot)} "
                        + $"underPitch={t.IsChildOf(rig.PitchPivot)} underBarrel={t.IsChildOf(rig.BarrelCluster)}");
            }

            rig.ResetToRestPose();
            Vector3 target = new Vector3(5.5f, 6.0f, 8.0f);
            for (int i = 0; i < 60; i++) rig.AimAt(target, 1f / 30f);
            Vector3 toTarget = (target - rig.PitchPivot.position).normalized;
            Debug.Log($"PHX_RIG aim yaw={rig.YawDegrees:F2} pitch={rig.PitchDegrees:F2} "
                + $"barrelDir={rig.BarrelDirection:F3} toTarget={toTarget:F3} "
                + $"angle={Vector3.Angle(rig.BarrelDirection, toTarget):F2} "
                + $"aimed={rig.IsAimedAt(target, 2f)} "
                + $"muzzle={rig.MuzzlePosition:F3}");

            rig.ResetToRestPose();
            rig.SetBarrelSpinActive(true);
            Vector3 before = rig.BarrelDirection;
            for (int i = 0; i < 30; i++) rig.AdvanceVisuals(1f / 30f);
            Debug.Log($"PHX_RIG spin before={before:F3} after={rig.BarrelDirection:F3} "
                + $"angleChange={Vector3.Angle(before, rig.BarrelDirection):F2} "
                + $"rate={rig.CurrentSpinRate:F0} barrelRootLocalRot={rig.BarrelCluster.localEulerAngles}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>Read-only probe: prints the widest parts so footprint trimming is data-driven.</summary>
    public static void ProbeBoundsBatch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException($"Missing prefab: {PrefabPath}");
        Debug.Log($"PHX_ROOT pos={prefab.transform.position} rot={prefab.transform.localEulerAngles} scale={prefab.transform.localScale}");
        var renderers = new List<Renderer>(prefab.GetComponentsInChildren<Renderer>(true));
        renderers.Sort((a, b) => b.bounds.size.x.CompareTo(a.bounds.size.x));
        for (int i = 0; i < Mathf.Min(10, renderers.Count); i++)
        {
            Renderer r = renderers[i];
            MeshFilter filter = r.GetComponent<MeshFilter>();
            string meshInfo = filter != null && filter.sharedMesh != null
                ? $"meshBounds={filter.sharedMesh.bounds.size} verts={filter.sharedMesh.vertexCount}"
                : "noMesh";
            Debug.Log($"PHX_WIDE {i}: {r.name} size={r.bounds.size} center={r.bounds.center} "
                + $"{meshInfo} matrixScale={r.transform.localToWorldMatrix.lossyScale}");
        }
        renderers.Sort((a, b) => b.bounds.size.z.CompareTo(a.bounds.size.z));
        for (int i = 0; i < Mathf.Min(6, renderers.Count); i++)
        {
            Renderer r = renderers[i];
            Debug.Log($"PHX_DEEP {i}: {r.name} size={r.bounds.size} center={r.bounds.center}");
        }
        renderers.Sort((a, b) => a.bounds.min.y.CompareTo(b.bounds.min.y));
        for (int i = 0; i < Mathf.Min(6, renderers.Count); i++)
        {
            Renderer r = renderers[i];
            Debug.Log($"PHX_LOW {i}: {r.name} minY={r.bounds.min.y:F3} center={r.bounds.center}");
        }
        renderers.Sort((a, b) => b.bounds.max.y.CompareTo(a.bounds.max.y));
        for (int i = 0; i < Mathf.Min(4, renderers.Count); i++)
        {
            Renderer r = renderers[i];
            Debug.Log($"PHX_HIGH {i}: {r.name} maxY={r.bounds.max.y:F3} center={r.bounds.center}");
        }
    }

    /// <summary>Read-only probe used before handing the asset to gameplay code.</summary>
    public static void ValidateBatch()
    {
        var problems = new List<string>();
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (definition == null)
        {
            throw new InvalidOperationException($"Missing trap definition: {DefinitionPath}");
        }
        if (definition.TrapId != TrapId) problems.Add($"trapId={definition.TrapId}, expected {TrapId}");
        if (definition.Footprint != new Vector2Int(2, 2))
            problems.Add($"grid footprint={definition.Footprint}, expected 2x2");
        if (definition.WorldFootprint != new Vector2(FootprintMeters, FootprintMeters))
            problems.Add($"world footprint={definition.WorldFootprint}, expected {FootprintMeters}x{FootprintMeters}");
        if (definition.MountType != TrapMountType.Ground) problems.Add("mount type must be Ground");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException($"Missing prefab: {PrefabPath}");
        if (definition.Prefab != prefab) problems.Add("definition.prefab does not point at the generated prefab");

        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        if (visualPrefab == null) problems.Add($"missing visual prefab: {VisualPrefabPath}");
        else
        {
            if (visualPrefab.GetComponent<TrapInstance>() != null)
                problems.Add("visual prefab must not contain TrapInstance");
            if (visualPrefab.GetComponent<PhalanxTurretRig>() == null)
                problems.Add("visual prefab is missing PhalanxTurretRig");
        }

        if (prefab.GetComponentsInChildren<Camera>(true).Length != 0
            || prefab.GetComponentsInChildren<Light>(true).Length != 0
            || prefab.GetComponentsInChildren<AudioListener>(true).Length != 0)
            problems.Add("prefab contains staging camera/light/audio listener");

        PhalanxTurretRig rig = prefab.GetComponent<PhalanxTurretRig>();
        if (rig == null) problems.Add("missing PhalanxTurretRig");
        else
        {
            if (rig.YawPivot == null || rig.PitchPivot == null || rig.BarrelCluster == null || rig.Muzzle == null)
                problems.Add("rig reference missing");
            else if (!rig.PitchPivot.IsChildOf(rig.YawPivot)
                || !rig.BarrelCluster.IsChildOf(rig.PitchPivot)
                || !rig.Muzzle.IsChildOf(rig.BarrelCluster))
                problems.Add("rig hierarchy is not yaw -> pitch -> barrel -> muzzle");
            else
            {
                Vector3 barrel = (rig.Muzzle.position - rig.BarrelCluster.position).normalized;
                if (Vector3.Dot(barrel, Vector3.forward) < 0.9f)
                    problems.Add($"muzzle does not face +Z (dot={Vector3.Dot(barrel, Vector3.forward):F2})");
                Vector3 yawOffset = rig.YawPivot.position - prefab.transform.position;
                yawOffset.y = 0f;
                if (yawOffset.magnitude > 0.05f)
                    problems.Add($"yaw pivot is off the base centre ({yawOffset})");
            }
        }

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) problems.Add("no renderers in prefab");
        int generatedMaterialSlots = 0;
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    problems.Add($"{renderer.name} has a null material slot");
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(material);
                if (!path.StartsWith(MaterialFolder, StringComparison.Ordinal))
                {
                    problems.Add($"{renderer.name} still uses FBX material {material.name} ({path})");
                    continue;
                }
                if (material.shader == null || material.shader.name != "Universal Render Pipeline/Lit")
                {
                    problems.Add($"{material.name} is not URP/Lit");
                    continue;
                }
                generatedMaterialSlots++;
            }
        }

        Bounds bounds = MeasureVertexBounds(prefab.transform);
        if (bounds.size.y < 1.5f) problems.Add($"model looks too short ({bounds.size.y:F2} m)");
        if (Mathf.Abs(bounds.min.y - prefab.transform.position.y) > 0.03f)
            problems.Add($"model is not standing on Y=0 (min.y={bounds.min.y:F3})");
        if (bounds.size.x > FootprintMeters + 0.05f)
            problems.Add($"model is wider than the {FootprintMeters} m footprint ({bounds.size.x:F2} m)");
        // The gun barrels intentionally overhang the plate like the design sheet;
        // the placement cells stay 2x2 while the muzzles may stick out forward.
        const float barrelOverhangBudget = 0.40f;
        if (bounds.size.z > FootprintMeters + barrelOverhangBudget)
            problems.Add($"model is deeper than footprint + barrel overhang ({bounds.size.z:F2} m)");

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("PHALANX_FAIL:\n- " + string.Join("\n- ", problems));
        }

        Debug.Log($"PHALANX_PASS rig/materials/footprint OK; renderers={renderers.Length}, "
            + $"materialSlots={generatedMaterialSlots}, "
            + $"size={bounds.size.x:F2}x{bounds.size.y:F2}x{bounds.size.z:F2}m, "
            + $"footprint={definition.Footprint}, world={definition.WorldFootprint}");
    }

    private static void ConfigureModelImporter()
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer))
        {
            throw new InvalidOperationException($"Model not imported yet: {ModelPath}");
        }
        bool dirty = false;
        if (importer.importCameras) { importer.importCameras = false; dirty = true; }
        if (importer.importLights) { importer.importLights = false; dirty = true; }
        if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
        if (importer.importBlendShapes) { importer.importBlendShapes = false; dirty = true; }
        if (dirty) importer.SaveAndReimport();
    }

    /// <summary>
    /// Read-only probe for the firing feedback: drives one round through
    /// PhalanxFireFx + SentryShellEjector on the shipped visual prefab and prints
    /// the observed flash / tracer / casing state.
    /// </summary>
    public static void ProbeFireFxBatch()
    {
        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        if (visualPrefab == null) throw new InvalidOperationException("Missing visual prefab: " + VisualPrefabPath);

        var problems = new System.Collections.Generic.List<string>();
        PhalanxFireFx fx = visualPrefab.GetComponent<PhalanxFireFx>();
        SentryShellEjector ejector = visualPrefab.GetComponent<SentryShellEjector>();
        if (fx == null) problems.Add("visual prefab is missing PhalanxFireFx");
        if (ejector == null) problems.Add("visual prefab is missing SentryShellEjector");
        if (fx != null && fx.Muzzle == null) problems.Add("PhalanxFireFx.muzzle is not wired");

        if (fx == null || ejector == null)
        {
            foreach (string problem in problems) Debug.LogError("PHALANX_FX_FAIL " + problem);
            throw new InvalidOperationException("PHALANX_FX_FAIL");
        }

        GameObject instance = UnityEngine.Object.Instantiate(visualPrefab);
        try
        {
            PhalanxFireFx liveFx = instance.GetComponent<PhalanxFireFx>();
            SentryShellEjector liveEjector = instance.GetComponent<SentryShellEjector>();
            liveFx.EnsureInitialized();

            Transform muzzle = liveFx.Muzzle;
            Vector3 origin = muzzle != null ? muzzle.position : instance.transform.position;
            Vector3 aim = origin + instance.transform.forward * 12f;

            liveFx.PlayShot(origin, aim);
            liveFx.Tick(0.01f);
            bool flashOn = liveFx.IsMuzzleFlashVisible;
            int tracersInFlight = liveFx.ActiveTracerCount;

            liveFx.Tick(1f);
            bool flashOff = !liveFx.IsMuzzleFlashVisible;
            int tracersAfterFlight = liveFx.ActiveTracerCount;

            liveEjector.Eject();
            int casings = liveEjector.ActiveCasingCount;
            liveEjector.Step(0.15f);
            int casingsAfterStep = liveEjector.ActiveCasingCount;
            // A throw that points down recycles the brass almost immediately; this catches
            // the axis-frame mistake the imported model makes so easy.
            liveEjector.Step(0.35f);
            int casingsAfterArc = liveEjector.ActiveCasingCount;

            if (!flashOn) problems.Add("muzzle flash never became visible");
            if (tracersInFlight != 1) problems.Add($"expected 1 tracer in flight, got {tracersInFlight}");
            if (!flashOff) problems.Add("muzzle flash did not time out");
            if (tracersAfterFlight != 0) problems.Add($"tracer did not retire, still {tracersAfterFlight}");
            if (casings != 1) problems.Add($"expected 1 active casing, got {casings}");
            if (casingsAfterStep != 1) problems.Add("casing vanished on the first step");
            if (casingsAfterArc != 1) problems.Add("casing fell out of the world within 0.5 s (throw points down)");

            if (problems.Count == 0)
                Debug.Log($"PHALANX_FX_PASS flash=on->off tracer=1->0 casing=1->{casingsAfterArc} "
                    + $"muzzle={origin:F3} aim={aim:F3}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }

        if (problems.Count > 0)
        {
            foreach (string problem in problems) Debug.LogError("PHALANX_FX_FAIL " + problem);
            throw new InvalidOperationException("PHALANX_FX_FAIL: " + problems.Count);
        }
    }

    private static Dictionary<string, Material> EnsureMaterials()
    {
        var result = new Dictionary<string, Material>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");
        if (lit == null) throw new InvalidOperationException("找不到 URP/Lit 着色器");

        foreach (KeyValuePair<string, PartStyle> pair in Styles)
        {
            PartStyle style = pair.Value;
            string path = $"{MaterialFolder}/{style.MaterialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != lit) material.shader = lit;
            Color color;
            if (!ColorUtility.TryParseHtmlString(style.BaseColor, out color)) color = Color.gray;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", style.Metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", style.Smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            result[pair.Key] = material;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    /// <summary>Resolves each renderer to one generated material by name tokens.</summary>
    private static void AssignMaterials(GameObject root, Dictionary<string, Material> materials)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            string key = ResolveStyleKey(renderer.name);
            Material material = materials[key];
            var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
        }
    }

    private static string ResolveStyleKey(string objectName)
    {
        if (Contains(objectName, "Radome")) return "Radome";
        if (Contains(objectName, "Radar_Face") || Contains(objectName, "Radar_Panel")) return "RadarPanel";
        if (Contains(objectName, "Glass") || Contains(objectName, "Lens")) return "Glass";
        if (Contains(objectName, "Warning") || Contains(objectName, "Band")
            || Contains(objectName, "Label") || Contains(objectName, "Edge")) return "Warning";
        if (Contains(objectName, "Barrel") || Contains(objectName, "Muzzle")) return "Barrel";
        if (Contains(objectName, "MetalDark") || Contains(objectName, "Vent")
            || Contains(objectName, "Trunnion_Cap") || Contains(objectName, "Bolt")
            || Contains(objectName, "Optic")) return "MetalDark";
        if (Contains(objectName, "Metal") || Contains(objectName, "Antenna")
            || Contains(objectName, "Clamp") || Contains(objectName, "Housing")) return "Metal";
        if (Contains(objectName, "ArmorDark") || Contains(objectName, "Panel")
            || Contains(objectName, "Foot") || Contains(objectName, "Roof")) return "ArmorDark";
        return "Armor";
    }

    private static bool Contains(string name, string token)
    {
        return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Normalises the imported FBX to gameplay axes. The up axis is measured
    /// between two co-axial parts (base plate -> yaw pivot); using the radar
    /// panel instead would inject a false tilt because it sits off-centre.
    /// The rotation is applied to the model child, never the prefab root,
    /// because placement owns the root rotation.
    /// </summary>
    private static void AlignModelToGameplayAxes(Transform model, Transform baseAnchor,
        Transform topAnchor, Transform pitch, Transform muzzle)
    {
        if (baseAnchor == null || topAnchor == null || pitch == null || muzzle == null) return;

        Vector3 up = topAnchor.position - baseAnchor.position;
        if (up.sqrMagnitude < 0.0001f) return;
        Quaternion upright = Quaternion.FromToRotation(up.normalized, Vector3.up);
        model.rotation = upright * model.rotation;

        Vector3 forward = muzzle.position - pitch.position;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
        {
            Quaternion yawFix = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
            model.rotation = yawFix * model.rotation;
        }

        Vector3 finalUp = (topAnchor.position - baseAnchor.position).normalized;
        Vector3 finalForward = (muzzle.position - pitch.position).normalized;
        Debug.Log($"[Phalanx] 轴向校验：up·Y={Vector3.Dot(finalUp, Vector3.up):F3} "
            + $"muzzle·Z={Vector3.Dot(finalForward, Vector3.forward):F3} "
            + $"(模型节点局部旋转 {model.localEulerAngles})");
    }

    private static void EnsureGroundContact(Transform model)
    {
        Bounds bounds = MeasureVertexBounds(model);
        if (Mathf.Abs(bounds.min.y) > 0.001f)
        {
            model.position -= new Vector3(0f, bounds.min.y, 0f);
        }
    }

    /// <summary>
    /// Bounds from the real mesh vertices. Renderer.bounds can be inflated by
    /// the bounding box stored in the FBX node, so placement and footing checks
    /// must not rely on it.
    /// </summary>
    private static Bounds MeasureVertexBounds(Transform root)
    {
        bool hasPoint = false;
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertices[i]);
                if (!hasPoint)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    hasPoint = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }
        return bounds;
    }

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
        serialized.FindProperty("displayName").stringValue = "密集阵近防炮";
        // TODO(确认): 价格是占位值；四级分支的报价由升级目录拥有，基础价按平衡另调。
        if (created) serialized.FindProperty("cost").intValue = 55;
        serialized.FindProperty("footprintWidth").intValue = 2;
        serialized.FindProperty("footprintHeight").intValue = 2;
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
