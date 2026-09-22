using System;
using System.IO;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class CampServicesAuthoring
{
    static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static string Request=>Path.Combine(Repo,"artifacts/request-camp-services");
    [InitializeOnLoadMethod] static void Watch()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(SessionState.GetBool("CampServices.Check",false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            var menu=Object.FindAnyObjectByType<MainMenuView>();
            if(MainMenuView.IsOpen && menu!=null)menu.StartGame();
            var player=CampPlayerView.Instance;
            if(player!=null && CampServicesView.Instance!=null && Object.FindAnyObjectByType<CampServicesProbe>()==null)player.gameObject.AddComponent<CampServicesProbe>();
            if(File.Exists(Path.Combine(Repo,"artifacts/camp-services-result.txt"))){SessionState.SetBool("CampServices.Check",false);EditorApplication.isPlaying=false;}
        }
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request))return;
        string action;try{action=File.ReadAllText(Request).Trim();File.Delete(Request);}catch(IOException){return;}
        try
        {
            if(action=="refresh")AssetDatabase.Refresh();
            else if(action=="traderboss")File.WriteAllText(Path.Combine(Repo,"artifacts/camp-trader-boss-result.txt"),CampServicesProbe.ProbeTraderBoss()?"PASS":"FAIL");
            else if(action=="bridgeheight")
            {
                
                var passage=Object.FindAnyObjectByType<CampRiverPassage>();var b=passage.BridgeBounds;
                var report=new System.Text.StringBuilder();float ground=CampPlayerView.Instance!=null?CampPlayerView.Instance.GroundHeight:.1f;
                report.AppendLine("bridge="+passage.Bridge+" bounds="+b+" size="+passage.CrossingSize);
                for(float z=b.min.z-.6f;z<=b.max.z+.6f;z+=.2f)report.AppendLine(z+" "+passage.SurfaceHeight(b.center.x,z,ground));
                File.WriteAllText(Path.Combine(Repo,"artifacts/camp-bridge-height.txt"),report.ToString());
            }
            else if(action=="tentbuild")File.WriteAllText(Path.Combine(Repo,"artifacts/camp-tent-build.txt"),Game.EditorTools.CampTentBuilder.EnsureBuilt(false)?"OK":"FAIL");
            else if(action=="bridgereport")File.WriteAllText(Path.Combine(Repo,"artifacts/camp-bridge-report.txt"),BridgeReport());
            else if(action=="status")File.WriteAllText(Path.Combine(Repo,"artifacts/camp-services-status.txt"),"play="+EditorApplication.isPlaying+" check="+SessionState.GetBool("CampServices.Check",false)+" player="+CampPlayerView.Instance+" services="+CampServicesView.Instance+" probe="+Object.FindAnyObjectByType<CampServicesProbe>()+" serviceComponents="+Object.FindObjectsByType<CampServicesView>().Length);
            else if(action=="stopcheck"){SessionState.SetBool("CampServices.Check",false);EditorApplication.isPlaying=false;}
            else if(action=="install")Install();
            else if(action=="check")
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Остановите Play перед проверкой.");
                string result=Path.Combine(Repo,"artifacts/camp-services-result.txt");if(File.Exists(result))File.Delete(result);
                SessionState.SetBool("CampServices.Check",true);EditorApplication.isPlaying=true;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Repo,"artifacts/camp-services-error.txt"),e.ToString());Debug.LogException(e);}
    }
    /// <summary>
    /// Что стоит на пути через мост: меши лагеря, которые навигация превратит в препятствие,
    /// а в Play — ещё и готовые коллайдеры и точки настила вне навигации. Без снимков.
    /// </summary>
    static string BridgeReport()
    {
        var report=new System.Text.StringBuilder();
        var passage=Object.FindAnyObjectByType<CampRiverPassage>();
        if(passage==null)return "no CampRiverPassage";
        var b=passage.BridgeBounds;
        var corridor=new Bounds(new Vector3(b.center.x,b.center.y,b.center.z),new Vector3(passage.CrossingSize.x+.6f,6,b.size.z+3f));
        report.AppendLine("play="+EditorApplication.isPlaying+" bridge="+b+" corridor="+corridor);
        var world=Object.FindAnyObjectByType<SceneWorldView>();
        var root=world!=null && world.CampRoot!=null?world.CampRoot.transform:null;
        report.AppendLine("campRoot="+(root!=null?root.name:"none"));
        if(root!=null)
            foreach(var mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                if(!CampPlayerView.UsedByNavigation(mesh))continue;
                var renderer=mesh.GetComponent<Renderer>();if(renderer==null)continue;
                var shape=renderer.bounds;if(!shape.Intersects(corridor) || shape.size.y<.35f)continue;
                report.AppendLine("MESH "+NodePath(mesh.transform)+" mesh="+mesh.sharedMesh.name+" bounds="+shape+" staticBatch="+renderer.isPartOfStaticBatch);
            }
        if(EditorApplication.isPlaying)
        {
            foreach(var collider in Physics.OverlapBox(corridor.center,corridor.extents))
                report.AppendLine("COLLIDER "+NodePath(collider.transform)+" "+collider.GetType().Name+" bounds="+collider.bounds);
            for(float z=b.min.z-1f;z<=b.max.z+1f;z+=.25f)
            {
                var p=new Vector3(b.center.x,b.center.y,z);
                bool on=UnityEngine.AI.NavMesh.SamplePosition(p,out var hit,.2f,UnityEngine.AI.NavMesh.AllAreas);
                report.AppendLine("NAV z="+z.ToString("0.00")+" "+(on?"yes y="+hit.position.y.ToString("0.00"):"NO"));
            }
        }
        return report.ToString();
    }
    static string NodePath(Transform t){var s=t.name;while(t.parent!=null){t=t.parent;s=t.name+"/"+s;}return s;}
    [MenuItem("Разлом/Лагерь/Подключить кузнеца и алхимика")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/SampleScene.unity" || Application.isPlaying)throw new InvalidOperationException("Нужна основная сцена вне Play.");
        string backup=Path.Combine(Repo,"artifacts/camp-services-before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity");EditorSceneManager.SaveScene(scene,backup,true);
        Add("smith-idle",CampServiceKind.Smith);Add("trader@Talking",CampServiceKind.Trader);Add("alchemist@Neutral Idle",CampServiceKind.Alchemist);
        var river=Object.FindAnyObjectByType<CampRiver>();var access=river.GetComponent<CampRiverPassage>()??Undo.AddComponent<CampRiverPassage>(river.gameObject);
        Undo.RecordObject(access,"Переход к алхимику");access.Bridge=GameObject.Find("CreatingBridge").transform;EditorUtility.SetDirty(access);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-services-installed.txt"),"NPC registered; passage enabled; backup="+backup);
    }
    static void Add(string name,CampServiceKind kind)
    {
        var go=GameObject.Find(name);if(go==null)throw new InvalidOperationException("Не найден NPC: "+name);
        var npc=go.GetComponent<CampServiceNpc>()??Undo.AddComponent<CampServiceNpc>(go);Undo.RecordObject(npc,"Взаимодействие с NPC");npc.Kind=kind;EditorUtility.SetDirty(npc);PrefabUtility.RecordPrefabInstancePropertyModifications(npc);
    }
}
