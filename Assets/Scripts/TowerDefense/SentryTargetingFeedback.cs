using UnityEngine;

/// <summary>
/// Plays the two-beep acquisition cue for a sentry turret. The sequence is
/// deterministic: beep for 0.1 s, silence for 0.1 s, beep for 0.1 s, stop.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Targeting Feedback")]
public sealed class SentryTargetingFeedback : MonoBehaviour
{
    public const float BeepDuration = 0.1f;
    public const float BeepGap = 0.1f;
    public const float SequenceDuration = 0.3f;

    [Header("Audio")]
    [SerializeField] private AudioClip beepClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;

    private AutoSentryTurret turret;
    private AudioSource audioSource;
    private AudioClip generatedBeepClip;
    private float sequenceElapsed = -1f;
    private int beepStartCount;

    public AudioClip BeepClip => beepClip;
    public AudioSource AudioSource => audioSource;
    public bool IsSequenceActive => sequenceElapsed >= 0f;
    public float SequenceElapsed => Mathf.Max(0f, sequenceElapsed);
    public int BeepStartCount => beepStartCount;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        BindTurret();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopSequence();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    public void EnsureInitialized()
    {
        if (audioSource == null)
        {
            Transform sourceNode = transform.Find("Runtime Targeting Audio");
            GameObject sourceObject = sourceNode != null ? sourceNode.gameObject : new GameObject("Runtime Targeting Audio");
            if (sourceNode == null) sourceObject.transform.SetParent(transform, false);
            audioSource = sourceObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = sourceObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
        }

        if (beepClip == null && generatedBeepClip == null)
            generatedBeepClip = CreateGeneratedBeep();
    }

    public void BeginSequence()
    {
        EnsureInitialized();
        if (turret == null) BindTurret();
        if (turret != null && !turret.TargetingFeedbackEnabled) return;

        sequenceElapsed = 0f;
        beepStartCount = 0;
        StartBeep();
    }

    public void Tick(float deltaTime)
    {
        if (sequenceElapsed < 0f) return;
        if (deltaTime < 0f) deltaTime = 0f;

        float previous = sequenceElapsed;
        float next = previous + deltaTime;
        if (previous < BeepDuration && next >= BeepDuration && audioSource != null)
            audioSource.Stop();
        if (previous < BeepDuration + BeepGap && next >= BeepDuration + BeepGap)
            StartBeep();
        if (next >= SequenceDuration)
        {
            if (audioSource != null) audioSource.Stop();
            sequenceElapsed = -1f;
        }
        else
        {
            sequenceElapsed = next;
        }
    }

    public void StopSequence()
    {
        sequenceElapsed = -1f;
        if (audioSource != null) audioSource.Stop();
    }

    private void StartBeep()
    {
        EnsureInitialized();
        AudioClip clip = beepClip != null ? beepClip : generatedBeepClip;
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        beepStartCount++;
    }

    private void BindTurret()
    {
        if (turret == null) turret = GetComponentInParent<AutoSentryTurret>();
    }

    private void Subscribe()
    {
        if (turret == null) return;
        turret.TargetAcquired -= HandleTargetAcquired;
        turret.TargetAcquired += HandleTargetAcquired;
    }

    private void Unsubscribe()
    {
        if (turret == null) return;
        turret.TargetAcquired -= HandleTargetAcquired;
    }

    private void HandleTargetAcquired(EnemyInstance enemy)
    {
        BeginSequence();
    }

    private static AudioClip CreateGeneratedBeep()
    {
        const int sampleRate = 48000;
        int sampleCount = Mathf.RoundToInt(sampleRate * BeepDuration);
        AudioClip clip = AudioClip.Create("Sentry_Targeting_Beep", sampleCount, 1, sampleRate, false);
        float[] samples = new float[sampleCount];
        const float frequency = 880f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            float fade = Mathf.Min(1f, i / (sampleRate * 0.005f),
                (samples.Length - 1 - i) / (sampleRate * 0.005f));
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.22f * fade;
        }
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (generatedBeepClip == null) return;
        if (Application.isPlaying) Destroy(generatedBeepClip);
        else DestroyImmediate(generatedBeepClip);
    }
}
