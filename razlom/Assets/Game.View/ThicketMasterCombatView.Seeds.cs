using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — «ТЕРНОВНИК», ВИД ШИПОВ (контракт boss-tempo-contract.md § 17.2, владелец 08.10; заменил веер § 16).
    /// Время и раскладка — чистые правила ThicketMasterSeedRules, кусты — ThicketMasterCombatView.SeedsBush.cs, клип тела —
    /// ThicketMasterClipRules (жест каста прорастания).
    ///
    /// • Жест (Started(ThicketSeeds, 0)): лапы в землю — пыль и комья у пальцев (VFX_Thicket_SproutPress, как у прорастания).
    /// • Рост куста (SproutTick … LaunchTick): на каждый шип Sim (TryGetThicketSeed, Released = false) в устье своей линии
    ///   (край листвы куста под большим шипом линии) набухает колючий стручок — тот же стручок, что летал у веера
    ///   (VFX_Thicket_SeedPod: ядро коры ThornWood, шипы с тлеющими кончиками), меньше; к выпуску — блик (кончики
    ///   разгораются, стручок вздрагивает).
    /// • Выпуск (EnemyProjectileLaunched): стручок срывается из устья и за 0,6 м пути сходит на линию, дальше — ровно остриё
    ///   Sim (ThicketMasterSeedRules.TipDistance по часам босса), катится и подскакивает над линией, кувыркается.
    /// • След — частицы-функции номера шипа, пройденного пути и тика (SetParticles): низкая пыль по земле (и клуб на каждом
    ///   касании), сорванные листья, щепки коры, слабые угольки и короткая лента.
    /// • Встал (EnemyActionImpact): попал — стручок лопается о героя (VFX_Thicket_SeedHit); конец линии — клюёт и уходит в
    ///   землю, клуб земли и листьев (VFX_Thicket_SeedDrop). Снят (смерть босса или героя: Cancelled) — стручок сжимается
    ///   без всплеска; ещё не выпущенный и пропавший из Sim (куст снят) — тоже.
    /// Пулы — при первой арене (стручков ThicketMasterSeedRules.PodPool = 2 каста по 12); в кадре ни Instantiate, ни аллокаций.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        public const string SeedPodName = "VFX_Thicket_SeedPod";
        public const string SeedHitName = "VFX_Thicket_SeedHit";
        public const string SeedDropName = "VFX_Thicket_SeedDrop";

        /// <summary>Дети префаба стручка: «Pod» — MeshRenderer (кора, кончики), «Ribbon» — LineRenderer ленты следа.</summary>
        public const string SeedPodChild = "Pod", SeedRibbonChild = "Ribbon";

        /// <summary>Системы следа стручка: без своей эмиссии, частицы ставит вид (SetParticles).</summary>
        public const string SeedDustSystem = "Seed Dust", SeedLeafSystem = "Seed Leaves", SeedSplinterSystem = "Seed Splinters",
            SeedMoteSystem = "Seed Motes";

        /// <summary>Точек ленты следа (LineRenderer.positionCount в префабе).</summary>
        public const int SeedRibbonPoints = 10;

        /// <summary>
        /// Свечение кончиков шипов (M_Thicket_SeedTip, _EmissionColor, гамма): тлеющий янтарь — стручок видно на тёмной
        /// поляне, за порог bloom выходит только в блике перед выпуском.
        /// </summary>
        public static readonly Color SeedTipGlow = new Color(.62f, .24f, .07f);

        /// <summary>Блик: свечение кончиков ×(1 + Boost·блик), размер стручка ×(1 + Swell·блик).</summary>
        private const float SeedGlintBoost = 2.6f, SeedGlintSwell = .14f;

        // След (тики Sim, метры пути).
        private const float SeedDustSpacing = .5f, SeedDustLife = 11f, SeedKickLife = 14f;
        private const float SeedLeafSpacing = .8f, SeedLeafLife = 30f, SeedSplinterSpacing = 1.1f, SeedSplinterLife = 13f;
        private const float SeedMoteSpacing = .35f, SeedMoteLife = 8f;
        private const float SeedRibbonLength = 1.2f, SeedGravity = 12f, SeedLeafGravity = 4f;
        /// <summary>После остановки вид живёт, пока доживает след (листья — до 36 тиков).</summary>
        private const float SeedLingerTicks = 40f;
        private const int SeedKickPuffs = 3;

        private static readonly Color SeedDustLight = new Color(.62f, .47f, .31f), SeedDustDark = new Color(.42f, .31f, .20f);
        private static readonly Color SeedLeafLight = new Color(.62f, .70f, .30f), SeedLeafDark = new Color(.42f, .52f, .20f);
        private static readonly Color SeedBarkLight = new Color(.66f, .52f, .36f), SeedBarkDark = new Color(.45f, .33f, .22f);
        private static readonly Color SeedEmber = new Color(1f, .62f, .22f), SeedEmberDeep = new Color(.95f, .40f, .10f);

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Стручок в пуле: экземпляр префаба, части, буферы частиц и состояние шипа.</summary>
        private sealed class SeedView
        {
            public Vfx Vfx;
            public Transform Pod;
            public MeshRenderer PodRenderer;
            public LineRenderer Ribbon;
            public ParticleSystem Dust, Leaves, Splinters, Motes;
            public ParticleSystem.Particle[] DustBuffer = new ParticleSystem.Particle[0], LeafBuffer = new ParticleSystem.Particle[0],
                SplinterBuffer = new ParticleSystem.Particle[0], MoteBuffer = new ParticleSystem.Particle[0];
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public float Glow = -1f;
            /// <summary>Подсетка кончиков шипов (свой материал со свечением); −1 — у рендера один материал.</summary>
            public int TipSlot = -1;

            public int Boss = -1, Serial, Volley, Index;
            public bool Shown, Seen, Released, Flying, Hit, Gone, HasMouth;
            public ThicketSeedState Seed;
            /// <summary>Остановка: метр пути и тик прихода туда; снят — тик снятия; показанное остриё.</summary>
            public float StopDistance, StopTick = float.MaxValue, GoneTick = float.MaxValue, ShownTip;
            /// <summary>Размер в устье в последнем кадре роста (снят до выпуска — сжимается от него).</summary>
            public float MouthScale;
            /// <summary>Устье в кусте (последний кадр роста) и сдвиг выпуска: устье минус начало пути на высоте полёта.</summary>
            public Vector3 Mouth, LaunchOffset;
        }

        private VfxPool _seedPods, _seedHit, _seedDrop;
        private SeedView[] _seeds = new SeedView[0];
        private bool _seedOverflowReported;

        // ------------------------------------------------------------ pools

        /// <summary>
        /// Пулы терновника (зовёт EnsurePools): стручки и кусты ведёт вид сам (UpdateSeeds, не AdvanceVfx), всплески —
        /// общие пулы (_bushSprout, _bushLaunch, _seedHit, _seedDrop — в списке _vfxPools).
        /// </summary>
        private void MakeSeedPools(ref int missing)
        {
            _seedPods = MakeVfxPool(SeedPodName, ThicketMasterSeedRules.PodPool, ref missing);
            // Одно попадание на каст; конец линии — у каждого шипа каста (касты — не чаще 6,5 с от начала, всплеск — 1,6 с).
            _seedHit = MakeVfxPool(SeedHitName, 2, ref missing);
            _seedDrop = MakeVfxPool(SeedDropName, Simulation.ThicketSeedSlots, ref missing);
            _seeds = new SeedView[_seedPods.Items.Length];
            for (int i = 0; i < _seeds.Length; i++) _seeds[i] = PrepareSeed(_seedPods.Items[i]);
            MakeBushPools(ref missing);
        }

        private static SeedView PrepareSeed(Vfx vfx)
        {
            var view = new SeedView { Vfx = vfx };
            Transform root = vfx.Fx.Root.transform;
            // Частицы — в мире, стручок и лента ставятся мировыми координатами: корень — в ноль мира.
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            view.Pod = root.Find(SeedPodChild);
            if (view.Pod != null) view.PodRenderer = view.Pod.GetComponent<MeshRenderer>();
            if (view.PodRenderer != null && view.PodRenderer.sharedMaterials.Length > 1) view.TipSlot = 1;
            var ribbon = root.Find(SeedRibbonChild);
            if (ribbon != null) view.Ribbon = ribbon.GetComponent<LineRenderer>();
            foreach (var ps in vfx.Fx.Particles)
            {
                // Своей эмиссии и своего времени нет: частицы ставит вид каждый кадр, петля держит систему живой.
                var main = ps.main; main.loop = true;
                var emission = ps.emission; emission.enabled = false;
                var buffer = new ParticleSystem.Particle[Mathf.Max(1, main.maxParticles)];
                switch (ps.name)
                {
                    case SeedDustSystem: view.Dust = ps; view.DustBuffer = buffer; break;
                    case SeedLeafSystem: view.Leaves = ps; view.LeafBuffer = buffer; break;
                    case SeedSplinterSystem: view.Splinters = ps; view.SplinterBuffer = buffer; break;
                    case SeedMoteSystem: view.Motes = ps; view.MoteBuffer = buffer; break;
                }
            }
            return view;
        }

        // ------------------------------------------------------------ hooks

        /// <summary>
        /// Жест терновника (Started(ThicketSeeds, 0)): лапы в землю, как у прорастания — пыль и комья у пальцев. Кусты и
        /// стручки встают из Sim сами (UpdateSeeds).
        /// </summary>
        partial void OnSeedsWindup(int boss, int tick, int launchTick)
        {
            var sim = _driver.Sim;
            if (sim == null) return;
            var v = TakeVfx(_sproutPress, boss, tick + SproutPressDelayTicks, GroundAt(BodyPosition()),
                Quaternion.LookRotation(FacingOf(sim, boss), Vector3.up), tick, 1f);
            if (v == null) return;
            v.Follow = VfxFollow.Toes; v.Pin = true;
            PlaceVfx(sim, v, -1f);
        }

        /// <summary>Шип сорвался с куста: стручок из устья переходит в полёт (отдачу куста и всплеск листьев ведёт куст).</summary>
        partial void OnSeedLaunched(int boss, int tick, int serial, Vector3 at)
        {
            var sim = _driver.Sim;
            if (sim == null) return;
            var view = FindSeed(boss, serial);
            bool known = TryGetSeed(sim, boss, serial, out ThicketSeedState s);
            if (view == null) view = known ? TakeSeed(boss, in s) : TakeBlankSeed(boss, serial, at, tick, true);
            if (view != null && !view.Released)
            {
                if (known) view.Seed = s;
                ReleaseSeed(view);
            }
        }

        /// <summary>
        /// Шип встал: остриё доходит до точки Sim по своему расписанию; попал — лопается о героя (всплеск у тела),
        /// не попал — клюёт и уходит в землю (клуб земли, когда стручок коснулся её).
        /// </summary>
        partial void OnSeedStopped(int boss, int tick, int serial, Vector3 at, bool hit)
        {
            var sim = _driver.Sim;
            if (sim == null) return;
            var view = FindSeed(boss, serial) ?? TakeBlankSeed(boss, serial, at, tick, false);
            if (view == null) return;
            if (!view.Released) ReleaseSeed(view);
            var s = view.Seed;
            Vector2 origin = new Vector2(s.Origin.X.ToFloat(), s.Origin.Y.ToFloat());
            Vector2 dir = SeedDirection(in s);
            Vector2 point = new Vector2(at.x, at.z);
            float stop = Mathf.Max(0f, Vector2.Dot(point - origin, dir));
            view.Flying = false; view.Hit = hit; view.Gone = false;
            view.StopDistance = stop;
            view.StopTick = ThicketMasterSeedRules.StopTick(s.ReleaseTick, stop, tick);
            var look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
            if (hit) TakeVfx(_seedHit, boss, tick, GroundAt(at), look, serial * 4 + 1, 1.8f);
            else TakeVfx(_seedDrop, boss, tick + Mathf.CeilToInt(ThicketMasterSeedRules.DropTicks * .45f), GroundAt(at), look,
                serial * 4 + 2, 1.6f);
        }

        /// <summary>
        /// Снят (смерть босса или героя): Amount — номер летящего шипа (Cancelled на сам жест Sim не шлёт). Невыпущенные
        /// стручки пропадают из Sim вместе с кустом — их гасит кадр (UpdateSeeds).
        /// </summary>
        private void SeedsCancelled(int boss, int serial, int tick)
        {
            var view = FindSeed(boss, serial);
            if (view != null && !view.Gone && (view.Flying || !view.Released)) GoneSeed(view, tick);
        }

        // ------------------------------------------------------------ frame

        /// <summary>Кадр: кусты и стручки из Sim (рост и полёт), пропавшие без события гаснут, всё рисуется по часам босса.</summary>
        private void UpdateSeeds(Simulation sim, float tick, float clock)
        {
            UpdateBushes(sim, tick, clock);
            if (_seeds.Length == 0) return;
            bool frozen = _boss >= 0 && sim.ThicketMasterFrozenTicksLeft(_boss) > 0;
            for (int i = 0; i < _seeds.Length; i++) _seeds[i].Seen = false;
            if (_boss >= 0)
                for (int k = 0; k < Simulation.ThicketSeedSlots; k++)
                {
                    if (!sim.TryGetThicketSeed(_boss, k, out ThicketSeedState s)) continue;
                    var view = FindSeed(_boss, s.Serial) ?? TakeSeed(_boss, in s);
                    if (view == null || view.Gone || (view.Released && !view.Flying)) continue;
                    view.Seen = true;
                    view.Seed = s;
                    if (s.Released && !view.Released) ReleaseSeed(view);
                }
            for (int i = 0; i < _seeds.Length; i++)
            {
                var v = _seeds[i];
                if (!v.Shown) continue;
                // Пропал из Sim без события (куст снят смертью, сброс стенда, другой босс): гаснет без всплеска.
                if (!v.Seen && !v.Gone && (v.Flying || !v.Released)) GoneSeed(v, clock);
                DrawSeed(v, clock, frozen);
            }
        }

        private SeedView FindSeed(int boss, int serial)
        {
            for (int i = 0; i < _seeds.Length; i++)
                if (_seeds[i].Shown && _seeds[i].Boss == boss && _seeds[i].Serial == serial) return _seeds[i];
            return null;
        }

        private static bool TryGetSeed(Simulation sim, int boss, int serial, out ThicketSeedState seed)
        {
            for (int k = 0; k < Simulation.ThicketSeedSlots; k++)
                if (sim.TryGetThicketSeed(boss, k, out seed) && seed.Serial == serial) return true;
            seed = default;
            return false;
        }

        /// <summary>Свободный стручок; нет — уступает самый давно вставший (летящий и растущий важнее доживающего следа).</summary>
        private SeedView FreeSeed()
        {
            SeedView free = null;
            for (int i = 0; i < _seeds.Length; i++) if (!_seeds[i].Shown) return _seeds[i];
            for (int i = 0; i < _seeds.Length; i++)
            {
                var v = _seeds[i];
                if (!v.Released || v.Flying) continue;
                float end = v.Gone ? v.GoneTick : v.StopTick;
                float best = free == null ? float.MaxValue : free.Gone ? free.GoneTick : free.StopTick;
                if (end < best) free = v;
            }
            if (free != null) HideSeed(free);
            else if (!_seedOverflowReported)
            {
                _seedOverflowReported = true;
                Debug.LogError("[thicketmaster-vfx] Терновник: исчерпан пул стручков — шип летит невидимым.");
            }
            return free;
        }

        private SeedView TakeSeed(int boss, in ThicketSeedState s)
        {
            var view = FreeSeed();
            if (view == null) return null;
            view.Shown = true; view.Boss = boss; view.Serial = s.Serial; view.Volley = s.Volley; view.Index = s.Index;
            view.Seed = s;
            view.Released = view.Flying = view.Hit = view.Gone = view.HasMouth = false;
            view.StopDistance = view.ShownTip = view.MouthScale = 0f;
            view.StopTick = view.GoneTick = float.MaxValue;
            view.Mouth = view.LaunchOffset = Vector3.zero;
            view.Glow = -1f;
            view.Vfx.Fx.Root.SetActive(true);
            if (view.PodRenderer != null) view.PodRenderer.enabled = false;
            if (view.Ribbon != null) view.Ribbon.enabled = false;
            Play(view.Dust); Play(view.Leaves); Play(view.Splinters); Play(view.Motes);
            if (s.Released) ReleaseSeed(view);
            return view;
        }

        /// <summary>
        /// Шип, которого нет в Sim (выпуск и остановка в одном тике): линия — от ближайшего куста через at. at — начало пути
        /// (выпуск) или точка остановки; без куста — от at по взгляду босса.
        /// </summary>
        private SeedView TakeBlankSeed(int boss, int serial, Vector3 at, int tick, bool atOrigin)
        {
            var sim = _driver.Sim;
            var dir = new Vector2(0f, 1f);
            var origin = new Vector2(at.x, at.z);
            if (TryNearestBush(boss, at, out Vector3 centre))
            {
                var away = new Vector2(at.x - centre.x, at.z - centre.z);
                if (away.sqrMagnitude > 1e-6f) dir = away.normalized;
                if (!atOrigin)
                    origin = new Vector2(centre.x, centre.z) + dir * Simulation.ThicketBushThornStart.ToFloat();
            }
            else if (sim != null && (uint)boss < (uint)sim.Entities.Count)
            {
                var f = sim.Entities.Facing[boss];
                var facing = new Vector2(f.X.ToFloat(), f.Y.ToFloat());
                if (facing.sqrMagnitude > 1e-6f) dir = facing.normalized;
            }
            var blank = new ThicketSeedState
            {
                Serial = serial, Released = true, ReleaseTick = tick, Index = -1,
                Origin = new FixVec2(Fix64.FromDouble(origin.x), Fix64.FromDouble(origin.y)),
                Direction = new FixVec2(Fix64.FromDouble(dir.x), Fix64.FromDouble(dir.y)),
                Length = Simulation.ThicketBushLaneLength,
            };
            return TakeSeed(boss, in blank);
        }

        private static void Play(ParticleSystem ps)
        {
            if (ps != null && !ps.isPlaying) ps.Play(false);
        }

        /// <summary>Выпуск: стручок отрывается из устья — сдвиг от начала пути запоминается и сходит на нет по пути.</summary>
        private void ReleaseSeed(SeedView v)
        {
            v.Released = true;
            v.Flying = true;
            if (!v.HasMouth) { v.LaunchOffset = Vector3.zero; return; }
            var s = v.Seed;
            float x = s.Origin.X.ToFloat(), z = s.Origin.Y.ToFloat();
            v.LaunchOffset = v.Mouth - new Vector3(x, GroundY(x, z) + ThicketMasterSeedRules.FlightLift, z);
        }

        private static void GoneSeed(SeedView v, float tick)
        {
            v.Gone = true;
            v.GoneTick = tick;
            v.StopDistance = v.ShownTip;
            v.Flying = false;
        }

        private static void HideSeed(SeedView v)
        {
            v.Shown = false; v.Seen = false; v.Boss = -1; v.Serial = 0;
            v.Released = v.Flying = v.Hit = v.Gone = v.HasMouth = false;
            if (v.Ribbon != null) v.Ribbon.enabled = false;
            if (v.PodRenderer != null) v.PodRenderer.enabled = false;
            if (v.Dust != null) v.Dust.SetParticles(v.DustBuffer, 0);
            if (v.Leaves != null) v.Leaves.SetParticles(v.LeafBuffer, 0);
            if (v.Splinters != null) v.Splinters.SetParticles(v.SplinterBuffer, 0);
            if (v.Motes != null) v.Motes.SetParticles(v.MoteBuffer, 0);
            if (v.Vfx.Fx.Root.activeSelf) v.Vfx.Fx.Root.SetActive(false);
        }

        /// <summary>Все стручки и кусты — вон (сброс арены, смена поколения или глубины).</summary>
        private void HideSeeds()
        {
            for (int i = 0; i < _seeds.Length; i++) HideSeed(_seeds[i]);
            HideBushes();
        }

        private static Vector2 SeedDirection(in ThicketSeedState s)
        {
            var dir = new Vector2(s.Direction.X.ToFloat(), s.Direction.Y.ToFloat());
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
        }
    }
}
