# 在 Unity 里跑编辑器脚本与校验

## 编辑器路径

版本策略见 [SKILL.md 的 Unity 编辑器版本](../SKILL.md#unity-编辑器版本)：本项目始终使用 `ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion`，更换项目版本属于独立迁移，不因运行脚本而自动升级。

```
C:\Program Files\Unity <m_EditorVersion>\Editor\Unity.exe
C:\Program Files\Unity\Hub\Editor\Unity <m_EditorVersion>\Editor\Unity.exe
```

版本号和安装目录不要写死。`scripts/run-unity-method.ps1` 先按项目记录版本在常见直接安装与 Hub 路径查找，找不到时会扫描并选择另一已安装版本、报告实际路径；只有预发布版时会明确告警。此回退不等于项目版本验证：如果报告版本与 `ProjectVersion.txt` 不一致，停止，不把结果当作本项目通过。自定义位置可用 `-UnityPath` 指向已核实的编辑器。不要读取 `Library/` 或历史日志推断版本。

## 批处理模板

```powershell
& "C:\Unity Project\ProjectAlphaOperationStriker\.agents\skills\alpha-striker-unity\scripts\run-unity-method.ps1" MapForgeWorldImporter.ImportDefault
```

它做的事等价于：

```
Unity.exe -batchmode -nographics -quit `
  -projectPath "C:\Unity Project\ProjectAlphaOperationStriker" `
  -executeMethod MapForgeWorldImporter.ImportDefault `
  -logFile "<temp>\alpha-striker-unity\<时间戳>-<方法名>.log"
```

- `-quit` 必须有，否则跑完方法也不会退出。
- `-nographics` 用于纯数据操作（导入、生成、校验），明显更快；只有需要渲染/截图时才去掉（脚本用 `-WithGraphics`）。
- `-executeMethod` 只能调用**静态**方法，写成 `类型名.方法名`。**`private static` 也可以**（已实测：`TrapPlacementGridEditor.ValidateTrapGrids` 是 private，成功执行）。类型必须在已编译的程序集里，编译器不认识的类型名会直接中止。
- 日志默认写到 `%TEMP%\alpha-striker-unity\`，不污染仓库（根目录 `*.log` 虽然被 gitignore，但 AGENTS.md 也禁止读它）。脚本还会在旁边写 `<日志>.stdout.txt` / `.stderr.txt` 两个副文件——**Unity 会把 `Aborting batchmode due to failure:` 这类信息打到 stdout 而不是日志文件**，少了这两路就会漏判。
- 一次完整运行的日志大约 28–50 KB（含启动、包列表、内存统计）。整体耗时 15–20 秒左右。

## 退出码不可信（实测数据）

仓库根目录遗留的日志正好构成一组对照：

| 日志 | 大小 | 有 `Exiting batchmode successfully now!` | 进程退出码 |
| --- | --- | --- | --- |
| `unity_ladder_generate4.log` | 34.1 KB | 有 | 0 |
| `unity_place_ladders.log` | 35.7 KB | 有 | 0 |
| `mapforge_import_three_route.log` | 31.4 KB | 有 | 0 |
| `unity_ladder_generate2.log` | 56.5 KB | **无**（方法抛异常） | 1 |
| `unity_ladder_generate3.log` | 28.9 KB | **无**（方法抛异常） | 1 |
| `unity_ladder_generate.log` | 1.2 KB | **无**（启动即中止） | 1 |
| `unity_import.log` | 1.0 KB | **无**（启动即中止） | 1 |

结论：**不要用 `$LASTEXITCODE` / `ExitCode` 判断成败。** 判定顺序：

1. 日志里有 `Exiting batchmode successfully now!` → 方法确实跑完了。
2. 日志只有 1.0–1.2 KB 且结尾是 `Application will terminate with return code 1` → Unity 在启动或脚本编译阶段就中止，`-executeMethod` **根本没执行**，不要以为“改的东西生效了”。注意这时日志尺寸很小且**没有** `Exiting batchmode successfully now!`——反过来，只要那个标记出现了，日志是多大都算成功。
3. 有 `error CS` → 编译错误。本工程 `Assets/KINEMATION`、`Assets/TextMesh Pro` 不在仓库里，缺这两个包的缺类型错误属于预期，其余 `error CS` 要当回事。
4. 有 `Exception:` → 方法抛异常（上面 `unity_ladder_generate2/3.log` 就是 `MissingComponentException`，Unity 直接中止）。此时**可能已经有半途写入的资产**，要检查生成物再决定是否回滚。

`scripts/run-unity-method.ps1` 已实现以上判定：0 = 日志确认成功，1 = 确认失败，2 = 环境问题。

## Library/ 单实例锁

同一时刻只有一个 Unity 实例能持有 `Library/`。编辑器开着（或上一次批处理没退干净）时批处理会卡住或直接失败。脚本会在启动前检查 `Unity.exe` 进程并给出警告；真遇到「日志只有 1 KB」时，先确认没有 Unity 在跑。

## 现成的菜单项 ↔ 可批处理的方法

`Assets/Editor` 里已有这些入口。菜单项是给人点的，`-executeMethod` 要的是 `类型名.方法名`：

| 菜单 | 批处理参数 | 可批处理 |
| --- | --- | --- |
| Tools/Map/Create Roadside Platforms | `CreateRoadsidePlatforms.Create` | 是 |
| Tools/Map/Place Symmetric Gameplay Ladders | `PlaceGameplayLadders.Place` | 是 |
| Tools/Generate Gameplay Ladder Prefab | `GenerateLadderPrefab.Generate` | 是 |
| Tools/Test Monster/Create Prefab | `CreateTestMonsterPrefab.Create` | 是 |
| Tools/塔防/生成飞行怪物预制体 | `FlyingSpawnPointAuthoring.CreateFlyingPrefab` | 是 |
| Tools/塔防/在场景中添加飞行出怪口 | `FlyingSpawnPointAuthoring.AddFlyingSpawnPoint` | 是 |
| （无菜单，批处理专用） | `FlyingSpawnPointAuthoring.CreateAssets` | 是 |
| MapForge/Redesign map with ceiling trap arches | `RedesignMapWithCeilingArches.Build` | 是 |
| MapForge/应用待同步版本 (Apply pending revision) | `MapForgeLiveSync.ApplyPendingFromMenu`（批处理直接调 `MapForgeLiveSync.ApplyPending()`） | 是 |
| MapForge/下次发送时完整重建 (Request full rebuild) | `MapForgeLiveSync.RequestFullRebuild` | 是 |
| MapForge/Import bundled world | `MapForgeWorldImporter.ImportDefault` | 是 |
| MapForge/Import mapforge-world.json | `MapForgeWorldImporter.ImportFromMenu` | **否**，会弹文件面板，批处理下必挂 |
| Tools/Tower Defense/Create Trap Grid From Monster Grid | `TrapPlacementGridEditor.CreateFromMonsterGrid` | 是 |
| Tools/Tower Defense/Create Ground And Platform Trap Grids | `TrapPlacementGridEditor.CreateGroundAndPlatformGrids` | 是 |
| Tools/Tower Defense/Validate Trap Grid Overlap | `TrapPlacementGridEditor.ValidateOverlap` | 是 |
| Tools/Tower Defense/Validate Trap Grids | `TrapPlacementGridEditor.ValidateTrapGrids` | 是 |
| Tools/塔防/生成导弹发射器预制体 | `MissileLauncherTurretAuthoring.CreateLauncherPrefab` | 是 |
| （无菜单，批处理专用） | `MissileLauncherTurretAuthoring.CreateLauncherPrefabBatch` | 是 |
| Tools/塔防/预览导弹发射器旋转 | `MissileLauncherTurretPreview.RenderPreview` | 是 |

`TrapPlacementGridEditor` 里这四个方法都是 `private static`：**实测 `-executeMethod` 能直接调用，不用改成 public**，也不用加包装方法。跑完在日志里确认方法确实执行了（例如 `Validate Trap Grids` 会打印 `陷阱网格校验：...`），而不是只看有没有报错。

## 更快的编译检查（不进 Unity）

`C:\MapForge\.validation-build\UnityCompile.csproj` 是局部辅助编译器，只能检查其 `<Compile Include>` 实际列出的文件及其引用；未列入的运行时代码、编辑器代码不会被检查。改完后可用它快速发现覆盖范围内的编译问题：

```powershell
dotnet build "C:\MapForge\.validation-build\UnityCompile.csproj"
```

UnityEngine 引用已改成 glob（`C:\Program Files\Unity *\Editor\Data\Managed\UnityEngine\*.dll` + Hub 通配），Unity 升级换版本号后**不用手改**，但仍不要把旧版 DLL 的编译结果当成新版验证。它还引用本工程的 `Library/ScriptAssemblies/Assembly-CSharp.dll`（netstandard2.1 / LangVersion 9），当前只编译这 7 个文件：

- `Assets/Scripts/MapForgeObjectProperties.cs`
- `Assets/Editor/MapForgeSceneOrganization.cs`
- `Assets/Editor/MapForgeWorldImporter.cs` + `Assets/Editor/MapForgeWorldImporter.Authored.cs`（同一个 partial class，两个都要在）
- `Assets/Editor/TrapGridAuthoring.cs`
- `Assets/Scripts/MapForgePathNode.cs`
- `Assets/Scripts/MapForgeTrigger.cs`

改了别的文件先确认它确实在 `<Compile Include>` 中；否则检查会**静默漏掉**改动。编译时的 `CS0436` 警告（UnityEngine 类型被 Assembly-CSharp 重复引入）是良性的，可以忽略。若辅助工程缺少其导入清单以外的项目类型，这个失败不能直接判定 Unity 工程编译失败；以项目版本对应的正式 Unity 编译为准。不要为让局部辅助工程通过而扩修无关导入器。

尤其要注意，单独的辅助构建不能替代完整工程编译、PlayMode 行为或场景验收。报告结果时写明实际覆盖范围与未执行的验证。

## 场景与预制体怎么读

`.unity` / `.prefab` / `.asset` 是大体量 YAML，**不要整文件读**。用关键字定位再读局部：

```powershell
Select-String -Path "Assets\Scenes\THREE_ROUTE_MERGE_MAP.unity" -Pattern 'MonsterPathGrid|TrapPlacementGrid|m_Name:'
```

主要场景是 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`。注意 MapForge 导入会**覆盖**它（见 [mapforge-pipeline.md](mapforge-pipeline.md)）。

## 跑编辑期测试（导弹发射器 / 陷阱血条 / 飞行出怪口 / 地面近战）

下列当前位于 `Assets/Editor` 的测试类用 `#if UNITY_INCLUDE_TESTS` 包住，普通 `-executeMethod` 不会运行测试，必须使用 Unity Test Framework 的 `-runTests`。测试源码如果从 `Tests~/` 临时暂存，按 [测试源码隔离与收尾](#测试源码隔离与收尾) 清理：

- `FlyingSpawnPointTests.cs` / `GroundEnemyCombatTests.cs`：空中出怪口的回归网，断言飞行怪出生在 `DefaultEntranceAltitude` 高度、`IsFlying` 为真且**没有** `GroundEnemyCombat`，同时校验普通出怪口仍留在地面。
- `TrapHealthBarUITests.cs`：陷阱血条选靶、身后/被遮挡过滤、低血比例。
- `MissileLauncherTurretTests.cs`：两轴限位（yaw 全圈、pitch 不超过 75°）、生成预制体的 rig 接线、原始 FBX 能自建 rig、空中过滤忽略地面怪。
- `MissileLauncherWeaponTests.cs`：选靶优先级与 tie-break、瞄准拦截点、齐射一发一管、装填阻塞、超程引爆、无 Collider 也结算伤害。

```powershell
Unity.exe -batchmode -nographics -projectPath "C:\Unity Project\ProjectAlphaOperationStriker" `
  -runTests -testPlatform EditMode -testFilter MissileLauncher `
  -testResults "$env:TEMP\missile.xml" `
  -logFile "$env:TEMP\missile-tests.log"
```

判定要检查 XML 里的预期 fixture 和用例数、失败数、跳过数，以及日志中的 Unity 编译状态；不要只看进程退出码或成功退出标记。局部辅助 `.csproj` 的覆盖范围有限，见[更快的编译检查](#更快的编译检查不进-unity)。

## 测试源码隔离与收尾

- 持久 Unity 测试源码放 `Tests~/Editor/`，不放进 Unity 会导入的 `Assets/`。Unity 忽略 `~` 后缀目录，runner 可在测试期间按需暂存。
- `RunTrapUpgradeRulesTests.ps1` 会把所选 fixture 临时复制到 `Assets/Editor`，调用现有 runner，并在 `finally` 清除暂存 `.cs` 与对应 `.meta`。其他临时 fixture runner 也须保证失败、超时路径会清理。
- 测试是否通过以 XML 中实际执行的用例数、失败数和跳过数为准；确认每个预期 fixture 都运行，不能只看 Unity 进程退出码或成功退出标记。
- 测试结束后确认临时源码和 `.meta` 已不存在，再启动正常 Unity 编译、打开编辑器或执行其他导入验证。不要依赖 `#if UNITY_INCLUDE_TESTS` 让完成的测试源码留在 `Assets/`。
- PlayMode / EditMode 夹具需避免意外销毁对象：`EnemyHealth` 进入死亡路径会调用 `Destroy`，在 EditMode 清理时可能报错并污染后续用例；需要聚焦其他行为时给夹具足够高的血量。
- 测试能证明其覆盖的契约，不自动证明完整玩家构建、主场景人工操作或视觉效果。最终记录已执行的验证与明确未执行的项目。
