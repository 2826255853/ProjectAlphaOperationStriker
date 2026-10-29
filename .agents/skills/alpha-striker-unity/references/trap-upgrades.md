# 陷阱升级系统参考

这份参考只在修改陷阱升级、经济支付、换局生命周期或升级属性时读取。当前实现以 `Assets/Scripts/TowerDefense/TrapUpgrade*.cs`、`TrapUpgradeRuntime.cs`、`TrapInstance.cs` 和 `EconomyManager.cs` 为准；文档中的步骤编号描述当前接入边界，不代表后续功能已经存在。

## 当前数据与服务边界

- 升级状态以 `TrapDefinition.TrapId` 为键，同一 `TrapId` 的场景实例共享等级和方向；不同 `TrapId` 隔离。等级为 1–4，三级升四级才选择 `Normal` 或 `Branch`。
- `TrapUpgradeCatalog` 是唯一倍率/费用配置来源。默认倍率为等级 1/2/3/4 = `1.00/1.25/1.50/2.00`；伤害、维修、最大生命值可分别覆盖。费用向上取整，至少 1 金币。
- `TrapUpgradeCatalogAuthoring.ValidateAll` 必须同时扫描 `Assets/Resources` 和 `Assets/TrapDefinitions`。重复 `TrapId` 只允许基础放置价格一致；不能只检查 `Resources`。
- `TrapUpgradeService` 的 `Func<Guid>` 构造器是无扣款规则/测试入口。正式玩法使用 `TrapUpgradeRuntime.GetOrCreate()` 或 `TrapUpgradeService.ForWallet(...)`，一个钱包只允许一个付费服务，配置不一致会拒绝复用。
- 分支升级同时要求 catalog 中 `integrated` 和调用者提供的分支效果适配器；缺任一条件都保持不可用，不要为了显示菜单而放开提交。

## 付费与换局不变量

- 正式升级先 `GetQuote`，再 `TryReserveUpgrade`，最后由 reservation `Commit` 完成。提交时重新校验 `RunId`、目标等级/方向、报价版本、价格、倍率和分支可用性；报价过期、换局或服务销毁都不能扣款。
- 成功顺序是静默提交钱包和等级，刷新所有同 `TrapId` 的场景实例，最后才发余额/`Upgraded` 通知。失败不扣款、不改等级；事件异常隔离。
- `EconomyManager.BeginRun` 是唯一新局边界：取消旧预约、清升级状态并刷新仍存在的实例（包括 inactive），然后发 `RunReset`。正常波次不要调用它。
- 域重载/钱包销毁走 `TrapUpgradeRuntime.ResetRuntimeState` 和服务 `Dispose`；不要在普通波次或菜单关闭时重置升级。
- 刷新实例要查找场景中的 inactive 对象，不能只依赖可能在场景重载期间为空的 `TrapInstance.ActiveTraps` 集合。监听回调应核对 `RunId`，防止换局后处理旧通知。

## 有效属性接入边界

截至项目记忆 2026-10-28 的第 4 步，已提供 `TrapInstance.BaseMaxHealth`、`MaxHealth`、`DamageMultiplier`、`RepairMultiplier`、`MaxHealthMultiplier`、`GetEffectiveDamage` 和 `GetEffectiveRepair`。升级或换局时，存活陷阱按原血量比例调整，已摧毁陷阱保持 0，维修封顶到有效最大生命值。继续开发前仍须核对当前源码，后续阶段可能已改变接入范围。

七类陷阱的伤害与维修组件仍需逐步接入这些接口；接入时只在各组件的单点结算处乘一次，不在投射物、状态机和 HUD 重复乘。涉及的当前单点见项目记忆中的“陷阱升级系统 · 第 1 步基线”。未完成接入前，不要在菜单或 catalog 文案中宣称升级已经改变战斗属性。

## 推荐验证顺序

1. 先运行规则与 Canvas 结构回归：
   `Assets/Editor/RunTrapUpgradeRulesTests.ps1`
2. 支付与换局生命周期：
   `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludePayments`
3. 有效生命/倍率接口：
   `Assets/Editor/RunTrapUpgradeRulesTests.ps1 -IncludeDurability`
4. 测试清理后再运行正常 Unity 编译和 `TrapUpgradeCatalogAuthoring.ValidateAll`。

运行结果必须同时记录 Unity 版本、XML 实际用例数/失败数/跳过数、日志中的编译错误，以及是否做过真实 PlayMode 菜单操作。当前已验证的是 EditMode 与场景模拟；真实 PlayMode 菜单流程、完整 Player 构建和七类战斗属性的最终接入不能由上述规则测试代替。
