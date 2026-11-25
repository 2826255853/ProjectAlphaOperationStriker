# 阿尔法项目：强袭行动

`ProjectAlphaOperationStriker` 是一款使用 Unity 6 开发的第一人称射击与塔防游戏。玩家在波次间布置防线，在战斗中用枪械和陷阱拦截地面与空中敌人，保护核心。项目仍在开发中。本仓库保存项目自有代码、场景和配套资源。

## 当前玩法

- **局外作战大厅**提供地图选择、音量设置和战绩页，可查看完赛局数、胜负场次、胜率和战略资源余额。完成对局后，胜局获得 2,000 战略资源，败局获得 1,000，战绩与余额保存在本机。
- **波次防守**包含准备和战斗阶段。小地图与波次 HUD 提示出怪口和进攻路线，敌人突破防线抵达核心时会提示来源。守住核心并消灭所有波次的敌人即可获胜，核心被摧毁则判负。
- **第一人称战斗**支持枪械交战和无人机侦察，两个视角都能放置临时位置标记。枪械击杀时有准星标记和确认音效。
- **陷阱管理**通过投影枪在布防网格上放置陷阱。局内金币用于购买和升级，已部署的陷阱可出售，维修站可为受损陷阱恢复耐久。部分陷阱可在四级选择分支，例如哨戒机枪的「密集阵」和 XM777 的 RG777「雷锤」。

## 当前地图

| 地图 | 构建状态与玩法 | 场景 |
| --- | --- | --- |
| 雷鸣工厂 | 当前构建启用的地图。厂房分两层，包含地面进攻和空袭路线，支持地面、平台和天花板布防。 | [DOUBLE_LAYER_FACILITY.unity](Assets/Scenes/DOUBLE_LAYER_FACILITY.unity) |
| 三路汇合图 | 开发场景。加入并启用构建场景后，可从大厅选择。 | [THREE_ROUTE_MERGE_MAP.unity](Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity) |

大厅选图列表随 Unity 构建场景配置更新。见 [EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset)。

## 常用操作

| 按键 | 操作 |
| --- | --- |
| `N` | 打开或关闭陷阱菜单 |
| `4`–`8` | 选择已装备的陷阱槽位并进入放置模式 |
| 鼠标左键 | 在放置模式下部署当前陷阱 |
| `G` | 跳过波次准备倒计时 |
| `Z` | 在人物或无人机视角下标记准星指向的位置 |
| `Esc` | 返回菜单上一级或在对局中打开暂停菜单 |

## 恢复开发环境

1. 在 Unity Hub 添加项目根目录，使用 `m_EditorVersion` 指定的编辑器版本打开项目。见 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)。
2. 从有授权的原始资源包恢复 `Assets/KINEMATION`，保留原资源版本和 `.meta` 中的 GUID。原目录包含 `FPSAnimationPack`、`KAnimationCore`、`Plugins`、`ProceduralRecoilAnimationSystem` 和 `RetargetPro`。
3. 在 Unity 中导入 TextMesh Pro Essential Resources，恢复 `Assets/TextMesh Pro`。Package Manager 根据 [包清单](Packages/manifest.json) 和 [锁文件](Packages/packages-lock.json) 解析项目依赖。
4. 依赖导入完成后，打开雷鸣工厂场景，进入 Play Mode，在作战大厅选图并开始游戏。

`Assets/KINEMATION` 和 `Assets/TextMesh Pro` 需要在新克隆后单独恢复。玩家脚本直接依赖 KINEMATION 类型，场景和预制体通过 GUID 引用相关资源。

## 工程入口

- 玩家控制、第一人称枪械、无人机与作战大厅见 [Assets/Scripts/FPS](Assets/Scripts/FPS)。
- 敌人、波次、寻路、布防网格、陷阱升级与对局存档见 [Assets/Scripts/TowerDefense](Assets/Scripts/TowerDefense)。
- MapForge 导入与实时同步、地图和预制体生成工具见 [Assets/Editor](Assets/Editor)。
- 正式模型和随附贴图见 [Assets/Models](Assets/Models)。
- 开发记录、生成入口与验证方法见 [项目记忆](Assets/Editor/PROJECT_MEMORY.md)。

仓库保留 Unity `.meta` 文件以维护资源 GUID。Unity 缓存、日志、IDE 生成文件、模型备份和本机第三方资源按 Git 忽略规则管理。
