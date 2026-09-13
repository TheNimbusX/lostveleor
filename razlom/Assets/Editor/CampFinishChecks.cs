using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;
using Object=UnityEngine.Object;

public static class CampFinishChecks
{
    static void Require(bool value,string name){if(!value)throw new InvalidOperationException("[camp-finish-check] "+name);Debug.Log("[camp-finish-check] PASS "+name);}
    public static void BuildCapture()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\','/').Contains("/artifacts/"),"isolated project");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var river=Object.FindAnyObjectByType<CampRiver>();var magic=Object.FindAnyObjectByType<CampMagicCircle>();
        Require(river!=null && magic!=null,"authored scene reopened");
        var biomes=magic.GetComponentsInChildren<CampMicrobiome>();Require(biomes.Length==4,"four authored altar microclimates");
        foreach(var biome in biomes)
        {
            Require(biome.GetComponentsInChildren<Collider>().Length==0,"microclimate is decorative "+biome.name);
            foreach(var filter in biome.GetComponentsInChildren<MeshFilter>())Require(EditorUtility.IsPersistent(filter.sharedMesh) && !CampPlayerView.UsedByNavigation(filter),"persistent decorative biome mesh "+filter.name);
            foreach(var particles in biome.Particles){particles.Simulate(2,false,true,true);Require(particles.particleCount>0,"microclimate emits particles "+particles.name);particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        }
        Require(EditorUtility.IsPersistent(river.SourceGround) && EditorUtility.IsPersistent(river.Ground.sharedMesh),"source and cut ground are persistent");
        Require(river.FoliageBoundary!=null && EditorUtility.IsPersistent(river.FoliageBoundary),"curved bank foliage boundary is persistent");
        long riverTriangles=0;
        foreach(var filter in river.GetComponentsInChildren<MeshFilter>())
        {
            long count=0;for(int i=0;i<filter.sharedMesh.subMeshCount;i++)count+=filter.sharedMesh.GetIndexCount(i)/3;
            riverTriangles+=count;if(filter.transform.IsChildOf(river.Dressing))Require(count<=10000,"bank prop stays within mesh budget "+filter.name);
        }
        Require(riverTriangles<100000,"whole river geometry below 100k triangles: "+riverTriangles);
        Require(river.BakedCentres.Length==river.BakedNormals.Length && river.BakedCentres.Length>2,"curved river sections retained");
        Require(river.SourceGround.vertexCount==21025,"original terrain topology retained");
        foreach(var filter in river.GetComponentsInChildren<MeshFilter>(true))Require(filter.sharedMesh!=null && EditorUtility.IsPersistent(filter.sharedMesh),"persistent mesh "+filter.name);
        foreach(var renderer in river.GetComponentsInChildren<Renderer>(true))foreach(var mat in renderer.sharedMaterials)Require(mat!=null && EditorUtility.IsPersistent(mat),"persistent material "+renderer.name);
        var flame=Object.FindAnyObjectByType<CampFlameProView>();Require(flame!=null && EditorUtility.IsPersistent(flame.GetComponent<MeshFilter>().sharedMesh),"campfire exists before Play Mode");
        var matrix=river.transform.worldToLocalMatrix*river.Ground.transform.localToWorldMatrix;int outside=0;
        foreach(var v in river.Ground.sharedMesh.vertices){var p=matrix.MultiplyPoint3x4(v);if(p.z<river.LandEdge(p.x)-.003f)outside++;}
        Require(outside==0,"ground has no triangles extending into river");
        foreach(var altar in new[]{magic.AlchemyAltar,magic.EarthAltar})
        {
            var p=river.transform.InverseTransformPoint(altar.position);Require(p.z-river.LandEdge(p.x)>3,"front altar remains on intact camp ground "+altar.name);
        }
        foreach(var filter in river.Geometry.GetComponentsInChildren<MeshFilter>())
            Require(filter.transform.localPosition==Vector3.zero && filter.transform.localRotation==Quaternion.identity && filter.transform.localScale==Vector3.one,"generated river mesh matches common contour "+filter.name);
        var ambience=Object.FindAnyObjectByType<CampAmbience>();var lights=ambience.GetComponentsInChildren<Light>(true);var baseline=new float[lights.Length];for(int i=0;i<lights.Length;i++)baseline[i]=lights[i].intensity;
        ambience.StopPreview();ambience.PreviewAt(13.5f);bool modulated=false;
        for(int i=0;i<lights.Length;i++)if(lights[i].GetComponentInParent<CampMagicCircle>()!=null)Require(Mathf.Abs(lights[i].intensity-baseline[i])<1e-6f,"magic light has one owner "+lights[i].name);else if(Mathf.Abs(lights[i].intensity-baseline[i])>.0001f)modulated=true;
        Require(modulated,"preview modulates camp lights");ambience.StopPreview();for(int i=0;i<lights.Length;i++)Require(Mathf.Abs(lights[i].intensity-baseline[i])<1e-6f,"preview restores "+lights[i].name);
        var pebble=river.Dressing.GetChild(0);var position=pebble.localPosition;pebble.localPosition+=new Vector3(.17f,0,.08f);var manual=pebble.localPosition;
        var originalGround=river.Ground.sharedMesh;int originalVertices=originalGround.vertexCount;float width=river.Width;
        Undo.IncrementCurrentGroup();Undo.RecordObject(river,"Test river width");river.Width+=.45f;
        Undo.FlushUndoRecordObjects();
        CampFinishAuthoring.Rebuild(river);Require(pebble.localPosition==manual,"rebuild preserves manually moved bank stones");
        Require(river.Ground.sharedMesh==originalGround,"rebuild preserves ground asset reference");
        Undo.FlushUndoRecordObjects();Undo.PerformUndo();
        Require(Mathf.Abs(river.Width-width)<.001f,"Undo restores river width");Require(river.Ground.sharedMesh.vertexCount==originalVertices,"Undo restores terrain mesh");
        Require(river.Geometry!=null && river.Geometry.GetComponentsInChildren<MeshFilter>().Length==4,"Undo preserves geometry references");
        Require(pebble.localPosition==manual,"Undo of river preserves earlier manual edit");pebble.localPosition=position;
        var points=(Vector2[])river.Contour.Clone();river.Contour[1]=river.Contour[0];bool rejected=false;
        try{CampFinishAuthoring.Rebuild(river);}catch(InvalidOperationException){rejected=true;}finally{river.Contour=points;river.Refresh();}
        Require(rejected && river.Ground.sharedMesh==originalGround,"invalid contour rejected before replacing geometry");
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(river.gameObject.scene);EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        river=Object.FindAnyObjectByType<CampRiver>();Require(river!=null && river.Geometry!=null && river.Ground.sharedMesh.vertexCount==originalVertices,"reopened edited scene has persistent geometry");
        Debug.Log("[camp-finish-check] ALL AUTHORING CHECKS PASSED");Game.EditorTools.RazlomCaptureBuild.Build();
    }
}
