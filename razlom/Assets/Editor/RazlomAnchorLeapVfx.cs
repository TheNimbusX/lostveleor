using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Game.View;

public static partial class RazlomPelagVfxAssetBuilder
{
    private const string LeapDustAtlas = Root + "/Textures/Pelag_LeapDust_v1.png";

    [MenuItem("Разлом/Pelag VFX/Пересобрать только бросок якоря")]
    public static void BuildAnchorLeapOnly()
    {
        var library=AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if(library==null || library.Entries==null) { Build(); return; }
        var oldEntries=(AbilityVfxLibrary.Entry[])library.Entries.Clone();
        var prefabs=new GameObject[(int)PelagVfxId.Count];
        foreach(var entry in oldEntries)
            if((int)entry.Id<prefabs.Length) prefabs[(int)entry.Id]=entry.Prefab;
        if(prefabs[(int)PelagVfxId.AnchorLeapThrow]==null) { Build(); return; }
        BuildAnchorLeapEffects(prefabs);
        CreateLibrary(prefabs);
        // Ручные размеры пулов других способностей сохраняются вместе с их prefab.
        foreach(var previous in oldEntries)
            if((int)previous.Id<library.Entries.Length) library.Entries[(int)previous.Id].Prewarm=previous.Prewarm;
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
        Debug.Log("[Pelag VFX] Пересобран только бросок якоря v"+LibraryVersion);
    }

    private static void BuildAnchorLeapEffects(GameObject[] prefabs)
    {
        if(AssetDatabase.LoadAssetAtPath<Texture2D>(LeapDustAtlas)==null)
            throw new System.InvalidOperationException("Не импортирован рисованный атлас пыли: "+LeapDustAtlas);
        var importer = AssetImporter.GetAtPath(LeapDustAtlas) as TextureImporter;
        if(importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
        {
            importer.textureType=TextureImporterType.Default; importer.sRGBTexture=true;
            importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=true;
            importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Bilinear;
            importer.npotScale=TextureImporterNPOTScale.None; importer.maxTextureSize=2048;
            importer.mipmapEnabled=true; importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        // Собственные материалы изолируют доработку броска от других способностей.
        var paint = Shader.Find("Razlom/Pelag Flipbook");
        var glow = Shader.Find("Razlom/Pelag Glow");
        Material dust = FlipbookMaterial("M_LeapDust", paint, LeapDustAtlas, new Color(.84f,.80f,.70f,1f), 1f);
        Material glint = FlipbookMaterial("M_LeapMetalGlint", glow, null, new Color(.83f,.94f,1f,1f), 1.6f);
        Material chips = FlipbookMaterial("M_LeapStoneChips", paint, null, new Color(.42f,.36f,.26f,1f), 1f);
        Material core = FlipbookMaterial("M_LeapContact", glow, ImpactBurstTexturePath, new Color(.83f,.93f,1f,1f), 1.3f);
        glint.SetTexture("_BaseMap", Texture2D.whiteTexture);
        chips.SetTexture("_BaseMap", Texture2D.whiteTexture);
        EditorUtility.SetDirty(glint); EditorUtility.SetDirty(chips);

        var impact = RootObject(PelagVfxId.AnchorLeapLand, "VFX_AnchorLeap_Land", .62f);
        AddFlipbook(impact,"Contact flash",core,1.45f,false,Vector3.up*.13f,3,4,.14f);
        LeapShards(impact,glint,7,true);
        LeapShards(impact,chips,11,false);
        LeapDust(impact,dust,4,.80f,.47f,1.8f);
        prefabs[(int)PelagVfxId.AnchorLeapLand] = Save(impact,"VFX_AnchorLeap_Land");

        var landing = RootObject(PelagVfxId.AnchorLeapLanding, "VFX_AnchorLeap_Landing", .72f);
        LeapDust(landing,dust,6,1.12f,.64f,2.2f);
        LeapShards(landing,chips,6,false);
        prefabs[(int)PelagVfxId.AnchorLeapLanding] = Save(landing,"VFX_AnchorLeap_Landing");

        var strokeShader=Shader.Find("Razlom/Leap Stroke");
        Material stroke=LoadOrCreateMaterial(MaterialFolder+"/M_LeapStroke.mat",strokeShader);
        stroke.SetColor("_BaseColor",new Color(.55f,.09f,.11f,1f));
        stroke.SetColor("_CoreColor",new Color(1f,.88f,.67f,1f));
        stroke.SetFloat("_Opacity",1f);stroke.SetFloat("_Phase",0f);
        stroke.SetFloat("_CoreWidth",.07f);
        stroke.SetFloat("_Emission",1.8f); EditorUtility.SetDirty(stroke);
        Material pressure=LoadOrCreateMaterial(MaterialFolder+"/M_LeapPressure.mat",strokeShader);
        pressure.CopyPropertiesFromMaterial(stroke);
        pressure.SetColor("_BaseColor",new Color(.70f,.13f,.13f,1f));
        pressure.SetColor("_CoreColor",new Color(1f,.94f,.84f,1f));
        pressure.SetFloat("_Emission",2.2f);pressure.SetFloat("_CoreWidth",.15f);
        EditorUtility.SetDirty(pressure);

        // Острые полосы воздуха остаются позади корпуса и подчёркивают направление тяги.
        var flight = RootObject(PelagVfxId.AnchorLeapFlight, "VFX_AnchorLeap_Flight", .30f);
        LeapSpeedStrokes(flight,stroke);
        prefabs[(int)PelagVfxId.AnchorLeapFlight] = Save(flight,"VFX_AnchorLeap_Flight");

        // Два слоя читаются как летящий тяжёлый металл с острым световым краем.
        var anchor = prefabs[(int)PelagVfxId.AnchorLeapThrow];
        string path = AssetDatabase.GetAssetPath(anchor);
        var editable = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach(var trail in editable.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.sharedMaterial=stroke;
                trail.time=.27f; trail.startWidth=.95f; trail.endWidth=.04f;
                trail.widthCurve=new AnimationCurve(new Keyframe(0,0f),new Keyframe(.18f,1f),new Keyframe(1,0));
                trail.widthMultiplier=.95f;
                trail.startColor=Color.white; trail.endColor=new Color(1,1,1,0);
                trail.minVertexDistance=.015f; trail.numCapVertices=0;trail.numCornerVertices=4;
            }
            PrefabUtility.SaveAsPrefabAsset(editable,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(editable); }
    }

    private static void LeapSpeedStrokes(GameObject root,Material material)
    {
        var ps=LeapParticles(root,"Directional air blades",material,5,.27f);
        var main=ps.main; main.startSpeed=new ParticleSystem.MinMaxCurve(.8f,1.6f);
        main.startSize3D=true;
        main.startSizeX=new ParticleSystem.MinMaxCurve(.25f,.42f);
        main.startSizeY=1f;
        main.startSizeZ=new ParticleSystem.MinMaxCurve(.8f,1.6f);
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Box;
        shape.scale=new Vector3(1.2f,.55f,.2f); shape.rotation=new Vector3(0,180,0);
        var scale=ps.sizeOverLifetime; scale.enabled=true;
        scale.size=new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.15f,1),new Keyframe(1,.05f)));
        var renderer=ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode=ParticleSystemRenderMode.Mesh; renderer.mesh=LeapBladeMesh();
        renderer.alignment=ParticleSystemRenderSpace.Local;
    }

    private static Mesh LeapBladeMesh()
    {
        string path=Root+"/Geometry/LeapAirBlade.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
        mesh.Clear(); mesh.name="Leap Air Blade";
        const int count=40;
        var vertices=new Vector3[count*2]; var uv=new Vector2[count*2];
        var indices=new int[(count-1)*6];
        for(int i=0;i<count;i++)
        {
            float t=i/(float)(count-1);
            float width=Mathf.Sin(t*Mathf.PI)*.5f;
            float bend=Mathf.Sin(t*Mathf.PI)*.18f;
            vertices[i*2]=new Vector3(bend-width,0,-t);
            vertices[i*2+1]=new Vector3(bend+width,.08f*Mathf.Sin(t*Mathf.PI),-t);
            uv[i*2]=new Vector2(t,0); uv[i*2+1]=new Vector2(t,1);
            if(i==count-1) continue;
            int n=i*6,a=i*2;
            indices[n]=a;indices[n+1]=a+2;indices[n+2]=a+1;
            indices[n+3]=a+1;indices[n+4]=a+2;indices[n+5]=a+3;
        }
        mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;
        mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static ParticleSystem LeapParticles(GameObject root,string name,Material material,int count,float life)
    {
        var host=new GameObject(name); host.transform.SetParent(root.transform,false);
        var ps=host.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.loop=false; main.playOnAwake=false; main.duration=life;
        main.startLifetime=new ParticleSystem.MinMaxCurve(life*.78f,life);
        main.maxParticles=count; main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.scalingMode=ParticleSystemScalingMode.Hierarchy;
        var emission=ps.emission; emission.rateOverTime=0;
        emission.SetBursts(new[]{new ParticleSystem.Burst(0f,(short)count)});
        var color=ps.colorOverLifetime; color.enabled=true;
        var fade=new Gradient();
        fade.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.07f),new GradientAlphaKey(.8f,.45f),new GradientAlphaKey(0,1)});
        color.color=fade;
        var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        return ps;
    }

    private static void LeapDust(GameObject root,Material material,int count,float size,float lifetime,float speed)
    {
        var ps=LeapParticles(root,"Low painted dust",material,count,lifetime);
        ps.transform.localPosition=Vector3.up*.10f;
        var main=ps.main;
        main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.65f,speed);
        main.startSize=new ParticleSystem.MinMaxCurve(size*.75f,size*1.15f);
        main.startRotation=new ParticleSystem.MinMaxCurve(-.18f,.18f);
        var shape=ps.shape;
        shape.shapeType=ParticleSystemShapeType.Circle; shape.radius=.23f; shape.radiusThickness=.65f;
        shape.rotation=new Vector3(90,0,0);
        var velocity=ps.velocityOverLifetime; velocity.enabled=true;
        velocity.space=ParticleSystemSimulationSpace.World; velocity.y=.14f;
        var limit=ps.limitVelocityOverLifetime; limit.enabled=true;
        limit.limit=.9f; limit.dampen=.16f;
        var scale=ps.sizeOverLifetime; scale.enabled=true;
        scale.size=new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.25f,.8f),new Keyframe(1,1.2f)));
        // Четыре разных силуэта выбираются один раз, без перескакивания между ними.
        var sheet=ps.textureSheetAnimation; sheet.enabled=true; sheet.numTilesX=2; sheet.numTilesY=2;
        sheet.startFrame=new ParticleSystem.MinMaxCurve(0f,.999f); sheet.frameOverTime=0f;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.sortingOrder=0;
        renderer.flip=new Vector3(.5f,0,0);
    }

    private static void LeapShards(GameObject root,Material material,int count,bool sparks)
    {
        var ps=LeapParticles(root,sparks?"Metal streaks":"Earth chips",material,count,sparks?.22f:.48f);
        var main=ps.main;
        main.startSpeed=new ParticleSystem.MinMaxCurve(sparks?3.5f:1.7f,sparks?6f:3.1f);
        main.gravityModifier=sparks?.25f:1.6f;
        main.startSize3D=true;
        main.startSizeX=new ParticleSystem.MinMaxCurve(sparks?.023f:.10f,sparks?.040f:.21f);
        main.startSizeY=new ParticleSystem.MinMaxCurve(sparks?.020f:.06f,sparks?.028f:.12f);
        main.startSizeZ=new ParticleSystem.MinMaxCurve(sparks?.35f:.10f,sparks?.60f:.23f);
        main.startRotation3D=true;
        main.startRotationZ=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone;
        shape.angle=sparks?67f:58f; shape.radius=.13f; shape.rotation=new Vector3(-90,0,0);
        var scale=ps.sizeOverLifetime; scale.enabled=true;
        scale.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,1,1,.25f));
        var renderer=ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode=ParticleSystemRenderMode.Mesh; renderer.mesh=ImpactChipMesh();
        renderer.alignment=ParticleSystemRenderSpace.Velocity;
    }
}
