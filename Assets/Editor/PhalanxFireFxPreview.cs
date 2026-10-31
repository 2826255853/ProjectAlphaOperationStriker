using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Renders the Phalanx firing feedback (muzzle flash / tracer streak / ejected brass) to PNG
/// so the effect can be judged without entering PlayMode. Drives exactly the same public API
/// the weapon uses at runtime (<see cref="PhalanxFireFx.PlayShot"/> + <see cref="PhalanxFireFx.Tick"/>,
/// <see cref="SentryShellEjector.Eject"/> + <see cref="SentryShellEjector.Step"/>).
///
/// Menu: Tools/塔防/预览密集阵开火特效
/// Batch (needs graphics): -executeMethod PhalanxFireFxPreview.RenderPreviewBatch -WithGraphics
/// </summary>
public static class PhalanxFireFxPreview
{
    private const int Width = 960;
    private const int Height = 600;
    private const string PrefabPath = "Assets/Resources/PhalanxVisual.prefab";

    [MenuItem("Tools/塔防/预览密集阵开火特效")]
    public static void RenderPreview()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("请先生成密集阵预制体：" + PrefabPath);
            return;
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "phalanx-fx-preview");
        Directory.CreateDirectory(outputDirectory);

        GameObject instance = Object.Instantiate(prefab);
        PhalanxFireFx fx = instance.GetComponent<PhalanxFireFx>();
        SentryShellEjector ejector = instance.GetComponent<SentryShellEjector>();
        if (fx == null || ejector == null)
        {
            Debug.LogError("PhalanxVisual.prefab 缺少 PhalanxFireFx / SentryShellEjector，请重新生成预制体。");
            Object.DestroyImmediate(instance);
            return;
        }

        var cameraObject = new GameObject("Preview Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        camera.transform.position = new Vector3(2.9f, 2.05f, -2.75f);
        camera.transform.LookAt(new Vector3(0f, 1.15f, 1.15f));

        var lightObject = new GameObject("Preview Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        lightObject.transform.rotation = Quaternion.Euler(45f, 35f, 0f);

        RenderTexture renderTexture = new RenderTexture(Width, Height, 24);
        camera.targetTexture = renderTexture;

        try
        {
            fx.EnsureInitialized();
            Vector3 muzzle = fx.Muzzle != null ? fx.Muzzle.position : new Vector3(0f, 1.06f, 1.5f);

            // 1) A single round: the flash is lit for a few hundredths of a second.
            fx.PlayShot(muzzle, muzzle + Vector3.forward * 10f);
            fx.Tick(0.005f);
            Render(camera, renderTexture, outputDirectory, "01-muzzle-flash");
            fx.ResetFx();

            // 2) A short burst: several streaks at different stages of flight.
            for (int i = 0; i < 6; i++)
            {
                Vector3 aim = muzzle + Quaternion.Euler(0f, -9f + i * 3.5f, 0f) * Vector3.forward * 11f;
                fx.PlayShot(muzzle, aim);
                fx.Tick(0.035f);
            }
            Render(camera, renderTexture, outputDirectory, "02-tracers");
            fx.ResetFx();

            // 3) Ejected brass: a dozen casings early in their arc, close to the gun.
            for (int i = 0; i < 12; i++)
            {
                ejector.Eject();
                ejector.Step(0.035f);
            }
            Render(camera, renderTexture, outputDirectory, "03-casings");
            LogCasingViewport(camera, ejector);
            ejector.RecycleAll();
        }
        finally
        {
            camera.targetTexture = null;
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(instance);
        }

        Debug.Log("PHALANX_FX_PREVIEW " + outputDirectory);
    }

    public static void RenderPreviewBatch() => RenderPreview();

    private static void LogCasingViewport(Camera camera, SentryShellEjector ejector)
    {
        var positions = new List<Vector3>();
        ejector.CollectActiveCasingPositions(positions);
        int onScreen = 0;
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 viewport = camera.WorldToViewportPoint(positions[i]);
            bool visible = viewport.z > 0f && viewport.x > 0f && viewport.x < 1f
                && viewport.y > 0f && viewport.y < 1f;
            if (visible) onScreen++;
            if (i < 4)
                builder.Append($" [{i}]={positions[i]:F2}->vp({viewport.x:F2},{viewport.y:F2},z{viewport.z:F1}){(visible ? " IN" : " OUT")}");
        }
        Debug.Log($"PHALANX_FX_PREVIEW_CASINGS total={positions.Count} onScreen={onScreen}{builder}");
    }

    private static void Render(Camera camera, RenderTexture renderTexture, string outputDirectory, string name)
    {
        camera.Render();
        RenderTexture.active = renderTexture;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, name + ".png"), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        RenderTexture.active = null;
    }
}
