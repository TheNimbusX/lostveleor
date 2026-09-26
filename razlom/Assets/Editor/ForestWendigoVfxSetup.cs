using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX лесного вендиго по утверждённым референсам Higgsfield
/// (ART/characters/act-1-enemies/forest-elite-wendigo/production/review/vfx_references,
/// ревизия claw-sync-r03; владелец 25.09: «реализовать точь-в-точь»).
///
/// V1–V5 — свои плоские диски и звёзды («до рефов как до луны»). V6 — куски
/// префабов Hovl/CFXR целиком: снежные текстуры Hovl дали белые крошки, клубы
/// CFXR — светлый мультяшный шар. V7 — материалы паков на наших эмиттерах:
/// комья и щепки — освещённые спрайты CFXR (debris 3x3, wood 3x3) в тёмной
/// земле, пыль — мягкий дым Hovl (Smoke.png) охрой с малой альфой, кратер —
/// трещины Hovl (Crack7) тёмной землёй поверх мягкого пятна, листья — CFXR
/// «Hit Leaves», лента когтей — плёнка меча CFXR (mask light) на
/// WendigoClawRibbon. Наш слой — состав, тонировка, направление и тайминг.
///
/// • Когти: свечение когтей на замахе; на контакте — три вложенных серпа
///   вокруг зверя (лента) и ОДНОВРЕМЕННО низкий веер комьев/щепок/листьев по
///   ходу удара, три борозды, короткая вспышка; комья падают и лежат.
/// • Приземление: комья вверх по баллистике, узкий столб и низкая юбка пыли,
///   листья следом, угольки, кратер.
/// • Отталкивание: из-под каждой стопы назад комья, тонкий столбик пыли,
///   борозда.
/// Красные метки рисует ForestWendigoCombatView шейдером Razlom/Wendigo Warning.
/// </summary>
public static class ForestWendigoVfxSetup
{
    private const string Revision = "WendigoVfxV10";
    private const string Root = "Assets/Resources/VFX/Wendigo";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrLeaves = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Nature/CFXR3 Hit Leaves A (Lit).prefab";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string CfxrTrailMaskLight = CfxrGraphics + "cfxr sword trail mask light.png";
    // Освещённые материалы CFXR в URP теряют цвет частиц (бледные), дым Hovl —
    // мягкие частицы без depth-текстуры (невидим): берём unlit CFXR и его же
    // размытые облака; трещины — текстура Hovl через одноканальный CFXR.
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string HovlCrackTexture = "Assets/Hovl Studio/HSFiles/Textures/Crack7.png";
    private const string SweepShaderName = "Razlom/Whirlwind Sweep";
    private const string GlowShaderName = "Razlom/Pelag Glow";

    /// <summary>
    /// V8: флипбуки из изолированных элементов Higgsfield (владелец 25.09: «решение
    /// 1 в 1 как на референсе», кредиты не проблема). Клипы на чёрном фоне
    /// сгенерированы по кадрам самих референсов, откеены по яркости в атласы
    /// (ART/…/forest-elite-wendigo/production/vfx_elements, scratch flipbook.py):
    /// серпы когтей 6×4 по 24 к/с, столб посадки 6×8, «свечи» отталкивания 6×8.
    /// Рисуются одной частицей-билбордом с раскадровкой по возрасту.
    /// </summary>
    private const string TextureFolder = Root + "/Textures";
    private const string ClawSheet = TextureFolder + "/Wendigo_ClawSheet.png";
    private const string LandingSkirtSheet = TextureFolder + "/Wendigo_LandingSkirtSheet.png";
    private const string LandingColumnSheet = TextureFolder + "/Wendigo_LandingColumnSheet.png";
    private const string TakeoffGroundSheet = TextureFolder + "/Wendigo_TakeoffGroundSheet.png";
    private const string TakeoffColumnSheet = TextureFolder + "/Wendigo_TakeoffColumnSheet.png";
    private const string CfxrCloudMaterial = CfxrGraphics + "cfxr smoke cloud x4 ab.mat";
    public const string ClawFlip = "VFX_Wendigo_ClawFlip";
    public const string LandingSkirt = "VFX_Wendigo_LandingSkirt";
    public const string LandingColumn = "VFX_Wendigo_LandingColumn";
    public const string TakeoffGround = "VFX_Wendigo_TakeoffGround";
    public const string TakeoffColumn = "VFX_Wendigo_TakeoffColumn";
    /// <summary>Кадр элемента снят камерой ~40°: слой на плоскости земли растягивается по глубине на 1/sin 40°.</summary>
    private const float GroundUnproject = 1.55f;
    /// <summary>Масштаб референса: зверь занимает 45 % высоты кадра 720 px при росте 2,3 м → кадр 5,1 × 9,1 м.</summary>
    private const float RefMetersPerPixel = 2.3f / 324f;

    public const string ClawGround = "VFX_Wendigo_ClawGround";
    public const string ClawGlow = "VFX_Wendigo_ClawGlow";
    public const string Landing = "VFX_Wendigo_Landing";
    public const string Takeoff = "VFX_Wendigo_Takeoff";

    private static readonly Color Ivory = new Color(1.55f, 1.42f, 1.12f);
    private static readonly Color Ember = new Color(1.25f, .38f, .08f);
    private static readonly Color SoilLight = new Color(.50f, .34f, .20f);
    private static readonly Color SoilDark = new Color(.24f, .15f, .09f);
    private static readonly Color Dust = new Color(.58f, .49f, .37f);
    private static readonly Color DustDark = new Color(.42f, .35f, .27f);
    private static readonly Color Olive = new Color(.55f, .60f, .30f);

    private sealed class Kit
    {
        public Mesh Star, Groove;
        public Material Clod, Bark, Dust, Crack, Leaf, Groove2, Ivory, IvoryGlow, Ember;
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
        };
    }

    [MenuItem("Разлом/Лесной вендиго/VFX: подключить")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Лесной вендиго/VFX: пересобрать")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Shader.Find(SweepShaderName) == null || AssetDatabase.LoadAssetAtPath<Material>(CfxrDebrisUnlit) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrSmokeBlurred) == null || AssetDatabase.LoadAssetAtPath<GameObject>(CfxrLeaves) == null)
        {
            Debug.LogWarning("[wendigo-vfx] Нет паков Hovl/CFXR или шейдера серпа, VFX вендиго не собран.");
            return;
        }
        if (!force && Built()) return;
        Build();
    }

    private static bool Built()
    {
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + LandingColumn + ".prefab");
        return importer != null && importer.userData == Revision
            && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + ClawFlip + ".prefab") != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder("Assets/Resources/VFX", "Wendigo");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        Shader sweep = Shader.Find(SweepShaderName);
        Shader glow = Shader.Find(GlowShaderName);
        Texture2D white = PelagWhirlwindVfxSetup.WhiteTexture();
        var kit = new Kit { Star = PelagWhirlwindVfxSetup.StarMesh() };
        kit.Groove = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/VFX/Pelag/Geometry/CleaveCrackBendL.asset");
        if (kit.Groove == null) kit.Groove = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/VFX/Pelag/Geometry/CleaveCrack.asset");

        kit.IvoryGlow = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Wendigo_IvoryGlow.mat", glow);
        kit.IvoryGlow.SetTexture("_BaseMap", white);
        kit.IvoryGlow.SetColor("_BaseColor", new Color(1.6f, 1.5f, 1.2f, 1f));
        kit.IvoryGlow.SetFloat("_Emission", 2.4f);
        EditorUtility.SetDirty(kit.IvoryGlow);
        kit.Ember = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/M_Wendigo_Ember.mat", glow);
        kit.Ember.SetTexture("_BaseMap", white);
        kit.Ember.SetColor("_BaseColor", Ember);
        kit.Ember.SetFloat("_Emission", 1.1f);
        EditorUtility.SetDirty(kit.Ember);
        kit.Ivory = Flat(sweep, "M_Wendigo_Ivory", Ivory, white);
        kit.Groove2 = Flat(sweep, "M_Wendigo_GroundMark", SoilDark, white);
        kit.Groove2.SetFloat("_Opacity", .7f);
        EditorUtility.SetDirty(kit.Groove2);
        // Материалы паков: комья и щепки — unlit-спрайты CFXR (цвет частиц), пыль —
        // размытые облака CFXR, трещины — Crack7 Hovl через одноканальную плёнку CFXR.
        kit.Clod = PackCopy("M_Wendigo_Clod", CfxrDebrisUnlit);
        kit.Bark = PackCopy("M_Wendigo_Bark", CfxrDebrisWood);
        kit.Dust = Plain(PackCopy("M_Wendigo_Dust", CfxrSmokeBlurred));
        kit.Crack = NoDissolve(PackCopy("M_Wendigo_Crack", CfxrTrailMaterial));
        var crackTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(HovlCrackTexture);
        if (crackTexture != null) kit.Crack.SetTexture("_MainTex", crackTexture);
        EditorUtility.SetDirty(kit.Crack);
        // Листья пака освещённые — в URP бледные; та же плёнка без освещения, цвет из частиц.
        var leafPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrLeaves);
        var leafRenderer = leafPrefab != null ? leafPrefab.GetComponent<ParticleSystemRenderer>() : null;
        if (leafRenderer != null && leafRenderer.sharedMaterial != null)
        {
            kit.Leaf = PackCopy("M_Wendigo_Leaf", AssetDatabase.GetAssetPath(leafRenderer.sharedMaterial));
            kit.Leaf.DisableKeyword("_CFXR_LIGHTING_ALL");
            kit.Leaf.DisableKeyword("_CFXR_LIGHTING_DIRECT");
            kit.Leaf.DisableKeyword("_CFXR_LIGHTING_INDIRECT");
            kit.Leaf.DisableKeyword("_NORMALMAP");
            kit.Leaf.DisableKeyword("_FADING_ON");
            if (kit.Leaf.HasProperty("_UseLighting")) kit.Leaf.SetFloat("_UseLighting", 0f);
            if (kit.Leaf.HasProperty("_UseSP")) kit.Leaf.SetFloat("_UseSP", 0f);
            EditorUtility.SetDirty(kit.Leaf);
        }
        // Лента когтей: плёнка меча пака с рисованной маской «light» (рваные концы), без dissolve.
        Material ribbon = NoDissolve(PackCopy("M_Wendigo_ClawRibbon", CfxrTrailMaterial));
        var maskLight = AssetDatabase.LoadAssetAtPath<Texture2D>(CfxrTrailMaskLight);
        if (maskLight != null) ribbon.SetTexture("_MainTex", maskLight);
        EditorUtility.SetDirty(ribbon);
        foreach (var stale in new[] { PrefabFolder + "/VFX_Wendigo_ClawSweep.prefab", MaterialFolder + "/M_Wendigo_CfxrTrail.mat",
            MaterialFolder + "/M_Wendigo_Soil.mat", MaterialFolder + "/M_Wendigo_SoilDark.mat",
            MaterialFolder + "/M_Wendigo_DustColumn.mat", "Assets/Resources/VFX/Pelag/Materials/M_Wendigo_ClawRibbon.mat" })
            if (AssetDatabase.LoadMainAssetAtPath(stale) != null) AssetDatabase.DeleteAsset(stale);

        // V9 (владелец 26.09: «старые примитивы убрать и листочки тоже, звёзды на пальцах убрать,
        // взлёт и приземление каловые по размеру»): остаются только три флипбука референса.
        // Серп лежит на плоскости земли: кадр элемента снят камерой ~40°, поэтому по
        // вертикали он растянут на 1/sin40° ≈ 1,55 — наша камера (48°) сожмёт его обратно,
        // а поворот по направлению удара становится честным со всех сторон.
        // V10 (владелец 26.09: прыжок «как будто поверх экрана, не на нашей земле», серп запаздывает):
        // посадка и отталкивание разрезаны на слой земли (юбка/кольцо, лежит на грунте) и
        // вертикальную вырезку (столб/«свечи», стоит на точке контакта); серп живёт 0,6 с и
        // стартует за 8 тиков до контакта (вью). Размеры — ячейка исходного кадра в метрах референса.
        SaveFlip(ClawFlip, ClawSheet, 6, 4, .6f, 1152f * RefMetersPerPixel * .65f, 576f * RefMetersPerPixel * .65f * GroundUnproject, 0f);
        SaveFlip(LandingSkirt, LandingSkirtSheet, 6, 8, 3.3f, 768f * RefMetersPerPixel * .8f, 317f * RefMetersPerPixel * .8f * GroundUnproject, 0f);
        SaveFlip(LandingColumn, LandingColumnSheet, 6, 8, 3.3f, 512f * RefMetersPerPixel * .8f, 475f * RefMetersPerPixel * .8f, .5f);
        SaveFlip(TakeoffGround, TakeoffGroundSheet, 6, 8, 2.8f, 768f * RefMetersPerPixel * .55f, 274f * RefMetersPerPixel * .55f * GroundUnproject, 0f);
        SaveFlip(TakeoffColumn, TakeoffColumnSheet, 6, 8, 2.8f, 563f * RefMetersPerPixel * .55f, 518f * RefMetersPerPixel * .55f, .5f);
        foreach (var stale in new[] { PrefabFolder + "/" + ClawGround + ".prefab", PrefabFolder + "/" + ClawGlow + ".prefab",
            PrefabFolder + "/" + Landing + ".prefab", PrefabFolder + "/" + Takeoff + ".prefab",
            PrefabFolder + "/VFX_Wendigo_LandingFlip.prefab", PrefabFolder + "/VFX_Wendigo_TakeoffFlip.prefab",
            MaterialFolder + "/M_Wendigo_Flip_LandingFlip.mat", MaterialFolder + "/M_Wendigo_Flip_TakeoffFlip.mat",
            MaterialFolder + "/M_Wendigo_Clod.mat", MaterialFolder + "/M_Wendigo_Bark.mat", MaterialFolder + "/M_Wendigo_Dust.mat",
            MaterialFolder + "/M_Wendigo_Crack.mat", MaterialFolder + "/M_Wendigo_Leaf.mat", MaterialFolder + "/M_Wendigo_GroundMark.mat",
            MaterialFolder + "/M_Wendigo_Ivory.mat", MaterialFolder + "/M_Wendigo_IvoryGlow.mat", MaterialFolder + "/M_Wendigo_Ember.mat",
            MaterialFolder + "/M_Wendigo_ClawRibbon.mat" })
            if (AssetDatabase.LoadMainAssetAtPath(stale) != null) AssetDatabase.DeleteAsset(stale);

        AssetDatabase.SaveAssets();
        var importer = AssetImporter.GetAtPath(PrefabFolder + "/" + LandingColumn + ".prefab");
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wendigo-vfx] Земля, приземление, отталкивание и свечение когтей собраны на материалах паков, ревизия " + Revision + ".");
    }

    // ----------------------------------------------------------- claw ground

    /// <summary>
    /// Земля под когтями: корень на земле в точке контакта, +X — по ходу удара,
    /// +Z — вверх. Узкий веер комьев и щепок вперёд-вверх, листья, низкая пыль
    /// по ходу, три борозды и короткая вспышка.
    /// </summary>
    private static void SaveClawGround(Kit kit)
    {
        var root = new GameObject(ClawGround);
        try
        {
            Clods(root, kit.Clod, "Clods", 16, 5.0f, 8.0f, .26f, .45f, 20f, Vector3.zero, Aim(new Vector3(1f, 0f, .55f)), 1.6f, 0f);
            Clods(root, kit.Clod, "Rocks", 5, 4.2f, 6.5f, .45f, .62f, 16f, Vector3.zero, Aim(new Vector3(1f, 0f, .45f)), 1.8f, 0f);
            Clods(root, kit.Bark, "Bark", 10, 5.5f, 8.5f, .16f, .28f, 26f, new Vector3(.1f, 0f, 0f), Aim(new Vector3(1f, 0f, .8f)), 1.3f, .02f);
            Leaves(root, kit, "Leaves", 8, new Vector3(.25f, 0f, .1f), Aim(new Vector3(1f, 0f, 1.1f)), 1.1f, .04f);
            Smoke(root, kit.Dust, "Dust", 6, 1.4f, .9f, 1.5f, 3.0f, 4.5f, 16f, .5f, new Vector3(.2f, 0f, .05f), Aim(new Vector3(1f, 0f, .35f)), 0f);
            Flash(root, kit, new Vector3(.25f, 0f, .12f), .7f, .09f, .02f);
            var lines = new (float angle, float length, float width, float y)[] { (12f, 1.1f, .3f, .22f), (0f, 1.25f, .34f, 0f), (-12f, 1.0f, .28f, -.22f) };
            for (int i = 0; i < lines.Length; i++)
            {
                var line = PelagWhirlwindVfxSetup.MeshChild(root, "Groove" + i, kit.Groove, kit.Groove2);
                line.transform.localPosition = new Vector3(-.15f, lines[i].y, .01f);
                line.transform.localRotation = Quaternion.Euler(0f, 0f, lines[i].angle);
                line.transform.localScale = new Vector3(lines[i].length, lines[i].width, 1f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + ClawGround + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------- claw glow

    private static void SaveClawGlow(Kit kit)
    {
        var root = new GameObject(ClawGlow);
        try
        {
            var glow = PelagWhirlwindVfxSetup.NewParticles(root, "Tips", 3, .62f, .62f, 0f, 0f, .30f, .40f);
            var main = glow.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = glow.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.34f, .05f, .16f);
            var size = glow.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .3f), new Keyframe(.5f, .8f), new Keyframe(.9f, 1.6f), new Keyframe(1f, 0f)));
            var noise = glow.noise; noise.enabled = true;
            noise.strength = .06f; noise.frequency = 3f;
            var renderer = glow.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = kit.Star;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = kit.IvoryGlow;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + ClawGlow + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // --------------------------------------------------------------- landing

    /// <summary>Приземление: корень на земле, +Z — вверх. Комья вверх, узкий столб и низкая юбка пыли, листья, угольки, кратер.</summary>
    private static void SaveLanding(Kit kit)
    {
        var root = new GameObject(Landing);
        try
        {
            // Столб, юбка, камни в столбе и угольки — во флипбуке посадки; здесь объёмные комья для глубины, листья и кратер.
            Clods(root, kit.Clod, "Clods", 14, 6.0f, 9.0f, .30f, .55f, 40f, Vector3.zero, Quaternion.identity, 1.7f, 0f);
            Clods(root, kit.Clod, "Rocks", 4, 5.0f, 7.5f, .60f, .85f, 30f, Vector3.zero, Quaternion.identity, 1.9f, 0f);
            Clods(root, kit.Bark, "Bark", 6, 6.0f, 8.0f, .18f, .30f, 42f, Vector3.zero, Quaternion.identity, 1.3f, .02f);
            Leaves(root, kit, "Leaves", 10, Vector3.zero, Quaternion.identity, 1.4f, .08f);
            Crater(root, kit, 3.2f, 4.2f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + Landing + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // --------------------------------------------------------------- takeoff

    /// <summary>Отталкивание одной стопы: +X — по ходу прыжка. Комья назад-вверх, узкий столбик пыли, угольки, борозда.</summary>
    private static void SaveTakeoff(Kit kit)
    {
        var root = new GameObject(Takeoff);
        try
        {
            // «Свечи», накат пыли и угольки — во флипбуке отталкивания (один на обе стопы); здесь комья назад и борозда.
            Clods(root, kit.Clod, "Clods", 8, 5.0f, 7.5f, .28f, .50f, 22f, Vector3.zero, Aim(new Vector3(-1f, 0f, .9f)), 1.6f, 0f);
            Clods(root, kit.Clod, "Rocks", 2, 4.2f, 6.0f, .55f, .72f, 18f, Vector3.zero, Aim(new Vector3(-1f, 0f, .8f)), 1.8f, 0f);
            Clods(root, kit.Bark, "Bark", 4, 5.5f, 8.0f, .16f, .26f, 28f, Vector3.zero, Aim(new Vector3(-1f, 0f, 1.1f)), 1.3f, .02f);
            Leaves(root, kit, "Leaves", 3, Vector3.zero, Aim(new Vector3(-1f, 0f, 1.2f)), .9f, .05f);
            Crater(root, kit, 1.5f, 4f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + Takeoff + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>Поворот, кладущий ось эмиссии (+Z системы) на направление в осях корня (+X удар, +Z вверх).</summary>
    private static Quaternion Aim(Vector3 direction) => Quaternion.FromToRotation(Vector3.forward, direction.normalized);

    /// <summary>
    /// Комья/щепки: освещённые спрайты пака (атлас 3×3, случайный кадр), тёмная
    /// земля, баллистика с гравитацией, столкновение с плоскостью корня, один
    /// отскок, лежат до конца жизни и гаснут.
    /// </summary>
    private static ParticleSystem Clods(GameObject root, Material material, string name, int count, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float cone, Vector3 at, Quaternion rotation, float gravity, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, 3.2f, 4.0f, speedMin, speedMax, sizeMin, sizeMax);
        particles.transform.localPosition = at;
        particles.transform.localRotation = rotation;
        var main = particles.main;
        main.startDelay = delay;
        main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(SoilLight, SoilDark);
        var emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .12f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var collision = particles.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, root.transform);
        collision.bounce = .22f;
        collision.dampen = .6f;
        collision.lifetimeLoss = 0f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.85f, 1f), new Keyframe(1f, 0f)));
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 3; sheet.numTilesY = 3;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f);
        sheet.cycleCount = 1;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Мягкая пыль Hovl: конус, рост размера, поворот, охра с малой альфой и плавным уходом.</summary>
    private static ParticleSystem Smoke(GameObject root, Material material, string name, int count, float life, float sizeMin, float sizeMax,
        float speedMin, float speedMax, float cone, float alpha, Vector3 at, Quaternion rotation, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, name, count, life * .8f, life, speedMin, speedMax, sizeMin, sizeMax);
        particles.transform.localPosition = at;
        particles.transform.localRotation = rotation;
        var main = particles.main;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(Dust.r, Dust.g, Dust.b, alpha), new Color(DustDark.r, DustDark.g, DustDark.b, alpha));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .15f;
        var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
        limit.dampen = .3f;
        limit.limit = new ParticleSystem.MinMaxCurve(speedMax * .3f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .45f), new Keyframe(.35f, 1f), new Keyframe(1f, 1.5f)));
        var over = particles.colorOverLifetime; over.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.85f, .3f), new GradientAlphaKey(0f, 1f) });
        over.color = gradient;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.6f, .6f);
        CloudSheet(particles);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -1f;
        return particles;
    }

    /// <summary>Узкая «свеча» пыли: частицы вытянуты по скорости, столбик держит форму, пока летит.</summary>
    private static void Plume(ParticleSystem particles)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.2f;
        renderer.velocityScale = .06f;
        var limit = particles.limitVelocityOverLifetime;
        limit.dampen = .18f;
    }

    /// <summary>Листья пака CFXR (оливковые), одна система из префаба без дочерних звёзд и линий.</summary>
    private static ParticleSystem Leaves(GameObject root, Kit kit, string name, int count, Vector3 at, Quaternion rotation, float scale, float delay)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrLeaves);
        var go = Object.Instantiate(prefab);
        for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = at;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(go);
        var particles = go.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false; main.playOnAwake = false;
        main.startDelay = delay;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startColor = new ParticleSystem.MinMaxGradient(Olive, new Color(.42f, .48f, .22f));
        if (main.duration < .2f) main.duration = .2f;
        var emission = particles.emission;
        emission.rateOverTime = 0f; emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (kit.Leaf != null) renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>Кратер: мягкое тёмное пятно (дым Hovl) и тёмные трещины Hovl на земле, держатся и гаснут к концу жизни.</summary>
    private static void Crater(GameObject root, Kit kit, float size, float life)
    {
        var patch = PelagWhirlwindVfxSetup.NewParticles(root, "Patch", 1, life, life, 0f, 0f, size, size);
        var patchMain = patch.main;
        patchMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        patchMain.startColor = new Color(SoilDark.r, SoilDark.g, SoilDark.b, .85f);
        var patchShape = patch.shape; patchShape.enabled = false;
        patch.transform.localPosition = new Vector3(0f, 0f, .012f);
        CloudSheet(patch);
        var cracks = PelagWhirlwindVfxSetup.NewParticles(root, "Cracks", 1, life, life, 0f, 0f, size * 1.15f, size * 1.15f);
        var cracksMain = cracks.main;
        cracksMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        cracksMain.startColor = new Color(.10f, .06f, .03f, 1f);
        var cracksShape = cracks.shape; cracksShape.enabled = false;
        cracks.transform.localPosition = new Vector3(0f, 0f, .02f);
        foreach (var ps in new[] { patch, cracks })
        {
            var over = ps.colorOverLifetime; over.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f) });
            over.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sortingFudge = 2f;
        }
        patch.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Dust;
        cracks.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Crack;
    }

    private static void Flash(GameObject root, Kit kit, Vector3 at, float size, float life, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, "Flash", 1, life, life, 0f, 0f, size, size);
        var main = particles.main;
        main.startRotation = 20f * Mathf.Deg2Rad;
        var emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)1) });
        var shape = particles.shape; shape.enabled = false;
        particles.transform.localPosition = at;
        var over = particles.sizeOverLifetime; over.enabled = true;
        over.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.5f, 1.15f), new Keyframe(1f, .3f)));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = kit.Star;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = kit.Ivory;
    }

    private static void Embers(GameObject root, Kit kit, int count, float size, float speedMin, float speedMax)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(root, "Embers", count, .9f, 1.4f, speedMin, speedMax, size * .7f, size);
        var main = particles.main;
        main.gravityModifier = -.12f;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(.04f, (short)count) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = .3f;
        var over = particles.sizeOverLifetime; over.enabled = true;
        over.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.6f, .8f), new Keyframe(1f, 0f)));
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .25f; noise.frequency = 1.5f;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = kit.Star;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = kit.Ember;
    }

    /// <summary>Атлас облаков CFXR 2×2: случайный кадр на частицу.</summary>
    private static void CloudSheet(ParticleSystem particles)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 2; sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        sheet.cycleCount = 1;
    }

    /// <summary>Без dissolve и без мягких частиц: в URP без depth-текстуры они гасят спрайт у земли.</summary>
    private static Material Plain(Material material)
    {
        NoDissolve(material);
        material.DisableKeyword("_FADING_ON");
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material NoDissolve(Material material)
    {
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // -------------------------------------------------------------- flipbooks

    /// <summary>
    /// Одна частица-билборд с атласом кадров: кадр идёт по возрасту линейно,
    /// цикл один; размер — ячейка атласа в метрах референса; pivotY поднимает
    /// спрайт так, чтобы точка контакта элемента легла на позицию частицы.
    /// </summary>
    private static void SaveFlip(string name, string sheetPath, int cols, int rows, float life, float width, float height, float pivotY)
    {
        var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        if (sheet == null) { Debug.LogWarning("[wendigo-vfx] Нет атласа " + sheetPath); return; }
        var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
        var standalone = importer != null ? importer.GetPlatformTextureSettings("Standalone") : null;
        // Шаблон проекта ставит Standalone-переопределение 2048: атлас 4080 px ужимался вдвое.
        if (importer != null && (!importer.alphaIsTransparency || importer.maxTextureSize < 4096 || importer.mipmapEnabled == false
            || importer.textureCompression != TextureImporterCompression.CompressedHQ || standalone.overridden
            || importer.npotScale != TextureImporterNPOTScale.None))
        {
            // Атлас не степень двойки: без npotScale=None Unity перетягивал 2868×3840 в 2048×4096.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            standalone.overridden = false;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();
            sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        }
        // У облаков CFXR включены dissolve и мягкие частицы (_FADING_ON): у земли
        // спрайт выцветал, серпы выходили бледными нитками. Флипбуку — чистая альфа.
        Material material = Plain(PackCopy("M_Wendigo_Flip_" + name.Substring("VFX_Wendigo_".Length), CfxrCloudMaterial));
        material.SetTexture("_MainTex", sheet);
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", 0f);
        EditorUtility.SetDirty(material);
        var root = new GameObject(name);
        try
        {
            var particles = PelagWhirlwindVfxSetup.NewParticles(root, "Frames", 1, life, life, 0f, 0f, 1f, 1f);
            var main = particles.main;
            main.startSize3D = true;
            main.startSizeX = width; main.startSizeY = height; main.startSizeZ = 1f;
            main.startRotation = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = particles.shape; shape.enabled = false;
            var anim = particles.textureSheetAnimation; anim.enabled = true;
            anim.numTilesX = cols; anim.numTilesY = rows;
            anim.animation = ParticleSystemAnimationType.WholeSheet;
            anim.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            anim.startFrame = 0f;
            anim.cycleCount = 1;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Local: поворот берётся у корня (вид ставит его лицом к камере с поворотом по направлению).
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.pivot = new Vector3(0f, pivotY, 0f);
            renderer.sharedMaterial = material;
            renderer.sortingFudge = -4f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------- materials

    /// <summary>Копия материала пака в нашей папке (перезаписывается при пересборке).</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        else EditorUtility.CopySerialized(source, material);
        material.name = name;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material Flat(Shader shader, string name, Color color, Texture2D mask)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        material.SetColor("_Core", color);
        material.SetColor("_Mid", color);
        material.SetColor("_Edge", color);
        material.SetColor("_Rim", color);
        material.SetFloat("_Head", 1f);
        material.SetFloat("_Erode", 0f);
        material.SetFloat("_Streaks", 0f);
        material.SetFloat("_Glow", 0f);
        material.SetFloat("_Flash", 1f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_Timed", 0f);
        material.SetTexture("_Mask", mask);
        EditorUtility.SetDirty(material);
        return material;
    }
}
