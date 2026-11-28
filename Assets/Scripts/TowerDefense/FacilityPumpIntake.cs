using UnityEngine;

public sealed class FacilityPumpIntake : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [SerializeField] private EnemySpawnPoint intake;
    [SerializeField] private EnemySpawnPoint rearApproach;
    [SerializeField] private Renderer beacon;
    [SerializeField] private AudioSource warning;
    [SerializeField] private bool freightSide;

    private MaterialPropertyBlock beaconProperties;
    private AudioClip impact;
    private bool warningPlayed;

    public void Configure(EnemySpawnPoint entrance, EnemySpawnPoint rear, Renderer indicator,
        AudioSource audioSource, bool freight)
    {
        intake = entrance;
        rearApproach = rear;
        beacon = indicator;
        warning = audioSource;
        freightSide = freight;
    }

    private void Awake()
    {
        beaconProperties = new MaterialPropertyBlock();
        impact = CreateImpact(freightSide);
    }

    private void Update()
    {
        // Sound the alarm as soon as the rear group arrives, before the wing wave.
        bool armed = (rearApproach.CurrentWave == 2 && rearApproach.SpawnedInCurrentWave > 0)
            || rearApproach.CurrentWave >= 3 || intake.CurrentWave >= 3;
        if (armed && !warningPlayed)
            warning.PlayOneShot(impact);
        warningPlayed = armed;

        Color color = armed ? Color.Lerp(new Color(.46f, .12f, .02f), new Color(1f, .68f, .1f),
            Mathf.PingPong(Time.time * 3f, 1f)) : new Color(.22f, .09f, .035f);
        beaconProperties.SetColor(BaseColor, color);
        beaconProperties.SetColor(ColorProperty, color);
        beacon.SetPropertyBlock(beaconProperties);
    }

    private void OnDestroy()
    {
        if (impact != null) Destroy(impact);
    }

    private static AudioClip CreateImpact(bool freight)
    {
        const int rate = 16000;
        var samples = new float[rate * 2];
        float[] strikes = freight ? new[] { 0f, .47f, .84f } : new[] { 0f, .16f, .29f, .58f };
        float pitch = freight ? 73f : 263f;
        foreach (float strike in strikes)
        {
            int start = Mathf.RoundToInt(strike * rate);
            for (int i = start; i < samples.Length; i++)
            {
                float t = (i - start) / (float)rate;
                float phase = 2f * Mathf.PI * pitch * t;
                float ring = Mathf.Sin(phase) + .46f * Mathf.Sin(phase * 2.73f) + .24f * Mathf.Sin(phase * 5.19f);
                samples[i] += .48f * (1f - strike * .65f) * ring * Mathf.Min(t * 800f, 1f)
                    * Mathf.Exp(-t * (freight ? 6f : 13f));
            }
        }
        var clip = AudioClip.Create(freight ? "Flat wagon coupler impact" : "Falling canal guardrail", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
