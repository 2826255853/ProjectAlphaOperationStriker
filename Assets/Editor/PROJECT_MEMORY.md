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

- `FPS/FirstPersonPlayerBootstrap.cs` 在场景加载后复用现有 KINEMATION `FPSPlayer` 或旧 `FirstPersonController`；都没有才加载 `Resources/FPSPlayer`。负责补生命、观察、攀爬、移动、射击与 UI 桥接，并停用作为出生参考的场景摄像机。
- `FPS/FirstPersonController.cs` **同一文件有多个实现**，包括 `FirstPersonController`、`FPSPackagePlayerMotion`、`FPSHitscanShooter`；改移动/鼠标/射击前查所有相关类。KINEMATION 集成是明确依赖，不能把它当成纯旧控制器工程。
- 光标所有权既受 `TrapSelectionMenu.CursorOwned`，也受 `GameStartMenu.CursorOwned` / `BlocksPlayerCursor` 控制。两份 `UpdateCursor` 都必须尊重菜单，其他恢复光标的路径也要一致。
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
| 机枪炮塔 | `AutoSentryTurret` | 子弹伤害分类为 `Bullet`；定义由现有资源加载链提供 |
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
- `EnemyHealth.TakeDamage(amount, TrapDamageType)` 发布分类伤害事件；枚举已有 `Fire`、`Explosive`、`Energy`、`Bullet`、`Melee`，`Physical` 保留兼容。分类接口不等于已经实现抗性系统。
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
| 燃烧与伤害类别 | `FireStatusEffectTests`、`TrapDamageClassificationTests` |
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
