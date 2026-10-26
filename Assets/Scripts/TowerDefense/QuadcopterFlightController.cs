#if ENABLE_DRONE_CODE
using UnityEngine;
using UnityEngine.InputSystem;

namespace TowerDefense
{
    /// <summary>
    /// 四轴无人机基础飞控（暂独立，未接入游戏逻辑）。
    ///
    /// 操作：
    ///   W/S  — 前/后平移（本机朝向）
    ///   A/D  — 左/右平移（本机朝向）
    ///   Q/E  — 水平左/右偏航
    ///   Shift/Ctrl — 上升/下降（世界 Y 轴）
    ///
    /// 挂载要求：
    ///   - 必须有 Rigidbody（Inspector 中建议 Interpolate=Interpolate）。
    ///   - 旋翼子节点按 QuadcopterDroneAuthoring.RotorNames 命名
    ///     （Rotor_FL / Rotor_FR / Rotor_RL / Rotor_RR），飞控会自动找到并驱动自转。
    ///   - 需要 New Input System 包（项目已启用 activeInputHandler=1）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class QuadcopterFlightController : MonoBehaviour
    {
        // ── 平移参数 ──────────────────────────────────────────────
        [Header("平移")]
        [Tooltip("前后 / 左右最大速度 (m/s)")]
        public float horizontalSpeed = 6f;

        [Tooltip("上升 / 下降最大速度 (m/s)")]
        public float verticalSpeed = 4f;

        [Tooltip("水平速度平滑时间 (s)，越小越灵敏")]
        public float horizontalSmoothTime = 0.12f;

        [Tooltip("垂直速度平滑时间 (s)")]
        public float verticalSmoothTime = 0.10f;

        // ── 偏航参数 ──────────────────────────────────────────────
        [Header("偏航")]
        [Tooltip("最大偏航角速度 (deg/s)")]
        public float yawSpeed = 90f;

        [Tooltip("偏航角速度平滑时间 (s)")]
        public float yawSmoothTime = 0.10f;

        // ── 旋翼视效 ──────────────────────────────────────────────
        [Header("旋翼")]
        [Tooltip("待机转速 (deg/s)")]
        public float rotorIdleSpeed = 600f;

        [Tooltip("满速时叠加的额外转速 (deg/s)")]
        public float rotorMaxExtraSpeed = 1200f;

        // ── 内部状态 ──────────────────────────────────────────────
        Rigidbody  _rb;
        Transform[] _rotors;

        // SmoothDamp 状态：本地坐标空间下的三轴目标速度
        Vector3 _smoothLocalVel;
        Vector3 _velDamp;

        float _smoothYawRate;
        float _yawDamp;

        // 旋翼名与 QuadcopterDroneAuthoring 保持一致
        static readonly string[] RotorNames = { "Rotor_FL", "Rotor_FR", "Rotor_RL", "Rotor_RR" };

        // ── Unity 生命周期 ─────────────────────────────────────────

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // 飞控自己维持高度，不需要引擎重力
            _rb.useGravity      = false;
            // Unity 6 重命名属性（旧版为 drag / angularDrag）
            _rb.linearDamping   = 4f;   // 被动水平阻力，松手后自然减速
            _rb.angularDamping  = 8f;

            // 只允许偏航旋转，Roll / Pitch 由约束保持水平
            _rb.constraints = RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationZ;

            _rotors = FindRotors();
        }

        void FixedUpdate()
        {
            ReadInputAndDrive();
        }

        void Update()
        {
            AnimateRotors();
        }

        // ── 输入与物理驱动 ────────────────────────────────────────

        void ReadInputAndDrive()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // 三轴原始输入（本地坐标）
            var inputX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            var inputY = (kb.shiftKey.isPressed ? 1f : 0f) - (kb.ctrlKey.isPressed ? 1f : 0f);
            var inputZ = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

            // 偏航原始输入
            var inputYaw = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);

            // ── 速度平滑 ──
            var targetLocalVel = new Vector3(
                inputX * horizontalSpeed,
                inputY * verticalSpeed,
                inputZ * horizontalSpeed
            );

            // 水平和垂直使用相同的 smoothTime，可拆开的话分别用两个参数
            // TODO(确认): 是否需要为垂直轴单独配 smoothTime
            _smoothLocalVel = Vector3.SmoothDamp(
                _smoothLocalVel, targetLocalVel,
                ref _velDamp,
                horizontalSmoothTime
            );

            // 将本地水平分量转换到世界空间，垂直分量直接用世界 Y
            var worldHoriz = transform.TransformDirection(
                new Vector3(_smoothLocalVel.x, 0f, _smoothLocalVel.z));

            _rb.linearVelocity = new Vector3(
                worldHoriz.x,
                _smoothLocalVel.y,
                worldHoriz.z
            );

            // ── 偏航 ──
            var targetYawRate = inputYaw * yawSpeed;
            _smoothYawRate = Mathf.SmoothDamp(
                _smoothYawRate, targetYawRate,
                ref _yawDamp,
                yawSmoothTime
            );

            // angularVelocity 单位是 rad/s
            _rb.angularVelocity = new Vector3(0f, _smoothYawRate * Mathf.Deg2Rad, 0f);
        }

        // ── 旋翼动画 ──────────────────────────────────────────────

        void AnimateRotors()
        {
            if (_rotors == null) return;

            // 根据飞行速度插值转速
            var t = _rb.linearVelocity.magnitude
                    / Mathf.Max(horizontalSpeed, 0.001f);
            var degPerSec = rotorIdleSpeed + rotorMaxExtraSpeed * Mathf.Clamp01(t);
            var delta = degPerSec * Time.deltaTime;

            for (var i = 0; i < _rotors.Length; i++)
            {
                if (_rotors[i] == null) continue;
                // 前左/后右顺时针，前右/后左逆时针 → 抵消反扭矩
                var sign = (i % 2 == 0) ? 1f : -1f;
                _rotors[i].Rotate(0f, sign * delta, 0f, Space.Self);
            }
        }

        // ── 工具方法 ──────────────────────────────────────────────

        /// <summary>切换回玩家模式前由协调器调用，清除速度残留。</summary>
        public void ResetVelocity()
        {
            _smoothLocalVel = Vector3.zero;
            _velDamp        = Vector3.zero;
            _smoothYawRate  = 0f;
            _yawDamp        = 0f;
            if (_rb != null)
            {
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        Transform[] FindRotors()
        {
            var result = new Transform[RotorNames.Length];
            for (var i = 0; i < RotorNames.Length; i++)
            {
                result[i] = FindDeep(transform, RotorNames[i]);
                if (result[i] == null)
                    Debug.LogWarning($"[QuadcopterFlightController] 找不到旋翼节点: {RotorNames[i]}", this);
            }
            return result;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}

#endif
