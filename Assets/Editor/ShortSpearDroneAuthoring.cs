using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

public static class ShortSpearDroneAuthoring
{
    public const string PrefabPath = "Assets/Resources/ShortSpearDroneEnemy.prefab";
    public const float MoveSpeed = 8f; // TODO(确认): Tune speed and health in live play.
    private const string ModelDirectory = "Assets/Models/ShortSpearDrone";
    private const string ModelPath = ModelDirectory + "/ShortSpear.fbx";
    private const string ControllerPath = ModelDirectory + "/ShortSpear.controller";
    private const string TrailMaterialPath = ModelDirectory + "/ShortSpearTrail.mat";
    private const float TrailLength = 4f;

    [MenuItem("Tools/塔防/生成短矛无人机怪物预制体")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException($"Missing Short Spear model at {ModelPath}.");

        var root = new GameObject("ShortSpearDroneEnemy");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            var health = new SerializedObject(root.AddComponent<EnemyHealth>());
            health.FindProperty("maxHealth").floatValue = 100f;
            health.ApplyModifiedPropertiesWithoutUndo();

            var follower = new SerializedObject(root.AddComponent<MonsterPathFollower>());
            follower.FindProperty("flying").boolValue = true;
            follower.FindProperty("moveSpeed").floatValue = MoveSpeed;
            follower.ApplyModifiedPropertiesWithoutUndo();

            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException("The Short Spear model has no renderers.");

            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            var hitbox = root.AddComponent<BoxCollider>();
            hitbox.center = bounds.center;
            hitbox.size = bounds.size;

            ConfigureAnimation(visual);
            CreateTrail(root.transform, bounds);
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException($"Could not save {PrefabPath}.");
            AssetDatabase.SaveAssets();
            Debug.Log($"SHORT_SPEAR_PREFAB_PASS: {PrefabPath}, model bounds {bounds.size:F2}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureAnimation(GameObject visual)
    {
        // The cinematic Flight clip includes a baked course. The in-place loop drives the rig in game.
        var loop = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Single(clip => clip.name == "Hover_Loop");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var states = controller.layers[0].stateMachine;
            states.defaultState = states.AddState("Flight");
        }
        var flight = controller.layers[0].stateMachine.defaultState;
        flight.motion = loop;
        EditorUtility.SetDirty(flight);

        if (!visual.TryGetComponent<Animator>(out var animator))
            animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
    }

    private static void CreateTrail(Transform drone, Bounds bounds)
    {
        var emitter = new GameObject("Red Flight Trail");
        emitter.transform.SetParent(drone, false);
        emitter.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);

        var trail = emitter.AddComponent<TrailRenderer>();
        trail.sharedMaterial = GetTrailMaterial();
        trail.time = TrailLength / MoveSpeed;
        trail.minVertexDistance = .08f;
        trail.startWidth = .18f;
        trail.endWidth = .025f;
        var red = new Color(1f, .025f, .025f);
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(red, 0f), new GradientColorKey(red, 1f) },
            new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(.45f, .65f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
    }

    private static Material GetTrailMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            throw new InvalidOperationException("Missing URP particle shader for the Short Spear trail.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, TrailMaterialPath);
        }
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }
}
