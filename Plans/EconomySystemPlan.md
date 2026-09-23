# Plan：局内经济系统

状态：六项实施任务全部完成（2026-09-22），最终综合回归 85 项通过、0 项失败。

第一项交付：`EconomyManager.cs` 提供默认 100 金币的运行时启动、可配置初始金额、`BeginRun`、`CanAfford`、`TrySpend`、`Grant` 和 `BalanceChanged`。重复获取钱包不重置余额；每次显式开局生成新的 `Guid RunId`（作为本局标识，不依赖可能重置的递增计数）。负数及溢出操作不会改变余额；关闭 Domain Reload 时清理旧局状态与订阅。

第一项验证：Unity 6000.6.2f1 编译及 `EconomySystemTests` EditMode 测试通过，9 项通过、0 项失败。测试结果：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step1\results.xml`。

第二项交付：`EnemySpawnPoint` 及其 Inspector 新增奖励覆盖配置（允许 0），未覆盖时地面怪 10、飞行怪 15；按最终怪物类型在启动移动前保存奖励和本局标识。`EnemyInstance` 订阅 `EnemyHealth.Died`，通过 `Alive / Killed / Leaked / Despawned` 终态互斥结算，停止移动并取消订阅；漏怪、清理与旧局回调不发钱，复用初始化同步重置生命及奖励状态。余额溢出时拒绝奖励但不阻断怪物销毁。

第二项验证：Unity 6000.6.2f1 编译通过；`EconomySystemTests;FlyingSpawnPointTests` 共 13 项通过、0 项失败。其中生命周期用例进入 Play Mode，使用真实致命伤害及路径到达事件验证重复击杀、地面/飞行漏怪、生成即到达核心、覆盖奖励快照、零奖励、负数配置、旧局回调、复用和清理。首轮测试夹具跨 Play Mode 重载的空引用已修复；最终报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step2-20260922-115435\results.xml`。

第三项交付：新增 `TrapPurchaseService.TryPurchase`，玩家单次放置及拖动连放统一通过购买服务。钱包增加按本局标识绑定的费用预留与可用余额，预留不发送余额事件，成功初始化实例及占格后才提交扣款；失败释放预留，创建异常清理部分对象，换局期间创建的陷阱撤销且不扣新局余额。网格在创建回调后重新校验，防止同格重入覆盖占格；通知订阅者异常不撤销已完成购买。拖动按成功实例收费，资金不足立即停止；`LastPlacementFailure` 暴露失败原因供第四项 UI 使用。编辑器及场景预置沿用不收费的底层网格接口，拆除不退款。运行时玩家没有原子批量购买入口，现有批量接口仅保留为底层布置工具。

第三项验证：Unity 6000.6.2f1 编译通过；`TrapPurchaseTests;EconomySystemTests;GroundEnemyCombatTests` 共 39 项通过、0 项失败。覆盖足额/恰好支付、余额不足、无效/重复格、零价/负价、缺失/未初始化钱包、创建异常、重入资金保护、同格竞争的部分对象清理、换局回滚、通知异常、控制器连放、编辑器免费批量及拆除不退款。测试辅助组件仅在 Editor 且启用测试时编译；首轮夹具挂载和 Unity 空对象断言问题已修复。最终报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step3-20260922-120442\results.xml`。HUD、价格配置及最终综合验收仍按第四至六项实施。

第四项交付：新增自动创建的 `EconomyHUD`，在波次面板下方显示金币；新增 `EconomyUIState` 统一钱包订阅，HUD、菜单及放置反馈随余额事件更新，换局/换场景时重新绑定，禁用/销毁时解除订阅。菜单显示余额、价格及“金币不足”，仍允许选择和查看买不起的陷阱；预览模型与目标格按能否购买变色，点击时重新走购买事务并提示差额，连放停止提示也显示差额。放置提示移到 HUD 下方避免遮挡；菜单沿用 `CursorOwned`，关闭当帧不处理放置输入。没有钱包时显示“金币：--”，不创建或补发钱包。

第四项验证：Unity 6000.6.2f1 编译通过；`EconomyUITests;TrapPurchaseTests;EconomySystemTests` 共 33 项通过、0 项失败（报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step4-20260922-124544\results.xml`）。覆盖实时奖惩/购买余额、足额及不足提示、预览颜色、无支付的菜单选择、关闭当帧光标所有权、重复启用订阅去重、禁用清理、同钱包重开、关闭 Domain Reload 后恢复，以及真实卸载钱包场景后绑定新钱包。批处理屏幕截图不可用，改为 Game View 离屏渲染；修复图像尺寸、方向及 URP 延迟帧读取后，8 项 UI 用例复验通过（报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step4-20260922-124955\results.xml`），并检查三张 1280×720 实际渲染图。预览目录：`C:\Users\Origami\Desktop\陷阱素材文件夹\EconomyUI\20260922-step4\`，包含 `placement-insufficient.png`、`placement-affordable.png` 和 `menu-insufficient.png`。测试中的 40 金币价格仅写入临时定义；正式陷阱价格仍留给第五项。

第五项交付：核对现有三种正式陷阱、五份定义资产，原价格均为 0，无已有正价需要保留。本次按基础调试价迁移为 40 金币：哨戒机枪、双联导弹发射器、地刺陷阱各 40；机枪及导弹发射器在 `Assets/Resources` 与 `Assets/TrapDefinitions` 的两份定义保持一致。`TrapDefinition.DefaultCost = 40` 作为新建定义的默认值，实际支付仍只读取资产 `Cost`，保留手工显式设置 0 的免费能力。地刺生成器新建定义时继承默认价格、重接已有定义时保留价格；导弹生成器只更新预制体引用，不重置价格。未发现独立的机枪定义生成器，编辑器新建定义沿用统一默认值。

第五项验证：五份 `.asset` 仅按字段定位核对价格，均为 40；Unity 6000.6.2f1 编译通过，既有 `TrapPurchaseTests;EconomyUITests` 共 23 项通过、0 项失败，覆盖 100 买 40 后剩 60、恰好支付、余额不足、连放停止、回滚、显式免费及 UI 差额。未重生成模型或预制体。报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step5-20260922-125606\results.xml`。40 为首版调试价格，尚不代表三种陷阱强度相同或已完成平衡；第六项的最终综合验收仍待实施。

第六项交付：新增 `EconomyAcceptanceTests.cs`，加载正式 `THREE_ROUTE_MERGE_MAP` 运行主场景自动验收，使用正式资源和物理命中链验证经济闭环。修复 `FlyingSpawnPointTests` 未清理生成怪物及测试钱包的问题，避免遗留飞行怪污染后续导弹选靶测试。新验收用例显式等待场景加载完成，并尊重机枪已有的 `AutoSentryTurret` 运行时模型兜底；保存的主场景有三处地面出怪口，飞行奖励场景在测试中临时增加飞行出怪口，使用正式飞行怪物资源，不把测试对象写回场景。

第六项发现并修复的场景问题：旧道路网格在 Y=0.20，实际路面 Y=0.10；旧地面网格在 Y=0.02，实际地面 Y=-0.02，均超出地刺的贴地校验。主场景只改两处 `placementHeight`：道路 0.12、地面 0，保持“物理表面 + 0.02 米”。新增 `TrapGridAuthoring.RoadPlacementHeight`，复用已有表面测高函数，让网格编辑器及 MapForge 导入器按实际道路表面生成高度；道路网格识别同步兼容其名称。不修改放置掩码、不放宽地刺支撑规则、不改怪物路径高度。

第六项最终验证：Unity **6000.6.2f1** 编译通过，`EconomyAcceptanceTests;EconomySystemTests;EconomyUITests;TrapPurchaseTests;FlyingSpawnPointTests;GroundEnemyCombatTests;MissileLauncherTurretTests;MissileLauncherWeaponTests;TrapHealthBarUITests;WeaponDamageProfileTests` 共 **85 项通过、0 项失败**。最终报告：`C:\Users\Origami\AppData\Local\Temp\alpha-striker-unity\economy-step6-20260922-130921\results.xml`。首轮 81/85、第二轮 84/85 的问题均已定位并修复；未因时限放弃测试。

主场景自动验收已验证：启动 100 → 玩家真实 hitscan 命中击杀地面怪 110 → 路径到达触发漏怪仍 110、核心扣 2 血 → 两次购买正式机枪后 70 / 30 → 第三次拒绝、提示还差 10 → 波次切换及暂停仍 30 → 导弹直接命中和范围伤害击杀两只飞行怪后 60，重复引爆不重复奖励 → 正式导弹发射器、地刺均可购买，拆除不退款 → 重新加载主场景恢复 100，HUD 和菜单一致且不重复创建。购买异常/回滚、旧局奖励、缺失钱包、显式免费等边界由同次回归覆盖。

网格数值校验：两套网格均与怪物网格对齐，无悬空、无缺失地面，覆盖 4608/4608 格、重叠 0 格，地面网格不占怪物走道。校验器另外报告 14 个“被上方物体遮住”的格子（地面 6、道路 8）；打印的 6 个地面格与现有出怪口标记位置一致，此项作为现有场景布局信息保留，本轮未调整标记或可放置范围。三种正式陷阱的购买验收全部通过。

场景修改前备份：`C:\Users\Origami\Desktop\陷阱素材文件夹\EconomyAcceptance\20260922-step6\THREE_ROUTE_MERGE_MAP.before-height-fix.unity`。实际 Game View 预览：`C:\Users\Origami\Desktop\陷阱素材文件夹\EconomyAcceptance\20260922-step6\previews\main-scene-ground-spike.png`，已检查贴地效果与波次/核心/金币显示；同目录保存重新渲染的余额不足、足额和菜单预览。最终验收为自动运行态验证，未将其描述为人工通关；数值平衡仍使用首版调试值。

## 目标与首版范围

形成「少量开局资金 → 放置陷阱/玩家射击 → 击杀赚取金币 → 继续布防」的循环。漏怪失去该怪物的奖励，同时保留现有核心扣血规则。

首版实现局内余额、击杀奖励、陷阱购买、余额显示和关键回归验证。暂不加入跨局存档、利息、波次补贴、出售退款、陷阱升级或多人经济；奖励飘字和音效为后续可选项。

## 游戏规则与暂定数值

| 项目 | 首版规则 |
| --- | --- |
| 开局金币 | 暂定 100，每局一次；波次切换和暂停不重置 |
| 普通地面怪 | 暂定击杀奖励 10 |
| 飞行怪 | 暂定击杀奖励 15 |
| 击杀归属 | 玩家武器与玩家陷阱击杀均计入同一个局内钱包 |
| 漏怪 | 奖励 0，不额外扣金币；核心仍按现有逻辑受伤 |
| 陷阱价格 | 唯一来源为现有 `TrapDefinition.Cost` |
| 支付 | 余额足够且放置成功才形成最终扣款；余额等于价格时允许购买 |
| 失败与取消 | 取消预览、无效位置、余额不足、创建失败均无净扣款 |
| 金额 | 非负整数；负数配置报错，0 费用允许作为明确配置 |
| 移除陷阱 | 首版不退款；被怪物摧毁也不退款 |

100/10/15 是初始调试数值，不代表已完成平衡。第五项已将原先价格为 0 的三种正式陷阱配置为 40，使 100 金币可购买 2 个陷阱、剩余 20；已有资产价格是最终支付依据，生成器不会覆盖之后的有效定价。

| 正式陷阱 | 当前调试价 | 定义位置 |
| --- | --- | --- |
| 哨戒机枪 | 40 | `Assets/Resources/AutoSentryTurret.asset`、`Assets/TrapDefinitions/AutoSentryTurret.asset` |
| 双联导弹发射器 | 40 | `Assets/Resources/DualMissileLauncherLoaded.asset`、`Assets/TrapDefinitions/DualMissileLauncherLoaded.asset` |
| 地刺陷阱 | 40 | `Assets/Resources/GroundSpike.asset` |

`// TODO(确认): 首版暂用开局 100、地面怪 10、飞行怪 15，后续根据首波怪物数量、漏怪率和各陷阱表现调整。`

## 已确认的项目入口

以下路径均相对于工程根目录 `C:\Unity Project\ProjectAlphaOperationStriker`。

| 文件/入口 | 现状与计划接入 |
| --- | --- |
| `Assets/Scripts/TowerDefense/EnemyHealth.cs` | 已有 `Died` 事件和死亡去重；使用事件结算，不能使用 `OnDestroy` 发钱 |
| `Assets/Scripts/TowerDefense/EnemyInstance.cs` | `HandleArrived` 扣核心血并销毁；在扣血前完成漏怪状态结算 |
| `Assets/Scripts/TowerDefense/EnemySpawnPoint.cs` | 生成并初始化实例；在开始移动前写入奖励及本局标识 |
| `Assets/Scripts/TowerDefense/TrapPlacementController.cs` | `PlaceAt` 是已确认的玩家放置入口；接入购买服务 |
| `Assets/Scripts/TowerDefense/TrapPlacementGrid.cs` | 已有 `CanPlaceTrap`、`TryPlaceTrap` 和批量放置；复用占格规则，保留编辑器布置能力 |
| `Assets/Scripts/TowerDefense/TrapDefinition.cs` | 已有 `Cost`；沿用价格字段 |
| `Assets/Scripts/TowerDefense/TrapSelectionMenu.cs` | 已显示花费，且跨场景保留；补余额及不足提示，换局时重新绑定钱包 |

## 实现设计

### 1. 局内钱包

新增 `Assets/Scripts/TowerDefense/EconomyManager.cs`，集中管理：

- `Balance`：只读余额。
- `BeginRun(startingMoney)`：由新一局入口调用一次；生成新的本局标识。首版初始资金用序列化字段配置，无须先建立新的关卡配置体系。
- `CanAfford(amount)` / `TrySpend(amount)`：校验金额和余额，禁止负余额。
- `Grant(amount)`：增加合法奖励，防止整数溢出。
- `BalanceChanged`：交易完成后通知 UI。

初始化必须早于首个怪物结算和首次购买。多个出怪口共用一个钱包，不得各自发开局金币。若钱包缺失或尚未初始化，购买失败并给出可诊断提示，不能降级为免费放置。

重开时销毁/失效旧局怪物，再创建或重置钱包；奖励携带本局标识，旧局回调不能给新局发钱。重新运行且关闭 Domain Reload 时也应重置静态引用及事件。

### 2. 击杀与漏怪的一次性结算

在 `EnemyInstance` 维护 `Alive / Killed / Leaked / Despawned`，只有 `Alive` 能进入终态：

1. 生成时完成类型、奖励值、本局标识及死亡订阅，再启动寻路/飞行。路径初始化可能立即到达核心，必须覆盖此情况。
2. 收到 `EnemyHealth.Died` 且实例仍为 `Alive` 时，先标记 `Killed`、停止移动与核心到达结算，再发一次奖励。不要把发钱代码散落到武器、炮塔或导弹中。
3. `HandleArrived` 确认到达核心且仍为 `Alive` 时，先标记 `Leaked`，再扣核心血和销毁。漏怪后的延迟致命伤害不再发钱。
4. 同帧死亡与到达按第一个成功终态结算；死亡先发生则奖励且不扣核心血，漏怪先发生则扣核心血且不奖励。
5. 场景卸载、直接清理、对象回收不算击杀；取消订阅。若复用对象，奖励状态必须和 `ResetHealth` 一起在重新生成时重置。

首版在 `EnemySpawnPoint` 增加可配置奖励覆盖值，未覆盖时按地面/飞行类型采用暂定奖励；生成后将金额快照写入实例。后续若建立怪物定义资产，再迁移配置，避免首版引入重复配置系统。

### 3. 购买与放置事务

新增 `Assets/Scripts/TowerDefense/TrapPurchaseService.cs`，作为玩家放置的统一付费入口，`TrapPlacementController.PlaceAt` 调用它：

1. 用现有 `CanPlaceTrap` 校验位置、占地和网格规则，再检查余额。
2. 在一次同步事务内预留费用并调用 `TryPlaceTrap`；陷阱和占格初始化全部成功后提交扣款，最后通知 UI。
3. 返回失败或异常时，清理部分创建的实例、释放占格、撤销预留款；余额不变。预留期间资金不可被另一笔购买重复使用。
4. 菜单选择与预览不扣款；确认放置必须再次校验，不能仅依赖菜单灰态。
5. 若连续绘制可放置多个陷阱，按每个成功实例分别收费，余额不足立即停止。原子批量购买若存在，则整批预留、整批提交或回滚，避免逐项与批量重复扣费。

编辑器布置和场景预置陷阱继续使用底层网格接口，不扣玩家金币。实施时检查所有运行时玩家放置调用，确保都经过购买服务；保留现有道路/空中目标/陷阱占地规则。

### 4. 玩家反馈

新增 `Assets/Scripts/TowerDefense/EconomyHUD.cs` 显示「金币：100」，沿用项目现有运行时 HUD 方式。

陷阱菜单显示当前余额及价格，余额不足时标记「金币不足」。仍允许查看陷阱信息；预览显示不可购买状态，点击放置时显示差额。钱包变化立即刷新，换场景重新订阅，销毁/禁用时取消订阅。沿用 `TrapSelectionMenu.CursorOwned` 的视角和射击锁定约定。

## 实施任务（每项不超过 15 分钟，以文件修改收尾）

- [x] 1. 新增钱包类、初始化入口和余额事件；补金额边界测试。
- [x] 2. 修改 `EnemyInstance` / `EnemySpawnPoint` 接入奖励配置、死亡事件及漏怪互斥状态；补结算测试。
- [x] 3. 新增购买服务并修改 `TrapPlacementController`；补成功支付、失败回滚与连放测试。
- [x] 4. 新增 HUD，更新陷阱菜单的余额与不足提示；处理换局订阅。
- [x] 5. 核对并配置现有陷阱价格及相应编辑器生成器默认值；只定位读取需要修改的资产片段。
- [x] 6. 完成编译和针对性回归，修复验证发现的问题并记录结果。

每项结束汇报文件改动摘要。前 4 项即组成包含漏怪保护的最小闭环；配置迁移、动画和音效不阻塞首版。实施若到 30 分钟，不再启动新测试，完成已启动测试后转为最小补丁和验证记录，列出未执行项。

## 验证与验收

针对性测试放在 `Assets/Editor/EconomySystemTests.cs` 与 `Assets/Editor/TrapPurchaseTests.cs`，沿用 `#if UNITY_INCLUDE_TESTS`；使用 EditMode 测试并对真实怪物事件及网格放置做集成验证。

| 用例 | 期望结果 |
| --- | --- |
| 开局及多出怪口 | 只初始化一次，余额 100 |
| 击杀地面/飞行怪 | 分别增加配置奖励；玩家武器和陷阱均适用 |
| 重复致命伤害/范围爆炸 | 每只怪物最多奖励一次，多个怪物各自结算 |
| 漏怪后延迟死亡 | 核心正常受伤，奖励 0 |
| 死亡后延迟到达 | 已奖励一次，核心不受该怪物伤害 |
| 生成即抵达核心 | 记作漏怪，无奖励 |
| 清理、回收与重开 | 无清理奖励，旧局事件不污染新余额 |
| 100 金币购买 40 金币陷阱 | 实例及占格有效，余额 60 |
| 40 金币购买 40 金币陷阱 | 允许，余额 0 |
| 39 金币购买 40 金币陷阱 | 拒绝，无实例/占格/扣款 |
| 无效格、取消、创建异常 | 无净扣款，无残留占格或实例 |
| 连续购买及批量失败 | 不超支、不重复扣款；原子失败全部回滚 |
| 菜单、HUD、换局 | 余额一致，事件不重复订阅；新局重置，波次不重置 |
| 编辑器预置陷阱 | 不需要钱包，不消费局内金币 |

主要场景 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity` 验收路径（第六项已由自动 Play Mode 用例覆盖，也可手动复验）：开局 100 → 杀地面怪 110 → 漏怪仍 110 → 放置调试价格 40 的陷阱后 70 → 再放一个后 30 → 第三个购买失败仍 30 → 重开恢复 100。

校验范围限于经济、奖励和放置流程，兼顾飞行怪、现有网格规则与菜单锁视角。按项目技能执行编译/Unity 测试；第三方包缺失时记录实际阻塞，不声称测试通过。
