using System;
using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagAnchorSlamContactSetup
{
    private const string Path="Assets/Resources/VFX/Pelag/Prefabs/VFX_AnchorSlam_Contact.prefab";
    private const string Version="Source Slam contact 2";
    [InitializeOnLoadMethod]
    private static void Schedule()=>EditorApplication.delayCall+=Install;
    public static void Validate()
    {
        var shape=AssetDatabase.LoadAssetAtPath<AnchorHeadShape>("Assets/Resources/Weapons/Pelag/AnchorChain/AnchorHeadShape.asset");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if(shape==null || shape.Points.Length<8 || prefab==null)
            throw new InvalidOperationException("Anchor Slam geometry or contact prefab missing from build");
        var library=AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        if(!Array.Exists(library.Entries,e=>e.Id==PelagVfxId.AnchorSlamContact && e.Prefab==prefab))
            throw new InvalidOperationException("Anchor Slam contact is not registered in VFX pool");
        Debug.Log($"[anchor-source-build] collision points={shape.Points.Length}; contact prefab registered");
    }
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        BuildHeadShape();
        var library=AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/VFX/Pelag/Prefabs/VFX_AnchorLeap_Land.prefab");
        if(library==null || source==null)return;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if(prefab==null || AssetImporter.GetAtPath(Path).userData!=Version)
        {
            var root=UnityEngine.Object.Instantiate(source);root.name="VFX_AnchorSlam_Contact";
            try
            {
                // Объём дают сколы и расходящиеся струи пыли; большая плоская печать убрана.
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    if(t.name.Contains("Ground Shockwave"))UnityEngine.Object.DestroyImmediate(t.gameObject);
                var dust=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/VFX/Pelag/Prefabs/VFX_DustHeavy.prefab");
                foreach(var emitter in dust.GetComponentsInChildren<ParticleSystem>(true))
                    if(emitter.name=="Dust Flipbook")
                    {
                        var cloud=UnityEngine.Object.Instantiate(emitter.gameObject,root.transform,false);
                        cloud.name="Ground dust from existing combat pack";cloud.transform.localScale=Vector3.one*.60f;
                        var cloudMain=cloud.GetComponent<ParticleSystem>().main;
                        cloudMain.startLifetimeMultiplier=.32f;cloudMain.startColor=new Color(.85f,.78f,.60f,.58f);
                    }
                foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main=ps.main;main.simulationSpace=ParticleSystemSimulationSpace.World;
                    if(ps.name.Contains("Smoke"))
                    {
                        main.startColor=new Color(.66f,.58f,.40f,.55f);
                        main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,1.4f);
                        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=68;shape.radius=.35f;
                        ps.transform.localRotation=Quaternion.Euler(-90,0,0);
                    }
                    if(ps.name.Contains("Impact Flash"))main.startSizeMultiplier*=.65f;
                }
                prefab=PrefabUtility.SaveAsPrefabAsset(root,Path);
                AssetImporter.GetAtPath(Path).userData=Version;
                AssetImporter.GetAtPath(Path).SaveAndReimport();
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        int index=Array.FindIndex(library.Entries,e=>e.Id==PelagVfxId.AnchorSlamContact);
        if(index<0){index=library.Entries.Length;Array.Resize(ref library.Entries,index+1);}
        if(library.Entries[index].Prefab==prefab)return;
        library.Entries[index]=new AbilityVfxLibrary.Entry{Id=PelagVfxId.AnchorSlamContact,Prefab=prefab,Prewarm=3};
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssetIfDirty(library);
    }

    private static void BuildHeadShape()
    {
        const string path="Assets/Resources/Weapons/Pelag/AnchorChain/AnchorHeadShape.asset";
        if(AssetDatabase.LoadAssetAtPath<AnchorHeadShape>(path)!=null)return;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorHead.fbx");
        if(source==null)return;
        var vertices=new System.Collections.Generic.List<Vector3>();
        foreach(var filter in source.GetComponentsInChildren<MeshFilter>())
            foreach(var v in filter.sharedMesh.vertices)
                vertices.Add(source.transform.InverseTransformPoint(filter.transform.TransformPoint(v)));
        if(vertices.Count==0)throw new InvalidOperationException("Anchor head mesh has no collision vertices");
        var points=new System.Collections.Generic.List<Vector3>();
        for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)for(int z=-2;z<=2;z++)
        {
            if(x==0 && y==0 && z==0)continue;
            var direction=new Vector3(x,y,z);float furthest=float.NegativeInfinity;Vector3 point=Vector3.zero;
            foreach(var v in vertices){float d=Vector3.Dot(v,direction);if(d>furthest){furthest=d;point=v;}}
            if(!points.Contains(point))points.Add(point);
        }
        var shape=ScriptableObject.CreateInstance<AnchorHeadShape>();shape.Points=points.ToArray();
        AssetDatabase.CreateAsset(shape,path);
        Debug.Log($"[anchor-head-shape] {shape.Points.Length} support points from original textured mesh");
    }
}
