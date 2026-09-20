using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;

[InitializeOnLoad]
public static class ForestBudImportRepair
{
    const string Report="Library/ForestBudImportRepair.done";
    static ForestBudImportRepair(){EditorApplication.delayCall+=RunOnce;}
    static void RunOnce()
    {
        if(File.Exists(Report))return;
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=RunOnce;return;}
        try
        {
            const string src="Assets/ForestBudRanged";
            const string dst="Assets/Resources/Characters/Forest_Bud";
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null)throw new Exception("URP/Lit shader unavailable");
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{src}))
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Texture color=mat.GetTexture("_MainTex"),normal=mat.GetTexture("_BumpMap"),metal=mat.GetTexture("_MetallicGlossMap"),ao=mat.GetTexture("_OcclusionMap");
                Color tint=mat.color;mat.shader=shader;mat.SetTexture("_BaseMap",color);mat.SetColor("_BaseColor",tint);mat.SetFloat("_Smoothness",.28f);
                if(normal!=null){mat.SetTexture("_BumpMap",normal);mat.EnableKeyword("_NORMALMAP");}
                if(metal!=null){mat.SetTexture("_MetallicGlossMap",metal);mat.EnableKeyword("_METALLICSPECGLOSSMAP");}
                if(ao!=null){mat.SetTexture("_OcclusionMap",ao);mat.EnableKeyword("_OCCLUSIONMAP");}
                EditorUtility.SetDirty(mat);
            }
            // The packaged fruit originally retained an imported embedded material.
            var fruit=PrefabUtility.LoadPrefabContents(src+"/ProjectileFruit.prefab");
            var fruitMat=AssetDatabase.FindAssets("t:Material",new[]{src}).Select(g=>AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).First(m=>m.name.Contains("Fruit"));
            foreach(var r in fruit.GetComponentsInChildren<Renderer>())r.sharedMaterial=fruitMat;
            PrefabUtility.SaveAsPrefabAsset(fruit,src+"/ProjectileFruit.prefab");PrefabUtility.UnloadPrefabContents(fruit);
            var model=PrefabUtility.LoadPrefabContents(src+"/ForestBudRanged.prefab");
            var animator=model.GetComponent<Animator>();
            if(animator==null||animator.avatar==null||!animator.avatar.isValid||animator.avatar.isHuman)throw new Exception("Generic avatar validation failed");
            int sockets=model.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("Spawn_Fruit_"));
            if(sockets!=5)throw new Exception("Socket count: "+sockets);
            // Старый пример использовал Rigidbody и AnimationEvent; бой теперь принадлежит Game.Sim.
            foreach(var behaviour in model.GetComponents<MonoBehaviour>())
                if(behaviour!=null && behaviour.GetType().Name=="ForestBudVolley") UnityEngine.Object.DestroyImmediate(behaviour);
            PrefabUtility.SaveAsPrefabAsset(model,dst+"/ForestBudRanged.prefab");PrefabUtility.UnloadPrefabContents(model);
            AssetDatabase.SaveAssets();
            File.WriteAllText(Report,"SUCCESS: duplicate script removed; URP materials assigned; Generic avatar valid; five sockets assigned; Resources prefab created.");
            Debug.Log("FOREST_BUD_IMPORT_REPAIR_SUCCESS");
        }
        catch(Exception e){File.WriteAllText(Report+".error",e.ToString());Debug.LogException(e);}
    }
}
