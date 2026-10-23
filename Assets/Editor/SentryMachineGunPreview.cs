using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Stage-six visual acceptance for the sentry machine gun art prefab. It renders the
/// authored prefab (not the raw FBX) from the four Blender angles, then scrubs the
/// runtime behaviours - yaw, recoil, muzzle flash and the casing arc - and captures
/// each frame, so the procedural animation can be reviewed as stills.
///
/// Everything is driven through the same public API the game uses
/// (<see cref="SentryFireFeedback.Tick"/>, <see cref="SentryShellEjector.Step"/>),
/// so a frame that looks wrong here is wrong in play mode too.
///
/// Menu: Tools/塔防/哨戒机枪/渲染阶段 6 预览
/// Batch: -executeMethod SentryMachineGunPreview.RenderPreviewBatch -WithGraphics
/// (without -WithGraphics every frame renders black; the method refuses to run.)
/// </summary>
public static class SentryMachineGunPreview
{
    public const string ArtPrefabPath = "Assets/Resources/SentryMachineGun.prefab";

    private const int Width = 800;
    private const int Height = 600;
    private const float FieldOfView = 38f;

    private sealed class Shot
    {
        public string Name;
        public Vector3 Direction;
        public float DistanceScale = 1f;
        public Vector3? FocusOverride;
        public Func<Vector3> FocusProvider;
        public Action Setup;
    }

    /// <summary>Stage-six frames land next to the stage-three material previews.</summary>
    public static string PreviewDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "陷阱素材文件夹", "SentryMachineGun", "previews", "unity-step6");

    [MenuItem("Tools/塔防/哨戒机枪/渲染阶段 6 预览")]
    public static void RenderPreviewMenu() => RenderPreview();

    /// <summary>Batch entry point; logs the pass marker only after every frame is written.</summary>
    public static void RenderPreviewBatch()
    {
        int frames = RenderPreview();
        Debug.Log("SENTRY_MG_STEP6_PREVIEW_PASS: " + frames + " frames -> " + PreviewDirectory);
    }

    /// <summary>Renders every stage-six still and returns how many were written.</summary>
    public static int RenderPreview()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException(
                "SentryMachineGunPreview needs a graphics device; run Unity with -WithGraphics.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefabPath);
        if (prefab == null)
            throw new InvalidOperationException(
                "Missing " + ArtPrefabPath + "; run SentryMachineGunAuthoring.CreateAssetsBatch first.");

        Directory.CreateDirectory(PreviewDirectory);

        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var cameraObject = new GameObject("Sentry Stage 6 Preview Camera");
        var keyObject = new GameObject("Sentry Stage 6 Preview Key");
        var fillObject = new GameObject("Sentry Stage 6 Preview Fill");
        var rimObject = new GameObject("Sentry Stage 6 Preview Rim");
        var target = new RenderTexture(Width, Height, 24);
        var written = new List<string>();
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.02f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            camera.targetTexture = target;

            // Same neutral key + two weak fills as the stage-three previews: the metals are
            // 0.7-0.9 metallic, so a strong warm key washes the whole gun orange.
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.85f;
            key.color = Color.white;
            keyObject.transform.rotation = Quaternion.Euler(40f, 35f, 0f);

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

            // Capture the rest pose before anything moves, and measure the framing once so
            // later frames (casings in flight, flash meshes) cannot change the camera.
            var feedback = root.GetComponent<SentryFireFeedback>();
            var ejector = root.GetComponent<SentryShellEjector>();
            if (feedback == null || ejector == null)
                throw new InvalidOperationException(
                    ArtPrefabPath + " is missing SentryFireFeedback / SentryShellEjector.");

            feedback.EnsureInitialized();
            Transform pivot = FindNode(root.transform, "Aim Pivot");
            Transform muzzle = FindNode(root.transform, "Muzzle");
            if (pivot == null || muzzle == null)
                throw new InvalidOperationException("Art prefab is missing Aim Pivot / Muzzle.");

            Bounds bounds = UnionBounds(root);
            Vector3 focus = bounds.center;
            float radius = Mathf.Max(0.1f, bounds.extents.magnitude);

            // The first camera.Render() of a batch session runs before URP finishes warming up
            // its pipeline state and comes out flat and mis-shaded (stage three hit this).
            for (int warmUp = 0; warmUp < 2; warmUp++) camera.Render();

            var shots = new List<Shot>();

            // --- authored angles (the gun points +Z at rest, matching the contract) ---
            shots.Add(new Shot
            {
                Name = "unity_step6_01_three_quarter",
                Direction = new Vector3(0.95f, 0.55f, 1f),
            });
            shots.Add(new Shot
            {
                Name = "unity_step6_02_side",
                Direction = new Vector3(1f, 0.22f, 0f),
            });
            shots.Add(new Shot
            {
                Name = "unity_step6_03_front",
                Direction = new Vector3(0f, 0.2f, 1f),
            });
            shots.Add(new Shot
            {
                Name = "unity_step6_04_top",
                Direction = new Vector3(0.02f, 1f, 0.06f),
            });

            // --- yaw sweep: the pivot is the only node that turns ---
            for (int i = 0; i < 3; i++)
            {
                int yaw = 90 * (i + 1);
                shots.Add(new Shot
                {
                    Name = "unity_step6_0" + (5 + i) + "_yaw" + yaw,
                    Direction = new Vector3(0.95f, 0.55f, 1f),
                    Setup = () => pivot.localRotation = Quaternion.Euler(0f, yaw, 0f),
                });
            }

            // --- recoil frames: kick-in peak, spring return, settled ---
            // Framed close on the muzzle: 0.04 m of travel is invisible at the wide angle.
            float duration = feedback.SingleShotDuration;
            float[] recoilProgress = { 0.18f, 0.5f, 1f };
            string[] recoilNames = { "peak", "return", "settled" };
            for (int i = 0; i < recoilProgress.Length; i++)
            {
                // Each capture restarts the shot, so sample its absolute age.
                float sampleTime = recoilProgress[i] * duration;
                string name = "unity_step6_08_recoil_" + recoilNames[i];
                shots.Add(new Shot
                {
                    Name = name,
                    Direction = new Vector3(1f, 0.3f, 0.55f),
                    DistanceScale = 0.42f,
                    FocusOverride = muzzle.position,
                    Setup = () =>
                    {
                        pivot.localRotation = Quaternion.identity;
                        feedback.ResetToRest();
                        feedback.PlayShot();
                        feedback.Tick(sampleTime);
                    },
                });
            }

            // --- muzzle flash: 0.045 s window, so tick well inside it ---
            shots.Add(new Shot
            {
                Name = "unity_step6_11_muzzle_flash",
                Direction = new Vector3(0.8f, 0.35f, 1f),
                DistanceScale = 0.5f,
                FocusOverride = muzzle.position,
                Setup = () =>
                {
                    feedback.ResetToRest();
                    feedback.PlayShot();
                    feedback.Tick(duration * 0.1f);
                },
            });

            // --- casing arc: three frames of one throw from the ejection port ---
            // The casing is only 0.055 m long, so these frames stay closer than the wide
            // angles - but the whole arc has to fit. The first attempt framed at 0.4x and
            // lost the brass: at 0.06 s it was still buried in the receiver, and by 0.5 s it
            // had dropped out of the bottom of the frame, so two of the three PNGs came out
            // byte-identical. Camera now sits behind-right (the port is on the right flank)
            // and the three samples (0.08 / 0.25 / 0.42 s) all stay inside the frame.
            // ResetToRest first so the muzzle flash from the frame above cannot leak in.
            Transform port = FindNode(root.transform, "Ejection Port");
            Vector3 casingFocus = (port != null ? port.position : focus) + Vector3.up * 0.30f;
            float casingElapsed = 0f;
            float[] casingTimes = { 0.08f, 0.25f, 0.42f };
            for (int i = 0; i < casingTimes.Length; i++)
            {
                // Copy the loop counter: a for-loop variable is shared by every closure,
                // so the three frames would otherwise all behave like the last one.
                int frame = i;
                float step = casingTimes[i] - casingElapsed;
                casingElapsed = casingTimes[i];
                shots.Add(new Shot
                {
                    Name = "unity_step6_" + (12 + i) + "_casing_"
                           + casingTimes[i].ToString("0.00", CultureInfo.InvariantCulture) + "s",
                    Direction = new Vector3(0.5f, 0.45f, -0.85f),
                    DistanceScale = 0.62f,
                    FocusOverride = casingFocus,
                    Setup = () =>
                    {
                        if (frame == 0)
                        {
                            feedback.ResetToRest();
                            ejector.RecycleAll();
                            ejector.Eject();
                        }
                        // Step on every frame, the first one included: ejecting alone leaves the
                        // brass at t=0, so a frame labelled 0.08 s would really show the port.
                        ejector.Step(step);
                        LogCasing(ejector, frame, casingTimes[frame]);
                    },
                });
            }

            foreach (Shot shot in shots)
            {
                shot.Setup?.Invoke();
                Capture(camera, focus, radius, shot, target, written);
            }

            WriteManifest(written);
            Debug.Log("[SentryMachineGunPreview] " + written.Count + " frames written to " + PreviewDirectory);
            return written.Count;
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(keyObject);
            Object.DestroyImmediate(fillObject);
            Object.DestroyImmediate(rimObject);
            Object.DestroyImmediate(target);
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Prints the live casing so a frame can be checked numerically, not just by eye: a
    /// casing that is outside the frustum still renders "fine" as an empty picture.
    /// </summary>
    private static void LogCasing(SentryShellEjector ejector, int frame, float time)
    {
        var text = new StringBuilder();
        text.Append("[SentryMachineGunPreview] casing frame ").Append(frame)
            .Append(" t=").Append(time.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" active=").Append(ejector.ActiveCasingCount);
        Transform poolRoot = FindNode(ejector.transform, "Shell Casings");
        if (poolRoot != null)
        {
            for (int i = 0; i < poolRoot.childCount; i++)
            {
                Transform child = poolRoot.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                text.Append(" | ").Append(child.name).Append(" @ ")
                    .Append(child.position.ToString("F3", CultureInfo.InvariantCulture));
            }
        }
        Debug.Log(text.ToString());
    }

    private static void Capture(Camera camera, Vector3 focus, float radius, Shot shot,
        RenderTexture target, List<string> written)
    {
        Vector3 focusPoint = shot.FocusProvider != null
            ? shot.FocusProvider()
            : shot.FocusOverride ?? focus;
        float distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f * shot.DistanceScale;
        camera.transform.position = focusPoint + shot.Direction.normalized * distance;
        camera.transform.LookAt(focusPoint);
        camera.Render();

        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            string path = Path.Combine(PreviewDirectory, shot.Name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            written.Add(path);
            Debug.Log("[SentryMachineGunPreview] " + shot.Name + " -> " + path);
        }
        finally
        {
            RenderTexture.active = null;
            Object.DestroyImmediate(texture);
        }
    }

    private static void WriteManifest(List<string> frames)
    {
        var json = new StringBuilder();
        json.Append("{\n  \"prefab\": \"").Append(ArtPrefabPath).Append("\",\n");
        json.Append("  \"directory\": \"").Append(Escape(PreviewDirectory)).Append("\",\n");
        json.Append("  \"frames\": [\n");
        for (int i = 0; i < frames.Count; i++)
        {
            var info = new FileInfo(frames[i]);
            json.Append("    { \"name\": \"").Append(Escape(Path.GetFileNameWithoutExtension(frames[i])))
                .Append("\", \"bytes\": ").Append(info.Exists ? info.Length : 0).Append(" }");
            json.Append(i + 1 < frames.Count ? ",\n" : "\n");
        }
        json.Append("  ]\n}\n");
        File.WriteAllText(Path.Combine(PreviewDirectory, "preview-manifest.json"), json.ToString());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static Bounds UnionBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Transform FindNode(Transform root, string nodeName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == nodeName) return child;
        return null;
    }
}
