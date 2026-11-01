using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ColorPillarPortalAuthoring
{
    private const string PrefabPath = "Assets/Prefabs/ColorPillarPortal.prefab";
    private const string MaterialFolder = "Assets/Models/ColorPillarPortal/Materials";
    private const string ScenePath = "Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity";

    [MenuItem("Tools/行动打击/生成颜色传送门并放入主场景")]
    public static void CreateAssetsAndPlaceInMainScene()
    {
        CreatePrefab();
        AddBluePairToScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("COLOR_PORTAL_PASS prefab=ColorPillarPortal pair=Blue trigger=3s");
    }

    public static void CreateAssetsBatch() => CreatePrefab();

    private static void CreatePrefab()
    {
        Directory.CreateDirectory("Assets/Prefabs");
        Directory.CreateDirectory(MaterialFolder);

        Material beamMaterial = CreateMaterial("M_ColorPillarPortal_Blue", new Color(0.03f, 0.22f, 1f, 0.82f));
        Material baseMaterial = CreateMaterial("M_ColorPillarPortal_Base", new Color(0.04f, 0.06f, 0.1f, 1f));

        var root = new GameObject("ColorPillarPortal");
        try
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Light Pillar";
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = Vector3.up * 2f;
            beam.transform.localScale = new Vector3(0.7f, 2f, 0.7f);
            beam.GetComponent<Renderer>().sharedMaterial = beamMaterial;
            Object.DestroyImmediate(beam.GetComponent<Collider>());

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Portal Base";
            baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localPosition = Vector3.up * 0.08f;
            baseObject.transform.localScale = new Vector3(1.4f, 0.08f, 1.4f);
            baseObject.GetComponent<Renderer>().sharedMaterial = baseMaterial;
            Object.DestroyImmediate(baseObject.GetComponent<Collider>());

            var arrival = new GameObject("Arrival Point").transform;
            arrival.SetParent(root.transform, false);
            arrival.localPosition = Vector3.up * 0.15f;

            var portal = root.AddComponent<ColorPillarPortal>();
            var serialized = new SerializedObject(portal);
            serialized.FindProperty("colorId").stringValue = "Blue";
            serialized.FindProperty("activationSeconds").floatValue = 3f;
            serialized.FindProperty("arrivalPoint").objectReferenceValue = arrival;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * 1.5f;
            trigger.size = new Vector3(2.4f, 3f, 2.4f);

            var lightObject = new GameObject("Blue Glow");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = Vector3.up * 2f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.12f, 0.3f, 1f);
            light.range = 7f;
            light.intensity = 5f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Material CreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color * 4f);
        material.EnableKeyword("_EMISSION");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AddBluePairToScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new System.InvalidOperationException("Color portal prefab was not generated");

        RemoveExistingPortals();
        GameObject first = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        GameObject second = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        first.name = "Color Portal Blue A";
        second.name = "Color Portal Blue B";
        first.transform.position = new Vector3(-8f, 0f, 0f);
        second.transform.position = new Vector3(8f, 0f, 0f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void RemoveExistingPortals()
    {
        foreach (ColorPillarPortal portal in Object.FindObjectsByType<ColorPillarPortal>(FindObjectsSortMode.None))
            Object.DestroyImmediate(portal.gameObject);
    }
}
