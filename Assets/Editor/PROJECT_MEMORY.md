# ProjectAlphaOperationStriker 项目记忆

重建日期：2026-10-16（系统重装后）。本文件保存可随工程保留的项目知识；以当前代码与配置为准，历史路径须重新核实。

## 项目定位与可信来源

- 工程：Unity 第一人称射击 + 塔防；配套 MapForge 浏览器地图编辑器。
- 工程根目录：`C:\Unity Project\ProjectAlphaOperationStriker`。
- 运行时代码：`Assets/Scripts/FPS`、`Assets/Scripts/TowerDefense`、`Assets/Scripts/MapForge*.cs`；编辑器工具与验证：`Assets/Editor`。
- 主场景：`Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`，已在 `ProjectSettings/EditorBuildSettings.asset` 中确认。
- Unity 版本唯一依据：`ProjectSettings/ProjectVersion.txt`。运行前动态匹配安装目录，不把历史版本号固定到脚本或文档。
- 本次确认包依赖：URP、新 Input System、Unity Test Framework；具体版本始终读 `Packages/manifest.json`。`activeInputHandler: 1`。
- 可信顺序：当前源码/配置 > 本文件的源码核对记录 > 技能中的历史环境描述。

## 系统重装后的环境快照（2026-10-16）

| 项目 | 已核实状态 | 对后续工作的影响 |
| --- | --- | --- |
| Unity | `C:\Program Files\Unity <项目记录版本>\Editor\Unity.exe` 对应文件存在；本次未发现正在运行的 Unity 进程 | 每次使用前重新匹配版本与进程 |
| Git | PATH 不可用；`C:\Program Files\Git\cmd\git.exe`、`bin\git.exe` 不存在 | 未取得 git status/diff；不能宣称工作区干净，也未执行提交 |
| .NET | `C:\Program Files\dotnet\dotnet.exe` 可定位 | SDK 可用性与编译尚未验证 |
| Node / Blender | PATH 不可用 | 不能据此认定机器所有位置都未安装 |
| Python | 仅定位到 `C:\Users\ASUS\AppData\Local\Microsoft\WindowsApps\python.exe` | 可能是应用执行别名，未验证真实解释器 |
| MapForge | 用户于 2026-10-16 确认原地图编辑器已迁移至 `C:\MapForge`（C 盘根目录）；2026-10-18 统一工程命名 | 启动与维护使用 `C:\MapForge\MapForge.csproj`；2026-10-18 确认 `.validation-build\UnityCompile.csproj` 不存在 |
| 第三方资源 | `Assets/KINEMATION`、`Assets/TextMesh Pro` 目录存在 | 只确认存在，未核验完整性或编译 |
| 陷阱中间素材 | 用户指定的 `C:\Users\Origami\Desktop\陷阱素材文件夹` 不存在 | 本次未创建；后续制作仍遵守指定路径，若需更换账户路径须明确更新约定 |

此表是一次性观察，不是安装/迁移清单。环境变化后更新，不为了解决玩法任务顺带安装或升级工具。

## 后续编辑约束

- 遵守根目录 `AGENTS.md`：先说明目标文件，最多 5 次只读命令后落笔；优先最小改动。
- 只探索 `Assets/Scripts`、`Assets/Editor`、`Assets/Models`、`ProjectSettings`、`Packages`；技能按其指定路径读取。
- 不读取或遍历 Library、Logs、Temp、obj、Blender 源文件及根目录日志；大型 Unity YAML 只定位并读取局部。
- 输入使用新 Input System；生成资产优先修改 Authoring/Importer。
- 中间素材遵守用户指定的桌面素材目录；系统重装后的旧账户路径不能当作已验证环境。

## 玩家与输入

- 菜单中文字体修复（2026-10-23）：`GameStartMenuView` 统一加载 `Resources/UI/Fonts/MenuChinese SDF`，初始化时也替换旧预制体中所有文本（含未激活的设置页）。源字体为随包携带的 Noto Sans CJK SC，路径 `Assets/Resources/UI/Fonts/NotoSansCJKsc-Regular.otf`，同目录保留 OFL 许可；不依赖目标机器的系统字体。`GameStartMenuAuthoring.GenerateAndValidate` 生成动态 TMP 字体、预填菜单及“继续游戏”字符、保存材质与图集子资源并重建菜单预制体；`Validate` 检查全部标签和字符覆盖，结果写到当前用户桌面 `GameStartMenu/font-validation.txt`。本次仅修字体，不改变战斗 HUD 显隐。
- 字体验收：Unity 编译、生成及 14 个标签（加“继续游戏”动态文字）的字符覆盖检查通过；主菜单与设置页渲染预览分别在 `C:\Users\ASUS\Desktop\GameStartMenu\font-main-menu.png`、`font-settings.png`，已确认中文字形正常。生成日志 `font-generation.log` 包含成功退出标记且无 C# 编译错误或异常；主场景、ProjectSettings、构建场景设置、包清单的前后 SHA256 均一致。备份在同目录 `font-fix-backup`，预览工具不保存或修改游戏场景。

- `FPS/FirstPersonPlayerBootstrap.cs` 在场景加载后复用现有 KINEMATION `FPSPlayer` 或旧 `FirstPersonController`；都没有才加载 `Resources/FPSPlayer`。负责补生命、观察、攀爬、移动、射击与 UI 桥接，并停用作为出生参考的场景摄像机。
- `FPS/FirstPersonController.cs` **同一文件有多个实现**，包括 `FirstPersonController`、`FPSPackagePlayerMotion`、`FPSHitscanShooter`；改移动/鼠标/射击前查所有相关类。KINEMATION 集成是明确依赖，不能把它当成纯旧控制器工程。
- 光标所有权既受 `TrapSelectionMenu.CursorOwned`，也受 `GameStartMenu.CursorOwned` / `BlocksPlayerCursor` 控制。两份 `UpdateCursor` 都必须尊重菜单，其他恢复光标的路径也要一致。
- `GameStartMenu`（`Assets/Scripts/FPS/GameStartMenu.cs`）负责开始/暂停状态、输入禁用、Esc、光标和设置值；界面由 `GameStartMenuView` 构建。运行时优先加载 `Assets/Resources/UI/GameStartMenu.prefab`，预制体壳缺少已序列化布局时会自行构建 Canvas，并从 `Assets/Resources/UI/MainMenuBackground.png` 读取背景。正式视觉源图为 `Assets/UI/MainMenuBackground.png`；现有项目编译正常时，可运行 `Tools/行动打击/生成战术主菜单`（`GameStartMenuAuthoring.Generate`）将完整 Canvas 烘焙到同一 Prefab 路径，`验证战术主菜单` 用于资源接线检查。
- `PlayerHealth` 提供生命事件；`PlayerLifeStateController` 负责死亡后暂停控制、隐藏模型、进入 `PlayerObserverMode` 并延时复活；默认复活延迟 3 秒，优先指定/场景命名出生点，否则回到初始位置。它使用真实时间等待，不等同于塔防失败。
- `PlayerSprintAimBridge` 桥接冲刺与瞄准；`Ladder.cs` 同时定义梯子与攀爬行为；`WeaponDamageProfile` / `WeaponDamageAuthoring` 管武器伤害配置。

## 出怪、战斗与胜负

- `WaveManager` 管总波数；`EnemySpawnPoint` 管每个入口的波配置、间隔、地面/飞行生成与奖励参数。入口可能多份，不能假设只有一个。
- `EnemySpawnPoint` → `EnemyInstance`（入口/波次/类型/本次击杀奖励）→ `EnemyHealth` 与 `MonsterPathFollower`。到达和死亡各有结算路径，避免重复结算。
- 地面移动复用 `MonsterPathGrid`；`GroundEnemyCombat` 管追击玩家/陷阱及返回任务路径。飞行走 `InitializeFlying`，不做网格寻路或地面投影，不附加 `GroundEnemyCombat`。
- 飞行怪兜底资源名为 `FlyingMonster`；`FlyingSpawnPointAuthoring` 生成 `Assets/Resources/FlyingMonster.prefab`，固定入口名 `Enemy Spawn Flying` 用于判重。
- `EnemyCore` 管核心生命与地面/飞行到达伤害；`GameFlowManager` 判核心死亡失败，所有入口完成且场上没有 `EnemyInstance` 时胜利。失败会停止出怪。

## 经济与购买边界

- `EconomyManager` 是一局共用钱包，带 `RunId`、余额与预约金额。`BeginRun` 是开新局边界，波次变化不能重新发开局钱。默认开局 100 金币是待平衡值。
- 玩家放置必须走 `TrapPurchaseService.TryPurchase`：验证格子 → 预约金额 → 创建陷阱 → 提交扣款；失败释放预约并移除已创建实例。不要绕过服务直接免费调用网格放置接口。
- `TrapPlacementGrid.TryPlaceTrap` 是底层放置接口，编辑器制作等场景可以直接使用。
- 出售走 `TrapSaleService.TrySell`，默认返还比例 50%，支持养成覆盖；直接 `RemoveTrap` 不退款。返还按成本向下取整，避免买卖增币。
- `EnemyInstance` 在有效死亡路径发击杀奖励；`WaveRewardService` 观察各入口波次变化/完成并发波次奖励与提前呼叫奖励。修改奖励时一起检查多入口行为与边界，不能仅凭注释认定玩法结算时机。
- `EconomyHUD`、`EconomyUIState` 提供显示状态；不另建第二份余额。

## 放置网格与几何不变量

- `TrapPlacementController` 负责选槽、瞄准、多网格选择、预览与购买/拆除；`TrapPlacementPreview` 负责表现。场景可能有多种高度的网格，不能假定全图单网格。
- `TrapDefinition` 是陷阱配置资产：价格、预制体、占格、固定世界尺寸、`WalkableFloorTrap`、`MountType`、实体安装包围盒。实际平衡数值以序列化资产为准，不把源码默认值当成所有现有资产的数值。
- `TrapMountType` 同时供定义和网格使用，`Ground` 与 `Wall` 必须匹配；墙面与 `WalkableFloorTrap` 同时开启无效。
- `TrapPlacementGrid.CanPlaceTrap` 是编辑器、运行时、批量放置共用判定：安装类别、占格、跨网格重叠、地刺平地支撑、墙体支撑、实体冲突、怪物通行净空。`TrapPlacementGeometry` 提供公共几何，不另写一套判定。
- 地面网格区分 `Ground` / `Road` / `Platform`。道路生成与放置链支持可通行地刺；地刺不能放在高台。旧文档中“道路全部不可放陷阱”的描述不能覆盖当前实现。
- 怪物网格锚在格角，陷阱网格锚在格心，地面锚点相差本地 `(+0.5, 0, +0.5) × cellSize`。复用 `TrapGridAuthoring.GroundAnchor` / `CellCenter` / `DominantSurfaceTop`，并留意 `TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 的对应定义。
- 地面/平台掩码由 `TrapGridAuthoring` 生成；墙面网格由 `WallTrapGridAuthoring` 或 MapForge 的 `unity.wallTrapGrids` 生成。墙面需要绑定支撑 Collider，验证法线、多个支撑探点、实体包围盒和道路净空；攻击/特效范围不计入实体安装体积。
- `MapForgeWallTrapGrid` 保存墙体对象 ID + 网格 ID；`MapForgeSceneOrganization` 和实时同步共同维护，修改格式时一起检查。

## 陷阱与伤害

| 能力 | 运行时入口 | 生成/资产入口与约束 |
| --- | --- | --- |
| 机枪炮塔 | `AutoSentryTurret` + `SentryFireFeedback` / `SentryShellEjector` / `SentryLaserSight` | 子弹伤害分类为 `Bullet`；美术为 `Assets/Resources/SentryMachineGun.prefab`（四脚支架 + 通用机枪 + 激光瞄准 + 开火后坐/枪口焰 + 抛壳），入口与契约见「哨戒机枪模型与开火/抛壳动画」小节 |
| 对空双联导弹 | `MissileLauncherTurret` → `MissileLauncherWeapon` → `MissileProjectile` | `MissileLauncherTurretAuthoring.CreateLauncherPrefabBatch`；预制体配置为 `Assets/Prefabs/DualMissileLauncher.prefab`，定义分别位于 `Assets/Resources` 和 `Assets/TrapDefinitions` |
| 导弹尾焰 | `MissileExhaustFx` | `Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx`；生成器负责安装两组并替换 URP 材质 |
| 地刺 | `GroundSpikeTrap` | `GroundSpikeTrapAuthoring.CreateAssets`；模型 `Assets/Models/GroundSpike/GroundSpike.fbx`，生成 `Assets/Resources/GroundSpike.prefab` / `.asset` |
| 墙面喷火器 | `FlamethrowerTrap` + `FlamethrowerFeedback` | `FlamethrowerTrapAuthoring.CreateAssetsBatch` / `CreateGraybox`；配置路径 `Assets/Resources/Flamethrower.prefab` / `.asset`；当前生成链含灰盒表现 |
| 燃烧 | `FireStatusEffect` + `FireStatusEffectVisual` | 状态挂在怪物上，独立于点燃它的陷阱 |
| 瞄准陷阱血条 | `TrapHealthBarUI` | 复用 `TrapInstance.CurrentHealth` / `MaxHealth`，场景加载后自建 HUD |

以上资产位置由源码常量确认，未逐个打开资源检查其当前内容。

- 导弹瞄准与速度估计统一走 `MissileAimSolver` / `MissileVelocityTracker`；默认空中目标判定是 `EnemyInstance.MonsterType`，不是世界高度。默认只对空，yaw 可全圈，pitch 限制 0–75°。拦截点超仰角可退回瞄准本体；本体也超限则不能开火。
- 导弹弹道有持续修正预测与冻结拦截点两种模式，伤害归 `Explosive`；命中/溅射自行结算，不应为“没有 Collider”擅自补物理体。
- 地刺状态：`Ready → Extending → Holding → Retracting → Cooldown`；记录上一帧位置以检测快速穿越，每轮攻击去重，伤害归 `Melee`。
- 喷火器状态：`Ready → Delay → Spraying → Cooldown`。攻击是向前的盒形体积，世界尺寸不随父级缩放；按 `EnemyHealth` 去重选靶，不要求目标有 Collider。每轮喷射对同一目标直接伤害一次，并施加燃烧。
- 燃烧在怪物上只有一份，重燃刷新时长并保留较高 DPS，不把不同来源的 DPS 简单相加；只积分有效剩余时间，死亡、重置和禁用时清理。
- 伤害抗性已落地（步骤 1–3，见本文下方专节）：`EnemyHealth` 上有七项可序列化抗性百分比、唯一分类结算入口 `TakeDamage(float, DamageType)` 与事件 `TypedDamaged`；玩家枪械/发射器经 `WeaponDamageProfile` 声明类别进入该入口，陷阱与燃烧经 `TrapDamageType` 兼容映射进入，旧 `Physical` 与无类别调用保持原始伤害。**新增伤害来源一律先选 `DamageType` 再调用分类入口，不要绕开它自行扣血。**
- 血条先射线选靶，失败时用 `FindTrapNearRay` 兜底，并尊重遮挡、身后过滤和菜单光标所有权。部分炮塔无 Collider，因此不能删掉近射线兜底。

## MapForge 管线与重装特别事项

- 当前导入器只接受 **v2** 世界格式，要求 `objects` 层级数据；不会自动迁移旧导出。
- `MapForgeWorldImporter` 是 partial 类，主文件与 `.Authored.cs` 必须一起保留；`MapForgeSceneOrganization` 负责 DTO、合法性、层级、组件映射和引用连接。
- 全量导入：`MapForgeWorldImporter.ImportDefault` 读取 `Assets/mapforge-world.json`，按世界名创建并保存场景。`Import` 会新建场景并覆盖同名场景文件，手工改动可能丢失。`ImportFromMenu` 会弹文件面板，不能作为无人值守批处理入口。
- 实时同步：MapForge 端 `C:\MapForge\MapForgeSync.cs`（2026-10-16 用户确认根目录已迁移，文件使用前核实） → 工程根 `MapForgeSync/mail/pending.json` → `Assets/Editor/MapForgeLiveSync.cs`。编辑器轮询 2 秒、心跳 15 秒；通过 revision/status/applied 文件回执。协议目录是运行状态，不作为源代码提交。
- `MapForgeLiveSync.ApplyPending` 应用增量；确需整图重建时走 `RebuildInPlace`，保留场景身份，并按生成归属保留手工对象。它与 `Import` 的覆盖语义不同。
- 全量同步 world 暂存配置为 `Assets/MapForgeSync/sync-world.json`。增量按稳定对象 ID 更新；墙网格、路径链接、游戏引用与状态回执也属于同步链。
- `MapForgeObjectProperties` 提供对象元数据；`MapForgePathNode` 有 ID 查找、角色和链接；`MapForgeTrigger` 承载地图触发行为。改 JSON 字段时同步检查导入 DTO 与运行时组件。
- **重装风险：** `AutoImportIfNeeded` 在编辑器加载/域重载后比较 `EditorPrefs` 中的内容指纹。指纹缺失时，即使场景已存在，也会调用覆盖场景的 `Import`。系统重装可能清除该指纹；本次未读取注册表，不能判定它是否确实丢失。首次重新打开 Unity 前先备份主场景和地图 JSON，避免自动导入覆盖人工场景修改。
- 生成对象归属名称也保存在 `EditorPrefs`；没有记录时，原地重建会按当前世界文件推导并保留未识别对象。不要将此兜底误认为 `Import` 也能保留旧场景全部内容。

## 常用交互（按当前输入代码）

- `N`：陷阱菜单；`4/5/6/7`：选陷阱槽并进入放置；`Esc`：退出相应菜单/放置状态。
- 放置时左键购买，支持拖放；右键仅触发投影枪预留事件（2026-10-19，计划步骤 1-2）。放置模式外 `E` 拆除准星所指陷阱；配合 Shift 走出售逻辑。拆除与出售的经济行为不同。
- `G`：跳过波间等待。奖励逻辑涉及 `WaveRewardService`，改按键时检查经济影响。

## 验证与预览入口

| 修改范围 | 优先定位的验证 |
| --- | --- |
| 玩家生命、观察/复活 | `PlayerHealthTests`，并进行实际 PlayMode 交互检查 |
| 冲刺/瞄准与武器伤害 | `PlayerSprintAimBridgeTests`、`WeaponDamageProfileTests` |
| 钱包、购买与 UI | `EconomySystemTests`、`TrapPurchaseTests`、`EconomyUITests`、`EconomyAcceptanceTests` |
| 地面怪与飞行入口 | `GroundEnemyCombatTests`、`FlyingSpawnPointTests` |
| 导弹与尾焰 | `MissileLauncherTurretTests`、`MissileLauncherWeaponTests`、`MissileExhaustFxTests` |
| 陷阱血条 | `TrapHealthBarUITests` |
| 墙面放置 | `WallTrapPlacementTests`、`WallTrapGridValidation.Run` |
| 地刺 | `GroundSpikeTrapValidation.Run`、`GroundSpikeStage5Acceptance.Run` |
| 喷火器 | `FlamethrowerPlacementValidation.Run`、`FlamethrowerGrayboxValidation.Run`、`FlamethrowerTimingValidation.Run` / `RunPlayMode`、`FlamethrowerAttackVolumeValidation.Run`、`FlamethrowerFeedbackValidation.Run` |
| 燃烧与伤害类别 | `FireStatusEffectTests`、`TrapDamageClassificationTests`、`EnemyDamageResistanceDataTests`、`EnemyDamageSettlementTests`、`WeaponDamageCategoryTests`、`WeaponDamageProfileTests` |
| 网格对齐/掩码 | `TrapPlacementGridEditor.ValidateTrapGrids` / `ValidateOverlap`；使用数值校验 |

- NUnit `*Tests` 使用 `#if UNITY_INCLUDE_TESTS`；通过 Unity `-runTests -testPlatform EditMode -testFilter <类名>` 运行，不能把 `-executeMethod` 当作执行 NUnit 的替代。
- `*Validation.Run` / `*Acceptance.Run` 是独立编辑器入口，先看方法副作用再调用；部分会创建资源、切换场景或进入 PlayMode，不能当作纯只读检查。
- 静态编辑器方法优先通过 `.agents/skills/alpha-striker-unity/scripts/run-unity-method.ps1` 调用。读 `references/unity-editor.md` 了解日志判定与单实例限制；测试要检查结果 XML 和实际用例数量，不能只看进程退出码。
- 历史 `dotnet build C:\MapForge\.validation-build\UnityCompile.csproj` 当前路径缺失；即使恢复，其 Include 列表也只覆盖部分地图代码，不能当作全项目编译通过。
- 预览：`GroundSpikeTrapAuthoring.RenderPreview`、`MissileLauncherTurretPreview.RenderPreview`、`MissileExhaustPreview.RenderPreview`、`FlamethrowerPlacementPreview.Render` / `RenderGraybox` / `RenderAttackVolume` / `RenderFeedback`。执行前检查输出位置；已有实现中部分走临时目录，喷火器走当前账户桌面，与用户指定旧账户素材路径不一定一致。新增产物应服从用户给定位置。
- 图形修改须有备份/回滚点和可查看预览；优先改生成器，再生成定稿资源。不要在本次记忆恢复任务中运行会写场景/资源的验证。

## 后续任务的最短开始方式

1. 读根目录 `AGENTS.md` 与本文件，选对应模块的源码和相关测试；不重复全项目扫描。
2. 先说明具体目标文件，在探索预算内落下最小修改，再完善；多文件功能先写依赖顺序和验收方式。
3. 需要地图/生成资源时，使用 MapForge 当前目录 `C:\MapForge`，并确认场景备份和导入语义；需要 Unity 时动态核实版本、安装与进程。
4. 用与变更对应的检查验证，并区分静态核对、编译、NUnit 和实际运行；有架构或环境变更时更新本文件。

## 本次完成与验证边界

- 已浏览运行时与编辑器代码索引，核对主要模块接口/数据流、包清单、主场景构建入口及本机工具路径；遵守受限目录与 YAML 局部读取规则。
- 本次仅写项目记忆、Unity 元数据和根目录读取入口；没有修改玩法代码、模型、场景、预制体或包配置。
- 未启动 Unity，未执行编译、NUnit、PlayMode 或会生成资源的验证；Git 当前不可用，未取得差异/仓库干净状态。
- 已确认（2026-10-16，用户提供）：MapForge 已迁移至 `C:\MapForge`。2026-10-18 已统一项目说明、技能、工程文件名、启动脚本及同步服务的根目录查找约定。尚未确认：素材源文件是否可恢复、Python/Blender 实际安装位置、Unity 第三方资源完整性、EditorPrefs 是否仍存在。

## MapForge 命名统一（2026-10-18）

- 配套工程文件为 `C:\MapForge\MapForge.csproj`；启动脚本 `Start-MapForge.ps1`、README 和 `MapForgeSync.FindContentRoot` 同步使用此文件名。
- Unity 代码已使用 MapForge 命名，本次只更新工程说明、技能和计划中的引用；没有改动地图格式、同步协议、场景及材质。
- 当前 Git、.NET SDK、Node 可从 PATH 调用；上面的 2026-10-16 环境快照属于历史记录。
- 轻量 Unity 编译工程 `C:\MapForge\.validation-build\UnityCompile.csproj` 当前不存在，不能继续把该命令当作可用的验证入口。
- 本次集中验证一轮：`dotnet build C:\MapForge\MapForge.csproj` 成功（0 警告、0 错误）；临时服务的 `/api/health` 返回 `ready / MapForge`，首页 HTTP 200，启动脚本 PowerShell 语法检查通过；`tests/unity-sync.test.js` 3 个用例通过、0 跳过。临时服务已停止，同步配置恢复原状；未启动 Unity 或执行 Unity 全项目编译。
- 项目源码及文档已无旧名称引用；Unity YAML 中的 `m_LightmapEditorSettings` 属于引擎序列化字段，保留原名。

## Git 环境恢复（2026-10-18）

- Git 已可从 PATH 运行（本次核对为 `2.55.0.windows.5`）；仓库根目录为 `C:\Unity Project\ProjectAlphaOperationStriker`，现有 `main` 分支与提交历史仍在，未删除或重建 `.git`。
- 保留远程 `origin`：`https://github.com/2826255853/ProjectAlphaOperationStriker.git`；禁用交互提示执行 `git ls-remote --exit-code origin refs/heads/main` 成功，远程 `main` 为 `0bf1726a2f5e8c434bc9ba52c2bbd7675dc3551b`，与本地 HEAD 一致。未验证写权限，未推送。
- 恢复开始时存在用户尚未提交的场景、材质、文档及工程版本改动；保留工作区内容，不重置或覆盖。
- `.gitignore` 继续排除 Unity 缓存、第三方素材及 MapForge 通信状态；补充导出对象、崩溃报告和测试结果的根目录忽略规则。Unity 正式资源及 `.meta` 继续跟踪。

- 初检缺少提交身份；依据最近三次提交中一致的作者记录，仅在本仓库恢复 `user.name = OrigamiTobiichi`、`user.email = 2826255853@qq.com`，`git var GIT_AUTHOR_IDENT` 验证成功。未修改全局 Git 身份。
- `git fsck --connectivity-only --no-dangling` 通过；`git check-ignore` 确认 Unity 缓存、通信状态及新增忽略项被排除，脚本、正式 FBX 和 `.meta` 未被误排除；被忽略的缓存和第三方素材未被跟踪。保留现有未提交改动，未自动暂存或创建提交；未运行 Unity。以上 2026-10-16 的 Git 不可用记录仅为历史快照。

## 投影枪计划步骤 1-2（2026-10-19）

- 计划与详细调用契约：`docs/TrapPlacementProjectionGunPlan.md` 的步骤 1-2 接入记录。
- `TrapPlacementController.PlacementMode` 是已有实例只读状态，`IsPlacementModeActive` 继续供普通射击桥查询；`PlacementModeChanged` 发布进入/退出。切槽、单次购买成功/失败及结束拖放均保留模式；`Esc` 和组件停用退出并重置拖放。
- 输入接口 `OnProjectionGunPrimaryAction()` 仍走 `PlaceAt → TrapPurchaseService.TryPurchase`；`OnProjectionGunSecondaryAction()` 仅发布 `ProjectionGunSecondaryAction`，没有默认游戏行为。`IsProjectionGunInputActive` 同时检查组件启用、现有放置状态和两个菜单的光标所有权。
- 当前输入轮询仍由 `TrapPlacementController.Update` 持有。步骤 3 接入专用投影枪控制器时需移交调用方，避免重复购买；不要再建一套模式或菜单状态。此轮没有制作模型、Prefab、场景接线或动画。
- 当前快速编译 csproj 仍缺失；按项目记录匹配的 Unity 编译无错误，`TrapPlacementProjectionGunContractValidation.Run` 的 26 条契约断言通过。该工具仅在批处理编辑器创建临时对象，不保存场景/资产；检查模式生命周期、购买/拖放扣款、菜单拦截与右键无默认行为。
- 校验前后场景、材质、地图及受保护配置的 SHA256 一致。最终日志：`C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\projection-gun-20261019-165819\unity-contract-final.log`；本轮未运行 NUnit 或 PlayMode。鼠标帧模拟已撤去，真实鼠标和第一人称显示验收留给后续步骤。
- 指定旧账户素材目录的创建操作被 Windows 拒绝；本轮无美术中间产物，代码校验日志/备份使用技能规定的系统临时目录。未改变陷阱美术素材的默认存放约定。

## 投影枪计划步骤 3（2026-10-19）

- 新增 `Assets/Scripts/TowerDefense/TrapPlacementProjectionGunController.cs`。它接管放置模式下的槽位、Esc、左右键和拖放按住/释放轮询，仍通过 `TrapPlacementController` 的既有入口完成预览、购买和右键预留事件；没有新增放置模式或菜单状态。
- `TrapPlacementProjectionGunController` 监听 `PlacementModeChanged`，通过 Inspector 的 `projectionGunPrefab` 和可选 `projectionGunAnchor` 显示/隐藏投影枪实例。未配置 Prefab 时只输出一次明确警告，不创建替代物，也不替换普通武器；步骤3允许先使用空物体或占位 Prefab。
- `FPSHitscanShooter` 在放置模式的 Update 入口和 `FireHitscan` 伤害边界都检查 `TrapPlacementController.IsPlacementModeActive`，避免同一帧普通枪械命中结算；退出放置模式后沿用原武器桥接逻辑。
- 本轮未改场景接线或制作模型。`git diff --check` 仅报告现有场景 YAML 的空字段行尾空白；Unity 批处理契约验证因编辑器进程已运行而跳过，不能据此宣称编译或 PlayMode 已通过。

## 投影枪模型资源交付（2026-10-20）

- 本次只制作模型资源，为已有阶段三控制器准备美术资产；不修改控制器、输入、主场景或 MapForge，不生成 Prefab、手部、骨骼、Animator 或 IK。模型制作对应原计划步骤 4，不代表步骤 5-8 或阶段三运行接线完成。
- 用户本次明确采用当前账户素材目录：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/`，替代本任务旧文档中的 Origami 路径；其他历史素材不搬迁。参考图为同级 `投影枪设计图.png`，未改写。
- 已核实 Blender 可执行文件 `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe`，本次报告版本为 5.2.2 LTS；这是环境快照，不是后续版本锁定要求。使用 Blender 后台建模和 Cycles 预览，没有使用图片生成替代三维模型。
- 素材目录包含 `build_projection_gun.py`、可编辑 `TrapPlacementProjectionGun.blend`、初版源和预览、定稿 FBX、六张最终预览、`material_parameters.json`、`first_person_checks.json`、`validation_report.json`、`validation.log` 与 `HANDOFF.md`。重复生成前备份已有源/FBX 到 `backups/<时间戳>/`；中间文件均不进仓库。
- 项目定稿：`Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun.fbx` 及新建目录/FBX 元数据。FBX 共 14,724 三角面、7,506 个复导入顶点、13 个网格、7 种材质；长 0.318850 m、宽 0.093914 m、高 0.206950 m。装饰件导出时按材质合并，源文件仍保留独立对象和可编辑倒角。
- 根 `TrapPlacementProjectionGun`，模型目标局部坐标枪口 +Z / 向上 +Y、单位米；`Muzzle` 目标局部位置 (0, 0.040, 0.210)，`GripAnchor` 为 (0, -0.035, -0.042)。`ProjectionWindow` 为独立网格，其对象原点不是镜片中心；发射点使用 `Muzzle`。
- 材质槽：`PG_WhiteShell`、`PG_BlueArmor`、`PG_DarkStructure`、`PG_GripRubber`、`PG_ProjectionLens`、`PG_CyanEmission`、`PG_DisplayGlass`。全部不透明、无外部贴图依赖；显示符号为几何，未制作纹理 UV 图集。Unity URP 材质和发光需后续按 HANDOFF 参数重建，不宣称 FBX 能保留 Blender 全部着色表现。
- 校验入口：使用核实的 Blender 调用 `--background --factory-startup --python-exit-code 1 --python <素材目录>/validate_projection_gun.py`。从空场景复导入 FBX，13 项断言通过：面数预算、几何/法线/退化面/闭合边检查、材质、尺度、根与挂点、枪口目标轴向、仅网格和空节点，以及两档 FOV 中央视野与近裁剪检查。详见报告；复制到项目之前核对通过报告与 FBX SHA256 一致。
- 预览为侧/正/俯/三分之四视角，以及 16:9 垂直 FOV 60°/75° 无手部第一人称；已目视检查。相同挂载下模型位于右下方、中央区域无遮挡，最近几何深度 0.310 m。60° 下握把底端出画为构图裁切，不是近裁剪切穿。
- 验证边界：没有启动 Unity、未做 Unity 导入/编译/PlayMode/实际游戏验收。FBX 元数据参照现有模型导入结构，关闭动画/灯光/相机/BlendShape、保留层级且不加 Collider；此配置未由 Unity 实测。Blender 复导入根存在约 +90° X 的轴转换，接入时需核对 Unity 实际根朝向，不应重复旋转子网格。

## 投影枪尾部屏幕补充版（2026-10-20）

- 用户提供补充设计图，要求原模型基础上仅增加尾端屏幕与边框，并先备份、不可直接覆盖原模型。本次按此约束在独立副本制作；项目原始 `Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun.fbx` 未改写。
- 制作前备份原始 Blender 源、原始项目 FBX、建模/校验脚本、参数、预览与交接资料至 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/backups/20261020-192315/`。原始项目 FBX 与备份 SHA-256 相同。
- 可编辑补充版：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/TrapPlacementProjectionGun_screen_update.blend`。一次性建模脚本：同目录 `add_rear_display.py`。桌面输出独立 FBX 为 `TrapPlacementProjectionGun_ScreenUpdated.fbx`；项目另存为 `Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun_ScreenUpdated.fbx`，配独立 `.meta` GUID，不复用原 FBX GUID。
- 新增 5 个独立对象：蓝色外护框、深色内嵌边框、青色发光导光圈、暗色屏幕玻璃、可编辑几何像素图形。屏幕朝模型尾部（设计轴 -S），显示四格状态符号、正向 READY 与分段状态条；模型单位、原根、握持和枪口位置保持不变。
- Blender/Cycles 预览：同目录 `preview_rear_screen_detail.png`（后视细节）和 `preview_rear_screen_three_quarter.png`（整体斜后视）。已目视确认屏幕朝向、文字方向和整体外形；未启动 Unity，Unity 导入与游戏内材质/发光仍需后续实际验收。恢复方式是删除新增 `TrapPlacementProjectionGun_ScreenUpdated.fbx` 及其 `.meta`，不触碰原 FBX；源文件可从上述备份恢复。

- 补充验证：Blender 空场景重新导入新增 FBX 成功，83 个网格、9,242 个顶点、5 个尾屏网格、8 个材质（含独立 PG_RearDisplayCyan），边界尺寸 0.093914 × 0.324500 × 0.206950 m。报告存于桌面素材目录 screen_updated_fbx_validation.json。原项目 FBX 与备份 SHA-256 相同；新旧 Unity .meta GUID 不同。未运行 Unity 导入/游戏验收。

## 四轴无人机模型（2026-10-20）
- 编译隔离（2026-10-23）：无人机功能暂不参与默认编译。`DroneCameraController.cs`、`QuadcopterFlightController.cs`、`QuadcopterDroneAuthoring.cs` 使用 `#if ENABLE_DRONE_CODE`；测试还要求 `UNITY_INCLUDE_TESTS`。需要恢复无人机功能时，在 Unity Player Settings / Scripting Define Symbols 显式加入 `ENABLE_DRONE_CODE`，然后再处理测试程序集配置。

- 本次只交付美术资产与导入工具，不含飞行、输入、AI 或场景接线；不修改场景、MapForge、陷阱逻辑与玩家代码。
- 素材目录沿用当前账户：`C:/Users/ASUS/Desktop/陷阱素材文件夹/QuadcopterDrone/`（生成/校验脚本、.blend、8 张预览、`validation_report.json`、`model_stats.json`、`HANDOFF.md`、`backups/`）。定稿 FBX 进 `Assets/Models/QuadcopterDrone/`，URP 材质进其 `Materials/` 子目录，预制体 `Assets/Prefabs/QuadcopterDrone.prefab`。
- Unity 侧工具 `Assets/Editor/QuadcopterDroneAuthoring.cs`：`ProbeModel` / `ProbePrefab`（写 `%TEMP%/alpha-striker-unity/quadcopter-drone/probe-*.json`）、`CreateDronePrefabMenu`（生成 URP 材质 + 预制体）。菜单在 `Tools/塔防/`。
- 规格：24,392 三角面、128 网格、14 空节点、6 材质；包围盒 0.6264 × 0.1923 × 0.6264 m；Unity 局部空间 +Y 向上、+Z 机头、+X 右；滑橇底面 Y = -0.078。
- 层级契约：`Drone_Root → Body / Arm_FL|FR|RL|RR → Rotor_FL|FR|RL|RR`，另有 `Camera_Gimbal`(0, 0.010, 0.178)、`Payload_Mount`(0, -0.070, -0.020)、`LandingGear_L|R`(∓0.072, -0.060, 0)。旋翼枢轴局部位置 (±0.200, 0.030, ±0.200)，**自转轴 = 枢轴局部 +Y**（`Rotate(Vector3.up, degPerSec*dt, Space.Self)`），实测自转 30° 桨盘漂移 0.3 mm；桨叶半径 0.095、保护环内径 0.108。
- 三条踩过的坑，改模型时别回退：① `export_scene.fbx` 的 `bake_space_transform` 必须为 **False**（为 True 时嵌套空节点逐层叠加坐标系转换，枢轴轴向与桨叶全部错乱）；② 导出前必须把网格顶点烘焙进父级空间（`build_quadcopter_drone.py` 的 flattening 段），FBX 无 parent-inverse 概念；③ Blender 空节点帧会带上 Z-up→Y-up 转换，故旋翼枢轴加 `ROTOR_PIVOT_FIX = +90° X` 补偿后在 Unity 才轴对齐；`.fbx.meta` 的 `bakeAxisConversion` 保持 0（设 1 会把整机转 180°、机头变成 -Z）。
- 材质：FBX 自带内置 Standard（URP 下品红）。`CreateDronePrefabMenu` 生成 `Assets/Models/QuadcopterDrone/Materials/M_DR_*.mat`（URP/Lit）并覆盖 6 个槽位后存预制体；改配色改该脚本 `Palette` 再重跑，不要手改 FBX 材质。`addColliders` 保持 0，碰撞体应在组件层加。
- 验证：Blender 复导入 39/39（`result: PASS`）、Unity 模型探针 PASS、Unity 预制体探针 PASS（6 材质均为 `Universal Render Pipeline/Lit`）；工程内 FBX 与校验件 SHA256 一致。**未做** Unity 实机飞行验收、PlayMode 测试、贴图/UV 图集与 LOD。

- 后续游戏接入路线见 `Assets/Editor/DroneControlIntegrationGuide.md`。当前仍是隔离的控制原型：默认未启用 `ENABLE_DRONE_CODE`，未接入玩家控制、菜单光标或场景流程；模型枢轴节点约定 `Camera_Gimbal` 与控制器默认查找 `Camera_Lens` 不一致，接入前必须显式绑定或统一契约。控制器测试此前编译可过但未被 Unity Test Runner 发现，不能视为功能验收通过。


## 投影枪阶段三续作（2026-10-20，进行中）

- 执行顺序：核对计划阶段三与现有控制器 → 补齐控制器接线和武器显示恢复 → 扩展契约验证并运行 Unity 编译/验证 → 更新原计划和本记忆中的验收结果。
- 复用 TrapPlacementController 的模式、购买和菜单状态；正式模型接入优先通过可重复生成的 Authoring 工具完成。场景或预制体写入前备份，视觉改动提供预览，保留现有无人机和模型未提交文件。

## 投影枪阶段四确认完成（2026-10-20）

- 原计划 `docs/TrapPlacementProjectionGunPlan.md` 步骤四已补齐模型验收与结果；阶段三运行验收及步骤五至八未在本轮执行。基础版与独立尾屏补充版均保留，后续 Prefab 优先用 `Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun_ScreenUpdated.fbx`。
- 新增 `Assets/Editor/TrapPlacementProjectionGunModelValidation.cs` 与 `.meta`；菜单 `Tools/塔防/投影枪/检查阶段四模型并生成预览`，批处理 `TrapPlacementProjectionGunModelValidation.Run`（需要图形，技能脚本加 `-WithGraphics`）。只创建隔离预览场景、临时 URP/Lit 材质和校验报告，不保存场景/Prefab/材质资产。
- 以项目记录匹配的 Unity 实测（本次 `6000.6.6f1`）：226 项检查通过（基础版 43、尾屏版 183），编译无错误、无校验异常；六张 Unity 预览已目视确认。含导入、尺寸、网格/法线/非退化三角面、材质槽、枪口/握持挂点、尾屏结构、两档 FOV 的中心视野与近裁剪检查。
- Unity 根为零位置/零旋转/单位缩放，枪口 +Z、向上 +Y；不需要按 Blender 复导入根旋转额外补 90°。`Muzzle=(0,0.040,0.210)`、`GripAnchor=(0,-0.035,-0.042)`，两版相同。已确认第一人称构图起点 `(0.140,-0.105,0.420)` m、旋转零、16:9 垂直 FOV 60°/75°；尾屏最近几何深度 0.30435 m，预览近裁剪 0.05 m。60° 底端出画是构图裁切。
- 基础版 Unity 统计：13 网格、14,724 三角面、8,146 顶点、7 材质；尾屏版：83 网格、17,852 三角面、14,042 顶点、8 材质。Unity 导入顶点与先前 Blender 回读顶点口径不同，不能混用。尾屏五个独立网格与 `PG_RearDisplayCyan` 保留。
- 产物完整目录 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage4-unity/`，含六张 PNG、`validation-report.json`；最终日志在同素材根目录 `stage4-unity-validation-final.log`。预览使用既有材质参数，只在内存替换；同步着色器编译 + 预热渲染避免第一张全灰，结束恢复编辑器原设置。
- 执行前备份 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/backups/stage4-unity-20261020-201222/`。两份 FBX、主场景与地图 JSON 的执行前后 SHA256 均相同；保留已有无人机资源和其他未提交修改。后续阶段五的完成结果见下节。

## 投影枪阶段五完成（2026-10-20）

- 详细验收记录：`docs/TrapPlacementProjectionGunPlan.md` 的“步骤 5 实际交付与验收”。新增 `Assets/Editor/TrapPlacementProjectionGunAuthoring.cs` 与 `TrapPlacementProjectionGunPrefabValidation.cs`（含 `.meta`）。
- 生成入口 `TrapPlacementProjectionGunAuthoring.CreateAssetsBatch`（菜单 `Tools/塔防/投影枪/生成阶段五材质和 Prefab`）；生成并验收入口 `TrapPlacementProjectionGunPrefabValidation.Run`（菜单 `Tools/塔防/投影枪/生成并验收阶段五 Prefab`，技能脚本需 `-WithGraphics`）。
- 从独立尾屏版 FBX 生成 `Assets/Resources/TrapPlacementProjectionGun.prefab`，八种正式 URP/Lit 材质保存在 `Assets/Models/TrapPlacementProjectionGun/Materials/PG_*.mat`。材质定义由生成器统一提供，阶段四预览复用；已符合定义的材质不重写，更新时复制材质属性并保留 URP 版本元数据，生成结束同步导入。
- 层级为零变换根 `TrapPlacementProjectionGun` → `FirstPersonMount`（相机相对位置 `(0.140, -0.105, 0.420)` m、零旋转）→ `Model`（嵌套尾屏版 FBX、零位置/旋转、单位缩放）。阶段六把根挂到相机/独立挂点，不要再次加模型偏移。原有 `Muzzle`、`GripAnchor`、`ProjectionWindow` 及五个尾屏网格保留。
- 全层级 Layer 2（内置 Ignore Raycast）；Renderer 关闭投射/接收阴影和运动矢量。只有 Transform、MeshFilter、MeshRenderer，不带物理、攻击脚本或动画/骨骼依赖。Prefab 的 URP 材质正常依赖包内编辑器 `AssetVersion.cs`，不将其误判为运行时武器脚本。
- 生成前校验资源与材质槽，已有目标资产先备份至 `stage5-unity/backups/<时间戳>/`。异常时回滚本阶段目标文件，仅保存拥有的资产，不保存场景；回滚故障注入未单独测试。
- Unity 按项目版本匹配，实际编译与批处理通过；577 项验收检查通过。连续两次生成的材质/Prefab 内容、GUID、全部 GameObject/组件本地 fileID 一致；83 个网格、17,852 个三角面，尺寸与阶段四一致。无 C# 编译错误或方法异常，日志含阶段五 PASS 与成功退出标记。当前快速编译 csproj 不存在，使用真实 Unity 编译结果。
- 三张正式 Prefab 预览已目视确认（60°/75° 第一人称 + 斜后视），输出完整目录 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage5-unity/`；报告 `validation-report.json`，成功日志 `stage5-acceptance-final.log`。两版 FBX、主场景/地图及相关元数据、项目/包配置共 13 个受保护文件的前后 SHA256 一致。
- 本阶段未执行运行时接线或 PlayMode。下一步是计划步骤六；阶段三尚未完成的运行验收与步骤七至八保持未验收状态。保留既有无人机资源和其他未提交修改。

## 投影枪步骤六完成（2026-10-21，待验证）

- `TrapPlacementProjectionGunController` 在无 Inspector Prefab 引用时从 `Resources/TrapPlacementProjectionGun` 解析正式资源；有 Prefab 时挂到指定 anchor 或主相机。Prefab 的 FirstPersonMount 已包含相机偏移，不得再叠加偏移。
- 放置模式切入时启用投影枪并暂时关闭当前普通武器 Renderer；退出或控制器停用时恢复 Renderer 原启用状态。普通武器输入屏蔽继续使用 `TrapPlacementController.SetWeaponInputSuppressed`，普通射击伤害边界仍有 `FPSHitscanShooter` 状态检查。
- 本步骤尚未运行自动化或实机验收；模式切换、资源缺失、普通武器恢复及左右键无副作用需步骤七至八验证。

## 投影枪步骤七测试实现（2026-10-21，验证受阻）

- 新增 `Assets/Editor/TrapPlacementProjectionGunStage7Tests.cs`（含 `.meta`），为 EditMode 用例，检查投影枪放置模式启停、缺失 Prefab 一次性警告/无替代物、右键预留事件与菜单门控、hitscan 伤害边界。现有 `TrapPlacementProjectionGunContractValidation` 提供左键购买/费用/拖放及右键无副作用契约覆盖。
- `C:\MapForge\.validation-build\UnityCompile.csproj` 不存在；Unity 批处理在测试运行前因 `FPSPlayer`、`FPSWeapon` 无法解析而全项目编译失败。当时将原因归为 KINEMATION 依赖缺失，未经核实；2026-10-21 续查定位到 `TrapPlacementProjectionGunController.cs` 漏写 `KINEMATION.FPSAnimationPack.Scripts.Player` / `.Weapon` 的 `using`，已补齐。此前阶段七测试和网格回归仍未通过验收，需单独重跑，不能用本次编译修复代替。
- 第一人称实机验收仍属于步骤八，本轮未运行。详细记录见 `docs/TrapPlacementProjectionGunPlan.md` 的步骤七实际交付段。

## FPS 类型引用修复（2026-10-21）

- `Assets/Scripts/TowerDefense/TrapPlacementProjectionGunController.cs` 补齐 Player / Weapon 两个 KINEMATION 命名空间引用；不添加替代类型、不修改第三方包、武器逻辑或资源。
- 使用 `ProjectSettings/ProjectVersion.txt` 对应的本机编辑器进行真实 Unity 批处理：编译无 C# 错误、无异常；`TrapPlacementProjectionGunContractValidation.Run` 返回 `PROJECTION_GUN_STEPS_1_2_PASS: 26 assertions`，日志包含成功退出标记。
- 日志：`C:/Users/ASUS/.codex/visualizations/2026/10/21/01a15cb0-f3df-7341-b260-a44f96111a38/fps-types-fix/unity-compile-contract-20261021-122447.log`。本次未执行阶段七 NUnit、网格回归或步骤八实机验收；不将契约检查等同于这些测试。

## 投影枪步骤七/八续作（2026-10-21）

- 阶段六控制器的 KINEMATION 类型编译问题已由两个 `using` 修复；真实 Unity 编译可通过。项目存在正式 `Resources/TrapPlacementProjectionGun.prefab`，所以将 Inspector 引用置空并不能模拟资源缺失，`Resources.Load` 会按设计加载正式 Prefab。测试通过直接调用一次性警告方法验证缺失分支，不移动或删除正式资源。
- 新增 `Assets/Editor/TrapPlacementProjectionGunStage78Validation.cs`，批处理 12 条隔离契约断言通过（含放置模式下 `FPSHitscanShooter.FireHitscan` 边界）；阶段 1-2 契约复跑 26 条通过。日志在 `C:/Users/ASUS/.codex/visualizations/2026/10/21/01a15cb5-4be9-73c3-8652-b3d4467bf91f/` 下，文件为 `stage78-validation-final5.log` 与 `projection-gun-contract-final.log`。
- `-runTests -testPlatform EditMode -testFilter TrapPlacementProjectionGunStage7Tests` 返回码 0，但未生成 XML/测试发现记录，不算 NUnit 通过。陷阱网格回归未运行。步骤八真实 Play Mode/第一人称输入、普通武器恢复、伤害隔离和菜单视角冻结验收尚未完成；不得把批处理契约测试记作实机验收。

## 投影枪步骤七自动化完成（2026-10-21）

- 以上“验证受阻/未生成 XML”是历史状态，本节取代阶段七的未验收结论；阶段八仍未完成。
- 新增可重复运行入口 `Assets/Editor/RunProjectionGunStage7Tests.ps1`：从 `ProjectSettings/ProjectVersion.txt` 精确定位已安装编辑器；已有 Unity 时拒绝启动第二实例。使用 `-runTests -testPlatform EditMode -extraScriptingDefines=UNITY_INCLUDE_TESTS`，**不要添加 `-quit`**，由异步 Test Runner 写完 XML 后退出。临时 define 不写入 PlayerSettings/包配置；未产出 XML、未发现指定 fixture、零用例、失败、跳过或编译错误均不算通过。
- `TrapPlacementProjectionGunStage7Tests` 扩为 13 项：模式进出、惰性创建/复用、停用后的 Renderer 状态快照恢复和重新订阅、正式 Resources Prefab 回退及挂点偏移、缺资源一次性警告、输入动作抑制/恢复、左键付费/拖放/余额不足、右键事件无出售/拆除副作用、两种菜单门控，以及真实物理靶标在放置时免伤、退出后普通 hitscan 恢复伤害。
- 控制器仅将资源解析后的显示逻辑提取为私有 `ShowResolvedProjectionGun(GameObject)`；缺资源测试直接传入 null，覆盖完整回退分支，不搬移/删除正式 Resources 资产，也不声称已模拟 Resources.Load 本身失败。
- 网格回归发现并修复两条过时断言：墙格 `CellToWorld` 是半格内缩的格心而非格角，类别不匹配文本按墙面目标提示。伤害分类测试显式 `ResetHealth()`，避免 EditMode 未分发 Awake 时初始血量为零。未更改网格、购买、伤害玩法实现。
- 真实 Unity 编译和 NUnit XML 确认 **70/70 通过、0 失败、0 跳过**：投影枪 13、购买 34、墙面网格 7、武器伤害档案 10、伤害类别 6。连续两轮全绿；最终结果目录 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage7-unity/20261021-130014-416/`，包含 `editmode-results.xml`、`editmode.log`、`summary.json` 及标准输出/错误，最终进程退出码也核实为 0。
- 普通编译（不加测试 define）及 `TrapPlacementProjectionGunContractValidation.Run` 另行通过：日志含 `PROJECTION_GUN_STEPS_1_2_PASS: 26 assertions`、成功退出标记，无 C# 编译错误/方法异常。日志 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage7-unity/20261021-125802-201/placement-contract.log`。没有改场景、模型、Prefab、地图或包配置。
- 运行示例：PowerShell 执行本工程 `Assets/Editor/RunProjectionGunStage7Tests.ps1`；默认产物写当前账户桌面“陷阱素材文件夹/TrapPlacementProjectionGun/stage7-unity/<时间戳>”。这是永久项目测试工具，不是模型制作一次性脚本。普通武器 Renderer 隐藏的完整第三方玩家链路、真实鼠标/视角冻结与第一人称画面仍属阶段八；本阶段快照恢复和伤害测试不替代实机验收。

## 投影枪步骤八自动化验收续作（2026-10-22，未通过）

- 新增 `Assets/Editor/TrapPlacementProjectionGunStage8Tests.cs` 与 `Assets/Editor/RunProjectionGunStage8Tests.ps1`。用例从主场景进入 Play Mode，以 Input System 虚拟键鼠事件覆盖实际玩家与控制器链路：投影枪/普通武器 Renderer 显隐、左键放置与普通武器伤害/弹药隔离、右键无经济/拆除副作用、退出后普通射击恢复、菜单光标所有权与视角冻结。测试临时重定向运行时网格/射线和 hitscan LayerMask，不保存场景或 Prefab。
- Unity 6000.6.6f1 多轮运行均未通过，不能记为阶段八验收。最近一次批处理编译失败的原因是 Input System 1.20.0 不支持 `InputSystem.Update(InputUpdateType.Dynamic)`；之后已移除手动 `InputSystem.Update()`，改为在自动输入更新后的 `InputSystem.onAfterUpdate` 临时泵送生产轮询器。因 30 分钟时限未重跑。更早失败显示手动更新未提供所需 `wasPressedThisFrame` 边沿；当前版的事件时序与其余断言均待真实 NUnit/XML 运行确认。
- 运行入口 `Assets/Editor/RunProjectionGunStage8Tests.ps1` 默认结果目录为桌面“陷阱素材文件夹/TrapPlacementProjectionGun/stage8-unity/<时间戳>”，版本从 `ProjectVersion.txt` 探测并拒绝并发 Unity。当前阶段状态是**测试已编写、尚未通过**；下一轮先运行此入口，确认 XML fixture 零失败/零跳过后再更新项目计划。

## 独立改进路线文档（2026-10-21）

- 后续工作汇总入口为 `docs/PROJECT_IMPROVEMENT_ROADMAP.md`，按“已有明确计划 → 此前已提及但未形成实施方案 → 新增建议”排序；每项记录来源、最小交付与验收边界。
- 当前明确计划优先收尾投影枪步骤八真实第一人称验收；阶段七 70/70 自动化完成的最新记录不回退为待办。养成、抗性、数值平衡等按现有记忆/源码预留区分，不将无人机美术交付等同于玩法完成。
- 本次受只读目录限制，未读取 `docs/` 中已有三份方案正文，也未读取旧聊天；路线文档明示此覆盖范围及后续合并入口，不宣称穷尽所有历史需求。本次仅新增路线文档并追加本入口，未运行 Unity/测试或修改玩法、场景、资源。

## 蒙面士兵（普通小怪）模型与骨骼（2026-10-21）

- 参考图为 `C:\Users\ASUS\Desktop\策划\普通小怪_蒙面士兵_三视图.png`；按 `关卡设计示例（15波）.docx` 定位为「普通小怪 / 小怪」（攻击 5、血量 50、移速中），供第 1–15 波地面出怪复用。
- 定稿资源：`Assets/Models/MaskedSoldier/MaskedSoldier.fbx`（身体 5,231 顶点 / 10,102 三角面；刀 152 顶点 / 288 三角面）、`Assets/Models/MaskedSoldier/Textures/MaskedSoldier_Camo.png`、`Assets/Prefabs/MaskedSoldier.prefab`（根节点 1.8 m CapsuleCollider）。
- 生成/导入入口：`Assets/Editor/MaskedSoldierAuthoring.cs`。菜单 `Tools/塔防/蒙面士兵/生成材质与预制体` = `CreateAssetsBatch`（生成 `Assets/Models/MaskedSoldier/Materials/*.mat` 的 URP/Lit 材质并 remap，存预制体）；菜单 `Tools/塔防/蒙面士兵/探针：模型与骨骼导入检查` = `ProbeModel`（只读，报告写到 `%TEMP%\alpha-striker-unity\masked-soldier\probe.json`）；菜单 `Tools/塔防/蒙面士兵/渲染预览图` = `RenderPreview`（需 `-WithGraphics`，输出三张 Unity 实机预览到桌面素材目录）。模型导入配置在 `ConfigureImporter`：Generic、不导入动画、不加 Collider、关网格压缩。
- **FBX 导出缩放**：必须用 Blender `apply_scale_options="FBX_SCALE_ALL"`。用 `FBX_SCALE_NONE` 时 Unity 导入后模型根节点是 100 倍缩放、网格本地 1/100（世界尺寸碰巧对，但脚本/碰撞/根运动易出错）。最终 FBX 已按 `FBX_SCALE_ALL` 重导：Unity 导入缩放 1.0，网格本地包围盒 0.7676 × 0.3780 × 1.8160 m；重导脚本 `reexport_fbx_scale_all.py`。
- 验证：Blender 空场景复导入 FBX 的 41/41 项通过（骨骼层级、刀父级与独立对象、权重归一化/影响数、尺寸、姿态变形、材质）；Unity 6000.6.6f1 批处理成功生成 15 个 URP 材质与预制体，探针确认缩放 1.0、21 骨骼、刀权重违规 0，三张 Unity 实机预览非品红。未做 PlayMode 战斗、动画剪辑或 LOD 验收。
- 骨骼 21 根：`Root / Hips / Spine / Chest / Neck / Head`，每侧 `Shoulder / UpperArm / LowerArm / Hand` + `UpperLeg / LowerLeg / Foot`，另加 `Knife`（父级 `Hand.R`）。骨骼命名使用 Blender 的 `.L` / `.R` 后缀；Unity Humanoid 自动映射不保证，按 Generic 用或手工绑 Avatar。
- **刀与身体分离**：`MaskedSoldier_Knife` 是独立蒙皮网格对象，所有顶点 100% 权重在 `Knife` 骨骼上（校验脚本断言无其他骨骼权重）；隐藏/替换/掉落刀不影响身体。不要把刀合并进 `MaskedSoldier_Body`。
- 蒙皮：躯干/四肢为沿骨链放样的连续网格，按高度/弧长在肩、肘、膝、髋做两骨平滑过渡（每顶点 ≤2 影响）；头盔、护具、手套、靴子、弹袋等硬质装具刚性绑到对应骨骼。站立高 1.816 m，脚底 z≈0.002，Unity 局部空间面向 +Z。
- 中间态与预览：`C:/Users/ASUS/Desktop/陷阱素材文件夹/MaskedSoldier/`，含 `build_masked_soldier.py`、`validate_masked_soldier.py`、`MaskedSoldier.blend`、`previews/`（正/侧/背/三分之四/持刀特写/骨骼姿态/FBX 回读姿态）、`model_stats.json`、`validation_report.json`、`backups/<时间戳>/`。重新生成前脚本会自动备份上一版 `.blend/.fbx`。
- 验证：Blender 空场景复导入 FBX 的 41/41 项通过（骨骼层级、刀父级与独立对象、权重归一化/影响数、尺寸、姿态变形、材质）；Unity 6000.6.6f1 批处理成功生成 15 个 URP 材质与预制体，探针确认缩放 1.0、21 骨骼、刀权重违规 0，三张 Unity 实机预览非品红。未做 PlayMode 战斗、动画剪辑或 LOD 验收。
- 关联修复：`Assets/Editor/TrapPlacementProjectionGunStage8Tests.cs` 补 `using UnityEngine.InputSystem.LowLevel;`（`MouseState` / `KeyboardState` 所在命名空间）；在此之前该文件会阻断全工程编译。

## 普通小怪走路/待机动画（2026-10-21）

- `Assets/Scripts/TowerDefense/MonsterWalkAnimator.cs` 是**程序化骨骼动画**：不建 Animator/AnimationClip、不改 FBX 导入设置（`MaskedSoldierAuthoring.ConfigureImporter` 仍 `importAnimation = false`）。它在 `LateUpdate` 读根节点实际水平位移算速度，0=待机呼吸/轻微摇摆、1=走路摆腿摆臂，臂腿反相、小腿只向后弯；`ApplyPose(phase, blend)` 是预览与测试共用的采样入口，`RestoreRestPose()` 精确还原导入姿态，骨骼按名字（忽略大小写与 `.` `_` 空格）查找，缺骨骼只跳过。
- 预制体由 `MaskedSoldierAuthoring.CreateAssetsBatch` 在根节点加 `MonsterWalkAnimator`；**不要手工给预制体加/改这个组件**，重跑生成器即可。
- **一键接入与场景备份**：`Assets/Editor/MaskedSoldierSceneWiring.cs`，菜单 `Tools/塔防/蒙面士兵/一键接入地面出怪点（含场景备份）` = `WireMenu`，批处理 `WireGroundSpawnersBatch`。它先把 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`（含 `.meta`）复制到 `C:/Users/ASUS/Desktop/陷阱素材文件夹/MaskedSoldier/scene-backups/<时间戳>/`，再用 `SerializedObject` 把**非飞行** `EnemySpawnPoint` 的 `enemyPrefab` 以及逐波覆盖里的空/`TestMonster` 预制体换成 `Assets/Prefabs/MaskedSoldier.prefab`，报告写到同素材目录 `scene-wiring/wiring-<时间戳>.json`（含改前/改后与跳过的对象）。不碰飞行出怪口、路径网格、核心、经济字段。
- 2026-10-21 已实际执行：场景只有 `Enemy Spawn Left / Center / Right` 三个地面出怪口（无飞行出怪口），3 个根 `enemyPrefab` + 9 条逐波覆盖全部改为蒙面士兵，场景 YAML 中 `TestMonster` 引用 0、蒙面士兵 GUID `7710ab44…` 引用 12。备份 `scene-backups/20261021-160246/` 与 shell 备份 `scene-backups/20261021-160217-shell/` 都在上述素材目录。
- 注意：`enemyPrefab` 是 Unity 侧手工字段，MapForge **全量导入**（`MapForgeWorldImporter.Import`）会按世界文件重写出怪点，可能把该字段重置；「Send to Unity」增量同步只改变动对象，一般不受影响。重装/重建场景后重新跑一次一键接入即可。
- 两个必须保留的细节：① `EnsureInitialized` 对每个 `SkinnedMeshRenderer` 设 `updateWhenOffscreen = true` 和 `forceMatrixRecalculationPerRender = true`，否则 LateUpdate 改骨骼会一帧延迟，编辑期批处理预览更是完全看不到姿态；② `Swing` 在角度为 0 时也必须写回 rest，不能提前 return，否则相位过中立点会残留上一姿态（曾导致“并腿相位仍是迈步”）。
- 预览入口仍是 `MaskedSoldierAuthoring.RenderPreview`（需 `-WithGraphics`），除原有三张静态图外新增 `walk_pose_idle/contact/passing/swing.png`，输出到 `C:/Users/ASUS/Desktop/陷阱素材文件夹/MaskedSoldier/previews/`；预览实例改用 `Object.Instantiate`（`PrefabUtility.InstantiatePrefab` 的实例在批处理下不反映姿态）。
- 验证：EditMode `Assets/Editor/MonsterWalkAnimatorTests.cs` 6/6 通过（预制体带组件、21 骨骼零缺失、走路摆腿>10°、待机腿不动、恢复精确、不动根节点）；`MaskedSoldierAuthoring.CreateAssetsBatch` 重新生成预制体成功。未做真实 Play Mode 验收，也不包含 LOD、受击/死亡动画。

## 怪物伤害抗性步骤 1：类别与数据契约（2026-10-22）

- 依据 `docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md` 第 1 步实施；**只建立数据契约，不改变实际扣血行为**。方案其余步骤（统一结算入口、伤害来源接入、Prefab 配置、回归）尚未实施，提案文档本身未改动。
- 新增 `Assets/Scripts/TowerDefense/DamageType.cs`：七值权威枚举 `Fire=0 / Electric=1 / Frost=2 / Light=3 / RangedPhysical=4 / MeleePhysical=5 / Explosive=6`，数值是序列化契约，只能追加不能重排；`DamageTypeCatalog.All / Count / IsDefined / GetDisplayName` 提供遍历与中文标签。
- `Assets/Scripts/TowerDefense/TrapDamageType.cs` 保留原枚举与全部数值，新增 `TrapDamageTypeCompatibility.TryGetDamageType(this TrapDamageType, out DamageType)`：`Fire→Fire`、`Explosive→Explosive`、`Energy→Electric`、`Bullet→RangedPhysical`、`Melee→MeleePhysical`；**旧 `Physical` 与未知值返回 false（无类别）**，禁止猜测成物理子类。
- `Assets/Scripts/TowerDefense/EnemyHealth.cs` 增加七项 `[SerializeField] float xxxResistancePercent`（默认 0、随实例/Prefab 序列化、不依赖共享资产），以及 `GetResistancePercent(DamageType)`、`SetResistancePercent(DamageType, float)`（拒绝 NaN/Infinity 与未定义类别）、`GetDamageMultiplier(DamageType)`（`max(0, 1 - percent/100)`，纯查询）。`ResetHealth` 明确不重置抗性；`TakeDamage(float)` / `TakeDamage(float, TrapDamageType)` 与 `Damaged` / `TrapDamaged` / `Died` 均未改动。
- EditMode 用例 `Assets/Editor/EnemyDamageResistanceDataTests.cs` 20 项：七类枚举完整唯一且旧枚举数值不变、五项映射、`Physical`/未知无类别、默认全零、七字段序列化存在、实例互不串值、运行时克隆继承且改克隆不影响源、`ResetHealth` 保持配置、非法输入被忽略、倍率公式（含 100%/150% 归零、-100% 双倍）。
- 真实 Unity 6000.6.6f1 批处理 `-runTests -testPlatform EditMode -testFilter "EnemyDamageResistanceDataTests|TrapDamageClassificationTests"`：**26/26 通过、0 失败、0 跳过、0 个 `error CS`**；结果 `C:/Users/ASUS/AppData/Local/Temp/alpha-striker-unity/damage-resistance-step1/20261022-170934/editmode-results.xml`。日志中 `CaptureException` 只是既有 `EnemyHealth.TakeDamage` 的 `Debug.Log` 调用栈，不是异常。未改场景、Prefab、材质或包配置。

## 怪物伤害抗性步骤 2：唯一分类结算入口（2026-10-22）

- 依据 `docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md` 第 2 步实施；只改 `Assets/Scripts/TowerDefense/EnemyHealth.cs` 与测试，未动伤害来源（步骤 3）、Prefab 或场景。
- 三个公开入口，都收敛到同一个私有 `ApplyDamage(requestedAmount, categorized, damageType, legacyTyped, legacyType)`：
  - `TakeDamage(float)` = 无类别原始伤害，不查抗性表，不发布 `TrapDamaged`/`TypedDamaged`。
  - `TakeDamage(float, DamageType)` = 唯一权威分类入口，按本实例 `max(0, 1 - percent/100)` 结算一次。
  - `TakeDamage(float, TrapDamageType)` = 兼容层，`TryGetDamageType` 成功则按映射类别结算并发布旧 `TrapDamaged`；旧 `Physical`/未知值走无类别原始伤害（仍发布 `TrapDamaged`，保持旧语义）。
- 新增 `TypedDamaged(EnemyHealth, float, DamageType)`：报告**抗性后实际扣血**（且被剩余血量截断），与 `TrapDamaged` 一致，不再把请求伤害当实际伤害。事件顺序保持 `Damaged → TrapDamaged（仅旧入口）→ TypedDamaged → Died`；`Died` 仍只在跨过 0 血时触发一次，`deathRaised` 后所有入口直接返回。
- 新增纯查询 `CalculateDamage(amount, DamageType)`（同公式、不扣血不发事件），供后续预览/UI 复用。
- 边界：最终伤害 `<= 0`（抗性 ≥100%、NaN）时不扣血、不发任何事件、不触发死亡；伤害请求 `<=0`/NaN 与死亡后调用照旧忽略；`Debug.Log` 文案格式不变，只是把数值换成实际扣血。
- EditMode 用例 `Assets/Editor/EnemyDamageSettlementTests.cs` 35 项：七类零/正/负抗、100%/150% 归零不发事件、请求值 vs 实际值（含被剩余血量截断）、无类别不吃抗性、五种旧映射只结算一次且同时发布两个事件、旧 `Physical` 不吃抗性、死亡只触发一次且死后免疫、`<=0` 忽略、`CalculateDamage` 纯查询、`ResetHealth` 后抗性仍生效。`LogAssert.ignoreFailingMessages` 只用于刻意击杀的用例（EditMode 下 `Destroy` 会报引擎错误）。
- 复跑入口 `Assets/Editor/RunEnemyDamageResistanceTests.ps1`（默认 fixture：`EnemyDamageSettlementTests`、`EnemyDamageResistanceDataTests`、`TrapDamageClassificationTests`、`WeaponDamageProfileTests`、`FireStatusEffectTests`；`-Fixtures` 可覆盖）。真实 Unity 6000.6.6f1：**77/77 通过、0 失败、0 跳过、0 个 `error CS`**，各 fixture 35/20/6/10/6；最终结果 `C:/Users/ASUS/Desktop/陷阱素材文件夹/EnemyDamageResistance/step2-unity/20261022-172502-024/editmode-results.xml`。
- `FireStatusEffectTests` 此前从未进过 EditMode 回归集，本次才发现整份夹具在 EditMode 下跑不起来，**与抗性改动无关**。三处纯测试侧修正，运行时逻辑未动：① `SetUp` 补 `health.ResetHealth()`——EditMode 不派发 `Awake`，`CurrentHealth` 为 0 会让 `FireStatusEffect.Ignite` 在 `IsDead` 早退处直接返回，`burn` 变 null；② `ReignitionRefreshesDurationWithoutStackingAndKeepsHigherRate` 补 `burn = GetComponent<FireStatusEffect>()`，它原来直接用上一个用例残留的引用（NUnit 复用同一 fixture 实例）而抛 `MissingReferenceException`；③ `DisablingEnemyClearsBurn` 改为反射调用私有 `OnDisable`，因为 EditMode 不派发生命周期消息，而 `SendMessage("OnDisable")` 会触发原生 `ShouldRunBehaviour()` 断言日志。**以后给这类“只有 PlayMode 才有意义”的用例写 EditMode 版时直接沿用这套补偿，不要指望 `Awake`/`OnEnable`/`OnDisable` 在 EditMode 生效。**
- 未做：步骤 3 的武器/陷阱来源接入、Prefab 抗性数值、PlayMode 实机验收；燃烧在真实运行时的表现只到 EditMode 行为断言，未实机核对。

## 怪物伤害抗性步骤 3：接入已有伤害来源（2026-10-22）

- 依据 `docs/ENEMY_DAMAGE_RESISTANCE_PROPOSAL.md` 第 3 步实施；只改 `Assets/Scripts/FPS/WeaponDamageProfile.cs`、`Assets/Scripts/FPS/FirstPersonController.cs`（`FPSHitscanShooter`）、`Assets/Scripts/TowerDefense/FireStatusEffect.cs`（仅注释）三处运行时文件 + 新增测试，未改场景、Prefab、美术或包配置。
- `WeaponDamageProfile.cs` 新增：`WeaponDamageTypeMode`（`Auto=0` 显式覆盖其余 7 类）、`WeaponDamageTypeMapping.TryGetForClass/TryGetForMode`、`WeaponDamageProfile.TryGetDamageType(out DamageType)` 与 `ConfigureDamageType(mode)`。**家族映射固定**：`Rifle/Pistol/Smg/Shotgun/Sniper/Lmg → RangedPhysical`、`Launcher → Explosive`、`Unknown → 无类别（返回 false）`。`WeaponDamageStats` 结构体与 8 参构造**故意未改**，避免动到既有资产序列化与测试。
- `FPSHitscanShooter` 新增 `ResolveDamageTypeFor(FPSWeapon, bool usePerWeaponDamage, out DamageType)`（档案优先，否则按解析出的家族映射；`usePerWeaponDamage=false` 或武器为空时返回无类别，**不按名字猜类别**）、只读 `CurrentWeaponDamageType`，`FireHitscan` 命中 `EnemyHealth` 时改为按类别走 `TakeDamage(float, DamageType)`，无类别时仍走 `TakeDamage(float)`。`SendMessageUpwards("TakeDamage", …)` 兼容分支保持原样。
- 陷阱与状态来源：`AutoSentryTurret`(`Bullet`)、`GroundSpikeTrap`(`Melee`)、`MissileProjectile`(`Explosive`)、`FlamethrowerTrap`(`Fire`)、`FireStatusEffect`(`Fire`) 全部已走 `TakeDamage(float, TrapDamageType)` → 兼容映射，**本就只结算一次**，故不改调用点，只在 `FireStatusEffect.Tick` 加注释说明“映射 1:1 到 Fire、只结算一次”。
- 差异记录（与提案文本不符处，按提案要求先记录不改行为）：项目当前**没有**玩家近战武器与手雷命中的伤害代码（`Assets/Scripts` 内无 melee/grenade 命中结算），所以“近战=MeleePhysical、手雷/RPG=爆炸”只通过档案声明落地：近战武器在 `WeaponDamageProfile` 上把 `damageTypeMode` 显式设为 `MeleePhysical`；RPG/发射器由 `Launcher → Explosive` 自动成立（玩家发射器走同一 hitscan 桥）。不为此新增近战/爆炸攻击系统。
- 新增 `Assets/Editor/WeaponDamageCategoryTests.cs` 18 项：六种枪族→远程物理、发射器→爆炸、`Unknown` 无类别、档案 Auto 派生、显式 `MeleePhysical` 覆盖与回退 Auto、无武器时无类别、暴露 `CurrentWeaponDamageType`、枪械只吃远程物理抗性、发射器只吃爆炸抗性、无类别武器保持原始伤害、燃烧按火抗只结算一次（`TrapDamaged` 恰好 1 次）、燃烧不吃爆炸/物理抗性、四种旧陷阱类别各只结算一次。
- `Assets/Editor/RunEnemyDamageResistanceTests.ps1` 的默认 fixture 增加 `WeaponDamageCategoryTests`。真实 Unity 6000.6.6f1：**95/95 通过、0 失败、0 跳过、0 个 `error CS`**，各 fixture 35/20/6/10/18/6；结果 `C:/Users/ASUS/Desktop/陷阱素材文件夹/EnemyDamageResistance/step2-unity/20261022-173601-983/editmode-results.xml`（目录名沿用 step2-unity）。
- **坑（本机环境）**：本机 `pwsh` 实际是 **Windows PowerShell 5.1**，它按 ANSI(GBK) 解码**无 BOM** 的 `.ps1`。凡是带中文路径字面量的脚本，一旦编辑工具把 BOM 去掉，运行时会静默把路径写成乱码目录（本次就把结果写到 `Desktop\闄烽槺绱犳潗鏂囦欢澶筡EnemyDamageResistance\...`），测试照常全绿但产物落错位置。修法：`[System.IO.File]::WriteAllText($p, $text, (New-Object System.Text.UTF8Encoding($true)))` 补回 BOM，并用 `[System.Management.Automation.Language.Parser]::ParseFile` 检查中文字面量解码是否正确；**改这类脚本后必须复查 BOM**。用 PowerShell 原地改写含中文的脚本也危险：`Get-Content -Raw` + `Set-Content` 在无 BOM 输入下会直接把乱码写回文件。
- 未做：步骤 4 的 Prefab 抗性数值与实例生命周期核对、步骤 5 的运行时实机检查；电/冰冻/光攻击与致盲按提案不属于本步。步骤 3 完成后未重跑整套（复用同一次 95/95 结果），只修了脚本编码与默认路径。


## 普通小怪攻击（挥刀）动画（2026-10-22）

- 攻击动作**没有**新建第二个动画组件，而是并入 `Assets/Scripts/TowerDefense/MonsterWalkAnimator.cs` 的同一趟骨骼写入：`ApplyPose` 末尾调用 `ApplyAttackPose` 把挥刀姿态叠加在走路/待机姿态之上。这样同一批骨骼（Spine / Chest / Hips / Head / UpperArm.R / LowerArm.R / Hand.R）只有一个写入者，避免两个组件抢骨骼 + LateUpdate 顺序 + 撤销上一帧 delta 这类问题。以后加动作仍走这个文件。
- 触发链路：`GroundEnemyCombat` 一般在出怪时添加、晚于 `Awake`，所以 `TickAttack` 每帧用 `GetComponent` / `GetComponentInParent` 兜底绑定，并订阅 `Attacked`。三种触发：① `IsAttacking` 上升沿；② 近战期间一段动作播完继续连砍（`loopWhileAttacking`）；③ `Attacked` 真正结算伤害时若已接近收招就重播，保证每一刀都有动作对应。外部可用 `TriggerAttack()` / `CancelAttack()`，`IsSwinging` / `AttackProgress` 只读。
- 时序用比例而不是写死帧：`attackDuration = 0.72s`，`attackWindupFraction = 0.30`（举刀完成）、`attackContactFraction = 0.44`（刀锋接触）、`attackRecoverFraction = 0.70`（收招完成）。`Keyed(t, time, value, ...)` 是唯一的曲线求值入口（分段 smoothstep），调节奏只改这些比例和关键帧，不要在别处再写一套插值。
- 姿态是「右臂举刀 → 斜劈向身体左前方 → 收刀回体侧」：起手绕角色右轴向后上抬（`attackArmSwingDegrees`）+ 向外张开（`attackOutwardDegrees`）+ 收肘（`attackElbowDegrees`）+ 躯干向右拧身蓄力；接触帧前挥、送胯、前倾（`attackLeanDegrees`）、甩腕（`attackWristDegrees`）。**符号约定**：绕角色 `right` 轴正角 = 手臂向后、负角 = 向前；绕 `up` 轴正角 = 向右转（右手持刀起手向右蓄力、下劈向左送力）。左臂与腿完全不参与，单手持刀。
- 挥刀进度 `1.0` 时所有曲线回到 0，即精确回到导入姿态，不会残留上一帧；无挥刀时 `ApplyPose(phase, blend)` 传 `-1` 哨兵，编辑器预览/测试可用 `ApplyPose(phase, blend, progress)` 采样任意关键帧。
- 预览仍是 `MaskedSoldierAuthoring.RenderPreview`（需 `-WithGraphics`），新增 `attack_pose_windup/contact/recover/contact_three_quarter.png`，输出到 `C:/Users/ASUS/Desktop/陷阱素材文件夹/MaskedSoldier/previews/`；正面机位看挥刀平面、斜前方机位确认刀臂不穿进胸腔（已目视确认）。调笔参数后必须重跑预览看图，别只靠数值判断。
- 回归测试：新增 `Assets/Editor/MonsterAttackPoseTests.cs`（7 项：下劈帧右臂大幅离开静止、举刀与下劈方向相反且夹角 > 60°、左臂与腿不动、进度 1.0 精确回静止、`-1` 不叠加、`TriggerAttack` / `CancelAttack`、`RestoreRestPose` 同时清挥刀）。跑法 `-runTests -testPlatform EditMode -testFilter "MonsterWalkAnimatorTests|MonsterAttackPoseTests"`，2026-10-22 实测 **13/13 通过、0 失败、0 跳过**。
- 未做真实 Play Mode 近战验收（出刀时机与伤害同步只按 `GroundEnemyCombat.Attacked` 逻辑推演）；没有受击/死亡/多段连招动画；所有角度与时序都是待调默认值。预制体未重新生成：`MonsterWalkAnimator` 新增字段在旧预制体上取脚本默认值即可生效。

## 哨戒机枪阶段 3：Unity 导入配置与 URP 材质（2026-10-23）

- 依据桌面设计稿 `C:\Users\ASUS\Desktop\陷阱素材文件夹\哨戒机枪设计.txt` 的 Step 3。唯一生成器是 `Assets/Editor/SentryMachineGunAuthoring.cs`：**不要手改 FBX 或 `.mat`**，配色只改脚本里的 switch 再重跑。批处理入口 `SentryMachineGunAuthoring.ConfigureImporterBatch`（配置导入 + 生成材质 + 探针），单跑探针 `SentryMachineGunAuthoring.ProbeModelBatch`；菜单 `Tools/塔防/哨戒机枪/配置导入并生成 URP 材质` 与 `Tools/塔防/哨戒机枪/探针模型`。
- 定稿模型：`Assets/Models/SentryMachineGun/SentryMachineGun.fbx`（69 网格 / 4132 三角面 / 75 节点）与 `SentryShellCasing.fbx`（3 网格 / 132 三角面）；Blender 源与预览图在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\`。
- 导入契约：`Generic`、`importAnimation=false`、`addCollider=false`、`meshCompression=Off`、`isReadable=true`、`globalScale=1`、`preserveHierarchy=true`、`weldVertices`/`optimizeMesh*` 全关（保证三角面数与顶点号确定，阶段 6 的预算断言依赖这点）、`materialImportMode=ImportStandard` + `AddRemap` 把 7 个 FBX 槽指向外部 URP 材质；若 remap 未绑定会自动退回 `ImportViaMaterialDescription` 重试一次再报错。
- 材质库 `Assets/Models/SentryMachineGun/Materials/`：`M_Sentry_DarkMetal / M_Sentry_Armor / M_Sentry_Accent / M_Sentry_Rubber / M_Sentry_LaserLens`（红自发光）/ `M_Sentry_Brass` / `M_Sentry_MuzzleFlash`（橙自发光）/ `M_Sentry_LaserBeam`（URP Unlit 加法混合，运行时激光线用）。材质比对走逐属性 `SameMaterial`，重复跑不会脏化 `.mat`（已实测二次运行零改动）。
- 探针 `ProbeModel` 写 `%TEMP%\alpha-striker-unity\sentry-machine-gun\probe-model.json`：19 项全绿——高 0.92 m、水平 1.261 × 1.710 m、脚底 y = 0、无 Collider、无空材质槽、全部 URP 着色器（无 Standard/品红）、8 个材质齐全、4 个 Marker 空节点齐备。
- **坐标轴结论（阶段 4 必须处理）**：Blender 沿 +Y 建枪，Unity 的 Y-up 转换把它变成 **-Z**。实测 `Marker_Pivot` (0, 0.62, 0)、`Marker_Muzzle` (0, 0.76, **-0.92**)、`Marker_Ejection` (**-0.13**, 0.78, 0.02)、`Marker_Laser_Emitter` (0, 0.895, -0.10)。所以阶段 4 要在 `Gun Recoil` 下给烘焙好的美术加 **180° 绕 Y 的 yaw**：之后 Muzzle 落到 +Z、抛壳口落到 **+X**（正是契约要求的右侧），**不需要重做 Blender**。Marker 派生的 Muzzle / Ejection Port / Laser Emitter 空节点必须放进同一个 yaw 容器，否则偏移会反向。
- 阶段 4 已完成（预制体、层级与组件接线），见下方「哨戒机枪阶段 4」小节；阶段 5-7（运行时验收、预览图、EditMode/PlayMode 测试）仍未做。
- 材质目视入口：`SentryMachineGunAuthoring.RenderMaterialPreview`（菜单 `Tools/塔防/哨戒机枪/预览导入材质`，**必须带 `-WithGraphics`**），输出到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\previews\unity-step3\`（三分之四 / 侧 / 正 / 俯四张，已目视确认：深枪灰机匣、橙色支腿、黄铜弹链、顶部红色激光镜）。截图前必须预热两帧，否则**第一张会渲染成平铺橙色**（批处理里 `camera.Render()` 首帧早于 URP 管线就绪），代码里已加两次丢弃式预热；以后写任何 `-WithGraphics` 截图工具都要照抄这个预热，别把首帧当真实结果。

## 哨戒机枪阶段 4：预制体、层级与组件接线（2026-10-23）

- 依据桌面设计稿 `C:\Users\ASUS\Desktop\陷阱素材文件夹\哨戒机枪设计.txt` 的 Step 4。生成器仍只有一个：`Assets/Editor/SentryMachineGunAuthoring.cs`，新增 `CreateAssetsBatch`（配置导入 → 备份 → 删旧资源 → 生成两个预制体 → 探针）与 `ProbePrefabBatch`；菜单 `Tools/塔防/哨戒机枪/生成阶段 4 预制体` 与 `Tools/塔防/哨戒机枪/探针预制体`。**两个预制体与材质一样由生成器拥有，不要手改。**
- 产物：`Assets/Resources/SentryMachineGun.prefab`（art 根）与 `Assets/Prefabs/SentryShellCasing.prefab`。旧 `Assets/Resources/SentryMachineGun.fbx|.blend`、`Assets/Models/SentryMachineGun.fbx|.blend`、`Assets/Models/create_sentry_machine_gun.py` 已在生成前删除（`deleted=5`，含 `.meta`），因此 `Resources.Load("SentryMachineGun")` 现在唯一命中新 prefab。生成前的原件与旧资源副本在 `C:/Users/ASUS/Desktop/陷阱素材文件夹/SentryMachineGun/stage4-unity/backups/<时间戳>/`。
- 层级契约（代码按名字查找，不能改名）：`SentryMachineGun → Base → Leg_FL|FR|RL|RR`（每条腿 4 个零件：Leg / Foot / Foot_Sole / Brace）+ 底座杂件；`Aim Pivot (0, 0.62, 0) → Gun Recoil → Muzzle / Ejection Port / Laser Emitter / Muzzle Flash / Art`。`Gun Recoil` 静止时零偏移，只给后坐动画用，yaw 只写在 `Aim Pivot` 上。
- **轴向补偿（关键）**：FBX 枪口朝 -Z。`Art` 容器带 180° yaw，枪身网格（`Gun_*` / `Laser_*`）放在 `Art` 下、局部坐标 = 烘焙坐标 − 支点，于是 `Aim Pivot` 静止 yaw=0 时枪口朝 **+Z**。实测 pivot 局部：`Muzzle (0, 0.14, 0.92)`、`Ejection Port (0.13, 0.16, -0.02)`（右侧 +X）、`Laser Emitter (0, 0.275, 0.10)`；这些值与设计稿的目标值（0,0.08,0.95 / 0.13,0.10,0.02）不同，以模型实测为准。Marker 空节点不保留，只用于推导这三个点。
- `Muzzle` / `Ejection Port` / `Laser Emitter` 是无网格空节点；`Muzzle Flash` 是默认关闭的容器（`FX_Muzzle_Flash` + `FX_Muzzle_Flash_Collar`），由 `SentryFireFeedback` 开关。
- 组件接线（预制体根，SerializedObject 写入，重跑生成器会覆盖手工改线）：`SentryFireFeedback`（recoilRoot / muzzle / muzzleFlash）、`SentryShellEjector`（ejectionPort / casingPrefab）、`SentryLaserSight`（laserEmitter / beamMaterial = `M_Sentry_LaserBeam`）。
- 运行时脚本本轮一并落地（属于阶段 5 的代码；行为验收已于同日完成，见下方「哨戒机枪阶段 5」小节）：`Assets/Scripts/TowerDefense/SentryFireFeedback.cs`（纯函数 `EvaluateRecoil01(progress)`、`Tick(dt)`、`PlayShot`、结束精确回零）、`SentryShellEjector.cs`（32 个对象池、`SamplePosition` / `SampleRotation` 纯函数、`ActiveCasingCount`、弹壳父级挂在 art 根不随 yaw）、`SentryLaserSight.cs`（运行时建 2 点 LineRenderer、Raycast 截断并忽略 Trigger、炮塔未启用时隐藏；**编辑期不建线**，否则生成预制体时会多出 `Laser Beam` 子物体）。`AutoSentryTurret` 只加了 `ShotFired` 事件和 `AimPivot` / `Muzzle` 只读属性，瞄准/伤害/冷却逻辑未动。
- 验证：真实 Unity `6000.6.6f1` 批处理 `SentryMachineGunAuthoring.CreateAssetsBatch`，日志 `SENTRY_MG_STEP4_PREFABS_PASS` + `SENTRY_MG_STEP4_PROBE_PASS | checks=37 failed=0`。探针覆盖：`Resources.Load` 唯一命中、9 个契约节点各 1 个、四腿各 4 零件、包围盒 1.261 × 0.92 × 1.71 m（高 ≤ 1.70、脚底 y = 0）、4132 三角面、无 Collider/Rigidbody/Animator、材质全 URP 且都来自材质库、三处组件接线非空、枪口焰默认隐藏、弹壳预制体只有 Transform/MeshFilter/MeshRenderer。报告：`%TEMP%\alpha-striker-unity\sentry-machine-gun\probe-prefab.json` 与桌面 `stage4-unity/probe-prefab.json`；生成摘要 `stage4-unity/stage4-generation.json`。
- 未做：阶段 6 预览图与 `SentryMachineGunTests`、PlayMode 实机（阶段 5 的运行时行为验收已在同日补齐，见下节）。
- **环境坑（本轮实际踩到，先看这里）**：① 未跟踪文件 `Assets/Editor/DroneCameraControllerTests.cs` 继承 `InputTestFixture`，但工程没把 `com.unity.inputsystem` 列为 testable、默认 `Assembly-CSharp-Editor` 也不是测试程序集，该文件必然 `CS0246` 并阻断所有批处理。本轮验证时临时移出、结束后原样放回；**它仍需要自己的测试 asmdef（注意 asmdef 无法引用默认程序集，测试目标 `DroneCameraController` 在 `Assembly-CSharp` 里，所以更现实的是改写为不使用 `InputTestFixture`）**。② 验证过程中 `Assets/Scripts/FPS/GameStartMenu.cs` 被并发删除（git 状态 `D`），引发 39 处 `CS0103: GameStartMenu`；已 `git checkout --` 恢复。改仓库前先确认没有其他会话在同时改文件。

## 哨戒机枪阶段 5：运行时动画脚本与行为验收（2026-10-23）

- 依据 `C:\Users\ASUS\Desktop\陷阱素材文件夹\哨戒机枪设计.txt` 的 Step 5。阶段 4 已落地的三个脚本本轮**修好了两个真实缺陷**并通过了行为验收，不再只是「代码存在」。
- 修 1（弹壳抛物线，真 bug）：`SentryShellEjector.Step` 原来每帧用「当前位置 + 恒定初速」的闭式公式推进，速度里**从不累加重力**，实测弹壳只会直线上升（t=1.0 s 时 y=2.93 m，纯 v·t），1.6 s 寿命到了也落不了地。改为记录出膛 `SpawnPosition` / `SpawnRotation`，每帧用 `SamplePosition(spawn, velocity, gravity, Age)` 解析积分；修复后顶点 0.25 s / 1.023 m、0.68 s 落地回收，符合手工计算。
- 修 2（可测性/健壮性）：`AutoSentryTurret.Update` 的循环体提取为 `public void Tick(float deltaTime)`（`Update` 仍传 `Time.deltaTime`，行为不变），使「弹链节奏」能在批处理里用固定步长确定性采样；`SentryLaserSight.UpdateBeam` 增加惰性 `GetComponentInParent<AutoSentryTurret>()`，不再依赖 `OnEnable`（编辑期不执行 `OnEnable`，否则线永远不显示）。**瞄准、伤害、冷却、兜底几何逻辑一行未改。**
- 新增验收入口 `Assets/Editor/SentryMachineGunStage5Validation.cs`：菜单 `Tools/塔防/哨戒机枪/验收阶段 5 运行时动画`，批处理 `SentryMachineGunStage5Validation.Run`（`-nographics` 即可，不写场景/预制体/材质）。覆盖 5 组：预制体接线（9 个契约节点各 1、7 个序列化引用、后坐/弹壳/激光参数）、炮塔回归（3 s 内 22–26 发 ≈ 8 发/s、每发 12 伤害、`ShotFired` 每发一次、冷却 1 s 期间 0 发、冷却后恢复、目标换到右翼后 yaw 跟随、目标消失即停火）、后坐与枪口焰（曲线端点 0 / 峰值落在 0.1–0.4、峰值位移 −0.04 m 与 3°、焰显示 0.045 s 并还原缩放、结束后**精确**回零、由真实 `ShotFired` 驱动）、弹壳（池 32 上限、挂在 art 根且不在 `Aim Pivot` 下、出膛点在 Ejection Port、弧线先升后降、向 +X 抛出、自旋、落地/超时回收、回收后对象停用）、激光（运行时 2 点 `LineRenderer`、射线被 3 m 处方块截断、炮塔禁用即隐藏并归零长度、重新启用恢复）。
- 结果：真实 Unity `6000.6.6f1` 批处理 **`SENTRY_MG_STAGE5_PASS: 114 assertions`**（含修复后的出厂预制体分支），`0` 失败、无 `error CS`、无异常、日志有 `Exiting batchmode successfully now!`。日志与归档：桌面 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\stage5-unity\`。
- 出厂预制体 `Assets/Prefabs/AutoSentryTurret.prefab` 原本导入为 `BrokenPrefabAsset`，本轮已查明根因并修复，见下方「哨戒机枪出厂预制体修复」。
- 顺带修掉的编译阻断：`Assets/Editor/DroneCameraControllerTests.cs` 不再继承 `InputTestFixture`（改用 `InputSystem.AddDevice<Mouse>()` + `QueueDeltaStateEvent` + `InputSystem.Update()`，并手写 `[SetUp]`/`[TearDown]`），工程恢复可编译。**遗留**：该 fixture 仍未被 `-runTests -testFilter DroneCameraControllerTests` 发现（0 用例），而同目录 `TrapPurchaseTests` 正常发现 34 条，属该文件/无人机功能自己的问题，与哨戒机枪无关，后续处理无人机时再查。
- 未做：阶段 6（`SentryMachineGunPreview.RenderPreview` 预览图、`SentryMachineGunTests` NUnit 全套）、PlayMode 实机验收。

## 哨戒机枪阶段 6：预览与 EditMode 回归（2026-10-23）

- 阶段 6 入口：`Assets/Editor/SentryMachineGunPreview.cs`（菜单 `Tools/塔防/哨戒机枪/渲染阶段 6 预览`；批处理 `SentryMachineGunPreview.RenderPreviewBatch`，必须使用 `-WithGraphics`）与 `Assets/Editor/SentryMachineGunTests.cs`（`UNITY_INCLUDE_TESTS` 下的 EditMode fixture；PowerShell 入口 `Assets/Editor/RunSentryMachineGunTests.ps1`）。预览基于 `Assets/Resources/SentryMachineGun.prefab`，不改场景和生成资产。
- 预览输出桌面 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\previews\unity-step6\`，含 14 张：三分之四/侧/正/俯、yaw 90/180/270、后坐峰值/回程/归零、枪口焰、弹壳 0.08/0.25/0.42 s，以及 `preview-manifest.json`。批处理预热两帧以等待 URP；目视确认外观、四腿、转向、后坐、枪口焰和可见弹壳弧线正常。
- 修复预览后坐采样：每张图都会重启单发周期，必须用该帧的**绝对**采样时间（0.18/0.5/1.0 × `SingleShotDuration`），不能把增量时间传给重置后的动画；否则回程和归零帧标签不实。弹壳帧使用 0.08/0.25/0.42 s，每帧都实际推进 `SentryShellEjector.Step`。
- 验证：Unity `6000.6.6f1` 图形批处理日志 `C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\20261023-141644-sentrymachinegunpreview-renderpreviewbatch.log` 有 `SENTRY_MG_STEP6_PREVIEW_PASS: 14 frames` 和成功退出标记；`Assets/Editor/RunSentryMachineGunTests.ps1` 实测 `SentryMachineGunTests` **11/11 通过、0 失败、0 跳过**，XML 与日志在桌面 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\stage6-unity\20261023-141711-189\`。覆盖占格/定义不变、尺寸、层级与朝向、URP 材质/无物理体、弹壳纯视觉、后坐/枪口焰、ShotFired/弹壳池与抛物线回收、激光遮挡/禁用、旧炮塔锁定和伤害。
- 本轮未做 PlayMode 实机战斗验收；阶段 6 仅要求 EditMode + 预览。Unity 测试 runner 因 Unity 编辑器单实例要求为 `-runTests` 单独启动，不与图形预览并行。

## 哨戒机枪出厂预制体修复（2026-10-23）

- 现象：`Assets/Prefabs/AutoSentryTurret.prefab` 的导入结果是 `UnityEditor.BrokenPrefabAsset`，子资源只有一个 `ImportLog`，`AssetDatabase.LoadAssetAtPath<GameObject>` 返回 null；两份 `AutoSentryTurret.asset` 的 `prefab` 引用（GUID 命中、fileID = 100100000）因此运行时解析成 null。
- **根因**（`SentryTrapPrefabRepair.DiagnoseBatch` 读出 ImportLog）：`Prefab file is corrupt: it contains an object using fileID 100100000, which is reserved for the Prefab asset. This usually indicates a bad merge or a manually edited Prefab file.` —— 该 prefab 是早期手写 YAML，把根 GameObject 的 fileID 用成了 Unity 保留值 `100100000`；现代 Unity 直接判损坏并拒绝导入。**手工改 YAML 修不好，必须让 Unity 自己重写。**
- 修复入口：`Assets/Editor/SentryTrapPrefabRepair.cs`。菜单 `Tools/塔防/哨戒机枪/诊断出厂陷阱预制体`（`DiagnoseBatch`，只读，打印主资源类型与 ImportLog 全文）与 `Tools/塔防/哨戒机枪/修复出厂陷阱预制体`（`RepairBatch`）。
- 修复步骤（`RepairBatch`，8 条自检）：先把 `prefab` + `.meta` 备份到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\backups\trapprefab-<时间戳>\` → 新建裸根 `Auto Sentry Turret` + `TrapInstance` + `AutoSentryTurret` → `PrefabUtility.SaveAsPrefabAsset` 写回原路径（**路径不变所以 `.meta` 与 GUID 不变**）→ 根对象 fileID 变了，用 `SerializedObject` 把 `Assets/Resources/AutoSentryTurret.asset` 与 `Assets/TrapDefinitions/AutoSentryTurret.asset` 的 `prefab` 重指到新根 → `ImportAsset(ForceUpdate)` + 校验。
- **两个坑**：① `AutoSentryTurret` 带 `[ExecuteAlways]`，`AddComponent` 当场跑 `Awake` 并把美术实例化进根节点；直接存盘会烘焙一份美术、运行时再生成一份 → 每座炮塔两根枪。`RepairBatch` 存盘前删掉 `Sentry Machine Gun Art` 子物体并把 `aimPivot` / `muzzle` 序列化字段置空（回到「裸根 + Awake 现造」）。② 定义资产里的 `{fileID: 100100000, guid: ...}` 是旧 fileID，不重指仍然是 null。
- 结果：`SENTRY_TRAP_PREFAB_REPAIR_PASS: 8 checks`（主资源是 GameObject、可 `LoadAssetAtPath`、GUID 不变 `8c6db5b3e6c7470ca27c3b9d5a46f102`、根带 TrapInstance / AutoSentryTurret、根无子物体、两份定义都解析到同一 prefab）。修复后 `SentryMachineGunStage5Validation.Run` 走真实预制体分支：实例化后**恰好一份**美术、Aim Pivot / Muzzle 自带解析、0.3 s 打 3 发共 36 伤害，总断言 114 条全过。
- 回滚：`backups/trapprefab-<时间戳>/` 有原 `prefab` 与 `.meta`；或 `git checkout -- Assets/Prefabs/AutoSentryTurret.prefab Assets/Resources/AutoSentryTurret.asset Assets/TrapDefinitions/AutoSentryTurret.asset`。**该 prefab 现在由工具生成，不要再手改 YAML。**

## 新游戏菜单 Play Mode 验收（2026-10-23）

- 新增 `Assets/Editor/GameStartMenuPlayModeTests.cs` 与运行入口 `Assets/Editor/RunGameStartMenuPlayModeTests.ps1`。测试从 `THREE_ROUTE_MERGE_MAP` 进入真实 Play Mode，验证主菜单打开时暂停时间、光标所有权、UI EventSystem、玩家 `InputAction` 禁用；进入设置、修改主音量、返回以及开始游戏后恢复时间、光标、菜单可见性和玩家输入。
- Unity `6000.6.6f1` 实测 NUnit **1/1 通过、0 失败、0 跳过**，无 C# 编译错误、异常或脚本编译阻断。结果归档：`C:\Users\ASUS\Desktop\陷阱素材文件夹\GameStartMenu\playmode-unity\20261023-110855-288\`，包含 `editmode-results.xml`、`editmode.log` 与 `summary.json`。
- 测试前备份并在测试后核对 `THREE_ROUTE_MERGE_MAP.unity`、`ProjectSettings/ProjectSettings.asset`、`ProjectSettings/EditorBuildSettings.asset`、`Packages/manifest.json` 的 SHA256，四项均未改变。未发现阻断，因此没有修改运行时菜单逻辑。

## 哨戒机枪枪口开孔（2026-10-23）

- 需求：在枪口开一个**小孔**模拟真实枪口，**不做完整内构**（不打通枪管、不加膛线/导气结构）。只改模型几何，不动 Unity 脚本、预制体、场景、材质。
- 唯一实现：`C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\add_muzzle_bore.py` 的 `bore_muzzle(dark_material=None)`。它用两段同轴圆柱对 `Gun_Muzzle_Tip` / `Gun_Muzzle_Device` / `Gun_Barrel` 做 `BOOLEAN DIFFERENCE`（solver=EXACT）：
  - 内孔：半径 **15.5 mm**，从枪口端面（Blender Y=0.945）往里钻 **87 mm**，孔底在 Y=0.858 封实；
  - 膛口冠（扩口）：半径 **23 mm**、深 **27 mm**，做出真实枪口的收口台阶；
  - 新内壁面沿用模型自带 `Sentry_DarkMetal`，所以孔内是暗色，不需要新建材质。
- **坐标契约**：枪口轴线 Blender `X=0, Z=0.76`，枪管沿 **+Y**；Unity 侧仍是 -Z（阶段 4 的 180° yaw 补偿不变）。切刀沿 +Y 覆盖 `[tip-depth, tip+0.06]`——**别把中心公式写成 `tip + overshoot - depth/2`**，那样切刀只从端面往里覆盖 `overshoot` 那么长，实测只钻出 30 mm 且扩口几乎没切到（本轮踩过）。
- **不新增对象、不改对象名**（脚本内部断言对象集合前后一致），因此 `Assets/Resources/SentryMachineGun.prefab` 按名字绑定的网格不受影响，**不需要重跑 `SentryMachineGunAuthoring.CreateAssetsBatch`**。
- 两种调用方式：① 独立脚本 `add_muzzle_bore.py` 打开 `SentryMachineGun.blend` 开孔后另存 `SentryMachineGun_muzzle_bore.blend` 并导出 FBX（源 blend 只读）；② `build_sentry_machine_gun.py` 的 `main()` 在 `build_sentry()` 之后直接 `import add_muzzle_bore; add_muzzle_bore.bore_muzzle(mats["dark"])`，**重建模型自动带孔**。两条路共用同一份代码，别各写一套参数。
- **幂等保护**：`bore_muzzle` 先用只打枪口对象的轴线射线测孔深，>20 mm 就判定「已开孔」直接跳过，避免重复运行把孔越钻越深。
- **导出坑**：独立脚本导出时必须排除预览场景对象（`Preview_Floor` 等），否则 FBX 会多出一个 `Preview_Floor` 网格（本轮踩过，70 vs 69 个网格）。用 `EXPORT_EXCLUDE_PREFIXES = ("Preview_", "BORE_CUT_", "Bore_", "Wide_")`，导出设置与建模脚本一致（`bake_space_transform=False`、`mesh_smooth_type="FACE"`）。
- 几何结果：开孔前 4132 三角面，开孔后 **4648**（+516，只加在三个枪口件上：`Gun_Muzzle_Tip` 60→320、`Gun_Muzzle_Device` 60→188、`Gun_Barrel` 60→188），切刀 32 段。包围盒不变（1.261 × 1.710 × 0.92 m），枪口端面仍在 Y=0.945 / Unity 的 `Muzzle` 空节点位置不变。
- 验证：① Blender 回读 FBX（`check_muzzle_bore_fbx.py`）——对象名/空节点集合与原 FBX 完全一致、原模型轴向射线打在端面（Y=0.945）、新模型打在孔底（Y=0.858，深 87 mm），5 项断言 `FBX_CHECK_RESULT PASS`；② 真实 Unity `6000.6.6f1` 跑 `Assets/Editor/RunSentryMachineGunTests.ps1` → `SentryMachineGunTests` **11/11 通过、0 失败、0 跳过**（结果在桌面 `SentryMachineGun\muzzle-bore-tests\20261023-143653-688\`），说明 FBX 重导入后预制体、材质、层级、朝向都没坏。
- 预览（已目视确认孔洞与扩口台阶）：`C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\previews\muzzle-bore\` 下 `muzzle_bore_01_muzzle_front.png`（正对枪口）、`02_muzzle_three_quarter.png`（斜前近景）、`03_side_overview.png`、`04_three_quarter_wide.png`、`05_side_wide.png`。**渲染前必须隐藏 `FX_Muzzle_Flash*`**，否则枪口焰锥（半径 0.21，伸到 Y=1.08）会把整个镜头糊住，什么也看不见。
- 备份与回滚：改前原件在 `SentryMachineGun\backups\muzzle-bore-20261023-142506\`（原 blend / 原 FBX / `.meta` / 建模脚本）；可用产物快照在 `backups\muzzle-bore-good-20261023-143340\`；建模脚本自动备份在 `backups\20261023-143452\`。回滚 = 把备份的 `SentryMachineGun.fbx` 拷回 `Assets/Models/SentryMachineGun/`（`.meta` 未改动，GUID 不变，无需重导预制体）。
- 未做：PlayMode 实机看孔（枪口焰、后坐动画下的观感）、LOD、孔内膛线/导气等内构；也没有改任何 C#、预制体、场景或材质。

## 哨戒机枪模型与开火/抛壳动画（2026-10-23，阶段 7 收尾总索引）

- 这一节是「哨戒机枪」能力的**唯一入口**：以后改模型、动画或排障先看这里，再按需跳到上方「哨戒机枪阶段 3/4/5/6」「哨戒机枪出厂预制体修复」「哨戒机枪枪口开孔」小节。设计文档仍是 `C:\Users\ASUS\Desktop\陷阱素材文件夹\哨戒机枪设计.txt`（阶段 7 的收尾要求即来自其 Step 7）。
- 素材目录（全部中间态都在这，不进仓库）：`C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\`。仓库内只有定稿：模型、材质、两个生成预制体和脚本。
- **数据流（结构未改，仍是 2×2 定义）**：`TrapDefinition`（`Assets/Resources/AutoSentryTurret.asset` + `Assets/TrapDefinitions/AutoSentryTurret.asset`）→ `Assets/Prefabs/AutoSentryTurret.prefab`（裸根 + `TrapInstance` + `AutoSentryTurret`）→ `AutoSentryTurret.Awake` 中 `Resources.Load("SentryMachineGun")` → `Assets/Resources/SentryMachineGun.prefab`（art 根）。价格、占格、安装类别、购买/经济/伤害结算链一行未改。
- **资源所有权**：模型 `Assets/Models/SentryMachineGun/SentryMachineGun.fbx`（69 网格 / 4,648 三角面，含枪口开孔）与 `SentryShellCasing.fbx`（3 网格 / 132 三角面）；材质 `Assets/Models/SentryMachineGun/Materials/M_Sentry_*.mat` 共 8 个；两个视觉预制体由 `SentryMachineGunAuthoring.CreateAssetsBatch` 生成；出厂陷阱预制体由 `SentryTrapPrefabRepair.RepairBatch` 生成。**这四类资产都归生成器所有，不要手改**：改配色只动脚本里的配色表/材质定义再重跑。
- **层级契约（代码按名字查找，改名即断线）**：`SentryMachineGun → Base → Leg_FL|FR|RL|RR`（每条腿 Leg / Foot / Foot_Sole / Brace 四件）+ 底座杂件；`Aim Pivot (0, 0.62, 0) → Gun Recoil → Art / Muzzle / Ejection Port / Laser Emitter / Muzzle Flash`。`Aim Pivot` **只承载 yaw**（`AutoSentryTurret` 写它），`Gun Recoil` 静止零偏移**只给后坐动画**，`Art` 带 180° yaw 把 FBX 的 -Z 枪口翻成 +Z。实测 pivot 局部：`Muzzle (0, 0.14, 0.92)`、`Ejection Port (0.13, 0.16, -0.02)`、`Laser Emitter (0, 0.275, 0.10)`。
- **组件分工**（预制体根挂三个表现组件，炮塔只对外发一个事件）：
  - `AutoSentryTurret`：只新增 `event ShotFired(Vector3 枪口位置)`、只读 `AimPivot` / `Muzzle`、可测入口 `Tick(float)`；瞄准、伤害、冷却、兜底几何逻辑未动。
  - `SentryFireFeedback`：订阅 `ShotFired` 驱动后坐（沿 -Z 0.04 m、抬枪 3°，kickIn 0.18、单发 0.11 s）与枪口焰（0.045 s）；`EvaluateRecoil01(progress)` 纯函数、`Tick(dt)` 手动推进、结束精确回零。
  - `SentryShellEjector`：32 个池化弹壳，父级挂 art 根**不随 yaw**；`SamplePosition` / `SampleRotation` 解析积分（含 -9.81 重力），初速局部 (+1.5, +2.2, -0.4)、自旋 900°/s、寿命 1.6 s、落地或超时回收；`ActiveCasingCount` 可断言。
  - `SentryLaserSight`：运行时建两点 `LineRenderer`（材质 `M_Sentry_LaserBeam`），沿枪管射线截断（忽略 Trigger），炮塔未启用即隐藏；**编辑期不建线**，否则生成预制体会多出 `Laser Beam` 子物体。
- **验证命令（工程根执行，Unity 版本从 `ProjectSettings/ProjectVersion.txt` 动态匹配）**：
  - 行为总验收：`SentryMachineGunStage5Validation.Run`（`-nographics` 足够）→ 期望 `SENTRY_MG_STAGE5_PASS: 114 assertions`，覆盖接线、炮塔回归、后坐/枪口焰、弹壳、激光五组。
  - 预制体探针：`SentryMachineGunAuthoring.ProbePrefabBatch` → `SENTRY_MG_STEP4_PROBE_PASS | checks=37 failed=0`。
  - EditMode 回归：`Assets/Editor/RunSentryMachineGunTests.ps1` → `SentryMachineGunTests` 11 条，判定以 XML 的失败/跳过数为准（0/0），不看进程退出码。
  - 预览：`SentryMachineGunPreview.RenderPreviewBatch`（**必须 `-WithGraphics`**，且必须预热两帧）→ 桌面 `previews/unity-step6/` 的 14 张 + `preview-manifest.json`。
  - 出厂预制体排障：`SentryTrapPrefabRepair.DiagnoseBatch`（只读，打印 ImportLog）。
- **阶段 7 收尾复跑（2026-10-23）**：`Assets/Editor/RunSentryMachineGunTests.ps1 -OutputDirectory <桌面>\SentryMachineGun\stage7-unity` 实测 `SentryMachineGunTests` **11/11 通过、0 失败、0 跳过**，日志 0 个 `error CS`、第 3358 行 `Test run completed. Exiting with code 0 (Ok).`；结果目录 `C:\Users\ASUS\Desktop\陷阱素材文件夹\SentryMachineGun\stage7-unity\20261023-144500-739\`。日志里大量 `Exception` 命中是 `Debug.Log` 堆栈中的 `UnityLogCheckDelegatingCommand:CaptureException` 帧（伤害日志正常输出），不是测试异常；`failed` 命中只有许可证与 `abort_threads` 退出噪声，**排障时别被关键字计数误导，直接看 XML 的 failed/skipped**。收尾报告：同目录 `STAGE7_CLOSEOUT.md`。
- **边界（本轮明确未做，别当成已完成）**：
  - 没有 PlayMode 实机战斗验收（真机下开火节奏、枪口焰与弹壳观感、激光在复杂场景里的截断都只做过批处理断言与静态预览）。
  - 没有 AnimationClip / Animator 资产，全部是程序化动画；没有 LOD、UV 图集、贴图绘制（纯材质色块）；没有受击/被摧毁表现；没有俯仰（运行时只有 yaw）。
  - 测试只覆盖 EditMode + 预览；`AutoSentryTurret` 的既有行为回归靠 `SentryMachineGunStage5Validation` 与 11 条 EditMode 用例，不替代实机。
- **回滚**：模型回滚把 `SentryMachineGun\backups\muzzle-bore-20261023-142506\SentryMachineGun.fbx` 拷回（`.meta` 未动，GUID 不变）；预制体/材质回滚走 `git checkout -- Assets/Resources/SentryMachineGun.prefab Assets/Prefabs/SentryShellCasing.prefab Assets/Models/SentryMachineGun`；出厂陷阱预制体回滚见「哨戒机枪出厂预制体修复」小节。当前这些文件在本工作区仍是未提交状态（`Assets/Models/SentryMachineGun/SentryMachineGun.fbx`、`Assets/Models/SentryMachineGun/Materials/M_Sentry_LaserBeam.mat` 为 `M`，`Assets/Editor/SentryMachineGunPreview.cs` / `SentryMachineGunTests.cs` / `RunSentryMachineGunTests.ps1` 为未跟踪），提交前先确认没有其他会话在改同一批文件。
