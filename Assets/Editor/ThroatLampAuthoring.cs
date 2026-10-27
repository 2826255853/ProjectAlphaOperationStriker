using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates the URP material library and gameplay prefab for the 扼喉之灯
/// (Throat Lamp) ceiling trap from the Blender FBX.
///
/// Contract: the trap hangs along the prefab's local +Z, which is exactly the
/// ceiling trap grid convention (grid rotation maps local +Z onto world down).
/// Gameplay is regenerated here so rebuilding the art cannot disconnect attacks.
/// </summary>
public static class ThroatLampAuthoring
{
    public const string ModelPath = "Assets/Models/ThroatLamp/ThroatLamp.fbx";
    public const string MaterialDir = "Assets/Models/ThroatLamp/Materials";
    public const string PrefabPath = "Assets/Prefabs/ThroatLamp.prefab";
    public const string DefinitionPath = "Assets/Resources/ThroatLamp.asset";
    public const string ProjectilePath = "Assets/Prefabs/ThroatLampPlasma.prefab";
    public const string GameplayOutput = @"C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\gameplay-validation";

    public const string RootName = "ThroatLamp";
    public const string CeilingGroupName = "Ceiling Mount";
    public const string YawGroupName = "Yaw Pivot";
    public const string PitchGroupName = "Pitch Pivot";
    public const string NozzleMarkerName = "Nozzle";

    // Distances from the ceiling plane, metres. They mirror build_throat_lamp.py.
    public const float YawPivotDepth = 0.075f;
    public const float PitchPivotDepth = 0.270f;
    public const float NozzleDepth = 0.400f;

    private struct MatSpec
    {
        public Color Base;
        public float Metallic;
        public float Smoothness;
        public Color Emission;
        public float EmissionIntensity;

        public MatSpec(float r, float g, float b, float metallic, float smoothness,
            float er = 0f, float eg = 0f, float eb = 0f, float intensity = 0f)
        {
            Base = new Color(r, g, b, 1f);
            Metallic = metallic;
            Smoothness = smoothness;
            Emission = new Color(er, eg, eb, 1f);
            EmissionIntensity = intensity;
        }
    }

    private static readonly Dictionary<string, MatSpec> Palette = new Dictionary<string, MatSpec>
    {
        { "TL_DarkMetal", new MatSpec(0.115f, 0.120f, 0.128f, 0.25f, 0.58f) },
        { "TL_Armor", new MatSpec(0.245f, 0.255f, 0.272f, 0.20f, 0.50f) },
        { "TL_YokeMetal", new MatSpec(0.480f, 0.500f, 0.520f, 0.30f, 0.62f) },
        { "TL_Rubber", new MatSpec(0.020f, 0.020f, 0.022f, 0.00f, 0.28f) },
        { "TL_Copper", new MatSpec(0.520f, 0.268f, 0.118f, 0.60f, 0.66f) },
        { "TL_PlasmaRing", new MatSpec(0.120f, 0.024f, 0.010f, 0.00f, 0.65f, 1.0f, 0.22f, 0.04f, 2.0f) },
        { "TL_PlasmaCore", new MatSpec(0.600f, 0.500f, 0.400f, 0.00f, 0.80f, 1.0f, 0.88f, 0.68f, 5.5f) },
        { "TL_Lens", new MatSpec(0.020f, 0.012f, 0.008f, 0.20f, 0.88f, 0.55f, 0.09f, 0.02f, 0.25f) },
    };

    private static readonly string[] CeilingPrefixes = { "Mount_" };
    private static readonly string[] YawPrefixes = { "Yaw_", "Yoke_" };
    private static readonly string[] PitchPrefixes = { "Head_" };

    private const string UrpLitShader = "Universal Render Pipeline/Lit";

    // ------------------------------------------------------------- animation
    // The art pass only ships presentation: a self-playing idle -> charge ->
    // discharge cycle on the two pivots plus animated plasma emission. No
    // gameplay script drives it yet, so the cycle loops on exit times alone.
    public const string AnimationDir = "Assets/Models/ThroatLamp/Animations";
    public const string IdleClipPath = AnimationDir + "/TL_IdleScan.anim";
    public const string ChargeClipPath = AnimationDir + "/TL_Charge.anim";
    public const string DischargeClipPath = AnimationDir + "/TL_Discharge.anim";
    public const string ControllerPath = AnimationDir + "/ThroatLamp.controller";

    public const float IdleSeconds = 6f;
    public const float ChargeSeconds = 1.4f;
    public const float DischargeSeconds = 0.9f;

    // Clip binding paths. They mirror the rig hierarchy above, so renaming a
    // rig node must change these strings too (the probe asserts they resolve).
    private const string YawBindPath = YawGroupName;
    private const string PitchBindPath = YawGroupName + "/" + PitchGroupName;
    private const string CoreRendererPath = PitchBindPath + "/Head_Plasma_Core";
    private const string RingRendererPath = PitchBindPath + "/Head_Containment_Ring";
    private const string LensRendererPath = PitchBindPath + "/Head_Lens";

    private const string ChargeTrigger = "Charge";
    private const string FireTrigger = "Fire";

    private static readonly Color CoreEmission = new Color(1f, 0.88f, 0.68f);
    private static readonly Color RingEmission = new Color(1f, 0.22f, 0.04f);
    private static readonly Color LensEmission = new Color(0.55f, 0.09f, 0.02f);

    // ------------------------------------------------------------------ menus
    [MenuItem("Tools/塔防/扼喉之灯/配置导入并生成材质与预制体")]
    public static void CreateAssetsMenu()
    {
        if (CreateAssetsInternal(out string summary))
            Debug.Log("THROAT_LAMP_ASSETS_PASS: " + summary);
        else
            EditorUtility.DisplayDialog("扼喉之灯", summary, "确定");
    }

    [MenuItem("Tools/塔防/扼喉之灯/探针：预制体结构与材质")]
    public static void ProbeMenu()
    {
        ProbePrefabBatch();
        EditorUtility.DisplayDialog("扼喉之灯", "探针结果已写入日志与临时目录。", "确定");
    }

    [MenuItem("Tools/塔防/扼喉之灯/渲染预览（需图形）")]
    public static void RenderPreviewMenu()
    {
        RenderPreviewBatch();
        EditorUtility.DisplayDialog("扼喉之灯", "预览已输出到桌面素材目录。", "确定");
    }

    public const string PreviewDir =
        @"C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\unity-previews";

    public static void RenderPreviewBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException("缺少预制体：" + PrefabPath + "（先生成材质与预制体）");
        }
        Directory.CreateDirectory(PreviewDir);

        var written = new List<string>();
        var root = new GameObject("Throat Lamp Preview Rig");
        RenderTexture target = null;
        GameObject camObject = null, keyObject = null, fillObject = null, rimObject = null;
        GameObject ceiling = null;
        AmbientMode previousAmbientMode = RenderSettings.ambientMode;
        Color previousAmbientLight = RenderSettings.ambientLight;
        try
        {
            // Metallic URP materials only reflect the environment; with a solid
            // clear color and no probes they read almost black. Flat ambient is
            // the cheapest way to make the preview match the Blender look.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            lamp.transform.localPosition = Vector3.zero;

            // A ceiling slab makes the mounting height readable, like the sheet.
            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Preview Ceiling";
            UnityEngine.Object.DestroyImmediate(ceiling.GetComponent<Collider>());
            ceiling.transform.SetParent(root.transform, false);
            ceiling.transform.localPosition = new Vector3(0f, 0f, -0.035f);
            ceiling.transform.localScale = new Vector3(3.2f, 3.2f, 0.07f);
            var slabMat = new Material(Shader.Find(UrpLitShader));
            slabMat.SetColor("_BaseColor", new Color(0.68f, 0.66f, 0.63f));
            slabMat.SetFloat("_Smoothness", 0.15f);
            ceiling.GetComponent<MeshRenderer>().sharedMaterial = slabMat;

            target = new RenderTexture(1024, 1024, 24) { antiAliasing = 4 };
            camObject = new GameObject("Throat Lamp Preview Camera");
            Camera camera = camObject.AddComponent<Camera>();
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.74f, 0.73f, 0.70f);
            camera.targetTexture = target;

            keyObject = MakeDirectional("Preview Key", new Vector3(-0.45f, 1f, -0.85f),
                new Color(1f, 0.96f, 0.90f), 1.30f, root.transform);
            fillObject = MakeDirectional("Preview Fill", new Vector3(0.95f, 0.15f, -0.35f),
                new Color(0.86f, 0.92f, 1f), 0.45f, root.transform);
            rimObject = MakeDirectional("Preview Rim", new Vector3(0.15f, -0.75f, -0.9f),
                new Color(0.90f, 0.94f, 1f), 0.55f, root.transform);

            // The first Render() of a batch session happens before URP is warm
            // and yields a washed out frame; drop two frames first.
            for (int warmUp = 0; warmUp < 2; warmUp++) camera.Render();

            var shots = new[]
            {
                new PreviewShot("01_front", new Vector3(0f, 1.75f, 0.20f), new Vector3(0f, 0f, 0.20f), Vector3.back),
                new PreviewShot("02_side", new Vector3(1.75f, 0f, 0.20f), new Vector3(0f, 0f, 0.20f), Vector3.back),
                new PreviewShot("03_underside", new Vector3(0f, 0f, 1.95f), new Vector3(0f, 0f, 0.30f), Vector3.up),
                new PreviewShot("04_three_quarter", new Vector3(1.00f, 1.15f, 1.20f), new Vector3(0f, 0f, 0.18f), Vector3.back),
                new PreviewShot("05_nozzle_detail", new Vector3(0.34f, 0.42f, 0.86f), new Vector3(0f, 0f, 0.40f), Vector3.back),
            };

            foreach (PreviewShot shot in shots)
            {
                camera.transform.position = shot.Eye;
                camera.transform.LookAt(shot.Target, shot.Up);
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                texture.Apply();
                string path = Path.Combine(PreviewDir, shot.Name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(texture);
                written.Add(path);
                Debug.Log("THROAT_LAMP_PREVIEW_SHOT: " + path);
            }
        }
        finally
        {
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            if (camObject != null) UnityEngine.Object.DestroyImmediate(camObject);
            if (keyObject != null) UnityEngine.Object.DestroyImmediate(keyObject);
            if (fillObject != null) UnityEngine.Object.DestroyImmediate(fillObject);
            if (rimObject != null) UnityEngine.Object.DestroyImmediate(rimObject);
            if (ceiling != null) UnityEngine.Object.DestroyImmediate(ceiling);
            if (target != null)
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        if (written.Count != 5)
            throw new InvalidOperationException("预览帧数不正确：" + written.Count);
        Debug.Log("THROAT_LAMP_PREVIEW_PASS: " + written.Count + " frames -> " + PreviewDir);
    }

    private readonly struct PreviewShot
    {
        public readonly string Name;
        public readonly Vector3 Eye;
        public readonly Vector3 Target;
        public readonly Vector3 Up;

        public PreviewShot(string name, Vector3 eye, Vector3 target, Vector3 up)
        {
            Name = name;
            Eye = eye;
            Target = target;
            Up = up;
        }
    }

    private static GameObject MakeDirectional(string name, Vector3 euler, Color color,
        float intensity, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.LookRotation(euler.normalized, Vector3.up);
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.Soft;
        return go;
    }

    // ------------------------------------------------ animation pose preview
    public const string AnimationPreviewSubdir = "anim";

    [MenuItem("Tools/塔防/扼喉之灯/渲染动画姿态预览（需图形）")]
    public static void RenderAnimationPreviewMenu()
    {
        RenderAnimationPreviewBatch();
        EditorUtility.DisplayDialog("扼喉之灯", "动画姿态预览已输出到桌面素材目录。", "确定");
    }

    /// <summary>
    /// Renders the poses the clips produce (yaw sweep left/right + charge flash)
    /// so the animated axes and the emission ramp can be checked without
    /// entering Play Mode. AnimationMode is avoided on purpose: material curves
    /// are not reliably sampled there, so the poses are applied by hand with the
    /// exact same values the clips write.
    /// </summary>
    public static void RenderAnimationPreviewBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException("缺少预制体：" + PrefabPath + "（先生成材质与预制体）");
        }
        string dir = Path.Combine(PreviewDir, AnimationPreviewSubdir);
        Directory.CreateDirectory(dir);

        var written = new List<string>();
        var created = new List<Material>();
        var root = new GameObject("Throat Lamp Animation Preview Rig");
        RenderTexture target = null;
        GameObject camObject = null, keyObject = null, fillObject = null, rimObject = null;
        GameObject ceiling = null;
        AmbientMode previousAmbientMode = RenderSettings.ambientMode;
        Color previousAmbientLight = RenderSettings.ambientLight;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);

            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            lamp.transform.localPosition = Vector3.zero;

            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Preview Ceiling";
            UnityEngine.Object.DestroyImmediate(ceiling.GetComponent<Collider>());
            ceiling.transform.SetParent(root.transform, false);
            ceiling.transform.localPosition = new Vector3(0f, 0f, -0.035f);
            ceiling.transform.localScale = new Vector3(3.2f, 3.2f, 0.07f);
            var slabMat = new Material(Shader.Find(UrpLitShader));
            slabMat.SetColor("_BaseColor", new Color(0.68f, 0.66f, 0.63f));
            slabMat.SetFloat("_Smoothness", 0.15f);
            ceiling.GetComponent<MeshRenderer>().sharedMaterial = slabMat;
            created.Add(slabMat);

            target = new RenderTexture(1024, 1024, 24) { antiAliasing = 4 };
            camObject = new GameObject("Throat Lamp Animation Preview Camera");
            Camera camera = camObject.AddComponent<Camera>();
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.74f, 0.73f, 0.70f);
            camera.targetTexture = target;
            camera.transform.position = new Vector3(1.00f, 1.15f, 1.20f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0.18f), Vector3.back);

            keyObject = MakeDirectional("Preview Key", new Vector3(-0.45f, 1f, -0.85f),
                new Color(1f, 0.96f, 0.90f), 1.30f, root.transform);
            fillObject = MakeDirectional("Preview Fill", new Vector3(0.95f, 0.15f, -0.35f),
                new Color(0.86f, 0.92f, 1f), 0.45f, root.transform);
            rimObject = MakeDirectional("Preview Rim", new Vector3(0.15f, -0.75f, -0.9f),
                new Color(0.90f, 0.94f, 1f), 0.55f, root.transform);

            for (int warmUp = 0; warmUp < 2; warmUp++) camera.Render();

            var poses = new[]
            {
                new AnimationPose("01_yaw_left", 26f, 4f, 1f),
                new AnimationPose("02_yaw_right", -26f, 7f, 1f),
                new AnimationPose("03_charge_flash", 0f, 0f, 2.6f),
            };

            foreach (AnimationPose pose in poses)
            {
                ApplyPose(lamp, pose, created);
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                texture.Apply();
                string path = Path.Combine(dir, pose.Name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(texture);
                written.Add(path);
                Debug.Log("THROAT_LAMP_ANIM_SHOT: " + path);
            }
        }
        finally
        {
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            if (camObject != null) UnityEngine.Object.DestroyImmediate(camObject);
            if (keyObject != null) UnityEngine.Object.DestroyImmediate(keyObject);
            if (fillObject != null) UnityEngine.Object.DestroyImmediate(fillObject);
            if (rimObject != null) UnityEngine.Object.DestroyImmediate(rimObject);
            if (ceiling != null) UnityEngine.Object.DestroyImmediate(ceiling);
            foreach (Material material in created)
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
            if (target != null)
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        if (written.Count != 3)
            throw new InvalidOperationException("动画姿态预览帧数不正确：" + written.Count);
        Debug.Log("THROAT_LAMP_ANIM_PREVIEW_PASS: " + written.Count + " frames -> " + dir);
    }

    private readonly struct AnimationPose
    {
        public readonly string Name;
        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float EmissionScale;

        public AnimationPose(string name, float yaw, float pitch, float emissionScale)
        {
            Name = name;
            Yaw = yaw;
            Pitch = pitch;
            EmissionScale = emissionScale;
        }
    }

    private static void ApplyPose(GameObject lamp, AnimationPose pose, List<Material> created)
    {
        Transform yaw = lamp.transform.Find(YawGroupName);
        Transform pitch = yaw != null ? yaw.Find(PitchGroupName) : null;
        if (yaw != null) yaw.localRotation = Quaternion.Euler(0f, pose.Yaw, 0f);
        if (pitch != null) pitch.localRotation = Quaternion.Euler(pose.Pitch, 0f, 0f);

        ApplyEmission(lamp.transform, CoreRendererPath, CoreEmission, 5.5f * pose.EmissionScale,
            created);
        ApplyEmission(lamp.transform, RingRendererPath, RingEmission, 2.0f * pose.EmissionScale,
            created);
        ApplyEmission(lamp.transform, LensRendererPath, LensEmission, 0.25f * pose.EmissionScale,
            created);

        Transform lens = lamp.transform.Find(LensRendererPath);
        if (lens != null) lens.localScale = Vector3.one * (pose.EmissionScale > 1.5f ? 1.10f : 1f);
    }

    private static void ApplyEmission(Transform root, string path, Color baseColor, float intensity,
        List<Material> created)
    {
        Transform node = root.Find(path);
        var renderer = node != null ? node.GetComponent<MeshRenderer>() : null;
        if (renderer == null || renderer.sharedMaterial == null) return;
        var instance = new Material(renderer.sharedMaterial);
        instance.SetColor("_EmissionColor", baseColor * intensity);
        instance.EnableKeyword("_EMISSION");
        created.Add(instance);
        renderer.sharedMaterial = instance;
    }

    public static void CreateAssetsBatch()
    {
        if (!CreateAssetsInternal(out string summary))
            throw new InvalidOperationException(summary);
        Debug.Log("THROAT_LAMP_ASSETS_PASS: " + summary);
    }

    public static void ProbePrefabBatch()
    {
        if (!ProbePrefabInternal(out string summary))
            throw new InvalidOperationException(summary);
        Debug.Log("THROAT_LAMP_PROBE_PASS: " + summary);
    }

    [MenuItem("Tools/塔防/扼喉之灯/预览玩法瞄准与等离子体（需图形）")]
    public static void RenderGameplayPreviewBatch()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Gameplay preview requires -WithGraphics.");
        Directory.CreateDirectory(GameplayOutput);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("ThroatLamp gameplay preview");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        RenderTexture target = null;
        Material ceilingMaterial = null;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        Color ambient = RenderSettings.ambientLight;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.4f, 0.42f, 0.47f);
            GameObject lamp = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), root.transform);
            lamp.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var trap = lamp.GetComponent<ThroatLampTrap>();
            var animator = lamp.GetComponent<Animator>();
            animator.Rebind();
            animator.Play("Charge", 0, 0.8f);
            animator.Update(0f);
            trap.AimAt(new Vector3(0.8f, -1.2f, 0.4f), 10f);

            GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.SetParent(root.transform, false);
            ceiling.transform.localPosition = new Vector3(0, 0.045f, 0);
            ceiling.transform.localScale = new Vector3(1.5f, 0.08f, 1.5f);
            UnityEngine.Object.DestroyImmediate(ceiling.GetComponent<Collider>());
            ceilingMaterial = new Material(Shader.Find(UrpLitShader));
            ceilingMaterial.SetColor("_BaseColor", new Color(0.3f, 0.35f, 0.42f));
            ceiling.GetComponent<Renderer>().sharedMaterial = ceilingMaterial;
            MakeDirectional("Gameplay key", new Vector3(-0.4f, 0.8f, -0.5f), Color.white, 1.8f, root.transform);
            MakeDirectional("Gameplay fill", new Vector3(0.8f, 0.2f, 0.5f), new Color(0.65f, 0.8f, 1f), 1.2f, root.transform);

            GameObject cameraObject = new GameObject("Gameplay camera");
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.08f, 0.12f);
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.01f;
            camera.transform.position = new Vector3(1.05f, -0.95f, 1.35f);
            camera.transform.LookAt(new Vector3(0.08f, -0.25f, 0.03f));
            target = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            for (int i = 0; i < 2; i++) camera.Render();
            CaptureGameplay(camera, target, "aim-and-charge.png");

            var projectile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath), root.transform);
            projectile.transform.SetPositionAndRotation(trap.Nozzle.position + trap.Nozzle.forward * 0.35f, trap.Nozzle.rotation);
            TrailRenderer trail = projectile.GetComponent<TrailRenderer>();
            for (int i = 0; i < 8; i++) trail.AddPosition(trap.Nozzle.position + trap.Nozzle.forward * (0.12f + i * 0.033f));
            CaptureGameplay(camera, target, "plasma-shot.png");
            Debug.Log("THROAT_LAMP_GAMEPLAY_PREVIEW_PASS: " + GameplayOutput);
        }
        finally
        {
            RenderTexture.active = null;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(root);
            if (ceilingMaterial != null) UnityEngine.Object.DestroyImmediate(ceilingMaterial);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;
        }
    }

    private static void CaptureGameplay(Camera camera, RenderTexture target, string filename)
    {
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(GameplayOutput, filename), texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = null; }
    }

    // ------------------------------------------------------------ asset build
    private static bool CreateAssetsInternal(out string summary)
    {
        summary = string.Empty;
        if (!File.Exists(ModelPath))
        {
            summary = "找不到模型：" + ModelPath + "。先把 Blender 导出的 ThroatLamp.fbx 复制进工程。";
            return false;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Models/ThroatLamp"))
        {
            summary = "缺少目录 Assets/Models/ThroatLamp。";
            return false;
        }

        // Materials must exist before the importer is configured: the remap
        // table is built from the material assets on disk.
        if (!CreateOrUpdateMaterials(out string materialError))
        {
            summary = materialError;
            return false;
        }
        ConfigureImporter();

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        Shader shader = Shader.Find(UrpLitShader);
        if (shader == null)
        {
            summary = "找不到 URP/Lit 着色器，工程可能未启用 URP。";
            return false;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            summary = "FBX 导入失败：" + ModelPath;
            return false;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        if (instance == null)
        {
            summary = "无法实例化 FBX：" + ModelPath;
            return false;
        }

        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            var root = new GameObject(RootName);
            var ceiling = NewChild(root, CeilingGroupName, Vector3.zero);
            var yaw = NewChild(root, YawGroupName, new Vector3(0f, 0f, YawPivotDepth));
            var pitch = NewChild(yaw, PitchGroupName,
                new Vector3(0f, 0f, PitchPivotDepth - YawPivotDepth));
            var nozzle = NewChild(pitch, NozzleMarkerName,
                new Vector3(0f, 0f, NozzleDepth - PitchPivotDepth));

            _cachedCeiling = ceiling.transform;
            _cachedYaw = yaw.transform;
            _cachedPitch = pitch.transform;

            var unassigned = new List<string>();
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                Transform node = renderer.transform;
                Transform group = GroupFor(node.name);
                if (group == null)
                {
                    unassigned.Add(node.name);
                    continue;
                }
                // Keep the baked FBX placement while moving into the pivot rig.
                node.SetParent(group, true);
            }

            if (unassigned.Count > 0)
            {
                summary = "以下网格没有匹配到层级分组：" + string.Join(", ", unassigned);
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;

            // Animation lives on the root only: the pivots keep their authored
            // rest pose, so a missing controller degrades to the static model.
            if (!BuildAnimationAssets(out string animationError))
            {
                summary = animationError;
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }
            Animator animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (animator.runtimeAnimatorController == null)
            {
                summary = "动画控制器生成后无法加载：" + ControllerPath;
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            WireGameplay(root);
            Bounds installation = MeasureBounds(root.GetComponentsInChildren<MeshRenderer>(true));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);
            if (!ok || saved == null)
            {
                summary = "保存预制体失败：" + PrefabPath;
                return false;
            }
            CreateDefinition(saved, installation);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!ProbePrefabInternal(out string probeSummary))
            {
                summary = "预制体已生成但探针未通过：" + probeSummary;
                return false;
            }
            summary = string.Format("{0} | materials={1} | {2}", PrefabPath, Palette.Count, probeSummary);
            return true;
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static GameObject NewChild(GameObject parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    private static void WireGameplay(GameObject root)
    {
        root.AddComponent<TrapInstance>();
        ThroatLampTrap trap = root.AddComponent<ThroatLampTrap>();
        var serialized = new SerializedObject(trap);
        GameObject previous = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        ThroatLampTrap previousTrap = previous != null ? previous.GetComponent<ThroatLampTrap>() : null;
        if (previousTrap != null)
        {
            var old = new SerializedObject(previousTrap);
            foreach (string field in new[] { "detectionRange", "projectileSpeed", "fireInterval", "chargeSeconds",
                "directDamage", "splashDamage", "splashRadius", "aimSpeedDegrees", "aimToleranceDegrees" })
                serialized.FindProperty(field).floatValue = old.FindProperty(field).floatValue;
        }
        Transform yaw = root.transform.Find(YawGroupName);
        Transform pitch = yaw.Find(PitchGroupName);
        serialized.FindProperty("yawPivot").objectReferenceValue = yaw;
        serialized.FindProperty("pitchPivot").objectReferenceValue = pitch;
        serialized.FindProperty("nozzle").objectReferenceValue = pitch.Find(NozzleMarkerName);
        serialized.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        serialized.FindProperty("projectilePrefab").objectReferenceValue = CreateProjectile();
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static PlasmaProjectile CreateProjectile()
    {
        string path = MaterialDir + "/TL_ProjectileTrail.mat";
        Material trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (trailMaterial == null)
        {
            trailMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(trailMaterial, path);
        }
        trailMaterial.SetColor("_BaseColor", new Color(3f, 1.2f, 0.25f));
        EditorUtility.SetDirty(trailMaterial);
        GameObject root = new GameObject("ThroatLamp Plasma");
        try
        {
            root.AddComponent<PlasmaProjectile>();
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Plasma Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * 0.2f;
            UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
            core.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("TL_PlasmaCore"));
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = 0.15f;
            trail.startWidth = 0.16f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            return PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath).GetComponent<PlasmaProjectile>();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CreateDefinition(GameObject prefab, Bounds bounds)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        bool isNew = definition == null;
        if (isNew)
        {
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("trapId").stringValue = "throat-lamp";
        serialized.FindProperty("displayName").stringValue = "扼喉之灯";
        serialized.FindProperty("prefab").objectReferenceValue = prefab;
        if (isNew) serialized.FindProperty("cost").intValue = 40;
        serialized.FindProperty("footprintWidth").intValue = 2;
        serialized.FindProperty("footprintHeight").intValue = 2;
        serialized.FindProperty("worldFootprint").vector2Value = new Vector2(2f, 2f);
        serialized.FindProperty("localRotation").vector3Value = Vector3.zero;
        serialized.FindProperty("walkableFloorTrap").boolValue = false;
        serialized.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Ceiling;
        serialized.FindProperty("installBoundsCenter").vector3Value = bounds.center;
        serialized.FindProperty("installBoundsSize").vector3Value = bounds.size;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform GroupFor(string objectName)
    {
        // The rig is created after this lookup, so resolve by name instead of
        // holding references: BuildGroups runs in the same call.
        if (CeilingPrefixes.Any(p => objectName.StartsWith(p, StringComparison.Ordinal)))
            return FindRigNode(CeilingGroupName);
        if (YawPrefixes.Any(p => objectName.StartsWith(p, StringComparison.Ordinal)))
            return FindRigNode(YawGroupName);
        if (PitchPrefixes.Any(p => objectName.StartsWith(p, StringComparison.Ordinal)))
            return FindRigNode(PitchGroupName);
        return null;
    }

    private static Transform _cachedCeiling;
    private static Transform _cachedYaw;
    private static Transform _cachedPitch;

    private static Transform FindRigNode(string name)
    {
        switch (name)
        {
            case CeilingGroupName: return _cachedCeiling;
            case YawGroupName: return _cachedYaw;
            case PitchGroupName: return _cachedPitch;
            default: return null;
        }
    }

    private static void ConfigureImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) return;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.addCollider = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.weldVertices = false;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialSearch = ModelImporterMaterialSearch.Local;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

        foreach (KeyValuePair<string, MatSpec> entry in Palette)
        {
            string path = MaterialPath(entry.Key);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key), material);
        }

        importer.SaveAndReimport();
    }

    private static string MaterialPath(string materialName)
    {
        return MaterialDir + "/" + materialName + ".mat";
    }

    private static bool CreateOrUpdateMaterials(out string error)
    {
        error = string.Empty;
        Shader shader = Shader.Find(UrpLitShader);
        if (shader == null)
        {
            error = "找不到 URP/Lit 着色器。";
            return false;
        }
        if (!AssetDatabase.IsValidFolder(MaterialDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Models/ThroatLamp"))
            {
                error = "缺少目录 Assets/Models/ThroatLamp。";
                return false;
            }
            AssetDatabase.CreateFolder("Assets/Models/ThroatLamp", "Materials");
        }

        foreach (KeyValuePair<string, MatSpec> entry in Palette)
        {
            string path = MaterialPath(entry.Key);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = entry.Key };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            MatSpec spec = entry.Value;
            material.SetColor("_BaseColor", spec.Base);
            material.SetFloat("_Metallic", spec.Metallic);
            material.SetFloat("_Smoothness", spec.Smoothness);

            bool emissive = spec.EmissionIntensity > 0f;
            material.SetColor("_EmissionColor", spec.Emission * spec.EmissionIntensity);
            if (emissive) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = emissive
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.None;

            material.SetFloat("_Surface", 0f);   // opaque
            material.SetFloat("_ZWrite", 1f);
            material.renderQueue = (int)RenderQueue.Geometry;

            // Explicit URP surface flags: the emissive parts must keep specular
            // highlights and reflections so the plasma reads as glass/metal and
            // not as flat paint.
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.SetOverrideTag("RenderType", "Opaque");
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }

    // ------------------------------------------------------------ animation
    /// <summary>
    /// Builds the three art clips. Gameplay triggers Charge and Fire;
    /// Discharge is the only state that automatically returns to Idle.
    /// </summary>
    private static bool BuildAnimationAssets(out string error)
    {
        error = string.Empty;
        if (!AssetDatabase.IsValidFolder("Assets/Models/ThroatLamp"))
        {
            error = "缺少目录 Assets/Models/ThroatLamp。";
            return false;
        }
        if (!AssetDatabase.IsValidFolder(AnimationDir))
        {
            AssetDatabase.CreateFolder("Assets/Models/ThroatLamp", "Animations");
        }

        AnimationClip idle = BuildIdleClip();
        AnimationClip charge = BuildChargeClip();
        AnimationClip discharge = BuildDischargeClip();
        if (idle == null || charge == null || discharge == null)
        {
            error = "生成动画片段失败。";
            return false;
        }

        // Rebuild from scratch so repeated runs never stack transitions.
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (controller == null)
        {
            error = "生成 AnimatorController 失败：" + ControllerPath;
            return false;
        }

        controller.AddParameter(ChargeTrigger, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(FireTrigger, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        machine.name = "Throat Lamp";
        AnimatorState idleState = machine.AddState("Idle", new Vector3(280f, 120f, 0f));
        AnimatorState chargeState = machine.AddState("Charge", new Vector3(540f, 120f, 0f));
        AnimatorState dischargeState = machine.AddState("Discharge", new Vector3(800f, 120f, 0f));
        idleState.motion = idle;
        chargeState.motion = charge;
        dischargeState.motion = discharge;
        machine.defaultState = idleState;

        // Attacks are driven by gameplay; only the cosmetic discharge exits itself.
        AddExitTransition(dischargeState, idleState, 0.25f);
        AddAnyStateTransition(machine, chargeState, ChargeTrigger);
        AddAnyStateTransition(machine, dischargeState, FireTrigger);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        AssetDatabase.ImportAsset(IdleClipPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(ChargeClipPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(DischargeClipPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        return true;
    }

    private static void AddExitTransition(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }

    private static void AddAnyStateTransition(AnimatorStateMachine machine, AnimatorState to,
        string trigger)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.06f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static AnimationClip BuildIdleClip()
    {
        AnimationClip clip = ResetClip(IdleClipPath, "TL_IdleScan", IdleSeconds, true);
        if (clip == null) return null;

        // Slow yaw sweep + gentle nod, one loop every IdleSeconds.
        AddCurve(clip, YawBindPath, "localEulerAnglesRaw.y", Sampled(IdleSeconds,
            t => 26f * Mathf.Sin(2f * Mathf.PI * t / IdleSeconds)));
        AddCurve(clip, PitchBindPath, "localEulerAnglesRaw.x", Sampled(IdleSeconds,
            t => 4f + 3.2f * Mathf.Sin(2f * Mathf.PI * t / 3f)));

        AddEmission(clip, CoreRendererPath, CoreEmission, IdleSeconds,
            t => 5.5f * (1f + 0.10f * Mathf.Sin(2f * Mathf.PI * t / 1.5f)));
        AddEmission(clip, RingRendererPath, RingEmission, IdleSeconds,
            t => 2.0f * (1f + 0.22f * Mathf.Sin(2f * Mathf.PI * t / 2f)));
        AddEmission(clip, LensRendererPath, LensEmission, IdleSeconds,
            t => 0.25f * (1f + 0.30f * Mathf.Sin(2f * Mathf.PI * t / 1.5f)));
        AddLensScale(clip, IdleSeconds,
            t => 1f + 0.02f * Mathf.Sin(2f * Mathf.PI * t / 1.5f));
        return clip;
    }

    private static AnimationClip BuildChargeClip()
    {
        AnimationClip clip = ResetClip(ChargeClipPath, "TL_Charge", ChargeSeconds, false);
        if (clip == null) return null;

        // Recentre the head and ramp the plasma up; no discharge yet.
        AddCurve(clip, YawBindPath, "localEulerAnglesRaw.y",
            Sampled(ChargeSeconds, t => Mathf.Lerp(0f, 0f, Ease(t / ChargeSeconds))));
        AddCurve(clip, PitchBindPath, "localEulerAnglesRaw.x", Sampled(ChargeSeconds,
            t => Mathf.Lerp(4f, 0f, Ease(t / ChargeSeconds))));
        AddCurve(clip, PitchBindPath, "m_LocalPosition.z", Sampled(ChargeSeconds,
            t => PitchPivotDepth - YawPivotDepth));

        AddEmission(clip, CoreRendererPath, CoreEmission, ChargeSeconds,
            t => Mathf.Lerp(5.5f, 22f, Ease(t / ChargeSeconds)));
        AddEmission(clip, RingRendererPath, RingEmission, ChargeSeconds,
            t => Mathf.Lerp(2.0f, 5.5f, Ease(t / ChargeSeconds)));
        AddEmission(clip, LensRendererPath, LensEmission, ChargeSeconds,
            t => Mathf.Lerp(0.25f, 0.95f, Ease(t / ChargeSeconds)));
        AddLensScale(clip, ChargeSeconds, t => Mathf.Lerp(1f, 1.12f, Ease(t / ChargeSeconds)));
        return clip;
    }

    private static AnimationClip BuildDischargeClip()
    {
        AnimationClip clip = ResetClip(DischargeClipPath, "TL_Discharge", DischargeSeconds, false);
        if (clip == null) return null;

        // Muzzle kick along the throat axis plus a hot plasma flash.
        AddCurve(clip, PitchBindPath, "localEulerAnglesRaw.x", Sampled(DischargeSeconds,
            t => 4f - 7f * Mathf.Exp(-t * 7f) * Mathf.Clamp01(1f - t / 0.5f)));
        AddCurve(clip, PitchBindPath, "m_LocalPosition.z", Sampled(DischargeSeconds,
            t => PitchPivotDepth - YawPivotDepth - 0.012f
                * Mathf.Exp(-t * 6f) * Mathf.Clamp01(1f - t / 0.55f)));

        AddEmission(clip, CoreRendererPath, CoreEmission, DischargeSeconds,
            t => Mathf.Lerp(22f, 5.5f, Ease(t / 0.8f)) + 12f * Mathf.Exp(-t * 14f));
        AddEmission(clip, RingRendererPath, RingEmission, DischargeSeconds,
            t => Mathf.Lerp(5.5f, 2.0f, Ease(t / 0.8f)) + 3f * Mathf.Exp(-t * 10f));
        AddEmission(clip, LensRendererPath, LensEmission, DischargeSeconds,
            t => Mathf.Lerp(0.95f, 0.25f, Ease(t / 0.8f)));
        AddLensScale(clip, DischargeSeconds, t => Mathf.Lerp(1.12f, 1f, Ease(t / 0.8f)));
        return clip;
    }

    private static float Ease(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * (3f - 2f * u);
    }

    private static AnimationClip ResetClip(string path, string clipName, float length, bool loop)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = clipName };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.name = clipName;
        clip.frameRate = 30f;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = false;
        settings.cycleOffset = 0f;
        settings.startTime = 0f;
        settings.stopTime = length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        return clip;
    }

    /// <summary>
    /// Samples <paramref name="evaluate"/> into a smooth curve. Numeric tangents
    /// are written per key so the clips do not depend on Unity's auto-tangent
    /// heuristics (those changed across editor versions).
    /// </summary>
    private static AnimationCurve Sampled(float length, Func<float, float> evaluate,
        float step = 1f / 15f)
    {
        int segments = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(1e-3f, step)));
        var keys = new Keyframe[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = length * i / segments;
            float h = length / segments;
            float before = Mathf.Max(0f, t - h);
            float after = Mathf.Min(length, t + h);
            float slope = after - before > 1e-5f
                ? (evaluate(after) - evaluate(before)) / (after - before)
                : 0f;
            keys[i] = new Keyframe(t, evaluate(t), slope, slope);
        }
        return new AnimationCurve(keys);
    }

    private static void AddCurve(AnimationClip clip, string path, string property,
        AnimationCurve curve, Type bindingType = null)
    {
        var binding = new EditorCurveBinding
        {
            path = path,
            type = bindingType ?? typeof(Transform),
            propertyName = property,
        };
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    private static void AddEmission(AnimationClip clip, string rendererPath, Color baseColor,
        float length, Func<float, float> intensity)
    {
        const string channels = "rgba";
        for (int c = 0; c < 4; c++)
        {
            float scale = c < 3 ? baseColor[c] : 1f;
            AddCurve(clip, rendererPath, "material._EmissionColor." + channels[c],
                Sampled(length, t => scale * intensity(t)), typeof(MeshRenderer));
        }
    }

    private static void AddLensScale(AnimationClip clip, float length, Func<float, float> scale)
    {
        AddCurve(clip, LensRendererPath, "m_LocalScale.x", Sampled(length, scale));
        AddCurve(clip, LensRendererPath, "m_LocalScale.y", Sampled(length, scale));
        AddCurve(clip, LensRendererPath, "m_LocalScale.z", Sampled(length, scale));
    }

    // ---------------------------------------------------------------- probing
    private static bool ProbePrefabInternal(out string summary)
    {
        summary = string.Empty;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            summary = "缺少预制体：" + PrefabPath;
            return false;
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var checks = new List<string[]>();
            void Check(string name, bool ok, string detail)
            {
                checks.Add(new[] { name, ok ? "PASS" : "FAIL", detail });
            }

            Transform ceiling = instance.transform.Find(CeilingGroupName);
            Transform yaw = instance.transform.Find(YawGroupName);
            Transform pitch = yaw != null ? yaw.Find(PitchGroupName) : null;
            Transform nozzle = pitch != null ? pitch.Find(NozzleMarkerName) : null;

            Check("ceiling_group", ceiling != null, ceiling != null ? "ok" : "missing");
            Check("yaw_group", yaw != null, yaw != null ? "ok" : "missing");
            Check("pitch_group", pitch != null, pitch != null ? "ok" : "missing");
            Check("nozzle_marker", nozzle != null, nozzle != null ? "ok" : "missing");

            int mountCount = ceiling != null ? ceiling.GetComponentsInChildren<MeshRenderer>().Length : 0;
            int yawCount = yaw != null
                ? yaw.GetComponentsInChildren<MeshRenderer>().Length - (pitch != null
                    ? pitch.GetComponentsInChildren<MeshRenderer>().Length : 0)
                : 0;
            int headCount = pitch != null ? pitch.GetComponentsInChildren<MeshRenderer>().Length : 0;

            Check("mount_meshes", mountCount >= 10, "count=" + mountCount);
            Check("yaw_meshes", yawCount >= 20, "count=" + yawCount);
            Check("head_meshes", headCount >= 14, "count=" + headCount);

            Vector3 yawPos = yaw != null ? yaw.localPosition : Vector3.zero;
            Vector3 pitchPos = pitch != null ? pitch.localPosition : Vector3.zero;
            Vector3 nozzlePos = nozzle != null ? nozzle.position - instance.transform.position
                : Vector3.zero;
            Check("yaw_pivot_depth", Mathf.Abs(yawPos.z - YawPivotDepth) < 1e-3f,
                "z=" + yawPos.z.ToString("F4"));
            Check("pitch_pivot_world_depth",
                Mathf.Abs(yawPos.z + pitchPos.z - PitchPivotDepth) < 1e-3f,
                "worldZ=" + (yawPos.z + pitchPos.z).ToString("F4"));
            Check("nozzle_marker_depth", Mathf.Abs(nozzlePos.z - NozzleDepth) < 2e-3f,
                "nozzleZ=" + nozzlePos.z.ToString("F4"));
            Check("nozzle_points_down", nozzle != null
                && Vector3.Dot(nozzle.forward, Vector3.forward) > 0.999f,
                nozzle != null ? nozzle.forward.ToString("F3") : "missing");

            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            Check("renderer_count", renderers.Length >= 42, "count=" + renderers.Length);
            int missingMaterial = renderers.Count(r => r.sharedMaterial == null);
            Check("no_missing_material", missingMaterial == 0, "missing=" + missingMaterial);
            int nonUrp = renderers.Count(r => r.sharedMaterial != null
                && r.sharedMaterial.shader != null
                && !r.sharedMaterial.shader.name.Contains("Universal Render Pipeline"));
            Check("all_urp_materials", nonUrp == 0, "nonUrp=" + nonUrp);
            var distinct = renderers.Where(r => r.sharedMaterial != null)
                .Select(r => r.sharedMaterial.name).Distinct().OrderBy(n => n).ToArray();
            Check("material_set", distinct.Length == Palette.Count,
                string.Join(",", distinct));

            Check("no_colliders",
                instance.GetComponentsInChildren<Collider>(true).Length == 0, "ok");
            Check("no_rigidbody",
                instance.GetComponentsInChildren<Rigidbody>(true).Length == 0, "ok");
            Check("gameplay_components", instance.GetComponent<TrapInstance>() != null
                && instance.GetComponent<ThroatLampTrap>() != null
                && instance.GetComponentsInChildren<MonoBehaviour>(true).Length == 2, "TrapInstance + ThroatLampTrap");
            Check("gameplay_rig", instance.GetComponent<ThroatLampTrap>() != null
                && instance.GetComponent<ThroatLampTrap>().HasBoundRig, "pivots / nozzle / animator / projectile");
            TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
            Check("ceiling_definition", definition != null && definition.Prefab == prefab
                && definition.MountType == TrapMountType.Ceiling
                && definition.WorldFootprint == new Vector2(2f, 2f), "Ceiling / 2 x 2 metres");
            Check("no_lights_cameras",
                instance.GetComponentsInChildren<Light>(true).Length == 0 &&
                instance.GetComponentsInChildren<Camera>(true).Length == 0, "ok");

            Bounds bounds = MeasureBounds(renderers);
            // Ceiling traps hang along local +Z: the mounting plate sits on the
            // z = 0 plane and the emitter reaches z ~ 0.41.
            Check("hangs_below_ceiling", bounds.min.z > -0.02f && bounds.max.z > 0.38f,
                "minZ=" + bounds.min.z.ToString("F4") + " maxZ=" + bounds.max.z.ToString("F4"));
            Check("width_plausible",
                Mathf.Abs(bounds.size.x - 0.456f) < 0.03f, "width=" + bounds.size.x.ToString("F4"));
            Check("drop_plausible",
                Mathf.Abs(bounds.size.z - 0.428f) < 0.05f, "drop=" + bounds.size.z.ToString("F4"));

            Material ring = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("TL_PlasmaRing"));
            Material core = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("TL_PlasmaCore"));
            Check("plasma_ring_emissive", ring != null && ring.IsKeywordEnabled("_EMISSION")
                && ring.GetColor("_EmissionColor").maxColorComponent > 1f,
                ring != null ? ring.GetColor("_EmissionColor").ToString("F2") : "missing");
            Check("plasma_core_emissive", core != null && core.IsKeywordEnabled("_EMISSION")
                && core.GetColor("_EmissionColor").maxColorComponent > 2f,
                core != null ? core.GetColor("_EmissionColor").ToString("F2") : "missing");

            // ------------------------------------------------- animation checks
            Animator animator = instance.GetComponent<Animator>();
            Check("animator_component", animator != null, animator != null ? "ok" : "missing");
            RuntimeAnimatorController controller = animator != null
                ? animator.runtimeAnimatorController : null;
            Check("animator_controller", controller != null,
                controller != null ? controller.name : "missing");
            Check("animator_no_root_motion", animator != null && !animator.applyRootMotion, "ok");
            Check("animator_always_animate",
                animator != null && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                animator != null ? animator.cullingMode.ToString() : "missing");

            AnimationClip[] clips = controller != null && controller.animationClips != null
                ? controller.animationClips : new AnimationClip[0];
            Check("animation_clips", clips.Length == 3, "count=" + clips.Length);

            int missingBindings = 0;
            foreach (AnimationClip clip in clips)
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    Transform target = string.IsNullOrEmpty(binding.path)
                        ? instance.transform : instance.transform.Find(binding.path);
                    if (target == null)
                    {
                        missingBindings++;
                        continue;
                    }
                    if (binding.type == typeof(MeshRenderer)
                        && target.GetComponent<MeshRenderer>() == null) missingBindings++;
                }
            }
            Check("animation_bindings_resolve", missingBindings == 0 && clips.Length > 0,
                "missing=" + missingBindings);

            float yawSweep = CurveRange(clips, YawBindPath, "localEulerAnglesRaw.y");
            float pitchSweep = CurveRange(clips, PitchBindPath, "localEulerAnglesRaw.x");
            Check("yaw_animated", yawSweep > 20f, "sweepDeg=" + yawSweep.ToString("F1"));
            Check("pitch_animated", pitchSweep > 4f, "sweepDeg=" + pitchSweep.ToString("F1"));
            int emissionCurves = clips.Sum(c => AnimationUtility.GetCurveBindings(c)
                .Count(b => b.propertyName.StartsWith("material._EmissionColor")));
            Check("emission_animated", emissionCurves >= 12, "curves=" + emissionCurves);

            AnimatorStateMachine machine = controller is AnimatorController animatorController
                && animatorController.layers.Length > 0
                ? animatorController.layers[0].stateMachine : null;
            Check("animation_states", machine != null && machine.states.Length == 3,
                machine != null ? "states=" + machine.states.Length : "missing");

            AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
            Check("idle_loops", idleClip != null
                && AnimationUtility.GetAnimationClipSettings(idleClip).loopTime,
                idleClip != null ? idleClip.length.ToString("F2") + "s" : "missing");
            Check("clip_lengths", clips.Length > 0 && clips.All(c => c.length > 0.5f)
                && clips.Any(c => Mathf.Abs(c.length - IdleSeconds) < 0.05f),
                string.Join("/", clips.Select(c => c.length.ToString("F2"))));

            bool pass = checks.All(c => c[1] == "PASS");
            summary = string.Join(", ", checks.Select(c => c[0] + "=" + c[1]));

            string dir = GameplayOutput;
            Directory.CreateDirectory(dir);
            var json = new System.Text.StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"model\": \"" + ModelPath + "\",");
            json.AppendLine("  \"prefab\": \"" + PrefabPath + "\",");
            json.AppendLine("  \"bounds\": { \"size\": [" + bounds.size.x.ToString("F4") + ", "
                + bounds.size.y.ToString("F4") + ", " + bounds.size.z.ToString("F4") + "], "
                + "\"min\": [" + bounds.min.x.ToString("F4") + ", " + bounds.min.y.ToString("F4") + ", "
                + bounds.min.z.ToString("F4") + "], \"max\": [" + bounds.max.x.ToString("F4") + ", "
                + bounds.max.y.ToString("F4") + ", " + bounds.max.z.ToString("F4") + "] },");
            json.AppendLine("  \"groups\": { \"mount\": " + mountCount + ", \"yaw\": " + yawCount
                + ", \"head\": " + headCount + " },");
            json.AppendLine("  \"checks\": [");
            for (int i = 0; i < checks.Count; i++)
            {
                json.AppendLine("    { \"name\": \"" + checks[i][0] + "\", \"result\": \""
                    + checks[i][1] + "\", \"detail\": \"" + checks[i][2].Replace("\\", "/") + "\" }"
                    + (i < checks.Count - 1 ? "," : ""));
            }
            json.AppendLine("  ],");
            json.AppendLine("  \"pass\": " + (pass ? "true" : "false"));
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "probe-prefab.json"), json.ToString());
            File.WriteAllText(Path.Combine(dir, "probe-prefab.txt"),
                string.Join(Environment.NewLine, checks.Select(c => c[1] + "  " + c[0] + "  " + c[2])));

            if (!pass) summary = "FAILED: " + summary;
            return pass;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static float CurveRange(AnimationClip[] clips, string path, string property)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (AnimationClip clip in clips)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path || binding.propertyName != property) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                foreach (Keyframe key in curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }
            }
        }
        return max > min ? max - min : 0f;
    }

    private static Bounds MeasureBounds(MeshRenderer[] renderers)
    {
        bool has = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.sharedMaterial == null) continue;
            if (!has)
            {
                bounds = renderer.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }
}
