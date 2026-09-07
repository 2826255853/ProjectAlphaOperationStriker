using System;
using UnityEngine;

/// <summary>Runtime health for enemies that can be damaged by player weapons.</summary>
[DisallowMultipleComponent]
public sealed class EnemyHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<EnemyHealth> Damaged;
    public event Action<EnemyHealth> Died;

    private bool deathRaised;

    private void Awake()
    {
        CurrentHealth = Mathf.Max(1f, maxHealth);
    }

    /// <summary>Apply positive damage and destroy the enemy when health reaches zero.</summary>
    public void TakeDamage(float amount)
    {
        if (deathRaised || amount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Damaged?.Invoke(this);

        if (CurrentHealth > 0f)
        {
            return;
        }

        deathRaised = true;
        Died?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>Restores this instance to full health when reused from a pool.</summary>
    public void ResetHealth()
    {
        deathRaised = false;
        CurrentHealth = Mathf.Max(1f, maxHealth);
    }
}
