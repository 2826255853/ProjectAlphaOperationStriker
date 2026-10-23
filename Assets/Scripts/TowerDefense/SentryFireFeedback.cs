using System;
using UnityEngine;

/// <summary>
/// Procedural firing feedback for the sentry machine gun art prefab. It owns the
/// <c>Gun Recoil</c> node (local -Z kick + muzzle lift) and the <c>Muzzle Flash</c>
/// mesh toggle, and it is driven only by <see cref="AutoSentryTurret.ShotFired"/>.
///
/// There is deliberately no Animator/AnimationClip: the whole weapon recoil is a
/// deterministic function of shot progress (<see cref="EvaluateRecoil01"/>), which
/// keeps EditMode sampling and offline previews identical to runtime playback.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tower Defense/Sentry Fire Feedback")]
public sealed class SentryFireFeedback : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform recoilRoot;
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject muzzleFlash;

    [Header("Recoil")]
    [SerializeField, Min(0f)] private float recoilDistance = 0.04f;
    [SerializeField, Min(0f)] private float recoilLiftDegrees = 3f;
    [Tooltip("Fraction of one shot spent kicking back before the spring returns home.")]
    [SerializeField, Range(0.02f, 0.9f)] private float kickInFraction = 0.18f;
    [SerializeField, Min(0.01f)] private float singleShotDuration = 0.11f;

    [Header("Muzzle Flash")]
    [SerializeField, Min(0f)] private float muzzleFlashDuration = 0.045f;
    [SerializeField, Min(0f)] private float muzzleFlashScaleJitter = 0.18f;

    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private Vector3 flashRestScale = Vector3.one;
    private Transform flashTransform;
    private AutoSentryTurret turret;
    private bool initialized;
    private float shotTime;
    private float flashTime;
    private uint randomState = 0x9E3779B9u;

    public Transform RecoilRoot => recoilRoot;
    public Transform Muzzle => muzzle;
    public GameObject MuzzleFlash => muzzleFlash;
    public float SingleShotDuration => singleShotDuration;
    public bool IsRecoiling => shotTime > 0f;
    public bool IsMuzzleFlashVisible => flashTime > 0f;
    public float RecoilDistance => recoilDistance;
    public float RecoilLiftDegrees => recoilLiftDegrees;

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
        ResetToRest();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>Lazily captures the rest pose and resolves the owned nodes.</summary>
    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        if (recoilRoot != null)
        {
            restLocalPosition = recoilRoot.localPosition;
            restLocalRotation = recoilRoot.localRotation;
        }
        if (muzzleFlash != null)
        {
            flashTransform = muzzleFlash.transform;
            flashRestScale = flashTransform.localScale;
            muzzleFlash.SetActive(false);
        }
        Apply(0f);
    }

    /// <summary>Pure recoil amplitude in 0..1 for a normalized shot progress in 0..1.</summary>
    public float EvaluateRecoil01(float progress)
    {
        if (progress <= 0f || progress >= 1f) return 0f;
        float kickIn = Mathf.Clamp(kickInFraction, 0.02f, 0.9f);
        if (progress < kickIn)
        {
            float t = progress / kickIn;
            return 1f - (1f - t) * (1f - t); // ease out into the peak
        }
        float d = (progress - kickIn) / (1f - kickIn);
        return (1f - d) * (1f - d); // ease in back to zero
    }

    /// <summary>Advances recoil and muzzle flash timers. Public for tests and previews.</summary>
    public void Tick(float deltaTime)
    {
        EnsureInitialized();
        if (deltaTime < 0f) deltaTime = 0f;

        if (shotTime > 0f)
        {
            shotTime -= deltaTime;
            if (shotTime <= 0f)
            {
                shotTime = 0f;
                Apply(0f);
            }
            else
            {
                Apply(EvaluateRecoil01(1f - shotTime / singleShotDuration));
            }
        }

        if (flashTime > 0f)
        {
            flashTime -= deltaTime;
            if (flashTime <= 0f)
            {
                flashTime = 0f;
                HideMuzzleFlash();
            }
        }
    }

    /// <summary>Restarts the recoil cycle and flashes the muzzle. Called once per shot.</summary>
    public void PlayShot()
    {
        EnsureInitialized();
        shotTime = singleShotDuration;
        Apply(EvaluateRecoil01(0.0001f));

        if (muzzleFlash == null) return;
        flashTime = muzzleFlashDuration;
        muzzleFlash.SetActive(true);
        if (flashTransform == null) flashTransform = muzzleFlash.transform;
        flashTransform.localRotation = Quaternion.Euler(0f, 0f, NextFloat(0f, 360f));
        float jitter = 1f + NextFloat(-muzzleFlashScaleJitter, muzzleFlashScaleJitter);
        flashTransform.localScale = flashRestScale * Mathf.Max(0.05f, jitter);
    }

    /// <summary>Returns the recoil node to its authored pose with zero offset.</summary>
    public void ResetToRest()
    {
        EnsureInitialized();
        shotTime = 0f;
        flashTime = 0f;
        HideMuzzleFlash();
        Apply(0f);
    }

    private void Apply(float amount01)
    {
        if (recoilRoot == null) return;
        float amount = Mathf.Clamp01(amount01);
        recoilRoot.localPosition = restLocalPosition + Vector3.back * (recoilDistance * amount);
        recoilRoot.localRotation = restLocalRotation * Quaternion.Euler(-recoilLiftDegrees * amount, 0f, 0f);
    }

    private void HideMuzzleFlash()
    {
        if (muzzleFlash != null && muzzleFlash.activeSelf) muzzleFlash.SetActive(false);
        if (flashTransform != null) flashTransform.localScale = flashRestScale;
    }

    private void BindTurret()
    {
        if (turret != null) return;
        turret = GetComponentInParent<AutoSentryTurret>();
    }

    private void Subscribe()
    {
        if (turret == null) return;
        turret.ShotFired -= HandleShotFired;
        turret.ShotFired += HandleShotFired;
    }

    private void Unsubscribe()
    {
        if (turret == null) return;
        turret.ShotFired -= HandleShotFired;
    }

    private void HandleShotFired(Vector3 muzzleWorldPosition)
    {
        PlayShot();
    }

    /// <summary>Deterministic small-range generator so previews and tests agree.</summary>
    private float NextFloat(float min, float max)
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return Mathf.Lerp(min, max, (randomState & 0xFFFFFF) / 16777215f);
    }
}
