using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;
using Object=UnityEngine.Object;

public static class CampFinishAuthoring
{
    const string Folder="Assets/Resources/Environment/Camp/River";
    static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static string Request=>Path.Combine(Repo,"artifacts/request-camp-finish");
    [InitializeOnLoadMethod] static void Watch()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request))return;
        string action=File.ReadAllText(Request).Trim();File.Delete(Request);
        try { if(action=="river-compact")CompactRiver();else if(action=="river-light")LightenRiver();else if(action=="river-final")FinishRiver();else if(action=="install")Install();else if(action=="redesign")Redesign();else if(action=="motion")MotionReview();else if(action=="polish")Polish();else if(action=="finish")Finish();else if(action=="rebuild")Rebuild(Object.FindAnyObjectByType<CampRiver>());else Inspect(); }
        catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Repo,"artifacts/camp-finish-error.txt"),e.ToString());}
    }
    static string Backup()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Нужен Edit Mode.");
        string output=Path.Combine(Repo,"artifacts/camp-finish-live",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        if(!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Path.Combine(output,"before.unity"),true))throw new IOException("Не сохранена копия открытой сцены.");
        if(Directory.Exists(Folder))foreach(string file in Directory.GetFiles(Folder))File.Copy(file,Path.Combine(output,Path.GetFileName(file)),true);
        return output;
    }
    [MenuItem("Разлом/Лагерь/Довести лагерь — камни, река, ветер")]
    public static void Install()
    {
        Backup();Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var world=Object.FindAnyObjectByType<SceneWorldView>();if(world==null || world.CampRoot==null)throw new InvalidOperationException("Нет лагеря.");
        var root=world.CampRoot.transform;var magic=root.GetComponentInChildren<CampMagicCircle>();
        int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Доводка лагеря");
        var river=root.GetComponentInChildren<CampRiver>();
        if(river==null)
        {
            var go=New("Река — нижняя граница лагеря",root);river=Undo.AddComponent<CampRiver>(go);
            go.transform.rotation=Quaternion.Euler(0,35,0);go.transform.localScale=Vector3.one;SetWorldScale(go.transform,Vector3.one);
            Vector3 forward=go.transform.forward;float lower=float.MaxValue;
            foreach(var altar in new[]{magic.FireAltar,magic.IceAltar,magic.EarthAltar,magic.AlchemyAltar})
                lower=Mathf.Min(lower,Vector3.Dot(altar.position-magic.Centre.position,forward)-1.7f);
            go.transform.position=magic.Centre.position+forward*(lower-river.Width*.5f-river.BankWidth-1.2f);
            foreach(var filter in root.GetComponentInChildren<CampGroundStudy>().GetComponentsInChildren<MeshFilter>())
                if(filter.sharedMesh!=null && filter.sharedMesh.name=="Camp surface"){river.Ground=filter;break;}
            if(river.Ground==null)throw new InvalidOperationException("Нет исходной поверхности лагеря.");
            river.SourceGround=Object.Instantiate(river.Ground.sharedMesh);river.SourceGround.name="Camp surface — до реки";
            AssetDatabase.CreateAsset(river.SourceGround,Folder+"/Original ground.asset");
            river.WaterMaterial=CopyMaterial("Assets/RPG Tiny Fantasy Forest PBR/Material/Special/Water_River.mat","Forest water");
            var water=river.WaterMaterial;water.SetColor("_DeepWaterColor",new Color(.035f,.24f,.28f,1));water.SetColor("_ShallowWaterColor",new Color(.18f,.40f,.34f,1));
            water.SetFloat("_Normal01Strength",.27f);water.SetFloat("_Normal02Strength",.17f);water.SetFloat("_Smoothness",.72f);water.SetFloat("_Depth",1.4f);
            water.SetFloat("_FoamAmount",.035f);water.SetFloat("_FoamScale",.4f);water.SetFloat("_FoamSpeed",.06f);water.SetFloat("_FoamCutoff",.93f);
            water.SetVector("_UV4Normal01",new Vector4(2,4,0,0));water.SetVector("_UV4Normal02",new Vector4(1,2,0,0));EditorUtility.SetDirty(water);
            BuildRiver(river);DressRiver(river);
        }
        if(root.Find("Детали лагеря — гравий у оград")==null){CampMagicAuthoring.RebuildPlaza(magic);DressCamp(root,magic);}
        var ambience=root.GetComponent<CampAmbience>();Undo.RecordObject(ambience,"Живой свет лагеря");
        ambience.FireVariation=.22f;ambience.LampVariation=.09f;ambience.FlagStrength=1;
        BakeFlags(root);PersistCampfire(root);
        EditorUtility.SetDirty(ambience);EditorUtility.SetDirty(river);river.Refresh();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);Undo.CollapseUndoOperations(group);
        Inspect();Debug.Log("[camp-finish] Installed authored river, dense plaza, anchored cloth and ambient lights.");
    }
    static GameObject New(string name,Transform parent)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(go,name);return go;
    }
    static void SetWorldScale(Transform t,Vector3 size)
    {
        var scale=t.parent!=null?t.parent.lossyScale:Vector3.one;t.localScale=new Vector3(size.x/scale.x,size.y/scale.y,size.z/scale.z);
    }
    static Material CopyMaterial(string source,string name)
    {
        string path=Folder+"/"+name+".mat";var result=AssetDatabase.LoadAssetAtPath<Material>(path);if(result!=null)return result;
        var original=AssetDatabase.LoadAssetAtPath<Material>(source);if(original==null)throw new InvalidOperationException("Нет материала "+source);
        result=new Material(original){name=name};AssetDatabase.CreateAsset(result,path);return result;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        string path=Folder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);mesh.name=name;
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        Undo.RegisterCompleteObjectUndo(saved,"Геометрия берега");
        // CopySerialized обновлял данные на диске, но оставлял старые буферы меша в открытой сцене.
        saved.Clear();saved.indexFormat=mesh.indexFormat;saved.name=name;saved.vertices=mesh.vertices;saved.normals=mesh.normals;saved.tangents=mesh.tangents;saved.colors=mesh.colors;
        for(int channel=0;channel<8;channel++){var values=new List<Vector4>();mesh.GetUVs(channel,values);if(values.Count>0)saved.SetUVs(channel,values);}
        saved.subMeshCount=mesh.subMeshCount;for(int i=0;i<mesh.subMeshCount;i++)saved.SetIndices(mesh.GetIndices(i),mesh.GetTopology(i),i,false);
        saved.bounds=mesh.bounds;saved.UploadMeshData(false);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;
    }
    static void MeshObject(string name,Transform parent,Mesh mesh,Material material)
    {
        var child=parent.Find(name);var go=child!=null?child.gameObject:New(name,parent);
        // Все четыре меша вычисляются в системе русла, иначе вырез земли расходится с берегом.
        Undo.RecordObject(go.transform,"Совместить меш с руслом");go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
        var filter=go.GetComponent<MeshFilter>();if(filter==null)filter=Undo.AddComponent<MeshFilter>(go);
        Undo.RecordObject(filter,"Меш русла");filter.sharedMesh=SaveMesh(mesh,name);EditorUtility.SetDirty(filter);
        var renderer=go.GetComponent<MeshRenderer>();if(renderer==null)renderer=Undo.AddComponent<MeshRenderer>(go);
        if(renderer.sharedMaterial==null){Undo.RecordObject(renderer,"Материал русла");renderer.sharedMaterial=material;EditorUtility.SetDirty(renderer);}
        renderer.shadowCastingMode=ShadowCastingMode.Off;
    }
    public static void Rebuild(CampRiver river)
    {
        if(river==null)return;Backup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Изменение русла");
        BuildRiver(river);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);Undo.CollapseUndoOperations(group);SceneView.RepaintAll();
    }
    static void Validate(CampRiver river)
    {
        if(river.SourceGround==null || river.Ground==null || river.Contour==null || river.Contour.Length<2 || river.Contour.Length>32)throw new InvalidOperationException("Нужны земля и 2–32 точки русла.");
        for(int i=1;i<river.Contour.Length;i++)if(river.Contour[i].x-river.Contour[i-1].x<.1f)throw new InvalidOperationException("Точки должны идти слева направо с промежутком от 0.1 м.");
        if(river.Width<1 || river.BankWidth<.3f || river.WaterLevel>=-.1f)throw new InvalidOperationException("Ширина от 1 м, берег от 0.3 м, уровень воды ниже -0.1 м.");
    }
    static void BuildRiver(CampRiver river)
    {
        Validate(river);Undo.FlushUndoRecordObjects();Undo.RecordObject(river,"Контур реки");Undo.RecordObject(river.Ground,"Земля у берега");
        river.BakeShape();BakeFoliageBoundary(river);
        // Объекты сохраняют идентичность: Undo не оставляет владельцу ссылку на удалённый контейнер.
        if(river.Geometry==null)river.Geometry=New("Русло — сохранённые меши",river.transform).transform;
        Undo.RecordObject(river.Geometry,"Совместить геометрию русла");river.Geometry.localPosition=Vector3.zero;river.Geometry.localRotation=Quaternion.identity;river.Geometry.localScale=Vector3.one;
        river.Ground.sharedMesh=SaveMesh(ClipGround(river),"Camp surface — речной вырез");EditorUtility.SetDirty(river.Ground);
        var land=CopyMaterial("Assets/Resources/Environment/Camp/ground/Study/CampSurface.mat","River bank");
        Undo.RecordObject(land,"Материал берега");
        var groundMaterial=river.Ground.GetComponent<Renderer>().sharedMaterial;
        land.shader=groundMaterial.shader;land.CopyPropertiesFromMaterial(groundMaterial);land.SetFloat("_IsRiverBank",1);EditorUtility.SetDirty(land);
        // Берег использует тот же материал грунта и мировую карту покраски.
        var bed=CopyMaterial("Assets/Resources/Environment/Camp/ground/Study/Trail stones.mat","River bed");bed.SetColor("_BaseColor",new Color(.32f,.39f,.32f));EditorUtility.SetDirty(bed);
        MeshObject("Ближний берег",river.Geometry,Ribbon(river,0),land);
        MeshObject("Дно реки",river.Geometry,Ribbon(river,1),bed);
        MeshObject("Дальний берег",river.Geometry,Ribbon(river,2),land);
        MeshObject("Лесная вода",river.Geometry,Ribbon(river,3),river.WaterMaterial);
        river.Refresh();EditorUtility.SetDirty(river);
    }
    static void BakeFoliageBoundary(CampRiver river)
    {
        const string path=Folder+"/Foliage boundary.asset";const int width=1024;
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null){texture=new Texture2D(width,1,TextureFormat.RFloat,false,true){name="Foliage boundary",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};AssetDatabase.CreateAsset(texture,path);}
        Undo.RegisterCompleteObjectUndo(texture,"Граница травы вдоль реки");var pixels=new float[width];
        float min=river.BakedLandContour[0].x,max=river.BakedLandContour[river.BakedLandContour.Length-1].x;
        for(int i=0;i<width;i++)pixels[i]=river.LandEdge(Mathf.Lerp(min,max,i/(float)(width-1)));
        texture.SetPixelData(pixels,0);texture.Apply(false,false);EditorUtility.SetDirty(texture);river.FoliageBoundary=texture;
    }
    struct Vertex
    {
        public Vector3 p,n;public Vector4 tangent,u0,u1,u2,u3;public Color color;
        public static Vertex Lerp(Vertex a,Vertex b,float t)=>new Vertex {p=Vector3.LerpUnclamped(a.p,b.p,t),n=Vector3.LerpUnclamped(a.n,b.n,t),tangent=Vector4.LerpUnclamped(a.tangent,b.tangent,t),u0=Vector4.LerpUnclamped(a.u0,b.u0,t),u1=Vector4.LerpUnclamped(a.u1,b.u1,t),u2=Vector4.LerpUnclamped(a.u2,b.u2,t),u3=Vector4.LerpUnclamped(a.u3,b.u3,t),color=Color.LerpUnclamped(a.color,b.color,t)};
    }
    static List<Vertex> Clip(List<Vertex> input,Func<Vertex,float> distance)
    {
        var output=new List<Vertex>();for(int i=0;i<input.Count;i++)
        {
            var a=input[i];var b=input[(i+1)%input.Count];float da=distance(a),db=distance(b);
            if(da>=0)output.Add(a);if((da>=0)!=(db>=0))output.Add(Vertex.Lerp(a,b,da/(da-db)));
        }
        return output;
    }
    static Mesh ClipGround(CampRiver river)
    {
        var source=river.SourceGround;var p=source.vertices;var n=source.normals;var t=source.tangents;var c=source.colors;
        var uv=new List<Vector4>[4];for(int i=0;i<4;i++){uv[i]=new List<Vector4>();source.GetUVs(i,uv[i]);}
        var vertices=new Vertex[p.Length];
        for(int i=0;i<p.Length;i++)vertices[i]=new Vertex {p=p[i],n=n.Length==p.Length?n[i]:Vector3.up,tangent=t.Length==p.Length?t[i]:new Vector4(1,0,0,1),color=c.Length==p.Length?c[i]:Color.white,u0=uv[0].Count==p.Length?uv[0][i]:Vector4.zero,u1=uv[1].Count==p.Length?uv[1][i]:Vector4.zero,u2=uv[2].Count==p.Length?uv[2][i]:Vector4.zero,u3=uv[3].Count==p.Length?uv[3][i]:Vector4.zero};
        Matrix4x4 matrix=river.transform.worldToLocalMatrix*river.Ground.transform.localToWorldMatrix;
        var result=new List<Vertex>();var indices=new List<int>();
        void Add(List<Vertex> polygon){for(int j=1;j+1<polygon.Count;j++){indices.Add(result.Count);result.Add(polygon[0]);indices.Add(result.Count);result.Add(polygon[j]);indices.Add(result.Count);result.Add(polygon[j+1]);}}
        var triangles=source.triangles;
        for(int k=0;k<triangles.Length;k+=3)
        {
            var polygon=new List<Vertex>{vertices[triangles[k]],vertices[triangles[k+1]],vertices[triangles[k+2]]};
            Vector3 a=matrix.MultiplyPoint3x4(polygon[0].p),b=matrix.MultiplyPoint3x4(polygon[1].p),d=matrix.MultiplyPoint3x4(polygon[2].p);
            float minX=Mathf.Min(a.x,b.x,d.x),maxX=Mathf.Max(a.x,b.x,d.x);
            float low=Mathf.Min(river.LandEdge(minX),river.LandEdge(maxX)),high=Mathf.Max(river.LandEdge(minX),river.LandEdge(maxX));
            // Добавляем внутренние узлы, чтобы треугольник не перескочил изгиб берега.
            foreach(var point in river.BakedLandContour)if(point.x>minX && point.x<maxX){low=Mathf.Min(low,point.y);high=Mathf.Max(high,point.y);}
            if(Mathf.Min(a.z,b.z,d.z)>=high){Add(polygon);continue;}
            if(Mathf.Max(a.z,b.z,d.z)<low)continue;
            var cuts=new List<float>{minX};foreach(var point in river.BakedLandContour)if(point.x>minX && point.x<maxX)cuts.Add(point.x);cuts.Add(maxX);
            for(int j=0;j<cuts.Count-1;j++)
            {
                float left=cuts[j],right=cuts[j+1],edge=river.LandEdge(left),slope=(river.LandEdge(right)-edge)/Mathf.Max(.0001f,right-left);
                var clipped=Clip(polygon,v=>matrix.MultiplyPoint3x4(v.p).x-left);
                clipped=Clip(clipped,v=>right-matrix.MultiplyPoint3x4(v.p).x);
                clipped=Clip(clipped,v=>{var q=matrix.MultiplyPoint3x4(v.p);return q.z-edge-slope*(q.x-left);});Add(clipped);
            }
        }
        var mesh=new Mesh{indexFormat=IndexFormat.UInt32};var rp=new List<Vector3>();var rn=new List<Vector3>();var rt=new List<Vector4>();var rc=new List<Color>();var ru=new[]{new List<Vector4>(),new List<Vector4>(),new List<Vector4>(),new List<Vector4>()};
        // Исходная поверхность — непрерывная сетка без UV-швов; общие вершины не дублируются для каждого треугольника.
        indices.Clear();var unique=new Dictionary<Vector3,int>();
        foreach(var v in result)
        {
            if(!unique.TryGetValue(v.p,out int index)){index=rp.Count;unique.Add(v.p,index);rp.Add(v.p);rn.Add(v.n);rt.Add(v.tangent);rc.Add(v.color);ru[0].Add(v.u0);ru[1].Add(v.u1);ru[2].Add(v.u2);ru[3].Add(v.u3);}
            indices.Add(index);
        }
        mesh.SetVertices(rp);mesh.SetNormals(rn);mesh.SetTangents(rt);mesh.SetColors(rc);for(int i=0;i<4;i++)if(uv[i].Count>0)mesh.SetUVs(i,ru[i]);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
    }
    static float GroundHeight(CampRiver river,Vector3 world,Vector3[] vertices=null)
    {
        var mesh=river.SourceGround;var p=river.Ground.transform.InverseTransformPoint(world);var bounds=mesh.bounds;int side=Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount));
        float x=Mathf.Clamp01((p.x-bounds.min.x)/bounds.size.x)*(side-1),z=Mathf.Clamp01((p.z-bounds.min.z)/bounds.size.z)*(side-1);
        int ix=Mathf.Min((int)x,side-2),iz=Mathf.Min((int)z,side-2);var v=vertices??mesh.vertices;int n=iz*side+ix;float tx=x-ix,tz=z-iz;
        float y=tx+tz<=1?v[n].y+tx*(v[n+1].y-v[n].y)+tz*(v[n+side].y-v[n].y):v[n+side+1].y+(1-tx)*(v[n+side].y-v[n+side+1].y)+(1-tz)*(v[n+1].y-v[n+side+1].y);
        return river.transform.InverseTransformPoint(river.Ground.transform.TransformPoint(new Vector3(p.x,y,p.z))).y;
    }
    static Mesh Ribbon(CampRiver river,int kind)
    {
        var points=new List<Vector3>();var uv=new List<Vector2>();var uv4=new List<Vector4>();var colors=new List<Color>();var triangles=new List<int>();
        var groundVertices=river.SourceGround.vertices;float length=0;
        for(int row=0;row<river.BakedCentres.Length;row++)
        {
            Vector2 centre=river.BakedCentres[row],normal=river.BakedNormals[row];float half=river.Width*.5f;
            if(row>0)length+=Vector2.Distance(centre,river.BakedCentres[row-1]);
            int columns=kind==0?6:kind==2?7:2;
            for(int j=0;j<columns;j++)
            {
                float f=j/(float)(columns-1),y,wet=f;Vector2 flat;
                float ripple=Mathf.Sin(length*1.9f)*.025f+Mathf.Sin(length*.77f+1.2f)*.055f;
                if(kind==0)
                {
                    wet=Mathf.Max(0,j-1)/4f;
                    flat=centre+normal*(half+(river.BankWidth+.06f)*(1-wet)+ripple*wet);
                    if(j==0)flat+=normal*MissingLandWidth(river,flat,normal);
                    y=Mathf.Lerp(GroundHeight(river,river.transform.TransformPoint(new Vector3(flat.x,0,flat.y)),groundVertices)+.008f,river.WaterLevel-.065f,Mathf.SmoothStep(0,1,wet));
                }
                else if(kind==1){flat=centre+normal*Mathf.Lerp(half,-half,f);y=river.WaterLevel-.4f;}
                else if(kind==2)
                {
                    float distance=j<=4?(river.BankWidth+.35f)*j/4f:j==5?4:26;
                    wet=1-Mathf.Clamp01(distance/(river.BankWidth+.35f));flat=centre-normal*(half+distance-ripple*(1-Mathf.Min(1,distance/river.BankWidth)));
                    y=Mathf.Lerp(river.WaterLevel-.065f,.06f,Mathf.SmoothStep(0,1,1-wet));
                }
                else{flat=centre+normal*(Mathf.Lerp(half+.075f,-half-.075f,f)+ripple);y=river.WaterLevel;}
                var p=new Vector3(flat.x,y,flat.y);points.Add(p);Vector3 world=river.transform.TransformPoint(p);
                uv.Add(kind==0 || kind==2?new Vector2(length*.075f,wet):kind==3?new Vector2(length*.075f,f):new Vector2(world.x*.12f,world.z*.12f));uv4.Add(new Vector4(length*.075f,f,0,0));colors.Add(Color.white);
                if(row>0 && j>0){int a=points.Count-1;triangles.Add(a-columns-1);triangles.Add(a-1);triangles.Add(a-columns);triangles.Add(a-columns);triangles.Add(a-1);triangles.Add(a);}
            }
        }
        var mesh=new Mesh();mesh.SetVertices(points);mesh.SetUVs(0,uv);mesh.SetUVs(3,uv4);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
    }
    static float MissingLandWidth(CampRiver river,Vector2 p,Vector2 normal)
    {
        var at=river.Ground.transform.InverseTransformPoint(river.transform.TransformPoint(new Vector3(p.x,0,p.y)));var bounds=river.SourceGround.bounds;
        if(at.x>=bounds.min.x && at.x<=bounds.max.x && at.z>=bounds.min.z && at.z<=bounds.max.z)return 0;
        var direction=river.Ground.transform.InverseTransformVector(river.transform.TransformVector(new Vector3(normal.x,0,normal.y)));
        float entry=0,exit=float.MaxValue;
        bool Slab(float value,float delta,float min,float max)
        {
            if(Mathf.Abs(delta)<.0001f)return value>=min && value<=max;
            float a=(min-value)/delta,b=(max-value)/delta;entry=Mathf.Max(entry,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));return entry<=exit;
        }
        // У поворота за деревом исходная квадратная земля заканчивается: продолжаем только недостающую полосу до её края.
        if(Slab(at.x,direction.x,bounds.min.x,bounds.max.x) && Slab(at.z,direction.z,bounds.min.z,bounds.max.z))return Mathf.Min(26,entry+.15f);
        return 20;
    }
    static void DressRiver(CampRiver river)
    {
        river.Dressing=New("Берег — камни и растения вручную",river.transform).transform;
        var magic=Object.FindAnyObjectByType<CampMagicCircle>();var source=magic.PlazaRoot.GetComponentInChildren<MeshFilter>();
        var stone=CopyMaterial("Assets/Resources/Environment/Camp/ground/Study/Trail stones.mat","Bank stones");stone.SetColor("_BaseColor",new Color(.76f,.82f,.79f));stone.SetTexture("_BaseMap",magic.PavingTexture);EditorUtility.SetDirty(stone);
        var random=new System.Random(1841);
        float[] clusters={-29,-21,-14,-7,-1,7,14,22,30};int i=0;
        foreach(float cluster in clusters)for(int j=0;j<6;j++)
        {
            float x=cluster+((float)random.NextDouble()-.5f)*3.2f;bool far=j==5;
            float z=river.CentreAt(x)+(far?-1:1)*(river.Width*.5f+((float)random.NextDouble()-.25f)*river.BankWidth);
            var go=New("Береговой камень "+i++,river.Dressing);go.transform.localPosition=new Vector3(x,far?-.38f:-.20f,z);go.transform.localRotation=Quaternion.Euler(0,i*137.5f,0);
            float size=j==0?1.4f:.27f+(float)random.NextDouble()*.65f;go.transform.localScale=new Vector3(size,2.8f+size*2.5f,size*.85f);
            go.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;go.AddComponent<MeshRenderer>().sharedMaterial=stone;
        }
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        if(grass!=null)
        foreach(float cluster in new[]{-24f,-16f,-9f,-4f,5f,12f,19f,27f})for(int j=0;j<3;j++)
        {
            float x=cluster+j*.42f;float z=river.CentreAt(x)+river.Width*.5f+river.BankWidth*(.35f+j*.12f);
            var plant=(GameObject)PrefabUtility.InstantiatePrefab(grass,river.Dressing);Undo.RegisterCreatedObjectUndo(plant,"Прибрежная трава");
            plant.name="Прибрежная осока "+i++;plant.transform.localPosition=new Vector3(x,-.12f,z);plant.transform.localRotation=Quaternion.Euler(0,i*117,0);plant.transform.localScale=Vector3.one*(.72f+j*.16f);
            foreach(var collider in plant.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            foreach(var renderer in plant.GetComponentsInChildren<MeshRenderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
    public static void Polish()
    {
        string backup=Backup();var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        Undo.RecordObject(river,"Изгибы лесной реки");
        river.Contour=new[]{new Vector2(-100,0),new Vector2(-26,1.1f),new Vector2(-19,-.9f),new Vector2(-14,-.8f),new Vector2(-10,.25f),new Vector2(-6,.1f),new Vector2(-2,-.85f),new Vector2(3,-.4f),new Vector2(8,1.05f),new Vector2(13,.75f),new Vector2(19,-.5f),new Vector2(26,.5f),new Vector2(100,0)};
        BuildRiver(river);if(river.Dressing!=null)Undo.DestroyObjectImmediate(river.Dressing.gameObject);DressRiver(river);
        var data=AssetDatabase.LoadAssetAtPath<CampPathPaintData>(CampPathPainter.DataPath);var bounds=river.Ground.GetComponent<Renderer>().sharedMaterial.GetVector("_SurfaceBounds");
        foreach(var asset in new Object[]{data,data.Surface,data.ClearedFoliage}){AssetDatabase.SaveAssetIfDirty(asset);File.Copy(AssetDatabase.GetAssetPath(asset),Path.Combine(backup,asset.name+".before"),true);Undo.RegisterCompleteObjectUndo(asset,"Каменная фактура школы");}
        var pixels=data.Surface.GetPixels32();
        for(int z=0;z<data.Height;z++)for(int x=0;x<data.Width;x++)
        {
            Vector3 p=new Vector3(bounds.x+x*bounds.z/(data.Width-1),0,bounds.y+z*bounds.w/(data.Height-1));float edge=magic.PlazaEdge(p);if(edge<-.25f)continue;
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.25f,.35f,edge));int index=z*data.Width+x;
            pixels[index].g=(byte)Mathf.Lerp(pixels[index].g,255,weight);data.Path[index]=(byte)Mathf.Max(data.Path[index],Mathf.RoundToInt(weight*255));
        }
        data.Surface.SetPixels32(pixels);data.Surface.Apply(false,false);CampPathPainter.Apply(data);EditorUtility.SetDirty(data);CampPathPainter.Save(data);
        foreach(var renderer in magic.transform.parent.GetComponentsInChildren<MeshRenderer>())
        {
            if(renderer.GetComponentInParent<CampRiver>()!=null || renderer.bounds.size.magnitude>5 || !renderer.name.ToLowerInvariant().Contains("grass") || !river.ContainsBlocked(renderer.bounds.center))continue;
            Undo.RecordObject(renderer,"Трава вне русла");renderer.enabled=false;EditorUtility.SetDirty(renderer);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);Inspect();
    }
    static void DressCamp(Transform root,CampMagicCircle magic)
    {
        var go=New("Детали лагеря — гравий у оград",root);go.AddComponent<CampSceneryDecoration>();
        var stone=magic.PlazaRoot.GetComponentInChildren<MeshFilter>();var material=stone.GetComponent<MeshRenderer>().sharedMaterial;
        var random=new System.Random(3041);int count=0;
        foreach(var t in root.GetComponentsInChildren<Transform>())
        {
            string name=t.name.ToLowerInvariant();if(!name.Contains("fence") || t.GetComponent<MeshFilter>()==null || t.IsChildOf(magic.transform))continue;
            if(count>=48)break;
            for(int j=0;j<2;j++)
            {
                var pebble=New("Гравий у ограды "+count++,go.transform);var at=t.position+t.right*((float)random.NextDouble()-.5f)*.65f+t.forward*.15f;at.y=magic.HeightAt(at)+.018f;
                pebble.transform.position=at;pebble.transform.rotation=Quaternion.Euler(0,count*137.5f,0);SetWorldScale(pebble.transform,new Vector3(.17f,.5f,.23f));
                pebble.AddComponent<MeshFilter>().sharedMesh=stone.sharedMesh;pebble.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
        }
    }
    static void BakeFlags(Transform root)
    {
        var shader=Shader.Find("Game/Camp Breeze Lit");int count=0;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(filter.sharedMesh==null)continue;string path=AssetDatabase.GetAssetPath(filter.sharedMesh);
            if(filter.GetComponentInParent<CampMagicCircle>()!=null && path.EndsWith(".asset") && (path.EndsWith("Fire.asset") || path.EndsWith("Ice.asset") || path.EndsWith("Alchemy.asset")))
            {
                var bends=new List<Vector4>();filter.sharedMesh.GetUVs(3,bends);bool changed=false;
                for(int i=0;i<bends.Count;i++){var v=bends[i];if(v.w>0){v.w=-v.w;bends[i]=v;changed=true;}}
                if(changed){Undo.RegisterCompleteObjectUndo(filter.sharedMesh,"Ветер флагов школы");filter.sharedMesh.SetUVs(3,bends);EditorUtility.SetDirty(filter.sharedMesh);}continue;
            }
            string name=filter.name.ToLowerInvariant();if(!name.Contains("banner") && !name.Contains("flag"))continue;
            if(path.StartsWith(Folder))continue;
            var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null || renderer.sharedMaterial==null)continue;
            var mesh=Object.Instantiate(filter.sharedMesh);var p=mesh.vertices;var uv=mesh.uv;var material=renderer.sharedMaterial;
            Texture2D pixels=Readable(material.mainTexture as Texture2D);var bounds=mesh.bounds;var bends2=new List<Vector4>();int weighted=0;
            // Маска проверяется по ткани и высоте; дерево, металл и верхнее крепление закреплены.
            for(int i=0;i<p.Length;i++)
            {
                Color color=pixels!=null && uv.Length==p.Length?pixels.GetPixelBilinear(uv[i].x,uv[i].y):Color.gray;
                bool cloth=(color.r>color.g*1.45f && color.r>color.b*1.45f) || (color.b>color.r*1.45f && color.b>color.g*1.15f);
                float weight=cloth && p[i].y>bounds.min.y+bounds.size.y*.3f?Mathf.Clamp01((bounds.max.y-p[i].y)/bounds.size.y)*-.065f:0;
                bends2.Add(new Vector4(p[i].x,bounds.max.y,p[i].z,weight));if(weight!=0)weighted++;
            }
            if(pixels!=null)Object.DestroyImmediate(pixels);if(weighted==0){Object.DestroyImmediate(mesh);continue;}
            mesh.SetUVs(3,bends2);bounds.Expand(.15f);mesh.bounds=bounds;mesh=SaveMesh(mesh,"Flag "+count);
            var mat=new Material(material){name="Flag breeze "+count};mat.shader=shader;AssetDatabase.CreateAsset(mat,Folder+"/Flag "+count+".mat");
            Undo.RecordObject(filter,"Закреплённый флаг");Undo.RecordObject(renderer,"Материал флага");filter.sharedMesh=mesh;renderer.sharedMaterial=mat;EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);count++;
        }
        Debug.Log("[camp-finish] additional flags="+count);
    }
    static Texture2D Readable(Texture2D source)
    {
        if(source==null)return null;var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;
        try{Graphics.Blit(source,rt);RenderTexture.active=rt;var texture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();return texture;}
        finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
    }
    static void PersistCampfire(Transform root)
    {
        CampFlameProView.Install(root.Find("Campfire"));var fire=root.GetComponentInChildren<CampFlameProView>(true);if(fire==null)return;
        var filter=fire.GetComponent<MeshFilter>();if(filter==null || EditorUtility.IsPersistent(filter.sharedMesh))return;
        // Install создаёт временный меш; отдельная сохранённая копия переживает выход из редактора.
        filter.sharedMesh=SaveMesh(Object.Instantiate(filter.sharedMesh),"Campfire flame");EditorUtility.SetDirty(filter);
    }
    public static void Inspect()
    {
        var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();if(magic==null)return;
        string output=Path.Combine(Repo,"artifacts/camp-finish-inspect");Directory.CreateDirectory(output);
        EditorSceneManager.SaveScene(magic.gameObject.scene,Path.Combine(output,"current.unity"),true);
        var text=new System.Text.StringBuilder();text.AppendLine("time="+DateTime.Now.ToString("O"));
        foreach(var renderer in magic.transform.parent.GetComponentsInChildren<MeshRenderer>())
            if(renderer.GetComponentInParent<CampRiver>()==null && renderer.bounds.size.y>3.5f)
                text.AppendLine($"landmark={renderer.name} parent={renderer.transform.parent.name} position={renderer.transform.position} bounds={renderer.bounds} riverLocal={river.transform.InverseTransformPoint(renderer.bounds.center)} mesh={AssetDatabase.GetAssetPath(renderer.GetComponent<MeshFilter>().sharedMesh)}");
        foreach(var biome in magic.GetComponentsInChildren<CampMicrobiome>())
        {
            text.AppendLine($"biome={biome.name} pos={biome.transform.position} scale={biome.transform.lossyScale}");
            foreach(var renderer in biome.GetComponentsInChildren<Renderer>())text.AppendLine($" biomeRenderer={renderer.name} active={renderer.enabled && renderer.gameObject.activeInHierarchy} bounds={renderer.bounds} shader={renderer.sharedMaterial.shader.name}");
        }
        if(river!=null)text.AppendLine($"river={river.transform.position} rotation={river.transform.eulerAngles} source={river.SourceGround.vertexCount} cut={river.Ground.sharedMesh.vertexCount}");
        if(river!=null)
        {
            long total=0;var meshes=new Dictionary<Mesh,int>();
            foreach(var filter in river.GetComponentsInChildren<MeshFilter>()){var mesh=filter.sharedMesh;if(!meshes.ContainsKey(mesh))meshes[mesh]=0;meshes[mesh]++;}
            foreach(var pair in meshes){long triangles=0;for(int i=0;i<pair.Key.subMeshCount;i++)triangles+=pair.Key.GetIndexCount(i)/3;total+=triangles*pair.Value;text.AppendLine($"riverCost={pair.Key.name} copies={pair.Value} vertices={pair.Key.vertexCount} trianglesEach={triangles}");}
            text.AppendLine("riverTotalTriangles="+total);
        }
        if(river!=null)foreach(var filter in river.Geometry.GetComponentsInChildren<MeshFilter>())
        {
            var v=filter.sharedMesh.vertices;int mid=0;for(int j=1;j<v.Length;j++)if(Mathf.Abs(v[j].x)<Mathf.Abs(v[mid].x))mid=j;
            var mat=filter.GetComponent<Renderer>().sharedMaterial;
            text.AppendLine($"riverMesh={filter.name} pos={filter.transform.position} local={filter.transform.localPosition} scale={filter.transform.lossyScale} vertices={v.Length} middle={v[mid]} uv={filter.sharedMesh.uv[mid]} bank={(mat.HasProperty("_RiverBank")?mat.GetFloat("_RiverBank"):-1)} shader={mat.shader.name}");
        }
        foreach(var renderer in magic.transform.parent.GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.name.ToLowerInvariant().Contains("flag") || renderer.name.ToLowerInvariant().Contains("banner"))text.AppendLine($"flag={renderer.name} mesh={AssetDatabase.GetAssetPath(renderer.GetComponent<MeshFilter>()?.sharedMesh)} bounds={renderer.bounds}");
        File.WriteAllText(Path.Combine(output,"report.txt"),text.ToString());
        Render(magic.Centre.position+Vector3.up*.6f,9.4f,Path.Combine(output,"school-river.png"));
        Render(new Vector3(-1,0,0),17,Path.Combine(output,"camp.png"));
        if(river!=null)
        {
            Render(river.transform.TransformPoint(new Vector3(0,0,1.8f)),10.8f,Path.Combine(output,"river-both-banks.png"));
            Render(river.transform.TransformPoint(new Vector3(7,0,11)),15,Path.Combine(output,"river-turn.png"));
            var all=river.GetComponentsInChildren<Transform>();var layers=new int[all.Length];for(int i=0;i<all.Length;i++){layers[i]=all[i].gameObject.layer;all[i].gameObject.layer=30;}
            try{Render(magic.Centre.position+Vector3.up*.6f,9.4f,Path.Combine(output,"river-only.png"),1<<30);}
            finally{for(int i=0;i<all.Length;i++)all[i].gameObject.layer=layers[i];}
        }
    }
    static void Render(Vector3 focus,float size,string path,int mask=-1)
    {
        var go=new GameObject("Camp finish review camera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();if(Camera.main!=null)camera.CopyFrom(Camera.main);
        camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size;camera.aspect=1.6f;camera.transform.rotation=Quaternion.Euler(48,35,0);camera.transform.position=focus-camera.transform.forward*80;camera.farClipPlane=240;
        camera.cullingMask=mask;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.requiresDepthTexture=true;
        var rt=RenderTexture.GetTemporary(1600,1000,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;Texture2D pixels=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);if(pixels!=null)Object.DestroyImmediate(pixels);Object.DestroyImmediate(go);}
    }
    static Vector2[] CompactContour(Vector3 tree)
    {
        return new[]{new Vector2(-44,-.65f),new Vector2(-30,.3f),new Vector2(-19,.8f),new Vector2(-11,-.5f),new Vector2(-3,.45f),new Vector2(5,.4f),
            new Vector2(tree.x-7,2.3f),new Vector2(tree.x-1,tree.z-10),new Vector2(tree.x+4.8f,tree.z-3.5f),new Vector2(tree.x+7.6f,tree.z+4),new Vector2(tree.x+10,tree.z+14),new Vector2(tree.x+12.5f,tree.z+27)};
    }
    static void CompactRiver()
    {
        CampSceneAmbiencePreview.Stop();Backup();var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        Transform tree=null;foreach(var renderer in magic.transform.parent.GetComponentsInChildren<MeshRenderer>())if(renderer.name=="stylized+tree+3d+model (2)"){tree=renderer.transform;break;}
        if(tree==null)throw new InvalidOperationException("Не найдено большое дерево у торговца.");
        int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Компактная река за деревом торговца");Undo.RecordObject(river,"Узкое изогнутое русло");Undo.RecordObject(river.transform,"Берег перед школой");
        river.Width=2.4f;river.BankWidth=.75f;river.WaterLevel=-.30f;river.FlowSpeed=1.8f;
        river.Contour=CompactContour(river.transform.InverseTransformPoint(tree.position));river.BakeShape();
        float shift=float.MaxValue;
        foreach(var altar in new[]{magic.AlchemyAltar,magic.EarthAltar}){var p=river.transform.InverseTransformPoint(altar.position);shift=Mathf.Min(shift,p.z-3.4f-river.LandEdge(p.x));}
        river.transform.position+=river.transform.forward*shift;river.Contour=CompactContour(river.transform.InverseTransformPoint(tree.position));
        BuildRiver(river);
        if(river.Dressing!=null)Undo.DestroyObjectImmediate(river.Dressing.gameObject);river.Dressing=New("Берега — лёгкие камни и трава",river.transform).transform;
        var rocks=new[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock01.prefab"),AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock03_1.prefab")};
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        var grassMaterial=CopyMaterial(AssetDatabase.GetAssetPath(grass.GetComponentInChildren<MeshRenderer>().sharedMaterial),"River grass");Undo.RecordObject(grassMaterial,"Цвет прибрежной травы");grassMaterial.SetColor("_BaseColor",new Color(1.13f,1.12f,.78f));grassMaterial.enableInstancing=true;EditorUtility.SetDirty(grassMaterial);
        var groundVertices=river.SourceGround.vertices;var random=new System.Random(6317);int count=0;
        // Небольшие разрозненные группы остаются отдельными объектами для ручной расстановки.
        for(int row=6;row<river.BakedCentres.Length-6;row+=10)
        for(int side=0;side<2;side++)
        {
            var centre=river.BakedCentres[row];var normal=river.BakedNormals[row];var tangent=new Vector2(normal.y,-normal.x);
            for(int j=0;j<3;j++)
            {
                bool plant=j==2;float distance=plant?river.BankWidth+.14f:.15f+(float)random.NextDouble()*river.BankWidth*.7f;
                Vector2 flat=centre+normal*(side==0?1:-1)*(river.Width*.5f+distance)+tangent*((float)random.NextDouble()-.5f)*1.2f;
                var at=new Vector3(flat.x,0,flat.y);
                float f=1-Mathf.Clamp01(distance/river.BankWidth);
                float y=side==0?Mathf.Lerp(GroundHeight(river,river.transform.TransformPoint(at),groundVertices)+.008f,river.WaterLevel-.065f,Mathf.SmoothStep(0,1,f)):Mathf.Lerp(river.WaterLevel-.065f,.06f,Mathf.SmoothStep(0,1,Mathf.Clamp01(distance/(river.BankWidth+.35f))));
                float size=plant?.72f:j==0?.57f:.26f;
                AddDressingMesh(plant?grass:rocks[count%2],river.Dressing,(side==0?"Ближний":"Дальний")+" берег — "+(plant?"трава ":"камень ")+count,new Vector3(flat.x,y-(plant?.015f:.06f),flat.y),new Vector3(size,plant?.42f:size*.64f,size*.8f),count++*137.5f);
                if(plant){var renderer=river.Dressing.GetChild(river.Dressing.childCount-1).GetComponent<MeshRenderer>();renderer.sharedMaterial=grassMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;}
            }
        }
        river.Refresh();EditorUtility.SetDirty(river);EditorUtility.SetDirty(river.transform);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);Undo.CollapseUndoOperations(undo);
        Inspect();Debug.Log("[camp-finish] compact river saved, tree="+tree.position+", dressing="+count);
    }
    static void LightenRiver()
    {
        Backup();var river=Object.FindAnyObjectByType<CampRiver>();var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab").GetComponentInChildren<MeshFilter>();
        int count=0;
        foreach(var filter in river.Dressing.GetComponentsInChildren<MeshFilter>())
        {
            if(filter.sharedMesh.vertexCount<100000)continue;
            var bounds=filter.GetComponent<Renderer>().bounds;Undo.RecordObject(filter,"Лёгкая прибрежная трава");Undo.RecordObject(filter.transform,"Масштаб лёгкой травы");Undo.RecordObject(filter.GetComponent<Renderer>(),"Материал лёгкой травы");
            filter.sharedMesh=source.sharedMesh;filter.GetComponent<Renderer>().sharedMaterials=source.GetComponent<Renderer>().sharedMaterials;
            var size=source.sharedMesh.bounds.size;filter.transform.localScale=new Vector3(.75f/size.x,.4f/size.y,.65f/size.z);
            filter.transform.position=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)-filter.transform.rotation*Vector3.Scale(filter.transform.lossyScale,new Vector3(source.sharedMesh.bounds.center.x,source.sharedMesh.bounds.min.y,source.sharedMesh.bounds.center.z));
            filter.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;EditorUtility.SetDirty(filter);EditorUtility.SetDirty(filter.transform);EditorUtility.SetDirty(filter.GetComponent<Renderer>());count++;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);Inspect();Debug.Log("[camp-finish] lightweight river grass replacements="+count);
    }
    static void FinishRiver()
    {
        CampSceneAmbiencePreview.Stop();Backup();var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Готовая лесная река");Undo.RecordObject(river,"Плавное русло");Undo.RecordObject(river.transform,"Река за оградой школы");
        river.Width=5.2f;river.BankWidth=1.9f;river.WaterLevel=-.42f;river.FlowSpeed=.42f;
        river.Contour=new[]{new Vector2(-100,0),new Vector2(-32,.4f),new Vector2(-23,.9f),new Vector2(-15,-.65f),new Vector2(-7,-.35f),new Vector2(1,.55f),new Vector2(9,.2f),new Vector2(18,-.8f),new Vector2(28,.4f),new Vector2(100,0)};
        // Кромка проходит снаружи передних алтарей и ограды; авторская площадка остаётся целой.
        float shift=float.MaxValue;
        foreach(var altar in new[]{magic.AlchemyAltar,magic.EarthAltar})
        {
            var p=river.transform.InverseTransformPoint(altar.position);shift=Mathf.Min(shift,p.z-3.7f-river.LandEdge(p.x));
        }
        river.transform.position+=river.transform.forward*shift;
        BuildRiver(river);
        if(river.Dressing!=null)Undo.DestroyObjectImmediate(river.Dressing.gameObject);
        river.Dressing=New("Берега — камни и трава",river.transform).transform;
        var rocks=new[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock01.prefab"),AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Rock03_1.prefab")};
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        var random=new System.Random(7314);int count=0;
        for(int side=0;side<2;side++)
        foreach(float cluster in new[]{-35f,-28f,-21f,-14f,-6f,2f,10f,18f,26f,34f})
        {
            for(int j=0;j<4;j++)
            {
                float x=cluster+j*.43f+(float)random.NextDouble()*.5f;
                float fraction=j==0?.47f:.25f+(float)random.NextDouble()*.6f;
                float distance=river.BankWidth*fraction,z=river.CentreAt(x)+(side==0?1:-1)*(river.Width*.5f+distance);
                float y=side==0?Mathf.Lerp(GroundHeight(river,river.transform.TransformPoint(new Vector3(x,0,z)))+.008f,river.WaterLevel-.09f,Mathf.SmoothStep(0,1,1-fraction)):Mathf.Lerp(river.WaterLevel-.09f,.06f,Mathf.SmoothStep(0,1,distance/(river.BankWidth+.6f)));
                float size=j==0?.90f:.23f+(float)random.NextDouble()*.48f;
                AddDressingMesh(rocks[count%2],river.Dressing,(side==0?"Ближний":"Дальний")+" берег — камень "+count,new Vector3(x,y-.08f,z),new Vector3(size,size*.6f,size*.82f),count++*137.5f);
            }
            for(int j=0;j<4;j++)
            {
                float x=cluster-.9f+j*.58f,z=side==0?river.LandEdge(x)-.18f-j*.08f:river.CentreAt(x)-river.Width*.5f-3.5f-j*.35f;
                float y=side==0?GroundHeight(river,river.transform.TransformPoint(new Vector3(x,0,z)))-.02f:.035f;
                float scale=.64f+(float)random.NextDouble()*.45f;
                AddDressingMesh(grass,river.Dressing,(side==0?"Ближний":"Дальний")+" берег — трава "+count,new Vector3(x,y,z),new Vector3(scale,.4f,scale*.85f),count++*137.5f);
            }
        }
        Undo.RecordObject(river.WaterMaterial,"Спокойная лесная вода");river.WaterMaterial.SetColor("_DeepWaterColor",new Color(.11f,.32f,.34f));river.WaterMaterial.SetColor("_ShallowWaterColor",new Color(.29f,.48f,.37f));EditorUtility.SetDirty(river.WaterMaterial);
        EditorUtility.SetDirty(river);EditorUtility.SetDirty(river.transform);river.Refresh();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);Undo.CollapseUndoOperations(undo);
        Inspect();Debug.Log("[camp-finish] final river saved, bank shift="+shift);
    }
    static void Redesign()
    {
        CampSceneAmbiencePreview.Stop();Backup();
        var river=Object.FindAnyObjectByType<CampRiver>();var ambience=Object.FindAnyObjectByType<CampAmbience>();
        Undo.RecordObject(river,"Мягкий берег лесной реки");Undo.RecordObject(river.transform,"Сохранить край лагеря");
        float edge=river.Width*.5f+river.BankWidth;
        river.Width=4.8f;river.BankWidth=2.1f;river.WaterLevel=-.38f;
        river.transform.position+=river.transform.forward*(edge-river.Width*.5f-river.BankWidth);
        Undo.RecordObject(river.WaterMaterial,"Рисованная лесная вода");
        var water=river.WaterMaterial;water.shader=Shader.Find("Game/Camp Forest Water");water.renderQueue=2010;
        water.SetColor("_DeepWaterColor",new Color(.095f,.30f,.33f));water.SetColor("_ShallowWaterColor",new Color(.28f,.46f,.32f));water.SetColor("_FoamColor",new Color(.55f,.73f,.63f));EditorUtility.SetDirty(water);
        BuildRiver(river);
        if(river.Dressing!=null)Undo.DestroyObjectImmediate(river.Dressing.gameObject);
        river.Dressing=New("Берег — камни и трава лагеря",river.transform).transform;
        string rockFolder="Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/";
        var rocks=new[]{AssetDatabase.LoadAssetAtPath<GameObject>(rockFolder+"Rock01.prefab"),AssetDatabase.LoadAssetAtPath<GameObject>(rockFolder+"Rock03_1.prefab")};
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
        var random=new System.Random(2147);int count=0;
        foreach(float cluster in new[]{-27f,-20f,-13f,-5f,3.5f,11f,18f,26f})
        {
            for(int j=0;j<4;j++)
            {
                float x=cluster+j*.5f+(float)random.NextDouble()*.45f;
                float bank=j==0?.7f:.30f+(float)random.NextDouble()*.6f;
                float z=river.CentreAt(x)+river.Width*.5f+river.BankWidth*bank;
                float y=Mathf.Lerp(.03f,river.WaterLevel-.09f,Mathf.SmoothStep(0,1,1-bank));
                float size=j==0?1.05f:.3f+(float)random.NextDouble()*.45f;
                AddDressingMesh(rocks[count%2],river.Dressing,"Серый прибрежный камень "+count,new Vector3(x,y-.08f,z),new Vector3(size,size*.55f,size*.76f),count++*137.5f);
            }
            for(int j=0;j<2;j++)
            {
                float x=cluster-1+j*.48f,z=river.LandEdge(x)-.45f;
                AddDressingMesh(grass,river.Dressing,"Трава лагеря у воды "+count,new Vector3(x,-.015f,z),new Vector3(.66f,.36f,.58f),count++*137.5f);
            }
        }
        Undo.RecordObject(ambience,"Читаемые ветер и мерцание");ambience.FireVariation=.42f;ambience.LampVariation=.32f;ambience.FlagStrength=1.15f;ambience.PreviewInScene=true;
        EditorUtility.SetDirty(ambience);EditorUtility.SetDirty(river);EditorUtility.SetDirty(river.transform);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);
        Inspect();MotionReview();Debug.Log("[camp-finish] river redesign saved");
    }
    static void AddDressingMesh(GameObject source,Transform parent,string name,Vector3 position,Vector3 size,float angle)
    {
        if(source==null)throw new InvalidOperationException("Нет модели для "+name);
        var filter=source.GetComponentInChildren<MeshFilter>();var mesh=filter.sharedMesh;var bounds=mesh.bounds;
        long triangles=0;for(int i=0;i<mesh.subMeshCount;i++)triangles+=mesh.GetIndexCount(i)/3;
        if(triangles>10000)throw new InvalidOperationException("Слишком тяжёлый прибрежный декор: "+source.name+" — "+triangles+" треугольников.");
        var go=New(name,parent);go.transform.localRotation=Quaternion.Euler(0,angle,0);
        go.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
        go.transform.localPosition=position-go.transform.localRotation*Vector3.Scale(go.transform.localScale,new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=filter.GetComponent<Renderer>().sharedMaterials;
        renderer.shadowCastingMode=ShadowCastingMode.On;
    }
    static void MotionReview()
    {
        CampSceneAmbiencePreview.Stop();var ambience=Object.FindAnyObjectByType<CampAmbience>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        string output=Path.Combine(Repo,"artifacts/camp-finish-motion");Directory.CreateDirectory(output);var report=new System.Text.StringBuilder();
        foreach(var filter in ambience.GetComponentsInChildren<MeshFilter>())
        {
            if(filter.sharedMesh==null)continue;var weights=new List<Vector4>();filter.sharedMesh.GetUVs(3,weights);int n=0;float max=0;
            foreach(var v in weights)if(v.w<0){n++;max=Mathf.Max(max,-v.w);}
            if(n>0)report.AppendLine($"cloth={filter.name} weighted={n} maximum={max} shader={filter.GetComponent<Renderer>().sharedMaterial.shader.name}");
        }
        for(int i=0;i<2;i++)
        {
            ambience.PreviewAt(14+i*.8f);
            Render(magic.FireAltar.position+Vector3.up*1.5f,2.1f,Path.Combine(output,"flags-"+i+".png"));
            var fire=ambience.transform.Find("Campfire");Render(fire.position+Vector3.up*.5f,3.2f,Path.Combine(output,"lights-"+i+".png"));
            report.AppendLine("time="+(14+i*.8f)+" breeze="+Shader.GetGlobalVector("_CampBreeze"));
            foreach(var light in ambience.GetComponentsInChildren<Light>())if(light.GetComponentInParent<CampMagicCircle>()==null)report.AppendLine(light.name+"="+light.intensity);
        }
        ambience.StopPreview();File.WriteAllText(Path.Combine(output,"report.txt"),report.ToString());
    }
    static void Finish()
    {
        Backup();var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        if(!river.HasBakedShape){Undo.RecordObject(river,"Сохранённый контур реки");river.BakeShape();EditorUtility.SetDirty(river);}
        if(magic.DecorationRoot.Find("Мох и гравий в швах")==null)
        {
            var group=New("Мох и гравий в швах",magic.DecorationRoot);var stones=magic.PlazaRoot.GetComponentsInChildren<MeshFilter>();
            var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/TreePlants/Grass01.prefab");
            for(int i=0;i<stones.Length;i+=3)
            {
                var source=stones[i];Vector3 at=source.transform.TransformPoint(source.sharedMesh.vertices[0]);at.y=magic.HeightAt(at)+.018f;
                var pebble=New("Гравий в шве "+i,group.transform);pebble.transform.position=at;SetWorldScale(pebble.transform,new Vector3(.12f,.4f,.16f));pebble.transform.rotation=Quaternion.Euler(0,i*137.5f,0);
                pebble.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;pebble.AddComponent<MeshRenderer>().sharedMaterial=source.GetComponent<Renderer>().sharedMaterial;
                if(i%6!=0 || grass==null)continue;
                var moss=(GameObject)PrefabUtility.InstantiatePrefab(grass,group.transform);moss.name="Мох в шве "+i;Undo.RegisterCreatedObjectUndo(moss,"Мох в шве");
                moss.transform.position=at+Vector3.right*.10f;SetWorldScale(moss.transform,new Vector3(.14f,.07f,.17f));
                foreach(var collider in moss.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
            }
        }
        if(Camera.main!=null){var data=Camera.main.GetUniversalAdditionalCameraData();Undo.RecordObject(data,"Глубина воды");data.requiresDepthTexture=true;EditorUtility.SetDirty(data);}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(river.gameObject.scene);EditorSceneManager.SaveScene(river.gameObject.scene);Inspect();
    }
}

[CustomEditor(typeof(CampRiver))]
public sealed class CampRiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();EditorGUILayout.HelpBox("X — вдоль реки, Y — изгиб русла. После изменения формы нажми «Обновить русло». Четыре меша русла совмещаются автоматически; камни и растения редактируются отдельно.",MessageType.Info);
        if(GUILayout.Button("Обновить русло и край земли"))CampFinishAuthoring.Rebuild((CampRiver)target);
    }
    void OnSceneGUI()
    {
        var river=(CampRiver)target;if(river.Contour==null)return;
        for(int i=0;i<river.Contour.Length;i++)
        {
            var p=river.Contour[i];var world=river.transform.TransformPoint(new Vector3(p.x,river.WaterLevel,p.y));
            EditorGUI.BeginChangeCheck();var changed=Handles.PositionHandle(world,river.transform.rotation);
            if(EditorGUI.EndChangeCheck()){Undo.RecordObject(river,"Точка русла");var local=river.transform.InverseTransformPoint(changed);river.Contour[i]=new Vector2(local.x,local.z);EditorUtility.SetDirty(river);}
            Handles.Label(world,"Русло "+i);if(i>0){var prev=river.Contour[i-1];Handles.DrawDottedLine(river.transform.TransformPoint(new Vector3(prev.x,river.WaterLevel,prev.y)),world,4);}
        }
    }
}
