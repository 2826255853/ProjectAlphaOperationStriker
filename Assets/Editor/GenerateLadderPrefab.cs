using UnityEditor;
using UnityEngine;
using System.IO;
public static class GenerateLadderPrefab {
 [MenuItem("Tools/Generate Gameplay Ladder Prefab")]
 public static void Generate(){
  AssetDatabase.Refresh();
  string modelPath="Assets/Models/GameplayLadder.fbx";
  var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
  if(model==null){ Debug.LogError("GameplayLadder model not found"); return; }
  var inst=(GameObject)PrefabUtility.InstantiatePrefab(model);
  inst.name="GameplayLadder";
  var col=inst.GetComponent<BoxCollider>(); if(col==null) col=inst.AddComponent<BoxCollider>();
  col.center=new Vector3(0,1.6f,0.28f); col.size=new Vector3(1.5f,3.3f,0.5f);
  inst.tag="Untagged"; var ladder=inst.GetComponent<Ladder>(); if(ladder==null) ladder=inst.AddComponent<Ladder>(); var ls=new SerializedObject(ladder); ls.FindProperty("height").floatValue=3.2f; ls.FindProperty("width").floatValue=1.35f; ls.FindProperty("depth").floatValue=0.5f; ls.ApplyModifiedPropertiesWithoutUndo();
  Directory.CreateDirectory("Assets/Prefabs");
  PrefabUtility.SaveAsPrefabAsset(inst,"Assets/Prefabs/GameplayLadder.prefab");
  Object.DestroyImmediate(inst);
  AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
  Debug.Log("GameplayLadder prefab generated");
 }
}



