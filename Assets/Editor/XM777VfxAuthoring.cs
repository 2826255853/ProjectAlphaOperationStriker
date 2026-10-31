using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Builds the editable, self-contained XM777 particle effect prefabs.</summary>
public static class XM777VfxAuthoring
{
    public const string EffectsFolder = "Assets/Prefabs/Effects";
    public const string MuzzlePrefabPath = EffectsFolder + "/XM777_Muzzle.prefab";
    /// <summary>Self-contained fallback impact effect, rebuilt by <see cref="CreateEffects"/>.</summary>
    public const string ImpactPrefabPath = EffectsFolder + "/XM777_Impact.prefab";
    /// <summary>Shipped realistic explosion (Hovl Studio) used as the gameplay impact.</summary>
    public const string GameplayImpactPrefabPath = "Assets/Hovl Studio/Realistic explosions/Prefabs/Explosion1.prefab";

    private const string TextureFolder = EffectsFolder + "/Textures";
    private const string MaterialFolder = EffectsFolder + "/Materials";
    private const string SoftTexturePath = TextureFolder + "/XM777_SoftParticle.png";
    private const string RingTexturePath = TextureFolder + "/XM777_ShockwaveRing.png";
    private const string FlameMaterialPath = MaterialFolder + "/XM777_Flame.mat";
    private const string SmokeMaterialPath = MaterialFolder + "/XM777_Smoke.mat";
    private const string RingMaterialPath = MaterialFolder + "/XM777_Shockwave.mat";

    [MenuItem("Tools/塔防/生成 XM777 特效预制体")]
    public static void CreateEffects()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(EffectsFolder);
        EnsureFolder(TextureFolder);
        EnsureFolder(MaterialFolder);

        CreateTextureIfMissing(SoftTexturePath, false);
        CreateTextureIfMissing(RingTexturePath, true);
        AssetDatabase.Refresh();

        Texture2D soft = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftTexturePath);
        Texture2D ring = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
        Material flame = CreateMaterial(FlameMaterialPath, soft, new Color(1f, 0.66f, 0.34f, 1f));
        Material smoke = CreateMaterial(SmokeMaterialPath, soft, new Color(0.3f, 0.29f, 0.27f, 0.72f));
        Material shockwave = CreateMaterial(RingMaterialPath, ring, new Color(0.9f, 0.84f, 0.68f, 0.48f));

        CreateMuzzlePrefab(flame, smoke);
        CreateImpactPrefab(flame, smoke, shockwave);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("XM777_VFX_GENERATED: muzzle flame/smoke and impact fire/smoke/shockwave prefabs are ready.");
    }

    public static void CreateEffectsBatch() => CreateEffects();

    public static GameObject LoadMuzzlePrefab() => AssetDatabase.LoadAssetAtPath<GameObject>(MuzzlePrefabPath);

    /// <summary>
    /// Single source of truth for the impact effect wired into <c>XM777.prefab</c>.
    /// The shipped explosion wins; the generated <see cref="ImpactPrefabPath"/> keeps the
    /// prefab valid on machines where the third-party package is not installed.
    /// Generator, validator and tests must all resolve the impact through this method.
    /// </summary>
    public static GameObject LoadImpactPrefab()
    {
        GameObject gameplay = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayImpactPrefabPath);
        if (gameplay != null) return gameplay;
        Debug.LogWarning($"XM777 未找到真实爆炸特效，回退到自带预制体：{GameplayImpactPrefabPath} -> {ImpactPrefabPath}");
        return AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrefabPath);
    }

    private static void CreateMuzzlePrefab(Material flame, Material smoke)
    {
        GameObject root = new GameObject("XM777 Muzzle FX");
        try
        {
            root.AddComponent<XM777VfxPlayback>();
            AddBurst(root.transform, "Muzzle Flash", flame, ParticleSystemRenderMode.Stretch,
                new Color(1f, 0.78f, 0.46f, 0.94f), new Color(0.75f, 0.15f, 0.025f, 0f),
                0.16f, 0.25f, 0.1f, 0.38f, 13f, 12, 15f, false, false);
            AddBurst(root.transform, "Muzzle Smoke", smoke, ParticleSystemRenderMode.Billboard,
                new Color(0.32f, 0.3f, 0.27f, 0.62f), new Color(0.15f, 0.14f, 0.13f, 0f),
                1.15f, 0.25f, 0.38f, 0.95f, 22f, 14, 2.4f, true, true);
            PrefabUtility.SaveAsPrefabAsset(root, MuzzlePrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateImpactPrefab(Material flame, Material smoke, Material shockwave)
    {
        GameObject root = new GameObject("XM777 Impact FX");
        try
        {
            XM777VfxPlayback playback = root.AddComponent<XM777VfxPlayback>();
            ParticleSystem fireball = AddBurst(root.transform, "Impact Fireball", flame, ParticleSystemRenderMode.Billboard,
                new Color(1f, 0.77f, 0.38f, 0.98f), new Color(0.65f, 0.08f, 0.01f, 0f),
                0.58f, 0.12f, 0.16f, 0.72f, 58f, 30, 5.2f, false, true);
            ConfigureImpactEmitter(fireball);
            ParticleSystem embers = AddBurst(root.transform, "Impact Embers", flame, ParticleSystemRenderMode.Stretch,
                new Color(1f, 0.67f, 0.25f, 0.98f), new Color(0.85f, 0.12f, 0.015f, 0f),
                0.62f, 0.18f, 0.025f, 0.07f, 72f, 16, 6f, false, true);
            ConfigureImpactEmitter(embers);
            ParticleSystem impactSmoke = AddBurst(root.transform, "Impact Smoke", smoke, ParticleSystemRenderMode.Billboard,
                new Color(0.28f, 0.27f, 0.25f, 0.68f), new Color(0.12f, 0.115f, 0.105f, 0f),
                1.7f, 0.24f, 0.3f, 0.92f, 42f, 22, 2.1f, true, true);
            ConfigureImpactEmitter(impactSmoke);

            GameObject ring = new GameObject("Ground Shockwave");
            ring.transform.SetParent(root.transform, false);
            ParticleSystem ringParticles = ring.AddComponent<ParticleSystem>();
            ConfigureRing(ringParticles, shockwave);
            SerializedObject serialized = new SerializedObject(playback);
            serialized.FindProperty("minimumLifetime").floatValue = 0.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ImpactPrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static ParticleSystem AddBurst(Transform parent, string name, Material material,
        ParticleSystemRenderMode renderMode, Color startColor, Color endColor,
        float lifetime, float duration, float minSize, float maxSize, float coneAngle,
        short count, float speed, bool risingSmoke, bool worldSpace)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.SetActive(false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.stopAction = ParticleSystemStopAction.None;
        main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.68f, speed * 1.32f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = startColor;
        main.maxParticles = Mathf.Max(count + 4, 8);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.07f;
        shape.radiusThickness = 0.8f;
        ConfigureColorAndSize(ps, startColor, endColor);
        if (risingSmoke)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 1.15f);
        }

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = renderMode;
        renderer.sharedMaterial = material;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.allowRoll = true;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(true);
        return ps;
    }

    private static void ConfigureRing(ParticleSystem ps, Material material)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.52f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.52f;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startColor = Color.white;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ConfigureColorAndSize(ps, Color.white, new Color(1f, 1f, 1f, 0f));
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.15f, 1f, 9f));
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void ConfigureImpactEmitter(ParticleSystem ps)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.08f;
        shape.radiusThickness = 0.4f;
    }

    private static void ConfigureColorAndSize(ParticleSystem ps, Color start, Color end)
    {
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        color.color = gradient;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.12f)));
    }

    private static Material CreateMaterial(string path, Texture2D texture, Color tint)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null) throw new System.InvalidOperationException("No supported particle shader was found.");
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CreateTextureIfMissing(string path, bool ring)
    {
        if (File.Exists(path)) return;
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        texture.name = Path.GetFileNameWithoutExtension(path);
        Color[] pixels = new Color[size * size];
        float center = (size - 1f) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - center) / center;
            float dy = (y - center) / center;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = ring
                ? Mathf.Exp(-Mathf.Pow((radius - 0.76f) / 0.055f, 2f)) * Mathf.Clamp01((1f - radius) * 10f)
                : Mathf.Pow(Mathf.Clamp01(1f - radius), 1.8f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
