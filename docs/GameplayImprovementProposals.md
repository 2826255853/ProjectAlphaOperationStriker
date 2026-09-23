# 玩法改进建议（2026-09-23）

本文件记录对 `ProjectAlphaOperationStriker`（FPS + 塔防）现状的玩法层面审查结论。
每条都写清「现状证据 → 方案 → 涉及文件 → 工作量 → 风险」，并标注本轮是否已落地。

## 现状速览（已核实）

| 系统 | 位置 | 现状 |
| --- | --- | --- |
| 经济 | `Assets/Scripts/TowerDefense/EconomyManager.cs` | 单钱包、开局 `startingMoney = 100`、支持预留（`TryReserve`）与提交回滚 |
| 收入 | `EnemyInstance.HandleDied` | **只有击杀奖励**：地面 10 / 飞行 15（`EnemySpawnPoint.GetKillReward`） |
| 波次 | `EnemySpawnPoint` + `WaveManager` | 全局 `totalWaves`、逐波 `enemyCount`/`prefab`、波间等待、G 键跳过 |
| 胜负 | `GameFlowManager` | 核心被拆 → 失败；所有出怪口波次完成且无存活敌人 → 胜利 |
| 陷阱 | `TrapInstance` / `TrapPurchaseService` / `TrapSaleService` | 可买；**拆解不退**（`RemovalDoesNotRefundPurchase` 语义未改），**出售退一半**（默认 `RefundRatio = 0.5f`，可被养成钩子提升到全额） |
| 玩家 | `PlayerHealth` | 有 `Died` 事件，但**没有任何运行时订阅者**（仅测试引用） |

金币吞吐的现实：10 只地面怪 = 100 金币，而一个基础陷阱 40（`TrapDefinition.DefaultCost`）。
即**一整波清完只够买 2.5 个陷阱**，且第 2 波之后没有任何额外收入来源，节奏会卡死。

---

## P0：已在本轮实现

### 1. 波次通关奖励 + 提前开波奖励

- **问题**：除击杀外无收入，中期买不起新陷阱；而「G 跳过等待」纯赚时间、没有收益取舍。
- **方案**：新增 `Assets/Scripts/TowerDefense/WaveRewardService.cs`（自建 HUD/服务同款 `RuntimeInitializeOnLoadMethod` 模式，无需改场景）。
  - 出怪口跨过一波时结算 `firstWaveReward + rewardGrowthPerWave x (wave - 1)`，默认 25 / 35 / 45。
  - 玩家按 G 提前开波时，按「省下的等待秒数 x `earlyCallBonusPerSecond`(2)`」发额外奖励，上限 `maxEarlyCallBonus`(40)，防止长休息被刷成巨款。
  - 纯函数 `RewardForWave(wave, first, growth)` 供 HUD/测试复用，不复制公式。
- **涉及文件**：新增 `WaveRewardService.cs`（+ `.meta`），不修改任何现有脚本，回归面最小。
- **数值位置**：全部是 `[SerializeField]`，改平衡不用改代码。
- **待确认**：`// TODO(确认)` 已在 `EconomyManager.startingMoney` 与 `TrapDefinition.DefaultCost` 处；奖励曲线需要和这两者一起调。
- **风险**：奖励偏高会让后期无压力；偏低则回到卡节奏。建议先按上表跑一局，用 HUD 的余额变动观察。

---

## P1：建议优先做（改动小、玩法收益大）

### 2. 陷阱出售（半价回收）— 本轮已落地

- **问题**：摆错一格只能**全额损失**拆掉重买，位置试错成本极高，新手会因此放弃布局。
- **方案**：新增 `Assets/Scripts/TowerDefense/TrapSaleService.cs`。语义分成两条路径，互不覆盖：
  - **拆解（拆除）不退**：`TrapPlacementGrid.RemoveTrap` 保持原样，`TrapPurchaseTests.RemovalDoesNotRefundPurchase` 语义未改。
  - **出售退一半**：`TrapSaleService.TrySell(grid, instance, out refund, out failure)`，默认 `RefundRatio = 0.5f`，
    取 `FloorToInt(Cost x ratio)` 并夹紧到 `0..Cost`，所以「买-卖」循环永远不会凭空生钱（差额即交易损耗）。
  - 结算顺序与购买镜像：先 `EconomyManager.Grant(refund)`，再 `RemoveTrap`；移除失败时 `TrySpend` 撤回这笔钱并报错，不会只扣不掉。
- **入口**：`TrapPlacementController.DismantleAimedTrap(bool sell)` / `DismantleHoveredTrap(bool sell)`，
  按住 Shift 即为出售（自由视角 `Shift+E`，放置模式 `Shift+右键`）；HUD 提示同时给出当前回收价。
- **养成系统预留（本次已留钩子，未接玩法）**：
  - `TrapSaleService.RefundRatio`（float，`0..1`）：全局回收比例，`1` 即全额回收。
  - `TrapSaleService.RefundRatioOverride`（`Func<TrapDefinition, float?>`）：按陷阱族/等级返回专属比例，返回 `null` 则回落到全局比例。
    养成系统只需在开局时订阅它，就能实现「升级后回收更多」，无需改动放置与经济代码。
  - 两种钩子都在 `SubsystemRegistration` 时重置，不会把上一局的委托带进下一局。
- **涉及文件**：新增 `TrapSaleService.cs`（+ `.meta`）、`TrapPlacementController.cs`（信号 + 文案）、`TrapPurchaseTests.cs`（新增 4 组用例）。
- **待确认**：`// TODO(确认)` 已标在 `DefaultRefundRatio` 上——最终回收曲线（是否按等级线性提升、满级是否真的全额）需要与养成数值一起定表。
- **工作量**：约 1 小时（已落地）。

### 3. 波次间准备期（读条 + 手动开波）

- **问题**：波间等待期玩家无事可做，`SkipWait()` 又是无条件最优解。
- **方案**：`WaveManager` 增加阶段状态机（`Preparing / Spawning / Resolved`）；准备期冻结出怪计时，
  HUD（`WaveStatusUI`，已能显示等待秒数与 G 键提示）提示「按 G 开始第 N 波」，
  提前开波走第 1 条的提前奖励。这样「等」和「提前」都有明确代价收益。
- **涉及文件**：`WaveManager.cs`、`EnemySpawnPoint.cs`、`WaveStatusUI.cs`。
- **工作量**：约 2 小时。**依赖第 1 条已落地**（否则提前开波无收益）。

### 4. 玩家死亡的后果

- **问题**：`PlayerHealth.Died` 无人订阅，玩家被打死后无任何反馈或惩罚（`GroundEnemyCombat` 会一直打）。
- **方案（最小）**：`GameFlowManager` 订阅 `PlayerHealth.Died` → 走 `Result.Defeat`（停止出怪，复用现成逻辑）；
  或折中做法：死亡扣 25% 余额并在出生点复活。前者一句话改动，后者更有容错。
- **涉及文件**：`GameFlowManager.cs`（失败路径）、可选 `PlayerHealth.cs` 加 `Revive()`。
- **工作量**：15 分钟（失败版）/ 1 小时（复活扣款版）。
- **风险**：若设计上是「倒下可被救」，直接判负会太硬；这也是唯一需要用户拍板的点。

---

## P2：中期玩法深度

### 5. 陷阱升级（1 → 3 级）

- **问题**：金币只能换「更多格子」，没有「加强已有防线」的入口；后期格子被占满后金币无处可花。
- **方案**：`TrapDefinition` 增加 `upgradeCosts[3]` 与每级伤害/血量/射程倍率；`TrapInstance` 记录 `Level`；
  `AutoSentryTurret`、`MissileLauncherWeapon`、`MissileLauncherTurret`、`TrapInstance.maxHealth` 从升级表读取而非固定字段。
  效果：同一格铺 3 级导弹发射器，让「对空」这件事也有成长曲线。
- **涉及文件**：`TrapDefinition.cs`、`TrapInstance.cs`、`TrapPlacementGrid.cs`（重写占位不变，只换数值）、
  `AutoSentryTurret.cs`、`MissileLauncher*.cs`、HUD（`TrapHealthBarUI` 显示等级）。
- **工作量**：半天。注意 `MissileLauncherWeapon` 的装填/齐射字段也要进升级表，否则只加成伤害会破坏平衡。

### 6. 难度曲线（血量/速度随波缩放）

- **问题**：`WaveSpawnSettings` 只有 `enemyCount` 与 `prefab`，第 3 波和第一波敌人一样硬，难度只靠数量堆。
- **方案**：`WaveManager` 增加 `healthScalePerWave`、`speedScalePerWave`（默认 1.0 保持现状），
  `EnemySpawnPoint.SpawnEnemy()` 应用后写入 `EnemyHealth` 与 `MonsterPathFollower`。
  叠加第 5 条升级后，玩家能直观感到「我的塔在变强、敌人也在变强」。
- **涉及文件**：`WaveManager.cs`、`EnemySpawnPoint.cs`、`EnemyHealth.cs`（暴露 `ApplyScale`）。
- **工作量**：约 2 小时。

### 7. 飞行威胁的回报/惩罚对称性

- **问题**：飞行怪击杀奖励更高（15 > 10），但漏进核心的伤害反而更低（`EnemyCore.flyingDamage = 1` < 地面 2），
  且只有导弹发射器与玩家能拦它——这条威胁线在收益上不成立，玩家没动力补对空。
- **方案**：把飞行漏怪改成更痛（`flyingDamage` 提到 3 或按比例扣核心），或在 `GameFlowManager` 里对「飞行渗漏数」单独计数，
  每漏一只扣余额。这样双联导弹发射器才有存在意义。
- **涉及文件**：`EnemyCore.cs`（数值）/ `EnemyInstance.HandleArrived`（统计）。
- **工作量**：30 分钟。

### 8. HUD 显示本波进度

- **问题**：`WaveStatusUI` 只显示「波次 N / M」，玩家不知道本波还剩几只，无法判断要不要提前开波。
- **方案**：直接读已有的 `EnemySpawnPoint.SpawnedInCurrentWave` / `EnemiesPerWave` 画一条进度条，
  敌人存活数用 `FindObjectsByType<EnemyInstance>()`（`GameFlowManager` 已每 0.2 秒扫一次，可复用同一节奏）。
- **涉及文件**：`WaveStatusUI.cs`。
- **工作量**：约 40 分钟。

---

## P3：可选实验

### 9. 陷阱修理 / 自动回血
`TrapInstance` 被打残后只能拆（且不退款，见第 2 条）。可加「修理费 = 缺失血量比例 x 造价」的快速修复，
让残血防线有救，也让金币有持续去处。涉及 `TrapInstance.cs` + `TrapPlacementController.cs`。

### 10. 连杀 / 空仓奖励
`GroundEnemyCombat` 有 `Attacked` 事件、`EnemyInstance` 有 `SpawnTime/WaveNumber`，
可以低成本实现「5 秒内连续击杀 x1.5 金币」或「一波无伤通关额外奖励」，提高操作收益。

### 11. 防止「一格塞满塔就不缺点」的兜底
`TrapPlacementGrid` 的占位与重叠规则已经比较完整（含墙体类别 `TrapMountType`），
但没有任何「同类陷阱数量上限 / 造价随数量上涨」机制。若要抑制单一最优解，这条是下一步的调参着力点。

---

## 落地顺序建议

1. 先跑一局验证第 1 条（波次奖励）的节奏是否合理，必要时只调 Inspector 数值。
2. 第 2 条已落地；补第 4 条（1 小时内），补齐「死亡反馈」缺口。
3. 再做第 3、6、8 条（准备期 + 难度曲线 + HUD 进度），让波次节奏可读可控。
4. 第 5 条（升级）与 P3 属于内容扩展，等前三步的数值稳定后再上。

## 验收方式

- 编译：Unity 批处理（`-executeMethod`）只跑静态方法；本工程 `Assets/KINEMATION`、`Assets/TextMesh Pro` 缺失属于预期编译噪声，不要据此判定失败。
- 现有回归测试：`TrapPurchaseTests`、`EconomyAcceptanceTests`、`GroundEnemyCombatTests` 等（`Assets/Editor`，`#if UNITY_INCLUDE_TESTS` 内），
  加经济相关改动后必须一起跑，尤其是「拆解不退款」与预留/回滚语义。
