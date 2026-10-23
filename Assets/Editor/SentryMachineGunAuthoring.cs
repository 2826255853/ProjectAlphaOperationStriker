using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Stage three of the sentry machine gun rework: the Unity-side import contract for the
/// Blender FBX exports plus the URP material library those meshes are remapped onto.
///
/// The Blender build script (<c>build_sentry_machine_gun.py</c> on the desktop asset folder)
/// exports a deliberately flat hierarchy: one empty root, 69 static meshes baked into root
/// space, and four marker empties. Stage four rebuilds the runtime hierarchy
/// (Aim Pivot / Gun Recoil / Muzzle / Ejection Port / Laser Emitter) from those markers, so
/// nothing here may invent transforms on top of the imported meshes.
///
/// Everything in this file is idempotent: re-running it only rewrites what actually changed,
/// which keeps the FBX <c>.meta</c> diff readable and makes the generator - not hand edits -
/// the single source of truth for the material palette.
/// </summary>
public static class SentryMachineGunAuthoring
{
    public const string ModelDirectory = "Assets/Models/SentryMachineGun";
    public const string SentryModelPath = ModelDirectory + "/SentryMachineGun.fbx";
    public const string CasingModelPath = ModelDirectory + "/SentryShellCasing.fbx";
    public const string MaterialDirectory = ModelDirectory + "/Materials";

    /// <summary>Material slots produced by the Blender build script, in source order.</summary>
    public static readonly string[] SourceMaterialNames =
    {
        "Sentry_DarkMetal",
        "Sentry_Armor",
        "Sentry_Accent",
        "Sentry_Rubber",
        "Sentry_LaserLens",
        "Sentry_Brass",
        "Sentry_MuzzleFlash",
    };

    /// <summary>Runtime-only material; it has no FBX slot and is wired by the stage-four prefab.</summary>
    public const string LaserBeamMaterialName = "M_Sentry_LaserBeam";

    public static readonly string[] MaterialAssetNames = SourceMaterialNames
        .Select(AssetNameForSlot)
        .Append(LaserBeamMaterialName)
        .ToArray();

    // Geometry contract shared with stage six. Values are the design limits from the spec,
    // not the measured Blender output, so the probe fails loudly if the model drifts.
    public const float MaxHeight = 1.70f;
    public const float MaxFootprint = 2.00f;
    public const float MinFootY = -0.01f;
    public const float CasingLengthTolerance = 0.010f;

    private const string UrpLit = "Universal Render Pipeline/Lit";
    private const string UrpUnlit = "Universal Render Pipeline/Unlit";

    public static string ProbeDirectory =>
        Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "sentry-machine-gun");

    public static string ProbePath => Path.Combine(ProbeDirectory, "probe-model.json");

    public static string AssetNameForSlot(string slotName) => "M_" + slotName;

    public static string MaterialPath(string assetName) => MaterialDirectory + "/" + assetName + ".mat";

    // ------------------------------------------------------------------ entries

    [MenuItem("Tools/塔防/哨戒机枪/配置导入并生成 URP 材质")]
    public static void ConfigureImporter()
    {
        RequireModel(SentryModelPath);
        RequireModel(CasingModelPath);

        EnsureFolder(MaterialDirectory);
        Dictionary<string, Material> materials = CreateAllMaterials();
        AssetDatabase.SaveAssets();

        ApplyImporter(SentryModelPath, SourceMaterialNames, materials);
        ApplyImporter(CasingModelPath, new[] { "Sentry_Brass" }, materials);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        Debug.Log("[SentryMachineGunAuthoring] Import settings and URP materials ready: "
                  + MaterialDirectory + " (" + materials.Count + " materials)");
    }

    [MenuItem("Tools/塔防/哨戒机枪/探针模型")]
    public static void ProbeModelMenu()
    {
        if (ProbeModel(out string summary)) Debug.Log(summary);
        else Debug.LogError(summary);
    }

    /// <summary>Batch entry: configure import + materials, then probe. Throws when the probe fails.</summary>
    public static void ConfigureImporterBatch()
    {
        ConfigureImporter();
        ProbeModelBatch();
    }

    /// <summary>Batch entry: probe only. Throws when any check fails so the batch reports failure.</summary>
    public static void ProbeModelBatch()
    {
        if (ProbeModel(out string summary)) Debug.Log(summary);
        else throw new InvalidOperationException(summary);
    }

    // ------------------------------------------------------------------ materials

    private static Dictionary<string, Material> CreateAllMaterials()
    {
        var created = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (string assetName in MaterialAssetNames)
            created[SlotForAssetName(assetName)] = SaveMaterial(assetName);
        return created;
    }

    private static string SlotForAssetName(string assetName) =>
        assetName == LaserBeamMaterialName
            ? LaserBeamMaterialName
            : assetName.Substring("M_".Length);

    /// <summary>
    /// Builds the palette from scratch. Changing the look of the gun means editing this
    /// switch and re-running the generator - never editing the FBX or the .mat files by hand.
    /// </summary>
    public static Material CreateMaterial(string assetName)
    {
        if (assetName == LaserBeamMaterialName) return CreateLaserBeamMaterial(assetName);

        string baseHex, emissionHex = null;
        float metallic, smoothness, emission = 0f;
        switch (assetName)
        {
            case "M_Sentry_DarkMetal":
                baseHex = "#2A2E31"; metallic = 0.80f; smoothness = 0.66f; break;
            case "M_Sentry_Armor":
                baseHex = "#4A5259"; metallic = 0.70f; smoothness = 0.60f; break;
            case "M_Sentry_Accent":
                baseHex = "#D97226"; metallic = 0.35f; smoothness = 0.58f; break;
            case "M_Sentry_Rubber":
                baseHex = "#17191B"; metallic = 0.05f; smoothness = 0.15f; break;
            case "M_Sentry_LaserLens":
                baseHex = "#C41A10"; metallic = 0.15f; smoothness = 0.82f;
                emissionHex = "#FF1A0A"; emission = 4.5f; break;
            case "M_Sentry_Brass":
                baseHex = "#C79A33"; metallic = 0.90f; smoothness = 0.68f; break;
            case "M_Sentry_MuzzleFlash":
                baseHex = "#FFC24D"; metallic = 0.00f; smoothness = 0.20f;
                emissionHex = "#FFB03A"; emission = 9.0f; break;
            default:
                throw new InvalidOperationException("Unknown sentry machine gun material: " + assetName);
        }

        Material material = NewOpaqueMaterial(assetName, baseHex);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission > 0f && emissionHex != null)
        {
            Color glow = ParseHex(emissionHex);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(glow.r * emission, glow.g * emission, glow.b * emission, 1f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return material;
    }

    /// <summary>URP Unlit additive blend, used by the runtime laser sight LineRenderer.</summary>
    private static Material CreateLaserBeamMaterial(string assetName)
    {
        Shader shader = RequireShader(UrpUnlit);
        Color color = ParseHex("#FF3A22");
        var material = new Material(shader) { name = assetName };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Surface", 1f); // transparent
        material.SetFloat("_Blend", 2f);   // additive
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_AlphaClip", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private static Material NewOpaqueMaterial(string assetName, string baseHex)
    {
        Shader shader = RequireShader(UrpLit);
        Color color = ParseHex(baseHex);
        var material = new Material(shader) { name = assetName };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Surface", 0f); // opaque
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_AlphaClip", 0f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        return material;
    }

    private static Material SaveMaterial(string assetName)
    {
        string path = MaterialPath(assetName);
        Material definition = CreateMaterial(assetName);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        try
        {
            // Unity's material importer also owns version metadata, so avoid dirtying a
            // material that already matches: a no-op run must leave the .mat untouched.
            if (!SameMaterial(existing, definition))
            {
                existing.shader = definition.shader;
                existing.CopyPropertiesFromMaterial(definition);
                existing.name = assetName;
                existing.renderQueue = definition.renderQueue;
                existing.globalIlluminationFlags = definition.globalIlluminationFlags;
                existing.SetOverrideTag("RenderType", definition.GetTag("RenderType", false, string.Empty));
                EditorUtility.SetDirty(existing);
            }
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
        return existing;
    }

    private static bool SameMaterial(Material actual, Material expected)
    {
        if (actual == null || expected == null) return false;
        if (actual.shader != expected.shader || actual.name != expected.name) return false;
        if (actual.renderQueue != expected.renderQueue) return false;
        if (actual.globalIlluminationFlags != expected.globalIlluminationFlags) return false;
        if (!actual.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(expected.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal))) return false;
        if (actual.GetTag("RenderType", false, string.Empty) != expected.GetTag("RenderType", false, string.Empty))
            return false;

        Shader shader = expected.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string property = shader.GetPropertyName(i);
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color:
                    if (actual.GetColor(property) != expected.GetColor(property)) return false;
                    break;
                case ShaderPropertyType.Vector:
                    if (actual.GetVector(property) != expected.GetVector(property)) return false;
                    break;
                case ShaderPropertyType.Texture:
                    if (actual.GetTexture(property) != expected.GetTexture(property)) return false;
                    if (actual.GetTextureOffset(property) != expected.GetTextureOffset(property)) return false;
                    if (actual.GetTextureScale(property) != expected.GetTextureScale(property)) return false;
                    break;
                case ShaderPropertyType.Int:
                    if (actual.GetInteger(property) != expected.GetInteger(property)) return false;
                    break;
                default:
                    if (!Mathf.Approximately(actual.GetFloat(property), expected.GetFloat(property))) return false;
                    break;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ importer

    private static void ApplyImporter(string modelPath, IEnumerable<string> slots,
        IDictionary<string, Material> materials)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Not an FBX model: " + modelPath);

        ApplyImporterSettings(importer, ModelImporterMaterialImportMode.ImportStandard);
        RemapMaterials(importer, slots, materials);
        importer.SaveAndReimport();

        // ImportStandard + AddRemap is the documented path, but if the project switches the
        // URP material description importer on this silently stops binding. Retry once with
        // the material-description mode instead of leaving magenta meshes behind.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (MaterialsBound(model, materials)) return;

        Debug.LogWarning("[SentryMachineGunAuthoring] Material remap did not bind for " + modelPath
                         + "; retrying with ImportViaMaterialDescription.");
        var retry = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (retry == null) throw new InvalidOperationException("Not an FBX model: " + modelPath);
        ApplyImporterSettings(retry, ModelImporterMaterialImportMode.ImportViaMaterialDescription);
        RemapMaterials(retry, slots, materials);
        retry.SaveAndReimport();

        model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (!MaterialsBound(model, materials))
            throw new InvalidOperationException("Could not bind the URP material library to " + modelPath);
    }

    private static void ApplyImporterSettings(ModelImporter importer, ModelImporterMaterialImportMode materialMode)
    {
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = true;
        importer.globalScale = 1f;
        importer.preserveHierarchy = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        // The Blender meshes are already authored at final density; welding or optimising
        // would renumber vertices and break the deterministic triangle budget in stage six.
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.materialImportMode = materialMode;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialSearch = ModelImporterMaterialSearch.Local;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
    }

    private static void RemapMaterials(ModelImporter importer, IEnumerable<string> slots,
        IDictionary<string, Material> materials)
    {
        foreach (string slot in slots)
        {
            Material material;
            if (!materials.TryGetValue(slot, out material) || material == null) continue;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), material);
        }
    }

    private static bool MaterialsBound(GameObject model, IDictionary<string, Material> materials)
    {
        if (model == null) return false;
        var allowed = new HashSet<Material>(materials.Values.Where(m => m != null));
        MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) return false;
        foreach (MeshRenderer renderer in renderers)
        {
            Material[] assigned = renderer.sharedMaterials;
            if (assigned.Length == 0) return false;
            foreach (Material material in assigned)
                if (material == null || !allowed.Contains(material)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ probe

    /// <summary>
    /// Reads the imported assets back and writes a machine-readable report to
    /// <see cref="ProbePath"/>. Returns false (and never throws) so callers decide how loud to be.
    /// </summary>
    public static bool ProbeModel(out string summary)
    {
        var checks = new List<Check>();
        var json = new StringBuilder();
        var failures = new List<string>();

        GameObject sentry = AssetDatabase.LoadAssetAtPath<GameObject>(SentryModelPath);
        GameObject casing = AssetDatabase.LoadAssetAtPath<GameObject>(CasingModelPath);

        checks.Add(new Check("sentry/model-present", sentry != null, SentryModelPath));
        checks.Add(new Check("casing/model-present", casing != null, CasingModelPath));

        json.Append("{\n");
        json.Append("  \"generatedAt\": ").Append(Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
        json.Append("  \"stage\": \"sentry-machine-gun/step3-import-and-materials\",\n");
        json.Append("  \"materialDirectory\": ").Append(Quote(MaterialDirectory)).Append(",\n");
        json.Append("  \"models\": {\n");

        Bounds sentryBounds = default(Bounds);
        bool haveSentryBounds = false;
        if (sentry != null)
        {
            sentryBounds = AppendModelReport(json, "sentry", sentry, checks, true);
            haveSentryBounds = true;
        }
        if (casing != null)
        {
            json.Append(",\n");
            Bounds casingBounds = AppendModelReport(json, "casing", casing, checks, false);
            AppendCasingChecks(checks, casingBounds);
        }
        json.Append("\n  },\n");

        if (sentry != null) AppendAxisContract(json, sentry, checks);
        if (haveSentryBounds) AppendSentryGeometryChecks(checks, sentryBounds);

        // Material library completeness: every declared asset must exist and use a URP shader.
        var missing = new List<string>();
        var wrongShader = new List<string>();
        foreach (string assetName in MaterialAssetNames)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(assetName));
            if (material == null) { missing.Add(assetName); continue; }
            if (!IsUrpShader(material.shader)) wrongShader.Add(assetName + " -> " + material.shader.name);
        }
        checks.Add(new Check("materials/library-complete", missing.Count == 0,
            missing.Count == 0 ? MaterialAssetNames.Length + " materials" : "missing: " + string.Join(", ", missing)));
        checks.Add(new Check("materials/urp-shaders", wrongShader.Count == 0,
            wrongShader.Count == 0 ? "all URP" : "non-URP: " + string.Join(", ", wrongShader)));

        foreach (Check check in checks)
            if (!check.Ok) failures.Add(check.Name + " (" + check.Detail + ")");

        bool passed = failures.Count == 0;
        json.Append("  \"checks\": [\n");
        for (int i = 0; i < checks.Count; i++)
        {
            Check check = checks[i];
            json.Append("    { \"name\": ").Append(Quote(check.Name))
                .Append(", \"ok\": ").Append(check.Ok ? "true" : "false")
                .Append(", \"detail\": ").Append(Quote(check.Detail)).Append(" }")
                .Append(i == checks.Count - 1 ? "\n" : ",\n");
        }
        json.Append("  ],\n");
        json.Append("  \"passed\": ").Append(passed ? "true" : "false").Append(",\n");
        json.Append("  \"failedChecks\": [");
        json.Append(string.Join(", ", failures.Select(Quote)));
        json.Append("]\n}\n");

        Directory.CreateDirectory(ProbeDirectory);
        File.WriteAllText(ProbePath, json.ToString(), new UTF8Encoding(false));

        var message = new StringBuilder();
        message.Append(passed ? "SENTRY_MG_STEP3_PROBE_PASS" : "SENTRY_MG_STEP3_PROBE_FAIL");
        message.Append(" | checks=").Append(checks.Count).Append(" failed=").Append(failures.Count);
        message.Append(" | report=").Append(ProbePath);
        if (!passed) message.Append(" | ").Append(string.Join("; ", failures));
        summary = message.ToString();
        return passed;
    }

    private static Bounds AppendModelReport(StringBuilder json, string key, GameObject model,
        List<Check> checks, bool requireUrpMaterials)
    {
        Transform[] nodes = model.GetComponentsInChildren<Transform>(true);
        MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
        MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);

        int triangles = 0;
        int vertices = 0;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            triangles += mesh.triangles.Length / 3;
            vertices += mesh.vertexCount;
        }

        Bounds bounds = UnionBounds(renderers);

        var materialRows = new List<string>();
        var seen = new HashSet<Material>();
        foreach (MeshRenderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !seen.Add(material)) continue;
                materialRows.Add("{ \"name\": " + Quote(material.name)
                                 + ", \"shader\": " + Quote(material.shader != null ? material.shader.name : "<null>")
                                 + ", \"emissive\": " + (material.IsKeywordEnabled("_EMISSION") ? "true" : "false") + " }");
            }
        }

        json.Append("    ").Append(Quote(key)).Append(": {\n");
        json.Append("      \"path\": ").Append(Quote(AssetDatabase.GetAssetPath(model))).Append(",\n");
        json.Append("      \"nodeCount\": ").Append(nodes.Length).Append(",\n");
        json.Append("      \"meshCount\": ").Append(filters.Length).Append(",\n");
        json.Append("      \"triangles\": ").Append(triangles).Append(",\n");
        json.Append("      \"vertices\": ").Append(vertices).Append(",\n");
        json.Append("      \"bounds\": { \"min\": ").Append(Vector(bounds.min))
            .Append(", \"max\": ").Append(Vector(bounds.max))
            .Append(", \"size\": ").Append(Vector(bounds.size)).Append(" },\n");
        json.Append("      \"materials\": [").Append(string.Join(", ", materialRows)).Append("],\n");
        json.Append("      \"hierarchy\": [");
        json.Append(string.Join(", ", nodes.Select(node => Quote(RelativePath(model.transform, node)))));
        json.Append("]\n    }");

        // Every renderer must resolve to a URP shader: a Standard material renders magenta
        // under URP, which is exactly the regression this probe exists to catch.
        var badShaders = new List<string>();
        var emptySlots = new List<string>();
        var missingMeshes = new List<string>();
        foreach (MeshRenderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) missingMeshes.Add(renderer.name);
            Material[] assigned = renderer.sharedMaterials;
            if (assigned.Length == 0) emptySlots.Add(renderer.name);
            foreach (Material material in assigned)
            {
                if (material == null) { emptySlots.Add(renderer.name); continue; }
                if (requireUrpMaterials && !IsUrpShader(material.shader))
                    badShaders.Add(renderer.name + " -> " + (material.shader != null ? material.shader.name : "<null>"));
            }
        }

        string prefix = key + "/";
        checks.Add(new Check(prefix + "has-meshes", filters.Length > 0, filters.Length + " meshes, " + triangles + " triangles"));
        checks.Add(new Check(prefix + "no-missing-mesh", missingMeshes.Count == 0,
            missingMeshes.Count == 0 ? "ok" : string.Join(", ", missingMeshes)));
        checks.Add(new Check(prefix + "no-empty-material-slot", emptySlots.Count == 0,
            emptySlots.Count == 0 ? "ok" : string.Join(", ", emptySlots)));
        checks.Add(new Check(prefix + "no-standard-or-magenta", badShaders.Count == 0,
            badShaders.Count == 0
                ? "all renderers use URP shaders"
                : string.Join(" | ", badShaders.Distinct())));
        checks.Add(new Check(prefix + "no-colliders", model.GetComponentsInChildren<Collider>(true).Length == 0,
            "colliders=" + model.GetComponentsInChildren<Collider>(true).Length));
        return bounds;
    }

    private static void AppendSentryGeometryChecks(List<Check> checks, Bounds bounds)
    {
        checks.Add(new Check("sentry/height", bounds.size.y <= MaxHeight,
            "height " + F(bounds.size.y) + " m (limit " + F(MaxHeight) + ")"));
        checks.Add(new Check("sentry/footprint",
            bounds.size.x <= MaxFootprint && bounds.size.z <= MaxFootprint,
            "footprint " + F(bounds.size.x) + " x " + F(bounds.size.z) + " m (limit " + F(MaxFootprint) + ")"));
        checks.Add(new Check("sentry/feet-on-ground", bounds.min.y >= MinFootY,
            "min y " + F(bounds.min.y) + " m (limit " + F(MinFootY) + ")"));
    }

    private static void AppendCasingChecks(List<Check> checks, Bounds bounds)
    {
        // The casing is modelled along +Y in Blender, which becomes +Z after the FBX
        // Y-up conversion; take the longest axis so the check survives an axis change.
        float length = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        checks.Add(new Check("casing/length", Mathf.Abs(length - 0.055f) <= CasingLengthTolerance,
            "length " + F(length) + " m (spec 0.055 +/- " + F(CasingLengthTolerance) + ")"));
    }

    // ------------------------------------------------------------------ preview

    /// <summary>Where the stage-three material check renders land, next to the Blender previews.</summary>
    public static string PreviewDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "SentryMachineGun", "previews", "unity-step3");

    [MenuItem("Tools/塔防/哨戒机枪/预览导入材质")]
    public static void RenderMaterialPreview()
    {
        GameObject model = RequireModel(SentryModelPath);
        Directory.CreateDirectory(PreviewDirectory);

        // Four angles: three-quarter, side, front and top, matching the Blender preview set so the
        // two can be compared directly. Requires -WithGraphics; -nographics renders black frames.
        // The gun is authored facing -Z (see the axis contract below), so "front" means negative Z.
        var shots = new[]
        {
            new { Name = "unity_01_three_quarter", Position = new Vector3(2.4f, 1.5f, -2.6f) },
            new { Name = "unity_02_side", Position = new Vector3(3.2f, 1.2f, 0f) },
            new { Name = "unity_03_front", Position = new Vector3(0f, 1.2f, -3.2f) },
            new { Name = "unity_04_top", Position = new Vector3(0.01f, 3.6f, 0f) },
        };

        const int width = 800, height = 600;
        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        var cameraObject = new GameObject("Sentry Preview Camera");
        var lightObject = new GameObject("Sentry Preview Light");
        var fillObject = new GameObject("Sentry Preview Fill");
        var rimObject = new GameObject("Sentry Preview Rim");
        RenderTexture target = new RenderTexture(width, height, 24);
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            camera.targetTexture = target;

            // Neutral key plus two weak fills: the metals are 0.7-0.9 metallic and pick up the light
            // colour almost completely, so a warm key at high intensity washes the whole gun orange.
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.85f;
            key.color = Color.white;
            lightObject.transform.rotation = Quaternion.Euler(40f, 35f, 0f);

            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.62f, 0.72f, 0.9f);
            fillObject.transform.rotation = Quaternion.Euler(15f, -140f, 0f);

            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.35f;
            rim.color = Color.white;
            rimObject.transform.rotation = Quaternion.Euler(70f, 200f, 0f);

            Vector3 focus = new Vector3(0f, 0.55f, -0.25f);
            camera.transform.position = shots[0].Position;
            camera.transform.LookAt(focus);

            // The first camera.Render() of a batch session runs before URP finishes warming up its
            // pipeline state and comes out flat and mis-shaded. Render two throwaway frames first so
            // every captured PNG is trustworthy.
            for (int warmUp = 0; warmUp < 2; warmUp++) camera.Render();

            foreach (var shot in shots)
            {
                camera.transform.position = shot.Position;
                camera.transform.LookAt(focus);
                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(PreviewDirectory, shot.Name + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = null;
                Debug.Log("[SentryMachineGunAuthoring] preview " + shot.Name);
            }
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(fillObject);
            Object.DestroyImmediate(rimObject);
            Object.DestroyImmediate(target);
            if (root != null) Object.DestroyImmediate(root);
        }
        Debug.Log("[SentryMachineGunAuthoring] previews written to " + PreviewDirectory);
    }

    // ------------------------------------------------------------------ axis contract

    /// <summary>
    /// Measures the four marker empties the Blender build script exports. Stage four rebuilds the
    /// runtime hierarchy from these, so the probe records the exact local offsets and the resulting
    /// barrel axis instead of letting the prefab generator guess - and instead of discovering the
    /// problem in stage six when the turret aims away from its target.
    /// </summary>
    private static void AppendAxisContract(StringBuilder json, GameObject sentry, List<Check> checks)
    {
        Transform pivot = FindNode(sentry.transform, "Marker_Pivot");
        Transform muzzle = FindNode(sentry.transform, "Marker_Muzzle");
        Transform ejection = FindNode(sentry.transform, "Marker_Ejection");
        Transform laser = FindNode(sentry.transform, "Marker_Laser_Emitter");

        var missing = new List<string>();
        if (pivot == null) missing.Add("Marker_Pivot");
        if (muzzle == null) missing.Add("Marker_Muzzle");
        if (ejection == null) missing.Add("Marker_Ejection");
        if (laser == null) missing.Add("Marker_Laser_Emitter");
        checks.Add(new Check("sentry/markers-present", missing.Count == 0,
            missing.Count == 0 ? "4 marker empties" : "missing: " + string.Join(", ", missing)));

        Vector3 pivotLocal = LocalOf(sentry.transform, pivot);
        Vector3 muzzleLocal = LocalOf(sentry.transform, muzzle);
        Vector3 ejectionLocal = LocalOf(sentry.transform, ejection);
        Vector3 laserLocal = LocalOf(sentry.transform, laser);

        Vector3 forward = muzzleLocal - pivotLocal;
        forward.y = 0f;
        bool degenerate = forward.sqrMagnitude < 1e-6f;
        string forwardAxis = degenerate ? "<degenerate>" : DominantAxis(forward.normalized);
        bool matchesContract = forwardAxis == "+Z";

        // +X is the gun's right only while the barrel points +Z. Report the flank explicitly so a
        // later yaw compensation cannot silently move the ejection port to the other side.
        Vector3 right = degenerate ? Vector3.right : Vector3.Cross(Vector3.up, forward.normalized);
        Vector3 lateral = ejectionLocal - pivotLocal;
        lateral.y = 0f;
        string ejectionSide = lateral.sqrMagnitude < 1e-6f
            ? "<centred>"
            : (Vector3.Dot(lateral, right) >= 0f ? "right" : "left");

        json.Append("  \"axisContract\": {\n");
        json.Append("    \"marker_pivot\": ").Append(Vector(pivotLocal)).Append(",\n");
        json.Append("    \"marker_muzzle\": ").Append(Vector(muzzleLocal)).Append(",\n");
        json.Append("    \"marker_ejection\": ").Append(Vector(ejectionLocal)).Append(",\n");
        json.Append("    \"marker_laserEmitter\": ").Append(Vector(laserLocal)).Append(",\n");
        json.Append("    \"muzzleRelativeToPivot\": ").Append(Vector(muzzleLocal - pivotLocal)).Append(",\n");
        json.Append("    \"barrelForwardAxis\": ").Append(Quote(forwardAxis)).Append(",\n");
        json.Append("    \"expectedForwardAxis\": \"+Z\",\n");
        json.Append("    \"forwardMatchesContract\": ").Append(matchesContract ? "true" : "false").Append(",\n");
        json.Append("    \"ejectionSideOfGun\": ").Append(Quote(ejectionSide)).Append(",\n");
        json.Append("    \"remediation\": ").Append(Quote(matchesContract
            ? "none"
            : "Blender authors the barrel along +Y and Unity's Y-up conversion maps that to -Z. "
              + "Stage four must yaw the baked art 180 degrees about Y inside Gun Recoil, which moves Muzzle "
              + "to local +Z and the ejection port to +X - i.e. exactly the contract's right flank - so no "
              + "Blender re-author is needed. Move the marker-derived Muzzle / Ejection Port / Laser Emitter "
              + "empties into the same yawed container so their offsets stay consistent.")).Append("\n");
        json.Append("  },\n");

        if (!matchesContract)
            Debug.LogWarning("[SentryMachineGunAuthoring] Barrel forward axis is " + forwardAxis
                             + " (contract expects +Z); ejection port sits on the gun's " + ejectionSide
                             + ". Stage four must compensate with a 180 degree yaw inside Gun Recoil.");
    }

    private static Transform FindNode(Transform root, string nodeName)
    {
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == nodeName) return node;
        return null;
    }

    private static Vector3 LocalOf(Transform root, Transform node) =>
        node == null ? Vector3.zero : root.InverseTransformPoint(node.position);

    private static string DominantAxis(Vector3 direction)
    {
        float ax = Mathf.Abs(direction.x), ay = Mathf.Abs(direction.y), az = Mathf.Abs(direction.z);
        if (ax >= ay && ax >= az) return direction.x >= 0f ? "+X" : "-X";
        if (ay >= az) return direction.y >= 0f ? "+Y" : "-Y";
        return direction.z >= 0f ? "+Z" : "-Z";
    }

    private static Bounds UnionBounds(Renderer[] renderers)
    {
        bool started = false;
        Bounds union = default(Bounds);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            if (!started) { union = renderer.bounds; started = true; }
            else union.Encapsulate(renderer.bounds);
        }
        return union;
    }

    // ------------------------------------------------------------------ stage four prefab

    public const string PrefabDirectory = "Assets/Prefabs";
    public const string SentryPrefabPath = "Assets/Resources/SentryMachineGun.prefab";
    public const string CasingPrefabPath = PrefabDirectory + "/SentryShellCasing.prefab";

    public const string AimPivotNodeName = "Aim Pivot";
    public const string GunRecoilNodeName = "Gun Recoil";
    public const string MuzzleNodeName = "Muzzle";
    public const string EjectionPortNodeName = "Ejection Port";
    public const string LaserEmitterNodeName = "Laser Emitter";
    public const string MuzzleFlashNodeName = "Muzzle Flash";
    public const string BaseNodeName = "Base";
    public const string ArtNodeName = "Art";

    /// <summary>Node names the runtime and the stage-six tests look up by name.</summary>
    public static readonly string[] ContractNodeNames =
    {
        AimPivotNodeName, GunRecoilNodeName, MuzzleNodeName, EjectionPortNodeName,
        LaserEmitterNodeName, "Leg_FL", "Leg_FR", "Leg_RL", "Leg_RR",
    };

    public static string PrefabProbePath => Path.Combine(ProbeDirectory, "probe-prefab.json");

    /// <summary>Desktop hand-off folder for stage-four backups, reports and later previews.</summary>
    public static string StageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "SentryMachineGun", "stage4-unity");

    [MenuItem("Tools/塔防/哨戒机枪/生成阶段 4 预制体")]
    public static void CreateAssetsMenu()
    {
        if (!CreateAssetsInternal(out string summary))
        {
            Debug.LogError(summary);
            return;
        }
        Debug.Log(summary);
        ProbePrefabMenu();
    }

    /// <summary>Batch entry: backup, delete the legacy art, build both prefabs, then probe.</summary>
    public static void CreateAssetsBatch()
    {
        if (!CreateAssetsInternal(out string summary)) throw new InvalidOperationException(summary);
        Debug.Log(summary);
        ProbePrefabBatch();
    }

    /// <summary>
    /// Rebuilds <c>Assets/Prefabs/SentryShellCasing.prefab</c> and
    /// <c>Assets/Resources/SentryMachineGun.prefab</c> from the imported FBX.
    /// Every hand edit to those prefabs is lost on the next run, which is intended:
    /// the generator owns the hierarchy, and the probe below is what keeps it honest.
    /// </summary>
    public static bool CreateAssetsInternal(out string summary)
    {
        ConfigureImporter();

        string backupDirectory = BackupStageFourInputs(out int backedUp);
        List<string> deleted = DeleteLegacyResources();
        EnsureFolder(PrefabDirectory);

        CreateCasingPrefab();
        CreateSentryPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        GameObject art = AssetDatabase.LoadAssetAtPath<GameObject>(SentryPrefabPath);
        GameObject casing = AssetDatabase.LoadAssetAtPath<GameObject>(CasingPrefabPath);
        bool ok = art != null && casing != null;

        var report = new StringBuilder();
        report.Append("{\n");
        report.Append("  \"generatedAt\": ").Append(Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
        report.Append("  \"stage\": \"sentry-machine-gun/step4-prefabs\",\n");
        report.Append("  \"artPrefab\": ").Append(Quote(SentryPrefabPath)).Append(",\n");
        report.Append("  \"artPrefabCreated\": ").Append(art != null ? "true" : "false").Append(",\n");
        report.Append("  \"casingPrefab\": ").Append(Quote(CasingPrefabPath)).Append(",\n");
        report.Append("  \"casingPrefabCreated\": ").Append(casing != null ? "true" : "false").Append(",\n");
        report.Append("  \"backupDirectory\": ").Append(Quote(backupDirectory)).Append(",\n");
        report.Append("  \"backedUpFiles\": ").Append(backedUp).Append(",\n");
        report.Append("  \"deletedLegacyResources\": [").Append(string.Join(", ", deleted.Select(Quote))).Append("]\n");
        report.Append("}\n");

        Directory.CreateDirectory(StageDirectory);
        File.WriteAllText(Path.Combine(StageDirectory, "stage4-generation.json"), report.ToString(),
            new UTF8Encoding(false));

        summary = (ok ? "SENTRY_MG_STEP4_PREFABS_PASS" : "SENTRY_MG_STEP4_PREFABS_FAIL")
                  + " | art=" + (art != null) + " casing=" + (casing != null)
                  + " | backup=" + backupDirectory
                  + " | deleted=" + deleted.Count
                  + " | report=" + Path.Combine(StageDirectory, "stage4-generation.json");
        return ok;
    }

    [MenuItem("Tools/塔防/哨戒机枪/探针预制体")]
    public static void ProbePrefabMenu()
    {
        if (ProbePrefab(out string summary)) Debug.Log(summary);
        else Debug.LogError(summary);
    }

    /// <summary>Batch entry: probe only. Throws when any check fails.</summary>
    public static void ProbePrefabBatch()
    {
        if (ProbePrefab(out string summary)) Debug.Log(summary);
        else throw new InvalidOperationException(summary);
    }

    private static string BackupStageFourInputs(out int copied)
    {
        copied = 0;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string directory = Path.Combine(StageDirectory, "backups", stamp);
        Directory.CreateDirectory(directory);

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string[] candidates =
        {
            SentryModelPath,
            CasingModelPath,
            SentryPrefabPath,
            CasingPrefabPath,
            "Assets/Resources/SentryMachineGun.fbx",
            "Assets/Resources/SentryMachineGun.blend",
            "Assets/Resources/SentryMachineGun.prefab",
            "Assets/Models/SentryMachineGun.fbx",
            "Assets/Models/SentryMachineGun.blend",
            "Assets/Models/create_sentry_machine_gun.py",
        };
        foreach (string relative in candidates)
        {
            string absolute = Path.Combine(projectRoot, relative);
            if (!File.Exists(absolute)) continue;
            string target = Path.Combine(directory, relative.Replace('/', '_'));
            File.Copy(absolute, target, true);
            copied++;
        }
        return directory;
    }

    /// <summary>
    /// Removes the pre-rework art that would otherwise shadow the new prefab under
    /// <c>Resources.Load("SentryMachineGun")</c>. All of it is unreferenced by GUID and
    /// recoverable from <c>backups/</c> plus git history.
    /// </summary>
    private static List<string> DeleteLegacyResources()
    {
        var deleted = new List<string>();
        string[] legacy =
        {
            "Assets/Resources/SentryMachineGun.fbx",
            "Assets/Resources/SentryMachineGun.blend",
            "Assets/Models/SentryMachineGun.fbx",
            "Assets/Models/SentryMachineGun.blend",
            "Assets/Models/create_sentry_machine_gun.py",
        };
        foreach (string path in legacy)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) continue;
            if (AssetDatabase.DeleteAsset(path)) deleted.Add(path);
            else throw new InvalidOperationException("Could not delete legacy asset: " + path);
        }
        if (deleted.Count > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return deleted;
    }

    private static void CreateCasingPrefab()
    {
        GameObject source = RequireModel(CasingModelPath);
        var clone = (GameObject)Object.Instantiate(source);
        clone.name = "SentryShellCasing";
        try
        {
            // The FBX wraps the three casing meshes in an empty node; the runtime asset is
            // flatten to root + three MeshRenderers so the pool can just toggle it.
            for (int i = 0; i < clone.transform.childCount; i++)
            {
                Transform child = clone.transform.GetChild(i);
                if (child.GetComponent<MeshRenderer>() != null || child.childCount == 0) continue;
                while (child.childCount > 0)
                {
                    Transform mesh = child.GetChild(0);
                    mesh.SetParent(clone.transform, true);
                }
                Object.DestroyImmediate(child.gameObject);
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(clone, CasingPrefabPath, out bool success);
            if (!success || saved == null)
                throw new InvalidOperationException("Could not save " + CasingPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(clone);
        }
    }

    /// <summary>
    /// Rebuilds the runtime hierarchy from the flat FBX. The Blender model is baked in
    /// root space and its barrel points -Z after Unity's Y-up conversion, so the gun's
    /// baked meshes are moved into an <c>Art</c> container under <c>Gun Recoil</c> that
    /// carries a 180 degree yaw. The marker-derived Muzzle / Ejection Port / Laser
    /// Emitter points are computed through the same yaw, so they stay on the barrel.
    /// </summary>
    private static void CreateSentryPrefab()
    {
        GameObject source = RequireModel(SentryModelPath);
        var clone = (GameObject)Object.Instantiate(source);
        clone.name = "Sentry Source";
        GameObject root = null;
        try
        {
            Transform sourceRoot = clone.transform;
            Transform container = null;
            for (int i = 0; i < sourceRoot.childCount; i++)
                if (sourceRoot.GetChild(i).name == "SentryMachineGun") container = sourceRoot.GetChild(i);
            if (container == null)
                throw new InvalidOperationException("Sentry FBX has no inner 'SentryMachineGun' container");

            Vector3 pivotLocal = MarkerLocal(sourceRoot, "Marker_Pivot");
            Vector3 muzzleBaked = MarkerLocal(sourceRoot, "Marker_Muzzle");
            Vector3 ejectionBaked = MarkerLocal(sourceRoot, "Marker_Ejection");
            Vector3 laserBaked = MarkerLocal(sourceRoot, "Marker_Laser_Emitter");

            Quaternion yaw = Quaternion.Euler(0f, 180f, 0f);
            Vector3 Yawed(Vector3 point) => pivotLocal + yaw * (point - pivotLocal);

            root = new GameObject("SentryMachineGun");
            Transform baseNode = NewNode(BaseNodeName, root.transform, Vector3.zero, Quaternion.identity);
            var legs = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (string side in new[] { "FL", "FR", "RL", "RR" })
                legs[side] = NewNode("Leg_" + side, baseNode, Vector3.zero, Quaternion.identity);

            Transform aimPivot = NewNode(AimPivotNodeName, root.transform, pivotLocal, Quaternion.identity);
            Transform gunRecoil = NewNode(GunRecoilNodeName, aimPivot, Vector3.zero, Quaternion.identity);
            Transform art = NewNode(ArtNodeName, gunRecoil, Vector3.zero, yaw);

            Transform muzzle = NewNode(MuzzleNodeName, gunRecoil,
                Yawed(muzzleBaked) - pivotLocal, Quaternion.identity);
            Transform ejection = NewNode(EjectionPortNodeName, gunRecoil,
                Yawed(ejectionBaked) - pivotLocal, Quaternion.identity);
            Transform laser = NewNode(LaserEmitterNodeName, gunRecoil,
                Yawed(laserBaked) - pivotLocal, Quaternion.identity);
            Transform flash = NewNode(MuzzleFlashNodeName, gunRecoil,
                Yawed(muzzleBaked) - pivotLocal, Quaternion.identity);

            MeshRenderer[] renderers = container.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                Transform mesh = renderer.transform;
                Vector3 position = sourceRoot.InverseTransformPoint(mesh.position);
                Quaternion rotation = Quaternion.Inverse(sourceRoot.rotation) * mesh.rotation;
                Vector3 scale = mesh.lossyScale;
                string meshName = mesh.name;

                if (meshName.StartsWith("FX_Muzzle_Flash", StringComparison.Ordinal))
                {
                    Reparent(mesh, flash, Yawed(position) - Yawed(muzzleBaked), yaw * rotation, scale);
                }
                else if (meshName.StartsWith("Base_", StringComparison.Ordinal))
                {
                    Transform parent = baseNode;
                    foreach (KeyValuePair<string, Transform> leg in legs)
                    {
                        if (!meshName.EndsWith("_" + leg.Key, StringComparison.Ordinal)) continue;
                        parent = leg.Value;
                        break;
                    }
                    Reparent(mesh, parent, position, rotation, scale);
                }
                else
                {
                    Reparent(mesh, art, position - pivotLocal, rotation, scale);
                }
            }

            Object.DestroyImmediate(container.gameObject);

            var feedback = root.AddComponent<SentryFireFeedback>();
            var ejector = root.AddComponent<SentryShellEjector>();
            var laserSight = root.AddComponent<SentryLaserSight>();
            GameObject casingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CasingPrefabPath);
            Material beamMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(LaserBeamMaterialName));

            Wire(feedback, "recoilRoot", gunRecoil);
            Wire(feedback, "muzzle", muzzle);
            Wire(feedback, "muzzleFlash", flash.gameObject);
            Wire(ejector, "ejectionPort", ejection);
            Wire(ejector, "casingPrefab", casingPrefab);
            Wire(laserSight, "laserEmitter", laser);
            Wire(laserSight, "beamMaterial", beamMaterial);

            // Hidden until the first shot: a live flash mesh on a parked turret reads as a bug.
            flash.gameObject.SetActive(false);

            // Defensive: the laser line is runtime-only, and a stray editor-time instance
            // must never be baked into the prefab.
            foreach (Transform stray in root.GetComponentsInChildren<Transform>(true)
                         .Where(node => node.name == "Laser Beam").ToArray())
                Object.DestroyImmediate(stray.gameObject);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SentryPrefabPath, out bool success);
            if (!success || saved == null)
                throw new InvalidOperationException("Could not save " + SentryPrefabPath);
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            Object.DestroyImmediate(clone);
        }
    }

    private static void Reparent(Transform node, Transform parent, Vector3 localPosition,
        Quaternion localRotation, Vector3 localScale)
    {
        node.SetParent(parent, false);
        node.localPosition = localPosition;
        node.localRotation = localRotation;
        node.localScale = localScale;
    }

    private static Transform NewNode(string name, Transform parent, Vector3 localPosition,
        Quaternion localRotation)
    {
        var node = new GameObject(name);
        node.transform.SetParent(parent, false);
        node.transform.localPosition = localPosition;
        node.transform.localRotation = localRotation;
        return node.transform;
    }

    private static Vector3 MarkerLocal(Transform sourceRoot, string markerName)
    {
        Transform marker = FindNode(sourceRoot, markerName);
        if (marker == null) throw new InvalidOperationException("Sentry FBX is missing " + markerName);
        return sourceRoot.InverseTransformPoint(marker.position);
    }

    private static void Wire(Component component, string fieldName, Object value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
            throw new InvalidOperationException(component.GetType().Name + " has no field " + fieldName);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ prefab probe

    /// <summary>
    /// Reads the generated prefabs back and writes <c>probe-prefab.json</c>. Fails when the
    /// hierarchy contract, bounds, materials, component wiring or Resources lookup drift.
    /// </summary>
    public static bool ProbePrefab(out string summary)
    {
        var checks = new List<Check>();
        var json = new StringBuilder();
        var failures = new List<string>();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SentryPrefabPath);
        GameObject casing = AssetDatabase.LoadAssetAtPath<GameObject>(CasingPrefabPath);
        checks.Add(new Check("prefab/art-present", prefab != null, SentryPrefabPath));
        checks.Add(new Check("prefab/casing-present", casing != null, CasingPrefabPath));

        json.Append("{\n");
        json.Append("  \"generatedAt\": ").Append(Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
        json.Append("  \"stage\": \"sentry-machine-gun/step4-prefabs\",\n");
        json.Append("  \"artPrefab\": ").Append(Quote(SentryPrefabPath)).Append(",\n");
        json.Append("  \"casingPrefab\": ").Append(Quote(CasingPrefabPath)).Append(",\n");

        GameObject loaded = Resources.Load<GameObject>("SentryMachineGun");
        string loadedPath = loaded != null ? AssetDatabase.GetAssetPath(loaded) : "<null>";
        checks.Add(new Check("prefab/resources-load-unique",
            loaded != null && loadedPath == SentryPrefabPath, loadedPath));

        if (prefab != null)
        {
            Transform root = prefab.transform;
            json.Append("  \"hierarchy\": [");
            json.Append(string.Join(", ", root.GetComponentsInChildren<Transform>(true)
                .Select(node => Quote(RelativePath(root, node)))));
            json.Append("],\n");

            foreach (string nodeName in ContractNodeNames)
            {
                int count = FindAll(root, nodeName).Length;
                checks.Add(new Check("hierarchy/" + nodeName, count == 1, count + " occurrence(s)"));
            }

            foreach (string side in new[] { "FL", "FR", "RL", "RR" })
            {
                Transform leg = FindAll(root, "Leg_" + side).FirstOrDefault();
                int legRenderers = leg != null ? leg.GetComponentsInChildren<MeshRenderer>(true).Length : 0;
                checks.Add(new Check("hierarchy/Leg_" + side + "-parts", legRenderers > 0,
                    legRenderers + " part(s)"));
            }

            Bounds bounds = UnionBounds(prefab.GetComponentsInChildren<MeshRenderer>(true));
            json.Append("  \"bounds\": { \"min\": ").Append(Vector(bounds.min))
                .Append(", \"max\": ").Append(Vector(bounds.max))
                .Append(", \"size\": ").Append(Vector(bounds.size)).Append(" },\n");

            checks.Add(new Check("geometry/height", bounds.size.y <= MaxHeight,
                "height " + F(bounds.size.y) + " m (limit " + F(MaxHeight) + ")"));
            checks.Add(new Check("geometry/footprint",
                bounds.size.x <= MaxFootprint && bounds.size.z <= MaxFootprint,
                "footprint " + F(bounds.size.x) + " x " + F(bounds.size.z) + " m (limit " + F(MaxFootprint) + ")"));
            checks.Add(new Check("geometry/feet-on-ground", bounds.min.y >= MinFootY,
                "min y " + F(bounds.min.y) + " m (limit " + F(MinFootY) + ")"));

            Transform aim = FindAll(root, AimPivotNodeName).FirstOrDefault();
            Transform muzzleNode = FindAll(root, MuzzleNodeName).FirstOrDefault();
            Transform ejectionNode = FindAll(root, EjectionPortNodeName).FirstOrDefault();

            bool muzzleUnderPivot = aim != null && muzzleNode != null && muzzleNode.IsChildOf(aim);
            checks.Add(new Check("contract/muzzle-under-aim-pivot", muzzleUnderPivot,
                muzzleUnderPivot ? "ok" : "Muzzle is not below Aim Pivot"));

            Vector3 muzzleForward = muzzleNode != null ? muzzleNode.forward : Vector3.zero;
            checks.Add(new Check("contract/muzzle-forward", Vector3.Dot(muzzleForward, Vector3.forward) > 0.99f,
                "forward " + Vector(muzzleForward)));

            Vector3 muzzleLocal = aim != null && muzzleNode != null
                ? aim.InverseTransformPoint(muzzleNode.position)
                : Vector3.zero;
            checks.Add(new Check("contract/muzzle-ahead", muzzleLocal.z > 0.5f,
                "pivot-local " + Vector(muzzleLocal)));

            Vector3 ejectionLocal = aim != null && ejectionNode != null
                ? aim.InverseTransformPoint(ejectionNode.position)
                : Vector3.zero;
            checks.Add(new Check("contract/ejection-right-flank", ejectionLocal.x > 0.02f,
                "pivot-local " + Vector(ejectionLocal)));

            int triangles = 0;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) triangles += filter.sharedMesh.triangles.Length / 3;
            json.Append("  \"triangles\": ").Append(triangles).Append(",\n");
            checks.Add(new Check("geometry/triangle-budget", triangles > 0 && triangles <= 20000,
                triangles + " triangles (budget 20000)"));

            checks.Add(new Check("prefab/no-colliders",
                prefab.GetComponentsInChildren<Collider>(true).Length == 0,
                "colliders=" + prefab.GetComponentsInChildren<Collider>(true).Length));
            checks.Add(new Check("prefab/no-rigidbody",
                prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0, "rigidbodies"));
            checks.Add(new Check("prefab/no-animator",
                prefab.GetComponentsInChildren<Animator>(true).Length == 0, "animators"));

            var allowedMaterials = new HashSet<Material>(MaterialAssetNames
                .Select(assetName => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(assetName)))
                .Where(material => material != null));
            var badShaders = new List<string>();
            var emptySlots = new List<string>();
            var foreignMaterials = new List<string>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] assigned = renderer.sharedMaterials;
                if (assigned.Length == 0) emptySlots.Add(renderer.name);
                foreach (Material material in assigned)
                {
                    if (material == null) { emptySlots.Add(renderer.name); continue; }
                    if (!IsUrpShader(material.shader)) badShaders.Add(renderer.name + " -> " + material.shader.name);
                    if (!allowedMaterials.Contains(material)) foreignMaterials.Add(renderer.name + " -> " + material.name);
                }
            }
            checks.Add(new Check("materials/no-empty-slot", emptySlots.Count == 0,
                emptySlots.Count == 0 ? "ok" : string.Join(", ", emptySlots)));
            checks.Add(new Check("materials/urp-shaders", badShaders.Count == 0,
                badShaders.Count == 0 ? "all URP" : string.Join(" | ", badShaders.Distinct())));
            checks.Add(new Check("materials/from-library", foreignMaterials.Count == 0,
                foreignMaterials.Count == 0 ? "all from " + MaterialDirectory : string.Join(" | ", foreignMaterials.Distinct())));

            var feedback = prefab.GetComponent<SentryFireFeedback>();
            checks.Add(new Check("wiring/fire-feedback",
                feedback != null && feedback.RecoilRoot != null && feedback.Muzzle != null
                && feedback.MuzzleFlash != null,
                feedback == null ? "missing component" : "recoilRoot / muzzle / muzzleFlash"));
            checks.Add(new Check("wiring/muzzle-flash-starts-hidden",
                feedback == null || (feedback.MuzzleFlash != null && !feedback.MuzzleFlash.activeSelf),
                feedback == null || feedback.MuzzleFlash == null ? "no flash reference"
                    : "activeSelf=" + feedback.MuzzleFlash.activeSelf));

            var ejector = prefab.GetComponent<SentryShellEjector>();
            checks.Add(new Check("wiring/shell-ejector",
                ejector != null && ejector.EjectionPort != null && ejector.CasingPrefab != null,
                ejector == null ? "missing component" : "ejectionPort / casingPrefab"));

            var laserSight = prefab.GetComponent<SentryLaserSight>();
            bool laserWired = laserSight != null && laserSight.LaserEmitter != null
                              && laserSight.BeamMaterial != null && IsUrpShader(laserSight.BeamMaterial.shader);
            checks.Add(new Check("wiring/laser-sight", laserWired,
                laserSight == null ? "missing component" : "laserEmitter / beamMaterial"));
        }

        if (casing != null)
        {
            int casingRenderers = casing.GetComponentsInChildren<MeshRenderer>(true).Length;
            checks.Add(new Check("casing/meshes", casingRenderers == 3, casingRenderers + " renderers"));

            var allowedTypes = new[] { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer) };
            var extraTypes = casing.GetComponentsInChildren<Component>(true)
                .Where(component => !allowedTypes.Contains(component.GetType()))
                .Select(component => component.GetType().Name)
                .Distinct()
                .ToList();
            checks.Add(new Check("casing/only-visual-components", extraTypes.Count == 0,
                extraTypes.Count == 0 ? "Transform / MeshFilter / MeshRenderer" : string.Join(", ", extraTypes)));
            checks.Add(new Check("casing/no-collider",
                casing.GetComponentsInChildren<Collider>(true).Length == 0,
                "colliders=" + casing.GetComponentsInChildren<Collider>(true).Length));
        }

        foreach (Check check in checks)
            if (!check.Ok) failures.Add(check.Name + " (" + check.Detail + ")");

        bool passed = failures.Count == 0;
        json.Append("  \"checks\": [\n");
        for (int i = 0; i < checks.Count; i++)
        {
            Check check = checks[i];
            json.Append("    { \"name\": ").Append(Quote(check.Name))
                .Append(", \"ok\": ").Append(check.Ok ? "true" : "false")
                .Append(", \"detail\": ").Append(Quote(check.Detail)).Append(" }")
                .Append(i == checks.Count - 1 ? "\n" : ",\n");
        }
        json.Append("  ],\n");
        json.Append("  \"passed\": ").Append(passed ? "true" : "false").Append(",\n");
        json.Append("  \"failedChecks\": [").Append(string.Join(", ", failures.Select(Quote))).Append("]\n}\n");

        Directory.CreateDirectory(ProbeDirectory);
        File.WriteAllText(PrefabProbePath, json.ToString(), new UTF8Encoding(false));
        Directory.CreateDirectory(StageDirectory);
        File.WriteAllText(Path.Combine(StageDirectory, "probe-prefab.json"), json.ToString(),
            new UTF8Encoding(false));

        var message = new StringBuilder();
        message.Append(passed ? "SENTRY_MG_STEP4_PROBE_PASS" : "SENTRY_MG_STEP4_PROBE_FAIL");
        message.Append(" | checks=").Append(checks.Count).Append(" failed=").Append(failures.Count);
        message.Append(" | report=").Append(PrefabProbePath);
        if (!passed) message.Append(" | ").Append(string.Join("; ", failures));
        summary = message.ToString();
        return passed;
    }

    private static Transform[] FindAll(Transform root, string nodeName) =>
        root.GetComponentsInChildren<Transform>(true).Where(node => node.name == nodeName).ToArray();

    // ------------------------------------------------------------------ helpers

    private static bool IsUrpShader(Shader shader)
    {
        if (shader == null) return false;
        if (shader.name == "Hidden/InternalErrorShader") return false;
        return shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
    }

    private static Shader RequireShader(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
        return shader;
    }

    private static GameObject RequireModel(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) throw new InvalidOperationException("Missing model: " + path);
        return model;
    }

    private static Color ParseHex(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color color))
            throw new InvalidOperationException("Bad colour: " + hex);
        return color;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
            throw new IOException("Could not create asset directory: " + path);
    }

    private static string RelativePath(Transform root, Transform node)
    {
        var parts = new List<string>();
        for (Transform current = node; current != null; current = current.parent)
        {
            parts.Add(current.name);
            if (current == root) break;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Vector(Vector3 value) =>
        "[" + F(value.x) + ", " + F(value.y) + ", " + F(value.z) + "]";

    private static string Quote(string value)
    {
        if (value == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private readonly struct Check
    {
        public readonly string Name;
        public readonly bool Ok;
        public readonly string Detail;

        public Check(string name, bool ok, string detail)
        {
            Name = name;
            Ok = ok;
            Detail = detail ?? string.Empty;
        }
    }
}
