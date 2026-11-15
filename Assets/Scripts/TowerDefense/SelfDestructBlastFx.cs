using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 自爆机器人引爆时的程序化表现：膨胀的球形闪光 + 衰减点光，播完自毁。
///
/// 全部在运行时用代码生成，不依赖任何美术资源与预制体，
/// 所以重新生成怪物预制体时不会丢表现；要替换成美术特效时只改 <see cref="SelfDestructBotEnemy"/> 里的调用点。
/// </summary>
public static class SelfDestructBlastFx
{
    /// <summary>在 <paramref name="center"/> 生成一次爆炸表现，半径决定尺寸与光强。</summary>
    public static SelfDestructBlast Spawn(Vector3 center, float radius)
    {
        var go = new GameObject("SelfDestructBlast");
        go.transform.position = center;
        var blast = go.AddComponent<SelfDestructBlast>();
        blast.Play(radius);
        return blast;
    }

    /// <summary>生成加法混合半透明材质（URP/Unlit，缺 URP 时退回内置 Sprites/Default）。</summary>
    internal static Material CreateAdditiveMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        var material = new Material(shader) { name = "SelfDestructBlastMat" };

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);          // Transparent
            material.SetFloat("_Blend", 1f);            // Additive
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);
        }
        return material;
    }
}

/// <summary>单次爆炸的运行时表现。由 <see cref="SelfDestructBlastFx.Spawn"/> 创建。</summary>
[DisallowMultipleComponent]
public sealed class SelfDestructBlast : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float lifetime = 0.45f;
    [SerializeField, Min(0f)] private float startScaleFactor = 0.18f;
    [SerializeField, Min(0f)] private float lightIntensity = 8f;

    private float radius = 1f;
    private float elapsed;
    private Transform core;
    private Material material;
    private Light flash;
    private Color baseColor = new Color(1f, 0.62f, 0.22f, 0.85f);
    private bool playing;

    /// <summary>当前是否正在播放。</summary>
    public bool IsPlaying => playing;

    /// <summary>开始播放；<paramref name="blastRadius"/> 是爆炸半径（米）。</summary>
    public void Play(float blastRadius)
    {
        radius = Mathf.Max(0.2f, blastRadius);
        elapsed = 0f;
        playing = true;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        sphere.name = "BlastCore";
        sphere.transform.SetParent(transform, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * radius * startScaleFactor;
        core = sphere.transform;

        material = SelfDestructBlastFx.CreateAdditiveMaterial(baseColor);
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var lightObject = new GameObject("BlastLight");
        lightObject.transform.SetParent(transform, false);
        flash = lightObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.66f, 0.30f);
        flash.intensity = lightIntensity;
        flash.range = radius * 2.2f;
        flash.shadows = LightShadows.None;
    }

    private void Update()
    {
        if (!playing) return;
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, lifetime));

        // 前 45% 快速膨胀，之后缓慢外扩并淡出
        float scaleT = t < 0.45f ? Mathf.Lerp(startScaleFactor, 1.05f, t / 0.45f * 1f) : Mathf.Lerp(1.05f, 1.25f, (t - 0.45f) / 0.55f);
        if (core != null)
        {
            core.localScale = Vector3.one * radius * scaleT;
        }

        float alpha = Mathf.Pow(1f - t, 1.6f);
        if (material != null)
        {
            Color faded = baseColor;
            faded.a = baseColor.a * alpha;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", faded);
            if (material.HasProperty("_Color")) material.SetColor("_Color", faded);
        }
        if (flash != null)
        {
            flash.intensity = lightIntensity * alpha;
        }

        if (t >= 1f)
        {
            playing = false;
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (material != null)
        {
            Destroy(material);
        }
    }
}
