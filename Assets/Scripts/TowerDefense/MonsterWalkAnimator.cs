using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 普通地面小怪（蒙面士兵）的走路 / 待机 / 近战挥刀程序化骨骼动画。
/// 不依赖 Animator、AnimationClip 或外部动画资源：在 LateUpdate 里按实际移动速度
/// 围绕角色本地右轴摆动大腿、小腿和上臂，并给髋部做起伏；停下来时自动过渡为呼吸/轻微摇摆。
/// 进入近战时（<see cref="GroundEnemyCombat.IsAttacking"/>）叠加一段右臂举刀 → 下劈 → 收招的挥刀姿态，
/// 因此同一套骨骼只有一个写入者，不会和走路层互相覆盖。
/// 骨骼按名字查找（忽略大小写、点、下划线、空格），缺骨骼时对应部位直接跳过，不会抛异常。
/// 只在 Play Mode 驱动；编辑器里由 <see cref="EnsureInitialized"/> + <see cref="ApplyPose"/> 采样。
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterWalkAnimator : MonoBehaviour
{
    [Header("速度混合")]
    [Tooltip("达到这个水平速度时视为完全走路。")]
    [SerializeField, Min(0.05f)] private float fullWalkSpeed = 1.4f;
    [Tooltip("速度采样平滑系数，越大越跟手。")]
    [SerializeField, Min(0.1f)] private float speedSmoothing = 8f;
    [Tooltip("低于该速度视为站立，避免微抖导致抖动。")]
    [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.06f;
    [Tooltip("待机/走路混合的过渡速度（每秒）。")]
    [SerializeField, Min(0.01f)] private float blendSharpness = 3.5f;

    [Header("走路")]
    [Tooltip("完全走路时每秒的完整步频。")]
    [SerializeField, Min(0.05f)] private float strideFrequency = 1.75f;
    [Tooltip("大腿前后摆动的最大角度。")]
    [SerializeField, Min(0f)] private float legSwingDegrees = 30f;
    [Tooltip("小腿向后弯曲的最大角度。")]
    [SerializeField, Min(0f)] private float kneeBendDegrees = 42f;
    [Tooltip("上臂前后摆动的最大角度。")]
    [SerializeField, Min(0f)] private float armSwingDegrees = 20f;
    [Tooltip("髋部随步伐上下起伏的幅度（米）。")]
    [SerializeField, Min(0f)] private float hipBobMeters = 0.035f;
    [Tooltip("髋部左右侧倾角度。")]
    [SerializeField, Min(0f)] private float hipRollDegrees = 3f;
    [Tooltip("躯干反向扭转角度。")]
    [SerializeField, Min(0f)] private float torsoTwistDegrees = 4f;
    [Tooltip("走路时躯干前倾角度。")]
    [SerializeField, Min(0f)] private float walkLeanDegrees = 4f;

    [Header("待机")]
    [Tooltip("待机时髋部上下起伏的幅度（米）。")]
    [SerializeField, Min(0f)] private float idleBobMeters = 0.01f;
    [Tooltip("待机呼吸时胸腔起伏角度。")]
    [SerializeField, Min(0f)] private float breathDegrees = 2f;
    [Tooltip("待机时头部/身体左右轻摆角度。")]
    [SerializeField, Min(0f)] private float idleSwayDegrees = 1.4f;
    [Tooltip("待机呼吸的循环速度（每秒弧度）。")]
    [SerializeField, Min(0.01f)] private float idlePhasePerSecond = 1.2f;

    [Header("挥刀攻击（右臂）")]
    [Tooltip("一次完整挥刀（举刀 → 下劈 → 收招）的时长，秒。")]
    [SerializeField, Min(0.15f)] private float attackDuration = 0.72f;
    [Tooltip("举刀（起手）结束的时间点，占整段动作的比例。")]
    [SerializeField, Range(0.05f, 0.8f)] private float attackWindupFraction = 0.3f;
    [Tooltip("刀锋接触目标的瞬间，占整段动作的比例。")]
    [SerializeField, Range(0.08f, 0.9f)] private float attackContactFraction = 0.44f;
    [Tooltip("收招完成的时间点，占整段动作的比例。")]
    [SerializeField, Range(0.1f, 1f)] private float attackRecoverFraction = 0.7f;
    [Tooltip("上臂前后挥动幅度：起手向后上抬 + 下劈向前。")]
    [SerializeField, Min(0f)] private float attackArmSwingDegrees = 78f;
    [Tooltip("起手时右臂向身体外侧张开的幅度。")]
    [SerializeField, Min(0f)] private float attackOutwardDegrees = 22f;
    [Tooltip("肘部弯曲/伸直的幅度，起手时收肘把刀带到身后。")]
    [SerializeField, Min(0f)] private float attackElbowDegrees = 46f;
    [Tooltip("手腕甩动的幅度，下劈时刀尖加速。")]
    [SerializeField, Min(0f)] private float attackWristDegrees = 32f;
    [Tooltip("躯干拧转幅度，起手向右蓄力、下劈向左送力。")]
    [SerializeField, Min(0f)] private float attackTorsoTwistDegrees = 20f;
    [Tooltip("下劈时躯干前倾幅度。")]
    [SerializeField, Min(0f)] private float attackLeanDegrees = 8f;
    [Tooltip("挥刀时头部反向稳定幅度。")]
    [SerializeField, Min(0f)] private float attackHeadTwistDegrees = 10f;
    [Tooltip("近战攻击间隔内是否持续循环挥刀（近战期间原地连砍）。")]
    [SerializeField] private bool loopWhileAttacking = true;

    private Transform hips;
    private Transform spine;
    private Transform chest;
    private Transform head;
    private Transform upperLegL;
    private Transform lowerLegL;
    private Transform upperLegR;
    private Transform lowerLegR;
    private Transform upperArmL;
    private Transform lowerArmL;
    private Transform upperArmR;
    private Transform lowerArmR;
    private Transform handR;

    private Quaternion restHipsRotation;
    private Vector3 restHipsPosition;
    private readonly Dictionary<Transform, Quaternion> restRotations = new Dictionary<Transform, Quaternion>();

    private readonly List<string> missingBones = new List<string>();
    private bool initialized;
    private float smoothedSpeed;
    private float walkBlend;
    private float phase;
    private float idlePhase;
    private Vector3 lastPosition;

    private GroundEnemyCombat combat;
    private bool combatBound;
    private bool wasAttacking;
    private bool attackActive;
    private float attackTime;

    /// <summary>当前平滑后的水平移动速度（米/秒）。</summary>
    public float CurrentSpeed => smoothedSpeed;

    /// <summary>0 = 完全待机，1 = 完全走路。</summary>
    public float WalkBlend => walkBlend;

    public bool IsWalking => walkBlend >= 0.5f;

    /// <summary>当前是否正在播放挥刀动作。</summary>
    public bool IsSwinging => attackActive;

    /// <summary>挥刀动作进度 0..1；没有挥刀时为 0。</summary>
    public float AttackProgress => attackActive ? Mathf.Clamp01(attackTime / Mathf.Max(0.0001f, attackDuration)) : 0f;

    /// <summary>没有在模型上找到的骨骼名，便于探针/测试输出。</summary>
    public IReadOnlyList<string> MissingBones => missingBones;

    public void Awake()
    {
        EnsureInitialized();
    }

    public void OnEnable()
    {
        lastPosition = transform.position;
        smoothedSpeed = 0f;
        walkBlend = 0f;
        attackActive = false;
        attackTime = 0f;
        wasAttacking = false;
        BindCombat();
    }

    public void OnDisable()
    {
        RestoreRestPose();
        UnbindCombat();
    }

    /// <summary>查找并缓存骨骼与静止姿态；可重复调用。</summary>
    public void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        var bones = new Dictionary<string, Transform>();
        foreach (var candidate in GetComponentsInChildren<Transform>(true))
        {
            var key = NormalizeName(candidate.name);
            if (!string.IsNullOrEmpty(key) && !bones.ContainsKey(key))
            {
                bones.Add(key, candidate);
            }
        }

        hips = Find(bones, "hips", "pelvis");
        spine = Find(bones, "spine");
        chest = Find(bones, "chest", "spine1", "spine2");
        head = Find(bones, "head");
        upperLegL = Find(bones, "upperlegl", "upperlegleft", "thighl", "thighleft");
        lowerLegL = Find(bones, "lowerlegl", "lowerlegleft", "calfl", "calfleft", "shinl", "shinleft");
        upperLegR = Find(bones, "upperlegr", "upperlegright", "thighr", "thighright");
        lowerLegR = Find(bones, "lowerlegr", "lowerlegright", "calfr", "calfright", "shinr", "shinright");
        upperArmL = Find(bones, "upperarml", "upperarmleft", "arml", "armleft");
        lowerArmL = Find(bones, "lowerarml", "lowerarmleft", "forearml", "forearmleft");
        upperArmR = Find(bones, "upperarmr", "upperarmright", "armr", "armright");
        lowerArmR = Find(bones, "lowerarmr", "lowerarmright", "forearmr", "forearmright");
        handR = Find(bones, "handr", "handright");

        restRotations.Clear();
        missingBones.Clear();
        RegisterRest(hips, "Hips");
        RegisterRest(spine, "Spine");
        RegisterRest(chest, "Chest");
        RegisterRest(head, "Head");
        RegisterRest(upperLegL, "UpperLeg.L");
        RegisterRest(lowerLegL, "LowerLeg.L");
        RegisterRest(upperLegR, "UpperLeg.R");
        RegisterRest(lowerLegR, "LowerLeg.R");
        RegisterRest(upperArmL, "UpperArm.L");
        RegisterRest(lowerArmL, "LowerArm.L");
        RegisterRest(upperArmR, "UpperArm.R");
        RegisterRest(lowerArmR, "LowerArm.R");

        // 手腕只用于挥刀的甩腕细节，缺了不影响基本姿态，所以不记入 MissingBones。
        if (handR != null)
        {
            restRotations[handR] = handR.localRotation;
        }

        if (hips != null)
        {
            restHipsPosition = hips.localPosition;
            restHipsRotation = hips.localRotation;
        }

        // 骨骼动画会超出导入时烘焙的网格包围盒，关闭视锥剔除避免抖动消失。
        // 我们在 LateUpdate 里直接改骨骼，强制每帧重算蒙皮矩阵，避免一帧延迟与编辑器预览不刷新。
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            smr.forceMatrixRecalculationPerRender = true;
        }

        initialized = true;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!initialized)
        {
            EnsureInitialized();
        }

        var dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        var delta = transform.position - lastPosition;
        lastPosition = transform.position;
        var horizontalSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        horizontalSpeed = Mathf.Min(horizontalSpeed, fullWalkSpeed * 3f);

        var speedT = 1f - Mathf.Exp(-speedSmoothing * dt);
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, horizontalSpeed, speedT);

        var span = Mathf.Max(0.001f, fullWalkSpeed - idleSpeedThreshold);
        var targetBlend = Mathf.Clamp01((smoothedSpeed - idleSpeedThreshold) / span);
        walkBlend = Mathf.MoveTowards(walkBlend, targetBlend, blendSharpness * dt);

        TickAttack(dt);

        // 步频随速度略微变化，慢走时不会像快进。
        var strideScale = Mathf.Clamp(smoothedSpeed / fullWalkSpeed, 0.55f, 1.35f);
        phase += dt * Mathf.PI * 2f * Mathf.Lerp(0f, strideFrequency * strideScale, walkBlend);
        idlePhase += dt * Mathf.PI * 2f * idlePhasePerSecond;

        ApplyPose(phase, walkBlend);
    }

    /// <summary>外部主动触发一次挥刀（例如受击反击、脚本化演出）；正在挥刀时从头重播。</summary>
    public void TriggerAttack()
    {
        attackActive = true;
        attackTime = 0f;
    }

    /// <summary>立刻结束挥刀（死亡、离开场地时使用），下一帧回到走路/待机姿态。</summary>
    public void CancelAttack()
    {
        attackActive = false;
        attackTime = 0f;
        wasAttacking = false;
    }

    private void TickAttack(float dt)
    {
        if (!combatBound)
        {
            BindCombat();
        }

        // GroundEnemyCombat 一般在出怪时添加，可能晚于 Awake，所以每帧兜底一次绑定。
        if (combat == null)
        {
            BindCombat();
        }

        if (combat != null)
        {
            var attackingNow = combat.IsAttacking;
            if (attackingNow && !wasAttacking)
            {
                TriggerAttack();
            }

            wasAttacking = attackingNow;

            // 近战期间持续连砍：一段动作播完还没打到下一刀就再来一次。
            if (loopWhileAttacking && attackingNow && !attackActive)
            {
                TriggerAttack();
            }
        }

        if (!attackActive)
        {
            return;
        }

        attackTime += dt;
        if (attackTime >= attackDuration)
        {
            if (loopWhileAttacking && combat != null && combat.IsAttacking)
            {
                attackTime -= attackDuration;
            }
            else
            {
                attackActive = false;
                attackTime = 0f;
            }
        }
    }

    private void BindCombat()
    {
        if (combatBound)
        {
            return;
        }

        combat = GetComponent<GroundEnemyCombat>();
        if (combat == null)
        {
            combat = GetComponentInParent<GroundEnemyCombat>();
        }

        if (combat == null)
        {
            return;
        }

        combat.Attacked += OnCombatAttacked;
        combatBound = true;
    }

    private void UnbindCombat()
    {
        if (!combatBound)
        {
            return;
        }

        if (combat != null)
        {
            combat.Attacked -= OnCombatAttacked;
        }

        combatBound = false;
        combat = null;
    }

    private void OnCombatAttacked(GroundEnemyCombat source, Transform target)
    {
        // 真正的伤害结算时刻就是出刀时刻：已经接近收招就重新起手，保证每一刀都有动作对应。
        if (!attackActive || AttackProgress >= attackRecoverFraction)
        {
            TriggerAttack();
        }
    }

    /// <summary>
    /// 采样并应用指定姿态。phase 为步伐相位（弧度），walkBlend 0=待机 1=走路。
    /// 供运行时驱动、编辑器预览和测试共用。
    /// </summary>
    public void ApplyPose(float stepPhase, float blend)
    {
        ApplyPose(stepPhase, blend, attackActive ? AttackProgress : -1f);
    }

    /// <summary>
    /// 采样并应用指定姿态，并叠加挥刀动作用 progress（0..1）指定到哪一帧。
    /// progress 小于 0 或大于 1 表示不叠加挥刀（仅走路/待机）。
    /// </summary>
    public void ApplyPose(float stepPhase, float blend, float attackProgress)
    {
        if (!initialized)
        {
            EnsureInitialized();
        }

        blend = Mathf.Clamp01(blend);
        var idle = 1f - blend;
        var right = transform.right;
        var up = transform.up;
        var forward = transform.forward;

        var swing = Mathf.Sin(stepPhase);
        var doubleStep = Mathf.Cos(stepPhase * 2f);

        // 髋部：整体起伏 + 走路侧倾 + 待机呼吸起伏。
        if (hips != null)
        {
            var bob = doubleStep * hipBobMeters * 0.5f * blend;
            var idleBob = Mathf.Sin(idlePhase * 1.35f) * idleBobMeters * idle;
            var localUp = hips.parent != null ? hips.parent.InverseTransformDirection(up) : up;
            hips.localPosition = restHipsPosition + localUp * (bob + idleBob);
            hips.localRotation = restHipsRotation;
            Swing(hips, restHipsRotation, swing * hipRollDegrees * blend, forward);
            Swing(hips, hips.localRotation * Quaternion.identity, -swing * torsoTwistDegrees * 0.4f * blend, up);
        }

        // 躯干前倾与反向扭转，胸腔叠加呼吸。
        Swing(spine, RestOf(spine), walkLeanDegrees * blend, right);
        Swing(spine, spine != null ? spine.localRotation : Quaternion.identity, -swing * torsoTwistDegrees * blend, up);
        Swing(chest, RestOf(chest), Mathf.Sin(idlePhase) * breathDegrees * idle, right);
        Swing(chest, chest != null ? chest.localRotation : Quaternion.identity, Mathf.Sin(idlePhase * 0.7f) * idleSwayDegrees * idle, up);

        // 头：待机时轻轻左右看，走路时保持稳定并轻微下压。
        Swing(head, RestOf(head), Mathf.Sin(idlePhase * 0.9f + 0.6f) * idleSwayDegrees * 1.6f * idle, up);
        Swing(head, head != null ? head.localRotation : Quaternion.identity, walkLeanDegrees * -0.6f * blend, right);

        // 腿：左右反相；小腿只在向后摆/离地阶段弯曲，避免膝盖反折。
        var leftLeg = swing * legSwingDegrees * blend;
        var rightLeg = -swing * legSwingDegrees * blend;
        var leftKnee = Mathf.Max(0f, -swing) * kneeBendDegrees * blend;
        var rightKnee = Mathf.Max(0f, swing) * kneeBendDegrees * blend;

        Swing(upperLegL, RestOf(upperLegL), leftLeg, right);
        Swing(lowerLegL, RestOf(lowerLegL), leftKnee, right);
        Swing(upperLegR, RestOf(upperLegR), rightLeg, right);
        Swing(lowerLegR, RestOf(lowerLegR), rightKnee, right);

        // 手臂与同侧腿反相，肘部保持一点自然弯曲。
        Swing(upperArmL, RestOf(upperArmL), -swing * armSwingDegrees * blend, right);
        Swing(upperArmR, RestOf(upperArmR), swing * armSwingDegrees * blend, right);
        Swing(lowerArmL, RestOf(lowerArmL), armSwingDegrees * 0.35f * blend + Mathf.Sin(idlePhase) * idle * 2f, right);
        Swing(lowerArmR, RestOf(lowerArmR), armSwingDegrees * 0.35f * blend + Mathf.Sin(idlePhase + 1.1f) * idle * 2f, right);

        // 走路时前脚掌轻微下压，让落地更实。
        ApplyFootPitch(upperLegL, lowerLegL, leftLeg, leftKnee);
        ApplyFootPitch(upperLegR, lowerLegR, rightLeg, rightKnee);

        // 挥刀叠加在走路/待机姿态之上；同一函数内写骨骼，避免两个组件抢同一批骨骼。
        ApplyAttackPose(attackProgress, right, up, forward);
    }

    /// <summary>右臂举刀 → 下劈 → 收招；叠加在上一步算好的走路/待机姿态上。</summary>
    private void ApplyAttackPose(float progress, Vector3 right, Vector3 up, Vector3 forward)
    {
        if (progress < 0f || progress > 1f)
        {
            return;
        }

        var windup = Mathf.Clamp01(attackWindupFraction);
        var contact = Mathf.Clamp(attackContactFraction, windup + 0.02f, 0.95f);
        var recover = Mathf.Clamp(attackRecoverFraction, contact + 0.02f, 1f);

        // 上臂前后：+1 = 向后上抬起（起手），-1 = 向前下劈出。
        var armPitch = Keyed(progress,
            0f, 0f,
            windup, 1f,
            contact, -1f,
            recover, -0.2f,
            1f, 0f);

        // 向外张开的幅度（绕角色前向轴），起手把刀带到身体右后方，下劈时让刀臂留在体侧前方而不横穿胸口。
        var armOut = Keyed(progress,
            0f, 0f,
            windup, 1f,
            contact, 0.35f,
            recover, 0.18f,
            1f, 0f);

        // 肘部：起手收肘，下劈时甩直。
        var elbow = Keyed(progress,
            0f, 0f,
            windup, 1f,
            contact, -0.12f,
            recover, 0.22f,
            1f, 0f);

        // 手腕：下劈末段刀尖加速。
        var wrist = Keyed(progress,
            0f, 0f,
            windup, 0.65f,
            contact, -1f,
            recover, -0.15f,
            1f, 0f);

        // 躯干：起手向右拧身蓄力，下劈向左送力。
        var twist = Keyed(progress,
            0f, 0f,
            windup, 1f,
            contact, -0.8f,
            recover, -0.2f,
            1f, 0f);

        // 前倾：起手略后仰，下劈压上体重心。
        var lean = Keyed(progress,
            0f, 0f,
            windup, -0.3f,
            contact, 0.85f,
            recover, 0.22f,
            1f, 0f);

        var torsoTwist = twist * attackTorsoTwistDegrees;
        Swing(spine, spine != null ? spine.localRotation : Quaternion.identity, torsoTwist * 0.55f, up);
        Swing(chest, chest != null ? chest.localRotation : Quaternion.identity, torsoTwist * 0.75f, up);
        Swing(hips, hips != null ? hips.localRotation : Quaternion.identity, -torsoTwist * 0.25f, up);
        Swing(spine, spine != null ? spine.localRotation : Quaternion.identity, lean * attackLeanDegrees, right);
        Swing(chest, chest != null ? chest.localRotation : Quaternion.identity, lean * attackLeanDegrees * 0.5f, right);
        Swing(head, head != null ? head.localRotation : Quaternion.identity, -twist * attackHeadTwistDegrees, up);

        Swing(upperArmR, upperArmR != null ? upperArmR.localRotation : Quaternion.identity,
            armPitch * attackArmSwingDegrees, right);
        Swing(upperArmR, upperArmR != null ? upperArmR.localRotation : Quaternion.identity,
            armOut * attackOutwardDegrees, forward);
        Swing(lowerArmR, lowerArmR != null ? lowerArmR.localRotation : Quaternion.identity,
            elbow * attackElbowDegrees, right);
        Swing(handR, handR != null ? handR.localRotation : Quaternion.identity,
            wrist * attackWristDegrees, right);

        // 起手时刀臂一侧的肩部略微上提，避免整条手臂穿进头盔。
        Swing(upperArmR, upperArmR != null ? upperArmR.localRotation : Quaternion.identity,
            Mathf.Max(0f, armPitch) * attackOutwardDegrees * 0.35f, up);
    }

    /// <summary>按 (time, value) 关键帧对做平滑插值；时间点必须递增。</summary>
    private static float Keyed(float t, params float[] keys)
    {
        if (keys == null || keys.Length < 2)
        {
            return 0f;
        }

        if (t <= keys[0])
        {
            return keys[1];
        }

        for (var i = 0; i + 3 < keys.Length; i += 2)
        {
            var t0 = keys[i];
            var v0 = keys[i + 1];
            var t1 = keys[i + 2];
            var v1 = keys[i + 3];
            if (t >= t1)
            {
                continue;
            }

            var u = Mathf.InverseLerp(t0, t1, t);
            // smoothstep：起手/收招有加减速，接触瞬间保持速度感。
            return Mathf.Lerp(v0, v1, u * u * (3f - 2f * u));
        }

        return keys[keys.Length - 1];
    }

    private void ApplyFootPitch(Transform thigh, Transform shin, float legAngle, float kneeAngle)
    {
        // 脚跟随大腿/小腿的摆动做反向补偿，保持大致水平。
        var foot = FindFoot(thigh);
        if (foot == null)
        {
            return;
        }

        var compensation = -(legAngle + kneeAngle) * 0.65f;
        Swing(foot, RestOf(foot), compensation, transform.right);
    }

    private readonly Dictionary<Transform, Transform> footCache = new Dictionary<Transform, Transform>();

    private Transform FindFoot(Transform thigh)
    {
        if (thigh == null)
        {
            return null;
        }

        if (footCache.TryGetValue(thigh, out var cached))
        {
            return cached;
        }

        Transform found = null;
        foreach (var child in thigh.GetComponentsInChildren<Transform>(true))
        {
            var key = NormalizeName(child.name);
            if (key == "footl" || key == "footleft" || key == "footr" || key == "footright")
            {
                found = child;
                break;
            }
        }

        footCache[thigh] = found;
        return found;
    }

    /// <summary>恢复导入时的静止姿态（停用、退出 Play Mode 时调用）。</summary>
    public void RestoreRestPose()
    {
        attackActive = false;
        attackTime = 0f;

        foreach (var pair in restRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
            }
        }

        if (hips != null)
        {
            hips.localPosition = restHipsPosition;
            hips.localRotation = restHipsRotation;
        }
    }

    private void RegisterRest(Transform bone, string label)
    {
        if (bone == null)
        {
            missingBones.Add(label);
            return;
        }

        restRotations[bone] = bone.localRotation;
    }

    private Quaternion RestOf(Transform bone)
    {
        if (bone != null && restRotations.TryGetValue(bone, out var rest))
        {
            return rest;
        }

        return bone != null ? bone.localRotation : Quaternion.identity;
    }

    /// <summary>绕世界轴旋转骨骼（换算到父级空间），保留导入时的骨骼层级与朝向。</summary>
    private static void Swing(Transform bone, Quaternion restLocal, float degrees, Vector3 worldAxis)
    {
        if (bone == null || bone.parent == null)
        {
            return;
        }

        var localAxis = bone.parent.InverseTransformDirection(worldAxis);
        if (localAxis.sqrMagnitude < 1e-8f)
        {
            bone.localRotation = restLocal;
            return;
        }

        // 即使角度为 0 也要写回 rest：相位经过中立点时不能残留上一帧的姿态。
        bone.localRotation = Quaternion.AngleAxis(degrees, localAxis.normalized) * restLocal;
    }

    private static Transform Find(Dictionary<string, Transform> bones, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (bones.TryGetValue(key, out var bone))
            {
                return bone;
            }
        }

        return null;
    }

    private static string NormalizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Append(char.ToLowerInvariant(c));
            }
        }

        return buffer.ToString();
    }
}
