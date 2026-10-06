using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Материалы и префабы Броска якоря (см. PelagAnchorThrowFoamVfxSetup.cs). Рецепт частиц — как у Абордажа v2
/// и Шквала v2 (кривые одного режима, мировые координаты у выбрасываемых, без Float32-цветов меша).
/// Любая правка — поднять Version там.
/// </summary>
public static partial class PelagAnchorThrowFoamVfxSetup
{
    // ----------------------------------------------------------------- материалы

    /// <summary>Материал с нуля при сохранении ассета и GUID (как у Абордажа v2 и Шквала v2).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    private static Material WaterBase(string name, Color deep, Color water, Color shallow)
    {
        Material material = Fresh(name, Shader.Find(WaterShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", deep);
        material.SetColor("_Water", water);
        material.SetColor("_Shallow", shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetFloat("_CoarseScale", .5f);
        material.SetFloat("_EndRag", .28f);
        material.SetFloat("_OutlinePx", 2f);
        material.SetFloat("_Glow", .2f);
        material.SetFloat("_DropLife", .07f);
        material.SetFloat("_EdgeEarly", .03f);
        material.SetFloat("_BreakRimPx", 1.5f);
        material.SetFloat("_FadeFrom", .40f);
        material.SetFloat("_FadeTo", .46f);
        material.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        return material;
    }

    /// <summary>
    /// Лист сети Невода (кадр B-net): кобальтовая вода на земле, по краям — толстый белый гребень
    /// (видимый край = край урона 1,5 м), поперёк светлеет к краям, ложится поверх низких препятствий
    /// (корни, кочки), поверх тел — никогда; рвётся разом на крупные капли после ловли.
    /// </summary>
    private static Material NetMaterial()
    {
        Material material = WaterBase(NetMaterialName, NetDeep, NetWater, NetShallow);
        material.SetVector("_Break", new Vector4(.30f, .05f, 0f, .08f));
        material.SetVector("_Crest", new Vector4(.16f, .10f, .035f, .80f));
        material.SetVector("_Bands", new Vector4(.20f, .80f, .30f, .35f));
        material.SetVector("_Across", new Vector4(-.55f, .92f, 1f, .35f));
        material.SetFloat("_ClumpScale", .55f);
        material.SetFloat("_BreakScale", 1.4f);
        material.SetVector("_Lines", new Vector4(5f, 1.6f, .32f, .65f));
        material.SetVector("_DarkLines", new Vector4(4f, 2.2f, .30f, .40f));
        material.SetVector("_Over", new Vector4(1f, .25f, .45f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Нити и узлы сети: узкие полосы белой пены с голубой тенью и тонким тёмным обводом — комья гребня
    /// съедают почти всю ширину нити, узел — круглый ком. Цвета свои (вид не красит нити формой).
    /// </summary>
    private static Material StrandMaterial()
    {
        Material material = WaterBase(StrandMaterialName, new Color(.30f, .42f, .85f), new Color(.80f, .88f, 1f), new Color(.95f, .97f, 1f));
        material.SetVector("_Break", new Vector4(.30f, .05f, 0f, .06f));
        material.SetVector("_Crest", new Vector4(.035f, .03f, .012f, 1f));
        material.SetVector("_Bands", new Vector4(.10f, .55f, 0f, 0f));
        material.SetFloat("_ClumpScale", 2.2f);
        material.SetFloat("_EndRag", .05f);
        material.SetFloat("_OutlinePx", 1.4f);
        material.SetFloat("_Glow", .3f);
        material.SetFloat("_BreakScale", 5f);
        material.SetVector("_Lines", new Vector4(1f, 1f, .5f, 0f));
        material.SetVector("_DarkLines", new Vector4(1f, 1f, .5f, 0f));
        material.SetVector("_Over", new Vector4(1f, .25f, .6f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Призрак Веера (кадр C-fan): наша голова якоря из воды индиго — сквозь тело видно землю, по кромке
    /// силуэта белая пена, растворяется пеной сверху вниз (шейдер пенного двойника Шквала, не меняется).
    /// </summary>
    private static Material GhostMaterial()
    {
        Material material = Fresh(GhostMaterialName, Shader.Find(GhostShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", FanDeep);
        material.SetColor("_Water", FanWater);
        material.SetColor("_Shallow", FanShallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", new Color(.74f, .72f, .98f));
        material.SetFloat("_BodyAlpha", .55f);
        material.SetFloat("_RimAlpha", .95f);
        material.SetVector("_Rim", new Vector4(.40f, .78f, 0f, 0f));
        material.SetFloat("_FoamScale", 3.2f);
        material.SetFloat("_FlowSpeed", .5f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ----------------------------------------------------------------- префабы

    /// <summary>
    /// Сеть Невода: корень — рука на земле (без поворота и масштаба), меши «Water» (лист, PelagAnchorThrowNetWater)
    /// и «Strands» (нити и узлы, PelagAnchorThrowNetStrands) пишет вид. «Foam» — комья пены узлов у ног пойманных,
    /// «Drops» — капли с краёв и валика; выбрасывает вид.
    /// </summary>
    private static void SaveNetPrefab(Material net, Material strand, Material foam, Material drop)
    {
        var root = new GameObject(NetName);
        try
        {
            AddElement(root, PelagVfxId.AnchorThrowNet, 6f, 1.5f);
            MeshLayer(root, "Water", net);
            MeshLayer(root, "Strands", strand);

            ParticleSystem knots = Emitted(root, "Foam", 120, .55f);
            var knotsMain = knots.main;
            knotsMain.gravityModifier = .5f;
            knotsMain.startSize = new ParticleSystem.MinMaxCurve(.12f, .22f);
            knotsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            knotsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(knots, .6f, 1f, .55f);
            Drag(knots, 3f);
            Renderer(knots, foam, ParticleSystemRenderMode.Billboard);

            ParticleSystem drops = Emitted(root, "Drops", 260, .42f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startSize = new ParticleSystem.MinMaxCurve(.04f, .08f);
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropBlue);
            DropSheet(drops);
            Drag(drops, 1.5f);
            Renderer(drops, drop, ParticleSystemRenderMode.Stretch);
            var dropsRenderer = drops.GetComponent<ParticleSystemRenderer>();
            dropsRenderer.lengthScale = 1.4f;
            dropsRenderer.velocityScale = .03f;

            Save(root, NetName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Призрак Веера: «Head» — копия нашей головы якоря (PelagAppearanceProfile.AnchorHeadPrefab, тот же
    /// поворот и подгонка размера, что у головы пула — PelagVfxController.CreatePooledEffect) на материале
    /// призрака; «Head/Ring» — кольцо (Anchor_Attachment): от него идёт водяная цепь. Металла нет.
    /// «Burst» — брызги, когда призрак рассыпается у руки; выбрасывает вид.
    /// </summary>
    private static void SaveGhostPrefab(Material ghost, Material foam, Material drop)
    {
        var root = new GameObject(GhostName);
        try
        {
            AddElement(root, PelagVfxId.AnchorThrowGhost, 6f, 1f);
            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            PelagAppearanceProfile appearance = Resources.Load<PelagAppearanceProfile>(PelagAppearanceProfile.ResourcePath);
            if (appearance != null && appearance.AnchorHeadPrefab != null) BuildGhostHead(head.transform, appearance, ghost);
            else Debug.LogWarning("[anchor-throw-setup] Нет PelagAppearance.AnchorHeadPrefab — призрак Веера без меша.");

            ParticleSystem burst = Emitted(root, "Burst", 80, .45f);
            var burstMain = burst.main;
            burstMain.gravityModifier = 1.2f;
            burstMain.startSize = new ParticleSystem.MinMaxCurve(.05f, .10f);
            burstMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropIndigo);
            DropSheet(burst);
            Drag(burst, 1.5f);
            Renderer(burst, drop, ParticleSystemRenderMode.Stretch);
            var burstRenderer = burst.GetComponent<ParticleSystemRenderer>();
            burstRenderer.lengthScale = 1.4f;
            burstRenderer.velocityScale = .03f;

            ParticleSystem bits = Emitted(root, "Drops", 40, .45f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .6f;
            bitsMain.startSize = new ParticleSystem.MinMaxCurve(.08f, .14f);
            bitsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(bits, .6f, 1f, .5f);
            Drag(bits, 3f);
            Renderer(bits, foam, ParticleSystemRenderMode.Billboard);

            Save(root, GhostName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void BuildGhostHead(Transform head, PelagAppearanceProfile appearance, Material ghost)
    {
        GameObject model = Object.Instantiate(appearance.AnchorHeadPrefab, head, false);
        model.name = "Model";
        model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var bounds = new Bounds();
        bool first = true;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                Vector3 point = head.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        float fit = appearance.HeadSize / Mathf.Max(.001f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
        model.transform.localScale = Vector3.one * fit;
        model.transform.localPosition = -bounds.center * fit;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = ghost;
            r.sharedMaterials = materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        Transform attachment = null;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == "Anchor_Attachment") { attachment = t; break; }
        var ring = new GameObject("Ring");
        ring.transform.SetParent(head, false);
        ring.transform.localPosition = attachment != null ? head.InverseTransformPoint(attachment.position) : Vector3.back * (.38f * appearance.HeadSize);
    }

    // ----------------------------------------------------------------- частицы и слои

    /// <summary>Слой, который выбрасывает вид (EmitParams): мировые координаты, без своей эмиссии, в петле.</summary>
    private static ParticleSystem Emitted(GameObject root, string name, int max, float lifeMax)
    {
        ParticleSystem particles = PelagWhirlwindVfxSetup.NewParticles(root, name, max, lifeMax * .7f, lifeMax, 0f, 0f, .1f, .1f);
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = max;
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = 0f;
        var shape = particles.shape; shape.enabled = false;
        return particles;
    }

    private static void Drag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = new ParticleSystem.MinMaxCurve(20f);
        limit.drag = new ParticleSystem.MinMaxCurve(drag);
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    private static void SizeCurve(ParticleSystem particles, float start, float peak, float end)
    {
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, start), new Keyframe(.2f, peak), new Keyframe(1f, end)));
    }

    /// <summary>Лист капель пака 1×3: мягкий эллипс — у жёсткого порогу не из чего сделать обвод.</summary>
    private static void DropSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 1;
        sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(.40f);
        sheet.cycleCount = 1;
    }

    private static void Renderer(ParticleSystem particles, Material material, ParticleSystemRenderMode mode)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
        });
    }

    private static void MeshLayer(GameObject root, string name, Material material)
    {
        var layer = new GameObject(name);
        layer.transform.SetParent(root.transform, false);
        layer.AddComponent<MeshFilter>();
        var renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private static void AddElement(GameObject root, PelagVfxId id, float lifetime, float authoredRadius)
    {
        var element = root.AddComponent<PelagVfxElement>();
        element.Id = id;
        element.DefaultLifetime = lifetime;
        element.AuthoredRadius = authoredRadius;
    }

    private static void Save(GameObject root, string name) => PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
}
