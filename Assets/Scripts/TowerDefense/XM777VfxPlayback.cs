using UnityEngine;

/// <summary>Plays one authored XM777 effect and removes it after all particles finish.</summary>
[DisallowMultipleComponent]
public sealed class XM777VfxPlayback : MonoBehaviour
{
    [SerializeField, Min(0f)] private float minimumLifetime = 0.25f;
    private float elapsed;
    private float lifetime;
    private bool playing;

    public void Play()
    {
        ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
        lifetime = minimumLifetime;
        elapsed = 0f;
        playing = true;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (system == null) continue;
            system.Play(true);
            ParticleSystem.MainModule main = system.main;
            lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax);
        }
    }

    private void Update() => Advance(Time.deltaTime);

    /// <summary>Deterministic lifetime step used by runtime and EditMode verification.</summary>
    public void Advance(float deltaTime)
    {
        if (!playing || deltaTime <= 0f) return;
        elapsed += deltaTime;
        if (elapsed < lifetime) return;
        playing = false;
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}
