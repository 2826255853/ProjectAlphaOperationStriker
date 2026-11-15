using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>自爆机器人的动画状态（与 Animator 里的状态名一一对应）。</summary>
public enum SelfDestructBotMode
{
    Idle = 0,
    Walk = 1,
    Attack = 2,
    Prime = 3,
}

/// <summary>
/// 自爆机器人（Self Destruct Bot）的动画驱动层。
///
/// 职责边界（与 <see cref="HurricaneDroneUnit"/> 同一套分工）：
///   * 本组件**只负责**把「玩法状态」翻译成 Animator 状态，并把挥臂命中时刻/炸弹挂点暴露出来。
///   * 寻路、索敌、挥臂伤害、自爆结算、血量与生成回收都不在这里，见 <see cref="SelfDestructBotEnemy"/>。
///
/// 动画由桌面素材目录的 Blender 脚本产出（SDB_Idle / SDB_Walk / SDB_Attack / SDB_Prime），
/// 挥臂命中时刻以动画事件 <c>OnArmSwing</c> 的形式写在 FBX 导入设置里（SDB_Attack 第 22 帧 @30fps）。
/// </summary>
[DisallowMultipleComponent]
public class SelfDestructBotUnit : MonoBehaviour
{
    public const string StateIdle = "SDB_Idle";
    public const string StateWalk = "SDB_Walk";
    public const string StateAttack = "SDB_Attack";
    public const string StatePrime = "SDB_Prime";

    /// <summary>SDB_Attack 里的挥臂命中帧（30 fps）。必须与 Blender 脚本的挥臂关键帧一致。</summary>
    public const int ArmSwingFrame = 22;
    public const float ClipFps = 30f;

    /// <summary>动画事件函数名（写在 FBX 导入设置里，见 SelfDestructBotAuthoring）。</summary>
    public const string ArmSwingEventName = "OnArmSwing";

    [Header("引用")]
    [Tooltip("留空则自动从同物体取 Animator。")]
    public Animator animator;

    [Tooltip("右手指尖挂点，由 SelfDestructBotAuthoring 生成时挂到 Forearm_R 骨骼下。")]
    public Transform hand;

    [Tooltip("背后炸弹的挂点，自爆特效从这里出。")]
    public Transform bomb;

    [Header("状态")]
    [SerializeField] private SelfDestructBotMode mode = SelfDestructBotMode.Idle;

    [SerializeField, Tooltip("状态切换的交叉淡入时间（秒）。")]
    private float crossFadeSeconds = 0.15f;

    [Header("攻击节奏")]
    [Tooltip("挥臂动作的有效时长（秒）。SDB_Attack 共 48 帧 @30fps = 1.6 s。")]
    [SerializeField] private float attackClipSeconds = 1.6f;

    [Tooltip("挥臂播完后是否自动回到待机。关掉就由调用方自己 StopAttack。")]
    [SerializeField] private bool autoExitAttack = true;

    private float attackEndsAt = -1f;
    private readonly HashSet<int> knownStates = new HashSet<int>();
    private readonly HashSet<int> warnedMissingStates = new HashSet<int>();

    /// <summary>每挥出一次（动画事件触发）时广播。</summary>
    public event Action<SelfDestructBotUnit> ArmSwung;

    public SelfDestructBotMode Mode => mode;

    /// <summary>挥手命中点：手掌位置，拿不到挂点时退回机体前方。</summary>
    public Vector3 HandPosition => hand != null ? hand.position : transform.position + transform.forward * 0.7f + Vector3.up * 0.9f;

    /// <summary>炸弹世界坐标，供自爆特效/范围伤害取中心。</summary>
    public Vector3 BombPosition => bomb != null ? bomb.position : transform.position + Vector3.up * 1.3f;

    /// <summary>本轮挥臂已经打出的次数（切出攻击状态时归零）。</summary>
    public int SwingsThisAttack { get; private set; }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ApplyMode(mode, true);
    }

    private void Update()
    {
        if (autoExitAttack && mode == SelfDestructBotMode.Attack && attackEndsAt > 0f && Time.time >= attackEndsAt)
        {
            attackEndsAt = -1f;
            SetMode(SelfDestructBotMode.Idle);
        }
    }

    /// <summary>切换动画状态。</summary>
    public void SetMode(SelfDestructBotMode next, bool immediate = false)
    {
        if (mode == next && !immediate)
        {
            return;
        }
        mode = next;
        ApplyMode(next, immediate);
    }

    /// <summary>开始一次挥臂。返回后由动画事件驱动 <see cref="ArmSwung"/>。</summary>
    public void PlayAttack()
    {
        SwingsThisAttack = 0;
        attackEndsAt = Time.time + Mathf.Max(0.1f, attackClipSeconds);
        SetMode(SelfDestructBotMode.Attack);
    }

    /// <summary>结束挥臂，回到待机（行走由调用方按位移重新决定）。</summary>
    public void StopAttack()
    {
        attackEndsAt = -1f;
        if (mode == SelfDestructBotMode.Attack)
        {
            SetMode(SelfDestructBotMode.Idle);
        }
    }

    /// <summary>是否正在挥臂动画里。</summary>
    public bool IsAttacking => mode == SelfDestructBotMode.Attack;

    /// <summary>动画事件接收点（FBX 导入设置里的 AnimationEvent 调用）。</summary>
    public void OnArmSwing()
    {
        SwingsThisAttack++;
        ArmSwung?.Invoke(this);
    }

    public static string StateNameOf(SelfDestructBotMode value)
    {
        switch (value)
        {
            case SelfDestructBotMode.Walk: return StateWalk;
            case SelfDestructBotMode.Attack: return StateAttack;
            case SelfDestructBotMode.Prime: return StatePrime;
            default: return StateIdle;
        }
    }

    private void ApplyMode(SelfDestructBotMode next, bool immediate)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        string stateName = StateNameOf(next);
        int hash = Animator.StringToHash(stateName);
        if (!knownStates.Contains(hash))
        {
            // Animator 在对象被销毁的过程中会把运行时状态机拆掉，此时 HasState 恒为 false；
            // 那是「拆机时刻」而不是「控制器缺状态」，不报警。
            if (!animator.isActiveAndEnabled)
            {
                return;
            }
            if (!animator.HasState(0, hash))
            {
                if (warnedMissingStates.Add(hash))
                {
                    Debug.LogWarning($"[SelfDestructBot] Animator 里没有状态 {stateName}（控制器 {animator.runtimeAnimatorController.name}）", this);
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

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        if (hand == null)
        {
            // 生成器写的锚点优先；没有锚点时退回前臂骨骼本身（自带预制体/场景实例也能用）。
            hand = FindDeep(transform, "SDB_Hand") ?? FindDeep(transform, "Forearm_R");
        }
        if (bomb == null)
        {
            bomb = FindDeep(transform, "SDB_Bomb") ?? FindDeep(transform, "Bomb");
        }
    }

    internal static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(BombPosition, 0.25f);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(HandPosition, 0.12f);
    }
}
