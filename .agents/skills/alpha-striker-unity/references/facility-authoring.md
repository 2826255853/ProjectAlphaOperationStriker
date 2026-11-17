# 双层设施与多层地图生成

用于双层设施结构、楼梯、坡道、货运路线和扩容任务。以下经验来自 2026-11-16 至 2026-11-18 的实现；尺寸、数量和能力状态以当前源码与项目记忆为准。

## 先选对生成入口

- Unity 源：`Assets/Editor/MapSources/double-layer-facility.json`；配套 MapForge 示例：`C:\MapForge\wwwroot\examples\double-layer-facility.json`。修改共享源时同步这两份并核对 SHA256；正式重建直接读 JSON，不依赖桌面的一次性生成脚本。
- 生成器：`Assets/Editor/DoubleLayerFacilityAuthoring.cs` 与 `.Cargo.cs` 是同一个 partial 类。`Build` 生成并保存；`ValidateSavedScene` 重载校验；`BuildAndPreview` 生成并渲染；`RenderPreview` 只用于已有场景校验和预览。渲染通过 runner 的 `-WithGraphics` 启用图形。
- 目标：`Assets/Scenes/DOUBLE_LAYER_FACILITY.unity`，加入启用的 Build Settings 后由 `MapSceneCatalog` 提供地图选择。不能把生成/注册成功当成跨场景菜单操作已通过。
- 当前是 geometry-only：在内存中保留 collider 映射、过滤玩法映射，并补楼梯平滑碰撞面。源里的休眠玩法信息保留供后续接入；不要删除源字段来实现过滤。普通 `ImportDefault` 或「Send to Unity」没有这套过滤与补充，不能替代专用入口。
- 产物、备份、日志放 `C:\Users\ASUS\Desktop\陷阱素材文件夹\DoubleLayerFacility\`。生成器会备份目标 scene、meta 和旧预览；修改源或生成器前仍需保留它们自己的恢复点。

## 几何与通行共同验收

1. 先核对源的唯一 ID、父级、links、尺寸与走面高度。坡道、栏杆、支撑、洞口与货运节点在源 JSON 中一起维护；货运验证复用 `route-cargo-*` 图及其 links，不另写一份坡高或路线公式。玩家楼梯路径不自动并入货运图。
2. 楼梯要检查上下两个方向、平台转弯和出口，用真实 FPSPlayer CharacterController 的尺寸、stepOffset、slopeLimit、skinWidth 执行 Move 探针。可见踏步与平滑碰撞面须对齐，不能只用中心射线宣称玩家能走。
3. 大怪路线要检查整只代理：当前 `.Cargo.cs` 使用 8×12×4.2 m 代理、额外 0.6 m 净空、沿线 0.5 m 和转角 5° 采样。具体参数随需求修改，不作为所有怪物的固定尺寸。出生点、转弯、变坡和终点均需有完整足迹支撑；中心能过不代表身体能过。
4. 坡道接缝射线可能命中端面。检查命中法线、邻近走面与实际支撑高度，不能通过忽略全部坡面碰撞来“修复”失败；净空检查仍需发现梁、栏杆和平台边缘。
5. 非等比缩放图元要同时检查 Renderer 和 Collider。此前扁平圆柱的 CapsuleCollider 穿楼板堵住一层，修复限定在该地图基座形状；不要把一次地图问题扩展成全局导入器改造。
6. 保存前与重新载入后验证受影响的不变量。临时探针在 finally 销毁；剖切预览临时隐藏 Renderer 后恢复原状态，不把剖切状态存入场景。结构改动输出总览、上下层、关键坡道/转弯和剖面，并实际打开检查。

## 报告能力边界

- 路径节点存在、代理扫掠通过，只证明作者指定路线的几何通行；不证明运行时 AI 寻路、动画、刷怪或战斗。
- `defense-reservation` 是几何预留区，需检查障碍、净空及支撑；它不是已启用的 TrapPlacementGrid。
- 地板面积、可放陷阱面积、运行时格数、路线长度分别报告。宣称增幅前用同一口径测量旧图和新图；地板扩容比例不能代替陷阱容量或路线增幅。
- 分开列出编译、生产探针、预览目视检查、人工 PlayMode、跨场景操作与 Player 构建的执行情况；旧里程碑的通过记录不作为本轮通过。
