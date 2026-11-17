---
name: alpha-striker-unity
description: 在 ProjectAlphaOperationStriker（Unity FPS + 塔防）及配套 MapForge 地图编辑器中修改代码、地图、陷阱升级、菜单或模型时使用，提供项目特有的数据流、资产生成、测试隔离与预览约定。不适用于其他 Unity 工程的通用问题。
---

# Alpha Striker Unity / MapForge

本工程是 Unity 6 的第一人称射击 + 塔防项目，配套地图编辑器为 MapForge（`C:\MapForge`）。先确定本次使用普通全量导入、实时增量同步，还是地图专用 Authoring 生成器；三条入口的过滤和补充几何不相同。只有任务涉及共享地图源或同步协议时才联动两边，运行时代码或单个陷阱修改不默认扩展到 MapForge。

工作节奏、只读范围和探索预算见工程根目录的 `AGENTS.md`，本 skill 不重复。编辑前先读 `Assets/Editor/PROJECT_MEMORY.md`；当前源码与配置优先于带日期的历史记录。这里只讲**这个工程特有的、容易做错的做法**。

## Plan 制作规范与近期经验

多文件或跨工具任务先给简短分步 Plan，说明可观察行为、目标文件、依赖顺序、每步最小产物及对应验证。涉及资产覆盖时补充备份/撤回入口，跨仓库任务说明两边字段如何一致。

Plan 不增加探索预算，也不要求先完成全部设计：按 `AGENTS.md` 的节奏，在确认首个目标文件后立即写入最小可用改动，再细化后续步骤。只做小改动时直接说明目标与验证即可。步骤按实际依赖选择，不必凑齐固定阶段。例如：

```text
Step 1 - 数据/接口：目标文件与最小实现；验证：编译或结构断言。
Step 2 - 生产/运行：生成器、引用和状态转换；验证：受影响的编辑期或运行时用例。
Step 3 - 交互/验收：需要的 UI、预览和旧功能回归；注明未覆盖的操作。
```

近期项目经验应作为 Plan 的默认检查项：

- **同一能力可能有多份实现**：玩家控制器、导入器或同步协议存在副本时，先搜索所有入口，明确“一处状态 + 多处尊重”或“两边协议一起改”，不能只修当前看到的文件。
- **编辑器生成链优先于手工资产修改**：预制体、材质、场景层级若由 Authoring/Importer 生成，应先改生成器并重新生成；手工改生成结果会在下一次导入时丢失。
- **运行时依赖要和资产目录匹配**：需要 `Resources.Load` 的资源必须放在 `Assets/Resources/`；由 `TrapDefinition` 或序列化字段引用的预制体不应为了套用旧经验而搬进 `Resources`。
- **不要凭模型外观补物理逻辑**：陷阱炮塔可能没有 Collider，血条要有射线失败后的近射线选靶，导弹伤害也可能由投射物自行结算；先确认现有数据流和测试契约，再决定是否增加 Collider。
- **预测、坐标和筛选逻辑只能有一个权威来源**：拦截点、半格偏移、飞行判定、陷阱网格可放置掩码等应复用现成 helper；新增第二套公式会造成炮塔/投射物或编辑器/运行时不一致。
- **先验证最小闭环，再扩展表现**：先让对象能生成、引用正确、核心状态能转换，再加入特效、HUD 和美术细节；每完成一个逻辑单元立即留下 diff 和验证结果。
- **视觉改动必须留可检查产物**：模型、尾焰、预制体或场景修改除编译外要生成预览图/探针结果，并把中间态放到桌面素材目录，最终只提交定稿资源。

## Unity 编辑器版本

- 执行本项目时使用 `ProjectSettings/ProjectVersion.txt` 记录的版本，不自行换成机器上更新的编辑器。升级项目版本是单独的迁移任务；不默认使用 Alpha、Beta 或其他预发布版。
- 每次运行 Unity 前，读取 `ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion`，再在 `C:\Program Files\Unity *\Editor\Unity.exe` 下核实对应安装；本机使用直接安装布局，脚本也兼容 Hub。文档、脚本、`.csproj` 不固定历史版本号，旧日志或生成文件不作为当前版本依据。
- 若项目记录的版本未安装，说明缺失，不擅自用其他版本代跑或迁移项目。`run-unity-method.ps1` 在精确路径缺失时可能回退到另一已安装版本；因此启动前先核实精确版本路径，并用 `-UnityPath` 显式传入，不能等自动回退运行后才检查。
- 批处理命令使用核实后的路径，不硬编码历史版本号；具体执行方式见 [references/unity-editor.md](references/unity-editor.md)。

## 四条工程规范怎么落地

`Assets/代码规范.txt` 要求四件事。它们的可执行做法如下：

1. **只用新版输入系统。** 新增输入代码写 `using UnityEngine.InputSystem`，不要用 `Input.GetKey` / `Input.GetAxis`。工程启用了 `ENABLE_INPUT_SYSTEM`，`Packages/manifest.json` 已带 `com.unity.inputsystem`。
2. **图形类编辑必须能预览、能撤回。** 三类改动各自的撤回手段不同：
   - Blender 建模：脚本常直接 `save_as_mainfile` 覆盖同一个 `.blend`。改之前先把 `.blend` 复制一份到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\<陷阱名>\` 下的备份副本，结束时用 `bpy.ops.render.render(write_still=True)` 出 PNG 预览。
   - MapForge 改地图：编辑器有 80 步内存内撤销，但**关闭页面即丢失**。改前备份该地图实际源 JSON，改后重新导出；双层设施等专用源见任务路由。
   - Unity 改场景：**全量导入**（`MapForgeWorldImporter.Import`）会覆盖目标场景。改前备份本次相关源文件、场景和 `.meta` 到桌面对应子目录；已有合适的提交也可作为恢复点。增量同步的手工对象保留机制见 [mapforge-pipeline.md](references/mapforge-pipeline.md)。不要把自动 stash、提交或整文件回滚作为前置条件；撤回仅覆盖本次改动，保留用户已有工作。
3. **地图不能遮挡怪物前进道路。** 平台/陷阱网格的可放置格必须排除怪物通道格，这条由 `TrapGridAuthoring` 的掩码函数保证——不要手写一套新的格子逻辑，见 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)。
4. **确认地图在 Unity 里真的没有错位。** 已接入陷阱网格的地图运行 `Tools/Tower Defense/Validate Trap Grids`；geometry-only 地图运行对应 Authoring 的尺寸、碰撞与通行校验，不能用空网格通过来验收。

## 不变量：半格偏移

怪物网格 `MonsterPathGrid` 把格 (x,y) 锚在**格角**，陷阱网格 `TrapPlacementGrid` 锚在**格心**，两者差半个格。所以陷阱网格位置 = 怪物网格位置 + 本地 `(+0.5, 0, +0.5) × cellSize`。

涉及格子坐标换算时用现成实现，别自己推：

- `TrapGridAuthoring.GroundAnchor(monsterGrid)` —— 正确的锚点。
- `TrapGridAuthoring.CellCenter(...)` / `DominantSurfaceTop(...)` —— 格心与落点高度。
- `MonsterPathGrid.CellToWorld` / `TryWorldToCell` —— 怪物侧换算。

`TrapPlacementGridEditor.MonsterGridToTrapGridOffset` 是同一常量的另一份定义，改偏移要两处一起改。

## 文件放置：陷阱制作的中间态文件

陷阱（建模 / 贴图 / 导出 / 预览）过程中的**一切中间态文件**都写在桌面素材文件夹，**不进仓库**：

`C:\Users\ASUS\Desktop\陷阱素材文件夹`

- 写什么：Blender 源文件与自动备份（`*.blend` / `*.blend1` / `*.blend2`）、临时导出的 `*.fbx` / `*.obj` / `*.glb`、贴图与烘焙结果、预览图与试渲染、一次性清单/日志，以及只为这次陷阱写的一次性脚本（含 Blender 的 `*.py`）。
- 怎么放：按陷阱名建子目录（如 `陷阱素材文件夹\Flamethrower\`），同一陷阱的源文件、导出件、预览图都放这个子目录里；目录不存在就直接创建，**不要**退而写到仓库根目录或 `Temp`。
- 什么才进仓库：只有**定稿**资源，即模型 `Assets/Models/<TrapName>/*.fbx`（含贴图）、最终预制体与陷阱定义（飞行怪物那套在 `Assets/Resources/`，双联导弹发射器在 `Assets/Prefabs/DualMissileLauncher.prefab` + `Assets/Resources/DualMissileLauncherLoaded.asset` + `Assets/TrapDefinitions/DualMissileLauncherLoaded.asset`），以及导入生成的材质库 `Assets/MapForgeMaterials/<场景名哈希>/`（生成物但被跟踪，见 [references/mapforge-pipeline.md](references/mapforge-pipeline.md)）。已有 `const string` 引用的路径（如 `FlyingSpawnPointAuthoring.FlyingModelPath`、`MissileLauncherTurretAuthoring.LauncherModelPath`）不要顺手搬家。
- 汇报时给出该文件夹下的完整绝对路径，用户要能直接点开看预览图。
- 与第 2 条规范的配合：修改共用 `.blend` 前先把副本备份到该文件夹的对应子目录，再用 `bpy.ops.render.render(write_still=True)` 把预览图输出到同一子目录。

## 按任务读取

只加载当前任务涉及的参考页；详细参数和历史验收记录留在项目记忆，不必每次通读。

| 当前任务 | 参考页与边界 |
| --- | --- |
| Unity 编译、生成器、测试、渲染 | [unity-editor.md](references/unity-editor.md)：精确版本、日志判定、测试 XML 与收尾隔离 |
| 普通地图导入、实时同步、格式或网格 | [mapforge-pipeline.md](references/mapforge-pipeline.md)：协议、坐标、手工对象保留 |
| 双层设施、楼梯/货运坡道、地图扩容 | [facility-authoring.md](references/facility-authoring.md)：专用生成器、源图、几何通行与预览 |
| 飞行怪、无人机、自爆机器人、菜单/HUD、瞄准镜、导弹、贝蒂跳雷 | [gameplay.md](references/gameplay.md)：按对应小节读取现有实现 |
| 陷阱升级、分支、付费预约、换局生命周期 | [trap-upgrades.md](references/trap-upgrades.md)：已接入能力与状态保留 |

## 已知会造成误判的情况

项目经验是带日期的状态记录，不是永久规则：修改前以当前源码、配置和对应测试为准。记录验收时区分已通过、未执行和替代验证；辅助工程或局部 `.csproj` 的结果只覆盖其实际编入的文件，不能代替完整 Unity 编译或运行时验收。

- `Assets/KINEMATION`、`Assets/TextMesh Pro` 被 `.gitignore` 排除。缺失时 C# 会报缺类型，属于预期，不要为此反复排查。
- 根目录的 `*.log`、`*.blend1`、`*.csproj` 都被忽略；`Assets/Models/*.blend` 和 `.fbx` 是被跟踪的。
- 仓库根目录的中间态文件（`/*.py`、`/*.blend`、`/*.blend1`、`/*.blend2`、`/*.png`、`/*.fbx`）已被 `.gitignore` 忽略：陷阱中间态按规范放在桌面「陷阱素材文件夹」，在仓库根目录找不到它们是**预期**，不要用 `git add -f` 硬塞回去。
- `Assets/FlyingMonsterPlane_preview.png` 这类预览图已迁出仓库（GUID 无任何引用）；要预览图去桌面「陷阱素材文件夹」对应子目录拿。
- 同一时刻只允许一个 Unity 实例持有 `Library/`。开编辑器时批处理会卡住或失败，先确认没有 Unity 在跑。
- `MapForgeSync/` 整个目录被 `.gitignore` 忽略，里面是每次同步都会改写的运行时状态（`mail/pending.json`、`revision.txt`、`status.json`）：它出现在工作区是**预期**，不要提交、也不要为它回滚场景。
- 一些旧的 `Assets/Editor` 编辑期测试类用 `#if UNITY_INCLUDE_TESTS` 条件编译，不能靠 `-executeMethod` 运行；按测试入口选择 Unity Test Framework runner，并确认 XML 实际包含预期用例。不要把某个局部 `.csproj` 当成全工程编译结论。
- `Assets/Models/<TrapName>/`（如 `Assets/Models/DualMissileExhaust/DualMissileExhaust.fbx`）就是「定稿模型」该待的仓库位置，别为了「归位」把它挪到桌面素材文件夹。
- 场景与预制体是体量很大的 YAML，用 `Select-String` 定位行号后只读局部片段。
