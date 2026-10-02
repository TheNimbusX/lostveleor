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
    /// • нырок — стена земли по контуру тела все 12 тиков ухода, гребень земли за бугром («Дюна»),
    ///   дрожь круга, выход за стеной земли с фонтаном комьев и корнями;
    /// • прорастание — лапы в землю, дрожь под каждым кругом, шипы-корни на ударе;
    /// • пыльца — золото с кроны, столб над облаком, ядовитое облако с кромкой и пульсом укуса лежит, пока лежит зона Sim;
    /// • ливень — пуф куста на залп, ягоды летят дугой к своим кругам, шлепки сока;
    /// • буря — лепестки с кроны, вихрь по арене, столбы света над кругами, порыв волны;
    /// • смерть — «цветущий холм»: вспышка в кусте, лепестки, цветы там, где легло тело.
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
        public const string RoarInhaleName = "VFX_Thicket_RoarInhale";
        public const string RoarBlastName = "VFX_Thicket_RoarBlast";
        public const string WakeTearName = "VFX_Thicket_WakeTear";
        public const string DeathBloomName = "VFX_Thicket_DeathBloom";

        /// <summary>Все префабы набора — сборщик проверяет по этому списку, что собрал всё.</summary>
        public static readonly string[] AttackPrefabNames =
        {
            PawSlashName, PawImpactName, StompRearName, StompQuakeName, StompOuterName, DiveBurstName, MoundName,
            DiveTremorName, EmergeName, SproutPressName, SproutTremorName, SproutSpikesName, PollenShakeName,
            PollenFallName, PollenCloudName, BushPuffName, BerryName, BerrySplatName, CrownShedName, StormVortexName,
            LightPillarName, StormWaveName, RoarInhaleName, RoarBlastName, WakeTearName, DeathBloomName,
        };

        /// <summary>
        /// Дети корня префаба, которые вид ставит сам: «Left»/«Right» — эмиттеры у костей
        /// (кроны, передние лапы), «Bush» — у куста, «Hump» — горб бугра (дрожь).
        /// Система с именем «Late …» стартует, когда тело легло (смерть); «~…» — эмиссия
        /// идёт столько, сколько скажет вид (буря, столб света).
        /// </summary>
        public const string LeftChild = "Left", RightChild = "Right", BushChild = "Bush", HumpChild = "Hump";
        public const string LatePrefix = "Late ", TimedPrefix = "~";
        /// <summary>
        /// Системы следа бугра («Trail Ridge», «Trail Trench», …): вид пускает их эмиссию, только пока
        /// бугор едет — стоит (круг лёг, Песочные Часы) — гребень не копится кучей в одной точке.
        /// </summary>
        public const string TrailPrefix = "Trail ";
        /// <summary>
        /// Бугор после выхода босса (ревью 02.10, п. 8: «органично»): эмиссия гаснет, горб уходит в
        /// землю на MoundSinkDepth м за MoundSinkSeconds с, гребень и комья доживают свою жизнь —
        /// не пропадают одним кадром под ещё редкой стеной выхода.
        /// </summary>
        public const float MoundSinkSeconds = .3f, MoundSinkDepth = 1f;
        /// <summary>Ребёнок префаба PawSlash с MeshFilter + MeshRenderer: сетку лент когтей пишет вид.</summary>
        public const string ClawsChild = "Claws";

        /// <summary>
        /// Ленты когтей (ревью 02.10: «след идёт не за лапой, а после неё»): кость пальцев бьющей
        /// лапы пишет ленту с ClawLeadTicks до удара по ClawTailTicks после (часы босса); хвост —
        /// последние ClawTrailSeconds пути. Ширина одной ленты и шаг между тремя когтями, м.
        /// </summary>
        public const int ClawLeadTicks = 6, ClawTailTicks = 2;
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

        private enum VfxFollow : byte { None, Mound, Bush, Crowns, Toes, ToeLeft, ToeRight, Body, Flight, Claws }

        /// <summary>Экземпляр префаба в пуле: разбор RootSnarerCombatView.Fx и состояние запуска.</summary>
        private sealed class Vfx
        {
            public RootSnarerCombatView.Fx Fx;
            public Transform Left, Right, Bush, Hump;
            /// <summary>Ленты когтей (только PawSlash); Index — 1 правая лапа, 0 левая.</summary>
            public ClawTrail Claws;
            public Vector3 BaseScale = Vector3.one;
            public int[] Late = new int[0], Timed = new int[0], Trail = new int[0];
            /// <summary>Бугор: возраст, на котором босс вылез (дальше бугор доживает на месте); MaxValue — ещё едет.</summary>
            public float Surfaced = float.MaxValue;

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
            _emerge, _sproutPress, _sproutTremor, _sproutSpikes, _pollenShake, _pollenFall, _pollenCloud, _bushPuff,
            _berry, _berrySplat, _crownShed, _stormVortex, _lightPillar, _stormWave, _roarInhale, _roarBlast,
            _wakeTear, _deathBloom;
        private VfxPool[] _vfxPools;
        private LayoutView _vfxLayout;
        private int _vfxGeneration = -1;
        /// <summary>Сколько бугор доживает после выхода: самая долгая жизнь его частиц вне горба.</summary>
        private float _moundLinger;

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
            _diveBurst = MakeVfxPool(DiveBurstName, 2, ref missing);
            _mound = MakeVfxPool(MoundName, 1, ref missing);
            _moundLinger = MoundLinger(_mound);
            _diveTremor = MakeVfxPool(DiveTremorName, 1, ref missing);
            _emerge = MakeVfxPool(EmergeName, 2, ref missing);
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
            _vfxPools = new[]
            {
                _pawSlash, _pawImpact, _stompRear, _stompQuake, _stompOuter, _diveBurst, _mound, _diveTremor, _emerge,
                _sproutPress, _sproutTremor, _sproutSpikes, _pollenShake, _pollenFall, _pollenCloud, _bushPuff, _berry,
                _berrySplat, _crownShed, _stormVortex, _lightPillar, _stormWave, _roarInhale, _roarBlast, _wakeTear,
                _deathBloom,
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
                v.Bush = root.Find(BushChild); v.Hump = root.Find(HumpChild);
                var claws = root.Find(ClawsChild);
                if (claws != null && claws.TryGetComponent(out MeshFilter clawFilter)) v.Claws = new ClawTrail(clawFilter);
                v.Late = SystemsNamed(v.Fx, LatePrefix);
                v.Timed = SystemsNamed(v.Fx, TimedPrefix);
                v.Trail = SystemsNamed(v.Fx, TrailPrefix);
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

        /// <summary>Самая долгая жизнь частицы бугра вне горба «Hump» (горб уходит в землю сам): столько он доживает после выхода.</summary>
        private static float MoundLinger(VfxPool pool)
        {
            if (pool.Items.Length == 0) return MoundSinkSeconds;
            var v = pool.Items[0];
            float longest = MoundSinkSeconds;
            for (int k = 0; k < v.Fx.Particles.Length; k++)
            {
                var ps = v.Fx.Particles[k];
                if (v.Hump != null && ps.transform.IsChildOf(v.Hump)) continue;
                longest = Mathf.Max(longest, CurveMax(ps.main.startLifetime));
            }
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
            v.From = v.To = v.Center = v.Last = position;
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
        /// Босс вылез (или ушёл из-под земли иначе): бугор встаёт на месте, эмиссия гаснет, горб
        /// уходит в землю (PlaceVfx), гребень, борозда и комья доживают свою жизнь — _moundLinger.
        /// </summary>
        private void SurfaceMound(Vfx v, float age)
        {
            if (v.Surfaced != float.MaxValue) return;
            v.Surfaced = Mathf.Max(0f, age);
            SetEmission(v, false);
            v.Fx.Life = Mathf.Min(v.Fx.Life, v.Surfaced + _moundLinger);
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
        }

        private void OnDisable() => RetireAllVfx();

        /// <summary>Сетки лент когтей создаёт вид (ClawTrail) — и удаляет вместе с собой.</summary>
        private void OnDestroy()
        {
            if (_pawSlash == null) return;
            for (int i = 0; i < _pawSlash.Items.Length; i++)
                if (_pawSlash.Items[i].Claws != null) _pawSlash.Items[i].Claws.Dispose();
        }

        // ------------------------------------------------------------ frame

        partial void OnArenaReset()
        {
            EnsurePools();
            RetireAllVfx();
            _vfxGeneration = _driver != null ? _driver.Generation : -1;
        }

        partial void OnBossBound(int boss) => EnsurePools();

        partial void OnFrame(Simulation sim, float tick)
        {
            if (_vfxPools == null) return;
            // Общий сброс той же симуляции (новый Разлом, стенд): тики начались заново.
            if (_driver.Generation != _vfxGeneration) { RetireAllVfx(); _vfxGeneration = _driver.Generation; return; }
            float clock = _boss >= 0 ? ThicketMasterClipRules.BossClock(sim, _boss, tick) : tick;
            for (int p = 0; p < _vfxPools.Length; p++)
            {
                var items = _vfxPools[p].Items;
                for (int i = 0; i < items.Length; i++)
                    if (items[i].Fx.Root.activeSelf) AdvanceVfx(sim, items[i], tick, clock);
            }
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
            RootSnarerCombatView.AnimateGrows(fx, age);
            RootSnarerCombatView.StepParticles(fx, age);
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
                        // Вылез: бугор стоит, горб уходит в землю (за стеной выхода), остальное доживает само.
                        if (v.Hump != null)
                        {
                            float s = Mathf.Clamp01((age - v.Surfaced) / MoundSinkSeconds);
                            s = s * s * (3f - 2f * s);
                            v.Hump.localPosition = new Vector3(0f, -MoundSinkDepth * s, 0f);
                            v.Hump.localScale = Vector3.one * (1f - .35f * s);
                        }
                        break;
                    }
                    Vector3 p = GroundAt(MoundPosition);
                    Vector3 step = p - v.Last; step.y = 0f;
                    // След — только на ходу: стоит (круг лёг, Часы) — гребень и борозда не копятся в одной точке.
                    SetEmission(v, v.Trail, step.sqrMagnitude > 1e-8f);
                    if (step.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
                    root.position = p;
                    v.Last = p;
                    if (v.Hump != null)
                    {
                        // Земля над ползущим телом дрожит: ±3 см и масштаб 1 ± 0,04 — от возраста, держит паузу.
                        float t = Mathf.Max(0f, age);
                        v.Hump.localPosition = new Vector3(0f, .03f * Mathf.Sin(t * 37f), 0f);
                        v.Hump.localScale = Vector3.one * (1f + .04f * Mathf.Sin(t * 23f + 1.3f));
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
            float window = (ClawLeadTicks + ClawTailTicks) / (float)Simulation.TicksPerSecond;
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
        /// Удар серии: ленты когтей бьющей лапы (правая — чётный удар) пишутся с ClawLeadTicks до
        /// контакта по ClawTailTicks после; срок перечитывается из Sim (Часы сдвигают его с телом).
        /// </summary>
        partial void OnPawWindup(int boss, int tick, int stage, bool right, int impactTick)
        {
            var sim = _driver.Sim;
            if (!sim.TryGetThicketMasterAction(boss, out ThicketMasterState a) || a.Action != ThicketMasterAction.Paw) return;
            float window = (ClawLeadTicks + ClawTailTicks) / (float)Simulation.TicksPerSecond;
            var v = TakeVfx(_pawSlash, boss, impactTick - ClawLeadTicks, Ground(a.Origin), Quaternion.identity,
                a.Serial * 16 + stage, window + ClawTrailSeconds + .05f);
            if (v == null) return;
            v.Serial = a.Serial; v.Stage = stage; v.Lead = -ClawLeadTicks;
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
                Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 2.6f);
        }

        partial void OnMoundTravel(int boss, int tick)
        {
            var sim = _driver.Sim;
            RetirePool(_mound, false, 0f);
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
            if (v == null) return;
            v.Serial = a.Serial; v.Stage = a.Stage; v.Lead = tick - impactTick;
            v.Anchor = VfxAnchor.Impact; v.Clock = true;
        }

        partial void OnEmerge(int boss, int tick, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            // Бугор не пропадает одним кадром (ревью 02.10, п. 8): гаснет эмиссия, горб уходит в
            // землю, гребень и комья доживают за стеной выхода.
            if (_mound != null)
                for (int i = 0; i < _mound.Items.Length; i++)
                {
                    var m = _mound.Items[i];
                    if (m.Fx.Root.activeSelf && m.Follow == VfxFollow.Mound) SurfaceMound(m, (tick - m.Fx.Tick) / (float)Simulation.TicksPerSecond);
                }
            RetirePool(_diveTremor, false, 0f);
            var v = TakeVfx(_emerge, boss, tick, GroundAt(at), Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 3f);
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

        /// <summary>Удар залпа: шлепок сока в каждом из четырёх кругов (попал или нет).</summary>
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
            // Коробки порыва 20 × 15 м — по осям поляны (GladeLayout: поворот 0, 20 м по X, 15 по Z), как вихрь.
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
                    break;
            }
        }

        /// <summary>
        /// Смерть — «цветущий холм»: все замахи и петли гаснут; вспышка в кусте и лепестки с
        /// куста и крон сразу, цветы и листья — когда тело легло (такт убийства LandsAt),
        /// холм уходит последним, через 2 с после тела.
        /// </summary>
        partial void OnBossKilled(int boss, int tick, Vector3 at)
        {
            var sim = _driver.Sim;
            RetirePool(_pawSlash, true, tick);
            RetirePool(_stompRear, false, 0f); RetirePool(_roarInhale, false, 0f);
            RetirePool(_diveTremor, false, 0f); RetirePool(_mound, false, 0f);
            RetirePool(_pollenShake, false, 0f); RetirePool(_pollenFall, false, 0f);
            RetirePool(_berry, false, 0f); RetirePool(_bushPuff, false, 0f); RetirePool(_sproutTremor, false, 0f);
            RetirePool(_crownShed, false, 0f); RetirePool(_stormVortex, false, 0f); RetirePool(_lightPillar, false, 0f);

            var beat = EnemyPresentationProfile.Kill(EnemyKind.ForestThicketMaster, false, false);
            float settle = beat.LandsAt > 0f ? beat.LandsAt
                : beat.HitStopSeconds + EnemyPresentationProfile.Death(EnemyKind.ForestThicketMaster).FallSeconds;
            Vector3 facing = _bossBody != null ? _bossBody.forward : FacingOf(sim, boss);
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            var v = TakeVfx(_deathBloom, boss, tick, GroundAt(at), Quaternion.LookRotation(facing.normalized, Vector3.up), tick,
                Mathf.Max(settle + 2f, beat.BodyGoneAt) + 2.5f);
            if (v == null) return;
            for (int i = 0; i < v.Late.Length; i++) v.Fx.Delays[v.Late[i]] = settle;
            if (v.Bush != null) v.Bush.position = BonePoint(_bossView != null ? _bossView.Bush : null, FallbackBush);
            v.Follow = VfxFollow.Crowns; v.Pin = true;
            PlaceVfx(sim, v, -1f);
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

        /// <summary>Центр арены босса (бури): точка поводка — середина поляны 20 × 15; нет памяти — тело.</summary>
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
    }
}
