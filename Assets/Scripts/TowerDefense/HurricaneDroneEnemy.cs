using System;
using UnityEngine;

/// <summary>
/// 飓风无人机作为「飞行怪物」的玩法层。
///
/// 职责边界（与 HurricaneDroneUnit 分工）：
///   * 位移仍然只由 <see cref="MonsterPathFollower.InitializeFlying"/> 负责（空中出怪口那条分支，
///     不走 MonsterPathGrid，也不做地面投影）；本组件不写第二套寻路。
///   * <see cref="HurricaneDroneUnit"/> 只把模式翻译成 Animator 状态；本组件负责**决定**模式
///     （按实际位移/转向切 Hover / FlyForward / BankTurn），并驱动机炮开火。
///   * 血量与死亡走 <see cref="EnemyHealth"/>；死亡后的销毁由 EnemyInstance / EnemyHealth 既有流程处理，
///     这里只在 Died 时停火停机，避免"死了还在打"。
///
/// 机炮结算方式（从 HANDOFF 文档列出的三个选项里选了第 1 种）：
///   动画事件 OnGunFire → ShotFired → 本组件对当前目标做视线检测后直接扣血。
///   陷阱目标用 <see cref="TrapInstance.TakeDamage(float)"/>，玩家目标用 PlayerHealth.TakeDamage，
///   不再另造弹丸对象，也不套 MissileAimSolver 的拦截点预测（40 mm 机炮没有追踪能力）。
///
/// 预制体由 <c>HurricaneDroneAuthoring.CreateAll</c> 生成（Assets/Resources/HurricaneDroneEnemy.prefab），
/// 手改预制体会在下次重建时丢失。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterPathFollower), typeof(EnemyHealth), typeof(HurricaneDroneUnit))]
[DefaultExecutionOrder(-5)]
public sealed class HurricaneDroneEnemy : MonoBehaviour
{
    [Header("索敌")]
    [SerializeField, Min(0f), Tooltip("对地攻击的最大水平距离（米）。超出后只飞向核心。")]
    private float attackRange = 45f;

    [SerializeField, Range(0f, 180f), Tooltip("目标必须落在机头正前方这个夹角内才会开火。")]
    private float aimConeDegrees = 40f;

    [SerializeField, Min(0.05f), Tooltip("重新选靶的间隔（秒）。")]
    private float targetRefreshInterval = 0.4f;

    [SerializeField, Tooltip("优先攻击能打空中目标的陷阱（如双联导弹发射器），其次才是最近的陷阱/玩家。")]
    private bool prioritizeAirDefense = true;

    [SerializeField, Tooltip("允许把玩家当作目标。关闭后只打陷阱。")]
    private bool allowAttackPlayer = true;

    [Header("机炮")]
    [SerializeField, Min(0f), Tooltip("每一发命中的伤害。HD_Attack 一轮有 6 发动画事件。")]
    private float damagePerShot = 6f;

    [SerializeField, Min(0.1f), Tooltip("两轮扫射之间的间隔（秒）。")]
    private float burstCooldown = 3.2f;

    [SerializeField, Min(0f), Tooltip("视线检测的起点相对机体的抬高量，避免打到自己的模型。")]
    private float muzzleHeightOffset = 0f;

    [Header("动画驱动")]
    [SerializeField, Min(0f), Tooltip("水平速度超过此值切到 FlyForward。")]
    private float forwardSpeedThreshold = 0.6f;

    [SerializeField, Min(0f), Tooltip("偏航角速度超过此值（度/秒）切到 BankTurn。")]
    private float bankTurnDegreesPerSecond = 45f;

    [Header("引用（生成器写入，留空则自动查找）")]
    [SerializeField] private MonsterPathFollower follower;
    [SerializeField] private EnemyHealth health;
    [SerializeField] private HurricaneDroneUnit unit;

    private Transform target;
    private bool targetIsPlayer;
    private float nextTargetRefreshTime;
    private float nextBurstTime;
    private Vector3 lastPosition;
    private float lastYaw;
    private float baseMoveSpeed;
    private bool movementPaused;
    private bool subscribed;

    /// <summary>当前锁定的目标；没有目标时为 null。</summary>
    public Transform CurrentTarget => target;

    /// <summary>当前目标是否是玩家。</summary>
    public bool IsTargetingPlayer => targetIsPlayer;

    /// <summary>本轮扫射已打出的发数（由动画事件累加）。</summary>
    public int ShotsFired { get; private set; }

    /// <summary>是否正处在攻击动画里。</summary>
    public bool IsAttacking => unit != null && unit.Mode == HurricaneDroneMode.Attack;

    /// <summary>已结算的总伤害，供测试与调试读取。</summary>
    public float TotalDamageDealt { get; private set; }

    /// <summary>每次成功对目标结算伤害时触发。</summary>
    public event Action<HurricaneDroneEnemy, Transform, float> HitTarget;

    private void Awake()
    {
        ResolveReferences();
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
        if (follower != null)
        {
            baseMoveSpeed = follower.MoveSpeed;
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        if (follower != null && baseMoveSpeed <= 0f)
        {
            baseMoveSpeed = follower.MoveSpeed;
        }
        lastPosition = transform.position;
        lastYaw = transform.eulerAngles.y;
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (unit != null)
        {
            unit.StopAttack();
        }
        ResumeMovement();
        target = null;
        targetIsPlayer = false;
    }

    private void OnValidate()
    {
        attackRange = Mathf.Max(0f, attackRange);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);
        damagePerShot = Mathf.Max(0f, damagePerShot);
        burstCooldown = Mathf.Max(0.1f, burstCooldown);
        forwardSpeedThreshold = Mathf.Max(0f, forwardSpeedThreshold);
        bankTurnDegreesPerSecond = Mathf.Max(0f, bankTurnDegreesPerSecond);
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

        if (health.IsDead)
        {
            HaltAfterDeath();
            return;
        }

        // 只有空中出怪口初始化过的飞行怪才接管；地面怪物不会挂这个组件。
        if (!follower.IsInitialized || !follower.IsFlying || follower.HasArrived)
        {
            UpdateFlightAnimation(Time.deltaTime);
            return;
        }

        if (Time.time >= nextTargetRefreshTime)
        {
            SelectTarget();
            nextTargetRefreshTime = Time.time + targetRefreshInterval;
        }

        bool targetValid = IsTargetStillValid();
        if (!targetValid)
        {
            target = null;
            targetIsPlayer = false;
            ResumeMovement();
            UpdateFlightAnimation(Time.deltaTime);
            return;
        }

        Vector3 targetPoint = TargetAimPoint();
        if (!IsInsideAimCone(targetPoint) || !HasLineOfSight(targetPoint))
        {
            // 目标在射界外：继续飞向核心，不悬停。
            ResumeMovement();
            UpdateFlightAnimation(Time.deltaTime);
            return;
        }

        // 进入攻击姿态：悬停 + 播攻击动画；动画事件负责逐发结算。
        PauseMovement();
        FaceTarget(targetPoint);
        if (!IsAttacking && Time.time >= nextBurstTime)
        {
            ShotsFired = 0;
            unit.PlayAttack();
            nextBurstTime = Time.time + burstCooldown;
        }
        UpdateFlightAnimation(Time.deltaTime);
    }

    /// <summary>由 HurricaneDroneUnit 的动画事件经 ShotFired 转发，或由测试直接调用。</summary>
    public void OnShotFired(HurricaneDroneUnit droneUnit)
    {
        if (health != null && health.IsDead)
        {
            return;
        }
        if (target == null || !IsTargetStillValid())
        {
            return;
        }

        ShotsFired++;
        if (!IsInsideAimCone(TargetAimPoint()) || !HasLineOfSight(TargetAimPoint()))
        {
            return;
        }

        ApplyDamage(TargetAimPoint());
    }

    /// <summary>当前目标的世界瞄准点（陷阱中心 / 玩家胸口）。</summary>
    public Vector3 TargetAimPoint()
    {
        if (target == null)
        {
            return transform.position + transform.forward * 5f;
        }
        PlayerHealth player = targetIsPlayer ? target.GetComponent<PlayerHealth>() : null;
        if (player != null)
        {
            return player.FeetPosition + Vector3.up * 1.1f;
        }
        return target.position + Vector3.up * 0.6f;
    }

    // ------------------------------------------------------------------ 选靶

    private void SelectTarget()
    {
        Transform best = null;
        bool bestIsPlayer = false;
        bool bestIsAirDefense = false;
        float bestDistance = float.PositiveInfinity;

        foreach (TrapInstance trap in TrapInstance.ActiveTraps)
        {
            if (!CanTargetTrap(trap))
            {
                continue;
            }
            float distance = PlanarDistance(transform.position, trap.transform.position);
            if (distance > attackRange)
            {
                continue;
            }
            bool airDefense = IsAirDefenseTrap(trap);
            if (prioritizeAirDefense && airDefense && !bestIsAirDefense)
            {
                best = trap.transform;
                bestIsPlayer = false;
                bestIsAirDefense = true;
                bestDistance = distance;
                continue;
            }
            if (prioritizeAirDefense && bestIsAirDefense && !airDefense)
            {
                continue;
            }
            if (distance < bestDistance)
            {
                best = trap.transform;
                bestIsPlayer = false;
                bestIsAirDefense = airDefense;
                bestDistance = distance;
            }
        }

        if (allowAttackPlayer)
        {
            PlayerHealth player = PlayerHealth.Instance;
            if (player != null && player.isActiveAndEnabled && !player.IsDead)
            {
                float distance = PlanarDistance(transform.position, player.FeetPosition);
                if (distance <= attackRange && !bestIsAirDefense && distance < bestDistance)
                {
                    best = player.transform;
                    bestIsPlayer = true;
                }
            }
        }

        target = best;
        targetIsPlayer = bestIsPlayer;
    }

    private static bool CanTargetTrap(TrapInstance trap)
    {
        return trap != null && trap.isActiveAndEnabled && !trap.IsDestroyed;
    }

    /// <summary>能打空中目标的陷阱：目前只有对空导弹发射器。</summary>
    private static bool IsAirDefenseTrap(TrapInstance trap)
    {
        return trap.GetComponentInChildren<MissileLauncherTurret>(true) != null;
    }

    private bool IsTargetStillValid()
    {
        if (target == null)
        {
            return false;
        }
        if (targetIsPlayer)
        {
            PlayerHealth player = target.GetComponent<PlayerHealth>();
            return player != null && player.isActiveAndEnabled && !player.IsDead;
        }
        TrapInstance trap = target.GetComponent<TrapInstance>();
        return CanTargetTrap(trap)
            && PlanarDistance(transform.position, trap.transform.position) <= attackRange;
    }

    // ------------------------------------------------------------------ 结算

    private void ApplyDamage(Vector3 aimPoint)
    {
        if (targetIsPlayer)
        {
            PlayerHealth player = target.GetComponent<PlayerHealth>();
            if (player == null || player.IsDead)
            {
                return;
            }
            player.TakeDamage(damagePerShot, transform.position);
        }
        else
        {
            TrapInstance trap = target.GetComponent<TrapInstance>();
            if (!CanTargetTrap(trap))
            {
                return;
            }
            trap.TakeDamage(damagePerShot);
        }

        TotalDamageDealt += damagePerShot;
        HitTarget?.Invoke(this, target, damagePerShot);
    }

    /// <summary>机头正前方的夹角判定（只看水平方向，机体俯仰不参与）。</summary>
    public bool IsInsideAimCone(Vector3 worldPoint)
    {
        Vector3 toTarget = worldPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return true;
        }
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return true;
        }
        float angle = Vector3.Angle(forward, toTarget);
        return angle <= Mathf.Max(0f, aimConeDegrees);
    }

    /// <summary>视线检测：从机体上方打到目标点，跳过自己与目标自身的碰撞体。</summary>
    public bool HasLineOfSight(Vector3 worldPoint)
    {
        Vector3 origin = transform.position + Vector3.up * muzzleHeightOffset;
        Vector3 delta = worldPoint - origin;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null)
            {
                continue;
            }
            if (hitTransform.IsChildOf(transform))
            {
                continue;
            }
            if (target != null && hitTransform.IsChildOf(target))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 移动/动画

    /// <summary>
    /// 悬停开火期间整个停掉 follower：它在 Update 里会朝航点转体，
    /// 与这里的 FaceTarget 抢朝向（转向抖动会让射界判定反复失败）。
    /// 注意 <see cref="MonsterPathFollower.CombatMovementPaused"/> 只在 followingCombatPath 时生效，
    /// 对飞行分支无效，所以这里直接开关组件。
    /// </summary>
    private void PauseMovement()
    {
        if (movementPaused || follower == null)
        {
            return;
        }
        baseMoveSpeed = Mathf.Max(baseMoveSpeed, follower.MoveSpeed);
        follower.MoveSpeed = 0f;
        follower.enabled = false;
        movementPaused = true;
    }

    private void ResumeMovement()
    {
        if (!movementPaused || follower == null)
        {
            return;
        }
        follower.MoveSpeed = baseMoveSpeed;
        follower.enabled = true;
        movementPaused = false;
    }

    private void FaceTarget(Vector3 worldPoint)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(direction, Vector3.up), 120f * Time.deltaTime);
    }

    private void UpdateFlightAnimation(float deltaTime)
    {
        if (unit == null || deltaTime <= 0f)
        {
            return;
        }

        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / deltaTime;
        lastYaw = yaw;

        // 攻击动画由 HurricaneDroneUnit 自己按 attackClipSeconds 收尾，这里不要抢。
        if (unit.Mode == HurricaneDroneMode.Attack)
        {
            return;
        }

        float planarSpeed = new Vector2(delta.x, delta.z).magnitude / deltaTime;
        HurricaneDroneMode next;
        if (planarSpeed > forwardSpeedThreshold)
        {
            next = HurricaneDroneMode.FlyForward;
        }
        else if (yawRate > bankTurnDegreesPerSecond)
        {
            next = HurricaneDroneMode.BankTurn;
        }
        else
        {
            next = HurricaneDroneMode.Hover;
        }

        if (unit.Mode != next)
        {
            unit.SetMode(next);
        }
    }

    private void HaltAfterDeath()
    {
        // 死亡后不再恢复移动：follower 保持暂停态（速度 0），由 EnemyInstance 的既有流程销毁对象。
        if (follower != null)
        {
            follower.MoveSpeed = 0f;
        }
        if (unit != null && unit.Mode == HurricaneDroneMode.Attack)
        {
            unit.StopAttack();
        }
        target = null;
        targetIsPlayer = false;
    }

    private void ResolveReferences()
    {
        if (follower == null)
        {
            follower = GetComponent<MonsterPathFollower>();
        }
        if (health == null)
        {
            health = GetComponent<EnemyHealth>();
        }
        if (unit == null)
        {
            unit = GetComponent<HurricaneDroneUnit>();
        }
        Subscribe();
    }

    private void Subscribe()
    {
        if (subscribed || unit == null)
        {
            return;
        }
        unit.ShotFired += OnShotFired;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || unit == null)
        {
            subscribed = false;
            return;
        }
        unit.ShotFired -= OnShotFired;
        subscribed = false;
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, 1f);
        if (target == null)
        {
            return;
        }
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.DrawLine(transform.position, TargetAimPoint());
    }
}
