using UnityEditor;
using UnityEngine;
using System.IO;

 [InitializeOnLoad]
public static class CreateTestMonsterPrefab
{
    static CreateTestMonsterPrefab()
    {
        EditorApplication.delayCall += CreateIfMissing;
    }

    [MenuItem("Tools/Test Monster/Create Prefab")]
    public static void Create()
    {
        const string modelPath = "Assets/Models/TestMonster.fbx";
        const string folder = "Assets/Prefabs";
        const string prefabPath = folder + "/TestMonster.prefab";

        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) throw new System.Exception("Could not load model at " + modelPath);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "TestMonster";
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        if (prefab == null) throw new System.Exception("Prefab creation failed");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created prefab: " + prefabPath);
    }

    private static void CreateIfMissing()
    {
        const string prefabPath = "Assets/Prefabs/TestMonster.prefab";
        if (!File.Exists(prefabPath)) Create();
    }
}
