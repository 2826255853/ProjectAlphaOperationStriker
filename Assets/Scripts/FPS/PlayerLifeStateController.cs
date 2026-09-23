using System.Collections;
using System.Collections.Generic;
using KINEMATION.FPSAnimationPack.Scripts.Player;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Coordinates gameplay control and observer mode around player life state.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerObserverMode))]
public sealed class PlayerLifeStateController : MonoBehaviour
{
    [Header("Respawn")]
    [SerializeField, Min(0f)] private float respawnDelay = 3f;
    [SerializeField] private Transform respawnPoint;

    private readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
    private readonly List<Collider> disabledColliders = new List<Collider>();
    private PlayerHealth health;
    private PlayerObserverMode observerMode;
    private bool gameplaySuspended;
    private Coroutine respawnRoutine;
    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;

    public bool IsObserverMode => observerMode != null && observerMode.IsActive;
    public float RespawnDelay
    {
        get => respawnDelay;
        set => respawnDelay = Mathf.Max(0f, value);
    }

    public Transform RespawnPoint
    {
        get => respawnPoint;
        set => respawnPoint = value;
    }

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        observerMode = GetComponent<PlayerObserverMode>();
        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;
        if (respawnPoint == null) respawnPoint = FindSceneRespawnPoint();
    }

    private void OnEnable()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        if (observerMode == null) observerMode = GetComponent<PlayerObserverMode>();
        health.Died += HandleDied;
        health.Revived += HandleRevived;
        if (health.IsDead) HandleDied(health);
    }

    private void OnDisable()
    {
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }

        if (health == null) return;
        health.Died -= HandleDied;
        health.Revived -= HandleRevived;
    }

    /// <summary>Enters observer mode while alive or dead.</summary>
    public bool TryEnterObserverMode()
    {
        if (IsObserverMode) return true;
        SuspendGameplay();
        if (observerMode.TryEnterObserverMode()) return true;

        if (!health.IsDead) RestoreGameplay();
        return false;
    }

    /// <summary>Returns to gameplay only while the player is alive.</summary>
    public bool TryExitObserverMode()
    {
        if (health.IsDead || !IsObserverMode) return false;
        observerMode.ExitObserverMode();
        RestoreGameplay();
        return true;
    }

    private void HandleDied(PlayerHealth _)
    {
        TryEnterObserverMode();
        if (respawnRoutine == null) respawnRoutine = StartCoroutine(RespawnAfterDelay());
    }

    private void HandleRevived(PlayerHealth _)
    {
        if (IsObserverMode) observerMode.ExitObserverMode();
        RestoreGameplay();
        LockCursorForGameplay();
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, respawnDelay));
        respawnRoutine = null;

        if (!health.IsDead) yield break;
        if (IsObserverMode) observerMode.ExitObserverMode();

        Transform target = respawnPoint;
        Vector3 position = target != null ? target.position : initialSpawnPosition;
        Quaternion rotation = target != null ? target.rotation : initialSpawnRotation;

        // CharacterController and weapon/input components remain suspended until
        // ResetHealth raises Revived, so teleporting cannot be blocked by physics.
        transform.SetPositionAndRotation(position, rotation);
        health.ResetHealth();
        LockCursorForGameplay();
    }

    private Transform FindSceneRespawnPoint()
    {
        string[] names = { "Player Spawn Point", "PlayerSpawn", "Player Respawn", "PlayerRespawn", "RespawnPoint" };
        foreach (string name in names)
        {
            GameObject candidate = GameObject.Find(name);
            if (candidate != null && candidate.transform != transform) return candidate.transform;
        }

        return null;
    }

    private static void LockCursorForGameplay()
    {
        if (TrapSelectionMenu.CursorOwned) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void SuspendGameplay()
    {
        if (gameplaySuspended) return;
        gameplaySuspended = true;

        foreach (Behaviour behaviour in GetComponentsInChildren<Behaviour>(true))
        {
            if (!ControlsPlayer(behaviour) || !behaviour.enabled) continue;
            disabledBehaviours.Add(behaviour);
            behaviour.enabled = false;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled) continue;
            disabledColliders.Add(collider);
            collider.enabled = false;
        }
    }

    private void RestoreGameplay()
    {
        foreach (Collider collider in disabledColliders)
            if (collider != null) collider.enabled = true;
        disabledColliders.Clear();

        foreach (Behaviour behaviour in disabledBehaviours)
            if (behaviour != null) behaviour.enabled = true;
        disabledBehaviours.Clear();
        gameplaySuspended = false;
    }

    private bool ControlsPlayer(Behaviour behaviour)
    {
        if (behaviour == this || behaviour == health || behaviour == observerMode
            || observerMode.IsDriverComponent(behaviour)) return false;
        return behaviour is FirstPersonController
               || behaviour is FPSPackagePlayerMotion
               || behaviour is FPSHitscanShooter
               || behaviour is FPSPlayer
               || behaviour is PlayerInput;
    }
}
