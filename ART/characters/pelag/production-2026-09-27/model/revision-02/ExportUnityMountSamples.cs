using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
public static class ExportUnityMountSamples {
 static float[] M(Matrix4x4 m) {return Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();}
 static float[] V(Vector3 v) {return new[]{v.x,v.y,v.z};}
 public static string Main() {
  string output="C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model/revision-02/unity_mount_samples.json";
  var current=SceneManager.GetActiveScene(); bool dirty=current.isDirty;
  var preview=EditorSceneManager.NewPreviewScene();
  var rows=new List<object>();
  try {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
   var swordPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/Pelag/FantasySaber/Pelag_FantasySaber.fbx");
   foreach(var entry in new[]{("bind", "",0f),("idle","Pelag_KnifeIdle_Grounded",0f),("cleave_windup","Pelag_AN_Cleave",7f/30f),("cleave_contact","Pelag_AN_Cleave",12f/30f),("roll_mid","Pelag_AN_Roll",11f/30f)}) {
    var go=UnityEngine.Object.Instantiate(prefab); go.hideFlags=HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(go,preview);go.transform.localScale=Vector3.one*1.82f;
    var animator=go.GetComponent<Animator>();if(animator==null)animator=go.AddComponent<Animator>();animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller");animator.applyRootMotion=false;animator.Rebind();animator.Update(0f);
    foreach(var a in go.GetComponentsInChildren<Animator>()) a.enabled=false;
    AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Characters/Pelag_v5/Pelag_KnifeIdle_Grounded.anim").SampleAnimation(go,0f);
    if(entry.Item2!="") AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Characters/Pelag_v5/"+entry.Item2+".anim").SampleAnimation(go,entry.Item3);
    var bones=go.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
    var sword=UnityEngine.Object.Instantiate(swordPrefab); sword.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(sword,preview);
    sword.transform.SetParent(bones["mixamorig:RightHand"],false); sword.transform.localPosition=new Vector3(-.019f,.08f,.019f);sword.transform.localRotation=Quaternion.Euler(4.778f,-.701f,77.315f);sword.transform.localScale=new Vector3(.5722176f,.4797959f,.4378099f);
    var smr=go.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();smr.BakeMesh(baked);
    rows.Add(new {label=entry.Item1,clip=entry.Item2,time=entry.Item3,root=M(go.transform.localToWorldMatrix),bones=bones.ToDictionary(p=>p.Key,p=>M(p.Value.localToWorldMatrix)),body=new{matrix=M(smr.localToWorldMatrix),bindBones=smr.bones.Select((b,i)=>new{name=b.name,matrix=M(smr.localToWorldMatrix*smr.sharedMesh.bindposes[i].inverse)}).ToArray(),vertices=baked.vertices.Select(v=>V(smr.transform.TransformPoint(v))).ToArray()},sword=new{root=M(sword.transform.localToWorldMatrix),meshes=sword.GetComponentsInChildren<MeshFilter>().Select(mf=>new{name=mf.name,matrix=M(mf.transform.localToWorldMatrix),relative=M(sword.transform.worldToLocalMatrix*mf.transform.localToWorldMatrix),vertices=entry.Item1=="bind"?mf.sharedMesh.vertices.Select(v=>V(v)).ToArray():null}).ToArray()}});
    UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(go);
   }
   File.WriteAllText(output,JsonConvert.SerializeObject(new{editorScene=current.path,editorPlaying=EditorApplication.isPlaying,source="Existing imported .anim sampled on isolated PreviewScene copy, no rebuild/no scene save",rows},Formatting.Indented));
  } finally {EditorSceneManager.ClosePreviewScene(preview);}
  return JsonConvert.SerializeObject(new {output,sceneUnchanged=SceneManager.GetActiveScene()==current,dirtyUnchanged=current.isDirty==dirty,previewClosed=!preview.IsValid()});
 }
}
