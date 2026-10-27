using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Text;
using Object = UnityEngine.Object;
public static class PlaceGameplayLadders {
 // 注意：主场景的 8 根梯子已于 2026-10-27 全部换成贴墙梯子 WallLadder。
 // 再跑本菜单的 Place 会把旧式 GameplayLadder 重新加回来，造成新旧并存；
 // 要重建贴墙梯子请用 ReplaceGameplayLaddersWithWallLadder.RunBatch。
 [MenuItem("Tools/Map/Place Symmetric Gameplay Ladders")]
 public static void Place(){
  // Always rebuild the grounded visual before placing scene instances.
  GenerateLadderPrefab.Generate();
  var scene=EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity",OpenSceneMode.Single);
  var old=GameObject.Find("GameplayLadders_Symmetric"); if(old) Object.DestroyImmediate(old);
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameplayLadder.prefab");
  if(prefab==null){Debug.LogError("GameplayLadder prefab missing");return;}
  var root=new GameObject("GameplayLadders_Symmetric");
  // Paired access points sit just outside matching west/east platform edges.
  const float edgeClearance=0.5f;
  Vector3[] spots={new Vector3(-6,0,40+edgeClearance),new Vector3(6,0,40+edgeClearance),new Vector3(-6,0,-40-edgeClearance),new Vector3(6,0,-40-edgeClearance),new Vector3(-22,0,-8+edgeClearance),new Vector3(22,0,-8+edgeClearance),new Vector3(-22,0,-40-edgeClearance),new Vector3(22,0,-40-edgeClearance)};
  float[] yawDegrees={180f,180f,0f,0f,180f,180f,0f,0f};
  const float platformHeight=2f;
  for(int i=0;i<spots.Length;i++){
   var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
   go.name=$"GameplayLadder_{(i%2==0?"Left":"Right")}_{i/2+1}";
   go.transform.SetParent(root.transform);
   go.transform.SetPositionAndRotation(spots[i],Quaternion.Euler(0f,yawDegrees[i],0f));
   go.transform.localScale=new Vector3(1f,platformHeight/GenerateLadderPrefab.Height,1f);
  }
  EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); Debug.Log("Placed 8 symmetric gameplay ladders");
 }

 [MenuItem("Tools/Map/Validate Gameplay Ladders And Render Preview")]
 public static void ValidateAndPreview(){
  EditorSceneManager.OpenScene("Assets/Scenes/THREE_ROUTE_MERGE_MAP.unity",OpenSceneMode.Single);
  var root=GameObject.Find("GameplayLadders_Symmetric");
  if(!root) throw new InvalidOperationException("Gameplay ladder group missing");
  var ladders=root.GetComponentsInChildren<Ladder>();
  if(ladders.Length!=8) throw new InvalidOperationException($"Expected 8 ladders, found {ladders.Length}");
  Physics.SyncTransforms();
  var report=new StringBuilder();
  foreach(var ladder in ladders){
   var bounds=GenerateLadderPrefab.VisualBounds(ladder.gameObject);
   if(bounds.size.y<bounds.size.x || bounds.size.y<bounds.size.z)
    throw new InvalidOperationException($"Ladder long axis is not vertical: {ladder.name}");
   var trigger=ladder.GetComponent<BoxCollider>();
   if(Mathf.Abs(bounds.min.y-ladder.Bottom)>0.01f || Mathf.Abs(bounds.max.y-ladder.Top)>0.01f
      || !trigger.isTrigger || Mathf.Abs(trigger.bounds.min.y-ladder.Bottom)>0.01f
      || Mathf.Abs(trigger.bounds.max.y-ladder.Top)>0.01f)
    throw new InvalidOperationException($"Visual/trigger/climbing range mismatch: {ladder.name} {bounds}");
   // Probe from foot level so overhead platforms/ceilings cannot mask the floor.
   if(!Physics.Raycast(ladder.transform.position+Vector3.up*0.25f,Vector3.down,out var hit,5f,~0,QueryTriggerInteraction.Ignore)
      || Mathf.Abs(bounds.min.y-hit.point.y)>0.05f)
    throw new InvalidOperationException($"Ladder foot is not on the floor: {ladder.name}");
   report.AppendLine($"{ladder.name}: mesh=[{bounds.min.y:F4},{bounds.max.y:F4}], climb=[{ladder.Bottom:F4},{ladder.Top:F4}], floor={hit.point.y:F4} ({hit.collider.name})");
  }
  string output=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"陷阱素材文件夹","GameplayLadder","ground-fix-"+DateTime.Now.ToString("yyyyMMdd"));
  Directory.CreateDirectory(output);
  File.WriteAllText(Path.Combine(output,"validation.txt"),report.ToString());
  var cameraObject=new GameObject("Ladder Preview Camera");
  var texture=new Texture2D(1200,900,TextureFormat.RGB24,false);
  var target=new RenderTexture(1200,900,24);
  RenderTexture previous=RenderTexture.active;
  try{
   var camera=cameraObject.AddComponent<Camera>();
   camera.clearFlags=CameraClearFlags.SolidColor;
   camera.backgroundColor=new Color(0.18f,0.22f,0.28f);
   camera.nearClipPlane=0.1f; camera.farClipPlane=200f;
   camera.orthographic=true; camera.orthographicSize=2.7f;
   var foot=ladders[0].transform.position;
   camera.transform.position=foot+new Vector3(4f,3f,6f);
   camera.transform.LookAt(foot+Vector3.up);
   camera.targetTexture=target;
   camera.Render();
   RenderTexture.active=target;
   texture.ReadPixels(new Rect(0,0,1200,900),0,0); texture.Apply();
   File.WriteAllBytes(Path.Combine(output,"grounded-ladder.png"),texture.EncodeToPNG());
  }
  finally{
   RenderTexture.active=previous;
   Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(texture);
   target.Release(); Object.DestroyImmediate(target);
  }
  Debug.Log("LADDER_GROUNDING_PASS: 8/8 visual, trigger, climb and floor checks passed. "+output);
 }

 public static void RepairAndPreviewBatch(){
  Place();
  ValidateAndPreview();
 }
}

