using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>Материалы воды Абордажа v2 (см. PelagAbordageFoamVfxSetup.cs). Любая правка — поднять Version там.</summary>
public static partial class PelagAbordageFoamVfxSetup
{
    /// <summary>Материал с нуля при сохранении ассета и GUID (как у Шквала v2 и серии сабли).</summary>
    private static Material Fresh(string name, Shader shader)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(name), shader);
        var clean = new Material(shader);
        EditorUtility.CopySerialized(clean, material);
        Object.DestroyImmediate(clean);
        material.name = name;
        return material;
    }

    /// <summary>Общий язык воды: цвета базы, пена, обвод, текстуры CFXR. Формы красит вид блоком.</summary>
    private static Material WaterBase(string name)
    {
        Material material = Fresh(name, Shader.Find(WaterShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", Deep);
        material.SetColor("_Water", Water);
        material.SetColor("_Shallow", Shallow);
        material.SetColor("_Foam", Foam);
        material.SetColor("_FoamShade", FoamShade);
        material.SetColor("_Outline", Outline);
        material.SetFloat("_CoarseScale", .5f);
        material.SetFloat("_ClumpScale", .85f);
        material.SetFloat("_EndRag", .28f);
        material.SetFloat("_OutlinePx", 2f);
        material.SetFloat("_Glow", .2f);
        material.SetFloat("_BreakScale", 2.2f);
        material.SetFloat("_DropLife", .07f);
        material.SetFloat("_EdgeEarly", .03f);
        material.SetFloat("_BreakRimPx", 1.5f);
        // Рвётся на 0,30 (вид ведёт возраст), белеет пеной за 0,10 до трещин; страховка 0,40–0,46.
        material.SetVector("_Break", new Vector4(.30f, .05f, 0f, .10f));
        material.SetFloat("_FadeFrom", .40f);
        material.SetFloat("_FadeTo", .46f);
        material.SetVector("_Lines", new Vector4(4.5f, 1.6f, .40f, .85f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.6f, .32f, .45f));
        material.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        return material;
    }

    /// <summary>
    /// Лента вдоль цепи и росчерк якоря: тонкая нить воды (3–7 см полуширины) — комья
    /// мелкие, обвод тоньше, струи две; лежит в воздухе, поверх препятствий не ложится
    /// (COLOR.r = 0 — прежний допуск 0,3 м), рвётся на капли за ~0,2 с после удара.
    /// </summary>
    private static Material RibbonMaterial()
    {
        Material material = WaterBase(RibbonMaterialName);
        material.SetVector("_Crest", new Vector4(.03f, .02f, .008f, 1f));
        material.SetVector("_Bands", new Vector4(.10f, .55f, 0f, 0f));
        material.SetFloat("_ClumpScale", 1.6f);
        material.SetFloat("_EndRag", .06f);
        material.SetFloat("_OutlinePx", 1.2f);
        material.SetFloat("_Glow", .25f);
        material.SetVector("_Lines", new Vector4(2f, 1.2f, .55f, .70f));
        material.SetVector("_DarkLines", new Vector4(2f, 1.2f, .45f, .25f));
        material.SetFloat("_BreakScale", 5f);
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Вода на земле — кольцо Обвала, кольцо падения Гейзера, струя Пробоины (рецепт кольца
    /// Пенных волн v4, waves-2): толстая полоса с градиентом глубины к светлому внешнему
    /// краю, крупные комья пены по обоим краям, рвётся разом на крупные капли; ложится
    /// поверх низких препятствий (корни, кочки), поверх тел — никогда.
    /// </summary>
    private static Material GroundMaterial(string name = GroundMaterialName)
    {
        Material material = WaterBase(name);
        material.SetVector("_Break", new Vector4(.30f, .04f, 0f, .06f));
        material.SetVector("_Crest", new Vector4(.17f, .13f, .035f, .70f));
        material.SetVector("_Bands", new Vector4(.15f, .75f, .30f, .30f));
        material.SetVector("_Across", new Vector4(.85f, 1f, 1f, 0f));
        material.SetFloat("_ClumpScale", .48f);
        material.SetFloat("_CoarseScale", .45f);
        material.SetFloat("_BreakScale", 1.3f);
        material.SetFloat("_DropLife", .08f);
        material.SetVector("_Lines", new Vector4(5f, 1.6f, .32f, .85f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.2f, .30f, .30f));
        material.SetVector("_Over", new Vector4(1f, .25f, .45f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Столб Гейзера (кадр F): вертикальная вода к камере — гребни пены по обоим краям
    /// одинаковые, струи бегут вверх (вид сдвигает шум), верх — рваный открытый конец
    /// (шапка пены), рвётся на капли, когда вода падает. Поперёк — от глубокой воды к светлой
    /// (_Across.x, как у воды на земле): столб читается объёмом, а не плоской полосой (ревью 03.10).
    /// Круг 2: струи ярче, шире и длиннее (шум столба вытянут вверх в PelagAbordageColumnWater),
    /// тёмные струи контрастнее, пена кроны светится чуть сильнее — блики падающей воды.
    /// Круг 3 (ревью 03.10: ствол бледный и тонкий — белая пена краёв съедала две трети ширины, тёмный
    /// бок): гребень по краям тонкий (0,035 м, рваность 0,02 — PelagAbordageVfxRules.GeyserTrunkFoam),
    /// глубина поперёк слабее (.25), вода к краям светлее, светлых струй больше и они сильнее, тёмные — мягче.
    /// </summary>
    private static Material ColumnMaterial()
    {
        Material material = WaterBase(ColumnMaterialName);
        material.SetVector("_Across", new Vector4(.25f, 1f, 1f, 0f));
        material.SetVector("_Crest", new Vector4(.035f, .03f, .02f, 1f));
        material.SetVector("_Bands", new Vector4(-.2f, .7f, 0f, 0f));
        material.SetFloat("_ClumpScale", .70f);
        material.SetFloat("_EndRag", .32f);
        material.SetFloat("_Glow", .30f);
        material.SetVector("_Lines", new Vector4(6f, 2.6f, .35f, 1f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.4f, .40f, .30f));
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Круг 4 — всплеск Обвала (кадр D; ревью в игре: «солнце» с пилой и чёрным обводом, сиреневая полоса,
    /// распад белил диск): свой шейдер Razlom/Abordage Quake Splash. Лопасти кобальта (цвета — блоком формы
    /// PelagAbordageFormLook) с плоскими светлыми пятнами лучами, белая пена на кончиках круглыми комьями и
    /// пятнами во внешней половине, без обвода; мокрая земля у героя непрозрачна (бурая, как в D: тёмная
    /// .27/.17/.11, средняя .41/.27/.18, светлые прожилки .55/.38/.25 — темнее и глуше охры пола). Распад —
    /// дырами от центра наружу, кайма пены рвётся на капли; числа — PelagAbordageVfxRules (Quake*).
    /// Круг 5: земля — мокрая грязь короткими пятнами трёх тонов по месту и белыми каплями (не «доски» со
    /// светлыми полосами вдоль луча), кромка дыр и страховка — жёсткий срез без полупрозрачного кобальта.
    /// </summary>
    private static Material QuakeMaterial()
    {
        Material material = Fresh(QuakeMaterialName, Shader.Find(QuakeShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        PelagAbordageFormLook.Palette cobalt = PelagAbordageFormLook.For(PelagForm.AbordageQuake);
        material.SetColor("_Deep", cobalt.Deep);
        material.SetColor("_Water", cobalt.Water);
        material.SetColor("_Shallow", cobalt.Shallow);
        material.SetColor("_Foam", new Color(1.10f, 1.12f, 1.16f));
        material.SetColor("_FoamShade", new Color(.70f, .80f, .98f));
        material.SetColor("_EarthDark", new Color(.27f, .17f, .11f));
        material.SetColor("_Earth", new Color(.41f, .27f, .18f));
        material.SetColor("_EarthLight", new Color(.55f, .38f, .25f));
        material.SetVector("_Patch", new Vector4(.9f, .56f, .75f, .75f));
        material.SetVector("_Crest", new Vector4(.44f, .14f, 2f, .9f));
        // Круг 5: земля — мокрая грязь пятнами по месту и белые капли (полос вдоль луча нет), кромка дыр — жёсткий срез.
        material.SetVector("_EarthEdge", new Vector4(.10f, .06f, 0f, 0f));
        material.SetVector("_Mud", new Vector4(PelagAbordageVfxRules.QuakeMudScale, PelagAbordageVfxRules.QuakeMudFineScale,
            PelagAbordageVfxRules.QuakeMudDropScale, PelagAbordageVfxRules.QuakeMudDropCut));
        material.SetVector("_MudTones", new Vector4(PelagAbordageVfxRules.QuakeMudDark, PelagAbordageVfxRules.QuakeMudLight, 0f, 0f));
        material.SetVector("_Hole", new Vector4(PelagAbordageVfxRules.QuakeHoleStart, PelagAbordageVfxRules.QuakeHoleSweep,
            PelagAbordageVfxRules.QuakeHoleCut, 1.3f));
        material.SetFloat("_HoleLead", PelagAbordageVfxRules.QuakeHoleLead);
        material.SetVector("_RimBreak", new Vector4(PelagAbordageVfxRules.QuakeRimThinFrom, PelagAbordageVfxRules.QuakeRimDropsFrom,
            PelagAbordageVfxRules.QuakeRimDropsGone, 3.4f));
        material.SetFloat("_FadeFrom", PelagAbordageVfxRules.QuakeFadeFrom);
        material.SetFloat("_FadeTo", PelagAbordageVfxRules.QuakeFadeTo);
        material.SetVector("_Over", new Vector4(1f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Круг 4 — розетка у основания Гейзера (кадр F; ревью: тёмное пятно пола у ствола): рецепт воды на земле,
    /// но диск сплошной (PelagAbordageRingWater.Style.Rosette) и у ствола светлый — глубина поперёк слабая,
    /// внутренней кромки с обводом нет; пена — по внешнему краю лепестков.
    /// </summary>
    private static Material RosetteMaterial()
    {
        Material material = GroundMaterial(RosetteMaterialName);
        material.SetVector("_Across", new Vector4(.35f, 1f, 0f, 0f));
        material.SetVector("_Crest", new Vector4(.17f, .13f, .035f, .20f));
        material.SetVector("_Bands", new Vector4(.15f, .75f, .20f, .15f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Круг 4 — клубы пены Гейзера слитыми массами (ревью: «яйца» с обводом у каждого). Оба материала — свой
    /// шейдер Razlom/Abordage Foam Puff на мягкой капле пака. <paramref name="back"/> — силуэт: маска раздута на
    /// PelagAbordageVfxRules.GeyserFoamOutline, весь цвета обвода, очередь 3041 — все силуэты ложатся раньше
    /// всех заливок. Иначе — заливка: без обвода и без каймы, мягкая тень снизу бугра (свет сверху), очередь
    /// 3042. Порог растёт с возрастом одинаково — силуэт сходит вместе с клубом.
    /// </summary>
    private static Material FoamPuffMaterial(string name, bool back)
    {
        Material material = Fresh(name, Shader.Find(FoamPuffShaderName));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DropTexture));
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", new Color(.64f, .95f, .82f, 1f));
        material.SetFloat("_CutFrom", .24f);
        material.SetFloat("_CutTo", .82f);
        material.SetFloat("_CutPower", 2.2f);
        material.SetFloat("_Back", back ? 1f : 0f);
        material.SetFloat("_Dilate", PelagAbordageVfxRules.GeyserFoamOutline);
        material.SetFloat("_ShadeOffset", .045f);
        material.SetFloat("_ShadeSoft", .30f);
        material.SetFloat("_ShadeStrength", .85f);
        material.SetFloat("_CameraPush", .1f);
        material.renderQueue = back ? 3041 : 3042;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Круг 3 — кольцо падения Гейзера: тонкая тихая полоса морской зелени (полуширина 0,07 м) —
    /// показывает, куда упадёт вода, но не забивает кадр: гребень пены тонкий, комья мелкие, обвод тоньше,
    /// свечения почти нет, струй две.
    /// </summary>
    private static Material FallMaterial()
    {
        Material material = WaterBase(FallMaterialName);
        material.SetVector("_Break", new Vector4(.30f, .04f, 0f, .06f));
        material.SetVector("_Across", new Vector4(.5f, .75f, .5f, 0f));
        material.SetVector("_Crest", new Vector4(.03f, .02f, .012f, .3f));
        material.SetVector("_Bands", new Vector4(.15f, .75f, .30f, .30f));
        material.SetFloat("_ClumpScale", .9f);
        material.SetFloat("_CoarseScale", .45f);
        material.SetFloat("_BreakScale", 2.2f);
        material.SetFloat("_OutlinePx", 1.4f);
        material.SetFloat("_Glow", .1f);
        material.SetVector("_Lines", new Vector4(2f, 1.4f, .30f, .50f));
        material.SetVector("_DarkLines", new Vector4(1f, 1.6f, .30f, .20f));
        material.SetVector("_Over", new Vector4(1f, .25f, .45f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Круг 3 — комья Обвала (ревью 03.10: клякса рывка с растворением пака рвала их в крошку): гранёные
    /// камни пака CFXR «debris unlit 3x3» на шейдере пены семьи Razlom/Sabre Foam Blob. Порог постоянный
    /// (без растворения), грани текстуры (канал r в линейном: обод 0,21, скос 0,59, верх 1) дают: обод —
    /// тёмный обвод семьи, скос — тень, верх — цвет частицы (тёмная мокрая земля). Рисуется после пены и
    /// воды (очередь +43): ком не прячется под клочьями. Цвет блока формы (_Shade) лишь слегка холодит скос.
    /// </summary>
    private static Material ClodMaterial()
    {
        Material material = Fresh(ClodMaterialName, Shader.Find(BlobShaderName));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DebrisTexture));
        material.SetColor("_Outline", Outline);
        material.SetColor("_Shade", new Color(.62f, .55f, .48f, 1f));
        material.SetFloat("_CutFrom", .08f);
        material.SetFloat("_CutTo", .08f);
        material.SetFloat("_CutPower", 1f);
        material.SetFloat("_OutlineWidth", .18f);
        material.SetFloat("_ShadeWidth", .55f);
        material.SetFloat("_CameraPush", .35f);
        material.renderQueue = 3043;
        EditorUtility.SetDirty(material);
        return material;
    }
}
