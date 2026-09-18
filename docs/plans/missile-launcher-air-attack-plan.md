# 计划：双联导弹发射器的实际对空攻击（Dual Missile Launcher – Air Attack）

状态：待实现（本文件是实施计划，代码尚未落地）
涉及主文件：`Assets/Scripts/TowerDefense/MissileLauncherTurret.cs`（已有，只读结论见下）

## 0. 规则集（本计划必须实现的全部行为）

| 编号 | 规则 | 归属组件 |
| --- | --- | --- |
| R1 | 目标筛选：只打空中怪（`AirOnly`） | `MissileLauncherTurret`（已实现） |
| R2 | 目标选择：**离炮塔最近的敌人优先**（回退，不再改主排序）；距离并列时用「离核心更近」破平 | `MissileLauncherTurret.AcquireTarget`（微调） |
| R3 | **炮塔自身主动按提前量瞄准**：炮管对准预测拦截点，导弹沿炮管方向出膛后持续修正 | `MissileAimSolver`（新增）+ `MissileLauncherTurret` + 武器（新增） |
| R4 | 双联齐射：左右管各 1 发，齐射后装填 | `MissileLauncherWeapon`（新增） |
| R5 | **超射程自爆**：导弹飞行距离超过射程即自爆，且自爆对敌人造成伤害 | `MissileProjectile`（新增） |
| R6 | 命中判定不依赖 Collider（飞行怪可能没有碰撞体） | `MissileProjectile`（新增） |
| R7 | 放置预览（幽灵）不得真的发射导弹 | `TrapPlacementController.RebuildPreview`（新增一行） |

## 1. 现状（只读确认过的事实）

- `Assets/Scripts/TowerDefense/MissileLauncherTurret.cs` 已完成**纯瞄准**：
  - `MissileTargetFilter`（`AirOnly` / `GroundOnly` / `Any`，默认 `AirOnly`）。
  - `AcquireTarget()` 用 `FindObjectsByType<EnemyInstance>()`，把 `detectionRange` 半径内**离炮塔最近**的合法目标选为 `target`。
  - `IsAirTarget(EnemyInstance)` 静态判据：`MonsterType.Flying` / `MonsterPathFollower.IsFlying` / `EnemySpawnPoint.IsFlyingEntrance`。
  - `AimAt(Vector3)` / `SnapAimAt` / `AimAtRest`；俯仰被 `ClampPitchDegrees` 硬锁 0..75 度；`CurrentYawDegrees` / `CurrentElevationDegrees` / `BarrelDirectionWorld` / `CurrentTarget` 均为公开可读。
- **缺口**：该组件没有任何 `ammo / fire / reload / projectile / damage` 逻辑；`Select-String` 全 `Assets/Scripts` 搜不到 projectile / rocket / homing，所以「对空攻击」目前只是「对空转头」。
- 已确认可用的支撑 API（本轮只读核实）：
  - `EnemyCore.Position`（`transform.position`）、`EnemyCore.IsWithinArrivalRange`（**水平**距离，y 置零）→ R2 的「离核心距离」沿用同一口径。
  - `EnemySpawnPoint.Core { get; set; }` → 可以从 `EnemyInstance.SpawnPoint.Core` 拿到该怪正在追的核心，天然支持多核心场景。
  - `MonsterPathFollower.MoveSpeed { get; set; }` 与 `DestinationPosition`；`InitializeFlying` 分支里 `step = toTarget.normalized * moveSpeed * dt`，即**飞行怪是直线朝目标点匀速移动**，转向只影响外观 → R3 的提前量有解析依据。
  - `MonsterPathFollower` **没有暴露 velocity** → 需在武器侧做速度估计（见 3.2）。
- 参考范式：`Assets/Scripts/TowerDefense/AutoSentryTurret.cs`（`EnemyHealth.TakeDamage` + `Debug.DrawLine` 曳光 + 无弹药限制的射击节奏）。
- 编辑器侧已有脚手架（未跟踪的新文件）：`Assets/Editor/MissileLauncherTurretAuthoring.cs`（`LauncherPrefabPath = Assets/Prefabs/DualMissileLauncher.prefab`、`LauncherModelPath`、`CreateLauncherPrefab`）、`MissileLauncherTurretTests.cs`、`MissileLauncherTurretPreview.cs`。

## 2. 目标与非目标

目标
1. R1–R7 全部可运行、可测试。
2. 灰盒场景无需额外美术资源也能看见弹道与爆炸。
3. 编辑期批处理可验证（PASS/FAIL 由测试结果决定，不看 Unity 退出码）。

非目标（本计划不做）
- 不做弹药经济/购买弹药（弹药无限，只有装填计时）。
- 不做地面目标的对地平衡（`GroundOnly` / `Any` 只保证不崩、能打）。
- 不改地图、不改 MapForge 导出格式、不改 `MonsterPathGrid`。
- 不改 `UpdateCursor` / `TrapSelectionMenu.CursorOwned`（与视角锁定无关）。

## 3. 设计决策

### 3.1 R2 目标选择（改 `MissileLauncherTurret`）

- **默认回退为「离炮塔最近」**：`AcquireTarget()` 主排序保持既有逻辑——在 `detectionRange` 硬过滤之后，取到炮塔平方距离最小者。本规则不做行为切换，避免手感回归。
- **并列破平**：当候选到炮塔的距离差在 `tieBreakDistanceEpsilon`（默认 0.25m）以内时，改取到核心水平距离更小者；距离口径与 `EnemyCore.IsWithinArrivalRange` 一致（y 置零）。
- 核心解析（只服务破平与调试显示）：`candidate.SpawnPoint.Core` → 序列化 `core` 字段 → `FindAnyObjectByType<EnemyCore>()`（`Awake` 补一次）。三者全空时破平退化为「先遍历到的那个」，并 `Debug.LogWarning` 一次，不静默失效。
- 保留枚举 `MissileTargetPriority { ClosestToTurret = 0, ClosestToCore = 1 }` 与 `targetPriority` 字段，**默认 `ClosestToTurret`**；`ClosestToCore` 仅作为非默认调试/关卡选项，不再是默认行为。
- 新增 `public bool TryGetCurrentAimPoint(out Vector3 aimPoint)`：返回炮塔当前实际瞄准的世界点（即提前量解算结果），供武器、Gizmo 与测试读取。

### 3.2 R3 炮塔主动提前量瞄准（新增 `MissileAimSolver`）

- `public static bool TrySolveIntercept(Vector3 shooter, Vector3 targetPosition, Vector3 targetVelocity, float projectileSpeed, out Vector3 interceptPoint, out float timeToImpact)`：迭代求解 + 收敛保护；`projectileSpeed <= 0`、迭代发散或结果 NaN/Inf 时返回 `false`，并把 `interceptPoint` 置为「目标当前位置」。
- **炮塔自己按提前量瞄准（本规则核心）**：`MissileLauncherWeapon.Update()` 每帧求 `interceptPoint`，调用 `turret.AimAt(interceptPoint)`，并写入 `TryGetCurrentAimPoint`。炮塔俯仰仍受 0..75° 硬夹，但瞄准输入从「敌人当前位置」改为「预测拦截点」，炮管会提前出现在导弹需要飞过的方位上。
- **导弹沿炮管方向出膛（提高飞行效率的落点）**：`MissileProjectile.Initialize()` 增加 `Vector3 initialDirection` 参数，取发射瞬间的 `turret.BarrelDirectionWorld`。导弹初始朝向 = 炮管朝向，而不是「朝敌人当前位置」——出膛即对准拦截方向，飞行中只剩小幅修正，转弯半径小、绕行距离短、命中更快。
- **对齐门限改用拦截点**：`Vector3.Angle(turret.BarrelDirectionWorld, interceptPoint - muzzle) <= aimToleranceDegrees` 且 `timeToImpact > 0` 才允许发射；否则继续转，不发射。
- **导弹中途修正**：默认 `guidanceMode = ContinuousLeadRefine`，每 `leadRefreshSeconds = 0.1s` 复用同一套 `MissileAimSolver` 重算拦截点（不另写一套解算）。保留 `FrozenLeadShot`（发射瞬间冻结提前量）作为备选。
- **速度估计**（`MonsterPathFollower` 未暴露 velocity）：主用采样平滑（逐帧差分 + `0.2s` 指数平滑），回退用解析式 `follower.MoveSpeed * (follower.DestinationPosition - pos).normalized`。
- **提前量与俯仰上限的冲突处理（必须实现，否则会死锁）**：目标迎头接近时，拦截点仰角可能超过 75°。规则：若拦截点所需仰角 > `MaxPitchDegrees`，改用「目标当前位置」作为瞄准输入；若当前位置仰角也 > 75°，则**不开火**并等待目标飞近。绝不允许在俯仰夹紧的姿态下开火，避免出现「永远转不到位、永远不开火」。

### 3.3 R5 超射程自爆

- 射程来源：`[SerializeField] private bool useTurretRangeAsMaxFlightRange = true;`，开启时读 `turret.DetectionRange`（公开属性，已确认存在）；否则用 `maxFlightRange`（默认 18m）。
- 自爆触发：累计飞行距离 `travelledDistance > maxFlightRange` → `Detonate()`。
- `Detonate()` 与直接命中走**同一个爆炸路径** `Explode(EnemyInstance directHit)`：
  - `Physics.OverlapSphere(position, splashRadius)` 找全部 `EnemyHealth`；
  - 直接命中者吃 100% `damagePerMissile`，其余按 `splashDamageRatio`（默认 0.5，R5 要求「会对敌人造成伤害」，所以不是 0）；
  - 用 `HashSet<EnemyHealth>` 去重，避免直接命中目标吃两次；
  - 无 `Collider` 的敌人不在 `OverlapSphere` 结果里 → 额外把「注册过的存活目标列表」也纳入判定（见 R6）。
- `maxLifetime`（默认 8s）降级为**安全网**，仅用于防止 NaN / 卡死状态；射程自爆是主路径。
- 爆炸视觉：`Debug.DrawLine` 八向 + 无 `projectilePrefab` 时程序化生成一个短暂膨胀球体（同 `AutoSentryTurret.BuildFallbackVisuals` 的 `Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")` 做法）。

### 3.4 R6 命中判定（不依赖 Collider）

- 主判据：`(target.position - position).sqrMagnitude <= proximityFuseRadius^2`（默认引信 1.2m）。
- 次判据：`Physics.SphereCast`（`QueryTriggerInteraction.Ignore`）命中带 `EnemyHealth` 的 Collider 时立即起爆。
- 飞行怪即使没有 Collider 也能被主判据打中；`EnemyInstance.EnsureDamageReceiver()` 只在有 `Renderer` 时才补 `CapsuleCollider`。

### 3.5 参数默认值汇总（未定项配 `// TODO(确认)`）

| 参数 | 默认值 | 备注 |
| --- | --- | --- |
| `targetPriority` | `ClosestToTurret`（回退） | R2 |
| `tieBreakDistanceEpsilon` | 0.25m | R2 并列破平阈值 |
| `damagePerMissile` | 45 | 待确认 |
| `splashRadius` | 2.5m | 爆炸与自爆共用 |
| `splashDamageRatio` | 0.5 | R5 要求自爆有伤害 |
| `missilesPerSalvo` | 2 | 「双联」字面要求 |
| `launchInterval` | 0.25s | 左右管间隔 |
| `reloadTime` | 6s | 待确认 |
| `projectileSpeed` | 60 m/s | 提前量求解输入 |
| `turnRateDegrees` | 180°/s | 导弹转向 |
| `proximityFuseRadius` | 1.2m | R6 主判据 |
| `maxFlightRange` | 18m（或 = 炮塔射程） | R5 |
| `maxLifetime` | 8s | 仅安全网 |
| `aimToleranceDegrees` | 8° | 对齐门限 |
| `leadRefreshSeconds` | 0.1s | R3 |
| `guidanceMode` | `ContinuousLeadRefine` | 冻结提前量为备选 |

## 4. 拆解的实施单元（每单元 ≤15 分钟，且以一次文件写入结束）

### 单元 1：骨架 + R2 微调（先改已有文件）  ✅ 已完成
- `Assets/Scripts/TowerDefense/MissileLauncherTurret.cs`：加 `MissileTargetPriority` 枚举（默认 `ClosestToTurret`）、`core` 字段、`TargetPriority` 属性、`TryGetCurrentAimPoint(out Vector3)`；`AcquireTarget()` 保持最近优先，仅当距离并列（≤ `tieBreakDistanceEpsilon`）时用核心距离破平。
- 新增 `Assets/Scripts/TowerDefense/MissileAimSolver.cs`：`TrySolveIntercept` 签名 + 空实现。
- 新增 `Assets/Scripts/TowerDefense/MissileProjectile.cs`：字段 + `Initialize(EnemyInstance target, Vector3 fallbackPoint, Vector3 initialDirection, float damage, ...)` 签名 + 空 `Update()`。
- 新增 `Assets/Scripts/TowerDefense/MissileLauncherWeapon.cs`：[Header] 序列化 3.5 全部字段、`muzzleA` / `muzzleB`、`projectilePrefab`；`public float ReloadRemaining`、`public int MissilesInFlight`、`public bool IsReloading`；空 `Update()`。
- 交付判据：Unity 能编译（`KINEMATION` / `TextMesh Pro` 缺失导致的报错属预期，见 AGENTS.md）。
- **完成记录**：4 个文件已落地；Unity 批处理 `TrapPlacementGridEditor.ValidateTrapGrids` 全量编译 **0 个 `error CS`**、`Exiting batchmode successfully now!`。剩余 warning（`CS0414` 未使用字段 / `CS0067` 未触发事件）为空 `Update()` 阶段的预期产物，单元 2–4 填逻辑后消失。

### 单元 2：R3 炮塔提前量瞄准 + 求解器  ✅ 已完成
- 实现 `MissileAimSolver.TrySolveIntercept`（迭代 + 收敛保护 + 俯仰上限退化判定）。
- 武器侧加目标速度估计（采样平滑 + 解析回退）与 `TryGetInterceptPoint(EnemyInstance, out Vector3)`。
- **炮塔瞄准改接拦截点**：`turret.AimAt(interceptPoint)`，写入 `TryGetCurrentAimPoint`，对齐门限改用拦截点夹角。
- **导弹出膛朝向改为沿炮管**：投射物 `Initialize` 接收 `initialDirection`，初始 `transform.forward` 即该方向。
- 交付判据：直线飞行的桩怪，拦截点在其运动方向**前方**（`Dot(intercept - targetPos, velocity) > 0`）、`timeToImpact > 0`；`TryGetCurrentAimPoint` 落在拦截点而非目标当前位置；拦截点仰角超过 75° 时退化为瞄当前位置。

### 单元 3：R5/R6 投射物飞行、自爆、伤害  ✅ 已完成
- `MissileProjectile.Update()`：转向（`Vector3.RotateTowards`）、推进、累计 `travelledDistance`、引信判定、`SphereCast` 次判据、超射程 → `Detonate()`。
- `Explode(directHit)`：`OverlapSphere` + 存活目标列表补充，`HashSet` 去重，按比例扣 `EnemyHealth.TakeDamage`。
- 无 `projectilePrefab` 时程序化外观（Capsule + `TrailRenderer` + 爆炸球）。
- 交付判据：日志出现 `[EnemyHealth] 受到伤害`；把 `maxFlightRange` 调到极小值时，导弹在离目标很远处自爆且目标仍掉血。
- **完成记录**：`MissileAimSolver`（固定点迭代 + `MissileVelocityTracker` 平滑结构）、武器侧 `TryGetInterceptPoint` / `EstimateVelocity` / `IsBarrelAligned`、炮塔 `AimDrivenExternally` 交出瞄准权、投射物全飞行逻辑均已落地。Unity 批处理 **0 error CS**，单元 1 遗留的 5 个 warning 全部消失。

### 单元 4：R4/R7 双联开火节奏与预览互斥  ✅ 已完成
- `MissileLauncherWeapon.Update()` 状态机：`Idle → Aiming → Firing(salvo, launchInterval) → Reloading(reloadTime) → Idle`。
- 发射：交替取 `muzzleA` / `muzzleB`；`MissilesInFlight` 由投射物 `Destroyed` 事件递减。
- `Assets/Scripts/TowerDefense/TrapPlacementController.cs::RebuildPreview()`：在禁用 `MissileLauncherTurret` 的那行下面加 `MissileLauncherWeapon` 的禁用（R7）。
- `Assets/Editor/MissileLauncherTurretAuthoring.cs::CreateLauncherPrefab()`：给预制体加 `MissileLauncherWeapon`，自动从模型里找 `Missile_01` / `Missile_02` 写进 `muzzleA` / `muzzleB`，并写入场景核心引用。
- 交付判据：放置预览不再开火；生成的预制体上组件与发射口齐全。
- **完成记录**：`MissileLauncherWeapon` 状态机（Idle/Aiming/Firing/Reloading）、`NextMuzzle()` 左右管交替、`CreateFallbackMissile()` 无预制体兜底、`TrapPlacementController.RebuildPreview()` 禁用 `MissileLauncherWeapon`（R7）、`MissileLauncherTurretAuthoring.WireWeapon()` 写入 `turret`/`muzzleA`/`muzzleB` 均已落地；预制体 `Assets/Prefabs/DualMissileLauncher.prefab` 重新生成后三处引用非空，`muzzleA` 与 `muzzleB` 指向不同节点。Unity 批处理 **0 error CS**。
- **顺手修掉的编辑期缺陷**：`MissileProjectile` / `MissileLauncherWeapon` 里对自建子弹的 `Destroy` 改为按 `Application.isPlaying` 分支（编辑期走 `DestroyImmediate`），否则 EditMode 测试会刷 `Destroy may not be called from edit mode` 报错。
- **为可测性加的接缝**：`MissileLauncherWeapon.Advance(float)`、`MissileProjectile.Advance(float)`、`MissileLauncherTurret.AimAt(Vector3, float deltaTime = -1f)`，让单元 5 的编辑期测试用固定步长驱动，不依赖 `Time.deltaTime`。

### 单元 5：编辑期测试  ✅ 已完成
- 新增 `Assets/Editor/MissileLauncherWeaponTests.cs`（复用 `MissileLauncherTurretTests` 的 `CreateTurret` 造桩思路）：
  1. `NearestEnemyIsTargetedByDefault` —— R2 回退：贴脸的怪优先于更远但更靠近核心的怪。
  2. `TiesAreBrokenByDistanceToTheCore` —— R2 破平：两怪到炮塔距离接近时取更靠近核心者；无核心时不报错、仍能选中目标。
  3. `TurretAimsAtTheInterceptPoint` —— R3：`TryGetCurrentAimPoint` ≈ 求解器给出的拦截点，且与目标当前位置有可测偏差。
  4. `InterceptPointLeadsTheTargetInsteadOfTrackingIt` —— R3：拦截点在目标速度方向前方，`timeToImpact ≈ distance / speed` 量级。
  5. `MissileLeavesAlongTheBarrelDirection` —— R3：出膛初速方向 ≈ 发射瞬间的 `BarrelDirectionWorld`。
  6. `LeadFallbackKeepsFiringWhenElevationExceedsPitchLimit` —— R3 退化：拦截点仰角 > 75° 时改瞄当前位置，不出现「永远不开火」。
  7. `AirOnlyTargetIsTheOnlyThingThatTriggersFiring` —— R1。
  8. `SalvoLaunchesOneMissilePerTube` —— R4：齐射数 2，`muzzleA` / `muzzleB` 各 1 发。
  9. `ReloadBlocksFurtherSalvos` —— R4：装填期内不再发射，`IsReloading == true`。
  10. `MissileDetonatesPastMaxRangeAndStillDealsDamage` —— R5：超程自爆 + 目标掉血。
  11. `ProjectileAppliesDamageWithoutACollider` —— R6：飞行怪无 Collider 仍被主判据命中。
  12. `AuthoredPrefabCarriesWeaponAndMuzzles` —— 预制体接线（与既有 `AuthoredPrefabHasARotatingRigWiredToThePitchPivot` 并列）。
- 跑法：`scripts/run-unity-method.ps1`（Unity 退出码不可用于判成败，见 `.agents/skills/alpha-striker-unity/references/unity-editor.md`）。
- **完成记录**：新增 `Assets/Editor/MissileLauncherWeaponTests.cs`（13 个用例，比计划多 1 个：`MissingCoreStillPicksATargetAndDoesNotThrow`，把「无核心不报错」从并列断言拆成独立用例）。以 `-runTests -testPlatform EditMode` 真跑：**13/13 通过**；全量 EditMode 套件 **36/36 通过**（含既有 23 个用例），日志 `Exiting batchmode successfully now!`。
- **为可测性新增的运行时接缝**：`MissileLauncherWeapon.Advance(float)`、`MissileProjectile.Advance(float)`、`MissileLauncherTurret.AimAt(Vector3, float)` 与 `RefreshTarget()`。编辑期 `Time.deltaTime` 恒为 0，测试改用固定步长驱动，不依赖帧序。
- **顺手修掉的编辑期缺陷**：自建子弹/投射物的 `Destroy` 改为 `DestroyNow`（按 `Application.isPlaying` 分支，编辑期走 `DestroyImmediate`）；`CreateMarkerMaterial` 在 `Shader.Find` 全空时兜底到 `Hidden/InternalErrorShader`，不再抛 NRE。
- **变异测试（证明用例不是空的）**：植入 6 个缺陷，确认都被对应用例抓住 ——
  - R2 只按核心距离排序 → `NearestEnemyIsTargetedByDefault` 失败；
  - R3 炮塔改瞄敌人当前位置（而非拦截点）→ 初版用例**没抓住**，随即加强 `TurretAimsAtTheInterceptPoint`：断言**炮管实际朝向**（对拦截点 <4°、对敌人 >8°），复测抓住；
  - R4 两管共用一个发射口 → `SalvoLaunchesOneMissilePerTube` 失败；
  - R5 自爆不造成伤害、R6 去掉距离引信、R1 空中过滤恒真 → 各自对应用例失败。
  植入后源码已按哈希校验逐字节还原（3 个文件 `restored=True`）。
- **交付判据**：13 个用例覆盖 R1–R7，且每条规则都有能真正失败的用例（见上）；`Assets/Prefabs/DualMissileLauncher.prefab` 重新生成后 `turret` / `muzzleA` / `muzzleB` 三处引用仍非空、两管指向不同节点。

## 5. 风险与规避

- **俯仰 0..75 度限制射界**：`detectionRange = 12` 时 12m 水平距离上最大可打高度 `12 * tan75° ≈ 44.8m`，空中出怪口默认 `entranceAltitude = 12`，够用；若提高高度需同步提高 `detectionRange`（测试里钉住这条关系）。
- **R2 已回退为最近优先**：不再出现「放过贴脸怪、去打远处但更靠近核心的怪」的手感变化；`ClosestToCore` 仅作为非默认调试选项。剩余风险转移到「并列破平依赖核心引用」：核心缺失时破平退化为「先遍历到的那个」，所以 `OnDrawGizmosSelected` 要画出当前目标与瞄准点，便于肉眼确认不是抽风。
- **R3 抖振**：速度估计若直接用单帧差分，噪声会让提前量乱跳 → 必须平滑，并对 `t` 设上限（默认不超过 `maxFlightRange / projectileSpeed` 的两倍），`t` 判定为 NaN/Inf 时回退瞄准当前位置。
- **R3 与俯仰上限的叠加**：炮塔按提前量瞄准会更频繁地顶到 75° 上限（目标迎头接近时拦截点仰角反而更高）→ 必须实现 3.2 的退化规则（拦截点超限改瞄当前位置、当前位置也超限就不开火），否则会出现「炮管永远差几度、导弹永远不发射」的死锁。测试 `LeadFallbackKeepsFiringWhenElevationExceedsPitchLimit` 专门覆盖这条。
- **R5 与 `maxLifetime` 语义重叠**：把 `maxLifetime` 明确为安全网、射程自爆为主路径，避免两个计时器互相掩盖 bug；两者都走同一个 `Explode()`。
- **飞行怪可能没有 Collider**：命中主判据用距离引信，不依赖 `Collider`；`OverlapSphere` 结果需用存活目标列表补充。
- **目标在飞行途中死亡**：`EnemyInstance` 被 `Destroy` 后 Unity 伪空，`Update()` 每帧判 `target == null` 转「飞向最后已知拦截点后自爆」；命中时重新 `GetComponent<EnemyHealth>()`，不缓存。
- **两份副本问题（本工程已知坑）**：本计划不碰 `UpdateCursor` / `TrapSelectionMenu.CursorOwned`。
- **场景 YAML 巨大**：不整文件读 `.unity` / `.prefab`，用 `Select-String` 定位行号后读局部片段。

## 6. 验收标准（对外可演示）

- 场景 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity` 放一台双联导弹发射器，`Enemy Spawn Flying` 出飞行怪；
- 同场存在两只飞行怪时，**只打离炮塔最近的那只**；两怪到炮塔距离接近时，取更靠近核心的那只（R2 回退 + 破平）；
- 炮塔**自己**先把炮管转到提前量拦截点（`TryGetCurrentAimPoint` 与拦截点一致），导弹沿此时炮管方向出膛，飞行中只做小幅修正而非大角度掉头（R3）；
- 双管各发 1 发 → 导弹追尾命中 → `EnemyHealth` 掉血并摧毁 → 6s 装填（R4）；
- 把目标移到射程边缘，导弹在超出射程处自爆，且爆炸范围内的飞行怪仍掉血（R5）；
- 地面怪经过时不发射（R1）；
- `Tools` 菜单生成的预制体、以及放置预览（幽灵）不会真的发射导弹（R7）。
