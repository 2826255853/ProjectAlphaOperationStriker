using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps objects the project authored under the generated root alive across a rebuild.
///
/// Ownership is name based and stored in EditorPrefs, so it survives domain reloads,
/// Unity restarts and the local scene file, but never reaches version control. An
/// object that is not part of the record written by the last Build belongs to the
/// project and is therefore lifted out before the generated root is destroyed and put
/// back afterwards. When no record exists yet (first rebuild of an older scene) the
/// name collision check below still prevents duplicates.
/// </summary>
public static partial class MapForgeWorldImporter
{

    /// <summary>
    /// Helper objects the importer creates next to the authored hierarchy, i.e. those
    /// that carry no MapForge metadata. Only used until a record exists.
    /// </summary>
    private static readonly string[] GeneratedHelperNames =
    {
        "Wave Manager", "MonsterPathGrid", "Enemy Core", "NavMesh Surface",
        "TrapPlacementGrid_Road", "TrapPlacementGrid_Ground", "TrapPlacementGrid_Platform"
    };

    /// <summary>EditorPrefs key holding the generated direct-child names of one scene.</summary>
    private static string GeneratedNamesKey(string sceneName) =>
        "MapForgeWorldImporter.GeneratedNames:" + Application.dataPath.Replace('\\', '/') + ":" + sceneName;

    /// <summary>
    /// Records the names the reader wrote directly under the generated root. The next
    /// rebuild reads this back, so anything else found there is project-authored.
    /// </summary>
    private static void RecordGeneratedChildren(Transform root, string sceneName)
    {
        if (root == null || string.IsNullOrEmpty(sceneName)) return;
        var names = new StringBuilder();
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null) continue;
            if (names.Length > 0) names.Append('\n');
            names.Append(child.name);
        }
        EditorPrefs.SetString(GeneratedNamesKey(sceneName), names.ToString());
    }

    /// <summary>Direct-child names of the last generated build, or null when never recorded.</summary>
    private static HashSet<string> ReadGeneratedNames(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        var raw = EditorPrefs.GetString(GeneratedNamesKey(sceneName), string.Empty);
        if (string.IsNullOrEmpty(raw)) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in raw.Split('\n'))
            if (!string.IsNullOrEmpty(line)) names.Add(line);
        return names.Count > 0 ? names : null;
    }

    /// <summary>
    /// Lifts every project-authored object out of the generated root and returns them
    /// (outermost first) so a rebuild can put them back. The caller destroys the root
    /// in between. Authored objects nested inside generated ones are lifted too, and a
    /// container that only holds generated objects is descended into rather than moved.
    /// </summary>
    private static List<GameObject> DetachAuthoredChildren(Transform root, World world,
        MapForgeSceneOrganization.Document organized, string sceneName)
    {
        var authored = new List<GameObject>();
        if (root == null) return authored;

        var names = ReadGeneratedNames(sceneName);
        if (names == null)
        {
            // No record yet (first rebuild of a scene imported by an older build): the names
            // this world is about to generate can still be derived from the world file, and
            // that is precise enough to tell "Enemy Spawn Right" from "Enemy Spawn Flying".
            // Anything the map no longer lists is treated as authored and kept for this one
            // rebuild; the record written below removes it on the next one.
            names = ExpectedRootNames(world, organized);
            Debug.LogWarning("MapForge: no record of the generated hierarchy exists for '" + sceneName +
                "', so this rebuild falls back to the names in the world file. Objects the map no " +
                "longer lists are kept once and removed by the next rebuild.");
        }

        var ids = GeneratedObjectIds(organized);
        CollectAuthored(root, names, ids, authored, true);
        // Leftover objects stay in the scene rather than inside a staging parent, so a
        // build that throws half way can never delete what the project authored.
        return authored;
    }

    /// <summary>
    /// Walks the generated hierarchy and lifts unowned objects to the scene root.
    /// </summary>
    private static void CollectAuthored(Transform node, HashSet<string> names, HashSet<string> ids,
        List<GameObject> authored, bool isRoot)
    {
        for (int i = node.childCount - 1; i >= 0; i--)
        {
            var child = node.GetChild(i);
            bool generated = OwnedByGenerator(child, ids) || (isRoot && names.Contains(child.name));
            // A folder that only holds generated objects belongs to the reader too, so it is
            // descended into rather than lifted, and its stray children are handled below.
            if (generated || HasGeneratedChild(child, ids))
            {
                CollectAuthored(child, names, ids, authored, false);
                continue;
            }
            authored.Add(child.gameObject);
            child.SetParent(null, true);
        }
    }
    /// <summary>True when an object carries MapForge metadata the current world still generates.</summary>
    private static bool OwnedByGenerator(Transform node, HashSet<string> ids)
    {
        var metadata = node.GetComponent<MapForgeObjectProperties>();
        return metadata != null && !string.IsNullOrEmpty(metadata.objectId) && ids.Contains(metadata.objectId);
    }

    /// <summary>True when any child of an object is generated, which makes the object a folder.</summary>
    private static bool HasGeneratedChild(Transform node, HashSet<string> ids)
    {
        for (int i = 0; i < node.childCount; i++)
            if (OwnedByGenerator(node.GetChild(i), ids)) return true;
        return false;
    }

    /// <summary>
    /// Names a build of this world writes directly under the generated root: the top-level
    /// authored nodes plus the helper objects the importer adds next to them.
    /// </summary>
    private static HashSet<string> ExpectedRootNames(World world, MapForgeSceneOrganization.Document organized)
    {
        var names = new HashSet<string>(GeneratedHelperNames, StringComparer.Ordinal);
        var ids = GeneratedObjectIds(organized);
        foreach (var node in organized.objects)
        {
            if (node == null || !string.IsNullOrEmpty(node.parentId)) continue;
            names.Add(string.IsNullOrEmpty(node.name) ? node.id : node.name);
        }
        if (world != null && world.gameplay != null && world.gameplay.spawns != null)
        {
            for (int i = 0; i < world.gameplay.spawns.Length; i++)
            {
                var spawn = world.gameplay.spawns[i];
                if (spawn == null) continue;
                names.Add(string.IsNullOrWhiteSpace(spawn.id) ? $"Enemy Spawn {i + 1}" : spawn.id);
            }
        }
        return names;
    }

    /// <summary>Authored IDs the passed world describes, i.e. the objects a rebuild regenerates.</summary>
    private static HashSet<string> GeneratedObjectIds(MapForgeSceneOrganization.Document organized)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in organized.objects)
            if (node != null && !string.IsNullOrEmpty(node.id)) ids.Add(node.id);
        return ids;
    }

    /// <summary>
    /// Puts the staged objects back under the rebuilt root. An object whose name the new
    /// build already uses is dropped instead of duplicated, and reported, because the
    /// map now owns that name.
    /// </summary>
    private static int RestoreAuthoredChildren(List<GameObject> authored, Transform root)
    {
        if (authored == null || authored.Count == 0) return 0;
        if (root == null)
        {
            Debug.LogError("MapForge: the map root is missing, so " + authored.Count +
                " project-authored object(s) were not restored.");
            return 0;
        }
        var existing = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < root.childCount; i++) existing.Add(root.GetChild(i).name);

        int restored = 0;
        foreach (var go in authored)
        {
            if (go == null) continue;
            // A name the new build already uses cannot be parented twice, and deleting the
            // project's object would lose its components. It is kept under a marked name
            // instead, so nothing is lost and the clash is visible in the hierarchy.
            if (existing.Contains(go.name))
            {
                var renamed = UniqueName(go.name + " (MapForge kept)", existing);
                Debug.LogWarning("MapForge: the map now generates '" + go.name + "', so the project-authored " +
                    "object of that name was kept as '" + renamed + "'. Rename or remove one of the two.");
                go.name = renamed;
            }
            // World position is kept: authored objects live in world space and the generated
            // root sits at the origin, so moving between the two parents does not shift them.
            go.transform.SetParent(root, true);
            existing.Add(go.name);
            restored++;
        }
        return restored;
    }

    /// <summary>Name not taken yet, by appending a counter.</summary>
    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            var candidate = name + " " + i;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}