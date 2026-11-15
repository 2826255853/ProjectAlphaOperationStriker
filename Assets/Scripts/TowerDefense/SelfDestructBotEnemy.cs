using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>自爆机器人的行为状态。</summary>
public enum SelfDestructBotState
{
    /// <summary>沿战斗路径/任务路径前进，边走边找机会。</summary>
    Advancing = 0,
    /// <summary>玩家进入挥臂距离：停步挥臂。</summary>
    Swinging = 1,
    /// <summary>贴到可攻击的地面陷阱：停步、蓄爆倒计时。</summary>
    Priming = 2,
    /// <summary>已引爆（对象随后销毁）。</summary>
    Detonated = 3,
}

/// <summary>
/// 自爆机器人的玩法层：双足机器人背着带警告标识的炸弹，边前进边伺机动手。
///
/// 三条行为（对应需求）：
///   1. <b>挥臂攻击</b>：玩家进入 <c>armSwingRange</c> 且站位可达时停步转身，播 SDB_Attack，
///      命中时刻由动画事件 <c>OnArmSwing</c> 触发，对仍在范围内的玩家结算 <c>armSwingDamage</c>。
///   2. <b>自爆</b>：成功走到「地面上可被攻击的陷阱」旁（<c>trapTriggerRange</c> 内，且剩余路径也够得着）时
///      停止前进、进入 SDB_Prime 蓄爆，<c>fuseSeconds</c> 后对 <c>blastRadius</c> 内的玩家与陷阱结算范围伤害。
///   3. 以上都不成立时继续朝核心前进（沿用 MonsterPathFollower 的任务路径）。
///
/// 分工：位移/寻路只走 <see cref="MonsterPathFollower"/>（FollowCombatPath / ResumeMissionPath），
/// 动画只走 <see cref="SelfDestructBotUnit"/>，血量和销毁走 <see cref="EnemyHealth"/>。
/// <b>不要再给这个预制体加 GroundEnemyCombat</b>：那套近战逻辑会与本组件抢 follower 的路径与移动暂停，
/// 所以 EnemySpawnPoint 对自爆机器人跳过了自动添加（见 EnemySpawnPoint.CreateEnemy）。
///
/// 预制体由 <c>SelfDestructBotAuthoring.CreateAll</c> 生成（Assets/Resources/SelfDestructBotEnemy.prefab），
/// 手改预制体会在下次重建时丢失。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterPathFollower), typeof(EnemyHealth), typeof(SelfDestructBotUnit))]
[DefaultExecutionOrder(-8)]
public sealed class SelfDestructBotEnemy : MonoBehaviour
{
    [Header("玩家挑衅（挥臂攻击）")]
    [SerializeField, Min(0f), Tooltip("玩家进入此水平距离且站在可达道路上时，停步逼近并挥臂。")]
    private float playerProvokeRadius = 7f;

    [SerializeField, Min(0f), Tooltip("玩家离开此距离后放弃追击，回到任务路径。")]
    private float playerDisengageRadius = 10f;

    [SerializeField, Min(0.1f), Tooltip("挥臂的命中距离（米，水平）。")]
    private float armSwingRange = 2.1f;

    [SerializeField, Min(0f), Tooltip("每次挥臂命中的伤害。")]
    private float armSwingDamage = 14f;

    [SerializeField, Min(0.1f), Tooltip("两次挥臂之间的最短间隔（秒）。")]
    private float armSwingCooldown = 1.9f;

    [Header("找陷阱自爆")]
    [SerializeField, Min(0f), Tooltip("扫描可攻击地面陷阱的半径（米）。")]
    private float trapDetectionRadius = 16f;

    [SerializeField, Min(0.1f), Tooltip("贴到这个距离就停步起爆（米，水平）。")]
    private float trapTriggerRange = 1.8f;

    [SerializeField, Tooltip("地刺 / 贝蒂7 这类「踩过去的地面陷阱」是否也算自爆目标。关掉后只炸立在地面上的炮塔类陷阱。")]
    private bool includeFloorTraps = true;

    [SerializeField, Min(0f), Tooltip("目标陷阱与自己脚下的最大高度差，防止去炸高台上的陷阱。")]
    private float heightTolerance = 0.75f;

    [Header("自爆")]
    [SerializeField, Min(0f), Tooltip("蓄爆（SDB_Prime）到引爆的秒数。0 表示贴到就炸。")]
    private float fuseSeconds = 1.2f;

    [SerializeField, Min(0f), Tooltip("爆炸半径（米）。")]
    private float blastRadius = 3.6f;

    [SerializeField, Min(0f), Tooltip("爆心对玩家的伤害；边缘按距离衰减到最小系数。")]
    private float blastDamageToPlayer = 55f;

    [SerializeField, Min(0f), Tooltip("爆心对陷阱的伤害；边缘按距离衰减到最小系数。")]
    private float blastDamageToTraps = 120f;

    [SerializeField, Range(0f, 1f), Tooltip("爆炸边缘的伤害系数（爆心为 1）。")]
    private float blastEdgeFactor = 0.35f;

    [SerializeField, Tooltip("是否生成程序化的爆炸表现（球状闪光 + 点光）。关掉只结算伤害。")]
    private bool spawnBlastFx = true;

    [Header("节奏")]
    [SerializeField, Min(0.05f), Tooltip("重新选靶的间隔（秒）。")]
    private float retargetInterval = 0.25f;

    [Header("引用（生成器写入，留空则自动查找）")]
    [SerializeField] private MonsterPathFollower follower;
    [SerializeField] private EnemyHealth health;
    [SerializeField] private SelfDestructBotUnit unit;

    private readonly List<Vector3> candidatePath = new List<Vector3>();
    private readonly List<Vector3> selectedPath = new List<Vector3>();
    private Transform target;
    private bool targetIsPlayer;
    private Vector3 pathEnd;
    private float nextRetargetTime;
    private float nextSwingTime;
    private float fuseRemaining = -1f;
    private bool movementHalted;
    private bool subscribed;

    /// <summary>当前状态。</summary>
    public SelfDestructBotState State { get; private set; } = SelfDestructBotState.Advancing;

    /// <summary>当前追踪的目标（玩家或陷阱）；没有目标时为 null。</summary>
    public Transform CurrentTarget => target;

    /// <summary>当前目标是否是玩家。</summary>
    public bool IsTargetingPlayer => target != null && targetIsPlayer;

    /// <summary>距离引爆还剩多少秒；未蓄爆时返回 0。</summary>
    public float FuseRemaining => fuseRemaining > 0f ? fuseRemaining : 0f;

    /// <summary>本次蓄爆是否已进入倒计时。</summary>
    public bool IsPriming => State == SelfDestructBotState.Priming;

    /// <summary>已挥臂次数（统计用）。</summary>
    public int SwingCount { get; private set; }

    /// <summary>已对玩家造成的挥臂总伤害。</summary>
    public float TotalSwingDamage { get; private set; }

    /// <summary>引爆时广播（伤害结算之后）。</summary>
    public event Action<SelfDestructBotEnemy, Vector3> Exploded;

    /// <summary>每次挥臂命中玩家时广播。</summary>
    public event Action<SelfDestructBotEnemy, PlayerHealth> StruckPlayer;

    /// <summary>进入蓄爆时广播。</summary>
    public event Action<SelfDestructBotEnemy, TrapInstance> PrimeStarted;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        RestoreMovement();
        target = null;
        targetIsPlayer = false;
    }

    private void OnValidate()
    {
        playerProvokeRadius = Mathf.Max(0f, playerProvokeRadius);
        playerDisengageRadius = Mathf.Max(playerProvokeRadius, playerDisengageRadius);
        armSwingRange = Mathf.Max(0.1f, armSwingRange);
        armSwingDamage = Mathf.Max(0f, armSwingDamage);
        armSwingCooldown = Mathf.Max(0.1f, armSwingCooldown);
        trapDetectionRadius = Mathf.Max(0f, trapDetectionRadius);
        trapTriggerRange = Mathf.Max(0.1f, trapTriggerRange);
        heightTolerance = Mathf.Max(0f, heightTolerance);
        fuseSeconds = Mathf.Max(0f, fuseSeconds);
        blastRadius = Mathf.Max(0f, blastRadius);
        blastDamageToPlayer = Mathf.Max(0f, blastDamageToPlayer);
        blastDamageToTraps = Mathf.Max(0f, blastDamageToTraps);
        retargetInterval = Mathf.Max(0.05f, retargetInterval);
    }

    private void Update()
    {
        if (follower == null || health == null || unit == null)
        {
            ResolveReferences();
            if (follower == null || health == null || unit == null)
            {
                return;
            }
        }

        if (State == SelfDestructBotState.Detonated) return;
        if (health.IsDead)
        {
            // 被击杀：不引爆，交给 EnemyHealth 的销毁流程。
            RestoreMovement();
            return;
        }
        if (!follower.IsInitialized || follower.IsFlying || follower.HasArrived) return;

        // 蓄爆中：只倒计时，不再移动、不再选靶。
        if (State == SelfDestructBotState.Priming)
        {
            FaceTargetIfAny();
            fuseRemaining -= Time.deltaTime;
            if (fuseRemaining <= 0f) Detonate();
            return;
        }

        if (Time.time >= nextRetargetTime)
        {
            SelectTarget();
            nextRetargetTime = Time.time + retargetInterval;
        }

        if (target == null)
        {
            RestoreMovement();
            unit.SetMode(SelfDestructBotMode.Walk);
            return;
        }

        Vector3 targetPosition = TargetPosition();
        float distance = PlanarDistance(transform.position, targetPosition);
        // 剩余路径也要够得着：防止隔着墙体/拐角就开始攻击。
        float remaining = follower.RemainingPathDistance + PlanarDistance(pathEnd, targetPosition);

        if (targetIsPlayer)
        {
            UpdatePlayerEngagement(targetPosition, distance, remaining);
            return;
        }

        UpdateTrapEngagement(targetPosition, distance, remaining);
    }

    // ------------------------------------------------------------------ 玩家：挥臂

    private void UpdatePlayerEngagement(Vector3 targetPosition, float distance, float remaining)
    {
        if (distance > playerDisengageRadius || !CanTargetPlayer(PlayerHealth.Instance))
        {
            target = null;
            targetIsPlayer = false;
            RestoreMovement();
            unit.SetMode(SelfDestructBotMode.Walk);
            return;
        }

        bool inSwingRange = distance <= armSwingRange && remaining <= armSwingRange;
        follower.CombatMovementPaused = inSwingRange;
        movementHalted = inSwingRange;

        if (!inSwingRange)
        {
            if (!follower.IsFollowingCombatPath || Time.time >= nextRetargetTime)
            {
                TryApproachPlayer();
            }
            if (!unit.IsAttacking) unit.SetMode(SelfDestructBotMode.Walk);
            return;
        }

        FaceTarget(targetPosition);
        if (unit.IsAttacking || Time.time < nextSwingTime) return;

        nextSwingTime = Time.time + armSwingCooldown;
        unit.PlayAttack();
    }

    private void TryApproachPlayer()
    {
        PlayerHealth player = PlayerHealth.Instance;
        if (player == null) return;
        if (TryBuildCombatPath(player.FeetPosition, true, playerDisengageRadius * 2f, out _))
        {
            BeginCombatPath(candidatePath);
        }
    }

    // ------------------------------------------------------------------ 陷阱：自爆

    private void UpdateTrapEngagement(Vector3 targetPosition, float distance, float remaining)
    {
        TrapInstance trap = target != null ? target.GetComponent<TrapInstance>() : null;
        if (trap == null || !CanTargetTrap(trap))
        {
            target = null;
            RestoreMovement();
            unit.SetMode(SelfDestructBotMode.Walk);
            return;
        }

        // 贴近了：停步 -> 蓄爆。这里的判据同时看直线距离和剩余路径，避免隔墙起爆。
        if (distance <= trapTriggerRange && remaining <= trapTriggerRange)
        {
            BeginPrime(trap, targetPosition);
            return;
        }

        if (!follower.IsFollowingCombatPath || Time.time >= nextRetargetTime)
        {
            if (TryBuildCombatPath(targetPosition, false, trapDetectionRadius * 2f, out _))
            {
                BeginCombatPath(candidatePath);
            }
        }
        if (!unit.IsAttacking) unit.SetMode(SelfDestructBotMode.Walk);
    }

    private void BeginPrime(TrapInstance trap, Vector3 targetPosition)
    {
        State = SelfDestructBotState.Priming;
        HaltMovement();
        FaceTarget(targetPosition);
        unit.SetMode(SelfDestructBotMode.Prime);
        fuseRemaining = Mathf.Max(0f, fuseSeconds);
        PrimeStarted?.Invoke(this, trap);
        if (fuseRemaining <= 0f) Detonate();
    }

    /// <summary>立即引爆并把范围伤害结算掉，然后销毁自己（不发放击杀奖励）。</summary>
    public void Detonate()
    {
        if (State == SelfDestructBotState.Detonated) return;
        State = SelfDestructBotState.Detonated;
        Vector3 center = unit != null ? unit.BombPosition : transform.position + Vector3.up * 1.3f;
        Explode(center);
        if (spawnBlastFx)
        {
            SelfDestructBlastFx.Spawn(center, blastRadius);
        }
        // 自爆不是"被击杀"：直接销毁，EnemyInstance 会把它记为 Despawned，不发放击杀奖励。
        Destroy(gameObject);
    }

    private void Explode(Vector3 center)
    {
        float radius = Mathf.Max(0.01f, blastRadius);

        // 玩家
        PlayerHealth player = PlayerHealth.Instance;
        if (player != null && player.isActiveAndEnabled && !player.IsDead)
        {
            float distance = PlanarDistance(center, player.FeetPosition);
            if (distance <= radius)
            {
                float factor = Falloff(distance, radius);
                player.TakeDamage(blastDamageToPlayer * factor, center);
            }
        }

        // 陷阱（先快照，避免结算过程中集合被改写）
        var traps = new List<TrapInstance>(TrapInstance.ActiveTraps);
        for (int i = 0; i < traps.Count; i++)
        {
            TrapInstance trap = traps[i];
            if (!CanTargetTrap(trap)) continue;
            float distance = PlanarDistance(center, trap.transform.position);
            if (distance > radius) continue;
            trap.TakeDamage(blastDamageToTraps * Falloff(distance, radius));
        }

        Exploded?.Invoke(this, center);
    }

    private float Falloff(float distance, float radius)
    {
        if (radius <= 0f) return 1f;
        float t = Mathf.Clamp01(distance / radius);
        return Mathf.Lerp(1f, Mathf.Clamp01(blastEdgeFactor), t);
    }

    // ------------------------------------------------------------------ 挥臂命中（动画事件）
    private void Subscribe()
    {
        if (subscribed || unit == null) return;
        unit.ArmSwung += OnArmSwung;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || unit == null)
        {
            subscribed = false;
            return;
        }
        unit.ArmSwung -= OnArmSwung;
        subscribed = false;
    }

    /// <summary>动画事件 OnArmSwing 转发到这里：只有目标仍在挥臂范围内才结算伤害。</summary>
    public void OnArmSwung(SelfDestructBotUnit source)
    {
        if (State == SelfDestructBotState.Detonated) return;
        if (health != null && health.IsDead) return;
        if (target == null || !targetIsPlayer) return;

        PlayerHealth player = target.GetComponent<PlayerHealth>();
        if (player == null || !player.isActiveAndEnabled || player.IsDead) return;
        if (PlanarDistance(transform.position, player.FeetPosition) > armSwingRange * 1.15f) return;

        SwingCount++;
        player.TakeDamage(armSwingDamage, transform.position);
        TotalSwingDamage += armSwingDamage;
        StruckPlayer?.Invoke(this, player);
    }

    // ------------------------------------------------------------------ 选靶
    private void SelectTarget()
    {
        // 陷阱优先：这是它存在的意义（清掉玩家布置的地面陷阱）。
        TrapInstance bestTrap = null;
        float bestTrapScore = float.PositiveInfinity;
        foreach (TrapInstance trap in TrapInstance.ActiveTraps)
        {
            if (!CanTargetTrap(trap)) continue;
            float distance = PlanarDistance(transform.position, trap.transform.position);
            if (distance > trapDetectionRadius) continue;
            if (!TryBuildCombatPath(trap.transform.position, false, trapDetectionRadius * 2f, out _)) continue;
            if (distance < bestTrapScore)
            {
                bestTrap = trap;
                bestTrapScore = distance;
                selectedPath.Clear();
                selectedPath.AddRange(candidatePath);
            }
        }

        if (bestTrap != null)
        {
            target = bestTrap.transform;
            targetIsPlayer = false;
            BeginCombatPath(selectedPath);
            return;
        }

        PlayerHealth player = PlayerHealth.Instance;
        if (CanTargetPlayer(player))
        {
            target = player.transform;
            targetIsPlayer = true;
            pathEnd = player.FeetPosition;
            if (TryBuildCombatPath(player.FeetPosition, true, playerDisengageRadius * 2f, out _))
            {
                BeginCombatPath(candidatePath);
            }
            else
            {
                follower.ResumeMissionPath();
            }
            return;
        }

        target = null;
        targetIsPlayer = false;
        follower.ResumeMissionPath();
    }

    private bool CanTargetPlayer(PlayerHealth player)
    {
        if (player == null || !player.isActiveAndEnabled || player.IsDead) return false;
        if (follower.PathGrid == null)
        {
            return PlanarDistance(transform.position, player.FeetPosition) <= playerProvokeRadius;
        }
        Vector3 feet = player.FeetPosition;
        return PlanarDistance(transform.position, feet) <= playerProvokeRadius
            && follower.PathGrid.TryWorldToCell(feet, out Vector2Int cell)
            && follower.PathGrid.IsOpen(cell)
            && Mathf.Abs(feet.y - follower.PathGrid.CellToWorld(cell).y) <= heightTolerance;
    }

    /// <summary>「地面上可被攻击的陷阱」：未摧毁、高度与脚下接近（不是高台陷阱）。</summary>
    private bool CanTargetTrap(TrapInstance trap)
    {
        if (trap == null || !trap.isActiveAndEnabled || trap.IsDestroyed) return false;
        if (!includeFloorTraps)
        {
            // 地刺与贝蒂7 是"踩过去"的地面陷阱，立在地面上的炮塔才算硬目标。
            if (trap.GetComponent<GroundSpikeTrap>() != null
                || trap.GetComponent<Betty7JumpMineTrap>() != null) return false;
        }
        return Mathf.Abs(trap.transform.position.y - transform.position.y) <= heightTolerance;
    }

    private bool TryBuildCombatPath(Vector3 targetPoint, bool requireOpenTarget, float maxDistance, out float distance)
    {
        candidatePath.Clear();
        distance = 0f;
        MonsterPathGrid grid = follower.PathGrid;
        if (grid != null)
        {
            if (!grid.TryWorldToCell(transform.position, out Vector2Int start) || !grid.IsOpen(start)) return false;
            if (!grid.TryFindPath(transform.position, targetPoint, targetPoint - transform.position,
                    candidatePath, out bool reachedTarget)) return false;
            if (requireOpenTarget && !reachedTarget) return false;
            // 路边陷阱可以从相邻的可行走格发起攻击。
            if (!reachedTarget && PlanarDistance(candidatePath[candidatePath.Count - 1], targetPoint) > trapTriggerRange)
            {
                return false;
            }
        }
        else
        {
            candidatePath.Add(targetPoint);
        }

        Vector3 previous = transform.position;
        foreach (Vector3 point in candidatePath)
        {
            distance += PlanarDistance(previous, point);
            previous = point;
        }
        distance += PlanarDistance(previous, targetPoint);
        return distance <= maxDistance;
    }

    private void BeginCombatPath(List<Vector3> path)
    {
        if (path == null || path.Count == 0) return;
        pathEnd = path[path.Count - 1];
        follower.FollowCombatPath(path);
        movementHalted = false;
    }

    private Vector3 TargetPosition()
    {
        if (target == null) return transform.position;
        if (targetIsPlayer)
        {
            PlayerHealth player = target.GetComponent<PlayerHealth>();
            return player != null ? player.FeetPosition : target.position;
        }
        return target.position;
    }

    // ------------------------------------------------------------------ 移动/朝向
    private void HaltMovement()
    {
        if (movementHalted || follower == null) return;
        follower.CombatMovementPaused = true;
        movementHalted = true;
    }

    private void RestoreMovement()
    {
        if (follower == null) return;
        if (follower.IsFollowingCombatPath) follower.CombatMovementPaused = false;
        movementHalted = false;
    }

    private void FaceTargetIfAny()
    {
        if (target != null) FaceTarget(TargetPosition());
    }

    private void FaceTarget(Vector3 worldPoint)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up), 420f * Time.deltaTime);
    }

    private void ResolveReferences()
    {
        if (follower == null) follower = GetComponent<MonsterPathFollower>();
        if (health == null) health = GetComponent<EnemyHealth>();
        if (unit == null) unit = GetComponent<SelfDestructBotUnit>();
        Subscribe();
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.55f);
        Gizmos.DrawWireSphere(transform.position, blastRadius);
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, armSwingRange);
        Gizmos.color = new Color(0.35f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, trapDetectionRadius);
    }
}
