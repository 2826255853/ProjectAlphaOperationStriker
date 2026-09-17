using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class CreateRoadsidePlatforms
{
    [MenuItem("Tools/Map/Create Roadside Platforms")]
    public static void Create()
    {
        // Always target the canonical scene the MapForge importer writes;
        // numbered copies (" 4") are no longer produced and were removed.
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity", OpenSceneMode.Single);
        var old = GameObject.Find("RoadsidePlatforms");
        if (old) Object.DestroyImmediate(old);
        foreach (var name in new[]
        {
            "Platform_Central_West_North", "Platform_Central_East_North",
            "Platform_Central_West_South", "Platform_Central_East_South",
            "Platform_Left_Outer", "Platform_Left_Inner_South",
            "Platform_Right_Inner_South", "Platform_Right_Outer",
            "Platform_Turn_Left_South", "Platform_Turn_Left_North",
            "Platform_Turn_Right_South", "Platform_Turn_Right_North"
        })
        {
            var existing = GameObject.Find(name);
            if (existing) Object.DestroyImmediate(existing);
        }
        var root = new GameObject("RoadsidePlatforms");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "RoadsidePlatformMaterial", color = new Color(0.32f, 0.36f, 0.40f) };
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoadsidePlatformMaterial.mat");
        if (existingMat != null)
        {
            Object.DestroyImmediate(mat);
            mat = existingMat;
        }
        else AssetDatabase.CreateAsset(mat, "Assets/RoadsidePlatformMaterial.mat");

        // Every platform is 4 m wide across its road and 2 m high.  The
        // junction-side segments stop at the road intersections so the
        // centre, left and right enemy lanes remain fully open.
        Add(root, mat, "Central_West_North", new Vector3(-6,1,14), new Vector3(4,2,52));
        Add(root, mat, "Central_East_North", new Vector3(6,1,14), new Vector3(4,2,52));
        Add(root, mat, "Central_West_South", new Vector3(-6,1,-30), new Vector3(4,2,20));
        Add(root, mat, "Central_East_South", new Vector3(6,1,-30), new Vector3(4,2,20));
        Add(root, mat, "Left_Outer", new Vector3(-22,1,-24), new Vector3(4,2,32));
        Add(root, mat, "Left_Inner_South", new Vector3(-10,1,-30), new Vector3(4,2,20));
        Add(root, mat, "Right_Inner_South", new Vector3(10,1,-30), new Vector3(4,2,20));
        Add(root, mat, "Right_Outer", new Vector3(22,1,-24), new Vector3(4,2,32));
        Add(root, mat, "Turn_Left_South", new Vector3(-8,1,-22), new Vector3(8,2,4));
        Add(root, mat, "Turn_Left_North", new Vector3(-12,1,-10), new Vector3(16,2,4));
        Add(root, mat, "Turn_Right_South", new Vector3(8,1,-22), new Vector3(8,2,4));
        Add(root, mat, "Turn_Right_North", new Vector3(12,1,-10), new Vector3(16,2,4));
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
