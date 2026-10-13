using System;
using UnityEditor;
using UnityEngine;

/// <summary>Step 4 asset and regeneration checks; also exercises the actual wall placement path.</summary>
public static class FlamethrowerGrayboxValidation
{
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Run in a batch editor outside Play mode.");
        checks = 0;
        TrapDefinition definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(FlamethrowerTrapAuthoring.DefinitionPath);
        Check(definition != null && definition.Prefab != null, "definition references a prefab");
        GameObject prefab = definition.Prefab;
        Check(AssetDatabase.GetAssetPath(prefab) == FlamethrowerTrapAuthoring.PrefabPath, "canonical graybox path");
        Check(prefab.GetComponents<TrapInstance>().Length == 1, "one root TrapInstance");
        Check(prefab.GetComponents<FlamethrowerTrap>().Length == 1, "one root runtime component");
        Check(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "visual-only prefab has no colliders");
        Check(prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0, "visual-only prefab has no rigidbodies");
        Transform root = prefab.transform;
        Transform plate = root.Find("Wall Backplate");
        Check(plate != null && Almost(plate.localScale, new Vector3(1.8f, 1.8f, 0.12f)), "1.8m square backplate");
        Transform nozzle = root.Find("Flame Nozzle");
        Check(nozzle != null && Vector3.Dot(nozzle.up, root.forward) > 0.9999f, "nozzle axis is root +Z");
        Transform arrow = Mount(root, FlamethrowerTrapAuthoring.DirectionName);
        Check(arrow.GetComponentsInChildren<MeshRenderer>().Length == 3, "visible shaft and two arrowhead wings");
        Check(arrow.Find("Shaft").localPosition.z < arrow.Find("Tip Left").localPosition.z
            && arrow.Find("Shaft").localPosition.z < arrow.Find("Tip Right").localPosition.z, "arrowhead points +Z");
        Transform attack = Mount(root, "Attack Origin");
        Check(Almost(attack.localPosition, Vector3.zero), "attack origin matches runtime root coordinates");
        Transform flame = Mount(root, "Flame Origin");
        Vector3 exit = nozzle.localPosition + Vector3.forward * nozzle.localScale.y;
        Check(Almost(flame.localPosition, exit), "flame mount starts at cylinder nozzle exit");
        foreach (string name in new[] { "Preheat Fx", "Spray Fx", "Cooldown Fx" })
        {
            Transform fx = Mount(flame, name);
            Check(Almost(fx.localPosition, Vector3.zero) && fx.childCount == 0
                && fx.GetComponents<Component>().Length == 1, name + " is an empty future effect mount");
        }
        int descendants = root.GetComponentsInChildren<Transform>(true).Length;
        string runtimeSettings = EditorJsonUtility.ToJson(prefab.GetComponent<FlamethrowerTrap>());
        string prefabGuid = AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.PrefabPath);
        string definitionGuid = AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.DefinitionPath);
        int price = definition.Cost;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(nozzle, out string nozzleGuid, out long nozzleId);
        FlamethrowerTrapAuthoring.CreateGraybox();
        definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(FlamethrowerTrapAuthoring.DefinitionPath);
        prefab = definition.Prefab;
        Check(AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.PrefabPath) == prefabGuid, "regeneration preserves prefab GUID");
        Check(AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.DefinitionPath) == definitionGuid, "regeneration preserves definition GUID");
        Check(price == definition.Cost, "regeneration preserves price");
        Check(runtimeSettings == EditorJsonUtility.ToJson(prefab.GetComponent<FlamethrowerTrap>()), "regeneration preserves attack tuning");
        Check(prefab.transform.GetComponentsInChildren<Transform>(true).Length == descendants, "regeneration does not duplicate children");
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform.Find("Flame Nozzle"), out string newGuid, out long newId);
        Check(newGuid == nozzleGuid && newId == nozzleId, "regeneration preserves existing nozzle identity");
        // This regression also checks every mesh corner against the solid install
        // box and places real prefabs on supported, obstructed and rotated walls.
        FlamethrowerPlacementValidation.Run();
        Debug.Log($"FLAMETHROWER_STEP4_PASS: {checks} assertions plus wall placement regression");
    }

    private static Transform Mount(Transform parent, string name)
    {
        Transform mount = parent.Find(name);
        Check(mount != null && mount.localScale == Vector3.one, name + " exists with unit scale");
        Check(Quaternion.Angle(mount.localRotation, Quaternion.identity) < 0.001f
            && Vector3.Dot(mount.forward, parent.forward) > 0.9999f, name + " inherits root +Z");
        return mount;
    }

    private static bool Almost(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Flamethrower step 4: " + message);
        checks++;
    }
}
