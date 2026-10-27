# 无人机系统游戏接入计划

> 历史计划（2026-10-23）。游戏内接入已于 2026-10-26 完成；当前资源路径、运行行为与验证记录以 [`DroneControlIntegrationGuide.md`](DroneControlIntegrationGuide.md) 和最新源码为准。

文档日期：2026-10-23  
基于：`DroneControlIntegrationGuide.md`（已有原型路线）+ 当前源码分析

---

## 一、现状盘点

| 文件 | 当前状态 | 缺口 |
|------|----------|------|
| `Assets/Scripts/TowerDefense/QuadcopterFlightController.cs` | 独立原型；直接读 `Keyboard.current`；用 `Rigidbody.linearVelocity` 驱动；四旋翼动画完整 | 包在 `#if ENABLE_DRONE_CODE`；不受玩家状态/菜单控制 |
| `Assets/Scripts/FPS/DroneCameraController.cs` | 独立原型；Yaw 旋转机身、Pitch 俯仰云台；自动查找 `Camera_Lens` | 包在 `#if ENABLE_DRONE_CODE`；`OnEnable/OnDisable` 直接锁/释全局光标；节点名与模型约定 `Camera_Gimbal` 不一致 |
| `Assets/Scripts/FPS/PlayerObserverMode.cs` | 已定义 `IObserverModeDriver` 接口与 `ObserverModeInput` 结构体；预留无人机驱动插槽 | 接口未被任何无人机代码实现 |
| `Assets/Editor/QuadcopterDroneAuthoring.cs` | 导入校验与预制体探针；菜单 `Tools/塔防/探针：无人机模型导入检查/预制体检查` | 包在 `#if ENABLE_DRONE_CODE` |
| `Assets/Editor/DroneCameraControllerTests.cs` | 文件存在 | 历史运行 0 个用例；程序集配置未解决 |
| `Assets/Resources/QuadcopterDrone.prefab` | 已接入 | 由生成器写入 Rigidbody / 飞控 / Camera / AudioListener，并由探针验证 |

### 关键约束（接入前必须满足）

1. `ENABLE_DRONE_CODE` 已在 Standalone 配置启用；关闭宏时无人机代码仍按预期排除。
2. 云台节点：`DroneCameraController` 自动查找 `Camera_Lens`，但模型约定是 `Camera_Gimbal`——接入前必须统一，或改成序列化显式引用。
3. `DroneCameraController.OnEnable` 直接调用 `Cursor.lockState = Locked`，会与 `GameStartMenu.CursorOwned` / `TrapSelectionMenu.CursorOwned` 冲突——接入前必须移除。
4. `QuadcopterFlightController` 键盘映射（W/S/A/D/Q/E/Shift/Ctrl）与 FPS 键位重叠，Drone 状态必须停用玩家输入后再启用飞控输入。
5. 玩家实现有两份（`FirstPersonController` / `FPSPackagePlayerMotion`），协调器不能假设只有一种。

---

## 二、目标架构

```
FirstPersonPlayerBootstrap
  └─ DroneControlSession          ← 新增；模式唯一权威
       ├─ state: Player | Drone
       ├─ ref: 玩家根对象（由 Bootstrap 传入）
       ├─ ref: QuadcopterDrone 实例（本局唯一）
       └─ 实现 IObserverModeDriver  ← 复用现有接口
```

**两个明确状态：**

| 状态 | 玩家移动 | 玩家相机 | 飞控 | 无人机相机 | 光标 |
|------|----------|----------|------|------------|------|
| `Player` | 启用 | 启用 | 输入关闭，RB 冻结 | 禁用 | 由玩家/菜单控制 |
| `Drone` | 停用 | 禁用 | 启用 | 启用 | 锁定（菜单打开时例外） |

切换时成对恢复，协调器记录进入前状态，不硬编码"一切原本都启用"。

---

## 三、分步实施计划

### 步骤 1 — 启用符号并验证基线编译（≤15 分钟）

**目标文件**：`ProjectSettings/ProjectSettings.asset`（或 Player Settings UI）

- 在 Editor 与 Standalone 配置的 `scriptingDefineSymbols` 中加入 `ENABLE_DRONE_CODE`。
- 修改后立即触发批处理编译（`-batchmode -logFile`），零 `error CS` 才进下一步。
- 运行 `Tools/塔防/探针：无人机预制体检查`，确认旋翼枢轴 / URP 材质 / `Camera_Gimbal` 节点齐全；将探针结果保存到 `C:\Users\Origami\Desktop\陷阱素材文件夹\QuadcopterDrone\probe-<日期>.txt`。
- **完成标志**：编译无错，探针报告无 FAIL 项。

---

### 步骤 2 — 修复云台节点名与移除光标副作用（≤15 分钟）

**目标文件**：`Assets/Scripts/FPS/DroneCameraController.cs`

两处最小改动：

1. **移除全局光标锁定**：删除 `OnEnable`/`OnDisable` 中对 `LockCursor`/`UnlockCursor` 的调用，改为暴露 `public void SetActive(bool active)` 供协调器控制。
2. **修复节点查找**：在 `FindDeep` 调用中将 `"Camera_Lens"` 改为 `"Camera_Gimbal"`，并在 `Awake` 增加断言：找不到时 `Debug.LogError`（保留警告降级为错误，便于 CI 捕获）。

改完后修复 `DroneCameraControllerTests` 的程序集配置（确认 `asmdef` 包含正确程序集引用），使测试可被发现；补两个最小测试用例：Awake 找到云台节点、SetActive(false) 不修改全局光标。

**完成标志**：EditMode 测试 `DroneCameraControllerTests` 至少 2 个用例通过，全局光标不再被单独组件控制。

---

### 步骤 3 — 实现 `DroneControlSession` 协调器（≤30 分钟，分两次提交）

**目标文件**：`Assets/Scripts/FPS/DroneControlSession.cs`（新增）

#### 提交 3a — 骨架与状态机

```csharp
// 最小骨架，空实现
public enum DroneControlState { Player, Drone }

[DisallowMultipleComponent]
public sealed class DroneControlSession : MonoBehaviour, IObserverModeDriver
{
    public DroneControlState State { get; private set; }

    // 由 Bootstrap 调用
    public void Bind(GameObject playerRoot, GameObject droneRoot) { ... }

    // 切换
    public void RequestToggle() { ... }

    // IObserverModeDriver
    public bool CanEnter(PlayerHealth p) => droneRoot != null && State == DroneControlState.Player;
    public void Enter(PlayerHealth p, Transform view) { ... }
    public void Tick(ObserverModeInput input, float dt) { ... }
    public void Exit() { ... }
}
```

提交骨架后继续填充逻辑。

#### 提交 3b — 切换逻辑

**进入 Drone 状态**（`Enter`）：
1. 记录玩家移动组件 Enable 状态与玩家相机 Enable 状态。
2. 停用 `FirstPersonController`（检查是否激活）和 `FPSPackagePlayerMotion`（同样检查）。
3. 停用玩家 Camera。
4. 调用 `droneFlightController.enabled = true`；调用 `droneCameraController.SetActive(true)`。
5. 将 Rigidbody 的 `isKinematic` 改为 false（飞行模式）。
6. 若当前没有菜单持有光标，锁定光标并通知 `UpdateCursor`。

**退出 Drone 状态**（`Exit`）：
1. 飞行命令归零：将 `QuadcopterFlightController` 的速度状态清零（需暴露 `public void ResetVelocity()`），设置 `isKinematic = true` 冻结无人机。
2. 关闭 `droneCameraController.SetActive(false)`；禁用无人机 Camera。
3. 按记录状态恢复玩家移动组件与玩家 Camera。
4. 光标归还玩家控制流程。

**切换守卫**（`RequestToggle` 中检查）：
- `GameStartMenu.CursorOwned` 为 true → 拒绝切换。
- `TrapSelectionMenu.CursorOwned` 为 true → 拒绝切换。
- 玩家已死亡（`PlayerHealth.IsDead`）→ 拒绝切换。

**完成标志**：新增 `DroneControlSessionTests`，覆盖：守卫拒绝切换、正常切换状态变化、Exit 后 Rigidbody 冻结、光标归还。

---

### 步骤 4 — `QuadcopterFlightController` 补充 `ResetVelocity`（≤10 分钟）

**目标文件**：`Assets/Scripts/TowerDefense/QuadcopterFlightController.cs`

在 `QuadcopterFlightController` 中添加：

```csharp
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
```

注意：`verticalSmoothTime` 在当前实现里复用了 `horizontalSmoothTime`，代码中已有 `// TODO(确认): 是否需要为垂直轴单独配 smoothTime`，本步骤不做决策，保持原样。

**完成标志**：`ResetVelocity` 后 Rigidbody 速度归零（可用 EditMode 测试断言）。

---

### 步骤 5 — Bootstrap 接线与输入绑定（≤20 分钟）

**目标文件**：`Assets/Scripts/FPS/FirstPersonPlayerBootstrap.cs`

- 在 Bootstrap 完成玩家复用/生成后，查找或实例化 `QuadcopterDrone.prefab`（本局唯一，不走经济系统）。
- 创建 `DroneControlSession` 并调用 `Bind(playerRoot, droneRoot)`。
- 注册切换输入动作：新 Input System，默认键 `V`，动作名 `ToggleDroneControl`。
  - 动作只在 `GameStartMenu.CursorOwned == false` 且 `TrapSelectionMenu.CursorOwned == false` 时响应。
  - 响应回调：`session.RequestToggle()`。
- 无人机出生位置：玩家出生点上方 3 m，偏移 `(0, 3, 2)` 本地坐标，避免与玩家模型重叠。

**完成标志**：Play Mode 中按 V 可在 Player/Drone 状态切换，控制台无报错。

---

### 步骤 6 — 场景接入与 HUD（≤20 分钟）

**目标文件**：`Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`（局部修改）、`Assets/Scripts/FPS/DroneHUD.cs`（新增）

场景改动：
- 先提交当前工作区，再操作场景，操作前备份：`C:\Users\Origami\Desktop\陷阱素材文件夹\QuadcopterDrone\scene-backups\<时间戳>\`。
- 通过 Bootstrap 运行时创建无人机，不在场景中手工放置无人机对象（避免与 MapForge 全量导入冲突）。
- 检查 Bootstrap 在场景中已挂载，若未挂载由 Authoring 工具接线。

HUD（`DroneHUD.cs`）：
- 当 `DroneControlSession.State == Drone` 时显示一条"无人机模式 / 按 V 返回"提示。
- 复用现有 TMP 字体 `Resources/UI/Fonts/MenuChinese SDF`，不引入新字体依赖。
- 使用 URP 友好的 Canvas（Screen Space - Overlay）。

**完成标志**：Play Mode 进入 Drone 状态后 HUD 提示出现，返回后消失。

---

### 步骤 7 — 边界场景处理（≤15 分钟）

在 `DroneControlSession` 中订阅以下事件，触发时强制退出 Drone 状态：

| 事件 | 来源 | 处理 |
|------|------|------|
| 玩家死亡 | `PlayerHealth.Died` | `Exit()` → 回 Player 状态（玩家已死，`PlayerLifeStateController` 负责复活流程） |
| 场景卸载 | `SceneManager.sceneUnloaded` | 清理所有引用，不调用 Exit（对象可能已销毁） |
| 无人机被销毁 | `droneRoot.OnDestroy` | `Exit()`，记录日志 |
| 游戏暂停（主菜单/Esc） | `GameStartMenu.CursorOwned` 变为 true | 停止飞控输入，不改变光标所有权 |

**完成标志**：PlayMode 测试：死亡后无人机相机自动关闭，玩家相机恢复。

---

## 四、验收矩阵

| 场景 | 通过标准 |
|------|----------|
| 启用符号编译 | Editor 与 Standalone Player 零 `error CS` |
| 探针通过 | 旋翼枢轴位置 / URP 材质 / Camera_Gimbal 均无 FAIL |
| 按 V 切换 | Player→Drone：玩家输入停用，无人机可飞行；Drone→Player：飞行停止，视角恢复 |
| 菜单打开时按 V | 切换被拒绝，`State` 不变 |
| 陷阱选择菜单打开时按 V | 切换被拒绝 |
| 玩家死亡时处于 Drone | 自动退出 Drone，相机归还 |
| 无人机 Drone→Player 后无速度残留 | Rigidbody 速度为零，下次进入 Drone 不出现突然位移 |
| HUD 显示 | Drone 状态显示提示，Player 状态隐藏 |
| 全量 MapForge 导入后 | 无人机由 Bootstrap 运行时生成，场景无手工无人机对象，不受全量导入影响 |

---

## 五、文件改动汇总

| 文件 | 动作 | 步骤 |
|------|------|------|
| `ProjectSettings/ProjectSettings.asset` | 加 `ENABLE_DRONE_CODE` | 1 |
| `Assets/Scripts/FPS/DroneCameraController.cs` | 移除光标副作用；修复节点名；加 `SetActive` | 2 |
| `Assets/Editor/DroneCameraControllerTests.cs` | 修复程序集配置；补 2 个测试 | 2 |
| `Assets/Scripts/TowerDefense/QuadcopterFlightController.cs` | 加 `ResetVelocity()` | 4 |
| `Assets/Scripts/FPS/DroneControlSession.cs` | 新增 | 3 |
| `Assets/Editor/DroneControlSessionTests.cs` | 新增 | 3 |
| `Assets/Scripts/FPS/FirstPersonPlayerBootstrap.cs` | 接线 Session，注册 V 键动作 | 5 |
| `Assets/Scripts/FPS/DroneHUD.cs` | 新增 | 6 |
| `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity` | Bootstrap 接线确认（不手工放无人机对象） | 6 |

中间素材（探针报告、场景备份）统一放  
`C:\Users\Origami\Desktop\陷阱素材文件夹\QuadcopterDrone\`

---

## 六、已知风险与决策记录

| 风险 | 决策 |
|------|------|
| `FPSPackagePlayerMotion` 与 `FirstPersonController` 并存 | 协调器两个都检查并分别控制 Enable，不假设只有一个 |
| MapForge 全量导入覆盖场景 | 无人机由 Bootstrap 运行时生成，不放在场景 YAML 中 |
| `DroneCameraController` 测试历史 0 用例 | 步骤 2 必须先修复发现问题再扩展测试，编译通过不等于测试通过 |
| 无人机无 Collider | 第一版 MVP 不加碰撞体；若后续加入，独立任务验证不卡几何 |
| 垂直轴 smoothTime 复用水平值 | 保留现有 TODO，本次不决策，平衡阶段再调 |
| `V` 键与现有 FPS 键位的冲突 | 当前 FPS 键位未使用 V，首版沿用；如冲突由 Input Action Asset 统一重绑 |
