# ProjectAlphaOperationStriker 项目记忆

## 防空导弹发射架 · 配色修复（2026-10-30）

- 症状：游戏内发射架「没有颜色」。根因是 `Assets/Models/Trap_Base_Disc_Dual_Launcher_Loaded.fbx` 的 11 个材质全部是 **FBX 内嵌子资产**（`guid: a1b2c3d4e5f6478899aabbccddeeff01`，type 3），材质名有语义（`Trap Base Dark Metal` / `Launcher Body Metal` / `Launcher Rail` / `Missile Body` / `Missile Nose` / `Missile Details` / `Missile Fins` / `Launcher Pylon Metal` / `Inner Groove` / `Warning Edge`）但**颜色全白**，所以整机渲染成一片白色高光（像素对比：旧渲染 top50 平均值 255,255,255）。预制体 `Assets/Prefabs/DualMissileLauncher.prefab` 直接引用了这些内嵌材质。
- 修复走生成器，不手改预制体：`MissileLauncherTurretAuthoring` 新增 `EnsureLauncherMaterials()` / `ApplyLauncherMaterials(root)` / `CreateLauncherPrefab` 里在 `FitLauncherToFootprint` 之后、挂尾焰之前调用。生成 `Assets/Models/DualMissileLauncher/Materials/M_DML_*.mat`（11 个 URP/Lit），按 **FBX 材质名 → 渲染器名 → 兜底装甲色** 三级解析（`ResolveLauncherStyle` / `MatchStyleToken` / `IsGenericMaterialName`），因此模型改名或换模型时只需改 `LauncherPartStyles` 表。
- 实际绑定：`DarkMetal`x3（机座盘/踏板/内圈）、`InnerGroove`x1、`WarningEdge`x9（外警示圈 + 8 个 `Radial_Lock`）、`PylonMetal`x3、`LauncherBody`x1、`Rail`x4（导轨/限位）、`MissileBody`x2、`MissileNose`x2、`MissileDetails`x2（弹尾环带，橙色标识）、`MissileFins`x8；备料 `ArmorLight` 供未匹配部件回退。共 35 个机体渲染器，8 个尾焰渲染器仍走既有 `Exhaust_*` 加色材质。
- **金属度全部压到 ≤0.55**：地图多为室内/半室内且没有反射探针，高金属度会让部件发黑；`WarningEdge` / `MissileDetails` 用橙色（#E87A12 / #E67517）、`MissileNose` 用红棕（#BD381F）、弹体近白（#EDEBE0），保证颜色可辨。
- 菜单图标同源：`TrapSelectionMenuAuthoring` 的 `dual_missile_launcher_loaded` 图标渲染自该预制体，新增批处理入口 `RegenerateDualMissileLauncherIconBatch()`（`GenerateIcons(false, "dual_missile_launcher_loaded")`）刷新 `Assets/Models/TrapMenu/Icons/dual_missile_launcher_loaded.png`。**坑**：图标/预览类渲染必须带 `-WithGraphics`，无图形批处理会写出整张 192 灰的空白图。
- 验证：`MissileLauncherTurretAuthoring.ProbeLauncherMaterials`（只读探针，打印 35 个渲染器的 FBX 材质名与解析结果）+ 预制体 YAML 静态核对（35/35 槽位绑定 `M_DML_*`、全部 `Universal Render Pipeline/Lit`，材质来自 `.mat` 而非 FBX 内嵌）；渲染对比 `MissileLauncherTurretPreview.RenderReloadStatePreview`。场景内没有预制体实例（运行时由 `TrapDefinition` 放置），无材质覆盖需要清理。
- 预览亮度注意：`RenderReloadStatePreview` / 图标渲染把线性渲染值直接写进 PNG，**画面比游戏内暗/过曝**，别据此判断配色；配色以 `.mat` 数值 + 游戏内观感为准。配色板图 `launcher-palette.png` 由 `.mat` 数据生成。
- 产物目录（含改动前预制体备份、探针/渲染日志、配色板、对比图）：`C:\Users\ASUS\Desktop\陷阱素材文件夹\DualMissileLauncher\material-fix-20261030\`。
- 未做：主场景 PlayMode 实机观察发射架在真实光照下的观感，也未做完整 Player 构建。

## 防空导弹发射架 · 逐管装填表现（2026-10-30）

- 需求：某一位置（导轨/发射管）的导弹发射后，**该位置变为空管**，直到装填完毕才重新出现导弹。
- 新增运行时 `Assets/Scripts/TowerDefense/MissileLauncherReloadVisual.cs`：按管维护 `tubeLoaded[]`，空管时只把该管导弹的 **Renderer 关掉**（`Renderer.enabled = false`），**不** `SetActive(false)`——`Missile_01/02` 同时是 `MissileLauncherWeapon` 的发射口与尾焰喷嘴基准，节点一停就丢掉位置。
- `MissileLauncherWeapon` 接线：`Awake` 里 `ResolveReloadVisual()`（找不到组件就 `AddComponent`，所以旧预制体/场景实例也能生效），把 `muzzleA/muzzleB` 通过 `SetTubes` 交给装填组件；发射时 `NextTubeIndex()` 决定这一发来自哪根管子并立刻 `ClearTube(index)`；`Advance` 顶部在 `reloadRemaining` 归零的那一帧调用 `FinishReload()` 恢复两管。目标中途消失也照常走完装填（否则会永久空管）。
- 管序固定：`Missile_01` = 0（先发），`Missile_02` = 1；`NextTubeIndex` 仍是齐射轮换（`missilesLaunchedInSalvo % 2`），改轮换规则只改这一个函数。
- 生成器 `MissileLauncherTurretAuthoring.WireWeapon` 现在会把组件挂进预制体并写 `reloadVisual` / `tubeVisuals`；`Assets/Prefabs/DualMissileLauncher.prefab` 已重新生成（阵位调参未变：`reloadTime 4`、`projectileSpeed 100`、`maxFlightRange 50`、`flightRangeFollowsDetection 0`）。改模型或重新生成后请复查这两个引用。
- 回归：`Assets/Editor/MissileLauncherWeaponTests.cs` 新增 3 例——`FiredTubeStaysEmptyUntilTheReloadFinishes`（逐管清空 + 装填中仍为空 + 目标消失也恢复）、`EmptyTubeHidesOnlyThatRailsMissileMesh`（只关网格、节点与发射点位置保留）、`AuthoredPrefabWiresOneReloadVisualPerTube`（预制体两个位置绑定 `Missile_01/02`）。`RunEnemyDamageResistanceTests.ps1 -Fixtures MissileLauncherWeaponTests,MissileLauncherTurretTests` **24/24 PASS**（Unity `6000.6.7f1`），XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\DualMissileLauncher\reload-visual-20261030\tests\20261030-213722-439\editmode-results.xml`。预览/回滚备份同目录（含改动前的 `DualMissileLauncher.prefab`）。
- 看图入口：`MissileLauncherTurretPreview.RenderReloadStatePreview`（菜单 `Tools/塔防/预览导弹装填状态`，批处理需 `-WithGraphics`），渲染「两管满装 / 左管空 / 两管空」三张 PNG 到 `%TEMP%\alpha-striker-unity\launcher-reload-preview`。
- 未做：主场景 PlayMode 人工观察整轮齐射→空管→装填回满的完整节奏。

## 防空导弹发射架射程调整（2026-10-30）

- 探测半径 `MissileLauncherTurret.detectionRange` 由 **12 m 改为 40 m**（源码默认值与 `Assets/Prefabs/DualMissileLauncher.prefab` 序列化值同步），判定仍是发射架根节点到 `EnemyInstance` 枢轴的 **3D 球面距离**，且仍然只打空中目标（`targetFilter: AirOnly`）。
- 弹体射程与探测解耦：`MissileLauncherWeapon.maxFlightRange` 由 18 m 改为 **50 m**，新增序列化开关 `flightRangeFollowsDetection`（默认 false）。`MissileLauncherWeapon.LaunchOneMissile` 在 `ConfigureTuning` 之后调用新增的 `MissileProjectile.SetFollowMountRange(bool)`，因此 `MissileProjectile.MaxFlightRange` 不再被发射架的 40 m 覆盖，自爆点按 50 m 飞行距离结算。
- 之所以需要这个开关：预制体 `projectilePrefab` 为空，运行时走 `CreateFallbackMissile` 兜底弹体，其 `useTurretRangeAsMaxFlightRange` 默认 true 会把弹体射程强制改回探测半径——只改 `maxFlightRange` 数字无效，这是本次踩到的点。
- 回归用例加在 `Assets/Editor/MissileLauncherWeaponTests.cs`：`AuthoredLauncherUsesFortyMetreDetectionAndFiftyMetreFlightRange`（预制体 40/50 + 开关为 false）与 `LaunchedMissileKeepsFiftyMetreFlightRangeWhenDetectionIsForty`（真实预制体发射链路断言 `MaxFlightRange == 50`）。入口 `Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures MissileLauncherWeaponTests,MissileLauncherTurretTests`。
- 弹速由 **60 m/s 提到 100 m/s**（`MissileLauncherWeapon.projectileSpeed` 默认值与预制体序列化值同步，`MissileProjectile.speed` 默认值一并对齐）；转向速率由 **180°/s 提到 360°/s**（`turnRateDegrees`，武器与弹体默认值及预制体同步）。100 m/s ÷ 360°/s 的转弯半径约 **15.9 m**，与 40 m 探测半径配合后横穿目标可被追上；再提弹速时必须同步检查该半径，否则会退化成直线弹。
- 装填时间由 **6 s 压到 4 s**（`MissileLauncherWeapon.reloadTime` 默认值与预制体序列化值同步）；齐射仍是 2 发、间隔 0.25 s，因此持续输出从约 0.33 发/秒提升到约 0.5 发/秒。升级系统仍然只改导弹直击/溅射伤害，不提供射程分支；`TrapUpgradeMenuSession` 未新增射程预览。

## 飓风无人机接入为飞行怪物（2026-10-30）

- 无人机从「只有动画的表现预制体」变成**可接入出怪口的飞行怪物**。新增运行时 `Assets/Scripts/TowerDefense/HurricaneDroneEnemy.cs`（`[RequireComponent]` MonsterPathFollower + EnemyHealth + HurricaneDroneUnit）：按实际位移/偏航切 `FlyForward`/`BankTurn`/`Hover`（攻击状态不抢，交给 `HurricaneDroneUnit` 的 `attackClipSeconds` 收尾），索敌后悬停播 `HD_Attack`，`OnGunFire` 动画事件（6 发）逐发结算：陷阱走 `TrapInstance.TakeDamage`，玩家走 `PlayerHealth.TakeDamage`，不生成弹丸、不套 `MissileAimSolver`。
- 选靶：`attackRange` 45 m 内陷阱，默认**优先打能对空的陷阱**（`MissileLauncherTurret`），其次最近陷阱/玩家（`allowAttackPlayer` 可关）。射界只看水平 `aimConeDegrees = 40°`，并用 `Physics.RaycastAll` 跳过自身与目标做视线检测。
- 位移仍是**唯一一条**飞行分支 `MonsterPathFollower.InitializeFlying`，不新增寻路；悬停开火通过临时把 `follower.MoveSpeed` 置 0 实现（`CombatMovementPaused` 只在 `followingCombatPath` 时生效，对飞行无效，别再踩这个坑）。
- 预制体由 `HurricaneDroneAuthoring.CreateAll` 生成：表现件 `Assets/Prefabs/HurricaneDrone.prefab`，飞行怪物 `Assets/Resources/HurricaneDroneEnemy.prefab`（`EnemyHealth` 260 血、`MonsterPathFollower` flying + 速度 3.4 + 飞行高度 10）。**不加 `GroundEnemyCombat`**（飞行怪必须在近战追击中缺席，`ProbeEnemyPrefabBatch` 会检查）。构建逻辑抽成 `CreateDroneInstance`，两个预制体共用同一套 Animator/Muzzle/Unit 装配。
- 新增 `Assets/Editor/HurricaneDroneSpawnAuthoring.cs`：幂等创建/刷新主场景空中出怪口 `Enemy Spawn Flying Drone`（父节点 `MapForgeWorld`，位置 `(-60,16,-40)`，`entranceAltitude 16`、`flyingSpawnSpread 4`、每波 3 只、间隔 2 s、首波延迟 12 s）。**坑**：`ApplyModifiedProperties` 会触发 `EnemySpawnPoint.OnValidate → EnsureWaveSpawnData`，用当时的 `enemiesPerWave`（默认 10）补默认逐波条目，所以逐波设置必须在基础字段之后用第二遍 `SerializedObject` 显式重写（`ApplyWaveSettings`）。
- 验证（Unity `6000.6.7f1`）：`dotnet build Assembly-CSharp-Editor.csproj` 0 错误（仅既有 warning）；`CreateAll` 成功（材质 8、动画 4、两个预制体）；`ProbeEnemyPrefabBatch` 12 项 OK → `HURRICANE_DRONE_ENEMY_PASS`；`CreateSpawnPointBatch` → `HURRICANE_DRONE_SPAWN_PASS`（16 项）；场景 diff 95 行新增、0 删除。场景/预制体改动前备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\enemy-prefab-20261030\`。
- 校验加强（2026-10-30）：`HurricaneDroneSpawnAuthoring.ValidateSpawnPoint` 现在对**每一波**都断言（启用、预制体=无人机、数量=3、类型=Flying、飞行高度=10、`GetEnemiesPerWave`=3），并断言逐波条目数 = `TotalWaves`，不再只看第 1 波。复跑 `ValidateBatch` **31/31 OK → `HURRICANE_DRONE_SPAWN_PASS`**（日志 `%TEMP%\alpha-striker-unity\drone-spawn2.log`）；同批 `ProbeEnemyPrefabBatch` → `HURRICANE_DRONE_ENEMY_PASS`。当前主场景 `WaveManager.totalWaves = 3`、出怪口 `enemiesPerWave = 3`、`initialWaveDelay = 12`、`waveTransitionDelays = [15, 30]`、`spawnInterval = 2`，即 3 波 × 每波 3 架 = 9 架。
- 未做：主场景 PlayMode 实机出怪/飞行/机炮命中/被击落验收，开火特效、弹壳、音效、坠毁动画、对象池，以及完整 Player 构建。当前主场景里**没有**旧的 `Enemy Spawn Flying`，空中出怪口只有这一个。
- 注意：本机 Unity 图形编辑器可能正开着（占用 `Library/`），但 `run-unity-method.ps1` 的批处理仍能正常跑完；判定仍以日志 `Exiting batchmode successfully now!` 为准。

## 陷阱升级系统 · 第 7 步（2026-10-29）

- `TrapSelectionMenuView.Upgrades.cs` 增量扩展已有 Canvas，增加选择/升级页签、模态确认和普通四级方向步骤；`TrapSelectionMenuCardView` 添加独立圆框箭头回调。选择页保留原槽位分配并关闭菜单；升级页复用分类/分页，卡片点击查看、箭头打开确认，满级入口禁用。三级确认进入普通方向选择，再次确认才付款；完整分支选项仍留待第 8 步。
- `TrapUpgradeMenuSession` 仅持有报价快照/选中定义/弹窗导航，确认参数为 TrapId、预期等级和报价版本，调用既有付费 service 重验不可变报价（含 RunId、配置与费用）。成功结果页拒绝重复确认；失败刷新报价并要求重新确认。取消不预约资金、不扣款；余额、升级和换局事件刷新界面，换局关闭旧弹窗。
- 七类属性预览从 `TrapDefinition.Prefab` 的实际基础字段读取，新增只读基础值 getter；最大血量使用预制体的 `BaseMaxHealth` 或与放置时一致的 `TrapInstance.DefaultBaseMaxHealth`。维修与伤害独立倍率、扼喉双伤害、贝蒂爆炸、火焰直伤/燃烧及导弹直伤/溅射均按报价倍率显示，不实例化战斗组件。
- 弹窗为现有 Canvas 的独立顶层遮罩；背景 CanvasGroup 禁止点击和导航，菜单操作也显式拒绝弹窗期间的分配/清空/筛选。Esc 返回一级；N 关闭菜单并清空弹窗。沿用 `TrapSelectionMenu.CursorOwned` 和松开鼠标后恢复武器机制，两份玩家 `UpdateCursor` 已核对，均位于 `FirstPersonController.cs`，无需改动。`EnsureEventSystem` 仅在运行模式调用 DontDestroyOnLoad，允许编辑期初始化菜单验证。
- 编辑器入口 `TrapUpgradeMenuAuthoring.AddMissingControlsAndPreview` 仅补缺失控件，不重建现有 Canvas/卡片/图标/字体；首次预制体备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step7\prefab-backup\`，七类确认预览在同级 `upgrade-<TrapId>.png`。工具可重复调用，已有控件与字体/布局保留；`TrapUpgradeArrowRing` 用网格绘圆框，无额外贴图。
- 新增持久测试 `Tests~/Editor/TrapUpgradeMenuTests.cs`（24 例）；`RunTrapUpgradeRulesTests.ps1 -IncludeStep7` 包含前六步回归。最终 EditMode **204/204 通过，0 失败、0 跳过**，其中菜单 24、前六步 180；报告：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step7\20261029-233524-610\editmode-results.xml`。四个界面用例初始化真实 Canvas 和放置控制器，验证独立箭头、底层隔离、原槽位分配、余额事件/成功结果/重复确认、换局关闭、满级禁用及重复补齐不改布局/字体。测试中定位并修复 Unity 组件不能用 `??` 判空的问题，背景 CanvasGroup 使用 Unity 的显式 null 判定补齐。
- 已确认 8 份临时测试源码及 `.meta` 全部清理；随后正常 Unity 编译、`AddMissingControlsAndPreview`、菜单及升级目录校验通过，无 C# 编译错误或方法异常。日志：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step7\normal-compile.log`。七类预览已重新生成；预览时动态字体产生的自动缓存变化恢复到原资产，未替换用户字体。未做真实 PlayMode 人工键鼠操作或完整 Player 构建。

## 陷阱升级系统 · 第 8 步（2026-10-30）

- `TrapUpgradeCatalog.GetBranches` / `TrapUpgradeService.GetBranches` 提供方向列表唯一读取入口；`TrapUpgradeService.GetQuote` 对分支同时校验 catalog `integrated` 与 `branchEffectAvailable`，报价和提交均拒绝未接入分支，不能只靠界面开关收款。
- `TrapUpgradeMenuSession` 新增 `Branch` 导航状态、选中分支和方向报价快照。三级确认进入方向页；普通四级进入二次确认；分支进入独立详情页，未接入时确认按钮不可用。返回/Esc 依次回到方向页、三级确认，状态与余额不变；成功后仍拒绝重复确认。
- `TrapSelectionMenuView.Upgrades.cs` 在用户现有 Canvas 中增量创建滚动方向列表，普通四级和每个 catalog 分支分别展示说明、费用、属性和状态；底层 CanvasGroup 继续阻断点击，方向页、分支详情页返回时恢复 EventSystem 焦点。`TrapSelectionMenu.CursorOwned`、玩家两份 `UpdateCursor` 无需新增副本。
- `TrapUpgradeMenuAuthoring.AddMissingControlsAndPreview` 的备份目录为 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step8\prefab-backup\`。预览工具创建临时内存 catalog 示例分支，不写正式资源；方向/禁用详情预览写入 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step8\`。
- `RunTrapUpgradeRulesTests.ps1 -IncludeStep8` 最终 **209/209 PASS，0 FAIL，0 SKIP**：菜单 29、规则 49、支付 34、耐久 19、战斗接入 13、延迟伤害 21、Betty7 13、导弹 13、燃烧 6、经济 10、Canvas 2。XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step8\20261030-005143-601\editmode-results.xml`。临时源码及 `.meta` 已清理。
- `TrapUpgradeCatalogAuthoring.ValidateAll` 与图形模式 `TrapUpgradeMenuAuthoring.AddMissingControlsAndPreview` 均通过，使用项目记录 Unity `6000.6.7f1`，无 C# 编译错误或方法异常。未做真实 PlayMode 人工键鼠操作或完整 Player 构建；方向页/禁用详情 PNG 已用 `view_image` 检查可见性。

## 陷阱升级系统 · 第 9 步（2026-10-30）

- `TrapHealthBarUI.GetLevelLabel(TrapInstance)` 读取实例已提交的 `UpgradeState`：1～3 级显示“等级 N”，普通四级显示“等级 4 · 普通”，分支从当前 wallet 的升级 service 按 TrapId/BranchId 查配置名称，缺失配置时回退到 BranchId。HUD 不创建升级 service，也不持有第二份等级状态；升级、新实例和换局沿用实例同步机制。
- IMGUI 面板默认 `(280, 82)`，等级使用独立蓝色样式行，长分支名称换行并按内容增高；名称和血量各用独立区域防止覆盖。`CanDrawPanel` 在绘制时检查 `TrapSelectionMenu.CursorOwned`，菜单开启与关闭当帧立即隐藏整块面板，解决原先只停止选靶却仍延迟显示的问题。原射线、遮挡、无 Collider 回退、移开延迟和世界锚点共用原实现。
- 持久用例：`Tests~/Editor/TrapUpgradeHealthBarTests.cs`（23 例）；原 `Assets/Editor/TrapHealthBarUITests.cs` 已迁至 `Tests~/Editor/TrapHealthBarUITests.cs`（4 例）。统一入口 `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludeStep9`，包含前序 209 例并在 finally 清理临时源码和 `.meta`。最终 **236/236 PASS，0 FAIL，0 SKIP**，覆盖七类正式定义的等级/普通方向/内存测试分支名称、新实例与换局刷新、真实物理遮挡和 Collider 命中、无 Collider 选靶、移开延迟、菜单开启/关闭当帧/重新瞄准。
- 报告：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapUpgradeSystem/step9/20261030-113351-058/editmode-results.xml`。未执行主场景人工键鼠验收或完整 Player 构建；本步不制作分支技能，不推进第 10 步资产兼容与端到端验收。
- 图形编辑器进入空白预览场景的 PlayMode，复用实际 `EnsureStyles` / `DrawPanel` 生成七类 × 五种状态的 IMGUI 检查图（含低血量），已用 `view_image` 检查中文与布局：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapUpgradeSystem/step9/healthbar-levels.png`（2560×1440）。临时脚本和编辑器恢复场景留档在同目录，项目内临时脚本及 `.meta` 已清理；内存示例分支不写正式 catalog。批处理模式截屏未生成，最终通过图形编辑器捕获；这不是主场景人工输入验收。
- 清理全部暂存测试/预览源码后，正常 Unity 编译及 `TrapUpgradeCatalogAuthoring.ValidateAll` 通过：`TRAP_UPGRADE_CATALOG_PASS: 10 definitions, 8 TrapIds, one catalog`，无 C# 编译错误或方法异常。日志：同目录 `normal-compile.log`。测试自动产生的 TMP 字体缓存变化已恢复，未修改正式字体、场景或预制体。

## 陷阱升级系统 · 第 10 步（2026-10-30）

- `TrapUpgradeCatalogAuthoring` 继续以 `Assets/Resources/TrapUpgradeCatalog.asset` 为唯一正式配置入口；默认创建只补缺失资产，不覆盖已有 catalog。`ValidateAll` 同时扫描 `Assets/Resources` 与 `Assets/TrapDefinitions`，拒绝重复 TrapId 的冲突基础价格，并新增七类正式 TrapId 定义与七条默认升级配置的完整性检查。
- `TrapUpgradeRuntime` 新增 `AfterSceneLoad` 自动初始化，优先复用当前钱包绑定的服务；缺少 catalog 时 `TrapUpgradeService` 继续使用普通等级成长，方向列表保持为空，分支不会被隐式启用。
- 既有生成链的兼容修复：`Betty7TrapAuthoring` 只在新建定义时写入首发价格；`ThroatLampAuthoring` 重建时保留既有 `TrapInstance.maxHealth` 与原有战斗调参；`RepairStationAuthoring` 重建时保留既有基础血量、维修量及四臂时序参数，同时重新绑定模型引用和四臂 rig。未修改地图、MapForge 协议或陷阱模型。
- 新增持久用例 `Tests~/Editor/TrapUpgradeStep10Tests.cs`，并扩展 `RunTrapUpgradeRulesTests.ps1 -IncludeStep10`。Step10 全量 EditMode **241/241 PASS，0 FAIL，0 SKIP**；新增兼容/缺省初始化用例 5 例。报告：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step10\20261030-131522-401\editmode-results.xml`。测试临时源码及 `.meta` 已清理，持久测试留在 `Tests~`。
- 清理测试源码后，正式 Unity `6000.6.7f1` 编译及 `TrapUpgradeCatalogAuthoring.ValidateAll` 通过；日志：`C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\20261030-131716-trapupgradecatalogauthoring-validateall.log`。`Betty7TrapAuthoring.ProbePrefab`、`ThroatLampAuthoring.ProbePrefabBatch`、`RepairStationAuthoring.ValidateBatch` 均通过；对应生成链重跑成功，运行产物备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step10\prefab-backup\`，随后恢复无关的材质/动画/预制体噪声。
- 未执行主场景真实 PlayMode 人工键鼠升级、攻击/维修过程验收或完整 Player 构建；这些项目不能由本次 EditMode 与编辑器探针结果替代。

## XM777 自动炮击陷阱接入（2026-10-30）

- 放置预览画面错乱修复（2026-10-30）：原 XM777 FBX 导入了 Blender 的 `PreviewCamera` 和三盏制作灯光，并被生成器保存在正式预制体中；瞄准地面网格使预览激活时，额外相机参与渲染，玩家与无人机模式共用此触发路径。`XM777.fbx.meta` 与 `XM777TrapAuthoring.CreateAssets` 现均关闭相机/灯光导入，已重新生成正式预制体；`TrapPlacementController.RebuildPreview` 也禁用预览所有子层级（含 inactive）的 Camera、AudioListener 和 Light，保护旧资源的预览。正式放置通过无相机/灯光的预制体避免同样问题。
- 本次 `XM777TrapTests` EditMode **15/15 PASS**：新增 active/inactive 制作相机用例，覆盖玩家/无人机视角相机引用下反复显隐，确认相机引用/位置/朝向不变；生成资产检查覆盖正式预制体、其实例及导入设置。报告与修改前备份：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\camera-fix\`。新增只读批处理探针 `XM777TrapAuthoring.ValidatePresentationBatch`，可检查相机/灯光/音频监听器未回流。测试不替代主场景人工 PlayMode 操作。
- 清理临时测试源码及 `.meta` 后，正常 Unity 编译完成，无 C# 编译错误，`normal-validation.log` 确认 `XM777_PRESENTATION_PASS`；正式预制体 Camera 从 1 降至 0、Light 从 3 降至 0，73 个 MeshRenderer 保留。日志在探针完成与退出阶段出现 Unity Licensing Client 的 `IServiceProvider` 已释放异常，随后仍记录成功退出；不应据 runner 的摘要宣称整份日志无异常。未执行主场景人工键鼠验收或完整 Player 构建。

- Visual repair on 2026-10-30: trap placement previews now disable the XM777 firing component, matching the other active trap preview behavior. `FitToFootprint` now preserves imported root scale while applying the 4 m footprint. The menu icon renderer now uses the normalized `Assets/Prefabs/XM777.prefab`; `TrapSelectionMenuAuthoring.RegenerateXM777IconBatch` refreshes only this icon. The updated EditMode suite passes 13/13, and the test source was removed from `Assets/Editor` before normal Unity validation.

- 运行时入口为 `Assets/Scripts/TowerDefense/XM777Trap.cs`：地面目标限定基座水平距离 10～100 m，使用 `MissileAimSolver` / `MissileVelocityTracker` 预测 500 m/s 炮弹的拦截点；每 3 秒发射一次（`XM777Trap.FireInterval = 3f`，2026-10-30 由 6 秒改为 3 秒），炮弹以定时命中记录表示，不创建 projectile GameObject。仰角按当前敌人水平距离从 0° 线性增加到 45°；索敌与预测落点使用四路宽松视线检测（见下文）。爆炸半径 4 m，基础爆炸伤害 45，并通过 `TrapInstance.GetEffectiveDamage` 接入升级倍率；爆炸范围伤害忽略墙体遮挡，飞行怪物不会成为目标。
- 作者工具 `Assets/Editor/XM777TrapAuthoring.cs` 从最终模型 `Assets/Models/XM777/XM777.fbx` 生成 `Assets/Prefabs/XM777.prefab` 与 `Assets/Resources/XM777.asset`（TrapId `xm777`、地面安装、40 金币），并将炮塔 yaw 枢轴、炮口和模型层级接线固定下来。`nextShotTime` 为可序列化运行态字段，陷阱重新启用时清空旧待爆炸记录与冷却状态。
- `TrapUpgradeCatalogAuthoring` 与 `TrapSelectionMenuAuthoring` 已纳入 `xm777`；正式目录 `Assets/Resources/TrapUpgradeCatalog.asset` 有对应默认条目，菜单图标生成器从 XM777 FBX 生成 `Assets/Models/TrapMenu/Icons/xm777.png`，两份既有定义目录仍由统一校验扫描。
- 持久测试 `Tests~/Editor/XM777TrapTests.cs` 共 12 例，入口 `Assets/Editor/RunXM777TrapTests.ps1`；2026-10-30 使用项目记录 Unity `6000.6.7f1` **12/12 PASS**。随后 `TrapUpgradeCatalogAuthoring.ValidateAll` 与 `TrapSelectionMenuAuthoring.Validate` 均通过。测试源码未留在 `Assets/Editor`，未做主场景真实 PlayMode 炮击过程或完整 Player 构建。
- XM777 制作中间态（Blender、FBX 备份、预览图和设计说明）完整保存在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\`；仓库只保留最终模型与 Unity 资产。

### XM777 转向修复（2026-10-30，滚转 → 水平旋转）

- 症状：游戏中炮塔「左右滚转」而不是水平转向。根因在模型导入轴系：`Assets/Models/XM777/XM777.fbx` 保留了 Blender 的坐标帧，预制体里的 `XM777_Base_Root` 局部旋转是 **X = -90°**，所以它的 `up` 是世界**水平**轴；`XM777Trap.AimYawAt` / `IsYawAligned` 原先都用 `yawPivot.parent.up` 当偏航轴，于是绕水平轴旋转 = 滚转。
- 修复：`XM777Trap` 新增 `yawAxisWorld`，由 `ResolveYawAxis()` 在捕获静止姿态时算出（取炮塔根节点 `transform.up`；与世界上方向夹角超过约 25° 时回退 `Vector3.up`）。两处偏航计算共用该字段，**不要**再回到 `yawPivot.parent.up`。
- 第二层坑（同一次修复）：`restYawRotation` 是四元数字段，默认值是 `(0,0,0,0)` 而不是单位四元数，原先只在 `Awake` 里赋值。编辑器工具/测试直接驱动组件（`Awake` 未运行）时 `restYawRotation * restBarrelForwardLocal` 归零，`AimYawAt` 会静默提前返回、炮塔完全不转。现改为 `EnsureRestPose()` 惰性捕获一次，`Awake` / `Tick` / `AimAt` 三个入口都调用；新增状态字段 `restCaptured`。
- 新增只读/工具接口：`XM777Trap.AimAt(Vector3, float)`（与游戏内同一套偏航数学，供工具和测试驱动）、`YawAxisWorld`、`BarrelDirectionWorld`、`BarrelElevationDegrees`。
- 回归用例 2 例，均在 `Tests~/Editor/XM777TrapTests.cs`：`TraversesHorizontallyWhenTheImportedRigParentIsAxisRotated` 复刻 `Base_Root` X=-90 的轴系，断言偏航轴竖直、转向时仰角不变、被施加的旋转轴必须竖直；`GeneratedPrefabTurnsAroundWorldUp` 用真实 `Assets/Prefabs/XM777.prefab` 走 `AimAt`，断言施加旋转轴竖直。**回退验证**：把两处轴临时改回 `yawPivot.parent.up` 后该用例失败（仰角从 45° 掉到约 0°），确认用例能抓住旧 bug。最终 EditMode **17/17 PASS**（Unity `6000.6.7f1`），XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\yaw-fix\before\20261030-183623-864\editmode-results.xml`（回退态 1 失败）与 `C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\tests\20261030-184002-057\editmode-results.xml`（修复态全通过）。
- 看图入口：`Assets/Editor/XM777TrapPreview.cs`，菜单 `Tools/塔防/预览 XM777 转向`，批处理 `XM777TrapPreview.RenderRotationPreview`（需 `-WithGraphics`）。按 0/45/90/180/270° 驱动真实预制体渲染并写 `report.txt`；实测 achieved 与请求差约 2°（枪口偏离旋转轴的视差，属既有瞄准实现，本次未改），仰角恒为 45.00°，PNG 在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\yaw-fix\preview\`。
- 本次未做主线场景人工 PlayMode 炮击验收，也未做完整 Player 构建；炮击命中/伤害逻辑未改动。

### XM777 距离俯仰与宽松索敌（2026-10-30）

- 当前目标和预测落点的射程均以陷阱根节点为基座、按世界水平投影计算；仰角为 `Clamp01((distance - 10) / 90) * 45`，10/55/100 米分别为 0/22.5/45°。水平转向仍跟踪预测拦截点，俯仰使用当前敌人位置的水平距离；无目标时保持最后姿态。
- `XM777Trap` 新增序列化 `pitchPivot`、`pitchSpeedDegrees`（默认 240°/秒），生成器接线至已有 `XM777_Pitch_Root`；旧实例按节点名补齐。捕获原始局部姿态、原始仰角及父坐标系里的俯仰轴，相对模型原有 45° 姿态施加角差；yaw 继续使用先前修复的竖直轴。`AimAt(Vector3, float)` 驱动双轴，新增只读 `PitchPivot` 与统一映射 `ElevationForDistance(float)`。双轴均进入 3° 容差才开火，飞行时间仍为实际炮口到落点的三维距离 / 500，不新增重力弹道。
- 索敌和预测落点共用 `HasClearTargetPath`：中心、中心上方 1.5 米、水平左右各 0.75 米四条射线，任一路畅通即可；左右按基座到检测点的水平方向确定。每条射线沿用最近非自身碰撞体判定、忽略 Trigger、允许无 Collider 敌人；完整覆盖四路的高墙仍阻挡。爆炸伤害规则不变。
- 正式预制体已由生成器重建；原预制体、`.meta` 与定义备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\distance-elevation\backup\`。`XM777TrapAuthoring.ValidatePresentationBatch` 同时校验 yaw → pitch → muzzle 引用及无制作相机/灯光；预览工具默认输出至同目录 `preview\`，渲染三种距离 × 五种水平朝向，并断言实际仰角和水平对准误差；旧章节中的“仰角恒为 45°”仅描述历史状态。
- `RunXM777TrapTests.ps1` 最终 **38/38 PASS，0 FAIL，0 SKIP**（项目记录 Unity `6000.6.7f1`）；包括射程边界、高差、线性映射、真实预制体与导入轴、近远切换、双轴开火门槛、上方/侧方绕障、完整遮挡、预测落点规则与当前距离仰角、提前量越界、忽略自身/Trigger、敌人 Collider、最近目标及原有伤害/冷却回归。最终 XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\distance-elevation\tests-final\20261030-190317-457\editmode-results.xml`；测试源码保留在 `Tests~/Editor/XM777TrapTests.cs`，临时源码和 `.meta` 已清理。
- 图形批处理正常编译、预制体引用/表现校验及 **15/15 姿态预览通过**：`normal-validation-preview.log` 与 `preview\report.txt` 在上述目录；0/22.5/45° 三张代表图已用 `view_image` 检查。最终测试清理后再次执行正常编译与引用校验通过（`normal-validation-final.log`，`XM777_PRESENTATION_PASS`，无 C# 编译错误或方法异常）。未做主场景人工 PlayMode 炮击验收或完整 Player 构建。

- 射速调整（2026-10-30）：`XM777Trap.FireInterval` 由 `6f` 改为 `3f`，仍是唯一冷却来源（`FireAt` 里 `nextShotTime = clock + FireInterval`），预制体不序列化该值，无需重建 `Assets/Prefabs/XM777.prefab`。持久用例 `FiresEveryThreeSecondsAndDoesNotCreateProjectileObjects` 同步改为 3 秒节奏并断言 `XM777Trap.FireInterval == 3f`；`RunXM777TrapTests.ps1` **38/38 PASS**，XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\tests\20261030-190959-384\editmode-results.xml`，临时测试源码与 `.meta` 已清理。未做主场景人工 PlayMode 炮击验收。
- 稳定索敌（2026-10-30）：`XM777Trap.Tick` 在当前目标存活有效时保留锁定，不再每帧因更近怪物、暂时超出射程或视线遮挡而切换；仅目标死亡/失效或 `FireAt` 完成本次发射后重新选择，目标改变时重置速度估计。新增 `KeepsLivingTargetWhileCloserEnemyArrivesBeforeShot`，并更新越界用例以验证存活目标仍锁定；`RunXM777TrapTests.ps1` 使用 Unity `6000.6.7f1` **43/43 PASS**，XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\tests\20261030-222532-647\editmode-results.xml`。测试运行器已清除临时 `Assets/Editor/XM777TrapTests.cs` 与 `.meta`；未做主场景人工 PlayMode 炮击验收。
- 实际开火俯仰射界（2026-10-30）：`XM777Trap` 新增硬边界 `MinimumFiringElevationDegrees = -15` / `MaximumFiringElevationDegrees = +75`，开火前按基座到预测落点的真实仰角调用 `IsWithinFiringElevation`；边界含 0.001° 浮点容差。模型炮管的距离映射仍保持 0°～45°，因此只改变“能否开火”，不改变模型俯仰姿态。`XM777TrapTests` 新增射界边界与越界开火门控用例，`RunXM777TrapTests.ps1` **52/52 PASS**；XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\tests\20261030-232726-509\editmode-results.xml`。未做主场景人工 PlayMode 炮击验收。
- 射界再次调整（2026-10-30）：按最新要求将 XM777 实际开火俯仰射界改为 **-30°～+45°**；模型炮管原有距离映射仍保持 0°～45°不变。边界与越界门控用例同步更新，预期重新运行 XM777 测试。

重建日期：2026-10-16（系统重装后）。本文件保存可随工程保留的项目知识；以当前代码与配置为准，历史路径须重新核实。

## 测试天花板恢复入口（2026-10-30）

- 主地图测试板使用 `CeilingTrapGridAuthoring.AddTestPanelToMainScene` 就地恢复；避免为恢复单个测试板执行全量地图导入，以保留手工墙面网格和空中出怪口。恢复后检查实体可见性、支撑 Collider 和天花板安装网格，并保存主场景。
- 2026-10-30 已恢复缺失的 `ceiling_test_panel_20261025`，尺寸 4×4 米、底面高度 4 米、`CeilingTrapGrid_bottom` 为 4×4 格。新增菜单 `Tools/塔防/恢复主地图测试天花板`，重复恢复会启用实体、Renderer、Collider 和网格，并清除编辑器隐藏状态；复用已加载主场景，避免重新打开场景丢失内存修改。正式 Unity 编译及恢复探针通过（16/16 格有支撑），场景 diff 仅新增 294 行，原 26 个名称包含 Wall 的对象数量不变、场景 `.meta` 未变；未执行 PlayMode 放置验收。恢复前场景备份及 `restore.log` 位于 `C:\Users\ASUS\Desktop\陷阱素材文件夹\CeilingTrapSystem\restore-20261030\`。

## 项目定位与可信来源

- 工程：Unity 第一人称射击 + 塔防；配套 MapForge 浏览器地图编辑器。
- 工程根目录：`C:\Unity Project\ProjectAlphaOperationStriker`。
- 运行时代码：`Assets/Scripts/FPS`、`Assets/Scripts/TowerDefense`、`Assets/Scripts/MapForge*.cs`；编辑器工具与验证：`Assets/Editor`。
- 主场景：`Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`，已在 `ProjectSettings/EditorBuildSettings.asset` 中确认。
- Unity 版本唯一依据：`ProjectSettings/ProjectVersion.txt`。运行前动态匹配安装目录，不把历史版本号固定到脚本或文档。
- 当前项目记录的 Unity 编辑器版本（2026-10-24）：`6000.6.7f1`（以 `ProjectSettings/ProjectVersion.txt` 为准；运行前动态匹配安装目录）。
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
| 陷阱中间素材（2026-10-27 更正） | 当前目录为 `C:\Users\ASUS\Desktop\陷阱素材文件夹`；`C:\Users\Origami` 是旧用户目录，已停用，不再使用 | 后续陷阱制作的中间产物统一放入 ASUS 当前桌面素材目录 |

此表是一次性观察，不是安装/迁移清单。环境变化后更新，不为了解决玩法任务顺带安装或升级工具。

## 后续编辑约束

- 遵守根目录 `AGENTS.md`：先说明目标文件，最多 5 次只读命令后落笔；优先最小改动。
- 只探索 `Assets/Scripts`、`Assets/Editor`、`Assets/Models`、`ProjectSettings`、`Packages`；技能按其指定路径读取。
- 不读取或遍历 Library、Logs、Temp、obj、Blender 源文件及根目录日志；大型 Unity YAML 只定位并读取局部。
- 输入使用新 Input System；生成资产优先修改 Authoring/Importer。
- 中间素材遵守用户指定的桌面素材目录；系统重装后的旧账户路径不能当作已验证环境。

## 玩家与输入

- 武器快捷分类（2026-10-23）：`PlayerWeaponCategory` / `PlayerWeaponCategories` 将现有 `WeaponClass.Pistol`（含左轮）映射为副武器（2）、`Launcher` 映射为发射器（3），其余包括 Unknown 均为主武器（1）。优先读武器子层级的 `WeaponDamageProfile`，无档案时按现有名字映射解析；运行时实例名去掉 `(Clone)`。当前 20 把武器为 14 主武器、4 副武器、2 发射器。
- `FirstPersonPlayerBootstrap.EnsureBridgeComponents` 安装 `PlayerWeaponCategorySwitcher`：支持主键盘/小键盘 1/2/3；重复按同类键循环该类所有武器，跨类返回记住上次武器。临时覆盖运行时 `ChangeWeapon` 的键盘绑定（移除旧 F），停用组件时恢复；保留滚轮。菜单、暂停、死亡、观察和玩家输入停用时阻止切枪。投影枪放置模式按任意 1/2/3 均退出并恢复普通武器 Renderer。
- KINEMATION 没有公开按索引装备接口，桥接反射集中在 switcher：`_weapons` / `_activeWeaponIndex` / `EquipWeapon(bool,bool)`。用与包内 `OnMouseWheel` 相同的立即装备路径（旧武器停用、设置索引、新武器启用、`EquipWeapon(false,true)`），同时取消待处理的 `EquipWeapon_Incremental` / `SetWeaponVisible`，避免延迟回调抢回武器。不编辑被忽略的第三方包或手工改 prefab。
- 数字键通过独立 `InputActionMap` 的 Button `performed` 事件接入，组件启停时同步启停，销毁时释放。回归入口：`Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures PlayerWeaponCategoryTests,WeaponDamageProfileTests -OutputDirectory C:/Users/ASUS/Desktop/WeaponCategories`。真实 Unity 6000.6.6f1 验收 **24/24 通过、0 失败、0 跳过、无 C# 编译错误**：分类测试 14 项（含真实主场景进入 Play Mode 的集成测试），原武器伤害测试 10 项。覆盖主键盘 1/2/3、20 把武器的 14/4/2 分类与完整类内循环、跨类记忆、小键盘 2 退出投影枪、旧 F 不再切枪、主菜单/陷阱菜单阻止切枪、组件停用恢复原 F 绑定。结果：`C:/Users/ASUS/Desktop/WeaponCategories/20261023-231621-073/editmode-results.xml`。
- 无图形输入验收注意（2026-10-23）：测试临时设置 `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`、`backgroundBehavior = IgnoreFocus` 和 `Application.runInBackground = true`，结束时恢复；否则虚拟键盘事件可能只进入编辑器状态，不能触发游戏 Action。测试注入时临时用 `ProcessEventsManually` + `InputSystem.Update()`，立即恢复更新模式，不保存项目输入配置。进入 Play Mode 的 `UnityTest` 不要在迭代器里用捕获局部变量的调试 lambda：域重载后闭包可能失效，赋值时抛空引用；采用静态回调。主键盘数字键绑定路径为 `<Keyboard>/1` / `2` / `3`，通过实际控件匹配和按键事件验证。

- 菜单中文字体修复（2026-10-23）：`GameStartMenuView` 统一加载 `Resources/UI/Fonts/MenuChinese SDF`，初始化时也替换旧预制体中所有文本（含未激活的设置页）。源字体为随包携带的 Noto Sans CJK SC，路径 `Assets/Resources/UI/Fonts/NotoSansCJKsc-Regular.otf`，同目录保留 OFL 许可；不依赖目标机器的系统字体。`GameStartMenuAuthoring.GenerateAndValidate` 生成动态 TMP 字体、预填菜单及“继续游戏”字符、保存材质与图集子资源并重建菜单预制体；`Validate` 检查全部标签和字符覆盖，结果写到当前用户桌面 `GameStartMenu/font-validation.txt`。本次仅修字体，不改变战斗 HUD 显隐。
- 字体验收：Unity 编译、生成及 14 个标签（加“继续游戏”动态文字）的字符覆盖检查通过；主菜单与设置页渲染预览分别在 `C:\Users\ASUS\Desktop\GameStartMenu\font-main-menu.png`、`font-settings.png`，已确认中文字形正常。生成日志 `font-generation.log` 包含成功退出标记且无 C# 编译错误或异常；主场景、ProjectSettings、构建场景设置、包清单的前后 SHA256 均一致。备份在同目录 `font-fix-backup`，预览工具不保存或修改游戏场景。

- `FPS/FirstPersonPlayerBootstrap.cs` 在场景加载后复用现有 KINEMATION `FPSPlayer` 或旧 `FirstPersonController`；都没有才加载 `Resources/FPSPlayer`。负责补生命、观察、攀爬、移动、射击与 UI 桥接，并停用作为出生参考的场景摄像机。
- `FPS/FirstPersonController.cs` **同一文件有多个实现**，包括 `FirstPersonController`、`FPSPackagePlayerMotion`、`FPSHitscanShooter`；改移动/鼠标/射击前查所有相关类。KINEMATION 集成是明确依赖，不能把它当成纯旧控制器工程。
- 光标所有权既受 `TrapSelectionMenu.CursorOwned`，也受 `GameStartMenu.CursorOwned` / `BlocksPlayerCursor` 控制。两份 `UpdateCursor` 都必须尊重菜单，其他恢复光标的路径也要一致。
- `GameStartMenu`（`Assets/Scripts/FPS/GameStartMenu.cs`）负责开始/暂停状态、输入禁用、Esc、光标和设置值；界面由 `GameStartMenuView` 构建。运行时优先加载 `Assets/Resources/UI/GameStartMenu.prefab`，预制体壳缺少已序列化布局时会自行构建 Canvas，并从 `Assets/Resources/UI/MainMenuBackground.png` 读取背景。正式视觉源图为 `Assets/UI/MainMenuBackground.png`；现有项目编译正常时，可运行 `Tools/行动打击/生成战术主菜单`（`GameStartMenuAuthoring.Generate`）将完整 Canvas 烘焙到同一 Prefab 路径，`验证战术主菜单` 用于资源接线检查。
- `PlayerHealth` 提供生命事件；`PlayerLifeStateController` 负责死亡后暂停控制、隐藏模型、进入 `PlayerObserverMode` 并延时复活；默认复活延迟 3 秒，优先指定/场景命名出生点，否则回到初始位置。它使用真实时间等待，不等同于塔防失败。
- `PlayerSprintAimBridge` 桥接冲刺与瞄准；`Ladder.cs` 同时定义梯子与攀爬行为；`WeaponDamageProfile` / `WeaponDamageAuthoring` 管武器伤害配置。
- 梯子地面修复（2026-10-27）：`GameplayLadder.fbx` 导入后长轴沿世界 -Z（包围盒中心约 Z=-1.63），不能直接把 FBX 根节点当作竖直梯子。`GenerateLadderPrefab.Generate` 用独立玩法根节点和 Visual 包装层，将模型长轴转向 +Y，再按真实 Renderer 包围盒归一到 Y=0..3.2；触发区和 Ladder 高度使用同一范围。`PlaceGameplayLadders.Place` 先重建该预制体，再按既有 8 个对称点和朝向缩放到 Y=0..2，已更新主场景。`PlaceGameplayLadders.ValidateAndPreview` 检查长轴竖直、模型/触发区/攀爬上下界、脚下实际地面，并渲染预览；批处理修复入口 `RepairAndPreviewBatch` 需带 `-WithGraphics`。Unity 编译及 8/8 数值探针通过，目视确认竖直贴地；未做人工 Play Mode 攀爬。预览、报告、日志和修改前场景/预制体备份位于 `C:\Users\ASUS\Desktop\陷阱素材文件夹\GameplayLadder\ground-fix-20261027\`。
- 十字路口梯子（2026-10-30）：`IntersectionLadderAuthoring.Place` 在主场景中央路口的南北平台各放左右对称的两架 WallLadder，点位为 x=±6、z≈-20/-12，脚底道路高度 0.1、平台高度 2。入口以碰撞体探测平台及高度，保存前校验梯顶和下梯方向，独立分组 `IntersectionWallLadders_Symmetric`，可重复执行；场景备份和总览在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\WallLadder\intersection-20261030-221208\`。Unity 批处理通过；未做人工 Play Mode 攀爬验收。

- 贴墙垂直梯子 WallLadder（2026-10-27 新增）：这是与 `GameplayLadder`（斜靠/独立式、占 1.35×0.5 m 触发区）**不同的模型**，两者都能用；主场景已按用户要求改用本模型（见下方「主场景梯子替换为贴墙梯子」）。
  - 模型 `Assets/Models/WallLadder/WallLadder.fbx`（Blender 源与全部中间态在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\WallLadder\`），单网格 1248 顶点 / 2368 三角面 / 3 材质槽。
  - **轴与原点约定**：长轴竖直，脚底在本地 Y=0，安装面（背面）在本地 Z=0，梯身朝 **+Z** 伸出，前进方向 +Z 背离墙面。因此摆放时根节点的 +Z 必须是“玩家所站的一侧”，墙体在 -Z 后方；相机/预览若放到 -Z 会被墙挡住。
  - 尺寸：可攀爬高度 3.20 m、导轨外缘 0.60 m、**含两侧墙面法兰总宽 0.66 m**、进深仅 **0.07 m**（10 mm 安装板 + 60 mm 导轨），这就是“贴合墙壁”的量化判据；`WallLadderAuthoring.MaxDepth = 0.12` 是上限。
  - 细节：10 根横档（间距 0.30 m，从 0.25 m 起）、3 组墙面安装板 + 12 颗螺栓、顶部深色端帽。材质 `WL_SafetyYellow` / `WL_SteelDark` / `WL_ZincBolt`。
  - 生成器 `Assets/Editor/WallLadderAuthoring.cs`：`CreateAssetsBatch`（生成 `Assets/Models/WallLadder/Materials/M_WL_*.mat` 三个 URP/Lit 材质 + `Assets/Prefabs/WallLadder.prefab`）、`ValidateBatch`（16 项数值探针）、`RenderPreviewBatch`（需 `-WithGraphics`，4 张 PNG）。菜单在 `Tools/塔防/贴墙梯子/`。
  - 预制体结构：`WallLadder`（`Ladder` + `BoxCollider` 触发区）→ `Visual`（按包围盒归零到 Y=0 / Z=0 / X 居中）→ FBX 实例。触发区 0.80 × 3.20 × 0.60，比模型宽，因为模型只有 7 cm 厚。
  - **`Ladder.cs` 新增 `topDismountDirection`（本地向量，零值 = 沿用 `transform.forward`）**：贴墙梯子设为 `(0,0,-1)`，顶部自动下梯时把玩家推向墙内上层平台，而不是推离墙面掉下去。默认零值让 `GameplayLadder` 等既有梯子行为完全不变；`Ladder.DismountDirection` 是唯一读取点，`LadderClimbing` 的自动下梯用它。
  - 校验结论：Blender 复导入 15/15（高度/宽度/进深/贴地/贴墙共面/竖直/居中/材质/三角面预算），Unity 预制体 16/16（含长轴、脚底、安装面 Z=0、进深上限、下梯方向、触发区、唯一碰撞体、全 URP 材质）。**未做人工 Play Mode 攀爬验收**。
  - 两个踩过的坑：① 横档端面与导轨内侧面**完全共面**会产生 z-fighting 虚线，横档要嵌入导轨 5 mm；端帽也要略大于导轨并吞掉其顶面。② Cycles 预览里导轨的“台阶状条纹”是金属高光 + 降噪的噪点，不是几何问题（`diagnose.py` 证明 928 面、0 退化面、0 重复顶点，导轨正面是单个平面四边形）；降金属度后完全干净。

- 主场景梯子替换为贴墙梯子（2026-10-27）：主场景里原有的 8 根 `GameplayLadder_*` 已全部换成贴墙梯子 `WallLadder_*`，分组从 `GameplayLadders_Symmetric` 改为 `WallLadders_Symmetric`（点位仍是 ±6/±22 的 X、z=±40 与 z=-8 共 8 处，左右对称）。
  - 入口 `Assets/Editor/ReplaceGameplayLaddersWithWallLadder.cs`：菜单 `Tools/Map/用贴墙梯子替换原有梯子`，批处理 `ReplaceGameplayLaddersWithWallLadder.RunBatch`（预览需 `-WithGraphics`）。**不要**再跑 `PlaceGameplayLadders.Place`：它会重新生成旧式 `GameplayLadder`，跑完就变成新旧并存。
  - 摆放规则（唯一一套，别在别处另写）：先从旧梯子脚点沿“朝平台方向”打水平射线取真实墙面；再在墙面**内侧** 0.6 m 处向下取最高水平面得平台顶面（探针方向写反会打到外侧地面，把梯子压成最小 0.9 m，这是本次踩过的坑）；根节点落在墙面上、沿背离墙方向退 5 mm（`WallGap`，防安装板与墙面共面 z-fighting）；旋转 = `LookRotation(-towardsPlatform)`，使本地 +Z 朝玩家、本地 -Z 朝平台内侧（与 `topDismountDirection` 一致）；等比缩放 `(平台顶面 − 地面) / ModelHeight`，顶端与平台顶面齐平，与旧梯子的攀爬上界完全一致，因此玩法可达性不变。
  - 当前实测：8 个点平台顶面都是 2.00 m、地面 -0.02 m，缩放 0.631、梯高 2.02 m、贴墙间隙 5 mm、触发区 0.50×2.02×0.38。校验 82/82 通过（长轴竖直、脚底贴地、顶端对齐平台、贴合墙面、攀爬上界=模型顶端、下梯朝平台内侧、触发区覆盖、唯一碰撞体、旧梯子清空、数量=8）。
  - 可重复运行：场景里没有 `GameplayLadder_*` 时脚本改以现有 `WallLadder_*` 为基准重算（朝平台方向取 `-forward`），所以改参数后直接重跑即可，不必回滚场景；每次运行前把场景副本写到输出目录。
  - 产物：场景备份、`validation.txt`、9 张预览在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\WallLadder\scene-replace-20261027-231250\`（修改前场景与 `GameplayLadder.prefab` 备份在同级 `scene-replace-backup\`）。**未做人工 Play Mode 攀爬验收**（自动下梯能否真站上平台仍待实测）。


## 出怪、战斗与胜负

- Betty7 选靶规则（2026-10-27）：`GroundEnemyCombat.CanTargetTrap` 对 `Betty7JumpMineTrap` 和 `GroundSpikeTrap` 使用同一排除规则；普通怪物默认不追击、不近战攻击这两类可通行陷阱，继续任务路径。保留序列化字段 `allowAttackGroundSpikes`，仅特殊怪物显式开启时允许攻击两者；不改 `TrapInstance.TakeDamage` 的通用受伤能力。回归入口 `Tests~/Editor/Betty7GroundEnemyCombatTests.cs`（真实 Betty7 预制体、路过/站在陷阱上、普通陷阱照常攻击、已有 Betty7 目标即时失效）；Unity EditMode **3/3 通过，无 C# 编译错误**，结果 `C:\Users\ASUS\Desktop\陷阱素材文件夹\Betty7\combat-tests\20261027-210423-405\editmode-results.xml`。临时导入的测试源码及 `.meta` 已清理；未做手动 PlayMode 验收。
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
- 墙面与地面陷阱按 `TrapDefinition.MountType` 使用相互独立的占用空间：跨类别的邻近、实体包围盒或跨网格 footprint 重叠不会阻止放置；同类别重叠以及墙体支撑、玩家碰撞和怪物通行净空检查保持不变。
- 怪物网格锚在格角，陷阱网格锚在格心，地面锚点相差本地 `(+0.5, 0, +0.5) × cellSize`。复用 `TrapGridAuthoring.GroundAnchor` / `CellCenter` / `DominantSurfaceTop`，并留意 `TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 的对应定义。
- 地面/平台掩码由 `TrapGridAuthoring` 生成；墙面网格由 `WallTrapGridAuthoring` 或 MapForge 的 `unity.wallTrapGrids` 生成。墙面需要绑定支撑 Collider，验证法线、多个支撑探点、实体包围盒和道路净空；攻击/特效范围不计入实体安装体积。
- 主地图墙面网格（2026-10-24）：`WallTrapGridAuthoring.EnsureStarterGridForCurrentMap` 在 `Platform_*` 两侧按 1 m 单元生成/对齐网格，支撑 Collider 绑定平台侧面；墙面开格需通过 `TrapPlacementGrid.HasWallSupport`，无连续支撑的格子保持关闭。主场景当前有 24 个墙面网格、1156 个开放格（关闭了 28 个支撑不足格）。场景实测用“先打开 `THREE_ROUTE_MERGE_MAP`，再调用 `TrapPlacementGridEditor.ValidateTrapGrids`”；不打开地图直接调用只会报告场景没有网格。此次地面格与怪物网格对齐通过，墙面支撑异常 0，4608/4608 个地面格无重复覆盖；原地面/道路掩码另报告 1096/8 个埋入地形格，尚未处理，不能称全地图校验通过。墙格沿墙方向按地面格距/半格格心对齐，法线方向贴合墙面（不能要求其同时落在地面格心上）；最后的专用对齐探针误比较了法线方向，修正后因 30 分钟限制未复跑。原场景副本：`C:\Users\ASUS\Desktop\TrapGridBackups\THREE_ROUTE_MERGE_MAP_before_wall_trap_grids_20261024.unity`；可检查预览：同目录 `THREE_ROUTE_MERGE_MAP_wall_trap_grids_preview.png`（绿=开放，红=关闭；仅预览叠加层，未保存进场景）。
- `MapForgeWallTrapGrid` 保存墙体对象 ID + 网格 ID；`MapForgeSceneOrganization` 和实时同步共同维护，修改格式时一起检查。

## 陷阱与伤害

| 能力 | 运行时入口 | 生成/资产入口与约束 |
| --- | --- | --- |
| 机枪炮塔 | `AutoSentryTurret` + `SentryFireFeedback` / `SentryShellEjector` / `SentryLaserSight` | 子弹伤害分类为 `Bullet`；美术为 `Assets/Resources/SentryMachineGun.prefab`（四脚支架 + 通用机枪 + 激光瞄准 + 开火后坐/枪口焰 + 抛壳），入口与契约见「哨戒机枪模型与开火/抛壳动画」小节 |
| 对空双联导弹 | `MissileLauncherTurret` → `MissileLauncherWeapon` → `MissileProjectile` | `MissileLauncherTurretAuthoring.CreateLauncherPrefabBatch`；预制体配置为 `Assets/Prefabs/DualMissileLauncher.prefab`，定义分别位于 `Assets/Resources` 和 `Assets/TrapDefinitions` |
| 导弹尾焰 | `MissileExhaustFx` | `Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx`；生成器负责安装两组并替换 URP 材质 |
| 地刺 | `GroundSpikeTrap` | `GroundSpikeTrapAuthoring.CreateAssets`；模型 `Assets/Models/GroundSpike/GroundSpike.fbx`，生成 `Assets/Resources/GroundSpike.prefab` / `.asset` |
| 贝蒂7号跳雷（地面陷阱） | `Betty7JumpMineTrap` | `Betty7TrapAuthoring.CreateAssets`；模型 `Assets/Models/Betty7/Betty7.fbx`，预制体 `Assets/Prefabs/Betty7.prefab`（四角 `Mine_01..04` 各自独立装填 + 方向择雷），定义 `Assets/Resources/Betty7.asset` |
| 墙面喷火器 | `FlamethrowerTrap` + `FlamethrowerFeedback` | 模型 `Assets/Models/Flamethrower/Flamethrower.fbx`（Blender 源在桌面「陷阱素材文件夹\Flamethrower」），`FlamethrowerModelAuthoring.CreateModelPrefabBatch` 生成 `Assets/Resources/Flamethrower.prefab`（`Model` 子节点 + `Flame Origin` / `Attack Origin` / 三个 Fx 挂点），定义 `Assets/Resources/Flamethrower.asset`；`CreateGraybox` 已加保护，检测到 `Model` 就拒绝覆盖 |
| 燃烧 | `FireStatusEffect` + `FireStatusEffectVisual` | 状态挂在怪物上，独立于点燃它的陷阱 |
| 瞄准陷阱血条 | `TrapHealthBarUI` | 复用 `TrapInstance.CurrentHealth` / `MaxHealth`，场景加载后自建 HUD |

以上资产位置由源码常量确认，未逐个打开资源检查其当前内容。

- 导弹瞄准与速度估计统一走 `MissileAimSolver` / `MissileVelocityTracker`；默认空中目标判定是 `EnemyInstance.MonsterType`，不是世界高度。默认只对空，yaw 可全圈，pitch 限制 0–75°。拦截点超仰角可退回瞄准本体；本体也超限则不能开火。
- 哨戒机枪竖直射界固定为 **-30°～+60°**（2026-10-24 明确）：`AutoSentryTurret.MinPitchDegrees = -30` / `MaxPitchDegrees = +60`，是硬边界，`ApplyPitchLimits` 只能在硬边界内收窄。瞄准改为按目标真实三维方向求仰角后夹取，`AimPivot` 同时承担 yaw 与 pitch；**目标超出射界时只跟踪、不开火**（`IsWithinFieldOfFire`，容差 0.5°），不要退化成“在限位姿态下照打”。`RequiredElevationDegrees` / `IsWithinFieldOfFire` / `ClampPitchDegrees` 是唯一判据，别在别处另写一套角度公式。出厂预制体 `Assets/Prefabs/AutoSentryTurret.prefab` 已显式写入 `minPitchDegrees: -30` / `maxPitchDegrees: 60`。回归：`SentryMachineGunTests`（18 例，含 5 例射界）与批处理 `SentryMachineGunStage5Validation.Run`（119 条断言）。
- 哨戒机枪的激光线挂在 `Aim Pivot` 下（`SentryLaserSight` 用 `laserEmitter.forward`），因此会跟随新增俯仰一起摆动，这符合弹道，不要为了“看起来水平”再把它拉平。
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
| 哨戒机枪（含 -30°～+60° 竖直射界） | `SentryMachineGunTests`（`Assets/Editor/RunSentryMachineGunTests.ps1 -ExpectedCases 18`）、批处理 `SentryMachineGunStage5Validation.Run` |
| 陷阱血条 | `TrapHealthBarUITests` |
| 墙面放置 | `WallTrapPlacementTests`、`WallTrapGridValidation.Run` |
| 地刺 | `GroundSpikeTrapValidation.Run`、`GroundSpikeStage5Acceptance.Run` |
| 喷火器 | `FlamethrowerPlacementValidation.Run`、`FlamethrowerGrayboxValidation.Run`（现校验模型预制体结构，不再生成灰盒）、`FlamethrowerTimingValidation.Run` / `RunPlayMode`、`FlamethrowerAttackVolumeValidation.Run`、`FlamethrowerFeedbackValidation.Run` |
| 燃烧与伤害类别 | `FireStatusEffectTests`、`TrapDamageClassificationTests`、`EnemyDamageResistanceDataTests`、`EnemyDamageSettlementTests`、`WeaponDamageCategoryTests`、`WeaponDamageProfileTests` |
| 网格对齐/掩码 | `TrapPlacementGridEditor.ValidateTrapGrids` / `ValidateOverlap`；使用数值校验 |

- NUnit `*Tests` 使用 `#if UNITY_INCLUDE_TESTS`；通过 Unity `-runTests -testPlatform EditMode -testFilter <类名>` 运行，不能把 `-executeMethod` 当作执行 NUnit 的替代。
- `*Validation.Run` / `*Acceptance.Run` 是独立编辑器入口，先看方法副作用再调用；部分会创建资源、切换场景或进入 PlayMode，不能当作纯只读检查。
- 静态编辑器方法优先通过 `.agents/skills/alpha-striker-unity/scripts/run-unity-method.ps1` 调用。读 `references/unity-editor.md` 了解日志判定与单实例限制；测试要检查结果 XML 和实际用例数量，不能只看进程退出码。
- 历史 `dotnet build C:\MapForge\.validation-build\UnityCompile.csproj` 当前路径缺失；即使恢复，其 Include 列表也只覆盖部分地图代码，不能当作全项目编译通过。
- 预览：`GroundSpikeTrapAuthoring.RenderPreview`、`MissileLauncherTurretPreview.RenderPreview`、`MissileExhaustPreview.RenderPreview`、`FlamethrowerPlacementPreview.Render` / `RenderGraybox` / `RenderAttackVolume` / `RenderFeedback`、`FlamethrowerModelAuthoring.RenderModelPreview`（**必须 `-WithGraphics`，且首帧是预热帧**，否则第一张图会偏灰）。执行前检查输出位置；已有实现中部分走临时目录，喷火器走当前账户桌面，与用户指定旧账户素材路径不一定一致。新增产物应服从用户给定位置。
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
- 接入步骤 1 完成（2026-10-24）：`ProjectSettings/ProjectSettings.asset` 的 `scriptingDefineSymbols` 已写入 `Standalone: ENABLE_DRONE_CODE`（编辑器脚本沿用活动构建目标组，没有独立 Editor 键）。真实 Unity 6000.6.7f1 批处理编译零 `error CS`、无异常；`QuadcopterDroneAuthoring.ProbePrefab` 27 项 PASS、`ProbeModel` 26 项 PASS（旋翼枢轴/自转轴/桨盘/URP 材质全部通过）。探针显示云台节点实际名为 `Camera_Lens`（局部 z=0.178），接入计划里“模型用 Camera_Gimbal”的说法需按实际层级复核。报告与备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\QuadcopterDrone\probe-20261024.txt`。下一步按计划步骤 2 处理云台查找与光标副作用。
- 接入步骤 2 完成（2026-10-24）：`DroneCameraController` 优先查找 `Camera_Gimbal`，并兼容现有预制体实际使用的 `Camera_Lens`；两者都缺失时输出错误。`SetActive(bool)` 只切换组件启用状态，生命周期不再更改全局光标。通过 `Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures DroneCameraControllerTests`（临时定义 `UNITY_INCLUDE_TESTS`，默认 Editor 程序集可发现，无需新增 asmdef）运行 EditMode 夹具 9/9；结果：`C:\Users\ASUS\Desktop\陷阱素材文件夹\QuadcopterDrone\step2-unity\20261024-234437-423\editmode-results.xml`。

- 本次只交付美术资产与导入工具，不含飞行、输入、AI 或场景接线；不修改场景、MapForge、陷阱逻辑与玩家代码。
- 素材目录沿用当前账户：`C:/Users/ASUS/Desktop/陷阱素材文件夹/QuadcopterDrone/`（生成/校验脚本、.blend、8 张预览、`validation_report.json`、`model_stats.json`、`HANDOFF.md`、`backups/`）。定稿 FBX 进 `Assets/Models/QuadcopterDrone/`，URP 材质进其 `Materials/` 子目录，预制体 `Assets/Prefabs/QuadcopterDrone.prefab`。
- Unity 侧工具 `Assets/Editor/QuadcopterDroneAuthoring.cs`：`ProbeModel` / `ProbePrefab`（写 `%TEMP%/alpha-striker-unity/quadcopter-drone/probe-*.json`）、`CreateDronePrefabMenu`（生成 URP 材质 + 预制体）。菜单在 `Tools/塔防/`。
- 规格：24,392 三角面、128 网格、14 空节点、6 材质；包围盒 0.6264 × 0.1923 × 0.6264 m；Unity 局部空间 +Y 向上、+Z 机头、+X 右；滑橇底面 Y = -0.078。
- 层级契约：`Drone_Root → Body / Arm_FL|FR|RL|RR → Rotor_FL|FR|RL|RR`，另有 `Camera_Gimbal`(0, 0.010, 0.178)、`Payload_Mount`(0, -0.070, -0.020)、`LandingGear_L|R`(∓0.072, -0.060, 0)。旋翼枢轴局部位置 (±0.200, 0.030, ±0.200)，**自转轴 = 枢轴局部 +Y**（`Rotate(Vector3.up, degPerSec*dt, Space.Self)`），实测自转 30° 桨盘漂移 0.3 mm；桨叶半径 0.095、保护环内径 0.108。
- 三条踩过的坑，改模型时别回退：① `export_scene.fbx` 的 `bake_space_transform` 必须为 **False**（为 True 时嵌套空节点逐层叠加坐标系转换，枢轴轴向与桨叶全部错乱）；② 导出前必须把网格顶点烘焙进父级空间（`build_quadcopter_drone.py` 的 flattening 段），FBX 无 parent-inverse 概念；③ Blender 空节点帧会带上 Z-up→Y-up 转换，故旋翼枢轴加 `ROTOR_PIVOT_FIX = +90° X` 补偿后在 Unity 才轴对齐；`.fbx.meta` 的 `bakeAxisConversion` 保持 0（设 1 会把整机转 180°、机头变成 -Z）。
- 材质：FBX 自带内置 Standard（URP 下品红）。`CreateDronePrefabMenu` 生成 `Assets/Models/QuadcopterDrone/Materials/M_DR_*.mat`（URP/Lit）并覆盖 6 个槽位后存预制体；改配色改该脚本 `Palette` 再重跑，不要手改 FBX 材质。`addColliders` 保持 0，碰撞体应在组件层加。
- 验证：Blender 复导入 39/39（`result: PASS`）、Unity 模型探针 PASS、Unity 预制体探针 PASS（6 材质均为 `Universal Render Pipeline/Lit`）；工程内 FBX 与校验件 SHA256 一致。**未做** Unity 实机飞行验收、PlayMode 测试、贴图/UV 图集与 LOD。

- 后续游戏接入路线见 `Assets/Editor/DroneControlIntegrationGuide.md`。当前仍是隔离的控制原型，尚未接入玩家控制或场景流程；步骤 2 已使 `DroneCameraController` 优先查找 `Camera_Gimbal` 并兼容预制体中的 `Camera_Lens`，同时取消自身的光标控制。`DroneCameraControllerTests` 现在可通过现有 EditMode runner 发现并已 9/9 通过。


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

## 哨戒机枪索敌反馈（2026-10-23）
- `AutoSentryTurret` 增加默认开启的 `targetingFeedbackEnabled` Inspector 开关与 `TargetAcquired` 事件；只在无有效目标切换为有目标时发出，目标仍有效时更换目标不会重复发出。关闭开关时事件不触发。
- `SentryLaserSight` 订阅目标事件并在索敌时播放 0.1 秒亮、0.1 秒灭、0.1 秒亮，结束后持续亮；保留原有射线截断与炮塔禁用隐藏行为，`BeginTargetingPulse` / `TickTargetingPulse` 支持确定性采样。
- 新增 `SentryTargetingFeedback`：可配置 AudioClip，缺省生成 48 kHz、精确 0.1 秒的 880 Hz 滴声；序列为立即播放、0.1 秒停止、0.2 秒再次播放、0.3 秒结束。反馈由炮塔根 Inspector 开关控制。
- `SentryMachineGunAuthoring.CreateAssetsBatch` 自动挂载新音频反馈组件并在探针中校验；生成的 `Assets/Resources/SentryMachineGun.prefab` 已包含该组件，不要手工修改生成 prefab。
- 验证：`SentryMachineGunAuthoring.CreateAssetsBatch` 成功；`Assets/Editor/RunSentryMachineGunTests.ps1 -ExpectedCases 13` 结果 13/13 通过；`SentryMachineGunStage5Validation.Run` 成功，零编译错误/异常。测试覆盖首次锁定、目标保持不重复、目标消失后重新获得、激光脉冲边界、两次滴声启动与开关关闭。

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

## 天花板陷阱系统首版（2026-10-25）
- `TrapMountType` 保留 `Ground=0`、`Wall=1`，新增 `Ceiling=2`；`TrapDefinition`/`TrapPlacementGrid` 统一使用三类匹配，Ceiling 与 `WalkableFloorTrap` 互斥。菜单筛选为“全部／地面／墙面／天花板”，类型标签和失败提示走 `TrapMountTypeExtensions`。
- `TrapPlacementGrid` 的贴面几何统一使用本地 XY；天花板网格的本地 +Z 指向下方。`CeilingTrapGridAuthoring` 只接受静态、水平、正缩放的 `BoxCollider`，按格中心与内缩四角检查支撑；同类占用仍冲突，Ceiling/Ground/Wall 的不同类别和支撑体互不阻挡。配置后调用 `Physics.SyncTransforms()`，避免 Play Mode 同帧采样关闭有效格。
- MapForge 字段为对象级 `unity.ceilingTrapGrids[]`：`id`、`origin`、`rotation`、`columns`、`rows`、`cellSize`、`surfaceOffset`、`autoFit`、`openCells`。前端位于 `C:\MapForge\wwwroot\ceiling-traps.js`，入口为“＋ 天花板与陷阱网格”和选中板块 Inspector 的“为底面添加／刷新网格”；服务端 `MapValidation.cs`、`map-format.schema.json`、Unity DTO/导入/实时同步均已同步。旧地图缺字段时按空数组处理。
- 灰盒资源由 `Assets/Editor/CeilingTrapTestPieceAuthoring.cs` 生成到 `Assets/Resources/CeilingTrapTest/`，定义 ID 为 `ceiling_test`，无攻击行为；预览入口为 `CeilingTrapSystemValidation.GenerateAndPreview`。中间态、截图、日志和 NUnit XML 在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\CeilingTrapSystem\`。
- 验证证据：`Assets/Editor/RunCeilingTrapPlacementTests.ps1 -WithGraphics` 使用 Unity `6000.6.7f1`，`CeilingTrapPlacementTests` **17/17 通过、0 失败、0 跳过**；最新结果目录为 `C:\Users\ASUS\Desktop\陷阱素材文件夹\CeilingTrapSystem\20261025-224737-640\`。Node 回归 `npm test` 为 25 通过、0 失败、4 跳过；MapForge API `tests/api.test.js` 在 `http://127.0.0.1:5097` 下 1/1 通过；浏览器 `tests/browser.mjs` 全部 PASS 且无未捕获异常；MapForge 编译 0 警告/0 错误；Unity 定向快速编译 0 错误，仅有既有 `MapForgeTrigger` 弃用警告。
- 主场景 `THREE_ROUTE_MERGE_MAP` 已加入 MapForge JSON 对应对象 `ceiling_test_panel_20261025` 和 `CeilingTrapGrid_bottom`；通过 `CeilingTrapGridAuthoring.AddTestPanelToMainScene` 添加。全量导入曾移除 24 个手工墙面网格，随后恢复导入前备份并仅添加测试板；静态比较确认原 24 个网格、`Enemy Spawn Flying` 和场景 GUID 都保留。主场景 diff 最终只增量加对象与几何，测试板材质在 `Assets/Resources/CeilingTrapTest/CeilingPanel.mat`。
- 首版限制：只支持水平盒体底面；倾斜、曲面、动态支撑、负缩放拒绝创建。主地图已做静态场景接入，尚未做主地图 Play Mode 实机放置/战斗验收；MapForge 增量同步入口已接线，但尚未对主地图执行人工 Send to Unity。

## 普通小怪引用回退修复（2026-10-25）
- `Assets/Editor/MapForgeWorldImporter.cs` 的全量导入默认地面出怪预制体已从 `Assets/Prefabs/TestMonster.prefab` 改为 `MaskedSoldierAuthoring.PrefabPath`（`Assets/Prefabs/MaskedSoldier.prefab`），避免重新导入地图后恢复测试怪物。
- 已运行 `MaskedSoldierSceneWiring.WireGroundSpawnersBatch` 更新主场景；Unity 批处理日志确认成功且无 C# 编译错误/异常。主场景 `THREE_ROUTE_MERGE_MAP.unity` 中 `TestMonster` 引用为 0，蒙面士兵 GUID 引用为 12；飞行出怪口逻辑未改。

## 墙面陷阱网格恢复（2026-10-25）
- 本次实际打开主场景时墙面网格为 **0**；已用 `WallTrapGridAuthoring.EnsureStarterGridForCurrentMap` 恢复并保存 **24 个墙面网格、1184 个开放格、0 个关闭格、0 个支撑异常**。此数据取代上文 2026-10-24 的历史场景快照。地面/天花板网格实例保留；完整校验中地面/道路另有既有 6/8 个埋入地形格，本次未修改，不应称全地图校验通过。
- 根因链路：`MapForgeWorldImporter.Import` 创建新场景后未重建手工墙格；原位重建虽然可保留墙格对象，却会替换平台 Collider，而原 `AlignPlatformSideGrids` 在尺寸相同时不会重新绑定。现在全量导入和 `RebuildInPlace` 都调用 `WallTrapGridAuthoring.RestoreForScene`，仅处理主地图 `Platform_*` 两侧；重新绑定 Collider 时保留同尺寸网格原掩码。生成前同步物理变换，避免用未同步的平台形状关闭有效格。
- 可重复恢复入口：菜单 `Tools/Tower Defense/Restore Main Map Wall Trap Grids` 或批处理 `WallTrapGridRestoration.Restore`（预览需 `-WithGraphics`）。自动备份后恢复两次，检查幂等性、开放格支撑，并调用项目完整网格校验；预览叠加层不会保存到场景。
- 产物目录：`C:\Users\ASUS\Desktop\陷阱素材文件夹\WallTrapGridRestore\`。修改前原始场景和 `.meta` 在 `backup-20261025-234958\`；`validation.txt` 为场景校验，`wall-grids-restored.png` 为已目视确认的 Unity 实机预览，`restore.log` 含成功退出标记且无 C# 编译错误/异常。需要撤回场景时从该备份复制 `.unity` 回原路径，保留原 `.meta`。
- 回归：`RunEnemyDamageResistanceTests.ps1 -Fixtures WallTrapPlacementTests,CeilingTrapPlacementTests` 使用项目记录的 Unity 版本运行，**25/25 通过、0 失败、0 跳过**（墙面 8，天花板 17）。新增墙面用例覆盖平台 Collider 被替换后重新绑定、手绘关闭格保留及重复创建不增生对象；结果在 `tests\20261025-235202-420\editmode-results.xml`。未做主地图 Play Mode 人工放置验收。

## 高台可见放置网格线（2026-10-26）

- 用户确认需求为在高台显示清晰网格线。主场景原本已有 `TrapPlacementGrid_Platform`：1,088 个开放格、1 m 格距、放置高度 2.02 m；本次保留该掩码，仅给该对象增加 `PlatformTrapGridLines` 组件。
- `Assets/Scripts/TowerDefense/PlatformTrapGridLines.cs` 在编辑器和运行时根据开放格生成一份共享网格，格心四周各退半格构造边界，共用边不重复绘制；线宽 0.035 m，额外抬高 0.012 m，无 Collider。生成的子对象、Mesh、Material 不序列化，禁用组件时清理；重新加载场景时自动重建。
- `MapForgeWorldImporter.CreateTrapPlacementGrid` 与 `TrapPlacementGridEditor.ConfigureGrid` 在生成平台网格时补上该组件。入口 `PlatformTrapGridAuthoring.ApplyToCurrentMap` 自动备份主场景、复用 `TrapGridAuthoring.BuildPlatformMask`、保存场景并生成俯视/近景预览。
- 原始场景与 `.meta` 的已校验备份：`C:\Users\ASUS\Desktop\TrapGridBackups\PlatformGrid-20261026-174516\`。预览与日志：`C:\Users\ASUS\Desktop\TrapGridBackups\PlatformGridOutput\platform-grid-detail.png`、`platform-grids.png`、`visible-grid.log`、`validation.txt`。Unity 按项目版本启动，日志确认 `PLATFORM_GRID_PASS` 和成功退出，无 C# 错误/方法异常；高台 0 埋入、0 悬空、0 无支撑、0 道路重叠。整图原有地面/道路 6/8 个埋入地形格仍保留，未做主地图人工 Play Mode 放置验收。
- 本次 `run-unity-method.ps1` 的 `-NoNewWindow` 启动触发 Mono `System.Console` 无效 handle；使用动态匹配项目版本的 Unity 路径并以 `Start-Process -WindowStyle Hidden`（不重定向标准句柄）启动后编译、校验、预览均成功。未更改公共运行脚本。

## DroneControlSession 测试恢复与收尾（2026-10-26）

- `Assets/Editor/DroneControlSessionTests.cs` 原有 7 个会话用例恢复后，测试类需导入 `TowerDefense` 命名空间；夹具复用飞控的 `Rigidbody`，预先创建旋翼节点，并按 `Drone → Camera_Gimbal → DroneCamera(Camera)` 构建相机层级后再挂相机控制器。菜单私有 setter 用反射注入，teardown 销毁对象后恢复菜单静态状态和光标。
- 首次会话测试 3/7 失败的根因是新建 `PlayerHealth` 未执行 `Awake`，令默认 `CurrentHealth == 0` 被误判死亡；夹具显式初始化后会话测试 **7/7** 通过。与相机夹具联跑 **16/16**（会话 7、相机 9；0 失败、0 跳过）。XML：`C:\Users\ASUS\Desktop\DroneControlSessionChecks\20261026-202314-248\editmode-results.xml` 与 `C:\Users\ASUS\Desktop\DroneControlSessionChecks\20261026-202357-777\editmode-results.xml`。
- 符号关闭编译首次暴露 `FirstPersonPlayerBootstrap.activeDroneSession` 字段未受 `ENABLE_DRONE_CODE` 保护，导致宏关闭时找不到已被排除的 `DroneControlSession`。字段现由同一宏包围；无人机初始化方法已有关闭时空实现，因此宏开启时的运行路径未变。
- 从 `Tests~/Editor/DroneControlSessionTests.cs` 清除了临时 `TODO(确认)`。会话和相机测试源码及 `.meta` 保留在 `Tests~\Editor`，Unity 后续导入不包含测试专用源码；重跑前临时复制到 `Assets/Editor`，测试结束后立即移回。
- Unity `6000.6.7f1` 批处理编译：`ENABLE_DRONE_CODE` 关闭时 0 个 C# 编译错误，开启时也为 0。关闭宏验证使用临时 `ProjectSettings.asset` 快照并在结束时恢复，当前 Standalone 符号仍为 `ENABLE_DRONE_CODE`。日志：`C:\Users\ASUS\Desktop\DroneControlSessionChecks\20261026-step4-symbol-off\unity-compile.log`、`C:\Users\ASUS\Desktop\DroneControlSessionChecks\20261026-step4-symbol-off\unity-compile-symbol-on.log`。未做完整 Player 构建或 Play Mode 实机飞行验收。

## 快速小怪（新怪物种类）与防暴警棍兵模型（2026-10-26）

- 依据：参考图 `C:\Users\ASUS\Desktop\策划\` 的防暴警棍兵三视图 + `关卡设计示例（15波）.docx` 的「普通怪·地面·快速小怪：攻击 8，血量 40，移速快，光属性易伤」。设计意图是体型小、相对不易命中，逼迫手打玩家停下开火（第 2 波起大量使用）。
- **怪物数值唯一来源是 `Assets/Scripts/TowerDefense/MonsterSpecies.cs`**：`MonsterSpecies`（Id / 显示名 / 血量 / 攻击 / 攻击间隔 / 移速 / 模型缩放 / 七类抗性 / 特性说明）+ `MonsterSpeciesCatalog.FastRunner`（`fast_runner`，40 HP、攻击 8、间隔 0.8 s、移速 4、缩放 0.9、光抗 -30%）。改平衡只改这里再重跑生成器，不要在生成器或预制体里另写数字。
- 生成入口 `Assets/Editor/FastRunnerAuthoring.cs`：菜单 `Tools/塔防/快速小怪/生成材质与预制体` = `CreateAssetsBatch`（`Assets/Models/FastRunner/Materials/FR_*.mat` 15 个 URP/Lit 材质 + `Assets/Prefabs/FastRunner.prefab`）；`ProbeModel`（骨骼/警棍权重探针，写 `%TEMP%/alpha-striker-unity/fast-runner/probe.json`）；`Validate` / `ValidateBatch`（12 项断言，写 `validation.json`，失败抛异常）；`RenderPreview`（**需 `-WithGraphics`**，正/侧/背/三分之四 + 两个走路相位，输出桌面素材目录 `FastRunner\unity-previews\`）。
- 预制体结构：根 `FastRunner`（`CapsuleCollider` 1.78 m / r 0.30、`EnemyHealth`、`GroundEnemyCombat`、`MonsterWalkAnimator`，整体缩放 0.9）→ 子节点 `FastRunner_Model`（FBX 实例）。**不要手工加/改这几个组件**，重跑生成器即可；`GroundEnemyCombat` 带 `RequireComponent`，会连带 `MonsterPathFollower`。
- 模型规格：Blender 源 `C:\Users\ASUS\Desktop\陷阱素材文件夹\FastRunner\build_fast_runner.py`（可编辑 `FastRunner.blend`）；身高 1.827 m、身体 7,607 顶点 / 14,708 三角面、警棍独立蒙皮网格 296 顶点 / 560 三角面；21 根骨骼（`Root/Hips/Spine/Chest/Neck/Head` + 两侧 `Shoulder/UpperArm/LowerArm/Hand` + `UpperLeg/LowerLeg/Foot` + `Baton` 挂 `Hand.R`），命名与 `MonsterWalkAnimator` 查找表一致，零缺失。
- **右手持棍**：警棍是 `FastRunner_Baton` 独立对象，权重 100% 在 `Baton` 骨骼上（父级 `Hand.R`），网格在世界 -X 侧，即角色右手；挥棍/摆动随 `Hand.R`。改握持方向只改 `build_baton()` 的 `axis` 与 `hand_center(-1)`。
- 材质要点：面罩 `FR_Visor_Glass` 是 URP 透明（`_Surface=1`、`SrcAlpha/OneMinusSrcAlpha`、`ZWrite=0`、`renderQueue=3000`、关 ShadowCaster），`_BaseColor` 的 **alpha 必须是 0.22 左右**，否则脸部被完全遮住（本轮踩过：alpha 默认 1，Unity 预览里只见一块黑板）。改配色改 `FastRunnerAuthoring.Palette` 再重跑。
- 一键进出怪口（已被下面的 50/50 混刷取代，保留作历史）：菜单 `Tools/塔防/快速小怪/写入选中出怪口的第1-3波` 原先把选中出怪口整波改成快速小怪；现在同名入口已改为写 50% 混刷。
- 验证：`Assets/Editor/FastRunnerTests.cs` 6/6 通过（数值表与文档一致、预制体血量/抗性/攻击接线、程序化步态骨骼齐全、警棍独立网格且挂 `Hand.R`、体型小于普通小怪）；Unity `6000.6.7f1`，结果 `C:\Users\ASUS\Desktop\陷阱素材文件夹\FastRunner\editmode\20261026-221746-892\editmode-results.xml`。`ValidateBatch` 12 项 PASS、`RenderPreview` 出 6 张图并目视确认。
- 未做：PlayMode 实机战斗/追击验收、LOD、UV 图集与贴图、受击/死亡表现、`MonsterSpecies` 尚未接入 HUD 或图鉴。

## 快速小怪接入刷怪：一半普通 + 一半快速（2026-10-27）

- 刷怪侧新增「混刷」，不新增怪物种类也不复制出怪口：`EnemySpawnPoint.WaveSpawnSettings` 增加 `alternateEnemyPrefab`（第二种预制体）、`alternateShare`（0–1 占比）、`alternateMoveSpeed`（0 = 沿用出怪口速度）。主预制体仍是 `enemyPrefab`。
- 选怪唯一判据是 `EnemySpawnPoint.UseAlternatePrefab(indexInWave, share)`：按 `floor(i*share)` 的增量判断，整波正好 `floor(总数*share)` 只第二种怪，且在波内均匀交错（0.5 时是 普通/快速 交替，不会扎堆）。`GetAlternateCount` 是同一公式的数量版本，改占比逻辑只改这两处。
- 混刷只作用于地面波次（`MonsterType.Ground`）；飞行出怪口和飞行波次直接忽略，避免把地面快速小怪混进空中。
- **快速小怪的移速靠 `alternateMoveSpeed` 传下去**：`EnemySpawnPoint` 原来给所有怪统一 `moveSpeed`，所以生成器写入 `MonsterSpeciesCatalog.FastRunner.MoveSpeed`（4），不要在场景里手填数字。
- 编辑器入口 `Assets/Editor/FastRunnerAuthoring.cs`：`ApplyMixedWaves(spawn, prefab, share)` 只写这三字段（不改数量），并把旧「整波快速小怪」配置的主预制体还原成出怪口默认预制体；菜单 `Tools/塔防/快速小怪/写入选中出怪口的 50% 快速小怪`；整场景入口菜单 `把主场景地面出怪口改为 50% 普通 + 50% 快速`，批处理 `FastRunnerAuthoring.ApplyMixedWavesToMainScene`（打开并保存构建场景列表第一个场景；**会写场景，先备份**）。
- 主场景已接线：`Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity` 的 3 个地面出怪口 × 3 波 = 9 个波次均为 `alternateShare 0.5` / `alternateMoveSpeed 4`。场景备份：`C:\Users\ASUS\Desktop\陷阱素材文件夹\FastRunner\backups\20261027-141924`。
- 踩过的坑：`EnemySpawnPoint.OnValidate` 会调 `EnsureWaveSpawnData()` 把波次列表补齐到总波数，所以编辑期测试改 `waveSpawnSettings` 时必须同时把 `totalWaves` 固定成期望值，否则数组会被重新撑大。
- 回归：`FastRunnerTests` 新增 5 个用例——交替数量正好一半、0.5 时严格交错、边界与 clamp、真实 `CreateEnemy` 刷 10 只中 5 只快速且移速 4、`ApplyMixedWaves` 还原主预制体且不改波次数量。Unity `6000.6.7f1` 实跑 **11/11 通过、0 失败**，结果 `C:\Users\ASUS\Desktop\陷阱素材文件夹\FastRunner\editmode\20261027-144717-879\editmode-results.xml`；场景写入日志 `%TEMP%\alpha-striker-unity\20261027-141931-fastrunnerauthoring-applymixedwavestomainscene.log`，场景 diff 为纯新增 27 行（9 波 × 3 字段）。
- 未做：PlayMode 实机验收（快速小怪实际追击玩家、被陷阱击杀的实战表现）。

## 测试源码编译隔离

测试专用源码在测试结束后不得继续被 Unity 导入或编译。长期保留的测试源码应放在 Unity 不导入的 `Tests~` 目录或项目外；临时放在 `Assets/` 中的测试源码，应在测试完成后立即移出或删除并清理对应 `.meta`，再启动后续 Unity 编译或打开项目。不应把 `#if UNITY_INCLUDE_TESTS` 单独视为充分隔离。

## 无人机游戏内 V 键切换接入（2026-10-26）

- 根因：玩家 Bootstrap 用 `Resources.Load("QuadcopterDrone")`，原预制体却在 `Assets/Prefabs/`；并且原生成器只保存模型外观，未写入 Rigidbody、飞控和游戏相机。
- 无人机运行时资源现统一为 `Assets/Resources/QuadcopterDrone.prefab`。原 `.prefab` 与 `.meta` 已迁移后由 `QuadcopterDroneAuthoring.CreateDronePrefabMenu` 重建，保留 GUID。生成器现在加 Rigidbody、`QuadcopterFlightController`、`DroneCameraController`、Camera 和 AudioListener；`ProbePrefab` 同步检查这些组件。
- `FirstPersonPlayerBootstrap` 通过 `SceneManager.sceneLoaded` 为复用/生成的玩家接入无人机，并对已有有效会话与 HUD 做幂等检查。无人机和 HUD 随玩家/场景清理；一名玩家仅保留一个无人机与 HUD。
- V 输入由 `DroneControlSession` 持有 `<Keyboard>/v` `InputAction`，组件禁用/销毁时释放。进入 Drone 停用 `PlayerInput`、项目 FPS 移动/武器输入、玩家 Camera/AudioListener；返回时恢复原启用状态，飞控速度清零并冻结。菜单占用光标时拒绝切换并暂停飞控/观察输入；玩家死亡、无人机销毁或场景卸载会回到 Player。
- 回归源码 `Tests~/Editor/DroneGameplayIntegrationTests.cs`（运行时临时复制到 `Assets/Editor`，测试结束立即移回）。Unity `6000.6.7f1` EditMode runner 结果 **2/2 通过**，其中 PlayMode 用例进入主地图实际按 V 往返，并覆盖重复 Bootstrap、HUD 不重复、菜单拦截、玩家死亡及无人机销毁。XML：`C:\Users\ASUS\AppData\Local\Temp\DroneGameplayIntegration\20261026-222914-470\editmode-results.xml`。此后补充的会话销毁清理与生成器组件默认禁用未重新跑 EditMode；最终资源生成通过。
- 最终生成器批处理成功：`C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\20261026-225415-quadcopterdroneauthoring-createdroneprefabmenu.log`；飞控、相机控制器、Camera 和监听器在预制体中初始禁用。Unity 当前启用 `ENABLE_DRONE_CODE`；关闭该符号的独立编译与 Standalone Player 构建尚未执行，其他含玩家场景也未逐个 PlayMode 验收。

## 无人机相机俯视修复（2026-10-27）

- 根因：模型原有 `Camera_Gimbal` 的本地 X 轴不是相机俯仰轴。预制体探针在原挂点写入本地 +30° 后，世界视线 `forward.y` 实测为 `+0.8660`（约向上 60°），因此限制值/鼠标输入本身不能修正其基准轴向。
- 修复：`DroneCameraController.Awake` 优先寻找独立 `DroneCameraPivot`；生成器在模型镜头位置建立该节点，直接挂载 Camera / AudioListener，并写入本地 +30° 初始俯角、85°下俯限位及序列化引用。旧 `Camera_Gimbal` / `Camera_Lens` 仍作为兼容回退。
- 验收：Unity `6000.6.7f1` 生成器批处理成功；最终 `QuadcopterDroneAuthoring.ProbePrefab` 报告 `result=PASS`，相机位于模型镜头位置（offset 0）、`forward.y=-0.5000`、俯视 30°、下俯限位 85°。
- 回归测试：为 `Tests~/Editor/DroneCameraControllerTests.cs` 加入默认朝下与独立枢轴优先用例，并更新俯仰预期。第一次运行暴露测试夹具再次调用 `Awake` 时没有清空已缓存枢轴，测试代码已修正。后续重跑被另一项目已运行的 Unity Editor 拒绝；没有终止该实例，修正后的 EditMode 全套仍待复验。临时复制到 `Assets/Editor` 的测试源码及 `.meta` 已在 runner 结束后清理。

## 贝蒂7号跳雷地面陷阱（2026-10-27）
- 放置预览攻击隔离：`TrapPlacementController.RebuildPreview` 必须停用预览层级里的全部 `Betty7JumpMineTrap`（含未激活子物体）；此前遗漏该类型，导致预览显示时 `LateUpdate → Tick` 也会侦测、发射和结算真实伤害。沿用其他陷阱的组件停用方式，`Tick` 已有 `isActiveAndEnabled` 门控，正式放置仍使用原预制体的启用组件。`Tests~/Editor/Betty7JumpMineSlotTests.cs` 新增真实预制体预览显示/隐藏/再次显示不攻击、不伤害的回归，Unity EditMode **13/13 通过、无编译错误**：`C:\Users\ASUS\Desktop\陷阱素材文件夹\Betty7\preview-tests\20261027-202018-106\editmode-results.xml`。临时测试源码及 `.meta` 已清理；未做手动 PlayMode 放置验收。
- 依据参考图（四角跳雷 ×4 + 中央侦测器 + 低矮装甲底座，即 `贝蒂7号 / 地面陷阱`）新建的可行走地面陷阱。模型脚本 `C:\Users\ASUS\Desktop\陷阱素材文件夹\Betty7\build_betty7.py`（Blender 5.2 无头运行，自动备份 .blend 并渲染 6 张预览），定稿 FBX 在 `Assets/Models/Betty7/Betty7.fbx`。
- 几何契约（Blender 名 → Unity 名字驱动，改名字会断线）：`Betty7_Base`（底座+四角插槽+橙色警示条+中央基座）、`Detector_Dome`（青色发光半球）、`Mine_01..Mine_04`（四角跳雷筒，静止时坐进插槽、沿 +Z 竖直上跳）。整机 2.0×2.0 m、高约 0.51 m，底面在 y=0。
- 生成链（不要手改生成物）：`Assets/Editor/Betty7TrapAuthoring.cs` —— `CreateAssets`（菜单 `Tools/塔防/接入贝蒂7号陷阱预制体`）从 FBX 搭 `Assets/Prefabs/Betty7.prefab`（根 `Betty7` 挂 `TrapInstance` + `Betty7JumpMineTrap`，跳雷统一放进 `Mines` 空节点），按 Blender 材质名生成 `Assets/Models/Betty7/Materials/*.mat`（URP/Lit，橙色与青色带自发光）并写入 `Assets/Resources/Betty7.asset`（trapId `betty7`、显示名 `贝蒂7号`、2×2、`walkableFloorTrap`、Ground、首版价 60）。`ProbePrefab` 打印层级/材质/shader，`RenderPreview`（需 `-WithGraphics`）出 `preview_unity_hero/top/launch.png`。
- 运行时 `Assets/Scripts/TowerDefense/Betty7JumpMineTrap.cs`：**每个跳雷槽位各自一套状态机** `Ready→Arming→Launching→Detonating→Recovering→Cooldown`，装填计时（`cooldownSeconds`）只属于刚起爆的那一颗，其余槽位保持各自的 `Ready`/冷却。开火是"方向择雷"：先按水平距离取 `detectRadius` 内最近的地面怪，再在**已装填**的槽位里选朝向最贴合该怪方位（`Vector2.Dot` 最大）的一颗，方位相同再比到怪的水平距离；同一时间**只允许一颗起爆**（`IsAnyMineEngaged` 门控），所以四角跳雷是轮流启用而不是齐爆。可测入口 `Tick(float)`，`State` 是所有槽位的聚合态（最活跃的优先），另有 `GetMineState/IsMineReady/GetMineReloadRemaining/GetMineDetonationCount/LastLaunchedMineIndex/LastLaunchDirection`。侦测与伤害都用水平距离，**只打地面怪**（`MonsterType.Flying` 优先、`MonsterPathFollower.IsFlying` 兜底），与地刺同一套判据；起爆中心是**被选中那颗跳雷自己的角位**（`MineBlastCenter`）而不是陷阱中心，伤害由 `EnemyHealth.TakeDamage(damage, TrapDamageType.Explosive)` 结算，**不依赖 Collider**（与导弹一致，所以不用给模型补物理体）。改择雷规则只改 `SelectMineForTarget`，不要把方向判断复制到别处。
- 起爆特效是运行时自建：四发加法亮球 + 点光，`Destroy` 在非 PlayMode 下自动换成 `DestroyImmediate`（否则编辑期测试/预览会刷 `Destroy may not be called from edit mode` 错误）。
- 验证：回归用例 `Tests~\Editor\Betty7JumpMineSlotTests.cs`（**12 例**，不进 `Assets/`，运行时临时复制到 `Assets/Editor` 后用 `Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures Betty7JumpMineSlotTests` 跑）覆盖：+Z/−Z/+X 三个方位各选对应侧的跳雷、同时只有一颗启用、单次发射只起爆一次、只有起爆的那颗进入装填、两个槽位的剩余装填时间差等于发射间隔、装填完毕后同一颗能再次发射、飞行怪不触发、侦测圈内但爆破圈外不掉血、跳雷回插槽，以及**真实 `Assets/Prefabs/Betty7.prefab` 四个 `mineRoots` 全部接线**。Unity `6000.6.7f1` EditMode 结果 **12/12 通过、0 失败、0 跳过、无 `error CS`**：`C:\Users\ASUS\Desktop\陷阱素材文件夹\Betty7\slot-tests\20261027-154704-418\editmode-results.xml`。用例夹具给怪物 100000 血：`EnemyHealth` 死亡时走 `Destroy`，编辑期会报错并留下待销毁对象污染后续 TearDown。早期旧用例（齐爆版 4/4）已归档为同目录 `Betty7JumpMineTrapTests.cs.bak`，仅作历史对照。
- 未做：PlayMode 实机放置/战斗验收、贴图与 UV（纯材质色块）、受击/销毁表现、对飞行怪的任何攻击（设计上刻意不做）。

## 扼喉之灯（天花板等离子陷阱）模型（2026-10-27）

- 依据 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\ThroatLamp_Orthographic_v01_prompt.txt`（三视图设计说明）制作的**天花板**陷阱模型。本轮**只做模型、URP 材质与预制体**：没有攻击/瞄准逻辑，没有 `TrapDefinition`，没有放进任何场景。
- 中间态全部在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\`：`build_throat_lamp.py`（唯一建模脚本）、`ThroatLamp.blend`、`ThroatLamp.fbx`、`previews\`（6 张 Cycles 图）、`unity-previews\`（5 张 URP 图）、`validation_report.json`、`material_parameters.json`、`backups\<时间戳>\`。定稿资源：`Assets/Models/ThroatLamp/ThroatLamp.fbx`、`Assets/Models/ThroatLamp/Materials/TL_*.mat`（8 个）、`Assets/Prefabs/ThroatLamp.prefab`。
- **坐标契约（关键，容易做错）**：天花板网格旋转来自 `CeilingTrapGridAuthoring.TryFit` 的 `LookRotation(-support.up, support.forward)`，即预制体**局部 +Z = 世界向下**；`TrapPlacementGrid.GetTrapWorldRotation` 对挂装面再乘 `definition.LocalRotation`。所以模型必须沿 **+Z 下垂**，Blender 侧因此**沿 −Y 建模**（`(bx,by,bz) → (bx, bz, −by)`）。按地面陷阱的习惯沿 −Y 出模会让装配横向伸出。接玩法时 `TrapDefinition.localRotation` 保持零即可。
- FBX 导出固定 `apply_scale_options="FBX_SCALE_ALL"`、`bake_space_transform=False`、`mesh_smooth_type="FACE"`、`axis_forward="-Z"`、`axis_up="Y"`；Blender 5.x 的 FBX 导出器**没有** `export_animations/export_cameras/export_lights` 参数，用了会直接 `TypeError`。
- 生成链只有一个入口 `Assets/Editor/ThroatLampAuthoring.cs`：`CreateAssetsBatch`（配置 `ModelImporter` + 生成材质 + 建预制体 + 探针；菜单 `Tools/塔防/扼喉之灯/配置导入并生成材质与预制体`）、`ProbePrefabBatch`、`RenderPreviewBatch`（**必须 `-WithGraphics`**，输出到桌面 `unity-previews\`，含两帧预热）。**先建材质再配置导入器**，否则 `AddRemap` 找不到材质、FBX 槽会退回内嵌 Standard（URP 下品红）。
- 预制体层级（生成器按名字前缀分组，**改名会断线**）：根 `ThroatLamp` → `Ceiling Mount`（`Mount_*`，z=0）/ `Yaw Pivot`（本地 z=0.075，`Yaw_*` `Yoke_*`）→ `Pitch Pivot`（本地 z=0.195，世界 z=0.270，`Head_*`）→ `Nozzle` 空节点（本地 z=0.130，世界 z=0.400，朝 +Z）。预制体**只有 Transform/MeshFilter/MeshRenderer 与两个空节点**：无 `TrapInstance`、无 Collider、无脚本，探针会断言这些为空。
- 尺寸与面数：72 个网格、9,616 三角面；包围盒 0.456（宽）× 0.410（下垂）× 0.456 m；安装板直径 0.456 m ≈ 2× 发射筒直径 0.20 m，符合设计稿比例。
- **发射口必须用开口圆环**：`build_throat_lamp.py` 的 `annulus()` 用 bmesh 建双层环（外壁/内壁/两端环面）。早期用实心 `cyl()` 时，最外那圈端面把约束环和白热核心整个遮住，预览只剩一块平板（本轮踩过）。沿 −Y 的正确层序：`Head_Nozzle_Rim`(0.104/0.086) → `Shoulder` → `Bowl`(0.086/0.064) → `Throat`(0.064/0.050) → `Head_Containment_Ring` torus(major 0.057) → `Head_Lens`(r 0.05，弱发光) → `Head_Plasma_Core`(r 0.017，白热)。
- 材质由 `ThroatLampAuthoring.Palette` 统一提供（改配色只改这里再重跑，**不要手改 `.mat`**）。Blender 与 Unity 的金属度/发光强度是**分别调过的两套数**：Unity 侧金属度 0.2–0.6、约束环发光 2.0、核心 5.5。原因：**URP 没有反射探针时金属几乎全黑**，早期 0.85 金属度在批处理预览里整机发黑；`RenderPreviewBatch` 另设 `RenderSettings.ambientMode = AmbientMode.Flat` 打底并在结束时恢复。
- 验收：Blender 侧 10 项结构断言 PASS + 6 张 Cycles 预览；Unity `6000.6.7f1` 批处理 `THROAT_LAMP_ASSETS_PASS`，探针 **24/24 PASS**（pivot 深度 0.075/0.270、Nozzle 世界 0.400 且 forward=(0,0,1)、72 个 renderer、8 个材质全 URP、无 Collider/Rigidbody/脚本/灯光相机、bounds minZ=0 maxZ=0.410），5 张 URP 预览目视确认无品红、发射口为橙红约束环 + 白热核心。日志：`%TEMP%\alpha-striker-unity\20261027-204405-throatlampauthoring-createassetsbatch.log`。
- 未做：攻击/瞄准/伤害、`TrapDefinition` 与 `TrapMountType.Ceiling` 接线、场景放置、PlayMode 验收、贴图与 UV（纯材质色块）、LOD。俯仰/水平旋转与等离子发光动画已在下一节补齐。

### 扼喉之灯：材质收尾与动画（2026-10-27）

- 材质收尾（`CreateOrUpdateMaterials` 内，改配色仍只改 `Palette`）：8 个 `TL_*.mat` 现在显式写 URP 表面开关——`_SpecularHighlights=1`、`_EnvironmentReflections=1`、禁用 `_SPECULARHIGHLIGHTS_OFF` / `_ENVIRONMENTREFLECTIONS_OFF`、`_Cull=Back`、`_AlphaClip=0`、`RenderType=Opaque`，等离子/镜片按玻璃金属读，不再像死板涂料。
- 动画由同一个生成器产出，资产在 `Assets/Models/ThroatLamp/Animations/`：`TL_IdleScan.anim`（6 s 循环）、`TL_Charge.anim`（1.4 s）、`TL_Discharge.anim`（0.9 s）、`ThroatLamp.controller`。**不要手改这些 `.anim` / `.controller`**：重跑 `CreateAssetsBatch` 会先 `ClearCurves` 再重建，控制器每次整份删除重建。
- 三条 clip 的曲线全部由编辑器按数学函数采样生成（`Sampled(length, f)`，默认 15 Hz，逐关键帧写入数值切线），不依赖 Unity 的自动切线策略（跨版本会变）。写曲线统一走 `AddCurve` / `AddEmission` / `AddLensScale`：
  - 旋转：`Transform.localEulerAnglesRaw.y`（yaw ±26° 扫视）、`.x`（pitch 4°±3.2° 点头、Discharge 时 −7° 后坐）。
  - 发光：`MeshRenderer.material._EmissionColor.r/g/b/a`（基准 约束环 2.0 / 核心 5.5 / 镜片 0.25；Charge 拉到 22 / 5.5 / 0.95，Discharge 再叠 `+12` / `+3` 指数闪光）。
  - 镜片呼吸：`m_LocalScale.x/y/z`（1.0↔1.12）。
- 控制器是**自播放循环**：`Idle →(exitTime 1.0, 0.12 s)→ Charge →(0.05 s)→ Discharge →(0.25 s)→ Idle`，默认态 `Idle`。另有 `Charge` / `Fire` 两个 Trigger 的 AnyState 转移留给后续玩法强制起爆；本轮没有任何脚本触发它们，接玩法时优先用这两个 Trigger。
- 预制体根新增唯一组件 `Animator`（`applyRootMotion=false`、`cullingMode=AlwaysAnimate`、`updateMode=Normal`），**仍然没有任何 MonoBehaviour**，`no_scripts` 探针依旧通过。
- 探针从 24 项扩到 36 项，新增 12 项动画断言：`animator_component` / `animator_controller` / `animator_no_root_motion` / `animator_always_animate` / `animation_clips`(3) / `animation_bindings_resolve`（每条 curve 的 path 必须能在预制体里解析，`MeshRenderer` 绑定还要真有 renderer）/ `yaw_animated`(>20°) / `pitch_animated`(>4°) / `emission_animated`(≥12 条) / `animation_states`(3) / `idle_loops` / `clip_lengths`。改层级名字会先在这几项炸掉。
- 动画姿态预览：`RenderAnimationPreviewBatch`（菜单 `Tools/塔防/扼喉之灯/渲染动画姿态预览（需图形）`，**必须 `-WithGraphics`**）按 clip 里的数值手工摆三个姿态（yaw ±26°、charge 三倍发光 + 镜片 1.10）渲染到 `unity-previews\anim\`。刻意不用 `AnimationMode` 采样：材质曲线在编辑期采样不可靠，手工摆姿态才能真看到发光变化。
- 验收：Unity `6000.6.7f1` 批处理 `THROAT_LAMP_ASSETS_PASS` + 探针 **36/36 PASS**、无编译错误无异常；静态 5 张与动画 3 张预览均目视确认（头部绕 X 左右摆、mount 不动、charge 帧白热核心与约束环明显变亮）。日志 `%TEMP%\alpha-striker-unity\20261027-210714-throatlampauthoring-createassetsbatch.log`、动画预览 `...\20261027-210742-throatlampauthoring-renderanimationpreviewbatch.log`；预览图 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\unity-previews\anim\`。
- 回滚点：改前把 `ThroatLamp.prefab` 与 `Materials\` 备份到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\backups\anim-20261027-210639\`。
- 仍未验证：Animator 的真实运行（运行时是否生成材质实例、过渡混合手感）只有结构性断言，**未进 PlayMode**；也没接 `TrapDefinition` / 场景放置。
- 顺带修复（阻塞编译）：`Assets/Scripts/TowerDefense/RepairStationTrap.cs` 的 `IsBetter` 用了 Unity 6.7 已升级为错误的 `GetInstanceID()`，改用 `RuntimeHelpers.GetHashCode` 做稳定 tie-break。不修整个工程无法编译。

## 维修站（Repair Station）地面陷阱模型（2026-10-27）

- 依据桌面 `陷阱素材文件夹\RepairStation\维修站设计图.jpg` 与 `RepairStation_Design_v01.txt` 制作。初始交付只有模型；2026-10-27 已补充 URP 材质、Prefab、TrapDefinition 和四臂维修逻辑，接入说明见下节。
- 中间态全部在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\RepairStation\`：`build_repair_station.py`（唯一建模脚本，含材质/导出/统计）、`RepairStation.blend`、`RepairStation.fbx`、`render_previews.py`、`validate_repair_station.py`、5 张 Cycles 预览、`materials.json`（9 个材质的 PBR 参数，供后续建 URP 材质）、`HANDOFF.md`、`build_report.json`、`validation_report.json`。定稿模型：`Assets/Models/RepairStation/RepairStation.fbx`。
- 几何契约：整体 1.902 × 1.902 m（底座八边形 1.90，蓝饰带外凸 1 mm），静止高 1.2495 m，底面 z=0，原点在底座中心；朝前为 Blender -Y（Unity +Z）。34 网格 / 55,564 三角面 / 28,272 顶点 / 9 材质。
- 层级（名字即契约，改名会断层级）：`RepairStation` →（`Base`、`Housing`）与四条臂 `Arm_Front|Right|Back|Left`（空节点，绕 Z 转 0/90/180/270）→ `Shoulder_Yaw` → `Shoulder_Pitch` →（`ShoulderJoint`/`UpperArm`/`Cable_A`）→ `Elbow` →（`ElbowJoint`/`Forearm`/`Cable_B`）→ `Wrist` →（`Torch`、`WeldAxis`）。顶点已按枢轴原点烘进网格，枢轴局部位移归零，接动画直接给旋转即可。
- 四向同构是硬要求：四条臂网格数、臂心半径/高度、包围盒 90° 旋转关系都由校验断言，改一条臂要四臂一起改并重跑校验。
- 校验入口：`blender -b --factory-startup --python validate_repair_station.py`，会把 FBX 重新导入空场景跑 36 项断言，当前 36/36 PASS（尺寸、居中、贴地、四向同构、21 个枢轴、无非单位缩放/镜像、无退化三角面）。
- 预览：`render_previews.py` 出 `preview_three_quarter / front / top / arm_detail / deployed`；`deployed` 是临时旋转枢轴的展臂姿态，**不写回模型**，用来核对枢轴向。
- 踩过的坑：`prim_oct` 造的是实心八边形，用它做“细饰线/凹槽”会整面盖住下层（曾把底座顶面整片染蓝）；这类环形装饰要用 `prim_oct_ring`（八边形环），且环高只有几毫米时 bevel 必须给到 ≤0.001，否则 bevel 会压出退化三角面（校验会报 degenerate）。
- 后续仍未做：焊弧/火花/烟雾粒子与声音、主场景预放置、UV/贴图/LOD。现有陷阱血条直接读取统一 TrapInstance 血量，无需另加血量 UI。

### RepairStation 游戏接入（2026-10-27）

- 运行时：`Assets/Scripts/TowerDefense/RepairStationTrap.cs`；`TrapInstance.Repair(float)` 返回实际恢复值，拒绝非正数/NaN/无穷/已死亡，不触发 Damaged 或 Destroyed。每臂独立 CD=4 秒、每次 25 点，发现有效目标立即回血；换目标、组件开关不重置 CD。时间走 `Time.time` / `deltaTime`，暂停不触发首次维修，也不推进动作。
- 选靶：`TrapPlacementGeometry.TryGetGroundFootprint` / `FindSharedGroundSide` 使用网格占格矩形的世界共边，不用模型 AABB；同高度、跨网格、旋转网格均可，排除斜角、自身、墙面/天花板、未放置、禁用、已死亡、移出网格记录位置的对象和 `TrapPlacementPreview`。优先血量比例最低，再按距离、实例 ID 稳定决胜；共边最长的方向唯一归属，平手前/右/后/左，单站不会用两臂重复修同目标。
- 生成入口：`RepairStationAuthoring.CreateAssetsBatch`（菜单 `Tools/塔防/维修站/生成材质、Prefab 与陷阱定义`）；九种 URP/Lit 材质在 `Assets/Models/RepairStation/Materials/`，参数直接保留于生成器字典。最终资源为 `Assets/Resources/RepairStation.prefab` 与 `Assets/Resources/RepairStation.asset`，菜单自动加载；默认 40 金币、100 血、固定 2×2 米占格，走现有购买/出售链。重复生成保留资产 GUID，已有定义价格不覆盖。
- FBX 的 **实际 Unity 导入** 与初始交接坐标描述不同：空节点本地 Z 坐标翻转、祖先有 X=90°；生成器给 visual 额外 Y=180°，令 Front 朝 Unity +Z。保留原始四臂命名，按实际表面法线绑定前右后左索引；不要按名字数组直接认定 Unity 方向。Blender 参考角 yaw Z=-90 / pitch Y=-52 / elbow Y=-72 / wrist Y=55，对应导入关节本地 Z=-90 / Y=52 / Y=72 / Y=-55。
- 动作：0.3 秒展开 + 0.4 秒停留 + 0.3 秒收回，不缩放网格；枪口仅通过 MaterialPropertyBlock 在工作时显示已有青色自发光，无新增粒子/声音/IK。目标移走、禁用、售出或摧毁时动作复位；放置预览显式禁用维修组件，组件另有真实放置检查。
- 结构校验入口：`RepairStationAuthoring.ValidateBatch`；完整生成+图形预览：`RepairStationAuthoring.GenerateAndPreviewBatch`（运行器带 `-WithGraphics`），独立预览 `RenderPreviewBatch`。三张图、资源探针、购买维修出售集成报告在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\RepairStation\unity-previews\`，备份在同级 `unity-backup-20261027-210633\`。不修改主场景或 MapForge。
- 集成探针已验证：500 金币购买后剩 460，四个目标 40→65，3.99 秒仍 65，4 秒到 90，出售返 20 后 480；预览图已检查四臂工作姿态与 Blender 参考相符。
- 持久测试在 `Tests~/Editor/RepairStationTests.cs`，临时复制至 `Assets/Editor` 后以 `RunEnemyDamageResistanceTests.ps1 -Fixtures RepairStationTests` 执行。必须在 finally 移除临时源码与 `.meta` 再正常编译；Play Mode 测试可能让新版编辑器重序列化 TimeManager.asset，测试前备份并于结束后恢复该文件。
- 验收：Unity 6000.6.7f1 **17/17 PASS，0 失败、0 跳过**（含真实 Play Mode 暂停与恢复）；结果 `C:\Users\ASUS\Desktop\陷阱素材文件夹\RepairStation\unity-tests\20261027-211932-392\editmode-results.xml`。测试源码及 meta 已清理，TimeManager.asset 已还原；之后正常编译/资源校验日志 `unity-previews\final-compile.log`，无 C# 编译错误或方法异常。未做主场景人工键鼠验收或完整 Player 构建。

### 扼喉之灯游戏接入（2026-10-27，覆盖上方美术阶段的未接入说明）

- 生成入口仍为 `ThroatLampAuthoring.CreateAssetsBatch`：生成现有 `Assets/Prefabs/ThroatLamp.prefab`、新增 `Assets/Prefabs/ThroatLampPlasma.prefab` 和 `Assets/Resources/ThroatLamp.asset`。定义 ID=`throat-lamp`，天花板安装、固定 2×2 米占用、默认40金币；安装包围盒来自模型实际尺寸，模型保持原有0.456米宽度，不按占格放大。重复生成保留定义价格和玩法组件的 Inspector 数值。
- `ThroatLampTrap` 负责地面选敌、持续锁定、提前量瞄准及蓄能发射。复用 `MissileAimSolver` / `MissileVelocityTracker`；默认射程18米、弹速20米/秒、首次蓄能1.4秒、两次发射间隔4秒（包含后续蓄能）。目标失效/遮挡取消蓄能，不补发；预测点超出弹体射程时不发射。只允许安装法线朝向的0~85度锥内目标，飞行敌人不参与。
- 坐标：安装根局部+Z映射世界向下，运行时 yaw 绕局部Z、pitch绕局部X。`LateUpdate` 最终写入转轴，确保 Animator 不覆盖自动瞄准；保留原发光、镜片、位移后坐，无目标时原待机扫描。控制器不再自动循环攻击，仅 Charge/Fire 触发攻击，Discharge 自动回 Idle。运行时旋转覆盖原clip的旋转，安装板不动。
- `PlasmaProjectile` 冻结发射方向，逐帧扫掠比较场景障碍和地面敌人身体包围盒（无 Collider 时使用 renderer / 小型回退包围盒）。直接80点电伤害，命中点2米内其他地面敌人40点电伤害，不重复结算、不伤飞行敌人/玩家/陷阱、不附带控制；超出18米直接销毁。弹体模型和 TrailRenderer 引用 URP 材质。
- 菜单、购买、出售、血条沿用原流程。`TrapPlacementController` 显式禁用虚影的 `ThroatLampTrap`，组件自身也拒绝推进带 `TrapPlacementPreview` 的对象，防止预览开火。不改主场景或 MapForge。
- 探针已由“无脚本”改为检查 TrapInstance+ThroatLampTrap、炮口/Animator/弹体引用和2×2米天花板定义，**38/38 PASS**。图形预览入口 `ThroatLampAuthoring.RenderGameplayPreviewBatch`（运行器带 `-WithGraphics`），使用独立预览场景及运行时同一 `AimAt`，生成 `aim-and-charge.png` 与 `plasma-shot.png`，已检查朝向、材质和弹体可见性。
- 持久测试 `Tests~/Editor/ThroatLampTests.cs`，临时复制至 `Assets/Editor` 后跑 `RunEnemyDamageResistanceTests.ps1 -Fixtures ThroatLampTests`。**13/13 PASS，0失败、0跳过**，包括真实 Play Mode 购买扣40、占用与越界拒绝且不扣钱、Animator蓄能/放电、实际命中、血条近射线选靶、出售返20及释放占格；另覆盖3种安装朝向、4秒间隔、提前量/停止/转向、遮挡/失效/重选蓄能、虚影禁攻、超程预测、电抗性、范围边界、无碰撞体与高速扫掠。
- 验收产物统一在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\gameplay-validation\`，13项测试 XML 为 `tests\20261027-214103-149\editmode-results.xml`。临时测试源码与 `.meta` 已移至其 `test-staging\`；Play Mode 造成的 TimeManager 重序列化在结束后恢复。原 Authoring/Prefab/动画备份在 `C:\Users\ASUS\Desktop\陷阱素材文件夹\ThroatLamp\backups\gameplay-20261027-213128\`。未做主场景人工键鼠验收或完整 Player 构建。

## 陷阱升级系统 · 第 1 步基线（2026-10-28）

- 计划：`docs/陷阱升级系统设计.md`（陷阱默认 1 级、最高 4 级，按 `TrapDefinition.TrapId` 共享等级，三级升四级选普通/分支方向）。第 1 步只做接入点核对，**未改功能代码**；完整基线记录在 `docs/陷阱升级系统-第1步基线.md`，计划各步骤已补「核对调整」。
- 代码基线 `main @ a7d82d7`（工作区干净），Unity `6000.6.7f1`。本次未启动 Unity、未编译、未跑测试。
- 七类正式陷阱的 `TrapId`：`auto_sentry_turret` / `ground-spike` / `dual_missile_launcher_loaded` / `flamethrower` / `betty7` / `throat-lamp` / `repair-station`。定义资产都在 `Assets/Resources/*.asset`（贝蒂7号 60 金币，其余 40），菜单 `Resources.LoadAll<TrapDefinition>("")` 只加载这里。
- **`auto_sentry_turret` 与 `dual_missile_launcher_loaded` 在 `Assets/TrapDefinitions/` 还有一份同名定义**，被 `SentryMachineGunTests`、`SentryTrapPrefabRepair`、`MissileLauncherTurretAuthoring`、`EconomyAcceptanceTests` 引用。升级配置以 `TrapId` 为键时要同时扫描两个目录并核对基础价格，不能只认 `Assets/Resources`。
- 血量口径：`TrapInstance.maxHealth`（序列化在预制体上，当前七类均为 100）就是「一级基础血量」；`TrapDefinition` 没有任何血量字段。`TrapInstance.Awake` 直接满血、`Repair` 以 `maxHealth` 封顶，升级改造要同时改这两处。
- **`Assets/Prefabs/DualMissileLauncher.prefab` 没有 `TrapInstance` 组件**，放置时由 `TrapPlacementGrid.TryPlaceTrap`（`TrapPlacementGrid.cs:776`）`AddComponent<TrapInstance>()` 补上，新放置实例的等级/满血逻辑必须覆盖这条分支。
- 伤害/维修的单点读取位置（升级只需在此乘倍率，不在弹体或状态里二次乘）：机枪 `AutoSentryTurret.FireAtTarget`（`damagePerShot` 12）；地刺 `GroundSpikeTrap.HasContact`（`damage` 40）；导弹 `MissileLauncherWeapon.LaunchOneMissile → ConfigureTuning`（`damagePerMissile` 45，溅射按 `damage × splashDamageRatio`）；喷火器 `FlamethrowerTrap.ApplyBurstDamage`（`directDamage` 20 + `FireStatusEffect.Ignite(dps)` 5）；贝蒂7号 `Betty7JumpMineTrap.DamageAround`（`damage` 55，需改为槽位在 `Arming → Launching` 采样）；扼喉之灯 `ThroatLampTrap.Advance → PlasmaProjectile.Initialize`（直击 80 / 溅射 40）；维修站 `RepairStationTrap.Tick → target.Repair(repairAmount)`（25）。
- **陷阱选择菜单现为 Canvas 预制体**：布局与卡片分别保存在 `Assets/Resources/UI/TrapSelectionMenu.prefab` / `TrapSelectionMenuCard.prefab`，由 `TrapSelectionMenuView` 绑定数据；54 张可编辑卡片组成 6×9 网格，保留 5 槽位、安装类别筛选、分页、详情和金币提示。`TrapSelectionMenuAuthoring.CreatePrefabIfMissing` 只创建缺失资产，只有显式 `RebuildPrefab` 才覆盖布局；缩略图生成入口 `GenerateMissingIcons` 只补缺失图片，`RegenerateAllIcons` 才覆盖。正式七类陷阱图标位于 `Assets/Models/TrapMenu/Icons/`，资源定义以 `TrapId` 共用同一 Sprite；`ceiling_test` 是 Resources 下的测试夹具，运行时目录不展示它。中文字体复用 `MenuChinese SDF`，EventSystem 复用现存对象并确保只使用 `InputSystemUIInputModule`。持久结构回归放在 `Tests~/Editor/TrapSelectionMenuCanvasTests.cs`，经济 UI 回归在 `EconomyUITests`；Unity 6000.6.7f1 预制体验证/生成器编译通过，结构与经济 UI 用例 **10/10 PASS**，含筛选、分页、卡片分配和清空。独立主场景投影枪集成用例在 N 键菜单步骤前因 Digit4 放置模式断言失败，故该用例未验证 N/Esc。1920×1080、1366×768、1280×720、2560×1080 预览及完整图标、日志、测试结果位于 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapSelectionMenu\`。
- 光标所有权判据只有 `TrapSelectionMenu.CursorOwned`（含关闭当帧）与 `GameStartMenu.CursorOwned`；新增弹窗的状态必须并入这两处，并同步检查 `FirstPersonController` 与 `FPSPackagePlayerMotion` 两份 `UpdateCursor`。
- 可复用的现成机制：`EconomyManager.SpendReservation.Commit` 已校验 `RunId`（换局后提交失败），`BeginRun` 是唯一新局边界；`ResetRuntimeState`（`SubsystemRegistration`）负责关闭域重载时清状态，升级状态也要在这里清。`TrapSaleService.RefundFor` 只按 `Cost × ratio` 计算，天然不含升级费用，无需改。
- 验证入口（本步未执行）：`Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures <类名>`（临时 `UNITY_INCLUDE_TESTS`、无 `-quit`、必须查 XML 用例数）、`RunSentryMachineGunTests.ps1 -ExpectedCases 18`、`.agents/skills/alpha-striker-unity/scripts/run-unity-method.ps1`；持久测试放 `Tests~/Editor/`（已有 `Betty7JumpMineSlotTests`、`ThroatLampTests`、`RepairStationTests`）。

## 陷阱升级系统 · 第 2 步（2026-10-28）

- 规则核心：`Assets/Scripts/TowerDefense/TrapUpgradeState.cs`（1-4 级、None/Normal/Branch 方向、绝对倍率结构）、`TrapUpgradeCatalog.cs`（可空覆盖项；默认 1.00/1.25/1.50/2.00，维修量可独立覆盖；费用 ceil、至少 1 金币）、`TrapUpgradeService.cs`（按注入的 `Func<Guid>`/RunId 隔离 TrapId 状态，纯报价/状态转换与通知）。`GetQuote` 校验负放置价、同 TrapId 报价基础价冲突；`TryUpgrade` 防跨服务、换局、配置变化、重复报价和事件重入。四级分支需配置 `integrated` 且由调用者提供分支效果适配判定；没有适配器时不可用。
- 生产配置入口 `TrapUpgradeCatalogAuthoring.CreateDefaultIfMissing` 创建 `Assets/Resources/TrapUpgradeCatalog.asset`；若该资产已存在不会覆盖。`ValidateAll` 同时扫描 `Assets/Resources` 与 `Assets/TrapDefinitions`，重复 TrapId 仅允许基础放置价格一致。默认 catalog 含七类正式 TrapId。
- **范围边界：** 这是无扣款的规则核心，禁止把 `TryUpgrade` 直接暴露为正式付费升级入口；钱包新局通知/生命周期/安全扣款只在第 3 步接入。未接入菜单、`TrapInstance`、伤害、维修及战斗运行状态。菜单目前是用户修改中的 Canvas 预制体；后续仅增量改 `TrapSelectionMenuView` / `TrapSelectionMenuCardView`，不得覆盖预制体布局、卡片、字体和图标。
- 持久测试 `Tests~/Editor/TrapUpgradeRulesTests.cs`（49 个用例）；`Assets/Editor/RunTrapUpgradeRulesTests.ps1` 在运行时暂存 `TrapUpgradeRulesTests` 与 Canvas 结构回归类到 `Assets/Editor`，调用既有 `RunEnemyDamageResistanceTests.ps1` 并在 finally 清理源码及 `.meta`。Unity `6000.6.7f1` EditMode **51/51 PASS**（规则 49、Canvas 菜单 2），无跳过；XML `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step2\20261028-164319-622\editmode-results.xml`。日志确认两份定义目录均扫描通过。后续应先单独实施第 3 步支付与换局生命周期，再进入陷阱属性接入。

## 陷阱升级系统 · 第 3 步（2026-10-28）

- 付费入口：`TrapUpgradeRuntime.GetOrCreate()` 懒加载 Resources catalog 并绑定当前钱包；显式依赖可用 `TrapUpgradeService.ForWallet(wallet, catalog, branchAdapter)`，同钱包强制复用唯一服务，配置不一致会拒绝。该服务的 `TryUpgrade` 已付费；原 `new TrapUpgradeService(Func<Guid>, ...)` 仍是无副作用规则测试入口，正式玩法不可使用。
- 延迟确认：`TryReserveUpgrade(quote, out UpgradeReservation, out failure)` 预约可用余额，`Commit(out failure)` 重验 RunId、等级/方向、报价版本、价格、倍率、分支配置/效果可用性；`Dispose` 取消，重复提交无效。成功先静默提交钱包与等级，再刷新所有同 TrapId 场景实例，最后余额/`Upgraded` 通知。失败不扣款、不改等级；事件异常隔离，整个支付与新局通知期间拒绝重入升级。回调中换局会停止旧局通知。
- `EconomyManager.BeginRun` 新增 `RunStarted(Guid)`，在余额通知前同步清升级状态、取消旧预约并刷新仍存在（含 inactive）的陷阱，随后发 `RunReset(Guid)`。`SubsystemRegistration` 显式释放运行服务和钱包绑定，钱包销毁也会释放；正常波次不调用 BeginRun。
- `TrapInstance.UpgradeStateChanged(TrapInstance, TrapUpgradeState)` 仅是本步的刷新钩子，耐久/伤害/维修属性尚未应用，留待第 4 步。刷新查找场景实例，不依赖关闭 scene/domain reload 时可能为空的 ActiveTraps 集合；监听者间也核对 RunId，避免回调换局后收到旧状态。菜单与所有美术预制体保留当前用户修改。
- 持久用例 `Tests~/Editor/TrapUpgradePaymentTests.cs`；运行 `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludePayments` 自动暂存/清理 3 份测试源码和 `.meta`。Unity `6000.6.7f1` EditMode **92/92 PASS**（支付/生命周期 31、规则 49、钱包 10、Canvas 结构 2），无失败/跳过。XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step3\20261028-170058-808\editmode-results.xml`。关闭域/场景重载按 reset 方法与 surviving/inactive 实例的 EditMode 场景模拟验收，未做真实 PlayMode 菜单操作。下一步为第 4 步耐久与有效属性接口。
- 测试隔离后的正常 Unity 编译与 `TrapUpgradeCatalogAuthoring.ValidateAll` 通过：10 definitions / 8 TrapIds / one catalog，两份目录均检查，日志确认成功退出且无 C# 编译错误。日志 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step3\normal-compile.log`。

## 陷阱升级系统 · 第 4 步（2026-10-28）

- `TrapInstance.maxHealth` 仍保存一级基础血量；新增 `BaseMaxHealth`、有效 `MaxHealth`、只读等级/方向/倍率及伤害/维修换算接口。实例等级或倍率变化时按原血量比例调整当前血量；已摧毁实例保持 0。`Repair` 现在封顶到有效最大血量。
- 通过 `Awake`、`OnEnable` 和放置用 `Initialize` 同步本局状态；增加血量初始化标志，避免服务在场景对象 `Awake` 前刷新时把未初始化的 0 误认为摧毁。服务刷新直接传入其权威倍率；运行时懒加载会复用钱包已绑定的 service，保留显式 catalog 配置。
- **范围边界：** 本步只提供 `DamageMultiplier`、`RepairMultiplier`、`MaxHealthMultiplier`、`UpgradeMultipliers` 与 `GetEffectiveDamage(baseDamage)` / `GetEffectiveRepair(baseRepair)` 接口，供第 5/6/7 步读取。七类攻击/维修组件保持一级基值及原有结算逻辑，后续步骤再接入；本步不修改菜单、预制体、地图或模型。
- `Tests~/Editor/TrapUpgradeDurabilityTests.cs` 新增 19 个用例：七类 TrapId 的 1→4 级与同类共享/异类隔离、各级受伤比例、自定义一级血量、三项独立倍率、维修封顶、摧毁后不复活、换局降级、反复启停，以及真实 `TryPlaceTrap` 在预制体有/无 TrapInstance 两种分支的满血继承。`TrapUpgradePaymentTests` 另新增 3 个生命周期用例（现共 34）。运行 `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludeDurability`，Unity EditMode **114/114 PASS**（耐久 19、支付/实例 34、规则 49、钱包 10、Canvas 2），无失败/跳过；四份临时测试源码及 `.meta` 均已清理，持久源码留在 `Tests~/Editor/`。XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step4\20261028-174517-824\editmode-results.xml`。
- 测试清理后，正常 Unity 编译与 `TrapUpgradeCatalogAuthoring.ValidateAll` **通过**，日志确认方法完成且无 C# 编译错误/异常：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step4\normal-compile.log`。未做真实 PlayMode 菜单操作或完整 Player 构建。
- 辅助 `dotnet build C:\MapForge\.validation-build\UnityCompile.csproj` 未通过：其局部导入器编译缺失 `WallTrapGridAuthoring`（两处）和 `MaskedSoldierAuthoring` 类型；该工程不包含本步 TowerDefense 运行时代码。未扩修无关导入器，以后续成功的正式 Unity 编译为本步编译结论。

## 陷阱升级系统 · 第 5 步（2026-10-29）

- `AutoSentryTurret.FireAtTarget` 每发以一级 `damagePerShot` 调用所属 `TrapInstance.GetEffectiveDamage`；缺少 `TrapInstance` 时保留一级伤害。`GroundSpikeTrap.HasContact` 在 `hitThisAttack` 去重后的有效刺击处调用 `trap.GetEffectiveDamage(damage)`。两处均保留原有 `TrapDamageType`、抗性入口、目标筛选、攻击频率与状态机。
- `RepairStationTrap.Tick` 每臂调用 `target.Repair` 前以一级 `repairAmount` 调用 `self.GetEffectiveRepair`；不改写序列化基础值，不重置 `readyAt` 或动画动作。满血封顶、死亡拒绝、共边邻接、四臂独立冷却、目标优先级与暂停规则保持原样。
- 持久测试 `Tests~/Editor/TrapUpgradeCombatIntegrationTests.cs` 共 13 个用例，覆盖机枪/地刺一级与 1.25/1.5/2 倍倍率、同类 TrapId 共享、异类隔离、反复启停、机枪无 TrapInstance 兼容、维修站 25/31.25/37.5/50 点维修、独立维修覆盖、满血封顶、不复活、四臂选靶和升级不提前完成冷却。运行器新增 `-IncludeStep5`，执行时临时暂存并清理测试源码。
- `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludeStep5`（Unity `6000.6.7f1`）EditMode **127/127 PASS**，无失败/跳过；第 5 步 13、耐久 19、支付/实例 34、规则 49、钱包 10、Canvas 2。XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step5\20261029-215904-264\editmode-results.xml`。测试清理后 `TrapUpgradeCatalogAuthoring.ValidateAll` 正常 Unity 编译通过，日志：`C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\20261029-220027-trapupgradecatalogauthoring-validateall.log`。
- 未进行真实 PlayMode 菜单操作或完整 Player 构建；第 6 步延迟/持续伤害接入仍待实施。

## Unity skill 文档更新（2026-10-29）

- `.agents/skills/alpha-striker-unity/SKILL.md` 与 `references/unity-editor.md` 已清理旧用户目录和固定 Unity 版本实例；执行版本以 `ProjectSettings/ProjectVersion.txt` 为准，辅助 `.csproj` 按其实际编入范围解释结果。
- 测试指南补充 `Tests~/Editor/` 持久测试、临时暂存 `.cs` 与 `.meta` 的清理、XML 用例数核对及测试后正常 Unity 编译；不把 `#if UNITY_INCLUDE_TESTS` 当作隔离。
- 新增 `references/trap-upgrades.md`，记录已完成的升级规则、付费预约、换局与有效生命接口，以及七类陷阱战斗属性仍待后续接入的边界。修改升级系统时先核对当前源码与本记忆中的最新阶段记录。
- 多文件 Plan 调整为不延迟首个最小文件写入；验收记录区分已通过、未执行和局部替代验证。

## 陷阱升级系统 · 第 6 步（2026-10-29）

- `MissileLauncherWeapon.LaunchOneMissile` 每发用 `TrapInstance.GetEffectiveDamage(damagePerMissile)` 写入 `MissileProjectile.ConfigureTuning`；放置可能在武器 Awake 后补 `TrapInstance`，因此发射时查找组件，无组件时保留一级行为。溅射仍只乘原 `splashDamageRatio`，在途弹体不读取新等级。
- `FlamethrowerTrap.ApplyBurstDamage` 对每个新命中的目标分别采样有效直伤与燃烧 DPS，再调用原有 Fire 分类入口及 `FireStatusEffect.Ignite`；已施加燃烧保持其 DPS，后续重新点燃沿用「刷新时长 + 取较高 DPS」。升级不清空本轮命中去重集合或重启喷射。
- `Betty7JumpMineTrap.MineSlot.LaunchDamage` 在每槽 `Arming → Launching` 以一级 `damage` 采样有效伤害；`Detonate → DamageAround(center, launchDamage)` 只读该快照。每次升空同时重置该槽 `DamageApplied`，修复原先槽位装填后再次起爆被旧去重标记阻止伤害的问题。升级不调用 `ResetToReady`，不改变各槽进度；下一次升空重新采样。
- `ThroatLampTrap.Advance` 在实际发射时分别采样一级 `directDamage` / `splashDamage` 后写入 `PlasmaProjectile.Initialize`；升级不重启蓄能，旧弹保持采样值。弹体保持原有地面筛选、遮挡、无 Collider 结算、电抗性与直击目标不重复溅射逻辑。
- 新增持久夹具 `Tests~/Editor/TrapUpgradeDelayedDamageTests.cs`（21 例）。`RunTrapUpgradeRulesTests.ps1 -IncludeStep6` 暂存前五步测试、本步测试及 `Betty7JumpMineSlotTests`，并运行既有 `MissileLauncherWeaponTests` / `FireStatusEffectTests`，finally 清理暂存源码及 `.meta`。Unity EditMode **180/180 PASS，0 失败、0 跳过**：本步 21、贝蒂 13、导弹 13、燃烧 6、前五步 127。报告与实际 Unity 版本见 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step6\20261029-221033-883\summary.json`，XML 在同目录 `editmode-results.xml`。
- 已确认 7 份临时测试源码及其 `.meta` 清理完成；随后正常 Unity 编译与 `TrapUpgradeCatalogAuthoring.ValidateAll` 通过，无 C# 编译错误或方法异常，日志归档 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapUpgradeSystem\step6\normal-compile.log`。未进行真实 PlayMode 人工键鼠操作或完整 Player 构建。本步完成七类陷阱普通伤害/维修倍率接入；第 7 步菜单升级及之后步骤仍待实施。升级 skill 参考中的「战斗属性待接入」属于第 4 步历史边界，以本节及当前代码为准。

## 飓风无人机 Hurricane Drone（2026-10-30 新增）

- 资产：`Assets/Models/HurricaneDrone/HurricaneDrone.fbx`（骨骼 + 4 段动画）、`Assets/Models/HurricaneDrone/Materials/M_HD_*.mat`（8 个 URP Lit）、`Assets/Animations/HurricaneDrone/HurricaneDrone.controller`、`Assets/Prefabs/HurricaneDrone.prefab`。尺寸 7.70 x 7.70 x 2.36 m，5662 顶点 / 8 材质槽 / 1 蒙皮网格。
- 骨骼：`Root -> Body -> (Fan_FL/Fan_FR/Fan_BL/Fan_BR, Gun_Yaw -> Gun_Pitch)`；`Muzzle` 空物体挂在 `Gun_Pitch` 下，本地位置 (0,-1.18,2.42)。Blender 里机头朝 -Y，导出后 Unity 机头朝 +Z。
- 动画：`HD_Hover` 1.97s 循环 / `HD_FlyForward` 1.30s 循环 / `HD_BankTurn` 1.57s 循环 / `HD_Attack` 2.97s 单次。开火用**导入设置里的动画事件** `OnGunFire`（0.73/0.87/1.00/1.53/1.67/1.80s），不是场景里手挂的，重新生成预制体不会丢。
- 生成链：Blender 脚本 `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\build_drone_v3.py` -> `HurricaneDroneAuthoring.CreateAll`（材质/导入设置/控制器/预制体，幂等）。菜单 `Tools/塔防/生成飓风无人机预制体`、`Tools/塔防/校验飓风无人机预制体`、`Tools/塔防/预览飓风无人机`。
- 运行时：`Assets/Scripts/TowerDefense/HurricaneDroneUnit.cs` **只管动画状态机**（Hover/FlyForward/BankTurn/Attack + ShotFired 事件），不含位移、选靶、伤害。飞行控制/伤害结算/血量是留给程序员的部分，规格见 `Assets/Editor/HURRICANE_DRONE_HANDOFF.md`。
- 两个已知坑：① `HD_Attack` 的 clip.length 被报成 5.34s（曲线数据其实只到 2.97s，FBX take 尾部有静止），`HurricaneDroneUnit.attackClipSeconds` 按 2.97s 计时收尾，别改成等 Animator 播完；② **本工程 batchmode 离屏渲染只出背景色** —— `Camera.Render()` 与 `RenderPipeline.SubmitRenderRequest` 都一样，现成的 `MissileLauncherTurretPreview` 同样如此，所以自动化视觉验收走 Blender 预览图，不要在 Unity 批处理里等截图。
- 已知坑③ **`Animator.HasState` 在拆机时刻恒为 false**（2026-10-30 实测）：无人机在攻击态被 `Destroy`/`SetActive(false)` 时，`HurricaneDroneEnemy.OnDisable → HurricaneDroneUnit.StopAttack` 会在 Animator 已被禁用之后才跑到（`[DefaultExecutionOrder(-5)]` 让脚本的 OnDisable 排在默认顺序组件之后），此时 `HasState(0, HD_Hover)` 返回 false，虽然控制器里那条状态明明存在，于是打出「Animator 里没有状态 HD_Hover」的假警告。实测数据：激活实例 `hasHover=True / hasFly=True / hasAttack=True`，`SetActive(false)` 之后同一个 Animator 引用 `HasState=False`。`HurricaneDroneUnit.ApplyMode` 现在缓存已验证过的状态（`knownStates`）并在 `!animator.isActiveAndEnabled` 时直接跳过，缺失提示也按状态去重（`warnedMissingStates`），所以拆机时不再刷警告；真缺状态时仍会报一次并带上控制器名（用 `AC_W_MGX5` 换控制器实测：两个缺失状态各报 1 次，第三次同一状态静默）。
- Blender 5.2 相关：`Action` 不再直接暴露 `fcurves`（改插值要走 `preferences.edit.keyframe_new_interpolation_type`）；`bake_anim_use_all_actions=True` 实测导不出任何 take，必须用 NLA 轨道导出；join 时要先建一个原点处的空对象当目标，否则会继承第一个部件的旋转/非等比缩放。
- XM777 特效接入（2026-10-30）：新增 `Assets/Scripts/TowerDefense/XM777VfxPlayback.cs`，由 `XM777Trap.FireAt` 在实际炮口触发 `muzzleEffectPrefab`，由 `Detonate` 在预测落点触发 `impactEffectPrefab`；特效播放完成后自动销毁，放置预览因 `XM777Trap.enabled = false` 不会触发。伤害、待命中记录、3 秒射击间隔和目标筛选未改。
- 特效生成器为 `Assets/Editor/XM777VfxAuthoring.cs`，菜单 `Tools/塔防/生成 XM777 特效预制体`；正式资源在 `Assets/Prefabs/Effects/XM777_Muzzle.prefab`、`XM777_Impact.prefab`，含火焰、烟雾与水平冲击波环。`XM777TrapAuthoring.CreateAssets` 会先生成/刷新特效并把两个预制体引用写入 `Assets/Prefabs/XM777.prefab`。
- XM777 特效中间验证目录为 `C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\vfx\`；EditMode 回归 `42/42 PASS`，XML：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\vfx\tests\20261030-193839-918\editmode-results.xml`。最终接线校验日志：`C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\vfx\normal-validation-final.log`，输出 `XM777_PRESENTATION_PASS`。未进行主场景人工 PlayMode 操作验收。
