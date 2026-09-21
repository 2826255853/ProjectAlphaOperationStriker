# MapForge 地图管线（C:\MapEditor → Unity）

## 数据流

```
浏览器编辑器 C:\MapEditor (ASP.NET + Three.js)
        │
        ├─ 「Send to Unity」增量修订（日常改动走这条）
        │      <工程>/MapForgeSync/mail/pending.json
        │      → MapForgeLiveSync 轮询 2 秒，就地改当前打开的场景
        │
        └─ 导出 mapforge-world.json（整图重排走这条）
               C:\Unity Project\ProjectAlphaOperationStriker\Assets\mapforge-world.json
               → 菜单 MapForge/Import *，或 MapForgeWorldImporter.ImportDefault
                     ↓
C:\Unity Project\ProjectAlphaOperationStriker\Assets\Scenes\<地图名>.unity
```

`Assets/mapforge-world.json` 是**被 git 跟踪**的当前地图（v2，约 140 KB，`name` 为 `THREE_ROUTE_MERGE_MAP`，23 个顶层对象、`gameplay.spawns` 3 个出怪口、`totalWaves = 3`）。导入后的场景名取自 JSON 的 `name` 字段。

启动编辑器：

```powershell
& "C:\MapEditor\Start-MapForge.ps1"          # 默认 http://localhost:5090
```

脚本会先探测 `/api/health`，已经有实例就直接开浏览器，否则 `dotnet run`。手动跑也可以：`dotnet run --project C:\MapEditor\MapEditor.csproj --no-launch-profile --urls http://localhost:5090`。测试在 `C:\MapEditor\tests\`（`node --test` 跑 `.test.js`；`tests/browser.mjs` 是浏览器端驱动）。

## 「Send to Unity」实时同步（走 MapForgeSync 文件夹）

除了导出/导入，MapForge 还有一条**增量同步**通道：点「Send to Unity」，Unity 编辑器自己把改动应用到**当前打开的场景**，不需要手动搬 JSON。

- 协议两侧各一份常量，必须一致：MapForge `C:\MapEditor\MapForgeSync.cs`（`MapForgeSync.Protocol`，浏览器侧 `wwwroot/unity-sync.js`，配置 `C:\MapEditor\mapforge-sync.json` 记 Unity 工程路径）与 Unity `Assets/Editor/MapForgeLiveSync.cs`（`MapForgeLiveSync.Protocol`）。
- 传输靠文件夹：写 `<工程>/MapForgeSync/mail/pending.json`；Unity 每 2 秒轮询（`PollSeconds = 2.0`，心跳 `HeartbeatSeconds = 15`），应用后回写 `mail/revision.txt`、`mail/status.json`、`mail/applied.txt` / `applied.json`，浏览器据此汇报结果。整个 `MapForgeSync/` 被 `.gitignore` 忽略。
- 增量修订只带真正改过的对象，Unity 就地重写（新增/改写/删除）；只有 MapForge 要求时才整图重建（首次发送、地图改名、`gameplay` 或场景级映射变了、改动量过大）。整图修订的 world 先落到 `Assets/MapForgeSync/sync-world.json` 再交导入器。
- **与全量导入的关键区别**：只动 `MapForgeWorld` 生成的层级，工程手工加的对象（如空中出怪口）不会像 `Import` 那样被删。保留机制在 `Assets/Editor/MapForgeWorldImporter.Authored.cs`（`MapForgeWorldImporter` 是 **partial**，两个文件都要在）：上次生成的直接子对象名记在 **EditorPrefs**（机器本地、不进版本库），据此把手工对象搬出去再搬回来。
- 入口：菜单 `MapForge/应用待同步版本 (Apply pending revision)` = `MapForgeLiveSync.ApplyPendingFromMenu`；`MapForge/下次发送时完整重建 (Request full rebuild)` = `MapForgeLiveSync.RequestFullRebuild`；批处理用 `MapForgeLiveSync.ApplyPending()`（无待同步修订时返回 null，不是失败）。
- 测试：`C:\MapEditor\tests\unity-sync.test.js`（`node --test`）。Unity 侧没有专门用例，改了先跑 [更快的编译检查](unity-editor.md#更快的编译检查不进-unity)，再点一次「Send to Unity」看 `status.json` 回执。

### 导入新增的按 id 关联组件

`MapForgeSceneOrganization` 把 JSON 的 `unity.pathNode` / `unity.trigger` 段建成运行时组件，并按 `id` 校验（`links` 必须指向存在的节点、`shape` 只能是 `box|sphere`、`when` 是 `enter|exit`、`action` 是 `spawn|damage|goal|message|enable|disable`）：

- `Assets/Scripts/MapForgePathNode.cs`：`role`（node/waypoint/junction/end）、`waitTime`、`links`；`MapForgePathNode.All` / `Find(objectId)` 供玩法代码用（`Find` 先比 `MapForgeObjectProperties.objectId`，没有元数据时退回比对象名）。
- `Assets/Scripts/MapForgeTrigger.cs`：`MapForgeTrigger.Fired` 事件把 `spawn` / `damage` / `goal` 交回玩法代码，`message` / `enable` / `disable` 由组件自己执行；`MapForgeTrigger.FindTarget(objectId)` 解析目标。

改 JSON 键名要同时改导入器与这两个组件。注意当前 `Assets/mapforge-world.json` 里**每个对象**都带 `unity.pathNode` 与 `unity.trigger` 块，但 `enabled` 全是 `false`：没有 `enabled: true` 就不会生成组件，别把「块存在」当成「功能已启用」。增量同步重写对象前会走 `MapForgeSceneOrganization.ClearGeneratedContent`，它按名字前缀清掉 `Prefab *` / `Trigger Volume *` 子对象，以及 `EnemySpawnPoint` / `EnemyCore` / `MapForgePathNode` / `MapForgeTrigger` 等组件，保留对象本身、元数据与几何子物体——所以**别往这些名字前缀下放手工子对象**，会被误删。

### 材质库产物

导入/同步会为场景生成材质，落在 `Assets/MapForgeMaterials/<场景名哈希>/`（目录名由 `MapForgeSceneOrganization` 里的 `Hash(sceneName)` 决定）。这批 `.mat` 是生成物但**被 git 跟踪**，改场景后跟着提交；不要手工改名或搬目录，重导会按同一哈希复用。

## v2 信封格式

Schema 在 `C:\MapEditor\map-format.schema.json`，服务端校验在 `C:\MapEditor\MapValidation.cs`，前端规范化在 `C:\MapEditor\wwwroot\scene-model.js`。必需字段：

`version` / `name` / `settings` / `objects` / `groups` / `gameplay`

- `version`：必须恰好是 `2`。前端 `normalizeDocument` 和服务端 `MapValidation` 都会拒绝缺 `version`、写成别的版本号或非数字的文档，**不会**把旧文件迁移成 v2；Unity 侧 `MapForgeSceneOrganization.Parse` 也要求 `version == 2`。
- `rotationUnit`：必须字面量 `"radians"`。写了别的值前后端都会直接报错（`旋转单位必须为 radians。`）。
- `settings`：`gridSize`（> 0）、`unit` 必须为 `"meter"`。
- `objects[]`：必需 `id`、`type`。`type` 只能是 `cube | sphere | cylinder | plane | folder | group`。`id` 非空且唯一，`parentId` 指向存在的对象且**不能成环**（前端限制层级深度 ≤ 256）。
- `transform`：`position` / `rotation` / `scale` 各 3 个有限数值；`scale` 每轴 ≥ 0.01。
- `objects[].color`：`#RRGGBB`；`layer`：0–31；`visible`/`locked`/`collapsed`/`collision`/`static`：布尔。
- **未知属性是扩展点，必须原样保留。** 前端 `normalizeDocument` 会把已知字段以外的内容收进 `extensions` 再拼回去，所以别为了“清理”删掉不认识的键。
- 导出的 JSON 缩进 2 空格，`null` 的 `parentId` 是顶层对象。

### 一个有坑的地方：`gameplay` 只有 Unity 侧才真正消费

`MapForgeWorldImporter` 读取的 `gameplay` 结构是：

```
gameplay.pathGrid  { origin, columns, rows, cellSize, pathHeight, openCells[], segments[]{start,end} }
gameplay.core      { position, arrivalRadius }
gameplay.totalWaves
gameplay.spawns[]  { id, position, travelDirection, moveSpeed, totalWaves, enemiesPerWave, initialWaveDelay, waveTransitionDelays[] }
roadsideSteps[]    { platformId, position, axis, nearSideSign, length, width, height }
```

浏览器端的 `scene-model.js` 对 `gameplay` 是 `additionalProperties: true` 的**透传**，不知道这些子字段的含义；示例文件 `wwwroot/examples/organized-world.json` 里 `gameplay` 是 `{}`。所以：**只改浏览器 UI 的代码不会影响 Unity 里跑出来的怪物路径**，要改路径得动 `Assets/mapforge-world.json` 的 `gameplay.pathGrid`（或改编辑器导出逻辑）。

### 导入只产地面出怪口，空中出怪口是编辑器后加的

`MapForgeWorldImporter` 从 `gameplay.spawns[]` 建出的每个 `EnemySpawnPoint` 都**不写** `flyingEntrance`，所以导入结果里全是地面出怪口（`MonsterType.Ground`）。**空中出怪口不来自 JSON**，是导入完成后用编辑器脚本补的：

```
FlyingSpawnPointAuthoring.CreateFlyingPrefab   # 生成 Assets/Resources/FlyingMonster.prefab
FlyingSpawnPointAuthoring.AddFlyingSpawnPoint  # 在 MapForgeWorld 下建 "Enemy Spawn Flying"
```

由此有个必须记住的顺序问题：**MapForge 导入会覆盖整个场景，连带删掉上一次手工加的 `Enemy Spawn Flying`。** 每次重导地图之后都要再跑一遍 `FlyingSpawnPointAuthoring.CreateAssets`（或菜单 `Tools/塔防/在场景中添加飞行出怪口`），并在 `git diff` 里确认它回来了。判重靠对象名 `Enemy Spawn Flying`，别改这个名字。

要长期保留飞行出怪口，要么每次导入后重建，要么把这一步并进导入流程（目前没有自动跑）。
`openCells` 是一维布尔数组，长度 `columns * rows`，行主序。导入时它被逐格拷进 `MonsterPathGrid`；`segments` 则是把起点/终点 `TryWorldToCell` 后按 Bresenham 走线设为开格。

### 旋转：弧度 + 固有 XYZ

JSON 里存的是**弧度**，并且是 Three.js 的**固有 X→Y→Z** 顺序。Unity 导入时用（`MapForgeSceneOrganization.cs`）：

```csharp
var r = Vector(node.transform.rotation) * Mathf.Rad2Deg;
go.transform.localRotation = Quaternion.AngleAxis(r.x, Vector3.right)
                          * Quaternion.AngleAxis(r.y, Vector3.up)
                          * Quaternion.AngleAxis(r.z, Vector3.forward);
```

**不要**直接写 `localEulerAngles = degrees`——Unity 的欧拉顺序和这里不同，肉眼小角度可能看不出，大角度会明显错位。层级导入只有 `MapForgeSceneOrganization` 这一条路径（旧版扁平导入已随 v1 支持一起删除），改旋转相关代码时认准它。

浏览器检查器里显示和输入的都是**度**（`degToRad` / `radToDeg` 在 `app.js`），只有落盘才是弧度。

### 图元尺寸不能直接换算

两边世界单位一致，但图元网格原生尺寸不同（`app.js` 的 `geometry()` vs `MapForgeSceneOrganization.MeshScale`）：

| type | Three.js 网格 | Unity 里乘的系数 |
| --- | --- | --- |
| `cube` | `BoxGeometry(1,1,1)` | `(1, 1, 1)` |
| `plane` | `BoxGeometry(3, 0.12, 3)`（薄板，不是平面） | `(3, 0.12, 3)` |
| `sphere` | `SphereGeometry(0.6)` → 直径 1.2 | `(1.2, 1.2, 1.2)` |
| `cylinder` | `CylinderGeometry(0.5, 0.5, 1.4)` | `(1, 0.7, 1)` |

导入时网格子对象上 `localScale = 系数`，作者缩放留在父级，目的是让实际包围盒跟编辑器里看到的一致（相邻地砖才能对齐）。改图元尺寸要两张表一起改。

## 导入的破坏性与撤回

**`MapForgeWorldImporter.Import` 会覆盖 `Assets/Scenes/<name>.unity`**，不合并、不给旧版留位置。所以：

1. 导入前先 `git stash` 或提交场景文件。
2. 导入后用 `git diff Assets/Scenes/<name>.unity` 看变化，不对就 `git checkout --` 回滚。
3. 别指望 Unity 的 Undo 能救——导入是写文件，不是编辑器操作。

自动导入有节流：`[InitializeOnLoad]` 在域重载后跑 `AutoImportIfNeeded`，但只在「场景文件不存在」或「JSON 内容 hash 变了」时才重导，hash 存在 `EditorPrefs` 里（键含 `Application.dataPath`，是机器本地的）。**改了 JSON 要真的改动内容**才会触发；手改后又改回原样不会重导。要强制重导就删掉 `Assets/Scenes/<name>.unity` 再跑一次。

`MapForge/Import mapforge-world.json` 会弹文件面板，**批处理下必挂**，脚本化用 `ImportDefault`（或 `Import(jsonPath)`）。

## 撤回手段速查

| 改动对象 | 撤回手段 | 保鲜期 |
| --- | --- | --- |
| 浏览器里的地图编辑 | Ctrl+Z / Ctrl+Y（上限 80 步，见 `scene-model.js` 的 `History`） | **关页面即丢** |
| `Assets/mapforge-world.json` | git（文件被跟踪） | 永久 |
| 实时同步改过的场景对象 | 同步只就地改写变过的对象；出错时 `git checkout -- Assets/Scenes/<name>.unity` | 永久（场景文件） |
| 导入后的 `.unity` 场景 | git（导入会覆盖，正是靠 git 兜底） | 永久 |
| Blender 模型 | 改前把 `.blend` 备份到桌面「陷阱素材文件夹」的陷阱子目录 | 手动 |

浏览器还有 `Ctrl+G` 编组、`Ctrl+A` 全选可见且未锁定的对象；锁定（`locked`，含父级继承）的对象在检查器里是只读的，`assertEditable` 会直接拒绝修改。

## 格子与路径联动（代码规范 3、4）

半格偏移不变量、以及要用哪些函数，见 [SKILL.md](../SKILL.md#不变量半格偏移)。这里补充要点：

- `TrapGridAuthoring.BuildGroundMask` / `BuildPlatformMask` **主动排除怪物通道格**（`monsterGrid.IsOpen(...)` 为真的格），这就是「地图不能遮挡怪物前进道路」的落地点。平台掩码还要求平台顶完整覆盖该格；地面掩码则排除被平台压住的格。**每个格只在其中一个层级出现**，不要另写一套格子逻辑。
- 两者都返回 `bool[]`（长度 `columns * rows`，行主序），`null` 表示该层级没有可用面。
- `TrapGridAuthoring.SurfaceOffset = 0.02f`：陷阱比所在面高这么多。
- `TrapPlacementGrid.ConfigureLayout(columns, rows, cellSize, height, openCells)` 是导入时构造陷阱网格的入口，`HasSameOpenCells` 用来比对掩码一致性。

**已知不一致（留意）**：`TrapGridAuthoring.GroundAnchor` 用的是 `CellSize * 0.5f`，而 `TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 是常量 `(0.5, 0, 0.5)` 再乘 `transform.TransformVector`（按 transform 缩放，不按 `CellSize`）。当前地图 `cellSize = 1.0`、网格 transform 无缩放时两者刚好相等；一旦 `cellSize ≠ 1`，两处会产生半格漂移。改格子尺寸时**两处一起改**，改完跑一次 `Validate Trap Grids` 确认。

## 改完地图的标准流程

1. 提交/备份当前 `Assets/mapforge-world.json` 与目标场景。
2. MapForge 里改，导出 JSON，覆盖 `Assets/mapforge-world.json`。
3. 用 [unity-editor.md](unity-editor.md) 里的脚本跑 `MapForgeWorldImporter.ImportDefault`，**按日志判定成败**（退出码不可信）。
4. 跑 `Tools/Tower Defense/Validate Trap Grids`（`TrapPlacementGridEditor.ValidateTrapGrids`），看输出的数值报告而不是靠肉眼。
5. 重跑 `FlyingSpawnPointAuthoring.CreateAssets` 把空中出怪口加回来（导入会覆盖掉它）。
6. `git diff` 复查场景与 JSON。

日常小幅改动不必走 2–5 步，直接点 MapForge 的「Send to Unity」增量同步（见上文）：它只改写变过的对象，也不会删掉空中出怪口。只有整图重排、换地图名或同步要求完整重建时才回到上面的全量导入流程。
