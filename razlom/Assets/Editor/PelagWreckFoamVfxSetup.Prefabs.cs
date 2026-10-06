using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Префабы Крушения v2 (см. PelagWreckFoamVfxSetup.cs). Слои частиц — рецепт Абордажа v2 и Шквала v2:
/// ловушки частиц Unity учтены (кривые одного режима, без Float32-цветов меша, мировые координаты у
/// выбрасываемых, не больше 8 вспышек на систему). У слоёв, которые вид бросает расчётом полёта
/// (PelagWreckVfxRules.Launch: комья, пласты и капли взрыва Панциря), торможения нет — иначе они
/// упали бы ближе края урона. Любая правка — поднять Version в PelagWreckFoamVfxSetup.cs.
/// </summary>
public static partial class PelagWreckFoamVfxSetup
{
    /// <summary>Дуга якоря: корень без поворота и масштаба, меш «Water» пишет вид; «Drops», «Foam» — выбрасывает вид.</summary>
    private static void SaveArcPrefab(Material arc, Material foam, Material drop)
    {
        var root = new GameObject(ArcName);
        try
        {
            AddElement(root, PelagVfxId.WreckArc, 8f, 1f);
            MeshLayer(root, "Water", arc);
            ParticleSystem drops = Emitted(root, "Drops", 300, .45f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startSize = new ParticleSystem.MinMaxCurve(.035f, .07f);
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(drops);
            Drag(drops, 1.2f);
            Renderer(drops, drop, ParticleSystemRenderMode.Stretch, 1.4f, .03f);
            ParticleSystem bits = Emitted(root, "Foam", 80, .45f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .6f;
            bitsMain.startSize = new ParticleSystem.MinMaxCurve(.08f, .15f);
            bitsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(bits, .6f, 1f, .5f);
            Drag(bits, 3f);
            Renderer(bits, foam, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, ArcName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Гребень вала / стены / горба: корень — начало полосы на земле, меши «Crest» (стоячий гребень) и
    /// «Trail» (мокрый след) пишет вид; «Spray» — капли с губы (кадр A — выше пояса), «Foam» — клочья.
    /// </summary>
    private static void SaveCrestPrefab(Material crest, Material trail, Material foam, Material crownDrop)
    {
        var root = new GameObject(CrestName);
        try
        {
            AddElement(root, PelagVfxId.WreckCrest, 2f, 6f);
            MeshLayer(root, "Crest", crest);
            MeshLayer(root, "Trail", trail);
            ParticleSystem spray = Emitted(root, "Spray", 360, .55f);
            var sprayMain = spray.main;
            sprayMain.gravityModifier = 1.6f;
            sprayMain.startSize = new ParticleSystem.MinMaxCurve(.08f, .16f);
            sprayMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            DropSheet(spray);
            Drag(spray, 1f);
            Renderer(spray, crownDrop, ParticleSystemRenderMode.Stretch, 1.5f, .025f);
            ParticleSystem bits = Emitted(root, "Foam", 140, .5f);
            var bitsMain = bits.main;
            bitsMain.gravityModifier = .4f;
            bitsMain.startSize = new ParticleSystem.MinMaxCurve(.12f, .24f);
            bitsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bitsMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            SizeCurve(bits, .6f, 1f, .55f);
            Drag(bits, 2.5f);
            Renderer(bits, foam, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, CrestName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Круг удара оземь и обрушение стены: корень — центр на земле, меш «Ring» (кольцо до радиуса Sim)
    /// пишет вид; «Clods» — комья земли (бросает вид, падают внутри круга); «Splash» — разовая корона
    /// брызг стоячими каплями по кругу авторского радиуса 1,2 м (вид масштабирует узел по кругу Sim).
    /// </summary>
    private static void SaveSlamPrefab(Material crater, Material foam, Material crownDrop, Material clod)
    {
        var root = new GameObject(SlamName);
        try
        {
            AddElement(root, PelagVfxId.WreckSlam, 1.2f, 1.2f);
            MeshLayer(root, "Ring", crater);
            ParticleSystem clods = Emitted(root, "Clods", 40, .5f);
            var clodsMain = clods.main;
            clodsMain.gravityModifier = 2.4f;
            clodsMain.startSize = new ParticleSystem.MinMaxCurve(.18f, .30f);
            clodsMain.startColor = new ParticleSystem.MinMaxGradient(ClodDark, ClodLight);
            clodsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var clodsSheet = clods.textureSheetAnimation; clodsSheet.enabled = true;
            clodsSheet.mode = ParticleSystemAnimationMode.Grid;
            clodsSheet.numTilesX = 3; clodsSheet.numTilesY = 3;
            clodsSheet.animation = ParticleSystemAnimationType.WholeSheet;
            clodsSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            clodsSheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 8.99f);
            var clodsSpin = clods.rotationOverLifetime; clodsSpin.enabled = true;
            clodsSpin.z = new ParticleSystem.MinMaxCurve(-540f * Mathf.Deg2Rad, 540f * Mathf.Deg2Rad);
            Renderer(clods, clod, ParticleSystemRenderMode.Billboard, 0f, 0f);

            var splash = new GameObject("Splash");
            splash.transform.SetParent(root.transform, false);
            // Стоячие капли по кругу: узкий конус вверх с кромки круга (корона брызг кадра A).
            ParticleSystem crown = Burst(splash, "Crown", 30, .35f, .5f, 3.5f, 6f, .07f, .12f, .55f);
            var crownMain = crown.main;
            crownMain.gravityModifier = 1.8f;
            crownMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, DropAqua);
            Cone(crown, 14f, 1f, -90f, 0f);
            DropSheet(crown);
            Renderer(crown, crownDrop, ParticleSystemRenderMode.Stretch, 1.8f, .03f);
            ParticleSystem crownFoam = Burst(splash, "CrownFoam", 14, .40f, .55f, 1.5f, 3f, .14f, .24f, .6f);
            var crownFoamMain = crownFoam.main;
            crownFoamMain.gravityModifier = .8f;
            crownFoamMain.startColor = new ParticleSystem.MinMaxGradient(DropWhite, FoamWhite);
            Cone(crownFoam, 30f, .9f, -90f, 0f);
            Drag(crownFoam, 3f);
            SizeCurve(crownFoam, .6f, 1f, .5f);
            Renderer(crownFoam, foam, ParticleSystemRenderMode.Billboard, 0f, 0f);
            Save(root, SlamName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Водяной панцирь: корень — ноги героя, меш «Bands» (ленты вокруг силуэта) пишет вид; «Drops» —
    /// капли удара и взрыва, «Sheets» — пласты воды взрыва (вытянуты по скорости), «Spots» — мокрые
    /// пятна на земле, где упали пласты (лежат плашмя, тают).
    /// </summary>
    private static void SaveShellPrefab(Material shell, Material drop, Material crownDrop)
    {
        var root = new GameObject(ShellName);
        try
        {
            AddElement(root, PelagVfxId.WreckShell, 6f, .72f);
            MeshLayer(root, "Bands", shell);
            Color pearl = new Color(.92f, .96f, 1f, 1f), pearlDeep = new Color(.78f, .86f, .94f, 1f);
            ParticleSystem drops = Emitted(root, "Drops", 200, .55f);
            var dropsMain = drops.main;
            dropsMain.gravityModifier = 1.6f;
            dropsMain.startSize = new ParticleSystem.MinMaxCurve(.05f, .09f);
            dropsMain.startColor = new ParticleSystem.MinMaxGradient(pearl, pearlDeep);
            DropSheet(drops);
            Renderer(drops, drop, ParticleSystemRenderMode.Stretch, 1.4f, .03f);
            ParticleSystem sheets = Emitted(root, "Sheets", 60, .55f);
            var sheetsMain = sheets.main;
            sheetsMain.gravityModifier = 1.6f;
            sheetsMain.startSize = new ParticleSystem.MinMaxCurve(.18f, .32f);
            sheetsMain.startColor = new ParticleSystem.MinMaxGradient(pearl, pearlDeep);
            DropSheet(sheets);
            Renderer(sheets, crownDrop, ParticleSystemRenderMode.Stretch, 1.8f, .02f);
            ParticleSystem spots = Emitted(root, "Spots", 24, .8f);
            var spotsMain = spots.main;
            spotsMain.gravityModifier = 0f;
            spotsMain.startSpeed = 0f;
            spotsMain.startSize = new ParticleSystem.MinMaxCurve(.32f, .52f);
            spotsMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            spotsMain.startColor = new ParticleSystem.MinMaxGradient(pearlDeep, pearl);
            SizeCurve(spots, .5f, 1f, .85f);
            var spotsFade = spots.colorOverLifetime; spotsFade.enabled = true;
            spotsFade.color = new ParticleSystem.MinMaxGradient(PelagWhirlwindVfxSetup.Gradient(Color.white, new Color(1f, 1f, 1f, 0f)));
            Renderer(spots, crownDrop, ParticleSystemRenderMode.HorizontalBillboard, 0f, 0f);
            Save(root, ShellName);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// Короткая трещина удара оземь: копия принятой линии раскола Рассекающего (главная линия, ответвления,
    /// щепки; длину ставит вид — PelagCleaveSplitView.Begin) с тёмным материалом M_Wreck_Crack. Кольцо ударной
    /// волны и белые осколки убраны: кольцо по земле — фигура Обвала Абордажа, свечение — не земля. Сам
    /// префаб Рассекающего не меняется (копия — Object.Instantiate без связи с ним).
    /// </summary>
    private static void SaveCrackPrefab(Material crack)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CleaveCrackPrefab);
        var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(CleaveCrackMaterial);
        if (source == null || sourceMaterial == null || crack == null) return;
        GameObject root = Object.Instantiate(source);
        root.name = CrackName;
        try
        {
            var element = root.GetComponent<PelagVfxElement>();
            if (element != null) element.Id = PelagVfxId.WreckCrack;
            var view = root.GetComponent<PelagCleaveSplitView>();
            if (view != null) view.Ring = null;
            foreach (string layer in new[] { "Ring", "Embers" })
            {
                Transform child = root.transform.Find(layer);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }
            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (renderer.sharedMaterial == sourceMaterial) renderer.sharedMaterial = crack;
            Save(root, CrackName);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
