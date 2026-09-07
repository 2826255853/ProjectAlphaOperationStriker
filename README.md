# ProjectAlphaOperationStriker

基于 Unity 的第一人称射击与塔防项目。此仓库备份项目自有核心代码及配套资源。

## 仓库内容

- `Assets/Scripts/FPS`：第一人称控制、玩家初始化、弹药 UI。
- `Assets/Scripts/TowerDefense`：敌人、波次、寻路、陷阱放置及自动炮塔。
- `Assets/Editor`：地图导入、网格编辑、场景与预制体生成工具。
- `Assets/Models`：测试怪物模型及 Blender 生成脚本。
- 场景、预制体、Resources、陷阱定义、渲染设置和地图 JSON。
- `Packages`、`ProjectSettings`：包依赖与 Unity 项目配置。

保留 Unity `.meta` 文件以维护资源 GUID。忽略 Unity 缓存、日志、IDE 生成文件、模型备份和第三方资源包。

## 恢复开发环境

1. 克隆仓库，使用 Unity Hub 添加项目，安装并使用 **Unity 6000.5.6f1**。
2. 从自己的合法来源安装原项目使用的 KINEMATION 资源，原目录包含 `FPSAnimationPack`、`KAnimationCore`、`Plugins`、`ProceduralRecoilAnimationSystem` 和 `RetargetPro`。请保留原始资源 GUID。
3. 恢复 TextMesh Pro Essentials 资源至 `Assets/TextMesh Pro`；Unity Package Manager 会按 `Packages/manifest.json` 和锁文件解析包依赖。
4. 打开 `Assets/Scenes/THREE_ROUTE_MERGE_MAP 4.unity`，确认依赖导入完成后运行。

本仓库不包含 `Assets/KINEMATION` 和 `Assets/TextMesh Pro`。FPS 脚本直接引用 KINEMATION 类型，场景和预制体也可能引用这些包中的资源，因此仅克隆仓库尚不能直接编译运行。第三方包的确切版本未记录在 Unity 包清单中，恢复时应使用原项目相同版本。
