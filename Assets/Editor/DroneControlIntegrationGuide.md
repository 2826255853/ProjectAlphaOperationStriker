# 无人机控制系统游戏接入指南

本文是后续把现有四轴无人机接入 FPS + 塔防主流程的实施路线。第一版假设每局开局提供一架无人机，玩家通过切换键接管飞行，再切回第一人称角色；不包含购买、解锁、地图终端或任务系统。

## 当前状态与前置条件

- 资源已就绪：`Assets/Models/QuadcopterDrone/QuadcopterDrone.fbx`、URP 材质与 `Assets/Prefabs/QuadcopterDrone.prefab`。通过 `Tools/塔防/生成无人机预制体` 生成或更新资源，通过对应模型/预制体探针检查资源。
- 运行时代码仍是独立原型：`Assets/Scripts/FPS/DroneCameraController.cs` 控制鼠标视角，`Assets/Scripts/TowerDefense/QuadcopterFlightController.cs` 直接读取键盘并驱动 Rigidbody；二者没有玩家模式、菜单或游戏流程接线。
- 两个运行时脚本、`Assets/Editor/QuadcopterDroneAuthoring.cs` 均包在 `#if ENABLE_DRONE_CODE` 中。当前默认编译不会包含它们。准备接入时，先在 Unity Player Settings 的 Scripting Define Symbols 为目标配置启用此符号，并确认 Editor 与 Player 均能编译；不要在尚未验证时把符号加到发布配置。
- **摄像头节点名称不一致**：模型约定节点是 `Camera_Gimbal`，`DroneCameraController` 自动查找的是 `Camera_Lens`。优先在接线时序列化显式 `cameraPivot` 引用；若该字段不可从接入组件设置，则统一脚本查找名与资源契约并补探针断言。不要依赖自动查找成功。
- 飞行控制器要求同一根对象上有 Rigidbody，并在 `Awake` 中关闭重力、设置阻尼及冻结 Roll/Pitch。模型导入不带 Collider；若玩法要求防撞或落地判定，由运行时/Prefab 接线阶段单独添加合适碰撞体。
- `DroneCameraController.OnEnable/OnDisable` 当前直接锁定/释放全局光标。集成前应移除这项全局副作用，让会话协调器统一管理光标，避免切换时与 `GameStartMenu`、`TrapSelectionMenu` 争夺所有权。
- `DroneCameraControllerTests` 目前在测试运行器中未被发现（历史运行结果为 0 个用例）。恢复功能前先解决测试发现/程序集配置，再将该测试扩展为可运行的控制器测试；编译成功不能视作测试通过。

## 目标结构与状态

新增一个运行时接入协调器，例如 `DroneControlSession`，由它独占模式切换、输入上下文和相机启停。它应在 `FirstPersonPlayerBootstrap` 完成玩家复用或生成后取得玩家根对象，避免依赖固定的场景玩家实现。玩家或无人机被销毁/禁用、必需引用缺失时，协调器要报告可定位的错误，并在仍可行时安全切回玩家模式。

采用两个明确状态：

| 状态 | 玩家角色 | 无人机 | 输入与视角 |
| --- | --- | --- | --- |
| `Player` | 正常移动、视角和武器输入 | 飞控输入关闭，保持当前位置 | 第一人称相机启用，玩家控制输入启用 |
| `Drone` | 移动及武器输入停用，角色留在原地 | 飞控启用 | 无人机相机启用，飞行/观察输入启用 |

模式转换要成对恢复相机启用状态、输入启用状态、Rigidbody 运行状态和光标状态。退出无人机时先将飞行命令归零并让 Rigidbody 停稳/保持位置，再关闭无人机输入；再次进入时不应出现沿用上次速度的突然移动。协调器记录并恢复进入前的状态，不用硬编码“所有东西原先都启用”。

## 分阶段接入

### 1. 启用并验证原型

启用 `ENABLE_DRONE_CODE`，先让当前代码在目标 Editor/Player 配置编译。生成或检查 Prefab，确认 Rigidbody、四个旋翼枢轴、URP 材质与 `Camera_Gimbal` 引用齐全。检查当前飞行参数和控制器直接读取键盘/鼠标的方式，为下一步输入迁移建立基线。

### 2. 建立模式协调与玩家绑定

在 `Assets/Scripts/FPS/` 新增会话协调器，并从玩家 Bootstrap 取得最终生效的玩家对象。不要只针对 `FirstPersonController`：工程还可能使用 `FPSPackagePlayerMotion`，两种玩家移动实现都必须在 Drone 状态停用，并在返回时恢复。将无人机作为本局唯一实例由 Bootstrap/会话协调器创建或绑定；第一版不接经济系统。

用 Unity 新 Input System 创建 Drone 模式切换动作，默认键设为 `V`。切换只在正常游戏中响应；主菜单、暂停菜单、陷阱选择 UI 持有光标时拒绝切换。会话协调器是模式的唯一权威来源，避免玩家控制器和无人机脚本各自判断当前模式。

### 3. 接管输入与相机

进入 Drone 状态时停用玩家的移动、视角和武器操作，停用第一人称 Camera，启用无人机 Camera、`DroneCameraController` 与 `QuadcopterFlightController`。返回 Player 时按记录状态逐项恢复。无人机与玩家 Camera 的启停要互斥，避免双相机同时渲染。

沿用 Unity 新 Input System。原型键位可作为第一版映射：W/S 前后，A/D 左右，Q/E 偏航，Shift/Ctrl 升降，鼠标控制观察方向，V 切换控制。把 Drone 的输入启停与模式绑定；在统一之前，飞控不能在无操控状态继续消费键盘输入。相机控制器只处理机身 Yaw 和云台 Pitch，不管理全局光标、菜单或游戏暂停。

### 4. 统一光标、菜单与暂停行为

只有会话/游戏输入协调层能决定光标锁定与可见性。Drone 状态也必须尊重 `TrapSelectionMenu.CursorOwned`、`GameStartMenu.CursorOwned` 与 `GameStartMenu.BlocksPlayerCursor`；不可由无人机控制器的启停回调直接覆盖菜单状态。

打开主菜单/暂停菜单时，Drone 输入应停止，菜单恢复其自身记录的光标与时间状态；关闭菜单后，仅当会话仍处于 Drone 状态且没有其他 UI 持有光标时才恢复无人机输入。按 V 退出 Drone 时，光标交还给正常玩家控制流程。测试覆盖菜单在 Drone 状态打开、关闭，以及菜单打开期间按 V 的情况。

### 5. 接入主场景并提供反馈

在 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity` 接入协调器与无人机 Prefab，优先由可重复的 Bootstrap/Authoring 流程接线，不手工修改生成器管理的对象。确保每局只创建一架、出生位置不与玩家/地图几何重叠、玩家回切位置不变。首版 HUD 至少显示当前控制模式和切换提示；无人机损坏/失效、玩家死亡/复活、场景卸载时都要终止或重置 Drone 状态，不能遗留锁定相机、输入或光标。

场景或 Prefab 改动前先备份/提交，遵守 Unity YAML 局部定位规则。若无人机仅需要悬停而不参与碰撞，可先使用明确的无碰撞 MVP；若加入碰撞，需验证不会卡在场景几何中，并定义碰撞后能否继续操控或如何退出。

## 验收标准

1. 编译：启用符号后 Editor 与目标 Player 配置编译通过；未启用时工程仍按预期排除无人机代码。
2. 飞控：WASD、Q/E、升降与鼠标观察输入方向正确；Pitch 保持限制范围；停用控制后输入不再影响无人机。
3. 切换：Player → Drone → Player 往返后相机唯一、玩家移动/射击状态恢复、无人机停稳且无速度突变；重复切换不累积输入或重复创建无人机。
4. UI 与生命周期：主菜单/暂停、陷阱选择光标所有权、玩家死亡/复活、无人机引用缺失/销毁及场景卸载时均不会留下错误输入、光标或相机状态。
5. Play Mode：在 `THREE_ROUTE_MERGE_MAP` 中从正常开局实际操控并返回；确认塔防波次与 `GameFlowManager` 未被无人机接管流程意外暂停或改变。

为输入转换、Pitch 限位和状态转换补 EditMode 测试；为菜单交互、主场景接线与往返切换补 PlayMode 验收。先修复 `DroneCameraControllerTests` 的测试发现问题，再依据 Unity Test Framework 实际发现结果确认新测试执行数；报告通过/失败/跳过数量，不以脚本编译代替行为测试。
