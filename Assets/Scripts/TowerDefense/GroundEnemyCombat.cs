using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Ground enemies prefer a nearby reachable player, then traps, then their mission route.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterPathFollower), typeof(EnemyHealth))]
[DefaultExecutionOrder(-10)]
public sealed class GroundEnemyCombat : MonoBehaviour
{
    [Header("Player priority")]
    [SerializeField, Min(0f), Tooltip("玩家进入此距离且站在可达道路上时，优先追击玩家。")]
    private float playerDetectionDistance = 8f;
    [SerializeField, Min(0f), Tooltip("追击中的玩家超过此距离时，放弃玩家并重新选择陷阱。")]
    private float playerDisengageDistance = 12f;
    [SerializeField, Min(0f), Tooltip("目标脚下与道路的最大高度差，防止追击高台上的玩家。")]
    private float roadHeightTolerance = 0.6f;

    [Header("Trap targeting")]
    [SerializeField, Min(0f)] private float trapDetectionDistance = 8f;

    [Header("Melee attack")]
    [SerializeField, Min(0.1f)] private float attackRange = 1.5f;
    [SerializeField, Min(0f)] private float attackDamage = 10f;
    [SerializeField, Min(0.01f)] private float attackInterval = 1f;
    [SerializeField, Min(0.05f)] private float repathInterval = 0.25f;

    private readonly List<Vector3> candidatePath = new List<Vector3>();
    private readonly List<Vector3> selectedPath = new List<Vector3>();
    private MonsterPathFollower follower;
    private EnemyHealth health;
    private PlayerHealth playerTarget;
    private TrapInstance trapTarget;
    private Vector3 pathEnd;
    private float nextRepathTime;
    private float nextAttackTime;

    public Transform CurrentTarget => playerTarget != null ? playerTarget.transform
        : trapTarget != null ? trapTarget.transform : null;
    public bool IsTargetingPlayer => playerTarget != null;
    public bool IsAttacking { get; private set; }
    public event Action<GroundEnemyCombat, Transform> Attacked;

    private void Awake()
    {
        follower = GetComponent<MonsterPathFollower>();
        health = GetComponent<EnemyHealth>();
    }

    private void OnValidate()
    {
        playerDetectionDistance = Mathf.Max(0f, playerDetectionDistance);
        playerDisengageDistance = Mathf.Max(playerDetectionDistance, playerDisengageDistance);
        roadHeightTolerance = Mathf.Max(0f, roadHeightTolerance);
        trapDetectionDistance = Mathf.Max(0f, trapDetectionDistance);
        attackRange = Mathf.Max(0.1f, attackRange);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackInterval = Mathf.Max(0.01f, attackInterval);
        repathInterval = Mathf.Max(0.05f, repathInterval);
    }

    private void Update()
    {
        IsAttacking = false;
        if (!follower.IsInitialized || follower.IsFlying || follower.HasArrived) return;
        if (health != null && health.IsDead)
        {
            follower.CombatMovementPaused = true;
            return;
        }

        // Check range/road validity every frame so a player cannot be hit after leaving the road.
        bool targetInvalid = playerTarget != null ? !CanTargetPlayer(playerTarget, playerDisengageDistance)
            : trapTarget != null ? !CanTargetTrap(trapTarget) : follower.IsFollowingCombatPath;
        if (targetInvalid || Time.time >= nextRepathTime)
        {
            SelectTarget();
            nextRepathTime = Time.time + repathInterval;
        }
        if (CurrentTarget == null) return;

        Vector3 targetPosition = playerTarget != null ? playerTarget.FeetPosition : trapTarget.transform.position;
        float distance = PlanarDistance(transform.position, targetPosition);
        // Requiring the remaining route to fit in melee range prevents attacks across closed cells/corners.
        float remainingDistance = follower.RemainingPathDistance + PlanarDistance(pathEnd, targetPosition);
        IsAttacking = distance <= attackRange && remainingDistance <= attackRange;
        follower.CombatMovementPaused = IsAttacking;
        if (!IsAttacking) return;

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), 720f * Time.deltaTime);
        if (Time.time < nextAttackTime) return;

        nextAttackTime = Time.time + attackInterval;
        Transform attackedTarget = CurrentTarget;
        if (playerTarget != null) playerTarget.TakeDamage(attackDamage);
        else trapTarget.TakeDamage(attackDamage);
        Debug.DrawLine(transform.position + Vector3.up, targetPosition + Vector3.up, Color.yellow, 0.2f);
        Attacked?.Invoke(this, attackedTarget);
    }

    private void SelectTarget()
    {
        PlayerHealth player = PlayerHealth.Instance;
        float playerRange = player != null && player == playerTarget
            ? playerDisengageDistance : playerDetectionDistance;
        if (CanTargetPlayer(player, playerRange)
            && TryBuildCombatPath(player.FeetPosition, true, playerDisengageDistance, out _))
        {
            playerTarget = player;
            trapTarget = null;
            BeginCombatPath(candidatePath);
            return;
        }

        playerTarget = null;
        // Keep attacking the same trap until it is destroyed or becomes unreachable.
        if (CanTargetTrap(trapTarget)
            && TryBuildCombatPath(trapTarget.transform.position, false, trapDetectionDistance * 2f, out _))
        {
            BeginCombatPath(candidatePath);
            return;
        }

        trapTarget = null;
        float bestDistance = float.PositiveInfinity;
        foreach (TrapInstance trap in TrapInstance.ActiveTraps)
        {
            if (!CanTargetTrap(trap)
                || !TryBuildCombatPath(trap.transform.position, false, trapDetectionDistance * 2f, out float distance)
                || distance >= bestDistance) continue;
            trapTarget = trap;
            bestDistance = distance;
            selectedPath.Clear();
            selectedPath.AddRange(candidatePath);
        }
        if (trapTarget != null) BeginCombatPath(selectedPath);
        else follower.ResumeMissionPath();
    }

    private bool CanTargetPlayer(PlayerHealth player, float range)
    {
        if (player == null || !player.isActiveAndEnabled || player.IsDead || follower.PathGrid == null) return false;
        Vector3 feet = player.FeetPosition;
        return PlanarDistance(transform.position, feet) <= range
            && follower.PathGrid.TryWorldToCell(feet, out Vector2Int cell)
            && follower.PathGrid.IsOpen(cell)
            && Mathf.Abs(feet.y - follower.PathGrid.CellToWorld(cell).y) <= roadHeightTolerance;
    }

    private bool CanTargetTrap(TrapInstance trap)
    {
        if (trap == null || !trap.isActiveAndEnabled || trap.IsDestroyed) return false;
        Vector3 position = trap.transform.position;
        return PlanarDistance(transform.position, position) <= trapDetectionDistance
            && Mathf.Abs(position.y - transform.position.y) <= roadHeightTolerance;
    }

    private bool TryBuildCombatPath(Vector3 target, bool requireOpenTarget, float maxDistance, out float distance)
    {
        candidatePath.Clear();
        distance = 0f;
        MonsterPathGrid grid = follower.PathGrid;
        if (grid != null)
        {
            // Combat cannot snap the monster through a blocked cell to another road.
            if (!grid.TryWorldToCell(transform.position, out Vector2Int start) || !grid.IsOpen(start)) return false;
            if (!grid.TryFindPath(transform.position, target, target - transform.position,
                    candidatePath, out bool reachedTarget)) return false;
            if (requireOpenTarget && !reachedTarget) return false;
            // A roadside trap can be attacked from an adjacent reachable road cell.
            if (!reachedTarget && PlanarDistance(candidatePath[candidatePath.Count - 1], target) > attackRange) return false;
        }
        else candidatePath.Add(target);

        Vector3 previous = transform.position;
        foreach (Vector3 point in candidatePath)
        {
            distance += PlanarDistance(previous, point);
            previous = point;
        }
        distance += PlanarDistance(previous, target);
        return distance <= maxDistance;
    }

    private void BeginCombatPath(List<Vector3> path)
    {
        pathEnd = path[path.Count - 1];
        follower.FollowCombatPath(path);
    }

    private void OnDisable()
    {
        playerTarget = null;
        trapTarget = null;
        IsAttacking = false;
        if (follower != null && (health == null || !health.IsDead)) follower.ResumeMissionPath();
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }
}
