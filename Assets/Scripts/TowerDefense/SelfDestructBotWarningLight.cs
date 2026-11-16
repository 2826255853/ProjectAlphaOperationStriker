using UnityEngine;

/// <summary>Drives the bomb warning glow while the robot is priming to detonate.</summary>
[DisallowMultipleComponent]
public sealed class SelfDestructBotWarningLight : MonoBehaviour
{
    [SerializeField] private SelfDestructBotEnemy enemy;
    [SerializeField] private Renderer[] warningRenderers;
    [SerializeField, Min(0.05f)] private float blinkFrequency = 3.5f;
    [SerializeField, Min(0f)] private float idleEmission = 0.15f;
    [SerializeField, Min(0f)] private float peakEmission = 8f;

    private MaterialPropertyBlock block;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (enemy == null) enemy = GetComponent<SelfDestructBotEnemy>();
        if (warningRenderers == null || warningRenderers.Length == 0)
            warningRenderers = FindWarningRenderers();
        block = new MaterialPropertyBlock();
    }

    private void OnEnable() => ApplyEmission(idleEmission);
    private void OnDisable() => ApplyEmission(idleEmission);

    private void Update()
    {
        bool priming = enemy != null && enemy.IsPriming;
        float pulse = priming ? Mathf.PingPong(Time.time * blinkFrequency * 2f, 1f) : idleEmission;
        ApplyEmission(priming ? Mathf.Lerp(idleEmission, peakEmission, pulse) : idleEmission);
    }

    private void ApplyEmission(float intensity)
    {
        if (warningRenderers == null) return;
        for (int i = 0; i < warningRenderers.Length; i++)
        {
            Renderer target = warningRenderers[i];
            if (target == null) continue;
            Material[] materials = target.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                if (material == null || !material.HasProperty(EmissionColorId)) continue;
                Color baseEmission = material.GetColor(EmissionColorId);
                target.GetPropertyBlock(block, slot);
                block.SetColor(EmissionColorId, baseEmission * intensity);
                target.SetPropertyBlock(block, slot);
            }
        }
    }

    private Renderer[] FindWarningRenderers()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        var matches = new System.Collections.Generic.List<Renderer>();
        foreach (Renderer candidate in all)
        {
            foreach (Material material in candidate.sharedMaterials)
            {
                if (material != null && material.name.Contains("BombGlow"))
                {
                    matches.Add(candidate);
                    break;
                }
            }
        }
        return matches.ToArray();
    }
}
