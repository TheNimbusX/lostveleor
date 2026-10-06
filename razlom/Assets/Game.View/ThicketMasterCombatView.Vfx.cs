using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — ЭФФЕКТЫ АТАК (план artifacts/tools/wf/boss-vfx-plan.md §2–3, контракт
    /// темпа boss-tempo-contract.md, 02.10). Только паки: префабы собирает
    /// ThicketMasterVfxSetup («Разлом/Босс/Хозяин Чащи/Собрать эффекты») в
    /// Resources/VFX/ThicketMaster/Attacks. Звука нет.
    ///
    /// Всё — от событий кадра (хуки каркаса) и состояния Sim, не опросом кругов:
    /// • пробуждение — корни рвутся у передних лап; рёв — вдох (листья, пыль стягивается),
    ///   волна (стоячая стена пыли, листьев и лепестков до внешнего радиуса, кромка, столб с крон);
    /// • лапа — на каждый удар серии три ленты когтей по кости пальцев бьющей лапы (ClawTrail),
    ///   веер земли и пыль там, где прошли когти;
    /// • топот — юбка пыли на дыбах, стоячая стена пыли и комья до края круга 5,2, своя стена второго кольца;
    /// • нырок (V14, владелец 04.10: «земля… как кольцо какое-то», «гладкая плоская» — ThicketMasterEarthRules): уход —
    ///   земля вокруг тела ломается рваными кусками (плиты дёрна сползают в яму, комья, камни поляны, зерно, пятна
    ///   разрытой земли, трещины, низкая пыль ≤ 1,25 с); ход под землёй (V17 — владелец 08.10: «как будто холмик просто
    ///   скользит по полу… без вау-эффекта пробуривания»): с головой не едет ничего жёсткого — земля рвётся на месте (плиты
    ///   и камни вырываются и падают, где встали, носовая волна, фонтан комьев, трещины от головы, низкая пыль, рябь
    ///   впереди), за головой рваная траншея (FurrowTrail) с плитами на губах, частота следа — по пройденным метрам, пока
    ///   стоит — земля у головы «кипит»; круг лёг — дрожь круга и вздутие у точки выхода (EmergeBulge: плиты поднимаются, трещины
    ///   разгораются); выход — плиты, камни и комья фонтаном вдоль тела, ударная волна пыли по полу, кратер, корни,
    ///   короткий свет из ямы (пыль ниже 1 м и ≤ 0,85 с — ревью 02.10, вечер: «вата» закрывала героя и босса);
    /// • прорастание — лапы в землю, дрожь под каждым кругом, шипы-корни на ударе;
    /// • пыльца — золото с кроны, столб над облаком, ядовитое облако с кромкой и пульсом укуса лежит, пока лежит зона Sim;
    /// • терновник (§ 17.2, 08.10; заменил веер § 16) — кусты на полу растут, дрожат и вянут, стручки-шипы из устьев
    ///   по 4 линиям, полёт по остриё Sim, всплески (ThicketMasterCombatView.Seeds*.cs, .SeedTrail.cs);
    /// • ливень — пуф куста на залп, ягоды летят дугой к своим кругам, брызги сока и кусочки ягод
    ///   (плоских луж и пятен нет — ревью 02.10, вечер: «лужи после буллет рейна говно»);
    /// • буря — канал на всю бурю (ревью 02.10, вечер: «непонятно, что босс делает»): вихрь лепестков
    ///   столбом вокруг тела, кроны горят розовым золотом, луч с крон вверх; лепестки с кроны, вихрь по
    ///   арене, столбы света над кругами, порыв волны. Ревью вечера: лепестков вдвое меньше и у героя
    ///   (1,5 м по лучу камеры) они гаснут (ClearAroundHero); розовая аура у ног — в поле бури
    ///   (ThicketStormDangerView), круг укрытия у босса больше не розовый;
    /// • смерть — «цветущий холм»: вспышка в кусте и кольцо лепестков, волна цветения на касании, тело
    ///   уходит под встающий холм (V13: мшистый пригорок 1,3 м), холм зацветает кучками цветов со светом, над ним
    ///   загорается тёплый свет (UpdateLights), лежит до смены арены (ThicketMasterDeathRules); по холму ходят — тела
    ///   встают на его верх (KnollFloor — крючок пола LayoutView.ShownFloorLevel), ничего у героя не оседает.
    ///
    /// Возраст эффекта — от тика Sim (Tick − 1 + Alpha): пауза, хит-стоп и съёмка держат
    /// кадр, повтор даёт тот же кадр (зерно систем — от номера действия). Эффекты замаха
    /// идут по часам босса (ThicketMasterClipRules.BossClock) и перечитывают свой срок из
    /// Sim каждый кадр: Песочные Часы сдвигают их вместе с телом. Пулы — при первой арене,
    /// в бою ни одного Instantiate и ни одной аллокации.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        public const string PrefabFolder = "VFX/ThicketMaster/Attacks/Prefabs/";

        /// <summary>Рост босса 4,14 / 3,6 м: всё, что идёт «от тела» на земле, — ×1,15.</summary>
        public const float BodyScale = 1.15f;

        public const string PawSlashName = "VFX_Thicket_PawSlash";
        public const string PawImpactName = "VFX_Thicket_PawImpact";
        public const string StompRearName = "VFX_Thicket_StompRear";
        public const string StompQuakeName = "VFX_Thicket_StompQuake";
        public const string StompOuterName = "VFX_Thicket_StompOuter";
        public const string DiveBurstName = "VFX_Thicket_DiveBurst";
        public const string MoundName = "VFX_Thicket_Mound";
        public const string DiveTremorName = "VFX_Thicket_DiveTremor";
        public const string EmergeName = "VFX_Thicket_Emerge";
        /// <summary>
        /// Вздутие у точки выхода (V14): от тика круга до удара у a.Origin земля пухнет — плиты дёрна поднимаются и клонятся,
        /// трещины разгораются, подскакивают камешки; на выходе снимается (OnEmerge).
        /// </summary>
        public const string EmergeBulgeName = "VFX_Thicket_EmergeBulge";
        public const string SproutPressName = "VFX_Thicket_SproutPress";
        public const string SproutTremorName = "VFX_Thicket_SproutTremor";
        public const string SproutSpikesName = "VFX_Thicket_SproutSpikes";
        public const string PollenShakeName = "VFX_Thicket_PollenShake";
        public const string PollenFallName = "VFX_Thicket_PollenFall";
        public const string PollenCloudName = "VFX_Thicket_PollenCloud";
        public const string BushPuffName = "VFX_Thicket_BushPuff";
        public const string BerryName = "VFX_Thicket_Berry";
        public const string BerrySplatName = "VFX_Thicket_BerrySplat";
        public const string CrownShedName = "VFX_Thicket_CrownShed";
        public const string StormVortexName = "VFX_Thicket_StormVortex";
        public const string LightPillarName = "VFX_Thicket_LightPillar";
        public const string StormWaveName = "VFX_Thicket_StormWave";
        public const string StormChannelName = "VFX_Thicket_StormChannel";
        public const string RoarInhaleName = "VFX_Thicket_RoarInhale";
        public const string RoarBlastName = "VFX_Thicket_RoarBlast";
        public const string WakeTearName = "VFX_Thicket_WakeTear";
        public const string DeathBloomName = "VFX_Thicket_DeathBloom";
        /// <summary>Холм смерти: земля, цветы, трава, ростки — лежит до смены арены (ThicketMasterDeathRules).</summary>
        public const string DeathHillName = "VFX_Thicket_DeathHill";

        /// <summary>Все префабы набора — сборщик проверяет по этому списку, что собрал всё.</summary>
        public static readonly string[] AttackPrefabNames =
        {
            PawSlashName, PawImpactName, StompRearName, StompQuakeName, StompOuterName, DiveBurstName, MoundName,
            DiveTremorName, EmergeName, SproutPressName, SproutTremorName, SproutSpikesName, PollenShakeName,
            PollenFallName, PollenCloudName, BushPuffName, BerryName, BerrySplatName, CrownShedName, StormVortexName,
            LightPillarName, StormWaveName, RoarInhaleName, RoarBlastName, WakeTearName, DeathBloomName,
            StormChannelName, DeathHillName, EmergeBulgeName,
            // Терновник (§ 17.2, 08.10; заменил веер § 16): шип-стручок с лентой и следом, куст, земля рвётся, выпуск, попадание, конец линии.
            SeedPodName, BushName, BushSproutName, BushLaunchName, SeedHitName, SeedDropName,
        };

        /// <summary>
        /// Дети корня префаба, которые вид ставит сам: «Left»/«Right» — эмиттеры у костей
        /// (кроны, передние лапы), «Bush» — у куста.
        /// Система с именем «Late …» стартует, когда тело легло (смерть); «~…» — эмиссия
        /// идёт столько, сколько скажет вид (буря, столб света).
        /// </summary>
        public const string LeftChild = "Left", RightChild = "Right", BushChild = "Bush";
        /// <summary>Канал бури: «Crown» — над серединой между кронами (луч вверх), ставит вид.</summary>
        public const string CrownChild = "Crown";
        /// <summary>Луч канала бури — столько метров над серединой между костями крон.</summary>
        public const float ChannelBeamLift = .35f;
        public const string LatePrefix = "Late ", TimedPrefix = "~";
        /// <summary>
        /// Системы следа бугра («Trail Burst Slabs», «Trail Bow», …): вид пускает их эмиссию, только пока бугор едет
        /// (частота — по пройденным метрам); стоит (круг лёг, Песочные Часы) — земля не копится кучей в одной точке, а у
        /// головы «кипит» («Still …» — только пока стоит). Прочие системы бугра («Head …») идут всё время под землёй;
        /// после выхода эмиссия гаснет, плиты, комья и траншея доживают свою жизнь на месте.
        /// </summary>
        public const string TrailPrefix = "Trail ", StillPrefix = "Still ";
        /// <summary>Ребёнок префаба PawSlash с MeshFilter + MeshRenderer: сетку лент когтей пишет вид.</summary>
        public const string ClawsChild = "Claws";
        /// <summary>
        /// Ребёнок префаба бугра с MeshFilter + MeshRenderer (V11): сетку сплошной борозды рыхлой земли по пройденному
        /// пути пишет вид (FurrowTrail) — вместо цепочки валиков «Trail Ridge».
        /// </summary>
        public const string FurrowChild = "Furrow";
        /// <summary>Ребёнок префаба холма смерти с точечным светом (V11): яркость ведёт вид (ThicketMasterDeathRules.KnollLight).</summary>
        public const string KnollLightChild = "Knoll Light";
        /// <summary>Ребёнок префаба выхода с точечным светом из ямы (V14): яркость ведёт вид (ThicketMasterEarthRules.EruptLight).</summary>
        public const string EruptLightChild = "Erupt Light";

        /// <summary>
        /// Ленты когтей (ревью 02.10: «след идёт не за лапой, а после неё»): кость пальцев бьющей
        /// лапы пишет ленту с ThicketMasterClipRules.ClawLeadTicks до удара (доля замаха шага: первый 17 — 7 тиков,
        /// следующие — промежуток серии (ThicketPawGapOf, 8–12) — 3–4, тяжёлый замах 30 — 12) по ClawTailTicks после
        /// (часы босса); хвост — последние ClawTrailSeconds пути. Ширина одной ленты и шаг между тремя когтями, м.
        /// </summary>
        public const int ClawTailTicks = 2;
        public const float ClawTrailSeconds = .16f, ClawWidth = .26f, ClawSpacing = .32f;
        /// <summary>
        /// Задержки от события: юбка дыбом, нос в земле, лапы в землю, лапы из земли. Нырок — с
        /// первого тика ухода (ревью 02.10): стена земли закрывает все 12 тиков, пока тело уходит.
        /// </summary>
        public const int StompRearDelayTicks = 4, DiveBurstDelayTicks = 0, SproutPressDelayTicks = 4, WakeTearDelayTicks = 14;
        /// <summary>Юбка пыли дыбом — из-под задних лап: столько метров назад от центра тела (×BodyScale в префабе).</summary>
        public const float StompRearBack = 1.3f;
        /// <summary>Дуга ягоды: высота над прямой, м — 3,6 + 0,4·номер круга залпа.</summary>
        public const float BerryArcHeight = 3.6f, BerryArcStep = .4f;

        private const float RewindSeconds = 1.5f;

        private enum VfxAnchor : byte { None, Impact, LastImpact, Shape, PollenLand, PollenWatch }

        private enum VfxFollow : byte { None, Mound, Bush, Crowns, Toes, ToeLeft, ToeRight, Body, Flight, Claws, Channel, Hill }

        /// <summary>Экземпляр префаба в пуле: разбор RootSnarerCombatView.Fx и состояние запуска.</summary>
        private sealed class Vfx
        {
            public RootSnarerCombatView.Fx Fx;
            public Transform Left, Right, Bush, Crown;
            /// <summary>Ленты когтей (только PawSlash); Index — 1 правая лапа, 0 левая.</summary>
            public ClawTrail Claws;
            /// <summary>Борозда за бугром (только Mound, V11).</summary>
            public FurrowTrail Furrow;
            /// <summary>Точечный свет экземпляра (холм смерти, V11): пиковая яркость и место из префаба; яркость 0, пока вид не зажжёт.</summary>
            public Light[] Lights = new Light[0];
            public float[] LightPeak = new float[0];
            /// <summary>Дальность света из префаба: поднятый над героем свет (KnollLightLift) дотягивается дальше на подъём.</summary>
            public float[] LightRange = new float[0];
            public Vector3[] LightLocal = new Vector3[0];
            /// <summary>Свет холма: касание боком, с от удара (KnollLight); MaxValue — свет не горит.</summary>
            public float LightLand = float.MaxValue;
            /// <summary>Свет из ямы выхода (V14, ребёнок EruptLightChild): яркость — ThicketMasterEarthRules.EruptLight по возрасту.</summary>
            public bool EruptLight;
            public Vector3 BaseScale = Vector3.one;
            public int[] Late = new int[0], Timed = new int[0], Trail = new int[0], Still = new int[0];
            /// <summary>
            /// Бугор (V15, V17): частота систем следа из префаба (на ходу ThicketMasterEarthRules.MoundRefSpeed) — вид множит
            /// её на TrailRateScale хода; прошлый возраст кадра (ход = путь / время) и последний множитель.
            /// </summary>
            public float[] TrailRate = new float[0];
            public float LastAge = float.NaN, TrailScale = -1f;
            /// <summary>Бугор: возраст, на котором босс вылез (дальше бугор доживает на месте); MaxValue — ещё едет.</summary>
            public float Surfaced = float.MaxValue;
            /// <summary>Буря: лепестки у героя гаснут (ClearAroundHero).</summary>
            public bool ClearHero;

            public int Owner = -1, Serial, Stage, Index, Lead, FlyTicks;
            public VfxAnchor Anchor;
            public VfxFollow Follow;
            /// <summary>Часы босса (замах), пока якорь жив.</summary>
            public bool Clock;
            /// <summary>Следовать за костью только до старта, потом стоять.</summary>
            public bool Pin;
            /// <summary>Облако пыльцы: сколько тиков лежит зона Sim (от падения до конца) — начало облака считается от конца зоны.</summary>
            public int LieTicks;
            public float FadeAge = float.MaxValue, FadeSeconds = .3f, Height;
            /// <summary>
            /// Смерть: до этого возраста корень едет за серединой падающего тела (касание боком), дальше стоит.
            /// С FreezeAge частицы не шагают — холм стоит как вырос (до смены арены).
            /// </summary>
            public float PinAge = float.MaxValue, FreezeAge = float.MaxValue;
            public Vector3 From, To, Center, Last;
        }

        private sealed class VfxPool
        {
            public Vfx[] Items = new Vfx[0];
            public int Cursor;
            /// <summary>Сколько секунд частицы префаба живут сами (NaturalSeconds): жизнь экземпляра не короче.</summary>
            public float Natural;
        }

        private VfxPool _pawSlash, _pawImpact, _stompRear, _stompQuake, _stompOuter, _diveBurst, _mound, _diveTremor,
            _emerge, _emergeBulge, _sproutPress, _sproutTremor, _sproutSpikes, _pollenShake, _pollenFall, _pollenCloud, _bushPuff,
            _berry, _berrySplat, _crownShed, _stormVortex, _lightPillar, _stormWave, _roarInhale, _roarBlast,
            _wakeTear, _deathBloom, _stormChannel, _deathHill;
        private VfxPool[] _vfxPools;
        private LayoutView _vfxLayout;
        private int _vfxGeneration = -1;
        /// <summary>Глубина забега, при которой поставлены эффекты: следующая арена той же симуляции снимает всё (холм смерти).</summary>
        private int _vfxDepth = -1;
        /// <summary>Сколько бугор доживает после выхода: самая долгая жизнь его частиц вне горба.</summary>
        private float _moundLinger;
        /// <summary>Общий буфер частиц для лепестков бури у героя: создаётся с пулами, в бою не растёт.</summary>
        private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[0];

        // ------------------------------------------------------------ pools

        private void EnsurePools()
        {
            if (_vfxPools != null) return;
            _vfxLayout = GetComponent<LayoutView>();
            int missing = 0;
            _pawSlash = MakeVfxPool(PawSlashName, 4, ref missing);
            // Удар лапы живёт ~2,4 с (листья), серия бьёт раз в 0,3 с: 6 — чтобы не перезапускать летящие.
            _pawImpact = MakeVfxPool(PawImpactName, 6, ref missing);
            _stompRear = MakeVfxPool(StompRearName, 2, ref missing);
            _stompQuake = MakeVfxPool(StompQuakeName, 2, ref missing);
            _stompOuter = MakeVfxPool(StompOuterName, 2, ref missing);
            // Нырок «под героя» в фазах 2–3 — раз в 6 с от начала, уход живёт 2,6 с, выход 3 с: двух экземпляров хватает подряд.
            _diveBurst = MakeVfxPool(DiveBurstName, ThicketMasterEarthRules.DivePool, ref missing);
            _mound = MakeVfxPool(MoundName, ThicketMasterEarthRules.MoundPool, ref missing);
            _moundLinger = MoundLinger(_mound);
            _diveTremor = MakeVfxPool(DiveTremorName, 1, ref missing);
            _emerge = MakeVfxPool(EmergeName, ThicketMasterEarthRules.EmergePool, ref missing);
            _emergeBulge = MakeVfxPool(EmergeBulgeName, ThicketMasterEarthRules.BulgePool, ref missing);
            _sproutPress = MakeVfxPool(SproutPressName, 1, ref missing);
            _sproutTremor = MakeVfxPool(SproutTremorName, 6, ref missing);
            _sproutSpikes = MakeVfxPool(SproutSpikesName, 8, ref missing);
            _pollenShake = MakeVfxPool(PollenShakeName, 1, ref missing);
            _pollenFall = MakeVfxPool(PollenFallName, 4, ref missing);
            _pollenCloud = MakeVfxPool(PollenCloudName, 4, ref missing);
            _bushPuff = MakeVfxPool(BushPuffName, 5, ref missing);
            _berry = MakeVfxPool(BerryName, 16, ref missing);
            _berrySplat = MakeVfxPool(BerrySplatName, 24, ref missing);
            _crownShed = MakeVfxPool(CrownShedName, 1, ref missing);
            _stormVortex = MakeVfxPool(StormVortexName, 1, ref missing);
            _lightPillar = MakeVfxPool(LightPillarName, 6, ref missing);
            _stormWave = MakeVfxPool(StormWaveName, 2, ref missing);
            _roarInhale = MakeVfxPool(RoarInhaleName, 1, ref missing);
            _roarBlast = MakeVfxPool(RoarBlastName, 2, ref missing);
            _wakeTear = MakeVfxPool(WakeTearName, 2, ref missing);
            _deathBloom = MakeVfxPool(DeathBloomName, 1, ref missing);
            _stormChannel = MakeVfxPool(StormChannelName, 1, ref missing);
            _deathHill = MakeVfxPool(DeathHillName, 1, ref missing);
            // Терновник: кусты и стручки ведёт UpdateSeeds (не AdvanceVfx), всплески терновника — общие пулы ниже.
            MakeSeedPools(ref missing);
            // Рельеф холма смерти (по нему ходят, KnollFloor) — растяжка вершины считается раз: до боя, не на добивании.
            _ = ThicketMasterDeathRules.KnollReliefScale;
            // Буря — лепестки у героя гаснут (ClearAroundHero).
            foreach (var pool in new[] { _stormVortex, _crownShed, _stormChannel, _stormWave })
                for (int i = 0; i < pool.Items.Length; i++) pool.Items[i].ClearHero = true;
            _particleBuffer = new ParticleSystem.Particle[ParticleBufferSize(new[] { _stormVortex, _crownShed, _stormChannel, _stormWave })];
            _vfxPools = new[]
            {
                _pawSlash, _pawImpact, _stompRear, _stompQuake, _stompOuter, _diveBurst, _mound, _diveTremor, _emerge, _emergeBulge,
                _sproutPress, _sproutTremor, _sproutSpikes, _pollenShake, _pollenFall, _pollenCloud, _bushPuff, _berry,
                _berrySplat, _crownShed, _stormVortex, _lightPillar, _stormWave, _roarInhale, _roarBlast, _wakeTear,
                _deathBloom, _stormChannel, _deathHill, _bushSprout, _bushLaunch, _seedHit, _seedDrop,
            };
            if (missing > 0)
                Debug.LogWarning($"[thicketmaster-vfx] Нет {missing} из {AttackPrefabNames.Length} префабов эффектов в Resources/{PrefabFolder} — " +
                    "собери «Разлом/Босс/Хозяин Чащи/Собрать эффекты». Без них атаки идут без эффектов.");
        }

        private VfxPool MakeVfxPool(string prefabName, int count, ref int missing)
        {
            var pool = new VfxPool();
            var prefab = Resources.Load<GameObject>(PrefabFolder + prefabName);
            if (prefab == null) { missing++; return pool; }
            pool.Items = new Vfx[count];
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = prefabName + " " + i;
                var v = new Vfx { Fx = RootSnarerCombatView.Prepare(go), BaseScale = prefab.transform.localScale };
                var root = go.transform;
                v.Left = root.Find(LeftChild); v.Right = root.Find(RightChild);
                v.Bush = root.Find(BushChild); v.Crown = root.Find(CrownChild);
                var claws = root.Find(ClawsChild);
                if (claws != null && claws.TryGetComponent(out MeshFilter clawFilter)) v.Claws = new ClawTrail(clawFilter);
                var furrow = root.Find(FurrowChild);
                if (furrow != null && furrow.TryGetComponent(out MeshFilter furrowFilter)) v.Furrow = new FurrowTrail(furrowFilter);
                // Свет префаба: пик — его яркость в префабе; гасится до первого кадра эффекта.
                v.Lights = go.GetComponentsInChildren<Light>(true);
                v.LightPeak = new float[v.Lights.Length];
                v.LightRange = new float[v.Lights.Length];
                v.LightLocal = new Vector3[v.Lights.Length];
                v.EruptLight = root.Find(EruptLightChild) != null;
                for (int l = 0; l < v.Lights.Length; l++)
                {
                    v.LightPeak[l] = v.Lights[l].intensity;
                    v.LightRange[l] = v.Lights[l].range;
                    v.LightLocal[l] = root.InverseTransformPoint(v.Lights[l].transform.position);
                    v.Lights[l].intensity = 0f;
                    v.Lights[l].enabled = false;
                }
                v.Late = SystemsNamed(v.Fx, LatePrefix);
                v.Timed = SystemsNamed(v.Fx, TimedPrefix);
                v.Trail = SystemsNamed(v.Fx, TrailPrefix);
                v.Still = SystemsNamed(v.Fx, StillPrefix);
                v.TrailRate = new float[v.Trail.Length];
                for (int t = 0; t < v.Trail.Length; t++) v.TrailRate[t] = v.Fx.Particles[v.Trail[t]].emission.rateOverTime.constant;
                go.SetActive(false);
                pool.Items[i] = v;
                if (i == 0) pool.Natural = NaturalSeconds(v.Fx.Particles);
            }
            return pool;
        }

        /// <summary>
        /// Сколько живут частицы префаба сами: задержка + эмиссия (поток — вся длительность,
        /// залпы — последний залп) + самая долгая жизнь частицы. Жизнь экземпляра короче этого
        /// гасила корень посреди полёта — листья и лепестки пропадали в воздухе (ревью 02.10).
        /// Петли не в счёт: их гасит вид (FadeAge, снятие якоря).
        /// </summary>
        private static float NaturalSeconds(ParticleSystem[] systems)
        {
            float end = 0f;
            for (int k = 0; k < systems.Length; k++)
            {
                var main = systems[k].main;
                if (main.loop) continue;
                var emission = systems[k].emission;
                float emit = 0f;
                if (emission.enabled)
                {
                    if (CurveMax(emission.rateOverTime) > 0f) emit = main.duration;
                    for (int b = 0; b < emission.burstCount; b++)
                    {
                        var burst = emission.GetBurst(b);
                        float last = burst.cycleCount == 0 ? main.duration
                            : burst.time + Mathf.Max(0, burst.cycleCount - 1) * burst.repeatInterval;
                        emit = Mathf.Max(emit, Mathf.Min(last, main.duration));
                    }
                }
                end = Mathf.Max(end, CurveMax(main.startDelay) + emit + CurveMax(main.startLifetime));
            }
            return end;
        }

        /// <summary>
        /// Самая долгая жизнь частицы бугра (V17: жёсткой головы нет — всё лежит на месте): столько он доживает после
        /// выхода; траншея осыпается за FurrowTrail.Life после того, как голова прошла.
        /// </summary>
        private static float MoundLinger(VfxPool pool)
        {
            if (pool.Items.Length == 0) return FurrowTrail.Life;
            var v = pool.Items[0];
            float longest = v.Furrow != null ? FurrowTrail.Life : 0f;
            for (int k = 0; k < v.Fx.Particles.Length; k++)
                longest = Mathf.Max(longest, CurveMax(v.Fx.Particles[k].main.startLifetime));
            return longest;
        }

        private static float CurveMax(ParticleSystem.MinMaxCurve curve)
            => curve.mode == ParticleSystemCurveMode.Constant || curve.mode == ParticleSystemCurveMode.TwoConstants
                ? curve.constantMax : curve.curveMultiplier;

        private static int[] SystemsNamed(RootSnarerCombatView.Fx fx, string prefix)
        {
            int n = 0;
            for (int k = 0; k < fx.Particles.Length; k++)
                if (fx.Particles[k].name.StartsWith(prefix, System.StringComparison.Ordinal)) n++;
            var found = new int[n];
            n = 0;
            for (int k = 0; k < fx.Particles.Length; k++)
                if (fx.Particles[k].name.StartsWith(prefix, System.StringComparison.Ordinal)) found[n++] = k;
            return found;
        }

        /// <summary>
        /// Следующий экземпляр пула: место, поворот, тик старта, зерно от serial, жизнь — не короче
        /// жизни частиц префаба (pool.Natural). Раньше гасить — только FadeAge или снятием якоря.
        /// </summary>
        private Vfx TakeVfx(VfxPool pool, int boss, int tick, Vector3 position, Quaternion rotation, int serial, float life)
        {
            if (pool == null || pool.Items.Length == 0) return null;
            var v = pool.Items[pool.Cursor++ % pool.Items.Length];
            RootSnarerCombatView.Restart(v.Fx, tick, position, rotation, serial, Mathf.Max(life, pool.Natural));
            v.Fx.Root.transform.localScale = v.BaseScale;
            v.Owner = boss; v.Serial = serial; v.Stage = 0; v.Index = -1; v.Lead = 0; v.FlyTicks = 0; v.LieTicks = 0;
            v.Anchor = VfxAnchor.None; v.Follow = VfxFollow.None; v.Clock = false; v.Pin = false;
            v.FadeAge = float.MaxValue; v.FadeSeconds = .3f; v.Height = 0f; v.Surfaced = float.MaxValue;
            v.LastAge = float.NaN;
            v.PinAge = v.FreezeAge = float.MaxValue;
            v.From = v.To = v.Center = v.Last = position;
            v.LightLand = float.MaxValue;
            for (int l = 0; l < v.Lights.Length; l++) { v.Lights[l].intensity = 0f; v.Lights[l].enabled = false; }
            if (v.Furrow != null) v.Furrow.Clear();
            return v;
        }

        /// <summary>Эмиссия систем indices экземпляра (модуль Emission): без аллокаций, частицы в полёте доживают.</summary>
        private static void SetEmission(Vfx v, int[] indices, bool on)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                var emission = v.Fx.Particles[indices[i]].emission;
                if (emission.enabled != on) emission.enabled = on;
            }
        }

        /// <summary>Эмиссия всех систем экземпляра.</summary>
        private static void SetEmission(Vfx v, bool on)
        {
            for (int k = 0; k < v.Fx.Particles.Length; k++)
            {
                var emission = v.Fx.Particles[k].emission;
                if (emission.enabled != on) emission.enabled = on;
            }
        }

        /// <summary>
        /// Босс вылез (или ушёл из-под земли иначе, или начался новый нырок): бугор встаёт на месте, эмиссия гаснет,
        /// плиты, комья и траншея доживают свою жизнь на месте — _moundLinger.
        /// </summary>
        private void SurfaceMound(Vfx v, float age)
        {
            if (v.Surfaced != float.MaxValue) return;
            v.Surfaced = Mathf.Max(0f, age);
            SetEmission(v, false);
            v.Fx.Life = Mathf.Min(v.Fx.Life, v.Surfaced + _moundLinger);
        }

        /// <summary>Частота систем следа бугра = частота префаба × scale (без аллокаций; мелкие колебания хода не пишутся).</summary>
        private static void SetTrailRate(Vfx v, float scale)
        {
            if (Mathf.Abs(scale - v.TrailScale) < .02f) return;
            v.TrailScale = scale;
            for (int i = 0; i < v.Trail.Length; i++)
            {
                var emission = v.Fx.Particles[v.Trail[i]].emission;
                emission.rateOverTime = v.TrailRate[i] * scale;
            }
        }

        /// <summary>Эмиссия «~»-систем идёт seconds (система остановлена Restart — длительность менять можно).</summary>
        private static void SetEmitSeconds(Vfx v, float seconds)
        {
            for (int i = 0; i < v.Timed.Length; i++)
            {
                var main = v.Fx.Particles[v.Timed[i]].main;
                main.duration = Mathf.Max(.05f, seconds);
            }
        }

        private static void RetireVfx(Vfx v)
        {
            if (v == null) return;
            v.Anchor = VfxAnchor.None;
            v.Owner = -1;
            RootSnarerCombatView.Retire(v.Fx);
        }

        private static void RetirePool(VfxPool pool, bool pendingOnly, float tick)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Items.Length; i++)
            {
                var v = pool.Items[i];
                if (!v.Fx.Root.activeSelf || (pendingOnly && v.Fx.Tick <= tick)) continue;
                RetireVfx(v);
            }
        }

        private void RetireAllVfx()
        {
            if (_vfxPools == null) return;
            for (int p = 0; p < _vfxPools.Length; p++) RetirePool(_vfxPools[p], false, 0f);
            HideSeeds();
        }

        private void OnDisable()
        {
            RetireAllVfx();
            ReleaseKnoll();
        }

        /// <summary>Сетки лент когтей создаёт вид (ClawTrail) — и удаляет вместе с собой.</summary>
        private void OnDestroy()
        {
            ReleaseKnoll();
            if (_pawSlash != null)
                for (int i = 0; i < _pawSlash.Items.Length; i++)
                    if (_pawSlash.Items[i].Claws != null) _pawSlash.Items[i].Claws.Dispose();
            if (_mound != null)
                for (int i = 0; i < _mound.Items.Length; i++)
                    if (_mound.Items[i].Furrow != null) _mound.Items[i].Furrow.Dispose();
        }

        // ------------------------------------------------------------ frame

        partial void OnArenaReset()
        {
            EnsurePools();
            RetireAllVfx();
            ReleaseKnoll();
            _vfxGeneration = _driver != null ? _driver.Generation : -1;
            _vfxDepth = RunDepth();
        }

        /// <summary>Живой босс на арене — бой заново: холм прошлой смерти (стенд, та же глубина) снять.</summary>
        partial void OnBossBound(int boss)
        {
            EnsurePools();
            RetirePool(_deathHill, false, 0f);
            RetirePool(_deathBloom, false, 0f);
            ReleaseKnoll();
        }

        /// <summary>Глубина забега (номер арены); −1 — забега нет (стенд, песочница).</summary>
        private int RunDepth() => _driver != null && _driver.Run != null ? _driver.Run.Depth : -1;

        partial void OnFrame(Simulation sim, float tick)
        {
            if (_vfxPools == null) return;
            // Общий сброс той же симуляции (новый Разлом, стенд): тики начались заново.
            if (_driver.Generation != _vfxGeneration) { RetireAllVfx(); ReleaseKnoll(); _vfxGeneration = _driver.Generation; return; }
            // Следующая арена той же симуляции (выбор маршрута после награды): холм смерти и всё прочее — прошлой арены.
            int depth = RunDepth();
            if (depth != _vfxDepth) { RetireAllVfx(); ReleaseKnoll(); _vfxDepth = depth; return; }
            float clock = _boss >= 0 ? ThicketMasterClipRules.BossClock(sim, _boss, tick) : tick;
            for (int p = 0; p < _vfxPools.Length; p++)
            {
                var items = _vfxPools[p].Items;
                for (int i = 0; i < items.Length; i++)
                    if (items[i].Fx.Root.activeSelf) AdvanceVfx(sim, items[i], tick, clock);
            }
            // Терновник: кусты и стручки (рост и полёт) — из Sim по часам босса (Часы держат кусты и шипы).
            UpdateSeeds(sim, tick, clock);
        }

        private void AdvanceVfx(Simulation sim, Vfx v, float tick, float clock)
        {
            var fx = v.Fx;
            if (v.Anchor != VfxAnchor.None && !Reanchor(sim, v, tick, clock)) return;
            float now = v.Clock ? clock : tick;
            float age = (now - fx.Tick) / Simulation.TicksPerSecond;
            if (age > fx.Life || age < -RewindSeconds) { RetireVfx(v); return; }
            if (!PlaceVfx(sim, v, age)) return;
            if (age > v.FadeAge)
            {
                float k = 1f - Mathf.Clamp01((age - v.FadeAge) / Mathf.Max(.01f, v.FadeSeconds));
                if (k <= 0f) { RetireVfx(v); return; }
                k = k * k * (3f - 2f * k);
                fx.Root.transform.localScale = v.BaseScale * Mathf.Max(.001f, k);
            }
            // Холм смерти дорос (FreezeAge): частицы стоят как есть — StepParticles не шагает тот же возраст.
            float stepAge = Mathf.Min(age, v.FreezeAge);
            RootSnarerCombatView.AnimateGrows(fx, stepAge);
            RootSnarerCombatView.StepParticles(fx, stepAge);
            // Буря — лепестки у героя гаснут (холм смерти у героя не трогается: по нему ходят, KnollFloor).
            if (v.ClearHero) ClearAroundHero(v);
            if (v.Lights.Length > 0) UpdateLights(v, age);
        }

        /// <summary>
        /// Свет холма смерти (V11): яркость — пик префаба × ThicketMasterDeathRules.KnollLight (загорается с цветением,
        /// садится до ровного слабого), место — над серединой, сдвинуто к камере по земле (светит видимый склон). Пауза
        /// держит (возраст — тики Sim). Гаснет с холмом (корень выключен). V13 — по пригорку ходят: рядом с героем свет
        /// поднимается над его головой и гаснет на остаток сближения (ThicketMasterDeathRules.KnollLightLift /
        /// KnollLightNear) — свет 2,9 м больше не сидит у героя в голове и не пересвечивает его.
        /// </summary>
        private void UpdateLights(Vfx v, float age)
        {
            // Выход (V14): короткая янтарная вспышка из ямы — свет — ребёнок корня, место из префаба; пауза держит (возраст — тики Sim).
            if (v.EruptLight)
            {
                float flash = ThicketMasterEarthRules.EruptLight(age);
                for (int l = 0; l < v.Lights.Length; l++)
                {
                    var lamp = v.Lights[l];
                    float glow = v.LightPeak[l] * flash;
                    bool lit = glow > 1e-3f;
                    if (lamp.enabled != lit) lamp.enabled = lit;
                    if (lit) lamp.intensity = glow;
                }
                return;
            }
            float k = v.LightLand == float.MaxValue ? 0f : ThicketMasterDeathRules.KnollLight(age, v.LightLand);
            Transform root = v.Fx.Root.transform;
            var camera = Camera.main;
            Vector3 look = camera != null ? camera.transform.forward : new Vector3(0f, -.743f, .669f);
            var toCamera = new Vector3(-look.x, 0f, -look.z);
            toCamera = toCamera.sqrMagnitude > 1e-6f ? toCamera.normalized : Vector3.back;
            bool hero = _driver != null && _driver.Sim != null;
            Vector3 feet = hero ? _driver.GetRenderPosition(Simulation.PlayerId) : Vector3.zero;
            for (int l = 0; l < v.Lights.Length; l++)
            {
                var light = v.Lights[l];
                float intensity = v.LightPeak[l] * k;
                bool on = intensity > 1e-3f;
                if (light.enabled != on) light.enabled = on;
                if (!on) continue;
                Vector3 at = root.TransformPoint(v.LightLocal[l]) + toCamera * ThicketMasterDeathRules.KnollLightToCamera;
                float range = v.LightRange[l];
                if (hero)
                {
                    float horizontal = new Vector2(at.x - feet.x, at.z - feet.z).magnitude;
                    float lift = ThicketMasterDeathRules.KnollLightLift(horizontal, at.y, feet.y);
                    at.y += lift;
                    range += lift;
                    intensity *= ThicketMasterDeathRules.KnollLightNear(horizontal, at.y, feet.y);
                }
                light.intensity = intensity;
                light.range = range;
                light.transform.position = at;
            }
        }

        /// <summary>
        /// Срок эффекта из Sim: замах — от ImpactTick шага, каст и буря — от LastImpactTick,
        /// круги — от удара своего круга, пыльца — от падения своей зоны (Часы их сдвигают).
        /// Якорь прошёл (удар случился, шаг сменился) — эффект стоит на своём тике и дальше
        /// идёт по обычным часам. Круг или зона исчезли (опасность снята) — эффект уходит.
        /// False — эффект снят.
        /// </summary>
        private bool Reanchor(Simulation sim, Vfx v, float tick, float clock)
        {
            var fx = v.Fx;
            switch (v.Anchor)
            {
                case VfxAnchor.Impact:
                    if (sim.TryGetThicketMasterAction(v.Owner, out ThicketMasterState a) && a.Serial == v.Serial
                        && a.Stage == v.Stage && !a.HitResolved)
                    {
                        fx.Tick = a.ImpactTick + v.Lead;
                        return true;
                    }
                    break;
                case VfxAnchor.LastImpact:
                    if (sim.TryGetThicketMasterAction(v.Owner, out ThicketMasterState b) && b.Serial == v.Serial)
                    {
                        fx.Tick = b.LastImpactTick + v.Lead;
                        return true;
                    }
                    break;
                case VfxAnchor.Shape:
                    if (!sim.TryGetThicketShape(v.Owner, v.Index, out FixVec2 c, out int impact, out bool resolved)
                        || Mathf.Abs(c.X.ToFloat() - v.Center.x) > .01f || Mathf.Abs(c.Y.ToFloat() - v.Center.z) > .01f)
                    {
                        RetireVfx(v);
                        return false;
                    }
                    if (!resolved) { fx.Tick = impact + v.Lead; return true; }
                    break;
                case VfxAnchor.PollenLand:
                    if (!sim.TryGetThicketPollenZone(v.Index, out ThicketPollenZone z) || z.Serial != v.Serial)
                    {
                        RetireVfx(v);
                        return false;
                    }
                    if (z.LandTick > sim.Tick - 1) { fx.Tick = z.LandTick + v.Lead; return true; }
                    break;
                case VfxAnchor.PollenWatch:
                    if (sim.TryGetThicketPollenZone(v.Index, out ThicketPollenZone w) && w.Serial == v.Serial)
                    {
                        // Песочные Часы двигают конец лежащей зоны (DelayThicketPollen) на всю заморозку
                        // босса: облако идёт по часам босса от сдвинутого начала — стоит под Часами
                        // (эмиссия и FadeAge те же) и гаснет ровно с зоной, а не за 2 с до неё.
                        fx.Tick = w.EndTick + 1 - v.LieTicks;
                        return true;
                    }
                    // Облако вытеснено новым или босс умер — зона Sim ушла: облако тает с этого кадра
                    // (сначала часы облака — на обычные, без скачка возраста).
                    v.Anchor = VfxAnchor.None;
                    if (v.Clock)
                    {
                        fx.Tick += Mathf.RoundToInt(tick - clock);
                        v.Clock = false;
                    }
                    v.FadeAge = Mathf.Min(v.FadeAge, Mathf.Max(0f, (tick - fx.Tick) / Simulation.TicksPerSecond));
                    v.FadeSeconds = .45f;
                    return true;
            }
            // Якорь прошёл: дальше обычные часы без скачка возраста.
            v.Anchor = VfxAnchor.None;
            if (v.Clock)
            {
                fx.Tick += Mathf.RoundToInt(tick - clock);
                v.Clock = false;
            }
            return true;
        }

        /// <summary>Место эффекта по его следованию (кость, бугор, полёт ягоды). False — эффект снят.</summary>
        private bool PlaceVfx(Simulation sim, Vfx v, float age)
        {
            Transform root = v.Fx.Root.transform;
            bool track = !v.Pin || age < 0f;
            switch (v.Follow)
            {
                case VfxFollow.Mound:
                {
                    if (v.Surfaced == float.MaxValue && age > .2f && !sim.ThicketUnderground(v.Owner)) SurfaceMound(v, age);
                    if (v.Surfaced != float.MaxValue)
                    {
                        // Вылез: голова встала, эмиссия погасла; плиты и комья лежат, где упали, траншея осыпается сама.
                        if (v.Furrow != null) v.Furrow.Build(age);
                        break;
                    }
                    // V17: с головой не едет ничего жёсткого — корень только переносит эмиттеры; всё, что они пускают, — в мире
                    // (земля рвётся там, где голова была). Корень повёрнут по ходу: нос (+Z) — куда едет, рябь — впереди.
                    Vector3 p = GroundAt(MoundPosition);
                    Vector3 step = p - v.Last; step.y = 0f;
                    // След — только на ходу; стоит (круг лёг, Часы) — земля у головы «кипит» (Still …), а не копится кучей.
                    bool moving = step.sqrMagnitude > 1e-8f;
                    SetEmission(v, v.Trail, moving);
                    SetEmission(v, v.Still, !moving);
                    // Частота следа — по пройденным метрам (ход Sim 7 м/с, догон — до 12): плит, камней и трещин на метр поровну.
                    float dt = age - v.LastAge;
                    if (moving && dt > 1e-3f) SetTrailRate(v, ThicketMasterEarthRules.TrailRateScale(Mathf.Sqrt(step.x * step.x + step.z * step.z) / dt));
                    v.LastAge = age;
                    if (step.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
                    root.position = p;
                    v.Last = p;
                    if (v.Furrow != null)
                    {
                        v.Furrow.Step(age, p, moving);
                        v.Furrow.Build(age);
                    }
                    break;
                }
                case VfxFollow.Bush:
                    if (track) root.position = BonePoint(_bossView != null ? _bossView.Bush : null, FallbackBush);
                    break;
                case VfxFollow.Crowns:
                    if (!track) break;
                    if (v.Left != null) v.Left.position = BonePoint(_bossView != null ? _bossView.CrownLeft : null, FallbackCrownLeft);
                    if (v.Right != null) v.Right.position = BonePoint(_bossView != null ? _bossView.CrownRight : null, FallbackCrownRight);
                    break;
                case VfxFollow.Toes:
                    if (!track) break;
                    if (v.Left != null) v.Left.position = GroundAt(BonePoint(_bossView != null ? _bossView.PawToeLeft : null, FallbackToeLeft));
                    if (v.Right != null) v.Right.position = GroundAt(BonePoint(_bossView != null ? _bossView.PawToeRight : null, FallbackToeRight));
                    break;
                case VfxFollow.ToeLeft:
                    if (track) root.position = GroundAt(BonePoint(_bossView != null ? _bossView.PawToeLeft : null, FallbackToeLeft));
                    break;
                case VfxFollow.ToeRight:
                    if (track) root.position = GroundAt(BonePoint(_bossView != null ? _bossView.PawToeRight : null, FallbackToeRight));
                    break;
                case VfxFollow.Body:
                    if (track && v.Owner >= 0 && v.Owner < sim.Entities.Count)
                        root.SetPositionAndRotation(GroundAt(BodyPosition()), Quaternion.LookRotation(FacingOf(sim, v.Owner), Vector3.up));
                    break;
                case VfxFollow.Flight:
                {
                    float s = age * Simulation.TicksPerSecond / Mathf.Max(1, v.FlyTicks);
                    if (s >= 1f) { RetireVfx(v); return false; }
                    s = Mathf.Max(0f, s);
                    Vector3 p = Vector3.Lerp(v.From, v.To, s) + Vector3.up * (4f * v.Height * s * (1f - s));
                    Vector3 step = p - v.Last;
                    if (step.sqrMagnitude > 1e-6f) root.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
                    root.position = p;
                    v.Last = p;
                    break;
                }
                case VfxFollow.Claws:
                    if (v.Claws != null) UpdateClaws(v, age);
                    break;
                case VfxFollow.Channel:
                {
                    if (!track) break;
                    // Канал бури: корень — тело на земле (вихрь, аура), «Left»/«Right» — кроны (свечение),
                    // «Crown» — над серединой между кронами (луч вверх). Поворот корня не нужен: всё круглое.
                    root.position = GroundAt(BodyPosition());
                    Vector3 left = BonePoint(_bossView != null ? _bossView.CrownLeft : null, FallbackCrownLeft);
                    Vector3 right = BonePoint(_bossView != null ? _bossView.CrownRight : null, FallbackCrownRight);
                    if (v.Left != null) v.Left.position = left;
                    if (v.Right != null) v.Right.position = right;
                    if (v.Crown != null) v.Crown.position = (left + right) * .5f + Vector3.up * ChannelBeamLift;
                    break;
                }
                case VfxFollow.Hill:
                    // Смерть: середина падающего тела до касания боком (PinAge), дальше холм стоит на месте.
                    if (age < v.PinAge) root.position = GroundAt(BodyCentroid());
                    break;
            }
            return true;
        }

        /// <summary>
        /// Ленты когтей за кадр: точка — кость пальцев бьющей лапы сейчас (голова ленты ровно на
        /// когтях), пишется только в окне удара; возраст — часы босса (стоят в хит-стопе, паузе, Часах).
        /// </summary>
        private void UpdateClaws(Vfx v, float age)
        {
            bool right = v.Index == 1;
            Transform toe = _bossView == null ? null : right ? _bossView.PawToeRight : _bossView.PawToeLeft;
            Vector3 paw = BonePoint(toe, right ? FallbackToeRight : FallbackToeLeft);
            // Окно ленты — свой упреждающий отрезок этого удара (v.Lead = −упреждение) и хвост после контакта.
            float window = (-v.Lead + ClawTailTicks) / (float)Simulation.TicksPerSecond;
            v.Claws.Step(age, paw, age >= 0f && age <= window, ClawTrailSeconds);
            var camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.743f, .669f);
            v.Claws.Build(age, view, ClawTrailSeconds, ClawWidth, ClawSpacing);
        }

        // ------------------------------------------------------------ hooks: wake, roar, paw, stomp

        partial void OnWake(int boss, int tick)
        {
            var sim = _driver.Sim;
            Quaternion look = Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up);
            for (int side = 0; side < 2; side++)
            {
                var v = TakeVfx(_wakeTear, boss, tick + WakeTearDelayTicks, GroundAt(BodyPosition()), look, tick * 2 + side, 1.8f);
                if (v == null) return;
                v.Follow = side == 0 ? VfxFollow.ToeLeft : VfxFollow.ToeRight;
                v.Pin = true;
                v.Fx.RiseSeconds = .08f; v.Fx.SinkSeconds = .3f; v.Fx.SinkAge = .5f;
                PlaceVfx(sim, v, -1f);
            }
        }

        partial void OnRoarWindup(int boss, int tick, int thresholds, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a)) return;
            var v = TakeVfx(_roarInhale, boss, tick, Ground(a.Origin), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up),
                a.Serial * 16, (impactTick - tick) / (float)Simulation.TicksPerSecond + .1f);
            if (v == null) return;
            v.Serial = a.Serial; v.Stage = a.Stage; v.Lead = tick - impactTick;
            v.Anchor = VfxAnchor.Impact; v.Clock = true;
            v.Follow = VfxFollow.Crowns;
            PlaceVfx(sim, v, -1f);
        }

        partial void OnRoarBlast(int boss, int tick, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            var v = TakeVfx(_roarBlast, boss, tick, GroundAt(at), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 1.6f);
            if (v == null) return;
            v.Follow = VfxFollow.Crowns; v.Pin = true;
            PlaceVfx(sim, v, -1f);
        }

        /// <summary>
        /// Удар серии: ленты когтей бьющей лапы (правая — чётный удар) пишутся с упреждения
        /// ThicketMasterClipRules.ClawLeadTicks (доля замаха этого шага: первый 17 → 7, следующие — промежуток
        /// серии, ThicketPawGapOf, → 3–4) до контакта по ClawTailTicks после; срок перечитывается
        /// из Sim (Часы сдвигают его с телом). Знак прочитан, когда серия ушла дальше (догон кадра, съёмка), —
        /// замах и удар этого шага считаются по полям серии (ThicketPawGapOf), а не по текущему шагу.
        /// </summary>
        partial void OnPawWindup(int boss, int tick, int stage, bool right, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a) || a.Action != ThicketMasterAction.Paw) return;
            int windup = ThicketMasterClipRules.PawWindupTicks(a, stage);
            if (a.Stage != stage) impactTick = ThicketMasterClipRules.PawImpactTick(a, stage);
            int lead = ThicketMasterClipRules.ClawLeadTicks(windup);
            float window = (lead + ClawTailTicks) / (float)Simulation.TicksPerSecond;
            var v = TakeVfx(_pawSlash, boss, impactTick - lead, Ground(a.Origin), Quaternion.identity,
                a.Serial * 16 + stage, window + ClawTrailSeconds + .05f);
            if (v == null) return;
            v.Serial = a.Serial; v.Stage = stage; v.Lead = -lead;
            v.Anchor = VfxAnchor.Impact; v.Clock = true;
            v.Follow = VfxFollow.Claws; v.Index = right ? 1 : 0;
            if (v.Claws != null) v.Claws.Clear();
        }

        partial void OnPawImpact(int boss, int tick, int stage, bool right, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            Transform toe = _bossView == null ? null : right ? _bossView.PawToeRight : _bossView.PawToeLeft;
            Vector3 point = toe != null ? GroundAt(toe.position) : GroundAt(at);
            TakeVfx(_pawImpact, boss, tick, point, Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick * 4 + stage, 1.6f);
        }

        partial void OnStompWindup(int boss, int tick, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a)) return;
            int start = tick + StompRearDelayTicks;
            var v = TakeVfx(_stompRear, boss, start, GroundAt(BodyPosition()), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up),
                a.Serial * 16, 1.4f);
            if (v == null) return;
            v.Serial = a.Serial; v.Stage = 0; v.Lead = start - impactTick;
            v.Anchor = VfxAnchor.Impact; v.Clock = true;
            v.Follow = VfxFollow.Body; v.Pin = true;
        }

        partial void OnStompImpact(int boss, int tick, int ring, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            Quaternion look = Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up);
            if (ring == 0) TakeVfx(_stompQuake, boss, tick, GroundAt(at), look, tick * 2, 2.2f);
            else TakeVfx(_stompOuter, boss, tick, GroundAt(at), look, tick * 2 + 1, 1.8f);
        }

        // ------------------------------------------------------------ hooks: dive

        partial void OnDiveBurrow(int boss, int tick)
        {
            var sim = _driver.Sim;
            TakeVfx(_diveBurst, boss, tick + DiveBurstDelayTicks, GroundAt(BodyPosition()),
                Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, ThicketMasterEarthRules.DiveLife);
        }

        partial void OnMoundTravel(int boss, int tick)
        {
            var sim = _driver.Sim;
            // Пул 2 (V17): хвост прошлого нырка (плиты на губах, траншея) доживает на месте, а не гаснет одним кадром;
            // прошлый бугор, если ещё едет (нырок снят без выхода), встаёт и доживает так же.
            for (int i = 0; i < (_mound != null ? _mound.Items.Length : 0); i++)
            {
                var m = _mound.Items[i];
                if (m.Fx.Root.activeSelf && m.Follow == VfxFollow.Mound) SurfaceMound(m, (tick - m.Fx.Tick) / (float)Simulation.TicksPerSecond);
            }
            var v = TakeVfx(_mound, boss, tick, GroundAt(MoundPosition), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 60f);
            if (v == null) return;
            // Прошлый выход погасил эмиссию экземпляра (SurfaceMound) — снова пускаем.
            SetEmission(v, true);
            v.Follow = VfxFollow.Mound;
        }

        partial void OnDiveLocked(int boss, int tick, Vector3 at, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a)) return;
            var v = TakeVfx(_diveTremor, boss, tick, GroundAt(at), Yaw(a.Serial), a.Serial * 16 + 2,
                (impactTick - tick) / (float)Simulation.TicksPerSecond + .2f);
            if (v != null)
            {
                v.Serial = a.Serial; v.Stage = a.Stage; v.Lead = tick - impactTick;
                v.Anchor = VfxAnchor.Impact; v.Clock = true;
            }
            // Вздутие (V14) — у точки выхода (Origin: там встаёт тело, туда доезжает голова бугра), а не в центре круга;
            // срок — по ImpactTick (Часы сдвигают), снимается на выходе.
            if (a.Action != ThicketMasterAction.Dive) return;
            RetirePool(_emergeBulge, false, 0f);
            var bulge = TakeVfx(_emergeBulge, boss, tick, Ground(a.Origin), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), a.Serial * 16 + 3,
                (impactTick - tick) / (float)Simulation.TicksPerSecond + .3f);
            if (bulge == null) return;
            bulge.Serial = a.Serial; bulge.Stage = a.Stage; bulge.Lead = tick - impactTick;
            bulge.Anchor = VfxAnchor.Impact; bulge.Clock = true;
        }

        partial void OnEmerge(int boss, int tick, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            // Бугор не пропадает одним кадром (ревью 02.10, п. 8): гаснет эмиссия, плиты, комья и траншея
            // доживают на месте за стеной выхода.
            if (_mound != null)
                for (int i = 0; i < _mound.Items.Length; i++)
                {
                    var m = _mound.Items[i];
                    if (m.Fx.Root.activeSelf && m.Follow == VfxFollow.Mound) SurfaceMound(m, (tick - m.Fx.Tick) / (float)Simulation.TicksPerSecond);
                }
            RetirePool(_diveTremor, false, 0f);
            // Вздутие (V14) уступает место выбросу в тот же кадр — его плиты и трещины подхватывает выход.
            RetirePool(_emergeBulge, false, 0f);
            var v = TakeVfx(_emerge, boss, tick, GroundAt(at), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick,
                ThicketMasterEarthRules.EmergeLife);
            if (v == null) return;
            // Корни вокруг ямы: выходят за 0,11 с, держатся до 1 с, уходят за 0,3 с.
            v.Fx.RiseSeconds = .11f; v.Fx.SinkSeconds = .3f; v.Fx.SinkAge = 1f;
        }

        // ------------------------------------------------------------ hooks: casts

        partial void OnSproutCast(int boss, int tick)
        {
            var sim = _driver.Sim;
            var v = TakeVfx(_sproutPress, boss, tick + SproutPressDelayTicks, GroundAt(BodyPosition()),
                Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 1f);
            if (v == null) return;
            v.Follow = VfxFollow.Toes; v.Pin = true;
            PlaceVfx(sim, v, -1f);
        }

        /// <summary>Дрожь земли под кругом прорастания до его удара; корешки высовываются за 6 тиков до шипов.</summary>
        partial void OnSproutMarked(int boss, int tick, int index)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketShape(boss, index, out FixVec2 c, out int impact, out bool resolved) || resolved) return;
            var v = TakeVfx(_sproutTremor, boss, tick, Ground(c), Yaw(tick * 8 + index), tick * 8 + index,
                (impact - tick) / (float)Simulation.TicksPerSecond + .1f);
            if (v == null) return;
            v.Index = index; v.Center = new Vector3(c.X.ToFloat(), 0f, c.Y.ToFloat()); v.Lead = tick - impact;
            v.Anchor = VfxAnchor.Shape; v.Clock = true;
        }

        partial void OnSproutImpact(int boss, int tick, int index, Vector3 at, bool hit)
        {
            if (_sproutTremor != null)
                for (int i = 0; i < _sproutTremor.Items.Length; i++)
                {
                    var t = _sproutTremor.Items[i];
                    if (t.Fx.Root.activeSelf && t.Index == index) RetireVfx(t);
                }
            TakeVfx(_sproutSpikes, boss, tick, GroundAt(at), Yaw(tick * 8 + index), tick * 8 + index, 1.6f);
        }

        /// <summary>Крона трясётся (эмиттеры на костях кроны), над каждым облаком — золотой столб до падения.</summary>
        partial void OnPollenCast(int boss, int tick, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a)) return;
            int land = tick + Simulation.ThicketPollenFallTicks;
            var shake = TakeVfx(_pollenShake, boss, tick, GroundAt(BodyPosition()), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up),
                a.Serial * 16, (land - tick) / (float)Simulation.TicksPerSecond + .6f);
            if (shake != null)
            {
                shake.Serial = a.Serial; shake.Lead = tick - a.LastImpactTick;
                shake.Anchor = VfxAnchor.LastImpact; shake.Clock = true;
                shake.Follow = VfxFollow.Crowns;
                PlaceVfx(sim, shake, -1f);
            }
            for (int k = 0; k < Simulation.ThicketPollenZones; k++)
            {
                if (!sim.TryGetThicketPollenZone(k, out ThicketPollenZone z) || z.Source != boss || z.StartTick != tick) continue;
                var v = TakeVfx(_pollenFall, boss, tick, Ground(z.Center), Yaw(z.Serial), z.Serial,
                    (z.LandTick - tick) / (float)Simulation.TicksPerSecond + .3f);
                if (v == null) continue;
                v.Index = k; v.Serial = z.Serial; v.Lead = tick - z.LandTick;
                v.Anchor = VfxAnchor.PollenLand; v.Clock = true;
            }
        }

        /// <summary>Облако легло: лежит, пока лежит зона Sim (вытеснена, босс умер — тает).</summary>
        partial void OnPollenLand(int boss, int tick, int slot, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            if (_pollenFall != null)
                for (int i = 0; i < _pollenFall.Items.Length; i++)
                {
                    var f = _pollenFall.Items[i];
                    if (f.Fx.Root.activeSelf && f.Index == slot) RetireVfx(f);
                }
            if (!sim.TryGetThicketPollenZone(slot, out ThicketPollenZone z)) return;
            int lieTicks = z.EndTick - z.LandTick + 1;
            float lies = lieTicks / (float)Simulation.TicksPerSecond;
            var v = TakeVfx(_pollenCloud, boss, tick, Ground(z.Center), Yaw(z.Serial), z.Serial, lies + .6f);
            if (v == null) return;
            v.Index = slot; v.Serial = z.Serial; v.LieTicks = lieTicks;
            v.Anchor = VfxAnchor.PollenWatch; v.Clock = true;
            v.FadeAge = lies; v.FadeSeconds = .5f;
        }

        partial void OnRainCast(int boss, int tick) { }

        /// <summary>
        /// Залп: пуф куста и по ягоде на каждый круг — летят дугой от куста к центру круга и
        /// касаются земли в тик удара. Метки кругов не нужны: центры и удары — TryGetThicketShape.
        /// </summary>
        partial void OnRainMarked(int boss, int tick, int volley)
        {
            var sim = _driver.Sim;
            Vector3 bush = BonePoint(_bossView != null ? _bossView.Bush : null, FallbackBush);
            Quaternion look = Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up);
            var puff = TakeVfx(_bushPuff, boss, tick, bush, look, tick * 8 + volley, .9f);
            if (puff != null) puff.Follow = VfxFollow.Bush;
            for (int k = 0; k < Simulation.ThicketRainCircles; k++)
            {
                int index = volley * Simulation.ThicketRainCircles + k;
                if (!sim.TryGetThicketShape(boss, index, out FixVec2 c, out int impact, out bool resolved) || resolved || impact <= tick) continue;
                var v = TakeVfx(_berry, boss, tick, bush, look, tick * 8 + index, 3f);
                if (v == null) return;
                v.Index = index; v.Center = new Vector3(c.X.ToFloat(), 0f, c.Y.ToFloat()); v.Lead = tick - impact;
                v.Anchor = VfxAnchor.Shape; v.Clock = true;
                v.Follow = VfxFollow.Flight;
                v.From = v.Last = bush; v.To = Ground(c); v.FlyTicks = impact - tick;
                v.Height = BerryArcHeight + BerryArcStep * k;
            }
        }

        /// <summary>Удар залпа: брызги сока и кусочки ягоды в каждом из четырёх кругов (попал или нет), луж нет.</summary>
        partial void OnRainVolley(int boss, int tick, int volley, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            int from = volley * Simulation.ThicketRainCircles;
            if (_berry != null)
                for (int i = 0; i < _berry.Items.Length; i++)
                {
                    var b = _berry.Items[i];
                    if (b.Fx.Root.activeSelf && b.Index >= from && b.Index < from + Simulation.ThicketRainCircles) RetireVfx(b);
                }
            for (int k = 0; k < Simulation.ThicketRainCircles; k++)
                if (sim.TryGetThicketShape(boss, from + k, out FixVec2 c, out _, out _))
                    TakeVfx(_berrySplat, boss, tick, Ground(c), Yaw(tick * 8 + from + k), tick * 8 + from + k, 1.9f);
        }

        // ------------------------------------------------------------ hooks: storm

        partial void OnStormBegin(int boss, int tick, int firstWaveTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a)) return;
            float span = (a.LastImpactTick - tick) / (float)Simulation.TicksPerSecond;
            var shed = TakeVfx(_crownShed, boss, tick, GroundAt(BodyPosition()), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up),
                a.Serial * 16, span + 1f + 3f);
            if (shed != null)
            {
                // Крона сыплет лепестки до LastImpactTick + 30, лепестки долетают сами.
                SetEmitSeconds(shed, span + 1f);
                shed.Serial = a.Serial; shed.Lead = tick - a.LastImpactTick;
                shed.Anchor = VfxAnchor.LastImpact; shed.Clock = true;
                shed.Follow = VfxFollow.Crowns;
                PlaceVfx(sim, shed, -1f);
            }
            var vortex = TakeVfx(_stormVortex, boss, tick, ArenaCenter(sim, boss), Quaternion.identity, a.Serial * 16 + 1, span + 1.5f);
            if (vortex != null)
            {
                vortex.Serial = a.Serial; vortex.Lead = tick - a.LastImpactTick;
                vortex.Anchor = VfxAnchor.LastImpact; vortex.Clock = true;
            }
            // Канал (ревью 02.10, вечер: «буря — непонятно, что босс делает… более явно»): от начала бури
            // до EndTick вокруг тела вихрь лепестков, у ног аура, кроны горят, с крон луч. «~»-системы
            // идут до EndTick, дальше лепестки долетают сами; срок — от LastImpactTick (Часы сдвигают).
            float channel = (a.EndTick - tick) / (float)Simulation.TicksPerSecond;
            var aura = TakeVfx(_stormChannel, boss, tick, GroundAt(BodyPosition()), Quaternion.identity, a.Serial * 16 + 2, channel + 4f);
            if (aura != null)
            {
                SetEmitSeconds(aura, channel);
                aura.Serial = a.Serial; aura.Lead = tick - a.LastImpactTick;
                aura.Anchor = VfxAnchor.LastImpact; aura.Clock = true;
                aura.Follow = VfxFollow.Channel;
                PlaceVfx(sim, aura, -1f);
            }
            Pillars(sim, boss, tick, 0);
        }

        partial void OnStormSecondWaveMarked(int boss, int tick, int secondWaveTick) => Pillars(_driver.Sim, boss, tick, 1);

        /// <summary>Столбы света над кругами-укрытиями волны: держатся до удара + 6 тиков, гаснут 0,3 с.</summary>
        private void Pillars(Simulation sim, int boss, int tick, int wave)
        {
            for (int k = 0; k < Simulation.ThicketStormSafeCircles; k++)
            {
                int index = wave * Simulation.ThicketStormSafeCircles + k;
                if (!sim.TryGetThicketShape(boss, index, out FixVec2 c, out int impact, out bool resolved) || resolved) continue;
                float hold = (impact - tick + Simulation.TelegraphLingerTicks) / (float)Simulation.TicksPerSecond;
                var v = TakeVfx(_lightPillar, boss, tick, Ground(c), Yaw(tick * 8 + index), tick * 8 + index, hold + .4f);
                if (v == null) return;
                SetEmitSeconds(v, hold);
                v.Index = index; v.Center = new Vector3(c.X.ToFloat(), 0f, c.Y.ToFloat()); v.Lead = tick - impact;
                v.Anchor = VfxAnchor.Shape; v.Clock = true;
                v.FadeAge = hold; v.FadeSeconds = .3f;
            }
        }

        partial void OnStormWave(int boss, int tick, int wave, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            // Коробки порыва — пол поляны 20,98 × 15,74 м (GladeLayout.BossFloorHalfWidth/Depth, поворот 0), как вихрь.
            TakeVfx(_stormWave, boss, tick, ArenaCenter(sim, boss), Quaternion.identity, tick * 2 + wave, 1.8f);
        }

        // ------------------------------------------------------------ cancel, death

        /// <summary>Шаг снят до удара (смерть босса или героя): гаснут замахи этого действия, удары доживают.</summary>
        partial void OnActionCancelled(int boss, EnemyActionKind kind, int stage, int tick)
        {
            switch (kind)
            {
                case EnemyActionKind.ThicketPaw: RetirePool(_pawSlash, true, tick); break;
                case EnemyActionKind.ThicketStomp: RetirePool(_stompRear, false, 0f); break;
                case EnemyActionKind.ThicketRoar: RetirePool(_roarInhale, false, 0f); break;
                case EnemyActionKind.ThicketDive:
                    RetirePool(_diveTremor, false, 0f);
                    RetirePool(_emergeBulge, false, 0f);
                    RetirePool(_mound, false, 0f);
                    break;
                case EnemyActionKind.ThicketPollen:
                    RetirePool(_pollenShake, false, 0f);
                    RetirePool(_pollenFall, false, 0f);
                    break;
                case EnemyActionKind.ThicketRain:
                    RetirePool(_berry, false, 0f);
                    RetirePool(_bushPuff, false, 0f);
                    break;
                case EnemyActionKind.ThicketStorm:
                    RetirePool(_crownShed, false, 0f);
                    RetirePool(_stormVortex, false, 0f);
                    RetirePool(_lightPillar, false, 0f);
                    RetirePool(_stormChannel, false, 0f);
                    break;
                // Терновник: Amount — номер шипа, снятого в полёте (ThicketMasterCombatView.Seeds); кусты гаснут сами.
                case EnemyActionKind.ThicketSeeds:
                    SeedsCancelled(boss, stage, tick);
                    break;
            }
        }

        /// <summary>
        /// Смерть — «цветущий холм» (владелец 02.10, вечер: «смерть надо доработать»; время —
        /// ThicketMasterDeathRules): все замахи и петли гаснут. DeathBloom — сразу розово-золотая вспышка в
        /// кусте и кольцо лепестков с листьями, лепестки с крон; на касании боком (LandsAt, «Late …») —
        /// волна цветения до 6 м, пыль и комья у кромки холма. DeathHill — холм встаёт из земли, тело
        /// уходит под него (ThicketMasterAnimatorView), холм зацветает крупными цветами, травой и ростками
        /// и лежит до смены арены (OnFrame снимает его с глубиной, поколением, симуляцией). Оба корня — у
        /// середины падающего тела до касания (BodyCentroid), дальше стоят. Наезд камеры — ThicketMasterDeathView.
        /// V13: по холму ходят — он ложится поверх пола (RegisterKnoll → KnollFloor) и поднимает тела вместе с ростом.
        /// </summary>
        partial void OnBossKilled(int boss, int tick, Vector3 at)
        {
            var sim = _driver.Sim;
            RetirePool(_pawSlash, true, tick);
            RetirePool(_stompRear, false, 0f); RetirePool(_roarInhale, false, 0f);
            RetirePool(_diveTremor, false, 0f); RetirePool(_emergeBulge, false, 0f); RetirePool(_mound, false, 0f);
            RetirePool(_pollenShake, false, 0f); RetirePool(_pollenFall, false, 0f);
            RetirePool(_berry, false, 0f); RetirePool(_bushPuff, false, 0f); RetirePool(_sproutTremor, false, 0f);
            RetirePool(_crownShed, false, 0f); RetirePool(_stormVortex, false, 0f); RetirePool(_lightPillar, false, 0f);
            RetirePool(_stormChannel, false, 0f);
            // Терновник: кусты Sim снимает сразу — вид дорисовывает увядание сам; летящие шипы снимают события (Cancelled).

            var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
            float settle = beat.LandsAt > 0f ? beat.LandsAt
                : beat.HitStopSeconds + EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster).FallSeconds;
            Vector3 facing = _bossBody != null ? _bossBody.forward : FacingOf(sim, boss);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            Quaternion look = Quaternion.LookRotation(facing.normalized, Vector3.up);
            Vector3 centre = _bossBody != null || _bossView != null ? GroundAt(BodyCentroid()) : GroundAt(at);

            RetirePool(_deathHill, false, 0f);
            ReleaseKnoll();
            var v = TakeVfx(_deathBloom, boss, tick, centre, look, tick, settle + ThicketMasterDeathRules.SettledAfterLand);
            if (v != null)
            {
                for (int i = 0; i < v.Late.Length; i++) v.Fx.Delays[v.Late[i]] = settle;
                // Куст и кроны — места удара (системы вспышки короткие, лепестки — в мире): ставятся раз.
                if (v.Bush != null) v.Bush.position = BonePoint(_bossView != null ? _bossView.Bush : null, FallbackBush);
                if (v.Left != null) v.Left.position = BonePoint(_bossView != null ? _bossView.CrownLeft : null, FallbackCrownLeft);
                if (v.Right != null) v.Right.position = BonePoint(_bossView != null ? _bossView.CrownRight : null, FallbackCrownRight);
                v.Follow = VfxFollow.Hill; v.PinAge = settle;
            }

            // Холм: все системы стартуют на касании (свои задержки — рост, цветение — в префабе), живут до смены арены.
            var hill = TakeVfx(_deathHill, boss, tick, centre, look, tick, ThicketMasterDeathRules.HillLifeSeconds);
            if (hill == null) return;
            for (int k = 0; k < hill.Fx.Delays.Length; k++) hill.Fx.Delays[k] = settle;
            hill.Follow = VfxFollow.Hill; hill.PinAge = settle;
            hill.FreezeAge = settle + ThicketMasterDeathRules.SettledAfterLand;
            // Тёплый свет над пригорком загорается с цветением (V11).
            hill.LightLand = settle;
            // По пригорку ходят (V13): тела встают на его верх по мере роста.
            RegisterKnoll(hill, boss, settle);
        }

        // ------------------------------------------------------------ knoll floor (V13: по пригорку ходят), hero clear

        /// <summary>
        /// Вид, чей холм смерти лежит поверх пола (LayoutView.FloorRaise → <see cref="KnollFloor"/>); null — холма нет.
        /// Статический: пол спрашивают тела и курсор (TickDriver.GetRenderPosition, наведение), у них нет вида босса.
        /// </summary>
        private static ThicketMasterCombatView _knollOwner;

        /// <summary>Делегат крючка пола — один на всё время (подписка и сравнение без аллокаций).</summary>
        private static readonly System.Func<float, float, float, float> KnollFloorHook = KnollFloor;

        /// <summary>Холм смерти этого вида, по которому ходят: экземпляр, его тик, касание боком (с от удара); null — нет.</summary>
        private Vfx _knoll;
        private int _knollTick, _knollBoss = -1;
        private float _knollLand;

        /// <summary>
        /// Холм встал на поляне (OnBossKilled): тела поднимаются на него по мере роста (ThicketMasterDeathRules.KnollFloor).
        /// Снимать не нужно: крючок сам перестаёт поднимать, когда экземпляр холма снят (все пути — RetireVfx), сменились
        /// симуляция, поколение или глубина; ReleaseKnoll — только чтобы не держать ссылку.
        /// </summary>
        private void RegisterKnoll(Vfx hill, int boss, float land)
        {
            _knoll = hill;
            _knollTick = hill.Fx.Tick;
            _knollBoss = boss;
            _knollLand = land;
            _knollOwner = this;
            LayoutView.FloorRaise = KnollFloorHook;
        }

        private void ReleaseKnoll()
        {
            _knoll = null;
            _knollBoss = -1;
            if (_knollOwner != this) return;
            _knollOwner = null;
            if (LayoutView.FloorRaise == KnollFloorHook) LayoutView.FloorRaise = null;
        }

        /// <summary>Без перезагрузки домена (Enter Play Mode Options) статика переживает Play — снять крючок прошлого запуска.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetKnollFloor()
        {
            _knollOwner = null;
            if (LayoutView.FloorRaise == KnollFloorHook) LayoutView.FloorRaise = null;
        }

        /// <summary>
        /// Пол с холмом смерти (крючок LayoutView.ShownFloorLevel): в точке (x, z) пола floor — большее из пола и видимого
        /// верха холма (ThicketMasterDeathRules.KnollFloor в осях холма, рост — по тикам Sim, как у частиц его сеток).
        /// Без аллокаций; вне холма — пол без счёта рельефа.
        /// </summary>
        public static float KnollFloor(float x, float z, float floor)
        {
            var owner = _knollOwner;
            if ((object)owner == null) return floor;
            if (owner == null) { _knollOwner = null; return floor; }
            float top = owner.KnollSurfaceAt(x, z);
            return top > floor ? top : floor;
        }

        /// <summary>Видимый верх холма смерти этого вида в мировой точке (x, z), м; −∞ — холма там нет (ещё не встал, снят, вне его).</summary>
        private float KnollSurfaceAt(float x, float z)
        {
            var hill = _knoll;
            if (hill == null || _driver == null) return float.NegativeInfinity;
            var fx = hill.Fx;
            if (fx.Root == null || !fx.Root.activeSelf || fx.Tick != _knollTick) return float.NegativeInfinity;
            var sim = _driver.Sim;
            if (sim == null || sim != _shown || _driver.Generation != _vfxGeneration || RunDepth() != _vfxDepth) return float.NegativeInfinity;
            // Рост — по тикам Sim с долей кадра, как частицы сеток (AdvanceVfx: возраст от fx.Tick, задержки — касание).
            float age = (sim.Tick - 1 + _driver.Alpha - fx.Tick) / Simulation.TicksPerSecond;
            float rise = ThicketMasterDeathRules.KnollRiseFraction(age, _knollLand);
            if (rise <= 0f) return float.NegativeInfinity;
            // Тело убитого босса уходит ПОД холм (ThicketMasterAnimatorView) — его точку Sim холм не поднимает.
            if (_knollBoss >= 0 && _knollBoss < sim.Entities.Count)
            {
                FixVec2 at = sim.Entities.Position[_knollBoss];
                if (Mathf.Abs(x - at.X.ToFloat()) < 1e-3f && Mathf.Abs(z - at.Y.ToFloat()) < 1e-3f) return float.NegativeInfinity;
            }
            Transform root = fx.Root.transform;
            Vector3 centre = root.position;
            Vector3 local = root.InverseTransformPoint(new Vector3(x, centre.y, z));
            if (ThicketMasterDeathRules.OffKnoll(local.x, local.z)) return float.NegativeInfinity;
            return centre.y + root.lossyScale.y * ThicketMasterDeathRules.KnollFloor(local.x, local.z, rise);
        }

        /// <summary>Буфер частиц на самую большую систему пулов, где вид правит частицы.</summary>
        private static int ParticleBufferSize(VfxPool[] pools)
        {
            int size = 1;
            foreach (var pool in pools)
                for (int i = 0; i < pool.Items.Length; i++)
                    foreach (var ps in pool.Items[i].Fx.Particles) size = Mathf.Max(size, ps.main.maxParticles);
            return size;
        }

        /// <summary>Лепестки бури не стоят у героя: радиус от луча камеры через грудь героя, м (полностью гаснут ближе HeroClearInner).</summary>
        public const float HeroClearRadius = 1.5f, HeroClearInner = .9f;

        /// <summary>
        /// Буря (ревью 02.10, вечер: «конфетти лепестков вокруг босса прячет босса и героя»): частицы вихря,
        /// крон, канала и порыва ближе HeroClearRadius к лучу камеры через грудь героя (на экране — круг около
        /// героя) гаснут и больше не загораются (альфа частицы — не выше доли по расстоянию). Лепесток живёт 2–3 с,
        /// поток новых идёт — у героя чисто, вокруг буря та же. Луч канала (одна частица на всю бурю) не гаснет:
        /// ThicketStormDangerRules.ClearsAroundHero.
        /// </summary>
        private void ClearAroundHero(Vfx v)
        {
            if (_driver == null || _driver.Sim == null) return;
            Vector3 chest = _driver.GetRenderPosition(Simulation.PlayerId) + Vector3.up;
            var camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.743f, .669f);
            float outer = HeroClearRadius * HeroClearRadius;
            for (int k = 0; k < v.Fx.Particles.Length; k++)
            {
                var ps = v.Fx.Particles[k];
                int n = ps.particleCount;
                if (n == 0) continue;
                var main = ps.main;
                if (!ThicketStormDangerRules.ClearsAroundHero(main.maxParticles, main.startLifetime.constantMax)) continue;
                if (n > _particleBuffer.Length) n = _particleBuffer.Length;
                n = ps.GetParticles(_particleBuffer, n);
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                Transform space = ps.transform;
                bool changed = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 world = local ? space.TransformPoint(_particleBuffer[i].position) : _particleBuffer[i].position;
                    Vector3 rel = world - chest;
                    rel -= view * Vector3.Dot(rel, view);
                    float d2 = rel.sqrMagnitude;
                    if (d2 >= outer) continue;
                    float t = Mathf.Clamp01((Mathf.Sqrt(d2) - HeroClearInner) / (HeroClearRadius - HeroClearInner));
                    byte alpha = (byte)(255f * t * t * (3f - 2f * t));
                    var color = _particleBuffer[i].startColor;
                    if (color.a <= alpha) continue;
                    color.a = alpha;
                    _particleBuffer[i].startColor = color;
                    changed = true;
                }
                if (changed) ps.SetParticles(_particleBuffer, n);
            }
        }

        // ------------------------------------------------------------ places

        // Кости без вида тела (серая заглушка): точки модели в позе привязки, м при росте 4,14
        // (замер разведки 02.10 по ThicketMaster_Rig.blend ×1,16), оси корня тела: +Z — взгляд.
        private static readonly Vector3 FallbackBush = new Vector3(0f, 1.5f, 1.9f);
        private static readonly Vector3 FallbackCrownLeft = new Vector3(-1.15f, 3.35f, 2.2f), FallbackCrownRight = new Vector3(1.15f, 3.35f, 2.2f);
        private static readonly Vector3 FallbackToeLeft = new Vector3(-.95f, 0f, 2f), FallbackToeRight = new Vector3(.95f, 0f, 2f);

        private Vector3 BodyPosition()
        {
            if (_bossBody != null) return _bossBody.position;
            return _boss >= 0 && _driver != null ? _driver.GetRenderPosition(_boss) : transform.position;
        }

        /// <summary>
        /// Середина тела по костям (смерть: лёжа на боку туловище и крона уходят вбок от корня): корень и
        /// грудь ×2, куст и голова ×1, кроны ×0,5; не дальше HillCentroidReach м от корня. Нет вида — корень.
        /// </summary>
        private Vector3 BodyCentroid()
        {
            Vector3 root = BodyPosition();
            if (_bossView == null) return root;
            Vector3 sum = root * 2f;
            float weight = 2f;
            Accumulate(_bossView.Chest, 2f, ref sum, ref weight);
            Accumulate(_bossView.Bush, 1f, ref sum, ref weight);
            Accumulate(_bossView.Head, 1f, ref sum, ref weight);
            Accumulate(_bossView.CrownLeft, .5f, ref sum, ref weight);
            Accumulate(_bossView.CrownRight, .5f, ref sum, ref weight);
            Vector3 shift = sum / weight - root;
            shift.y = 0f;
            return root + Vector3.ClampMagnitude(shift, HillCentroidReach);
        }

        /// <summary>Середина тела для холма — не дальше стольких метров от корня.</summary>
        private const float HillCentroidReach = 1.8f;

        private static void Accumulate(Transform bone, float w, ref Vector3 sum, ref float weight)
        {
            if (bone == null) return;
            sum += bone.position * w;
            weight += w;
        }

        /// <summary>Мировая точка кости; нет кости — точка модели local от корня тела.</summary>
        private Vector3 BonePoint(Transform bone, Vector3 local)
        {
            if (bone != null) return bone.position;
            Quaternion rotation = _bossBody != null ? _bossBody.rotation : Quaternion.identity;
            return BodyPosition() + rotation * local;
        }

        private Vector3 GroundAt(Vector3 p) => new Vector3(p.x, GroundY(p.x, p.z), p.z);

        private Vector3 Ground(FixVec2 p) => GroundAt(new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat()));

        private float GroundY(float x, float z) => _vfxLayout != null ? _vfxLayout.WeaponGroundHeight(x, z) : 0f;

        private static Vector3 FacingOf(Simulation sim, int id)
        {
            if ((uint)id >= (uint)sim.Entities.Count) return Vector3.forward;
            var f = sim.Entities.Facing[id];
            var forward = new Vector3(f.X.ToFloat(), 0f, f.Y.ToFloat());
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        /// <summary>Центр арены босса (бури): точка поводка — середина поляны (не угол, где босс встаёт); нет памяти — тело.</summary>
        private Vector3 ArenaCenter(Simulation sim, int boss)
        {
            if (sim.TryGetThicketMasterMemory(boss, out ThicketMasterMemory m)) return Ground(m.Home);
            return GroundAt(BodyPosition());
        }

        /// <summary>Поворот вокруг вертикали по номеру — круги не повторяют друг друга, повтор даёт тот же.</summary>
        private static Quaternion Yaw(int serial)
        {
            uint h = (uint)serial * 2654435761u;
            h ^= h >> 15;
            return Quaternion.Euler(0f, (h % 3600u) * .1f, 0f);
        }

        // ------------------------------------------------------------ claw trails

        /// <summary>
        /// Три светящиеся ленты когтей одного удара (ревью 02.10: «след плоский и идёт не за лапой,
        /// а после неё»). Точки — путь кости пальцев (Step), их возраст — часы босса: хит-стоп,
        /// пауза и Часы держат ленту вместе с лапой, перемотка съёмки снимает точки «из будущего».
        /// Лента — сглаженный Catmull-Rom путь последних keep секунд, повёрнутая к камере, по
        /// поперёк три полосы вершин (тёмный край — ядро — тёмный край), когти — со сдвигом вбок;
        /// к хвосту сужается и гаснет: ядро золото → листовое золото → тёмная листва. Сетка, массивы
        /// и треугольники — при создании пула: в кадре ни одной аллокации.
        /// </summary>
        private sealed class ClawTrail
        {
            private const int MaxSamples = 24, Subdivisions = 3, MaxPoints = (MaxSamples - 1) * Subdivisions + 1;
            private const int Claws = 3, Lanes = 3, ClawVertices = MaxPoints * Lanes;
            /// <summary>Не чаще одной точки за столько секунд часов: голова между ними едет за костью.</summary>
            private const float MinStep = 1f / 90f;

            private readonly Transform _space;
            private readonly Mesh _mesh;
            private readonly Vector3[] _samples = new Vector3[MaxSamples];
            private readonly float[] _times = new float[MaxSamples];
            private readonly Vector3[] _points = new Vector3[MaxPoints];
            private readonly float[] _pointTimes = new float[MaxPoints];
            private readonly Vector3[] _vertices = new Vector3[Claws * ClawVertices];
            private readonly Color32[] _colors = new Color32[Claws * ClawVertices];
            private readonly Vector2[] _uvs = new Vector2[Claws * ClawVertices];
            private int _count;

            public ClawTrail(MeshFilter filter)
            {
                _space = filter.transform;
                _mesh = new Mesh { name = "ThicketClawTrail" };
                _mesh.MarkDynamic();
                _mesh.vertices = _vertices;
                _mesh.colors32 = _colors;
                _mesh.uv = _uvs;
                var triangles = new int[Claws * (MaxPoints - 1) * 12];
                int t = 0;
                for (int c = 0; c < Claws; c++)
                    for (int k = 0; k < MaxPoints - 1; k++)
                    {
                        int a = c * ClawVertices + k * Lanes, b = a + Lanes;
                        triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                        triangles[t++] = a + 1; triangles[t++] = b + 1; triangles[t++] = b;
                        triangles[t++] = a + 1; triangles[t++] = a + 2; triangles[t++] = b + 1;
                        triangles[t++] = a + 2; triangles[t++] = b + 2; triangles[t++] = b + 1;
                    }
                _mesh.triangles = triangles;
                filter.sharedMesh = _mesh;
            }

            public void Clear() => _count = 0;

            /// <summary>Сетка создана в рантайме — Unity её сама не удалит (нет ссылок из сцены).</summary>
            public void Dispose()
            {
                if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            }

            /// <summary>Точка кости на возрасте age (с); emit — окно удара идёт; keep — длина хвоста, с.</summary>
            public void Step(float age, Vector3 point, bool emit, float keep)
            {
                while (_count > 0 && _times[_count - 1] > age + 1e-4f) _count--;
                int drop = 0;
                while (_count - drop >= 2 && age - _times[drop + 1] > keep) drop++;
                if (drop > 0)
                {
                    System.Array.Copy(_samples, drop, _samples, 0, _count - drop);
                    System.Array.Copy(_times, drop, _times, 0, _count - drop);
                    _count -= drop;
                }
                if (!emit) return;
                // Голова ближе MinStep к прошлой точке — голова едет за костью; дальше — новая точка.
                bool replace = _count > 0 && (age - _times[_count - 1] < 1e-5f || (_count >= 2 && _times[_count - 1] - _times[_count - 2] < MinStep));
                if (replace)
                {
                    _samples[_count - 1] = point;
                    _times[_count - 1] = age;
                    return;
                }
                if (_count == MaxSamples)
                {
                    System.Array.Copy(_samples, 1, _samples, 0, MaxSamples - 1);
                    System.Array.Copy(_times, 1, _times, 0, MaxSamples - 1);
                    _count--;
                }
                _samples[_count] = point;
                _times[_count] = age;
                _count++;
            }

            /// <summary>Сетка на возраст age: view — взгляд камеры, keep — хвост, с; width — лента, spacing — шаг когтей, м.</summary>
            public void Build(float age, Vector3 view, float keep, float width, float spacing)
            {
                int n = Subdivide();
                Matrix4x4 toLocal = _space.worldToLocalMatrix;
                for (int c = 0; c < Claws; c++)
                {
                    // Средний коготь длиннее и толще, крайние — короче: след не одной полосой.
                    float lane = (c - 1) * spacing, reach = c == 1 ? 1f : .78f, thick = c == 1 ? 1f : .85f;
                    Vector3 side = Vector3.right;
                    int start = c * ClawVertices;
                    for (int k = 0; k < MaxPoints; k++)
                    {
                        int at = start + k * Lanes;
                        if (k >= n)
                        {
                            Vector3 rest = k == 0 ? Vector3.zero : _vertices[at - Lanes + 1];
                            for (int l = 0; l < Lanes; l++)
                            {
                                _vertices[at + l] = rest;
                                _colors[at + l] = new Color32(0, 0, 0, 0);
                                _uvs[at + l] = new Vector2(.5f, .5f);
                            }
                            continue;
                        }
                        Vector3 tangent = _points[Mathf.Min(k + 1, n - 1)] - _points[Mathf.Max(k - 1, 0)];
                        Vector3 across = Vector3.Cross(tangent, view);
                        if (across.sqrMagnitude > 1e-8f) side = across.normalized;
                        float f = Mathf.Clamp01((age - _pointTimes[k]) / Mathf.Max(.01f, keep * reach));
                        float half = width * thick * .5f * Mathf.Pow(1f - f, .6f);
                        Vector3 center = _points[k] + side * lane;
                        _vertices[at] = toLocal.MultiplyPoint3x4(center - side * half);
                        _vertices[at + 1] = toLocal.MultiplyPoint3x4(center);
                        _vertices[at + 2] = toLocal.MultiplyPoint3x4(center + side * half);
                        float alpha = Mathf.Pow(1f - f, 1.4f);
                        _colors[at + 1] = Core(f, alpha);
                        _colors[at] = _colors[at + 2] = new Color32(22, 32, 10, (byte)(alpha * 230f));
                        float u = .1f + .8f * f;
                        _uvs[at] = new Vector2(u, .22f);
                        _uvs[at + 1] = new Vector2(u, .5f);
                        _uvs[at + 2] = new Vector2(u, .78f);
                    }
                }
                _mesh.vertices = _vertices;
                _mesh.colors32 = _colors;
                _mesh.uv = _uvs;
                _mesh.RecalculateBounds();
            }

            /// <summary>Сглаженный путь (Catmull-Rom по Subdivisions на отрезок): точки от хвоста к голове.</summary>
            private int Subdivide()
            {
                if (_count == 0) return 0;
                int n = 0;
                for (int i = 0; i < _count - 1; i++)
                {
                    Vector3 p0 = _samples[Mathf.Max(i - 1, 0)], p1 = _samples[i], p2 = _samples[i + 1], p3 = _samples[Mathf.Min(i + 2, _count - 1)];
                    for (int s = 0; s < Subdivisions; s++)
                    {
                        float t = s / (float)Subdivisions, t2 = t * t, t3 = t2 * t;
                        _points[n] = .5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
                        _pointTimes[n] = Mathf.Lerp(_times[i], _times[i + 1], t);
                        n++;
                    }
                }
                _points[n] = _samples[_count - 1];
                _pointTimes[n] = _times[_count - 1];
                return n + 1;
            }

            /// <summary>Ядро по доле хвоста f: золото → листовое золото → листва → тёмная листва (свечение — _HdrMultiply материала).</summary>
            private static Color32 Core(float f, float alpha)
            {
                Color gold = new Color(1f, .89f, .47f), leafGold = new Color(.84f, 1f, .43f), leaf = new Color(.47f, .67f, .2f), dark = new Color(.16f, .24f, .07f);
                Color c = f < .35f ? Color.Lerp(gold, leafGold, f / .35f)
                    : f < .75f ? Color.Lerp(leafGold, leaf, (f - .35f) / .4f)
                    : Color.Lerp(leaf, dark, (f - .75f) / .25f);
                c.a = alpha;
                return c;
            }
        }

        // ------------------------------------------------------------ dive furrow (V11, V15)

        /// <summary>
        /// Борозда за бугром (V11, ревью 03.10: цепочка гладких валиков «Trail Ridge» читалась камнями через ручей): одна
        /// сплошная лента по пройденному пути. Точки — путь головы по земле через Spacing м (за кадр далеко — точки
        /// вставляются через шаг), их возраст — возраст бугра (тики Sim: пауза держит, перемотка снимает точки «из будущего»).
        /// V15 (владелец 07.10: «плоский шарик земляной» — земля бугра гладкая, «пластилин»): лента фактурная, тем же
        /// атласом, что плиты дёрна (M_Thicket_Turf): поперёк 15 точек (ThicketMasterEarthRules.FurrowAcross) — дёрн по
        /// краям поднят к рваной губе, за губой желоб, в середине навал каменистой земли комьями; линия разрыва дёрна рваная
        /// по пройденному пути (не «плывёт»); на шве дёрн | земля точки раздвоены — у каждой половины атласа свои UV,
        /// треугольник шва не строится. По возрасту точки (FurrowLift): встаёт за 0,08 с, держится до FurrowHold, к Life
        /// осыпается — вширь и в землю. V17 (владелец 08.10: «как будто холмик скользит… без вау-эффекта пробуривания»):
        /// кучи над головой нет — лента и есть разлом: траншея (губы дёрна выше всего, внутри низкая тёмная земля комьями,
        /// ~2,1 × 0,4 м), над самой головой земля вспучена почти до губ (FurrowHeave по пути от головы) и за ней опадает;
        /// точки стоят на месте — меняется только их высота, фактура по миру не плывёт. Тон — только вершинами (серый
        /// Color32: сырая земля темнее, желоб — тень). Сетка, массивы и треугольники — при создании пула: в кадре ни одной
        /// аллокации.
        /// </summary>
        private sealed class FurrowTrail
        {
            /// <summary>Сколько живёт точка траншеи, с: встала → осыпалась в землю.</summary>
            public const float Life = ThicketMasterEarthRules.FurrowLife;
            /// <summary>
            /// Точек на ленту: на самом быстром ходу бугра (догон, ThicketMoundMaxStep 0,4 м за тик, 12 м/с) за Life + кадр
            /// путь ~20 м — ~83 точки через Spacing с головой и одной старше Life; 96 — с запасом. Поперёк — Across.
            /// </summary>
            private const int MaxSamples = ThicketMasterEarthRules.FurrowMaxSamples, Across = ThicketMasterEarthRules.FurrowColumns;
            /// <summary>Шаг точек по пути, м; полуширина и высота ленты, м.</summary>
            private const float Spacing = .24f, HalfWidth = ThicketMasterEarthRules.FurrowHalfWidth, Height = ThicketMasterEarthRules.FurrowHeight;

            private readonly Transform _space;
            private readonly Mesh _mesh;
            private readonly Vector3[] _points = new Vector3[MaxSamples];
            private readonly float[] _times = new float[MaxSamples], _along = new float[MaxSamples];
            private readonly Vector3[] _world = new Vector3[MaxSamples * Across];
            private readonly Vector3[] _vertices = new Vector3[MaxSamples * Across];
            private readonly Vector3[] _normals = new Vector3[MaxSamples * Across];
            private readonly Color32[] _colors = new Color32[MaxSamples * Across];
            private readonly Vector2[] _uvs = new Vector2[MaxSamples * Across];
            private int _count;

            public FurrowTrail(MeshFilter filter)
            {
                _space = filter.transform;
                _mesh = new Mesh { name = "ThicketMoundFurrow" };
                _mesh.MarkDynamic();
                for (int i = 0; i < _normals.Length; i++) _normals[i] = Vector3.up;
                _mesh.vertices = _vertices;
                _mesh.normals = _normals;
                _mesh.colors32 = _colors;
                _mesh.uv = _uvs;
                int quads = 0;
                for (int c = 0; c < Across - 1; c++) if (!ThicketMasterEarthRules.FurrowSeam(c)) quads++;
                var triangles = new int[(MaxSamples - 1) * quads * 6];
                int t = 0;
                for (int i = 0; i < MaxSamples - 1; i++)
                    for (int c = 0; c < Across - 1; c++)
                    {
                        // Шов дёрн | земля: точки совпадают, UV — с разных половин атласа — треугольника нет.
                        if (ThicketMasterEarthRules.FurrowSeam(c)) continue;
                        // a — слева сзади, b — справа сзади, d — слева спереди, e — справа спереди: лицом вверх.
                        int a = i * Across + c, b = a + 1, d = a + Across, e = d + 1;
                        triangles[t++] = a; triangles[t++] = d; triangles[t++] = e;
                        triangles[t++] = a; triangles[t++] = e; triangles[t++] = b;
                    }
                _mesh.triangles = triangles;
                filter.sharedMesh = _mesh;
            }

            public void Clear() => _count = 0;

            /// <summary>Сетка создана в рантайме — Unity её сама не удалит.</summary>
            public void Dispose()
            {
                if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            }

            /// <summary>Голова бугра в point (земля) на возрасте age, с; moving — бугор едет (стоит — новых точек нет).</summary>
            public void Step(float age, Vector3 point, bool moving)
            {
                while (_count > 0 && _times[_count - 1] > age + 1e-4f) _count--;
                int drop = 0;
                while (drop < _count - 1 && age - _times[drop + 1] > Life) drop++;
                if (drop > 0)
                {
                    System.Array.Copy(_points, drop, _points, 0, _count - drop);
                    System.Array.Copy(_times, drop, _times, 0, _count - drop);
                    System.Array.Copy(_along, drop, _along, 0, _count - drop);
                    _count -= drop;
                }
                if (_count == 0) { Append(point, age, 0f); return; }
                if (!moving) return;
                // Все точки, кроме последней, стоят ровно через Spacing по пути; последняя — голова, едет за бугром
                // (ревью 03.10: прежде старая голова застывала точкой почти каждый кадр — шаг 0,13–0,18 м, и на самом
                // быстром ходу бугра 16,5 м/с 84 точек не хватало на Life — хвост рубился ступенями в 0,7–0,9 с).
                // Новые точки кладутся от последней стоящей по пути «стоящая → прежняя голова → бугор».
                int fixedAt = _count >= 2 ? _count - 2 : 0;
                Vector3 anchor = _points[fixedAt], head = _points[_count - 1];
                float anchorTime = _times[fixedAt], headTime = _times[_count - 1], anchorAlong = _along[fixedAt];
                float toHead = Flat(head - anchor), toPoint = Flat(point - head), path = toHead + toPoint;
                if (toPoint < 1e-4f) return;
                if (_count >= 2) _count--;
                float laid = 0f;
                // Скачок за кадр больше ленты (перемотка, рывок) — кладутся только последние MaxSamples − 1 точек.
                int last = Mathf.FloorToInt((path - .02f) / Spacing);
                for (int k = Mathf.Max(1, last - (MaxSamples - 2)); k <= last; k++)
                {
                    float d = k * Spacing;
                    Vector3 at;
                    float time;
                    if (d <= toHead && toHead > 1e-4f)
                    {
                        float f = d / toHead;
                        at = Vector3.Lerp(anchor, head, f);
                        time = Mathf.Lerp(anchorTime, headTime, f);
                    }
                    else
                    {
                        float f = (d - toHead) / toPoint;
                        at = Vector3.Lerp(head, point, f);
                        time = Mathf.Lerp(headTime, age, f);
                    }
                    Append(at, time, anchorAlong + d);
                    laid = d;
                }
                // Голова: от последней стоящей точки — по прямой (её шум по пути не «плывёт» от кадра к кадру).
                Append(point, age, anchorAlong + laid + Flat(point - _points[_count - 1]));
            }

            private void Append(Vector3 point, float time, float along)
            {
                if (_count == MaxSamples)
                {
                    System.Array.Copy(_points, 1, _points, 0, MaxSamples - 1);
                    System.Array.Copy(_times, 1, _times, 0, MaxSamples - 1);
                    System.Array.Copy(_along, 1, _along, 0, MaxSamples - 1);
                    _count--;
                }
                _points[_count] = point;
                _times[_count] = time;
                _along[_count] = along;
                _count++;
            }

            private static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

            /// <summary>Сетка на возраст age, с.</summary>
            public void Build(float age)
            {
                int n = _count;
                Vector3 tangent = Vector3.forward;
                // Голова бугра — последняя точка: путь от неё назад решает, насколько земля над точкой вспучена (FurrowHeave).
                float headAlong = n > 0 ? _along[n - 1] : 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector3 ahead = _points[Mathf.Min(i + 1, n - 1)] - _points[Mathf.Max(i - 1, 0)];
                    ahead.y = 0f;
                    if (ahead.sqrMagnitude > 1e-8f) tangent = ahead.normalized;
                    var side = new Vector3(tangent.z, 0f, -tangent.x);
                    float a = age - _times[i], s = _along[i];
                    // Встаёт за 0,08 с с перелётом, держится до FurrowHold, к Life осыпается: ниже, шире, в землю.
                    float fall = ThicketMasterEarthRules.FurrowFall(a);
                    // Над головой земля вспучена почти до губ и за ней опадает в траншею (V17): точка стоит — меняется высота.
                    float heave = ThicketMasterEarthRules.FurrowHeave(headAlong - s);
                    float lift = Height * ThicketMasterEarthRules.FurrowLift(a) * Smooth(s / .8f);
                    float spread = 1f + .25f * fall, sink = -.03f * fall;
                    // Рваные края, линия разрыва дёрна и комья навала — шум по пройденному пути: лента не «плывёт».
                    float widthLeft = 1f + .22f * Noise(s * 1.4f, 3), widthRight = 1f + .22f * Noise(s * 1.4f, 17);
                    float tearLeft = ThicketMasterEarthRules.FurrowTear(s, false), tearRight = ThicketMasterEarthRules.FurrowTear(s, true);
                    float heap = ThicketMasterEarthRules.FurrowHeapBase + ThicketMasterEarthRules.FurrowHeapSwing * Noise(s * 3.7f, 31);
                    float wet = .06f * Noise(s * 3.1f, 41);
                    for (int c = 0; c < Across; c++)
                    {
                        float u = ThicketMasterEarthRules.FurrowAcross(c, tearLeft, tearRight), au = Mathf.Abs(u);
                        bool soil = ThicketMasterEarthRules.FurrowSoil(c);
                        // Точки шва (губа дёрна и срез земли) — одна точка: у обеих один шум.
                        int lane = c == 4 ? 3 : c == 10 ? 11 : c;
                        bool cut = lane != c;
                        float crumbs = Noise(s * 4.6f + lane * 7.3f, 53);
                        float w = (u < 0f ? widthLeft : widthRight) * HalfWidth * spread * (au >= 1f ? 1f : 1f + .1f * crumbs);
                        Vector3 p = _points[i] + side * (u * w) + tangent * (.07f * crumbs * Mathf.Min(1f, au));
                        float y;
                        if (au >= 1.05f) y = p.y - .07f;
                        else if (au >= 1f) y = p.y - .012f + sink;
                        else
                        {
                            // Земля внутри — комьями (шум по пути и поперёк); губа дёрна и срез — ровнее.
                            float rough = soil && !cut ? heap * (1f + ThicketMasterEarthRules.FurrowCrumbSwing * crumbs) : 1f + .1f * crumbs;
                            float body = ThicketMasterEarthRules.FurrowBody(c) + heave * ThicketMasterEarthRules.FurrowHeadExtra(c);
                            y = p.y + sink + lift * body * rough;
                        }
                        int at = i * Across + c;
                        _world[at] = new Vector3(p.x, y, p.z);
                        // Тон вершиной (материал — тон пола): дёрн почти как плиты, губа и край темнее; земля сырая, желоб — тень
                        // (своей тени у Particles/Lit нет — глубину траншеи держит тон: под губой и на дне темнее губ).
                        float tone;
                        if (!soil) tone = (lane == 3 || lane == 11 ? .84f : au >= 1f ? .92f : .97f) * (1f + .04f * crumbs);
                        else if (cut) tone = .7f;
                        else if (c == 5 || c == 9) tone = .6f + wet;
                        else tone = (c == 7 ? .74f : .8f) + wet + .06f * crumbs + .08f * heave;
                        byte g = (byte)(255f * Mathf.Clamp01(tone));
                        _colors[at] = new Color32(g, g, g, 255);
                        ThicketMasterEarthRules.AtlasUv(soil, p.x, p.z, out float uu, out float vv);
                        _uvs[at] = new Vector2(uu, vv);
                    }
                }
                Matrix4x4 toLocal = _space.worldToLocalMatrix;
                for (int i = 0; i < MaxSamples; i++)
                    for (int c = 0; c < Across; c++)
                    {
                        int at = i * Across + c;
                        if (i >= n || n < 2)
                        {
                            // Пусто: вершины стянуты в точку под землёй — треугольники вырождены.
                            Vector3 rest = n > 0 ? _points[n - 1] + Vector3.down * .2f : Vector3.down * 100f;
                            _vertices[at] = toLocal.MultiplyPoint3x4(rest);
                            _normals[at] = Vector3.up;
                            continue;
                        }
                        Vector3 across = _world[i * Across + Mathf.Min(c + 1, Across - 1)] - _world[i * Across + Mathf.Max(c - 1, 0)];
                        Vector3 along = _world[Mathf.Min(i + 1, n - 1) * Across + c] - _world[Mathf.Max(i - 1, 0) * Across + c];
                        Vector3 normal = Vector3.Cross(along, across);
                        if (normal.y < 0f) normal = -normal;
                        normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.up;
                        _vertices[at] = toLocal.MultiplyPoint3x4(_world[at]);
                        _normals[at] = toLocal.MultiplyVector(normal).normalized;
                    }
                _mesh.vertices = _vertices;
                _mesh.normals = _normals;
                _mesh.colors32 = _colors;
                _mesh.uv = _uvs;
                _mesh.RecalculateBounds();
            }

            private static float Smooth(float x)
            {
                x = Mathf.Clamp01(x);
                return x * x * (3f - 2f * x);
            }

            /// <summary>Гладкий шум −1…1 по одной оси (решётка с хешем, сглаженная интерполяция).</summary>
            private static float Noise(float x, int salt)
            {
                int i = Mathf.FloorToInt(x);
                float f = x - i;
                f = f * f * (3f - 2f * f);
                return Mathf.Lerp(Lattice(i, salt), Lattice(i + 1, salt), f);
            }

            private static float Lattice(int i, int salt)
            {
                uint h = unchecked((uint)i * 2654435761u ^ (uint)salt * 2246822519u);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFF) / 32767.5f - 1f;
            }
        }
    }
}
