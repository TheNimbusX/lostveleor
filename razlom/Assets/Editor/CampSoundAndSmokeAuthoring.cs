using System;
using System.IO;
using System.Linq;
using System.Text;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class CampSoundAndSmokeAuthoring
{
    const string Main="Assets/Scenes/SampleScene.unity";
    const string Audio="Assets/Resources/Audio/Camp/Prepared";
    const string MaterialPath="Assets/Resources/Environment/Camp/Unified/Chimney smoke.mat";
    static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static string Output=>Path.Combine(Repo,"ART/CAMP/sound-and-smoke-2026-09-13");
    static string Request=>Path.Combine(Repo,"artifacts/request-camp-sound-smoke");
    [InitializeOnLoadMethod] static void Watch()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        if(SessionState.GetBool("CampSmokeCapture",false) && EditorApplication.isPlaying)
        {
            if(MainMenuView.IsOpen)
            {
                var menu=Object.FindAnyObjectByType<MainMenuView>();
                if(menu!=null)typeof(MainMenuView).GetMethod("StartGame",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(menu,null);
                return;
            }
            if(Time.timeSinceLevelLoad>8)
            {SessionState.SetBool("CampSmokeCapture",false);try{Capture();}finally{EditorApplication.isPlaying=false;}}
        }
        if(Application.isBatchMode || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request))return;
        string action=File.ReadAllText(Request).Trim();File.Delete(Request);Directory.CreateDirectory(Output);
        try
        {
            if(action=="install")Install();
            else if(action=="capture"){SaveCurrent();SessionState.SetBool("CampSmokeCapture",true);EditorApplication.isPlaying=true;}
        }
        catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString());}
    }
    static void SaveCurrent()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=Main)throw new InvalidOperationException("Для этого прохода должна быть открыта основная сцена лагеря.");
        CampSceneAmbiencePreview.Stop();
        if(scene.isDirty)EditorSceneManager.SaveScene(scene);
    }
    [MenuItem("Разлом/Лагерь/Подключить звуки и лёгкий дым кузницы")]
    public static void Install()
    {
        SaveCurrent();Directory.CreateDirectory(Output);
        var scene=EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene,Path.Combine(Output,"main-before-smoke-sound.unity"),true);
        var camp=Object.FindAnyObjectByType<SceneWorldView>().CampRoot;
        if(Object.FindAnyObjectByType<CampSoundscape>()!=null || camp.GetComponentInChildren<CampChimneySmoke>()!=null)
            throw new InvalidOperationException("Звуки или дым уже установлены; правьте сохранённые объекты.");
        foreach(string path in Directory.GetFiles(Audio,"*.ogg"))
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
            var settings=importer.defaultSampleSettings;
            settings.loadType=path.Contains("Anvil")?AudioClipLoadType.DecompressOnLoad:AudioClipLoadType.Streaming;
            settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.8f;
            settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;settings.sampleRateOverride=48000;
            importer.defaultSampleSettings=settings;importer.loadInBackground=false;importer.SaveAndReimport();
        }
        var smith=camp.transform.Find("Smith Shelter");
        var forge=smith.GetComponentsInChildren<MeshFilter>().First(x=>x.name.StartsWith("medieval+blacksmith+forge"));
        float top=float.MinValue;
        foreach(var v in forge.sharedMesh.vertices)top=Mathf.Max(top,forge.transform.TransformPoint(v).y);
        Vector3 centre=Vector3.zero;int count=0;
        foreach(var v in forge.sharedMesh.vertices)
        {var p=forge.transform.TransformPoint(v);if(p.y>top-.07f){centre+=p;count++;}}
        if(count==0)throw new InvalidOperationException("Не найден верх трубы.");
        centre/=count;centre.y=top+.04f;
        var smokeObject=new GameObject("Лёгкий дым из трубы");smokeObject.transform.position=centre;
        smokeObject.transform.SetParent(forge.transform,true);
        var particles=smokeObject.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;main.loop=true;main.duration=5;main.prewarm=true;main.playOnAwake=true;
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;
        main.startLifetime=new ParticleSystem.MinMaxCurve(2.5f,3.4f);main.startSpeed=0;
        main.startSize=new ParticleSystem.MinMaxCurve(.32f,.46f);main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        main.startColor=new Color(.79f,.82f,.81f,.22f);main.maxParticles=32;
        var emission=particles.emission;emission.rateOverTime=5;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.12f;shape.rotation=new Vector3(90,0,0);
        var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(.10f,.16f);velocity.y=new ParticleSystem.MinMaxCurve(.4f,.52f);velocity.z=new ParticleSystem.MinMaxCurve(.035f,.08f);
        var size=particles.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.65f,1,2.5f));
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(.5f,.55f),new GradientAlphaKey(0,1)});
        var color=particles.colorOverLifetime;color.enabled=true;color.color=gradient;
        var noise=particles.noise;noise.enabled=true;noise.strength=.085f;noise.frequency=.45f;noise.scrollSpeed=.16f;noise.quality=ParticleSystemNoiseQuality.Low;
        var rotation=particles.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-.2f,.2f);
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;
        var material=new Material(Shader.Find("Razlom/Camp Chimney Smoke")){name="Chimney smoke"};
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hovl Studio/HSFiles/Textures/Smoke5.png"));
        AssetDatabase.CreateAsset(material,MaterialPath);renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        smokeObject.AddComponent<CampChimneySmoke>().Smoke=particles;
        var root=new GameObject("Звуковое окружение лагеря");root.transform.SetParent(camp.transform.parent,false);
        var sound=root.AddComponent<CampSoundscape>();sound.CampRoot=camp;sound.River=camp.GetComponentInChildren<CampRiver>();
        var magic=camp.GetComponentInChildren<CampMagicCircle>();
        var anvil=smith.GetComponentsInChildren<Transform>().FirstOrDefault(x=>x.name.StartsWith("anvil"))??forge.transform;
        CampSoundscape.Layer Layer(CampSoundscape.Place place,string filename,string label,Transform anchor,float gain,float near,float far)
        {
            var go=new GameObject(label);go.transform.SetParent(root.transform,false);var audio=go.AddComponent<AudioSource>();
            audio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Audio+"/Camp_"+filename+".ogg");
            if(audio.clip==null)throw new InvalidOperationException("Нет клипа "+filename);
            audio.playOnAwake=false;audio.loop=place!=CampSoundscape.Place.Smith;audio.spatialBlend=0;audio.volume=0;
            audio.priority=place==CampSoundscape.Place.Music?128:180;
            return new CampSoundscape.Layer{Place=place,Source=audio,Anchor=anchor,Gain=gain,Near=near,Far=far};
        }
        sound.Layers=new[]{
            Layer(CampSoundscape.Place.Forest,"Birds","Птицы в лесу",camp.transform,.5f,0,1),
            Layer(CampSoundscape.Place.Fire,"Fire","Костёр",camp.transform.Find("Campfire"),.82f,2.8f,12),
            Layer(CampSoundscape.Place.River,"River","Река",sound.River.transform,.64f,3.5f,23),
            Layer(CampSoundscape.Place.Cauldron,"Cauldron","Котёл",magic.AlchemyAltar,.48f,2.5f,9),
            Layer(CampSoundscape.Place.Smith,"Anvil","Наковальня",anvil,.68f,3.5f,14),
            Layer(CampSoundscape.Place.Music,"Music","Тихая тема лагеря",camp.transform,.3f,0,1)};
        particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Path.Combine(Output,"installation.txt"),"chimney="+centre+" top="+top+" rate=5 lifetime=2.5..3.4 max=32\nLayers="+sound.Layers.Length+"\n");
    }
    static void Capture()
    {
        var smoke=Object.FindAnyObjectByType<CampChimneySmoke>();
        var cameraObject=new GameObject("Smoke review camera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);camera.enabled=false;camera.transform.rotation=Quaternion.Euler(48,35,0);
        camera.transform.position=smoke.transform.position+Vector3.down*1.2f-camera.transform.forward*60;
        camera.orthographic=true;camera.orthographicSize=4.2f;camera.aspect=16f/9;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var rt=RenderTexture.GetTemporary(1920,1080,24);var old=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
            File.WriteAllBytes(Path.Combine(Output,"chimney-game.png"),texture.EncodeToPNG());Object.DestroyImmediate(texture);
            File.WriteAllText(Path.Combine(Output,"smoke-runtime.txt"),"particles="+smoke.Smoke.particleCount+" world="+smoke.transform.position+" material="+smoke.Smoke.GetComponent<Renderer>().sharedMaterial.name);
        }
        finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(cameraObject);}
    }
}
