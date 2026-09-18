using UnityEngine;

/// <summary>
/// Lead/intercept maths for the dual missile launcher.
///
/// The mount aims at the predicted intercept point instead of the enemy's
/// current position, so the barrel is already pointing where the missile has to
/// fly (air-attack plan, rule R3). Keeping the solver in one place means the
/// mount and the missiles always agree on the same prediction.
/// </summary>
public static class MissileAimSolver
{
    /// <summary>Upper bound on the fixed-point refinement rounds.</summary>
    public const int MaxIterations = 3;

    /// <summary>
    /// Predicts where a constant-velocity target will be when a projectile of
    /// <paramref name="projectileSpeed"/> can reach it.
    ///
    /// Returns false when the solution is not usable: non-positive projectile
    /// speed, divergent iteration (a target faster than the missile), or a NaN /
    /// Infinity result. In that case <paramref name="interceptPoint"/> falls back
    /// to the target's current position and <paramref name="timeToImpact"/> is 0,
    /// so callers can still aim somewhere sensible.
    /// </summary>
    /// <param name="maxTimeToImpact">Optional sanity cap; values at or below 0 derive a default from the distance.</param>
    public static bool TrySolveIntercept(
        Vector3 shooter,
        Vector3 targetPosition,
        Vector3 targetVelocity,
        float projectileSpeed,
        out Vector3 interceptPoint,
        out float timeToImpact,
        float maxTimeToImpact = 0f)
    {
        interceptPoint = targetPosition;
        timeToImpact = 0f;

        if (projectileSpeed <= 0.0001f) return false;

        float maxTime = maxTimeToImpact > 0f
            ? maxTimeToImpact
            : DefaultMaxTime(shooter, targetPosition, projectileSpeed);

        // Fixed-point iteration: t(n+1) = |P + V*t(n) - S| / projectileSpeed.
        float t = Vector3.Distance(shooter, targetPosition) / projectileSpeed;
        if (!IsFinite(t) || t > maxTime) return false;

        for (int i = 0; i < MaxIterations; i++)
        {
            Vector3 predicted = targetPosition + targetVelocity * t;
            if (!IsFinite(predicted)) return false;

            float next = (predicted - shooter).magnitude / projectileSpeed;
            if (!IsFinite(next) || next > maxTime) return false;

            if (Mathf.Abs(next - t) <= 0.0001f)
            {
                t = next;
                break;
            }
            t = next;
        }

        Vector3 point = targetPosition + targetVelocity * t;
        if (!IsFinite(point)) return false;

        interceptPoint = point;
        timeToImpact = t;
        return true;
    }

    private static float DefaultMaxTime(Vector3 shooter, Vector3 targetPosition, float projectileSpeed)
    {
        float distance = Vector3.Distance(shooter, targetPosition);
        return Mathf.Max(distance, 1f) * 4f / projectileSpeed;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
}

/// <summary>
/// Smoothed world-space velocity estimate for a moving transform.
///
/// <see cref="MonsterPathFollower"/> does not expose a velocity, so both the
/// mount and the missiles sample positions through this tracker instead of
/// writing two different differentiation routines.
/// </summary>
public struct MissileVelocityTracker
{
    private Vector3 lastPosition;
    private Vector3 velocity;
    private float lastTime;
    private bool hasSample;

    public Vector3 Velocity => velocity;
    public bool HasSample => hasSample;

    /// <summary>Feeds a new sample and returns the exponentially smoothed velocity.</summary>
    public Vector3 Sample(Vector3 position, float time, float smoothingSeconds)
    {
        if (hasSample)
        {
            float deltaTime = time - lastTime;
            if (deltaTime > 0.0001f)
            {
                Vector3 raw = (position - lastPosition) / deltaTime;
                float smoothing = Mathf.Max(0f, smoothingSeconds);
                float blend = smoothing <= 0.0001f ? 1f : 1f - Mathf.Exp(-deltaTime / smoothing);
                velocity = Vector3.Lerp(velocity, raw, blend);
            }
        }

        lastPosition = position;
        lastTime = time;
        hasSample = true;
        return velocity;
    }

    public void Reset()
    {
        velocity = Vector3.zero;
        hasSample = false;
    }
}
