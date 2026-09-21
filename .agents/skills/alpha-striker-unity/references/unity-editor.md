# 在 Unity 里跑编辑器脚本与校验

## 编辑器路径

`ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion` 是 `6000.6.0f1`，机器上同时也装了 `6000.5.6f1`。

```
C:\Program Files\Unity\Hub\Editor\Unity 6000.6.0f1\Editor\Unity.exe
```

不要混用版本：`Library/` 是被哪个版本导入的，就继续用哪个版本跑。历史上的旧日志里有 6000.5.6f1 的批处理记录，现在统一用 6000.6.0f1。

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
| MapForge/Import bundled world | `MapForgeWorldImporter.ImportDefault` | 是 |
| MapForge/Import mapforge-world.json | `MapForgeWorldImporter.ImportFromMenu` | **否**，会弹文件面板，批处理下必挂 |
| Tools/Tower Defense/Create Trap Grid From Monster Grid | `TrapPlacementGridEditor.CreateFromMonsterGrid` | 是 |
| Tools/Tower Defense/Create Ground And Platform Trap Grids | `TrapPlacementGridEditor.CreateGroundAndPlatformGrids` | 是 |
| Tools/Tower Defense/Validate Trap Grid Overlap | `TrapPlacementGridEditor.ValidateOverlap` | 是 |
| Tools/Tower Defense/Validate Trap Grids | `TrapPlacementGridEditor.ValidateTrapGrids` | 是 |

`TrapPlacementGridEditor` 里这四个方法都是 `private static`：**实测 `-executeMethod` 能直接调用，不用改成 public**，也不用加包装方法。跑完在日志里确认方法确实执行了（例如 `Validate Trap Grids` 会打印 `陷阱网格校验：...`），而不是只看有没有报错。

## 更快的编译检查（不进 Unity）

改完 `Assets/Editor` 里的 C# 后，先用这个 csproj 做秒级编译检查，别每次都花几分钟起 Unity：

```powershell
dotnet build "C:\MapEditor\.validation-build\UnityCompile.csproj"
```

它引用 `Unity 6000.6.0f1` 的 UnityEngine DLL 和本工程的 `Library/ScriptAssemblies/Assembly-CSharp.dll`（netstandard2.1 / LangVersion 9），当前只编译这 5 个文件：

- `Assets/Scripts/MapForgeObjectProperties.cs`
- `Assets/Editor/MapForgeSceneOrganization.cs`
- `Assets/Editor/MapForgeWorldImporter.cs` + `Assets/Editor/MapForgeWorldImporter.Authored.cs`（同一个 partial class，两个都要在）
- `Assets/Editor/TrapGridAuthoring.cs`

改了别的文件就顺手往 csproj 的 `<Compile Include>` 里加一条，否则检查会**静默漏掉**改动。编译时的 `CS0436` 警告（UnityEngine 类型被 Assembly-CSharp 重复引入）是良性的，可以忽略。

注意这只覆盖 `Assets/Editor` 那一侧：`Assets/Scripts/FPS`、`Assets/Scripts/TowerDefense` 的运行时脚本改完还是得靠 Unity 编译（或加进上面的 csproj）。

## 场景与预制体怎么读

`.unity` / `.prefab` / `.asset` 是大体量 YAML，**不要整文件读**。用关键字定位再读局部：

```powershell
Select-String -Path "Assets\Scenes\THREE_ROUTE_MERGE_MAP.unity" -Pattern 'MonsterPathGrid|TrapPlacementGrid|m_Name:'
```

主要场景是 `Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`。注意 MapForge 导入会**覆盖**它（见 [mapforge-pipeline.md](mapforge-pipeline.md)）。

## 跑编辑期测试（飞行出怪口 / 地面近战）

`Assets/Editor/FlyingSpawnPointTests.cs` 与 `Assets/Editor/GroundEnemyCombatTests.cs` 的类体用 `#if UNITY_INCLUDE_TESTS` 包住，默认编译下不进程序集，因此：

- 想要它们在**批处理里真的跑**，命令要带 `-runTests`，不要套 `run-unity-method.ps1` 的普通模板。
- 只想确认编译是否通过（不跑断言），直接跑 [更快的编译检查](#更快的编译检查不进-unity) 那条 `dotnet build` 即可，前提是把这两个测试文件加进 csproj 的 `<Compile Include>`。

这两个用例正是「空中出怪口」的回归网：断言飞行怪出生在 `DefaultEntranceAltitude`（14）高度、`IsFlying` 为真、且**没有** `GroundEnemyCombat`；同时校验普通出怪口仍留在地面。改动 `EnemySpawnPoint` 的飞行分支后跑一次。
