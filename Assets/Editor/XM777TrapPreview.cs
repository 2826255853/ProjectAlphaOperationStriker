using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode visual verification for the XM777 artillery mount.
///
/// Renders the authored prefab at several traverse angles through
/// <see cref="XM777Trap.AimAt"/> (the same math the gameplay loop uses) so the
/// turret can be confirmed to swing horizontally instead of rolling. The log
/// records requested and achieved azimuth/elevation at each horizontal range.
/// </summary>
public static class XM777TrapPreview
{
    private const int Width = 720;
    private const int Height = 540;

    /// <summary>Where the PNGs land by default (trap staging folder on the desktop).</summary>
    public const string DefaultOutputDirectory =
        @"C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\distance-elevation\preview";

    private static readonly float[] Azimuths = { 0f, 45f, 90f, 180f, 270f };
    private static readonly float[] Distances = { 10f, 55f, 100f };

    [MenuItem("Tools/塔防/预览 XM777 转向")]
    public static void RenderRotationPreview()
    {
        RenderRotationPreview(DefaultOutputDirectory);
    }

    public static void RenderRotationPreview(string outputDirectory)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XM777TrapAuthoring.PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到预制体：{XM777TrapAuthoring.PrefabPath}，请先生成。");
            return;
        }
        XM777TrapAuthoring.ValidatePresentationBatch();

        if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = DefaultOutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        GameObject root = (GameObject)Object.Instantiate(prefab);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var report = new StringBuilder();
        try
        {
            XM777Trap trap = root.GetComponent<XM777Trap>();
            if (trap == null)
            {
                Debug.LogError("预制体上没有 XM777Trap 组件。");
                return;
            }

            // Tool-only instance: nothing should return fire during a render.
            trap.enabled = false;

            Camera camera = CreateCamera(new Vector3(9f, 6.5f, -9f), new Vector3(0f, 1.6f, 0f));
            GameObject lightObject = new GameObject("Preview Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(45f, 35f, 0f);

            RenderTexture renderTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = renderTexture;

            Vector3 up = trap.YawAxisWorld;
            Vector3 restAzimuth = Vector3.ProjectOnPlane(trap.BarrelDirectionWorld, up).normalized;
            report.AppendLine($"XM777 range/elevation preview: yaw axis = {Format(up)}; " +
                "distance is horizontal from the base; 10 m = 0°, 55 m = 22.5°, 100 m = 45°.");

            bool passed = true;
            foreach (float distance in Distances)
            foreach (float requested in Azimuths)
            {
                Vector3 direction = Quaternion.AngleAxis(requested, up) * restAzimuth;
                Vector3 point = root.transform.position + direction * distance;
                // Settle the existing muzzle/pivot parallax through production aiming.
                for (int step = 0; step < 6; step++) trap.AimAt(point, 10f);

                Vector3 barrel = Vector3.ProjectOnPlane(trap.BarrelDirectionWorld, up);
                float achieved = Vector3.SignedAngle(restAzimuth, barrel.normalized, up);
                float elevation = trap.BarrelElevationDegrees;
                float expected = XM777Trap.ElevationForDistance(distance);
                Vector3 targetDirection = Vector3.ProjectOnPlane(point - trap.Muzzle.position, up);
                if (Mathf.Abs(elevation - expected) > 0.5f || Vector3.Angle(barrel, targetDirection) > 3f)
                    passed = false;

                string name = $"range-{distance:000}-yaw-{Mathf.RoundToInt(requested):D3}";
                Render(camera, renderTexture, Path.Combine(outputDirectory, name + ".png"));
                report.AppendLine($"  {name}: requested {requested:F0}° -> achieved {achieved:F1}°, " +
                                  $"barrel elevation {elevation:F2}° (expected {expected:F2}°)");
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(lightObject);

            report.AppendLine(passed
                ? "XM777_RANGE_PREVIEW_PASS: traverse aligned and range elevation correct in all 15 poses."
                : "XM777_RANGE_PREVIEW_FAIL: traverse or range elevation outside tolerance.");
            File.WriteAllText(Path.Combine(outputDirectory, "report.txt"), report.ToString(), Encoding.UTF8);
            Debug.Log(report.ToString() + $"预览图已输出：{outputDirectory}");
            if (!passed) throw new System.InvalidOperationException("XM777 range/elevation preview failed; see report.txt.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Camera CreateCamera(Vector3 position, Vector3 lookAt)
    {
        var cameraObject = new GameObject("Preview Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 40f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
        camera.transform.position = position;
        camera.transform.LookAt(lookAt);
        return camera;
    }

    private static void Render(Camera camera, RenderTexture target, string path)
    {
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        RenderTexture.active = null;
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }
}
