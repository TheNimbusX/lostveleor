using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Крушение «холодное железо» — база (06.10). Целевой кадр владельца:
/// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/base-v1.webp (кратер с камнями,
/// крупные звенья цепи впечатаны в полосу и светятся холодным голубым #4FA8FF…#9CD8FF с белым
/// жаром, осколки камня и железа по краям). Размер — по числам Sim (круг ImpactRadius, полоса
/// 1,5 м до WallEnd), без молний (свет — из трещин и звеньев), без пены, бирюзы, красного,
/// оранжевого и золота. Вид — Game.View/PelagVfxController.WreckIron*.cs, числа —
/// PelagWreckIronRules.
///
/// СОБРАНО НА ПАКАХ И НАШЕМ (V6 — разбор V5 06.10: звенья читались голубыми неоновыми трубками на
/// траве, камни — бежевой картошкой, кратер не читался):
///  • камни — свои гранёные меши (Slabs.cs: плита, осколок, клин; блоки и комья — гранёный кусок
///    WreckIronChunk) с фактурой камня арены B, приглушённой к серо-бурому, на нашем шейдере
///    Razlom/Wreck Iron: большинство — плиты торчком с наклоном наружу (корона вокруг якоря и ряды
///    вдоль полосы), толстый тёмный контур (оболочка по сглаженной нормали + UnitOutlineMask);
///    железные осколки (немного) — свой меш-плита на том же шейдере;
///  • звенья — свой меш WreckIronLink (овал 1,25 м, прут 0,19 м) в режиме «звено»: тёмное железо
///    #2A2F36 со стальными бликами и толстым контуром, вдавлено в паз; голубой #4FA8FF → #9CD8FF и
///    белые блики — только изнутри: внутренняя сторона прута, щель у земли, жилы-трещины Hovl (Crack4);
///  • разбитая земля круга и полосы — Razlom/Wreck Iron Ground: трещины пака Hovl (Crack4 — сеть,
///    Crater40 — тёмная воронка, Crater19 — светящиеся трещины из-под якоря), шум CFXR; паз под каждым
///    звеном со светом из дыры звена; свет в полосе — у оси, без молний;
///  • летящие куски — свой гранёный меш WreckIronChunk меш-частицами (камень, ком земли, железо
///    цветом частицы); мелочь — копии CFXR: «debris unlit 3x3», пыль «smoke cloud x4 ab blurred»,
///    искры и росчерк «stretch trait hdr ab», вспышка «proc glow soft hdr ab» с _HdrMultiply.
///
/// V7 (06.10, разбор владельца «земля по краям и форма слишком чёткая»; раньше — «кругло-ровные, потом
/// ровный прямоугольник»): ни одного ровного края. Кромка круга — неровные кучки плит и мелочи с
/// промежутками, край полосы ходит шумом (PelagWreckIronRules.LaneEdge), куски кучками разного размера,
/// крупные вывернуты наружу; земля — рваное пятно Hovl Crater2 вместо диска, пятна Crater2 вдоль края
/// полосы и по кромке, звезда трещин Hovl «Crack» за кромку круга, трещины Crack4 за край полосы, рваные
/// концы полосы (шейдер); брызги земли — копия CFXR «debris flat unlit 3x3» (слой «Spray»); контур камней
/// тоньше. Звенья, свет и тайминг по фронту Sim — без изменений.
///
/// Префабы: VFX_Pelag_WreckIron_Slam (удар оземь: «Ground» — меш пишет вид, «Links/L0…3»,
/// «Slabs/S00…», «Rubble/R00…», «Irons/I00…» — куски, которые вид ставит и ведёт; «Shards», «Dust», «Sparks» —
/// выбрасывает вид; «Light» — холодный свет), _Streak (росчерк маха у головы якоря), _Hit (искра
/// железа на теле), _Knock (камешки и пыль у ног сбитого).
///
/// МИГРАЦИЯ ПО ВЕРСИИ: сборка идёт, только если своих ассетов нет или поднята <see cref="Version"/>
/// (userData .meta префаба удара). Сохраняются только свои ассеты (SaveAssetIfDirty, префабы —
/// SaveAsPrefabAsset), общего SaveAssets нет (razlom-animator-builder-saveassets); библиотека
/// пишется, только если наши записи поменялись. Ассеты «морской пены» (PelagWreckFoamVfxSetup)
/// не трогаются: на них остаются формы Волнорез и Девятый вал до своей переделки.
/// Любая правка сборки — поднять Version.
/// </summary>
public static partial class PelagWreckIronVfxSetup
{
    /// <summary>Версия сборки ассетов Крушения «холодное железо». Поднимать при любой правке сборки.</summary>
    private const int Version = 7;
    private static readonly string Revision = "PelagWreckIronV" + Version;

    private const string Root = "Assets/Resources/VFX/Pelag";
    private const string PrefabFolder = Root + "/Prefabs";
    private const string MaterialFolder = Root + "/Materials";
    private const string GeometryFolder = Root + "/Geometry";
    private const string LibraryPath = Root + "/AbilityVfxLibrary.asset";
    private const string CfxrGraphics = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/";
    private const string HovlTextures = "Assets/Hovl Studio/HSFiles/Textures/";
    private const string MeadowFolder = "Assets/Resources/Environment/Meadow/";

    // Чужое — только чтение (V6: от камня арены берётся только фактура).
    private const string RockBMaterial = MeadowFolder + "ArenaCreatingRockB_Surface.mat";
    private const string LaneCrackTexture = HovlTextures + "Crack4.png";
    private const string CraterDirtTexture = HovlTextures + "Crater40.png";
    private const string CraterCrackTexture = HovlTextures + "Crater19.png";
    // V7: рваное пятно разбитой земли, рисованная звезда трещин (паки Hovl).
    private const string SplatTexture = HovlTextures + "Crater2.png";
    private const string StarTexture = "Assets/Hovl Studio/Magic effects pack/Textures/Crack.png";
    private const string NoiseTexture = CfxrGraphics + "cfxr perlin mid.png";
    private const string CfxrDebris = CfxrGraphics + "cfxr debris unlit 3x3 ab.mat";
    private const string CfxrDirt = CfxrGraphics + "cfxr debris flat unlit 3x3 ab.mat";
    private const string CfxrDust = CfxrGraphics + "cfxr smoke cloud x4 ab blurred.mat";
    private const string CfxrStreak = CfxrGraphics + "cfxr stretch trait hdr ab.mat";
    private const string CfxrGlow = CfxrGraphics + "cfxr proc glow soft hdr ab.mat";
    private const string SolidShaderName = "Razlom/Wreck Iron";
    private const string GroundShaderName = "Razlom/Wreck Iron Ground";

    public const string SlamName = "VFX_Pelag_WreckIron_Slam";
    public const string StreakName = "VFX_Pelag_WreckIron_Streak";
    public const string HitName = "VFX_Pelag_WreckIron_Hit";
    public const string KnockName = "VFX_Pelag_WreckIron_Knock";
    private const string IronMeshName = "WreckIronShard";

    private const string StoneMaterialName = "M_WreckIron_Stone";
    private const string IronMaterialName = "M_WreckIron_Iron";
    private const string LinkMaterialName = "M_WreckIron_Link";
    private const string GroundMaterialName = "M_WreckIron_Ground";
    private const string DebrisMaterialName = "M_WreckIron_Debris";
    private const string DustMaterialName = "M_WreckIron_Dust";
    private const string SparkMaterialName = "M_WreckIron_Spark";
    private const string StreakMaterialName = "M_WreckIron_Streak";
    private const string FlashMaterialName = "M_WreckIron_Flash";
    private const string ChunkMaterialName = "M_WreckIron_Chunk";
    private const string DirtMaterialName = "M_WreckIron_Dirt";

    private static readonly string[] OwnPrefabs = { SlamName, StreakName, HitName, KnockName };
    private static readonly string[] OwnMaterials =
    {
        StoneMaterialName, IronMaterialName, LinkMaterialName, GroundMaterialName, DebrisMaterialName,
        DustMaterialName, SparkMaterialName, StreakMaterialName, FlashMaterialName, ChunkMaterialName, DirtMaterialName
    };

    /// <summary>Детей-плит, блоков/комьев и железных осколков в префабе удара (вид берёт по надобности).</summary>
    public const int SlabSlots = 42, RubbleSlots = 60, IronSlots = 12;

    private static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";
    private static string MaterialPath(string name) => MaterialFolder + "/" + name + ".mat";
    private static string MeshPath(string name) => GeometryFolder + "/" + name + ".asset";

    // Холодный голубой из кадра (#4FA8FF … #9CD8FF), белый жар; земля, камень, сталь.
    // Насыщенный холодный голубой: тонмаппинг боя выбеливает яркое — синий канал ведёт, красный почти пуст.
    // V5: на экране ядро — #4FA8FF, жар — #9CD8FF (разбор владельца 06.10), белый — только блики звеньев.
    // Значения HDR-цветов идут в шейдер как есть (линейные): проба 06.10 — (.31,.66,1) давала бледный
    // серо-голубой, поэтому синий канал ведёт, красный почти пуст.
    private static readonly Color Sky = new Color(.08f, .38f, 1f);
    private static readonly Color SkyPale = new Color(.34f, .68f, 1f);
    private static readonly Color HotWhite = new Color(.6f, .85f, 1f);
    // Камень (V6) — серо-бурый, темнее и серее V5 (бежевая «картошка»), тень тёплая и тёмная.
    private static readonly Color StoneTint = new Color(.47f, .48f, .52f);
    private static readonly Color StoneShade = new Color(.34f, .29f, .27f);
    private const float StoneSaturation = .35f;
    private static readonly Color EarthClod = new Color(.42f, .28f, .18f);
    private static readonly Color Steel = new Color(.20f, .22f, .26f);
    private static readonly Color SteelShade = new Color(.30f, .32f, .44f);
    // Звено (V6): тёмное железо (#2A2F36 на экране под светом арены), стальные блики по свету.
    private static readonly Color LinkIron = new Color(.14f, .155f, .18f);
    private static readonly Color SteelHighlight = new Color(.42f, .47f, .55f);
    // V7: земля разбита, а не залита тушью — теплее и прозрачнее (пятна Hovl иначе читались кляксами).
    private static readonly Color DarkEarth = new Color(.13f, .085f, .06f);
    private static readonly Color Outline = new Color(.05f, .035f, .035f);
    private static readonly Color ChipStone = new Color(.46f, .40f, .34f);
    private static readonly Color ChipIron = new Color(.22f, .25f, .30f);
    private static readonly Color DustBrown = new Color(.42f, .34f, .26f, .55f);

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Migrate;

    /// <summary>Загрузка редактора: собрать, только если ассетов нет или версия сменилась.</summary>
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Install(false);
    }

    [MenuItem("Разлом/Pelag VFX/Крушение: подключить «холодное железо» (база)")]
    public static void Install() => Install(false);

    [MenuItem("Разлом/Pelag VFX/Крушение: пересобрать «холодное железо» (база)")]
    public static void Rebuild() => Install(true);

    public static void Install(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>(LibraryPath);
        if (library == null || library.Entries == null) return;
        if (Shader.Find(SolidShaderName) == null || Shader.Find(GroundShaderName) == null
            || AssetDatabase.LoadAssetAtPath<Material>(RockBMaterial) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(LaneCrackTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(CraterDirtTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(CraterCrackTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexture) == null
            || AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexture) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrDirt) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrDebris) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrDust) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrStreak) == null
            || AssetDatabase.LoadAssetAtPath<Material>(CfxrGlow) == null)
        {
            Debug.LogWarning("[wreck-iron-setup] Нет шейдеров Razlom/Wreck Iron, фактуры камня арены, трещин Hovl или материалов CFXR — «холодное железо» не собрано.");
            return;
        }
        if (force || !UpToDate()) Build();

        bool ok = true;
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronSlam, PrefabPath(SlamName), 2);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronStreak, PrefabPath(StreakName), 3);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronHit, PrefabPath(HitName), 8);
        ok &= PelagWhirlwindVfxSetup.Bind(library, PelagVfxId.WreckIronKnock, PrefabPath(KnockName), 6);
        if (!ok) Debug.LogWarning("[wreck-iron-setup] Не все префабы «холодного железа» нашлись — недостающие записи библиотеки не созданы.");
        AssetDatabase.SaveAssetIfDirty(library);
    }

    /// <summary>Все свои ассеты на месте и собраны этой версией.</summary>
    private static bool UpToDate()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath(SlamName));
        if (importer == null || importer.userData != Revision) return false;
        foreach (string name in OwnPrefabs)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) return false;
        foreach (string name in OwnMaterials)
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name)) == null) return false;
        foreach (string name in SlabMeshNames)
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(name)) == null) return false;
        return AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(IronMeshName)) != null
            && AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(LinkMeshName)) != null
            && AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(ChunkMeshName)) != null;
    }

    private static void Build()
    {
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Prefabs");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Materials");
        PelagWhirlwindVfxSetup.EnsureFolder(Root, "Geometry");
        var kit = new Kit
        {
            Stone = StoneMaterial(), Iron = IronMaterial(), Link = LinkMaterial(), Ground = GroundMaterial(),
            Debris = DebrisMaterial(), Dust = DustMaterial(), Spark = SparkMaterial(), Streak = StreakMaterial(),
            Flash = FlashMaterial(), Chunk = ChunkMaterial(), Dirt = DirtMaterial(), IronMesh = IronShardMesh(),
            LinkMesh = IronLinkMesh(), ChunkMesh = IronChunkMesh(), SlabMeshes = SlabMeshes()
        };
        // Только свои материалы и меш — на диск; префабы SaveAsPrefabAsset пишет сам.
        foreach (Material m in new[] { kit.Stone, kit.Iron, kit.Link, kit.Ground, kit.Debris, kit.Dust, kit.Spark, kit.Streak, kit.Flash, kit.Chunk, kit.Dirt })
            AssetDatabase.SaveAssetIfDirty(m);
        foreach (Mesh mesh in new[] { kit.IronMesh, kit.LinkMesh, kit.ChunkMesh })
            AssetDatabase.SaveAssetIfDirty(mesh);
        foreach (Mesh mesh in kit.SlabMeshes)
            AssetDatabase.SaveAssetIfDirty(mesh);

        SaveSlamPrefab(kit);
        SaveStreakPrefab(kit);
        SaveHitPrefab(kit);
        SaveKnockPrefab(kit);

        // Материал мог потерять свойства в памяти при живом файле на диске — перечитываем (razlom-unity-particle-gotchas).
        foreach (string name in OwnMaterials)
            AssetDatabase.ImportAsset(MaterialPath(name), ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(PrefabPath(SlamName));
        if (importer != null && importer.userData != Revision)
        {
            importer.userData = Revision;
            importer.SaveAndReimport();
        }
        Debug.Log("[wreck-iron-setup] Крушение «холодное железо» собрано: удар оземь (рваная земля с пятнами и трещинами Hovl, пазы звеньев, кучки плит, щебень, железо, звенья тёмного железа, брызги земли, пыль, искры, свет), росчерк маха, искра на теле, камешки у ног; ревизия " + Revision + ".");
    }

    /// <summary>Что нужно префабам: свои материалы и свои меши (звено, осколок, кусок, плиты).</summary>
    private sealed class Kit
    {
        public Material Stone, Iron, Link, Ground, Debris, Dust, Spark, Streak, Flash, Chunk, Dirt;
        public Mesh IronMesh, LinkMesh, ChunkMesh;
        public Mesh[] SlabMeshes;
    }
}
