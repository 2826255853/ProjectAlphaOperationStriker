using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public static class RedesignMapWithCeilingArches
{
    private const string AutoBuildSessionKey = "MapForge.CeilingTrapArches.AutoBuildDone";

    static RedesignMapWithCeilingArches()
    {
        if (Application.isBatchMode || SessionState.GetBool(AutoBuildSessionKey, false)) return;
        EditorApplication.delayCall += AutoBuildOnce;
    }

    private static void AutoBuildOnce()
    {
        if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
        SessionState.SetBool(AutoBuildSessionKey, true);
        Build();
    }

    [MenuItem("MapForge/Redesign map with ceiling trap arches")]
    public static void Build()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/THREE_BRANCH_TEST_MAP 3.unity");
        if (!scene.IsValid()) scene = EditorSceneManager.OpenScene("Assets/Scenes/THREE_BRANCH_TEST_MAP 3.unity", OpenSceneMode.Single);
        else SceneManager.SetActiveScene(scene);
        var existing = GameObject.Find("CeilingTrapArches");
        if (existing != null) Object.DestroyImmediate(existing);
        var root = new GameObject("CeilingTrapArches");
        var world = GameObject.Find("MapForgeWorld");
        if (world != null) root.transform.SetParent(world.transform, false);

        var frameMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CeilingTrapFrame_Mat" };
        frameMat.color = new Color(0.12f, 0.16f, 0.2f, 1f);
        frameMat.SetFloat("_Metallic", 0.8f); frameMat.SetFloat("_Smoothness", 0.55f);
        var plateMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CeilingTrapPlate_Mat" };
        plateMat.color = new Color(0.32f, 0.36f, 0.4f, 1f);
        plateMat.SetFloat("_Metallic", 0.65f); plateMat.SetFloat("_Smoothness", 0.45f);
        var stripeMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CeilingTrapStripe_Mat" };
        stripeMat.color = new Color(0.95f, 0.55f, 0.05f, 1f);

        float[] laneX = { -14.5f, -9.5f, -4.5f, 0.5f, 4.5f, 9.5f, 14.5f };
        float[] archZ = { -11.5f, -3.5f, 4.5f, 12.5f };
        int index = 0;
        foreach (float x in laneX)
        foreach (float z in archZ)
        {
            CreateArch(root.transform, x, z, 3.2f, 0.42f, 4.2f, frameMat, plateMat, stripeMat, index++);
        }

        var grid = GameObject.Find("TrapPlacementGrid");
        if (grid != null)
        {
            var so = new SerializedObject(grid.GetComponent<TrapPlacementGrid>());
            so.FindProperty("placementHeight").floatValue = 5.55f;
            so.ApplyModifiedPropertiesWithoutUndo();
            grid.transform.position = new Vector3(-8.5f, 0f, -13.5f);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Ceiling trap arch redesign complete: " + index + " arch frames created.");
    }

    private static void CreateArch(Transform parent, float x, float z, float width, float beam, float sideHeight, Material frame, Material plate, Material stripe, int index)
    {
        var arch = new GameObject("CeilingArch_" + index.ToString("00"));
        arch.transform.SetParent(parent, false);
        arch.transform.position = new Vector3(x, 0f, z);
        MakeCube(arch.transform, "LeftColumn", new Vector3(-width * .5f, sideHeight * .5f, 0f), new Vector3(beam, sideHeight, beam), frame);
        MakeCube(arch.transform, "RightColumn", new Vector3(width * .5f, sideHeight * .5f, 0f), new Vector3(beam, sideHeight, beam), frame);
        float radius = width * .5f;
        float centerY = sideHeight;
        int segments = 10;
        Vector3 prev = new Vector3(-radius, centerY, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = Mathf.PI - Mathf.PI * i / segments;
            Vector3 next = new Vector3(Mathf.Cos(a) * radius, centerY + Mathf.Sin(a) * radius, 0f);
            MakeBeam(arch.transform, "ArchRib_" + i.ToString("00"), prev, next, beam, frame);
            prev = next;
        }
        MakeCube(arch.transform, "TrapMountPlate", new Vector3(0f, centerY + radius + 0.18f, 0f), new Vector3(width - 0.25f, 0.24f, 1.45f), plate);
        MakeCube(arch.transform, "HazardStripe", new Vector3(0f, centerY + radius + 0.315f, 0f), new Vector3(width - 0.45f, 0.035f, 0.95f), stripe);
        var marker = new GameObject("CeilingTrapMountPoint");
        marker.transform.SetParent(arch.transform, false);
        marker.transform.localPosition = new Vector3(0f, centerY + radius + 0.45f, 0f);
        marker.AddComponent<CeilingTrapMountPoint>();
    }

    private static GameObject MakeCube(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = mat; return go;
    }

    private static void MakeBeam(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = MakeCube(parent, name, (a + b) * .5f, new Vector3(thickness, thickness, thickness), mat);
        var d = b - a; go.transform.localScale = new Vector3(thickness, d.magnitude, thickness); go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
    }
}

public sealed class CeilingTrapMountPoint : MonoBehaviour
{
    public string MountType = "CeilingTrap";
}
