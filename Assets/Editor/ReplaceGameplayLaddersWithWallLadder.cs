using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 把主场景里原有的 GameplayLadder（斜靠/独立式）实例换成新做的贴墙梯子 WallLadder。
///
/// 做法：
/// 1. 从每根旧梯子的脚点和「朝平台方向」出发，向平台一侧打水平射线，找到真正的墙面；
/// 2. 在墙面上往平台内部取一点、从上方向下打射线，得到上层平台顶面高度；
/// 3. 贴墙梯子的安装面是本地 Z = 0、梯身朝本地 +Z，所以根节点放在墙面上并旋转 180°
///    （本地 +Z = 背离墙、玩家所站的一侧），本地 -Z 就是顶部下梯方向（朝平台内侧）；
/// 4. 顶端对齐平台顶面（与旧梯子的攀爬上界一致，玩法可达性不变），按高度等比缩放；
/// 5. 删除旧梯子组，输出数值校验报告与预览图。
///
/// 可重复运行：场景里已经没有 GameplayLadder_* 时，改以现有 WallLadder_* 为基准重新测量并重建，
/// 因此修正参数后直接重跑即可，不需要回滚场景。
///
/// 菜单：Tools/Map/用贴墙梯子替换原有梯子
/// 批处理：ReplaceGameplayLaddersWithWallLadder.RunBatch（预览需要 -WithGraphics）
/// </summary>
public static class ReplaceGameplayLaddersWithWallLadder
{
    public const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";
    public const string OldGroupName = "GameplayLadders_Symmetric";
    public const string NewGroupName = "WallLadders_Symmetric";
    public const string OldNamePrefix = "GameplayLadder_";
    public const string NewNamePrefix = "WallLadder_";

    /// <summary>安装面离墙面留 5 mm，避免安装板与墙面共面产生 z-fighting。</summary>
    private const float WallGap = 0.005f;
    /// <summary>顶端相对平台顶面的抬高量；0 = 与旧梯子攀爬上界完全一致。</summary>
    private const float TopLip = 0f;
    private const float MinScale = 0.30f;
    private const float MaxScanDistance = 3f;

    private sealed class Placement
    {
        public string Name;
        public string SourceName;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public float FootY;
        public float PlatformTopY;
        public float ExpectedHeight;
        public Vector3 TowardsPlatform;
        public string WallCollider = "<none>";
        public string TopCollider = "<none>";
        public readonly List<string> Notes = new List<string>();
    }

    [MenuItem("Tools/Map/用贴墙梯子替换原有梯子")]
    public static void ReplaceAndPreview() => Run();

    public static void RunBatch() => Run();

    private static void Run()
    {
        string output = OutputRoot();
        Directory.CreateDirectory(output);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLadderAuthoring.PrefabPath);
        if (prefab == null)
        {
            WallLadderAuthoring.CreateAssetsBatch();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallLadderAuthoring.PrefabPath);
        }
        if (prefab == null) throw new InvalidOperationException($"找不到贴墙梯子预制体：{WallLadderAuthoring.PrefabPath}");

        if (File.Exists(ScenePath))
            File.Copy(ScenePath, Path.Combine(output, "THREE_ROUTE_MERGE_MAP_before_wall_ladder.unity"), true);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 基准可以是旧梯子，也可以是上一次生成的贴墙梯子（可重复运行）。
        List<Ladder> sources = CollectSceneLadders(OldNamePrefix);
        bool fromOld = sources.Count > 0;
        if (!fromOld)
        {
            sources = CollectSceneLadders(NewNamePrefix);
            if (sources.Count == 0)
                throw new InvalidOperationException($"场景里既没有 {OldNamePrefix}* 也没有 {NewNamePrefix}* 梯子");
        }

        var plans = new List<Placement>();
        var probeLog = new StringBuilder();
        probeLog.AppendLine($"基准={(fromOld ? "旧 GameplayLadder" : "现有 WallLadder")}");
        foreach (Ladder source in sources)
        {
            // 旧梯子朝向平台；贴墙梯子的 forward 背离墙，所以朝平台方向取反。
            Vector3 towards = Flatten(source.transform.forward);
            if (!fromOld) towards = -towards;
            Placement plan = BuildPlacement(source.name, source.transform.position, towards, fromOld, probeLog);
            if (plan != null) plans.Add(plan);
        }
        if (plans.Count == 0) throw new InvalidOperationException("没有可替换的梯子点位");

        var group = new GameObject(NewGroupName);
        var instances = new List<GameObject>();
        try
        {
            foreach (Placement plan in plans)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                go.name = plan.Name;
                go.transform.SetPositionAndRotation(plan.Position, plan.Rotation);
                go.transform.localScale = Vector3.one * plan.Scale;
                instances.Add(go);
            }

            foreach (Ladder source in sources)
                if (source != null) Object.DestroyImmediate(source.gameObject);
            foreach (string stale in new[] { OldGroupName, NewGroupName })
            {
                var leftover = GameObject.Find(stale);
                if (leftover != null && leftover != group && leftover.transform.childCount == 0)
                    Object.DestroyImmediate(leftover);
            }

            var oldGroup = GameObject.Find(OldGroupName);
            if (oldGroup != null && oldGroup.transform.childCount == 0) Object.DestroyImmediate(oldGroup);

            Physics.SyncTransforms();

            string report = Validate(instances, plans, probeLog);
            File.WriteAllText(Path.Combine(output, "validation.txt"), report, Encoding.UTF8);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            RenderPreviews(instances, plans, output);

            Debug.Log($"[WallLadder] 已用贴墙梯子替换 {plans.Count} 根原有梯子（基准={(fromOld ? "旧梯子" : "现有贴墙梯子")}），" +
                      $"报告与预览：{output}");
        }
        catch
        {
            // 失败不保存场景：把新建层级清掉，场景仍可回滚到备份。
            foreach (GameObject go in instances) if (go != null) Object.DestroyImmediate(go);
            if (group != null) Object.DestroyImmediate(group);
            throw;
        }
    }

    // ------------------------------------------------------------------ 探测

    private static List<Ladder> CollectSceneLadders(string namePrefix)
    {
        var result = new List<Ladder>();
        foreach (Ladder ladder in Object.FindObjectsByType<Ladder>(FindObjectsInactive.Include))
        {
            if (ladder == null) continue;
            if (!ladder.gameObject.scene.IsValid() || ladder.gameObject.scene.path != ScenePath) continue;
            if (!ladder.name.StartsWith(namePrefix, StringComparison.Ordinal)) continue;
            result.Add(ladder);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return result;
    }

    private static Placement BuildPlacement(string sourceName, Vector3 foot, Vector3 towards, bool fromOld,
        StringBuilder log)
    {
        if (towards.sqrMagnitude < 1e-4f) towards = Vector3.forward;
        var plan = new Placement
        {
            SourceName = sourceName,
            Name = NewNamePrefix + sourceName.Substring(sourceName.IndexOf('_') + 1),
            TowardsPlatform = towards,
        };

        // 1) 墙面：从墙面外侧一点起，沿朝平台方向打水平射线。
        Vector3 origin = foot + Vector3.up * 0.9f - towards * 0.05f;
        if (!Physics.Raycast(origin, towards, out RaycastHit wall, MaxScanDistance, ~0,
                QueryTriggerInteraction.Ignore))
        {
            // 打不到墙面时退化成原位摆放（仍可运行，报告里标红）。
            plan.Position = foot;
            plan.Rotation = Quaternion.LookRotation(-towards, Vector3.up);
            plan.FootY = foot.y;
            plan.PlatformTopY = foot.y + 2f;
            plan.ExpectedHeight = 2f;
            plan.Scale = Mathf.Min(1f, 2f / WallLadderAuthoring.ModelHeight);
            plan.Notes.Add("没有命中墙面，按原位摆放");
            log.AppendLine($"{sourceName}: 墙面探针失败，退化摆放");
            return plan;
        }

        plan.WallCollider = wall.collider != null ? wall.collider.name : "<null>";
        float wallFacing = Vector3.Dot(Flatten(wall.normal), -towards);
        if (wallFacing < 0.7f) plan.Notes.Add($"墙面法线与朝向夹角异常 dot={wallFacing:F2}");

        // 2) 地面：玩家一侧（墙面外 0.15 m）向下找地板。
        Vector3 outsideProbe = new Vector3(wall.point.x, 0f, wall.point.z) - towards * 0.15f;
        float footY = foot.y;
        if (Physics.Raycast(new Vector3(outsideProbe.x, wall.point.y + 2.5f, outsideProbe.z), Vector3.down,
                out RaycastHit floor, 6f, ~0, QueryTriggerInteraction.Ignore))
            footY = floor.point.y;
        else
            plan.Notes.Add("墙面脚下没有探测到地板");

        // 3) 上层平台：从平台内部（墙面内侧 0.6 m）高处向下打，取最高的水平面。
        Vector3 topProbe = new Vector3(wall.point.x, wall.point.y, wall.point.z) + towards * 0.6f;
        float platformTop = footY + 2f;
        bool foundTop = false;
        foreach (RaycastHit hit in Physics.RaycastAll(topProbe + Vector3.up * 8f, Vector3.down, 16f, ~0,
                     QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.normal.y < 0.5f || hit.point.y <= footY + 0.5f) continue;
            platformTop = hit.point.y;
            plan.TopCollider = hit.collider != null ? hit.collider.name : "<null>";
            foundTop = true;
            break;
        }
        if (!foundTop) plan.Notes.Add("平台顶面探针失败，按 2 m 平台处理");

        float target = platformTop + TopLip - footY;
        if (target < 0.9f)
        {
            plan.Notes.Add($"平台过低({target:F2} m)，按最小值 0.9 m 处理");
            target = 0.9f;
        }
        float scale = target / WallLadderAuthoring.ModelHeight;
        if (scale > 1f)
        {
            plan.Notes.Add($"平台高 {target:F2} m 超过梯子模型 {WallLadderAuthoring.ModelHeight:F2} m，不拉伸");
            scale = 1f;
        }
        scale = Mathf.Max(scale, MinScale);

        plan.Position = new Vector3(wall.point.x, footY, wall.point.z) - towards * WallGap;
        plan.Rotation = Quaternion.LookRotation(-towards, Vector3.up);
        plan.FootY = footY;
        plan.PlatformTopY = platformTop;
        plan.Scale = scale;
        plan.ExpectedHeight = scale * WallLadderAuthoring.ModelHeight;

        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} -> {1}: wall={2}@({3:F2},{4:F2},{5:F2}) top={6}@{7:F2} floorY={8:F2} scale={9:F3} height={10:F2}",
            sourceName, plan.Name, plan.WallCollider, wall.point.x, wall.point.y, wall.point.z,
            plan.TopCollider, platformTop, footY, scale, plan.ExpectedHeight));
        return plan;
    }

    // ------------------------------------------------------------------ 校验

    private static string Validate(List<GameObject> instances, List<Placement> plans, StringBuilder probeLog)
    {
        var lines = new List<string> { "==== 探针 ====", probeLog.ToString().TrimEnd(), "==== 检查 ====" };
        int failed = 0;
        void Check(string name, bool ok, string detail)
        {
            if (!ok) failed++;
            lines.Add($"{(ok ? "PASS" : "FAIL")}\t{name}\t{detail}");
        }

        for (int i = 0; i < instances.Count; i++)
        {
            GameObject go = instances[i];
            Placement plan = plans[i];
            Ladder ladder = go.GetComponent<Ladder>();
            var trigger = go.GetComponent<BoxCollider>();
            Bounds b = WallLadderAuthoring.WorldBounds(go);

            Check($"{go.name} 有 Ladder 组件", ladder != null, ladder == null ? "null" : "ok");
            Check($"{go.name} 长轴竖直", b.size.y > b.size.x && b.size.y > b.size.z, Fmt(b.size));
            Check($"{go.name} 脚底贴地", Mathf.Abs(b.min.y - plan.FootY) <= 0.03f,
                $"minY={b.min.y:F4} floor={plan.FootY:F4}");
            Check($"{go.name} 顶端对齐平台顶面", Mathf.Abs(b.max.y - plan.PlatformTopY) <= 0.05f,
                $"maxY={b.max.y:F3} platformTop={plan.PlatformTopY:F3} scale={plan.Scale:F3}");

            // 贴合墙面：从墙面外侧 0.30 m 处朝墙（本地 -Z）打射线，剩余间隙应≈WallGap。
            Vector3 origin = go.transform.position + Vector3.up * 0.9f + go.transform.forward * 0.30f;
            bool flush = Physics.Raycast(origin, -go.transform.forward, out RaycastHit hit, 0.60f, ~0,
                QueryTriggerInteraction.Ignore);
            float gap = flush ? hit.distance - 0.30f : float.NaN;
            Check($"{go.name} 贴合墙面", flush && gap >= -0.002f && gap <= 0.03f,
                flush ? $"gap={gap * 1000f:F1} mm ({hit.collider.name})" : "射线未命中墙面");

            if (ladder != null)
            {
                Check($"{go.name} 攀爬上界=模型顶端",
                    Mathf.Abs(ladder.Top - b.max.y) <= 0.02f, $"Top={ladder.Top:F3} maxY={b.max.y:F3}");
                Check($"{go.name} 顶部下梯朝向平台内侧",
                    Vector3.Dot(ladder.DismountDirection.normalized, plan.TowardsPlatform) > 0.99f,
                    $"dismount={ladder.DismountDirection.ToString("F3")}");
            }

            if (trigger != null)
            {
                Bounds tb = trigger.bounds;
                Check($"{go.name} 触发区覆盖攀爬范围",
                    trigger.isTrigger &&
                    Mathf.Abs(tb.min.y - plan.FootY) <= 0.03f &&
                    Mathf.Abs(tb.max.y - b.max.y) <= 0.03f,
                    $"trigger=[{tb.min.y:F3},{tb.max.y:F3}]");
                Check($"{go.name} 触发区宽于模型", tb.size.x > b.size.x, $"{tb.size.x:F3} > {b.size.x:F3}");
            }

            Check($"{go.name} 只有一个碰撞体",
                go.GetComponentsInChildren<Collider>().Length == 1,
                go.GetComponentsInChildren<Collider>().Length.ToString());

            foreach (string note in plan.Notes) lines.Add($"NOTE\t{go.name}\t{note}");
        }

        var leftover = new List<string>();
        foreach (string name in CollectSceneLadders(OldNamePrefix).Select(l => l.name)) leftover.Add(name);
        Check("旧梯子已全部移除", leftover.Count == 0, leftover.Count == 0 ? "0" : string.Join(",", leftover));
        Check($"贴墙梯子数量 = {plans.Count}", instances.Count == plans.Count, instances.Count.ToString());

        int passed = lines.Count(l => l.StartsWith("PASS", StringComparison.Ordinal));
        lines.Add($"结果: {(failed == 0 ? "PASS" : "FAIL")} {passed}P/{failed}F");
        return string.Join(Environment.NewLine, lines);
    }

    // ------------------------------------------------------------------ 预览

    private static void RenderPreviews(List<GameObject> instances, List<Placement> plans, string output)
    {
        var cameraObject = new GameObject("Wall Ladder Preview Camera");
        var texture = new Texture2D(1400, 900, TextureFormat.RGB24, false);
        var target = new RenderTexture(1400, 900, 24);
        RenderTexture previous = RenderTexture.active;
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 60f;
            camera.allowHDR = false;

            for (int i = 0; i < instances.Count; i++)
            {
                Placement plan = plans[i];
                Vector3 forward = plan.Rotation * Vector3.forward;   // 玩家所站的一侧
                Vector3 look = plan.Position + Vector3.up * (plan.ExpectedHeight * 0.5f);
                camera.fieldOfView = 60f;
                camera.transform.position = look + forward * 2.6f + plan.Rotation * Vector3.left * 1.8f
                                            + Vector3.up * 0.7f;
                camera.transform.LookAt(look);
                Shot(camera, target, texture, Path.Combine(output, $"{instances[i].name}.png"));
            }

            if (instances.Count > 0)
            {
                Placement plan = plans[0];
                Vector3 forward = plan.Rotation * Vector3.forward;
                Vector3 look = plan.Position + Vector3.up * (plan.ExpectedHeight * 0.55f);
                camera.fieldOfView = 42f;
                camera.transform.position = look + plan.Rotation * Vector3.left * 1.5f + forward * 1.5f
                                            + Vector3.up * 0.2f;
                camera.transform.LookAt(look);
                Shot(camera, target, texture, Path.Combine(output, "00_side_flush.png"));
            }
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(texture);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void Shot(Camera camera, RenderTexture target, Texture2D texture, string file)
    {
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        texture.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(file, texture.EncodeToPNG());
        camera.targetTexture = null;
    }

    // ------------------------------------------------------------------ 工具

    private static string OutputRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "陷阱素材文件夹", "WallLadder", "scene-replace-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude < 1e-6f ? Vector3.zero : value.normalized;
    }

    private static string Fmt(Vector3 v) =>
        string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
}
