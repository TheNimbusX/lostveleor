using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Запекает авторскую разметку дорог в одну поверхность с низкими обочинами.</summary>
public static class CampSurfaceBuilder
{
    const string Folder="Assets/Resources/Environment/Camp/ground/Study/";
    public static float Stoniness(Vector3 p) => Mathf.Lerp(.64f,1f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.30f,.70f,Mathf.PerlinNoise(p.x*.32f+5,p.z*.32f+13))));
    public static float Coverage(Vector3 p) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(.28f,.72f,Mathf.PerlinNoise(p.x*.43f+8,p.z*.43f+17)*.65f+Mathf.PerlinNoise(p.x*1.3f+31,p.z*1.3f+2)*.35f));
    public static float Path(float footprint,Vector3 p)
    {
        float edge=(Mathf.PerlinNoise(p.x*2.1f+7,p.z*2.1f+3)-.5f)*.22f;
        edge+=(Mathf.PerlinNoise(p.x*7.3f,p.z*7.3f)-.5f)*.07f;
        float shoulder=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,.75f,Mathf.PerlinNoise(p.x*1.15f+31,p.z*1.15f+9)));
        edge-=shoulder*.24f*(1-Mathf.SmoothStep(.72f,1,footprint));
        return Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.76f,footprint+edge));
    }
    public static Func<Vector3,float> Create(Transform parent,Bounds bounds,Vector3 origin,Func<Vector3,float> footprint,List<Bounds> props)
    {
        Func<Vector3,float> height=p=>
        {
            float road=Path(footprint(p),p);
            float hummock=Mathf.PerlinNoise(p.x*.85f+11,p.z*.85f+18);
            return (1-road)*hummock*.16f*(1-Contact(p,props));
        };
        const int res=512;
        var map=new Texture2D(res,res,TextureFormat.RGBA32,false,true);
        var colors=new Color[res*res];
        for(int z=0;z<res;z++)for(int x=0;x<res;x++)
        {
            Vector3 p=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x/(float)(res-1)),origin.y,Mathf.Lerp(bounds.min.z,bounds.max.z,z/(float)(res-1)));
            float path=Path(footprint(p),p);
            float stones=Stoniness(p);
            colors[z*res+x]=new Color(path,stones,1-Contact(p,props)*.40f,Coverage(p));
        }
        map.SetPixels(colors);map.Apply();map.wrapMode=TextureWrapMode.Clamp;map.filterMode=FilterMode.Bilinear;
        var saved=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"CampSurfaceMap.asset");
        if(saved==null){AssetDatabase.CreateAsset(map,Folder+"CampSurfaceMap.asset");saved=map;}
        else {EditorUtility.CopySerialized(map,saved);UnityEngine.Object.DestroyImmediate(map);EditorUtility.SetDirty(saved);}
        const int grid=144;
        var vertices=new Vector3[(grid+1)*(grid+1)];
        var uv=new Vector2[vertices.Length];
        for(int z=0;z<=grid;z++)for(int x=0;x<=grid;x++)
        {
            int i=z*(grid+1)+x;uv[i]=new Vector2(x/(float)grid,z/(float)grid);
            Vector3 p=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,uv[i].x),origin.y,Mathf.Lerp(bounds.min.z,bounds.max.z,uv[i].y));
            vertices[i]=p-origin+Vector3.up*height(p);
        }
        var indices=new int[grid*grid*6];int index=0;
        for(int z=0;z<grid;z++)for(int x=0;x<grid;x++)
        {
            int i=z*(grid+1)+x;
            indices[index++]=i;indices[index++]=i+grid+1;indices[index++]=i+1;
            indices[index++]=i+1;indices[index++]=i+grid+1;indices[index++]=i+grid+2;
        }
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"CampSurface.asset");
        if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,Folder+"CampSurface.asset");}else mesh.Clear();
        mesh.name="Camp surface";mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Environment/Camp/ground/M_Ground_Base_New.mat");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"CampSurface.mat");
        if(mat==null){mat=new Material(source);AssetDatabase.CreateAsset(mat,Folder+"CampSurface.mat");}else mat.CopyPropertiesFromMaterial(source);
        mat.SetFloat("_IsSurface",1);mat.SetTexture("_SurfaceMap",saved);
        mat.SetVector("_SurfaceBounds",new Vector4(bounds.min.x,bounds.min.z,bounds.size.x,bounds.size.z));
        var stonesTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Environment/Camp/ground/CampTrailStones_v2.png");
        mat.SetTexture("_StonyTex",stonesTexture!=null?stonesTexture:source.GetTexture("_DirtTex"));
        mat.SetTexture("_TurfTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Environment/Camp/ground/CampTurf_v3.png"));
        mat.SetShaderPassEnabled("DepthOnly",true);mat.renderQueue=2000;EditorUtility.SetDirty(mat);
        var go=new GameObject("Continuous ground");go.transform.SetParent(parent,false);go.layer=2;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;
        return height;
    }
    static float Contact(Vector3 p,List<Bounds> props)
    {
        float proximity=0;
        foreach(var b in props)
        {
            float dx=Mathf.Max(b.min.x-p.x,0,p.x-b.max.x),dz=Mathf.Max(b.min.z-p.z,0,p.z-b.max.z);
            float distance=Mathf.Sqrt(dx*dx+dz*dz);
            proximity=Mathf.Max(proximity,1-Mathf.SmoothStep(0,1,distance/.45f));
        }
        return proximity;
    }
}
