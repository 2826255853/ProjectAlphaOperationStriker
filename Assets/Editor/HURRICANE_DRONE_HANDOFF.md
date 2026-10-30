# 飓风无人机（Hurricane Drone）· 美术交付与程序员接入说明

> 交付日期：2026-10-30
> 美术侧（建模 / 骨骼 / 动画 / 材质 / 预制体）已完成；本文档列清已交付内容、可用的接口，
> 以及**留给程序员的全部后续工作**。文中的「已核对」都附了核对方式，未核对项会明确写出。

---

## 1. 交付物清单

| 用途 | 路径 |
| --- | --- |
| 模型 + 骨骼 + 4 段动画（FBX） | `Assets/Models/HurricaneDrone/HurricaneDrone.fbx` |
| URP 材质（8 个，导入时自动回填） | `Assets/Models/HurricaneDrone/Materials/M_HD_*.mat` |
| 动画控制器 | `Assets/Animations/HurricaneDrone/HurricaneDrone.controller` |
| 可直接用的预制体 | `Assets/Prefabs/HurricaneDrone.prefab` |
| 运行时动画驱动组件 | `Assets/Scripts/TowerDefense/HurricaneDroneUnit.cs` |
| 生成 / 校验 / 预览工具 | `Assets/Editor/HurricaneDroneAuthoring.cs`、`Assets/Editor/HurricaneDronePreview.cs` |
| Blender 源脚本（可重建，不进仓库） | `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\build_drone_v3.py` |
| 可编辑源文件（.blend） | `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\HurricaneDrone.blend` |
| 预览图（8 张） | `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\preview_*.png` |

菜单入口：

- `Tools/塔防/生成飓风无人机预制体` → 重建材质 / 导入设置 / 控制器 / 预制体（幂等，可重复跑）
- `Tools/塔防/校验飓风无人机预制体` → 打印骨骼、炮口、包围盒、材质槽
- `Tools/塔防/预览飓风无人机` → 离屏渲染 6 张图到 `%TEMP%\alpha-striker-unity\hurricane-drone-preview`
- 批处理：`HurricaneDroneAuthoring.CreateAll`、`HurricaneDroneAuthoring.ProbePrefabBatch`、`HurricaneDronePreview.RenderPreviewBatch`

---

## 2. 资产规格（设计图对齐情况）

| 项目 | 设计图 | 实际模型 | 核对方式 |
| --- | --- | --- | --- |
| 全展（X × Z） | 7.60 × 7.60 m | **7.70 × 7.70 m** | 预制体包围盒 |
| 整机高（含下方炮组） | 2.40 m | **2.36 m** | 预制体包围盒 |
| 升力风扇 | 4 组涵道风扇，X 型 45° 布局 | 4 组，外径 2.60 m，涵道环 + 7 叶桨，相邻成对反转 | 顶视预览 |
| 控制核心 | 白色装甲 + 蓝色主透镜 | 八边装甲舱 + 正面竖长主透镜 + 顶部副透镜（自发光） | 透视预览 |
| 机腹武器 | 40 mm 机炮，下半球万向节，可 180° 扫射 | 机炮挂在 `Gun_Yaw`（偏航不限位）× `Gun_Pitch`（-15°~+60°）两轴骨骼上 | 侧视 / 炮组特写预览 |
| 网格规模 | — | 5 662 顶点 / 5 314 面 / 8 个材质槽 / 1 个蒙皮网格 | `ProbePrefabBatch` |

坐标约定：Blender 里机头朝 **-Y**、Z 向上；导出后 Unity 里机头朝 **+Z**、Y 向上（`globalScale = 1`，1 Blender 单位 = 1 米）。

---

## 3. 骨骼

```
Root
└── Body                 （机体：悬停浮动 / 俯仰 / 侧倾 / 后坐震颤）
    ├── Fan_FL  Fan_FR   （桨叶自转，相邻反向）
    ├── Fan_BL  Fan_BR
    └── Gun_Yaw          （机炮偏航，绕竖直轴）
        └── Gun_Pitch    （机炮俯仰，炮管沿 +Z）
```

`Muzzle` 空物体挂在 `Gun_Pitch` 下，位置 (0, -1.18, 2.42)，随俯仰一起动 —— 弹道 / 枪口火光 / 音效都从这里取。

---

## 4. 动画契约

| 状态名 | 时长 | 循环 | 内容 |
| --- | --- | --- | --- |
| `HD_Hover` | 1.97 s | 是 | 悬停浮动 ±5.5 cm、±1.6° 微侧倾，桨叶 3 圈 |
| `HD_FlyForward` | 1.30 s | 是 | 前飞：机头下压 13~16°，桨叶 4 圈 |
| `HD_BankTurn` | 1.57 s | 是 | 转向：侧倾到 -20°、偏航 24°，桨叶 3 圈 |
| `HD_Attack` | 2.97 s | 否 | 机炮偏航扫到 -28°、抬头 12°，**两轮 3 连发**，含后坐与机体震颤，末帧回正 |

**开火时机**：`HD_Attack` 上有 6 个动画事件，函数名 `OnGunFire`，时间 0.73 / 0.87 / 1.00 / 1.53 / 1.67 / 1.80 s，与模型后坐严格对齐。
事件写在 **FBX 导入设置**里（`HurricaneDrone.fbx.meta` 的 `clipAnimations[].events`），不是手写在场景里 —— 重新生成预制体不会丢。

> ⚠️ 已知瑕疵：`HD_Attack` 的曲线数据只到 **2.97 s**（实测末关键帧 2.97 s），但 Unity 报的
> `clip.length` 是 **5.34 s**（FBX take 尾部多出一段静止，其它三段正常）。
> 影响：直接用 `Animator` 播这段会「播完定住 2.37 s」。
> 处理：`HurricaneDroneUnit` 按 `attackClipSeconds = 2.97` 计时收尾（`autoExitAttack` 默认开），
> 所以玩法上看不出问题。如果以后要严格修 take 长度，改 Blender 的 NLA 导出即可，不必改代码。

`HurricaneDroneUnit` 已实现 `OnGunFire()` 接收点，但目前**只广播 `ShotFired` 事件、不做任何伤害结算**（避免与既有的 `WeaponDamageStats` / `MissileProjectile` 两套结算重复）。

---

## 5. 留给程序员的接入点（按优先级）

### P0 · 让它飞起来

`HurricaneDroneUnit` 只管动画，**不含任何位移逻辑**。需要新增一个飞行控制组件（建议名 `HurricaneDroneFlight`），负责：

- 空中巡航路径 / 悬停点 / 追击位移；
- 按机动幅度切换动画：平移大 → `FlyForward`，转向 → `BankTurn`，静止 → `Hover`（`SetMode`）；
- 朝向对齐速度方向（模型正面是 **+Z**）。

参考现成实现：`Assets/Scripts/TowerDefense/MonsterPathFollower.InitializeFlying` 那套「完全不使用怪物网格的另一条移动分支」，飞行怪的 `IsFlying` 就是这么做的；不要为无人机再写一套网格寻路。

### P0 · 开火与伤害

在 `HurricaneDroneUnit.OnGunFire()` 里选择一种结算方式，**不要三套并存**：

1. **纯特效 + 瞬时射线**：从 `MuzzlePosition` 沿 `MuzzleRotation * forward` 做 `Physics.Raycast`，用 `WeaponDamageStats` 算伤害（和玩家武器同一套公式与弱点倍率）；
2. **复用弹道**：照 `MissileProjectile` 的「投射物自行结算伤害」写法做一个机炮弹丸，这样不需要 Collider、也不受物理层影响；
3. **只放特效**：如果玩法上无人机只是氛围单位，`ShotFired` 事件留给特效/音效即可。

注意：机炮是 **40 mm 自动炮**，设计上没有追踪能力，建议用直线弹道 + 小溅射；不要套 `MissileAimSolver` 的拦截点预测（那是给对空导弹用的）。

### P1 · 血量、死亡、回收

- 现有陷阱统一走 `TrapInstance.CurrentHealth / MaxHealth`，**空中单位是敌人还是陷阱要先定**；目前无人机按「敌方空中单位」定位（与 `EnemyInstance.MonsterType == Flying` 同一套判据）。
- 死亡动画**没有做**。可选方案：程序侧用 `Body` 骨骼做坠落自转（不用新动画），或让美术补一段 `HD_Crash`（约 1.5 s，机体翻滚 + 桨叶停转）。
- 生成与回收复用空中出怪口：`EnemySpawnPoint.flyingEntrance` + `Resources.Load("FlyingMonster")` 的兜底模式；无人机若要对象池，注意 `Animator` 需要 `Rebind()`。

### P1 · 表现补充（美术已留好挂点，缺资源）

- 枪口火光 / 烟：挂到 `Muzzle`；侧向抛壳可从 `Gun_Pitch` 右侧的弹药箱位置出（`HD_AmmoBox` 在模型 +X 侧）；
- 螺旋桨风效：`Fan_*` 骨骼正下方；
- 击毁爆炸 / 曳光弹：走项目现有特效池。

### P2 · 性能

- 单个无人机：1 个蒙皮网格、8 个材质槽 → 8 个 draw call。若同屏数量 ≥ 6，建议在 `HurricaneDroneAuthoring` 里把材质合并成 3~4 个（白壳 / 深色金属 / 发光 / 黄铜），或改用图集；
- 距离 ≥ 60 m 时可切到 `Animator.cullingMode = CullCompletely`（预制体当前是 `AlwaysAnimate`，为了远处也看得见桨叶转动，按需改）。

---

## 6. 已验证 / 未验证

**已核对**

- 模型导入：尺寸 7.70 × 7.70 × 2.36 m，8 个材质槽，8 根骨骼齐全，`Muzzle` 位置正确（`ProbePrefabBatch` 输出）；
- 动画：4 段 clip 名称 / 时长 / 循环标志正确，`HD_Attack` 有 6 个 `OnGunFire` 事件（`HurricaneDronePreview.ReportClips` 输出）；
- 材质：URP Lit 重映射成功，`M_HD_*` 材质名与 FBX 内材质名一致（`.meta` 的 `materials` 段）。

**未验证 / 未执行**

- **主场景 PlayMode 人工验收**：没有把无人机放进 `THREE_ROUTE_MERGE_MAP.unity` 跑过；
- **Unity 侧离屏预览图不可用（环境限制，非本资产问题）**：`HurricaneDronePreview.RenderPreview`
  在 batchmode 下出图全是纯背景，两种渲染路径都试过 —— `Camera.Render()` 与
  `RenderPipeline.SubmitRenderRequest`（URP 正确做法）均只出背景色；
  用现成的 `MissileLauncherTurretPreview.RenderPreview` 复测**同样是纯色图**，说明是这台机器 /
  这套工程在 batchmode 下的渲染限制，与无人机资产无关。视觉验收以 Blender 预览图为准。
  想在编辑器里再试：菜单 `Tools/塔防/预览飓风无人机`（有 Game 视图时往往能出图）；
- 战斗中表现（开火特效、弹道手感）、音效、LOD、对象池均未接入；
- 完整 Player 构建。

---

## 7. 重建流程

1. 改模型：编辑 `C:\Users\ASUS\Desktop\陷阱素材文件夹\HurricaneDrone\build_drone_v3.py`，跑
   `"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P build_drone_v3.py`
   → 覆盖 FBX 并重出 `preview_*.png`；
2. 回 Unity 跑 `HurricaneDroneAuthoring.CreateAll`（菜单 `Tools/塔防/生成飓风无人机预制体`）；
3. 跑 `Tools/塔防/校验飓风无人机预制体` 看数值是否仍然对得上设计图。

**注意**：预制体、材质、控制器都是生成物，手改会在下次 `CreateAll` 时被覆盖。要改外观请改 Blender 脚本或 `HurricaneDroneAuthoring` 里的定义。
