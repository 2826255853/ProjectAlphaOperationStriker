using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unity half of MapForge's "send to Unity" button.
///
/// MapForge writes one revision into &lt;project&gt;/MapForgeSync/mail/pending.json and this
/// editor plug-in applies it to the map's scene. The scene is edited in place: created
/// objects are added, changed objects are rewritten on their existing GameObject, and
/// deleted objects are removed. A revision that did not touch an object leaves it alone,
/// so the whole scene is never torn down for an incremental send.
///
/// Only the generated MapForgeWorld hierarchy is touched. Everything the project authored
/// beside the map - the airborne "Enemy Spawn Flying" entrance, for example - survives a
/// sync instead of being deleted the way a full import deletes it.
///
/// A full rebuild is only done when MapForge says so (first send, renamed map, gameplay or
/// scene-level mapping changed, or most of the map changed). Even then the scene asset is
/// rebuilt in place, so it keeps its path, its GUID and the objects around the map.
/// </summary>
[InitializeOnLoad]
public static class MapForgeLiveSync
{
    /// <summary>Revision file format version, mirrored by MapForgeSync.Protocol.</summary>
    public const int Protocol = 1;

    private const string GeneratedRoot = MapForgeWorldImporter.RootName;
    private const double PollSeconds = 2.0;
    private const double HeartbeatSeconds = 15.0;

    /// <summary>Where a full revision's world file is written before the importer reads it.</summary>
    private const string WorldAssetPath = "Assets/MapForgeSync/sync-world.json";

    /// <summary>One revision, exactly as MapForge writes it into MapForgeSync/mail/pending.json.</summary>
    [Serializable] public class Revision
    {
        public int protocol = Protocol;
        public string editor;
        public string mode = "increment";
        public string map;
        public int revision;
        public int baseRevision;
        public string sentAtUtc;
        public Summary summary;
        public Counts counts;
        public Changes changes;
        public string rebuildReason;
        public bool unityChanged;
        public MapForgeSceneOrganization.UnitySceneData unity;
        /// <summary>Changed objects, in document (hierarchy) order. Present on an increment revision.</summary>
        public MapForgeSceneOrganization.Node[] objects;
        /// <summary>Whole world file as JSON text, present on a full revision and re-imported verbatim.</summary>
        public string world;
    }

    [Serializable] public class Summary { public int created, updated, deleted; }
    [Serializable] public class Counts { public int objects; }
    [Serializable] public class Changes { public string[] created, updated, deleted; }

    /// <summary>Everything one sync step reports, in the order the browser shows it.</summary>
    public class Outcome
    {
        public bool Ok;
        public string Message;
        public readonly List<string> Log = new List<string>();
        public int Created, Updated, Deleted, PrefabInstances, Colliders, NavMesh, SpawnPoints, Cores, PathNodes, Triggers, TrapGrids;
        public string SceneName;
    }

    private static double _nextPoll, _nextHeartbeat;
    private static bool _applying;

    static MapForgeLiveSync()
    {
        EditorApplication.update += Tick;
        EditorApplication.delayCall += Tick;
    }

    /// <summary>MapForgeSync folder inside this Unity project; the other half of the protocol lives there.</summary>
    public static string SyncRoot => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "MapForgeSync");
    private static string MailRoot => Path.Combine(SyncRoot, "mail");
    private static string PendingPath => Path.Combine(MailRoot, "pending.json");
    private static string AppliedPath => Path.Combine(MailRoot, "applied.txt");
    private static string AppliedJsonPath => Path.Combine(MailRoot, "applied.json");
    private static string OutboxRoot => Path.Combine(SyncRoot, "outbox");
    private static string ResultPath => Path.Combine(OutboxRoot, "result.json");
    private static string StatusPath => Path.Combine(SyncRoot, "status.json");

    private static void Tick()
    {
        var now = EditorApplication.timeSinceStartup;
        if (now >= _nextHeartbeat) { _nextHeartbeat = now + HeartbeatSeconds; Heartbeat(); }
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;
        if (_applying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        ApplyPending();
    }

    /// <summary>Machine-readable connection state, which is what MapForge's badge and its wait loop read.</summary>
    private static void Heartbeat()
    {
        try
        {
            var payload = new Json
            {
                ["protocol"] = Protocol,
                ["editor"] = "Unity",
                ["unityVersion"] = Application.unityVersion,
                ["projectPath"] = Directory.GetParent(Application.dataPath)!.FullName,
                ["appliedRevision"] = AppliedRevision(),
                ["sceneName"] = EditorSceneManager.GetActiveScene().name,
                ["map"] = ReadString(AppliedJsonPath, "map"),
                ["lastSeenUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["isPlaying"] = EditorApplication.isPlaying,
                ["hasMapForgeWorld"] = GameObject.Find(GeneratedRoot) != null,
                // Objects of the generated hierarchy the sync can match by ID. A scene built
                // before the importer wrote object metadata has none, which MapForge reads as
                // "this scene needs a full rebuild first" and reports before anything is written.
                ["objects"] = IdentifiedObjectCount(),
            };
            Directory.CreateDirectory(SyncRoot);
            File.WriteAllText(StatusPath, payload.ToIndentedString());
        }
        catch (Exception exception) { Debug.LogWarning("MapForge sync heartbeat failed: " + exception.Message); }
    }

    /// <summary>
    /// Objects under the generated root that carry a MapForge identity, i.e. the ones an
    /// incremental revision can find and rewrite. Counting them is how MapForge tells a
    /// scene it can patch from one that first needs a full rebuild.
    /// </summary>
    private static int IdentifiedObjectCount()
    {
        try
        {
            var root = GameObject.Find(GeneratedRoot);
            return root == null ? 0 : root.GetComponentsInChildren<MapForgeObjectProperties>(true).Length;
        }
        catch (Exception) { return 0; }
    }

    private static int AppliedRevision()
    {
        try { return File.Exists(AppliedPath) && int.TryParse(File.ReadAllText(AppliedPath).Trim(), out var applied) ? applied : 0; }
        catch (Exception) { return 0; }
    }

    [MenuItem("MapForge/应用待同步版本 (Apply pending revision)", priority = 2)]
    public static void ApplyPendingFromMenu()
    {
        var outcome = ApplyPending();
        if (outcome == null) Debug.Log("MapForge 同步：没有等待应用的版本。");
    }

    [MenuItem("MapForge/下次发送时完整重建 (Request full rebuild)", priority = 3)]
    public static void RequestFullRebuild()
    {
        try
        {
            Directory.CreateDirectory(MailRoot);
            File.WriteAllText(AppliedPath, "0");
            Debug.Log("MapForge 同步：已清除已应用版本，下一次发送会完整重建场景。");
        }
        catch (Exception exception) { Debug.LogError("MapForge 同步：无法清除状态 - " + exception.Message); }
    }
    // ---------------------------------------------------------------- poll and dispatch

    /// <summary>
    /// Applies the pending revision when it is newer than the applied one. Returns null when
    /// there is nothing to do, so callers can tell "idle" apart from "failed".
    /// </summary>
    public static Outcome ApplyPending()
    {
        if (!File.Exists(PendingPath)) return null;
        Revision revision;
        try { revision = JsonUtility.FromJson<Revision>(File.ReadAllText(PendingPath)); }
        catch (Exception exception)
        {
            Debug.LogError("MapForge sync: the pending revision could not be read (" + exception.Message + ").");
            return null;
        }
        if (revision == null || revision.revision <= 0) return null;
        if (revision.protocol > Protocol)
        {
            // Applying a newer format could corrupt the scene, so the revision is left in
            // place and MapForge keeps waiting instead of being told it succeeded.
            Debug.LogError($"MapForge sync: revision {revision.revision} needs protocol {revision.protocol} but this project implements {Protocol}. Update Assets/Editor/MapForgeLiveSync.cs.");
            return null;
        }
        int applied = AppliedRevision();
        if (revision.revision <= applied) return null;
        return Apply(revision, applied);
    }

    /// <summary>Applies one revision and reports the result back to MapForge.</summary>
    public static Outcome Apply(Revision revision, int appliedRevision)
    {
        _applying = true;
        var outcome = new Outcome();
        var watch = Stopwatch.StartNew();
        try
        {
            // A sync that left the scene dirty would otherwise be thrown away by the next
            // domain reload, so ask before discarding unfinished work.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                outcome.Ok = false;
                outcome.Message = "当前场景有未保存的修改，同步已取消。保存后重新发送即可。";
            }
            else if (IsFullRevision(revision))
            {
                outcome.Message = FullRebuild(revision, outcome);
                outcome.Ok = true;
            }
            else
            {
                outcome.Message = Increment(revision, appliedRevision, outcome);
            }
        }
        catch (Exception exception)
        {
            outcome.Ok = false;
            outcome.Message = "同步失败：" + exception.Message;
            outcome.Log.Add(exception.GetType().Name + ": " + exception.Message);
            Debug.LogError("MapForge sync failed: " + exception);
        }
        watch.Stop();
        // A failed step is not recorded as applied, so the revision is retried rather than
        // silently skipped, and the editor keeps the previous revision as its baseline.
        if (outcome.Ok) MarkApplied(revision);
        WriteResult(revision, outcome, (int)watch.ElapsedMilliseconds);
        _applying = false;
        return outcome;
    }

    /// <summary>A revision is a full rebuild when it carries the whole world file.</summary>
    private static bool IsFullRevision(Revision revision) =>
        revision.mode == "full" || !string.IsNullOrWhiteSpace(revision.world);

    // -------------------------------------------------------------------- full rebuild

    /// <summary>
    /// Rebuilds the generated hierarchy from the world file the revision carried. The scene
    /// asset itself is reused, so the rebuild keeps its path, its GUID and every object the
    /// project placed beside the map.
    /// </summary>
    private static string FullRebuild(Revision revision, Outcome outcome)
    {
        var sceneName = Sanitize(SceneNameFor(revision));
        if (string.IsNullOrWhiteSpace(revision.world))
        {
            outcome.Ok = false;
            return "这是一次完整重建，但版本里没有地图内容。请在 MapForge 里重新发送。";
        }
        var path = WriteWorldAsset(revision.world);
        var scenePath = SceneAssetPath(sceneName);
        var before = File.Exists(scenePath) ? IndexSceneObjects(sceneName) : null;
        if (File.Exists(scenePath))
        {
            // The scene exists, so rebuild only the generated root and keep the scene
            // file, which is what preserves the rest of the scene and its GUID.
            MapForgeWorldImporter.RebuildInPlace(path);
        }
        else
        {
            // First send for this map: there is no scene to patch, so let the importer
            // create it exactly the way the "Import MapForge world" menu item does.
            MapForgeWorldImporter.Import(path);
        }
        var scene = MapForgeWorldImporter.OpenTargetScene(sceneName);
        outcome.SceneName = scene.IsValid() ? scene.name : sceneName;
        CountAgainst(before, sceneName, outcome);
        RefreshDiagnostics(outcome);
        outcome.Message = $"已完整重建场景 '{outcome.SceneName}'（{Explain(revision)}）：新建 {outcome.Created} / 修改 {outcome.Updated} / 删除 {outcome.Deleted}。";
        outcome.Log.Add(outcome.Message);
        return outcome.Message;
    }

    /// <summary>Writes the revision's world file into the project so the importer can read it as an asset.</summary>
    private static string WriteWorldAsset(string json)
    {
        var folder = Path.Combine(Application.dataPath, "MapForgeSync");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sync-world.json"), json);
        AssetDatabase.Refresh();
        return WorldAssetPath;
    }

    /// <summary>
    /// Reports a full rebuild as a change set by comparing the object IDs that were in the
    /// scene before it with the ones the new hierarchy has. A full rebuild does not carry a
    /// per-object change list, and inventing one would make the receipt misleading.
    /// </summary>
    private static void CountAgainst(HashSet<string> before, string sceneName, Outcome outcome)
    {
        var root = MapForgeWorldImporter.FindRoot(MapForgeWorldImporter.OpenTargetScene(sceneName));
        var after = MapForgeWorldImporter.IndexObjects(root);
        if (before == null)
        {
            outcome.Created = after.Count;
            outcome.Updated = 0;
            outcome.Deleted = 0;
            return;
        }
        outcome.Created = after.Keys.Count(id => !before.Contains(id));
        outcome.Updated = after.Keys.Count(id => before.Contains(id));
        outcome.Deleted = before.Count(id => !after.ContainsKey(id));
    }

    /// <summary>Object IDs currently under the generated root of a scene, or null when the scene is not there yet.</summary>
    private static HashSet<string> IndexSceneObjects(string sceneName)
    {
        var scene = MapForgeWorldImporter.OpenTargetScene(sceneName);
        var root = MapForgeWorldImporter.FindRoot(scene);
        if (root == null) return null;
        return new HashSet<string>(MapForgeWorldImporter.IndexObjects(root).Keys, StringComparer.Ordinal);
    }

    private static string Explain(Revision revision) =>
        string.IsNullOrWhiteSpace(revision.rebuildReason) ? "MapForge 要求完整重建" : revision.rebuildReason;
    // ---------------------------------------------------------------------- increment

    /// <summary>
    /// Applies a change set to the generated hierarchy and touches nothing else: only the
    /// objects MapForge actually changed are rewritten, and the untouched ones keep their
    /// GameObjects, their components and any tuning the project did on them.
    /// </summary>
    private static string Increment(Revision revision, int appliedRevision, Outcome outcome)
    {
        var sceneName = Sanitize(SceneNameFor(revision));
        var scene = MapForgeWorldImporter.OpenTargetScene(sceneName);
        if (!scene.IsValid())
        {
            outcome.Ok = false;
            return $"找不到场景 'Assets/Scenes/{sceneName}.unity'。请在 MapForge 里用“完整重建”发送一次。";
        }
        if (appliedRevision != revision.baseRevision)
        {
            // Unity is a different number of steps behind than this patch was built on, so
            // applying it would silently drift. Refusing is what makes the incremental
            // path safe; MapForge turns this into a "send a full rebuild" prompt.
            outcome.Ok = false;
            return $"Unity 已应用第 {appliedRevision} 版，但这个增量基于第 {revision.baseRevision} 版。请在 MapForge 里用“完整重建”重新发送。";
        }
        var root = MapForgeWorldImporter.FindRoot(scene);
        if (root == null)
        {
            outcome.Ok = false;
            return $"场景 '{scene.name}' 中没有 {GeneratedRoot} 对象。请在 MapForge 里用“完整重建”发送一次。";
        }

        var lookups = new Lookups(root.transform);
        // A scene imported before the live sync existed has no MapForgeObjectProperties, so
        // nothing can be matched by ID and every patched object would be created a second
        // time. Refusing keeps such a scene intact until a full rebuild re-imports it with
        // the metadata the incremental path needs.
        if (lookups.KnownCount == 0)
        {
            outcome.Ok = false;
            return $"场景 '{scene.name}' 里的对象缺少 MapForge 标识，无法增量更新。请在 MapForge 里用“完整重建”发送一次。";
        }
        // The nodes travel in document order, which is also the hierarchy order, so a
        // parent's GameObject exists before its children are created under it.
        var nodes = revision.objects ?? Array.Empty<MapForgeSceneOrganization.Node>();
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new List<MapForgeSceneOrganization.Node>();

        Delete(revision, lookups, outcome);
        foreach (var node in nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id)) continue;
            bool created = lookups.Find(node.id) == null;
            SyncNode(node, root.transform, sceneName, lookups);
            touched.Add(node.id);
            if (created)
            {
                outcome.Created++;
                outcome.Log.Add("新建：" + node.id);
            }
            else
            {
                outcome.Updated++;
                // A prefab instance, a path link or a spawn point resolves other objects by
                // ID, so its mapping waits until every object of this revision exists.
                deferred.Add(node);
            }
        }
        foreach (var node in deferred)
        {
            if (node.unity == null || node.unity.prefab == null) continue;
            MapForgeSceneOrganization.ApplyUnityMapping(node, lookups.Find(node.id), lookups.Geometry(node.id), revision.unity, lookups.PrefabCache);
        }

        // Path links point at other object IDs, and a target may have been created or
        // deleted in this revision, so the links of every touched node are rebuilt.
        RefreshPathNodeLinks(root.transform, touched, outcome);
        // The trap grids are derived from the platform and ground colliders that this step
        // just rewrote, so they are recomputed rather than left describing the old geometry.
        if (TouchesGeometry(nodes))
        {
            outcome.TrapGrids = MapForgeWorldImporter.RebuildTrapPlacementGrids(root.transform);
            outcome.Log.Add("已按新几何重建陷阱网格：" + outcome.TrapGrids + " 个。");
        }
        // Spawn points and cores share one path grid, which the mapping pass above may
        // have replaced.
        MapForgeSceneOrganization.LinkGameplayReferences(root.transform);
        RefreshDiagnostics(outcome);
        outcome.SceneName = scene.name;
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        // Only a step that reached this point may be recorded as applied; the early returns
        // above leave Ok false so the revision is retried instead of skipped.
        outcome.Ok = true;
        outcome.Message = $"已增量更新场景 '{scene.name}'：新建 {outcome.Created} / 修改 {outcome.Updated} / 删除 {outcome.Deleted}。";
        return outcome.Message;
    }

    /// <summary>Removes the deleted objects, children before their parents so no orphans are left behind.</summary>
    private static void Delete(Revision revision, Lookups lookups, Outcome outcome)
    {
        var deleted = revision.changes?.deleted ?? Array.Empty<string>();
        foreach (var id in deleted.OrderByDescending(lookups.Depth))
        {
            var go = lookups.Find(id);
            if (go == null) { outcome.Log.Add("删除跳过（已不存在）：" + id); continue; }
            lookups.Remove(id);
            UnityEngine.Object.DestroyImmediate(go);
            outcome.Deleted++;
            outcome.Log.Add("删除：" + id);
        }
    }

    /// <summary>
    /// Creates or rewrites one object, reusing the GameObject and the geometry child that are
    /// already in the scene. Rebuilding them instead would break every reference the project
    /// holds to those objects and would make the object's position in the hierarchy depend on
    /// this step rather than on the authoring order.
    /// </summary>
    private static void SyncNode(MapForgeSceneOrganization.Node node, Transform root, string sceneName, Lookups lookups)
    {
        var go = lookups.Find(node.id);
        if (go == null)
        {
            go = MapForgeSceneOrganization.CreateHierarchyNode(node, root, sceneName, out var created);
            lookups.Add(node.id, go, created);
        }
        else
        {
            // Everything a previous mapping generated is dropped first, so a mapping that
            // no longer applies cannot leave its component behind.
            MapForgeSceneOrganization.ClearGeneratedContent(go, lookups.Geometry(node.id), node);
            MapForgeSceneOrganization.ApplyMetadata(node, go.GetComponent<MapForgeObjectProperties>());
            MapForgeSceneOrganization.EnsureGeometryChild(node, go, lookups.Geometry(node.id), sceneName, out var geometry);
            lookups.Add(node.id, go, geometry);
        }
        // The revisions carry authored local transforms, so the object is re-parented to the
        // parent this revision names before the local transform is written.
        var parent = string.IsNullOrEmpty(node.parentId) ? root : lookups.Find(node.parentId)?.transform;
        if (parent != null && go.transform.parent != parent) go.transform.SetParent(parent, false);
        MapForgeSceneOrganization.ApplyNodeTransform(node, go, lookups.Geometry(node.id), lookups.Nodes);
        lookups.Add(node.id, go, lookups.Geometry(node.id));
        // A prefab instance resolves assets through the library and may reference another
        // object by ID, so only the mappings without a prefab run inline.
        if (node.unity != null && node.unity.prefab == null)
            MapForgeSceneOrganization.ApplyUnityMapping(node, go, lookups.Geometry(node.id), null, lookups.PrefabCache);
    }

    /// <summary>
    /// True when the change set rewrote geometry, which is what makes the derived trap
    /// placement grids stale. Folders and groups carry no collider, so a revision that only
    /// moved them around leaves the grids valid.
    /// </summary>
    private static bool TouchesGeometry(MapForgeSceneOrganization.Node[] nodes) =>
        nodes.Any(node => node != null && node.type != "folder" && node.type != "group");
    // --------------------------------------------------------------------- path links

    /// <summary>
    /// Rebuilds the MapForgePathNode.links of every touched object. Links name object IDs,
    /// and this revision may have created, deleted or renamed the target, so a link whose
    /// target no longer exists is reported and dropped instead of failing at run time.
    /// </summary>
    private static void RefreshPathNodeLinks(Transform root, HashSet<string> touched, Outcome outcome)
    {
        if (touched.Count == 0) return;
        var byId = new HashSet<string>(MapForgeWorldImporter.IndexObjects(root.gameObject).Keys, StringComparer.Ordinal);
        foreach (var pathNode in root.GetComponentsInChildren<MapForgePathNode>(true))
        {
            var metadata = pathNode.GetComponent<MapForgeObjectProperties>();
            if (metadata == null || !touched.Contains(metadata.objectId)) continue;
            var links = new List<string>();
            foreach (var link in pathNode.links ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(link)) continue;
                if (byId.Contains(link)) links.Add(link);
                else outcome.Log.Add("已移除失效路径连线：" + metadata.objectId + " -> " + link);
            }
            if (links.Count == (pathNode.links?.Length ?? 0)) continue;
            pathNode.links = links.ToArray();
            EditorUtility.SetDirty(pathNode);
        }
    }

    // -------------------------------------------------------------------- diagnostics

    /// <summary>
    /// Counts the generated components after a sync, so the receipt reports what the scene
    /// actually contains rather than only what the revision asked for.
    /// </summary>
    private static void RefreshDiagnostics(Outcome outcome)
    {
        outcome.PrefabInstances = 0; outcome.Colliders = 0; outcome.NavMesh = 0; outcome.SpawnPoints = 0;
        outcome.Cores = 0; outcome.PathNodes = 0; outcome.Triggers = 0;
        var root = GameObject.Find(GeneratedRoot);
        if (root == null) return;
        foreach (var metadata in root.GetComponentsInChildren<MapForgeObjectProperties>(true))
        {
            if (metadata == null) continue;
            if (!string.IsNullOrEmpty(metadata.prefabId)) outcome.PrefabInstances++;
            if (metadata.colliderType != "auto" || metadata.colliderIsTrigger) outcome.Colliders++;
            if (metadata.navRole != "none") outcome.NavMesh++;
            if (metadata.spawnPointEnabled) outcome.SpawnPoints++;
            if (metadata.enemyCoreEnabled) outcome.Cores++;
            if (metadata.pathNodeEnabled) outcome.PathNodes++;
            if (metadata.triggerEnabled) outcome.Triggers++;
        }
        // A spawn point that lost its path grid would not spawn anything, and that is worth
        // surfacing while the author is still looking at the map instead of at play time.
        var orphans = root.GetComponentsInChildren<EnemySpawnPoint>(true)
            .Count(spawn => spawn.GetComponent<MonsterPathGrid>() == null && root.GetComponentInChildren<MonsterPathGrid>(true) == null);
        if (orphans > 0) outcome.Log.Add($"{orphans} 个出怪口没有怪物路径网格引用。");
    }

    // ---------------------------------------------------------------------- reporting

    /// <summary>
    /// Records the applied revision. The marker file is written first and the pending file is
    /// deleted last, so an interrupted step is retried instead of being reported as applied.
    /// </summary>
    private static void MarkApplied(Revision revision)
    {
        try
        {
            Directory.CreateDirectory(MailRoot);
            File.WriteAllText(AppliedPath, revision.revision.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(AppliedJsonPath, new Json
            {
                ["revision"] = revision.revision,
                ["map"] = revision.map ?? string.Empty,
                ["mode"] = revision.mode ?? "increment",
                ["appliedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            }.ToIndentedString());
        }
        catch (Exception exception) { Debug.LogWarning("MapForge sync: could not record the applied revision (" + exception.Message + ")."); return; }
        try { if (File.Exists(PendingPath)) File.Delete(PendingPath); }
        catch (Exception exception) { Debug.LogWarning("MapForge sync: could not clear the pending revision (" + exception.Message + ")."); }
    }

    /// <summary>Writes the receipt MapForge polls while it waits for the result.</summary>
    private static void WriteResult(Revision revision, Outcome outcome, int durationMs)
    {
        try
        {
            Directory.CreateDirectory(OutboxRoot);
            File.WriteAllText(ResultPath, new Json
            {
                ["protocol"] = Protocol,
                ["editor"] = "Unity",
                ["ok"] = outcome.Ok,
                ["revision"] = revision.revision,
                ["mode"] = revision.mode ?? "increment",
                ["map"] = revision.map ?? string.Empty,
                ["sceneName"] = string.IsNullOrEmpty(outcome.SceneName) ? EditorSceneManager.GetActiveScene().name : outcome.SceneName,
                ["message"] = outcome.Message ?? string.Empty,
                ["created"] = outcome.Created,
                ["updated"] = outcome.Updated,
                ["deleted"] = outcome.Deleted,
                ["trapGrids"] = outcome.TrapGrids,
                ["appliedRevision"] = AppliedRevision(),
                ["components"] = new Json
                {
                    ["prefabInstances"] = outcome.PrefabInstances,
                    ["colliders"] = outcome.Colliders,
                    ["navMesh"] = outcome.NavMesh,
                    ["spawnPoints"] = outcome.SpawnPoints,
                    ["cores"] = outcome.Cores,
                    ["pathNodes"] = outcome.PathNodes,
                    ["triggers"] = outcome.Triggers,
                },
                ["durationMs"] = durationMs,
                ["appliedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["log"] = outcome.Log.ToArray(),
            }.ToIndentedString());
        }
        catch (Exception exception) { Debug.LogWarning("MapForge sync: could not write the result file (" + exception.Message + ")."); }
        if (outcome.Ok) Debug.Log("MapForge sync: " + outcome.Message);
        else Debug.LogError("MapForge sync: " + outcome.Message);
    }
    // ------------------------------------------------------------------------ helpers

    /// <summary>
    /// Scene this revision belongs to. The map name is the scene name, but a first send may
    /// arrive before the scene exists, in which case the open scene is the one to patch.
    /// </summary>
    private static string SceneNameFor(Revision revision)
    {
        if (!string.IsNullOrWhiteSpace(revision.map)) return revision.map;
        // A revision without a map name targets the scene the editor has open, which is
        // what a first send from an unsaved map looks like.
        var active = EditorSceneManager.GetActiveScene().name;
        return string.IsNullOrWhiteSpace(active) ? "UNTITLED_WORLD" : active;
    }

    private static string SceneAssetPath(string sceneName) => Path.Combine(Application.dataPath, "Scenes", Sanitize(sceneName) + ".unity");

    private static string Sanitize(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return "UNTITLED_WORLD";
        foreach (var invalid in Path.GetInvalidFileNameChars()) sceneName = sceneName.Replace(invalid, '_');
        return sceneName.Trim();
    }

    /// <summary>Reads one string field out of a small JSON file without a parser dependency.</summary>
    private static string ReadString(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"" + key + "\"", StringComparison.Ordinal)) continue;
                int colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                return trimmed.Substring(colon + 1).Trim().Trim(',').Trim('"');
            }
        }
        catch (Exception exception) { Debug.LogWarning("MapForge sync: could not read " + path + " (" + exception.Message + ")."); }
        return string.Empty;
    }

    /// <summary>
    /// ID lookups over the generated hierarchy. The sync keeps this in step with its own
    /// change set, so a node created earlier in the same revision resolves for its children.
    /// </summary>
    private sealed class Lookups
    {
        private readonly GameObject _root;
        private readonly Dictionary<string, GameObject> _objects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _geometry = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public readonly Dictionary<string, MapForgeSceneOrganization.Node> Nodes = new Dictionary<string, MapForgeSceneOrganization.Node>(StringComparer.Ordinal);
        public readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public Lookups(Transform root)
        {
            _root = root.gameObject;
            foreach (var metadata in root.GetComponentsInChildren<MapForgeObjectProperties>(true))
            {
                if (metadata == null || string.IsNullOrEmpty(metadata.objectId)) continue;
                Add(metadata.objectId, metadata.gameObject, FindGeometry(metadata));
            }
        }

        /// <summary>Objects the scene currently holds metadata for; zero means nothing can be patched by ID.</summary>
        public int KnownCount => _objects.Count;

        public GameObject Find(string id) => !string.IsNullOrEmpty(id) && _objects.TryGetValue(id, out var go) ? go : null;
        public GameObject Geometry(string id) => !string.IsNullOrEmpty(id) && _geometry.TryGetValue(id, out var go) ? go : null;
        public string RootId => _root != null ? _root.name : null;

        public void Add(string id, GameObject go, GameObject geometry)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (go == null) { Remove(id); return; }
            _objects[id] = go;
            if (geometry != null) _geometry[id] = geometry;
            else _geometry.Remove(id);
        }

        public void Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _objects.Remove(id);
            _geometry.Remove(id);
            Nodes.Remove(id);
        }

        /// <summary>Hierarchy depth of an object, or -1 when it is not in the scene.</summary>
        public int Depth(string id)
        {
            var go = Find(id);
            if (go == null) return -1;
            int depth = 0;
            for (var current = go.transform.parent; current != null && current != _root.transform; current = current.parent) depth++;
            return depth;
        }

        private static GameObject FindGeometry(MapForgeObjectProperties metadata)
        {
            // The geometry child is named after the object ID, which is the stable lookup the
            // project's authoring tools use; the fallback keeps a renamed child working.
            var named = metadata.transform.Find(metadata.objectId);
            if (named != null) return named.gameObject;
            foreach (var candidate in metadata.GetComponentsInChildren<MeshFilter>(true))
                if (candidate.transform.parent == metadata.transform) return candidate.gameObject;
            return null;
        }
    }

    /// <summary>
    /// Tiny JSON writer. Unity's JsonUtility cannot serialize the mix of booleans, numbers,
    /// arrays and nested objects the status and receipt files need, and a dictionary cannot
    /// be deserialized by it either, so the handful of fields are written directly.
    /// </summary>
    private sealed class Json
    {
        private readonly List<KeyValuePair<string, string>> _fields = new List<KeyValuePair<string, string>>();
        public object this[string key] { set => _fields.Add(new KeyValuePair<string, string>(key, Encode(value))); }

        public string ToIndentedString()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("{\n");
            for (int i = 0; i < _fields.Count; i++)
            {
                builder.Append("  \"").Append(Escape(_fields[i].Key)).Append("\": ").Append(_fields[i].Value);
                builder.Append(i < _fields.Count - 1 ? ",\n" : "\n");
            }
            return builder.Append("}\n").ToString();
        }

        private static string Encode(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool flag: return flag ? "true" : "false";
                case int number: return number.ToString(CultureInfo.InvariantCulture);
                case long number: return number.ToString(CultureInfo.InvariantCulture);
                case float number: return number.ToString("R", CultureInfo.InvariantCulture);
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                case Json nested: return nested.ToIndentedString();
                case string[] array: return "[" + string.Join(", ", array.Select(item => "\"" + Escape(item) + "\"")) + "]";
                default: return "\"" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture)) + "\"";
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var builder = new System.Text.StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ') builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }
    }
}