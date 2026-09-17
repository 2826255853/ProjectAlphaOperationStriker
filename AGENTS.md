# AGENTS.md

## 首要目标：尽早产出文件修改

本项目的任务默认期望**先动手、后完善**。长时间只读探索不给文件修改，视为失败的工作方式。

### 硬性节奏要求

1. **探索预算**：在开始任何探索前，先用 3 行以内说明「我要改哪个文件、改成什么样」。随后最多执行 5 次只读命令（含搜索、读文件）。达到上限后必须开始编辑文件。
2. **早写**：一旦确定第一个要修改的文件，立即写入最小可用改动，再逐步完善。不要等「全部想清楚」才落笔。
3. **每 10 分钟一次可见进展**：至少产生一次文件写入或一次明确的中间汇报（改了什么、下一步是什么）。若 10 分钟内没有文件写入，立即停止分析，改为提交当前最小可行补丁。
4. **不确定就定默认值**：拿不准需求时，选择合理默认实现，并在代码中加 `// TODO(确认): ...` 注释说明。不要为了等确认而停下。
5. **30 分钟硬性时限**：从任务开始计时，若 AI 在 30 分钟内仍未处理完毕，**立即停止启动任何新的测试**。当前正在执行的测试允许跑完，但跑完后不再进入后续测试，直接切换到项目编辑（写代码 / 改配置 / 改场景与资源），并优先产出可提交的最小文件改动。
6. **超时后仍要汇报**：因超时切换到编辑时，用一句话说明「已完成的测试 + 放弃的测试 + 现在开始改哪个文件」，然后立刻落笔。

### 探索范围约束

- 只读以下目录：`Assets/Scripts`、`Assets/Editor`、`Assets/Models`、`ProjectSettings`、`Packages`。
- **禁止**读取或遍历：`Library/`、`Logs/`、`Temp/`、`obj/`、`*.blend`、`*.blend1`、根目录下的 `*.log`。
- `.unity` / `.prefab` / `.asset` 为 YAML，体量大。用 `Select-String` 按关键字（GUID、组件名、对象名）定位行号，只读取定位到的局部片段，**不要整文件读取**。
- 优先用 `git status` / `git diff --stat` 了解现状，而不是全量列目录。

### 任务切分

- 把任务拆成多个 ≤15 分钟的单元，每个单元必须结束于一次文件修改。
- 大改动先提交骨架（类、方法签名、空实现），再填充逻辑。
- 每个逻辑单元完成后立即给出简短 diff 摘要，不要攒到最后一次性汇报。

### 本项目已知信息（避免重复探索）

- Unity 版本：编辑器内已安装 `6000.5.6f1` 与 `6000.6.0f1`；生成的 csproj 指向 `6000.6.0f1`。
- 未纳入仓库的第三方包：`Assets/KINEMATION`、`Assets/TextMesh Pro`。缺少它们时 C# 编译会报缺失类型，这属于预期情况，不要为此反复排查。
- 主要场景：`Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity`。
- 代码目录：`Assets/Scripts/FPS`、`Assets/Scripts/TowerDefense`、`Assets/Editor`。
- 近期新增能力（2026-09）：**空中出怪口**（场景对象名 `Enemy Spawn Flying`，预制体 `Assets/Resources/FlyingMonster.prefab`，由 `Assets/Editor/FlyingSpawnPointAuthoring.cs` 生成/放置；飞行移动走 `MonsterPathFollower.InitializeFlying`，不走怪物路径网格）与**打开陷阱菜单时锁定玩家视角**（判据 `TrapSelectionMenu.CursorOwned`，`FirstPersonController` / `FPSPackagePlayerMotion` 两份 `UpdateCursor` 都尊重它）。细节见 `.agents/skills/alpha-striker-unity/SKILL.md`。
- 根目录下的 `*.py` 为 Blender 生成/导出脚本，与 Unity 运行时代码无关。

### 中断与升级

- 30 分钟到点：跑完当前测试即视为中断点。不再排队或启动新测试，转为直接编辑项目文件，并在本轮汇报里标注哪些测试被放弃。
- 若连续两次尝试仍无法定位问题，直接汇报「已确认的事实 + 已排除的可能 + 需要用户提供的信息」，不要继续无限探索。
- 用户说「先做个最小版本」时，只做能跑通的最小改动，不附带重构与优化。