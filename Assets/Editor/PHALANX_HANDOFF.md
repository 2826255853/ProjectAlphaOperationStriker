# 密集阵近防炮（Phalanx CIWS）交接说明

本文件对应陷阱 `phalanx`。**建模、材质、预制体与转向 rig 已完成**；索敌、射击、伤害、升级分支、菜单图标与升级目录条目属于后续玩法任务，尚未接入。

## 1. 已完成的部分

| 环节 | 产物 | 入口 |
| --- | --- | --- |
| 建模 | Blender 源 `Phalanx.blend`、导出 `Phalanx.fbx` | 桌面 `陷阱素材文件夹\Phalanx\phalanx_build.py`（`blender -b -P phalanx_build.py`） |
| 定稿模型 | `Assets/Models/Phalanx/Phalanx.fbx` | 由桌面导出件复制，勿手改 |
| 材质 | `Assets/Models/Phalanx/Materials/M_PHX_*.mat`（9 个 URP/Lit） | `PhalanxTrapAuthoring.EnsureMaterials` |
| 预制体 | `Assets/Prefabs/Phalanx.prefab` | `Tools/塔防/生成密集阵近防炮陷阱` = `PhalanxTrapAuthoring.CreateAssets` |
| 陷阱定义 | `Assets/Resources/Phalanx.asset`（TrapId `phalanx`，2×2 格，地面安装） | 同上，`prefab` 字段由生成器回写 |
| 转向 rig | `Assets/Scripts/TowerDefense/PhalanxTurretRig.cs` | 由生成器接线到 yaw/pitch/barrel/muzzle |
| 预览 | 4 张 Blender 渲染 + Unity 侧 6 姿态渲染 | `PhalanxPreview.RenderPreviewBatch`（需 `-WithGraphics`） |

模型规模：115 个网格对象 / 约 1.99 万三角面 / 10 个材质槽来源。预制体包围盒 **1.76 × 2.12 × 2.34 m**，占格 2×2 m，炮管前伸 0.34 m（在 `barrelOverhangBudget = 0.40` 内）。

## 2. 模型层级约定（不要改名）

```
Phalanx                       ← 预制体根（放置控制器拥有它的 transform）
└─ PHX_Base_Root              ← FBX 导入节点，局部旋转 X=270（Blender Z-up 残留）
   ├─ PHX_Base_Plate / Riser / Deck / Hub / Collar / Bolt_* / Leg_* / Foot_*
   └─ PHX_Yaw_Root            ← 偏航枢轴（z=0.46）
      ├─ PHX_Turret_Shell / Cheek_L|R / Glacis / Front_Plate / Panels / Ammo_* / Radar_* …
      └─ PHX_Pitch_Root       ← 俯仰枢轴
         ├─ PHX_Gun_Cradle / Trunnion_* / Tracker_* / Radome_*
         └─ PHX_Barrel_Root   ← 六管旋转簇（自转轴）
            ├─ PHX_Barrel_1..6 / Tip_1..6 / Jacket / Clamp_* / Muzzle_Ring
            └─ PHX_Muzzle     ← 炮口空物体
```

`PhalanxTrapAuthoring.ValidateBatch` 会断言 yaw→pitch→barrel→muzzle 的父子关系、炮口朝 +Z、yaw 枢轴位于底座中心、所有材质槽都是 `M_PHX_*` 的 URP/Lit、以及包围盒在 2×2 占格 + 炮管前伸预算内。

## 3. 轴系坑（已修，勿回退）

`PHX_Base_Root` 带着 FBX 的 `X=270°` 节点旋转，所以**子节点的局部 `Vector3.up` 不是世界上方向**（局部 up 对应世界 -Z）。`PhalanxTurretRig` 原先用 `Vector3.up` / `Vector3.right` 作局部旋转轴，结果偏航变成了绕水平轴侧倾，瞄准 60 帧后仍差 15.9°、`IsAimedAt` 恒为 false。

现在 `EnsureRestPose()` 一次性解析：

- `yawAxisLocal = Quaternion.Inverse(yawPivot.parent.rotation) * Vector3.up`
- `pitchAxisLocal = Quaternion.Inverse(pitchPivot.parent.rotation) * Vector3.right`

与 XM777 的 `yawAxisWorld` 修复同一类问题：**不要**再在局部旋转里写死 `Vector3.up` / `Vector3.right`。回归用例 `Tests~/Editor/PhalanxTurretRigTests.cs`（2 例，真实预制体收敛 + 轴旋转父节点下偏航轴必须竖直）。

## 4. rig 的对外契约

`PhalanxTurretRig` 只做动作，不含任何玩法：

- `AimAt(Vector3 worldTarget, float deltaTime)` —— 偏航/俯仰按 `yawSpeedDegreesPerSecond = 240`、`pitchSpeedDegreesPerSecond = 180` 收敛，俯仰夹在 `minPitchDegrees = -8` / `maxPitchDegrees = 85`。
- `IsAimedAt(worldTarget, toleranceDegrees)` —— 玩法层用它当开火门槛（与 XM777 的双轴容差同一模式）。
- `SetBarrelSpinActive(bool)` / `AdvanceVisuals(deltaTime)` —— 六管自转（1500°/s，0.25 s 平滑），纯表现，玩法不必调用。
- `ResetToRestPose()`、`BarrelDirection`、`MuzzlePosition`、`YawDegrees`、`PitchDegrees`、`CurrentSpinRate` 供工具与测试读取。

## 5. 尚未接入（后续玩法任务）

1. **索敌与射击**：新玩法组件应复用 `MissileAimSolver` 的拦截点预测（密集阵弹速高，提前量仍按同一权威实现），不要另写一套预测。
2. **伤害与升级倍率**：走 `TrapInstance.GetEffectiveDamage`，与其它陷阱一致。
3. **陷阱菜单**：`TrapSelectionMenuAuthoring` 尚无 `phalanx` 条目，需要图标（`Assets/Models/TrapMenu/Icons/phalanx.png`）与分类/排序。
4. **升级目录**：`TrapUpgradeCatalogAuthoring` 无 `phalanx` 条目，`Assets/Resources/TrapUpgradeCatalog.asset` 需要默认四级与分支配置；在此之前 `TrapUpgradeService` 只会给普通等级成长。
5. **主场景摆放与 PlayMode 验收**：当前未放置、未做人工实机观察。
6. 价格 `cost = 55` 是占位值（生成器内有 `TODO(确认)`）。

## 6. 校验入口

```powershell
# 生成（模型或脚本改动后必须重跑）
.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1 PhalanxTrapAuthoring.CreateAssetsBatch

# 只读校验：rig 接线 / 材质 / 占格
.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1 PhalanxTrapAuthoring.ValidateBatch

# 只读探针：FBX 导入轴、rig 瞄准与自转
.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1 PhalanxTrapAuthoring.ProbeImportBatch
.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1 PhalanxTrapAuthoring.ProbeRigBatch

# 预制体多姿态渲染（必须带图形）
.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1 PhalanxPreview.RenderPreviewBatch -WithGraphics
```

成功标记：`PHALANX_CREATED` / `PHALANX_PASS`。rig 探针的判据是 `aimed=True` 且 `angle` 接近 0。

## 7. 中间态文件位置

`C:\Users\ASUS\Desktop\陷阱素材文件夹\Phalanx\`

- `phalanx_build.py`、`Phalanx.blend`（含 `.blend1` 自动备份）、`Phalanx.fbx`
- `preview\01_three_quarter.png`、`02_right_side.png`、`03_front.png`、`04_top.png`
- `modeling-pass2-20261031\backup\` —— 本轮改动前的 `.blend` / 脚本 / 仓库 FBX / 预制体
- `modeling-pass2-20261031\tests\` —— rig 回归 XML 与日志
