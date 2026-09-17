using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves an enemy along a MonsterPathGrid route toward a destination.</summary>
public sealed class MonsterPathFollower : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    [SerializeField, Min(0.01f)] private float stoppingDistance = 0.05f;
    [SerializeField] private bool flying;
    [SerializeField, Min(0f)] private float flightHeight = 3f;

    private readonly List<Vector3> waypoints = new List<Vector3>();
    private MonsterPathGrid pathGrid;
    private Transform destination;
    private Vector3 destinationPoint;
    private Vector3 travelDirection;
    private EnemyCore targetCore;
    private EnemyCore finalCore;
    private Vector3 lastPathTarget;
    private int waypointIndex;
    private bool initialized;
    private bool travellingToRequiredWaypoint;
    private bool currentPathReachesDestination;
    private bool followingCombatPath;

    public bool IsInitialized => initialized;
    public bool HasArrived { get; private set; }
    public bool HasCoreTarget => targetCore != null || finalCore != null;
    public bool IsTravellingToRequiredWaypoint => travellingToRequiredWaypoint;
    public event Action<MonsterPathFollower> Arrived;
    public IReadOnlyList<Vector3> Waypoints => waypoints;
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Max(0f, value); }
    public bool IsFlying => flying;
    public MonsterPathGrid PathGrid => pathGrid;
    public bool IsFollowingCombatPath => followingCombatPath;
    public bool CombatMovementPaused { get; set; }
    public float RemainingPathDistance
    {
        get
        {
            float distance = 0f;
            Vector3 previous = transform.position;
            for (int i = waypointIndex; i < waypoints.Count; i++)
            {
                Vector3 delta = waypoints[i] - previous;
                if (!flying) delta.y = 0f;
                distance += delta.magnitude;
                previous = waypoints[i];
            }
            return distance;
        }
    }
    public float FlightHeight { get => flightHeight; set => flightHeight = Mathf.Max(0f, value); }
    public Vector3 DestinationPosition => destination != null ? destination.position : destinationPoint;

    /// <summary>Temporarily follows a validated combat route without replacing the mission destination.</summary>
    public void FollowCombatPath(IReadOnlyList<Vector3> path)
    {
        if (!initialized || flying || HasArrived) return;
        followingCombatPath = true;
        CombatMovementPaused = false;
        waypoints.Clear();
        for (int i = 0; i < path.Count; i++) waypoints.Add(path[i]);
        waypointIndex = 0;
    }

    public void ResumeMissionPath()
    {
        if (!followingCombatPath) return;
        followingCombatPath = false;
        CombatMovementPaused = false;
        RebuildPath();
    }

    public void SetDestination(Vector3 worldPosition)
    {
        targetCore = null;
        finalCore = null;
        travellingToRequiredWaypoint = false;
        destination = null;
        flying = false;
        destinationPoint = worldPosition;
        HasArrived = false;
        RebuildPath();
    }

    public void SetDestination(Transform target)
    {
        targetCore = null;
        finalCore = null;
        travellingToRequiredWaypoint = false;
        destination = target;
        flying = false;
        HasArrived = false;
        RebuildPath();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        stoppingDistance = Mathf.Max(0.01f, stoppingDistance);
        flightHeight = Mathf.Max(0f, flightHeight);
    }

    /// <summary>Called by EnemySpawnPoint immediately after an enemy is created.</summary>
    public void Initialize(MonsterPathGrid grid, Transform target, Vector3 fallbackTarget, Vector3 direction, float speed)
    {
        Initialize(grid, target, fallbackTarget, direction, speed, null);
    }

    public void Initialize(MonsterPathGrid grid, Transform target, Vector3 fallbackTarget, Vector3 direction, float speed, EnemyCore core)
    {
        pathGrid = grid;
        destination = target;
        destinationPoint = fallbackTarget;
        travelDirection = direction;
        moveSpeed = Mathf.Max(0f, speed);
        flying = false;
        targetCore = core;
        finalCore = core;
        travellingToRequiredWaypoint = false;
        waypointIndex = 0;
        HasArrived = false;
        initialized = true;
        RebuildPath();
    }

    /// <summary>Initializes a flying monster. Flying movement ignores the ground grid.</summary>
    public void InitializeFlying(Transform target, Vector3 fallbackTarget, float speed, float height, EnemyCore core)
    {
        pathGrid = null;
        destination = target;
        destinationPoint = fallbackTarget;
        travelDirection = Vector3.forward;
        moveSpeed = Mathf.Max(0f, speed);
        flightHeight = Mathf.Max(0f, height);
        flying = true;
        targetCore = core;
        finalCore = core;
        travellingToRequiredWaypoint = false;
        waypointIndex = 0;
        HasArrived = false;
        initialized = true;
        RebuildPath();
    }

    /// <summary>
    /// Starts a two-leg route: required waypoint first, then the final core.
    /// The second leg is not started unless the required waypoint is actually reachable.
    /// </summary>
    public void InitializeViaWaypoint(
        MonsterPathGrid grid,
        Transform waypoint,
        Vector3 fallbackWaypoint,
        Vector3 direction,
        float speed,
        EnemyCore core)
    {
        pathGrid = grid;
        destination = waypoint;
        destinationPoint = fallbackWaypoint;
        travelDirection = direction;
        moveSpeed = Mathf.Max(0f, speed);
        targetCore = null;
        flying = false;
        finalCore = core;
        travellingToRequiredWaypoint = true;
        waypointIndex = 0;
        HasArrived = false;
        initialized = true;
        RebuildPath();
    }

    public void RebuildPath()
    {
        followingCombatPath = false;
        CombatMovementPaused = false;
        waypointIndex = 0;
        waypoints.Clear();
        Vector3 target = destination != null ? destination.position : destinationPoint;
        if (flying) target.y += flightHeight;
        lastPathTarget = target;
        if (targetCore != null && targetCore.IsWithinArrivalRange(transform.position))
        {
            CompleteArrival();
            return;
        }

        bool foundGridPath = false;
        currentPathReachesDestination = pathGrid == null;
        if (pathGrid != null)
            foundGridPath = pathGrid.TryFindPath(transform.position, target, travelDirection, waypoints, out currentPathReachesDestination);
        else if (waypoints.Count == 0 && (target - transform.position).sqrMagnitude > stoppingDistance * stoppingDistance)
            waypoints.Add(target);

        if (pathGrid != null && !foundGridPath)
        {
            // There is no open cell to use. Keep the enemy alive so it can never be
            // mistaken for a successful arrival at the core.
            HasArrived = false;
        }
    }

    private void Update()
    {
        if (!initialized || HasArrived) return;
        if (followingCombatPath && CombatMovementPaused) return;
        if (!followingCombatPath && targetCore != null && targetCore.IsWithinArrivalRange(transform.position))
        {
            CompleteArrival();
            return;
        }
        if (!followingCombatPath && destination != null && pathGrid != null && (destination.position - lastPathTarget).sqrMagnitude > pathGrid.CellSize * pathGrid.CellSize)
            RebuildPath();
        if (waypointIndex >= waypoints.Count)
        {
            HandleCurrentDestinationReached();
            return;
        }

        Vector3 target = waypoints[waypointIndex];
        if (!flying) target.y = transform.position.y;
        Vector3 toTarget = target - transform.position;
        if (toTarget.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Count) HandleCurrentDestinationReached();
            return;
        }

        Vector3 step = toTarget.normalized * moveSpeed * Time.deltaTime;
        transform.position += step.magnitude >= toTarget.magnitude ? toTarget : step;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion desired = Quaternion.LookRotation(toTarget, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
        }
    }

    private void HandleCurrentDestinationReached()
    {
        // Reaching a player/trap must never complete a required waypoint or damage the core.
        if (followingCombatPath) return;
        if (!currentPathReachesDestination) return;

        if (travellingToRequiredWaypoint)
        {
            BeginCoreLeg();
            return;
        }

        if (targetCore == null || targetCore.IsWithinArrivalRange(transform.position))
            CompleteArrival();
    }

    private void BeginCoreLeg()
    {
        travellingToRequiredWaypoint = false;
        targetCore = finalCore;
        if (targetCore == null)
        {
            CompleteArrival();
            return;
        }

        destination = targetCore.transform;
        destinationPoint = targetCore.Position;
        waypointIndex = 0;
        HasArrived = false;
        RebuildPath();
    }

    private void CompleteArrival()
    {
        if (HasArrived) return;
        HasArrived = true;
        Arrived?.Invoke(this);
    }
}
