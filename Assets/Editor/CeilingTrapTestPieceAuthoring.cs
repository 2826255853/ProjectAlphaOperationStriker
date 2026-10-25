using UnityEditor;
using UnityEngine;

public static class CeilingTrapTestPieceAuthoring
{
    public const string Directory = "Assets/Resources/CeilingTrapTest";
    public const string DefinitionPath = Directory + "/CeilingTrapTest.asset";

    [MenuItem("Tools/塔防/生成天花板陷阱测试件")]
    public static void CreateAssets()
    {
        if (!AssetDatabase.IsValidFolder(Directory)) AssetDatabase.CreateFolder("Assets/Resources", "CeilingTrapTest");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Directory + "/CeilingTrapTest.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, Directory + "/CeilingTrapTest.mat");
        }
        material.color = new Color(.2f, .65f, .85f);
        var root = new GameObject("天花板测试件（无攻击）");
        root.AddComponent<TrapInstance>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body"; body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, .15f);
        body.transform.localScale = new Vector3(.8f, .8f, .3f);
        body.GetComponent<Renderer>().sharedMaterial = material;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Directory + "/CeilingTrapTest.prefab");
        Object.DestroyImmediate(root);
        var definition = AssetDatabase.LoadAssetAtPath<TrapDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        var s = new SerializedObject(definition);
        s.FindProperty("trapId").stringValue = "ceiling_test";
        s.FindProperty("displayName").stringValue = "天花板测试件（无攻击）";
        s.FindProperty("prefab").objectReferenceValue = prefab;
        s.FindProperty("cost").intValue = TrapDefinition.DefaultCost;
        s.FindProperty("mountType").enumValueIndex = (int)TrapMountType.Ceiling;
        s.FindProperty("walkableFloorTrap").boolValue = false;
        s.FindProperty("installBoundsCenter").vector3Value = new Vector3(0f, 0f, .15f);
        s.FindProperty("installBoundsSize").vector3Value = new Vector3(.8f, .8f, .3f);
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(material); EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        Debug.Log("CEILING_TRAP_ASSETS_PASS: " + DefinitionPath);
    }
}
