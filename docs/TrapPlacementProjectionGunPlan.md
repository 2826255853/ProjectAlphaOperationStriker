# 陷阱放置模式：投影枪实施计划

## 目标与边界

在陷阱放置模式下显示一把手枪形状的投影枪，并让输入行为与普通武器明确隔离：

- 左键继续执行现有陷阱放置流程，不触发普通枪械伤害。
- 右键只保留输入事件，第一版不产生可见或游戏状态副作用。
- 暂不接入手部骨骼、Animator、IK、握持姿态或后坐力动画；模型先挂在独立的第一人称挂点。
- 退出放置模式后恢复普通武器显示与射击行为。
- 不改陷阱网格、费用、购买、合法性、MapForge 地图数据或现有放置规则。

## 实施步骤

每一步单独完成并验证后再进入下一步。若某一步验证失败，先修复该步，不把问题带入后续阶段。

### 步骤 1：核对现有调用链并确定接入契约

**工作内容**

- 核对 `TrapSelectionMenu.CursorOwned`、`TrapPlacementController` 和 `FirstPersonController` 的实际职责与输入时序。
- 确认放置模式的进入、切换陷阱、取消、完成及菜单鼠标锁定边界；复用现有状态，不另建一套菜单状态。
- 记录左键当前放置入口、普通射击结算入口，以及玩家第一人称武器挂点的来源。
- 明确投影枪控制器读取放置模式状态的方式，以及左右键事件的调用方。

**完成条件**

- 能指出放置与普通射击各自唯一的接入位置。
- 不改动现有放置、购买或网格行为。

### 步骤 2：增加可查询的放置模式状态与输入接口

**工作内容**

- 在 `TrapPlacementController` 暴露只读放置模式状态；按步骤 1 确认的生命周期更新状态。
- 保持现有左键放置调用链不变。
- 为投影枪左键转发与右键预留 `OnProjectionGunSecondaryAction`（或等价事件/方法）；右键第一版只触发接口。
- 不改变 `TrapSelectionMenu` 的鼠标锁定和视角冻结逻辑。

**完成条件**

- 放置模式状态能正确覆盖进入、切换、取消和完成场景。
- 接口可由后续控制器调用；右键不改变场景、陷阱或玩家状态。

### 步骤 3：建立专用武器控制器并隔离普通射击

**工作内容**

- 新增 `TrapPlacementProjectionGunController`，按放置模式启用/显示投影枪，退出时隐藏/禁用。
- 在 `FirstPersonController` 的武器/射击桥接处禁止放置模式下的普通枪械命中结算，避免左键同时造成伤害和放置。
- 由控制器转发左右键：左键走既有放置入口，右键只触发预留接口。
- 增加最小配置：优先通过 Inspector 引用投影枪 Prefab；缺少引用时只输出一次明确警告，不替换普通武器。

**完成条件**

- 放置模式下普通射击不会造成伤害，左键仍能进入原放置流程。
- 离开模式后普通武器行为恢复；菜单打开时视角冻结行为不变。
- 此步可先用空物体或占位 Prefab 验证，不等待最终模型。

### 步骤 4：制作并确认投影枪模型

**状态：已完成（2026-10-20，模型交付 + Unity 导入与独立第一人称预览验收）。**

**中间态目录**

`C:\Users\ASUS\Desktop\陷阱素材文件夹\TrapPlacementProjectionGun\`

沿用模型制作时已确认的当前账户目录；旧账户 Origami 路径不再用于本投影枪产物。

**工作内容**

- 制作低面数手枪轮廓：枪身、握把、扳机护圈和短投影发射口。
- 加入投影识别元素：发光镜片/投影窗口、简化散热槽和陷阱网格符号面板。
- 按第一人称视角调整比例、握持方向与枪口朝向；不制作手掌、手指或骨骼绑定。
- 为枪身、投影窗口和发光部件分配可在 Unity 替换的材质槽。
- 输出临时 FBX 与预览图到上述目录，检查枪口轴向、镜头遮挡和画面占比。
- 定稿后仅将最终模型及其必要贴图放入 `Assets/Models/TrapPlacementProjectionGun/`；Blender 文件、迭代导出和预览仍留在中间态目录。

**完成条件**

- 第一人称预览中模型朝向正确、无遮挡关键视野，材质槽可区分。
- 最终资源与中间态文件位置符合项目约定。

### 步骤 5：生成并检查投影枪 Prefab

**状态：已完成（2026-10-20，正式材质/Prefab 生成、重复生成与独立预览验收）。**

**实施顺序（2026-10-20）**

1. 在 `TrapPlacementProjectionGunAuthoring.cs` 建立模型/材质/Prefab 路径契约和生成前检查，采用阶段四尾屏版及已验收的相机偏移；缺少资源时先报错再停止。
2. 生成八种正式 URP/Lit 材质及 `Assets/Resources/TrapPlacementProjectionGun.prefab`，保留 FBX 挂点，补第一人称挂点；生成过程不保存主场景、不修改运行时控制器。
3. 新增阶段五结构校验与正式 Prefab 预览，检查引用、Layer、变换、组件限制及重复生成的 GUID/对象稳定性，按项目版本执行 Unity 编译和批处理。
4. 记录实际结果并更新项目记忆。执行前备份已有目标资源，失败恢复；回滚只涉及本阶段新增生成器、材质和 Prefab。第一人称运行接线仍由步骤六完成。

**工作内容**

- 新增 `Assets/Editor/TrapPlacementProjectionGunAuthoring.cs`，提供可重复运行的编辑器批处理入口。
- 从 `ProjectSettings/ProjectVersion.txt` 对应的 Unity 环境执行，不写死编辑器版本。
- 从最终 FBX 生成 `Assets/Resources/TrapPlacementProjectionGun.prefab`；如现有武器引用约定更适合其他路径，则沿用该约定。
- 建立模型、枪口、投影窗口和第一人称挂点层级，设置合适的 Layer、Renderer 材质与本地旋转。
- 移除不需要的物理组件、攻击脚本及手部动画依赖；不创建骨骼约束。
- 生成后检查模型、材质、挂点和组件接线；必需资源缺失时明确报错并停止，不留下半成品引用。

**完成条件**

- 重复运行生成器结果稳定，不产生重复对象或隐式替换普通武器。
- Prefab 结构和必需引用通过自动检查。

### 步骤 6：接入第一人称显示并完成输入闭环

**工作内容**

- 将投影枪实例接到玩家第一人称武器挂点；优先运行时创建，避免直接改大型主场景 YAML。
- 进入放置模式时显示投影枪并隐藏/禁用普通武器视觉与普通射击输入；退出时完整恢复。
- 左键只调用现有陷阱放置动作；右键只触发空事件。
- 保留后续动画接入所需的组件挂点与事件接口，但不实现 Animator、IK、骨骼、握持或后坐力。

**完成条件**

- 投影枪只在放置模式显示；进入、退出和切换过程无残留隐藏/禁用状态。
- 左键放置不伴随普通射击；右键没有游戏状态副作用。

### 步骤 7：补充自动化测试与编译验证

**状态：已完成（2026-10-21，真实 Unity 编译、70 项 EditMode 用例及放置契约回归通过）。**

**工作内容**

- 增加 EditMode 测试，覆盖放置模式状态、专用武器启停、普通射击被抑制、左键继续放置，以及右键只触发预留接口。
- 验证缺少 Prefab 引用时警告只出现一次，且普通武器不会被自动替换。
- 执行项目版本对应的真实 Unity 编译；历史快速编译工程 `C:\MapForge\.validation-build\UnityCompile.csproj` 当前不存在，不再作为可执行前提。
- 使用现有陷阱放置/网格验证流程，确认合法性、费用和陷阱实例创建行为未变化。

**完成条件**

- 新增测试通过，编译检查通过；如环境缺少被忽略的第三方资源，按项目记忆记录为环境限制，不扩展处理范围。
- 现有放置行为没有回归。

### 步骤 8：进行第一人称运行验收并整理交付

**验收场景**

- 进入陷阱放置模式：只显示手枪形投影枪。
- 左键：能正常放置陷阱，且不会产生普通武器伤害。
- 右键：没有可见副作用，预留事件可被测试观察。
- 退出放置模式：普通武器显示和普通射击恢复。
- 菜单打开时视角仍冻结；模型不依赖 KINEMATION 或手部骨骼动画资源。

记录实际完成的步骤、测试结果、未完成项与预览位置；不把未运行的验收写成已通过。

## 步骤 1-2 接入记录（2026-10-19）

### 步骤 1：调用链与生命周期契约

- 唯一输入轮询入口为 `TrapPlacementController.Update`。`4/5/6/7` 先调用 `SelectTrapSlot`，再通过 `SetPlacementMode(true)` 进入；模式内切槽保持放置状态。`Esc` 通过 `SetPlacementMode(false)` 退出，组件停用也退出并清理拖放状态。
- 一次购买成功或一次拖放结束只结束该次动作，仍保留放置模式，允许继续购买；购买失败也不退出。现有行为没有“购买一次即完成并退出”的边界。
- 复用已有只读实例属性 `PlacementMode` 与静态 `IsPlacementModeActive`。后续投影枪控制器读取前者，已有 `FPSHitscanShooter` 继续读取后者；`PlacementModeChanged(bool)` 通知实际进入/退出，切槽和重复设置不发布模式切换。
- `TrapSelectionMenu.CursorOwned` 包含菜单打开及关闭当帧。菜单只暂停放置输入，不改变模式；`GameStartMenu.CursorOwned` 同样阻止操作。两份玩家控制器的 `UpdateCursor` 不变。
- 左键唯一购买链为 `Update → OnProjectionGunPrimaryAction → PlaceAt → TrapPurchaseService.TryPurchase`；拖放继续使用 `PlaceAlongLine → PlaceAt`。网格、费用、预约扣款、合法性不变。
- 普通射击结算为 `FirstPersonController.cs` 中 `FPSHitscanShooter.Update → FireHitscan`。当前代码已经在静态放置状态为真时丢弃枪械弹药变化；`SetWeaponInputSuppressed` 已屏蔽 `PlayerInput` 中射击/瞄准/换弹动作。本轮记录并沿用。
- 第一人称视角来自 `FirstPersonPlayerBootstrap` 复用或加载的 `Resources/FPSPlayer`，相机由玩家根对象 `GetComponentInChildren<Camera>(true)` 获取；`FPSHitscanShooter` 从同一根对象寻找子级 `FPSPlayer` 并调用 `GetActiveWeapon()`。现有自有脚本未提供专用投影枪挂点，后续步骤应在已解析的玩家相机下建立独立挂点；本轮不创建或绑定模型。

### 步骤 2：公开接口与输入所有权

- `IsProjectionGunInputActive`：实例启用、处于放置模式且两个菜单均不占用光标时才允许调用输入接口；它不会因没有可用网格或瞄准无效格而退出模式。
- `OnProjectionGunPrimaryAction()`：返回本次购买是否成功，复用当前悬停格及原拖放起始逻辑。没有悬停格或输入被菜单拦截时返回 `false`。
- `OnProjectionGunSecondaryAction()`：仅发布 `ProjectionGunSecondaryAction` 事件；不拆除/出售、不扣款、不切换模式。`Update` 在检查网格/相机前转发右键，因此即使未瞄准有效表面也可观察事件。
- 当前左右键由 `TrapPlacementController.Update` 调用。后续步骤 3 移交轮询时应替换该调用方，避免同一点击由两个控制器重复转发；拖放的按住/释放处理仍在原入口。
- 放置模式外的 `E` 拆除与 `Shift+E` 出售继续沿用 `RemoveTrap`；界面不再提示模式内右键拆除/出售。
- 验证结果（2026-10-19）：步骤 1 调用链核对完成，步骤 2 接口实现完成。按 `ProjectSettings/ProjectVersion.txt` 匹配本机 Unity，通过 `TrapPlacementProjectionGunContractValidation.Run` 执行 26 条断言，输出 `PROJECTION_GUN_STEPS_1_2_PASS: 26 assertions` 和成功退出标记；无 C# 编译错误或异常。
- 校验覆盖：模式进入/重复设置/切槽/取消/停用与重启、左键购买成功和占格失败、拖放逐格扣款与余额不足停止、两类菜单拦截、右键接口只发布事件。校验前后场景、材质、地图及受保护配置的 SHA256 一致，未保存生成资产。
- 边界：这是独立编辑器方法的契约校验，不是 NUnit 或 PlayMode 验收。早期校验的场景对象引用已修正；批处理鼠标帧模拟未通过，已移除该额外检查，因此真实鼠标输入与第一人称交互不记为通过。专用武器控制器、模型和完整运行验收仍属于步骤 3-8。
- 本次日志与备份位于 `C:\Users\ASUS\AppData\Local\Temp\alpha-striker-unity\projection-gun-20261019-165819\`，最终日志为 `unity-contract-final.log`。指定旧账户素材目录当前拒绝访问，本轮未制作模型或其他美术中间素材；代码校验产物使用技能规定的系统临时目录。

## 步骤 4 续作计划与验收（2026-10-20，已完成）

- 目标：确认已有基础版和尾屏补充版模型的 Unity 导入、第一人称朝向与画面占比，完成本步骤资源交付记录。
- 数据流：已有 FBX → `Assets/Editor/TrapPlacementProjectionGunModelValidation.cs` → 临时校验对象和预览；沿用模型材质槽，预览材质只在内存创建。
- 执行单元：先补模型校验工具，再运行轴向/尺寸/挂点/网格与材质槽检查，最后生成两档 FOV 预览并目视确认、更新验收记录。
- 中间产物采用已确认的当前账户路径：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/`。
- 回滚：本轮不覆盖两份 FBX，不保存场景或生成 Prefab；新增校验工具及独立预览可单独移除。
- 完成标准：Unity 编译与模型检查通过；60°/75° 第一人称预览可检查；报告明确记录实际轴向和后续挂载所需配置。Prefab 与运行时接线仍属于步骤 5-6。

### 实际交付与验证结果

- 新增 `Assets/Editor/TrapPlacementProjectionGunModelValidation.cs`，菜单 `Tools/塔防/投影枪/检查阶段四模型并生成预览`，批处理方法 `TrapPlacementProjectionGunModelValidation.Run`。通过 `PreviewRenderUtility` 在独立预览场景实例化 FBX，以内存 URP/Lit 材质按既有交接参数重建配色与发光；结束后清理对象并恢复异步着色器编译设置。
- 使用 `ProjectSettings/ProjectVersion.txt` 对应的本机已安装编辑器运行，未硬编码版本。最终执行记录为 Unity `6000.6.6f1`，编译无 C# 错误、无方法异常，日志包含 `PROJECTION_GUN_STAGE4_PASS: 226 checks` 与成功退出标记。

| 模型 | 网格 | 三角面 | Unity 导入顶点 | 材质槽种类 | 尺寸：宽 × 高 × 长（m） |
| --- | ---: | ---: | ---: | ---: | --- |
| `TrapPlacementProjectionGun.fbx` | 13 | 14,724 | 8,146 | 7 | 0.093914 × 0.206950 × 0.318850 |
| `TrapPlacementProjectionGun_ScreenUpdated.fbx` | 83 | 17,852 | 14,042 | 8 | 0.093914 × 0.206950 × 0.324500 |

- 验证包括导入配置、根变换、尺寸、有限顶点与法线、非退化三角面、材质槽、独立投影窗口、枪口与握持挂点、尾屏五个独立网格，以及无骨骼/Animator/脚本/物理组件。基础版 43 项、尾屏版 183 项通过。Unity 顶点数与 Blender 复导入统计口径不同，记录两者各自实际结果。
- Unity 实测：根位置/旋转为零、缩放为一，枪口 +Z、向上 +Y；`Muzzle = (0, 0.040, 0.210)`，`GripAnchor = (0, -0.035, -0.042)`。**不需要额外加 90° 轴修正。** 后续优先采用独立尾屏补充版；基础版保留为原始资产。
- 第一人称初始挂载偏移 `(0.140, -0.105, 0.420)` m，本地旋转 `(0, 0, 0)`，1600×900，垂直 FOV 60°/75°、近裁剪 0.05 m。两版均处于右下方，投影范围最左 X 为 0.5733 / 0.5551，避开中心瞄准区；最近几何深度为基础版 0.3100 m / 尾屏版 0.30435 m。60° 下握把底端出画属于构图裁切。
- 六张 Unity 预览已逐张目视确认：两版各有 60°/75° 第一人称与斜后视图；配色、发光和尾屏方向正确。首帧着色器回退问题已通过同步编译与预热渲染修正。
- 预览与报告目录：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage4-unity/`，报告 `validation-report.json`。最终日志：同素材根目录 `stage4-unity-validation-final.log`。
- 执行前备份：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/backups/stage4-unity-20261020-201222/`。执行前后两份 FBX、主场景与 MapForge 地图 JSON 的 SHA256 一致。
- 验证边界：完成阶段四模型确认；本轮没有生成正式 URP 材质资产/Prefab，没有运行 PlayMode 或验收真实玩家输入。阶段三此前未完成的运行验收仍单独保留，阶段五至八尚待推进。

## 步骤 5 实际交付与验收（2026-10-20）

- 新增 `Assets/Editor/TrapPlacementProjectionGunAuthoring.cs`。菜单 `Tools/塔防/投影枪/生成阶段五材质和 Prefab`，批处理方法 `TrapPlacementProjectionGunAuthoring.CreateAssetsBatch`。公开路径常量供校验复用；正式资源由生成器维护，不手工调整生成物。
- 来源为阶段四独立尾屏版 `Assets/Models/TrapPlacementProjectionGun/TrapPlacementProjectionGun_ScreenUpdated.fbx`，原始基础版与尾屏版 FBX 均保留不变。生成 `Assets/Resources/TrapPlacementProjectionGun.prefab` 和 `Assets/Models/TrapPlacementProjectionGun/Materials/` 下八种 `PG_*.mat`，使用 URP/Lit，沿用已验收的色彩、粗糙度、金属度及发光强度。
- Prefab 根 `TrapPlacementProjectionGun` 位置/旋转为零、缩放为一；唯一子节点 `FirstPersonMount` 保存相机相对偏移 `(0.140, -0.105, 0.420)` m，旋转为零。其下 `Model` 为原 FBX 的嵌套实例，本地位置/旋转为零、缩放为一；保留唯一 `Muzzle`、`GripAnchor`、独立 `ProjectionWindow` 及五个尾屏网格。阶段六把 Prefab 根接到相机/独立挂点时，不要再重复施加上述偏移。
- 全层级采用内置 `Ignore Raycast`（Layer 2），不创建新 Layer；Renderer 关闭投影/接收阴影及运动矢量。只有 Transform、MeshFilter、MeshRenderer，无 Collider/Rigidbody、攻击脚本、Animator、IK 或骨骼约束。
- 生成前检查模型、URP Shader、八种材质槽及输出资产类型。写入前将已有目标资源及 `.meta` 备份到素材目录；保存前后检查结构，生成异常时恢复原有目标资源并移除本次新建资产。仅保存本生成器拥有的资产，不保存主场景。失败恢复分支未单独进行故障注入测试。
- 材质定义由生成器统一提供，阶段四预览也复用此定义，避免两份配色漂移。既有材质符合定义时跳过重写；需要更新时复制材质属性，保留 Unity/URP 所持有的版本元数据。完成同步导入后再返回资源。
- 新增 `Assets/Editor/TrapPlacementProjectionGunPrefabValidation.cs`。菜单 `Tools/塔防/投影枪/生成并验收阶段五 Prefab`，批处理 `TrapPlacementProjectionGunPrefabValidation.Run`，使用技能脚本的 `-WithGraphics`。校验实际生成资源，连续生成两次并比较 GameObject/组件本地 fileID、Prefab/八种材质/源模型及对应 `.meta` 的 SHA256；检查嵌套 FBX、材质引用、挂点、几何尺寸、Layer、组件和第一人称构图。
- 按 `ProjectSettings/ProjectVersion.txt` 匹配本机编辑器执行，日志确认编译无 C# 错误、无方法异常，包含 `PROJECTION_GUN_STAGE5_PASS: 577 checks` 与 `Exiting batchmode successfully now!`。快速编译项目 `C:/MapForge/.validation-build/UnityCompile.csproj` 当前不存在，以本次真实 Unity 编译为准。
- 八种正式材质、Prefab 及其 GUID/对象引用在重复生成时稳定；Prefab 包含 83 个网格、17,852 个三角面，宽/高/长为约 `0.093914 / 0.206950 / 0.324500` m。依赖仅包括本 Prefab、源 FBX、八种材质、URP Shader 及 URP 编辑器 `AssetVersion.cs` 元数据，不依赖项目武器脚本、KINEMATION 或动画资产。
- 三张正式 Prefab 预览已目视确认：60°/75° 第一人称画面均位于右下方且避开中心瞄准区，最近几何清除近裁剪面；斜后视图中尾屏朝向、文字方向和发光正确。60° 时握把底端的构图裁切沿用阶段四验收结果。
- 输出目录：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage5-unity/`。包含 `prefab-first-person-60.png`、`prefab-first-person-75.png`、`prefab-rear-three-quarter.png`、`validation-report.json`、`protected-before.json`、`protected-after.json`。最终成功日志 `stage5-acceptance-final.log`；早期失败日志保留供回溯。
- 执行前场景/地图/模型备份为上述目录 `session-backup-20261020-202710/`；每次生成的目标资源备份在 `backups/<时间戳>/`。两份 FBX、主场景、地图 JSON 及其元数据、项目版本/Layer/构建设置、包配置共 13 个受保护文件的执行前后 SHA256 一致。
- 回滚方式：移除本阶段新增的生成器/验收脚本及 `.meta`、正式 Prefab/八种材质及其 `.meta`；若目标资产在生成前已存在则从对应备份恢复。模型、无人机资源及其他已有修改不属于回滚范围。
- 完成边界：本阶段交付可复用的正式视觉资产，未修改运行时控制器、主场景或 MapForge 数据。真实玩家显示、普通武器隐藏/恢复、输入闭环和 PlayMode 验收仍属于阶段三未完成的运行验收及步骤六至八。

## 建议提交顺序

1. 步骤 1-2：状态查询、输入契约与空右键接口。
2. 步骤 3：控制器骨架、普通射击隔离及占位对象验证。
3. 步骤 4-5：最终模型、编辑器生成器与 Prefab。
4. 步骤 6：真实模型的运行时显示和输入接线。
5. 步骤 7-8：自动化测试、编译、运行验收与交付记录。

## 暂不做

- 不绑定手部骨骼动画、IK、握持姿态或手指动画。
- 不增加伤害、弹药、换弹或普通枪械后坐力。
- 不重写陷阱网格、费用、购买或放置合法性系统。
- 不修改 MapForge 地图数据。

## 步骤 6 实际交付（2026-10-21）

- 修改 `Assets/Scripts/TowerDefense/TrapPlacementProjectionGunController.cs`：Inspector 未指定 Prefab 时从 `Resources/TrapPlacementProjectionGun` 自动加载正式 Prefab；仍不存在时保留一次性警告且不替换普通武器。实例挂在显式 `projectionGunAnchor` 或 `Camera.main` 下，Prefab 自带 `FirstPersonMount` 偏移，因此不重复施加相机偏移。
- 进入放置模式时启用投影枪实例并隐藏当前 `FPSPlayer.GetActiveWeapon()` 下所有 Renderer，同时记录各 Renderer 原启用状态；退出放置模式、控制器停用时关闭投影枪并逐项恢复原状态。菜单/开局菜单及左键放置、右键空事件仍沿用既有输入门控和 API。普通射击输入沿用 `TrapPlacementController.SetWeaponInputSuppressed`，伤害边界继续由 `FPSHitscanShooter` 的放置模式检查保护。
- 本步骤完成代码接线；未执行 EditMode/PlayMode 或 Unity 实机验收。缺少 Prefab 回退、武器切换中状态恢复等自动化覆盖，以及真实第一人称显示/左右键行为，留待步骤七至八验证。

## 步骤 7 实际交付与验证状态（2026-10-21）

- 新增 `Assets/Editor/TrapPlacementProjectionGunStage7Tests.cs`，覆盖放置模式下投影枪启停、缺少 Prefab 时仅警告一次且不创建替代物、右键预留事件及菜单门控，以及 `FPSHitscanShooter.FireHitscan` 的放置模式伤害边界。现有 `TrapPlacementProjectionGunContractValidation` 还覆盖左键购买/费用/占格、重复放置拒绝、拖放购买和右键无经济/拆除副作用。
- 指定快速编译命令未能执行：`C:\MapForge\.validation-build\UnityCompile.csproj` 当前不存在。随后用项目版本匹配的 Unity 6000.6.6f1 启动 EditMode 测试，但在测试发现/执行前全项目脚本编译失败，`TrapPlacementProjectionGunController.cs` 报 `FPSPlayer`、`FPSWeapon` 类型缺失；因此本阶段自动化测试未通过，不能记录为已通过。缺失类型属于项目记忆所述未随仓库提供的 KINEMATION 依赖边界，本阶段不扩展修复第三方依赖。
- 现有陷阱放置网格回归验证也受同一项目编译错误阻断；未运行 PlayMode 或实机验收（后者属于步骤八）。步骤七测试代码已写入工作区，但完成条件中的“测试及编译通过、现有放置行为无回归”仍待恢复依赖后重跑。

## 步骤 7-8 续作结果（2026-10-21）

- 纠正此前失败归因：`FPSPlayer` / `FPSWeapon` 并非本机 KINEMATION 包缺失，而是阶段六控制器漏写两个 KINEMATION 命名空间引用；现有代码已补齐并由后续 Unity 批处理编译确认。
- 新增 `Assets/Editor/TrapPlacementProjectionGunStage78Validation.cs`，批处理入口 `TrapPlacementProjectionGunStage78Validation.Run`。隔离场景检查实例惰性创建、模式进入显示/退出隐藏、没有替代对象、右键仅发布预留事件、菜单拦截输入等 10 条断言；不保存场景或资源。一次性缺 Prefab 警告抽为私有方法，以便不移动/删除现有正式 Resources Prefab 的条件下直接验证警告只触发一次。
- Unity 6000.6.6f1 批处理编译成功；`TrapPlacementProjectionGunContractValidation.Run` 输出 `PROJECTION_GUN_STEPS_1_2_PASS: 26 assertions`；新增验证输出 `PROJECTION_GUN_STAGE78_PASS: 12 assertions`，覆盖 `FPSHitscanShooter.FireHitscan` 在放置模式的安全边界；均成功退出。日志分别为 `C:/Users/ASUS/.codex/visualizations/2026/10/21/01a15cb5-4be9-73c3-8652-b3d4467bf91f/projection-gun-contract-final.log` 与 `stage78-validation-final5.log`。
- 阶段七 NUnit 调用返回码为 0，但没有产生 Test Results XML 或测试发现记录，故不能记作 NUnit 通过；现有网格回归也未运行。不能将两项契约检查替代 NUnit、伤害边界回归或网格回归。
- 步骤八未完成：没有在真实玩家 Play Mode 中确认普通武器 Renderer 隐藏/恢复、真实左键购买与伤害隔离、鼠标右键无副作用、菜单视角冻结及最终画面；批处理契约测试不等同于实机验收。需在可交互 Unity Editor 中完成这些项目后才能将阶段八标记为通过。

## 步骤 7 最终验收（2026-10-21，已完成）

本节取代上文阶段七的“受阻/未生成 XML/网格未回归”历史结论；阶段八保持未验收。

- 永久测试入口：`Assets/Editor/RunProjectionGunStage7Tests.ps1`（含 Unity `.meta`）。依据 `ProjectSettings/ProjectVersion.txt` 精确定位已安装 Unity，拒绝并发编辑器，保留超时保护；`-runTests -testPlatform EditMode -extraScriptingDefines=UNITY_INCLUDE_TESTS` 不带 `-quit`，让异步 Test Runner 写完 XML 后自行退出。临时 define 不修改项目设置。零用例、缺 XML、未执行指定 fixture、失败/跳过和编译错误都拒绝标为通过；不能只看进程退出码。
- 投影枪测试扩至 13 项：显示启停、实例复用、停用后的 Renderer 快照恢复和重新订阅、Resources 正式资源/挂点、缺资源完整显示回退分支及一次性警告、普通攻击/瞄准/换弹动作抑制和精确恢复、左键购买与拖放收费、右键无出售/拆除副作用、两个菜单门控，以及物理靶标放置时免伤且退出后恢复普通射击伤害。
- 控制器只提取私有 `ShowResolvedProjectionGun(GameObject)`，分离资源解析和显示应用，支持不搬移/删除正式资源的缺失分支测试；未修改公开接口或原有输入/购买/伤害规则。普通武器 Renderer 测试使用状态快照，不冒充完整第三方玩家实机链路。
- 修正旧回归测试：墙格坐标按当前格心契约断言，类别提示按目标墙面断言；伤害分类用例通过 `ResetHealth()` 显式初始化 EditMode 中不会自动 Awake 的对象。未改网格或伤害运行时代码。
- 连续两轮 NUnit 全绿：**70/70，通过 70、失败 0、跳过 0**。投影枪 13、购买 34、墙面网格 7、武器伤害档案 10、伤害分类 6；最终 XML 中五个 fixture 均实际执行。真实 Unity 编译通过，最终测试进程退出码 0。
- 最终结果：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage7-unity/20261021-130014-416/editmode-results.xml`；同目录 `summary.json`、`editmode.log` 和标准输出/错误。脚本默认路径（含中文目录）已在 Windows PowerShell 5.1 实际执行验证，脚本采用带 BOM 的 UTF-8。
- 不加测试 define 的普通 Unity 编译与 `TrapPlacementProjectionGunContractValidation.Run` 也通过，日志含 `PROJECTION_GUN_STEPS_1_2_PASS: 26 assertions`、`Exiting batchmode successfully now!`，无 C# 错误/方法异常。日志：`C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage7-unity/20261021-125802-201/placement-contract.log`。
- 未改场景、地图、模型、Prefab 或包配置，保留其他已有未提交修改。阶段八真实第一人称鼠标交互、第三方普通武器显隐完整链路和菜单视角冻结仍需单独实机验收。

## 步骤 8 自动化实机链路测试（2026-10-22，未通过）

- 新增 `Assets/Editor/TrapPlacementProjectionGunStage8Tests.cs` 与 `Assets/Editor/RunProjectionGunStage8Tests.ps1`。测试从主场景进入 Play Mode，使用 Input System 设备事件驱动现有控制器轮询，覆盖真实玩家相机/普通武器、投影枪显隐、左键购买、放置时普通射击隔离、右键无出售/拆除副作用、退出后普通武器恢复射击，以及陷阱菜单的光标拥有与视角冻结。
- Unity 6000.6.6f1 已实际启动多轮 NUnit 运行。前几轮分别发现并修正了测试对相机层级、玩家根对象上的 `FPSHitscanShooter` 以及全局输入设备的错误假设；这些轮次均为测试失败，不计入阶段八通过。
- 最后一次运行目录为 `C:/Users/ASUS/Desktop/陷阱素材文件夹/TrapPlacementProjectionGun/stage8-unity/20261022-133118-731/`，因 `InputSystem.Update(InputUpdateType.Dynamic)` 在本项目 Input System 1.20.0 中不存在该重载而在编译阶段失败，未生成 NUnit XML。之后移除了手动 `InputSystem.Update()`，改为在自动输入更新后的 `InputSystem.onAfterUpdate` 临时泵送轮询器；这一版未重新运行。
- 当前结论：阶段八**未通过**；必须在下一轮先运行 `Assets/Editor/RunProjectionGunStage8Tests.ps1`，确认生成 XML 且 fixture 零失败/零跳过后，才能记录实机验收通过。未修改运行时玩法、场景、Prefab 或地图数据。
