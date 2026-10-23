using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asset, regeneration and wall-placement checks for the flamethrower prefab.
/// The prefab now carries the modelled visuals, so the structural assertions
/// follow the model hierarchy instead of the retired graybox primitives.
/// </summary>
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
        Check(AssetDatabase.GetAssetPath(prefab) == FlamethrowerTrapAuthoring.PrefabPath, "canonical prefab path");
        Check(prefab.GetComponents<TrapInstance>().Length == 1, "one root TrapInstance");
        Check(prefab.GetComponents<FlamethrowerTrap>().Length == 1, "one root runtime component");
        Check(prefab.GetComponentsInChildren<Collider>(true).Length == 0, "visual-only prefab has no colliders");
        Check(prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0, "visual-only prefab has no rigidbodies");

        Transform root = prefab.transform;
        Transform model = root.Find(FlamethrowerModelAuthoring.ModelRootName);
        Check(model != null, "model container exists");
        Renderer[] parts = model.GetComponentsInChildren<Renderer>(true);
        Check(parts.Length >= 8, "model is built from the authored parts, found " + parts.Length);
        Check(root.Find(FlamethrowerTrapAuthoring.DirectionName) == null,
            "graybox direction marker is gone");

        Bounds bounds = MeasureBounds(model);
        Check(bounds.size.x > 1.6f && bounds.size.y > 1.6f, "model is sized for a 2x2 wall footprint");
        Check(bounds.min.z > 0f, "model sits in front of the wall plane");

        Transform attack = Mount(root, "Attack Origin");
        Check(Almost(attack.localPosition, Vector3.zero), "attack origin matches runtime root coordinates");
        Transform flame = Mount(root, "Flame Origin");
        Check(flame.localPosition.z > bounds.max.z - 0.05f, "flame mount starts at the muzzle plane");

        // Barrels must actually point +Z, or every flame effect fires backwards.
        Vector3 barrelCentre = Vector3.zero;
        int barrels = 0;
        foreach (Renderer part in parts)
        {
            if (part.name.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) < 0) continue;
            barrelCentre += part.bounds.center;
            barrels++;
        }
        Check(barrels > 0, "barrel meshes are present");
        barrelCentre /= Mathf.Max(1, barrels);
        Check(barrelCentre.z > bounds.center.z, "barrels sit in front of the mounting plate");

        float padZ = 0f;
        foreach (Renderer part in parts)
        {
            if (part.name.IndexOf("Slab", StringComparison.OrdinalIgnoreCase) < 0) continue;
            padZ = part.bounds.center.z;
            break;
        }
        Check(padZ < barrelCentre.z, "concrete pad stays behind the barrels");

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
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(model, out string modelGuid, out long modelId);

        FlamethrowerModelAuthoring.CreateModelPrefab();

        definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(FlamethrowerTrapAuthoring.DefinitionPath);
        prefab = definition.Prefab;
        Check(AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.PrefabPath) == prefabGuid, "regeneration preserves prefab GUID");
        Check(AssetDatabase.AssetPathToGUID(FlamethrowerTrapAuthoring.DefinitionPath) == definitionGuid, "regeneration preserves definition GUID");
        Check(price == definition.Cost, "regeneration preserves price");
        Check(runtimeSettings == EditorJsonUtility.ToJson(prefab.GetComponent<FlamethrowerTrap>()), "regeneration preserves attack tuning");
        Check(prefab.transform.GetComponentsInChildren<Transform>(true).Length == descendants, "regeneration does not duplicate children");
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            prefab.transform.Find(FlamethrowerModelAuthoring.ModelRootName), out string newGuid, out long newId);
        Check(newGuid == modelGuid && newId == modelId, "regeneration preserves model container identity");

        // This regression also checks every mesh corner against the solid install
        // box and places real prefabs on supported, obstructed and rotated walls.
        FlamethrowerPlacementValidation.Run();
        Debug.Log($"FLAMETHROWER_STEP4_PASS: {checks} assertions plus wall placement regression");
    }

    private static Bounds MeasureBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (!has)
            {
                bounds = renderer.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
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
