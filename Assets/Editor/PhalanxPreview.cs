using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode visual verification for the Phalanx CIWS trap prefab.
/// Renders the authored prefab from several angles (plus one aiming pose driven
/// through PhalanxTurretRig) so the model, materials and rig can be checked by
/// eye instead of trusting numbers.
///
/// Batch entry (graphics required):
///   -executeMethod PhalanxPreview.RenderPreviewBatch -WithGraphics
/// </summary>
public static class PhalanxPreview
{
    private const int Width = 900;
    private const int Height = 720;

    [MenuItem("Tools/塔防/预览密集阵近防炮")]
    public static void RenderPreview()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhalanxTrapAuthoring.PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到预制体：{PhalanxTrapAuthoring.PrefabPath}，请先生成。");
            return;
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "phalanx-preview");
        Directory.CreateDirectory(outputDirectory);

        GameObject root = Object.Instantiate(prefab);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Camera camera = null;
        GameObject lightObject = null;
        GameObject fillObject = null;
        RenderTexture renderTexture = null;
        try
        {
            PhalanxTurretRig rig = root.GetComponent<PhalanxTurretRig>();
            if (rig == null)
            {
                Debug.LogError("预制体上没有 PhalanxTurretRig 组件。");
                return;
            }

            var cameraObject = new GameObject("Preview Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.14f);

            lightObject = new GameObject("Preview Key Light");
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.97f, 0.92f);
            lightObject.transform.rotation = Quaternion.Euler(42f, 34f, 0f);

            fillObject = new GameObject("Preview Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.72f, 0.82f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(20f, 210f, 0f);

            renderTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = renderTexture;

            RenderShot(camera, renderTexture, outputDirectory, "01-three-quarter",
                new Vector3(3.1f, 2.05f, 3.7f), new Vector3(0f, 1.02f, 0f));

            RenderShot(camera, renderTexture, outputDirectory, "02-side",
                new Vector3(4.4f, 1.25f, 0.15f), new Vector3(0f, 1.00f, 0f));

            RenderShot(camera, renderTexture, outputDirectory, "03-front",
                new Vector3(0.25f, 1.30f, 4.3f), new Vector3(0f, 1.02f, 0f));

            // Aiming pose: traverse 35 degrees and elevate toward a high target.
            rig.ResetToRestPose();
            rig.SetBarrelSpinActive(true);
            Vector3 target = Quaternion.Euler(0f, 35f, 0f) * new Vector3(0f, 5.5f, 9f);
            for (int i = 0; i < 90; i++)
            {
                rig.AimAt(target, 1f / 30f);
                rig.AdvanceVisuals(1f / 30f);
            }
            Debug.Log($"PHALANX_PREVIEW aim yaw={rig.YawDegrees:F1} pitch={rig.PitchDegrees:F1} "
                + $"aimed={rig.IsAimedAt(target, 5f)} spin={rig.CurrentSpinRate:F0} deg/s");

            RenderShot(camera, renderTexture, outputDirectory, "04-aiming",
                new Vector3(3.4f, 2.35f, 3.4f), new Vector3(0f, 1.15f, 0f));

            Debug.Log($"PHALANX_PREVIEW_DONE {outputDirectory}");
        }
        finally
        {
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
            if (lightObject != null) Object.DestroyImmediate(lightObject);
            if (fillObject != null) Object.DestroyImmediate(fillObject);
            if (renderTexture != null)
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(renderTexture);
            }
            Object.DestroyImmediate(root);
        }
    }

    public static void RenderPreviewBatch()
    {
        RenderPreview();
    }

    private static void RenderShot(Camera camera, RenderTexture renderTexture, string directory,
        string name, Vector3 position, Vector3 lookAt)
    {
        camera.transform.position = position;
        camera.transform.LookAt(lookAt);
        camera.Render();
        RenderTexture.active = renderTexture;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        RenderTexture.active = null;
    }
}
