using UnityEngine;

public sealed class FacilityPumpIntake : MonoBehaviour
{
    private const float OpenSeconds = 4f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [SerializeField] private EnemySpawnPoint intake;
    [SerializeField] private EnemySpawnPoint rearApproach;
    [SerializeField] private Transform shutter;
    [SerializeField] private Renderer shutterPanel;
    [SerializeField] private Renderer beacon;
    [SerializeField] private AudioSource warning;

    private MaterialPropertyBlock beaconProperties;
    private Vector3 closedScale;
    private Collider shutterCollider;
    private AudioClip spinDown;
    private bool warningPlayed;

    public void Configure(EnemySpawnPoint pump, EnemySpawnPoint rear, Transform gate, Renderer panel,
        Renderer indicator, AudioSource audioSource)
    {
        intake = pump;
        rearApproach = rear;
        shutter = gate;
        shutterPanel = panel;
        beacon = indicator;
        warning = audioSource;
    }

    private void Awake()
    {
        closedScale = shutter.localScale;
        beaconProperties = new MaterialPropertyBlock();
        shutterCollider = shutterPanel.GetComponent<BoxCollider>();
        spinDown = CreateSpinDown();
        Update();
    }

    private void Update()
    {
        bool thirdWave = intake.CurrentWave >= 3;
        float open = thirdWave ? Mathf.Clamp01(1f - intake.RemainingWaitTime / OpenSeconds) : 0f;
        shutter.localScale = new Vector3(closedScale.x, closedScale.y * Mathf.Max(.02f, 1f - open), closedScale.z);
        shutterCollider.enabled = open < .95f;

        // Rear approaches advance to wave three immediately after spawning their wave-two enemy.
        bool armed = rearApproach.CurrentWave >= 3 || thirdWave;
        if (armed && !warningPlayed)
        {
            warning.PlayOneShot(spinDown);
        }
        warningPlayed = armed;

        Color color = !armed ? new Color(.22f, .09f, .035f)
            : open >= 1f ? new Color(.13f, .88f, .47f)
            : Color.Lerp(new Color(.44f, .16f, .02f), new Color(1f, .67f, .12f),
                Mathf.PingPong(Time.time * 3f, 1f));
        beaconProperties.SetColor(BaseColor, color);
        beaconProperties.SetColor(ColorProperty, color);
        beacon.SetPropertyBlock(beaconProperties);
    }

    private void OnDestroy()
    {
        if (spinDown != null) Destroy(spinDown);
    }

    private static AudioClip CreateSpinDown()
    {
        const int sampleRate = 16000;
        var samples = new float[sampleRate * 3 / 2];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float progress = i / (float)samples.Length;
            float frequency = Mathf.Lerp(106f, 29f, progress);
            phase += 2f * Mathf.PI * frequency / sampleRate;
            float envelope = Mathf.Clamp01(progress * 12f) * (1f - progress) * (1f - progress);
            samples[i] = (Mathf.Sin(phase) + .3f * Mathf.Sin(phase * 2f)) * envelope * .55f;
        }
        var clip = AudioClip.Create("Cooling pump spin-down", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
