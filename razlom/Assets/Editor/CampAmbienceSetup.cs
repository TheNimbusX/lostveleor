using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game.View;
using Object = UnityEngine.Object;

public static class CampAmbienceSetup
{
    const string Folder="Assets/Resources/Environment/Camp/Ambience";
    static string Repo=>Directory.GetParent(Application.dataPath).Parent.FullName;
    static string Request=>Path.Combine(Repo,"artifacts/request-camp-ambience");

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update+=Poll; }
    static void Poll()
    {
        if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Install(); }
        catch(Exception e) { Debug.LogException(e);File.WriteAllText(Path.Combine(Repo,"artifacts/camp-ambience-error.txt"),e.ToString()); }
    }

    public static void BuildCapture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Install();
        Game.EditorTools.RazlomCaptureBuild.Build();
    }

    [MenuItem("Разлом/Лагерь/Добавить лёгкий ветер и мерцание")]
    public static void Install()
    {
        var world=Object.FindAnyObjectByType<SceneWorldView>();
        if(world==null || world.CampRoot==null) throw new InvalidOperationException("Camp root is missing.");
        var root=world.CampRoot;
        var scene=root.scene;
        string output=Path.Combine(Repo,"artifacts/camp-ambience-live");
        Directory.CreateDirectory(output);
        EditorSceneManager.SaveScene(scene,Path.Combine(output,"before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity"),true);
        EnsureDefaultVolumeProfile(output);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        // У принятой земли меняется только дополнительный вершинный канал ветра.
        CampGroundStudyBuilder.Install();
        if(root.GetComponent<CampAmbience>()==null) Undo.AddComponent<CampAmbience>(root);
        var shader=Shader.Find("Game/Camp Breeze Lit");
        if(shader==null) throw new InvalidOperationException("Camp Breeze Lit shader is missing.");
        int foliage=0;
        var meshes=new Dictionary<Mesh,Mesh>();
        var materials=new Dictionary<Material,Material>();
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter=renderer.GetComponent<MeshFilter>();
            var source=filter!=null?filter.sharedMesh:null;
            if(source==null) continue;
            string path=AssetDatabase.GetAssetPath(source);
            if(path.StartsWith(Folder+"/",StringComparison.Ordinal) && renderer.sharedMaterial!=null && renderer.sharedMaterial.shader==shader)
            {
                foliage++;
                continue;
            }
            bool tree=path.Contains("/Trees/Fir/") || path.EndsWith("/stylized+tree+3d+model.fbx",StringComparison.Ordinal);
            bool bush=path.Contains("/Vegetation/Bushes/");
            if(!tree && !bush) continue;
            bool supported=true;
            foreach(var mat in renderer.sharedMaterials)
                if(mat==null || mat.shader.name!="Universal Render Pipeline/Lit") supported=false;
            if(!supported) continue;
            if(!meshes.TryGetValue(source,out var mesh))
            {
                mesh=BakePlant(source,tree);
                meshes.Add(source,mesh);
            }
            Undo.RecordObject(filter,"Camp foliage breeze");filter.sharedMesh=mesh;
            var assigned=renderer.sharedMaterials;
            for(int i=0;i<assigned.Length;i++)
            {
                if(!materials.TryGetValue(assigned[i],out var breeze))
                {
                    string matPath=Folder+"/"+AssetKey(assigned[i])+".mat";
                    breeze=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if(breeze==null) {breeze=new Material(assigned[i]);AssetDatabase.CreateAsset(breeze,matPath);}
                    else EditorUtility.CopySerialized(assigned[i],breeze);
                    breeze.name=assigned[i].name+" - Camp breeze";
                    breeze.shader=shader;breeze.SetShaderPassEnabled("MotionVectors",true);
                    EditorUtility.SetDirty(breeze);materials.Add(assigned[i],breeze);
                }
                assigned[i]=breeze;
            }
            Undo.RecordObject(renderer,"Camp foliage breeze");renderer.sharedMaterials=assigned;
            EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(filter);
            foliage++;
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(output,"installed.txt"),$"lights={root.GetComponentsInChildren<Light>(true).Length} foliageRenderers={foliage} foliageMeshes={meshes.Count} materials={materials.Count}\nscene={scene.path}");
        Debug.Log($"[camp-ambience] Installed: {foliage} authored foliage renderers plus meadow; lighting baselines preserved.");
    }

    static void EnsureDefaultVolumeProfile(string output)
    {
        const string path="Assets/DefaultVolumeProfile.asset";
        var profile=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
        if(profile==null || profile.components.Count!=0) return;
        string backup=Path.Combine(output,"empty-default-volume-profile.asset");
        if(!File.Exists(backup)) File.Copy(path,backup);
        // Unity 6.5 в плеере получает список типов из этого профиля. Пустой список ломает URP до первого кадра.
        // Стандартная функция редактора добавляет нейтральные значения; CombatLook не изменяется.
        UnityEditor.Rendering.VolumeProfileUtils.EnsureAllOverridesForDefaultProfile(profile);
        EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
        Debug.Log($"[camp-ambience] Restored {profile.components.Count} neutral default volume components.");
    }

    static string AssetKey(Object source)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long id);
        return guid+"_"+id;
    }

    static Mesh BakePlant(Mesh source,bool tree)
    {
        string path=Folder+"/"+AssetKey(source)+".asset";
        var result=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(result==null) {result=Object.Instantiate(source);AssetDatabase.CreateAsset(result,path);}
        else EditorUtility.CopySerialized(source,result);
        result.name=source.name+" - Camp breeze";
        var bounds=source.bounds;
        // Общий пивот XZ сохраняет фазу между LOD одного дерева.
        Vector3 pivot=new Vector3(0,bounds.min.y,0);
        var positions=source.vertices;
        var bends=new Vector4[positions.Length];
        for(int i=0;i<positions.Length;i++)
        {
            float height=(positions[i].y-bounds.min.y)/Mathf.Max(bounds.size.y,.001f);
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(tree ? .3f : .08f,.95f,height));
            bends[i]=new Vector4(pivot.x,pivot.y,pivot.z,weight*(tree ? .85f : 2.4f)*Mathf.Deg2Rad);
        }
        result.SetUVs(3,bends);
        bounds.Expand(bounds.size.y*.035f);result.bounds=bounds;EditorUtility.SetDirty(result);
        return result;
    }

    [MenuItem("Разлом/Лагерь/Аудит живого окружения")]
    public static void Audit()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var world = Object.FindAnyObjectByType<SceneWorldView>();
        if (world == null || world.CampRoot == null) throw new InvalidOperationException("Camp root is missing.");
        var report = new StringBuilder();
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            report.AppendLine($"LIGHT {Hierarchy(light.transform)} type={light.type} intensity={light.intensity} range={light.range} bake={light.lightmapBakeType} position={light.transform.position} active={light.isActiveAndEnabled}");
        foreach (var renderer in world.CampRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            report.Append($"MESH {Hierarchy(renderer.transform)} mesh={AssetDatabase.GetAssetPath(mesh)} bounds={renderer.bounds} materials=");
            foreach (var mat in renderer.sharedMaterials) report.Append(mat == null ? "null; " : $"{mat.name} [{mat.shader.name}] ({AssetDatabase.GetAssetPath(mat)}); ");
            report.AppendLine();
        }
        string destination = Path.GetFullPath("../camp-ambience-audit.txt");
        File.WriteAllText(destination, report.ToString());
        Debug.Log("[camp-ambience] Audit: " + destination);
    }

    static string Hierarchy(Transform node)
    {
        string result = node.name;
        while (node.parent != null) { node = node.parent; result = node.name + "/" + result; }
        return result;
    }
}
