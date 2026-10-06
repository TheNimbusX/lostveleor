using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>Материалы воды Крушения v2 (см. PelagWreckFoamVfxSetup.cs). Любая правка — поднять Version там.</summary>
public static partial class PelagWreckFoamVfxSetup
{
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

    /// <summary>Общий язык воды: цвета палитры, пена, обвод, текстуры CFXR. Формы красит вид блоком.</summary>
    private static Material WaterBase(string name, PelagWreckFormLook.Palette palette)
    {
        Material material = Fresh(name, Shader.Find(WaterShaderName));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(BubblesTexture));
        material.SetTexture("_ErodeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PerlinTexture));
        material.SetColor("_Deep", palette.Deep);
        material.SetColor("_Water", palette.Water);
        material.SetColor("_Shallow", palette.Shallow);
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
        // Рвётся на 0,30 (возраст ведёт вид), белеет пеной за 0,10 до трещин; страховка 0,40–0,46.
        material.SetVector("_Break", new Vector4(.30f, .05f, 0f, .10f));
        material.SetFloat("_FadeFrom", .40f);
        material.SetFloat("_FadeTo", .46f);
        material.SetVector("_Lines", new Vector4(4.5f, 1.6f, .40f, .85f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.6f, .32f, .45f));
        material.SetVector("_Across", new Vector4(0f, 1f, 1f, 0f));
        return material;
    }

    private static PelagWreckFormLook.Palette Base => PelagWreckFormLook.For(PelagForm.None);

    /// <summary>
    /// Дуга за головой якоря и вихрь Девятого вала: лента в воздухе 3–15 см полуширины — комья
    /// мельче, обвод тоньше; лежит в воздухе (COLOR.r = 0), рвётся на капли за ~0,2 с после удара.
    /// </summary>
    private static Material ArcMaterial()
    {
        Material material = WaterBase(ArcMaterialName, Base);
        material.SetVector("_Crest", new Vector4(.05f, .035f, .012f, 1f));
        material.SetVector("_Bands", new Vector4(.10f, .55f, 0f, 0f));
        material.SetFloat("_ClumpScale", 1.3f);
        material.SetFloat("_EndRag", .10f);
        material.SetFloat("_OutlinePx", 1.4f);
        material.SetFloat("_Glow", .22f);
        material.SetVector("_Lines", new Vector4(2f, 1.3f, .50f, .75f));
        material.SetVector("_DarkLines", new Vector4(2f, 1.3f, .40f, .30f));
        material.SetFloat("_BreakScale", 4f);
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Стоячий гребень (вал, стена Волнореза, горб Девятого вала; кадры A, B, C): профиль от земли за
    /// спиной (−1, глубокая вода) к губе (+1, светлая вода и толстая рваная пена гребня); струи бегут
    /// к губе (вид сдвигает шум), концы рваные. Вертикальный — поверх препятствий не ложится.
    /// </summary>
    private static Material CrestMaterial()
    {
        Material material = WaterBase(CrestMaterialName, Base);
        material.SetVector("_Across", new Vector4(.65f, 1f, 1f, .35f));
        material.SetVector("_Crest", new Vector4(.18f, .12f, .035f, .55f));
        material.SetVector("_Bands", new Vector4(.15f, .75f, .30f, .35f));
        material.SetFloat("_ClumpScale", .6f);
        material.SetFloat("_EndRag", .30f);
        material.SetFloat("_Glow", .22f);
        material.SetVector("_Lines", new Vector4(5f, 1.8f, .40f, .90f));
        material.SetVector("_DarkLines", new Vector4(3f, 2.4f, .35f, .40f));
        material.SetFloat("_BreakScale", 1.8f);
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Вода на земле: мокрый след вала (<paramref name="trail"/> — кромки полосы одинаковые, это края
    /// урона) и кольцо круга удара / обрушения (рецепт воды на земле Абордажа: градиент к светлому
    /// внешнему краю). Ложится поверх низких препятствий (корни, кочки), поверх тел — никогда.
    /// </summary>
    private static Material GroundMaterial(string name, bool trail)
    {
        Material material = WaterBase(name, Base);
        material.SetVector("_Break", new Vector4(.30f, .04f, 0f, .06f));
        material.SetVector("_Crest", trail ? new Vector4(.12f, .09f, .03f, 1f) : new Vector4(.17f, .13f, .035f, .70f));
        material.SetVector("_Bands", new Vector4(.15f, .75f, .30f, .30f));
        material.SetVector("_Across", trail ? new Vector4(0f, 1f, 1f, 0f) : new Vector4(.85f, 1f, 1f, 0f));
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
    /// Водяной панцирь (кадр D): жемчужные ленты вокруг героя — пена мягче и не ярче тела (без
    /// свечения, razlom-no-character-whitening), обвод тонкий и светлее, внутренний край ленты
    /// полупрозрачный и без обвода. По телу вода не рисуется (трафарет тел).
    /// </summary>
    private static Material ShellMaterial()
    {
        Material material = WaterBase(ShellMaterialName, PelagWreckFormLook.For(PelagForm.WreckShell));
        material.SetColor("_Foam", new Color(1f, 1.03f, 1.05f));
        material.SetColor("_FoamShade", new Color(.74f, .82f, .90f));
        material.SetColor("_Outline", new Color(.10f, .14f, .20f, .75f));
        material.SetFloat("_OutlinePx", 1.2f);
        material.SetFloat("_Glow", 0f);
        material.SetVector("_Across", new Vector4(.30f, .60f, 0f, .30f));
        material.SetVector("_Crest", new Vector4(.05f, .04f, .015f, .80f));
        material.SetVector("_Bands", new Vector4(.10f, .60f, .20f, .20f));
        material.SetFloat("_ClumpScale", 1.1f);
        material.SetFloat("_EndRag", .12f);
        material.SetVector("_Lines", new Vector4(2.5f, 1.2f, .45f, .60f));
        material.SetVector("_DarkLines", new Vector4(2f, 1.2f, .40f, .20f));
        material.SetFloat("_BreakScale", 3f);
        material.SetVector("_Over", new Vector4(.30f, .25f, 0f, .30f));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Короткая трещина удара оземь (кадр A: тёмные трещины в земле у точки удара): копия материала линии
    /// раскола Рассекающего (Razlom/Whirlwind Sweep, раскадровка по возрасту частицы, маска — настоящий
    /// ассет) в тонах земли — щель тёмная, излом светлее, без свечения и линий скорости. Чужой материал не
    /// меняется.
    /// </summary>
    private static Material CrackMaterial(Material source)
    {
        Material material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialPath(CrackMaterialName), source.shader);
        EditorUtility.CopySerialized(source, material);
        material.name = CrackMaterialName;
        material.SetColor("_Core", new Color(.035f, .022f, .014f));
        material.SetColor("_Mid", new Color(.09f, .058f, .036f));
        material.SetColor("_Edge", new Color(.16f, .105f, .066f));
        material.SetColor("_Rim", new Color(.05f, .032f, .02f));
        material.SetFloat("_Glow", 0f);
        material.SetFloat("_Streaks", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
