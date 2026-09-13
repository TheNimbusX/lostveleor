using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;
using Object=UnityEngine.Object;

public static class CampMicrobiomesAuthoring
{
    const string Folder="Assets/Resources/Environment/Camp/Microbiomes";
    static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    [InitializeOnLoadMethod]static void Watch()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        string request=Path.Combine(Repo,"artifacts/request-camp-biomes");
        if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request))return;
        string action=File.ReadAllText(request).Trim();File.Delete(request);try{if(action=="widen")Widen();else if(action=="conform")Conform();else Install();}catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Repo,"artifacts/camp-biomes-error.txt"),e.ToString());}
    }
    [MenuItem("Разлом/Лагерь/Добавить микробиомы алтарей")]
    public static void Install()
    {
        CampSceneAmbiencePreview.Stop();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        if(magic==null)throw new InvalidOperationException("Нет школы магии.");
        string backup=Path.Combine(Repo,"artifacts/camp-biomes-live",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(backup);
        if(!EditorSceneManager.SaveScene(magic.gameObject.scene,Path.Combine(backup,"before.unity"),true))throw new IOException("Не сохранена исходная сцена.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();magic.RefreshGround();
        var ambience=magic.GetComponentInParent<CampAmbience>();if(ambience!=null){Undo.RecordObject(ambience,"Прежнее покачивание флагов");ambience.FlagStrength=1;EditorUtility.SetDirty(ambience);}
        var altars=new[]{magic.IceAltar,magic.FireAltar,magic.AlchemyAltar,magic.EarthAltar};
        string[] names={"Лёд — снег и иней","Огонь — пепел","Яд — разлитое зелье","Земля — осколки и мох"};
        Color[] colors={new Color(.70f,.85f,.89f,.95f),new Color(.19f,.15f,.12f,.61f),new Color(.095f,.33f,.015f,.90f),new Color(.15f,.25f,.026f,.82f)};
        for(int kind=0;kind<4;kind++)
        {
            var altar=altars[kind];if(altar==null || altar.GetComponentInChildren<CampMicrobiome>()!=null)continue;
            var group=New(names[kind],altar);group.AddComponent<CampMagicDecoration>();var biome=group.AddComponent<CampMicrobiome>();
            Vector3 s=altar.lossyScale;group.transform.localScale=new Vector3(1/s.x,1/s.y,1/s.z);
            var material=Material("Ground "+kind,"Game/Camp Biome Ground");material.SetColor("_Color",colors[kind]);material.SetFloat("_Kind",kind);
            material.SetTexture("_Detail",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Environment/Camp/ground/CampEarth_v1.png"));EditorUtility.SetDirty(material);
            var particles=new List<ParticleSystem>();var random=new System.Random(540+kind);
            float[] angles=kind==2?new[]{145f,205f,265f}:new[]{-35f,22f,150f,202f,245f,310f};
            for(int j=0;j<angles.Length;j++)
            {
                float a=angles[j]*Mathf.Deg2Rad,radius=kind==0?1.95f:kind==1?1.79f:kind==2?1.76f:1.83f;
                Vector3 local=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);Vector3 at=group.transform.TransformPoint(local);at.y=magic.HeightAt(at)+.035f;
                float size=kind==2?.92f:kind==0?.92f:.76f;
                Patch(group.transform,at,new Vector2(size,size*.72f),j*73f,material,magic,"Участок "+j,kind);
                if(kind==0 || kind==3)
                {
                    for(int k=0;k<2;k++)
                    {
                        Vector3 position=at+new Vector3(((float)random.NextDouble()-.5f)*.48f,0,((float)random.NextDouble()-.5f)*.48f);position.y=magic.HeightAt(position)+.018f;
                        Pebble(group.transform,position,kind==0?new Vector3(.22f,.09f,.18f):new Vector3(.29f,.27f,.24f),kind,j*2+k);
                    }
                }
                if(kind==2)particles.Add(Particles(group.transform,at+Vector3.up*.05f,new Vector3(.5f,.02f,.35f),2));
            }
            if(kind<2)
            {
                Vector3 at=altar.position+Vector3.up*(kind==0?3.3f:1.35f);
                particles.Add(Particles(group.transform,at,new Vector3(3.9f,.2f,3.6f),kind));
            }
            biome.Particles=particles.ToArray();EditorUtility.SetDirty(biome);
            foreach(var ps in particles)ps.Simulate(2,false,true,true);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(magic.gameObject.scene);EditorSceneManager.SaveScene(magic.gameObject.scene);
        CampFinishAuthoring.Inspect();
        File.WriteAllText(Path.Combine(Repo,"artifacts/camp-biomes-installed.txt"),DateTime.Now.ToString("O")+" groups="+magic.GetComponentsInChildren<CampMicrobiome>().Length+" colliders="+CountColliders(magic));
        Debug.Log("[camp-biomes] four authored altar microclimates, no runtime generation");
    }
    static int CountColliders(CampMagicCircle magic){int n=0;foreach(var biome in magic.GetComponentsInChildren<CampMicrobiome>())n+=biome.GetComponentsInChildren<Collider>().Length;return n;}
    static void Conform()
    {
        CampSceneAmbiencePreview.Stop();var magic=Object.FindAnyObjectByType<CampMagicCircle>();magic.RefreshGround();
        foreach(var biome in magic.GetComponentsInChildren<CampMicrobiome>())
        foreach(var filter in biome.GetComponentsInChildren<MeshFilter>())
        {
            var t=filter.transform;Undo.RecordObject(t,"Микробиом на видимой земле");var p=t.position;p.y=magic.HeightAt(p)+.035f;t.position=p;
            if(t.name.StartsWith("Участок")){var mesh=filter.sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Рельеф микробиома");var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++){p=t.TransformPoint(vertices[i]);p.y=magic.HeightAt(p)+.035f;vertices[i]=t.InverseTransformPoint(p);}mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);}
            EditorUtility.SetDirty(t);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(magic.gameObject.scene);EditorSceneManager.SaveScene(magic.gameObject.scene);CampFinishAuthoring.Inspect();
    }
    static void Widen()
    {
        CampSceneAmbiencePreview.Stop();var magic=Object.FindAnyObjectByType<CampMagicCircle>();magic.RefreshGround();
        string backup=Path.Combine(Repo,"artifacts/camp-biomes-live",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(backup);EditorSceneManager.SaveScene(magic.gameObject.scene,Path.Combine(backup,"before.unity"),true);
        var altars=new[]{magic.IceAltar,magic.FireAltar,magic.AlchemyAltar,magic.EarthAltar};
        for(int kind=0;kind<altars.Length;kind++)
        {
            var biome=altars[kind].GetComponentInChildren<CampMicrobiome>();if(biome==null)continue;
            float factor=kind==0?1.52f:kind==1?1.42f:kind==2?1.6f:1.48f;
            foreach(Transform t in biome.transform)
            {
                Undo.RecordObject(t,"Микробиом снаружи основания алтаря");var p=t.localPosition;p.x*=factor;p.z*=factor;t.localPosition=p;
                var filter=t.GetComponent<MeshFilter>();var ps=t.GetComponent<ParticleSystem>();
                if(filter!=null)
                {
                    Vector3 world=t.position;world.y=magic.HeightAt(world)+.035f;t.position=world;
                    if(t.name.StartsWith("Участок"))
                    {
                        var mesh=filter.sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Посадка участка на рельеф");var vertices=mesh.vertices;
                        for(int i=0;i<vertices.Length;i++){world=t.TransformPoint(vertices[i]);world.y=magic.HeightAt(world)+.035f;vertices[i]=t.InverseTransformPoint(world);}mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                    }
                }
                if(ps!=null && kind<2){var shape=ps.shape;shape.scale=new Vector3(3.9f,.2f,3.6f);Vector3 world=t.position;world.y=altars[kind].position.y+(kind==0?3.3f:1.35f);t.position=world;EditorUtility.SetDirty(ps);}
                EditorUtility.SetDirty(t);
            }
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(magic.gameObject.scene);EditorSceneManager.SaveScene(magic.gameObject.scene);CampFinishAuthoring.Inspect();
    }
    static GameObject New(string name,Transform parent){var go=new GameObject(name);go.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(go,name);return go;}
    static Material Material(string name,string shader)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material!=null)return material;
        material=new Material(Shader.Find(shader)){name=name};AssetDatabase.CreateAsset(material,path);return material;
    }
    static void Patch(Transform parent,Vector3 centre,Vector2 size,float angle,Material material,CampMagicCircle magic,string name,int kind)
    {
        var go=New(name,parent);go.transform.position=centre;go.transform.rotation=Quaternion.Euler(0,angle,0);
        const int side=9;var vertices=new Vector3[side*side];var uv=new Vector2[vertices.Length];var triangles=new List<int>();
        for(int z=0;z<side;z++)for(int x=0;x<side;x++)
        {
            int i=z*side+x;uv[i]=new Vector2(x/(float)(side-1),z/(float)(side-1));
            var point=new Vector3((uv[i].x-.5f)*size.x,0,(uv[i].y-.5f)*size.y);var world=go.transform.TransformPoint(point);world.y=magic.HeightAt(world)+.035f;vertices[i]=go.transform.InverseTransformPoint(world);
            if(x>0 && z>0){triangles.Add(i-side-1);triangles.Add(i-1);triangles.Add(i-side);triangles.Add(i-side);triangles.Add(i-1);triangles.Add(i);}
        }
        var mesh=new Mesh{name="Микробиом "+kind+" "+name};mesh.vertices=vertices;mesh.uv=uv;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,Folder+"/"+mesh.name+".asset");go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
    }
    static void Pebble(Transform parent,Vector3 at,Vector3 size,int kind,int index)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Fantasy Forest PBR/Prefab/Rock/Cobble01.prefab").GetComponentInChildren<MeshFilter>();var mesh=source.sharedMesh;var bounds=mesh.bounds;
        var go=New(kind==0?"Снежная крошка "+index:"Каменный осколок "+index,parent);go.transform.rotation=Quaternion.Euler(0,index*137.5f,0);
        go.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);go.transform.position=at-go.transform.rotation*Vector3.Scale(go.transform.localScale,new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
        var material=Material(kind==0?"Snow gravel":"Earth shards","Universal Render Pipeline/Lit");material.SetColor("_BaseColor",kind==0?new Color(.70f,.83f,.87f):new Color(.34f,.35f,.25f));material.SetFloat("_Smoothness",kind==0?.25f:.08f);EditorUtility.SetDirty(material);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    static ParticleSystem Particles(Transform parent,Vector3 at,Vector3 box,int kind)
    {
        var go=New(kind==0?"Редкие снежинки":kind==1?"Лёгкий пепел":"Пузырьки разлитого яда",parent);go.transform.position=at;go.transform.rotation=Quaternion.identity;
        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(142+kind*27);
        var main=ps.main;main.loop=true;main.playOnAwake=true;main.startSpeed=0;main.startLifetime=kind==0?3.2f:kind==1?2.6f:1.6f;main.maxParticles=kind==2?8:22;
        main.startSize=new ParticleSystem.MinMaxCurve(kind==0?.045f:.022f,kind==0?.075f:kind==1?.043f:.055f);
        main.startColor=kind==0?new Color(.83f,.95f,1,.9f):kind==1?new Color(.40f,.34f,.27f,.7f):new Color(.42f,1.2f,.08f,.5f);
        var emission=ps.emission;emission.rateOverTime=kind==0?5:kind==1?4:1.2f;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=box;
        var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=new ParticleSystem.MinMaxCurve(.015f,.06f);float vy=kind==0?-.28f:kind==1?.16f:.08f;velocity.y=new ParticleSystem.MinMaxCurve(Mathf.Min(vy*.9f,vy*1.1f),Mathf.Max(vy*.9f,vy*1.1f));velocity.z=new ParticleSystem.MinMaxCurve(-.03f,.02f);
        var noise=ps.noise;noise.enabled=true;noise.strength=.035f;noise.frequency=.6f;noise.scrollSpeed=.12f;
        var over=ps.colorOverLifetime;over.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.18f),new GradientAlphaKey(.65f,.7f),new GradientAlphaKey(0,1)});over.color=gradient;
        var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=.3f;
        var material=Material(kind==0?"Snowflakes":kind==1?"Ash motes":"Poison bubbles",kind==2?"Game/Camp Magic Motes":"Game/Camp Biome Particles");
        if(kind==0){material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hovl Studio/Magic effects pack/Textures/Snowflake.png"));material.SetFloat("_Textured",1);}if(kind==2)material.SetFloat("_Style",2);EditorUtility.SetDirty(material);
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;return ps;
    }
}
