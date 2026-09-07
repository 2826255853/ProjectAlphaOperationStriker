using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class CreateRoadsidePlatforms
{
    [MenuItem("Tools/Map/Create Roadside Platforms")]
    public static void Create()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
        var old = GameObject.Find("RoadsidePlatforms");
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject("RoadsidePlatforms");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "RoadsidePlatformMaterial", color = new Color(0.32f, 0.36f, 0.40f) };
        AssetDatabase.CreateAsset(mat, "Assets/RoadsidePlatformMaterial.mat");

        // Each block is 4 m wide across the road and 2 m high. Overlapping end blocks form continuous corners.
        Add(root, mat, "Central_West", new Vector3(-6,1,12), new Vector3(4,2,56));
        Add(root, mat, "Central_East", new Vector3(6,1,12), new Vector3(4,2,56));
        Add(root, mat, "Lower_West", new Vector3(-6,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Lower_East", new Vector3(6,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Left_Outer", new Vector3(-22,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Left_Inner", new Vector3(-14,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Right_Inner", new Vector3(14,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Right_Outer", new Vector3(22,1,-28), new Vector3(4,2,24));
        Add(root, mat, "Turn_Left_South", new Vector3(-8,1,-22), new Vector3(24,2,4));
        Add(root, mat, "Turn_Left_North", new Vector3(-8,1,-10), new Vector3(24,2,4));
        Add(root, mat, "Turn_Right_South", new Vector3(8,1,-22), new Vector3(24,2,4));
        Add(root, mat, "Turn_Right_North", new Vector3(8,1,-10), new Vector3(24,2,4));
        Selection.activeGameObject = root;
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }
    static void Add(GameObject root, Material mat, string name, Vector3 pos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Platform_" + name;
        go.transform.SetParent(root.transform);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.isStatic = true;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
