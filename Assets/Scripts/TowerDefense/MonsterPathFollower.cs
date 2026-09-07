using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves an enemy along a MonsterPathGrid route toward a destination.</summary>
public sealed class MonsterPathFollower : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    [SerializeField, Min(0.01f)] private float stoppingDistance = 0.05f;

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

    public bool IsInitialized => initialized;
    public bool HasArrived { get; private set; }
    public bool HasCoreTarget => targetCore != null || finalCore != null;
    public bool IsTravellingToRequiredWaypoint => travellingToRequiredWaypoint;
    public event Action<MonsterPathFollower> Arrived;
    public IReadOnlyList<Vector3> Waypoints => waypoints;
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Max(0f, value); }
    public Vector3 DestinationPosition => destination != null ? destination.position : destinationPoint;

    public void SetDestination(Vector3 worldPosition)
    {
        targetCore = null;
        finalCore = null;
        travellingToRequiredWaypoint = false;
        destination = null;
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
        HasArrived = false;
        RebuildPath();
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        stoppingDistance = Mathf.Max(0.01f, stoppingDistance);
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
        finalCore = core;
        travellingToRequiredWaypoint = true;
        waypointIndex = 0;
        HasArrived = false;
        initialized = true;
        RebuildPath();
    }

    public void RebuildPath()
    {
        waypoints.Clear();
        Vector3 target = destination != null ? destination.position : destinationPoint;
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
        if (targetCore != null && targetCore.IsWithinArrivalRange(transform.position))
        {
            CompleteArrival();
            return;
        }
        if (destination != null && pathGrid != null && (destination.position - lastPathTarget).sqrMagnitude > pathGrid.CellSize * pathGrid.CellSize)
            RebuildPath();
        if (waypointIndex >= waypoints.Count)
        {
            HandleCurrentDestinationReached();
            return;
        }

        Vector3 target = waypoints[waypointIndex];
        target.y = transform.position.y;
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
