using System;
using UnityEngine;

/// <summary>
/// Presentation layer for the flamethrower state machine.
/// It creates lightweight local particles and procedural spatial audio at runtime so the
/// trap remains functional when final art or audio assets are not present yet.
/// </summary>
[DisallowMultipleComponent]
public sealed class FlamethrowerFeedback : MonoBehaviour
{
    [Header("Optional authored overrides")]
    [SerializeField] private ParticleSystem preheatParticles;
    [SerializeField] private ParticleSystem sprayParticles;
    [SerializeField] private ParticleSystem cooldownParticles;
    [SerializeField] private AudioClip preheatClip;
    [SerializeField] private AudioClip sprayClip;
    [SerializeField] private AudioClip sprayEndClip;
    [SerializeField] private AudioClip cooldownCompleteClip;

    [Header("Presentation")]
    [SerializeField, Min(0f)] private float sprayAudioVolume = 0.72f;
    [SerializeField, Min(0f)] private float oneShotAudioVolume = 0.8f;
    [SerializeField, Min(0.1f)] private float audioMaxDistance = 18f;

    private FlamethrowerTrap trap;
    private Transform flameOrigin;
    private Transform runtimeRoot;
    private ParticleSystem flameCore;
    private ParticleSystem smoke;
    private ParticleSystem embers;
    private ParticleSystem preheatSparks;
    private ParticleSystem cooldownSmoke;
    private Light heatLight;
    private AudioSource oneShotSource;
    private AudioSource spraySource;
    private AudioClip generatedPreheatClip;
    private AudioClip generatedSprayClip;
    private AudioClip generatedSprayEndClip;
    private AudioClip generatedCooldownCompleteClip;
    private Texture2D softParticleTexture;
    private FlamethrowerTrap.AttackState currentState = FlamethrowerTrap.AttackState.Ready;
    private FlamethrowerTrap.AttackState previousState = FlamethrowerTrap.AttackState.Ready;
    private bool initialized;

    public FlamethrowerTrap.AttackState CurrentState => currentState;
    public bool IsInitialized => initialized;

    public void Bind(FlamethrowerTrap owner)
    {
        if (owner != null) trap = owner;
        EnsureCreated();
    }

    public void SetState(FlamethrowerTrap.AttackState next)
    {
        EnsureCreated();
        previousState = currentState;
        currentState = next;

        if (!initialized) return;
        StopAllVisuals();

        switch (next)
        {
            case FlamethrowerTrap.AttackState.Delay:
                Play(preheatSparks);
                SetLight(0.35f);
                PlayOneShot(preheatClip != null ? preheatClip : generatedPreheatClip, oneShotAudioVolume);
                break;

            case FlamethrowerTrap.AttackState.Spraying:
                Play(sprayParticles);
                Play(flameCore);
                Play(smoke);
                Play(embers);
                SetLight(1.7f);
                StartSprayAudio();
                break;

            case FlamethrowerTrap.AttackState.Cooldown:
                Play(cooldownSmoke);
                SetLight(0.2f);
                StopSprayAudio();
                PlayOneShot(sprayEndClip != null ? sprayEndClip : generatedSprayEndClip, oneShotAudioVolume);
                break;

            case FlamethrowerTrap.AttackState.Ready:
                SetLight(0f);
                StopSprayAudio();
                if (previousState == FlamethrowerTrap.AttackState.Cooldown)
                {
                    PlayOneShot(cooldownCompleteClip != null
                        ? cooldownCompleteClip
                        : generatedCooldownCompleteClip, oneShotAudioVolume * 0.7f);
                }
                break;
        }
    }

    public void ResetFeedback()
    {
        EnsureCreated();
        currentState = FlamethrowerTrap.AttackState.Ready;
        previousState = FlamethrowerTrap.AttackState.Ready;
        StopAllVisuals();
        StopSprayAudio();
        SetLight(0f);
    }

    private void Awake()
    {
        EnsureCreated();
    }

    private void OnEnable()
    {
        if (initialized) ResetFeedback();
    }

    private void OnDisable()
    {
        if (!initialized) return;
        StopAllVisuals();
        StopSprayAudio();
        SetLight(0f);
    }

    private void OnDestroy()
    {
        if (softParticleTexture != null) DestroyRuntimeObject(softParticleTexture);
        DestroyRuntimeObject(generatedPreheatClip);
        DestroyRuntimeObject(generatedSprayClip);
        DestroyRuntimeObject(generatedSprayEndClip);
        DestroyRuntimeObject(generatedCooldownCompleteClip);
    }

    private void Update()
    {
        if (!initialized || heatLight == null) return;
        if (currentState == FlamethrowerTrap.AttackState.Spraying)
        {
            float flicker = Mathf.PerlinNoise(Time.time * 13.5f, 0.37f);
            heatLight.intensity = Mathf.Lerp(1.2f, 2.15f, flicker);
        }
        else if (currentState == FlamethrowerTrap.AttackState.Delay)
        {
            heatLight.intensity = Mathf.Lerp(0.18f, 0.6f,
                0.5f + 0.5f * Mathf.Sin(Time.time * 22f));
        }
    }

    private void EnsureCreated()
    {
        if (initialized) return;

        try
        {
            if (trap == null) trap = GetComponent<FlamethrowerTrap>();
            flameOrigin = FindChild("Flame Origin") ?? transform;
            runtimeRoot = FindChild("Runtime Flame Feedback");
            if (runtimeRoot == null)
            {
                GameObject root = new GameObject("Runtime Flame Feedback");
                runtimeRoot = root.transform;
                runtimeRoot.SetParent(flameOrigin, false);
            }

            softParticleTexture = CreateSoftParticleTexture();
            CreateVisuals();
            CreateAudio();
            initialized = true;
        }
        catch (Exception exception)
        {
            // Presentation must never block the damage state machine.
            Debug.LogWarning($"Flamethrower feedback disabled on {name}: {exception.Message}", this);
            initialized = false;
        }
    }

    private void CreateVisuals()
    {
        Transform preheatMount = FindChild("Preheat Fx") ?? flameOrigin;
        Transform sprayMount = FindChild("Spray Fx") ?? flameOrigin;
        Transform cooldownMount = FindChild("Cooldown Fx") ?? flameOrigin;

        if (preheatParticles == null)
        {
            preheatSparks = CreateParticles("Runtime Preheat Sparks", preheatMount, 34f, 0.08f, 0.18f,
                3.2f, 0.06f, 0.13f, 13f, new Color(1f, 0.55f, 0.08f, 0.9f),
                new Color(1f, 0.05f, 0f, 0f), false);
        }
        else preheatSparks = preheatParticles;

        if (sprayParticles == null)
        {
        sprayParticles = CreateParticles("Runtime Flame Stream", sprayMount, 220f, 0.45f, 0.95f,
            6.6f, 0.18f, 0.5f, 10f, new Color(1f, 0.62f, 0.08f, 0.62f),
            new Color(0.72f, 0.025f, 0f, 0f), false);
        }
        ConfigureRange(sprayParticles, 7.3f, trap != null ? trap.Range : 8f);

        flameCore = CreateParticles("Runtime Flame Core", sprayMount, 120f, 0.2f, 0.45f,
            9.5f, 0.08f, 0.24f, 7f, new Color(1f, 1f, 0.86f, 0.9f),
            new Color(1f, 0.28f, 0.01f, 0f), true);
        ConfigureRange(flameCore, 9.5f, trap != null ? trap.Range : 8f);

        smoke = CreateParticles("Runtime Flame Smoke", sprayMount, 22f, 0.7f, 1.2f,
            1.4f, 0.28f, 0.58f, 13f, new Color(0.18f, 0.16f, 0.13f, 0.56f),
            new Color(0.04f, 0.035f, 0.03f, 0f), false);
        ConfigureSmoke(smoke);

        embers = CreateParticles("Runtime Embers", sprayMount, 15f, 0.28f, 0.64f,
            3.4f, 0.025f, 0.075f, 8f, new Color(1f, 0.36f, 0.03f, 0.95f),
            new Color(0.72f, 0.02f, 0f, 0f), true);

        if (cooldownParticles == null)
        {
            cooldownSmoke = CreateParticles("Runtime Cooldown Smoke", cooldownMount, 5f, 0.8f, 1.35f,
                0.85f, 0.2f, 0.4f, 12f, new Color(0.18f, 0.17f, 0.15f, 0.34f),
                new Color(0.04f, 0.035f, 0.03f, 0f), false);
        }
        else cooldownSmoke = cooldownParticles;
        ConfigureSmoke(cooldownSmoke);

        Transform heatLightMount = FindChild("Runtime Flame Heat");
        heatLight = heatLightMount != null ? heatLightMount.GetComponent<Light>() : null;
        if (heatLight == null)
        {
            GameObject lightObject = new GameObject("Runtime Flame Heat");
            lightObject.transform.SetParent(flameOrigin, false);
            heatLight = lightObject.AddComponent<Light>();
        }
        heatLight.type = LightType.Point;
        heatLight.color = new Color(1f, 0.22f, 0.035f);
        heatLight.range = 5.5f;
        heatLight.shadows = LightShadows.None;
        heatLight.intensity = 0f;
        heatLight.enabled = true;
    }

    private void CreateAudio()
    {
        oneShotSource = GetOrCreateAudioSource("Runtime Flame One Shots", false);
        spraySource = GetOrCreateAudioSource("Runtime Flame Spray Audio", true);
        generatedPreheatClip = CreatePreheatClip();
        generatedSprayClip = CreateSprayClip();
        generatedSprayEndClip = CreateSprayEndClip();
        generatedCooldownCompleteClip = CreateCooldownCompleteClip();
    }

    private AudioSource GetOrCreateAudioSource(string objectName, bool loop)
    {
        Transform child = FindChild(objectName);
        GameObject sourceObject;
        if (child == null)
        {
            sourceObject = new GameObject(objectName);
            sourceObject.transform.SetParent(flameOrigin, false);
        }
        else sourceObject = child.gameObject;

        AudioSource source = sourceObject.GetComponent<AudioSource>();
        if (source == null) source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f;
        source.minDistance = 1.4f;
        source.maxDistance = audioMaxDistance;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.volume = 1f;
        return source;
    }

    private ParticleSystem CreateParticles(string objectName, Transform parent, float rate,
        float minLifetime, float maxLifetime, float speed, float minSize, float maxSize,
        float coneAngle, Color startColor, Color endColor, bool additive)
    {
        GameObject particleObject = new GameObject(objectName);
        particleObject.transform.SetParent(parent, false);
        ParticleSystem particleSystem = particleObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particleSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed * 1.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = startColor;
        main.maxParticles = Mathf.CeilToInt(rate * Mathf.Max(maxLifetime, 0.1f) * 2f) + 16;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.055f;
        shape.radiusThickness = 0.82f;

        ParticleSystem.ColorOverLifetimeModule color = particleSystem.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(startColor.a, 0f), new GradientAlphaKey(endColor.a, 1f) });
        color.color = gradient;

        ParticleSystem.SizeOverLifetimeModule size = particleSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.2f, 1f),
                new Keyframe(0.78f, 0.75f), new Keyframe(1f, 0.05f)));

        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particleSystem.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = additive ? 0.1f : 0.45f;

        ParticleSystem.NoiseModule noise = particleSystem.noise;
        noise.enabled = true;
        noise.strength = additive ? 0.22f : 0.16f;
        noise.frequency = additive ? 1.7f : 0.9f;
        noise.scrollSpeed = 0.8f;

        ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        // Soft billboards keep the flame volume readable and avoid hard rectangular streaks.
        // Forward motion comes from the particle velocity and cone shape.
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.velocityScale = additive ? 0.32f : 0.12f;
        renderer.lengthScale = additive ? 0.42f : 0.35f;
        renderer.material = CreateParticleMaterial(additive);
        renderer.sortingFudge = additive ? -1f : 0f;
        return particleSystem;
    }

    private void ConfigureSmoke(ParticleSystem particleSystem)
    {
        if (particleSystem == null) return;
        ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 1.05f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.25f, 1.15f);

        ParticleSystem.RotationOverLifetimeModule rotation = particleSystem.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
    }

    private void ConfigureRange(ParticleSystem particleSystem, float speed, float desiredRange)
    {
        if (particleSystem == null) return;
        ParticleSystem.MainModule main = particleSystem.main;
        float lifetime = Mathf.Clamp(desiredRange / Mathf.Max(speed, 0.1f), 0.35f, 1.45f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.72f, lifetime * 1.28f);
    }

    private Material CreateParticleMaterial(bool additive)
    {
        Shader shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit");
        if (shader == null) return null;

        Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.mainTexture = softParticleTexture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", softParticleTexture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", softParticleTexture);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)(additive
            ? UnityEngine.Rendering.BlendMode.One
            : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        return material;
    }

    private Texture2D CreateSoftParticleTexture()
    {
        const int resolution = 64;
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
        {
            name = "Runtime Flame Soft Particle",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[resolution * resolution];
        float center = (resolution - 1f) * 0.5f;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.65f);
                pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void StopAllVisuals()
    {
        Stop(preheatSparks);
        Stop(sprayParticles);
        Stop(flameCore);
        Stop(smoke);
        Stop(embers);
        Stop(cooldownSmoke);
    }

    private static void Play(ParticleSystem particleSystem)
    {
        if (particleSystem != null) particleSystem.Play(true);
    }

    private static void Stop(ParticleSystem particleSystem)
    {
        if (particleSystem != null) particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void SetLight(float intensity)
    {
        if (heatLight == null) return;
        heatLight.intensity = intensity;
        heatLight.enabled = intensity > 0f;
    }

    private void StartSprayAudio()
    {
        if (!Application.isPlaying || spraySource == null) return;
        spraySource.clip = sprayClip != null ? sprayClip : generatedSprayClip;
        spraySource.volume = sprayAudioVolume;
        if (spraySource.clip != null && !spraySource.isPlaying) spraySource.Play();
    }

    private void StopSprayAudio()
    {
        if (spraySource == null) return;
        if (spraySource.isPlaying) spraySource.Stop();
        spraySource.clip = null;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (!Application.isPlaying || oneShotSource == null || clip == null) return;
        oneShotSource.PlayOneShot(clip, volume);
    }

    private Transform FindChild(string childName)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == childName) return all[i];
        }
        return null;
    }

    private AudioClip CreatePreheatClip()
    {
        return CreateProceduralClip("Flamethrower_Preheat", 0.22f, (time, noise) =>
        {
            float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 1320f, time / 0.22f) * time);
            float hiss = noise * (0.12f + 0.35f * Mathf.Clamp01(time / 0.08f));
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / 0.22f));
            return (sweep * 0.28f + hiss) * envelope;
        });
    }

    private AudioClip CreateSprayClip()
    {
        return CreateProceduralClip("Flamethrower_Spray", 2.0f, (time, noise) =>
        {
            float low = Mathf.Sin(2f * Mathf.PI * 68f * time) * 0.18f;
            float mid = Mathf.Sin(2f * Mathf.PI * 143f * time + Mathf.Sin(time * 5f) * 0.7f) * 0.1f;
            float modulation = 0.72f + 0.18f * Mathf.Sin(time * 11f) + 0.1f * Mathf.Sin(time * 31f);
            float crackle = Mathf.Max(0f, noise - 0.72f) * 2.2f;
            return Mathf.Clamp((noise * 0.54f + low + mid + crackle * 0.12f) * modulation, -0.95f, 0.95f);
        });
    }

    private AudioClip CreateSprayEndClip()
    {
        return CreateProceduralClip("Flamethrower_SprayEnd", 0.32f, (time, noise) =>
        {
            float envelope = 1f - Mathf.Clamp01(time / 0.32f);
            float hiss = noise * 0.42f;
            float pressure = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 88f, time / 0.32f) * time) * 0.22f;
            return (hiss + pressure) * envelope * envelope;
        });
    }

    private AudioClip CreateCooldownCompleteClip()
    {
        return CreateProceduralClip("Flamethrower_CooldownComplete", 0.16f, (time, noise) =>
        {
            float click = Mathf.Sin(2f * Mathf.PI * 940f * time) * Mathf.Exp(-time * 34f);
            float ring = Mathf.Sin(2f * Mathf.PI * 186f * time) * Mathf.Exp(-time * 20f) * 0.3f;
            return click * 0.62f + ring + noise * 0.025f;
        });
    }

    private static AudioClip CreateProceduralClip(string clipName, float duration,
        Func<float, float, float> sampleFunction)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(duration * sampleRate);
        float[] samples = new float[sampleCount];
        uint seed = (uint)clipName.GetHashCode();
        for (int i = 0; i < sampleCount; i++)
        {
            seed = 1664525u * seed + 1013904223u;
            float noise = ((seed >> 8) & 0x00ffffff) / 8388607.5f - 1f;
            float time = i / (float)sampleRate;
            samples[i] = sampleFunction(time, noise);
        }
        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
