# 项目资源清理与文件结构优化方案

> 目标：降低 `Assets/` 的无效扫描、重复资源和路径歧义，同时保持 Unity 序列化引用、`Resources.Load`、编辑器生成器和 MapForge 同步链路不被破坏。

## 1. 目录职责

- `Assets/Scripts/FPS`：玩家、输入、武器和场景切换运行时代码。
- `Assets/Scripts/TowerDefense`：陷阱、敌人、投射物、升级和运行时特效。
- `Assets/Editor`：编辑器菜单、Authoring、验证脚本和项目交接文档。
- `Assets/Scenes`：正式场景；主场景仍为 `THREE_ROUTE_MERGE_MAP.unity`。
- `Assets/Prefabs`：需要被场景或代码直接引用的正式预制体。
- `Assets/Resources`：只有运行时通过 `Resources.Load` 或定义资产约定加载的资源。
- `Assets/Models/<AssetName>`：定稿模型、材质和贴图；每个模型独立子目录。
- `Assets/Animations/<AssetName>`：动画控制器和正式动画资源。
- `Assets/TrapDefinitions`、`Assets/Settings`、`Assets/UI`：可序列化配置、系统设置和正式 UI 资源。
- `Assets/MapForgeMaterials`、`Assets/MapForgeSync`：按现有 MapForge 管线处理，禁止手工改名或随意合并。
- `Assets/KINEMATION`、`Assets/TextMesh Pro`、`Assets/Hovl Studio`：第三方/外部包，单独统计，不与项目资源混清。

## 2. 清理分级

### A. 可自动确认后处理（低风险）

1. **中间态迁移出仓库**
   - `Assets/Models` 下的 `.blend`、`.blend1`、`.blend2`、临时 `.py`、临时导出模型和预览图，迁移到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\<对应陷阱>\`。
   - 当前重点候选包括 `Assets/Models/GameplayLadder.blend`、`Assets/Models/create_test_monster.py`。移动前必须确认没有脚本、文档或生成器把它们当作正式输入。
   - 只保留定稿 `.fbx`、正式材质和 Unity 需要的 `.meta`。

2. **孤立 `.meta` 与空目录检查**
   - 对每个 `.meta` 检查同名资源是否存在；孤立 `.meta` 只能在确认没有 GUID 引用后删除。
   - 空目录不提交；Unity 会按需重建目录。

3. **生成结果与生成器分离**
   - `Assets/Editor/*Authoring.cs` 是源头，`Assets/Prefabs`、`Assets/Resources` 中的产物是结果。
   - 禁止为了整理目录而手改由 Authoring 重新生成的 Prefab、材质或配置；应修改生成器后重新生成并验证。

### B. 需要逐项人工确认（中风险）

1. **重复模型/旧版本模型**：重点检查同一资产的多个导出版本，例如 `TrapPlacementProjectionGun` 下的多个 FBX、旧版发射器模型和带 `Loaded` 后缀的模型。通过 `rg` 查找路径、GUID、Prefab/Authoring 引用；确认无引用后再归档或删除。
2. **重复材质**：以材质 GUID 和实际引用为准合并，不按名称猜测。运行时生成器使用的材质必须保留在生成器约定目录。
3. **旧陷阱定义/预制体**：检查 `Assets/Resources`、`Assets/TrapDefinitions` 是否仍被 `TrapUpgradeCatalog`、`Resources.Load`、菜单 Authoring 或场景引用。未使用资源先移动到桌面隔离目录，观察一个迭代周期后再删除。

### C. 禁止直接清理

- `Assets/Resources` 中仍由 `Resources.Load`、字符串路径或序列化字段访问的资源。
- `Assets/MapForgeMaterials`、`Assets/MapForgeSync` 的运行时同步文件。
- 主场景及其被 MapForge 维护的对象。
- 第三方目录和缺失包相关文件。
- 任何只在 YAML 中出现 GUID 引用、但尚未完成引用图确认的资源。

## 3. 推荐执行顺序

### 第 1 批：建立清单和回滚点

- 建立独立分支或提交回滚点。
- 输出路径、类型、大小、GUID、引用来源、是否由生成器重建等字段。
- 不删除文件，只生成候选列表。

### 第 2 批：迁移明确的中间态

- 先复制到桌面陷阱素材目录，再从仓库移除源文件及其 `.meta`。
- 更新 `PROJECT_MEMORY.md`，记录新的素材归档位置。
- Unity 重新导入后执行脚本编译和目标 Authoring 验证。

### 第 3 批：处理重复/旧版本资源

- 每次只处理一个资产族。
- 先改引用源或生成器，再移动旧资源。
- 每批只做一次结构性变更，保留可恢复的桌面归档。

### 第 4 批：目录职责收口

- 将散落在 `Assets/Models` 根目录的正式模型按资产名归档到子目录。
- 将编辑器说明、交接文档集中到 `Assets/Editor`。
- 对新目录补充简短 `README`，只写路径职责和生成入口。

### 第 5 批：验证与提交

- 使用 `ProjectSettings/ProjectVersion.txt` 对应的 Unity 版本进行 C# 编译验证。
- 运行受影响资产的 `Validate...`/`Probe...` Authoring 方法和必要 EditMode 回归。
- 检查 `git diff --name-status`、GUID 引用和仓库中是否残留中间态文件。
- 更新 `Assets/Editor/PROJECT_MEMORY.md`，记录已迁移文件、保留原因和未处理候选。

## 4. 建议的自动化工具

新增只读审计入口（建议 `Assets/Editor/ResourceInventoryAuthoring.cs`）：

- 扫描允许目录，输出 `path/type/size/guid`。
- 扫描 C#、Prefab、场景、ScriptableObject 中的路径字符串和 GUID。
- 标记孤立 `.meta`、未引用资源、重复文件名、仓库内中间态文件。
- 默认只输出报告，不执行删除；报告放到桌面素材目录，不写入仓库。

## 5. 当前第一轮候选

先完成引用扫描，再处理以下低风险候选：

- `Assets/Models/GameplayLadder.blend`
- `Assets/Models/create_test_monster.py`
- 其他位于 `Assets/Models` 根目录、且不属于定稿模型的脚本、预览图和临时导出文件
- 已确认没有引用的孤立 `.meta` 和空目录

暂缓处理多版本 FBX、`Loaded` 命名模型、Prefab、Resources 资产、MapForge 目录、第三方目录和任何由 Authoring 自动生成的正式产物。

## 6. 完成标准

1. 每个正式资源只有一个权威路径。
2. 运行时加载路径、Prefab/场景 GUID 和编辑器生成链路不变，或有明确迁移记录。
3. 中间态全部位于 `C:\Users\ASUS\Desktop\陷阱素材文件夹\` 对应子目录。
4. Unity 编译、受影响的 Authoring 验证和必要回归测试通过。
5. `PROJECT_MEMORY.md` 能说明已完成的清理、保留的例外和下一批候选。

## 第一轮低风险项执行记录（2026-11-14）

- 已将 `Assets/Models/GameplayLadder.blend` 及其存在的 `.meta` 迁移到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\GameplayLadder\`。
- 已将 `Assets/Models/create_test_monster.py` 及其存在的 `.meta` 迁移到 `C:\Users\ASUS\Desktop\陷阱素材文件夹\TestMonster\`。
- 在 `Assets/Scripts`、`Assets/Editor`、`Assets/Models`、`ProjectSettings`、`Packages` 中未发现这两个文件的计划外引用；搜索命中仅为本计划中的候选记录。
- 两个被迁移 `.meta` 的 GUID 均无项目内引用；随后检查上述允许目录，孤立 `.meta` 与空目录均为 0。
- 尚未处理其他中间态、重复模型/材质、旧陷阱定义或目录归档项；它们需要逐项核对引用后再处理。
