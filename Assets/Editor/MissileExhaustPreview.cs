using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode visual verification for the launcher's launch plumes.
///
/// Renders the authored prefab twice - cold at rest and burning at full strength
/// during a salvo - from behind/side so the nozzle fit, the plume direction and the
/// scale can be judged by eye instead of trusting the numbers printed by
/// <see cref="MissileLauncherTurretAuthoring"/>.
/// </summary>
public static class MissileExhaustPreview
{
    private const int Width = 720;
    private const int Height = 540;

    [MenuItem("Tools/塔防/预览导弹发射器尾焰")]
    public static void RenderPreview()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MissileLauncherTurretAuthoring.LauncherPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"找不到预制体：{MissileLauncherTurretAuthoring.LauncherPrefabPath}，请先生成。");
            return;
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "alpha-striker-unity", "exhaust-preview");
        Directory.CreateDirectory(outputDirectory);

        var root = (GameObject)Object.Instantiate(prefab);
        root.transform.position = Vector3.zero;
        try
        {
            MissileLauncherTurret turret = root.GetComponent<MissileLauncherTurret>();
            MissileExhaustFx fx = root.GetComponent<MissileExhaustFx>();
            if (turret != null) turret.enabled = false;
            if (fx == null)
            {
                Debug.LogError("预制体上没有 MissileExhaustFx 组件，尾焰没法预览。");
                return;
            }
            // Stop the weapon from actually shooting during the preview.
            var weapon = root.GetComponent<MissileLauncherWeapon>();
            if (weapon != null) weapon.enabled = false;

            var cameraObject = new GameObject("Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);

            var lightObject = new GameObject("Preview Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, 200f, 0f);

            var keyLightObject = new GameObject("Preview Key Light");
            Light key = keyLightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.7f;
            keyLightObject.transform.rotation = Quaternion.Euler(25f, 40f, 0f);

            RenderTexture renderTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = renderTexture;

            var shots = new[]
            {
                new { Name = "01-idle", Intensity = 0f },
                new { Name = "02-firing", Intensity = 1f },
                new { Name = "03-firing-half", Intensity = 0.5f }
            };

            for (int i = 0; i < shots.Length; i++)
            {
                fx.SetPreviewIntensity(shots[i].Intensity);
                FrameCamera(camera, root, fx, turret);

                camera.Render();
                RenderTexture.active = renderTexture;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(outputDirectory, shots[i].Name + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = null;

                Debug.Log($"预览 {shots[i].Name}：强度 {shots[i].Intensity:F2}，" +
                          $"尾焰世界包围盒 {PlumeBounds(root).size}");
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(keyLightObject);
            Debug.Log($"尾焰预览图已输出：{outputDirectory}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>Places the camera behind and slightly to the side of the plume stack.</summary>
    private static void FrameCamera(Camera camera, GameObject root, MissileExhaustFx fx, MissileLauncherTurret turret)
    {
        Bounds bounds = PlumeBounds(root);
        Vector3 center = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 1.5f);

        // Look along the plume from behind: the barrels point one way, the plumes the other.
        Vector3 plumeDirection = PlumeDirection(root, turret);
        Vector3 side = Vector3.Cross(Vector3.up, plumeDirection).normalized;
        if (side.sqrMagnitude < 0.5f) side = Vector3.right;

        camera.transform.position = center + plumeDirection * (radius * 1.1f) + side * (radius * 0.9f) + Vector3.up * (radius * 0.45f);
        camera.transform.LookAt(center);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200f;
    }

    private static Vector3 PlumeDirection(GameObject root, MissileLauncherTurret turret)
    {
        List<Transform> plumes = Plumes(root);
        if (plumes.Count >= 2)
        {
            Vector3 axis = plumes[1].position - plumes[0].position;
            axis = Vector3.ProjectOnPlane(axis, Vector3.up);
            if (axis.sqrMagnitude > 0.0001f) axis = Vector3.zero; // rails are side by side: not the plume axis
        }
        if (plumes.Count > 0 && turret != null && turret.HasRig)
        {
            Vector3 barrel = turret.RestBarrelDirectionWorld.normalized;
            return -Vector3.ProjectOnPlane(barrel, Vector3.up).normalized;
        }
        return Vector3.forward;
    }

    private static List<Transform> Plumes(GameObject root)
    {
        var plumes = new List<Transform>();
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name.StartsWith("Exhaust Plume", System.StringComparison.Ordinal) && all[i].parent != null)
                plumes.Add(all[i]);
        return plumes;
    }

    private static Bounds PlumeBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled) continue;
            if (!any) { bounds = renderers[i].bounds; any = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }
}
