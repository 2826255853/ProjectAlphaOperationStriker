using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GenerateLadderPrefab
{
    public const string PrefabPath = "Assets/Prefabs/GameplayLadder.prefab";
    public const float Height = 3.2f;

    [MenuItem("Tools/Generate Gameplay Ladder Prefab")]
    public static void Generate()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/GameplayLadder.fbx");
        if (model == null) throw new InvalidOperationException("GameplayLadder model not found");

        // The FBX pivot is not its foot. Correct the visual while keeping the
        // gameplay root and climbing trigger anchored to the ground.
        var root = new GameObject("GameplayLadder");
        try
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            Bounds imported = VisualBounds(instance);
            // This FBX arrives with its long axis along Z, pointing away from
            // the foot pivot. Turn that axis upright before fitting its height.
            if (imported.size.z > imported.size.y && imported.size.z > imported.size.x)
            {
                Vector3 upAxis = imported.center.z < instance.transform.position.z ? Vector3.back : Vector3.forward;
                instance.transform.rotation = Quaternion.FromToRotation(upAxis, Vector3.up) * instance.transform.rotation;
            }
            Bounds upright = VisualBounds(instance);
            if (upright.size.y < 0.001f || upright.size.y < upright.size.x || upright.size.y < upright.size.z)
                throw new InvalidOperationException("GameplayLadder model has no vertical extent");
            visual.localScale = new Vector3(1f, Height / upright.size.y, 1f);
            Bounds scaled = VisualBounds(instance);
            visual.position -= Vector3.up * scaled.min.y;

            var ladder = root.AddComponent<Ladder>();
            var data = new SerializedObject(ladder);
            data.FindProperty("height").floatValue = Height;
            data.FindProperty("width").floatValue = 1.35f;
            data.FindProperty("depth").floatValue = 0.5f;
            data.ApplyModifiedPropertiesWithoutUndo();
            var trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * (Height * 0.5f);
            trigger.size = new Vector3(1.35f, Height, 0.5f);

            Directory.CreateDirectory("Assets/Prefabs");
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Could not save GameplayLadder prefab");
            AssetDatabase.SaveAssets();
            Debug.Log($"GameplayLadder grounded: imported={imported}, upright={upright}, corrected Y=[0, {Height}]");
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static Bounds VisualBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("GameplayLadder has no renderers");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}



