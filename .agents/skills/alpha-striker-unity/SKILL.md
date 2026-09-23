---
name: alpha-striker-unity
description: 在 ProjectAlphaOperationStriker（Unity FPS + 塔防）及配套 MapForge 地图编辑器里改代码、改地图、生成或调整模型时使用。涵盖 Unity 批处理执行与校验、MapForge JSON 导入与「Send to Unity」实时同步、怪物路径与陷阱网格联动、空中出怪口（飞行怪物）、双联导弹发射器对空陷阱、陷阱菜单视角锁定与陷阱血条等近期能力、以及图形改动的预览与撤回要求。不适用于与该工程无关的通用 Unity 问题。
---

# Alpha Striker Unity / MapForge

本工程是 Unity 6 的第一人称射击 + 塔防项目，地图由浏览器编辑器 MapForge（`C:\MapEditor`）产出：要么导出 JSON 再由 Unity 编辑器脚本全量导入成场景，要么点「Send to Unity」让编辑器把增量修订直接应用到当前场景（`MapForgeSync/` 文件夹协议）。改地图和改代码是同一件事的两半，通常需要同时动 `C:\Unity Project\ProjectAlphaOperationStriker` 和 `C:\MapEditor`。

工作节奏、只读范围和探索预算见工程根目录的 `AGENTS.md`，本 skill 不重复。这里只讲**这个工程特有的、容易做错的做法**。

## Unity 编辑器版本

- 后续尽量使用 Unity 最新正式发行版（含正式 Update / LTS 版本），不默认使用 Alpha、Beta 或其他预发布版本，也不固定在某个旧版本。
- 每次运行 Unity 前，读取 `ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion`，再核对本机实际安装位置。本机 Unity **不在** Unity Hub 目录，而是 `C:\Program Files\Unity 6000.6.2f1\Editor\Unity.exe`（2026-09-22 核对；`C:\Program Files\Unity\Hub\Editor` 不存在）。版本号随升级不定期变化，因此**任何文档、脚本、`.csproj` 都不要写死版本号**：以项目记录的版本 + `C:\Program Files\Unity *` 下的实际目录为准，脚本会自动探测。旧文档或生成的 `.csproj` 中的历史版本不能作为当前版本依据。
- 日常执行与校验优先使用项目记录的正式版；升级时优先选择最新正式发行版并检查包兼容性。若该版本尚未安装，说明缺失情况，不擅自降级或用预发布版替代；普通代码任务不顺带安装编辑器或迁移项目。
- 批处理命令使用核实后的路径，不硬编码历史版本号；具体执行方式见 [references/unity-editor.md](references/unity-editor.md)。

## 四条工程规范怎么落地

`Assets/代码规范.txt` 要求四件事。它们的可执行做法如下：

1. **只用新版输入系统。** 新增输入代码写 `using UnityEngine.InputSystem`，不要用 `Input.GetKey` / `Input.GetAxis`。工程启用了 `ENABLE_INPUT_SYSTEM`，`Packages/manifest.json` 已带 `com.unity.inputsystem`。
2. **图形类编辑必须能预览、能撤回。** 三类改动各自的撤回手段不同：
   - Blender 建模：脚本常直接 `save_as_mainfile` 覆盖同一个 `.blend`。改之前先把 `.blend` 复制一份到 `C:\Users\Origami\Desktop\陷阱素材文件夹\<陷阱名>\` 下的备份副本，结束时用 `bpy.ops.render.render(write_still=True)` 出 PNG 预览（示例脚本 `C:\Users\Origami\Desktop\陷阱素材文件夹\FlyingMonster\create_flying_monster_plane.py`）。
   - MapForge 改地图：编辑器有 80 步内存内撤销，但**关闭页面即丢失**。改前把 `Assets/mapforge-world.json` 备份或提交，改后重新导出。
   - Unity 改场景：**全量导入**（`MapForgeWorldImporter.Import`）会**覆盖**目标场景文件，所以导入前先 `git stash` 或提交，之后用 `git diff` 回滚。MapForge 的「Send to Unity」增量同步只就地改写变过的对象、且不动 `MapForgeWorld` 之外的手工对象（见[下文](#mapforgesend-to-unity实时同步2026-09-新增)），但同样要先把场景提交一份再动手。
3. **地图不能遮挡怪物前进道路。** 平台/陷阱网格的可放置格必须排除怪物通道格，这条由 `TrapGridAuthoring` 的掩码函数保证——不要手写一套新的格子逻辑，见 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)。
4. **确认地图在 Unity 里真的没有错位。** 不要靠肉眼看场景视图下结论，用 `Tools/Tower Defense/Validate Trap Grids` 输出数值结果。

## 不变量：半格偏移

怪物网格 `MonsterPathGrid` 把格 (x,y) 锚在**格角**，陷阱网格 `TrapPlacementGrid` 锚在**格心**，两者差半个格。所以陷阱网格位置 = 怪物网格位置 + 本地 `(+0.5, 0, +0.5) × cellSize`。

涉及格子坐标换算时用现成实现，别自己推：

- `TrapGridAuthoring.GroundAnchor(monsterGrid)` —— 正确的锚点。
- `TrapGridAuthoring.CellCenter(...)` / `DominantSurfaceTop(...)` —— 格心与落点高度。
- `MonsterPathGrid.CellToWorld` / `TryWorldToCell` —— 怪物侧换算。

`TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 是同一常量的另一份定义，改偏移要两处一起改。

## 文件放置：陷阱制作的中间态文件

陷阱（建模 / 贴图 / 导出 / 预览）过程中的**一切中间态文件**都写在桌面素材文件夹，**不进仓库**：

`C:\Users\Origami\Desktop\陷阱素材文件夹`

- 写什么：Blender 源文件与自动备份（`*.blend` / `*.blend1` / `*.blend2`）、临时导出的 `*.fbx` / `*.obj` / `*.glb`、贴图与烘焙结果、预览图与试渲染、一次性清单/日志，以及只为这次陷阱写的一次性脚本（含 Blender 的 `*.py`）。
- 怎么放：按陷阱名建子目录（如 `陷阱素材文件夹\Flamethrower\`），同一陷阱的源文件、导出件、预览图都放这个子目录里；目录不存在就直接创建，**不要**退而写到仓库根目录或 `Temp`。
- 什么才进仓库：只有**定稿**资源，即模型 `Assets/Models/<TrapName>/*.fbx`（含贴图）、最终预制体与陷阱定义（飞行怪物那套在 `Assets/Resources/`，双联导弹发射器在 `Assets/Prefabs/DualMissileLauncher.prefab` + `Assets/Resources/DualMissileLauncherLoaded.asset` + `Assets/TrapDefinitions/DualMissileLauncherLoaded.asset`），以及导入生成的材质库 `Assets/MapForgeMaterials/<场景名哈希>/`（生成物但被跟踪，见 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)）。已有 `const string` 引用的路径（如 `FlyingSpawnPointAuthoring.FlyingModelPath`、`MissileLauncherTurretAuthoring.LauncherModelPath`）不要顺手搬家。
- 汇报时给出该文件夹下的完整绝对路径，用户要能直接点开看预览图。
- 与第 2 条规范的配合：修改共用 `.blend` 前先把副本备份到该文件夹的对应子目录，再用 `bpy.ops.render.render(write_still=True)` 把预览图输出到同一子目录。

## 近期改动的约定（2026-09 新增）

以下是近期新加的能力（2026-09），改代码时**必须沿用，不要另起一套**。

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

覆盖这两点的编辑期测试：`Assets/Editor/FlyingSpawnPointTests.cs`、`Assets/Editor/GroundEnemyCombatTests.cs`（真机跑法见 [references/unity-editor.md](references/unity-editor.md)）。

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
- 回归测试：`Assets/Editor/MissileLauncherTurretTests.cs`（两轴限位、预制体接线的旋转台、原始 FBX 能自建 rig、空中过滤）与 `Assets/Editor/MissileLauncherWeaponTests.cs`（选靶优先级、拦截点领先、齐射两发、装填阻塞、超程引爆、无 Collider 结算）。两者都在 `#if UNITY_INCLUDE_TESTS` 里，跑法 `-runTests -testPlatform EditMode`（可加 `-testFilter MissileLauncher`）。

### MapForge「Send to Unity」实时同步（2026-09 新增）

除了一次性导入，现在还多了一条**增量同步**通道：MapForge 里点「Send to Unity」，Unity 编辑器自己把改动应用到**当前打开的场景**上。

- 协议两边各一份常量：MapForge 的 `C:\MapEditor\MapForgeSync.cs`（`MapForgeSync.Protocol`，另有浏览器侧 `C:\MapEditor\wwwroot\unity-sync.js`、配置 `C:\MapEditor\mapforge-sync.json`）与 Unity 的 `Assets/Editor/MapForgeLiveSync.cs`（`MapForgeLiveSync.Protocol`）。**改协议要两边一起改**，版本号是握手的依据。
- 传输靠文件夹而不是网络：MapForge 写 `<工程>/MapForgeSync/mail/pending.json`，Unity 侧每 2 秒轮询一次（`PollSeconds`，心跳 `HeartbeatSeconds = 15`），应用后把结果写回同目录（`revision.txt`、`status.json`、`applied.txt` / `applied.json`），浏览器据此汇报成败。
- 增量修订只带「这次真的改过的对象」，Unity 就地把它们重写：新增的建、改过的原地改写、删掉的移除。只有 MapForge 要求时才整图重建（首次发送、地图改名、`gameplay`/场景级映射变了、或改动量过大）；重建也是**原地重建**场景资产，所以 scene 的路径、GUID 以及地图之外的手工对象都保住。整图修订的 world 文件先落到 `Assets/MapForgeSync/sync-world.json` 再交给导入器。
- **只动 `MapForgeWorld` 生成的层级**，这就是它和 `MapForgeWorldImporter.Import` 的关键区别：手工加的空中出怪口等对象**不会**像全量导入那样被删掉。保留机制在 `Assets/Editor/MapForgeWorldImporter.Authored.cs`（`MapForgeWorldImporter` 现在是 **partial**，两个文件都要在），它把上次生成过的直接子对象名记在 **EditorPrefs**（机器本地、不进版本库），据此把工程自制的对象搬出去再搬回来。
- 手动/批处理入口：菜单 `MapForge/应用待同步版本 (Apply pending revision)` = `MapForgeLiveSync.ApplyPendingFromMenu`，菜单 `MapForge/下次发送时完整重建 (Request full rebuild)` = `MapForgeLiveSync.RequestFullRebuild`；批处理直接调 `MapForgeLiveSync.ApplyPending()`（public，无待同步修订时返回 null，不算失败）。
- 两侧各自的测试：浏览器/服务端在 `C:\MapEditor\tests\unity-sync.test.js`（`node --test`）；Unity 侧没有独立用例，改完先跑 `dotnet build "C:\MapEditor\.validation-build\UnityCompile.csproj"` 做编译检查，再手动点一次「Send to Unity」看 `status.json` 的回执。
- 新增按 `id` 关联的运行时组件也是这批改动的一部分：`Assets/Scripts/MapForgePathNode.cs`（`MapForgePathNode.All` / `Find(objectId)`，`role` / `waitTime` / `links`）与 `Assets/Scripts/MapForgeTrigger.cs`（`MapForgeTrigger.Fired` 事件 + `FindTarget(objectId)`，支持 `spawn/damage/goal/message/enable/disable`）。导入时它们由 `MapForgeSceneOrganization` 从 JSON 的 `unity.pathNode` / `unity.trigger` 段建立并做合法性校验；改 JSON 键名要同步这两处。

## 路由

- 要在 Unity 里跑编辑器脚本、批处理导入或校验，读 [references/unity-editor.md](references/unity-editor.md)，并优先用 `scripts/run-unity-method.ps1`，因为 Unity 的退出码**不能**用来判断成败。
- 要改地图、改 MapForge 导出格式、或调整格子/路径联动，读 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)。
- 两边都要动的地图改动，有两条路：日常改动走**实时同步**（MapForge 点「Send to Unity」→ 编辑器轮询应用），整图重排或换了地图名走**导入**（导出 JSON → MapForgeWorldImporter.ImportDefault → Validate Trap Grids）。两条路都会写场景文件，动手前先提交。
- 改空中出怪口、飞行怪物移动、或陷阱菜单的视角/准星锁定，读上面的[近期改动的约定](#近期改动的约定2026-09-新增)；这几处都各有一份以上的复制实现，别只改一处。
- 改双联导弹发射器（炮塔两轴、拦截点预测、齐射/装填、对空过滤、预制体生成），读上面的[双联导弹发射器](#双联导弹发射器对空陷阱2026-09-新增)。
- 改「Send to Unity」实时同步、MapForgeSync/ 目录或 MapForgeWorldImporter.Authored.cs 的手工对象保留，读上面的[MapForge「Send to Unity」实时同步](#mapforgesend-to-unity实时同步2026-09-新增)。
- 改陷阱血条显示（准星选靶、遮挡判定、HUD 样式），读上面的[瞄准陷阱时显示陷阱血条](#瞄准陷阱时显示陷阱血条2026-09-新增)。

## 已知会造成误判的情况

- `Assets/KINEMATION`、`Assets/TextMesh Pro` 被 `.gitignore` 排除。缺失时 C# 会报缺类型，属于预期，不要为此反复排查。
- 根目录的 `*.log`、`*.blend1`、`*.csproj` 都被忽略；`Assets/Models/*.blend` 和 `.fbx` 是被跟踪的。
- 仓库根目录的中间态文件（`/*.py`、`/*.blend`、`/*.blend1`、`/*.blend2`、`/*.png`、`/*.fbx`）已被 `.gitignore` 忽略：陷阱中间态按规范放在桌面「陷阱素材文件夹」，在仓库根目录找不到它们是**预期**，不要用 `git add -f` 硬塞回去。
- `Assets/FlyingMonsterPlane_preview.png` 这类预览图已迁出仓库（GUID 无任何引用）；要预览图去桌面「陷阱素材文件夹」对应子目录拿。
- 同一时刻只允许一个 Unity 实例持有 `Library/`。开编辑器时批处理会卡住或失败，先确认没有 Unity 在跑。
- `MapForgeSync/` 整个目录被 `.gitignore` 忽略，里面是每次同步都会改写的运行时状态（`mail/pending.json`、`revision.txt`、`status.json`）：它出现在工作区是**预期**，不要提交、也不要为它回滚场景。
- `Assets/Editor` 的编辑期测试类（`FlyingSpawnPointTests`、`GroundEnemyCombatTests`、`TrapHealthBarUITests`、`MissileLauncherTurretTests`、`MissileLauncherWeaponTests`）都被 `#if UNITY_INCLUDE_TESTS` 包住：`-executeMethod` 不会执行它们，必须用 `-runTests`；只想验证编译就走 `dotnet build "C:\MapEditor\.validation-build\UnityCompile.csproj"`。
- `Assets/Models/<TrapName>/`（如 `Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx`）就是「定稿模型」该待的仓库位置，别为了「归位」把它挪到桌面素材文件夹。
- 场景与预制体是体量很大的 YAML，用 `Select-String` 定位行号后只读局部片段。
