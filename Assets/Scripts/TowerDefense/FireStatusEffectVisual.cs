using UnityEngine;

/// <summary>Enemy-owned burn presentation. It has no dependency on the flamethrower instance.</summary>
[DisallowMultipleComponent]
public sealed class FireStatusEffectVisual : MonoBehaviour
{
    private ParticleSystem flame;
    private ParticleSystem smoke;
    private Light glow;
    private bool burning;
    private Transform mount;
    private Material flameMaterial;
    private Material smokeMaterial;

    public bool IsBurning => burning;

    private void Awake()
    {
        EnsureCreated();
        SetBurning(false);
    }

    private void OnDisable() => SetBurning(false);

    public void SetBurning(bool value)
    {
        EnsureCreated();
        burning = value;
        if (burning)
        {
            flame.Play(true);
            smoke.Play(true);
            glow.enabled = true;
            glow.intensity = 0.7f;
        }
        else
        {
            flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            glow.enabled = false;
            glow.intensity = 0f;
        }
    }

    private void Update()
    {
        if (!burning || glow == null) return;
        float flicker = Mathf.PerlinNoise(Time.time * 16f, 0.71f);
        glow.intensity = Mathf.Lerp(0.45f, 0.95f, flicker);
    }

    private void EnsureCreated()
    {
        if (flame != null) return;
        mount = FindMount();
        flame = CreateParticles("Runtime Burn Flame", mount, 12f, 0.35f, 0.7f, 0.9f,
            0.08f, 0.2f, 28f, new Color(1f, 0.9f, 0.5f, 0.95f),
            new Color(0.9f, 0.05f, 0f, 0f), true);
        smoke = CreateParticles("Runtime Burn Smoke", mount, 4f, 0.65f, 1.1f, 0.48f,
            0.12f, 0.28f, 22f, new Color(0.16f, 0.14f, 0.11f, 0.5f),
            new Color(0.03f, 0.025f, 0.02f, 0f), false);
        ParticleSystem.VelocityOverLifetimeModule velocity = smoke.velocityOverLifetime;
        velocity.enabled = true;
        velocity.y = new ParticleSystem.MinMaxCurve(0.2f, 0.65f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.35f);

        GameObject glowObject = new GameObject("Runtime Burn Glow");
        glowObject.transform.SetParent(mount, false);
        glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.18f, 0.025f);
        glow.range = 1.8f;
        glow.shadows = LightShadows.None;
        glow.enabled = false;
    }

    private Transform FindMount()
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == "Burn FX" || children[i].name == "Fire FX"
                || children[i].name == "Head" || children[i].name == "Torso") return children[i];
        }
        GameObject mountObject = new GameObject("Burn FX");
        mountObject.transform.SetParent(transform, false);
        mountObject.transform.localPosition = Vector3.up * 1.05f;
        return mountObject.transform;
    }

    private ParticleSystem CreateParticles(string objectName, Transform parent, float rate,
        float minLifetime, float maxLifetime, float speed, float minSize, float maxSize,
        float angle, Color startColor, Color endColor, bool additive)
    {
        GameObject particleObject = new GameObject(objectName);
        particleObject.transform.SetParent(parent, false);
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed * 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = startColor;
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = 0.035f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(startColor.a, 0f), new GradientAlphaKey(endColor.a, 1f) });
        color.color = gradient;

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.08f)));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = additive ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        renderer.velocityScale = additive ? 0.55f : 0.1f;
        renderer.lengthScale = additive ? 0.65f : 0.3f;
        Material material = CreateMaterial(additive);
        renderer.material = material;
        if (additive) flameMaterial = material;
        else smokeMaterial = material;
        return particles;
    }

    private static Material CreateMaterial(bool additive)
    {
        Shader shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null) return null;
        Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)(additive
            ? UnityEngine.Rendering.BlendMode.One
            : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        return material;
    }
}
