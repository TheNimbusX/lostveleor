using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class CampAlchemistBankAuthoring
{
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Output => Path.Combine(Repo, "ART/CAMP/alchemist-bank");
    static string Request => Path.Combine(Repo, "artifacts/request-alchemist-bank");
    const string Folder = "Assets/Resources/Environment/Camp/AlchemistBank";
    [InitializeOnLoadMethod] static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string action;
        try { action = File.ReadAllText(Request).Trim(); File.Delete(Request); } catch(IOException) { return; }
        Directory.CreateDirectory(Output);
        try { if (action == "refresh") AssetDatabase.Refresh(); else if(action == "apply") Apply(); else if(action == "restore" && EditorSceneManager.GetActiveScene().path == "" && !EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); else Inspect(); }
        catch (Exception e) { File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); Debug.LogException(e); }
    }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    static readonly Vector3[] Trail = {new Vector3(2.82f,0,-24.7f),new Vector3(2.55f,0,-25.9f),new Vector3(1.3f,0,-26.7f),new Vector3(-.7f,0,-27.05f),new Vector3(-2.6f,0,-26.8f),new Vector3(-4.1f,0,-26.5f)};
    static float TrailDistance(Vector3 p)
    {
        float best = float.MaxValue;
        for(int i=1;i<Trail.Length;i++) { Vector3 a=Trail[i-1],d=Trail[i]-a; p.y=0; best=Mathf.Min(best,Vector3.Distance(p,a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude))); }
        return best;
    }
    static float Footprint(Vector3 p)
    {
        float width=.88f+(Mathf.PerlinNoise(p.x*1.2f+21,p.z*.9f)-.5f)*.25f;
        float path=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(width*.58f,width,TrailDistance(p)));
        var centre=new Vector2(-4.05f,-26.25f); var q=(new Vector2(p.x,p.z)-centre);q.x/=1.8f;q.y/=1.35f;
        return Mathf.Max(path,1-Mathf.SmoothStep(.65f,1,q.magnitude));
    }
    [MenuItem("Разлом/Лагерь/Оформить берег алхимика")]
    public static void Apply()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/SampleScene.unity" || Application.isPlaying)throw new InvalidOperationException("Нужна открытая основная сцена вне Play.");
        var world=Object.FindAnyObjectByType<SceneWorldView>();var camp=world.CampRoot.transform;
        if(camp.Find("Берег алхимика — трава")!=null) { BuildGrass(camp,camp.GetComponentInChildren<CampRiver>()); SaveResult(scene); return; }
        string backup=Path.Combine(Repo,"artifacts/alchemist-bank-before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity");
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Не удалось сохранить копию сцены.");
        Directory.CreateDirectory(Folder);
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Оформить берег алхимика");
        var bridge=GameObject.Find("CreatingBridge").GetComponentInChildren<MeshRenderer>();
        var wood=new Material(bridge.sharedMaterial){name="Camp bridge — warm wood"};
        wood.SetColor("_BaseColor",new Color(1.24f,1.12f,.96f,1));wood.SetFloat("_Smoothness",.08f);wood.SetFloat("_Metallic",0);
        AssetDatabase.CreateAsset(wood,Folder+"/Bridge wood.mat");Undo.RecordObject(bridge,"Цвет моста");bridge.sharedMaterial=wood;PrefabUtility.RecordPrefabInstancePropertyModifications(bridge);
        var river=camp.GetComponentInChildren<CampRiver>();
        var shore=river.Geometry.Find("Дальний берег").GetComponent<MeshRenderer>();
        var surface=new Material(shore.sharedMaterial){name="Alchemist bank surface"};
        var oldMap=surface.GetTexture("_SurfaceMap") as Texture2D;var oldBounds=surface.GetVector("_SurfaceBounds");
        var bounds=new Vector4(Mathf.Min(-16,oldBounds.x),Mathf.Min(-38,oldBounds.y),0,0);
        bounds.z=Mathf.Max(10,oldBounds.x+oldBounds.z)-bounds.x;bounds.w=Mathf.Max(-16,oldBounds.y+oldBounds.w)-bounds.y;
        const int res=1024;var map=new Texture2D(res,res,TextureFormat.RGBA32,false,true){name="Alchemist bank paths",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        var colors=new Color[res*res];
        for(int z=0;z<res;z++)for(int x=0;x<res;x++)
        {
            var p=new Vector3(bounds.x+x*bounds.z/(res-1),0,bounds.y+z*bounds.w/(res-1));
            Color c=oldMap.GetPixelBilinear((p.x-oldBounds.x)/oldBounds.z,(p.z-oldBounds.y)/oldBounds.w);
            float path=Footprint(p); c.r=Mathf.Max(c.r,path);if(path>.01f)c.g=Mathf.Lerp(.30f,.58f,Mathf.PerlinNoise(p.x*.7f,p.z*.7f));
            colors[z*res+x]=c;
        }
        map.SetPixels(colors);map.Apply();AssetDatabase.CreateAsset(map,Folder+"/Bank paths.asset");
        surface.SetTexture("_SurfaceMap",map);surface.SetVector("_SurfaceBounds",bounds);AssetDatabase.CreateAsset(surface,Folder+"/Bank surface.mat");
        Undo.RecordObject(shore,"Дорожки за рекой");shore.sharedMaterial=surface;
        BuildGrass(camp,river); SaveResult(scene); Undo.CollapseUndoOperations(group);
        File.WriteAllText(Path.Combine(Output,"backup.txt"),backup);
    }
    static void SaveResult(UnityEngine.SceneManagement.Scene scene)
    {
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Selection.activeObject=null;SceneView.RepaintAll();Render(new Vector3(-3,0,-24),Quaternion.Euler(48,35,0),8,"after.png");
    }
    static void BuildGrass(Transform camp,CampRiver river)
    {
        var existing=camp.Find("Берег алхимика — трава");
        var root=existing!=null?existing.gameObject:new GameObject("Берег алхимика — трава");
        if(existing==null){Undo.RegisterCreatedObjectUndo(root,"Трава берега");root.transform.SetParent(camp,true);root.AddComponent<CampSceneryDecoration>();}
        var verts=new[]{new List<Vector3>(),new List<Vector3>(),new List<Vector3>()};var bends=new[]{new List<Vector4>(),new List<Vector4>(),new List<Vector4>()};
        var random=new System.Random(20092026);var points=new List<Vector3>();
        var obstacles=Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r=>r.GetComponentInParent<CampRiver>()==null && r.GetComponentInParent<CampGroundStudy>()==null && r.bounds.size.x<5 && r.bounds.size.z<5 && r.bounds.size.y>.45f && r.bounds.size.y<2.8f && r.bounds.max.z< -23).Select(r=>r.bounds).ToArray();
        for(int i=0;i<6500 && points.Count<420;i++)
        {
            var p=new Vector3(-7.7f+(float)random.NextDouble()*13.5f,.064f,-30.6f+(float)random.NextDouble()*6.9f);
            var local=river.transform.InverseTransformPoint(p);
            if(local.z>river.CentreAt(local.x)-river.Width*.5f-river.BankWidth-.5f || Footprint(p)>.08f)continue;
            if(obstacles.Any(b=>p.x>b.min.x-.23f && p.x<b.max.x+.23f && p.z>b.min.z-.23f && p.z<b.max.z+.23f))continue;
            float density=Mathf.PerlinNoise(p.x*.63f+8,p.z*.63f+17);
            if(random.NextDouble()>Mathf.Lerp(.15f,.85f,density) || points.Any(q=>(q-p).sqrMagnitude<.055f))continue;
            points.Add(p);int tone=Mathf.Clamp(Mathf.FloorToInt(density*5)-1,0,2),first=verts[tone].Count;
            float turn=(float)random.NextDouble()*6.28f,scale=.7f+(float)random.NextDouble()*.6f;
            for(int leaf=0;leaf<6;leaf++)CampGroundStudyBuilder.Leaf(verts[tone],p,turn+leaf*1.0472f,.23f*scale,.06f*scale,.13f*scale);
            CampGroundStudyBuilder.AddBend(verts[tone],bends[tone],first,p);
        }
        for(int tone=0;tone<3;tone++)
        {
            string path=Folder+"/Grass "+tone+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh{name="Alchemist meadow "+tone};AssetDatabase.CreateAsset(mesh,path);}else {Undo.RegisterCompleteObjectUndo(mesh,"Трава берега");mesh.Clear();}
            mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(verts[tone]);mesh.SetTriangles(Enumerable.Range(0,verts[tone].Count).ToArray(),0);mesh.SetUVs(3,bends[tone]);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh.normals=mesh.normals.Select(n=>Vector3.Lerp(n,Vector3.up,.72f).normalized).ToArray();EditorUtility.SetDirty(mesh);
            string name="Берег — подлесок "+tone;var child=root.transform.Find(name);var go=child!=null?child.gameObject:new GameObject(name){layer=2};go.transform.SetParent(root.transform,false);go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.identity;go.transform.localScale=new Vector3(1/root.transform.lossyScale.x,1/root.transform.lossyScale.y,1/root.transform.lossyScale.z);
            var filter=go.GetComponent<MeshFilter>()??go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>()??go.AddComponent<MeshRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Environment/Camp/Unified/Camp Study Grass "+tone+".mat");renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        File.WriteAllText(Path.Combine(Output,"applied.txt"),"tufts="+points.Count+" triangles="+verts.Sum(v=>v.Count/3));
    }
    static void Inspect()
    {
        var report = new StringBuilder();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            string path = PathOf(r.transform);
            if (r.bounds.max.x < -10 || r.bounds.min.x > 6 || r.bounds.max.z < -31 || r.bounds.min.z > -18) continue;
            report.AppendLine(path + " active=" + r.gameObject.activeInHierarchy + " pos=" + r.transform.position.ToString("F3") + " bounds=" + r.bounds + " materials=" + string.Join(";", r.sharedMaterials.Select(AssetDatabase.GetAssetPath)));
        }
        var river = Object.FindAnyObjectByType<CampRiver>();
        if(river==null)throw new InvalidOperationException("Откройте основную сцену лагеря.");
        report.AppendLine("RIVER pos=" + river.transform.position.ToString("F3") + " angles=" + river.transform.eulerAngles + " scale=" + river.transform.lossyScale);
        foreach(var p in new[]{new Vector3(2.82f,0,-25),new Vector3(-4.13f,0,-25.47f)}) report.AppendLine("LOCAL " + p + " = " + river.transform.InverseTransformPoint(p));
        var view = SceneView.lastActiveSceneView;
        if (view != null) { report.AppendLine("VIEW " + view.pivot + " " + view.rotation.eulerAngles + " " + view.camera.orthographicSize); Render(new Vector3(-6,0,-22), Quaternion.Euler(48,35,0), 11, "before.png"); }
        File.WriteAllText(Path.Combine(Output, "inspection.txt"), report.ToString());
    }
    static void Render(Vector3 centre, Quaternion rotation, float size, string filename)
    {
        var go = new GameObject("Bank review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); if (Camera.main != null) camera.CopyFrom(Camera.main);
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = size; camera.aspect = 16f / 9;
        camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * 65, rotation);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var rt = RenderTexture.GetTemporary(1600, 900, 24); var previous = RenderTexture.active;
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, filename), texture.EncodeToPNG()); Object.DestroyImmediate(texture); }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(go); }
    }
}
