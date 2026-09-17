---
name: alpha-striker-unity
description: 在 ProjectAlphaOperationStriker（Unity FPS + 塔防）及配套 MapForge 地图编辑器里改代码、改地图、生成或调整模型时使用。涵盖 Unity 批处理执行与校验、MapForge JSON 导入、怪物路径与陷阱网格联动、空中出怪口（飞行怪物）与陷阱菜单视角锁定等近期能力、以及图形改动的预览与撤回要求。不适用于与该工程无关的通用 Unity 问题。
---

# Alpha Striker Unity / MapForge

本工程是 Unity 6 的第一人称射击 + 塔防项目，地图由浏览器编辑器 MapForge（`C:\MapEditor`）导出 JSON，再由 Unity 编辑器脚本导入成场景。改地图和改代码是同一件事的两半，通常需要同时动 `C:\Unity Project\ProjectAlphaOperationStriker` 和 `C:\MapEditor`。

工作节奏、只读范围和探索预算见工程根目录的 `AGENTS.md`，本 skill 不重复。这里只讲**这个工程特有的、容易做错的做法**。

## 四条工程规范怎么落地

`Assets/代码规范.txt` 要求四件事。它们的可执行做法如下：

1. **只用新版输入系统。** 新增输入代码写 `using UnityEngine.InputSystem`，不要用 `Input.GetKey` / `Input.GetAxis`。工程启用了 `ENABLE_INPUT_SYSTEM`，`Packages/manifest.json` 已带 `com.unity.inputsystem`。
2. **图形类编辑必须能预览、能撤回。** 三类改动各自的撤回手段不同：
   - Blender 建模：脚本常直接 `save_as_mainfile` 覆盖同一个 `.blend`。改之前先把 `.blend` 复制一份到临时路径，结束时用 `bpy.ops.render.render(write_still=True)` 出 PNG 预览（见 `create_flying_monster_plane.py`）。
   - MapForge 改地图：编辑器有 80 步内存内撤销，但**关闭页面即丢失**。改前把 `Assets/mapforge-world.json` 备份或提交，改后重新导出。
   - Unity 改场景：`MapForge` 导入会**覆盖**目标场景文件，所以导入前先 `git stash` 或提交，之后用 `git diff` 回滚。
3. **地图不能遮挡怪物前进道路。** 平台/陷阱网格的可放置格必须排除怪物通道格，这条由 `TrapGridAuthoring` 的掩码函数保证——不要手写一套新的格子逻辑，见 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)。
4. **确认地图在 Unity 里真的没有错位。** 不要靠肉眼看场景视图下结论，用 `Tools/Tower Defense/Validate Trap Grids` 输出数值结果。

## 不变量：半格偏移

怪物网格 `MonsterPathGrid` 把格 (x,y) 锚在**格角**，陷阱网格 `TrapPlacementGrid` 锚在**格心**，两者差半个格。所以陷阱网格位置 = 怪物网格位置 + 本地 `(+0.5, 0, +0.5) × cellSize`。

涉及格子坐标换算时用现成实现，别自己推：

- `TrapGridAuthoring.GroundAnchor(monsterGrid)` —— 正确的锚点。
- `TrapGridAuthoring.CellCenter(...)` / `DominantSurfaceTop(...)` —— 格心与落点高度。
- `MonsterPathGrid.CellToWorld` / `TryWorldToCell` —— 怪物侧换算。

`TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 是同一常量的另一份定义，改偏移要两处一起改。

## 近期改动的约定（2026-09 新增）

以下两条是近期新加的能力，改代码时**必须沿用，不要另起一套**。

### 空中出怪口（飞行怪物）

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

### 陷阱菜单锁定玩家视角

打开陷阱选择菜单时玩家视角必须冻结（不能转视角、不能误射），这条靠**一处状态 + 两处尊重**实现：

- 唯一判据是 `TrapSelectionMenu.CursorOwned`（静态属性，等价于「菜单开着 **或** 菜单刚在本帧关闭」）。
- `FirstPersonController.UpdateCursor()` 与 `FPSPackagePlayerMotion.UpdateCursor()` 都要在开头判断 `TrapSelectionMenu.CursorOwned` 并**直接返回**，不再抢回鼠标锁。有**两份** `UpdateCursor`（两个控制器副本），改一份要改两份。
- `TrapSelectionMenu` 自己开菜单时记下 `previousLock` / `previousVisible`，关闭时还原。

所以新增任何「打开 UI 就该放鼠标」的功能时：**不要**再写一套 `Cursor.lockState = None` 的临时逻辑，也不要让控制器去猜菜单状态；而是让菜单复用 `CursorOwned`（或按同样模式扩展），并在两个 `UpdateCursor` 里一起尊重它。注意关闭那一帧仍要为真，否则点击关闭菜单的同帧视角会被重新锁回。

## 路由

- 要在 Unity 里跑编辑器脚本、批处理导入或校验，读 [references/unity-editor.md](references/unity-editor.md)，并优先用 `scripts/run-unity-method.ps1`，因为 Unity 的退出码**不能**用来判断成败。
- 要改地图、改 MapForge 导出格式、或调整格子/路径联动，读 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)。
- 两边都要动的地图改动，顺序是：MapForge 改 → 导出 JSON → Unity 导入 → Unity 校验。
- 改空中出怪口、飞行怪物移动、或陷阱菜单的视角/准星锁定，读上面的[近期改动的约定](#近期改动的约定2026-09-新增)；这两处都各有一份以上的复制实现，别只改一处。

## 已知会造成误判的情况

- `Assets/KINEMATION`、`Assets/TextMesh Pro` 被 `.gitignore` 排除。缺失时 C# 会报缺类型，属于预期，不要为此反复排查。
- 根目录的 `*.log`、`*.blend1`、`*.csproj` 都被忽略；`Assets/Models/*.blend` 和 `.fbx` 是被跟踪的。
- 同一时刻只允许一个 Unity 实例持有 `Library/`。开编辑器时批处理会卡住或失败，先确认没有 Unity 在跑。
- 场景与预制体是体量很大的 YAML，用 `Select-String` 定位行号后只读局部片段。