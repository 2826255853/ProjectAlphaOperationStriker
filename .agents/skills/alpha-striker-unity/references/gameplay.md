# 玩法入口与不变量

以下整理已有玩法的能力不变量，改代码时**必须沿用，不要另起一套**。里程碑测试记录与未覆盖范围以 `Assets/Editor/PROJECT_MEMORY.md` 为准；不要把某次 EditMode 通过等同于真实 PlayMode、完整构建或人工场景验收。

## 准心、ADS 与第三方相机（2026-11）

- `FirstPersonPlayerBootstrap` 幂等挂载 `HipFireCrosshairUI` / `WeaponScopeSight`；优先通过项目自有桥接组件接入，不直接修改未入库的 KINEMATION 玩家或武器资源。
- ADS 状态复用 `PlayerSprintAimBridge.IsAiming`，准心/瞄准镜同时尊重 `TrapSelectionMenu.CursorOwned` 和 `GameStartMenu.CursorOwned`。切武器、开菜单和禁用控制器都是相关回归边界。
- `HipFireCrosshairUI.LateUpdate`（执行顺序 1000）采样最终移动和落地状态；`WeaponScopeSight.LateUpdate`（1010）在包内相机后处理 FOV。改执行顺序前核对包内移动/相机更新时机。
- FOV 进入/退出混合从当帧实际相机值开始，不写死腰射 FOV；武器匹配复用 `MatchesScopeWeapon`。纯函数测试只覆盖混合和名称匹配，不能证明包内 ADS 动画、切枪或实机观感正确。

## 自爆机器人与出怪配置（2026-11）

- `SelfDestructBotAuthoring.CreateAll` 生成模型接线、材质、动画事件、controller 与 `Resources/SelfDestructBotEnemy.prefab`；`ProbeBatch` 输出 `SELF_DESTRUCT_BOT_PASS`。`SelfDestructBotUnit` 负责动画事件，`SelfDestructBotEnemy` 负责战斗。
- 该敌人不能再挂 `GroundEnemyCombat`，否则两套战斗逻辑争用 follower；保留 `EnemySpawnPoint.CreateEnemy` 对它的排除。自爆经 Destroy 记为 Despawned，不发击杀奖励；不要把它改成普通击杀路径而顺带增加奖励。
- 警示灯由 `SelfDestructBotEnemy.IsPriming` 驱动，生成器按 `M_SDB_BombGlow` 材质绑定。修改模型命名或材质槽时同步生成器筛选；controller 重建可能改变子资产 ID，检查实际引用。
- `DeveloperTestSpawnChainAuthoring.ApplyMainSceneBatch` / `ValidateMainSceneBatch` 配置与校验主场景地面测试波次，保留空中入口。生成前备份 scene 和 meta；逐波敌人写入 `WaveSpawnSettings.enemyPrefab`，不要把所有敌人塞到默认 prefab 字段。
- 修改 EnemySpawnPoint 序列化数据时，基础字段先 Apply，再第二遍重写逐波条目：OnValidate 会按当前 enemiesPerWave 补默认数据。配置探针通过不代表实际刷怪、近战或自爆体验已验收。

### 空中出怪口（飞行怪物）（2026-09 新增）

飞行不是「走得快的怪物」，而是**完全不使用 `MonsterPathGrid` 的另一条移动分支**：

- 怪物原型用 `MonsterType` 区分（`Ground = 0` / `Flying = 1`）。`EnemyInstance.MonsterType` 决定伤害走哪条路径。
- 飞行移动由 `MonsterPathFollower.InitializeFlying(target, fallbackTarget, speed, height, core)` 开启；`IsFlying` 为真时 `TargetPosition` 直接取目标 Y + `flightHeight`，**不做网格寻路、不做地面投影**。
- 出怪口由 `EnemySpawnPoint` 的两个字段控制：`flyingEntrance`（只产飞行怪，且自身会在运行时被抬到空中）与 `entranceAltitude`（世界 Y，默认 12）。生成时另有 `flyingSpawnSpread` 做水平随机散布。
- 相关编辑器入口在 `FlyingSpawnPointAuthoring`：`CreateFlyingPrefab`（生成 `Assets/Resources/FlyingMonster.prefab`）、`AddFlyingSpawnPoint`（在 `MapForgeWorld` 下创建名为 `Enemy Spawn Flying` 的空中出怪口）、`CreateAssets`（两者 + 存场景，可批处理）。
- 空中出怪口的**名字固定为 `Enemy Spawn Flying`**，`AddFlyingSpawnPoint` 靠它判重（已存在就跳过）。改名会造出第二个出怪口。

改飞行怪物时容易踩的两个坑：

1. **不要给飞行怪加 `GroundEnemyCombat`。** 近战追击依赖网格可达性，飞行怪应在战斗中缺席（`FlyingSpawnPointTests.FlyingEntranceSpawnsAirborneMonsters` 会断言 `GroundEnemyCombat == null`）。
2. **不要给飞行怪写死 Prefab 引用。** `EnemySpawnPoint` 在 `enemyPrefab` 为空时用 `Resources.Load<GameObject>("FlyingMonster")` 兜底，所以预制体必须待在 `Assets/Resources/` 下。移动或改名要同步 `FlyingMonsterResourcePath`。

覆盖这两点的编辑期测试：`Assets/Editor/FlyingSpawnPointTests.cs`、`Assets/Editor/GroundEnemyCombatTests.cs`（真机跑法见 [references/unity-editor.md](unity-editor.md)）。

### 飓风无人机 = 第二种飞行怪物（2026-10-30 新增）

无人机（`Assets/Models/HurricaneDrone/HurricaneDrone.fbx`，美术交付说明在 `Assets/Editor/HURRICANE_DRONE_HANDOFF.md`）现在既是表现预制体，也是一套**完整的飞行怪物**：

- 表现件 `Assets/Prefabs/HurricaneDrone.prefab`（`HurricaneDroneUnit` 只管动画）；飞行怪物 `Assets/Resources/HurricaneDroneEnemy.prefab` = 表现件 + `EnemyHealth`(260) + `MonsterPathFollower`(flying, 速度 3.4, 高度 10) + `HurricaneDroneEnemy`。两者都由 `HurricaneDroneAuthoring.CreateAll` 重建，**手改预制体会被覆盖**。
- `Assets/Scripts/TowerDefense/HurricaneDroneEnemy.cs` 是玩法层：按位移/偏航切 `FlyForward`/`BankTurn`/`Hover`（攻击态不抢），索敌后悬停播 `HD_Attack`，`OnGunFire` 动画事件逐发结算（陷阱 `TrapInstance.TakeDamage`、玩家 `PlayerHealth.TakeDamage`），不造弹丸、不套 `MissileAimSolver`。位移仍只有 `InitializeFlying` 一条分支。
- **悬停开火必须 `follower.enabled = false`**：`CombatMovementPaused` 只在 `followingCombatPath` 时生效，对飞行分支无效；而 follower 的 Update 会朝航点转体，与开火朝向互相抢。
- 出怪口由 `Assets/Editor/HurricaneDroneSpawnAuthoring.cs` 幂等维护（场景对象 `Enemy Spawn Flying Drone`，挂在 `MapForgeWorld` 下）。**坑**：`ApplyModifiedProperties` 会触发 `EnemySpawnPoint.OnValidate → EnsureWaveSpawnData`，用当时的 `enemiesPerWave`（默认 10）补默认逐波条目，所以逐波设置要在基础字段之后用第二遍 `SerializedObject` 重写。
- 校验入口：`HurricaneDroneAuthoring.ProbeEnemyPrefabBatch`（`HURRICANE_DRONE_ENEMY_PASS`）、`HurricaneDroneSpawnAuthoring.ValidateBatch`（`HURRICANE_DRONE_SPAWN_PASS`）。当前主场景里没有旧的 `Enemy Spawn Flying`，空中出怪口只有无人机这一个。

### 陷阱菜单锁定玩家视角（2026-09 新增）

打开陷阱选择菜单时玩家视角必须冻结（不能转视角、不能误射），这条靠**一处状态 + 两处尊重**实现：

- 唯一判据是 `TrapSelectionMenu.CursorOwned`（静态属性，等价于「菜单开着 **或** 菜单刚在本帧关闭」）。
- `FirstPersonController.UpdateCursor()` 与 `FPSPackagePlayerMotion.UpdateCursor()` 都要在开头判断 `TrapSelectionMenu.CursorOwned` 并**直接返回**，不再抢回鼠标锁。有**两份** `UpdateCursor`（两个控制器副本），改一份要改两份。
- `TrapSelectionMenu` 自己开菜单时记下 `previousLock` / `previousVisible`，关闭时还原。

### 瞄准陷阱时显示陷阱血条（2026-09 新增）

准星对准某个已放置的陷阱时，屏幕会叠一条血条（陷阱名 + 当前/最大血量 + 血量条）。实现集中在 `Assets/Scripts/TowerDefense/TrapHealthBarUI.cs`：

- 血量数据源是 `TrapInstance.CurrentHealth` / `MaxHealth`（和 `GroundEnemyCombat` 打陷阱用的是同一个字段），血条不自己存血量。
- 选靶有两条路：先用 `Physics.Raycast` 从准星打出去（`FirstPersonController` 的射击也用同一套射线），命中解得 `TrapInstance` 就直接用；打空了就用 `TrapHealthBarUI.FindTrapNearRay` 在 `TrapInstance.ActiveTraps` 里挑离射线最近的（阈值 `AimConeRadius = 0.35`），因为生成的炮塔模型**没有 Collider**，只靠射线检测不到。障碍物会把射线距离截断，被挡住的陷阱不会显示。
- 组件用 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 自建 HUD 对象（和 `WaveStatusUI`、`TrapSelectionMenu` 同一模式），用 IMGUI 画在陷阱世界坐标上方，所以**不需要**改场景、预制体或 Canvas。
- 面板在 `TrapSelectionMenu.CursorOwned` 为真时隐藏（菜单开着不选靶）。`lingerAfterAimLost` 控制视线移开后的粘滞时间，防止准星抖动闪烁。
- 回归测试：`Assets/Editor/TrapHealthBarUITests.cs`（选靶、身后/被遮挡过滤、低血比例），跑法 `-runTests -testPlatform EditMode -testFilter TrapHealthBarUITests`。注意本工程 `Assets/Editor` 的测试类都用 `#if UNITY_INCLUDE_TESTS` 包住，直接 `-executeMethod` 不会执行。

### 双联导弹发射器（对空陷阱）（2026-09 新增）

这是第一个**主动对空**的陷阱，四个脚本各管一段，别把它们合并或绕过：

| 文件 | 管什么 |
| --- | --- |
| `Assets/Scripts/TowerDefense/MissileLauncherTurret.cs` | 两轴旋转 + 选靶（yaw 不限位，pitch 夹在 `MinPitchDegrees = 0` / `MaxPitchDegrees = 75`） |
| `Assets/Scripts/TowerDefense/MissileLauncherWeapon.cs` | 瞄准判定、齐射、装填 |
| `Assets/Scripts/TowerDefense/MissileProjectile.cs` | 弹道飞行、近炸/接触/超程引爆、溅射结算（`MissileGuidanceMode`：`ContinuousLeadRefine` 默认，`FrozenLeadShot` 冻结发射瞬间的拦截点） |
| `Assets/Scripts/TowerDefense/MissileAimSolver.cs` | **唯一的**拦截点预测与速度估计（`MissileVelocityTracker`） |

要点：

- **瞄准的是拦截点，不是怪物当前位置。** 炮塔与导弹都调 `MissileAimSolver.TrySolveIntercept`，所以两边算出的落点必然一致；要改预测逻辑只改这个文件。怪物侧的 `MonsterPathFollower` 不暴露速度，速度估计走 `MissileVelocityTracker`，不要另写一套差分。
- **选靶由三个字段决定**：`targetFilter`（默认 `AirOnly`）、`targetPriority`（默认 `ClosestToTurret`）、`tieBreakDistanceEpsilon`。空中判定统一用 `MissileLauncherTurret.IsAirTarget`（按 `EnemyInstance.MonsterType == Flying`），和空中出怪口是同一套判据。默认只打飞行怪 → 这个陷阱不会因为地面怪路过而开火。
- **仰角超限时退化瞄本体，而不是卡死**：迎面高速目标会把拦截点顶到 75° 以上，此时 `TryGetInterceptPoint` 退回瞄敌人当前位置并把 `IsUsingLead` 置 false；若连本体都在限位之上，炮塔就只继续转、**绝不在被夹住的姿态下开火**。改发射条件时别把这个分支砍掉，回归用例是 `LeadFallbackKeepsFiringWhenElevationExceedsPitchLimit`。
- **导弹不依赖 Collider 结算伤害**：命中判定与溅射在 `MissileProjectile` 里算（用例 `ProjectileAppliesDamageWithoutACollider`）。因此生成的陷阱模型**不需要**加 Collider——这和「陷阱血条要绕射线检测」是同一个原因，不要为了统一而给陷阱模型补物理体。
- **预制体由编辑器脚本生成，别手搓**：`MissileLauncherTurretAuthoring.CreateLauncherPrefab`（菜单 `Tools/塔防/生成导弹发射器预制体`）从 `Assets/Models/Trap_Base_Disc_Dual_Launcher_Loaded.fbx` 搭 `Yaw Pivot` → `Pitch Pivot` → 炮管层级并存成 `Assets/Prefabs/DualMissileLauncher.prefab`，最后把 `Assets/Resources/DualMissileLauncherLoaded.asset` 与 `Assets/TrapDefinitions/DualMissileLauncherLoaded.asset` 的 `prefab` 字段指过去。批处理入口是 `MissileLauncherTurretAuthoring.CreateLauncherPrefabBatch`。改模型路径或预制体路径要同时改这两个 `const string`。
- **尾焰（发射特效）**：`Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx`（Blender 源在桌面「陷阱素材文件夹\DualMissileExhaust」）不要手工往预制体里拖。`MissileLauncherTurretAuthoring.CreateLauncherPrefab` 会自动把两组尾焰挂到 `Pitch Pivot` 下：每组只保留自己那根管子的火焰网格（按 `_01` / `_02` 后缀裁剪，重复名带空格序号也认），喷嘴位置用 `Missile_01/02` 的包围盒尾部反推，朝向从模型自身的 `HotGasCore → OuterFlame` 轴算出，所以 Blender 改轴不用改代码。驱动在 `Assets/Scripts/TowerDefense/MissileExhaustFx.cs`：只有 `MissileLauncherWeapon.State == Firing` 时全强度，`lingerAfterLaunchSeconds` 后淡出，`idleScale = 0` 表示平时完全不显示（`Awake` 会先压到静止态，所以预制体里看到火焰、运行时进场不喷是正常的）。FBX 自带的材质是内置 `Standard`，URP 下是品红，生成器会在 `Assets/Models/DualMissileExhaust/Materials/` 生成并指派 4 个 URP Unlit 加法混合材质，别手改回 FBX 材质。
- 尾焰的看图入口：`MissileExhaustPreview.RenderPreview`（菜单 `Tools/塔防/预览导弹发射器尾焰`），输出到 `%TEMP%\alpha-striker-unity\exhaust-preview`，渲染静止 / 全强度 / 半强度三张；确认挂载数值用 `MissileLauncherTurretAuthoring.ProbeExhaustMount`。回归用例 `Assets/Editor/MissileExhaustFxTests.cs`（两组尾焰各对一根导轨 + 只在开火时可见），跑法 `-runTests -testPlatform EditMode -testFilter MissileExhaustFxTests`。- 需要看图时跑 `MissileLauncherTurretPreview.RenderPreview`（菜单 `Tools/塔防/预览导弹发射器旋转`），它按 6 组 yaw/pitch 渲染预制体并把 PNG 写到 `%TEMP%\alpha-striker-unity\launcher-preview`（它读的是 `DualMissileLauncher.prefab`，找不到会报「请先生成」）。别靠场景视图肉眼判断；要留档的图按规范复制到桌面「陷阱素材文件夹」的对应子目录。
- **玩家武器伤害按武器区分**：玩家侧命中结算在 `Assets/Scripts/FPS/FirstPersonController.cs` 的 `FPSHitscanShooter`（hitscan 桥，KINEMATION 只管射速/弹药/动画）。伤害公式是 `基础伤害 x 距离衰减 x 目标种类缩放 x 弱点倍率`，全部在纯结构体 `WeaponDamageStats`（`Assets/Scripts/FPS/WeaponDamageProfile.cs`）里算，所以不依赖 KINEMATION 预制体即可单测（用例 `Assets/Editor/WeaponDamageProfileTests.cs`，跑法 `-runTests -testPlatform EditMode -testFilter WeaponDamageProfileTests`）。武器分类两条路径：预制体上有 `WeaponDamageProfile` 组件就用手填值（菜单 `Tools/塔防/给武器预制体写入伤害档案` 会把随包武器的默认档案写进 `Assets/KINEMATION/FPSAnimationPack/Prefabs/*.prefab`）；没有组件就按**预制体名字**走 `WeaponDamageStats.DefaultsFor`，先查 `TryGetKnownClass` 的精确表（AK / ASVal / G3 / MX16A4、MPS5 / PDW90、Drake-12 / Striker-V / KXG12、Kar98k / L96X / SVD / Mk14EBR、M1911 / Kolibri / X18 / Viper-357、MGX5、RPG / DGL50），再退回子串启发式。`FPSPlayer_Settings_Utlimate.asset` 里那 20 把武器全部有精确分类——**新增武器要同步补 `TryGetKnownClass`，否则会掉进 Unknown 用回退伤害（默认 25，衰减曲线取 Unknown 档）**。改公式后同步改 `AmmoDisplayUI` 的 HUD 文案（它显示基础伤害与弱点倍率）。
- 生成的预制体落在 `Assets/Prefabs/DualMissileLauncher.prefab`（**不是** `Assets/Resources/`）：它由 `TrapDefinition` 资产直接引用，不需要 `Resources.Load` 兜底；飞行怪物那套才必须待在 `Resources/`。新增陷阱按各自的引用方式选目录，别照抄。
- **逐管装填表现（2026-10-30）**：某根管子发射后该位置**空管**，直到装填完毕才重新出现导弹。实现在 `Assets/Scripts/TowerDefense/MissileLauncherReloadVisual.cs`：按管维护 `tubeLoaded[]`，空管时只关该管导弹的 `Renderer.enabled`，**不要** `SetActive(false)`——`Missile_01/02` 同时是发射口和尾焰喷嘴基准，节点停用会丢位置。`MissileLauncherWeapon` 在 `Awake` 里 `ResolveReloadVisual()`（缺组件就自建，旧预制体/场景实例也能生效），发射时按 `NextTubeIndex()` 清空对应管，`reloadRemaining` 归零那一帧 `FinishReload()` 恢复两管；管序固定 `Missile_01` = 0、`Missile_02` = 1，轮换仍由 `missilesLaunchedInSalvo % 2` 决定。生成器 `WireWeapon` 把组件、`reloadVisual` 与 `tubeVisuals` 写进预制体，重生成后复查这两个引用。看图：`MissileLauncherTurretPreview.RenderReloadStatePreview`（菜单 `Tools/塔防/预览导弹装填状态`，批处理需 `-WithGraphics`）渲染两管满装 / 左管空 / 两管空三张 PNG。
- 回归测试：`Assets/Editor/MissileLauncherTurretTests.cs`（两轴限位、预制体接线的旋转台、原始 FBX 能自建 rig、空中过滤）与 `Assets/Editor/MissileLauncherWeaponTests.cs`（选靶优先级、拦截点领先、齐射两发、装填阻塞、逐管空管与装填恢复、超程引爆、无 Collider 结算）。两者都在 `#if UNITY_INCLUDE_TESTS` 里，跑法 `-runTests -testPlatform EditMode`（可加 `-testFilter MissileLauncher`），或用 `Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures MissileLauncherWeaponTests,MissileLauncherTurretTests`。

### 贝蒂7号跳雷（地面陷阱，2026-10 改为逐槽装填）

`Assets/Scripts/TowerDefense/Betty7JumpMineTrap.cs` 现在是**四个槽位各一套状态机**，不是整机一个状态机：

- 装填时间 `cooldownSeconds` 只属于刚起爆的那一颗；其余槽位保持自己的 `Ready`/冷却，`GetMineReloadRemaining(i)` 读的是单个槽位。
- 开火是"方向择雷"：取 `detectRadius` 内最近的地面怪 → 在已装填槽位里选方位最贴合的那颗（`SelectMineForTarget`，`Vector2.Dot` 最大、同方位再比水平距离）。改方向规则只改这一个函数。
- **同一时间只允许一颗起爆**（`IsAnyMineEngaged` 门控）：这是"启动最靠近的一个可使用的跳雷"的落点，四角跳雷不再齐爆。想恢复齐爆就得重写状态机，别只改一个布尔。
- 起爆中心是**被选中那颗跳雷自己的角位**（`MineBlastCenter`），不是陷阱中心；所以"侦测圈内但爆破圈外"的距离判据要按角位算，写回归用例时别再用陷阱中心。
- `State` 变成所有槽位的聚合态（最活跃优先），`DetonationCount` 是总数，另有 `LastLaunchedMineIndex` / `LastLaunchDirection` / `GetMineState` / `IsMineReady` / `GetMineDetonationCount` 供测试与工具使用。
- 序列化字段名一个都没改，`Assets/Prefabs/Betty7.prefab` 无需重新生成；改字段名会丢掉预制体里已保存的数值。
- 回归用例在 `Tests~\Editor\Betty7JumpMineSlotTests.cs`（12 例，含真实预制体四槽接线），跑法：复制到 `Assets/Editor` 后 `Assets/Editor/RunEnemyDamageResistanceTests.ps1 -Fixtures Betty7JumpMineSlotTests`，跑完删除源码与 `.meta`。夹具的怪物必须给高血量：`EnemyHealth` 死亡时调用 `Destroy`，编辑期会报错并留下待销毁对象污染后续 TearDown。

