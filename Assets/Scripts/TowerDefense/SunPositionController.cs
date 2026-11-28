using System;
using UnityEngine;

/// <summary>Moves a directional light along a simplified solar path.</summary>
[DisallowMultipleComponent]
public sealed class SunPositionController : MonoBehaviour
{
    public enum ProgressSource
    {
        InGameTime,
        CompletedWaves
    }

    [SerializeField] private Light sun;
    [SerializeField] private ProgressSource progressSource = ProgressSource.InGameTime;
    [SerializeField, Range(0f, 24f)] private float startHour = 9f;
    [SerializeField, Range(0f, 24f)] private float endHour = 17f;
    [SerializeField, Min(0.01f)] private float durationSeconds = 600f;
    [SerializeField, Min(1)] private int wavesToEnd = 3;
    [SerializeField] private AnimationCurve progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private WaveManager waveManager;
    private EconomyManager wallet;
    private float elapsedRunSeconds;
    private bool runStarted;

    public void Configure(ProgressSource source, float fromHour, float toHour, float seconds, int waveCount,
        AnimationCurve curve)
    {
        if (sun == null) sun = GetComponent<Light>();
        progressSource = source;
        startHour = Mathf.Clamp(fromHour, 0f, 24f);
        endHour = Mathf.Clamp(toHour, 0f, 24f);
        durationSeconds = Mathf.Max(0.01f, seconds);
        wavesToEnd = Mathf.Max(1, waveCount);
        progressCurve = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
        ApplyProgress(0f);
    }

    private void Start()
    {
        if (sun == null) sun = GetComponent<Light>();
        if (sun == null)
        {
            enabled = false;
            return;
        }

        waveManager = WaveManager.GetOrCreate();
        waveManager.CompletedWavesChanged += HandleCompletedWavesChanged;
        HandleCompletedWavesChanged(waveManager.CompletedWaves);

        wallet = EconomyManager.Instance;
        if (wallet != null)
        {
            wallet.RunStarted += HandleRunStarted;
            if (wallet.IsInitialized) HandleRunStarted(Guid.Empty);
        }
        else
        {
            runStarted = true;
        }
    }

    private void OnDestroy()
    {
        if (waveManager != null) waveManager.CompletedWavesChanged -= HandleCompletedWavesChanged;
        if (wallet != null) wallet.RunStarted -= HandleRunStarted;
    }

    private void Update()
    {
        if (wallet == null) BindWallet();
        if (progressSource != ProgressSource.InGameTime || !runStarted) return;

        elapsedRunSeconds += Time.deltaTime;
        ApplyProgress(elapsedRunSeconds / durationSeconds);
    }

    private void OnValidate()
    {
        startHour = Mathf.Clamp(startHour, 0f, 24f);
        endHour = Mathf.Clamp(endHour, 0f, 24f);
        durationSeconds = Mathf.Max(0.01f, durationSeconds);
        wavesToEnd = Mathf.Max(1, wavesToEnd);
        if (progressCurve == null || progressCurve.length == 0)
            progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    private void HandleRunStarted(Guid runId)
    {
        elapsedRunSeconds = 0f;
        runStarted = true;
        if (progressSource == ProgressSource.InGameTime) ApplyProgress(0f);
    }

    private void BindWallet()
    {
        wallet = EconomyManager.Instance;
        if (wallet == null) return;

        wallet.RunStarted += HandleRunStarted;
        if (wallet.IsInitialized) HandleRunStarted(wallet.RunId);
    }

    private void HandleCompletedWavesChanged(int completedWaves)
    {
        if (progressSource == ProgressSource.CompletedWaves)
            ApplyProgress(completedWaves / (float)wavesToEnd);
    }

    private void ApplyProgress(float progress)
    {
        if (sun == null) sun = GetComponent<Light>();
        if (sun == null) return;

        float normalized = Mathf.Clamp01(progress);
        float evaluated = progressCurve != null ? progressCurve.Evaluate(normalized) : normalized;
        float hour = Mathf.Lerp(startHour, endHour, Mathf.Clamp01(evaluated));
        // With latitude and axial tilt omitted, the sun follows an east-west arc overhead at noon.
        float hourAngle = (hour - 12f) * 15f;
        Vector3 sunDirection = new Vector3(-Mathf.Sin(hourAngle * Mathf.Deg2Rad),
            Mathf.Cos(hourAngle * Mathf.Deg2Rad), 0f);
        sun.transform.rotation = Quaternion.LookRotation(-sunDirection, Vector3.forward);
    }
}
