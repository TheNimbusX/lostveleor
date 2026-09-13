using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game.View;
using Object=UnityEngine.Object;

public static class CampMagicAuthoring
{
    const string MeshLibrary="Assets/Resources/Environment/Camp/Magic/Layout.asset";
    static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));

    public static void EnsureLayout(CampMagicCircle view)
    {
        if(view.DecorationRoot!=null)return;
        Undo.RecordObject(view,"Сохранённое оформление школы");EnsureAnchors(view);
        WithSurfaces(view,view.BuildDecoration);Undo.RegisterCreatedObjectUndo(view.DecorationRoot.gameObject,"Оформление школы");
        Persist(view);PreviewParticles(view);Finish(view);
    }
    static void EnsureAnchors(CampMagicCircle view)
    {
        var root=view.transform.Find("Точки дорожек");
        if(root==null)
        {
            var go=new GameObject("Точки дорожек");Undo.RegisterCreatedObjectUndo(go,"Точки дорожек");go.transform.SetParent(view.transform,false);root=go.transform;
            go.AddComponent<CampMagicDecoration>();
        }
        if(view.PathStarts==null || view.PathStarts.Length!=4)view.PathStarts=new Transform[4];
        if(view.PathEnds==null || view.PathEnds.Length!=4)view.PathEnds=new Transform[4];
        Transform Point(string name,Transform parent,Vector3 at)
        {
            var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Точка дорожки");go.transform.SetParent(parent,false);go.transform.position=at;return go.transform;
        }
        var altars=new[]{view.FireAltar,view.IceAltar,view.AlchemyAltar,view.EarthAltar};string[] names={"Огонь","Лёд","Яд","Земля"};
        for(int i=0;i<4;i++)
        {
            var altar=altars[i];if(altar==null)continue;
            if(view.PathStarts[i]==null)view.PathStarts[i]=Point(names[i]+" — начало у лестницы",altar,altar.TransformPoint(view.PathSocket(altar)));
            if(view.PathEnds[i]==null)
            {
                Vector3 toward=altar.position-view.Centre.position;toward.y=0;
                view.PathEnds[i]=Point(names[i]+" — конец у сердца",root,view.Centre.position+toward.normalized*1.4f);
            }
        }
        if(view.EarthToe==null)view.EarthToe=Point("Земля — выход со ступеней",view.EarthAltar,view.EarthAltar.TransformPoint(new Vector3(view.EarthPathSocket.x,.025f,.39f)));
        if(view.EarthBend==null)view.EarthBend=Point("Земля — направление поворота",view.EarthAltar,view.EarthToe.position+view.EarthAltar.forward*.15f);
    }
    public static void RebuildPaths(CampMagicCircle view)
    {
        string backup=Backup(view);int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Дорожки школы по точкам");
        Undo.RecordObject(view,"Дорожки школы");EnsureAnchors(view);
        if(view.DecorationRoot==null){EnsureLayout(view);return;}
        if(view.PathsRoot!=null)Undo.DestroyObjectImmediate(view.PathsRoot.gameObject);
        WithSurfaces(view,view.BuildPaths);Undo.RegisterCreatedObjectUndo(view.PathsRoot.gameObject,"Обновлённые дорожки");
        Persist(view);CampMagicSetup.PaintPaths(view,backup,false);Finish(view);Undo.CollapseUndoOperations(group);
    }
    public static void RebuildAll(CampMagicCircle view)
    {
        string backup=Backup(view);int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Заново собрать оформление школы");
        Undo.RecordObject(view,"Оформление школы");
        if(view.DecorationRoot!=null)Undo.DestroyObjectImmediate(view.DecorationRoot.gameObject);
        view.DecorationRoot=null;EnsureLayout(view);CampMagicSetup.PaintPaths(view,backup);Finish(view);Undo.CollapseUndoOperations(group);
    }
    static string Backup(CampMagicCircle view)
    {
        if(Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Оформление редактируется вне Play Mode.");
        string folder=Path.Combine(Repo,"artifacts/camp-magic-authoring",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        if(!EditorSceneManager.SaveScene(view.gameObject.scene,Path.Combine(folder,"before.unity"),true))throw new IOException("Не сохранена копия сцены.");
        if(File.Exists(MeshLibrary))File.Copy(MeshLibrary,Path.Combine(folder,"Layout.asset.before"),true);
        return folder;
    }
    public static void RebuildPlaza(CampMagicCircle view)
    {
        Undo.RecordObject(view,"Каменная площадка");
        if(view.PlazaRoot!=null)Undo.DestroyObjectImmediate(view.PlazaRoot.gameObject);
        WithSurfaces(view,view.BuildPlazaOnly);Undo.RegisterCreatedObjectUndo(view.PlazaRoot.gameObject,"Каменная площадка");
        Persist(view);Finish(view);
    }
    static void WithSurfaces(CampMagicCircle view,Action build)
    {
        // В игре эти коллизии создаёт навигация. Для укладки в Edit Mode они нужны лишь на время измерения.
        var temporary=new List<MeshCollider>();
        try
        {
            foreach(var filter in view.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh==null || filter.GetComponent<Collider>()!=null || filter.GetComponentInParent<CampMagicDecoration>()!=null)continue;
                var collider=filter.gameObject.AddComponent<MeshCollider>();collider.hideFlags=HideFlags.HideAndDontSave;collider.sharedMesh=filter.sharedMesh;temporary.Add(collider);
            }
            Physics.SyncTransforms();build();
        }
        finally {foreach(var collider in temporary)if(collider!=null)Object.DestroyImmediate(collider);}
    }
    static void Persist(CampMagicCircle view)
    {
        if(!File.Exists(MeshLibrary))AssetDatabase.CreateAsset(new Mesh{name="Библиотека оформления школы"},MeshLibrary);
        var existing=new Dictionary<string,Object>();
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(MeshLibrary))existing[asset.name]=asset;
        var replacements=new Dictionary<Object,Object>();var temporary=new List<Object>();
        Object Save(Object source,string key)
        {
            if(source==null || EditorUtility.IsPersistent(source))return source;
            if(replacements.TryGetValue(source,out var reused))return reused;
            source.name=key;
            if(existing.TryGetValue(key,out var saved) && saved.GetType()==source.GetType())
            {
                Undo.RegisterCompleteObjectUndo(saved,"Геометрия школы");EditorUtility.CopySerialized(source,saved);EditorUtility.SetDirty(saved);temporary.Add(source);
            }
            else {saved=source;AssetDatabase.AddObjectToAsset(saved,MeshLibrary);existing[key]=saved;}
            replacements[source]=saved;return saved;
        }
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(var filter in view.DecorationRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                string key="Mesh_"+AnimationUtility.CalculateTransformPath(filter.transform,view.DecorationRoot);
                filter.sharedMesh=(Mesh)Save(filter.sharedMesh,key);EditorUtility.SetDirty(filter);
                if(filter.GetComponent<CampMagicStone>()!=null)GameObjectUtility.SetStaticEditorFlags(filter.gameObject,StaticEditorFlags.BatchingStatic);
            }
            foreach(var renderer in view.DecorationRoot.GetComponentsInChildren<Renderer>(true))
            {
                string path=AnimationUtility.CalculateTransformPath(renderer.transform,view.DecorationRoot);var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i];string key=source!=null && source.name.StartsWith("Камень школы ")?"Material_"+source.name:"Material_"+path+"_"+i;
                    materials[i]=(Material)Save(source,key);
                }
                renderer.sharedMaterials=materials;EditorUtility.SetDirty(renderer);
            }
            if(view.StoneMaterialAssets!=null)
                for(int i=0;i<view.StoneMaterialAssets.Length;i++)
                    if(view.StoneMaterialAssets[i]!=null && replacements.TryGetValue(view.StoneMaterialAssets[i],out var material))view.StoneMaterialAssets[i]=(Material)material;
        }
        finally {AssetDatabase.StopAssetEditing();}
        foreach(var source in temporary)Object.DestroyImmediate(source);
        AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(MeshLibrary);
    }
    static void Finish(CampMagicCircle view)
    {
        EditorUtility.SetDirty(view);PrefabUtility.RecordPrefabInstancePropertyModifications(view);
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);SceneView.RepaintAll();
    }
    public static void PreviewParticles(CampMagicCircle view)
    {
        if(view.DecorationRoot==null)return;
        foreach(var ps in view.DecorationRoot.GetComponentsInChildren<ParticleSystem>())
        {ps.Simulate(1.5f,true,true);ps.Pause();}
        EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
    }
    [MenuItem("Разлом/Лагерь/Школа магии/Прижать выбранные камни к земле")]
    public static void SnapSelected()
    {
        if(Application.isPlaying)return;var stones=new HashSet<CampMagicStone>();
        foreach(var selected in Selection.gameObjects)foreach(var stone in selected.GetComponentsInChildren<CampMagicStone>())stones.Add(stone);
        var prepared=new HashSet<CampMagicCircle>();
        foreach(var stone in stones)
        {
            var circle=stone.GetComponentInParent<CampMagicCircle>();if(circle==null)continue;
            if(prepared.Add(circle))circle.RefreshGround();
            Undo.RecordObject(stone.transform,"Камень по рельефу");Vector3 p=stone.transform.position;p.y=circle.HeightAt(p)+.008f;stone.transform.position=p;
            EditorSceneManager.MarkSceneDirty(stone.gameObject.scene);
        }
        SceneView.RepaintAll();
    }
}

[CustomEditor(typeof(CampMagicCircle))]
public sealed class CampMagicCircleInspector:Editor
{
    public override void OnInspectorGUI()
    {
        var view=(CampMagicCircle)target;
        EditorGUILayout.HelpBox("Камни, кристаллы, свет и эффекты сохранены в «Оформление школы магии». Их можно двигать и настраивать прямо в Scene. Play сохраняет эту расстановку. После перемещения «Точек дорожек» нажми обновление дорожек.",MessageType.Info);
        using(new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if(GUILayout.Button("Выделить сохранённое оформление") && view.DecorationRoot!=null)Selection.activeGameObject=view.DecorationRoot.gameObject;
            if(GUILayout.Button("Обновить дорожки по точкам"))CampMagicAuthoring.RebuildPaths(view);
            if(GUILayout.Button("Показать частицы — контрольный кадр"))CampMagicAuthoring.PreviewParticles(view);
            if(GUILayout.Button("Пересобрать всё — заменит ручную расстановку оформления"))CampMagicAuthoring.RebuildAll(view);
        }
        EditorGUILayout.Space();DrawDefaultInspector();
    }
    void OnSceneGUI()
    {
        var view=(CampMagicCircle)target;var altars=new[]{view.FireAltar,view.IceAltar,view.AlchemyAltar,view.EarthAltar};
        Color[] colors={new Color(1,.4f,.1f),Color.cyan,Color.green,Color.yellow};
        for(int k=0;k<altars.Length;k++)
        {
            if(altars[k]==null || view.Centre==null)continue;Handles.color=colors[k];
            Vector3 previous=view.ConduitPoint(altars[k],0);
            for(int i=1;i<=32;i++){Vector3 next=view.ConduitPoint(altars[k],i/32f);Handles.DrawDottedLine(previous,next,4);previous=next;}
            if(view.PathStarts!=null && view.PathStarts.Length>k && view.PathStarts[k]!=null)Handles.Label(view.PathStarts[k].position,view.PathStarts[k].name);
        }
    }
}

[CustomEditor(typeof(CampMagicStone)),CanEditMultipleObjects]
public sealed class CampMagicStoneInspector:Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Отдельный камень школы. Можно двигать, поворачивать и масштабировать обычным Transform. После изменения рельефа прижми его к земле.",MessageType.Info);
        if(GUILayout.Button("Прижать выбранные камни к земле"))CampMagicAuthoring.SnapSelected();
    }
}
