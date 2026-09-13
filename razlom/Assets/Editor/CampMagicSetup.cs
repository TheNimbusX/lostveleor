using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Game.View;
using Object = UnityEngine.Object;

// Работаем с текущей авторской сценой: её несохранённая расстановка важнее версии на диске.
public static class CampMagicSetup
{
    [Serializable] sealed class MeshInspection { public Vector3[] vertices; public Vector2[] uv; public int[] triangles; public string texture; }
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static string Request => Path.Combine(Repo,"artifacts/request-camp-magic");
    [InitializeOnLoadMethod]
    static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (Application.isBatchMode || Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        try
        {
            string action = File.ReadAllText(Request).Trim(); File.Delete(Request);
            if(action=="install")Install();else Inspect();
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Repo,"artifacts/camp-magic-error.txt"),e.ToString()); }
    }
    public static void BuildCapture()
    {
        if(!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/artifacts/"))throw new InvalidOperationException("Нужен изолированный проект.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");Install();
        // Повторное открытие ловит временные меши и материалы, которые не пережили сериализацию сцены.
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var view=Object.FindAnyObjectByType<CampMagicCircle>();
        if(view==null || view.DecorationRoot==null || view.DecorationRoot.GetComponentsInChildren<CampMagicStone>().Length<20)throw new InvalidOperationException("Не сохранено оформление школы.");
        foreach(var filter in view.DecorationRoot.GetComponentsInChildren<MeshFilter>())
            if(filter.sharedMesh==null || !EditorUtility.IsPersistent(filter.sharedMesh))throw new InvalidOperationException("Временный или потерянный меш: "+filter.name);
        foreach(var renderer in view.DecorationRoot.GetComponentsInChildren<Renderer>())
            foreach(var material in renderer.sharedMaterials)if(material==null || !EditorUtility.IsPersistent(material))throw new InvalidOperationException("Временный или потерянный материал: "+renderer.name);
        Debug.Log($"[camp-magic-authoring] scene reopened, persistent stones={view.DecorationRoot.GetComponentsInChildren<CampMagicStone>().Length}, colliders={view.DecorationRoot.GetComponentsInChildren<Collider>().Length}");
        Game.EditorTools.RazlomCaptureBuild.Build();
    }
    const string AssetsFolder="Assets/Resources/Environment/Camp/Magic";
    [MenuItem("Разлом/Лагерь/Оживить круг магии")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Выйди из Play Mode.");
        var world=Object.FindAnyObjectByType<SceneWorldView>();
        var magic=world!=null && world.CampRoot!=null?world.CampRoot.transform.Find("Magic Circle"):null;
        if(magic==null)throw new InvalidOperationException("Нет Magic Circle.");
        Transform Find(string name)
        {
            foreach(var t in magic.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith(name,StringComparison.Ordinal))return t;
            throw new InvalidOperationException("Не найден "+name);
        }
        var fire=Find("fantasy+temple+altar" );var ice=Find("ice+crystal+altar");
        var alchemy=Find("alchemy+cauldron");var earth=Find("stone+altar");var centre=Find("rock+platform");
        string output=Path.Combine(Repo,"artifacts/camp-magic-live",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        if(!EditorSceneManager.SaveScene(magic.gameObject.scene,Path.Combine(output,"before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity"),true))throw new IOException("Не удалось сохранить копию сцены.");
        Directory.CreateDirectory(AssetsFolder);AssetDatabase.Refresh();
        var shader=Shader.Find("Game/Camp Magic Lit");if(shader==null)throw new InvalidOperationException("Нет Camp Magic Lit.");
        int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Живой круг четырёх стихий");
        var view=magic.GetComponent<CampMagicCircle>();if(view==null)view=Undo.AddComponent<CampMagicCircle>(magic.gameObject);
        Undo.RecordObject(view,"Настройки круга магии");
        view.FireAltar=fire;view.IceAltar=ice;view.AlchemyAltar=alchemy;view.EarthAltar=earth;view.Centre=centre;
        view.FlameMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Environment/Camp/FlamePro/M_FlamePro.mat");
        view.PavingMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Environment/Camp/ground/Study/Trail stones.mat");
        view.PavingTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Environment/Camp/ground/CampTrailStones_v2.png");
        view.FireSocket=new Vector3(0,.41f,-.045f);view.AlchemySocket=new Vector3(0,.53f,.065f);
        view.AlchemyPathSocket=new Vector3(.235f,.025f,.147f);view.EarthPathSocket=new Vector3(.035f,.025f,.267f);
        Transform[] models={fire,ice,alchemy,earth,centre};
        for(int i=0;i<models.Length;i++)PrepareModel(models[i],i,shader);
        if(view.Heart==null)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Environment/Camp/enchanted+crystal+pedestal+3d+model.fbx");
            if(source==null)throw new InvalidOperationException("Нет модели кристалла.");
            var filter=source.GetComponentInChildren<MeshFilter>();var renderer=source.GetComponentInChildren<MeshRenderer>();
            if(filter==null || renderer==null)throw new InvalidOperationException("В модели кристалла нет меша.");
            var mesh=BakeCrystal(filter.sharedMesh);
            var heart=new GameObject("Heart Crystal");Undo.RegisterCreatedObjectUndo(heart,"Сердце круга");heart.transform.SetParent(magic,false);
            heart.transform.position=centre.position+Vector3.up*.69f;
            float scale=1.7f/mesh.bounds.size.y;Vector3 parentScale=magic.lossyScale;
            heart.transform.localScale=new Vector3(scale/parentScale.x,scale/parentScale.y,scale/parentScale.z);
            heart.AddComponent<MeshFilter>().sharedMesh=mesh;var heartRenderer=heart.AddComponent<MeshRenderer>();
            heartRenderer.sharedMaterial=PrepareMaterial(renderer.sharedMaterial,"Heart",5,shader);
            heart.AddComponent<CampMagicDecoration>();view.Heart=heart.transform;
        }
        Undo.RecordObject(view.Heart,"Выразительный силуэт сердца");
        var heartMesh=view.Heart.GetComponent<MeshFilter>().sharedMesh;
        float heartScale=2.05f/heartMesh.bounds.size.y;var parent=view.Heart.parent.lossyScale;
        view.Heart.localScale=new Vector3(heartScale/parent.x,heartScale/parent.y,heartScale/parent.z);
        if(view.DecorationRoot==null)PaintPaths(view,output);
        CampMagicAuthoring.EnsureLayout(view);
        EditorUtility.SetDirty(view);PrefabUtility.RecordPrefabInstancePropertyModifications(view);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(magic.gameObject.scene);EditorSceneManager.SaveScene(magic.gameObject.scene);Undo.CollapseUndoOperations(group);
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-magic-installed.txt"),$"{DateTime.Now:O}: four altars, heart={view.Heart.position:F3}, source transforms preserved");
        if(!Application.isBatchMode){Selection.activeGameObject=view.gameObject;SceneView.lastActiveSceneView?.FrameSelected();}
    }
    static Material PrepareMaterial(Material source,string name,int kind,Shader shader)
    {
        string path=AssetsFolder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
        material.shader=shader;material.SetFloat("_MagicKind",kind);material.SetFloat("_MagicStrength",1);material.SetFloat("_MagicPhase",kind*1.73f);
        material.SetFloat("_Smoothness",.22f);material.SetShaderPassEnabled("MotionVectors",true);EditorUtility.SetDirty(material);return material;
    }
    static void PrepareModel(Transform model,int kind,Shader shader)
    {
        foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null || renderer.sharedMaterial==null || filter.sharedMesh==null)continue;
            string key=new[]{"Fire","Ice","Alchemy","Earth","Centre"}[kind];
            Material source=renderer.sharedMaterial;var material=PrepareMaterial(source,key,kind,shader);
            string meshPath=AssetsFolder+"/"+key+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(mesh==null || mesh.name!=key+" — anchored cloth v2")
            {
                var saved=mesh;
                mesh=Object.Instantiate(filter.sharedMesh);mesh.name=key+" — anchored cloth v2";
                var bends=new List<Vector4>(mesh.vertexCount);var vertices=mesh.vertices;var uv=mesh.uv;
                Texture2D texture=source.mainTexture as Texture2D;Texture2D pixels=texture!=null?Readable(texture):null;
                var flagBounds=new[]{new Bounds(),new Bounds()};bool[] found={false,false};
                if(kind<3 && pixels!=null)
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=vertices[i];if(Mathf.Abs(p.x)<.2f || p.y<.3f)continue;
                    Color c=pixels.GetPixelBilinear(uv[i].x,uv[i].y);
                    bool cloth=kind==0?c.r>c.g*1.5f && c.r>c.b*1.5f:kind==1?c.b>c.r*1.7f && c.b>c.g*1.2f:c.g>c.r*1.2f && c.g>c.b*1.4f;
                    if(!cloth)continue;int side=p.x<0?0:1;
                    if(!found[side]){flagBounds[side]=new Bounds(p,Vector3.one*.001f);found[side]=true;}else flagBounds[side].Encapsulate(p);
                }
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=vertices[i];int side=p.x<0?0:1;var bounds=flagBounds[side];bounds.Expand(.004f);
                    float weight=found[side] && bounds.Contains(p)?Mathf.Clamp01((bounds.max.y-p.y)/Mathf.Max(.01f,bounds.size.y))*.042f:0;
                    bends.Add(new Vector4(p.x,bounds.max.y,p.z,-weight));
                }
                if(pixels!=null)Object.DestroyImmediate(pixels);
                mesh.SetUVs(3,bends);
                if(saved==null)AssetDatabase.CreateAsset(mesh,meshPath);
                else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);mesh=saved;EditorUtility.SetDirty(mesh);}
                Debug.Log($"[camp-magic-bake] {key}: cloth left={found[0]} right={found[1]}");
            }
            Undo.RecordObject(filter,"Ткань алтаря");Undo.RecordObject(renderer,"Свет стихий");filter.sharedMesh=mesh;renderer.sharedMaterial=material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
        }
    }
    public static void PaintPaths(CampMagicCircle circle,string backup,bool includePlaza=true)
    {
        var data=AssetDatabase.LoadAssetAtPath<CampPathPaintData>(CampPathPainter.DataPath);if(data==null)return;
        var study=Object.FindAnyObjectByType<CampGroundStudy>();if(study==null)return;
        Vector4 bounds=Vector4.zero;
        foreach(var renderer in study.GetComponentsInChildren<MeshRenderer>())
            if(renderer.sharedMaterial!=null && renderer.sharedMaterial.HasProperty("_SurfaceBounds")) {bounds=renderer.sharedMaterial.GetVector("_SurfaceBounds");if(bounds.z>0)break;}
        if(bounds.z<=0 || bounds.w<=0)throw new InvalidOperationException("Нет координат карты дорожек.");
        foreach(var asset in new Object[]{data,data.Surface,data.ClearedFoliage})
        {
            if(asset==null)continue;AssetDatabase.SaveAssetIfDirty(asset);string path=AssetDatabase.GetAssetPath(asset);
            string copy=Path.Combine(backup,Path.GetFileName(path)+".before-magic");if(!File.Exists(copy))File.Copy(path,copy);
        }
        Undo.RegisterCompleteObjectUndo(data,"Дорожки четырёх стихий");
        // Новая кладка объединяет прежние следы дорожек; чужие мазки вне школы сохраняются.
        if(includePlaza)for(int z=0;z<data.Height;z++)for(int x=0;x<data.Width;x++)
        {
            Vector3 p=new Vector3(bounds.x+x*bounds.z/(data.Width-1),0,bounds.y+z*bounds.w/(data.Height-1));
            float edge=circle.PlazaEdge(p);
            if(edge<-.3f)continue;
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.3f,.6f,edge));int index=z*data.Width+x;
            data.Path[index]=(byte)Mathf.Max(data.Path[index],Mathf.RoundToInt(weight*255));
        }
        foreach(var altar in new[]{circle.FireAltar,circle.IceAltar,circle.AlchemyAltar,circle.EarthAltar})
            for(int i=0;i<16;i++)CampPathPainter.PaintSegment(data,bounds,circle.ConduitPoint(altar,i/16f),circle.ConduitPoint(altar,(i+1)/16f),1.08f,.3f,false);
        CampPathPainter.Apply(data);CampPathPainter.Save(data);
    }
    static Mesh BakeCrystal(Mesh source)
    {
        string path=AssetsFolder+"/Heart.asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved!=null)return saved;
        var p=source.vertices;var normals=source.normals;var uv=source.uv;var tri=source.triangles;
        var ids=new Dictionary<int,int>();var output=new List<Vector3>();var outNormals=new List<Vector3>();var outUv=new List<Vector2>();var triangles=new List<int>();
        for(int i=0;i<tri.Length;i+=3)
        {
            Vector3 mid=(p[tri[i]]+p[tri[i+1]]+p[tri[i+2]])/3;
            if(mid.y<.145f || new Vector2(mid.x,mid.z).magnitude>.115f)continue;
            for(int j=0;j<3;j++)
            {
                int id=tri[i+j];if(!ids.TryGetValue(id,out int index)){index=output.Count;ids.Add(id,index);output.Add(p[id]);outNormals.Add(normals[id]);outUv.Add(uv[id]);}triangles.Add(index);
            }
        }
        if(output.Count<20)throw new InvalidOperationException("Не выделен центральный кристалл.");
        float minY=float.MaxValue;foreach(var v in output)minY=Mathf.Min(minY,v.y);
        for(int i=0;i<output.Count;i++)output[i]-=Vector3.up*minY;
        var mesh=new Mesh{name="Heart crystal — authored pedestal fragment"};mesh.SetVertices(output);mesh.SetNormals(outNormals);mesh.SetUVs(0,outUv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        mesh.SetUVs(3,new List<Vector4>(new Vector4[output.Count]));AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static Texture2D Readable(Texture texture)
    {
        // Читаем исходный JPG на CPU: результат одинаков в редакторе и -nographics сборке.
        string path=AssetDatabase.GetAssetPath(texture);
        var copy=new Texture2D(2,2,TextureFormat.RGBA32,false);
        if(!File.Exists(path) || !ImageConversion.LoadImage(copy,File.ReadAllBytes(path),false))
        {Object.DestroyImmediate(copy);throw new IOException("Не прочитана текстура "+path);}
        return copy;
    }
    [MenuItem("Разлом/Лагерь/Диагностика круга магии")]
    public static void Inspect()
    {
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world == null || world.CampRoot == null) throw new InvalidOperationException("Нет лагеря.");
        var magic = world.CampRoot.transform.Find("Magic Circle");
        if (magic == null) throw new InvalidOperationException("Нет Magic Circle.");
        string output = Path.Combine(Repo,"artifacts/camp-magic-inspect"); Directory.CreateDirectory(output);
        var report = new StringBuilder();
        report.AppendLine($"time={DateTime.Now:O} scene={magic.gameObject.scene.path} dirty={magic.gameObject.scene.isDirty}");
        foreach (Transform child in world.CampRoot.transform) report.AppendLine($"campGroup={child.name} pos={child.position:F3}");
        Bounds bounds = new Bounds(magic.position,Vector3.zero); bool first = true;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            bool belongs=t.IsChildOf(magic);
            if (!belongs && !t.name.Contains("cauldron") && !t.name.Contains("crystal") && !t.name.Contains("altar")) continue;
            string path = AnimationUtility.CalculateTransformPath(t,null);
            report.AppendLine($"node={path} pos={t.position:F4} local={t.localPosition:F4} rot={t.eulerAngles:F2} scale={t.lossyScale:F4} active={t.gameObject.activeInHierarchy}");
            var filter = t.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                report.AppendLine($" mesh={AssetDatabase.GetAssetPath(filter.sharedMesh)} vertices={filter.sharedMesh.vertexCount} submeshes={filter.sharedMesh.subMeshCount} bounds={filter.sharedMesh.bounds}");
                if(t.name.StartsWith("alchemy+cauldron") || t.name.StartsWith("stone+altar") || t.name.StartsWith("fantasy+temple") || t.name.StartsWith("ice+crystal"))
                {
                    var mesh=filter.sharedMesh;var material=t.GetComponent<MeshRenderer>().sharedMaterial;
                    File.WriteAllText(Path.Combine(output,t.name+".json"),JsonUtility.ToJson(new MeshInspection{vertices=mesh.vertices,uv=mesh.uv,triangles=mesh.triangles,texture=AssetDatabase.GetAssetPath(material.mainTexture)}));
                }
            }
            foreach (var renderer in t.GetComponents<Renderer>())
            {
                report.AppendLine($" bounds={renderer.bounds}");
                if (renderer.enabled && renderer.gameObject.activeInHierarchy) { if (first) {bounds=renderer.bounds;first=false;} else bounds.Encapsulate(renderer.bounds); }
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null) report.AppendLine($" material={AssetDatabase.GetAssetPath(mat)} shader={mat.shader.name} texture={AssetDatabase.GetAssetPath(mat.mainTexture)}");
            }
        }
        report.AppendLine($"TOTAL={bounds}");
        if (!EditorSceneManager.SaveScene(magic.gameObject.scene,Path.Combine(output,"authored-before.unity"),true)) throw new IOException("Не сохранена копия текущей сцены.");
        File.WriteAllText(Path.Combine(output,"hierarchy.txt"),report.ToString());
        Render(bounds,Path.Combine(output,"before.png"));
        var crystalAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Environment/Camp/enchanted+crystal+pedestal+3d+model.fbx");
        if(crystalAsset!=null)
        {
            var preview=Object.Instantiate(crystalAsset);preview.hideFlags=HideFlags.HideAndDontSave;
            preview.transform.position=new Vector3(0,100,0);preview.transform.localScale=Vector3.one*5;
            Bounds assetBounds=new Bounds(preview.transform.position,Vector3.zero);bool firstAsset=true;
            foreach(var t in preview.GetComponentsInChildren<Transform>())t.gameObject.layer=30;
            foreach(var r in preview.GetComponentsInChildren<Renderer>()) {if(firstAsset){assetBounds=r.bounds;firstAsset=false;}else assetBounds.Encapsulate(r.bounds);}
            try {Render(assetBounds,Path.Combine(output,"crystal-asset.png"),1<<30);} finally {Object.DestroyImmediate(preview);}
        }
    }
    static void Render(Bounds bounds,string path,int cullingMask=-1)
    {
        var go = new GameObject("Magic circle inspection camera") {hideFlags=HideFlags.HideAndDontSave};
        var camera = go.AddComponent<Camera>(); var source = Camera.main;
        if (source != null) camera.CopyFrom(source);
        camera.enabled=false;camera.orthographic=true;camera.aspect=1.6f;
        camera.cullingMask=cullingMask;camera.orthographicSize=Mathf.Max(cullingMask==-1?4:1,bounds.extents.magnitude*.78f);
        camera.transform.rotation=Quaternion.Euler(48,35,0);
        camera.transform.position=bounds.center-camera.transform.forward*60;
        camera.nearClipPlane=.1f;camera.farClipPlane=180;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var target=RenderTexture.GetTemporary(1600,1000,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
        Texture2D pixels=null;
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();
            File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);if(pixels!=null)Object.DestroyImmediate(pixels);Object.DestroyImmediate(go);}
    }
}
