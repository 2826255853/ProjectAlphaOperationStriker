using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Stage-five acceptance: persistent references, repeatable generation and actual prefab previews.</summary>
public static class TrapPlacementProjectionGunPrefabValidation
{
    [Serializable] private sealed class Report
    {
        public string status = "RUNNING", unityVersion, prefab, error;
        public string scope = "Stage five: generated assets, stable references and isolated previews; no PlayMode acceptance.";
        public Vector3 firstPersonOffset = TrapPlacementProjectionGunAuthoring.FirstPersonOffset;
        public List<string> checks = new List<string>();
        public List<string> previews = new List<string>();
        public List<string> assets = new List<string>();
        public List<string> dependencies = new List<string>();
    }

    [MenuItem("Tools/塔防/投影枪/生成并验收阶段五 Prefab")]
    public static void Run()
    {
        string output = TrapPlacementProjectionGunAuthoring.OutputDirectory;
        Directory.CreateDirectory(output);
        var report = new Report { unityVersion = Application.unityVersion, prefab = TrapPlacementProjectionGunAuthoring.PrefabPath };
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            GameObject prefab = TrapPlacementProjectionGunAuthoring.CreateAssets();
            report.checks.AddRange(ValidateStructure(prefab, true));
            var fingerprints = AssetFingerprints();
            report.assets.AddRange(fingerprints.Select(e => e.Key + " SHA256=" + e.Value));
            string signature = PersistentSignature(prefab);
            prefab = TrapPlacementProjectionGunAuthoring.CreateAssets();
            report.checks.AddRange(ValidateStructure(prefab, true).Select(s => "Repeat: " + s));
            Check(report.checks, signature == PersistentSignature(prefab), "Regeneration preserves all GameObject/component local file IDs");
            var second = AssetFingerprints();
            foreach (var entry in fingerprints)
            {
                Check(report.checks, second.TryGetValue(entry.Key, out string hash) && hash == entry.Value,
                    "Regeneration preserves GUID and serialized content: " + entry.Key
                    + (hash == entry.Value ? "" : " before=" + entry.Value + " after=" + hash));
            }
            var dependencies = AssetDatabase.GetDependencies(TrapPlacementProjectionGunAuthoring.PrefabPath, true);
            report.dependencies.AddRange(dependencies);
            Check(report.checks, dependencies.Contains(TrapPlacementProjectionGunAuthoring.ModelPath), "Prefab keeps a dependency on the final FBX");
            Check(report.checks, TrapPlacementProjectionGunAuthoring.MaterialNames.All(n => dependencies.Contains(TrapPlacementProjectionGunAuthoring.MaterialPath(n))),
                "All eight generated materials are persistent prefab dependencies");
            // URP materials legitimately depend on the package's editor-only MaterialVersion script.
            Check(report.checks, !dependencies.Any(p => p.Contains("KINEMATION")
                || (p.StartsWith("Assets/", StringComparison.Ordinal) && p.EndsWith(".cs"))
                || p.EndsWith(".anim") || p.EndsWith(".controller")), "No project weapon scripts, third-party animation or hand-rig dependencies");
            RenderPrefab(prefab, output, report);
            report.status = "PASS";
            Debug.Log("PROJECTION_GUN_STAGE5_PASS: " + report.checks.Count + " checks; " + output);
        }
        catch (Exception ex) { report.status = "FAIL"; report.error = ex.ToString(); throw; }
        finally
        {
            ShaderUtil.allowAsyncCompilation = previousAsync;
            File.WriteAllText(Path.Combine(output, "validation-report.json"), JsonUtility.ToJson(report, true));
        }
    }

    public static List<string> ValidateStructure(GameObject root, bool persistent)
    {
        var checks = new List<string>();
        Check(checks, root != null, "Prefab root exists");
        Check(checks, root.name == "TrapPlacementProjectionGun" && root.activeSelf
            && Near(root.transform.localPosition, Vector3.zero) && Near(root.transform.localScale, Vector3.one)
            && Quaternion.Angle(root.transform.localRotation, Quaternion.identity) < 0.01f, "Active identity root");
        Transform mount = root.transform.Find(TrapPlacementProjectionGunAuthoring.MountName);
        Check(checks, root.transform.childCount == 1 && mount != null, "Exactly one first-person mount");
        Check(checks, Near(mount.localPosition, TrapPlacementProjectionGunAuthoring.FirstPersonOffset)
            && Near(mount.localScale, Vector3.one) && Quaternion.Angle(mount.localRotation, Quaternion.identity) < 0.01f,
            "First-person mount owns the verified offset and zero axis correction");
        Transform model = mount.Find("Model");
        Check(checks, mount.childCount == 1 && model != null && Near(model.localPosition, Vector3.zero)
            && Near(model.localScale, Vector3.one) && Quaternion.Angle(model.localRotation, Quaternion.identity) < 0.01f,
            "Exactly one unscaled model at mount origin");
        Check(checks, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject) == TrapPlacementProjectionGunAuthoring.ModelPath,
            "Model remains a nested instance of the tail-screen FBX");
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        Check(checks, nodes.All(t => t.gameObject.layer == TrapPlacementProjectionGunAuthoring.VisualLayer
            && !t.gameObject.isStatic && t.gameObject.activeSelf), "All nodes active, dynamic and on Ignore Raycast layer");
        Check(checks, root.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is MeshFilter || c is MeshRenderer),
            "Static visual components only; no physics, scripts, Animator, IK or constraints");
        foreach (string name in new[] { "Muzzle", "GripAnchor", "ProjectionWindow" })
            Check(checks, nodes.Count(t => t.name == name) == 1, "Unique required node: " + name);
        Transform muzzle = nodes.Single(t => t.name == "Muzzle");
        Check(checks, Near(model.InverseTransformPoint(muzzle.position), new Vector3(0, 0.040f, 0.210f))
            && Vector3.Dot(model.InverseTransformDirection(muzzle.forward), Vector3.forward) > 0.999f, "Muzzle position and +Z forward");
        Check(checks, Near(model.InverseTransformPoint(nodes.Single(t => t.name == "GripAnchor").position), new Vector3(0, -0.035f, -0.042f)),
            "GripAnchor position");
        Check(checks, nodes.Single(t => t.name == "ProjectionWindow").GetComponent<MeshRenderer>() != null,
            "ProjectionWindow exposes a separate renderer");
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        Check(checks, renderers.Length == 83 && nodes.Count(t => t.name.StartsWith("RearDisplay_", StringComparison.Ordinal)
            && t.GetComponent<MeshFilter>() != null) == 5, "83 source meshes including five rear-screen meshes");
        var materialNames = new HashSet<string>();
        int triangles = 0;
        foreach (MeshRenderer renderer in renderers)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            Check(checks, mesh != null && renderer.enabled && renderer.sharedMaterials.Length == mesh.subMeshCount
                && renderer.sharedMaterials.All(m => m != null), "Valid renderer/mesh/slots: " + renderer.name);
            triangles += mesh.triangles.Length / 3;
            Check(checks, renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows
                && renderer.motionVectorGenerationMode == MotionVectorGenerationMode.ForceNoMotion, "First-person shadow/motion settings: " + renderer.name);
            foreach (Material material in renderer.sharedMaterials)
            {
                materialNames.Add(material.name);
                Check(checks, material.shader != null && material.shader.name == "Universal Render Pipeline/Lit"
                    && AssetDatabase.GetAssetPath(material) == TrapPlacementProjectionGunAuthoring.MaterialPath(material.name),
                    "Persistent URP material: " + material.name);
            }
        }
        Check(checks, triangles == 17852, "Geometry matches accepted 17,852-triangle model");
        Check(checks, materialNames.SetEquals(TrapPlacementProjectionGunAuthoring.MaterialNames), "Exactly eight material slots");
        foreach (string name in materialNames)
        {
            var actual = AssetDatabase.LoadAssetAtPath<Material>(TrapPlacementProjectionGunAuthoring.MaterialPath(name));
            var expected = TrapPlacementProjectionGunAuthoring.CreateMaterial(name);
            try
            {
                Check(checks, actual != null && actual.GetColor("_BaseColor") == expected.GetColor("_BaseColor")
                    && Mathf.Approximately(actual.GetFloat("_Metallic"), expected.GetFloat("_Metallic"))
                    && Mathf.Approximately(actual.GetFloat("_Smoothness"), expected.GetFloat("_Smoothness"))
                    && actual.GetColor("_EmissionColor") == expected.GetColor("_EmissionColor")
                    && actual.IsKeywordEnabled("_EMISSION") == expected.IsKeywordEnabled("_EMISSION"),
                    "Palette, surface and emission match handoff: " + name);
            }
            finally { Object.DestroyImmediate(expected); }
        }
        if (persistent)
            Check(checks, EditorUtility.IsPersistent(root) && AssetDatabase.GetAssetPath(root) == TrapPlacementProjectionGunAuthoring.PrefabPath,
                "Saved prefab at the documented Resources path");
        return checks;
    }

    private static void RenderPrefab(GameObject prefab, string output, Report report)
    {
        var preview = new PreviewRenderUtility();
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab);
            preview.AddSingleGO(instance);
            TrapPlacementProjectionGunModelValidation.ConfigurePreview(preview);
            var points = instance.GetComponentsInChildren<MeshFilter>(true)
                .SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v))).ToArray();
            foreach (float fov in new[] { 60f, 75f })
            {
                preview.camera.fieldOfView = fov;
                preview.camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var projected = points.Select(p => preview.camera.WorldToViewportPoint(p)).ToArray();
                Check(report.checks, projected.Min(p => p.z) > 0.06f && projected.Min(p => p.x) > 0.54f
                    && projected.Max(p => p.x) < 0.95f && projected.Max(p => p.y) < 0.55f
                    && projected.Max(p => p.y) > 0.15f && projected.Min(p => p.y) > -0.2f,
                    fov + " FOV: final prefab clears near clip and central aim region");
                string path = Path.Combine(output, "prefab-first-person-" + (int)fov + ".png");
                TrapPlacementProjectionGunModelValidation.Render(preview, path);
                report.previews.Add(path);
            }
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            Check(report.checks, Vector3.Distance(bounds.size, new Vector3(0.093914f, 0.206950f, 0.324500f)) < 0.001f,
                "Final prefab preserves metre dimensions");
            preview.camera.fieldOfView = 40f;
            preview.camera.transform.position = bounds.center + new Vector3(0.40f, 0.20f, -0.55f);
            preview.camera.transform.LookAt(bounds.center);
            string rear = Path.Combine(output, "prefab-rear-three-quarter.png");
            TrapPlacementProjectionGunModelValidation.Render(preview, rear);
            report.previews.Add(rear);
        }
        finally { preview.Cleanup(); if (instance != null) Object.DestroyImmediate(instance); }
    }

    private static Dictionary<string, string> AssetFingerprints()
    {
        var paths = TrapPlacementProjectionGunAuthoring.MaterialNames.Select(TrapPlacementProjectionGunAuthoring.MaterialPath)
            .Append(TrapPlacementProjectionGunAuthoring.PrefabPath).Append(TrapPlacementProjectionGunAuthoring.ModelPath).ToArray();
        return paths.Concat(paths.Select(p => p + ".meta")).ToDictionary(p => p, Hash);
    }

    private static string PersistentSignature(GameObject prefab)
    {
        return string.Join("\n", prefab.GetComponentsInChildren<Transform>(true).SelectMany(t =>
            t.GetComponents<Component>().Cast<Object>().Prepend(t.gameObject)).Select(o =>
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long localId))
                    throw new InvalidOperationException("Missing persistent ID for " + o.name);
                return o.GetType().Name + " " + o.name + " " + guid + " " + localId;
            }));
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.0001f;
    private static void Check(List<string> checks, bool passed, string message)
    {
        checks.Add((passed ? "PASS " : "FAIL ") + message);
        if (!passed) throw new InvalidOperationException("Projection gun prefab: " + message);
    }
}
