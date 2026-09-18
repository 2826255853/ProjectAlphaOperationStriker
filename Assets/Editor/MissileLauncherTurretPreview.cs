using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode visual verification for the missile launcher mount.
/// Renders the authored prefab at several yaw/pitch combinations to PNG files so
/// the rotation limits can be confirmed by eye instead of trusting numbers.
/// </summary>
public static class MissileLauncherTurretPreview
{
    private const int Width = 640;
    private const int Height = 480;

    private sealed class Shot
    {
        public string Name;
        public float Azimuth;
        public float Elevation;
    }

    [MenuItem("Tools/塔防/预览导弹发射器旋转")]
    public static void RenderPreview()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MissileLauncherTurretAuthoring.LauncherPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到预制体：{MissileLauncherTurretAuthoring.LauncherPrefabPath}，请先生成。");
            return;
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "launcher-preview");
        Directory.CreateDirectory(outputDirectory);

        var shots = new[]
        {
            new Shot { Name = "01-yaw-000-pitch-00", Azimuth = 0f, Elevation = 0f },
            new Shot { Name = "02-yaw-090-pitch-00", Azimuth = 90f, Elevation = 0f },
            new Shot { Name = "03-yaw-180-pitch-00", Azimuth = 180f, Elevation = 0f },
            new Shot { Name = "04-yaw-270-pitch-00", Azimuth = 270f, Elevation = 0f },
            new Shot { Name = "05-yaw-000-pitch-75", Azimuth = 0f, Elevation = 75f },
            new Shot { Name = "06-clamped-below-horizon", Azimuth = 45f, Elevation = -40f }
        };

        var root = (GameObject)Object.Instantiate(prefab);
        root.transform.position = Vector3.zero;
        try
        {
            MissileLauncherTurret turret = root.GetComponent<MissileLauncherTurret>();
            if (turret == null)
            {
                Debug.LogError("预制体上没有 MissileLauncherTurret 组件。");
                return;
            }
            turret.enabled = false;

            var cameraObject = new GameObject("Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            camera.transform.position = new Vector3(8.5f, 5f, -8.5f);
            camera.transform.LookAt(new Vector3(0f, 1.3f, 0f));

            var lightObject = new GameObject("Preview Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightObject.transform.rotation = Quaternion.Euler(45f, 35f, 0f);

            RenderTexture renderTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = renderTexture;

            for (int i = 0; i < shots.Length; i++)
            {
                Shot shot = shots[i];
                Vector3 horizontal = Quaternion.AngleAxis(shot.Azimuth, Vector3.up) * root.transform.forward;
                Vector3 direction = Quaternion.AngleAxis(shot.Elevation, Vector3.Cross(horizontal, Vector3.up)) * horizontal;
                turret.SnapAimAt(turret.PitchPivot.position + direction * 10f);

                camera.Render();
                RenderTexture.active = renderTexture;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(outputDirectory, shot.Name + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = null;

                Debug.Log($"预览 {shot.Name}：请求方位 {shot.Azimuth:F0}° / 请求俯仰 {shot.Elevation:F0}°" +
                          $" → 实际方位 {turret.CurrentYawDegrees:F1}° / 实际俯仰 {turret.CurrentElevationDegrees:F1}°");
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Debug.Log($"预览图已输出：{outputDirectory}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
