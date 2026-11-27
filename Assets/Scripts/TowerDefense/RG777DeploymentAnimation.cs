using UnityEngine;

/// <summary>Folds the RG777 from its authored ground-contact pose and sequences the power buses.</summary>
[DisallowMultipleComponent]
public sealed class RG777DeploymentAnimation : MonoBehaviour
{
    [SerializeField] private Transform[] legs;
    [SerializeField] private Transform[] spades;

    [System.Serializable]
    private struct Lamp
    {
        public Renderer renderer;
        [Range(0, 1)] public float ignition;
    }

    [SerializeField] private Lamp[] lamps;
    private Quaternion[] legRest, spadeRest;
    private Vector3[] legAxes, spadeAxes;
    private MaterialPropertyBlock block;
    private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
    private static readonly int Tint = Shader.PropertyToID("_BaseColor");
    private float startedAt;

    private void CaptureRestPose()
    {
        if (legRest != null) return;
        legRest = new Quaternion[legs.Length];
        spadeRest = new Quaternion[legs.Length];
        legAxes = new Vector3[legs.Length];
        spadeAxes = new Vector3[legs.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            Vector3 axis = Vector3.Cross(spades[i].position - legs[i].position, transform.up).normalized;
            legAxes[i] = legs[i].InverseTransformDirection(axis);
            spadeAxes[i] = spades[i].InverseTransformDirection(axis);
            legRest[i] = legs[i].localRotation;
            spadeRest[i] = spades[i].localRotation;
        }
        block = new MaterialPropertyBlock();
    }

    public void Begin(bool preview)
    {
        startedAt = Time.time;
        SetProgress(0f);
        enabled = !preview;
        if (preview)
        {
            // The placement controller owns the ghost's green/red base tint.
            block.Clear();
            block.SetColor(Emission, Color.black);
            foreach (Lamp lamp in lamps) lamp.renderer.SetPropertyBlock(block);
        }
    }

    private void LateUpdate()
    {
        float progress = (Time.time - startedAt) / TrapInstance.DeploymentCooldownSeconds;
        SetProgress(Mathf.Clamp01(progress));
        if (progress >= 1f) enabled = false;
    }

    public void SetProgress(float progress)
    {
        CaptureRestPose();
        for (int i = 0; i < legs.Length; i++)
        {
            float delay = i * .035f;
            float leg = Mathf.SmoothStep(62f, 0f, Mathf.InverseLerp(delay, .59f + delay, progress));
            // Level the shoe before the leg settles, keeping its leading edge above the floor.
            float foot = Mathf.SmoothStep(-79f, 0f, Mathf.InverseLerp(.14f + delay, .54f + delay, progress));
            legs[i].localRotation = legRest[i] * Quaternion.AngleAxis(leg, legAxes[i]);
            spades[i].localRotation = spadeRest[i] * Quaternion.AngleAxis(foot, spadeAxes[i]);
        }
        foreach (Lamp lamp in lamps)
        {
            if (progress >= 1f)
            {
                lamp.renderer.SetPropertyBlock(null);
                continue;
            }
            float rise = Mathf.Clamp01((progress - lamp.ignition) / .065f);
            float pulse = Mathf.Max(0f, 1f - Mathf.Abs(progress - lamp.ignition - .065f) / .065f);
            Material material = lamp.renderer.sharedMaterial;
            block.SetColor(Tint, Color.Lerp(new Color(.025f, .035f, .04f), material.GetColor(Tint), rise));
            block.SetColor(Emission, material.GetColor(Emission) * (rise + .85f * pulse));
            lamp.renderer.SetPropertyBlock(block);
        }
    }
}
