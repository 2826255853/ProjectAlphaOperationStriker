using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Stage four validates FBX models in an isolated preview scene, without saving assets.</summary>
public static class TrapPlacementProjectionGunModelValidation
{
    public const string ModelDirectory = "Assets/Models/TrapPlacementProjectionGun/";
    public static readonly Vector3 PreviewOffset = TrapPlacementProjectionGunAuthoring.FirstPersonOffset;
    private const int Width = 1600, Height = 900;
    private static readonly string[] RequiredMaterials = { "PG_WhiteShell", "PG_BlueArmor", "PG_DarkStructure",
        "PG_GripRubber", "PG_ProjectionLens", "PG_CyanEmission", "PG_DisplayGlass" };

    [Serializable] private sealed class ViewReport
    {
        public float verticalFov, nearestDepth;
        public Vector2 viewportMin, viewportMax;
        public string preview;
    }

    [Serializable] private sealed class ModelReport
    {
        public string asset, sha256;
        public Vector3 size, muzzle, muzzleForward, grip;
        public int meshCount, triangles, vertices, rearDisplayMeshes;
        public string[] materials;
        public List<string> checks = new List<string>();
        public List<ViewReport> views = new List<ViewReport>();
    }

    [Serializable] private sealed class Report
    {
        public string status = "RUNNING", unityVersion, error;
        public string scope = "Imported FBX and isolated editor previews; no Prefab or PlayMode acceptance.";
        public Vector3 firstPersonOffset = PreviewOffset, firstPersonEuler = Vector3.zero;
        public float nearClip = 0.05f;
        public int width = Width, height = Height;
        public List<ModelReport> models = new List<ModelReport>();
    }

    [MenuItem("Tools/塔防/投影枪/检查阶段四模型并生成预览")]
    public static void Run()
    {
        // The current-account desktop was confirmed during this model's delivery.
        string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "TrapPlacementProjectionGun", "stage4-unity");
        Directory.CreateDirectory(output);
        var report = new Report { unityVersion = Application.unityVersion };
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            ValidateModel("TrapPlacementProjectionGun", false, output, report);
            ValidateModel("TrapPlacementProjectionGun_ScreenUpdated", true, output, report);
            report.status = "PASS";
            Debug.Log("PROJECTION_GUN_STAGE4_PASS: " + report.models.Sum(m => m.checks.Count)
                + " checks; previews/report: " + output);
        }
        catch (Exception ex) { report.status = "FAIL"; report.error = ex.ToString(); throw; }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            File.WriteAllText(Path.Combine(output, "validation-report.json"), JsonUtility.ToJson(report, true));
        }
    }

    private static void ValidateModel(string name, bool rearScreen, string output, Report report)
    {
        string path = ModelDirectory + name + ".fbx";
        var result = new ModelReport { asset = path };
        report.models.Add(result);
        using (var sha = SHA256.Create())
            result.sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(result, asset != null, "FBX imports successfully");
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        Check(result, importer != null && !importer.importAnimation && !importer.addCollider
            && !importer.importCameras && !importer.importLights && !importer.importBlendShapes
            && importer.preserveHierarchy && Mathf.Approximately(importer.globalScale, 1f),
            "Importer preserves hierarchy/metre scale; no animation, colliders, cameras, lights or blend shapes");
        var preview = new PreviewRenderUtility();
        var materials = new Dictionary<string, Material>();
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(asset);
            preview.AddSingleGO(instance);
            var root = instance.transform;
            Check(result, root.localPosition.sqrMagnitude < 0.000001f
                && Quaternion.Angle(root.localRotation, Quaternion.identity) < 0.1f
                && Vector3.Distance(root.localScale, Vector3.one) < 0.0001f, "Root has zero translation/rotation and unit scale");
            var nodes = instance.GetComponentsInChildren<Transform>(true);
            var muzzle = nodes.SingleOrDefault(t => t.name == "Muzzle");
            var grip = nodes.SingleOrDefault(t => t.name == "GripAnchor");
            Check(result, muzzle != null && grip != null, "Unique Muzzle and GripAnchor exist");
            result.muzzle = root.InverseTransformPoint(muzzle.position);
            result.muzzleForward = root.InverseTransformDirection(muzzle.forward);
            result.grip = root.InverseTransformPoint(grip.position);
            Check(result, Vector3.Distance(result.muzzle, new Vector3(0f, 0.040f, 0.210f)) < 0.001f
                && Vector3.Dot(result.muzzleForward, Vector3.forward) > 0.999f, "Muzzle position matches handoff and forward is +Z");
            Check(result, Vector3.Distance(result.grip, new Vector3(0f, -0.035f, -0.042f)) < 0.001f, "GripAnchor matches handoff");
            Check(result, nodes.Count(t => t.name == "ProjectionWindow" && t.GetComponent<MeshFilter>() != null) == 1,
                "ProjectionWindow is a separate mesh");
            Check(result, instance.GetComponentsInChildren<Component>(true).All(c => c is Transform
                || c is MeshFilter || c is MeshRenderer), "Only static meshes and empty transforms are present");

            var points = new List<Vector3>();
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            result.meshCount = filters.Length;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                Check(result, mesh != null && mesh.vertexCount > 0, "Mesh exists: " + filter.name);
                // Editor-only access works for non-readable imported models outside PlayMode.
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var indices = mesh.triangles;
                bool valid = normals.Length == vertices.Length && indices.Length % 3 == 0;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    valid &= Finite(vertices[i]) && normals.Length > i;
                    if (normals.Length > i) valid &= Finite(normals[i]) && normals[i].sqrMagnitude > 0.1f;
                    points.Add(filter.transform.TransformPoint(vertices[i]));
                }
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    bool inRange = a >= 0 && b >= 0 && c >= 0 && a < vertices.Length && b < vertices.Length && c < vertices.Length;
                    valid &= inRange;
                    if (inRange) valid &= Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude > 1e-18f;
                }
                var renderer = filter.GetComponent<MeshRenderer>();
                Check(result, valid && renderer != null && renderer.sharedMaterials.Length == mesh.subMeshCount
                    && renderer.sharedMaterials.All(m => m != null), "Finite geometry/normals, nondegenerate triangles, valid slots: " + filter.name);
                result.vertices += vertices.Length;
                result.triangles += indices.Length / 3;
            }
            Check(result, points.Count > 0 && result.triangles <= 30000, "Within the 30,000 triangle validation budget");
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            result.size = bounds.size;
            Check(result, Vector3.Distance(result.size, new Vector3(0.093914f, 0.206950f, rearScreen ? 0.324500f : 0.318850f)) < 0.001f,
                "Metre dimensions match delivery: X width, Y height, Z length");
            Check(result, result.muzzle.z > bounds.max.z - 0.002f, "Muzzle lies at or beyond front lens");
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            result.materials = renderers.SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct().OrderBy(n => n).ToArray();
            Check(result, RequiredMaterials.All(n => result.materials.Contains(n)) && result.materials.Length == (rearScreen ? 8 : 7),
                "Distinct body/lens/emission/display slots are preserved");
            result.rearDisplayMeshes = filters.Count(f => f.name.StartsWith("RearDisplay_", StringComparison.Ordinal));
            Check(result, result.rearDisplayMeshes == (rearScreen ? 5 : 0)
                && (!rearScreen || result.materials.Contains("PG_RearDisplayCyan")), "Rear variant has five display meshes and separate emission slot");
            foreach (var renderer in renderers)
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => PreviewMaterial(m.name, materials)).ToArray();
            ConfigurePreview(preview);
            root.position = PreviewOffset;
            foreach (float fov in new[] { 60f, 75f })
            {
                var camera = preview.camera;
                camera.fieldOfView = fov;
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var view = new ViewReport { verticalFov = fov, nearestDepth = float.PositiveInfinity,
                    viewportMin = Vector2.one * float.PositiveInfinity, viewportMax = Vector2.one * float.NegativeInfinity,
                    preview = Path.Combine(output, name + "-first-person-" + (int)fov + ".png") };
                foreach (var point in points)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(point + PreviewOffset);
                    view.viewportMin = Vector2.Min(view.viewportMin, new Vector2(viewport.x, viewport.y));
                    view.viewportMax = Vector2.Max(view.viewportMax, new Vector2(viewport.x, viewport.y));
                    view.nearestDepth = Mathf.Min(view.nearestDepth, viewport.z);
                }
                result.views.Add(view);
                Check(result, view.nearestDepth > camera.nearClipPlane + 0.01f, fov + " FOV: geometry clears near clip");
                Check(result, view.viewportMin.x > 0.54f && view.viewportMax.x < 0.95f
                    && view.viewportMax.y > 0.15f && view.viewportMax.y < 0.55f && view.viewportMin.y > -0.2f,
                    fov + " FOV: lower-right composition clears central aim region");
                Render(preview, view.preview);
            }
            root.position = Vector3.zero;
            preview.camera.fieldOfView = 40f;
            preview.camera.transform.position = new Vector3(0.40f, 0.20f, -0.55f);
            preview.camera.transform.LookAt(bounds.center);
            Render(preview, Path.Combine(output, name + "-rear-three-quarter.png"));
            Debug.Log("[ProjectionGunModel] " + path + " triangles=" + result.triangles + " size=" + result.size.ToString("F6"));
        }
        finally
        {
            preview.Cleanup();
            if (instance != null) Object.DestroyImmediate(instance);
            foreach (var material in materials.Values) Object.DestroyImmediate(material);
        }
    }

    internal static void ConfigurePreview(PreviewRenderUtility preview)
    {
        preview.camera.nearClipPlane = 0.05f;
        preview.camera.farClipPlane = 10f;
        preview.camera.aspect = (float)Width / Height;
        preview.camera.clearFlags = CameraClearFlags.SolidColor;
        preview.camera.backgroundColor = new Color(0.055f, 0.075f, 0.11f);
        preview.ambientColor = new Color(0.32f, 0.35f, 0.40f);
        preview.lights[0].intensity = 1.25f;
        preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
        preview.lights[1].intensity = 0.8f;
        preview.lights[1].transform.rotation = Quaternion.Euler(330f, 140f, 0f);
    }

    internal static void Render(PreviewRenderUtility preview, string path)
    {
        preview.BeginStaticPreview(new Rect(0, 0, Width, Height));
        // Warm up the preview pipeline before capturing its first material draw.
        preview.Render(true, false);
        preview.Render(true, false);
        Texture2D texture = preview.EndStaticPreview();
        try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
    }

    private static Material PreviewMaterial(string name, Dictionary<string, Material> cache)
    {
        if (cache.TryGetValue(name, out var material)) return material;
        material = TrapPlacementProjectionGunAuthoring.CreateMaterial(name);
        material.name = name + " (Preview Only)";
        material.hideFlags = HideFlags.HideAndDontSave;
        cache.Add(name, material);
        return material;
    }

    private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
        && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

    private static void Check(ModelReport report, bool passed, string message)
    {
        report.checks.Add((passed ? "PASS " : "FAIL ") + message);
        if (!passed) throw new InvalidOperationException(report.asset + ": " + message);
    }
}
