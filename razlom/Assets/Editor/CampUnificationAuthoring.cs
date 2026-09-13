using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.View;
using Object = UnityEngine.Object;

// Переносит согласованный художественный проход по отдельным объектам, сохраняя авторскую композицию.
public static class CampUnificationAuthoring
{
    const string Main = "Assets/Scenes/SampleScene.unity";
    const string Study = "Assets/Scenes/Studies/CampAtmospheric.unity";
    const string Folder = "Assets/Resources/Environment/Camp/Unified";
    static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Output => Path.Combine(Repo, "ART/CAMP/unified-2026-09-13");
    static string Request => Path.Combine(Repo, "artifacts/request-camp-unification");

    [InitializeOnLoadMethod] static void Watch() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (SessionState.GetBool("CampUnifiedPlay",false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            if (MainMenuView.IsOpen)
            {
                var menu=Object.FindAnyObjectByType<MainMenuView>();
                if(menu!=null) typeof(MainMenuView).GetMethod("StartGame",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(menu,null);
                return;
            }
            if(Time.timeSinceLevelLoad>5)
            {
                SessionState.SetBool("CampUnifiedPlay",false);
                try { if(SessionState.GetBool("CampUnifiedNav",false)){SessionState.SetBool("CampUnifiedNav",false);DumpNavigation();}else CaptureFrames(); }
                finally { EditorApplication.isPlaying=false; }
            }
        }
        if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        string action;
        try { action = File.ReadAllText(Request).Trim(); } catch (IOException) { return; }
        File.Delete(Request); Directory.CreateDirectory(Output);
        try
        {
            if (action == "inspect") Inspect();
            else if (action == "apply") Apply();
            else if (action == "capture") { SaveCurrent();EditorSceneManager.OpenScene(Main);SessionState.SetBool("CampUnifiedPlay",true);EditorApplication.isPlaying=true; }
            else if (action == "lantern") InstallLantern();
            else if (action == "edges") FinishEdges();
            else if (action == "nav") { SaveCurrent();EditorSceneManager.OpenScene(Main);SessionState.SetBool("CampUnifiedPlay",true);SessionState.SetBool("CampUnifiedNav",true);EditorApplication.isPlaying=true; }
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); }
    }

    static readonly Dictionary<Material,Material> Materials = new Dictionary<Material,Material>();
    static void DumpNavigation()
    {
        var camp=Object.FindAnyObjectByType<CampPlayerView>();
        var map=typeof(CampPlayerView).GetProperty("WalkMap",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(camp);
        object Field(string name)=>map.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(map);
        int width=(int)Field("_width"),height=(int)Field("_height");var cells=(bool[])Field("_cells");var origin=(Game.Sim.FixVec2)Field("_origin");
        var png=new Texture2D(width,height,TextureFormat.RGB24,false);var colors=new Color[cells.Length];
        float ox=origin.X.ToFloat(),oz=origin.Y.ToFloat();int start=Mathf.FloorToInt((camp.Position.z-oz)*8)*width+Mathf.FloorToInt((camp.Position.x-ox)*8);
        var reached=new bool[cells.Length];var queue=new Queue<int>();queue.Enqueue(start);reached[start]=true;
        while(queue.Count>0)
        {
            int at=queue.Dequeue(),x=at%width,z=at/width;
            foreach(var d in new[]{new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)})
            {int nx=x+d.x,nz=z+d.y;if(nx<0||nx>=width||nz<0||nz>=height)continue;int n=nz*width+nx;if(!cells[n]||reached[n])continue;reached[n]=true;queue.Enqueue(n);}
        }
        for(int i=0;i<colors.Length;i++)colors[i]=!cells[i]?new Color(.07f,.07f,.07f):reached[i]?new Color(.15f,.85f,.7f):new Color(1,.4f,.15f);
        colors[start]=Color.white;png.SetPixels(colors);png.Apply();File.WriteAllBytes(Path.Combine(Output,"navigation.png"),png.EncodeToPNG());Object.DestroyImmediate(png);
        var report=new StringBuilder();report.AppendLine("origin="+origin+" width="+width+" height="+height+" cell=.125 cyan=connected orange=disconnected");
        var river=Object.FindAnyObjectByType<CampRiver>();
        foreach(float x in new[]{12f,19f})
        {
            var wanted=river.transform.TransformPoint(new Vector3(x,camp.GroundHeight,river.LandEdge(x)+1.3f));float best=float.MaxValue;Vector3 closest=default;
            for(int i=0;i<reached.Length;i++)if(reached[i])
            {var p=new Vector3(ox+(i%width+.5f)*.125f,camp.GroundHeight,oz+(i/width+.5f)*.125f);float d=(p-wanted).sqrMagnitude;if(d<best){best=d;closest=p;}}
            report.AppendLine("RIVER APPROACH localX="+x+" requested="+wanted+" connected="+closest+" distance="+Mathf.Sqrt(best));
        }
        foreach(var c in Object.FindAnyObjectByType<SceneWorldView>().CampRoot.GetComponentsInChildren<Collider>())
            report.AppendLine("COLLIDER "+Relative(c.transform,Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform)+" bounds="+c.bounds+" enabled="+c.enabled);
        File.WriteAllText(Path.Combine(Output,"navigation.txt"),report.ToString());
    }
    static readonly Dictionary<Mesh,Mesh> Meshes = new Dictionary<Mesh,Mesh>();
    static Material Copy(Material original)
    {
        if (original == null) return null;
        if (Materials.TryGetValue(original,out var material)) return material;
        material = new Material(original) { name = "Camp " + original.name, enableInstancing = true };
        AssetDatabase.CreateAsset(material,AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + material.name.Replace('/','_') + ".mat"));
        Materials.Add(original,material); return material;
    }
    static Mesh Copy(Mesh original)
    {
        if (Meshes.TryGetValue(original,out var mesh)) return mesh;
        mesh = Object.Instantiate(original); mesh.name = original.name;
        AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + mesh.name.Replace('/','_') + ".asset"));
        Meshes.Add(original,mesh); return mesh;
    }
    static SceneWorldView World(Scene scene)
    {
        foreach(var root in scene.GetRootGameObjects())
        {
            var world=root.GetComponentInChildren<SceneWorldView>(true); if(world!=null)return world;
        }
        throw new InvalidOperationException("В сцене нет SceneWorldView.");
    }
    [MenuItem("Разлом/Лагерь/Перенести атмосферный стиль на лагерь")]
    public static void Apply()
    {
        SaveCurrent();
        var main=EditorSceneManager.OpenScene(Main); var camp=World(main).CampRoot;
        if(camp.transform.Find("Художественный проход лагеря")!=null) throw new InvalidOperationException("Проход уже перенесён: ручные правки не перезаписываются.");
        CampSceneAmbiencePreview.Stop();
        EditorSceneManager.SaveScene(main,Path.Combine(Output,"main-before.unity"),true);
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); Materials.Clear(); Meshes.Clear();
        var study=EditorSceneManager.OpenScene(Study,OpenSceneMode.Additive);
        var source=World(study).CampRoot;
        var transforms=new Dictionary<Transform,Matrix4x4>();
        foreach(var t in camp.GetComponentsInChildren<Transform>(true)) transforms[t]=t.localToWorldMatrix;
        int materialCount=0,meshCount=0;
        try
        {
            SceneManager.SetActiveScene(study);
            var mode=RenderSettings.ambientMode; var sky=RenderSettings.ambientSkyColor;
            var equator=RenderSettings.ambientEquatorColor; var ground=RenderSettings.ambientGroundColor;
            var sun=RenderSettings.sun;
            var sourceLights=new Dictionary<string,Light>();
            foreach(var sceneRoot in study.GetRootGameObjects())
                foreach(var light in sceneRoot.GetComponentsInChildren<Light>(true))
                    if(light.type==LightType.Directional)sourceLights[light.name]=light;
            VolumeProfile selected=null;
            foreach(var sceneRoot in study.GetRootGameObjects())
                foreach(var volume in sceneRoot.GetComponentsInChildren<Volume>(true))
                    if(volume.name=="Global Volume") selected=volume.sharedProfile;
            SceneManager.SetActiveScene(main);
            RenderSettings.ambientMode=mode; RenderSettings.ambientSkyColor=sky;
            RenderSettings.ambientEquatorColor=equator; RenderSettings.ambientGroundColor=ground;
            foreach(var sceneRoot in main.GetRootGameObjects())
                foreach(var light in sceneRoot.GetComponentsInChildren<Light>(true))
                    if(light.type==LightType.Directional && sourceLights.TryGetValue(light.name,out var original))
                    {
                        light.color=original.color;light.intensity=original.intensity;light.shadows=original.shadows;light.shadowStrength=original.shadowStrength;
                        if(original==sun)RenderSettings.sun=light;
                    }
            var decoration=new GameObject("Художественный проход лагеря");decoration.transform.SetParent(camp.transform,true);decoration.AddComponent<CampSceneryDecoration>();
            // Цветокоррекция включается вместе с CampRoot; профиль забега сохраняется отдельно.
            string profilePath=Folder+"/CampLook.asset";
            if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(selected),profilePath))throw new IOException("Профиль лагеря не скопирован.");
            var campVolume=decoration.AddComponent<Volume>();campVolume.isGlobal=true;campVolume.priority=20;campVolume.weight=1;
            campVolume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                var target=camp.transform.Find(Relative(renderer.transform,source.transform));
                if(target==null || target.GetComponent<Renderer>()==null)continue;
                var original=renderer.sharedMaterials;var updated=target.GetComponent<Renderer>().sharedMaterials;
                for(int i=0;i<Math.Min(original.Length,updated.Length);i++)
                    if(original[i]!=null && AssetDatabase.GetAssetPath(original[i]).StartsWith("Assets/Scenes/Studies/SmithDepth/"))
                    {updated[i]=Copy(original[i]);materialCount++;}
                target.GetComponent<Renderer>().sharedMaterials=updated;
                var filter=renderer.GetComponent<MeshFilter>();var targetFilter=target.GetComponent<MeshFilter>();
                if(filter!=null && targetFilter!=null && filter.name!="Continuous ground" && AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith("Assets/Scenes/Studies/SmithDepth/"))
                {targetFilter.sharedMesh=Copy(filter.sharedMesh);meshCount++;}
            }
            var oldBase=source.transform.Find("Кузница — древнее основание");
            if(oldBase!=null)
            {
                var clone=Object.Instantiate(oldBase.gameObject);clone.name=oldBase.name;
                clone.transform.localScale=oldBase.lossyScale;
                SceneManager.MoveGameObjectToScene(clone,main);clone.transform.SetParent(camp.transform,true);
                foreach(var r in clone.GetComponentsInChildren<Renderer>())
                {
                    var materials=r.sharedMaterials;for(int i=0;i<materials.Length;i++)materials[i]=Copy(materials[i]);r.sharedMaterials=materials;
                }
                foreach(var f in clone.GetComponentsInChildren<MeshFilter>())
                    if(AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Scenes/Studies/SmithDepth/"))f.sharedMesh=Copy(f.sharedMesh);
            }
            var forge=camp.transform.Find("Smith Shelter/medieval+blacksmith+forge+3d+model");
            Vector3 smithOrigin=forge.position;
            forge.position+=Vector3.up*.24f;
            var river=camp.GetComponentInChildren<CampRiver>();
            // Исходная регулярная сетка сохраняется для последующих ручных правок реки.
            var originalGround=Copy(river.SourceGround);var vertices=originalGround.vertices;
            Vector3 back=smithOrigin-camp.transform.Find("Anchor - Smith").position;back.y=0;back.Normalize();Vector3 across=Vector3.Cross(Vector3.up,back);
            for(int i=0;i<vertices.Length;i++)
            {
                var p=river.Ground.transform.TransformPoint(vertices[i]);var local=p-smithOrigin;
                float u=Vector3.Dot(local,across),v=Vector3.Dot(local,back);
                float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.6f,3.6f,v))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(6.8f,9f,v)))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4.5f,7f,Mathf.Abs(u))))*(.35f+.24f*Mathf.PerlinNoise(p.x*.31f,p.z*.31f));
                p.y+=rise;vertices[i]=river.Ground.transform.InverseTransformPoint(p);
            }
            originalGround.vertices=vertices;originalGround.RecalculateNormals();originalGround.RecalculateBounds();EditorUtility.SetDirty(originalGround);river.SourceGround=originalGround;
            EditorSceneManager.CloseScene(study,true);
            CampFinishAuthoring.Rebuild(river);
            TuneCampMaterials(camp);
            GroupOpenAreas(camp);
            Dress(camp,decoration.transform);
            CampSceneAmbiencePreview.Stop();
            int changed=0;
            foreach(var entry in transforms)
                if(entry.Key!=forge && entry.Key!=null && entry.Key.localToWorldMatrix!=entry.Value)changed++;
            if(changed!=0)throw new InvalidOperationException("Изменились авторские трансформы: "+changed);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(main);
            File.WriteAllText(Path.Combine(Output,"transfer.txt"),"material slots="+materialCount+"\nfoliage meshes="+meshCount+"\nauthored transforms changed excluding raised forge="+changed+"\nmain="+Main);
        }
        finally { if(study.isLoaded)EditorSceneManager.CloseScene(study,true);SceneManager.SetActiveScene(main); }
    }

    static void TuneCampMaterials(GameObject camp)
    {
        foreach(var renderer in camp.GetComponentsInChildren<Renderer>(true))
        {
            if(renderer.GetComponentInParent<CampGroundStudy>()!=null || renderer.GetComponentInParent<CampRiver>()!=null || renderer.GetComponentInParent<CampMagicDecoration>()!=null)continue;
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var source=materials[i];if(source==null || !source.HasProperty("_Smoothness"))continue;
                // Листва сохраняет исходный tint: белый атлас у палитровых моделей — часть материала.
                bool foliage=renderer.name.ToLowerInvariant().Contains("tree") || renderer.name.Contains("Spruce") || renderer.name.Contains("Bush");
                var mat=AssetDatabase.GetAssetPath(source).StartsWith(Folder)?source:Copy(source);
                mat.SetFloat("_Smoothness",foliage?.08f:Mathf.Min(mat.GetFloat("_Smoothness"),.24f));
                if(foliage && mat.HasProperty("_SpecularHighlights")){mat.SetFloat("_SpecularHighlights",0);mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");}
                materials[i]=mat;EditorUtility.SetDirty(mat);
            }
            renderer.sharedMaterials=materials;
        }
        foreach(var light in camp.GetComponentsInChildren<Light>())
        {
            if(light.GetComponentInParent<CampMagicCircle>()!=null)continue;
            string path=Relative(light.transform,camp.transform);
            if(path.StartsWith("Campfire/")) { light.intensity=2.7f;light.range=4.4f;light.color=new Color(1,.47f,.16f); }
            else if(path.StartsWith("Tent - Trader/")) { light.intensity=2.8f;light.range=3.2f;light.color=new Color(1,.63f,.31f); }
            else if(!path.StartsWith("Smith Shelter/")) {light.intensity=Mathf.Min(light.intensity,3.7f);light.color=new Color(1,.58f,.26f);}
        }
    }
    static void GroupOpenAreas(GameObject camp)
    {
        var ground=camp.GetComponentInChildren<CampGroundStudy>();
        Vector3 fire=camp.transform.Find("Campfire").position,trade=camp.transform.Find("Anchor - Trader").position;
        int serial=0;
        foreach(var filter in ground.GetComponentsInChildren<MeshFilter>())
        {
            if(!filter.name.StartsWith("Grass") && !filter.name.StartsWith("Meadow petals") && !filter.name.StartsWith("Flower centres"))continue;
            var source=filter.sharedMesh;var bends=new List<Vector4>();source.GetUVs(3,bends);if(bends.Count!=source.vertexCount)continue;
            var mesh=Copy(source);var keep=new bool[bends.Count];
            for(int i=0;i<keep.Length;i++)
            {
                var p=filter.transform.TransformPoint(new Vector3(bends[i].x,bends[i].y,bends[i].z));
                float distFire=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(fire.x,fire.z));
                float distTrade=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(trade.x,trade.z));
                float cluster=Mathf.PerlinNoise(p.x*.66f+19,p.z*.66f+7);
                keep[i]=distFire>1.5f && !(distFire<5.7f && cluster<.36f) && !(distTrade<5.3f && cluster<.38f);
            }
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var indices=mesh.GetTriangles(sub);var kept=new List<int>();
                for(int i=0;i<indices.Length;i+=3)if(keep[indices[i]]&&keep[indices[i+1]]&&keep[indices[i+2]])kept.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
                mesh.SetTriangles(kept,sub);
            }
            mesh.RecalculateBounds();filter.sharedMesh=mesh;EditorUtility.SetDirty(mesh);serial++;
        }
    }
    static void Dress(GameObject camp,Transform parent)
    {
        var rockPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock01.prefab");
        var grassPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        var stone=Copy(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Environment/Camp/River/Bank stones.mat"));
        var ground=camp.GetComponentInChildren<CampGroundStudy>().transform.Find("Continuous ground");
        var fire=camp.transform.Find("Campfire");
        var features=new GameObject("Костёр — обжитая площадка");features.transform.SetParent(parent,false);
        var fixedBounds=new List<Bounds>();
        foreach(var renderer in fire.GetComponentsInChildren<MeshRenderer>())
            if(renderer.name.Contains("bench") || renderer.name.Contains("crate"))fixedBounds.Add(renderer.bounds);
        int serial=0;
        for(int i=0;i<19;i++)
        {
            float angle=(40+i*16)*Mathf.Deg2Rad,radius=1.8f+.3f*Mathf.Sin(i*7.3f);
            var p=fire.position+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
            bool blocked=false;foreach(var b in fixedBounds){var expanded=b;expanded.Expand(.35f);p.y=b.center.y;if(expanded.Contains(p))blocked=true;}p.y=.035f;
            if(blocked || Vector3.Distance(p,camp.transform.Find("Anchor - Player").position)<1.1f)continue;
            PlacePrefab(rockPrefab,features.transform,"Притоптанный камень "+serial++,p,new Vector3(.24f+i%3*.06f,.11f,.22f+i%2*.04f),i*137.5f,stone);
        }
        var river=camp.GetComponentInChildren<CampRiver>();
        var shore=new GameObject("Торговец — зелёный берег");shore.transform.SetParent(parent,false);
        // Кластеры идут вдоль ближней кромки, а не по воде или середине подхода к торговцу.
        foreach(float x in new[]{11.8f,14.8f,18f,21f})
        {
            var p=river.transform.TransformPoint(new Vector3(x,.015f,river.LandEdge(x)+.38f));
            for(int i=0;i<3;i++)
            {
                var at=p+river.transform.right*((i-1)*.42f)+river.transform.forward*(.1f+i*.07f);
                PlacePrefab(rockPrefab,shore.transform,"Серый камень у корней "+serial++,at,new Vector3(.5f+i*.12f,.24f+i*.035f,.43f),i*113+x*13,stone);
                PlacePrefab(grassPrefab,shore.transform,"Прибрежные листья "+serial++,at+river.transform.forward*.4f,new Vector3(.65f,.46f,.59f),i*137+x*17,null);
            }
        }
        var magic=camp.GetComponentInChildren<CampMagicCircle>();
        magic.Atmosphere=.82f;EditorUtility.SetDirty(magic);
        if(magic.StoneMaterialAssets!=null)
            foreach(var mat in magic.StoneMaterialAssets)if(mat!=null && mat.HasProperty("_Smoothness")){mat.SetFloat("_Smoothness",.12f);EditorUtility.SetDirty(mat);}
    }
    static GameObject PlacePrefab(GameObject prefab,Transform parent,string name,Vector3 position,Vector3 size,float yaw,Material material)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=name;go.transform.localScale=Vector3.one;
        var renderer=go.GetComponentInChildren<Renderer>();var bounds=renderer.bounds;
        go.transform.localScale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));
        go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        foreach(var collider in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        if(material!=null)foreach(var r in go.GetComponentsInChildren<Renderer>())r.sharedMaterial=material;
        return go;
    }
    static void CaptureFrames()
    {
        var camp=Object.FindAnyObjectByType<SceneWorldView>().CampRoot;
        var magic=camp.GetComponentInChildren<CampMagicCircle>();var river=camp.GetComponentInChildren<CampRiver>();
        Capture(camp.transform.Find("Campfire").position+Vector3.up*.7f,6.7f,"01-campfire.png");
        Capture(camp.transform.Find("Anchor - Trader").position+new Vector3(2,.6f,1),6.8f,"02-trader.png");
        Capture(magic.Centre.position+Vector3.up*.6f,9.4f,"03-magic.png");
        Capture(new Vector3(-2,.5f,0),17.5f,"04-overview.png");
        Capture(river.transform.TransformPoint(new Vector3(19,0,river.LandEdge(19))),7f,"05-river-turn.png");
        Capture(camp.transform.Find("Campfire/fonar (2)").GetComponent<Renderer>().bounds.center,2.7f,"06-lantern.png");
    }
    public static void InstallLantern()
    {
        SaveCurrent();EditorSceneManager.OpenScene(Main);
        string path=Folder+"/CampLantern_LOD0.fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        if(!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Environment/Camp/fonar.fbx").GetComponentInChildren<MeshFilter>();
        var low=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>();
        var mesh=Object.Instantiate(low.sharedMesh);mesh.name="Camp lantern — 18k";
        var matrix=original.transform.worldToLocalMatrix*low.transform.localToWorldMatrix;
        var vertices=mesh.vertices;var normals=mesh.normals;var normalMatrix=matrix.inverse.transpose;
        for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
        for(int i=0;i<normals.Length;i++)normals[i]=normalMatrix.MultiplyVector(normals[i]).normalized;
        mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();
        var a=original.sharedMesh.bounds;var b=mesh.bounds;
        var report=new StringBuilder();report.AppendLine("original="+a+"\noptimized="+b+"\nconversion="+matrix);
        float tolerance=a.size.magnitude*.01f;
        if(Vector3.Distance(a.center,b.center)>tolerance || Vector3.Distance(a.size,b.size)>tolerance)
        {File.WriteAllText(Path.Combine(Output,"lantern-install.txt"),report.ToString());Object.DestroyImmediate(mesh);throw new InvalidOperationException("Система координат нового фонаря не совпадает.");}
        string meshPath=Folder+"/Camp lantern — 18k.asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(meshPath)!=null)throw new InvalidOperationException("Облегчённый фонарь уже установлен.");
        AssetDatabase.CreateAsset(mesh,meshPath);
        int count=0;
        foreach(var filter in Object.FindAnyObjectByType<SceneWorldView>().CampRoot.GetComponentsInChildren<MeshFilter>(true))
            if(filter.sharedMesh==original.sharedMesh){filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);count++;}
        report.AppendLine("replaced="+count+"\ntriangles="+mesh.triangles.Length/3);
        File.WriteAllText(Path.Combine(Output,"lantern-install.txt"),report.ToString());
        AssetDatabase.SaveAssets();CampSceneAmbiencePreview.Stop();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }
    public static void FinishEdges()
    {
        SaveCurrent();EditorSceneManager.OpenScene(Main);
        var camp=Object.FindAnyObjectByType<SceneWorldView>().CampRoot;
        var root=camp.transform.Find("Художественный проход лагеря");
        if(root.Find("Продолжение лесной земли")!=null)throw new InvalidOperationException("Продолжение земли уже сохранено.");
        var river=camp.GetComponentInChildren<CampRiver>();
        var vertices=new List<Vector3>();var indices=new List<int>();
        const int side=65;const float step=2.5f;
        var near=new bool[side*side];var far=new bool[side*side];
        for(int z=0;z<side;z++)for(int x=0;x<side;x++)
        {
            var p=new Vector3(-80+x*step,-.055f,-80+z*step);vertices.Add(p);
            var local=river.transform.InverseTransformPoint(p);float centre=river.CentreAt(local.x),land=river.LandEdge(local.x);
            near[z*side+x]=local.z>land+1;
            far[z*side+x]=local.z<centre-Mathf.Max(river.Width+river.BankWidth,(land-centre)*1.3f)-2;
        }
        void Tri(int a,int b,int c)
        {
            // Вода остаётся открытой. Продолжение служит только видимым фоном за существующей землёй.
            if((near[a]&&near[b]&&near[c])||(far[a]&&far[b]&&far[c]))indices.AddRange(new[]{a,b,c});
        }
        for(int z=0;z<side-1;z++)for(int x=0;x<side-1;x++){int i=z*side+x;Tri(i,i+side,i+1);Tri(i+1,i+side,i+side+1);}
        var mesh=new Mesh{name="Forest ground continuation"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var uv=new Vector2[vertices.Count];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(vertices[i].x,vertices[i].z);mesh.uv=uv;
        AssetDatabase.CreateAsset(mesh,Folder+"/Forest ground continuation.asset");
        var source=river.Ground.GetComponent<Renderer>().sharedMaterial;
        var material=new Material(source){name="Forest ground continuation"};
        var surface=new Texture2D(1,1,TextureFormat.RGBA32,false,true){name="Quiet forest layers",wrapMode=TextureWrapMode.Clamp};
        surface.SetPixel(0,0,new Color(0,0,1,1));surface.Apply();AssetDatabase.CreateAsset(surface,Folder+"/Quiet forest layers.asset");
        material.SetTexture("_SurfaceMap",surface);material.SetFloat("_IsRiverBank",0);AssetDatabase.CreateAsset(material,Folder+"/Forest ground continuation.mat");
        var go=new GameObject("Продолжение лесной земли");go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        // У береговых мешей сохраняем фактуру, но освещение верхнего ребра задаёт горизонтальная нормаль.
        CampFinishAuthoring.Rebuild(river);
        AssetDatabase.SaveAssets();CampSceneAmbiencePreview.Stop();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }
    static void Capture(Vector3 focus,float size,string filename)
    {
        var go=new GameObject("Camp review camera") { hideFlags=HideFlags.HideAndDontSave };
        var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
        camera.transform.rotation=Quaternion.Euler(48,35,0);camera.transform.position=focus-camera.transform.forward*80;
        camera.orthographic=true;camera.orthographicSize=size;camera.aspect=16f/9;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.requiresDepthTexture=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;Texture2D pixels=null;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,filename),pixels.EncodeToPNG());
        }
        finally { RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);if(pixels!=null)Object.DestroyImmediate(pixels);Object.DestroyImmediate(go); }
    }
    static void SaveCurrent()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.isDirty) return;
        if (scene.path != Main && scene.path != Study) throw new InvalidOperationException("Открыта другая несохранённая сцена.");
        CampSceneAmbiencePreview.Stop();
        EditorSceneManager.SaveScene(scene, Path.Combine(Output, "open-scene-before-" + DateTime.Now.ToString("HHmmss") + ".unity"), true);
        EditorSceneManager.SaveScene(scene);
    }
    static string Relative(Transform t, Transform root)
    {
        if (t == root) return "";
        return t.parent == root ? t.name : Relative(t.parent, root) + "/" + t.name;
    }
    public static void Inspect()
    {
        SaveCurrent(); string previous = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(Main);
        try
        {
            var camp = Object.FindAnyObjectByType<SceneWorldView>().CampRoot;
            var report = new StringBuilder();
            report.AppendLine("Camp=" + camp.transform.position + " scale=" + camp.transform.lossyScale);
            foreach (Transform child in camp.transform)
                report.AppendLine("CHILD " + child.name + " pos=" + child.position + " rot=" + child.eulerAngles + " scale=" + child.lossyScale);
            var entries = new List<KeyValuePair<long, string>>();
            var materials = new HashSet<Material>();
            foreach (var r in camp.GetComponentsInChildren<Renderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>(); long triangles = 0;
                if (filter != null && filter.sharedMesh != null)
                    for (int i=0;i<filter.sharedMesh.subMeshCount;i++) triangles += filter.sharedMesh.GetIndexCount(i)/3;
                entries.Add(new KeyValuePair<long,string>(triangles, Relative(r.transform,camp.transform) + " bounds=" + r.bounds + " triangles=" + triangles + " mesh=" + (filter != null ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "none")));
                if (r.transform.parent != null && (Relative(r.transform,camp.transform).Contains("Campfire") || Relative(r.transform,camp.transform).Contains("Merchant") || r.name.Contains("tree")))
                {
                    report.AppendLine("RENDER " + Relative(r.transform,camp.transform) + " pos=" + r.transform.position + " bounds=" + r.bounds + " tris=" + triangles);
                    foreach (var mat in r.sharedMaterials) if (mat != null) { materials.Add(mat); report.AppendLine("  mat=" + AssetDatabase.GetAssetPath(mat)); }
                }
            }
            entries.Sort((a,b)=>b.Key.CompareTo(a.Key));
            for(int i=0;i<Math.Min(entries.Count,25);i++) report.AppendLine("HEAVY " + entries[i].Value);
            foreach (var mat in materials)
            {
                report.AppendLine("MATERIAL " + AssetDatabase.GetAssetPath(mat) + " shader=" + mat.shader.name);
                foreach(string prop in new[]{"_BaseColor","_Smoothness","_Metallic"})
                    if(mat.HasProperty(prop)) report.AppendLine("  " + prop + "=" + (prop.Contains("Color")?mat.GetColor(prop).ToString():mat.GetFloat(prop).ToString()));
            }
            foreach(var light in camp.GetComponentsInChildren<Light>(true)) report.AppendLine("LIGHT " + Relative(light.transform,camp.transform) + " pos=" + light.transform.position + " intensity=" + light.intensity + " range=" + light.range + " color=" + light.color);
            var river = camp.GetComponentInChildren<CampRiver>();
            report.AppendLine("RIVER pos="+river.transform.position+" rot="+river.transform.eulerAngles+" scale="+river.transform.lossyScale+" width="+river.Width+" bank="+river.BankWidth+" flow="+river.FlowSpeed);
            foreach(var p in river.Contour) report.AppendLine("CONTOUR " + p);
            File.WriteAllText(Path.Combine(Output,"main-inspect.txt"),report.ToString());
        }
        finally { EditorSceneManager.OpenScene(previous); }
    }
}
