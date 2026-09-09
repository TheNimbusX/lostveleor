using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.View;
using Object = UnityEngine.Object;

public static class CampGroundStudyBuilder
{
    const string Folder = "Assets/Resources/Environment/Camp/ground/Study";
    const string RootName = "Ground Study - Campfire";
    static string Repo => Directory.GetParent(Application.dataPath).Parent.FullName;
    static string Request => Path.Combine(Repo, "artifacts/request-ground-study");
    static readonly List<Texture2D> TemporaryTextures = new List<Texture2D>();
    sealed class Road { public Transform Transform; public Matrix4x4 ToLocal; public Texture2D Mask; }

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command;
        try { command=File.ReadAllText(Request).Trim(); }
        catch(IOException) { return; }
        if(command=="capture-ground-shadows" || command=="capture-ground-key")
        {
            File.Delete(Request);
            try
            {
                Shader.SetGlobalFloat("_CampShadowDiagnostic",command=="capture-ground-key"?2:1);
                CaptureCurrentCamera();
                File.Copy(Path.Combine(Repo,"artifacts/ground-study-live/current-camera.png"),Path.Combine(Repo,command=="capture-ground-key"?"artifacts/ground-study-live/key-diagnostic.png":"artifacts/ground-study-live/shadow-diagnostic.png"),true);
            }
            finally { Shader.SetGlobalFloat("_CampShadowDiagnostic",0); CaptureCurrentCamera(); }
            return;
        }
        if(command=="capture-v3") { File.Delete(Request); CaptureCurrentCamera(); RazlomLightingReport.Report(); return; }
        if(command != "ground-surface-v5") return;
        File.Delete(Request);
        try { Install(); EditorApplication.delayCall += CaptureCurrentCamera; }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Repo,"artifacts/ground-study-error.txt"),e.ToString()); }
    }

    public static void BuildCapture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Install();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    [MenuItem("Разлом/Лагерь/Участок земли у костра")]
    public static void Install()
    {
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world == null || world.CampRoot == null) throw new InvalidOperationException("Open the authored camp scene first.");
        var camp = world.CampRoot.transform;
        Transform fire = camp.Find("Campfire");
        if (fire == null) throw new InvalidOperationException("Campfire anchor is missing.");
        var scene = camp.gameObject.scene;
        string outDir = Path.Combine(Repo,"artifacts/ground-study-live");
        Directory.CreateDirectory(outDir);
        // Копия включает несохранённую работу владельца; установка идёт через живой редактор.
        string backup = Path.Combine(outDir,"before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity");
        EditorSceneManager.SaveScene(scene,backup,true);
        CampGroundSetup.Apply();
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var previous = camp.Find(RootName);
        if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
        Vector3 center = fire.position;
        var ground = camp.Find("Ground/Ground_Base");
        if (ground == null) ground = camp.Find("Ground_Base");
        center.y = ground != null ? ground.position.y + .025f : .025f;
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root,"Install camp ground study");
        root.transform.SetParent(camp,true); root.transform.position = center;
        root.AddComponent<CampGroundStudy>().Radius=12f;
        var floorBounds=ground != null ? ground.GetComponent<Renderer>().bounds : new Bounds(center,new Vector3(24,1,24));
        var roads = new List<Road>();
        var authoredSurfaces=new List<Renderer>();
        var originalEnabled=new List<bool>();
        var masks = new Dictionary<Texture,Texture2D>();
        var blocked = new List<Bounds>();
        foreach (var renderer in camp.GetComponentsInChildren<MeshRenderer>())
        {
            var mat = renderer.sharedMaterial;
            if (mat != null && mat.shader.name == "Game/Camp Ground")
            {
                authoredSurfaces.Add(renderer); originalEnabled.Add(renderer.enabled);
                if (renderer.enabled && mat.GetFloat("_IsPath") > .5f)
                {
                    var tex = mat.GetTexture("_BaseMap");
                    if (tex != null)
                    {
                        if (!masks.TryGetValue(tex,out var readable)) { readable=ReadMask(tex); masks.Add(tex,readable); }
                        roads.Add(new Road {Transform=renderer.transform,ToLocal=renderer.transform.worldToLocalMatrix,Mask=readable});
                    }
                }
                continue;
            }
            var b = renderer.bounds;
            if (b.max.y > center.y+.22f && b.min.y < center.y+.65f) { b.Expand(.18f); blocked.Add(b); }
        }
        Func<Vector3,float> footprint = p =>
        {
            // Небольшая эрозия общего контура сужает обочины без разрывов между исходными пятнами.
            const float shoulder=.16f;
            float value=SampleRoad(roads,p);
            value=Mathf.Min(value,SampleRoad(roads,p+Vector3.right*shoulder));
            value=Mathf.Min(value,SampleRoad(roads,p-Vector3.right*shoulder));
            value=Mathf.Min(value,SampleRoad(roads,p+Vector3.forward*shoulder));
            return Mathf.Min(value,SampleRoad(roads,p-Vector3.forward*shoulder));
        };
        Func<Vector3,float> surfaceHeight=CampSurfaceBuilder.Create(root.transform, floorBounds, center, footprint, blocked);
        var grass = new List<Vector3>[] {new List<Vector3>(),new List<Vector3>(),new List<Vector3>()};
        var grassBend = new List<Vector4>[] {new List<Vector4>(),new List<Vector4>(),new List<Vector4>()};
        var stone = new List<Vector3>();
        var petals = new List<Vector3>();
        var pollen = new List<Vector3>();
        var petalBend = new List<Vector4>();
        var pollenBend = new List<Vector4>();
        var tuftPositions = new List<Vector3>();
        var stonePositions = new List<Vector3>();
        var random = new System.Random(90317);
        int tufts=0,stones=0;
        for(int i=0;i<24000;i++)
        {
            float x=Next(random,-11.5f,11.5f), z=Next(random,-11.5f,11.5f);
            var local = new Vector3(x,0,z);
            if(local.magnitude>11.5f || local.magnitude<1.15f) continue;
            Vector3 p=center+local;
            if(p.x<floorBounds.min.x+.2f||p.x>floorBounds.max.x-.2f||p.z<floorBounds.min.z+.2f||p.z>floorBounds.max.z-.2f) continue;
            bool occupied=false;
            foreach(var b in blocked) if(p.x>b.min.x&&p.x<b.max.x&&p.z>b.min.z&&p.z<b.max.z) {occupied=true;break;}
            if(occupied) continue;
            float path=CampSurfaceBuilder.Path(footprint(p),p);
            local.y=surfaceHeight(p);
            float grouping=Mathf.PerlinNoise(p.x*.65f+8,p.z*.65f+17);
            float cluster=CampSurfaceBuilder.Coverage(p);
            float density=Mathf.Lerp(.14f,.74f,cluster);
            if(path>.12f) density=Mathf.Max(density,.56f);
            if(path<.68f && random.NextDouble()<density && tufts<2400 && Spaced(tuftPositions,local,Mathf.Lerp(.26f,.17f,cluster)))
            {
                int firstGrass0=grass[0].Count,firstGrass1=grass[1].Count,firstGrass2=grass[2].Count;
                int firstPetal=petals.Count,firstPollen=pollen.Count;
                tuftPositions.Add(local);
                float patch=Mathf.PerlinNoise(p.x*.73f+6,p.z*.73f+21);
                int tone=Mathf.Clamp(Mathf.FloorToInt(patch*4.7f)-1,0,2);
                float tuftHeight=Mathf.Lerp(.70f,1.25f,cluster)*Next(random,.85f,1.10f)*Mathf.Lerp(1f,.58f,path);
                bool broadleaf=random.NextDouble()<.68;
                if(broadleaf)
                {
                    // Низкие розетки чередуются со злаками, чтобы подлесок не состоял из одинаковых игл.
                    float rotation=Next(random,0,Mathf.PI*2);
                    for(int leaf=0;leaf<8;leaf++)
                    {
                        float angle=rotation+leaf*Mathf.PI*2/8+Next(random,-.35f,.35f);
                        int leafTone=random.NextDouble()<.24?Mathf.Clamp(tone+(random.Next(2)*2-1),0,2):tone;
                        Leaf(grass[leafTone],local,angle,Next(random,.19f,.32f)*tuftHeight,Next(random,.055f,.09f),Next(random,.10f,.23f)*tuftHeight);
                    }
                }
                for(int blade=0;blade<(broadleaf?2:8);blade++)
                {
                    float angle=Next(random,0,Mathf.PI*2), h=Next(random,.10f,.25f)*tuftHeight;
                    Vector3 at=local+new Vector3(Next(random,-.10f,.10f),0,Next(random,-.10f,.10f));
                    Vector3 side=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Next(random,.019f,.043f);
                    Vector3 lean=new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle))*h*.90f;
                    Vector3 mid=at+Vector3.up*h*.5f+lean*.22f;
                    Vector3 upper=at+Vector3.up*h*.86f+lean*.60f,tip=at+Vector3.up*h+lean;
                    Tri(grass[tone],at-side*.5f,at+side*.5f,mid+side);
                    Tri(grass[tone],at-side*.5f,mid+side,mid-side);
                    Tri(grass[tone],mid-side,mid+side,upper+side*.5f);
                    Tri(grass[tone],mid-side,upper+side*.5f,upper-side*.5f);
                    Tri(grass[tone],upper-side*.5f,upper+side*.5f,tip);
                }
                float flowerPatch=Mathf.PerlinNoise(p.x*.92f+43,p.z*.92f+71);
                if(path<.5f && flowerPatch>.57f && random.NextDouble()<.48)
                    for(int f=0;f<random.Next(3,6);f++)
                    {
                        Vector3 flower=local+new Vector3(Next(random,-.18f,.18f),Next(random,.21f,.35f),Next(random,-.18f,.18f));
                        Vector3 stemBase=new Vector3(flower.x,local.y,flower.z);
                        Tri(grass[0],stemBase-Vector3.right*.008f,stemBase+Vector3.right*.008f,flower);
                        Flower(random.NextDouble()<.25?pollen:petals,pollen,flower,Next(random,.045f,.066f));
                    }
                AddBend(grass[0],grassBend[0],firstGrass0,local);
                AddBend(grass[1],grassBend[1],firstGrass1,local);
                AddBend(grass[2],grassBend[2],firstGrass2,local);
                AddBend(petals,petalBend,firstPetal,local);
                AddBend(pollen,pollenBend,firstPollen,local);
                tufts++;
            }
            else if(path>.25f && random.NextDouble()<.045f && stones<140 && Spaced(stonePositions,local,.34f))
            {
                stonePositions.Add(local);
                float radius=Next(random,.08f,.20f), height=Next(random,.018f,.055f), turn=Next(random,0,6.28f);
                Vector3 top=local+new Vector3(radius*.12f,height,-radius*.1f);
                var ring=new Vector3[7];
                var rim=new Vector3[7];
                for(int n=0;n<7;n++) {float a=turn+n*Mathf.PI*2/7;ring[n]=local+new Vector3(Mathf.Cos(a)*radius*Next(random,.78f,1.2f),-.01f,Mathf.Sin(a)*radius*.75f);}
                for(int n=0;n<7;n++) rim[n]=Vector3.Lerp(ring[n],top,.30f)+Vector3.up*height*.65f;
                for(int n=0;n<7;n++) {int next=(n+1)%7;Tri(stone,ring[next],ring[n],rim[n]);Tri(stone,ring[next],rim[n],rim[next]);Tri(stone,rim[next],rim[n],top);}
                stones++;
            }
        }
        Color[] colors={new Color(.25f,.36f,.11f),new Color(.34f,.44f,.14f),new Color(.44f,.52f,.20f)};
        for(int n=0;n<3;n++) SaveMesh(root.transform,"Grass "+n,grass[n],colors[n],grassBend[n]);
        SaveMesh(root.transform,"Trail stones",stone,new Color(.52f,.49f,.38f));
        SaveMesh(root.transform,"Meadow petals",petals,new Color(.85f,.82f,.65f),petalBend);
        SaveMesh(root.transform,"Flower centres",pollen,new Color(.91f,.65f,.10f),pollenBend);
        var study=root.GetComponent<CampGroundStudy>();
        study.AuthoredSurfaces=authoredSurfaces.ToArray(); study.OriginalEnabled=originalEnabled.ToArray(); study.Refresh();
        foreach(var texture in TemporaryTextures) Object.DestroyImmediate(texture);
        TemporaryTextures.Clear();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(outDir,"installed.txt"),$"center={center} radius=12 tufts={tufts} stones={stones} roads={roads.Count}\nscene={scene.path}");
        SceneView.RepaintAll();
        Debug.Log($"[ground-study] {tufts} tufts, {stones} stones, {roads.Count} road masks, center {center}");
    }

    static float Next(System.Random r,float min,float max) => min+(float)r.NextDouble()*(max-min);
    static float SampleRoad(List<Road> roads,Vector3 p)
    {
        float path=0;
        foreach(var road in roads)
        {
            Vector3 q=road.ToLocal.MultiplyPoint3x4(p);
            if(Mathf.Abs(q.x)>.5f||Mathf.Abs(q.y)>.5f)continue;
            path=Mathf.Max(path,road.Mask.GetPixelBilinear(q.x+.5f,q.y+.5f).a);
        }
        return path;
    }
    static bool Spaced(List<Vector3> positions,Vector3 p,float spacing)
    {
        foreach(var other in positions) if((other-p).sqrMagnitude<spacing*spacing)return false;
        return true;
    }
    static void Flower(List<Vector3> petals,List<Vector3> pollen,Vector3 center,float radius)
    {
        const int count=8;
        for(int n=0;n<count;n++)
        {
            float a=n*Mathf.PI*2/count;
            Vector3 along=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
            Vector3 side=new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a))*radius*.32f;
            Vector3 mid=center+along*.64f-Vector3.up*radius*.1f;
            Vector3 tip=center+along+Vector3.up*radius*.16f;
            Tri(petals,center,mid+side,tip+side*.42f);Tri(petals,center,tip+side*.42f,tip-side*.42f);Tri(petals,center,tip-side*.42f,mid-side);
            float b=(n+1)*Mathf.PI*2/count;
            Tri(pollen,center+Vector3.up*.004f,center+new Vector3(Mathf.Cos(b),.004f,Mathf.Sin(b))*radius*.24f,center+new Vector3(Mathf.Cos(a),.004f,Mathf.Sin(a))*radius*.24f);
        }
    }
    static void Leaf(List<Vector3> vertices,Vector3 root,float angle,float length,float width,float height)
    {
        Vector3 along=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
        Vector3 across=new Vector3(-along.z,0,along.x);
        // Складка и четыре сечения дают выпуклый лист с округлыми плечами, а не плоский ромб.
        Vector3 previousLeft=root,previousRight=root,previousRidge=root;
        for(int row=1;row<=4;row++)
        {
            float t=row/4f;
            float halfWidth=Mathf.Sin(t*Mathf.PI)*width;
            Vector3 ridge=root+along*(length*t)+Vector3.up*(Mathf.Sin(t*Mathf.PI*.78f)*height+.018f);
            Vector3 left=ridge-across*halfWidth-Vector3.up*halfWidth*.28f;
            Vector3 right=ridge+across*halfWidth-Vector3.up*halfWidth*.28f;
            Tri(vertices,previousLeft,left,ridge);Tri(vertices,previousLeft,ridge,previousRidge);
            Tri(vertices,previousRidge,ridge,right);Tri(vertices,previousRidge,right,previousRight);
            previousLeft=left;previousRight=right;previousRidge=ridge;
        }
    }
    static void Tri(List<Vector3> v,Vector3 a,Vector3 b,Vector3 c) {v.Add(a);v.Add(b);v.Add(c);}
    static Texture2D ReadMask(Texture source)
    {
        // PNG читается с диска, чтобы batchmode без GPU считал тот же контур.
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
        if(!texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)))) throw new InvalidOperationException("Cannot read ground mask.");
        TemporaryTextures.Add(texture); return texture;
    }
    [MenuItem("Разлом/Лагерь/Снимок текущей камеры земли")]
    public static void CaptureCurrentCamera()
    {
        var camera=Camera.main;
        if(camera==null) return;
        int height=900,width=Mathf.Clamp(Mathf.RoundToInt(camera.aspect*height),900,2400);
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
            RenderTexture.active=target;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            string dir=Path.Combine(Repo,"artifacts/ground-study-live");Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir,"current-camera.png"),texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
        finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
    }
    static void AddBend(List<Vector3> vertices,List<Vector4> bends,int first,Vector3 root)
    {
        for(int i=first;i<vertices.Count;i++)
        {
            float height=Mathf.Max(0,vertices[i].y-root.y);
            float angle=Mathf.SmoothStep(0,1,Mathf.Clamp01(height/.24f))*6f*Mathf.Deg2Rad;
            bends.Add(new Vector4(root.x,root.y,root.z,angle));
        }
    }
    static void SaveMesh(Transform parent,string name,List<Vector3> vertices,Color color,List<Vector4> bends=null)
    {
        string meshPath=Folder+"/"+name+".asset", matPath=Folder+"/"+name+".mat";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(mesh==null) {mesh=new Mesh();AssetDatabase.CreateAsset(mesh,meshPath);} else mesh.Clear();
        mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
        mesh.name=name; mesh.SetVertices(vertices);
        int[] indices=new int[vertices.Count];for(int i=0;i<indices.Length;i++)indices[i]=i;
        mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        if(bends!=null)
        {
            mesh.SetUVs(3,bends);
            var bounds=mesh.bounds;bounds.Expand(.12f);mesh.bounds=bounds;
        }
        if(name.StartsWith("Grass"))
        {
            // Световой объём низкой листвы направлен вверх: тонкие двухсторонние листья не чернеют.
            var normals=mesh.normals;
            for(int i=0;i<normals.Length;i++) normals[i]=Vector3.Lerp(normals[i],Vector3.up,.72f).normalized;
            mesh.normals=normals;
        }
        var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if(mat==null) {mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,matPath);}
        if(bends!=null)
        {
            var breeze=Shader.Find("Game/Camp Breeze Lit");
            if(breeze==null) throw new InvalidOperationException("Camp Breeze Lit shader is missing.");
            mat.shader=breeze;
            mat.SetShaderPassEnabled("MotionVectors",true);
        }
        mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",0);mat.SetFloat("_Cull",0);EditorUtility.SetDirty(mat);
        var child=new GameObject(name);child.transform.SetParent(parent,false);child.layer=2;
        child.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
    }
}
