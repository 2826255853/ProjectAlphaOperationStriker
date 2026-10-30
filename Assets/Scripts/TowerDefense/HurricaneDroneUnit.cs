using System;
using UnityEngine;

/// <summary>飓风无人机的动画状态（与 Animator 里的状态名一一对应）。</summary>
public enum HurricaneDroneMode
{
    Hover = 0,
    FlyForward = 1,
    BankTurn = 2,
    Attack = 3,
}

/// <summary>
/// 飓风无人机（Hurricane Drone）的动画驱动层。
///
/// 职责边界（重要）：
///   * 本组件**只负责**把「玩法状态」翻译成 Animator 状态，并把炮口/开火事件暴露出来。
///   * 飞行路径、目标选择、命中结算、血量、生成与回收**都不在这里**，见
///     Assets/Editor/HURRICANE_DRONE_HANDOFF.md 的「留给程序员的接入点」。
///
/// 动画由 Blender 脚本产出（骨骼 HD_Hover / HD_FlyForward / HD_BankTurn / HD_Attack），
/// 开火时刻以动画事件 OnGunFire 的形式写在导入设置里（30 fps 的 22/26/30/46/50/54 帧）。
/// </summary>
[DisallowMultipleComponent]
public class HurricaneDroneUnit : MonoBehaviour
{
    public const string StateHover = "HD_Hover";
    public const string StateFlyForward = "HD_FlyForward";
    public const string StateBankTurn = "HD_BankTurn";
    public const string StateAttack = "HD_Attack";
    public const string GunFireEventName = "OnGunFire";

    [Header("引用")]
    [Tooltip("留空则自动从同物体取 Animator。")]
    public Animator animator;

    [Tooltip("炮口挂点，由 HurricaneDroneAuthoring 生成时自动挂到 Gun_Pitch 骨骼下。")]
    public Transform muzzle;

    [Header("状态")]
    [SerializeField] HurricaneDroneMode mode = HurricaneDroneMode.Hover;

    [Tooltip("状态切换的交叉淡入时间（秒）。")]
    [SerializeField] float crossFadeSeconds = 0.25f;

    [Header("攻击节奏")]
    [Tooltip("攻击动画的有效时长（秒）。HD_Attack 的曲线数据只到 2.97 s（90 帧 @30fps），" +
             "但导入后的 clip.length 会被报成 5.34 s（FBX take 尾部多出一段静止）。" +
             "这里按有效时长收尾，避免机体在最后一帧定住。")]
    [SerializeField] float attackClipSeconds = 2.97f;

    [Tooltip("攻击播完后是否自动回到悬停。关掉就由调用方自己 StopAttack。")]
    [SerializeField] bool autoExitAttack = true;

    float attackEndsAt = -1f;

    /// <summary>已经确认存在于控制器里的状态（hash），避免在拆机时刻重复校验。</summary>
    readonly System.Collections.Generic.HashSet<int> knownStates = new System.Collections.Generic.HashSet<int>();

    /// <summary>已经提示过的缺失状态，避免每个实例/每一帧刷同一条警告。</summary>
    readonly System.Collections.Generic.HashSet<int> warnedMissingStates = new System.Collections.Generic.HashSet<int>();

    /// <summary>每打出一发时触发（由动画事件驱动，时机与模型后坐一致）。</summary>
    public event Action<HurricaneDroneUnit> ShotFired;

    /// <summary>炮口当前位置，供子弹/曳光/火光的生成点使用。</summary>
    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

    /// <summary>炮口朝向（模型炮管指向 +Z）。</summary>
    public Quaternion MuzzleRotation => muzzle != null ? muzzle.rotation : transform.rotation;

    /// <summary>本次攻击已打出的发数，切出攻击状态时归零。</summary>
    public int ShotsThisBurst { get; private set; }

    public HurricaneDroneMode Mode => mode;

    void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        if (muzzle == null)
        {
            var found = transform.Find("Muzzle");
            if (found != null)
            {
                muzzle = found;
            }
        }
    }

    void Start()
    {
        ApplyMode(mode, true);
    }

    void Update()
    {
        // HD_Attack 的 clip.length 比实际动画长（见 attackClipSeconds 注释），
        // 所以用计时器收尾，而不是等 Animator 自己播完。
        if (autoExitAttack && mode == HurricaneDroneMode.Attack &&
            attackEndsAt > 0f && Time.time >= attackEndsAt)
        {
            attackEndsAt = -1f;
            SetMode(HurricaneDroneMode.Hover, false);
        }
    }

    /// <summary>切换动画状态。攻击状态可选择循环。</summary>
    public void SetMode(HurricaneDroneMode next, bool immediate = false)
    {
        if (mode == next && !immediate)
        {
            return;
        }
        mode = next;
        ApplyMode(next, immediate);
    }

    /// <summary>进入攻击状态。返回后由动画事件驱动 ShotFired。</summary>
    public void PlayAttack()
    {
        ShotsThisBurst = 0;
        attackEndsAt = Time.time + Mathf.Max(0.1f, attackClipSeconds);
        SetMode(HurricaneDroneMode.Attack, false);
    }

    /// <summary>结束攻击，回到悬停。</summary>
    public void StopAttack()
    {
        if (mode == HurricaneDroneMode.Attack)
        {
            SetMode(HurricaneDroneMode.Hover, false);
        }
    }

    void ApplyMode(HurricaneDroneMode next, bool immediate)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        string stateName = StateNameOf(next);
        int hash = Animator.StringToHash(stateName);

        if (!knownStates.Contains(hash))
        {
            // Animator 在对象被禁用/销毁的过程中会把运行时状态机拆掉，此时 HasState 恒返回 false。
            // HurricaneDroneEnemy.OnDisable → StopAttack 正好走这条路径（无人机死亡、退场或停场景时），
            // 那是「拆机时刻」而不是「控制器缺状态」，不校验也不报警；只有 Animator 还活着时才判定缺失。
            if (!animator.isActiveAndEnabled)
            {
                return;
            }
            if (!animator.HasState(0, hash))
            {
                if (warnedMissingStates.Add(hash))
                {
                    Debug.LogWarning($"[HurricaneDrone] Animator 里没有状态 {stateName}（控制器 {animator.runtimeAnimatorController.name}）");
                }
                return;
            }
            knownStates.Add(hash);
        }

        if (immediate || crossFadeSeconds <= 0f)
        {
            animator.Play(hash, 0, 0f);
        }
        else
        {
            animator.CrossFadeInFixedTime(hash, crossFadeSeconds, 0);
        }
    }

    public static string StateNameOf(HurricaneDroneMode m)
    {
        switch (m)
        {
            case HurricaneDroneMode.FlyForward: return StateFlyForward;
            case HurricaneDroneMode.BankTurn: return StateBankTurn;
            case HurricaneDroneMode.Attack: return StateAttack;
            default: return StateHover;
        }
    }

    /// <summary>
    /// 动画事件接收点（由 FBX 导入设置里的 AnimationEvent 调用）。
    /// 程序员在这里接伤害/弹道；默认只抛 ShotFired 事件，不做任何结算。
    /// </summary>
    public void OnGunFire()
    {
        ShotsThisBurst++;

        // TODO(确认): 这里决定「一发」是什么——瞬时射线、曳光弹对象、还是纯特效。
        // 目前只广播事件，不产生伤害，避免与既有 WeaponDamageStats / MissileProjectile 两套结算重复。
        ShotFired?.Invoke(this);
    }

    void OnDrawGizmosSelected()
    {
        if (muzzle == null)
        {
            return;
        }
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireSphere(muzzle.position, 0.15f);
        Gizmos.DrawRay(muzzle.position, muzzle.forward * 6f);
    }
}
