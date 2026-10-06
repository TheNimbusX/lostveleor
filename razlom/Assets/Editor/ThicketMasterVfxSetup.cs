using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ЭФФЕКТЫ АТАК ХОЗЯИНА ЧАЩИ (план artifacts/tools/wf/boss-vfx-plan.md §3, 02.10).
///
/// Собирает 35 префабов (V17: терновник — стручок, куст, прорастание, выпуск, попадание, конец линии; ThicketMasterVfxSetup.Seeds.cs) в Resources/VFX/ThicketMaster/Attacks/Prefabs из паков —
/// CFXR (комья, щепки, пыль-облака, листья, плёнка лент когтей, искры, лепестки, сок,
/// ветровые штрихи), Hovl (маски трещин, колец и пятен земли, горб HalfSphere2,
/// столб CylinderFromGround, цветок Flower) — на своих копиях материалов в
/// Attacks/Materials: без освещения CFXR (в URP бледнеет), без dissolve и мягких
/// частиц у декалей (без depth-текстуры гаснут у земли); свои цвета и масштабы.
/// Корни и шипы — свои трубы в коре RootBark (как у Корнехвата и Вендиго), ягода —
/// своя икосфера на URP Particles/Lit. Наш слой — раскладка, тайминг, отклик.
///
/// Ревью 02.10 («плоско, по-наклеечному», «блекло»): волны рёва и топота — стоячие стены пыли,
/// бегущие наружу (Wall/Streaks/RadialPush), комья топота и лапы — меш-частицы своих
/// многогранников на непрозрачной M_Thicket_Earth (Chunks), нырок и выход прячут
/// тело за стеной земли по контуру корпуса, бугор тянет гребень земли, пыльца — ядовитый
/// объём с кромкой и пульсом укуса; ленты когтей строит вид по кости лапы (PawSlash).
///
/// Ревью 02.10, вечер: буря дольше (волны 90 / 75) и явнее — канал на всю бурю (StormChannel:
/// вихрь лепестков столбом вокруг тела, розовая аура у ног, кроны горят розовым золотом, луч с
/// крон вверх), вихрь по арене нарастает к каждой волне; ливень без луж — ни пятна сока на земле,
/// ни плоского шлепка пака, только брызги капель вверх и кусочки ягод.
///
/// Владелец 02.10, вечер: «смерть надо доработать» (в игре — тёмный ком, цветов с камеры не видно):
/// DeathBloom — вспышка и кольцо лепестков на добивании, волна цветения на касании боком; DeathHill —
/// холм встаёт, тело уходит под него, холм зацветает крупными цветами и лежит до смены арены.
///
/// Ревью 02.10, вечер (V8): пыль нырка, выхода, топота и смерти — низкие мелкие клубы наружу ≤ 0,8 с
/// (LowPuffs) вместо бежевых «ватных» стен до 2–3 м; объём — комья. След бугра — рыхлый гребень,
/// осыпается за ~1 с, без плоской бурой «тропинки». Буря — лепестков вдвое меньше, аура у ног — в поле
/// бури (круг укрытия у босса больше не розовый). Холм смерти — гора из бугров ~1,9 м, цветы без света.
///
/// Владелец 03.10, ночь (V9): «земля в холме после смерти и при ползании под землёй — слишком большие куски
/// примитивов». Земля, которую двигает босс (холм, бугор и его гребень, нырок, выход, комья смерти), — из земли
/// пола поляны (M_Thicket_Ground: фактура, яркость и тон LocationTheme Meadow, Particles/Lit — свет арены и тени на ней;
/// своей тени не отбрасывает: у Particles/Lit нет прохода ShadowCaster, объём — от света и формы):
/// гладкие сетки с шумом и сглаженными нормалями (HillMesh + HillPatchMesh, SwellMesh, LipMesh), обломки —
/// мелкие комья 4–20 см (ClodMesh, Clods), зерно (Grains), камешки ≤ 0,25 м (Stones). Топот и лапа — как были.
///
/// Круги «от тела» (лапа, топот и его кольцо, рёв, нырок) и круги касты (прорастание,
/// ливень, пыльца, свет бури) берутся из констант Simulation — ревизия включает их,
/// и смена радиуса в Sim пересобирает префабы сама.
///
/// Меню «Разлом/Босс/Хозяин Чащи/Собрать эффекты» — пересборка целиком (идемпотентна:
/// материалы и меши перезаписываются на месте). После компиляции — само, если сборки
/// нет или ревизия старая. В Play не собирает. В консоль — что собрано и чего не нашлось.
/// Звука нет.
/// </summary>
public static partial class ThicketMasterVfxSetup
{
    // V7 (02.10, вечер): смерть — «цветущий холм» заново (DeathBloom переделан, новый DeathHill).
    // V8 (02.10, ревью вечера «Смерть и эффекты»): холм — гора из бугров ~1,9 м без моховой шапки, цветы без
    // света (40), без тёплого облака; пыль низкая и короткая; след нырка — осыпающийся гребень; буря — вдвое
    // меньше лепестков, аура у ног ушла в поле бури.
    // V9 (03.10, ночь, владелец: «земля холма и бугра — слишком большие куски примитивов»): земля босса — гладкие
    // сетки с шумом на фактуре пола поляны (M_Thicket_Ground): холм — основание + лоскут на бугор, голова бугра —
    // один вал, гребень — гладкие валики, вал по контуру тела у нырка и выхода; обломки — комья 4–20 см, зерно,
    // камешки ≤ 0,25 м; мох — пятнами в цвете вершин.
    // V10 (03.10, ночь, проверка находок V9): бугры холма несут высоту над основанием (UV1) — вид оседает их по
    // точкам, а не по краю сетки (гора за героем стоит), места цветов, травы и ростков — бугор под собой (UV0),
    // они опускаются с ним; один тон частиц у основания и бугров, у головы и гребня бугра (без швов тона); вал
    // нырка и выхода замкнут без складки (шум по кругу).
    // V11 (03.10, «лес забирает хозяина»): холм — один мшистый пригорок 1,8 м (HillHeight) с тремя сливающимися горбами
    // (KnollTop): 19 частиц-ячеек одного рельефа (оседают у героя по отдельности, швов нет), мох — фактура травы
    // пола, земля — тонким кольцом у подножия и рваными пятнами; цветы со светом (нормали к чашке, как у куста Ф3)
    // кучками по 3–5 с листьями и травой между ними; тёплый точечный свет над пригорком. След бугра — сплошная
    // низкая борозда рыхлой земли (сетку пишет вид, FurrowChild) вместо цепочки валиков, траву рвёт вбок.
    // V12 (03.10, проверка находок V11): нормали ячеек мха — по видимому верху (контуры ячеек не рисуются трещинами в
    // свете), розетки листьев лежат по склону, цветы клонятся к нему наполовину (выравнивание по направлению места,
    // меши верхом на −Z), мелкие цветы — не на круче; кольцо земли ÷ свой тон вершин; своё свечение цветов 0,4 / 0,36
    // (гамма); вершина рельефа — ровно HillHeight 1,8 м.
    // V13 (владелец 03.10, утро: «холм после смерти, если по нему пройтись, оч коряво выглядит»): по пригорку ходят —
    // рельеф перенесён в ThicketMasterDeathRules (KnollSurface: его же берёт вид для пола тел), пригорок ниже и положе
    // (1,3 м, 6,2 × 7,8 м, склон ≤ ~34°, подножие сходит к полу без ступеньки), мох — одна сетка вместо 19 оседающих
    // ячеек, места цветов без UV бугров; цветов, травы и комьев — по площади больше.
    // V14 (владелец 04.10: «VFX закапывания и выкапывания… земля странно выглядит, как кольцо какое-то… нужно, чтобы
    // земля была фактурная, а не гладкая плоская»): гладкого вала LipMesh и симметричных декалей Crater40/Crater19 у
    // нырка и выхода больше нет. Земля — из паков и своей арены (ThicketMasterEarthRules): плиты дёрна (Cobble01–06
    // RPG Tiny Fantasy Forest, сверху дёрн пола CampTurf_v3, снизу каменистая земля CampStonyEarth_v1 — запечённый атлас),
    // камни поляны (MeadowPebble0/1), комья на каменистой земле (ClodMesh, NoiseSphere1 Hovl), зерно Debris2, пыль
    // SmokeAnim2 (8 × 8), рваные пятна разрытой земли (dirt_0–5 без пурпурной каймы, пятно 9e02a550), трещины
    // Crater43 / Crack4 / Crack6, крошка Crater18. Новый префаб — вздутие у точки выхода (EmergeBulge).
    // V15 (владелец 07.10: «нырок и выход отличный. а движение под землей — нет. все еще плоский шарик земляной»): голова
    // бугра — не гладкий вал SwellMesh, а каменистая куча на атласе плит (RubbleMoundMesh: дёрн на боках, порван по гребню,
    // комья, запечённые плиты дёрна и большие комья) с плитами, встающими на боках, и тлеющим швом; за головой плиты дёрна
    // встают на дыбы и падают, с гребня катятся камни и комья, впереди бежит трещина; борозда — дёрн по краям и навал
    // каменистой земли (атлас плит), пятна разрытой земли вдоль пути. Нырок, выход, вздутие и холм смерти не менялись.
    // V16 (07.10, «Веер шипов-семян», контракт § 16): новые префабы веера — стручок с лентой и следом (SeedPod), замах у
    // куста (SeedSwell), выпуск (SeedLaunch), попадание (SeedHit), конец линии (SeedDrop); сетка стручка ThicketSeedPod,
    // материалы M_Thicket_SeedWood / SeedTip / SeedRibbon (ThicketMasterVfxSetup.Seeds.cs). Прочие префабы не менялись.
    // V17 (08.10, «Терновник», контракт § 17.2 — вместо веера): куст на полу (Bush: падуб поляны + шипы и стебли Шипомёта,
    // земля, листья, угольки — частицы ставит вид), земля рвётся на прорастании (BushSprout), выпуск по 4 линиям (BushLaunch);
    // стручок меньше (шип куста). Замах в кусте на груди (SeedSwell) и выпуск с груди (SeedLaunch) сняты — их префабы удаляет
    // удачная сборка (DeleteStaleSeedFan).
    // V18 (08.10, ход под землёй, контракт § 17.1, владелец: «всё ещё как будто холмик просто скользит по полу… без вау-эффекта
    // пробуривания»): каменистой кучи головы V15, пятна под ней и плит на её плечах нет — с головой не едет ничего жёсткого,
    // земля рвётся на месте (SaveMound: плиты и камни вырываются и падают, где встали, носовая волна, фонтан комьев, трещины
    // лучами от головы, низкий занавес пыли, рябь впереди, плиты на губах траншеи; пока стоит — «кипит»), траншея вместо
    // навала (вид, FurrowTrail). Сетки и места V15 (ThicketMoundRubble, ThicketMound*, ThicketCrackQuad) и материал пятна
    // снимает удачная сборка (DeleteStaleMoundHead).
    private const string RevisionBase = "ThicketVfxV18";
    private const string VfxFolder = "Assets/Resources/VFX";
    private const string BossFolder = VfxFolder + "/ThicketMaster";
    private const string Root = BossFolder + "/Attacks";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string Log = "[thicketmaster-vfx] ";

    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string CfxrMeshes = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Meshes/";
    private const string CfxrPrefabs = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
    private const string CfxrDebrisUnlit = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDebrisWood = CfxrGraphics + "cfxr debris wood unlit 3x3 ab.mat";
    private const string CfxrSmokeBlurred = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrCloudBlur = CfxrGraphics + "cfxr cloud blur.png";
    private const string CfxrLeafMaterial = CfxrGraphics + "cfxr leave a ab lit normal.mat";
    private const string CfxrTrailMaterial = CfxrGraphics + "cfxr sword trail plain.mat";
    private const string CfxrGlowSoft = CfxrGraphics + "cfxr proc glow soft ab.mat";
    private const string CfxrGlowCrisp = CfxrGraphics + "cfxr proc glow crisp ab.mat";
    private const string CfxrMagicStar = CfxrGraphics + "cfxr magic star hdr ab.mat";
    private const string CfxrPetal = CfxrGraphics + "cfxr petal pink x4 ab lit normal.mat";
    private const string CfxrWind = CfxrGraphics + "cfxr stretch smoke fume blur ab.mat";
    private const string CfxrStreak = CfxrGraphics + "cfxr stretch trait blur ab.mat";
    private const string CfxrJuice = CfxrGraphics + "cfxr blood splash dissolve ab.mat";
    private const string CfxrLeafMesh = CfxrMeshes + "cfxr mesh leave.fbx";
    private const string CfxrSplashPrefab = CfxrPrefabs + "Liquids/CFXR2 Blood Shape Splash.prefab";
    private const string Hovl = "Assets/Hovl Studio/HSFiles/";
    private const string HovlTextures = Hovl + "Textures/";
    private const string HovlModels = Hovl + "Models/";
    private const string RootBarkTexture = "Assets/Resources/VFX/RootSnarer/Textures/RootBark.png";
    private const string BarkFallback = "Assets/Fantasy Forest Environment Free Sample/Textures/bark01_bottom.tga";

    // Палитра плана §1.2 (как у Корнехвата и Вендиго). За порог блума 1,05 выходит только
    // красный канал янтаря и ядро дуги — героя и тело ничто не высветляет.
    private static readonly Color SoilLight = new Color(.56f, .41f, .26f), SoilDark = new Color(.30f, .20f, .12f);
    private static readonly Color DustLight = new Color(.76f, .58f, .39f), DustDark = new Color(.58f, .43f, .28f);
    private static readonly Color LeafLight = new Color(.62f, .70f, .30f), LeafDark = new Color(.42f, .52f, .20f);
    private static readonly Color BarkLight = new Color(.66f, .52f, .36f), BarkDark = new Color(.45f, .33f, .22f);
    private static readonly Color RockLight = new Color(.50f, .38f, .27f), RockDark = new Color(.34f, .25f, .17f);
    private static readonly Color PollenGold = new Color(1f, .84f, .38f), PollenDeep = new Color(.90f, .66f, .20f);
    private static readonly Color BerryRed = new Color(.86f, .12f, .10f), BerryDeep = new Color(.52f, .05f, .08f), Juice = new Color(.70f, .06f, .14f);
    private static readonly Color PetalWhite = new Color(1f, .96f, .92f), PetalPink = new Color(1f, .70f, .80f), PetalGold = new Color(1f, .86f, .45f);
    private static readonly Color ShaftCore = new Color(1.25f, 1.12f, .80f), ShaftEdge = new Color(1f, .86f, .45f);
    private static readonly Color Ivory = new Color(1.16f, 1.07f, .88f);
    private static readonly Color CrackTone = new Color(SoilDark.r * .7f, SoilDark.g * .7f, SoilDark.b * .7f, .95f);

    // Тёмная битва (владелец 02.10: свет арены босса остаётся тёмным): на ней читается только
    // то, что светится. Светящиеся слои — на копиях материалов с _HdrMultiply (CFXR умножает
    // цвет частицы — сам цвет частицы в 8 битах выше 1 не поднять) и уходят за порог bloom.
    // Янтарь — удар о землю (лапа, топот, нырок, корни), листовое золото — рёв и когти,
    // золото — пыльца и свет бури.
    private static readonly Color Ember = new Color(1f, .58f, .16f), EmberDeep = new Color(.95f, .36f, .08f);
    private static readonly Color LeafGlow = new Color(.80f, 1f, .38f), PollenGlow = new Color(1f, .82f, .32f);
    private static readonly Color SafeGlow = new Color(1f, .90f, .55f), BerryGlow = new Color(1f, .22f, .16f);
    // Пыльца — яд (ревью 02.10: «не читается, что это урон»): болезненное золото-зелень, а не тёплое золото.
    private static readonly Color ToxicLight = new Color(.80f, .88f, .30f), ToxicDeep = new Color(.52f, .66f, .14f), ToxicGlow = new Color(.80f, 1f, .24f);

    private sealed class Kit
    {
        public Material Clod, Splinter, Leaf, Haze, Soil, Crack, Star, Furrow, Ring, RingThin, RingRibbon, Slash, Spark, Drop,
            StarMote, Petal, Wind, Streak, Juice, Berry, Flower, Pillar, Wood, WoodDark;
        /// <summary>Светящиеся копии (_HdrMultiply): кольца волн, трещины, вспышки, когти, столб, лепестки бури.</summary>
        public Material GlowRing, GlowRingThin, GlowCrack, GlowStar, Glow, GlowSlash, GlowPillar, GlowPetal;
        /// <summary>
        /// Ревью 02.10 («плоско, наклейки»): объём. Earth — непрозрачные комья и горбы гребня
        /// (URP Particles/Lit, цвет из частиц, свет и тени на них; своей тени нет — у шейдера нет ShadowCaster); GlowHaze — светящаяся пыль гребня
        /// волны и кромки пыльцы; GlowClaw — ленты когтей (сетку пишет вид по кости лапы).
        /// </summary>
        public Material Earth, GlowHaze, GlowClaw;
        /// <summary>
        /// Цветы холма смерти. V11 (ревью 03.10: без света они были плоскими бумажными вырезками — «наклейки»):
        /// URP Particles/Simple Lit с вырезом и светом, как цветы куста Ф3 — меш с нормалями к чашке
        /// (<see cref="KnollFlower"/>), слабое своё свечение по текстуре лепестков; цвет из частицы.
        /// </summary>
        public Material HillFlowerRose, HillFlowerIvory;
        /// <summary>
        /// Пригорок смерти (V11): KnollMoss — мох и трава, фактура травы пола (CampTurf), тон пересчитан под траву пола
        /// и чуть свежее; KnollSoil — свежая земля у подножия и в рваных пятнах, фактура земли пола, на свету почти
        /// как пол и не темнее его в тени; KnollGreen — листья и трава со светом (без фактуры, цвет из частицы).
        /// </summary>
        public Material KnollMoss, KnollSoil, KnollGreen;
        /// <summary>Цветок Hovl с нормалями к чашке (общий код с кустом Ф3), пучок травинок, розетка листьев.</summary>
        public Mesh KnollFlower, KnollTuft, KnollLeaves;
        /// <summary>
        /// Земля, которую двигает босс (владелец 03.10, ночь: «земля в холме и при ползании — слишком большие куски
        /// примитивов»): URP Particles/Lit с фактурой земли поляны (LocationTheme Meadow: EarthTexture, яркость и тон
        /// пола), свет арены и тени на ней (своей тени не отбрасывает — у Particles/Lit нет прохода ShadowCaster), цвет
        /// частицы и вершин только темнит (сырая свежая земля, мох).
        /// </summary>
        public Material Ground;
        public Mesh LeafMesh, HalfSphere, Cylinder, FlowerMesh, BerryMesh;
        public Mesh[] Spikes, Curls, Tips, Chunks;
        /// <summary>Холм смерти: основание — земля пригорка (HillGround), ростки-завитки.</summary>
        public Mesh Hill;
        /// <summary>Мох пригорка (V13): одна сетка на весь холм — рельеф KnollTop, в координатах холма (MossMesh).</summary>
        public Mesh KnollMossMesh;
        public Mesh[] Sprouts;
        /// <summary>Мелкие комья земли (0,05–0,2 м частицей): гладкие бугристые катышки, фактура земли.</summary>
        public Mesh[] Clods;
        /// <summary>Камешки-акценты (≤ 0,25 м): плотнее и площе комьев, kit.Earth.</summary>
        public Mesh[] Pebbles;
        /// <summary>Валики (V9 — гребень бугра; с V11 — комья на рваных пятнах пригорка смерти).</summary>
        public Mesh[] RidgeSwells;
        /// <summary>
        /// Земля нырка и выхода (V14). Turf — плиты дёрна (атлас: дёрн пола | каменистая земля), StonySoil — комья на
        /// каменистой земле, RockA/RockB — камни поляны (Tripo-текстуры MeadowPebble), все — URP Particles/Lit непрозрачные;
        /// DirtPatch — рваные пятна разрытой земли (атлас dirt_0–5 без каймы, 3 × 2), WornPatch — большое пятно с травяной
        /// кромкой, оба — URP Particles/Lit прозрачные (свет арены как у пола); CrackRadial / CrackFine — тёмные трещины
        /// Crater43 / Crack6, Rubble — крошка Crater18 (копии CFXR одним каналом); Grain — зерно Debris2, DustFlip — пыль
        /// SmokeAnim2 8 × 8 (копии CFXR).
        /// </summary>
        public Material Turf, StonySoil, RockA, RockB, DirtPatch, WornPatch, CrackRadial, CrackFine, Rubble, Grain, DustFlip;
        /// <summary>Плиты дёрна (копии Cobble01–06 с новой развёрткой), камни поляны, большие комья (NoiseSphere1).</summary>
        public Mesh[] Slabs, BigClods;
        public Mesh RockMeshA, RockMeshB;
        /// <summary>
        /// Ход под землёй (V17, владелец 08.10: «как будто холмик просто скользит по полу»): CrackRay — луч трещины на земле
        /// от точки вдоль +Z (трещины разбегаются от головы бугра во все стороны); GlowFurrow — Crack5 (веер трещин) угольком.
        /// Каменистой кучи головы V15 (MoundRubble) и тёмного пятна под ней (MoundContact) больше нет.
        /// </summary>
        public Mesh CrackRay;
        public Material GlowFurrow;
        /// <summary>
        /// Веер шипов-семян (V16, ThicketMasterVfxSetup.Seeds.cs): SeedPod — стручок (ядро и шипы — подсетка 0, кончики шипов —
        /// подсетка 1), SeedWood / SeedTip — кора ThornWood Шипомёта (URP Lit), кончики тлеют; SeedRibbon — лента следа
        /// (копия ленты когтей Вендиго); SeedShards — шипы Шипомёта осколками.
        /// </summary>
        public Mesh SeedPod;
        public Mesh[] SeedShards;
        public Material SeedWood, SeedTip, SeedRibbon;
        /// <summary>Терновник (V17): листва куста — копии меша и материала падуба поляны; шипы линий и стебли — шипы Шипомёта.</summary>
        public Mesh BushLeafMesh, BushSpire;
        public Mesh[] BushCanes;
        public Material BushLeaves;
    }

    private static readonly List<string> Missing = new List<string>();
    private static readonly List<string> Built = new List<string>();
    private static readonly List<string> Failed = new List<string>();

    /// <summary>
    /// Ревизия с радиусами Sim и слепком пригорка (<see cref="KnollPrint"/>): сменился круг в Sim или правила
    /// пригорка — сборка устарела.
    /// </summary>
    private static string Revision
    {
        get
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return RevisionBase + "/" + KnollPrint() + "/" + MoundPrint() + "/" + string.Join(",", new[]
            {
                Simulation.ThicketPawRadius.ToFloat(), Simulation.ThicketStompRadius.ToFloat(), Simulation.ThicketStompRingOuterRadius.ToFloat(),
                Simulation.ThicketRoarInnerRadius.ToFloat(), Simulation.ThicketRoarOuterRadius.ToFloat(), Simulation.ThicketDiveRadius.ToFloat(),
                Simulation.ThicketSproutRadius.ToFloat(), Simulation.ThicketRainRadius.ToFloat(), Simulation.ThicketPollenRadius.ToFloat(),
                Simulation.ThicketStormSafeRadius.ToFloat(), ThicketMasterCombatView.BodyScale,
                // Буря и пол поляны: волны и стойка задают долгие частицы канала, пол — коробки вихря и порыва.
                Simulation.ThicketStormFirstWaveTicks, Simulation.ThicketStormSecondWaveTicks, Simulation.ThicketStormRecoveryTicks,
                GladeLayout.BossFloorHalfWidth.ToFloat(), GladeLayout.BossFloorHalfDepth.ToFloat(),
                // Веер семян (V16): замах задаёт блик у куста, высота полёта и размер — место всплеска.
                ThicketMasterSeedRules.GlintTicks, ThicketMasterSeedRules.FlightLift, ThicketMasterSeedRules.PodScale,
                // Терновник (V17): радиус куста Sim и устье стручка задают раскладку куста.
                Simulation.ThicketBushRadius.ToFloat(), ThicketMasterSeedRules.MouthRadius, ThicketMasterSeedRules.MouthLift,
            }.Select(f => f.ToString("0.###", c)));
        }
    }

    /// <summary>
    /// Слепок правил пригорка для ревизии (V13): вид ставит тела на ThicketMasterDeathRules.KnollFloor прямо из кода
    /// правил, а сетки земли и мха, места цветов и кривые роста запечены сборкой. Сменились правила без нового
    /// RevisionBase — сборка всё равно устарела (иначе тела висят над старой сеткой или тонут в ней). Верх, земля и мох
    /// по сетке 13 × 13 через весь пригорок с запасом на неровный край, рост, раскрытие цветка и сроки — в мм и мс,
    /// свёрнуты в 32-битный хеш.
    /// </summary>
    private static string KnollPrint()
    {
        int hash = 17;
        float reach = 1f + ThicketMasterDeathRules.KnollRimSwing;
        for (int i = -6; i <= 6; i++)
            for (int j = -6; j <= 6; j++)
            {
                float x = i / 6f * ThicketMasterDeathRules.HillHalfWidth * reach, z = j / 6f * ThicketMasterDeathRules.HillHalfLength * reach;
                float top = ThicketMasterDeathRules.KnollTop(x, z);
                hash = KnollMix(hash, ThicketMasterDeathRules.KnollSurface(x, z));
                hash = KnollMix(hash, ThicketMasterDeathRules.KnollGround(x, z));
                hash = KnollMix(hash, ThicketMasterDeathRules.KnollMossLift(x, z, top));
            }
        for (int k = 0; k <= 12; k++)
        {
            hash = KnollMix(hash, ThicketMasterDeathRules.HillRiseHeight(k / 12f));
            hash = KnollMix(hash, ThicketMasterDeathRules.HillRiseWidth(k / 12f));
            hash = KnollMix(hash, ThicketMasterDeathRules.FlowerGrow(k / 12f * ThicketMasterDeathRules.FlowerGrowSeconds));
        }
        foreach (float value in new[]
                 {
                     ThicketMasterDeathRules.HillSink, ThicketMasterDeathRules.HillRiseDelay, ThicketMasterDeathRules.HillRiseSeconds,
                     ThicketMasterDeathRules.BloomDelay, ThicketMasterDeathRules.BloomSpread, ThicketMasterDeathRules.FlowerGrowSeconds,
                     ThicketMasterDeathRules.PetalDriftSeconds, ThicketMasterDeathRules.HillRiseHeightKeys, ThicketMasterDeathRules.HillRiseWidthKeys,
                 })
            hash = KnollMix(hash, value);
        return hash.ToString("x8");
    }

    private static int KnollMix(int hash, float value) => unchecked(hash * 31 + Mathf.RoundToInt(value * 1000f));

    /// <summary>
    /// Слепок правил хода под землёй для ревизии (V17): места частиц у головы, рябь впереди, баллистика вырванных плит,
    /// сроки, ход, на котором задана частота следа, и бюджет запечены сборкой (SaveMound) — сменились правила без нового
    /// RevisionBase, сборка всё равно устарела. Значения — в тысячных, свёрнуты в 32-битный хеш.
    /// </summary>
    private static string MoundPrint()
    {
        int hash = 23;
        foreach (float value in new[]
                 {
                     ThicketMasterEarthRules.HeadHalfWidth, ThicketMasterEarthRules.HeadAhead, ThicketMasterEarthRules.HeadBehind,
                     ThicketMasterEarthRules.BurstSpeedMin, ThicketMasterEarthRules.BurstSpeedMax, ThicketMasterEarthRules.BurstLean,
                     ThicketMasterEarthRules.BurstGravity, ThicketMasterEarthRules.BurstLifeMin, ThicketMasterEarthRules.BurstLifeMax,
                     ThicketMasterEarthRules.LipSlabLifeMin, ThicketMasterEarthRules.LipSlabLifeMax, ThicketMasterEarthRules.LipSlabTiltMin,
                     ThicketMasterEarthRules.LipSlabTiltMax, ThicketMasterEarthRules.RippleNear, ThicketMasterEarthRules.RippleFar,
                     ThicketMasterEarthRules.RippleNearHalf, ThicketMasterEarthRules.RippleFarHalf, ThicketMasterEarthRules.RippleDepth,
                     ThicketMasterEarthRules.RippleSpeedMin, ThicketMasterEarthRules.RippleSpeedMax, ThicketMasterEarthRules.RippleGravity,
                     ThicketMasterEarthRules.RippleLifeMin, ThicketMasterEarthRules.RippleLifeMax, ThicketMasterEarthRules.FurrowHalfWidth,
                     ThicketMasterEarthRules.MoundRefSpeed, ThicketMasterEarthRules.MoundParticlesPeak,
                 })
            hash = KnollMix(hash, value);
        return hash.ToString("x8");
    }

    [InitializeOnLoadMethod]
    private static void Schedule()
    {
        EditorApplication.delayCall += () => Install(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => Install(false);
        };
    }

    [MenuItem("Разлом/Босс/Хозяин Чащи/Собрать эффекты")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!force && IsBuilt()) return;
        string[] required = { CfxrDebrisUnlit, CfxrSmokeBlurred, CfxrTrailMaterial, CfxrCloudBlur, HovlTextures + "Crater19.png" };
        var absent = new List<string>();
        foreach (string path in required) if (AssetDatabase.LoadMainAssetAtPath(path) == null) absent.Add(path);
        if (absent.Count > 0)
        {
            Debug.LogWarning(Log + "Эффекты атак НЕ собраны: нет ассетов паков CFXR/Hovl —\n  " + string.Join("\n  ", absent));
            return;
        }
        Build();
    }

    private static bool IsBuilt()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterCombatView.PawSlashName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in ThicketMasterCombatView.AttackPrefabNames)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        return true;
    }

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

    private static void Build()
    {
        Missing.Clear(); Built.Clear(); Failed.Clear();
        PelagWhirlwindVfxSetup.EnsureFolder(VfxFolder, "ThicketMaster");
        PelagWhirlwindVfxSetup.EnsureFolder(BossFolder, "Attacks");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Textures");
        var kit = Materials();
        Geometry(kit);
        SeedKit(kit);

        Step(ThicketMasterCombatView.PawSlashName, () => SavePawSlash(kit));
        Step(ThicketMasterCombatView.PawImpactName, () => SavePawImpact(kit));
        Step(ThicketMasterCombatView.StompRearName, () => SaveStompRear(kit));
        Step(ThicketMasterCombatView.StompQuakeName, () => SaveStompQuake(kit));
        Step(ThicketMasterCombatView.StompOuterName, () => SaveStompOuter(kit));
        Step(ThicketMasterCombatView.DiveBurstName, () => SaveDiveBurst(kit));
        Step(ThicketMasterCombatView.MoundName, () => SaveMound(kit));
        Step(ThicketMasterCombatView.DiveTremorName, () => SaveDiveTremor(kit));
        Step(ThicketMasterCombatView.EmergeBulgeName, () => SaveEmergeBulge(kit));
        Step(ThicketMasterCombatView.EmergeName, () => SaveEmerge(kit));
        Step(ThicketMasterCombatView.SproutPressName, () => SaveSproutPress(kit));
        Step(ThicketMasterCombatView.SproutTremorName, () => SaveSproutTremor(kit));
        Step(ThicketMasterCombatView.SproutSpikesName, () => SaveSproutSpikes(kit));
        Step(ThicketMasterCombatView.PollenShakeName, () => SavePollenShake(kit));
        Step(ThicketMasterCombatView.PollenFallName, () => SavePollenFall(kit));
        Step(ThicketMasterCombatView.PollenCloudName, () => SavePollenCloud(kit));
        Step(ThicketMasterCombatView.BushPuffName, () => SaveBushPuff(kit));
        Step(ThicketMasterCombatView.BerryName, () => SaveBerry(kit));
        Step(ThicketMasterCombatView.BerrySplatName, () => SaveBerrySplat(kit));
        Step(ThicketMasterCombatView.CrownShedName, () => SaveCrownShed(kit));
        Step(ThicketMasterCombatView.StormVortexName, () => SaveStormVortex(kit));
        Step(ThicketMasterCombatView.LightPillarName, () => SaveLightPillar(kit));
        Step(ThicketMasterCombatView.StormWaveName, () => SaveStormWave(kit));
        Step(ThicketMasterCombatView.StormChannelName, () => SaveStormChannel(kit));
        Step(ThicketMasterCombatView.RoarInhaleName, () => SaveRoarInhale(kit));
        Step(ThicketMasterCombatView.RoarBlastName, () => SaveRoarBlast(kit));
        Step(ThicketMasterCombatView.WakeTearName, () => SaveWakeTear(kit));
        Step(ThicketMasterCombatView.DeathBloomName, () => SaveDeathBloom(kit));
        Step(ThicketMasterCombatView.DeathHillName, () => SaveDeathHill(kit));
        Step(ThicketMasterCombatView.SeedPodName, () => SaveSeedPod(kit));
        Step(ThicketMasterCombatView.BushName, () => SaveBush(kit));
        Step(ThicketMasterCombatView.BushSproutName, () => SaveBushSprout(kit));
        Step(ThicketMasterCombatView.BushLaunchName, () => SaveBushLaunch(kit));
        Step(ThicketMasterCombatView.SeedHitName, () => SaveSeedHit(kit));
        Step(ThicketMasterCombatView.SeedDropName, () => SaveSeedDrop(kit));
        // V13: мох холма — одна сетка; сетки ячеек V11–V12 снимаются, только когда новый холм собрался.
        if (Failed.Count == 0) DeleteStaleKnollCells();
        // V14: вал LipMesh и места старых клубов/комьев нырка и выхода не нужны — снимаются, только когда новые собрались.
        if (Failed.Count == 0) DeleteStaleDiveEarth();
        // V15, V17: прежние головы бугра (гладкий вал, каменистая куча V15 с местами и пятном) не нужны — снимаются, только
        // когда новый бугор собрался.
        if (Failed.Count == 0) DeleteStaleMoundHead();
        // V17: замах и выпуск веера с груди (SeedSwell, SeedLaunch) сняты — префабы удаляются, только когда терновник собрался.
        if (Failed.Count == 0) DeleteStaleSeedFan();

        AssetDatabase.SaveAssets();
        // Ловушка 25.09: свежесохранённые материалы держатся в памяти не теми — переимпорт с диска.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(ThicketMasterCombatView.PawSlashName));
        if (importer != null && Failed.Count == 0 && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        string report = Log + $"Собрано {Built.Count} из {ThicketMasterCombatView.AttackPrefabNames.Length} префабов в {PrefabFolder}, ревизия {Revision}.";
        if (Failed.Count > 0) report += "\n  НЕ собраны (ошибка выше): " + string.Join(", ", Failed);
        if (Missing.Count > 0) report += "\n  Не нашлось в паках (взята замена или слой пропущен):\n    " + string.Join("\n    ", Missing);
        if (Failed.Count > 0) Debug.LogError(report);
        else if (Missing.Count > 0) Debug.LogWarning(report);
        else Debug.Log(report + " Всё из паков на месте.");
    }

    private static void Step(string name, System.Action save)
    {
        try
        {
            save();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) != null) Built.Add(name);
            else Failed.Add(name);
        }
        catch (System.Exception e)
        {
            Failed.Add(name);
            Debug.LogException(e);
        }
    }

    private static void Note(string what)
    {
        if (!Missing.Contains(what)) Missing.Add(what);
    }

    // ------------------------------------------------------------- materials

    private static Kit Materials()
    {
        var kit = new Kit();
        kit.Clod = PackCopy("M_Thicket_Clod", CfxrDebrisUnlit);
        kit.Splinter = PackCopy("M_Thicket_Splinter", CfxrDebrisWood) ?? kit.Clod;
        kit.Leaf = Unlit(PackCopy("M_Thicket_Leaf", CfxrLeafMaterial)) ?? kit.Clod;
        kit.Haze = Textured(Plain(PackCopy("M_Thicket_Haze", CfxrSmokeBlurred)), CfxrCloudBlur, true);
        kit.Soil = Textured(Plain(PackCopy("M_Thicket_Soil", CfxrSmokeBlurred)), HovlTextures + "Crater40.png", false);
        kit.Crack = Textured(NoDissolve(PackCopy("M_Thicket_Crack", CfxrTrailMaterial)), HovlTextures + "Crack4.png", true);
        kit.Star = Textured(NoDissolve(PackCopy("M_Thicket_Star", CfxrTrailMaterial)), HovlTextures + "Crater19.png", true);
        kit.Furrow = Textured(NoDissolve(PackCopy("M_Thicket_Furrow", CfxrTrailMaterial)), HovlTextures + "Crack5.png", true);
        kit.Ring = Textured(NoDissolve(PackCopy("M_Thicket_Ring", CfxrTrailMaterial)), HovlTextures + "Circle17.png", true);
        kit.RingThin = Textured(NoDissolve(PackCopy("M_Thicket_RingThin", CfxrTrailMaterial)), HovlTextures + "Circle41.png", true);
        kit.RingRibbon = Textured(NoDissolve(PackCopy("M_Thicket_RingRibbon", CfxrTrailMaterial)), HovlTextures + "Circle82.png", true);
        kit.Pillar = Textured(NoDissolve(PackCopy("M_Thicket_Pillar", CfxrTrailMaterial)), HovlTextures + "Trail25.png", true);
        // Дуга когтей — плёнка пака с её dissolve вдоль UV.x (принятый подход Вихря и Раскола).
        kit.Slash = PackCopy("M_Thicket_Slash", CfxrTrailMaterial);
        kit.Spark = Plain(PackCopy("M_Thicket_Spark", CfxrGlowSoft)) ?? kit.Haze;
        kit.Drop = Plain(PackCopy("M_Thicket_Drop", CfxrGlowCrisp)) ?? kit.Spark;
        kit.StarMote = Plain(PackCopy("M_Thicket_StarMote", CfxrMagicStar)) ?? kit.Spark;
        kit.Wind = Plain(PackCopy("M_Thicket_Wind", CfxrWind)) ?? kit.Haze;
        kit.Streak = Plain(PackCopy("M_Thicket_Streak", CfxrStreak)) ?? kit.Drop;
        // Лепесток: одним каналом — цвет целиком из частиц (белые, розовые, золотые из одной текстуры).
        kit.Petal = Unlit(PackCopy("M_Thicket_Petal", CfxrPetal));
        if (kit.Petal != null && kit.Petal.HasProperty("_SingleChannel")) kit.Petal.SetFloat("_SingleChannel", 1f);
        if (kit.Petal == null) kit.Petal = kit.Leaf;
        kit.Juice = PackCopy("M_Thicket_Juice", CfxrJuice);
        if (kit.Juice != null)
        {
            kit.Juice.DisableKeyword("_FADING_ON");
            if (kit.Juice.HasProperty("_UseSP")) kit.Juice.SetFloat("_UseSP", 0f);
        }
        // Пятна сока на земле нет (ревью 02.10, вечер: «лужи после буллет рейна говно»): M_Thicket_JuiceStain больше не собирается.
        kit.Berry = UrpParticles("M_Thicket_Berry", "Universal Render Pipeline/Particles/Lit", null, false) ?? kit.Drop;
        kit.Flower = UrpParticles("M_Thicket_Flower", "Universal Render Pipeline/Particles/Simple Lit", HovlTextures + "Flower2.png", true) ?? kit.Petal;
        kit.HillFlowerRose = BloomFlower("M_Thicket_HillFlowerRose", KnollRoseGlow) ?? kit.Flower;
        kit.HillFlowerIvory = BloomFlower("M_Thicket_HillFlowerIvory", KnollIvoryGlow) ?? kit.Flower;
        kit.Wood = Wood("M_Thicket_Wood", Color.white);
        kit.WoodDark = Wood("M_Thicket_WoodDark", new Color(.80f, .76f, .72f));

        // Светящиеся слои тёмной битвы.
        kit.GlowRing = Glow(PackCopy("M_Thicket_GlowRing", CfxrTrailMaterial), HovlTextures + "Circle17.png", 3.2f) ?? kit.Ring;
        kit.GlowRingThin = Glow(PackCopy("M_Thicket_GlowRingThin", CfxrTrailMaterial), HovlTextures + "Circle41.png", 3.4f) ?? kit.RingThin;
        kit.GlowCrack = Glow(PackCopy("M_Thicket_GlowCrack", CfxrTrailMaterial), HovlTextures + "Crack4.png", 2.6f) ?? kit.Crack;
        kit.GlowStar = Glow(PackCopy("M_Thicket_GlowStar", CfxrTrailMaterial), HovlTextures + "Crater19.png", 2.6f) ?? kit.Star;
        kit.Glow = Hdr(Plain(PackCopy("M_Thicket_Glow", CfxrGlowSoft)), 3.2f) ?? kit.Spark;
        kit.GlowSlash = Hdr(PackCopy("M_Thicket_GlowSlash", CfxrTrailMaterial), 2.4f) ?? kit.Slash;
        kit.GlowPillar = Glow(PackCopy("M_Thicket_GlowPillar", CfxrTrailMaterial), HovlTextures + "Trail25.png", 2.2f) ?? kit.Pillar;
        kit.GlowPetal = Hdr(Unlit(PackCopy("M_Thicket_GlowPetal", CfxrPetal)), 1.7f);
        if (kit.GlowPetal != null && kit.GlowPetal.HasProperty("_SingleChannel")) kit.GlowPetal.SetFloat("_SingleChannel", 1f);
        if (kit.GlowPetal == null) kit.GlowPetal = kit.Petal;
        kit.Earth = UrpParticles("M_Thicket_Earth", "Universal Render Pipeline/Particles/Lit", null, false) ?? kit.Clod;
        if (kit.Earth != null && kit.Earth.HasProperty("_Smoothness")) kit.Earth.SetFloat("_Smoothness", .06f);
        kit.Ground = GroundMaterial() ?? kit.Earth;
        kit.KnollSoil = KnollSoilMaterial() ?? kit.Ground;
        kit.KnollMoss = KnollMossMaterial() ?? kit.KnollSoil;
        kit.KnollGreen = KnollGreenMaterial() ?? kit.Earth;
        kit.GlowHaze = Hdr(Textured(Plain(PackCopy("M_Thicket_GlowHaze", CfxrSmokeBlurred)), CfxrCloudBlur, true), 1.45f) ?? kit.Haze;
        // Лента когтей: плёнка без dissolve (маска полосы по V, Cull Off в шейдере) — сетку вид разворачивает к камере.
        kit.GlowClaw = Hdr(NoDissolve(PackCopy("M_Thicket_GlowClaw", CfxrTrailMaterial)), 2.6f) ?? kit.GlowSlash;
        // V15: тлеющий шов по гребню бугра и уголёк трещины впереди головы — Crack5 (веер трещин вдоль), слабее сети выхода.
        kit.GlowFurrow = Glow(PackCopy("M_Thicket_GlowFurrow", CfxrTrailMaterial), HovlTextures + "Crack5.png", 2.2f) ?? kit.GlowCrack;
        Queue(kit.GlowFurrow, 2991);
        EarthKit(kit);

        // Декали земли — до пыли и комьев; лепестки и листья — поверх пыли; свет бури — последним.
        foreach (var decal in new[] { kit.Soil, kit.Crack, kit.Star, kit.Furrow }) Queue(decal, 2990);
        foreach (var glow in new[] { kit.GlowCrack, kit.GlowStar }) Queue(glow, 2991);
        foreach (var ring in new[] { kit.Ring, kit.RingThin, kit.RingRibbon }) Queue(ring, 2995);
        foreach (var ring in new[] { kit.GlowRing, kit.GlowRingThin }) Queue(ring, 2996);
        Queue(kit.Leaf, 3005); Queue(kit.Petal, 3006); Queue(kit.GlowPetal, 3006); Queue(kit.Pillar, 3008); Queue(kit.GlowPillar, 3008);
        Queue(kit.StarMote, 3008); Queue(kit.Glow, 3009); Queue(kit.GlowHaze, 3001); Queue(kit.GlowClaw, 3010);
        return kit;
    }

    /// <summary>Маска Hovl одним каналом на копии плёнки CFXR без dissolve, со свечением hdr.</summary>
    private static Material Glow(Material material, string texturePath, float hdr)
        => Hdr(Textured(NoDissolve(material), texturePath, true), hdr);

    /// <summary>Свечение CFXR: цвет частицы × _HdrMultiply (больше 0 — включено), уходит за порог bloom.</summary>
    private static Material Hdr(Material material, float hdr)
    {
        if (material == null) return null;
        if (material.HasProperty("_HdrMultiply")) material.SetFloat("_HdrMultiply", hdr);
        else Note("_HdrMultiply нет у " + material.name + " — слой не светится");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Queue(Material material, int queue)
    {
        if (material == null) return;
        material.renderQueue = queue;
        EditorUtility.SetDirty(material);
    }

    /// <summary>Копия материала пака в Attacks/Materials (перезаписывается при пересборке); нет источника — null.</summary>
    private static Material PackCopy(string name, string sourcePath)
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
        if (source == null) { Note(sourcePath + " (материал " + name + ")"); return null; }
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
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

    /// <summary>Без dissolve и мягких частиц: в URP без depth-текстуры они гасят спрайт у земли.</summary>
    private static Material Plain(Material material)
    {
        if (material == null) return null;
        NoDissolve(material);
        material.DisableKeyword("_FADING_ON");
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material NoDissolve(Material material)
    {
        if (material == null) return null;
        material.DisableKeyword("_CFXR_DISSOLVE");
        material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
        if (material.HasProperty("_UseDissolve")) material.SetFloat("_UseDissolve", 0f);
        if (material.HasProperty("_UseDissolveOffsetUV")) material.SetFloat("_UseDissolveOffsetUV", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Освещённый материал CFXR без освещения: цвет только из частиц (иначе в URP бледный).</summary>
    private static Material Unlit(Material material)
    {
        if (material == null) return null;
        foreach (var keyword in new[] { "_CFXR_LIGHTING_ALL", "_CFXR_LIGHTING_DIRECT", "_CFXR_LIGHTING_INDIRECT",
            "_CFXR_LIGHTING_WPOS_OFFSET", "_NORMALMAP", "_FADING_ON", "_CFXR_DITHERED_SHADOWS_ON" })
            material.DisableKeyword(keyword);
        if (material.HasProperty("_UseLighting")) material.SetFloat("_UseLighting", 0f);
        if (material.HasProperty("_UseNormalMap")) material.SetFloat("_UseNormalMap", 0f);
        if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0f);
        if (material.HasProperty("_CFXR_DITHERED_SHADOWS")) material.SetFloat("_CFXR_DITHERED_SHADOWS", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Маска Hovl (или своя текстура) на материал CFXR; нет текстуры — материал как есть, отмечено.</summary>
    private static Material Textured(Material material, string texturePath, bool singleChannel)
    {
        if (material == null) return null;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture != null) material.SetTexture("_MainTex", texture);
        else Note(texturePath + " (маска " + material.name + ")");
        if (material.HasProperty("_SingleChannel")) material.SetFloat("_SingleChannel", singleChannel ? 1f : 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Стандартные частицы URP (ягода — Particles/Lit с блеском; цветок — Particles/Simple Lit
    /// с вырезом по альфе): цвет из частиц, свет URP, без бледности освещённых CFXR.
    /// Слот текстуры без своей карты не пишется (умолчание шейдера — белая).
    /// </summary>
    private static Material UrpParticles(string name, string shaderName, string texturePath, bool clip)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) { Note("шейдер " + shaderName + " (материал " + name + ")"); return null; }
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", shader);
        material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", clip ? .2f : .65f);
        if (texturePath != null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else Note(texturePath + " (текстура " + name + ")");
        }
        if (clip)
        {
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", .35f);
            material.EnableKeyword("_ALPHATEST_ON");
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Своё свечение цветов пригорка по текстуре лепестков (как FlowerGlow 0,5 у куста Ф3, но слабее: цветы крупнее,
    /// форму и тень должен давать свет, не эмиссия): розовые — пыльная роза, светлые — сливки. Цвет частицы эмиссию
    /// не умножает — потому два материала. Значения — гамма (SetColor переводит в линейное: 0,4 → ~0,13, у куста 0,5 →
    /// ~0,22): прежние 0,2 / 0,18 давали ~0,03 — в 7 раз слабее куста, а свет пригорка к нижним кучкам падает до
    /// ~0,05–0,13 (квадрат расстояния) — в синей тени цветы уходили бы в серо-синие пятна, как куст до FlowerGlow 0,5.
    /// </summary>
    private static readonly Color KnollRoseGlow = new Color(.98f, .68f, .76f) * .4f, KnollIvoryGlow = new Color(1f, .95f, .86f) * .36f;

    /// <summary>
    /// Цветок холма смерти СО СВЕТОМ (V11, ревью 03.10: без света — плоские бумажные вырезки, «наклейки»; V8 снял свет,
    /// потому что нормали Hovl Flower.fbx смотрят вниз и цветок освещался снизу тёмно-синим). Теперь меш — копия с
    /// нормалями к раскрытию чашки (ThicketMasterDressingSetup.UprightFlowerMesh, тот же код, что у цветов куста Ф3 —
    /// в игре они хороши), материал — URP Particles/Simple Lit: непрозрачный с вырезом 0,35, двусторонний, цвет
    /// частицы умножает Flower2, свет и тени арены, точечный свет пригорка; бликов нет (цвет блика чёрный, ключ
    /// _SPECULAR_COLOR и _SpecularHighlights 1 — так его держит проверка материала URP, как у куста Ф3); слабое своё
    /// свечение glow по той же текстуре — в синей тени цветок не уходит в чёрное. За порог bloom не выходит.
    /// </summary>
    private static Material BloomFlower(string name, Color glow)
    {
        var material = UrpParticles(name, "Universal Render Pipeline/Particles/Simple Lit", HovlTextures + "Flower2.png", true);
        if (material == null) return null;
        OpaqueParticle(material, true);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .3f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", new Color(0f, 0f, 0f, .3f));
        material.EnableKeyword("_SPECULAR_COLOR");
        material.DisableKeyword("_SPECGLOSSMAP");
        if (material.HasProperty("_EmissionColor"))
        {
            var petals = AssetDatabase.LoadAssetAtPath<Texture2D>(HovlTextures + "Flower2.png");
            if (petals != null && material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", petals);
            material.SetColor("_EmissionColor", new Color(glow.r, glow.g, glow.b, 1f));
            material.EnableKeyword("_EMISSION");
            // RealtimeEmissive держит _EMISSION при проверке материала URP (урок V8: ключ срезался, как у ягод куста).
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Частицы URP непрозрачные (очередь Geometry / AlphaTest с вырезом), цвет частицы умножает (_ColorMode 0), без мягких
    /// частиц, искажения и смеси кадров; двусторонние.
    /// </summary>
    private static void OpaqueParticle(Material material, bool clip)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 1f);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", clip ? 1f : 0f);
        foreach (var keyword in new[] { "_SURFACE_TYPE_TRANSPARENT", "_COLORADDSUBDIFF_ON", "_COLOROVERLAY_ON", "_COLORCOLOR_ON",
            "_SOFTPARTICLES_ON", "_FADING_ON", "_DISTORTION_ON", "_FLIPBOOKBLENDING_ON", "_RECEIVE_SHADOWS_OFF", "_ALPHAPREMULTIPLY_ON",
            "_ALPHAMODULATE_ON", "_NORMALMAP" })
            material.DisableKeyword(keyword);
        if (clip)
        {
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
        }
    }

    /// <summary>Оформление поляны (пол арены босса): отсюда фактура, яркость и тон земли пола.</summary>
    private const string ArenaThemePath = "Assets/Resources/Locations/Meadow.asset";
    /// <summary>Фактура земли, если оформления нет (её же берёт Meadow на 03.10).</summary>
    private const string ArenaEarthFallback = "Assets/Resources/Terrain/TCom_Sand_Muddy2_2x2_1K_albedo.tif";
    /// <summary>Средний цвет TCom_Sand_Muddy2 в линейном пространстве (замер 03.10) — по нему подгоняется тон.</summary>
    private static readonly Vector3 EarthTextureMean = new Vector3(.0666f, .0440f, .0233f);
    /// <summary>
    /// Плитка фактуры на земле босса, м (у пола — EarthTileMeters 3). Фактура почти однотонная — зерно видно,
    /// повтора нет.
    /// </summary>
    private static float GroundTileMeters = 3f;
    /// <summary>
    /// Земля босса на свету чуть темнее пола: пол берёт окружающий свет ×0,55 (CampPainterlyGround), частицы —
    /// целиком; сырая свежая земля — ещё темнее цветом частиц и вершин.
    /// </summary>
    private const float GroundLightMatch = .75f;

    /// <summary>
    /// Земля босса из ТОЙ ЖЕ земли, что пол поляны (владелец 03.10, ночь): URP Particles/Lit, непрозрачная, свет и
    /// тени арены на ней (своей тени не отбрасывает: у Particles/Lit нет прохода ShadowCaster), фактура — EarthTexture оформления поляны. Тон: как считает пол (CampPainterlyGround: фактура ×
    /// _DirtGain × _DirtTint, на 30 % к серому, × (0,92; 0,86; 0,78), потёртость ~0,95) — пересчитан в множитель
    /// _BaseColor по среднему цвету фактуры (вершины и частицы только темнят, выше 1 их не поднять).
    /// </summary>
    private static Material GroundMaterial() => EarthMaterial("M_Thicket_Ground", GroundLightMatch);

    /// <summary>Земля пола поляны с множителем света match (доля тона пола на свету; см. GroundLightMatch, KnollSoilMatch).</summary>
    private static Material EarthMaterial(string name, float match)
    {
        var material = UrpParticles(name, "Universal Render Pipeline/Particles/Lit", null, false);
        if (material == null) return null;
        var theme = AssetDatabase.LoadAssetAtPath<LocationTheme>(ArenaThemePath);
        var style = theme != null ? theme.Style : null;
        var texture = style != null && style.EarthTexture != null ? style.EarthTexture : AssetDatabase.LoadAssetAtPath<Texture2D>(ArenaEarthFallback);
        if (theme == null) Note(ArenaThemePath + " (оформление поляны; тон земли — по умолчанию)");
        if (texture != null) material.SetTexture("_BaseMap", texture);
        else Note(ArenaEarthFallback + " (фактура земли босса)");
        float brightness = style != null ? style.EarthBrightness : 2.3f;
        Color tint = (style != null ? style.EarthTint : new Color(1.18f, 1.05f, .72f)).linear;
        GroundTileMeters = style != null ? Mathf.Max(1f, style.EarthTileMeters) : 3f;
        // Средний цвет пола в линейном: фактура × яркость × тон, к серому на 30 %, землистый множитель шейдера пола.
        var mean = new Vector3(EarthTextureMean.x * brightness * tint.r, EarthTextureMean.y * brightness * tint.g, EarthTextureMean.z * brightness * tint.b);
        float luma = .2126f * mean.x + .7152f * mean.y + .0722f * mean.z;
        var floor = new Vector3((.3f * luma + .7f * mean.x) * .92f, (.3f * luma + .7f * mean.y) * .86f, (.3f * luma + .7f * mean.z) * .78f) * .95f;
        var gain = new Color(floor.x / EarthTextureMean.x, floor.y / EarthTextureMean.y, floor.z / EarthTextureMean.z) * match;
        SetLinearBaseColor(material, gain);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .04f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 1f);
        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_SPECULAR_SETUP");
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Множитель цвета в линейном пространстве → _BaseColor (SetColor ждёт гамму; выше 1 — можно).</summary>
    private static void SetLinearBaseColor(Material material, Color gain)
        => material.SetColor("_BaseColor", new Color(Mathf.LinearToGammaSpace(gain.r), Mathf.LinearToGammaSpace(gain.g), Mathf.LinearToGammaSpace(gain.b), 1f));

    /// <summary>
    /// Свет пригорка (V11). Пол поляны берёт окружающий свет ×0,55 (_AmbientStrength CampPainterlyGround), частицы URP —
    /// целиком, прямой свет — одинаково: на свету тон частицы = тон пола × match, в тени (только окружающий) — × match / 0,55.
    /// Земля пригорка на свету — почти пол (0,9; прежняя земля босса 0,75 — в тени выходила темнее травы вокруг), в тени
    /// светлее пола (≈ 1,6×) — не темнее, как просил ведущий; мох — трава пола × 1,1 (свежее), на свету 0,82 — склоны
    /// от солнца и так темнее по свету формы, в синей тени пригорок читается.
    /// </summary>
    private const float KnollSoilMatch = .9f, KnollMossMatch = .82f, KnollMossFresh = 1.1f;

    /// <summary>Средний тон вершин мха (пятна, низ склонов, кромка) — делится из множителя, чтобы средний мох вышел целевым.</summary>
    private const float KnollMossVertexMean = .92f;

    /// <summary>
    /// Средний тон видимого кольца земли: вершины (пятна 0,84–1, у подножия ×0,88, под кромкой мха ×0,9) × цвет частицы
    /// основания 0,96 — 0,80 (замер порта ревью 03.10). Делится из множителя, как у мха: без этого кольцо выходило
    /// 0,72 земли пола на свету — тёмно-бурой полосой у подножия на солнечной стороне.
    /// </summary>
    private const float KnollSoilVertexMean = .8f;

    /// <summary>Свежая земля пригорка: фактура и тон земли пола, свет — KnollSoilMatch (÷ средний тон вершин кольца).</summary>
    private static Material KnollSoilMaterial() => EarthMaterial("M_Thicket_KnollSoil", KnollSoilMatch / KnollSoilVertexMean);

    /// <summary>Фактуры травы пола (CampPainterlyGround: _GrassTex «луг» и _TurfTex «дёрн»), если в материале пола их нет.</summary>
    private const string FloorMeadowFallback = "Assets/Resources/Environment/Camp/ground/CampMeadow_v2.png";
    private const string FloorTurfFallback = "Assets/Resources/Environment/Camp/ground/CampTurf_v3.png";
    /// <summary>Средний цвет CampMeadow_v2 и CampTurf_v3 в линейном пространстве (замер 03.10, V11).</summary>
    private static readonly Vector3 MeadowTextureMean = new Vector3(.410f, .343f, .073f), TurfTextureMean = new Vector3(.0866f, .1323f, .0251f);
    /// <summary>Плитка дёрна на полу, м (CampPainterlyGround: p / 5,2) — мох пригорка той же крупности.</summary>
    private const float TurfTileMeters = 5.2f;

    /// <summary>
    /// Мох и трава пригорка (V11): URP Particles/Lit, фактура дёрна пола (_TurfTex материала пола поляны), тон — как
    /// считает пол (CampPainterlyGround: луг на 10 % к серому × (0,60; 0,82; 0,68) × средний макрошум (0,925; 0,95; 0,875),
    /// дёрн × (0,89; 0,97; 0,86), доля дёрна ~0,8 × GroundTurfWeight) — пересчитан в множитель _BaseColor по среднему
    /// цвету дёрна; × KnollMossFresh (свежее), × KnollMossMatch (свет), ÷ средний тон вершин.
    /// </summary>
    private static Material KnollMossMaterial()
    {
        var material = UrpParticles("M_Thicket_KnollMoss", "Universal Render Pipeline/Particles/Lit", null, false);
        if (material == null) return null;
        var theme = AssetDatabase.LoadAssetAtPath<LocationTheme>(ArenaThemePath);
        var style = theme != null ? theme.Style : null;
        var floorMaterial = style != null ? style.CampSurfaceMaterial : null;
        Texture turf = floorMaterial != null && floorMaterial.HasProperty("_TurfTex") ? floorMaterial.GetTexture("_TurfTex") : null;
        if (turf == null) turf = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTurfFallback);
        if (turf != null) material.SetTexture("_BaseMap", turf);
        else Note(FloorTurfFallback + " (фактура мха пригорка)");
        float turfWeight = style != null ? style.GroundTurfWeight : .85f;
        var meadow = MeadowTextureMean;
        float luma = .2126f * meadow.x + .7152f * meadow.y + .0722f * meadow.z;
        var grass = new Vector3((.1f * luma + .9f * meadow.x) * .60f * .925f, (.1f * luma + .9f * meadow.y) * .82f * .95f,
            (.1f * luma + .9f * meadow.z) * .68f * .875f);
        var turfTone = new Vector3(TurfTextureMean.x * .89f, TurfTextureMean.y * .97f, TurfTextureMean.z * .86f);
        float w = Mathf.Clamp01(.8f * turfWeight);
        var floor = Vector3.Lerp(grass, turfTone, w);
        float k = KnollMossFresh * KnollMossMatch / KnollMossVertexMean;
        SetLinearBaseColor(material, new Color(floor.x / TurfTextureMean.x * k, floor.y / TurfTextureMean.y * k, floor.z / TurfTextureMean.z * k));
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .05f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 1f);
        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_SPECULAR_SETUP");
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Листья и трава пригорка (V11): URP Particles/Simple Lit без фактуры — цвет из частицы, свет арены и пригорка,
    /// двусторонние, без бликов (прежняя трава — неосвещённый лист CFXR, плоская наклейка).
    /// </summary>
    private static Material KnollGreenMaterial()
    {
        var material = UrpParticles("M_Thicket_KnollGreen", "Universal Render Pipeline/Particles/Simple Lit", null, false);
        if (material == null) return null;
        OpaqueParticle(material, false);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .25f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", new Color(0f, 0f, 0f, .25f));
        material.EnableKeyword("_SPECULAR_COLOR");
        material.DisableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------- earth of the dive and emerge (V14)

    private const string TextureFolder = Root + "/Textures";
    private const string CampGroundFolder = "Assets/Resources/Environment/Camp/ground/";
    private const string StonyEarthTexture = CampGroundFolder + "CampStonyEarth_v1.png";
    /// <summary>Большое пятно вытоптанной земли с травяной рваной кромкой (M_Ground_Dirt лагеря): кромка уходит в дёрн.</summary>
    private const string WornDirtTexture = CampGroundFolder + "9e02a550-10a8-4dad-b8d6-39e8c2e44e50.png";
    /// <summary>Рисованные пятна разрытой земли лагеря (ART/CAMP/ground-2026-09-24): dirt_0 … dirt_5.</summary>
    private const string DirtDetailFolder = "Assets/Resources/Environment/Camp/GroundDetails/";
    private const string MeadowFolder = "Assets/Resources/Environment/Meadow/";
    private const string CobbleFolder = "Assets/RPG Tiny Fantasy Forest PBR/Mesh/Rock/";
    /// <summary>Метка запекания атласов: сменились правила запекания — атласы перепекаются (иначе берутся готовые).</summary>
    private const string EarthBakeVersion = "EarthBake1";
    /// <summary>Средний цвет CampStonyEarth_v1 в линейном (замер разведки 04.10: sRGB 177/145/98).</summary>
    private static readonly Vector3 StonyTextureMean = new Vector3(.448f, .288f, .122f);
    /// <summary>
    /// Каменистая земля в атласе плит — во столько темнее (линейно): с тоном дёрна пола (_BaseColor плит) бока и низ плиты
    /// выходят ~0,8 земли пола — сырая земля под дёрном.
    /// </summary>
    private const float SlabSoilDarken = .23f;
    /// <summary>Доля тона пола на свету (ср. GroundLightMatch 0,75 — сидело слишком тёмным): дёрн плит, комья, пятна.</summary>
    private const float SlabLightMatch = .9f, SoilLightMatch = .9f, DirtPatchGain = .72f, WornPatchGain = .55f;

    /// <summary>
    /// Материалы земли нырка и выхода (V14): плиты дёрна, комья, камни поляны — URP Particles/Lit непрозрачные (свет арены,
    /// цвет частицы только темнит); пятна разрытой земли — URP Particles/Lit прозрачные (свет как у пола, в тени не
    /// светятся); трещины, крошка, зерно, пыль — копии CFXR без освещения. Нет источника — слой берёт прежний материал.
    /// </summary>
    private static void EarthKit(Kit kit)
    {
        var theme = AssetDatabase.LoadAssetAtPath<LocationTheme>(ArenaThemePath);
        var style = theme != null ? theme.Style : null;
        var floorMaterial = style != null ? style.CampSurfaceMaterial : null;
        Texture floorTurf = floorMaterial != null && floorMaterial.HasProperty("_TurfTex") ? floorMaterial.GetTexture("_TurfTex") : null;
        string turfPath = floorTurf != null ? AssetDatabase.GetAssetPath(floorTurf) : null;
        if (string.IsNullOrEmpty(turfPath)) turfPath = FloorTurfFallback;

        // Плиты дёрна: атлас 1024 × 512 — слева дёрн пола, справа каменистая земля (темнее). Развёртка плит — SlabMesh.
        var turfAtlas = BakeAtlas("ThicketTurfSoil", 2, 1, 512, new[] { turfPath, StonyEarthTexture },
            (cell, pixels, x0, y0, width) => { if (cell == 1) DarkenLinear(pixels, x0, y0, 512, width, SlabSoilDarken); });
        var turfTone = FloorTurfTone(style);
        kit.Turf = SolidEarth("M_Thicket_Turf", turfAtlas != null ? turfAtlas : AssetDatabase.LoadAssetAtPath<Texture2D>(turfPath),
            new Color(turfTone.x / TurfTextureMean.x, turfTone.y / TurfTextureMean.y, turfTone.z / TurfTextureMean.z) * SlabLightMatch) ?? kit.Ground;

        var earthTone = FloorEarthTone(style);
        kit.StonySoil = SolidEarth("M_Thicket_StonySoil", AssetDatabase.LoadAssetAtPath<Texture2D>(StonyEarthTexture),
            new Color(earthTone.x / StonyTextureMean.x, earthTone.y / StonyTextureMean.y, earthTone.z / StonyTextureMean.z) * SoilLightMatch) ?? kit.Ground;

        // Камни поляны: Tripo-фактура каждого камня из его же материала поляны (MeadowPebble i_Surface).
        kit.RockA = SolidEarth("M_Thicket_RockA", PebbleTexture(0), new Color(.66f, .66f, .64f)) ?? kit.Earth;
        kit.RockB = SolidEarth("M_Thicket_RockB", PebbleTexture(1), new Color(.66f, .66f, .64f)) ?? kit.RockA;

        // Пятна разрытой земли: атлас 3 × 2 из dirt_0–5, пурпурная кайма полупрозрачного края заменена своим цветом пятна.
        var dirtSources = new string[6];
        for (int i = 0; i < dirtSources.Length; i++) dirtSources[i] = DirtDetailFolder + "dirt_" + i + ".png";
        var dirtAtlas = BakeAtlas("ThicketDirtPatches", 3, 2, 256, dirtSources,
            (cell, pixels, x0, y0, width) => Defringe(pixels, x0, y0, 256, width));
        kit.DirtPatch = LitPatch("M_Thicket_DirtPatch", dirtAtlas, DirtPatchGain);
        if (dirtAtlas == null && kit.DirtPatch != null)
        {
            // Без атласа — одно пятно dirt_0 (кайма тонет в тёмном тоне частицы).
            kit.DirtPatch.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dirtSources[0]));
            Note("атлас пятен ThicketDirtPatches не запёкся — пятна из одного dirt_0");
        }
        kit.WornPatch = LitPatch("M_Thicket_WornPatch", AssetDatabase.LoadAssetAtPath<Texture2D>(WornDirtTexture), WornPatchGain);

        kit.CrackRadial = Textured(NoDissolve(PackCopy("M_Thicket_CrackRadial", CfxrTrailMaterial)), HovlTextures + "Crater43.png", true) ?? kit.Crack;
        kit.CrackFine = Textured(NoDissolve(PackCopy("M_Thicket_CrackFine", CfxrTrailMaterial)), HovlTextures + "Crack6.png", true) ?? kit.Crack;
        kit.Rubble = Textured(Plain(PackCopy("M_Thicket_Rubble", CfxrSmokeBlurred)), HovlTextures + "Crater18.png", true) ?? kit.Soil;
        foreach (var decal in new[] { kit.CrackRadial, kit.CrackFine, kit.Rubble }) Queue(decal, 2990);
        kit.Grain = Textured(Plain(PackCopy("M_Thicket_Grain", CfxrDebrisUnlit)), HovlTextures + "Debris2.png", false) ?? kit.Clod;
        kit.DustFlip = EarthDustFlipbook
            ? Hdr(Textured(Plain(PackCopy("M_Thicket_DustFlip", CfxrSmokeBlurred)), HovlTextures + "SmokeAnim2.png", false), DustFlipHdr) ?? kit.Haze
            : kit.Haze;
    }

    /// <summary>
    /// Пыль нырка и выхода — объёмная SmokeAnim2 Hovl (8 × 8 кадров, клуб растёт и редеет) на копии CFXR. Ведущему для A/B:
    /// false — прежняя мягкая пыль kit.Haze (cfxr cloud blur), раскладка и сроки те же.
    /// </summary>
    private static readonly bool EarthDustFlipbook = true;
    /// <summary>SmokeAnim2 серая (среднее 67 из 255): цвет частицы × HDR — землистая пыль, не светится.</summary>
    private const float DustFlipHdr = 2.4f;

    private static Texture2D PebbleTexture(int index)
    {
        var surface = AssetDatabase.LoadAssetAtPath<Material>(MeadowFolder + "MeadowPebble" + index + "_Surface.mat");
        var texture = surface != null && surface.HasProperty("_BaseMap") ? surface.GetTexture("_BaseMap") as Texture2D : null;
        if (texture == null) Note(MeadowFolder + "MeadowPebble" + index + "_Surface.mat (фактура камня поляны)");
        return texture;
    }

    /// <summary>
    /// Тон дёрна пола в линейном (CampPainterlyGround — тот же счёт, что у мха пригорка, KnollMossMaterial): луг на 10 % к
    /// серому × множители шейдера, дёрн × (0,89; 0,97; 0,86), доля дёрна 0,8 × GroundTurfWeight.
    /// </summary>
    private static Vector3 FloorTurfTone(LayoutStyle style)
    {
        float turfWeight = style != null ? style.GroundTurfWeight : .85f;
        var meadow = MeadowTextureMean;
        float luma = .2126f * meadow.x + .7152f * meadow.y + .0722f * meadow.z;
        var grass = new Vector3((.1f * luma + .9f * meadow.x) * .60f * .925f, (.1f * luma + .9f * meadow.y) * .82f * .95f,
            (.1f * luma + .9f * meadow.z) * .68f * .875f);
        var turfTone = new Vector3(TurfTextureMean.x * .89f, TurfTextureMean.y * .97f, TurfTextureMean.z * .86f);
        return Vector3.Lerp(grass, turfTone, Mathf.Clamp01(.8f * turfWeight));
    }

    /// <summary>Тон земли пола в линейном (тот же счёт, что у EarthMaterial): фактура × яркость × тон, к серому на 30 %.</summary>
    private static Vector3 FloorEarthTone(LayoutStyle style)
    {
        float brightness = style != null ? style.EarthBrightness : 2.3f;
        Color tint = (style != null ? style.EarthTint : new Color(1.18f, 1.05f, .72f)).linear;
        var mean = new Vector3(EarthTextureMean.x * brightness * tint.r, EarthTextureMean.y * brightness * tint.g, EarthTextureMean.z * brightness * tint.b);
        float luma = .2126f * mean.x + .7152f * mean.y + .0722f * mean.z;
        return new Vector3((.3f * luma + .7f * mean.x) * .92f, (.3f * luma + .7f * mean.y) * .86f, (.3f * luma + .7f * mean.z) * .78f) * .95f;
    }

    /// <summary>
    /// Непрозрачная земля-меш (плиты, комья, камни): URP Particles/Lit, фактура texture, множитель gain в линейном, цвет
    /// частицы умножает; свет и тени арены на ней (своей тени нет — у Particles/Lit нет ShadowCaster).
    /// </summary>
    private static Material SolidEarth(string name, Texture2D texture, Color gain)
    {
        var material = UrpParticles(name, "Universal Render Pipeline/Particles/Lit", null, false);
        if (material == null) return null;
        OpaqueParticle(material, false);
        if (texture != null) material.SetTexture("_BaseMap", texture);
        else Note("фактура " + name + " — без неё белая");
        SetLinearBaseColor(material, gain);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .05f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        material.DisableKeyword("_SPECULAR_SETUP");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Пятно земли на полу: URP Particles/Lit прозрачное (альфа-смешение, без записи глубины), свет как у пола — в синей
    /// тени не светится, на свету не плоское; множитель gain (линейный, серый); очередь 2985 — под трещинами CFXR (2990).
    /// </summary>
    private static Material LitPatch(string name, Texture2D texture, float gain)
    {
        var material = UrpParticles(name, "Universal Render Pipeline/Particles/Lit", null, false);
        if (material == null) return null;
        if (texture != null) material.SetTexture("_BaseMap", texture);
        else Note("фактура " + name);
        void Set(string property, float value) { if (material.HasProperty(property)) material.SetFloat(property, value); }
        Set("_Surface", 1f); Set("_Blend", 0f); Set("_ColorMode", 0f); Set("_AlphaClip", 0f); Set("_ZWrite", 0f); Set("_Cull", 0f);
        Set("_SrcBlend", (float)BlendMode.SrcAlpha); Set("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        Set("_SrcBlendAlpha", (float)BlendMode.One); Set("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        Set("_SoftParticlesEnabled", 0f); Set("_CameraFadingEnabled", 0f); Set("_DistortionEnabled", 0f); Set("_FlipbookBlending", 0f);
        Set("_ReceiveShadows", 1f); Set("_Smoothness", .04f); Set("_Metallic", 0f);
        // Очередь: и при автоматической (3000 + смещение), и при своей — под трещинами CFXR.
        Set("_QueueOffset", -15f); Set("_QueueControl", 1f);
        foreach (var keyword in new[] { "_ALPHATEST_ON", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON", "_COLORADDSUBDIFF_ON", "_COLOROVERLAY_ON",
            "_COLORCOLOR_ON", "_SOFTPARTICLES_ON", "_FADING_ON", "_DISTORTION_ON", "_FLIPBOOKBLENDING_ON", "_RECEIVE_SHADOWS_OFF", "_NORMALMAP",
            "_SPECULAR_SETUP", "_EMISSION" })
            material.DisableKeyword(keyword);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent - 15;
        SetLinearBaseColor(material, new Color(gain, gain, gain));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Атлас columns × rows клеток cell² из текстур sources (по строкам сверху), запечён в Attacks/Textures/name.png: каждая
    /// текстура рисуется в RenderTexture (источники не читаемые — GetPixels нельзя), клетка читается ReadPixels в своё место,
    /// process правит пиксели клетки (кайма, тон). Уже запечён с той же меткой — берётся готовый. Нет графики (пакетный
    /// режим без устройства) или источника — null (слой берёт запасную фактуру), отмечено.
    /// </summary>
    private static Texture2D BakeAtlas(string name, int columns, int rows, int cell, string[] sources,
        System.Action<int, Color32[], int, int, int> process)
    {
        string path = TextureFolder + "/" + name + ".png";
        string stamp = EarthBakeVersion + "|" + cell + "|" + string.Join("|", sources);
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && AssetImporter.GetAtPath(path) is TextureImporter baked && baked.userData == stamp) return existing;
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Note("атлас " + name + ": нет графического устройства — не запечён");
            return existing;
        }
        int width = columns * cell, height = rows * cell;
        var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var rt = RenderTexture.GetTemporary(cell, cell, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        try
        {
            for (int i = 0; i < sources.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sources[i]);
                if (source == null) { Note(sources[i] + " (атлас " + name + ")"); return existing; }
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                atlas.ReadPixels(new Rect(0, 0, cell, cell), (i % columns) * cell, (rows - 1 - i / columns) * cell, false);
                RenderTexture.active = previous;
            }
            var pixels = atlas.GetPixels32();
            long sum = 0;
            for (int i = 0; i < pixels.Length; i += 7) sum += pixels[i].r + pixels[i].g + pixels[i].b;
            if (sum == 0) { Note("атлас " + name + ": чтение RenderTexture вернуло пустоту — не запечён"); return existing; }
            if (process != null)
                for (int i = 0; i < sources.Length; i++) process(i, pixels, (i % columns) * cell, (rows - 1 - i / columns) * cell, width);
            atlas.SetPixels32(pixels);
            atlas.Apply(false);
            System.IO.File.WriteAllBytes(path, atlas.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(atlas);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            // Только степень двойки (768 у атласа 3×2 по 256 импортёр не примет) — ближайшая сверху.
            importer.maxTextureSize = Mathf.Min(2048, Mathf.NextPowerOfTwo(Mathf.Max(width, height)));
            importer.userData = stamp;
            importer.SaveAndReimport();
        }
        Debug.Log(Log + "Запечён атлас " + path + " (" + width + " × " + height + ").");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Клетка темнее в k раз в линейном пространстве (пиксели — гамма sRGB).</summary>
    private static void DarkenLinear(Color32[] pixels, int x0, int y0, int cell, int width, float k)
    {
        var table = new byte[256];
        for (int v = 0; v < 256; v++)
            table[v] = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(v / 255f) * k)), 0, 255);
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                int i = (y0 + y) * width + x0 + x;
                var p = pixels[i];
                pixels[i] = new Color32(table[p.r], table[p.g], table[p.b], p.a);
            }
    }

    /// <summary>
    /// Кайма пятен dirt_*: полупрозрачный край несёт пурпур (RGB 86/12/72 при альфе &lt; 40, 111/43/58 при 40–120) — в
    /// альфа-смешении это лиловый ореол. Край получает средний цвет плотной середины своего пятна (альфа &gt; 200),
    /// переход 120–200 — смесью; альфа не меняется.
    /// </summary>
    private static void Defringe(Color32[] pixels, int x0, int y0, int cell, int width)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                var p = pixels[(y0 + y) * width + x0 + x];
                if (p.a <= 200) continue;
                r += p.r; g += p.g; b += p.b; n++;
            }
        if (n == 0) return;
        var mean = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                int i = (y0 + y) * width + x0 + x;
                var p = pixels[i];
                if (p.a >= 200) continue;
                float t = p.a < 120 ? 0f : (p.a - 120) / 80f;
                pixels[i] = new Color32((byte)Mathf.Lerp(mean.r, p.r, t), (byte)Mathf.Lerp(mean.g, p.g, t), (byte)Mathf.Lerp(mean.b, p.b, t), p.a);
            }
    }

    /// <summary>Кора корней и шипов: URP Lit с RootBark.png Корнехвата (запасная — кора пака), тень есть.</summary>
    private static Material Wood(string name, Color tint)
    {
        var material = PelagWhirlwindVfxSetup.LoadOrCreateMaterial(MaterialFolder + "/" + name + ".mat", Shader.Find("Universal Render Pipeline/Lit"));
        var bark = AssetDatabase.LoadAssetAtPath<Texture2D>(RootBarkTexture);
        if (bark == null)
        {
            Note(RootBarkTexture + " (кора корней; взята " + BarkFallback + ")");
            bark = AssetDatabase.LoadAssetAtPath<Texture2D>(BarkFallback);
        }
        if (bark != null) material.SetTexture("_BaseMap", bark);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", .12f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // -------------------------------------------------------------- geometry

    private static void Geometry(Kit kit)
    {
        kit.LeafMesh = LoadMesh(CfxrLeafMesh, null);
        // Комья топота и лапы (ревью 02.10) — сколотые многогранники; земля нырка и смерти — гладкая (ниже).
        kit.Chunks = new[] { ChunkMesh("ThicketChunkA", .55f, 61), ChunkMesh("ThicketChunkB", .75f, 62), ChunkMesh("ThicketChunkC", .45f, 63) };
        // Модели Hovl (замер Blender 02.10, ось +Y вверх, основание в нуле): горб r1 h1,
        // цилиндр r1 h2, цветок ~2,3 × 1,74 × 2,4 — чашкой вверх.
        kit.HalfSphere = LoadMesh(HovlModels + "HalfSphere2.fbx", null);
        kit.Cylinder = LoadMesh(HovlModels + "CylinderFromGround.fbx", "CylinderFromGround");
        kit.FlowerMesh = LoadMesh(HovlModels + "Flower.fbx", null);
        kit.BerryMesh = Icosphere("ThicketBerry");
        // Земля, которую двигает босс (владелец 03.10, ночь: «слишком большие куски примитивов»): гладкие сетки с
        // шумом и сглаженными нормалями на фактуре пола — холм (основание + лоскут на каждый бугор), голова и гребень
        // бугра, вал по контуру тела у нырка и выхода; обломки — мелкие комья и камешки.
        kit.Clods = new[] { ClodMesh("ThicketClodA", 91, .32f, .78f), ClodMesh("ThicketClodB", 92, .40f, .62f), ClodMesh("ThicketClodC", 93, .26f, .88f) };
        kit.Pebbles = new[] { ClodMesh("ThicketPebbleA", 95, .14f, .55f), ClodMesh("ThicketPebbleB", 96, .18f, .48f) };
        // Голова бугра V9–V14 (гладкий вал SwellMesh «ThicketRidgeHead») — владелец 07.10: «плоский шарик земляной»; V15 —
        // каменистая куча, с V17 головы нет вовсе (земля рвётся на месте). Валики RidgeSwells остаются: на них рваные пятна
        // пригорка смерти.
        kit.RidgeSwells = new[] { SwellMesh("ThicketRidgeSwellA", 102, 1f, 1f, 1f, .3f, 8, 22), SwellMesh("ThicketRidgeSwellB", 103, 1f, 1f, 1f, .38f, 8, 22) };
        // V14 (владелец 04.10: вал по контуру — «кольцо какое-то», земля «гладкая плоская»): земля нырка и выхода — из паков:
        // плиты дёрна (Cobble01–06 RPG Tiny Fantasy Forest с новой развёрткой на атлас дёрн | земля), камни поляны
        // (MeadowPebble0/1 — Tripo-камень арены), большие комья (NoiseSphere1 Hovl на каменистой земле).
        var slabs = new List<Mesh>();
        for (int i = 1; i <= 6; i++)
        {
            var slab = SlabMesh("ThicketSlab" + i, CobbleFolder + "Cobble0" + i + ".fbx", 300 + i);
            if (slab != null) slabs.Add(slab);
        }
        kit.Slabs = slabs.Count > 0 ? slabs.ToArray() : kit.Chunks;
        kit.RockMeshA = RockMesh("ThicketRockA", MeadowFolder + "MeadowPebble0_Mesh.asset");
        kit.RockMeshB = RockMesh("ThicketRockB", MeadowFolder + "MeadowPebble1_Mesh.asset");
        var bigA = BigClodMesh("ThicketBigClodA", 331, .72f);
        var bigB = BigClodMesh("ThicketBigClodB", 332, .86f);
        kit.BigClods = bigA != null && bigB != null ? new[] { bigA, bigB } : kit.Clods;
        // V17: каменистой кучи головы бугра (V15) нет — земля рвётся на месте; трещины разбегаются от головы лучами.
        kit.CrackRay = FlatRay("ThicketCrackRay");
        // Холм смерти: гладкое основание (HillGround) и мох (MossMesh) — один рельеф ThicketMasterDeathRules.KnollSurface,
        // фактура в координатах холма без швов; мох — пятнами в цвете вершин. Ростки — код корня Корнехвата, тоньше.
        // V13: мох — одна сетка (ячейки V11 оседали у героя ямой), по пригорку ходят.
        kit.Hill = HillMesh("ThicketDeathHill");
        kit.KnollMossMesh = MossMesh("ThicketDeathMoss");
        kit.KnollFlower = KnollFlowerMesh(kit.FlowerMesh);
        kit.KnollTuft = TuftMesh("ThicketKnollTuft", 121);
        kit.KnollLeaves = LeafRosetteMesh("ThicketKnollLeaves", 131);
        kit.Sprouts = new[]
        {
            CurlRoot("ThicketSproutA", 41, 1f, .07f, 2.2f, .06f, 0, 1.4f),
            CurlRoot("ThicketSproutB", 42, .9f, .06f, 2.6f, .08f, 0, 2f),
            CurlRoot("ThicketSproutC", 43, 1.1f, .075f, 1.9f, .05f, 0, 1.2f),
        };
        // Шипы прорастания (код корня-шипа Вендиго): радиус, загиб, скрутка, узлы, колючки.
        kit.Spikes = new[]
        {
            RootSpike("ThicketSpikeA", 11, .15f, .12f, 1.4f, .10f, 1),
            RootSpike("ThicketSpikeB", 12, .13f, .20f, 2.2f, .12f, 2),
            RootSpike("ThicketSpikeC", 13, .16f, .08f, 1.0f, .08f, 0),
            RootSpike("ThicketSpikeD", 14, .12f, .24f, 2.8f, .16f, 1),
        };
        // Корни выхода из-под земли и пробуждения (код корня Корнехвата): длина, радиус, загиб, узлы, колючки, скрутка.
        kit.Curls = new[]
        {
            CurlRoot("ThicketCurlA", 21, 1.15f, .15f, 1.0f, .16f, 3, 2.2f),
            CurlRoot("ThicketCurlB", 22, .95f, .13f, 1.3f, .18f, 2, 2.8f),
            CurlRoot("ThicketCurlC", 23, 1.30f, .16f, .8f, .14f, 2, 1.8f),
        };
        // Кончики корней под кругом прорастания — высовываются за 6 тиков до шипов.
        kit.Tips = new[]
        {
            CurlRoot("ThicketTipA", 31, .26f, .06f, .9f, .12f, 1, 2f),
            CurlRoot("ThicketTipB", 32, .22f, .05f, 1.2f, .14f, 0, 2.6f),
        };
    }

    /// <summary>Меш из файла пака; nameSuffix — подмеш по окончанию имени (FBX с несколькими мешами).</summary>
    private static Mesh LoadMesh(string path, string nameSuffix)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh && (nameSuffix == null || mesh.name.EndsWith(nameSuffix, System.StringComparison.Ordinal))) return mesh;
        Note(path + (nameSuffix != null ? " → меш «" + nameSuffix + "»" : " (меш)"));
        return null;
    }

    /// <summary>Ягода: икосфера одного деления (80 треугольников), радиус 0,5, белый Color32 (Float32-цвет ломает меш-частицу).</summary>
    private static Mesh Icosphere(string name)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var points = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var cache = new Dictionary<long, int>();
        int Middle(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int found)) return found;
            points.Add(((points[a] + points[b]) * .5f).normalized);
            cache[key] = points.Count - 1;
            return points.Count - 1;
        }
        for (int i = 0; i < 12; i++) points[i] = points[i].normalized;
        var triangles = new List<int>();
        for (int f = 0; f < faces.Length; f += 3)
        {
            int a = faces[f], b = faces[f + 1], c = faces[f + 2];
            int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
            triangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var uv = new List<Vector2>();
        foreach (var p in points)
        {
            vertices.Add(p * .5f);
            colors.Add(new Color32(255, 255, 255, 255));
            uv.Add(new Vector2(.5f + Mathf.Atan2(p.z, p.x) / (2f * Mathf.PI), .5f + p.y * .5f));
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Ком земли / камень: сплюснутый неровный октаэдр со сколами (код EnemyDeathVfxSetup), около 1 м, грани плоские.</summary>
    private static Mesh ChunkMesh(string name, float flatten, int salt)
    {
        Vector3[] corners =
        {
            new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 c = corners[i] * .5f;
            c += new Vector3(Mathf.Lerp(-.14f, .14f, Hash01(i * 3, salt)), Mathf.Lerp(-.1f, .1f, Hash01(i * 3 + 1, salt)),
                Mathf.Lerp(-.14f, .14f, Hash01(i * 3 + 2, salt)));
            c.y *= flatten;
            corners[i] = c;
        }
        int[,] faces = { { 0, 4, 2 }, { 0, 2, 5 }, { 0, 5, 3 }, { 0, 3, 4 }, { 1, 2, 4 }, { 1, 5, 2 }, { 1, 3, 5 }, { 1, 4, 3 } };
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        for (int f = 0; f < faces.GetLength(0); f++)
        {
            Vector3 a = corners[faces[f, 0]], b = corners[faces[f, 1]], c = corners[faces[f, 2]];
            Vector3 mid = (b + c) * .5f + (b + c - 2f * a).normalized * Mathf.Lerp(-.04f, .06f, Hash01(100 + f, salt));
            Face(vertices, colors, triangles, a, b, mid, a + b + mid, Tone(f * 2, salt));
            Face(vertices, colors, triangles, a, mid, c, a + mid + c, Tone(f * 2 + 1, salt));
        }
        return SolidMesh(name, vertices, colors, triangles);
    }

    /// <summary>Тон грани 170…255: комья не одного цвета, но цвет задаёт частица.</summary>
    private static byte Tone(int face, int salt) => (byte)Mathf.RoundToInt(Mathf.Lerp(170f, 255f, Hash01(face, salt + 500)));

    /// <summary>Треугольник наружу (по outward), своя тройка вершин — плоская грань, тон Color32.</summary>
    private static void Face(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 c,
        Vector3 outward, byte tone)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var swap = b; b = c; c = swap; }
        int at = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        var color = new Color32(tone, tone, tone, 255);
        colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
    }

    private static Mesh SolidMesh(string name, List<Vector3> vertices, List<Color32> colors, List<int> triangles)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        var uv = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) uv.Add(new Vector2(.5f, .5f));
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ------------------------------------------------------------- smooth soil (03.10, ночь)

    /// <summary>Значение шума 0…1 на решётке (детерминированно, Hash01), сглаженная интерполяция.</summary>
    private static float Noise2(float x, float z, int salt)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);
        float a = Lattice(ix, iz, salt), b = Lattice(ix + 1, iz, salt), c = Lattice(ix, iz + 1, salt), d = Lattice(ix + 1, iz + 1, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    private static float Lattice(int x, int z, int salt) => Hash01(unchecked(x * 92837111 ^ z * 689287499), salt);

    /// <summary>Бугристость земли −1…1: octaves октав шума, каждая вдвое мельче и слабее.</summary>
    private static float Fbm(float x, float z, int salt, int octaves)
    {
        float sum = 0f, amp = .5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * (Noise2(x, z, salt + o * 17) * 2f - 1f);
            norm += amp;
            x = x * 2.03f + 1.7f;
            z = z * 2.03f - 3.1f;
            amp *= .5f;
        }
        return sum / norm;
    }

    /// <summary>Шум 0…1 точки единичной сферы (три проекции) — бугры комьев без шва.</summary>
    private static float SphereNoise(Vector3 p, float scale, int salt)
        => (Noise2(p.x * scale + 3.1f, p.y * scale, salt) + Noise2(p.y * scale - 1.7f, p.z * scale, salt + 1)
            + Noise2(p.z * scale, p.x * scale + 5.3f, salt + 2)) / 3f;

    /// <summary>Тон вершины Color32 (серый, ≤ 255): цвет задаёт частица, вершина только темнит.</summary>
    private static Color32 Gray(float k)
    {
        byte b = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(k));
        return new Color32(b, b, b, 255);
    }

    /// <summary>Единичная сфера: икосаэдр, subdivisions делений (1 — 42 вершины, 80 треугольников), вершины общие.</summary>
    private static void UnitSphere(int subdivisions, List<Vector3> points, List<int> triangles)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        points.Clear(); triangles.Clear();
        points.AddRange(new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        });
        for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
        triangles.AddRange(new[]
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        });
        for (int level = 0; level < subdivisions; level++)
        {
            var cache = new Dictionary<long, int>();
            int Middle(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int found)) return found;
                points.Add(((points[a] + points[b]) * .5f).normalized);
                cache[key] = points.Count - 1;
                return points.Count - 1;
            }
            var next = new List<int>(triangles.Count * 4);
            for (int f = 0; f < triangles.Count; f += 3)
            {
                int a = triangles[f], b = triangles[f + 1], c = triangles[f + 2];
                int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles.Clear();
            triangles.AddRange(next);
        }
    }

    /// <summary>
    /// Ком земли (владелец 03.10, ночь: вместо «больших кусков примитивов» — мелкая настоящая земля): икосфера одного
    /// деления (42 вершины), радиус ~0,5 с бугристостью jitter (крупный и мелкий шум), сплюснут по высоте до flatten,
    /// низ чуть приплюснут; нормали сглажены — катышек рыхлой земли, а не многогранник. Тон 0,8–1 (Color32), в
    /// ямках темнее. UV — мелкий кусок фактуры земли.
    /// </summary>
    private static Mesh ClodMesh(string name, int salt, float jitter, float flatten)
    {
        var points = new List<Vector3>();
        var triangles = new List<int>();
        UnitSphere(1, points, triangles);
        var vertices = new List<Vector3>(points.Count);
        var colors = new List<Color32>(points.Count);
        var uv = new List<Vector2>(points.Count);
        foreach (var p in points)
        {
            float coarse = SphereNoise(p, 1.4f, salt) * 2f - 1f, fine = SphereNoise(p, 3.6f, salt + 7) * 2f - 1f;
            float r = .5f * (1f + jitter * (.75f * coarse + .45f * fine));
            var v = p * r;
            v.y *= flatten;
            if (v.y < -.18f * flatten) v.y = Mathf.Lerp(v.y, -.18f * flatten, .6f);
            vertices.Add(v);
            colors.Add(Gray(Mathf.Lerp(.8f, 1f, Mathf.Clamp01(.55f + .9f * (coarse * .6f + fine * .4f)))));
            uv.Add(new Vector2(v.x + v.y * .5f, v.z - v.y * .5f) * .25f + new Vector2(Hash01(salt, 3), Hash01(salt, 4)));
        }
        return SoilMesh(name, vertices, uv, colors, triangles);
    }

    /// <summary>Сетка земли: общие вершины — сглаженные нормали, Color32, UV фактуры.</summary>
    private static Mesh SoilMesh(string name, List<Vector3> vertices, List<Vector2> uv, List<Color32> colors, List<int> triangles)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Доля фактуры дёрна на ширину плиты (плита ~0,85 м частицей, дёрн пола — плитка 5,2 м): верх плиты — кусок пола той
    /// же крупности. Каменистая земля боков — своя доля (крупнее зерно, видно на 13 px бока).
    /// </summary>
    private const float SlabTurfSpan = .85f / TurfTileMeters, SlabSoilSpan = .3f;

    /// <summary>Самый подробный меш файла пака (LOD0 — больше всех вершин); нет — null, отмечено.</summary>
    private static Mesh PackMesh(string path, string what)
    {
        Mesh best = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Mesh mesh && (best == null || mesh.vertexCount > best.vertexCount)) best = mesh;
        if (best == null) Note(path + " (" + what + ")");
        return best;
    }

    /// <summary>Вершины и треугольники меша пака: копия (Instantiate — у FBX паков Read/Write выключен), копия снимается.</summary>
    private static bool ReadMesh(Mesh source, out Vector3[] points, out Vector3[] normals, out Vector2[] uv, out int[] triangles)
    {
        var work = Object.Instantiate(source);
        try
        {
            points = work.vertices;
            normals = work.normals;
            uv = work.uv;
            triangles = work.triangles;
        }
        finally { Object.DestroyImmediate(work); }
        bool ok = points != null && points.Length > 2 && triangles != null && triangles.Length >= 3;
        if (!ok) Note(source.name + " (меш пака не прочитался — слой берёт свой меш)");
        return ok;
    }

    /// <summary>
    /// Плита дёрна (V14) — копия плоского камня-плитки пака (Cobble0N.fbx, RPG Tiny Fantasy Forest PBR; ассет пака не
    /// меняется): тонкая ось — вверх (лежит), середина в нуле (кувыркается вокруг себя), наибольший размер по земле — 1.
    /// Развёртка пака сведена в одну клетку палитры — пишется новая, треугольники раздельные: верх (нормаль вверх) —
    /// дёрн пола (левая половина атласа, плоско по x/z, своя доля фактуры на плиту), бока и низ — каменистая земля
    /// (правая половина). Тон Color32: верх 0,9–1 на всю плиту, бока 0,78, низ 0,62 (цвет частицы умножает).
    /// </summary>
    private static Mesh SlabMesh(string name, string path, int salt)
    {
        var source = PackMesh(path, "плита дёрна");
        if (source == null || !ReadMesh(source, out var points, out _, out _, out var triangles)) return null;
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        int thin = b.size.x < b.size.y ? (b.size.x < b.size.z ? 0 : 2) : (b.size.y < b.size.z ? 1 : 2);
        // Поворот (не зеркало — обход треугольников сохраняется): тонкая ось → +Y.
        Vector3 Turn(Vector3 p) => thin == 0 ? new Vector3(-p.y, p.x, p.z) : thin == 2 ? new Vector3(p.x, p.z, -p.y) : p;
        Vector3 centre = Turn(b.center), size = Turn(b.size);
        float scale = 1f / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)));
        float topU = Mathf.Lerp(.03f, .78f, Hash01(1, salt)), topV = Mathf.Lerp(.03f, .78f, Hash01(2, salt));
        float soilU = Mathf.Lerp(.03f, .6f, Hash01(3, salt)), soilV = Mathf.Lerp(.03f, .6f, Hash01(4, salt));
        byte topTone = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(.9f, 1f, Hash01(5, salt)));
        var vertices = new List<Vector3>(triangles.Length);
        var colors = new List<Color32>(triangles.Length);
        var uv = new List<Vector2>(triangles.Length);
        var faces = new List<int>(triangles.Length);
        var corner = new Vector3[3];
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++) corner[k] = (Turn(points[triangles[t + k]]) - centre) * scale;
            var n = Vector3.Cross(corner[1] - corner[0], corner[2] - corner[0]);
            if (n.sqrMagnitude < 1e-12f) continue;
            n.Normalize();
            bool top = n.y > .45f, bottom = n.y < -.45f;
            // Верх — один тон на плиту (тон по треугольникам дробил бы дёрн на грани), бока и низ темнее.
            byte tone = top ? topTone : bottom ? (byte)158 : (byte)199;
            for (int k = 0; k < 3; k++)
            {
                var p = corner[k];
                Vector2 w;
                if (top) w = new Vector2(.5f * Mathf.Clamp(topU + (p.x + .5f) * SlabTurfSpan, .01f, .99f), Mathf.Clamp(topV + (p.z + .5f) * SlabTurfSpan, .01f, .99f));
                else
                {
                    var q = bottom ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                    w = new Vector2(.5f + .5f * Mathf.Clamp(soilU + (q.x + .5f) * SlabSoilSpan, .02f, .98f), Mathf.Clamp(soilV + (q.y + .5f) * SlabSoilSpan, .01f, .99f));
                }
                faces.Add(vertices.Count);
                vertices.Add(p);
                uv.Add(w);
                colors.Add(new Color32(tone, tone, tone, 255));
            }
        }
        if (faces.Count < 3) { Note(path + " (плита дёрна: нет треугольников)"); return null; }
        return SoilMesh(name, vertices, uv, colors, faces);
    }

    /// <summary>
    /// Камень поляны (V14) — копия камня арены (MeadowPebble i_Mesh: Tripo arena_smallRock, упрощённый поляной; ассет
    /// поляны не меняется): Z-вверх → Y-вверх, середина в нуле (кувыркается вокруг себя, а не вокруг подошвы), наибольший
    /// размер — 1. Развёртка Tripo своя — фактура камня из материала поляны (M_Thicket_RockA/B). Тон вершин белый Color32.
    /// </summary>
    private static Mesh RockMesh(string name, string path)
    {
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (source == null) { Note(path + " (камень поляны)"); return null; }
        if (!ReadMesh(source, out var points, out var normals, out var uv, out var triangles)) return null;
        var vertices = new List<Vector3>(points.Length);
        var turned = new List<Vector3>(points.Length);
        var b = new Bounds(new Vector3(points[0].x, points[0].z, -points[0].y), Vector3.zero);
        foreach (var p in points) b.Encapsulate(new Vector3(p.x, p.z, -p.y));
        float scale = 1f / Mathf.Max(.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        foreach (var p in points) vertices.Add((new Vector3(p.x, p.z, -p.y) - b.center) * scale);
        bool hasNormals = normals != null && normals.Length == points.Length;
        if (hasNormals) foreach (var n in normals) turned.Add(new Vector3(n.x, n.z, -n.y));
        var colors = new List<Color32>(points.Length);
        for (int i = 0; i < points.Length; i++) colors.Add(new Color32(255, 255, 255, 255));
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        if (uv != null && uv.Length == points.Length) mesh.SetUVs(0, new List<Vector2>(uv));
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        if (hasNormals) mesh.SetNormals(turned);
        else mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Большой ком земли (V14) — копия бугристой сферы Hovl (NoiseSphere1.fbx; ассет пака не меняется): середина в нуле,
    /// наибольший размер 1, сплюснут до flatten, низ чуть приплюснут; развёртка — как у мелких комьев (кусок каменистой
    /// земли в метрах), тон вершин 0,8–1 (снизу темнее).
    /// </summary>
    private static Mesh BigClodMesh(string name, int salt, float flatten)
    {
        var source = PackMesh(HovlModels + "NoiseSphere1.fbx", "большой ком");
        if (source == null || !ReadMesh(source, out var points, out _, out _, out var triangles)) return null;
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        float scale = 1f / Mathf.Max(.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        var vertices = new List<Vector3>(points.Length);
        var colors = new List<Color32>(points.Length);
        var uv = new List<Vector2>(points.Length);
        var offset = new Vector2(Hash01(salt, 3), Hash01(salt, 4)) * .7f;
        foreach (var p in points)
        {
            var v = (p - b.center) * scale;
            v.y *= flatten;
            if (v.y < -.2f * flatten) v.y = Mathf.Lerp(v.y, -.2f * flatten, .55f);
            vertices.Add(v);
            colors.Add(Gray(Mathf.Lerp(.8f, 1f, Mathf.Clamp01(.6f + v.y))));
            uv.Add(new Vector2(v.x + v.y * .5f, v.z - v.y * .5f) * .25f + offset);
        }
        return SoilMesh(name, vertices, uv, colors, new List<int>(triangles));
    }

    /// <summary>
    /// Полярная сетка рельефа: центр, rings колец × sides по углу (общие вершины, нормали сглажены), point(t, angle) —
    /// точка на доле радиуса t (0 — центр, 1 — край; кольцо rings + 1 — юбка под землю, если skirt). Треугольники наружу
    /// (вверх).
    /// </summary>
    private static void PolarGrid(int rings, int sides, bool skirt, System.Func<float, float, Vector3> point, List<Vector3> vertices,
        List<int> triangles)
    {
        int last = skirt ? rings + 1 : rings;
        vertices.Add(point(0f, 0f));
        for (int r = 1; r <= last; r++)
            for (int s = 0; s < sides; s++)
            {
                float t = r <= rings ? r / (float)rings : 1f + (r - rings) * .07f;
                float angle = (s + (r % 2) * .5f) / sides * Mathf.PI * 2f;
                vertices.Add(point(t, angle));
            }
        int Ring(int r, int s) => 1 + (r - 1) * sides + ((s % sides) + sides) % sides;
        // Порядок вершин: Cross(b − a, c − a) смотрит вверх (лицо Unity).
        for (int s = 0; s < sides; s++) triangles.AddRange(new[] { 0, Ring(1, s), Ring(1, s + 1) });
        for (int r = 1; r < last; r++)
        {
            // Кольца сдвинуты на полшага через одно: треугольники ровнее, без лучей «звезды».
            bool odd = r % 2 == 1;
            for (int s = 0; s < sides; s++)
            {
                int a = Ring(r, s), b = Ring(r, s + 1);
                int c = odd ? Ring(r + 1, s) : Ring(r + 1, s - 1), d = odd ? Ring(r + 1, s + 1) : Ring(r + 1, s);
                triangles.AddRange(new[] { a, d, b, a, c, d });
            }
        }
    }

    /// <summary>Неровный контур вала/бугра по углу: множитель радиуса около 1 (без правильного эллипса).</summary>
    private static float Outline(float angle, int salt, float amount)
        => 1f + amount * (.55f * Mathf.Sin(2f * angle + 6.28f * Hash01(1, salt)) + .3f * Mathf.Sin(3f * angle + 6.28f * Hash01(2, salt))
            + .15f * Mathf.Sin(5f * angle + 6.28f * Hash01(3, salt)));

    /// <summary>Мягкий горб: 1 в середине, 0 с нулевым наклоном на краю (t — доля радиуса).</summary>
    private static float Bell(float t) => t >= 1f ? 0f : (1f - t * t) * (1f - t * t);

    /// <summary>
    /// Вал земли (голова «Дюны» и валики гребня бугра): гладкий вытянутый по Z горб полуширины halfW, полудлины halfL
    /// (нос +Z короче и круче, хвост длиннее), высоты height; контур неровный, рыхлые бугры шумом noise (доля высоты)
    /// и мелкая крошка; край уходит под землю юбкой. Тон: свежая сырая земля, в ложбинах и у края темнее. UV — фактура
    /// земли в метрах (у валиков гребня — на типичный размер частицы ~0,75 м).
    /// </summary>
    private static Mesh SwellMesh(string name, int salt, float halfW, float halfL, float height, float noise, int rings, int sides)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var heights = new List<float>();
        float meters = halfW > 1.01f ? 1f : .75f;
        Vector3 Point(float t, float angle)
        {
            float k = t * Outline(angle, salt, .09f);
            float along = Mathf.Cos(angle) >= 0f ? halfL * .82f : halfL * 1.18f;
            float x = Mathf.Sin(angle) * halfW * k, z = Mathf.Cos(angle) * along * k;
            float nx = x / halfW * 1.6f, nz = z / halfL * 1.6f;
            float lumps = Fbm(nx + salt * .37f, nz, salt, 3);
            float crumbs = Fbm(nx * 3.1f, nz * 3.1f, salt + 40, 2);
            float y = t > 1f ? -.1f * height
                : height * Bell(t) * (1f + noise * lumps) + height * .045f * crumbs * Mathf.Sqrt(Bell(t));
            heights.Add(t > 1f ? -1f : lumps * .6f + crumbs * .4f);
            return new Vector3(x, y, z);
        }
        PolarGrid(rings, sides, true, Point, vertices, triangles);
        var colors = new List<Color32>(vertices.Count);
        var uv = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            float edge = Mathf.Clamp01(Mathf.Max(Mathf.Abs(v.x) / halfW, Mathf.Abs(v.z) / (halfL * 1.18f)));
            float tone = Mathf.Lerp(.84f, 1f, Mathf.Clamp01(.5f + heights[i])) * Mathf.Lerp(1f, .86f, Mathf.SmoothStep(0f, 1f, (edge - .6f) / .4f));
            colors.Add(Gray(tone));
            uv.Add(new Vector2(v.x, v.z) * (meters / GroundTileMeters));
        }
        return SoilMesh(name, vertices, uv, colors, triangles);
    }

    /// <summary>
    /// Луч трещины 1 × 1 на земле (XZ, лицом вверх) от нуля вдоль +Z (V17, ход под землёй): трещины разбегаются от головы
    /// бугра во все стороны — частица ставит основание луча в точку у головы, поворот вокруг вертикали — наугад. UV по
    /// длине: v = 1 у основания (широкий конец веера Crack5 — у головы), остриё — на конце луча. Каменистая куча головы
    /// V15 (RubbleMoundMesh: рельеф RubbleSurface, запечённые плиты и комья) снята — владелец 08.10: «как будто холмик
    /// просто скользит по полу».
    /// </summary>
    private static Mesh FlatRay(string name)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(new List<Vector3> { new Vector3(-.5f, 0f, 0f), new Vector3(-.5f, 0f, 1f), new Vector3(.5f, 0f, 1f), new Vector3(.5f, 0f, 0f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f) });
        mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        var white = new Color32(255, 255, 255, 255);
        mesh.SetColors(new List<Color32> { white, white, white, white });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Корень, загнутый к +Z (код Корнехвата): основание под землёй (−0,14 м), растёт по +Y,
    /// угол от вертикали curl·s^1,7; сечение с долями и скруткой, пята, узлы, колючки.
    /// </summary>
    private static Mesh CurlRoot(string name, int salt, float length, float radius, float curl, float knots, int thorns, float twist)
    {
        const int rings = 18;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        var centers = new Vector3[rings + 1];
        var radii = new float[rings + 1];
        var p = new Vector3(0f, -.14f, 0f);
        centers[0] = p;
        for (int r = 1; r <= rings; r++)
        {
            float s = (r - .5f) / rings;
            float phi = curl * Mathf.Pow(s, 1.7f);
            float wobble = .18f * Mathf.Sin(s * 5f + phase);
            p += new Vector3(wobble, Mathf.Cos(phi), Mathf.Sin(phi)).normalized * (length / rings);
            centers[r] = p;
        }
        for (int r = 0; r <= rings; r++)
        {
            float s = r / (float)rings;
            radii[r] = radius * Mathf.Pow(1f - s, .75f)
                * (1f + .5f * Mathf.Pow(Mathf.Max(0f, 1f - s / .16f), 2f))
                * (1f + knots * (Mathf.Sin(s * 13f + phase) + .5f * Mathf.Sin(s * 29f + 2f * phase)) * (1f - s)) + .004f;
        }
        return Tube(name, salt, centers, radii, 9, twist, thorns, length * .9f);
    }

    /// <summary>Труба вдоль хребта (код Корнехвата): рамки переносом, сечение с долями, колючки; кора u — вокруг, v — вдоль.</summary>
    private static Mesh Tube(string name, int salt, Vector3[] centers, float[] radii, int sides, float twist, int thorns, float vLength)
    {
        int rings = centers.Length - 1;
        float phase = Hash01(salt, 11) * Mathf.PI * 2f;
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        var tangents = new Vector3[rings + 1];
        var normals = new Vector3[rings + 1];
        var binormals = new Vector3[rings + 1];
        Vector3 carried = Vector3.zero;
        for (int r = 0; r <= rings; r++)
        {
            Vector3 tangent = (centers[Mathf.Min(rings, r + 1)] - centers[Mathf.Max(0, r - 1)]).normalized;
            Vector3 normal = r == 0 ? Vector3.Cross(tangent, Vector3.forward) : carried - tangent * Vector3.Dot(carried, tangent);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right);
            normal.Normalize();
            carried = normal;
            tangents[r] = tangent; normals[r] = normal; binormals[r] = Vector3.Cross(normal, tangent);
        }
        var white = new Color32(255, 255, 255, 255);
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .24f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(centers[r] + (normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a)) * radii[r] * lobe);
                uv.Add(new Vector2(s / (float)sides, t * vLength));
                colors.Add(white);
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        for (int k = 0; k < thorns; k++)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(.22f, .7f, (k + Hash01(salt, 20 + k)) / thorns) * rings), 1, rings - 1);
            float a = Hash01(salt, 30 + k) * Mathf.PI * 2f;
            Vector3 outward = normals[r] * Mathf.Cos(a) + binormals[r] * Mathf.Sin(a);
            Vector3 axis = (outward * .8f + tangents[r] * .6f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangents[r]).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = centers[r] + outward * radii[r] * .7f;
            float baseRadius = radii[r] * .4f, length = Mathf.Max(.06f, radii[r] * (1.1f + .6f * Hash01(salt, 40 + k)));
            int start = vertices.Count;
            float v0 = r / (float)rings * vLength;
            for (int s = 0; s <= 5; s++)
            {
                float b = s / 5f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s * .06f, v0)); colors.Add(white);
                vertices.Add(root + axis * length); uv.Add(new Vector2(s * .06f, v0 + .1f)); colors.Add(white);
                if (s < 5) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        var meshNormals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            meshNormals[a] = meshNormals[b] = (meshNormals[a] + meshNormals[b]).normalized;
        }
        mesh.normals = meshNormals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Корень-шип (код Вендиго): длина 1 по −Z, основание в нуле — меш-частица с
    /// выравниванием по направлению смотрит −Z вдоль нормали точки. Кора: v = 0 у земли.
    /// </summary>
    private static Mesh RootSpike(string name, int salt, float radius, float bend, float twist, float knots, int thorns)
    {
        const int rings = 12, sides = 8;
        float phase = Hash01(salt, 7) * Mathf.PI * 2f;
        System.Func<float, Vector3> spine = t => new Vector3(bend * t * t + .045f * Mathf.Sin(t * 6f + phase), t,
            bend * .3f * Mathf.Sin(t * 3.1f + phase));
        System.Func<float, float> thickness = t => radius * Mathf.Pow(1f - t, .85f)
            * (1f + .35f * Mathf.Pow(Mathf.Max(0f, 1f - t / .2f), 2f))
            * (1f + knots * (Mathf.Sin(t * 11f + phase) + .5f * Mathf.Sin(t * 27f + 2f * phase)) * (1f - t));
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            RingFrame(spine, t, out Vector3 center, out _, out Vector3 side, out Vector3 other);
            float rad = thickness(t);
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f + twist * t;
                float lobe = 1f + .24f * Mathf.Sin(2f * a + phase) + .10f * Mathf.Sin(3f * a + 2f * phase) + .06f * Mathf.Sin(5f * a + phase);
                vertices.Add(center + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * rad * lobe);
                uv.Add(new Vector2(s / (float)sides, t));
                if (r == rings || s == sides) continue;
                int v = r * (sides + 1) + s, up = v + sides + 1;
                triangles.AddRange(new[] { v, up, v + 1, v + 1, up, up + 1 });
            }
        }
        for (int k = 0; k < thorns; k++)
        {
            float t = Mathf.Lerp(.26f, .62f, (k + Hash01(salt, 20 + k)) / thorns);
            RingFrame(spine, t, out Vector3 center, out Vector3 tangent, out Vector3 side, out Vector3 other);
            float a = Hash01(salt, 30 + k) * Mathf.PI * 2f;
            Vector3 outward = side * Mathf.Cos(a) + other * Mathf.Sin(a);
            Vector3 axis = (outward * .8f + tangent * .6f).normalized;
            Vector3 u1 = Vector3.Cross(axis, tangent).normalized, u2 = Vector3.Cross(u1, axis);
            Vector3 root = center + outward * thickness(t) * .7f;
            float baseRadius = thickness(t) * .45f, length = .16f + .1f * Hash01(salt, 40 + k);
            int start = vertices.Count;
            for (int s = 0; s <= 5; s++)
            {
                float b = s / 5f * Mathf.PI * 2f;
                vertices.Add(root + (u1 * Mathf.Cos(b) + u2 * Mathf.Sin(b)) * baseRadius); uv.Add(new Vector2(s * .06f, t));
                vertices.Add(root + axis * length); uv.Add(new Vector2(s * .06f, t + .1f));
                if (s < 5) triangles.AddRange(new[] { start + s * 2, start + s * 2 + 1, start + s * 2 + 2 });
            }
        }
        var upright = Quaternion.FromToRotation(Vector3.up, Vector3.back);
        var colors = new List<Color32>();
        for (int i = 0; i < vertices.Count; i++) { vertices[i] = upright * vertices[i]; colors.Add(new Color32(255, 255, 255, 255)); }
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        var normals = mesh.normals;
        for (int r = 0; r <= rings; r++)
        {
            int a = r * (sides + 1), b = a + sides;
            normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
        }
        mesh.normals = normals;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void RingFrame(System.Func<float, Vector3> spine, float t, out Vector3 center, out Vector3 tangent,
        out Vector3 side, out Vector3 other)
    {
        center = spine(t);
        tangent = (spine(Mathf.Min(1f, t + .01f)) - spine(Mathf.Max(0f, t - .01f))).normalized;
        side = Vector3.Cross(tangent, Vector3.forward).normalized;
        other = Vector3.Cross(side, tangent);
    }

    /// <summary>Точки эмиссии: вершины — места, нормали — направления, треугольники вырожденные.</summary>
    private static Mesh Points(string name, List<Vector3> positions, List<Vector3> directions)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(positions);
        mesh.SetNormals(directions);
        var colors = new List<Color32>();
        for (int i = 0; i < positions.Count; i++) colors.Add(new Color32(255, 255, 255, 255));
        mesh.SetColors(colors);
        var triangles = new int[((positions.Count + 2) / 3) * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = Mathf.Min(i, positions.Count - 1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>count мест по кольцу [inner, outer], направление — вверх с наклоном наружу tiltMin…tiltMax.</summary>
    private static Mesh RingPoints(string name, int count, float inner, float outer, float tiltMin, float tiltMax, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float radius = Mathf.Lerp(inner, outer, Hash01(i, salt + 1));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Шипы прорастания: центр + count−1 мест на r·0,3–0,9, наклон наружу 10–35°.</summary>
    private static Mesh SpikePoints(string name, int count, float radius, int salt)
    {
        var positions = new List<Vector3> { Vector3.down * .1f };
        var directions = new List<Vector3> { Vector3.up };
        for (int i = 1; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .6f) / (count - 1) * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float tilt = Mathf.Lerp(10f, 35f, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * radius * Mathf.Lerp(.3f, .9f, Hash01(i, salt + 1)) + Vector3.down * .1f);
            directions.Add((Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized);
        }
        return Points(name, positions, directions);
    }

    // ------------------------------------------------------------- hill of death geometry (V13: пригорок, по которому ходят)

    // Рельеф пригорка — ThicketMasterDeathRules (KnollTop, KnollGround, KnollSurface, KnollRim): тот же счёт у вида,
    // который ставит на пригорок тела (ThicketMasterCombatView.KnollFloor, рост — HillRiseHeight / HillRiseWidth), —
    // сетки и пол тел совпадают.
    private const float HillW = ThicketMasterDeathRules.HillHalfWidth, HillL = ThicketMasterDeathRules.HillHalfLength;

    /// <summary>
    /// Основание и мох собраны на столько метров выше и стоят на столько же ниже корня: рост по высоте идёт
    /// из-под земли (край выходит к нулю только в полный рост), без мерцания плоского диска в полу.
    /// </summary>
    private const float HillSink = ThicketMasterDeathRules.HillSink;

    private static float Smooth01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    /// <summary>Неровный край холма: множитель радиуса по углу (ThicketMasterDeathRules.KnollRim).</summary>
    private static float HillRim(float angle) => ThicketMasterDeathRules.KnollRim(angle);

    /// <summary>Доля радиуса холма в точке: 0 — середина, 1 — неровный край (ThicketMasterDeathRules.KnollDistance).</summary>
    private static float HillDistance(float x, float z) => ThicketMasterDeathRules.KnollDistance(x, z);

    /// <summary>Верх пригорка T (мох), м: вершина ровно HillHeight 1,3 м, склон не круче ~34° (ThicketMasterDeathRules.KnollTop).</summary>
    private static float KnollTop(float x, float z) => ThicketMasterDeathRules.KnollTop(x, z);

    /// <summary>Основание (земля) в точке, м: не выше HillBaseHeight; вне края — 0.</summary>
    private static float HillGround(float x, float z) => ThicketMasterDeathRules.KnollGround(x, z);

    /// <summary>
    /// Верх холма, м: основание и мох над ним — ровно то, что рисуют сетки (на мху — KnollTop, в кольце земли —
    /// основание); цветы, листья, трава и ростки встают на него, тела ходят по нему (ThicketMasterDeathRules.KnollSurface).
    /// </summary>
    private static float HillTop(float x, float z) => ThicketMasterDeathRules.KnollSurface(x, z);

    /// <summary>
    /// Рваные пятна 0…1 на склонах (V11: «земля видна несколькими рваными пятнами»): мох там цветом вершин уходит в
    /// сырую бурую дернину, сверху лежат комья свежей земли (система «Torn»). Пояс склона — 0,36–1,01 м верха
    /// (V13: те же доли высоты, что 0,5–1,4 м у горы 1,8 м).
    /// </summary>
    private static float KnollTorn(float x, float z, float t)
    {
        // Две октавы шума: пятна мелкие (0,1–0,6 м) и рваные, а не квадратные кляксы решётки.
        float n = .65f * Noise2(x * 1.6f + 11f, z * 1.6f - 4f, 811) + .35f * Noise2(x * 3.9f - 2f, z * 3.9f + 7f, 812);
        return Smooth01((n - .66f) / .06f) * Smooth01((t - .36f) / .15f) * Smooth01((1.01f - t) / .15f);
    }

    /// <summary>Точка основания холма на доле радиуса d по углу angle (0 — вдоль взгляда тела), высота — по основанию.</summary>
    private static Vector3 HillPoint(float angle, float d, float lift)
    {
        float k = d * HillRim(angle);
        float x = Mathf.Sin(angle) * HillW * k, z = Mathf.Cos(angle) * HillL * k;
        return new Vector3(x, HillGround(x, z) + lift, z);
    }

    /// <summary>Крутизна верха холма в точке (м на м).</summary>
    private static float HillSlope(float x, float z)
    {
        const float e = .12f;
        float gx = (HillTop(x + e, z) - HillTop(x - e, z)) / (2f * e), gz = (HillTop(x, z + e) - HillTop(x, z - e)) / (2f * e);
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    /// <summary>
    /// Сетки пригорка — полярные от середины до края: основание (кольца × углы, юбка под землю) и мох. V13: пригорок
    /// 6,2 × 7,8 м — шаг ~0,16 м по радиусу у основания, ~0,13 м у мха (ячейки V11 были ~0,14).
    /// </summary>
    private const int HillRings = 24, HillSides = 96, MossRings = 30, MossSides = 128;

    /// <summary>
    /// Основание холма — земля (видна кольцом у подножия и под рваными пятнами): полярная сетка HillRings × HillSides
    /// по основанию, сглаженные нормали, юбка под землю (−0,12 м) — без щели у пола; фактура земли пола в метрах
    /// холма, тон — свежая сырая земля (HillSoil).
    /// </summary>
    private static Mesh HillMesh(string name)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        PolarGrid(HillRings, HillSides, true, (t, angle) =>
        {
            float k = t * HillRim(angle);
            float x = Mathf.Sin(angle) * HillW * k, z = Mathf.Cos(angle) * HillL * k;
            // Опущен на HillSink: частица стоит на −HillSink, и купол, сжатый по высоте в начале роста, — под землёй.
            return new Vector3(x, (t > 1f ? -.12f : HillGround(x, z)) + HillSink, z);
        }, vertices, triangles);
        return HillSoil(name, vertices, triangles, false);
    }

    /// <summary>
    /// Мох пригорка — одна сетка на весь холм (V13, владелец 03.10: «если по нему пройтись, оч коряво выглядит» —
    /// 19 ячеек V11 оседали у героя ямой, кромки ячеек выскакивали): полярная сетка MossRings × MossSides до края,
    /// высота — основание + ThicketMasterDeathRules.KnollMossLift (на мху — ровно верх T, где мха нет — под основанием
    /// на KnollMossTuck), частица стоит в середине холма на −HillSink, как основание: растут одним телом. Нормали —
    /// по видимому верху (HillSoil).
    /// </summary>
    private static Mesh MossMesh(string name)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        PolarGrid(MossRings, MossSides, false, (t, angle) =>
        {
            float k = t * HillRim(angle);
            float x = Mathf.Sin(angle) * HillW * k, z = Mathf.Cos(angle) * HillL * k;
            float top = KnollTop(x, z);
            return new Vector3(x, ThicketMasterDeathRules.KnollBase(top) + ThicketMasterDeathRules.KnollMossLift(x, z, top) + HillSink, z);
        }, vertices, triangles);
        return HillSoil(name, vertices, triangles, true);
    }

    /// <summary>
    /// Места на верху холма (цветы, трава, ростки, комья): меш точек. V13: мох не оседает — буграм под местами
    /// (UV-каналы V10–V12) больше нечего помнить.
    /// </summary>
    private static Mesh HillPlacePoints(string name, List<Vector3> places, List<Vector3> directions = null)
        // Направления (нормали точек) — для частиц с выравниванием по направлению места (листья, цветы — по склону).
        => directions != null ? Points(name, places, directions) : PlacePoints(name, places);

    /// <summary>
    /// Пригорок. Основание (moss = false) — земля: UV — фактура земли пола в метрах холма; тон вершин — свежая сырая
    /// земля пятнами 0,84–1, у подножия и под кромкой мха темнее (сырая, в тени дернины); нормали — по треугольникам.
    /// Мох — UV дёрна пола (TurfTileMeters), нормали — разностями видимого верха HillTop, тон: пятна 0,86–1, внизу
    /// склонов темнее, у кромки мха темнее (край дернины), рваные пятна — бурая сырая дернина (KnollTorn). Тон
    /// зависит только от места в холме. Цвет частицы умножает.
    /// </summary>
    private static Mesh HillSoil(string name, List<Vector3> vertices, List<int> triangles, bool moss)
    {
        var uv = new List<Vector2>(vertices.Count);
        var colors = new List<Color32>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            var p = vertices[i];
            float t = KnollTop(p.x, p.z);
            float mottle = .6f * Noise2(p.x * 2.1f, p.z * 2.1f, 781) + .4f * Noise2(p.x * 5.3f, p.z * 5.3f, 782);
            Color c;
            if (!moss)
            {
                uv.Add(new Vector2(p.x, p.z) / GroundTileMeters);
                float tone = Mathf.Lerp(.84f, 1f, mottle);
                // У самого подножия — сырая, под кромкой мха (верх ~0,1 м, V13) — в тени дернины.
                tone *= Mathf.Lerp(1f, .88f, Smooth01((HillDistance(p.x, p.z) - .85f) / .15f));
                tone *= Mathf.Lerp(1f, .9f, Smooth01((t - .04f) / .06f));
                c = new Color(tone, tone, tone);
            }
            else
            {
                uv.Add(new Vector2(p.x, p.z) / TurfTileMeters);
                // Внизу склонов темнее — до 0,8 м верха (V13: та же доля высоты, что 1,1 м у горы 1,8 м).
                float tone = Mathf.Lerp(.86f, 1f, mottle) * Mathf.Lerp(.88f, 1f, Smooth01(t / .8f));
                tone *= Mathf.Lerp(.85f, 1f, ThicketMasterDeathRules.KnollMoss(p.x, p.z, t));
                c = new Color(tone, tone, tone);
                // Рваная дернина: на зелёном дёрне бурая (красный больше зелёного), темнее.
                c = Color.Lerp(c, new Color(.92f * tone, .6f * tone, .7f * tone), KnollTorn(p.x, p.z, t) * .9f);
            }
            colors.Add(new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(c.r)), (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(c.g)),
                (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(c.b)), 255));
        }
        var mesh = SoilMesh(name, vertices, uv, colors, triangles);
        if (moss)
        {
            // Нормали мха — по ВИДИМОМУ верху (HillTop: основание и мох над ним), а не по треугольникам: у кромки мха
            // сетка уходит под основание круто, и этот сход тянулся бы в свет тёмным ободком.
            const float e = .04f;
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                var p = vertices[i];
                float tx = (HillTop(p.x + e, p.z) - HillTop(p.x - e, p.z)) / (2f * e), tz = (HillTop(p.x, p.z + e) - HillTop(p.x, p.z - e)) / (2f * e);
                normals[i] = new Vector3(-tx, 1f, -tz).normalized;
            }
            mesh.normals = normals;
        }
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Сетки ячеек мха V11–V12 (ThicketDeathMound0…18): с V13 мох — одна сетка, после сборки их нет.</summary>
    private const string StaleKnollCellMesh = "ThicketDeathMound";

    /// <summary>
    /// Снять сетки ячеек мха V11–V12 из Geometry (они в Resources — уехали бы в сборку игры мёртвым грузом). Только
    /// после удачной сборки: новый префаб холма на них уже не ссылается.
    /// </summary>
    private static void DeleteStaleKnollCells()
    {
        for (int i = 0; i < 32; i++)
        {
            string path = GeometryFolder + "/" + StaleKnollCellMesh + i + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null && AssetDatabase.DeleteAsset(path)) Debug.Log(Log + "Снята сетка ячейки мха V12: " + path);
        }
    }

    /// <summary>
    /// V14: свои сетки V9–V13, на которые новые префабы нырка и выхода не ссылаются (вал LipMesh, места клубов, комьев и
    /// зерна по контуру) — сняты из Resources, чтобы не уехать в сборку мёртвым грузом. Ассеты паков не трогаются.
    /// </summary>
    private static void DeleteStaleDiveEarth()
    {
        foreach (string name in new[]
                 {
                     "ThicketDiveLip", "ThicketEmergeLip", "ThicketDiveWallPoints", "ThicketDiveChunkPoints", "ThicketDiveGrainPoints",
                     "ThicketEmergeWallPoints", "ThicketEmergeChunkPoints", "ThicketEmergeGrainPoints", "ThicketEmergeSettlePoints",
                 })
        {
            string path = GeometryFolder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null && AssetDatabase.DeleteAsset(path)) Debug.Log(Log + "Снята сетка V13: " + path);
        }
    }

    /// <summary>
    /// Голова бугра прежних сборок — новый префаб бугра на неё не ссылается; снята из Resources: гладкий вал V9–V14
    /// (ThicketRidgeHead) и V15 (V17, владелец 08.10: «как будто холмик просто скользит по полу») — каменистая куча
    /// ThicketMoundRubble, места её частиц ThicketMound*, квадрат шва ThicketCrackQuad и материал пятна под кучей.
    /// </summary>
    private static void DeleteStaleMoundHead()
    {
        foreach (string name in new[]
                 {
                     "ThicketRidgeHead", "ThicketMoundRubble", "ThicketCrackQuad", "ThicketMoundHeadPoints", "ThicketMoundChurnL",
                     "ThicketMoundChurnR", "ThicketMoundGrainSpots", "ThicketMoundSpraySpots", "ThicketMoundDustSpots",
                     "ThicketMoundSlabSpotsL", "ThicketMoundSlabSpotsR", "ThicketMoundRockSpots", "ThicketMoundBigClodSpots",
                     "ThicketMoundFlankPoints", "ThicketMoundTrailGrainSpots", "ThicketMoundGrassPoints", "ThicketMoundPatchSpots",
                 })
        {
            string path = GeometryFolder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null && AssetDatabase.DeleteAsset(path)) Debug.Log(Log + "Снята сетка прежней головы бугра: " + path);
        }
        string contact = MaterialFolder + "/M_Thicket_MoundContact.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(contact) != null && AssetDatabase.DeleteAsset(contact))
            Debug.Log(Log + "Снят материал пятна под кучей V15: " + contact);
    }

    /// <summary>
    /// count мест на ВЕРХУ холма (основание и мох, HillTop) от середины к краю (d от dMin до dMax, шаг не
    /// ближе spacing м, крутизна не больше maxSlope): порядок — от вершины наружу с разбросом, цветение идёт по
    /// нему волной; высота — верх минус sink.
    /// </summary>
    private static List<Vector3> HillPlaces(int count, float dMin, float dMax, float spacing, float sink, int salt, float maxSlope = 99f)
    {
        var places = new List<Vector3>();
        var order = new List<float>();
        for (int tries = 0; places.Count < count && tries < count * 60; tries++)
        {
            float a = Hash01(tries, salt) * Mathf.PI * 2f;
            float d = Mathf.Lerp(dMin, dMax, Mathf.Sqrt(Hash01(tries, salt + 1)));
            float k = d * HillRim(a);
            float x = Mathf.Sin(a) * HillW * k, z = Mathf.Cos(a) * HillL * k;
            if (HillSlope(x, z) > maxSlope) continue;
            var p = new Vector3(x, HillTop(x, z) - sink, z);
            bool free = true;
            for (int i = 0; i < places.Count && free; i++)
            {
                Vector3 q = places[i] - p; q.y = 0f;
                free = q.sqrMagnitude >= spacing * spacing;
            }
            if (!free) continue;
            places.Add(p);
            order.Add(d + .25f * Hash01(tries, salt + 2));
        }
        var sorted = Enumerable.Range(0, places.Count).OrderBy(i => order[i]).Select(i => places[i]).ToList();
        return sorted;
    }

    /// <summary>
    /// Нормаль верха холма под пятном радиуса reach, м (наклон секущей через ±reach — плоскость, на которую ляжет
    /// розетка или цветок такого размера, а не касательная в одной точке).
    /// </summary>
    private static Vector3 HillNormal(float x, float z, float reach)
    {
        float gx = (HillTop(x + reach, z) - HillTop(x - reach, z)) / (2f * reach);
        float gz = (HillTop(x, z + reach) - HillTop(x, z - reach)) / (2f * reach);
        return new Vector3(-gx, 1f, -gz).normalized;
    }

    /// <summary>
    /// Направление частицы на склоне: вертикаль, наклонённая к нормали холма n на долю lean (1 — лечь по склону), и
    /// разброс до jitter° в сторону по зерну (i, salt) — вместо случайного наклона startRotation (у выравнивания по
    /// направлению случайным остаётся только разворот вокруг оси).
    /// </summary>
    private static Vector3 SlopeDirection(Vector3 n, float lean, float jitter, int i, int salt)
    {
        var dir = Vector3.Slerp(Vector3.up, n, lean);
        float phi = Hash01(i, salt) * Mathf.PI * 2f, tilt = Hash01(i, salt + 1) * jitter;
        var axis = Vector3.Cross(dir, new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi)));
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
        return (Quaternion.AngleAxis(tilt, axis.normalized) * dir).normalized;
    }

    /// <summary>Меш мест эмиссии из списка (направление — вверх).</summary>
    private static Mesh PlacePoints(string name, List<Vector3> places)
    {
        var ups = new List<Vector3>();
        for (int i = 0; i < places.Count; i++) ups.Add(Vector3.up);
        return Points(name, places, ups);
    }

    // ------------------------------------------------------------- knoll flora (V11)

    /// <summary>
    /// Цветок пригорка: копия Hovl Flower.fbx с нормалями к раскрытию чашки — тот же код, что у цветов куста Ф3
    /// (ThicketMasterDressingSetup.UprightFlowerMesh: «нормалей развёрнуто к чашке 118 из 144»), свой ассет в Geometry
    /// эффектов. Ось чашки — по рамке, как у куста (+Y у Flower.fbx на 02.10); в копии она повёрнута на −Z: цветок
    /// ставится частицей с выравниванием по направлению места (наклон к склону, HillFlowers), как розетка листьев.
    /// Поворот — после общего кода: цветы куста Ф3 не меняются.
    /// </summary>
    private static Mesh KnollFlowerMesh(Mesh source)
    {
        if (source == null) return null;
        var b = source.bounds;
        bool zUp = Mathf.Abs(b.min.y) > .2f * b.size.y && Mathf.Abs(b.min.z) < .1f * b.size.z;
        bool zDown = Mathf.Abs(b.min.y) > .2f * b.size.y && Mathf.Abs(b.max.z) < .1f * b.size.z;
        if (zUp || zDown) Note("Flower.fbx: ось чашки ±Z (ждали +Y) — копия цветка пригорка повёрнута по найденной оси");
        var axis = zUp ? Vector3.forward : zDown ? Vector3.back : Vector3.up;
        var mesh = ThicketMasterDressingSetup.UprightFlowerMesh(source, axis, GeometryFolder + "/ThicketKnollFlower.asset", "ThicketKnollFlower", out int flipped);
        Debug.Log(Log + $"Цветок пригорка: нормалей развёрнуто к чашке {flipped} из {source.vertexCount}.");
        var turn = Quaternion.FromToRotation(axis, Vector3.back);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = turn * vertices[i];
        for (int i = 0; i < normals.Length; i++) normals[i] = turn * normals[i];
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Сетка зелени: вершины, нормали (наклонены вверх — свет сверху, как у травы пола), тон Color32, UV в середине.</summary>
    private static Mesh FloraMesh(string name, List<Vector3> vertices, List<Vector3> normals, List<Color32> colors, List<int> triangles)
    {
        Mesh mesh = PelagWhirlwindVfxSetup.LoadOrCreateMesh(GeometryFolder + "/" + name + ".asset", name);
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        var uv = new List<Vector2>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++) uv.Add(new Vector2(.5f, .5f));
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>
    /// Пучок травы (высота 1 — размер частицы в метрах): 7 сужающихся травинок от общего корня, наклон наружу 8–35°,
    /// изгиб; у корня темнее (0,55), к кончику светлее. Нормали — к свету сверху (геометрия + вверх 0,8): трава
    /// светится как трава пола, а не тёмной изнанкой. Двусторонняя (материал без отсечения граней).
    /// </summary>
    private static Mesh TuftMesh(string name, int salt)
    {
        const int blades = 7, rows = 5;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        for (int b = 0; b < blades; b++)
        {
            float yaw = (b + Hash01(b, salt) * .7f) / blades * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var side = new Vector3(outward.z, 0f, -outward.x);
            float lean = Mathf.Lerp(8f, 35f, Hash01(b, salt + 1)) * Mathf.Deg2Rad, bend = Mathf.Lerp(.15f, .5f, Hash01(b, salt + 2));
            float height = Mathf.Lerp(.65f, 1f, Hash01(b, salt + 3)), width = Mathf.Lerp(.05f, .08f, Hash01(b, salt + 4));
            var root = outward * Mathf.Lerp(.02f, .1f, Hash01(b, salt + 5));
            int at = vertices.Count;
            for (int r = 0; r < rows; r++)
            {
                float s = r / (rows - 1f);
                float tilt = lean + bend * s * s;
                var spine = root + (outward * Mathf.Sin(tilt) * s + Vector3.up * Mathf.Cos(tilt) * s) * height;
                float half = width * (1f - s * .92f) * .5f;
                var face = Vector3.Cross(side, (outward * Mathf.Cos(tilt) - Vector3.up * Mathf.Sin(tilt)) * -1f).normalized;
                var normal = (face + Vector3.up * .8f).normalized;
                byte tone = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(.55f, 1f, s));
                vertices.Add(spine - side * half); vertices.Add(spine + side * half);
                normals.Add(normal); normals.Add(normal);
                colors.Add(new Color32(tone, tone, tone, 255)); colors.Add(new Color32(tone, tone, tone, 255));
                if (r == 0) continue;
                int a = at + (r - 1) * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
        }
        return FloraMesh(name, vertices, normals, colors, triangles);
    }

    /// <summary>
    /// Розетка листьев под кучкой цветов (поперечник ~2 — размер частицы ×2 в метрах): 6 широких листьев от середины,
    /// подняты на 20–40°, кончик чуть вниз, жилка выше краёв (лодочкой); у середины и по краям темнее, к кончику и по
    /// жилке светлее. Нормали — вверх с наклоном по листу. Верх розетки — −Z (ToAlignedUp): частица ложится по
    /// склону выравниванием по направлению места (shape.alignToDirection ставит −Z меша вдоль нормали места).
    /// </summary>
    private static Mesh LeafRosetteMesh(string name, int salt)
    {
        const int leaves = 6, rows = 5;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        for (int l = 0; l < leaves; l++)
        {
            float yaw = (l + Hash01(l, salt) * .5f) / leaves * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var side = new Vector3(outward.z, 0f, -outward.x);
            float rise = Mathf.Lerp(20f, 40f, Hash01(l, salt + 1)) * Mathf.Deg2Rad, length = Mathf.Lerp(.75f, 1f, Hash01(l, salt + 2));
            float width = Mathf.Lerp(.3f, .4f, Hash01(l, salt + 3));
            int at = vertices.Count;
            for (int r = 0; r < rows; r++)
            {
                float s = r / (rows - 1f);
                float tilt = rise * (1f - .9f * s);
                var spine = (outward * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)) * s * length;
                spine.y += .04f;
                float half = width * Mathf.Sin(Mathf.PI * Mathf.Lerp(.08f, 1f, s)) * .5f;
                var along = outward * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt);
                var up = Vector3.Cross(along, side).normalized;
                if (up.y < 0f) up = -up;
                for (int c = -1; c <= 1; c++)
                {
                    var p = spine + side * (c * half) - up * (c != 0 ? .035f * half / Mathf.Max(.01f, width * .5f) : 0f);
                    vertices.Add(p);
                    normals.Add((up + Vector3.up * .5f + side * (c * .25f)).normalized);
                    float tone = Mathf.Lerp(.6f, 1f, s) * (c == 0 ? 1f : .85f);
                    byte b = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(tone));
                    colors.Add(new Color32(b, b, b, 255));
                }
                if (r == 0) continue;
                int a = at + (r - 1) * 3;
                triangles.AddRange(new[] { a, a + 3, a + 1, a + 1, a + 3, a + 4, a + 1, a + 4, a + 2, a + 2, a + 4, a + 5 });
            }
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = ToAlignedUp(vertices[i]);
            normals[i] = ToAlignedUp(normals[i]);
        }
        return FloraMesh(name, vertices, normals, colors, triangles);
    }

    /// <summary>
    /// Поворот «верх +Y → −Z» (−90° вокруг X): меш-частица с shape.alignToDirection смотрит своей −Z вдоль нормали
    /// места эмиссии (ловушка частиц Unity, Вендиго V11), а не +Y.
    /// </summary>
    private static Vector3 ToAlignedUp(Vector3 v) => new Vector3(v.x, v.z, -v.y);

    /// <summary>Детерминированный разброс: сборка даёт одинаковые ассеты на любой машине.</summary>
    private static float Hash01(int i, int salt)
    {
        uint h = (uint)(i * 747796405 + salt * 2891336453u);
        h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    // ------------------------------------------------------------- particles

    /// <summary>Кривая по парам (время, значение), отрезки линейные.</summary>
    private static AnimationCurve Curve(params float[] pairs)
    {
        var keys = new Keyframe[pairs.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    /// <summary>Прозрачность по жизни: появление к fadeIn, уход с fadeOut (доли жизни).</summary>
    private static Gradient Alpha(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(.001f, fadeIn)),
                new GradientAlphaKey(1f, Mathf.Max(fadeIn + .001f, fadeOut)), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Устойчивое зерно по имени: String.GetHashCode меняется от запуска к запуску.</summary>
    private static uint Seed(string name)
    {
        uint hash = 2166136261u;
        foreach (char c in name) hash = (hash ^ c) * 16777619u;
        return hash & 0x7FFFFFFF;
    }

    private static Quaternion Aim(Vector3 direction) => Quaternion.FromToRotation(Vector3.forward, direction.normalized);

    private static GameObject Child(GameObject parent, string name, Vector3 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = local;
        return go;
    }

    /// <summary>Система с одним залпом, местное пространство, постоянное зерно: вид переигрывает её по возрасту.</summary>
    private static ParticleSystem Particles(GameObject parent, string name, int count, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float delay)
    {
        var particles = PelagWhirlwindVfxSetup.NewParticles(parent, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = Seed(parent.name + "/" + name);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startDelay = delay;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        return particles;
    }

    private static void World(ParticleSystem particles)
    {
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
    }

    /// <summary>Эмиссия потоком: rate частиц в секунду seconds секунд (вместо залпа).</summary>
    private static void Stream(ParticleSystem particles, float rate, float seconds, int max)
    {
        var main = particles.main;
        main.duration = Mathf.Max(.05f, seconds);
        main.maxParticles = Mathf.Max(1, max);
        var emission = particles.emission;
        emission.SetBursts(new ParticleSystem.Burst[0]);
        emission.rateOverTime = rate;
    }

    private static void Collide(ParticleSystem particles, Transform ground, float bounce)
    {
        var collision = particles.collision; collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, ground);
        collision.bounce = bounce;
        collision.dampen = .65f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = .45f;
        collision.minKillSpeed = 0f;
    }

    /// <summary>Атлас n×n: кадр наугад на частицу, без анимации.</summary>
    private static void Sheet(ParticleSystem particles, int tiles, float lastFrame)
    {
        var sheet = particles.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = tiles; sheet.numTilesY = tiles;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, lastFrame);
        sheet.cycleCount = 1;
    }

    /// <summary>Комья, камни, щепки: спрайты CFXR 3×3, баллистика, отскок от земли ground, тают в конце.</summary>
    private static ParticleSystem Debris(GameObject host, Transform ground, string name, Material material, int count, Vector3 at,
        Vector3 direction, float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity,
        float delay, Color light, Color dark)
    {
        var particles = Particles(host, name, count, 1.3f, 1.9f, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Collide(particles, ground, .25f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        Sheet(particles, 3, 8.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        return particles;
    }

    /// <summary>Листья: меш листа CFXR без освещения, кувыркаются, планируют и ложатся на землю ground.</summary>
    private static ParticleSystem Leaves(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at,
        Vector3 direction, float cone, float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, 1.8f, 2.4f, speedMin, speedMax, .16f, .26f, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = .45f;
        main.startColor = new ParticleSystem.MinMaxGradient(LeafLight, LeafDark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = .2f;
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.6f);
        drag.dampen = .12f;
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        if (ground != null) Collide(particles, ground, .05f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = kit.LeafMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = kit.LeafMesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Leaf;
        return particles;
    }

    /// <summary>Мягкое облако CFXR (пыль, пыльца): малая альфа, тормозит, растёт и тает. flat — лежит на земле.</summary>
    private static ParticleSystem Dust(GameObject host, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay, bool flat)
        => Cloud(host, kit, name, count, at, cone, radius, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, delay, flat,
            new Color(DustLight.r, DustLight.g, DustLight.b, alpha), new Color(DustDark.r, DustDark.g, DustDark.b, alpha));

    private static ParticleSystem Cloud(GameObject host, Kit kit, string name, int count, Vector3 at, float cone, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, float delay, bool flat, Color a, Color b)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        // Без мягких частиц земля срезала бы стоячее облако прямой линией — поднимаем.
        particles.transform.localPosition = flat ? at : at + Vector3.up * (.45f * sizeMax);
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = flat ? 0f : -.02f;
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.25f);
        drag.dampen = .22f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, .25f, .95f, 1f, 1.45f));
        var color = particles.colorOverLifetime; color.enabled = true;
        color.color = Alpha(.08f, .45f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.5f, .5f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.flip = new Vector3(.5f, .5f, 0f);
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Haze;
        renderer.sortingFudge = flat ? 1f : -1f;
        return particles;
    }

    /// <summary>
    /// Низкая пыль удара (ревью 02.10, вечер: бежевые «ватные» облака на нырке, выходе, топоте и смерти
    /// закрывали героя и босса на 1–2 с — «наклейки»): много мелких клубов встают в точках points (на высоте
    /// точки, ≤ 0,5 м; верх клуба ≤ ~1,1 м), разлетаются наружу по направлению точки и тормозят, живут
    /// ≤ 0,8 с, прозрачнее прежних, землистые (не бежевые). Объём дают комья рядом (Chunks/Debris), не вата.
    /// </summary>
    private static ParticleSystem LowPuffs(GameObject host, Kit kit, string name, int count, Mesh points, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay)
    {
        var puffs = Cloud(host, kit, name, count, Vector3.zero, 0f, .1f, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, delay, false,
            new Color(DustDark.r, DustDark.g, DustDark.b, alpha), new Color(SoilLight.r, SoilLight.g, SoilLight.b, alpha * .9f));
        FromPoints(puffs, points, false, .25f);
        puffs.transform.localPosition = Vector3.zero;
        Shuffle(puffs);
        var main = puffs.main; main.gravityModifier = 0f;
        var drag = puffs.limitVelocityOverLifetime; drag.limit = new ParticleSystem.MinMaxCurve(.6f); drag.dampen = .18f;
        var size = puffs.sizeOverLifetime; size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .3f, .95f, 1f, 1.25f));
        var fade = puffs.colorOverLifetime; fade.color = Alpha(.06f, .3f);
        return puffs;
    }

    /// <summary>Пятно на земле: горизонтальный билборд, быстро раскрывается, тает с fadeOut (доля жизни).</summary>
    private static ParticleSystem Decal(GameObject host, string name, Material material, float sizeMin, float sizeMax,
        float life, float fadeOut, Color color, float delay, float lift)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, sizeMin, sizeMax, delay);
        particles.transform.localPosition = Vector3.up * lift;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .04f, 1.04f, .08f, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, fadeOut);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 4f;
        return particles;
    }

    /// <summary>
    /// Кольцо волны: горизонтальный билборд маски Hovl одним каналом, растёт от from до
    /// 1 за grow доли жизни. size — поперечник частицы: билборд лёжа рисуется ≈ 1/√2 от
    /// startSize (проба 29.09), поэтому зовущие дают 2r·√2.
    /// </summary>
    private static ParticleSystem Wave(GameObject host, string name, Material material, float size, float life,
        float from, float grow, Color color, float delay)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, size, size, delay);
        particles.transform.localPosition = Vector3.up * .05f;
        var main = particles.main;
        main.startColor = color;
        main.startRotation = 0f;
        var shape = particles.shape; shape.enabled = false;
        var curve = particles.sizeOverLifetime; curve.enabled = true;
        curve.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, from, grow * .6f, Mathf.Lerp(from, 1f, .86f), grow, 1f, 1f, 1f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, .45f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = 3f;
        return particles;
    }

    /// <summary>Искры и пылинки: всплывают и гаснут (alpha-blend — героя не высветляют).</summary>
    private static ParticleSystem Motes(GameObject host, string name, Material material, int count, Vector3 at, float radius,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color color, float delay, bool ring)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = -.06f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, color.a * .9f),
            new Color(color.r * .85f, color.g * .9f, color.b * .7f, color.a * .7f));
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ring ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
        shape.angle = ring ? 0f : 20f;
        shape.radius = Mathf.Max(.01f, radius);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(.8f);
        drag.dampen = .15f;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .4f, .2f, 1f, 1f, .2f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.1f, .55f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -2f;
        return particles;
    }

    /// <summary>Вспышка удара: один мягкий светящийся билборд (kit.Glow), раскрывается и гаснет за life.</summary>
    private static ParticleSystem Flash(GameObject host, Kit kit, string name, Vector3 at, float size, float life, Color color,
        float delay, bool flat)
    {
        var particles = Particles(host, name, 1, life, life, 0f, 0f, size, size, delay);
        particles.transform.localPosition = at;
        var main = particles.main;
        main.startColor = color;
        var shape = particles.shape; shape.enabled = false;
        var grow = particles.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .45f, .25f, 1f, 1f, 1.15f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(0f, .25f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = kit.Glow;
        renderer.sortingFudge = -3f;
        return particles;
    }

    /// <summary>Угольки: светящиеся искры (kit.Glow) разлетаются конусом вверх, падают и гаснут — их видно на тёмной арене.</summary>
    private static ParticleSystem Embers(GameObject host, Kit kit, string name, int count, Vector3 at, float radius,
        float speedMin, float speedMax, float life, Color color, float delay)
    {
        var particles = Motes(host, name, kit.Glow, count, at, radius, speedMin, speedMax, .06f, .13f, life * .7f, life, color, delay, false);
        var main = particles.main; main.gravityModifier = .35f;
        var shape = particles.shape; shape.angle = 55f;
        var drag = particles.limitVelocityOverLifetime; drag.limit = new ParticleSystem.MinMaxCurve(3f); drag.dampen = .08f;
        return particles;
    }

    /// <summary>Разноцветие лепестков: 50 % белых, 35 % розовых, 15 % золотых — случайный цвет из ступенчатого градиента.</summary>
    private static ParticleSystem.MinMaxGradient PetalColors(float alpha)
    {
        var gradient = new Gradient { mode = GradientMode.Fixed };
        gradient.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .5f), new GradientColorKey(PetalPink, .85f), new GradientColorKey(PetalGold, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>Лепестки: атлас 2×2 одним каналом, кувыркаются в 3D, медленно падают кружась.</summary>
    private static ParticleSystem Petals(GameObject host, Kit kit, string name, int count, Vector3 at, float lifeMin, float lifeMax,
        float speedMin, float speedMax, float delay)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, .10f, .22f, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.gravityModifier = .08f;
        main.startColor = PetalColors(1f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
        spin.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var drag = particles.limitVelocityOverLifetime; drag.enabled = true;
        drag.limit = new ParticleSystem.MinMaxCurve(1.2f);
        drag.dampen = .1f;
        var noise = particles.noise; noise.enabled = true;
        noise.strength = .4f; noise.frequency = .6f; noise.scrollSpeed = .3f;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.05f, .8f);
        Sheet(particles, 2, 3.99f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Petal;
        return particles;
    }

    /// <summary>
    /// Стена волны (ревью 02.10: «плоско, по-наклеечному»): стоячие клубы встают на кольце from
    /// на высоте height и бегут наружу до to за travel с — радиальная скорость по кривой гаснет
    /// к нулю ровно на краю (путь = скорость·travel/2), клубы растут, поднимаются rise м/с и тают.
    /// Система лежит осью +Z вверх: кольцо Circle — в плоскости земли.
    /// </summary>
    private static ParticleSystem Wall(GameObject host, string name, Material material, int count, float height, float from, float to,
        float travel, float life, float sizeMin, float sizeMax, Color a, Color b, float rise, float delay)
    {
        var particles = Particles(host, name, count, life * .92f, life * 1.08f, 0f, 0f, sizeMin, sizeMax, delay);
        particles.transform.localPosition = Vector3.up * height;
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(.01f, from);
        shape.radiusThickness = 0f;
        shape.arc = 360f;
        float tau = Mathf.Clamp(travel / Mathf.Max(.05f, life), .05f, .9f);
        float speed = 2f * Mathf.Max(0f, to - from) / Mathf.Max(.05f, travel);
        var push = particles.velocityOverLifetime; push.enabled = true;
        push.space = ParticleSystemSimulationSpace.Local;
        push.radial = new ParticleSystem.MinMaxCurve(speed, Curve(0f, 1f, tau, 0f, 1f, 0f));
        push.z = new ParticleSystem.MinMaxCurve(rise);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .55f, tau, 1f, 1f, 1.3f));
        var fade = particles.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(.05f, Mathf.Clamp(tau + .1f, .3f, .8f));
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-.8f, .8f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.flip = new Vector3(.5f, .5f, 0f);
        renderer.sharedMaterial = material;
        renderer.sortingFudge = -1f;
        return particles;
    }

    /// <summary>Штрихи скорости волны: та же стена, но вытянутые по скорости частицы (Stretch) — порыв наружу.</summary>
    private static ParticleSystem Streaks(GameObject host, string name, Material material, int count, float height, float from, float to,
        float travel, float life, float sizeMin, float sizeMax, Color color, float delay)
    {
        var particles = Wall(host, name, material, count, height, from, to, travel, life, sizeMin, sizeMax, color,
            new Color(color.r * .9f, color.g * .9f, color.b * .85f, color.a * .8f), 0f, delay);
        var size = particles.sizeOverLifetime;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, .6f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.2f;
        renderer.velocityScale = .09f;
        renderer.sortingFudge = -2f;
        return particles;
    }

    /// <summary>
    /// Комья и камни объёмом: меш-частицы сколотых многогранников (kit.Chunks) на непрозрачной
    /// земле kit.Earth (свет URP, тени на них), кувыркаются в 3D, баллистика, отскок от ground, к концу уходят.
    /// Нет мешей — плоские спрайты Debris.
    /// </summary>
    private static ParticleSystem Chunks(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float delay, Color light, Color dark)
    {
        if (kit.Chunks == null || kit.Chunks.Length == 0 || kit.Chunks[0] == null)
            return Debris(host, ground, name, kit.Clod, count, at, direction, cone, radius, speedMin, speedMax, sizeMin, sizeMax, gravity, delay, light, dark);
        var particles = Particles(host, name, count, 1.4f, 2.0f, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
        spin.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
        spin.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
        Collide(particles, ground, .3f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .8f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(AtMostFourMeshes(kit.Chunks));
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Earth;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return particles;
    }

    /// <summary>
    /// Тон земли босса частицей (умножает фактуру пола kit.Ground): сухая — почти как пол, сырая свежая — темнее.
    /// </summary>
    private static readonly Color GroundDry = new Color(.97f, .95f, .93f), GroundDamp = new Color(.78f, .75f, .72f);
    /// <summary>Зерно земли (спрайты CFXR без света): темнее прежних комьев — на тёмной арене не светится.</summary>
    private static readonly Color GrainLight = new Color(SoilLight.r * .8f, SoilLight.g * .8f, SoilLight.b * .8f), GrainDark = new Color(SoilDark.r * .9f, SoilDark.g * .9f, SoilDark.b * .9f);

    /// <summary>
    /// Мелкие комья земли (владелец 03.10, ночь: вместо «больших кусков примитивов»): гладкие катышки kit.Clods на
    /// фактуре пола kit.Ground (свет, тени на них), 3D-кувырок, баллистика, отскок от ground, к концу жизни уменьшаются и
    /// уходят. Размер — поперечник частицы, м (меш ~1 в поперечнике). Нет мешей — спрайты Debris.
    /// </summary>
    private static ParticleSystem Clods(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float delay)
    {
        if (kit.Clods == null || kit.Clods.Length == 0 || kit.Clods[0] == null)
            return Debris(host, ground, name, kit.Clod, count, at, direction, cone, radius, speedMin, speedMax, sizeMin, sizeMax, gravity, delay, GrainLight, GrainDark);
        var particles = Particles(host, name, count, .9f, 1.4f, speedMin, speedMax, sizeMin, sizeMax, delay);
        particles.transform.localPosition = at;
        particles.transform.localRotation = Aim(direction);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(GroundDry, GroundDamp);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = Mathf.Max(.01f, radius);
        var spin = particles.rotationOverLifetime; spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
        spin.y = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        Collide(particles, ground, .22f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .7f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(AtMostFourMeshes(kit.Clods));
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.Ground;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return particles;
    }

    /// <summary>
    /// Мелкое зерно земли: спрайты комьев CFXR 2,5–6,5 см (без света, темнее), летят, падают, отскакивают и гаснут за
    /// 0,6–1 с — «пыль» комьями, без ваты.
    /// </summary>
    private static ParticleSystem Grains(GameObject host, Transform ground, Kit kit, string name, int count, Vector3 at, Vector3 direction,
        float cone, float radius, float speedMin, float speedMax, float delay)
    {
        var grains = Debris(host, ground, name, kit.Clod, count, at, direction, cone, radius, speedMin, speedMax, .025f, .065f, 1.5f, delay,
            GrainLight, GrainDark);
        var main = grains.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.0f);
        return grains;
    }

    /// <summary>
    /// Места у головы бугра (V17; корень едет за бугром, +Z — ход): count мест на боку side (+1 справа, −1 слева, 0 — по
    /// очереди), |x| xMin…xMax м, z zMin…zMax м, высота yMin…yMax над полом (ниже нуля — из-под пола). Направление —
    /// наружу outward, вверх up, назад back (доли, разброс ±jitter).
    /// </summary>
    private static Mesh MoundPoints(string name, int count, int salt, float side, float xMin, float xMax, float zMin, float zMax,
        float yMin, float yMax, float outward, float up, float back, float jitter = .3f)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float s = side != 0f ? side : i % 2 == 0 ? 1f : -1f;
            float x = s * Mathf.Lerp(xMin, xMax, Hash01(i, salt));
            float z = Mathf.Lerp(zMin, zMax, Hash01(i, salt + 1));
            float y = Mathf.Lerp(yMin, yMax, Hash01(i, salt + 2));
            positions.Add(new Vector3(x, y, z));
            float lo = 1f - jitter, hi = 1f + jitter;
            var d = new Vector3(s * outward * Mathf.Lerp(lo, hi, Hash01(i, salt + 3)), up * Mathf.Lerp(lo, hi, Hash01(i, salt + 4)),
                -back * Mathf.Lerp(lo, hi, Hash01(i, salt + 5)));
            directions.Add(d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up);
        }
        return Points(name, positions, directions);
    }

    /// <summary>
    /// Места ряби впереди головы (V17, ThicketMasterEarthRules.Ripple*): конус от RippleNear до RippleFar м перед головой,
    /// полуширина растёт от RippleNearHalf к RippleFarHalf; высота yMin…yMax (ниже нуля — плита лежит под полом).
    /// Направление — вверх.
    /// </summary>
    private static Mesh RipplePoints(string name, int count, int salt, float yMin, float yMax)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float t = Hash01(i, salt);
            float z = Mathf.Lerp(ThicketMasterEarthRules.RippleNear, ThicketMasterEarthRules.RippleFar, t);
            float half = Mathf.Lerp(ThicketMasterEarthRules.RippleNearHalf, ThicketMasterEarthRules.RippleFarHalf, t);
            positions.Add(new Vector3((Hash01(i, salt + 1) * 2f - 1f) * half, Mathf.Lerp(yMin, yMax, Hash01(i, salt + 2)), z));
            directions.Add(Vector3.up);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Эмиссия из вершин меша по порядку (Loop): каждое место — одна частица.</summary>
    private static void FromPoints(ParticleSystem particles, Mesh points, bool align, float randomDirection)
    {
        particles.transform.localRotation = Quaternion.identity;
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.mesh = points;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.alignToDirection = align;
        shape.randomDirectionAmount = randomDirection;
        shape.normalOffset = 0f;
    }

    /// <summary>
    /// Шипы-корни (код Вендиго): частица на точку, ось — нормаль точки, меш из набора
    /// (−Z — длина, основание в нуле). Растёт только длина: кривые early/late по доле жизни.
    /// </summary>
    private static ParticleSystem Spikes(GameObject root, string name, Mesh[] meshes, Mesh points, Material material, float life,
        float lengthMin, float lengthMax, float thickMin, float thickMax, AnimationCurve early, AnimationCurve late)
    {
        var particles = Particles(root, name, points.vertexCount, life, life, 0f, 0f, lengthMin, lengthMax, 0f);
        var main = particles.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(thickMin, thickMax);
        main.startSizeY = new ParticleSystem.MinMaxCurve(thickMin, thickMax);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(lengthMin, lengthMax);
        FromPoints(particles, points, true, 0f);
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.separateAxes = true;
        size.x = new ParticleSystem.MinMaxCurve(1f);
        size.y = new ParticleSystem.MinMaxCurve(1f);
        size.z = new ParticleSystem.MinMaxCurve(1f, early, late);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(AtMostFourMeshes(meshes));
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return particles;
    }

    /// <summary>Растущий корень: MeshRenderer-ребёнок корня префаба «Grow|мс|°|подпись» (RootSnarerCombatView.AnimateGrows).</summary>
    private static void GrowChild(GameObject root, string label, Mesh mesh, Material material, Vector3 position, Quaternion rotation,
        float scale, int delayMs, int twistDegrees)
    {
        if (mesh == null) return;
        var go = new GameObject(RootSnarerCombatView.GrowPrefix + delayMs + RootSnarerCombatView.GrowSeparator + twistDegrees
            + RootSnarerCombatView.GrowSeparator + label);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>
    /// Префаб в Attacks/Prefabs. Перед сохранением у всех систем — постоянное зерно (вид меняет его
    /// от номера действия: повтор удара — тот же кадр), без автозапуска и без петли.
    /// </summary>
    private static void Save(GameObject root)
    {
        try
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps.useAutoRandomSeed)
                {
                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = Seed(root.name + "/" + ps.name);
                }
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(root.name));
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------- prefabs: paw, stomp

    private const float BS = ThicketMasterCombatView.BodyScale;
    private const float Root2 = 1.4142f;

    /// <summary>
    /// Следы когтей (ревью 02.10: «след плоский и идёт не за лапой, а после неё»): готовой дуги
    /// CFXR больше нет. Префаб — только «Claws» (MeshFilter + MeshRenderer, kit.GlowClaw):
    /// три светящиеся ленты строит вид каждый кадр по кости пальцев бьющей лапы
    /// (ThicketMasterCombatView.Vfx, ClawTrail) — лента ровно за когтями, держит хит-стоп и паузу.
    /// </summary>
    private static void SavePawSlash(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PawSlashName);
        var claws = Child(root, ThicketMasterCombatView.ClawsChild, Vector3.zero);
        claws.AddComponent<MeshFilter>();
        var renderer = claws.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = kit.GlowClaw;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Save(root);
    }

    /// <summary>
    /// Удар лапы о землю: корень — под пальцами лапы, +Z — взгляд тела. Тёмная звезда трещин
    /// под пальцами (плоское — только она и пятно), веер земли там, где прошли когти: комья
    /// объёмом и спрайтами дугой вперёд-вбок, штрихи пыли по дуге, стоячие клубы, щепки, листья.
    /// </summary>
    private static void SavePawImpact(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PawImpactName);
        var ground = root.transform;
        Decal(root, "Star", kit.Star, 1.8f * BS * Root2, 1.9f * BS * Root2, 1.4f, .55f, CrackTone, 0f, .04f);
        Decal(root, "Soil", kit.Soil, 1.2f * BS, 1.4f * BS, 1.4f, .6f, new Color(SoilDark.r, SoilDark.g, SoilDark.b, .7f), 0f, .03f);
        Flash(root, kit, "Flash", Vector3.up * .5f, .8f * BS, .1f, new Color(Ember.r, Ember.g, Ember.b, .5f), 0f, false);
        Embers(root, kit, "Embers", 12, Vector3.up * .2f, .4f, 2.5f, 5f, .8f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        // Веер земли по дуге когтей: точки на дуге ±70° впереди лапы, вылет наружу-вверх.
        var fan = ArcPoints("ThicketPawFanPoints", 18, .35f * BS, .9f * BS, 70f, 30f, 60f, 811);
        var spray = Chunks(root, ground, kit, "Spray Chunks", 18, Vector3.up * .08f, Vector3.up, 0f, .1f, 3.2f, 5.6f, .12f, .26f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(spray, fan, false, .25f);
        var clods = Debris(root, ground, "Clods", kit.Clod, 16, Vector3.up * .08f, Vector3.up, 0f, .1f, 2.6f, 5.0f, .10f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, fan, false, .3f);
        // Пыль — низкими мелкими клубами наружу по дуге (ревью 02.10, вечер: «вата» закрывала героя).
        LowPuffs(root, kit, "Dust", 16, ArcPoints("ThicketPawDustPoints", 16, .3f * BS, 1.1f * BS, 75f, 15f, 40f, 821), 1.2f, 2.6f,
            .55f, .85f, .45f, .65f, .38f, 0f);
        var streaks = Particles(root, "Streaks", 10, .25f, .35f, 5f, 8f, .25f, .4f, 0f);
        FromPoints(streaks, ArcPoints("ThicketPawStreakPoints", 10, .4f * BS, .8f * BS, 70f, 70f, 85f, 831), false, .1f);
        var streaksMain = streaks.main; streaksMain.startColor = new Color(DustLight.r, DustLight.g, DustLight.b, .55f);
        var streaksFade = streaks.colorOverLifetime; streaksFade.enabled = true; streaksFade.color = Alpha(0f, .3f);
        var streaksRenderer = streaks.GetComponent<ParticleSystemRenderer>();
        streaksRenderer.renderMode = ParticleSystemRenderMode.Stretch; streaksRenderer.lengthScale = 2f; streaksRenderer.velocityScale = .12f;
        streaksRenderer.sharedMaterial = kit.Wind;
        Debris(root, ground, "Splinters", kit.Splinter, 4, Vector3.up * .1f, new Vector3(0f, 1f, .2f), 40f, .2f, 2.2f, 3.6f, .10f, .18f, 1.3f, .01f, BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 5, Vector3.up * .15f, Vector3.up, 50f, 1.2f, 2.6f, .02f);
        Save(root);
    }

    /// <summary>count мест на дуге ±halfArc° вокруг +Z на радиусах [inner, outer], направление — наружу с подъёмом tiltMin…tiltMax от горизонта.</summary>
    private static Mesh ArcPoints(string name, int count, float inner, float outer, float halfArc, float tiltMin, float tiltMax, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Lerp(-halfArc, halfArc, (i + Hash01(i, salt) * .8f) / count) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float lift = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * Mathf.Lerp(inner, outer, Hash01(i, salt + 1)));
            directions.Add((outward * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Дыбом: из-под задних лап юбка пыли и комья. Корень — центр тела, +Z — взгляд.</summary>
    private static void SaveStompRear(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompRearName);
        var rear = Child(root, "Rear", new Vector3(0f, 0f, -ThicketMasterCombatView.StompRearBack * BS));
        // Юбка стоячими клубами (ревью 02.10: лёжа — «наклейка»): кольцо пыли из-под лап наружу — низкое,
        // мелкое и короткое (ревью 02.10, вечер: «ватные» клубы закрывали героя и босса).
        Wall(rear, "Skirt", kit.Haze, 26, .3f, .5f * BS, 2.2f * BS, .35f, .6f, .55f, .85f,
            new Color(DustDark.r, DustDark.g, DustDark.b, .34f), new Color(SoilLight.r, SoilLight.g, SoilLight.b, .3f), .1f, 0f);
        Dust(rear, kit, "Puff", 6, Vector3.zero, 55f, .6f, .6f, 1.4f, .5f, .8f, .45f, .65f, .28f, .02f, false);
        Debris(rear, root.transform, "Clods", kit.Clod, 6, Vector3.up * .08f, new Vector3(0f, 1f, -.4f), 45f, .6f, 2.0f, 3.6f, .10f, .2f, 1.6f, 0f, SoilLight, SoilDark);
        Save(root);
    }

    /// <summary>Стена топота: доля радиуса, с которой встаёт, и за сколько секунд добегает до края круга.</summary>
    private const float StompWallFrom = .22f, StompWallTravel = .32f, StompOuterTravel = .26f;

    /// <summary>
    /// Топот, круг (r из Sim). Ревью 02.10: «плоско, по-наклеечному» — объём: низкая стена мелких
    /// клубов встаёт у лап и за 0,32 с добегает до края круга (живёт 0,6 с, гребня на 1,7 м нет — ревью
    /// вечера: «вата» закрывала героя и босса), штрихи порыва, комья и камни объёмом летят наружу и
    /// падают в круг, тонкая светящаяся кромка бежит со стеной.
    /// Плоское — только тёмные звёзды трещин под передними лапами. Корень — центр тела, +Z — взгляд.
    /// </summary>
    private static void SaveStompQuake(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompQuakeName);
        var ground = root.transform;
        float r = Simulation.ThicketStompRadius.ToFloat();
        float grow = StompWallTravel / .7f;
        Wave(root, "Ring", kit.GlowRingThin, 2f * r * Root2, .7f, StompWallFrom, grow, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        // Стена — низкая (центр клубов 0,35 м), мелкая, короткая (0,6 с) и прозрачнее; гребня на 1,7 м больше нет
        // (ревью 02.10, вечер: бежевая «вата» закрывала героя и босса на 1–2 с). Объём — комья ниже.
        Wall(root, "Wall", kit.Haze, 110, .35f, r * StompWallFrom, r * .97f, StompWallTravel, .6f, .65f, 1.05f,
            new Color(DustDark.r, DustDark.g, DustDark.b, .4f), new Color(SoilLight.r, SoilLight.g, SoilLight.b, .36f), .12f, 0f);
        Wall(root, "Glow Crest", kit.GlowHaze, 24, .45f, r * StompWallFrom, r, StompWallTravel, .55f, .7f, 1.0f,
            new Color(Ember.r, Ember.g * .9f, Ember.b, .32f), new Color(EmberDeep.r, EmberDeep.g, EmberDeep.b, .26f), .2f, 0f);
        Streaks(root, "Streaks", kit.Wind, 30, .35f, r * StompWallFrom, r, StompWallTravel, .45f, .35f, .55f,
            new Color(DustLight.r, DustLight.g, DustLight.b, .6f), 0f);
        var embers = Embers(root, kit, "Embers", 28, Vector3.up * .15f, .2f, 3f, 6f, 1.0f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        FromPoints(embers, RingPoints("ThicketStompEmberPoints", 28, r * .3f, r * .9f, 40f, 70f, 171), false, .3f);
        foreach (float side in new[] { -1f, 1f })
        {
            var paw = Child(root, side < 0f ? "Paw L" : "Paw R", new Vector3(.95f * side, 0f, 2f));
            Decal(paw, "Star", kit.Star, 2.2f * Root2, 2.3f * Root2, 2.0f, .6f, CrackTone, 0f, .045f);
            Chunks(paw, ground, kit, "Chunks", 7, Vector3.up * .08f, Vector3.up, 45f, .3f, 2.6f, 4.4f, .14f, .28f, 1.6f, 0f, SoilLight, SoilDark);
            Dust(paw, kit, "Puff", 8, Vector3.zero, 60f, .4f, .6f, 1.4f, .5f, .8f, .45f, .65f, .38f, 0f, false);
        }
        var clods = Chunks(root, ground, kit, "Chunks", 34, Vector3.up * .1f, Vector3.up, 0f, .1f, 5f, 8f, .14f, .32f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketStompClodPoints", 34, r * .2f, r * .45f, 38f, 58f, 111), false, .25f);
        var rocks = Chunks(root, ground, kit, "Rocks", 10, Vector3.up * .1f, Vector3.up, 0f, .1f, 4f, 6.5f, .3f, .46f, 1.9f, .01f, RockLight, RockDark);
        FromPoints(rocks, RingPoints("ThicketStompRockPoints", 10, r * .3f, r * .55f, 35f, 55f, 121), false, .25f);
        var sprites = Debris(root, ground, "Clods", kit.Clod, 20, Vector3.up * .1f, Vector3.up, 0f, .1f, 4f, 7f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(sprites, RingPoints("ThicketStompSpritePoints", 20, r * .25f, r * .6f, 40f, 65f, 112), false, .3f);
        var leaves = Leaves(root, ground, kit, "Leaves", 8, Vector3.up * .15f, Vector3.up, 0f, 2.5f, 4.5f, .03f);
        FromPoints(leaves, RingPoints("ThicketStompLeafPoints", 8, r * .3f, r * .7f, 30f, 60f, 131), false, .5f);
        Save(root);
    }

    /// <summary>
    /// Топот, второе кольцо (полоса r..r_out из Sim): своя низкая стена мелких клубов встаёт на краю
    /// круга и бежит до внешнего края, штрихи, комья объёмом наружу, тонкая кромка до r_out.
    /// </summary>
    private static void SaveStompOuter(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StompOuterName);
        float inner = Simulation.ThicketStompRadius.ToFloat(), outer = Simulation.ThicketStompRingOuterRadius.ToFloat();
        Wave(root, "Ring", kit.GlowRingThin, 2f * outer * Root2, .7f, inner / outer, StompOuterTravel / .7f, new Color(Ember.r, Ember.g, Ember.b, .95f), 0f);
        // Низкая короткая стена мелких клубов, без гребня на 1,7 м (ревью 02.10, вечер — как у круга топота).
        Wall(root, "Wall", kit.Haze, 120, .35f, inner * .95f, outer * .97f, StompOuterTravel, .6f, .65f, 1.05f,
            new Color(DustDark.r, DustDark.g, DustDark.b, .38f), new Color(SoilLight.r, SoilLight.g, SoilLight.b, .34f), .12f, 0f);
        Wall(root, "Glow Crest", kit.GlowHaze, 30, .45f, inner * .95f, outer, StompOuterTravel, .5f, .7f, 1.0f,
            new Color(Ember.r, Ember.g * .9f, Ember.b, .3f), new Color(EmberDeep.r, EmberDeep.g, EmberDeep.b, .24f), .2f, 0f);
        Streaks(root, "Streaks", kit.Wind, 36, .35f, inner, outer, StompOuterTravel, .42f, .35f, .55f,
            new Color(DustLight.r, DustLight.g, DustLight.b, .55f), 0f);
        var sparks = Embers(root, kit, "Embers", 22, Vector3.up * .15f, .2f, 2.5f, 5f, .9f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        FromPoints(sparks, RingPoints("ThicketStompOuterEmberPoints", 22, inner, outer, 40f, 70f, 181), false, .3f);
        var clods = Chunks(root, root.transform, kit, "Chunks", 24, Vector3.up * .1f, Vector3.up, 0f, .1f, 3.5f, 6f, .12f, .28f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketStompOuterClodPoints", 24, inner, (inner + outer) * .5f, 40f, 65f, 161), false, .25f);
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: dive

    /// <summary>
    /// Контур тела 4,14 м сверху (корпус Sim, § 8 контракта): полуоси вбок и вдоль взгляда, сдвиг центра вперёд, м
    /// (ThicketMasterEarthRules — те же числа у правил раскладки и тестов).
    /// </summary>
    private const float BodyHalfWidth = ThicketMasterEarthRules.BodyHalfWidth, BodyHalfLength = ThicketMasterEarthRules.BodyHalfLength,
        BodyShift = ThicketMasterEarthRules.BodyShift;

    // ------------------------------------------------------------- earth layers (V14)

    /// <summary>
    /// Места на рваной полосе вокруг тела (ThicketMasterEarthRules.ContourSpot: дугами с разрывами — не кольцо): доля
    /// радиуса контура dMin…dMax, высота yMin…yMax; направление — наружу (inward = false) или внутрь, с подъёмом
    /// tiltMin…tiltMax° от земли (меньше нуля — вниз).
    /// </summary>
    private static Mesh ContourPoints(string name, int count, int salt, float dMin, float dMax, float yMin, float yMax, bool inward,
        float tiltMin, float tiltMax)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            ThicketMasterEarthRules.ContourSpot(i, count, salt, dMin, dMax, out float angle, out float d);
            ThicketMasterEarthRules.BodyPoint(angle, d, out float x, out float z);
            ThicketMasterEarthRules.BodyNormal(angle, out float nx, out float nz);
            var flat = new Vector3(nx, 0f, nz) * (inward ? -1f : 1f);
            float lift = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(new Vector3(x, Mathf.Lerp(yMin, yMax, Hash01(i, salt + 2)), z));
            directions.Add((flat * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>
    /// Места выброса выхода внутри тела (ThicketMasterEarthRules.AlongBodySpot: гуще к носу и хвосту — тело рвёт землю
    /// вдоль себя): доля радиуса dMin…dMax, направление — наружу с подъёмом tiltMin…tiltMax°.
    /// </summary>
    private static Mesh AlongBodyPoints(string name, int count, int salt, float dMin, float dMax, float yMin, float yMax, float tiltMin, float tiltMax)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            ThicketMasterEarthRules.AlongBodySpot(i, count, salt, dMin, dMax, out float angle, out float d);
            ThicketMasterEarthRules.BodyPoint(angle, d, out float x, out float z);
            ThicketMasterEarthRules.BodyNormal(angle, out float nx, out float nz);
            float lift = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(new Vector3(x, Mathf.Lerp(yMin, yMax, Hash01(i, salt + 2)), z));
            directions.Add((new Vector3(nx, 0f, nz) * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Места на кольце [inner, outer] вокруг точки (вздутие у точки выхода): направление — наружу с подъёмом tilt°.</summary>
    private static Mesh AroundPoints(string name, int count, int salt, float inner, float outer, float y, float tiltMin, float tiltMax)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .7f) / count * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float lift = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            positions.Add(outward * Mathf.Lerp(inner, outer, Hash01(i, salt + 1)) + Vector3.up * y);
            directions.Add((outward * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>
    /// Обломки земли объёмом (V14): меш-частицы meshes на непрозрачном material (плиты дёрна, камни поляны, большие
    /// комья), кувырок в 3D, который гаснет к spinStop доли жизни (лёг — не крутится), баллистика, отскок от ground
    /// (радиус столкновения — collide доли размера, плита лежит, а не висит), лежит щебнем и к концу жизни уходит
    /// (размер к нулю за последние ~18 %). Размер — поперечник, м. Места и поток ставит зовущий.
    /// </summary>
    private static ParticleSystem EarthChunks(GameObject host, Transform ground, string name, Mesh[] meshes, Material material, int count,
        float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float lifeMin, float lifeMax, float spin, float spinStop,
        float bounce, float collide, Color light, Color dark)
    {
        var particles = Particles(host, name, count, lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, 0f);
        var main = particles.main;
        main.gravityModifier = gravity;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        RandomTumble(particles);
        var rotation = particles.rotationOverLifetime; rotation.enabled = true;
        rotation.separateAxes = true;
        var fall = Curve(0f, 1f, spinStop, 0f, 1f, 0f);
        var fallBack = Curve(0f, -1f, spinStop, 0f, 1f, 0f);
        rotation.x = new ParticleSystem.MinMaxCurve(spin, fallBack, fall);
        rotation.y = new ParticleSystem.MinMaxCurve(spin * .5f, fallBack, fall);
        rotation.z = new ParticleSystem.MinMaxCurve(spin, fallBack, fall);
        if (ground != null)
        {
            Collide(particles, ground, bounce);
            var collision = particles.collision;
            collision.dampen = .5f;
            collision.radiusScale = collide;
        }
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .82f, 1f, 1f, 0f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.SetMeshes(AtMostFourMeshes(meshes));
        renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.enableGPUInstancing = true;
        return particles;
    }

    /// <summary>Плиты дёрна: плоский поперечник sizeMin…sizeMax (X и Z порознь), толщина — thickMin…thickMax высоты меша.</summary>
    private static void SlabSize(ParticleSystem slabs, float sizeMin, float sizeMax, float thickMin, float thickMax)
    {
        var main = slabs.main;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSizeY = new ParticleSystem.MinMaxCurve(thickMin, thickMax);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
    }

    /// <summary>
    /// Пятна земли на полу (V14): горизонтальные билборды material (лёжа рисуются ~1/√2 размера — зовущие дают ×√2),
    /// по местам points, разворот наугад, раскрываются от open доли к 1 за grow доли жизни, проявляются за fadeIn,
    /// тают с fadeOut; atlasX × atlasY — кадр атласа наугад. Поток — span с (0 — залп).
    /// </summary>
    private static ParticleSystem GroundPatches(GameObject host, string name, Material material, int count, Mesh points, float sizeMin, float sizeMax,
        float life, float fadeIn, float fadeOut, float open, float grow, Color light, Color dark, float span, int atlasX, int atlasY, float sortFudge)
    {
        var patches = Particles(host, name, count, life * .96f, life, 0f, 0f, sizeMin, sizeMax, 0f);
        var main = patches.main;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        if (points != null) FromPoints(patches, points, false, 0f);
        else { var shape = patches.shape; shape.enabled = false; }
        var size = patches.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, open, Mathf.Max(.01f, grow), 1f, 1f, 1.03f));
        var fade = patches.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(fadeIn, fadeOut);
        if (atlasX * atlasY > 1)
        {
            var sheet = patches.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = atlasX; sheet.numTilesY = atlasY;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, atlasX * atlasY - .01f);
            sheet.cycleCount = 1;
        }
        if (span > 0f) Stream(patches, count / span, span, count);
        var renderer = patches.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = sortFudge;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        return patches;
    }

    /// <summary>
    /// Пыль земли (V14): клубы SmokeAnim2 (kit.DustFlip, 8 × 8 кадров за жизнь — клуб растёт и редеет сам) по местам
    /// points наружу; низкие (центр ≤ 0,3 м, верх ≤ DustMaxHeight), короткие (≤ DustMaxLife), землистые. На kit.Haze
    /// (A/B ведущего) — без кадров.
    /// </summary>
    private static ParticleSystem EarthDust(GameObject host, Kit kit, string name, int count, Mesh points, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float delay)
    {
        var dust = LowPuffs(host, kit, name, count, points, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, alpha, delay);
        Flipbook(dust, kit);
        var size = dust.sizeOverLifetime; size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .35f, 1f, 1f, 1.1f));
        return dust;
    }

    /// <summary>Пыль на kit.DustFlip: материал и кадры SmokeAnim2 за жизнь (8 × 8, один проход).</summary>
    private static void Flipbook(ParticleSystem dust, Kit kit)
    {
        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = kit.DustFlip;
        if (kit.DustFlip == null || kit.DustFlip == kit.Haze) return;
        var sheet = dust.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 8; sheet.numTilesY = 8;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        // Начало — на 0–6 кадре (клуб не крошечный), конец — не дальше 63-го: без перескока на первый кадр.
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, 1f, .89f));
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 6f);
        sheet.cycleCount = 1;
        // Кадры SmokeAnim2 уже с поворотом клуба — свой поворот слабее.
        var spin = dust.rotationOverLifetime; spin.z = new ParticleSystem.MinMaxCurve(-.4f, .4f);
    }

    /// <summary>Зерно земли (V14): комочки Debris2 (2 × 2, kit.Grain) 3–7 см вместо белых многоугольников CFXR.</summary>
    private static ParticleSystem EarthGrains(GameObject host, Transform ground, Kit kit, string name, int count, float speedMin, float speedMax, float delay)
    {
        var grains = Debris(host, ground, name, kit.Grain, count, Vector3.up * .1f, Vector3.up, 0f, .1f, speedMin, speedMax, .03f, .07f, 1.5f, delay,
            GrainLight, GrainDark);
        var main = grains.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.0f);
        if (kit.Grain != kit.Clod) Sheet(grains, 2, 3.99f);
        return grains;
    }

    /// <summary>
    /// Рваная трава (V14, как у бугра V11): пучки дёрна со светом (kit.KnollTuft на kit.KnollGreen) по местам points,
    /// кувыркаются и ложатся; нет пучка — мелкие комья.
    /// </summary>
    private static ParticleSystem TornGrass(GameObject host, Transform ground, Kit kit, string name, int count, Mesh points, float speedMin,
        float speedMax, float delay)
    {
        var grass = Clods(host, ground, kit, name, count, Vector3.zero, Vector3.up, 0f, .1f, speedMin, speedMax, .2f, .34f, 1.2f, delay);
        FromPoints(grass, points, false, .2f);
        Shuffle(grass);
        var main = grass.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.52f, .70f, .26f), new Color(.36f, .52f, .17f));
        var renderer = grass.GetComponent<ParticleSystemRenderer>();
        if (kit.KnollTuft != null)
        {
            renderer.SetMeshes(new[] { kit.KnollTuft });
            renderer.sharedMaterial = kit.KnollGreen;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        return grass;
    }

    /// <summary>Мелкие комья земли на каменистой земле (V14): ClodMesh на kit.StonySoil вместо гладкой земли TCom.</summary>
    private static ParticleSystem StonyClods(GameObject host, Transform ground, Kit kit, string name, int count, Mesh points, float speedMin,
        float speedMax, float sizeMin, float sizeMax, float gravity, float delay)
    {
        var clods = Clods(host, ground, kit, name, count, Vector3.zero, Vector3.up, 0f, .1f, speedMin, speedMax, sizeMin, sizeMax, gravity, delay);
        FromPoints(clods, points, false, .3f);
        Shuffle(clods);
        var renderer = clods.GetComponent<ParticleSystemRenderer>();
        if (renderer.renderMode == ParticleSystemRenderMode.Mesh) renderer.sharedMaterial = kit.StonySoil;
        renderer.enableGPUInstancing = true;
        return clods;
    }

    /// <summary>Одна декаль на земле (трещины, крошка, большое пятно): Decal с ростом от open за grow доли жизни и своим поворотом.</summary>
    private static ParticleSystem GroundDecal(GameObject host, string name, Material material, float size, float life, float fadeIn, float fadeOut,
        Color color, float delay, float lift, float open, float grow, float rotation)
    {
        var decal = Decal(host, name, material, size, size, life, fadeOut, color, delay, lift);
        var main = decal.main; main.startRotation = rotation;
        var curve = decal.sizeOverLifetime; curve.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, open, Mathf.Max(.01f, grow), 1f, 1f, 1f));
        var fade = decal.colorOverLifetime; fade.color = Alpha(fadeIn, fadeOut);
        return decal;
    }

    /// <summary>
    /// Знак наклона плит, валящихся в яму (V14): кромка к телу (−Z меша при выравнивании по направлению — ловушка
    /// частиц Unity) уходит вниз при отрицательном повороте по X. Если в игре плиты клонятся наружу — сменить на +1.
    /// </summary>
    private const float DiveSlabPitchSign = -1f;

    /// <summary>
    /// Уход в землю (V14, владелец 04.10: «как кольцо какое-то… земля фактурная, а не гладкая плоская»). Корень — тело на
    /// тике начала нырка (вид ставит с T), +Z — взгляд; сроки — ThicketMasterEarthRules. Земля вокруг тела ломается
    /// рваными кусками, а не валом: под телом проступает пятно сырой земли с травяной кромкой (9e02a550), по контуру —
    /// рваные пятна разрытой земли (dirt_0–5) и тёмные трещины (Crater43 + Crack4, сеть на 0,5 с тлеет углём); все 12 тиков
    /// ухода плиты дёрна (Cobble01–06: сверху дёрн пола, снизу земля) по контуру сползают к телу, клонятся в яму и уходят
    /// вниз; комья каменистой земли, большие комья (NoiseSphere1), камни поляны и зерно (Debris2) брызжут вверх и падают
    /// назад; низкая пыль SmokeAnim2 у контура гаснет к ~1,25 с; рваная трава валится внутрь. Вала LipMesh и пары
    /// симметричных декалей больше нет.
    /// </summary>
    private static void SaveDiveBurst(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DiveBurstName);
        var ground = root.transform;
        float dive = ThicketMasterEarthRules.BurrowSeconds, life = ThicketMasterEarthRules.DiveLife;
        float rotation = 0f;

        // Пол: пятно сырой земли под телом (кромка уходит в дёрн — без чёткого края), рваные пятна по контуру.
        GroundPatches(root, "Worn Patch", kit.WornPatch, 1, null, 13f, 13f, life, .06f, .76f, .82f, .1f,
            Color.white, new Color(.92f, .9f, .88f), 0f, 1, 1, 6f).transform.localPosition = new Vector3(0f, .025f, BodyShift);
        GroundPatches(root, "Dirt Patches", kit.DirtPatch, ThicketMasterEarthRules.DivePatches,
            ContourPoints("ThicketDivePatchSpots", ThicketMasterEarthRules.DivePatches, 1401, .78f, 1.02f, .035f, .045f, false, 0f, 0f),
            2.6f, 3.6f, life * .96f, .04f, .74f, .55f, .1f, Color.white, new Color(.82f, .8f, .78f), .25f, 3, 2, 5f);

        // Трещины: рваная звезда Crater43 и сеть Crack4 — тёмные; та же сеть угольком на 0,55 с (тёмная арена).
        GroundDecal(root, "Cracks", kit.CrackRadial, 2f * BodyHalfLength * 1.15f * Root2, life, 0f, .7f, CrackTone, 0f, .05f, .6f, .12f, rotation);
        GroundDecal(root, "Cracks Net", kit.Crack, 2f * BodyHalfLength * .95f * Root2, life, 0f, .66f,
            new Color(CrackTone.r, CrackTone.g, CrackTone.b, .8f), .05f, .055f, .5f, .14f, rotation + .6f);
        GroundDecal(root, "Cracks Glow", kit.GlowCrack, 2f * BodyHalfLength * .95f * Root2, .55f, 0f, .3f,
            new Color(Ember.r, Ember.g, Ember.b, .7f), .05f, .06f, .5f, .3f, rotation + .6f);

        // Плиты дёрна по контуру: сползают внутрь (к телу), клонятся кромкой в яму и уходят в землю, пока тело уходит.
        var slabs = EarthChunks(root, null, "Slabs", kit.Slabs, kit.Turf, ThicketMasterEarthRules.DiveSlabs, .7f, 1.4f, .55f, 1.05f, 0f, .75f, 1.0f,
            0f, .45f, 0f, .3f, Color.white, new Color(.84f, .82f, .8f));
        SlabSize(slabs, .5f, 1.05f, .5f, .85f);
        FromPoints(slabs, ContourPoints("ThicketDiveSlabSpots", ThicketMasterEarthRules.DiveSlabs, 1411, .86f, 1.04f, .0f, .04f, true, -22f, -8f), true, 0f);
        Shuffle(slabs);
        var slabsMain = slabs.main;
        slabsMain.startRotationX = 0f; slabsMain.startRotationY = 0f;
        slabsMain.startRotationZ = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        var slabsTilt = slabs.rotationOverLifetime;
        // Оси вращения и скорости — в одном режиме кривых (Unity требует один режим на x/y/z).
        slabsTilt.x = new ParticleSystem.MinMaxCurve(2.2f * DiveSlabPitchSign, Curve(0f, .55f, .45f, 0f, 1f, 0f), Curve(0f, 1f, .45f, 0f, 1f, 0f));
        slabsTilt.y = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f), Curve(0f, 0f, 1f, 0f));
        slabsTilt.z = new ParticleSystem.MinMaxCurve(.4f, Curve(0f, -1f, .45f, 0f, 1f, 0f), Curve(0f, 1f, .45f, 0f, 1f, 0f));
        var slabsSink = slabs.velocityOverLifetime; slabsSink.enabled = true;
        slabsSink.space = ParticleSystemSimulationSpace.Local;
        slabsSink.x = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        slabsSink.z = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        slabsSink.y = new ParticleSystem.MinMaxCurve(-1.3f, Curve(0f, 0f, .35f, .3f, 1f, 1f));
        var slabsSize = slabs.sizeOverLifetime; slabsSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .7f, .9f, 1f, 0f));
        Stream(slabs, ThicketMasterEarthRules.DiveSlabs / dive, dive, ThicketMasterEarthRules.DiveSlabs);

        // Брызги земли вверх по контуру — все 12 тиков ухода: комья каменистой земли, большие комья, камни поляны, зерно.
        var clods = StonyClods(root, ground, kit, "Clods", ThicketMasterEarthRules.DiveClods,
            ContourPoints("ThicketDiveClodSpots", 48, 1421, .9f, 1.08f, .08f, .22f, false, 50f, 80f), 2.8f, 5.2f, .05f, .18f, 1.6f, 0f);
        Stream(clods, ThicketMasterEarthRules.DiveClods / dive, dive, ThicketMasterEarthRules.DiveClods);
        var big = EarthChunks(root, ground, "Big Clods", kit.BigClods, kit.StonySoil, ThicketMasterEarthRules.DiveBigClods, 2.5f, 4.2f, .22f, .34f, 1.8f,
            1.1f, 1.6f, 7f, .4f, .2f, .4f, GroundDry, GroundDamp);
        FromPoints(big, ContourPoints("ThicketDiveBigSpots", ThicketMasterEarthRules.DiveBigClods, 1431, .9f, 1.05f, .1f, .2f, false, 55f, 75f), false, .2f);
        Stream(big, ThicketMasterEarthRules.DiveBigClods / dive, dive, ThicketMasterEarthRules.DiveBigClods);
        int rocksA = ThicketMasterEarthRules.DiveRocks * 3 / 5, rocksB = ThicketMasterEarthRules.DiveRocks - rocksA;
        var rockSpots = ContourPoints("ThicketDiveRockSpots", ThicketMasterEarthRules.DiveRocks, 1441, .88f, 1.05f, .08f, .18f, false, 45f, 70f);
        var rockA = EarthChunks(root, ground, "Rocks A", new[] { kit.RockMeshA ?? kit.Pebbles[0] }, kit.RockA, rocksA, 2.4f, 4.4f, .12f, .28f, 1.9f,
            1.3f, 1.9f, 8f, .45f, .25f, .45f, Color.white, new Color(.8f, .8f, .78f));
        FromPoints(rockA, rockSpots, false, .2f);
        Stream(rockA, rocksA / dive, dive, rocksA);
        var rockB = EarthChunks(root, ground, "Rocks B", new[] { kit.RockMeshB ?? kit.Pebbles[0] }, kit.RockB, rocksB, 2.4f, 4.4f, .12f, .26f, 1.9f,
            1.3f, 1.9f, 8f, .45f, .25f, .45f, Color.white, new Color(.8f, .8f, .78f));
        FromPoints(rockB, rockSpots, false, .2f);
        Shuffle(rockB);
        Stream(rockB, rocksB / dive, dive, rocksB);
        var grains = EarthGrains(root, ground, kit, "Grains", ThicketMasterEarthRules.DiveGrains, 2.5f, 5.5f, 0f);
        FromPoints(grains, ContourPoints("ThicketDiveGrainSpots", 40, 1451, .92f, 1.08f, .05f, .2f, false, 40f, 80f), false, .4f);
        Shuffle(grains);
        Stream(grains, ThicketMasterEarthRules.DiveGrains / (dive + .1f), dive + .1f, ThicketMasterEarthRules.DiveGrains);

        // Низкая пыль у контура: закрывает линию, где тело входит в землю, гаснет к ~1,25 с — до того, как бугор отъедет.
        var dust = EarthDust(root, kit, "Dust", ThicketMasterEarthRules.DiveDust,
            ContourPoints("ThicketDiveDustSpots", 32, 1461, .95f, 1.1f, .12f, .3f, false, 5f, 25f), 1.5f, 3f, .8f, 1.2f, .5f,
            ThicketMasterEarthRules.DiveDustLife, .45f, 0f);
        Stream(dust, ThicketMasterEarthRules.DiveDust / (dive + .1f), dive + .1f, ThicketMasterEarthRules.DiveDust);

        // Трава рвётся с краёв и валится внутрь; щепки, листья и редкие угольки — как были, меньше.
        var grass = TornGrass(root, ground, kit, "Torn Grass", ThicketMasterEarthRules.DiveTufts,
            ContourPoints("ThicketDiveTuftSpots", ThicketMasterEarthRules.DiveTufts, 1471, .95f, 1.1f, .03f, .08f, true, 45f, 70f), 1.8f, 3.2f, 0f);
        Stream(grass, ThicketMasterEarthRules.DiveTufts / dive, dive, ThicketMasterEarthRules.DiveTufts);
        Debris(root, ground, "Splinters", kit.Splinter, 6, Vector3.up * .1f, Vector3.up, 45f, .5f, 3f, 5.5f, .10f, .2f, 1.3f, .12f, BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 6, Vector3.up * .2f, Vector3.up, 55f, 2.5f, 4.5f, .1f);
        Embers(root, kit, "Embers", 8, Vector3.up * .2f, .6f * BS, 3f, 6.5f, 1.0f, new Color(Ember.r, Ember.g, Ember.b, 1f), .1f);
        Save(root);
    }

    /// <summary>
    /// count мест по контуру тела (эллипс ax вбок × az вдоль взгляда, центр сдвинут на shift по +Z);
    /// filled — внутри эллипса; высота yMin…yMax; направление — наружу с подъёмом tiltMin…tiltMax.
    /// </summary>
    private static Mesh EllipsePoints(string name, int count, float ax, float az, float shift, bool filled, float yMin, float yMax,
        float tiltMin, float tiltMax, int salt)
    {
        var positions = new List<Vector3>();
        var directions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i + Hash01(i, salt) * .8f) / count * Mathf.PI * 2f;
            float d = filled ? Mathf.Sqrt(Hash01(i, salt + 1)) : Mathf.Lerp(.92f, 1.08f, Hash01(i, salt + 1));
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            positions.Add(new Vector3(outward.x * ax * d, Mathf.Lerp(yMin, yMax, Hash01(i, salt + 2)), outward.z * az * d + shift));
            float lift = Mathf.Lerp(tiltMin, tiltMax, Hash01(i, salt + 3)) * Mathf.Deg2Rad;
            directions.Add((outward * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift)).normalized);
        }
        return Points(name, positions, directions);
    }

    /// <summary>Места точек — вразброс, а не по кругу подряд: поток не обходит контур «часовой стрелкой».</summary>
    private static void Shuffle(ParticleSystem particles)
    {
        var shape = particles.shape;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Random;
    }

    /// <summary>
    /// Сколько секунд идут потоки бугра. Под землёй он до 2,8 с (ход 18–60 + круг 24 тика, § 17.1), но каждые Песочные
    /// Часы добавляют 2 с (ThicketHourglassShiftTicks), а эффект идёт по тикам Sim: эмиссия не должна кончиться, пока
    /// бугор ещё под землёй. С запасом; гасит эмиссию вид, когда босс вылез (ThicketMasterCombatView.SurfaceMound).
    /// </summary>
    private const float MoundStreamSeconds = 12f;

    /// <summary>Столько штук на метр хода → частота префаба на ходу MoundRefSpeed (вид множит её на TrailRateScale хода).</summary>
    private static float PerMetre(float count) => count * ThicketMasterEarthRules.MoundRefSpeed;

    /// <summary>
    /// Ход под землёй — земля рвётся на месте (V17, владелец 08.10: «само перемещение под землёй оч быстрое и всё ещё как
    /// будто холмик просто скользит по полу. без какой-то фактуры и вау-эффекта пробуривания земли»; раньше — «плоский
    /// шарик», «как в Дюне», «земля фактурная»). Каменистой кучи головы V15 (RubbleMoundMesh), тёмного пятна под ней и плит
    /// на её плечах больше нет: с головой не едет ничего, что несёт облик, — корень едет за MoundPosition (вид; +Z — ход)
    /// и только переносит эмиттеры; всё, что они пускают, — в мире, там, где голова была, а меши — в осях мира (лёгшие
    /// плиты не поворачиваются, когда бугор сворачивает). Земля — принятые ассеты нырка и выхода V14 (плиты ThicketSlab1–6
    /// на M_Thicket_Turf, камни поляны ThicketRockA/B, комья, зерно Debris2, пыль SmokeAnim2, пятна dirt_0–5, Crack5).
    /// Правила и бюджет — ThicketMasterEarthRules (раздел «ход под землёй»).
    /// Всегда («Head …»): у головы тлеют короткие трещины углём (Crack5 лучами, без пятен-ореолов), сыплются угольки —
    /// голову видно на тёмной арене. На ходу («Trail …», частота — по пройденным метрам):
    ///  • «Burst Slabs», «Big Slabs», «Rocks A/B» — из пола у головы вырываются плиты дёрна, большие плиты и камни поляны,
    ///    кувыркаются, падают туда, где встали (снос ≤ BurstDrift), лежат и уходят в землю;
    ///  • «Bow» — носовая волна: комья каменистой земли брызжут с носа в обе стороны; «Fountain», «Clods», «Grains» — фонтан
    ///    больших комьев, мелких комьев и зерна вверх;
    ///  • «Cracks» — тёмные трещины разбегаются от головы во все стороны; «Dust» — низкий короткий занавес пыли по бокам;
    ///  • «Ripple», «Ripple Grains» — тяжёлый гул: в конусе перед головой плиты дёрна чуть приподнимаются из пола и уходят
    ///    обратно, подскакивает зерно — голова рвёт уже «раскачанную» землю;
    ///  • «Lip Slabs» — на губах траншеи плиты дёрна встают вздыбленными, оседают и к концу уходят в землю; «Patches» —
    ///    пятна разрытой земли вдоль пути; «Grass» — рваная трава летит с губ.
    /// Пока стоит («Still …»: круг лёг, Песочные Часы) — земля у головы «кипит»: куски дёрна и зерно подскакивают и уходят
    /// обратно (вздутие у точки выхода — свой префаб EmergeBulge). «Furrow» — траншея по пройденному пути (сетку пишет вид,
    /// FurrowTrail): губы дёрна вывернуты вверх, внутри тёмная каменистая земля комьями, над самой головой вспучено — тот
    /// же атлас плит (kit.Turf).
    /// </summary>
    private static void SaveMound(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.MoundName);
        var ground = root.transform;
        const string trail = ThicketMasterCombatView.TrailPrefix, still = ThicketMasterCombatView.StillPrefix;
        var grey = new Color(.84f, .82f, .8f);
        var stone = new Color(.8f, .8f, .78f);
        float w = ThicketMasterEarthRules.HeadHalfWidth, ahead = ThicketMasterEarthRules.HeadAhead, behind = ThicketMasterEarthRules.HeadBehind;
        float lip = ThicketMasterEarthRules.FurrowHalfWidth;

        // ---- всегда под землёй: уголь трещин у головы и угольки (голову видно на тёмной арене, без ореолов)
        if (kit.CrackRay != null)
        {
            var glow = CrackRays(root, "Head Glow", kit.CrackRay, kit.GlowFurrow, ThicketMasterEarthRules.MoundHeadGlow, .3f, .42f, .35f, .5f,
                .8f, 1.3f, MoundPoints("ThicketBurrowGlowSpots", 12, 1801, 0f, 0f, .3f, -.1f, .55f, .055f, .06f, 0f, 0f, 0f),
                new Color(Ember.r, Ember.g, Ember.b, .62f), new Color(EmberDeep.r, EmberDeep.g, EmberDeep.b, .48f), .12f, .45f, .3f, 2f);
            Stream(glow, 14f, MoundStreamSeconds, ThicketMasterEarthRules.MoundHeadGlow);
        }
        var embers = Embers(root, kit, "Head Embers", ThicketMasterEarthRules.MoundHeadEmbers, Vector3.up * .15f, .45f, 1.4f, 2.8f, .7f,
            new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        Stream(embers, 7f, MoundStreamSeconds, ThicketMasterEarthRules.MoundHeadEmbers); World(embers);

        // ---- на ходу: из пола у головы вырываются плиты дёрна и камни — падают туда, где встали, лежат, уходят в землю.
        // Места — у самого пола, над плоскостью столкновения (из-под неё частица не выходит): плита растёт из земли за первые
        // 6 % жизни (OutOfGround), низ её — под полом, а не появляется в воздухе. Наклон наружу 0,6 · BurstLean: с разбросом
        // ±15 % и наклоном назад 0,06 горизонталь к вертикали ≤ 0,3 — не больше BurstLean (снос ≤ BurstDrift).
        float b0 = ThicketMasterEarthRules.BurstSpeedMin, b1 = ThicketMasterEarthRules.BurstSpeedMax, bg = ThicketMasterEarthRules.BurstGravity;
        float l0 = ThicketMasterEarthRules.BurstLifeMin, l1 = ThicketMasterEarthRules.BurstLifeMax;
        float lean = .6f * ThicketMasterEarthRules.BurstLean;
        var burst = EarthChunks(root, ground, trail + "Burst Slabs", kit.Slabs, kit.Turf, ThicketMasterEarthRules.MoundBurstSlabs, b0, b1, .55f, 1.05f, bg,
            l0, l1, 5.5f, .35f, .12f, .3f, Color.white, grey);
        SlabSize(burst, .55f, 1.05f, .5f, .85f);
        FromPoints(burst, MoundPoints("ThicketBurrowBurstSpots", 24, 1811, 0f, 0f, .9f * w, -behind, .85f * ahead, .01f, .05f, lean, 1f, .06f, .15f),
            false, 0f);
        Shuffle(burst);
        OutOfGround(burst);
        Stream(burst, PerMetre(3f), MoundStreamSeconds, ThicketMasterEarthRules.MoundBurstSlabs); World(burst);
        var big = EarthChunks(root, ground, trail + "Big Slabs", kit.Slabs, kit.Turf, ThicketMasterEarthRules.MoundBigSlabs, b0 * .85f, b1 * .85f, 1.1f, 1.45f,
            bg, l0 + .2f, l1 + .2f, 3.5f, .4f, .1f, .3f, Color.white, grey);
        SlabSize(big, 1.1f, 1.45f, .6f, .9f);
        FromPoints(big, MoundPoints("ThicketBurrowBigSlabSpots", 8, 1812, 0f, .2f * w, .75f * w, -.6f * behind, .5f * ahead, .01f, .05f, lean, 1f, .04f, .15f),
            false, 0f);
        Shuffle(big);
        OutOfGround(big);
        Stream(big, PerMetre(.4f), MoundStreamSeconds, ThicketMasterEarthRules.MoundBigSlabs); World(big);
        var rockSpots = MoundPoints("ThicketBurrowRockSpots", 16, 1813, 0f, .1f * w, .85f * w, -behind, ahead, .02f, .06f, .3f, 1f, .1f);
        var rocksA = EarthChunks(root, ground, trail + "Rocks A", new[] { kit.RockMeshA ?? kit.Pebbles[0] }, kit.RockA, ThicketMasterEarthRules.MoundRocksA,
            3f, 5f, .14f, .38f, 1.9f, 1.3f, l1, 8f, .45f, .25f, .45f, Color.white, stone);
        FromPoints(rocksA, rockSpots, false, .15f);
        Shuffle(rocksA);
        OutOfGround(rocksA);
        Stream(rocksA, PerMetre(1f), MoundStreamSeconds, ThicketMasterEarthRules.MoundRocksA); World(rocksA);
        var rocksB = EarthChunks(root, ground, trail + "Rocks B", new[] { kit.RockMeshB ?? kit.Pebbles[0] }, kit.RockB, ThicketMasterEarthRules.MoundRocksB,
            3f, 5f, .14f, .34f, 1.9f, 1.3f, l1, 8f, .45f, .25f, .45f, Color.white, stone);
        FromPoints(rocksB, rockSpots, false, .15f);
        Shuffle(rocksB);
        OutOfGround(rocksB);
        Stream(rocksB, PerMetre(.55f), MoundStreamSeconds, ThicketMasterEarthRules.MoundRocksB); World(rocksB);

        // ---- фонтан и носовая волна: большие комья и мелкие — вверх, с носа — в обе стороны; зерно
        var fountain = EarthChunks(root, ground, trail + "Fountain", kit.BigClods, kit.StonySoil, ThicketMasterEarthRules.MoundFountain, 4.5f, 6.5f,
            .16f, .3f, 1.8f, 1.1f, 1.4f, 7f, .4f, .2f, .4f, GroundDry, GroundDamp);
        FromPoints(fountain, MoundPoints("ThicketBurrowFountainSpots", 12, 1814, 0f, 0f, .4f, -.15f, .45f, .02f, .08f, .22f, 1f, .1f), false, .1f);
        Shuffle(fountain);
        WorldMeshes(fountain);
        Stream(fountain, PerMetre(.8f), MoundStreamSeconds, ThicketMasterEarthRules.MoundFountain); World(fountain);
        var clods = StonyClods(root, ground, kit, trail + "Clods", ThicketMasterEarthRules.MoundClods,
            MoundPoints("ThicketBurrowClodSpots", 24, 1815, 0f, 0f, .55f * w, -.3f, .6f, 0f, .1f, .4f, 1f, .1f), 3.5f, 6f, .05f, .13f, 1.6f, 0f);
        var clodsMain = clods.main; clodsMain.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.1f);
        WorldMeshes(clods);
        Stream(clods, PerMetre(4.5f), MoundStreamSeconds, ThicketMasterEarthRules.MoundClods); World(clods);
        var bow = StonyClods(root, ground, kit, trail + "Bow", ThicketMasterEarthRules.MoundBow,
            MoundPoints("ThicketBurrowBowSpots", 24, 1816, 0f, .2f, .7f, .25f, .85f, .02f, .1f, 1f, .8f, .25f), 3.2f, 5.4f, .05f, .14f, 1.6f, 0f);
        var bowMain = bow.main; bowMain.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.1f);
        WorldMeshes(bow);
        Stream(bow, PerMetre(5f), MoundStreamSeconds, ThicketMasterEarthRules.MoundBow); World(bow);
        var grains = EarthGrains(root, ground, kit, trail + "Grains", ThicketMasterEarthRules.MoundGrains, 2.5f, 5.5f, 0f);
        FromPoints(grains, MoundPoints("ThicketBurrowGrainSpots", 24, 1817, 0f, 0f, .8f, -.3f, .8f, .02f, .1f, .6f, 1f, .15f), false, .35f);
        Shuffle(grains);
        Stream(grains, PerMetre(7f), MoundStreamSeconds, ThicketMasterEarthRules.MoundGrains); World(grains);

        // ---- трещины разбегаются от головы во все стороны (тёмные, Crack5 лучами); низкий короткий занавес пыли по бокам
        if (kit.CrackRay != null)
        {
            var cracks = CrackRays(root, trail + "Cracks", kit.CrackRay, kit.Furrow, ThicketMasterEarthRules.MoundCracks, 1.1f, 1.5f, .45f, .75f,
                1.5f, 2.5f, MoundPoints("ThicketBurrowCrackSpots", 12, 1821, 0f, .1f, .45f, -.25f, .35f, .04f, .045f, 0f, 0f, 0f),
                CrackTone, new Color(CrackTone.r, CrackTone.g, CrackTone.b, .8f), .04f, .55f, .2f, 3f);
            Stream(cracks, PerMetre(1.1f), MoundStreamSeconds, ThicketMasterEarthRules.MoundCracks);
        }
        var dust = EarthDust(root, kit, trail + "Dust", ThicketMasterEarthRules.MoundDust,
            MoundPoints("ThicketBurrowDustSpots", 12, 1818, 0f, .85f * w, 1.25f * w, -.7f, .4f, .12f, .25f, 1f, .25f, .2f),
            .6f, 1.4f, .7f, 1.1f, .45f, .6f, .48f, 0f);
        Stream(dust, PerMetre(1.6f), MoundStreamSeconds, ThicketMasterEarthRules.MoundDust); World(dust);

        // ---- тяжёлый гул впереди: плиты дёрна в конусе перед головой чуть приподнимаются из пола и уходят обратно (без
        // столкновения — сами прячутся под пол), подскакивает зерно.
        var ripple = EarthChunks(root, null, trail + "Ripple", kit.Slabs, kit.Turf, ThicketMasterEarthRules.MoundRipple,
            ThicketMasterEarthRules.RippleSpeedMin, ThicketMasterEarthRules.RippleSpeedMax, .35f, .6f, ThicketMasterEarthRules.RippleGravity,
            ThicketMasterEarthRules.RippleLifeMin, ThicketMasterEarthRules.RippleLifeMax, 0f, .5f, 0f, .3f, Color.white, grey);
        SlabSize(ripple, .35f, .6f, .45f, .7f);
        float depth = ThicketMasterEarthRules.RippleDepth;
        FromPoints(ripple, RipplePoints("ThicketBurrowRippleSpots", 16, 1819, -depth - .01f, -depth + .01f), false, 0f);
        Shuffle(ripple);
        Upright(ripple, .12f);
        var rippleSize = ripple.sizeOverLifetime; rippleSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, .6f));
        WorldMeshes(ripple);
        Stream(ripple, PerMetre(2.2f), MoundStreamSeconds, ThicketMasterEarthRules.MoundRipple); World(ripple);
        var hop = EarthGrains(root, ground, kit, trail + "Ripple Grains", ThicketMasterEarthRules.MoundRippleGrains, .8f, 1.8f, 0f);
        FromPoints(hop, RipplePoints("ThicketBurrowRippleGrainSpots", 16, 1820, .02f, .04f), false, .2f);
        Shuffle(hop);
        Stream(hop, PerMetre(2f), MoundStreamSeconds, ThicketMasterEarthRules.MoundRippleGrains); World(hop);

        // ---- губы траншеи: плиты дёрна встают из-под губы вздыбленными (наклон в осях мира, разворот наугад), оседают
        // (наклон уходит на ~0,2 рад) и к концу жизни уходят в землю — лежат на месте, пока голова уходит дальше.
        var lips = EarthChunks(root, null, trail + "Lip Slabs", kit.Slabs, kit.Turf, ThicketMasterEarthRules.MoundLipSlabs, 0f, 0f, .5f, .9f, 0f,
            ThicketMasterEarthRules.LipSlabLifeMin, ThicketMasterEarthRules.LipSlabLifeMax, 0f, .5f, 0f, .3f, Color.white, grey);
        SlabSize(lips, .5f, .9f, .5f, .8f);
        FromPoints(lips, MoundPoints("ThicketBurrowLipSpots", 16, 1822, 0f, .55f * lip, .95f * lip, -.9f, -.15f, -.1f, -.03f, 0f, 1f, 0f), false, 0f);
        Shuffle(lips);
        var lipsMain = lips.main;
        lipsMain.startRotation3D = true;
        lipsMain.startRotationX = new ParticleSystem.MinMaxCurve(ThicketMasterEarthRules.LipSlabTiltMin, ThicketMasterEarthRules.LipSlabTiltMax);
        lipsMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        lipsMain.startRotationZ = new ParticleSystem.MinMaxCurve(-.2f, .2f);
        var lipsRise = lips.velocityOverLifetime; lipsRise.enabled = true;
        lipsRise.space = ParticleSystemSimulationSpace.World;
        // Оси скорости — в одном режиме кривых (Unity требует один режим на x/y/z).
        lipsRise.x = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        lipsRise.z = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        lipsRise.y = new ParticleSystem.MinMaxCurve(1.1f, Curve(0f, 1f, .12f, 0f, .75f, 0f, 1f, -.35f));
        var lipsSettle = lips.rotationOverLifetime;
        lipsSettle.x = new ParticleSystem.MinMaxCurve(-.5f, Curve(0f, 0f, .15f, 0f, .45f, .6f, .7f, 0f, 1f, 0f), Curve(0f, 0f, .15f, 0f, .45f, 1f, .7f, 0f, 1f, 0f));
        var lipsSize = lips.sizeOverLifetime; lipsSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .1f, 1f, .82f, 1f, 1f, 0f));
        WorldMeshes(lips);
        Stream(lips, PerMetre(2f), MoundStreamSeconds, ThicketMasterEarthRules.MoundLipSlabs); World(lips);

        // Пятна разрытой земли вдоль пути (атлас dirt_0–5) — под траншеей и по её губам; тают вместе с ней.
        var patches = GroundPatches(root, trail + "Patches", kit.DirtPatch, ThicketMasterEarthRules.MoundPatches,
            MoundPoints("ThicketBurrowPatchSpots", 12, 1823, 0f, 0f, .5f, -.9f, -.1f, .035f, .045f, 0f, 1f, 0f),
            2.2f, 3.2f, ThicketMasterEarthRules.FurrowLife, .05f, .55f, .7f, .15f, Color.white, new Color(.82f, .8f, .78f), 0f, 3, 2, 5f);
        Stream(patches, PerMetre(1.2f), MoundStreamSeconds, ThicketMasterEarthRules.MoundPatches); World(patches);

        // Рваная трава: пучки дёрна со светом летят с губ вбок-вверх, кувыркаются и ложатся по бокам траншеи.
        var grass = Clods(root, ground, kit, trail + "Grass", ThicketMasterEarthRules.MoundGrass, Vector3.zero, Vector3.up, 0f, .1f, 1.6f, 3.2f,
            .18f, .3f, 1.2f, 0f);
        FromPoints(grass, MoundPoints("ThicketBurrowGrassSpots", 16, 1824, 0f, .75f * lip, lip, -.4f, .3f, .02f, .06f, 1f, .45f, .3f), false, .2f);
        Shuffle(grass);
        var grassMain = grass.main;
        grassMain.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.15f);
        grassMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.52f, .70f, .26f), new Color(.36f, .52f, .17f));
        var grassSpin = grass.rotationOverLifetime;
        grassSpin.x = new ParticleSystem.MinMaxCurve(-6f, 6f); grassSpin.y = new ParticleSystem.MinMaxCurve(-3f, 3f); grassSpin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var grassRenderer = grass.GetComponent<ParticleSystemRenderer>();
        if (kit.KnollTuft != null)
        {
            grassRenderer.SetMeshes(new[] { kit.KnollTuft });
            grassRenderer.sharedMaterial = kit.KnollGreen;
            grassRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        WorldMeshes(grass);
        Stream(grass, PerMetre(1f), MoundStreamSeconds, ThicketMasterEarthRules.MoundGrass); World(grass);

        // ---- пока стоит (круг лёг, Часы): земля у головы «кипит» — куски дёрна и зерно подскакивают и уходят обратно
        var boil = EarthChunks(root, null, still + "Boil", kit.Slabs, kit.Turf, ThicketMasterEarthRules.MoundBoil, 1.3f, 2.4f, .4f, .75f, 1.4f,
            .4f, .55f, 0f, .5f, 0f, .3f, Color.white, grey);
        SlabSize(boil, .4f, .75f, .45f, .75f);
        FromPoints(boil, MoundPoints("ThicketBurrowBoilSpots", 12, 1825, 0f, 0f, .9f, -.7f, .7f, -.12f, -.06f, .15f, 1f, 0f), false, 0f);
        Shuffle(boil);
        Upright(boil, .25f);
        var boilSize = boil.sizeOverLifetime; boilSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, .85f, 1f, 1f, .6f));
        WorldMeshes(boil);
        Stream(boil, 11f, MoundStreamSeconds, ThicketMasterEarthRules.MoundBoil); World(boil);
        var boilGrains = EarthGrains(root, ground, kit, still + "Grains", ThicketMasterEarthRules.MoundBoilGrains, 1f, 2.2f, 0f);
        FromPoints(boilGrains, MoundPoints("ThicketBurrowBoilGrainSpots", 16, 1826, 0f, 0f, 1.1f, -.9f, .9f, .02f, .06f, .3f, 1f, 0f), false, .3f);
        Shuffle(boilGrains);
        Stream(boilGrains, 15f, MoundStreamSeconds, ThicketMasterEarthRules.MoundBoilGrains); World(boilGrains);

        // Траншея (V11, V15, V17 — рваная): одна сплошная лента по пройденному пути — сетку пишет вид (FurrowTrail) на этом
        // ребёнке, в мире: губы дёрна вывернуты вверх, внутри тёмная каменистая земля комьями (атлас плит), над головой
        // вспучено; осыпается за FurrowLife.
        var furrow = Child(root, ThicketMasterCombatView.FurrowChild, Vector3.zero);
        furrow.AddComponent<MeshFilter>();
        var furrowRenderer = furrow.AddComponent<MeshRenderer>();
        furrowRenderer.sharedMaterial = kit.Turf != null ? kit.Turf : kit.Ground;
        furrowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        furrowRenderer.receiveShadows = true;
        furrowRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        furrowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Save(root);
    }

    /// <summary>
    /// Меш-частицы бугра — в осях мира (V17): корень едет и поворачивается за бугром, а лёгшие плиты, камни и трещины не
    /// должны поворачиваться вместе с ним (при «Local» меш частицы следует повороту системы).
    /// </summary>
    private static void WorldMeshes(ParticleSystem particles)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.alignment = ParticleSystemRenderSpace.World;
    }

    /// <summary>
    /// Обломок выходит из земли (V17): место у самого пола (над плоскостью столкновения), размер с 0,5 до полного за
    /// первые 6 % жизни, пока он уже летит вверх, — низ плиты под полом, она рвётся из земли, а не возникает в воздухе;
    /// к концу жизни уходит (размер к нулю за последние ~18 %). Меш — в осях мира.
    /// </summary>
    private static void OutOfGround(ParticleSystem particles)
    {
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .06f, 1f, .82f, 1f, 1f, 0f));
        WorldMeshes(particles);
    }

    /// <summary>
    /// Лучи трещин на земле (V17): меш ray (луч от нуля вдоль +Z, ThicketCrackRay) по местам points — основание луча у
    /// места, поворот вокруг вертикали наугад (в осях мира: трещина лежит, как легла, когда бугор сворачивает); ширина
    /// widthMin…widthMax, длина lengthMin…lengthMax — раскрывается от open за 0,25 жизни (трещина бежит от головы), ширина
    /// — от 0,6 за 0,2; проявляется за fadeIn, тает с fadeOut; частицы в мире.
    /// </summary>
    private static ParticleSystem CrackRays(GameObject host, string name, Mesh ray, Material material, int count, float lifeMin, float lifeMax,
        float widthMin, float widthMax, float lengthMin, float lengthMax, Mesh points, Color a, Color b, float fadeIn, float fadeOut, float open,
        float sortFudge)
    {
        var rays = Particles(host, name, count, lifeMin, lifeMax, 0f, 0f, 1f, 1f, 0f);
        var main = rays.main;
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(widthMin, widthMax);
        main.startSizeY = 1f;
        main.startSizeZ = new ParticleSystem.MinMaxCurve(lengthMin, lengthMax);
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = 0f;
        FromPoints(rays, points, false, 0f);
        Shuffle(rays);
        var size = rays.sizeOverLifetime; size.enabled = true;
        size.separateAxes = true;
        size.x = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, .2f, 1f, 1f, 1f));
        size.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, 1f));
        size.z = new ParticleSystem.MinMaxCurve(1f, Curve(0f, open, .25f, 1f, 1f, 1.04f));
        var fade = rays.colorOverLifetime; fade.enabled = true;
        fade.color = Alpha(fadeIn, fadeOut);
        var renderer = rays.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = ray;
        renderer.alignment = ParticleSystemRenderSpace.World;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = sortFudge;
        World(rays);
        return rays;
    }

    /// <summary>
    /// Меш-частицы земли: бугристые горбы/комья вразброс, непрозрачная kit.Earth, свет и тени на них. Отбрасывание тени
    /// включено, но у URP Particles/Lit (kit.Earth, kit.Ground) нет прохода ShadowCaster — тень появится, только если
    /// материал сменить на шейдер с ним.
    /// </summary>
    private static void EarthMeshes(ParticleSystem particles, Kit kit, Mesh[] meshes, ParticleSystemRenderSpace alignment)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (meshes != null && meshes.Length > 0 && meshes[0] != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.SetMeshes(AtMostFourMeshes(meshes));
            renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
        }
        else if (kit.HalfSphere != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = kit.HalfSphere;
        }
        renderer.alignment = alignment;
        renderer.sharedMaterial = kit.Earth;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    /// <summary>Круг выхода лёг: дрожь — клубы внутри круга (всё чаще), подпрыгивают камешки, растёт сеть трещин.</summary>
    private static void SaveDiveTremor(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DiveTremorName);
        float r = Simulation.ThicketDiveRadius.ToFloat();
        float windup = Simulation.ThicketDiveLockTicks / (float)Simulation.TicksPerSecond;
        var puffs = Dust(root, kit, "Puffs", 12, Vector3.zero, 85f, r * .8f, .2f, .6f, .6f, 1.0f, .5f, .8f, .22f, 0f, false);
        Stream(puffs, 14f, windup, 12);
        var puffRate = puffs.emission; puffRate.rateOverTime = new ParticleSystem.MinMaxCurve(14f, Curve(0f, .15f, 1f, 1f));
        var pebbles = Debris(root, root.transform, "Pebbles", kit.Clod, 14, Vector3.up * .05f, Vector3.up, 10f, r * .75f, .8f, 1.6f, .05f, .10f, 1.6f, 0f, RockLight, RockDark);
        Stream(pebbles, 15f, windup, 14);
        var cracks = Decal(root, "Cracks", kit.Crack, 2f * r * .6f, 2f * r * .62f, windup + .4f, .8f, CrackTone, 0f, .04f);
        var grow = cracks.sizeOverLifetime;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .75f, 1f, 1f, 1f));
        // Трещины разгораются к удару: под кругом уже светится то, что сейчас вылезет.
        var glow = Decal(root, "Cracks Glow", kit.GlowCrack, 2f * r * .58f, 2f * r * .6f, windup + .3f, .85f, new Color(Ember.r, Ember.g, Ember.b, .9f), 0f, .045f);
        var glowGrow = glow.sizeOverLifetime;
        glowGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .15f, .8f, 1f, 1f, 1f));
        var glowFade = glow.colorOverLifetime;
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.35f, .4f), new GradientAlphaKey(1f, .85f), new GradientAlphaKey(0f, 1f) });
        glowFade.color = ramp;
        var sparks = Embers(root, kit, "Embers", 16, Vector3.up * .1f, r * .7f, 1.2f, 2.6f, .7f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        Stream(sparks, 12f, windup, 16);
        Save(root);
    }

    /// <summary>
    /// Вздутие у точки выхода (V14): круг лёг — у точки выхода (a.Origin, где уже стоит голова бугра) земля пухнет все
    /// ThicketDiveLockTicks до удара: плиты дёрна кольцом 1,75–2,5 м поднимаются из пола на 0,1–0,25 м и клонятся наружу,
    /// под ними растёт тёмная звезда трещин (Crater43), сеть Crack4 разгорается углём к удару, всё чаще подскакивают
    /// зерно и камешки поляны, у кромки — редкая низкая пыль. Корень — точка выхода, срок — от тика круга (вид держит
    /// его по ImpactTick: Часы сдвигают вместе с телом); на выходе вид снимает вздутие — его место занимает выброс.
    /// </summary>
    private static void SaveEmergeBulge(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.EmergeBulgeName);
        var ground = root.transform;
        float swell = ThicketMasterEarthRules.BulgeSeconds, life = swell + .3f, at = swell / life;
        GroundDecal(root, "Cracks", kit.CrackRadial, 2f * 2.7f * Root2, life, 0f, .9f, CrackTone, 0f, .05f, .2f, at * .95f, 0f);
        var glow = GroundDecal(root, "Cracks Glow", kit.GlowCrack, 2f * 2.2f * Root2, life, 0f, .9f, new Color(Ember.r, Ember.g, Ember.b, .9f), 0f,
            .055f, .15f, at * .95f, .6f);
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.3f, at * .45f), new GradientAlphaKey(1f, at), new GradientAlphaKey(0f, 1f) });
        var glowFade = glow.colorOverLifetime; glowFade.color = ramp;

        // Плиты дёрна поднимаются из пола и клонятся наружу (кромка к точке выхода встаёт): земля пухнет.
        var slabs = EarthChunks(root, null, "Slabs", kit.Slabs, kit.Turf, ThicketMasterEarthRules.BulgeSlabs, 0f, 0f, .5f, .9f, 0f, life, life,
            0f, .5f, 0f, .3f, Color.white, new Color(.84f, .82f, .8f));
        SlabSize(slabs, .5f, .9f, .5f, .8f);
        FromPoints(slabs, AroundPoints("ThicketBulgeSlabSpots", ThicketMasterEarthRules.BulgeSlabs, 1501, 1.75f, 2.5f, -.12f, 0f, 0f), true, 0f);
        var slabsMain = slabs.main;
        slabsMain.startRotationX = 0f; slabsMain.startRotationY = 0f;
        slabsMain.startRotationZ = new ParticleSystem.MinMaxCurve(-.15f, .15f);
        var tilt = slabs.rotationOverLifetime;
        tilt.x = new ParticleSystem.MinMaxCurve(.5f * DiveSlabPitchSign, Curve(0f, .6f, at * .9f, .3f, 1f, 0f), Curve(0f, 1f, at * .9f, .5f, 1f, 0f));
        tilt.y = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f), Curve(0f, 0f, 1f, 0f));
        tilt.z = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f), Curve(0f, 0f, 1f, 0f));
        var rise = slabs.velocityOverLifetime; rise.enabled = true;
        rise.space = ParticleSystemSimulationSpace.Local;
        rise.x = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        rise.z = new ParticleSystem.MinMaxCurve(0f, Curve(0f, 0f, 1f, 0f));
        rise.y = new ParticleSystem.MinMaxCurve(.42f, Curve(0f, 1f, at * .65f, .25f, at, 0f, 1f, 0f));
        var grow = slabs.sizeOverLifetime; grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .85f, at * .5f, 1f, 1f, 1f));

        // Зерно и камешки подскакивают всё чаще к удару; редкая низкая пыль у кромки.
        var grains = EarthGrains(root, ground, kit, "Grains", ThicketMasterEarthRules.BulgeGrains, 1f, 2.4f, 0f);
        FromPoints(grains, AroundPoints("ThicketBulgeGrainSpots", 16, 1511, .5f, 2.2f, .05f, 70f, 88f), false, .3f);
        Shuffle(grains);
        Stream(grains, ThicketMasterEarthRules.BulgeGrains / swell, swell, ThicketMasterEarthRules.BulgeGrains);
        var grainsRate = grains.emission;
        grainsRate.rateOverTime = new ParticleSystem.MinMaxCurve(ThicketMasterEarthRules.BulgeGrains * 1.7f / swell, Curve(0f, .15f, 1f, 1f));
        var pebbles = EarthChunks(root, ground, "Pebbles", new[] { kit.RockMeshA ?? kit.Pebbles[0] }, kit.RockA, ThicketMasterEarthRules.BulgePebbles,
            1.2f, 2.4f, .08f, .14f, 1.6f, .6f, .9f, 8f, .6f, .3f, .45f, Color.white, new Color(.8f, .8f, .78f));
        FromPoints(pebbles, AroundPoints("ThicketBulgePebbleSpots", 8, 1521, .6f, 2f, .06f, 70f, 88f), false, .2f);
        Shuffle(pebbles);
        Stream(pebbles, ThicketMasterEarthRules.BulgePebbles / swell, swell, ThicketMasterEarthRules.BulgePebbles);
        var pebblesRate = pebbles.emission;
        pebblesRate.rateOverTime = new ParticleSystem.MinMaxCurve(ThicketMasterEarthRules.BulgePebbles * 1.7f / swell, Curve(0f, .15f, 1f, 1f));
        var dust = EarthDust(root, kit, "Dust", 6, AroundPoints("ThicketBulgeDustSpots", 8, 1531, 1.8f, 2.6f, .12f, 5f, 20f), .3f, .8f, .5f, .8f,
            .45f, .6f, .3f, swell * .35f);
        Stream(dust, 6f / (swell * .6f), swell * .6f, 6);
        Save(root);
    }

    /// <summary>
    /// Выход из-под земли (V14, владелец 04.10: «мощнейший эффект… земля фактурная»). Корень — точка выхода, +Z — взгляд;
    /// сроки — ThicketMasterEarthRules. За EruptSeconds из-под тела вдоль его длины (гуще к носу и хвосту) рвутся плиты
    /// дёрна 0,45–0,85 м и три большие 1–1,3 м (Cobble01–06: дёрн пола сверху, земля снизу), камни поляны 0,15–0,42 м
    /// (MeadowPebble0/1), фонтан комьев каменистой земли (ClodMesh, NoiseSphere1) и зерно Debris2 — кувыркаются, падают,
    /// отскакивают, ложатся щебнем и к 2,2–2,8 с уходят в землю; по полу до края круга бежит низкая ударная волна пыли
    /// SmokeAnim2 (≤ 0,85 с, верх ≤ 1 м), у контура — низкие клубы первые 0,36 с; трава рвётся наружу. Кратер: пятно сырой
    /// земли с травяной кромкой, рваные пятна разрытой земли у края, звезда трещин Crater43 и тонкие Crack6, крошка
    /// Crater18 до края круга, уголь в середине 0,6 с. Принятый отклик — вспышка, тонкая кромка до круга, шесть корней,
    /// листья, щепки, угольки; новый — короткий янтарный свет из ямы (обломки ловят его на тёмной арене). Вала LipMesh,
    /// пары Crater40/Crater19, белых многоугольников CFXR и сплошного кольца клубов больше нет.
    /// </summary>
    private static void SaveEmerge(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.EmergeName);
        var ground = root.transform;
        float r = Simulation.ThicketDiveRadius.ToFloat();
        float erupt = ThicketMasterEarthRules.EruptSeconds, life = ThicketMasterEarthRules.EmergeLife;
        var grey = new Color(.84f, .82f, .8f);
        var stone = new Color(.8f, .8f, .78f);

        // Отклик удара (принят): вспышка, тонкая кромка до края круга, свет из ямы.
        Wave(root, "Ring", kit.GlowRingThin, 2f * r * Root2, .7f, .3f, .4f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        Flash(root, kit, "Flash", Vector3.up * .8f, 1.2f * BS, .1f, new Color(Ember.r, Ember.g, Ember.b, .5f), 0f, false);
        var lamp = Child(root, ThicketMasterCombatView.EruptLightChild, new Vector3(0f, EruptLightHeight, BodyShift));
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = EruptLightColor;
        light.range = EruptLightRange;
        light.intensity = EruptLightPeak;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.Auto;

        // Кратер.
        GroundPatches(root, "Worn Patch", kit.WornPatch, 1, null, 13.5f, 13.5f, life, .035f, .73f, .7f, .05f, Color.white, new Color(.92f, .9f, .88f),
            0f, 1, 1, 6f).transform.localPosition = new Vector3(0f, .025f, BodyShift);
        GroundPatches(root, "Dirt Patches", kit.DirtPatch, ThicketMasterEarthRules.EmergePatches,
            ContourPoints("ThicketEmergePatchSpots", ThicketMasterEarthRules.EmergePatches, 1601, .9f, 1.18f, .035f, .045f, false, 0f, 0f),
            2.8f, 4f, life * .97f, .03f, .72f, .45f, .06f, Color.white, grey, .12f, 3, 2, 5f);
        GroundDecal(root, "Cracks", kit.CrackRadial, 2f * BodyHalfLength * 1.25f * Root2, life, 0f, .7f, CrackTone, 0f, .05f, .7f, .05f, 0f);
        GroundDecal(root, "Cracks Fine", kit.CrackFine, 2f * BodyHalfLength * 1.4f * Root2, life, 0f, .68f,
            new Color(CrackTone.r, CrackTone.g, CrackTone.b, .8f), 0f, .055f, .75f, .06f, 1.1f);
        GroundDecal(root, "Rubble", kit.Rubble, 2f * r * 1.05f * Root2, life - .2f, .05f, .7f, new Color(SoilDark.r, SoilDark.g, SoilDark.b, .85f),
            .2f, .045f, .85f, .1f, .4f);
        GroundDecal(root, "Crater Glow", kit.GlowCrack, 2f * 1.9f * Root2, .6f, 0f, .35f, new Color(Ember.r, Ember.g, Ember.b, .8f), 0f, .06f, .6f, .15f, 2.2f);

        // Плиты дёрна вдоль тела (и три большие) — вверх и наружу, кувырок гаснет, когда легли; лежат щебнем и уходят.
        int slabCount = ThicketMasterEarthRules.EmergeSlabs - 3;
        var slabs = EarthChunks(root, ground, "Slabs", kit.Slabs, kit.Turf, slabCount, 5f, 8.5f, .45f, .85f, 2f,
            ThicketMasterEarthRules.RubbleLifeMin, ThicketMasterEarthRules.RubbleLifeMax, 6f, .3f, .15f, .3f, Color.white, grey);
        SlabSize(slabs, .45f, .85f, .45f, .8f);
        FromPoints(slabs, AlongBodyPoints("ThicketEmergeSlabSpots", 24, 1611, .5f, 1f, .05f, .2f, 55f, 80f), false, .15f);
        Shuffle(slabs);
        Stream(slabs, slabCount / .2f, .2f, slabCount);
        var bigSlabs = EarthChunks(root, ground, "Slabs Big", kit.Slabs, kit.Turf, 3, 4f, 6f, 1f, 1.3f, 2.1f,
            ThicketMasterEarthRules.RubbleLifeMin, ThicketMasterEarthRules.RubbleLifeMax, 4f, .3f, .12f, .3f, Color.white, grey);
        SlabSize(bigSlabs, 1f, 1.3f, .6f, .9f);
        FromPoints(bigSlabs, AlongBodyPoints("ThicketEmergeBigSlabSpots", 3, 1612, .55f, .9f, .1f, .2f, 60f, 75f), false, .1f);
        Stream(bigSlabs, 3f / .12f, .12f, 3);
        var bigSlabsMain = bigSlabs.main; bigSlabsMain.startDelay = .02f;

        // Камни поляны и комья каменистой земли — фонтаном вдоль тела; зерно.
        var rockA = EarthChunks(root, ground, "Rocks A", new[] { kit.RockMeshA ?? kit.Pebbles[0] }, kit.RockA, ThicketMasterEarthRules.EmergeRocksA,
            4f, 8f, .15f, .42f, 2f, 2f, 2.8f, 8f, .3f, .25f, .45f, Color.white, stone);
        FromPoints(rockA, AlongBodyPoints("ThicketEmergeRockSpots", 18, 1621, .3f, .95f, .1f, .25f, 55f, 82f), false, .2f);
        Shuffle(rockA);
        Stream(rockA, ThicketMasterEarthRules.EmergeRocksA / (erupt * .6f), erupt * .6f, ThicketMasterEarthRules.EmergeRocksA);
        var rockB = EarthChunks(root, ground, "Rocks B", new[] { kit.RockMeshB ?? kit.Pebbles[0] }, kit.RockB, ThicketMasterEarthRules.EmergeRocksB,
            4f, 8f, .15f, .38f, 2f, 2f, 2.8f, 8f, .3f, .25f, .45f, Color.white, stone);
        FromPoints(rockB, AlongBodyPoints("ThicketEmergeRockSpots", 18, 1621, .3f, .95f, .1f, .25f, 55f, 82f), false, .2f);
        Shuffle(rockB);
        Stream(rockB, ThicketMasterEarthRules.EmergeRocksB / (erupt * .6f), erupt * .6f, ThicketMasterEarthRules.EmergeRocksB);
        var clods = StonyClods(root, ground, kit, "Clods", ThicketMasterEarthRules.EmergeClods,
            AlongBodyPoints("ThicketEmergeClodSpots", 72, 1631, .3f, 1f, .1f, .3f, 60f, 85f), 4f, 8f, .05f, .2f, 1.6f, 0f);
        Stream(clods, ThicketMasterEarthRules.EmergeClods / (erupt * .8f), erupt * .8f, ThicketMasterEarthRules.EmergeClods);
        var big = EarthChunks(root, ground, "Big Clods", kit.BigClods, kit.StonySoil, ThicketMasterEarthRules.EmergeBigClods, 3.5f, 6.5f, .22f, .38f, 1.9f,
            1.6f, 2.4f, 7f, .35f, .2f, .4f, GroundDry, GroundDamp);
        FromPoints(big, AlongBodyPoints("ThicketEmergeBigSpots", 12, 1641, .35f, .9f, .1f, .25f, 60f, 82f), false, .2f);
        Shuffle(big);
        Stream(big, ThicketMasterEarthRules.EmergeBigClods / (erupt * .6f), erupt * .6f, ThicketMasterEarthRules.EmergeBigClods);
        var grains = EarthGrains(root, ground, kit, "Grains", ThicketMasterEarthRules.EmergeGrains, 3.5f, 7f, 0f);
        FromPoints(grains, AlongBodyPoints("ThicketEmergeGrainSpots", 56, 1651, .5f, 1.05f, .05f, .25f, 45f, 85f), false, .4f);
        Shuffle(grains);
        Stream(grains, ThicketMasterEarthRules.EmergeGrains / erupt, erupt, ThicketMasterEarthRules.EmergeGrains);

        // Пыль: низкая ударная волна до края круга (≤ 0,85 с) и низкие клубы у контура первые EruptSeconds; немного оседает.
        float shockLife = ThicketMasterEarthRules.EmergeShockLife / 1.08f;
        var shock = Wall(root, "Shock", kit.DustFlip, ThicketMasterEarthRules.EmergeShock, .3f, BodyHalfWidth * .9f, r * .97f, .35f, shockLife, .8f, 1.15f,
            new Color(DustDark.r, DustDark.g, DustDark.b, .42f), new Color(SoilLight.r, SoilLight.g, SoilLight.b, .36f), .05f, .03f);
        Flipbook(shock, kit);
        float tau = Mathf.Clamp(.35f / shockLife, .05f, .9f);
        var shockSize = shock.sizeOverLifetime; shockSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, tau, 1f, 1f, 1.1f));
        var low = EarthDust(root, kit, "Low Dust", ThicketMasterEarthRules.EmergeLowDust,
            ContourPoints("ThicketEmergeDustSpots", 20, 1661, .95f, 1.08f, .12f, .28f, false, 5f, 20f), 2f, 4f, .8f, 1.1f, .5f,
            ThicketMasterEarthRules.EmergeLowDustLife, .5f, 0f);
        Stream(low, ThicketMasterEarthRules.EmergeLowDust / erupt, erupt, ThicketMasterEarthRules.EmergeLowDust);
        var settle = EarthDust(root, kit, "Settle", 6, ContourPoints("ThicketEmergeSettleSpots", 8, 1671, 1f, 1.12f, .1f, .2f, false, 5f, 15f),
            .2f, .6f, .5f, .7f, .55f, .75f, .22f, .4f);
        Stream(settle, 6f / .3f, .3f, 6);

        // Трава рвётся наружу; щепки, листья, угольки — меньше прежнего (объём теперь — земля).
        var grass = TornGrass(root, ground, kit, "Torn Grass", ThicketMasterEarthRules.EmergeTufts,
            ContourPoints("ThicketEmergeTuftSpots", ThicketMasterEarthRules.EmergeTufts, 1681, .85f, 1.05f, .03f, .08f, false, 50f, 75f), 2.5f, 5f, 0f);
        Stream(grass, ThicketMasterEarthRules.EmergeTufts / (erupt * .7f), erupt * .7f, ThicketMasterEarthRules.EmergeTufts);
        Debris(root, ground, "Splinters", kit.Splinter, 8, Vector3.up * .1f, Vector3.up, 40f, .6f, 4f, 7f, .10f, .22f, 1.3f, .01f, BarkLight, BarkDark);
        Leaves(root, ground, kit, "Leaves", 10, Vector3.up * .2f, Vector3.up, 55f, 2.5f, 5f, .03f);
        Embers(root, kit, "Embers", 12, Vector3.up * .2f, .8f * BS, 4f, 8f, 1.1f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        for (int i = 0; i < 6; i++)
        {
            float angle = (i + (Hash01(i, 211) - .5f) * .5f) / 6f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float at = Mathf.Lerp(1.0f, 1.6f, Hash01(i, 212)) * BS;
            GrowChild(root, "Корень " + i, kit.Curls[i % kit.Curls.Length], i % 3 == 2 ? kit.WoodDark : kit.Wood, outward * at,
                Quaternion.LookRotation(outward, Vector3.up), Mathf.Lerp(.8f, 1.1f, Hash01(i, 213)) * BS,
                Mathf.RoundToInt(Hash01(i, 214) * 60f), 0);
        }
        Save(root);
    }

    /// <summary>
    /// Свет из ямы на выходе (V14): тёплый точечный, низко над серединой тела, без теней; яркость ведёт вид
    /// (ThicketMasterEarthRules.EruptLight, ~0,38 с). Высоко над полом — пятна-ожога на полу нет; героя не высветляет
    /// (Razlom/Texture Toon берёт только главный свет).
    /// </summary>
    private const float EruptLightHeight = 1.3f, EruptLightRange = 6.5f, EruptLightPeak = 2f;
    private static readonly Color EruptLightColor = new Color(1f, .62f, .3f);

    // ------------------------------------------------------------- prefabs: sprout

    /// <summary>Жест прорастания — лапы в землю: у каждой передней лапы («Left»/«Right», ставит вид) клубы и комья.</summary>
    private static void SaveSproutPress(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutPressName);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var paw = Child(root, side, Vector3.zero);
            Dust(paw, kit, "Dust", 5, Vector3.up * .03f, 80f, .35f, .8f, 1.6f, .7f, 1.1f, .7f, 1.0f, .30f, 0f, true);
            Debris(paw, paw.transform, "Clods", kit.Clod, 2, Vector3.up * .06f, Vector3.up, 40f, .2f, 1.8f, 3f, .1f, .18f, 1.6f, 0f, SoilLight, SoilDark);
        }
        Save(root);
    }

    /// <summary>
    /// Дрожь под кругом прорастания до его удара (30 тиков): клубы внутри круга всё чаще,
    /// камешки, сеть трещин растёт 0,2 → 1; за 6 тиков до удара высовываются три кончика корней.
    /// </summary>
    private static void SaveSproutTremor(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutTremorName);
        float r = Simulation.ThicketSproutRadius.ToFloat();
        float windup = Simulation.ThicketSproutImpactTicks / (float)Simulation.TicksPerSecond;
        var puffs = Dust(root, kit, "Puffs", 8, Vector3.zero, 85f, r * .7f, .2f, .5f, .5f, .8f, .5f, .7f, .22f, 0f, false);
        Stream(puffs, 9f, windup, 8);
        var puffRate = puffs.emission; puffRate.rateOverTime = new ParticleSystem.MinMaxCurve(9f, Curve(0f, .2f, 1f, 1f));
        var pebbles = Debris(root, root.transform, "Pebbles", kit.Clod, 8, Vector3.up * .05f, Vector3.up, 10f, r * .7f, .7f, 1.4f, .05f, .09f, 1.6f, 0f, RockLight, RockDark);
        Stream(pebbles, 9f, windup, 8);
        var cracks = Decal(root, "Cracks", kit.Crack, 2f * r * Root2 * .8f, 2f * r * Root2 * .82f, windup + .2f, .9f, CrackTone, 0f, .04f);
        var grow = cracks.sizeOverLifetime;
        grow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .8f, 1f, 1f, 1f));
        var glow = Decal(root, "Cracks Glow", kit.GlowCrack, 2f * r * Root2 * .78f, 2f * r * Root2 * .8f, windup + .15f, .9f, new Color(Ember.r, Ember.g, Ember.b, .9f), 0f, .045f);
        var glowGrow = glow.sizeOverLifetime;
        glowGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .2f, .8f, 1f, 1f, 1f));
        var glowFade = glow.colorOverLifetime;
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.3f, .35f), new GradientAlphaKey(1f, .85f), new GradientAlphaKey(0f, 1f) });
        glowFade.color = ramp;
        int tipsAt = Mathf.RoundToInt((Simulation.ThicketSproutImpactTicks - 6) * 1000f / Simulation.TicksPerSecond);
        for (int i = 0; i < 3; i++)
        {
            float angle = (i + Hash01(i, 301) * .6f) / 3f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            GrowChild(root, "Кончик " + i, kit.Tips[i % kit.Tips.Length], kit.Wood, outward * r * Mathf.Lerp(.2f, .5f, Hash01(i, 302)),
                Quaternion.LookRotation(outward, Vector3.up), Mathf.Lerp(.5f, .65f, Hash01(i, 303)), tipsAt + i * 30, 0);
        }
        Save(root);
    }

    /// <summary>
    /// Удар круга прорастания: шипы-корни в коре (7 мест: центр и кольцо r·0,3–0,9, наклон наружу),
    /// выходят за 0,1 с, стоят до 0,6 с, уходят к 1,1 с; комья, кольцо пыли, звезда трещин.
    /// </summary>
    private static void SaveSproutSpikes(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.SproutSpikesName);
        var ground = root.transform;
        float r = Simulation.ThicketSproutRadius.ToFloat();
        const float life = 1.6f;
        Spikes(root, "Spikes", kit.Spikes, SpikePoints("ThicketSproutSpikePoints", 7, r, 401), kit.Wood, life, 1.2f, 2.0f, 1.2f, 1.7f,
            Curve(0f, 0f, .01f, 0f, .05f, 1.12f, .07f, 1f, .36f, 1f, .6f, 0f, 1f, 0f),
            Curve(0f, 0f, .03f, 0f, .075f, 1.12f, .095f, 1f, .4f, 1f, .69f, 0f, 1f, 0f));
        Decal(root, "Star", kit.Star, 2f * r * Root2, 2f * r * Root2 * 1.05f, life, .6f, CrackTone, 0f, .04f);
        Decal(root, "Star Glow", kit.GlowStar, 2f * r * Root2 * .95f, 2f * r * Root2, .8f, .2f, new Color(Ember.r, Ember.g, Ember.b, .9f), 0f, .045f);
        Flash(root, kit, "Flash", Vector3.up * .5f, 1.3f * r, .14f, new Color(Ember.r, Ember.g, Ember.b, .5f), 0f, false);
        Embers(root, kit, "Embers", 12, Vector3.up * .15f, r * .5f, 2.5f, 5f, .8f, new Color(Ember.r, Ember.g, Ember.b, 1f), 0f);
        Decal(root, "Soil", kit.Soil, 1.6f * r, 1.7f * r, life, .6f, new Color(SoilDark.r, SoilDark.g, SoilDark.b, .75f), 0f, .03f);
        var clods = Debris(root, ground, "Clods", kit.Clod, 14, Vector3.up * .1f, Vector3.up, 0f, .1f, 3f, 5.5f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        FromPoints(clods, RingPoints("ThicketSproutClodPoints", 14, r * .2f, r * .8f, 20f, 45f, 411), false, .35f);
        var dust = Dust(root, kit, "DustRing", 8, Vector3.up * .03f, 0f, .1f, .6f, 1.4f, 1.0f, 1.5f, .9f, 1.3f, .28f, .02f, true);
        FromPoints(dust, RingPoints("ThicketSproutDustPoints", 8, r * .8f, r * 1.15f, 70f, 85f, 421), false, .3f);
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: pollen

    /// <summary>Крона трясётся: у каждой кроны («Left»/«Right», вид держит их на костях) золотые клубы, искры, листья — в мире.</summary>
    private static void SavePollenShake(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenShakeName);
        float shake = Simulation.ThicketCastGestureTicks / (float)Simulation.TicksPerSecond;
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var gold = Cloud(crown, kit, "Gold", 10, Vector3.zero, 70f, .6f, .3f, .8f, .6f, 1.0f, .9f, 1.3f, 0f, false,
                new Color(PollenGold.r, PollenGold.g, PollenGold.b, .35f), new Color(PollenDeep.r, PollenDeep.g, PollenDeep.b, .30f));
            Stream(gold, 16f, shake, 10); World(gold);
            var goldMain = gold.main; goldMain.gravityModifier = .05f;
            var sparks = Motes(crown, "Sparks", kit.Glow, 15, Vector3.zero, .6f, .4f, 1.2f, .05f, .10f, .8f, 1.2f,
                new Color(PollenGlow.r, PollenGlow.g, PollenGlow.b, .95f), 0f, false);
            Stream(sparks, 25f, shake, 15); World(sparks);
            var sparksMain = sparks.main; sparksMain.gravityModifier = .25f;
            var leaves = Leaves(crown, root.transform, kit, "Leaves", 3, Vector3.zero, Vector3.up, 70f, .6f, 1.4f, .05f);
            World(leaves);
        }
        Save(root);
    }

    /// <summary>Над облаком до его падения: золотой столб с высоты 3,5 м — искры и клубы вниз, расширяются к земле.</summary>
    private static void SavePollenFall(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenFallName);
        float fall = Simulation.ThicketPollenFallTicks / (float)Simulation.TicksPerSecond;
        var top = Child(root, "Column", Vector3.up * 3.5f);
        var sparks = Motes(top, "Sparks", kit.Glow, 24, Vector3.zero, .5f, 3f, 4.5f, .06f, .12f, .7f, .9f,
            new Color(PollenGlow.r, PollenGlow.g, PollenGlow.b, .95f), 0f, false);
        sparks.transform.localRotation = Aim(Vector3.down);
        var sparksMain = sparks.main; sparksMain.gravityModifier = .3f;
        var sparksShape = sparks.shape; sparksShape.angle = 12f;
        var sparksDrag = sparks.limitVelocityOverLifetime; sparksDrag.enabled = false;
        Stream(sparks, 40f, fall * .75f, 24);
        var clouds = Cloud(top, kit, "Clouds", 6, Vector3.zero, 15f, .4f, 2.5f, 3.5f, .6f, .8f, .8f, 1.0f, 0f, false,
            new Color(PollenGold.r, PollenGold.g, PollenGold.b, .30f), new Color(PollenDeep.r, PollenDeep.g, PollenDeep.b, .26f));
        clouds.transform.localPosition = Vector3.zero;
        clouds.transform.localRotation = Aim(Vector3.down);
        var cloudsDrag = clouds.limitVelocityOverLifetime; cloudsDrag.enabled = false;
        var cloudsGrow = clouds.sizeOverLifetime; cloudsGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .6f, 1f, 2.2f));
        Stream(clouds, 10f, fall * .6f, 6);
        Save(root);
    }

    /// <summary>
    /// Облако пыльцы лежит (зона Sim, 4 с). Ревью 02.10: «не читается, что это урон, и плоско
    /// по-наклеечному лежит» — плёнки на земле больше нет, облако объёмное и ядовитое
    /// (болезненное золото-зелень Toxic*): внутри клубятся стоячие клубы (кружат, ворочаются,
    /// второй ярус выше), вверх тянутся светящиеся споры; граница — кольцо светящихся и тёмных
    /// клубов ровно по радиусу Sim и тонкая светящаяся кромка; на каждый укус Sim (раз в
    /// ThicketPollenPulseTicks от падения) — пульс: кромка вспыхивает и расходится, из облака
    /// выбрасывает клубы и споры. Эмиссия до (жизнь зоны − 0,5) с — к концу гаснет сама.
    /// </summary>
    private static void SavePollenCloud(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.PollenCloudName);
        float r = Simulation.ThicketPollenRadius.ToFloat();
        float lies = Simulation.ThicketPollenLifeTicks / (float)Simulation.TicksPerSecond;
        float pulse = Simulation.ThicketPollenPulseTicks / (float)Simulation.TicksPerSecond;
        int pulses = Simulation.ThicketPollenLifeTicks / Simulation.ThicketPollenPulseTicks;
        float emit = lies - .5f;
        Color toxic = new Color(ToxicLight.r, ToxicLight.g, ToxicLight.b, .5f), deep = new Color(ToxicDeep.r, ToxicDeep.g, ToxicDeep.b, .48f);

        var churn = PollenPuffs(root, kit, kit.Haze, "Churn", 20, .75f, r * .72f, 1f, 1.3f, 1.9f, 1.0f, 1.3f, toxic, deep, .55f, .15f);
        Stream(churn, 15f, emit, 20);
        var high = PollenPuffs(root, kit, kit.Haze, "Churn High", 14, 1.45f, r * .6f, 1f, .8f, 1.2f, .9f, 1.2f,
            new Color(toxic.r, toxic.g, toxic.b, .38f), new Color(deep.r, deep.g, deep.b, .34f), -.45f, .3f);
        Stream(high, 11f, emit, 14);
        // Граница: кольцо клубов ровно по радиусу Sim — светящиеся и тёмные вперемешку, медленно кружат.
        var border = PollenPuffs(root, kit, kit.GlowHaze, "Border Glow", 26, .45f, r, 0f, .7f, 1.0f, .7f, .9f,
            new Color(ToxicGlow.r, ToxicGlow.g, ToxicGlow.b, .4f), new Color(ToxicLight.r, ToxicLight.g, ToxicLight.b, .32f), .4f, .3f);
        Stream(border, 28f, emit, 26);
        var rimDust = PollenPuffs(root, kit, kit.Haze, "Border Dust", 16, .55f, r * .97f, 0f, .9f, 1.25f, .8f, 1.1f,
            new Color(deep.r, deep.g, deep.b, .45f), new Color(ToxicDeep.r * .8f, ToxicDeep.g * .8f, ToxicDeep.b * .8f, .42f), -.3f, .2f);
        Stream(rimDust, 15f, emit, 16);
        var rim = Decal(root, "Rim", kit.GlowRingThin, 2f * r * Root2, 2f * r * Root2, lies, .95f,
            new Color(ToxicGlow.r, ToxicGlow.g, ToxicGlow.b, .85f), 0f, .06f);
        var rimSpin = rim.rotationOverLifetime; rimSpin.enabled = true; rimSpin.z = new ParticleSystem.MinMaxCurve(.25f);
        var spores = Motes(root, "Spores", kit.Glow, 40, Vector3.up * .2f, r * .85f, .05f, .25f, .05f, .11f, .9f, 1.4f,
            new Color(ToxicGlow.r, ToxicGlow.g, ToxicGlow.b, .95f), 0f, true);
        var sporesRise = spores.velocityOverLifetime; sporesRise.enabled = true; sporesRise.space = ParticleSystemSimulationSpace.Local;
        sporesRise.z = new ParticleSystem.MinMaxCurve(.55f);
        Stream(spores, 30f, emit, 40);

        // Пульс укуса: раз в pulse с от падения (Sim: NextPulseTick = падение + 15·k), pulses раз.
        var ring = Particles(root, "Pulse Ring", 2, .34f, .34f, 0f, 0f, 2f * r * Root2, 2f * r * Root2, 0f);
        ring.transform.localPosition = Vector3.up * .07f;
        var ringMain = ring.main; ringMain.startRotation = 0f; ringMain.startColor = new Color(ToxicGlow.r, ToxicGlow.g, ToxicGlow.b, 1f);
        var ringShape = ring.shape; ringShape.enabled = false;
        var ringGrow = ring.sizeOverLifetime; ringGrow.enabled = true;
        ringGrow.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .82f, .35f, 1.04f, 1f, 1.1f));
        var ringFade = ring.colorOverLifetime; ringFade.enabled = true; ringFade.color = Alpha(0f, .25f);
        var ringRenderer = ring.GetComponent<ParticleSystemRenderer>();
        ringRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; ringRenderer.sharedMaterial = kit.GlowRingThin; ringRenderer.sortingFudge = 3f;
        Pulses(ring, 1, pulse, pulses, lies);
        var puffs = PollenPuffs(root, kit, kit.Haze, "Pulse Puffs", 20, .5f, r * .8f, 1f, .7f, 1.1f, .5f, .65f,
            new Color(ToxicLight.r, ToxicLight.g, ToxicLight.b, .55f), new Color(ToxicDeep.r, ToxicDeep.g, ToxicDeep.b, .5f), 0f, 1.6f);
        Pulses(puffs, 9, pulse, pulses, lies);
        var bite = Motes(root, "Pulse Spores", kit.Glow, 28, Vector3.up * .3f, r * .8f, .3f, .9f, .06f, .12f, .45f, .6f,
            new Color(ToxicGlow.r, ToxicGlow.g, ToxicGlow.b, 1f), 0f, true);
        var biteShape = bite.shape; biteShape.radiusThickness = 1f;
        var biteRise = bite.velocityOverLifetime; biteRise.enabled = true; biteRise.space = ParticleSystemSimulationSpace.Local; biteRise.z = new ParticleSystem.MinMaxCurve(1.8f);
        Pulses(bite, 12, pulse, pulses, lies);
        Save(root);
    }

    /// <summary>
    /// Клубы пыльцы: стоячие билборды на диске/кольце радиуса radius (filled 1 — по всему диску,
    /// 0 — по кромке) на высоте height, кружат orbit рад/с вокруг вертикали, поднимаются rise м/с,
    /// ворочаются (шум). Система лежит осью +Z вверх.
    /// </summary>
    private static ParticleSystem PollenPuffs(GameObject root, Kit kit, Material material, string name, int count, float height, float radius,
        float filled, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color a, Color b, float orbit, float rise)
    {
        var puffs = Cloud(root, kit, name, count, Vector3.zero, 0f, radius, 0f, 0f, sizeMin, sizeMax, lifeMin, lifeMax, 0f, false, a, b);
        puffs.transform.localPosition = Vector3.up * height;
        puffs.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var shape = puffs.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = Mathf.Max(.01f, radius);
        shape.radiusThickness = filled; shape.arc = 360f;
        var drag = puffs.limitVelocityOverLifetime; drag.enabled = false;
        var swirl = puffs.velocityOverLifetime; swirl.enabled = true; swirl.space = ParticleSystemSimulationSpace.Local;
        swirl.orbitalZ = new ParticleSystem.MinMaxCurve(orbit);
        swirl.z = new ParticleSystem.MinMaxCurve(rise);
        var noise = puffs.noise; noise.enabled = true;
        noise.strength = .3f; noise.frequency = .6f; noise.scrollSpeed = .4f;
        var size = puffs.sizeOverLifetime; size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .3f, 1f, 1f, 1.25f));
        var fade = puffs.colorOverLifetime; fade.color = Alpha(.15f, .55f);
        var spin = puffs.rotationOverLifetime; spin.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
        puffs.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        return puffs;
    }

    /// <summary>Залпы по count частиц: первый через interval, всего cycles раз (эмиссия потоком выключена).</summary>
    private static void Pulses(ParticleSystem particles, int count, float interval, int cycles, float seconds)
    {
        var main = particles.main;
        main.duration = seconds + .1f;
        main.maxParticles = Mathf.Max(main.maxParticles, count * 2);
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(interval, (short)count, cycles, interval) });
    }

    // ------------------------------------------------------------- prefabs: rain

    /// <summary>Пуф куста на залп (корень — на кости куста): листья, красные капли, клубы — в мире.</summary>
    private static void SaveBushPuff(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BushPuffName);
        var leaves = Leaves(root, null, kit, "Leaves", 8, Vector3.zero, Vector3.forward + Vector3.up, 60f, 1.2f, 2.6f, 0f);
        World(leaves);
        var drops = Particles(root, "Drops", 10, .5f, .8f, 1.5f, 3f, .06f, .10f, 0f);
        drops.transform.localRotation = Aim(Vector3.forward + Vector3.up);
        var dropsMain = drops.main; dropsMain.gravityModifier = 1f; dropsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
        var dropsShape = drops.shape; dropsShape.enabled = true; dropsShape.shapeType = ParticleSystemShapeType.Cone; dropsShape.angle = 50f; dropsShape.radius = .3f;
        var dropsRenderer = drops.GetComponent<ParticleSystemRenderer>();
        dropsRenderer.renderMode = ParticleSystemRenderMode.Billboard; dropsRenderer.sharedMaterial = kit.Drop;
        World(drops);
        var puff = Cloud(root, kit, "Puff", 2, Vector3.zero, 50f, .3f, .4f, .9f, .7f, 1.0f, .6f, .8f, 0f, false,
            new Color(LeafLight.r, LeafLight.g, LeafLight.b, .25f), new Color(LeafDark.r, LeafDark.g, LeafDark.b, .22f));
        puff.transform.localPosition = Vector3.zero;
        World(puff);
        Save(root);
    }

    /// <summary>
    /// Ягода в полёте (вид ведёт корень по дуге куст → круг): крупная и две мелкие ягоды —
    /// меш-частицы своей икосферы на URP Particles/Lit, крутятся; капли сока по пути — в мире.
    /// </summary>
    private static void SaveBerry(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BerryName);
        foreach (int k in new[] { 0, 1 })
        {
            int count = k == 0 ? 1 : 2;
            float size = (k == 0 ? .30f : .16f) * BS;
            var berry = Particles(root, k == 0 ? "Big" : "Small", count, 4f, 4f, 0f, 0f, size, size * 1.1f, 0f);
            var main = berry.main;
            main.startColor = k == 0 ? new ParticleSystem.MinMaxGradient(BerryRed) : new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = berry.shape; shape.enabled = k == 1; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .14f * BS;
            var spin = berry.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(4f, 7f); spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f); spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var renderer = berry.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = kit.BerryMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
            renderer.mesh = kit.BerryMesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = kit.Berry;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        var juice = Particles(root, "Juice", 14, .5f, .7f, 0f, .3f, .04f, .06f, 0f);
        var juiceMain = juice.main; juiceMain.gravityModifier = 1f; juiceMain.startColor = new ParticleSystem.MinMaxGradient(Juice, BerryRed);
        var juiceShape = juice.shape; juiceShape.enabled = true; juiceShape.shapeType = ParticleSystemShapeType.Sphere; juiceShape.radius = .12f;
        juice.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Drop;
        Stream(juice, 10f, 1.4f, 14); World(juice);
        // На тёмной арене ягоду видно по свечению: красный ореол на ягоде и светящийся след.
        var halo = Particles(root, "Halo", 1, 4f, 4f, 0f, 0f, .9f * BS, .9f * BS, 0f);
        var haloMain = halo.main; haloMain.startColor = new Color(BerryGlow.r, BerryGlow.g, BerryGlow.b, .55f);
        var haloShape = halo.shape; haloShape.enabled = false;
        var haloRenderer = halo.GetComponent<ParticleSystemRenderer>(); haloRenderer.sharedMaterial = kit.Glow; haloRenderer.sortingFudge = 2f;
        var trail = Motes(root, "Trail", kit.Glow, 40, Vector3.zero, .08f, 0f, .2f, .10f, .18f, .25f, .35f,
            new Color(BerryGlow.r, BerryGlow.g, BerryGlow.b, .8f), 0f, false);
        Stream(trail, 40f, 1.4f, 40); World(trail);
        Save(root);
    }

    /// <summary>
    /// Брызги сока в круге залпа (ревью 02.10, вечер: «лужи после буллет рейна говно»): ничего
    /// плоского на земле — ни пятна сока, ни меша шлепка пака (корневая система CFXR2 Blood Shape
    /// Splash снята), только его капли «Stretched»/«Circles» в цветах сока, свой венчик брызг вверх,
    /// короткая вспышка и три кусочка ягоды.
    /// </summary>
    private static void SaveBerrySplat(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.BerrySplatName);
        float r = Simulation.ThicketRainRadius.ToFloat();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CfxrSplashPrefab);
        if (source != null)
        {
            var splash = Object.Instantiate(source);
            splash.name = "Splash";
            splash.transform.SetParent(root.transform, false);
            splash.transform.localPosition = Vector3.up * .05f;
            splash.transform.localScale = Vector3.one * (.9f * r / 1.4f);
            RazlomPelagVfxAssetBuilder.SanitizeImportedVfx(splash);
            // Плоский шлепок пака — меш-лист корневой системы: он и читался лужей. Капли-дети (свои
            // системы без субэмиттеров) остаются; рендерер уходит после системы — она его требует.
            var sheet = splash.GetComponent<ParticleSystem>();
            if (sheet != null)
            {
                var sheetRenderer = splash.GetComponent<ParticleSystemRenderer>();
                Object.DestroyImmediate(sheet);
                if (sheetRenderer != null) Object.DestroyImmediate(sheetRenderer);
            }
            foreach (var ps in splash.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = new ParticleSystem.MinMaxGradient(BerryRed, Juice);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                string material = renderer.sharedMaterial.name;
                if (material.Contains("splash") && kit.Juice != null) renderer.sharedMaterial = kit.Juice;
                else if (material.Contains("glow")) renderer.sharedMaterial = kit.Drop;
            }
        }
        else
        {
            Note(CfxrSplashPrefab + " (шлепок сока — только капли)");
            var drops = Particles(root, "Drops", 18, .4f, .6f, 2f, 4f, .06f, .12f, 0f);
            drops.transform.localRotation = Aim(Vector3.up);
            var dropsMain = drops.main; dropsMain.gravityModifier = 1.2f; dropsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, Juice);
            var dropsShape = drops.shape; dropsShape.enabled = true; dropsShape.shapeType = ParticleSystemShapeType.Cone; dropsShape.angle = 60f; dropsShape.radius = .3f;
            drops.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Drop;
        }
        // Венчик брызг вверх: капли сока узким конусом на 0,6–0,9 м, вытянуты по скорости, падают обратно и тают.
        var spray = Particles(root, "Spray", 16, .45f, .7f, 3.2f, 5.2f, .05f * BS, .09f * BS, 0f);
        spray.transform.localPosition = Vector3.up * .1f;
        spray.transform.localRotation = Aim(Vector3.up);
        var sprayMain = spray.main; sprayMain.gravityModifier = 1.5f; sprayMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, Juice);
        var sprayShape = spray.shape; sprayShape.enabled = true; sprayShape.shapeType = ParticleSystemShapeType.Cone; sprayShape.angle = 28f; sprayShape.radius = .25f * r;
        var sprayFade = spray.colorOverLifetime; sprayFade.enabled = true; sprayFade.color = Alpha(0f, .7f);
        var sprayRenderer = spray.GetComponent<ParticleSystemRenderer>();
        sprayRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sprayRenderer.lengthScale = 1.6f; sprayRenderer.velocityScale = .05f;
        sprayRenderer.sharedMaterial = kit.Drop;
        // Вспышка — короткая и меньше круга: на тёмной арене виден миг удара, не красный диск на земле.
        Flash(root, kit, "Flash", Vector3.up * .4f, 1.4f * r, .15f, new Color(BerryGlow.r, BerryGlow.g, BerryGlow.b, .7f), 0f, false);
        var bits = Particles(root, "Bits", 3, 1.2f, 1.6f, 1.5f, 3f, .07f * BS, .10f * BS, 0f);
        bits.transform.localPosition = Vector3.up * .1f;
        bits.transform.localRotation = Aim(Vector3.up);
        var bitsMain = bits.main; bitsMain.gravityModifier = 1.4f; bitsMain.startColor = new ParticleSystem.MinMaxGradient(BerryRed, BerryDeep);
        var bitsShape = bits.shape; bitsShape.enabled = true; bitsShape.shapeType = ParticleSystemShapeType.Cone; bitsShape.angle = 50f; bitsShape.radius = .2f;
        Collide(bits, root.transform, .3f);
        var bitsRenderer = bits.GetComponent<ParticleSystemRenderer>();
        bitsRenderer.renderMode = kit.BerryMesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        bitsRenderer.mesh = kit.BerryMesh;
        bitsRenderer.sharedMaterial = kit.Berry;
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: storm

    /// <summary>
    /// Крона сыплет лепестки всю бурю: «~»-системы у каждой кроны («Left»/«Right»), длительность
    /// эмиссии ставит вид (до LastImpactTick + 30); лепестки в мире, кружат и падают.
    /// </summary>
    private static void SaveCrownShed(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.CrownShedName);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            // Вдвое реже (ревью 02.10, вечер: конфетти лепестков у босса закрывало и его, и героя).
            var petals = Petals(crown, kit, ThicketMasterCombatView.TimedPrefix + "Petals", 45, Vector3.zero, 2.2f, 3.0f, .6f, 1.4f, 0f);
            var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .8f;
            Stream(petals, 12f, 4.5f, 45); World(petals);
            Collide(petals, root.transform, .02f);
        }
        Save(root);
    }

    private static float StormSeconds => (Simulation.ThicketStormFirstWaveTicks + Simulation.ThicketStormSecondWaveTicks) / (float)Simulation.TicksPerSecond;

    /// <summary>Канал бури — от Started(Storm, 0) до EndTick: обе волны и стойка после второй (5,9 с при 90 / 75 / 12).</summary>
    private static float ChannelSeconds => (Simulation.ThicketStormFirstWaveTicks + Simulation.ThicketStormSecondWaveTicks
        + Simulation.ThicketStormRecoveryTicks) / (float)Simulation.TicksPerSecond;

    /// <summary>Полуоси пола поляны босса, м (GladeLayout; +10% площади 02.10 — 10,49 × 7,87).</summary>
    private static float FloorHalfWidth => GladeLayout.BossFloorHalfWidth.ToFloat();
    private static float FloorHalfDepth => GladeLayout.BossFloorHalfDepth.ToFloat();

    /// <summary>
    /// Вихрь лепестков по арене: коробка на пол поляны + 1 м с каждой стороны, высотой 2,5 м, вокруг
    /// центра поляны, лепестки кружат вокруг вертикали 0,35 рад/с и поднимаются, ветровые штрихи.
    /// Поток нарастает к каждой волне (буря стала 3 + 2,5 с — ревью 02.10, вечер): тихо в начале,
    /// гуще за секунду до удара, пик на ударе, спад; поток — на площадь пола (плотность та же).
    /// </summary>
    private static void SaveStormVortex(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StormVortexName);
        float span = StormSeconds + .5f;
        float first = Simulation.ThicketStormFirstWaveTicks / (float)Simulation.TicksPerSecond / span;
        float second = StormSeconds / span;
        var box = new Vector3(2f * FloorHalfWidth + 2f, 2.5f, 2f * FloorHalfDepth + 2f);
        // Прежние 220 в секунду были на коробку 22 × 17 м; ревью 02.10, вечер (конфетти закрывало босса и героя) — вдвое меньше.
        float flow = 110f * box.x * box.z / (22f * 17f);
        var petals = Petals(root, kit, "Petals", 280, Vector3.up * 1.6f, 2.0f, 2.8f, 0f, .3f, 0f);
        petals.transform.localRotation = Quaternion.identity;
        petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var petalSize = petals.main; petalSize.startSize = new ParticleSystem.MinMaxCurve(.18f, .34f);
        var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        Stream(petals, flow, span, Mathf.CeilToInt(flow * 2.8f * 1.25f));
        // Плоской розовой дымки под всей поляной больше нет (ревью 02.10, п. 12): она шла поверх
        // поля опасности бури (ThicketStormDangerView, очередь Transparent−15) и заливала его
        // чистые круги укрытий. Где урон, а где укрытие, на земле рисует только это поле.
        var rate = petals.emission;
        rate.rateOverTime = new ParticleSystem.MinMaxCurve(flow, Curve(0f, .2f, first - .2f, .35f, first - .04f, .75f, first, 1f,
            first + .07f, .4f, second - .2f, .4f, second - .04f, .8f, second, 1f, Mathf.Min(.99f, second + .05f), .3f, 1f, .12f));
        var swirl = petals.velocityOverLifetime; swirl.enabled = true; swirl.space = ParticleSystemSimulationSpace.Local;
        swirl.orbitalY = new ParticleSystem.MinMaxCurve(.35f);
        swirl.y = new ParticleSystem.MinMaxCurve(.4f);
        var petalsMain = petals.main; petalsMain.gravityModifier = 0f;
        var wind = Particles(root, "Wind", 24, .8f, 1.2f, 0f, 0f, .5f, .9f, 0f);
        wind.transform.localPosition = Vector3.up * 1.4f;
        var windShape = wind.shape; windShape.enabled = true; windShape.shapeType = ParticleSystemShapeType.Box; windShape.scale = box;
        var windMain = wind.main; windMain.startColor = new Color(PetalWhite.r, PetalWhite.g, PetalWhite.b, .38f);
        var windSwirl = wind.velocityOverLifetime; windSwirl.enabled = true; windSwirl.space = ParticleSystemSimulationSpace.Local;
        windSwirl.orbitalY = new ParticleSystem.MinMaxCurve(.5f);
        var windFade = wind.colorOverLifetime; windFade.enabled = true; windFade.color = Alpha(.2f, .5f);
        var windRenderer = wind.GetComponent<ParticleSystemRenderer>();
        windRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        windRenderer.lengthScale = 4f; windRenderer.velocityScale = .15f;
        windRenderer.sharedMaterial = kit.Wind;
        Stream(wind, 7f, span, 24);
        Save(root);
    }

    /// <summary>
    /// Столб света над кругом-укрытием (r из Sim): цилиндр Hovl CylinderFromGround (r1, h2) с
    /// градиентом Trail25 одним каналом, alpha-blend, ядро ≤ 1,25 по R — героя в круге не
    /// высветляет; звёздочки вверх («~» — пока держится). Гасит вид. Плоского на земле нет:
    /// круг на полу (вырез в опасности, золотая кромка) рисует поле бури ThicketStormDangerView.
    /// </summary>
    private static void SaveLightPillar(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.LightPillarName);
        float r = Simulation.ThicketStormSafeRadius.ToFloat();
        const float life = 6f, height = 3.5f;
        if (kit.Cylinder != null)
        {
            var shaft = Particles(root, "Shaft", 1, life, life, 0f, 0f, 1f, 1f, 0f);
            var main = shaft.main;
            main.startRotation = 0f;
            main.startSize3D = true;
            main.startSizeX = r; main.startSizeY = height / 2f; main.startSizeZ = r;
            main.startColor = new Color(SafeGlow.r, SafeGlow.g, SafeGlow.b, .55f);
            var shape = shaft.shape; shape.enabled = false;
            var grow = shaft.sizeOverLifetime; grow.enabled = true; grow.separateAxes = true;
            grow.x = new ParticleSystem.MinMaxCurve(1f); grow.z = new ParticleSystem.MinMaxCurve(1f);
            grow.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, .15f / life, 1f, 1f, 1f));
            var renderer = shaft.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = kit.Cylinder;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = kit.GlowPillar;
        }
        // Светлого диска и светящейся кромки на земле больше нет (ревью 02.10, п. 12): они лежали
        // поверх поля опасности (очередь 2996 > 2985), возвращали «жёлтую тарелку» в середину
        // круга и удваивали золотую кромку, которую рисует шейдер поля ровно по радиусу Sim.
        var stars = Motes(root, ThicketMasterCombatView.TimedPrefix + "Stars", kit.Glow, 18, Vector3.up * .1f, r * .85f, .6f, 1.2f, .10f, .18f,
            1.0f, 1.4f, new Color(SafeGlow.r, SafeGlow.g, SafeGlow.b, .9f), 0f, true);
        Stream(stars, 8f, 2f, 18);
        Save(root);
    }

    /// <summary>Порыв волны бури: лепестки из всей арены наружу-вверх и тормозят, листья, низкая пыль (коробки — пол поляны).</summary>
    private static void SaveStormWave(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StormWaveName);
        var box = new Vector3(2f * FloorHalfWidth, 1f, 2f * FloorHalfDepth);
        // Кольцо порыва доходит до углов пола (было 12 м при поле 20 × 15).
        float gust = .96f * Mathf.Sqrt(FloorHalfWidth * FloorHalfWidth + FloorHalfDepth * FloorHalfDepth);
        var petals = Petals(root, kit, "Petals", 120, Vector3.up * .8f, 1.2f, 1.7f, 0f, 0f, 0f);
        petals.transform.localRotation = Quaternion.identity;
        petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var petalSize = petals.main; petalSize.startSize = new ParticleSystem.MinMaxCurve(.18f, .34f);
        // Удар волны — розовое светящееся кольцо от центра поляны к краям.
        Wave(root, "Gust Ring", kit.GlowRingThin, 2f * gust * Root2, .7f, .08f, .55f, new Color(PetalPink.r, PetalPink.g, PetalPink.b, 1f), 0f);
        Wave(root, "Gust Band", kit.GlowRing, 2f * gust * Root2, .55f, .08f, .5f, new Color(PetalPink.r, PetalPink.g, PetalPink.b, .35f), .02f);
        var shape = petals.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var burst = petals.velocityOverLifetime; burst.enabled = true; burst.space = ParticleSystemSimulationSpace.Local;
        burst.radial = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 6f, .3f, 1.5f, 1f, .3f));
        burst.y = new ParticleSystem.MinMaxCurve(1.5f);
        var drag = petals.limitVelocityOverLifetime; drag.limit = new ParticleSystem.MinMaxCurve(8f);
        var leaves = Leaves(root, root.transform, kit, "Leaves", 30, Vector3.up * .5f, Vector3.up, 70f, 3f, 6f, 0f);
        leaves.transform.localRotation = Quaternion.identity;
        var leafShape = leaves.shape; leafShape.shapeType = ParticleSystemShapeType.Box; leafShape.scale = box;
        var dust = Dust(root, kit, "DustLow", 20, Vector3.up * .03f, 0f, .1f, 1f, 2.5f, 1.4f, 2.2f, 1.0f, 1.4f, .24f, 0f, true);
        dust.transform.localRotation = Quaternion.identity;
        var dustShape = dust.shape; dustShape.shapeType = ParticleSystemShapeType.Box; dustShape.scale = new Vector3(box.x, .1f, box.z);
        Save(root);
    }

    /// <summary>
    /// Канал бури (ревью 02.10, вечер: «буря — непонятно, что босс делает… сделать бурю дольше и
    /// более явно»): всю бурю, от Started(Storm, 0) до EndTick, видно, что босс её зовёт. Корень —
    /// центр тела на земле (вид ведёт его за телом, VfxFollow.Channel):
    /// • вихрь — лепестки столбом в контуре корпуса (r 1,0–1,4 м, у земли прозрачнее), кружат
    ///   2,4 рад/с и поднимаются до ~3 м; с 2,3 м — над головой героя — воронка расходится до ~4,9 м
    ///   и r ~2,6 м; ветровые штрихи по той же спирали в контуре корпуса. На высоте героя лепестков
    ///   у круга 0 укрытия нет (находки ревью 02.10: «видимости героя нет», сквозное окно частицы
    ///   не режет);
    /// • аура у ног — розовое кольцо и пульсы от тела — с 02.10 (вечер) рисует само поле бури
    ///   (ThicketStormDangerView, _Aura): внутри укрытий она гаснет до лёгкой дымки, кромка у всех кругов золотая;
    /// • кроны («Left»/«Right» — на костях крон) горят розовым золотом: мягкое свечение и искры вверх;
    /// • луч — с середины между кронами («Crown») вверх на 7 м: розовый цилиндр Hovl с ядром и
    ///   лепестки, винтом летящие по нему вверх. Луч тонкий (r 0,32) и полупрозрачный, свечение
    ///   крон — 1,2–1,6 м: на экране они ложатся на пол за боссом, а там бывает герой (круг 0
    ///   укрытия — вплотную к боссу, ревью 02.10: «видимости героя нет»). Не золотой столб от земли —
    ///   столбы над укрытиями (LightPillar) не спутать.
    /// «~»-системы идут, сколько скажет вид (до EndTick), гуще к ударам волн; одиночные долгие
    /// частицы (луч) живут ChannelSeconds. Всё — alpha-blend (свечение — копии с _HdrMultiply): героя
    /// не высветляет, тело не перекрашивает. Под всей поляной ничего не лежит — где урон, рисует только
    /// поле бури. Лепестков ~вдвое меньше, у героя (1,5 м по лучу камеры) их гасит вид (ClearAroundHero).
    /// </summary>
    private static void SaveStormChannel(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.StormChannelName);
        float channel = ChannelSeconds;
        float first = Simulation.ThicketStormFirstWaveTicks / (float)Simulation.TicksPerSecond / channel;
        float second = StormSeconds / channel;
        // Множитель потока по доле канала: ровно, гуще за полсекунды до удара волны, пик на ударе, спад.
        AnimationCurve swell = Curve(0f, .45f, first - .17f, .55f, first - .02f, 1f, first + .05f, .6f,
            second - .14f, .6f, second - .02f, 1f, Mathf.Min(.995f, second + .04f), .5f, 1f, .35f);
        var roseGold = new Color(1f, .80f, .58f);

        // Вихрь лепестков вокруг корпуса. Укрытие ближнего боя — круг 0 бури вплотную к телу
        // (центр в 2 м от босса, r 2): герой стоит в 1,7–4 м от центра, и сквозное «окно» вида
        // режет только тело и накладки, не частицы. Поэтому на высоте героя вихрь — столб в контуре
        // корпуса (ChannelColumnRadius, без расхода, у земли прозрачнее), а воронка раскрывается
        // только с ChannelFunnelFrom — над головой героя: камера боя (48°) кладёт точку высоты h на
        // 0,9h дальше по экрану, и воронка у бока и перед телом рисуется выше героя, не на нём.
        // Потоки лепестков канала — ~вдвое реже (ревью 02.10, вечер: конфетти у босса закрывало его и героя).
        var tornado = Petals(root, kit, ThicketMasterCombatView.TimedPrefix + "Tornado", 180, Vector3.up * .2f, 2.0f, 2.4f, 0f, 0f, 0f);
        ChannelPetals(tornado, kit, .16f, .30f, Alpha(.25f, .7f));
        var tornadoShape = tornado.shape; tornadoShape.enabled = true;
        tornadoShape.shapeType = ParticleSystemShapeType.Circle;
        tornadoShape.radius = ChannelColumnRadius; tornadoShape.radiusThickness = .3f; tornadoShape.arc = 360f;
        // Шум слабее обычных лепестков: не выносит столб из контура корпуса к герою.
        var tornadoNoise = tornado.noise; tornadoNoise.strength = .2f;
        Spiral(tornado, 1.25f, 2.4f, 0f);
        Stream(tornado, 50f, channel, 180);
        Swell(tornado, 50f, swell);

        // Воронка над головой героя: с той же окружности на высоте ChannelFunnelFrom расходится
        // вверх до ~4,9 м и r ~2,6 м — над кронами видно, что босс крутит бурю.
        var funnel = Petals(root, kit, ThicketMasterCombatView.TimedPrefix + "Tornado Funnel", 170, Vector3.up * ChannelFunnelFrom,
            2.0f, 2.6f, 0f, 0f, 0f);
        ChannelPetals(funnel, kit, .18f, .32f, Alpha(.1f, .7f));
        var funnelShape = funnel.shape; funnelShape.enabled = true;
        funnelShape.shapeType = ParticleSystemShapeType.Circle;
        funnelShape.radius = ChannelColumnRadius; funnelShape.radiusThickness = .3f; funnelShape.arc = 360f;
        Spiral(funnel, 1.0f, 2.0f, .45f);
        Stream(funnel, 45f, channel, 170);
        Swell(funnel, 45f, swell);

        var wind = Particles(root, ThicketMasterCombatView.TimedPrefix + "Tornado Wind", 32, .9f, 1.2f, 0f, 0f, .7f, 1.1f, 0f);
        wind.transform.localPosition = Vector3.up * .6f;
        wind.transform.localRotation = Aim(Vector3.up);
        var windMain = wind.main;
        windMain.startColor = new ParticleSystem.MinMaxGradient(new Color(PetalWhite.r, PetalWhite.g, PetalWhite.b, .32f),
            new Color(PetalPink.r, PetalPink.g, PetalPink.b, .28f));
        var windShape = wind.shape; windShape.enabled = true; windShape.shapeType = ParticleSystemShapeType.Circle;
        windShape.radius = ChannelColumnRadius * .95f; windShape.radiusThickness = .2f; windShape.arc = 360f;
        Spiral(wind, 1.6f, 2.8f, .1f);
        var windFade = wind.colorOverLifetime; windFade.enabled = true; windFade.color = Alpha(.25f, .55f);
        var windRenderer = wind.GetComponent<ParticleSystemRenderer>();
        windRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        windRenderer.lengthScale = 3f; windRenderer.velocityScale = .16f;
        windRenderer.sharedMaterial = kit.Wind;
        Stream(wind, 20f, channel, 32);
        Swell(wind, 20f, swell);

        // Аура у ног (розовое кольцо, полоса и пульсы от тела) — больше не частицы: её рисует поле бури
        // (ThicketStormDanger.shader, _Aura; ThicketStormDangerRules.AuraOf). Ревью 02.10, вечер: «круг укрытия у
        // босса розовый, а остальные золотые» — кольцо r 2,6 м пересекало круг 0, и частицы под полем заливали его
        // розовым; поле гасит ауру внутри укрытий до лёгкой розовой дымки, золотая кромка у всех кругов одна.

        // Кроны горят розовым золотом: свечение на кости кроны и искры вверх (места — заглушка, ставит вид).
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, new Vector3(side == ThicketMasterCombatView.LeftChild ? -1.15f : 1.15f, 3.35f, 2.2f));
            var glow = Particles(crown, ThicketMasterCombatView.TimedPrefix + "Crown Glow", 14, .5f, .7f, 0f, 0f, 1.2f, 1.6f, 0f);
            var glowMain = glow.main;
            glowMain.startColor = new ParticleSystem.MinMaxGradient(new Color(roseGold.r, roseGold.g, roseGold.b, .42f),
                new Color(PetalPink.r, PetalPink.g, PetalPink.b, .38f));
            var glowShape = glow.shape; glowShape.enabled = false;
            var glowSize = glow.sizeOverLifetime; glowSize.enabled = true;
            glowSize.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .7f, .35f, 1f, 1f, .8f));
            var glowFade = glow.colorOverLifetime; glowFade.enabled = true; glowFade.color = Alpha(.3f, .45f);
            var glowRenderer = glow.GetComponent<ParticleSystemRenderer>();
            glowRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            glowRenderer.sharedMaterial = kit.Glow;
            glowRenderer.sortingFudge = -3f;
            Stream(glow, 18f, channel, 14);
            Swell(glow, 18f, swell);
            var motes = Motes(crown, ThicketMasterCombatView.TimedPrefix + "Crown Motes", kit.StarMote, 36, Vector3.zero, .6f, .9f, 1.8f,
                .10f, .18f, .9f, 1.3f, new Color(roseGold.r, roseGold.g, roseGold.b, .9f), 0f, false);
            Stream(motes, 12f, channel, 36);
            World(motes);
        }

        // Луч с крон вверх и лепестки по нему винтом.
        var top = Child(root, ThicketMasterCombatView.CrownChild, new Vector3(0f, 3.35f + ThicketMasterCombatView.ChannelBeamLift, 2.2f));
        const float beamHeight = 7f;
        if (kit.Cylinder != null)
        {
            Beam(top, kit, "Beam", .32f, beamHeight, channel, new Color(PetalPink.r, PetalPink.g, PetalPink.b, .4f));
            Beam(top, kit, "Beam Core", .14f, beamHeight * .92f, channel, new Color(1f, .92f, .84f, .45f));
        }
        else Note(HovlModels + "CylinderFromGround.fbx (луч канала бури — только лепестки)");
        var rising = Petals(top, kit, ThicketMasterCombatView.TimedPrefix + "Beam Petals", 44, Vector3.zero, 1.3f, 1.7f, 3.2f, 4.6f, 0f);
        var risingMain = rising.main; risingMain.gravityModifier = 0f; risingMain.startSize = new ParticleSystem.MinMaxCurve(.14f, .24f);
        var risingShape = rising.shape; risingShape.enabled = true; risingShape.shapeType = ParticleSystemShapeType.Cone;
        risingShape.angle = 6f; risingShape.radius = .3f;
        var risingDrag = rising.limitVelocityOverLifetime; risingDrag.enabled = false;
        Spiral(rising, 0f, 3f, 0f);
        rising.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        Stream(rising, 22f, channel, 44);
        Swell(rising, 22f, swell);
        Save(root);
    }

    /// <summary>
    /// Столб вихря канала, м от центра тела: в контуре корпуса (бёдра — 1,69 м вбок, грудь — 2,74
    /// вперёд, хвост — 2,04 назад), чтобы на высоте героя лепестки не стояли в круге 0 укрытия
    /// (вплотную к телу, герой там в 1,7–4 м от центра).
    /// </summary>
    private const float ChannelColumnRadius = 1.4f;

    /// <summary>С этой высоты, м, вихрь канала раскрывается воронкой — над головой героя (≈ 1,8 м).</summary>
    private const float ChannelFunnelFrom = 2.3f;

    /// <summary>
    /// Лепестки канала: без тяжести и без ограничения скорости (оно гасило бы подъём и кружение),
    /// розовый набор, вырастают и сжимаются к концу, прозрачность fade, светящийся материал.
    /// </summary>
    private static void ChannelPetals(ParticleSystem petals, Kit kit, float sizeMin, float sizeMax, Gradient fade)
    {
        var main = petals.main;
        main.gravityModifier = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = ChannelPetalColors();
        var drag = petals.limitVelocityOverLifetime; drag.enabled = false;
        var size = petals.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0f, .5f, .15f, 1f, .85f, 1f, 1f, .3f));
        var color = petals.colorOverLifetime; color.enabled = true; color.color = fade;
        petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
    }

    /// <summary>Лепестки канала: 30 % белых, 55 % розовых, 15 % золотых (буря — розовая, не белая).</summary>
    private static ParticleSystem.MinMaxGradient ChannelPetalColors()
    {
        var gradient = new Gradient { mode = GradientMode.Fixed };
        gradient.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .3f), new GradientColorKey(PetalPink, .85f), new GradientColorKey(PetalGold, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>
    /// Спираль вверх: система лежит осью +Z вверх (Aim(Vector3.up)), скорости — в её осях: подъём
    /// rise м/с, кружение spin рад/с вокруг оси, расход от центра radial м/с. Оси скорости и
    /// орбиты — одного режима (константы): иначе Unity ругается на смешанные кривые.
    /// </summary>
    private static void Spiral(ParticleSystem particles, float rise, float spin, float radial)
    {
        var velocity = particles.velocityOverLifetime; velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(rise);
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(spin);
        velocity.radial = new ParticleSystem.MinMaxCurve(radial);
    }

    /// <summary>Поток rate частиц в секунду × кривая curve по доле длительности системы (её ставит вид).</summary>
    private static void Swell(ParticleSystem particles, float rate, AnimationCurve curve)
    {
        var emission = particles.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate, curve);
    }

    /// <summary>
    /// Луч канала: цилиндр Hovl CylinderFromGround (r1, h2, основание в нуле) радиусом radius и
    /// высотой height, одна частица на life с: вырастает за 0,4 с, гаснет за последние 0,45 с.
    /// </summary>
    private static void Beam(GameObject host, Kit kit, string name, float radius, float height, float life, Color color)
    {
        var beam = Particles(host, name, 1, life, life, 0f, 0f, 1f, 1f, 0f);
        var main = beam.main;
        main.startRotation = 0f;
        main.startSize3D = true;
        main.startSizeX = radius; main.startSizeY = height / 2f; main.startSizeZ = radius;
        main.startColor = color;
        var shape = beam.shape; shape.enabled = false;
        var grow = beam.sizeOverLifetime; grow.enabled = true; grow.separateAxes = true;
        grow.x = new ParticleSystem.MinMaxCurve(1f); grow.z = new ParticleSystem.MinMaxCurve(1f);
        grow.y = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 0f, .4f / life, 1f, 1f, 1f));
        var fade = beam.colorOverLifetime; fade.enabled = true; fade.color = Alpha(.2f / life, 1f - .45f / life);
        var renderer = beam.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = kit.Cylinder;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = kit.GlowPillar;
    }

    // ------------------------------------------------------------- prefabs: roar, wake

    /// <summary>
    /// Вдох рёва (36 тиков): с крон («Left»/«Right») сыплются листья; последние 12 тиков кольцо пыли
    /// на внешнем радиусе рёва стягивается внутрь. Корень — центр тела.
    /// </summary>
    private static void SaveRoarInhale(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.RoarInhaleName);
        float outer = Simulation.ThicketRoarOuterRadius.ToFloat();
        float windup = Simulation.ThicketRoarWindupTicks / (float)Simulation.TicksPerSecond;
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var leaves = Leaves(crown, root.transform, kit, "Leaves", 4, Vector3.zero, Vector3.up, 80f, .3f, .9f, .1f);
            Stream(leaves, 5f, windup * .8f, 4); World(leaves);
        }
        var ring = Dust(root, kit, "Inhale", 24, Vector3.up * .03f, 0f, .1f, 0f, 0f, 1.0f, 1.5f, .45f, .6f, .26f, windup - 12f / Simulation.TicksPerSecond, true);
        FromPoints(ring, RingPoints("ThicketRoarInhalePoints", 24, outer * .9f, outer, 80f, 89f, 501), false, 0f);
        var pull = ring.velocityOverLifetime; pull.enabled = true; pull.space = ParticleSystemSimulationSpace.Local;
        pull.radial = new ParticleSystem.MinMaxCurve(-2.5f);
        var drag = ring.limitVelocityOverLifetime; drag.enabled = false;
        Save(root);
    }

    /// <summary>Рёв: за сколько секунд волна добегает от внутреннего до внешнего радиуса.</summary>
    private const float RoarTravel = .42f;

    /// <summary>
    /// Рёв (ревью 02.10: «очень блеклый для такого большого АОЕ, не красочный и плоский»).
    /// Корень — центр тела. Стоячая стена пыли золото-зелени встаёт на внутреннем радиусе и за
    /// RoarTravel с добегает до внешнего (низ, светящийся гребень выше), в ней летят листья и
    /// розовые лепестки, штрихи порыва; под стеной — тонкая светящаяся кромка листового золота;
    /// с крон («Left»/«Right», вид ставит на кости) — столб листьев и лепестков; над полосой
    /// кольца — светящиеся пылинки. Плоских плёнок нет.
    /// </summary>
    private static void SaveRoarBlast(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.RoarBlastName);
        float inner = Simulation.ThicketRoarInnerRadius.ToFloat(), outer = Simulation.ThicketRoarOuterRadius.ToFloat();
        Color dustA = new Color(.80f, .76f, .42f, .58f), dustB = new Color(.60f, .68f, .28f, .52f);
        Wave(root, "Edge", kit.GlowRingThin, 2f * outer * Root2, .75f, inner / outer, RoarTravel / .75f, new Color(.92f, 1f, .42f, 1f), 0f);
        Wall(root, "Wall", kit.Haze, 76, .8f, inner, outer * .97f, RoarTravel, .85f, 1.7f, 2.5f, dustA, dustB, .5f, 0f);
        Wall(root, "Crest", kit.GlowHaze, 48, 1.75f, inner, outer * .95f, RoarTravel, .7f, 1.0f, 1.5f,
            new Color(LeafGlow.r, LeafGlow.g, LeafGlow.b, .34f), new Color(PollenGlow.r, PollenGlow.g, PollenGlow.b, .3f), .7f, .02f);
        Streaks(root, "Streaks", kit.Wind, 52, .5f, inner, outer, RoarTravel, .5f, .4f, .6f, new Color(.96f, .95f, .72f, .55f), 0f);
        var leaves = Leaves(root, root.transform, kit, "Wall Leaves", 44, Vector3.up * .9f, Vector3.up, 0f, 0f, 0f, 0f);
        RadialPush(leaves, inner, outer * .95f, RoarTravel, 2.1f, .9f);
        var petals = Petals(root, kit, "Wall Petals", 40, Vector3.up * 1.2f, 1.6f, 2.2f, 0f, 0f, .02f);
        petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var petalsMain = petals.main; petalsMain.startSize = new ParticleSystem.MinMaxCurve(.16f, .3f);
        RadialPush(petals, inner, outer, RoarTravel, 1.9f, .6f);
        var motes = Motes(root, "Motes", kit.Glow, 40, Vector3.up * .4f, outer * .85f, .1f, .4f, .07f, .13f, 1.0f, 1.5f,
            new Color(LeafGlow.r, LeafGlow.g, LeafGlow.b, .95f), .15f, true);
        var motesRise = motes.velocityOverLifetime; motesRise.enabled = true; motesRise.space = ParticleSystemSimulationSpace.Local;
        motesRise.z = new ParticleSystem.MinMaxCurve(.6f);
        Flash(root, kit, "Flash", Vector3.up * 2.4f, 1.4f * BS, .12f, new Color(LeafGlow.r, LeafGlow.g, LeafGlow.b, .4f), 0f, false);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.zero);
            var crownLeaves = Leaves(crown, root.transform, kit, "Leaves", 20, Vector3.zero, Vector3.up, 50f, 3.5f, 6.5f, 0f);
            World(crownLeaves);
            var crownPetals = Petals(crown, kit, "Petals", 16, Vector3.zero, 1.8f, 2.4f, 3f, 5.5f, .02f);
            crownPetals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
            var crownShape = crownPetals.shape; crownShape.enabled = true; crownShape.shapeType = ParticleSystemShapeType.Cone; crownShape.angle = 45f; crownShape.radius = .4f;
            World(crownPetals);
            var puff = Cloud(crown, kit, "Puff", 3, Vector3.zero, 40f, .3f, .6f, 1.4f, 1.0f, 1.4f, .6f, .8f, 0f, false,
                new Color(LeafGlow.r, LeafGlow.g, LeafGlow.b, .3f), new Color(PollenGlow.r, PollenGlow.g, PollenGlow.b, .26f));
            puff.transform.localPosition = Vector3.zero;
            puff.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowHaze;
            World(puff);
        }
        Save(root);
    }

    /// <summary>
    /// Любую систему — в стену волны: частицы встают на кольце from (в плоскости земли) и бегут
    /// наружу до to за travel с (радиальная скорость по кривой к нулю), поднимаются rise м/с;
    /// life — средняя жизнь частиц системы (кривая — по доле жизни). Тормоз пака снят.
    /// </summary>
    private static void RadialPush(ParticleSystem particles, float from, float to, float travel, float life, float rise)
    {
        particles.transform.localRotation = Aim(Vector3.up);
        var main = particles.main;
        main.startSpeed = 0f;
        var shape = particles.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(.01f, from);
        shape.radiusThickness = 0f;
        shape.arc = 360f;
        var drag = particles.limitVelocityOverLifetime; drag.enabled = false;
        float tau = Mathf.Clamp(travel / Mathf.Max(.05f, life), .05f, .9f);
        var push = particles.velocityOverLifetime; push.enabled = true;
        push.space = ParticleSystemSimulationSpace.Local;
        push.radial = new ParticleSystem.MinMaxCurve(2f * Mathf.Max(0f, to - from) / Mathf.Max(.05f, travel), Curve(0f, 1f, tau, 0f, 1f, 0f));
        push.z = new ParticleSystem.MinMaxCurve(rise);
    }

    /// <summary>Пробуждение: у передней лапы (корень на земле под пальцами) рвутся пять коротких корней и уходят, комья, пыль, мох.</summary>
    private static void SaveWakeTear(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.WakeTearName);
        var ground = root.transform;
        for (int i = 0; i < 5; i++)
        {
            float angle = (i + Hash01(i, 601) * .6f) / 5f * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            GrowChild(root, "Корень " + i, kit.Curls[i % kit.Curls.Length], i % 2 == 0 ? kit.Wood : kit.WoodDark,
                outward * Mathf.Lerp(.3f, .6f, Hash01(i, 602)), Quaternion.LookRotation(outward, Vector3.up),
                Mathf.Lerp(.45f, .6f, Hash01(i, 603)), Mathf.RoundToInt(Hash01(i, 604) * 50f), 0);
        }
        Debris(root, ground, "Clods", kit.Clod, 16, Vector3.up * .08f, Vector3.up, 40f, .4f, 2.4f, 4.4f, .1f, .22f, 1.6f, 0f, SoilLight, SoilDark);
        Dust(root, kit, "Dust", 6, Vector3.zero, 55f, .4f, .6f, 1.4f, .9f, 1.3f, .9f, 1.2f, .3f, 0f, false);
        var moss = Leaves(root, ground, kit, "Moss", 6, Vector3.up * .15f, Vector3.up, 50f, 1.4f, 2.8f, .02f);
        var mossMain = moss.main; mossMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.46f, .54f, .22f), new Color(.30f, .38f, .14f));
        Save(root);
    }

    // ------------------------------------------------------------- prefabs: death

    /// <summary>Волна цветения на касании: до стольких метров и за столько секунд.</summary>
    private const float BloomWaveReach = 6f, BloomWaveTravel = .5f;

    /// <summary>Розово-золотая кромка и свечение смерти (не белое: тело и героя не высветляет).</summary>
    private static readonly Color BloomGlow = new Color(1f, .74f, .62f), BloomGold = new Color(1f, .84f, .52f);

    /// <summary>
    /// «Цветущий холм», переходная часть (владелец 02.10, вечер: «смерть надо доработать» — в игре смерть
    /// читалась тёмным комом). Корень — середина падающего тела (вид ведёт его до касания, дальше стоит),
    /// +Z — взгляд тела. Время — ThicketMasterDeathRules.
    /// • Сразу (добивание): короткая розово-золотая вспышка в кусте («Bush», alpha-blend, 0,2 с — тело
    ///   не высветляет), кольцо светящихся лепестков с листьями от тела наружу (~4,5 м), тонкая кромка
    ///   3,5 м, лепестки с крон («Left»/«Right»). Стоп-кадр и тряска — такт убийства (как был).
    /// • На касании боком («Late …», вид ставит задержку LandsAt): волна цветения — светящаяся кромка
    ///   до 6 м, стоячая стена лепестков, листьев, светящейся дымки и пыли бежит наружу за 0,5 с, штрихи
    ///   порыва; пока холм встаёт и тело уходит под него — клубы пыли по краю холма и комья (прячут, как
    ///   тело уходит в землю за краем холма).
    /// Сам холм с цветами — VFX_Thicket_DeathHill (лежит до смены арены).
    /// </summary>
    private static void SaveDeathBloom(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DeathBloomName);
        var ground = root.transform;
        const string late = ThicketMasterCombatView.LatePrefix;

        // Добивание: вспышка в кусте — маленькая и короткая.
        var bush = Child(root, ThicketMasterCombatView.BushChild, Vector3.up * 1.5f);
        Flash(bush, kit, "Flash", Vector3.zero, 2.2f, .2f, new Color(BloomGlow.r, BloomGlow.g, BloomGlow.b, .7f), 0f, false);
        var sparkle = Motes(bush, "Sparkle", kit.StarMote, 14, Vector3.zero, .5f, 1.2f, 2.6f, .10f, .18f, .6f, .9f,
            new Color(BloomGold.r, BloomGold.g, BloomGold.b, .95f), 0f, false);
        World(sparkle);

        // Кольцо лепестков и листьев от тела наружу: встают на кольце 0,8 м на высоте 1,4 м, бегут до ~4,5 м.
        var burst = Child(root, "Burst", Vector3.up * 1.4f);
        var burstPetals = Petals(burst, kit, "Petals", 70, Vector3.zero, 1.8f, 2.6f, 0f, 0f, 0f);
        burstPetals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var burstPetalsMain = burstPetals.main; burstPetalsMain.startSize = new ParticleSystem.MinMaxCurve(.16f, .30f);
        burstPetalsMain.startColor = BloomPetalColors();
        RadialPush(burstPetals, .8f, 4.5f, .45f, 2.2f, .5f);
        World(burstPetals); Collide(burstPetals, ground, .02f);
        var burstLeaves = Leaves(burst, ground, kit, "Leaves", 30, Vector3.zero, Vector3.up, 0f, 0f, 0f, 0f);
        RadialPush(burstLeaves, .8f, 4.2f, .45f, 2.1f, .4f);
        World(burstLeaves);
        Wave(root, "Ring", kit.GlowRingThin, 2f * 3.5f * Root2, .5f, .15f, .6f, new Color(BloomGlow.r, BloomGlow.g, BloomGlow.b, .8f), 0f);
        foreach (string side in new[] { ThicketMasterCombatView.LeftChild, ThicketMasterCombatView.RightChild })
        {
            var crown = Child(root, side, Vector3.up * 3.3f);
            var petals = Petals(crown, kit, "Petals", 20, Vector3.zero, 2.5f, 3.2f, 1.2f, 3f, .05f);
            petals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
            World(petals); Collide(petals, ground, .02f);
        }

        // Касание боком: волна цветения до BloomWaveReach.
        float from = 2.0f, reach = BloomWaveReach;
        Wave(root, late + "Edge", kit.GlowRingThin, 2f * reach * Root2, .9f, from / reach, BloomWaveTravel / .9f,
            new Color(BloomGlow.r, BloomGlow.g, BloomGlow.b, 1f), 0f);
        Wave(root, late + "Band", kit.GlowRing, 2f * reach * Root2, .7f, from / reach, BloomWaveTravel / .7f,
            new Color(BloomGold.r, BloomGold.g, BloomGold.b, .3f), .02f);
        Wall(root, late + "Glow Wall", kit.GlowHaze, 40, .6f, from, reach * .97f, BloomWaveTravel, .8f, 1.0f, 1.5f,
            new Color(BloomGlow.r, BloomGlow.g, BloomGlow.b, .28f), new Color(BloomGold.r, BloomGold.g, BloomGold.b, .22f), .4f, 0f);
        // Пыль волны — низкая, мелкая, 0,6 с (ревью 02.10, вечер: «вата» на смерти закрывала тело и героя).
        Wall(root, late + "Dust Wall", kit.Haze, 90, .3f, from, reach * .95f, BloomWaveTravel, .6f, .6f, 1.0f,
            new Color(DustDark.r, DustDark.g, DustDark.b, .34f), new Color(SoilLight.r, SoilLight.g, SoilLight.b, .3f), .1f, 0f);
        var wavePetals = Petals(root, kit, late + "Wall Petals", 80, Vector3.up * .9f, 1.6f, 2.2f, 0f, 0f, .02f);
        wavePetals.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.GlowPetal;
        var wavePetalsMain = wavePetals.main; wavePetalsMain.startSize = new ParticleSystem.MinMaxCurve(.16f, .30f);
        wavePetalsMain.startColor = BloomPetalColors();
        RadialPush(wavePetals, from, reach, BloomWaveTravel, 1.9f, .7f);
        var waveLeaves = Leaves(root, ground, kit, late + "Wall Leaves", 40, Vector3.up * .7f, Vector3.up, 0f, 0f, 0f, 0f);
        RadialPush(waveLeaves, from, reach * .93f, BloomWaveTravel, 2.1f, .6f);
        Streaks(root, late + "Streaks", kit.Wind, 30, .4f, from, reach, BloomWaveTravel, .45f, .35f, .55f,
            new Color(1f, .93f, .9f, .5f), 0f);

        // Холм встаёт, тело уходит под него: низкая короткая пыль по краю холма и комья, пока он растёт.
        float riseFrom = ThicketMasterDeathRules.HillRiseDelay, rise = ThicketMasterDeathRules.HillRiseSeconds;
        var cover = LowPuffs(root, kit, late + "Cover", 60, EllipsePoints("ThicketDeathCoverPoints", 34, HillW, HillL, 0f, false,
            .15f, .4f, 10f, 30f, 941), .8f, 1.8f, .55f, .9f, .5f, .7f, .32f, riseFrom);
        Stream(cover, 60f / rise, rise, 60);
        // Мелкие комья (≤ 0,18 м) и зерно с краёв встающего холма (владелец 03.10, ночь: вместо 26 сколотых многогранников до 0,26 м).
        var clods = Clods(root, ground, kit, late + "Clods", 100, Vector3.up * .1f, Vector3.up, 0f, .1f, 1.6f, 3.4f, .05f, .18f, 1.6f, riseFrom);
        FromPoints(clods, EllipsePoints("ThicketDeathClodPoints", 60, HillW * .95f, HillL * .95f, 0f, false, .05f, .25f, 40f, 75f, 951), false, .3f);
        Shuffle(clods);
        Stream(clods, 100f / (rise * .8f), rise * .8f, 100);
        var grains = Grains(root, ground, kit, late + "Grains", 80, Vector3.up * .1f, Vector3.up, 0f, .1f, 1.4f, 3.2f, riseFrom);
        FromPoints(grains, EllipsePoints("ThicketDeathGrainPoints", 48, HillW, HillL, 0f, false, .05f, .2f, 35f, 75f, 953), false, .4f);
        Shuffle(grains);
        Stream(grains, 80f / (rise * .8f), rise * .8f, 80);
        Save(root);
    }

    /// <summary>Лепестки смерти: 35 % белых, 50 % розовых, 15 % золотых.</summary>
    private static ParticleSystem.MinMaxGradient BloomPetalColors()
    {
        var gradient = new Gradient { mode = GradientMode.Fixed };
        gradient.SetKeys(
            new[] { new GradientColorKey(PetalWhite, .35f), new GradientColorKey(PetalPink, .85f), new GradientColorKey(PetalGold, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>
    /// Пригорок (V11, «лес забирает хозяина»): кучек цветов (по 3–5), розеток листьев сверх кучек, пучков травы,
    /// ростков, комьев свежей земли на рваных пятнах, комьев и камешков в кольце земли у подножия. V13: пригорок
    /// в 1,5 раза больше по площади (6,2 × 7,8 м), кольцо земли шире (подножие пологое) — всего больше в той же густоте.
    /// </summary>
    private const int KnollClusters = 14, KnollExtraLeaves = 8, KnollTufts = 48, HillSprouts = 7, KnollTornLumps = 12,
        HillRimClods = 110, HillRimStones = 10;

    /// <summary>Комья кольца земли — только где основание не выше стольких метров (низ подножия, мха там нет).</summary>
    private const float RimClodMaxFloor = .2f;

    /// <summary>Цветок, м (ведущий 03.10: 0,35–0,9): середина кучки — крупный, вокруг — мельче.</summary>
    private const float KnollFlowerMin = .35f, KnollFlowerMid = .55f, KnollFlowerMax = .9f;

    /// <summary>
    /// Посадка зелени и цветов на склоне (ревью 03.10: стояли по уровню — нижний край висел над мхом): цветок клонится к
    /// склону на долю KnollFlowerLean (половина — стебель всё же тянется вверх) с разбросом до KnollFlowerJitter°;
    /// розетка листьев ложится по склону целиком (секущая через ±KnollLeafReach м — её радиус), разброс до
    /// KnollLeafJitter°, середина утоплена в мох на KnollLeafSink м; мелкий цветок кучки ищет место не круче
    /// KnollSmallFlowerMaxSlope (м на м, ~40°).
    /// </summary>
    private const float KnollFlowerLean = .5f, KnollFlowerJitter = 10f, KnollLeafReach = .3f, KnollLeafJitter = 5f, KnollLeafSink = .05f,
        KnollSmallFlowerMaxSlope = .85f;

    /// <summary>
    /// Место мелкого цветка кучки вокруг середины centre: на угле a и радиусе r, а если там круче
    /// KnollSmallFlowerMaxSlope — тот же радиус с поворотом ±0,3 и ±0,6 рад (первое не крутое; иначе — самое пологое).
    /// </summary>
    private static Vector3 SmallFlowerSpot(Vector3 centre, float a, float r)
    {
        Vector3 best = centre;
        float bestSlope = float.MaxValue;
        for (int j = 0; j < 5; j++)
        {
            float turn = j == 0 ? 0f : ((j + 1) / 2) * .3f * (j % 2 == 1 ? 1f : -1f);
            var p = centre + new Vector3(Mathf.Sin(a + turn) * r, 0f, Mathf.Cos(a + turn) * r);
            float slope = HillSlope(p.x, p.z);
            if (slope < bestSlope) { bestSlope = slope; best = p; }
            if (slope <= KnollSmallFlowerMaxSlope) break;
        }
        return best;
    }

    /// <summary>
    /// Тёплый точечный свет пригорка: над серединой на столько метров (вид сдвигает его к камере на
    /// ThicketMasterDeathRules.KnollLightToCamera), дальность, пиковая яркость (ровный свет — KnollLightRest от неё;
    /// для сравнения фонарь поляны — 0,9 на 3,8 м), тёплый цвет. Яркость по времени ведёт вид (KnollLight).
    /// </summary>
    /// V13: пригорок ниже на 0,5 м — свет ниже на столько же (от вершины до света те же 1,6 м). Голова героя на
    /// вершине выше света — вид поднимает свет над ней и гасит вблизи (ThicketMasterDeathRules.KnollLightLift / Near).
    private const float KnollLightHeight = 2.9f, KnollLightRange = 6.5f, KnollLightPeak = 1.6f;
    private static readonly Color KnollLightColor = new Color(1f, .8f, .58f);

    /// <summary>Палитра цветов пригорка (как куст Ф3: розовые и белые, без неона и жёлтого): пыльная роза и сливки.</summary>
    private static readonly Color[] KnollRose = { new Color(1f, .70f, .80f), new Color(.95f, .62f, .72f), new Color(.98f, .76f, .80f), new Color(.92f, .58f, .70f) };
    private static readonly Color[] KnollIvory = { new Color(1f, .96f, .92f), new Color(.98f, .93f, .86f), new Color(.96f, .90f, .82f) };

    /// <summary>
    /// «Цветущий холм», то, что остаётся. V11 (ревью 03.10, «лес забирает хозяина»): вместо кучи тёмных бурых бугров-
    /// «подушек» с плоскими бумажными цветами — один мягкий мшистый пригорок, который зацветает. Корень — место касания
    /// боком, +Z — взгляд тела; вид ставит задержку всех систем на LandsAt, время — ThicketMasterDeathRules. Всё «вечное» —
    /// меш-частицы с кольцевым буфером PauseUntilReplaced: дорастают и стоят, пока вид не снимет холм (смена арены).
    /// • Пригорок 6,2 × 7,8 м, вершина HillHeight 1,3 м, склон не круче ~34° (V13: по нему ходят) — один рельеф
    ///   ThicketMasterDeathRules.KnollSurface (три сливающихся горба). «Soil» — основание: свежая земля (kit.KnollSoil,
    ///   фактура и тон земли пола), видна кольцом у подножия. «Moss» — мох одной сеткой (kit.KnollMoss, фактура дёрна
    ///   пола, чуть свежее травы пола). Обе части растут одним телом из-под земли (по высоте и вширь — ключи
    ///   ThicketMasterDeathRules.HillRiseHeight / HillRiseWidth) за HillRiseSeconds; по ним же вид поднимает тела.
    /// • Рваные пятна дернины на склонах (цвет вершин мха) и на них комья свежей земли («Torn Soil»); в кольце земли —
    ///   мелкие комья и камешки.
    /// • Цветение с BloomDelay: 14 кучек по 3–5 цветов (0,35–0,9 м, крупный в середине) — Hovl Flower с нормалями к чашке
    ///   на Particles/Simple Lit со светом (как куст Ф3), раскрываются по очереди от вершины к краю за BloomSpread;
    ///   под каждой кучкой розетка листьев, между ними пучки травы и розетки, ростки-завитки — всё со светом
    ///   (kit.KnollGreen). Лепестков и искр немного (тише прежнего).
    /// • «Knoll Light» — тёплый точечный свет над пригорком: загорается с цветением, садится до ровного слабого и горит,
    ///   пока лежит холм (вид, ThicketMasterDeathRules.KnollLight); ореола и дымки нет.
    /// • Герой на холме (V13): идёт по верху — вид кладёт пригорок поверх пола (ThicketMasterCombatView.KnollFloor);
    ///   ничего не оседает, цветы и трава стоят, герой проходит сквозь них.
    /// </summary>
    private static void SaveDeathHill(Kit kit)
    {
        var root = new GameObject(ThicketMasterCombatView.DeathHillName);
        float riseFrom = ThicketMasterDeathRules.HillRiseDelay, rise = ThicketMasterDeathRules.HillRiseSeconds;
        // Ключи кривых роста — те же, по которым вид поднимает тела на пригорок (ThicketMasterDeathRules.KnollFloor).
        var riseY = SampledCurve(ThicketMasterDeathRules.RiseCurve, ThicketMasterDeathRules.HillRiseHeightKeys);
        var riseXZ = SampledCurve(ThicketMasterDeathRules.HillRiseWidthCurve, ThicketMasterDeathRules.HillRiseWidthKeys);

        // Основание — свежая земля; мох — одна сетка того же рельефа, частица стоит в середине холма (растут одним телом).
        if (kit.Hill != null) HillPart(root, kit, "Soil", kit.Hill, kit.KnollSoil, new Color(.97f, .96f, .95f), riseFrom, rise, riseXZ, riseY);
        if (kit.KnollMossMesh != null) HillPart(root, kit, "Moss", kit.KnollMossMesh, kit.KnollMoss, Color.white, riseFrom, rise, riseXZ, riseY);

        // Кольцо земли у подножия: мелкие комья вразброс — только где верх — основание (мха нет) и низко.
        var rimPlaces = new List<Vector3>();
        for (int i = 0; rimPlaces.Count < HillRimClods && i < HillRimClods * 10; i++)
        {
            Vector3 p = HillPoint(Hash01(i, 771) * Mathf.PI * 2f, Mathf.Lerp(.78f, 1.12f, Mathf.Sqrt(Hash01(i, 772))), 0f);
            float top = HillTop(p.x, p.z), floor = HillGround(p.x, p.z);
            if (top > floor + .01f || floor > RimClodMaxFloor) continue;
            rimPlaces.Add(new Vector3(p.x, Mathf.Max(0f, floor) - .015f, p.z));
        }
        var rim = Particles(root, "Rim Clods", Mathf.Max(1, rimPlaces.Count), rise * .7f, rise * .7f, 0f, 0f, .05f, .15f, riseFrom + rise * .3f);
        FromPoints(rim, PlacePoints("ThicketDeathRimPoints", rimPlaces), false, 0f);
        RandomTumble(rim);
        var rimMain = rim.main; rimMain.startColor = new ParticleSystem.MinMaxGradient(GroundDry, GroundDamp);
        var rimGrow = rim.sizeOverLifetime; rimGrow.enabled = true;
        rimGrow.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.RiseCurve, 5));
        EarthMeshes(rim, kit, kit.Clods, ParticleSystemRenderSpace.Local);
        rim.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.KnollSoil;
        Persist(rim);

        var stonePlaces = new List<Vector3>();
        for (int i = 0; i < HillRimStones; i++)
        {
            Vector3 p = HillPoint(Hash01(i, 781) * Mathf.PI * 2f, Mathf.Lerp(.9f, 1.12f, Hash01(i, 782)), 0f);
            stonePlaces.Add(new Vector3(p.x, Mathf.Max(0f, HillGround(p.x, p.z)) + .01f, p.z));
        }
        var stones = Particles(root, "Rim Stones", stonePlaces.Count, rise * .6f, rise * .6f, 0f, 0f, .1f, .22f, riseFrom + rise * .4f);
        FromPoints(stones, PlacePoints("ThicketDeathStonePoints", stonePlaces), false, 0f);
        RandomTumble(stones);
        var stonesMain = stones.main; stonesMain.startColor = new ParticleSystem.MinMaxGradient(RockLight, RockDark);
        var stonesGrow = stones.sizeOverLifetime; stonesGrow.enabled = true;
        stonesGrow.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.RiseCurve, 5));
        EarthMeshes(stones, kit, kit.Pebbles, ParticleSystemRenderSpace.Local);
        Persist(stones);

        // Рваные пятна: на бурой дернине (цвет вершин мха) — низкие комья свежей земли, встают с холмом.
        var tornPlaces = new List<Vector3>();
        for (int i = 0; tornPlaces.Count < KnollTornLumps && i < 1200; i++)
        {
            Vector3 p = HillPoint(Hash01(i, 821) * Mathf.PI * 2f, Mathf.Sqrt(Hash01(i, 822)) * .9f, 0f);
            float t = KnollTop(p.x, p.z);
            if (KnollTorn(p.x, p.z, t) < .55f) continue;
            bool free = true;
            foreach (var q in tornPlaces) free &= (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) > .45f * .45f;
            if (free) tornPlaces.Add(new Vector3(p.x, HillTop(p.x, p.z) - .025f, p.z));
        }
        if (tornPlaces.Count > 0 && kit.RidgeSwells != null && kit.RidgeSwells.Length > 0 && kit.RidgeSwells[0] != null)
        {
            var torn = Particles(root, "Torn Soil", tornPlaces.Count, rise * .5f, rise * .5f, 0f, 0f, 1f, 1f, riseFrom + rise * .55f);
            FromPoints(torn, HillPlacePoints("ThicketDeathTornPoints", tornPlaces), false, 0f);
            var tornMain = torn.main;
            tornMain.startSize3D = true;
            tornMain.startSizeX = new ParticleSystem.MinMaxCurve(.2f, .36f);
            tornMain.startSizeY = new ParticleSystem.MinMaxCurve(.05f, .09f);
            tornMain.startSizeZ = new ParticleSystem.MinMaxCurve(.2f, .36f);
            tornMain.startColor = new ParticleSystem.MinMaxGradient(GroundDry, GroundDamp);
            Upright(torn, .12f);
            var tornGrow = torn.sizeOverLifetime; tornGrow.enabled = true;
            tornGrow.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.RiseCurve, 5));
            EarthMeshes(torn, kit, kit.RidgeSwells, ParticleSystemRenderSpace.Local);
            torn.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.KnollSoil;
            Persist(torn);
        }
        else Note("пригорок: рваных пятен под комья не нашлось");

        // Цветы кучками: середины кучек — по верху от вершины к краю (только мох, не на рваной дернине), вокруг 2–4
        // мельче; кучка — одного рода (розовые или светлые). Порядок цветения — по кучкам от вершины, в кучке подряд.
        var centres = new List<Vector3>();
        foreach (var c in HillPlaces(KnollClusters + 6, 0f, .76f, 1.05f, 0f, 791, 1.1f))
            if (centres.Count < KnollClusters && KnollTorn(c.x, c.z, KnollTop(c.x, c.z)) < .2f) centres.Add(c);
        if (centres.Count < KnollClusters) Note($"пригорок: кучек цветов {centres.Count} из {KnollClusters}");
        var big = new[] { new List<Vector3>(), new List<Vector3>() };
        var small = new[] { new List<Vector3>(), new List<Vector3>() };
        var bigLean = new[] { new List<Vector3>(), new List<Vector3>() };
        var smallLean = new[] { new List<Vector3>(), new List<Vector3>() };
        var bigOrder = new[] { new List<int>(), new List<int>() };
        var smallOrder = new[] { new List<int>(), new List<int>() };
        var clusterTimes = new List<float>();
        int order = 0;
        for (int c = 0; c < centres.Count; c++)
        {
            int family = Hash01(c, 796) < .56f ? 0 : 1;
            int count = 3 + Mathf.Min(2, Mathf.FloorToInt(Hash01(c, 795) * 3f));
            float turn = Hash01(c, 797) * Mathf.PI * 2f;
            clusterTimes.Add(order);
            for (int k = 0; k < count; k++)
            {
                Vector3 p = centres[c];
                if (k > 0)
                {
                    float a = turn + (k - 1 + .3f * Hash01(c * 8 + k, 798)) / (count - 1) * Mathf.PI * 2f;
                    float r = Mathf.Lerp(.28f, .48f, Hash01(c * 8 + k, 799));
                    p = SmallFlowerSpot(centres[c], a, r);
                }
                p.y = HillTop(p.x, p.z) - .06f;
                // Цветок клонится к склону на KnollFlowerLean (ревью 03.10: стоял по уровню — край чашки висел над склоном).
                var lean = SlopeDirection(HillNormal(p.x, p.z, k == 0 ? .3f : .2f), KnollFlowerLean, KnollFlowerJitter, order, 841);
                (k == 0 ? big : small)[family].Add(p);
                (k == 0 ? bigLean : smallLean)[family].Add(lean);
                (k == 0 ? bigOrder : smallOrder)[family].Add(order++);
            }
        }
        int total = order;
        for (int i = 0; i < clusterTimes.Count; i++)
            clusterTimes[i] = ThicketMasterDeathRules.FlowerStart((int)clusterTimes[i], total, 0f) - ThicketMasterDeathRules.BloomDelay;
        float span = FlowerSpan(kit);
        HillFlowers(root, kit, "Flowers Rose", kit.HillFlowerRose, HillPlacePoints("ThicketDeathRosePoints", big[0], bigLean[0]), bigOrder[0], total,
            KnollRose, KnollFlowerMid / span, KnollFlowerMax / span);
        HillFlowers(root, kit, "Flowers Rose Small", kit.HillFlowerRose, HillPlacePoints("ThicketDeathRoseSmallPoints", small[0], smallLean[0]), smallOrder[0], total,
            KnollRose, KnollFlowerMin / span, KnollFlowerMid / span);
        HillFlowers(root, kit, "Flowers Ivory", kit.HillFlowerIvory, HillPlacePoints("ThicketDeathIvoryPoints", big[1], bigLean[1]), bigOrder[1], total,
            KnollIvory, KnollFlowerMid / span, KnollFlowerMax / span);
        HillFlowers(root, kit, "Flowers Ivory Small", kit.HillFlowerIvory, HillPlacePoints("ThicketDeathIvorySmallPoints", small[1], smallLean[1]), smallOrder[1], total,
            KnollIvory, KnollFlowerMin / span, KnollFlowerMid / span);

        float bloomFrom = ThicketMasterDeathRules.BloomDelay, spread = ThicketMasterDeathRules.BloomSpread;
        // Розетки листьев: под каждой кучкой (раскрываются чуть раньше её цветов) и несколько между кучками. Лежат по
        // склону (ревью 03.10: стояли по уровню — на склонах 29–46° нижний край розетки висел на 0,25–0,35 м над мхом).
        var leafPlaces = new List<Vector3>();
        var leafTimes = new List<float>();
        for (int c = 0; c < centres.Count; c++)
        {
            leafPlaces.Add(new Vector3(centres[c].x, HillTop(centres[c].x, centres[c].z) - KnollLeafSink, centres[c].z));
            leafTimes.Add(Mathf.Max(0f, clusterTimes[c] - .15f));
        }
        foreach (var p in HillPlaces(KnollExtraLeaves, .25f, .85f, .9f, KnollLeafSink, 805, 1.1f))
        {
            bool clear = true;
            foreach (var c in centres) clear &= (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z) > .6f * .6f;
            if (!clear) continue;
            leafPlaces.Add(p);
            leafTimes.Add(spread * Hash01(leafPlaces.Count, 806));
        }
        var leafIndex = Enumerable.Range(0, leafPlaces.Count).OrderBy(i => leafTimes[i]).ToList();
        leafPlaces = leafIndex.Select(i => leafPlaces[i]).ToList();
        leafTimes = leafIndex.Select(i => leafTimes[i]).ToList();
        var leafLean = new List<Vector3>(leafPlaces.Count);
        for (int i = 0; i < leafPlaces.Count; i++)
            leafLean.Add(SlopeDirection(HillNormal(leafPlaces[i].x, leafPlaces[i].z, KnollLeafReach), 1f, KnollLeafJitter, i, 851));
        var leaves = Flora(root, kit, "Leaves", kit.KnollLeaves, HillPlacePoints("ThicketDeathLeafPoints", leafPlaces, leafLean), leafPlaces.Count,
            .3f, .45f, 0f, new Color(.46f, .64f, .24f), new Color(.32f, .50f, .17f), bloomFrom, true);
        TimedBursts(leaves, leafTimes);
        var leavesMain = leaves.main; leavesMain.duration = spread + .2f;

        // Трава пучками по всему мху, между кучками: встаёт с первыми цветами.
        var tufts = HillPlaces(KnollTufts, .08f, .9f, .42f, .02f, 801, 1.6f);
        var grass = Flora(root, kit, "Grass", kit.KnollTuft, HillPlacePoints("ThicketDeathTuftPoints", tufts), tufts.Count,
            .26f, .48f, .18f, new Color(.56f, .74f, .28f), new Color(.38f, .56f, .18f), bloomFrom - .25f);
        Bursts(grass, tufts.Count, spread);

        // Ростки-завитки молодой зелени.
        if (kit.Sprouts != null && kit.Sprouts.Length > 0 && kit.Sprouts[0] != null)
        {
            var sproutPlaces = HillPlaces(HillSprouts, .2f, .8f, .9f, .04f, 811);
            var sprouts = Particles(root, "Sprouts", sproutPlaces.Count, .7f, .7f, 0f, 0f, .4f, .62f, bloomFrom + .3f);
            FromPoints(sprouts, HillPlacePoints("ThicketDeathSproutPoints", sproutPlaces), false, 0f);
            Bursts(sprouts, sproutPlaces.Count, spread * .8f);
            var sproutsMain = sprouts.main;
            sproutsMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.52f, .70f, .25f), new Color(.40f, .58f, .18f));
            Upright(sprouts, .2f);
            var sproutsGrow = sprouts.sizeOverLifetime; sproutsGrow.enabled = true;
            sproutsGrow.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.FlowerGrow, ThicketMasterDeathRules.FlowerGrowSeconds, .7f, 6));
            EarthMeshes(sprouts, kit, kit.Sprouts, ParticleSystemRenderSpace.Local);
            sprouts.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.KnollGreen;
            Persist(sprouts);
        }

        // Пока цветёт: редкие лепестки сверху и немного золотых искр (тише V10: без свечения, вдвое меньше).
        float drift = ThicketMasterDeathRules.PetalDriftSeconds;
        var falling = Petals(root, kit, "Falling", 30, Vector3.up * 3f, 2.2f, 3.0f, 0f, .3f, bloomFrom);
        falling.transform.localRotation = Quaternion.identity;
        var fallingMain = falling.main; fallingMain.gravityModifier = .1f; fallingMain.startColor = BloomPetalColors();
        fallingMain.startSize = new ParticleSystem.MinMaxCurve(.1f, .18f);
        var fallingShape = falling.shape; fallingShape.enabled = true; fallingShape.shapeType = ParticleSystemShapeType.Box;
        fallingShape.scale = new Vector3(2f * HillW, .5f, 2f * HillL);
        Stream(falling, 7.5f, drift, 30);
        Collide(falling, root.transform, .02f);
        var motes = Motes(root, "Motes", kit.StarMote, 14, Vector3.up * 1.4f, HillW * .6f, .25f, .6f, .07f, .12f, 1.6f, 2.2f,
            new Color(BloomGold.r, BloomGold.g, BloomGold.b, .5f), bloomFrom, true);
        Stream(motes, 3.5f, drift, 14);

        // Свет пригорка: яркость по времени ставит вид (ThicketMasterDeathRules.KnollLight × пик).
        var glow = Child(root, ThicketMasterCombatView.KnollLightChild, Vector3.up * KnollLightHeight);
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = KnollLightColor;
        light.range = KnollLightRange;
        light.intensity = KnollLightPeak;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.Auto;
        Save(root);
    }

    /// <summary>
    /// Поперечник Hovl Flower.fbx, м (~2,3): размер частицы цветка = метры / он. У копии пригорка (kit.KnollFlower) ось
    /// чашки — −Z, поперечник — по X и Y.
    /// </summary>
    private static float FlowerSpan(Kit kit)
    {
        var mesh = kit.KnollFlower != null ? kit.KnollFlower : kit.FlowerMesh;
        if (mesh == null) return 2.3f;
        var b = mesh.bounds;
        return Mathf.Max(.01f, Mathf.Max(b.size.x, kit.KnollFlower != null ? b.size.y : b.size.z));
    }

    /// <summary>Меш-частица наугад повёрнута во всех осях (комья, камешки).</summary>
    private static void RandomTumble(ParticleSystem particles)
    {
        var main = particles.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    }

    /// <summary>
    /// Зелень пригорка (листья, трава): по месту на частицу, меш mesh на kit.KnollGreen (свет), стоит с наклоном до ±tilt,
    /// разворот наугад, вырастает за 0,5 с (RiseCurve); цвет — между light и dark. Залпы ставит зовущий. aligned — меш
    /// верхом на −Z ложится вдоль нормали своего места (AlignedSpin; наклон и разброс — в нормалях точек), tilt не нужен.
    /// </summary>
    private static ParticleSystem Flora(GameObject root, Kit kit, string name, Mesh mesh, Mesh points, int count, float sizeMin, float sizeMax,
        float tilt, Color light, Color dark, float delay, bool aligned = false)
    {
        var flora = Particles(root, name, Mathf.Max(1, count), .5f, .5f, 0f, 0f, sizeMin, sizeMax, delay);
        FromPoints(flora, points, aligned, 0f);
        var main = flora.main;
        main.startColor = new ParticleSystem.MinMaxGradient(light, dark);
        if (aligned) AlignedSpin(flora);
        else Upright(flora, tilt);
        var grow = flora.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.RiseCurve, 5));
        EarthMeshes(flora, kit, new[] { mesh }, ParticleSystemRenderSpace.Local);
        var renderer = flora.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = kit.KnollGreen;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        Persist(flora);
        return flora;
    }

    /// <summary>Часть пригорка одной частицей в середине холма (основание, ячейка мха): растёт по осям riseXZ / riseY, свой материал.</summary>
    private static void HillPart(GameObject root, Kit kit, string name, Mesh mesh, Material material, Color color, float delay, float rise,
        AnimationCurve riseXZ, AnimationCurve riseY)
    {
        if (mesh == null) return;
        var part = Particles(root, name, 1, rise, rise, 0f, 0f, 1f, 1f, delay);
        var main = part.main;
        main.startColor = color;
        Upright(part, 0f);
        var shape = part.shape; shape.enabled = false;
        part.transform.localPosition = Vector3.down * HillSink;
        GrowAxes(part, riseXZ, riseY);
        EarthMeshes(part, kit, new[] { mesh }, ParticleSystemRenderSpace.Local);
        part.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        Persist(part);
    }

    /// <summary>
    /// Цветы пригорка: по месту на частицу (места — в порядке распускания), каждый раскрывается за FlowerGrowSeconds с
    /// перелётом (FlowerGrow) в свой миг FlowerStart — залпами по одному; размер sizeMin…sizeMax (доли поперечника
    /// Flower.fbx), разворот наугад. Меш — kit.KnollFlower (нормали к чашке, ось чашки −Z), материал со светом; цветок
    /// ставится выравниванием по направлению места — наклон к склону и разброс (до ±10°) в нормалях точек (SaveDeathHill).
    /// </summary>
    private static void HillFlowers(GameObject root, Kit kit, string name, Material material, Mesh points, List<int> order, int total,
        Color[] palette, float sizeMin, float sizeMax)
    {
        if (order.Count == 0) return;
        float grow = ThicketMasterDeathRules.FlowerGrowSeconds;
        var flowers = Particles(root, name, order.Count, grow, grow, 0f, 0f, sizeMin, sizeMax, ThicketMasterDeathRules.BloomDelay);
        var mesh = kit.KnollFlower != null ? kit.KnollFlower : kit.FlowerMesh;
        bool aligned = mesh != null && mesh == kit.KnollFlower;
        FromPoints(flowers, points, aligned, 0f);
        var main = flowers.main;
        main.duration = ThicketMasterDeathRules.BloomSpread + .1f;
        var gradient = new Gradient { mode = GradientMode.Fixed };
        var keys = new GradientColorKey[palette.Length];
        for (int i = 0; i < palette.Length; i++) keys[i] = new GradientColorKey(palette[i], (i + 1f) / palette.Length);
        gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        if (aligned) AlignedSpin(flowers);
        else
        {
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(-.21f, .21f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-.21f, .21f);
        }
        var times = new float[order.Count];
        for (int i = 0; i < order.Count; i++)
            times[i] = Mathf.Max(0f, ThicketMasterDeathRules.FlowerStart(order[i], total, 0f) - ThicketMasterDeathRules.BloomDelay);
        TimedBursts(flowers, times);
        var size = flowers.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, SampledCurve(ThicketMasterDeathRules.FlowerGrow, grow, grow, 7));
        var renderer = flowers.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        renderer.mesh = mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.sharedMaterial = mesh != null ? material : kit.GlowPetal;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        Persist(flowers);
    }

    /// <summary>
    /// Меш-частица с выравниванием по направлению места (shape.alignToDirection, −Z меша вдоль нормали точки): поворот
    /// 2D наугад — разворот вокруг своей оси (так у шипов Вендиго и прорастания: «поворот вокруг оси случайный»).
    /// </summary>
    private static void AlignedSpin(ParticleSystem particles)
    {
        var main = particles.main;
        main.startRotation3D = false;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    }

    /// <summary>Меш-частица стоит прямо: поворот 3D, наклон до ±tilt рад, разворот вокруг вертикали наугад.</summary>
    private static void Upright(ParticleSystem particles, float tilt)
    {
        var main = particles.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(-tilt, tilt);
        main.startRotationY = tilt > 0f ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(-tilt, tilt);
    }

    /// <summary>Рост по осям: X и Z — xz, Y — y (все три кривой — один режим модуля).</summary>
    private static void GrowAxes(ParticleSystem particles, AnimationCurve xz, AnimationCurve y)
    {
        var size = particles.sizeOverLifetime; size.enabled = true; size.separateAxes = true;
        size.x = new ParticleSystem.MinMaxCurve(1f, xz);
        size.y = new ParticleSystem.MinMaxCurve(1f, y);
        size.z = new ParticleSystem.MinMaxCurve(1f, xz);
    }

    /// <summary>
    /// Частица дорастает за свою жизнь и остаётся на последнем кадре (кольцевой буфер PauseUntilReplaced,
    /// новых частиц после залпа нет) — холм лежит, пока вид его не снимет.
    /// </summary>
    private static void Persist(ParticleSystem particles)
    {
        var main = particles.main;
        main.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
    }

    /// <summary>Рендер частиц Unity берёт не больше 4 мешей (больше — предупреждение «Too many meshes passed to SetMeshes»
    /// и обрезка до первых 4); отдаём первые 4 сами — вид тот же, без предупреждения.</summary>
    private static Mesh[] AtMostFourMeshes(Mesh[] meshes)
    {
        if (meshes == null || meshes.Length <= 4) return meshes;
        var four = new Mesh[4];
        System.Array.Copy(meshes, four, 4);
        return four;
    }

    /// <summary>count залпов по одной частице, равномерно за span с (первый — сразу).</summary>
    private static void Bursts(ParticleSystem particles, int count, float span)
    {
        var times = new float[Mathf.Max(1, count)];
        for (int i = 0; i < times.Length; i++) times[i] = count > 1 ? span * i / (count - 1f) : 0f;
        TimedBursts(particles, times);
        var main = particles.main;
        main.duration = span + .1f;
    }

    /// <summary>Больше залпов у системы частиц Unity не держит («Burst index … too high, max 8»).</summary>
    private const int MaxBurstsPerSystem = 8;

    /// <summary>
    /// По одной частице в моменты times (по возрастанию). Подряд идущие моменты сливаются в залп с повторами
    /// (не больше <see cref="MaxBurstsPerSystem"/> залпов): первый и последний момент группы точные, между ними —
    /// поровну. До 16 моментов — все точные.
    /// </summary>
    private static void TimedBursts(ParticleSystem particles, IList<float> times)
    {
        int n = times.Count;
        int per = Mathf.Max(1, (n + MaxBurstsPerSystem - 1) / MaxBurstsPerSystem);
        var bursts = new List<ParticleSystem.Burst>(MaxBurstsPerSystem);
        for (int from = 0; from < n; from += per)
        {
            int count = Mathf.Min(per, n - from);
            float first = times[from], last = times[from + count - 1];
            float interval = count > 1 ? Mathf.Max(.0001f, (last - first) / (count - 1f)) : .01f;
            bursts.Add(new ParticleSystem.Burst(first, (short)1, count, interval));
        }
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(bursts.ToArray());
    }

    /// <summary>Кривая по доле жизни 0…1 из функции f(x), samples точек (отрезки линейные).</summary>
    private static AnimationCurve SampledCurve(System.Func<float, float> f, int samples)
    {
        var pairs = new float[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float x = i / (samples - 1f);
            pairs[i * 2] = x;
            pairs[i * 2 + 1] = f(x);
        }
        return Curve(pairs);
    }

    /// <summary>Кривая по доле жизни life с из функции возраста f(секунды) на отрезке 0…span с.</summary>
    private static AnimationCurve SampledCurve(System.Func<float, float> f, float span, float life, int samples)
    {
        var pairs = new float[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float x = i / (samples - 1f);
            pairs[i * 2] = Mathf.Min(1f, span * x / life);
            pairs[i * 2 + 1] = f(span * x);
        }
        return Curve(pairs);
    }
}
