using System;
using UnityEngine;

/// <summary>Damage receiver and ground position for the local FPS player.</summary>
[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    private CharacterController characterController;

    public static PlayerHealth Instance { get; private set; }
    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<PlayerHealth> Damaged;
    public event Action<PlayerHealth> Died;

    public Vector3 FeetPosition
    {
        get
        {
            if (characterController == null || !characterController.enabled) return transform.position;
            Bounds bounds = characterController.bounds;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        characterController = GetComponentInChildren<CharacterController>();
        ResetHealth();
    }

    private void OnEnable() => Instance = this;
    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Damaged?.Invoke(this);
        if (IsDead) Died?.Invoke(this);
    }

    public void ResetHealth() => CurrentHealth = Mathf.Max(1f, maxHealth);
}
