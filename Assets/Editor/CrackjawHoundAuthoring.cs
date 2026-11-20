using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CrackjawHoundAuthoring
{
    private const string Model = "Assets/Models/CrackjawHound/CrackjawHound_Realistic.fbx";
    private const string Prefab = "Assets/Resources/CrackjawHound.prefab";

    [MenuItem("Tools/Tower Defense/Build Crackjaw Hound")]
    public static void Build()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = clip.takeName.Split('|').Last();
            clip.wrapMode = clip.name == "Run" ? WrapMode.Loop : WrapMode.Once;
            clip.loopTime = clip.name == "Run";
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var root = new GameObject("Crackjaw Hound");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            model.transform.SetParent(root.transform, false);
            var motion = model.GetComponent<Animation>() ?? model.AddComponent<Animation>();
            motion.playAutomatically = false;
            motion.cullingType = AnimationCullingType.AlwaysAnimate;
            var animations = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().ToArray();
            var run = animations.Single(c => c.name == "Run");
            var bite = animations.Single(c => c.name == "Bite");
            if (Mathf.Abs(run.length - 16f / 30f) > .01f || Mathf.Abs(bite.length - 1f) > .01f)
                throw new InvalidOperationException($"Unexpected clip durations {run.length}, {bite.length}");
            motion.AddClip(run, "Run");
            motion.AddClip(bite, "Bite");
            var bones = model.GetComponentsInChildren<Transform>();
            var jaw = bones.Single(t => t.name == "Jaw");
            var hips = bones.Single(t => t.name == "Body");
            var forward = Vector3.ProjectOnPlane(jaw.position - hips.position, Vector3.up);
            model.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.mainTexture)
                throw new InvalidOperationException("The hound requires a skinned mesh and baked coat texture.");
            Quaternion rest = jaw.localRotation;
            bite.SampleAnimation(model, 11f / 30f);
            Quaternion open = jaw.localRotation;
            bite.SampleAnimation(model, 17f / 30f);
            float closure = Quaternion.Angle(open, jaw.localRotation);
            if (closure < 18f) throw new InvalidOperationException($"Bite jaw rotation only {closure} degrees");
            jaw.localRotation = rest;
            run.SampleAnimation(model, 0f);
            var paw = bones.Single(t => t.name == "ForeLPaw");
            Vector3 start = paw.position;
            run.SampleAnimation(model, run.length * .5f);
            if (Vector3.Distance(start, paw.position) < .08f) throw new InvalidOperationException("Run stride missing");
            run.SampleAnimation(model, run.length);
            if (Vector3.Distance(start, paw.position) > .005f) throw new InvalidOperationException("Run cycle discontinuity");
            run.SampleAnimation(model, 0f);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, .34f, -.02f);
            collider.size = new Vector3(.34f, .67f, 1.22f);
            var hound = root.AddComponent<CrackjawHound>();
            var data = new SerializedObject(hound);
            data.FindProperty("motion").objectReferenceValue = motion;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, Prefab, out bool saved);
            if (!saved) throw new InvalidOperationException("Failed to save Crackjaw Hound");
            Debug.Log($"CRACKJAW_ANIMATION_PASS run={run.length:F3}s bite={bite.length:F3}s closure={closure:F1} degrees");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
