using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
public static class PlaceGameplayLadders {
 [MenuItem("Tools/Map/Place Symmetric Gameplay Ladders")]
 public static void Place(){
  var scene=EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity",OpenSceneMode.Single);
  var old=GameObject.Find("GameplayLadders_Symmetric"); if(old) Object.DestroyImmediate(old);
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameplayLadder.prefab");
  if(prefab==null){Debug.LogError("GameplayLadder prefab missing");return;}
  var root=new GameObject("GameplayLadders_Symmetric");
  // Paired access points on matching west/east platforms.
  Vector3[] spots={new Vector3(-6,0,40),new Vector3(6,0,40),new Vector3(-6,0,-40),new Vector3(6,0,-40),new Vector3(-22,0,-8),new Vector3(22,0,-8),new Vector3(-22,0,-40),new Vector3(22,0,-40)};
  for(int i=0;i<spots.Length;i++){var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name=$"GameplayLadder_{(i%2==0?"Left":"Right")}_{i/2+1}"; go.transform.SetParent(root.transform); go.transform.SetPositionAndRotation(spots[i],Quaternion.identity);}
  EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Debug.Log("Placed 8 symmetric gameplay ladders");
 }
}

