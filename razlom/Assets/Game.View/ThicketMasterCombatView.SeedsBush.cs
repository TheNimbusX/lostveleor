using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Терновник» — КУСТЫ (контракт boss-tempo-contract.md § 17.2, владелец 08.10). Куст ведёт вид по состоянию Sim
    /// (TryGetThicketBush, места 0–2) и часам босса; сроки — ThicketMasterSeedRules, рисунок — .SeedsBushDraw.cs.
    ///
    /// Префаб VFX_Thicket_Bush (ThicketMasterVfxSetup.SeedPrefabs.cs): «Core» — падуб поляны (тот же меш и фактура, что
    /// кусты арены), «Lanes/Lane 0…3» — четыре больших шипа ThornWood по линиям креста (+Z корня — линия 0, линия k —
    /// поворот −90°·k), «Canes/Cane …» — колючие стебли Шипомёта (ThornA–C, побеги) между ними; системы без эмиссии —
    /// частицы ставит вид: пятно разрытой земли и трещины у основания, листья (стряхнутые в дрожи, сухие в увядании),
    /// угольки и блик на кончиках шипов линий, пыль (земля шевелится до прорастания, оседает при уходе).
    /// Всплески: VFX_Thicket_BushSprout — земля рвётся на прорастании (плиты дёрна, комья, зерно, трава, пыль);
    /// VFX_Thicket_BushLaunch — выпуск: листья, щепки и искры по четырём линиям.
    ///
    /// Куст пропал из Sim раньше GoneTick (смерть босса или героя, рестарт) — вянет сам за LostWitherTicks от того кадра.
    /// Пулы — при первой арене (ThicketMasterSeedRules.BushPool = 2 каста по 3), в кадре ни Instantiate, ни аллокаций.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        public const string BushName = "VFX_Thicket_Bush";
        public const string BushSproutName = "VFX_Thicket_BushSprout";
        public const string BushLaunchName = "VFX_Thicket_BushLaunch";

        /// <summary>Дети корня куста: листва, узел шипов линий («Lane k»), узел стеблей («Cane k»).</summary>
        public const string BushCoreChild = "Core", BushLanesChild = "Lanes", BushCanesChild = "Canes", BushLanePrefix = "Lane ";

        /// <summary>Системы куста без своей эмиссии: частицы ставит вид (SetParticles).</summary>
        public const string BushGroundSystem = "Bush Ground", BushCrackSystem = "Bush Cracks", BushLeafSystem = "Bush Leaves",
            BushMoteSystem = "Bush Motes", BushGlintSystem = "Bush Glint", BushDustSystem = "Bush Dust";

        /// <summary>Куст под ногами — пятно разрытой земли и трещины, м (лёжа билборд рисуется ~1/√2 размера).</summary>
        private const float BushPatchSize = 2.3f, BushCrackSize = 2.6f;

        /// <summary>Листья и пыль доживают после ухода куста, тиков.</summary>
        private const float BushLingerTicks = 30f;

        /// <summary>Сухой тон увядшего куста (множитель базового цвета): листва — бурая, кора — серая.</summary>
        private static readonly Color BushDryLeaf = new Color(.56f, .42f, .26f), BushDryWood = new Color(.62f, .55f, .48f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Куст в пуле: экземпляр префаба, части с исходными местами, буферы частиц и состояние куста Sim.</summary>
        private sealed class BushView
        {
            public Vfx Vfx;
            public Transform Root, Core;
            public Renderer CoreRenderer;
            public Vector3 CorePos, CoreScale;
            public Quaternion CoreRot;
            /// <summary>Ось сетки листвы, что смотрит вверх после поворота (0 — X, 1 — Y, 2 — Z).</summary>
            public int CoreUp = 1;
            public Color CoreBase = Color.white, WoodBase = Color.white;
            /// <summary>Шипы линий (0–3) и стебли: части, исходные места, ось «никнет» (наружу-вниз) и ось качания.</summary>
            public Transform[] Lanes = new Transform[0], Canes = new Transform[0];
            public Vector3[] LanePos = new Vector3[0], LaneScale = new Vector3[0], LaneDroop = new Vector3[0];
            public Vector3[] CanePos = new Vector3[0], CaneScale = new Vector3[0], CaneDroop = new Vector3[0];
            public Quaternion[] LaneRot = new Quaternion[0], CaneRot = new Quaternion[0];
            public Renderer[] Wood = new Renderer[0];
            public readonly MaterialPropertyBlock LeafBlock = new MaterialPropertyBlock(), WoodBlock = new MaterialPropertyBlock();
            public float Dry = -1f;
            public bool BodyShown;

            public ParticleSystem Ground, Cracks, Leaves, Motes, Glint, Dust;
            public ParticleSystem.Particle[] GroundBuffer = new ParticleSystem.Particle[0], CrackBuffer = new ParticleSystem.Particle[0],
                LeafBuffer = new ParticleSystem.Particle[0], MoteBuffer = new ParticleSystem.Particle[0],
                GlintBuffer = new ParticleSystem.Particle[0], DustBuffer = new ParticleSystem.Particle[0];

            public int Boss = -1, Serial, Cast, Order;
            public bool Shown, Seen, Sprouted, Lost, SproutFired, LaunchFired;
            public Vector3 Centre;
            public float Yaw;
            /// <summary>Последние сроки Sim (Часы сдвигают — перечитываются каждый кадр, пока куст есть).</summary>
            public int SproutTick, LaunchTick, WitherTick, GoneTick;
            /// <summary>Куст пропал из Sim раньше срока: часы босса в тот кадр.</summary>
            public float LostClock;
        }

        private VfxPool _bushPool, _bushSprout, _bushLaunch;
        private BushView[] _bushes = new BushView[0];
        private bool _bushOverflowReported;

        // ------------------------------------------------------------ pools

        private void MakeBushPools(ref int missing)
        {
            _bushPool = MakeVfxPool(BushName, ThicketMasterSeedRules.BushPool, ref missing);
            // Всплески — один на куст; кусты каста прорастают через 9 тиков, касты — не чаще 6,5 с от начала.
            _bushSprout = MakeVfxPool(BushSproutName, ThicketMasterSeedRules.BushPool, ref missing);
            _bushLaunch = MakeVfxPool(BushLaunchName, ThicketMasterSeedRules.BushPool, ref missing);
            _bushes = new BushView[_bushPool.Items.Length];
            for (int i = 0; i < _bushes.Length; i++) _bushes[i] = PrepareBush(_bushPool.Items[i]);
        }

        private static BushView PrepareBush(Vfx vfx)
        {
            var view = new BushView { Vfx = vfx, Root = vfx.Fx.Root.transform };
            view.Core = view.Root.Find(BushCoreChild);
            if (view.Core != null)
            {
                view.CorePos = view.Core.localPosition; view.CoreRot = view.Core.localRotation; view.CoreScale = view.Core.localScale;
                Vector3 meshUp = Quaternion.Inverse(view.CoreRot) * Vector3.up;
                float ux = Mathf.Abs(meshUp.x), uy = Mathf.Abs(meshUp.y), uz = Mathf.Abs(meshUp.z);
                view.CoreUp = ux > uy && ux > uz ? 0 : uz > uy ? 2 : 1;
                view.CoreRenderer = view.Core.GetComponent<Renderer>();
                if (view.CoreRenderer != null && view.CoreRenderer.sharedMaterial != null && view.CoreRenderer.sharedMaterial.HasProperty(BaseColorId))
                    view.CoreBase = view.CoreRenderer.sharedMaterial.GetColor(BaseColorId);
            }
            var lanes = view.Root.Find(BushLanesChild);
            var canes = view.Root.Find(BushCanesChild);
            int laneCount = lanes != null ? lanes.childCount : 0, caneCount = canes != null ? canes.childCount : 0;
            view.Lanes = new Transform[laneCount]; view.LanePos = new Vector3[laneCount]; view.LaneScale = new Vector3[laneCount];
            view.LaneRot = new Quaternion[laneCount]; view.LaneDroop = new Vector3[laneCount];
            view.Canes = new Transform[caneCount]; view.CanePos = new Vector3[caneCount]; view.CaneScale = new Vector3[caneCount];
            view.CaneRot = new Quaternion[caneCount]; view.CaneDroop = new Vector3[caneCount];
            view.Wood = new Renderer[laneCount + caneCount];
            for (int k = 0; k < laneCount; k++)
            {
                // Порядок — по номеру в имени «Lane k»: шип k смотрит вдоль линии k.
                var t = lanes.GetChild(k);
                int lane = k;
                if (t.name.StartsWith(BushLanePrefix, System.StringComparison.Ordinal)
                    && int.TryParse(t.name.Substring(BushLanePrefix.Length), out int parsed) && parsed >= 0 && parsed < laneCount) lane = parsed;
                view.Lanes[lane] = t;
            }
            for (int k = 0; k < laneCount; k++)
            {
                var t = view.Lanes[k] != null ? view.Lanes[k] : lanes.GetChild(k);
                view.Lanes[k] = t;
                view.LanePos[k] = t.localPosition;
                view.LaneRot[k] = t.localRotation; view.LaneScale[k] = t.localScale;
                view.LaneDroop[k] = DroopAxis(t.localRotation, t.localPosition);
                view.Wood[k] = t.GetComponent<Renderer>();
            }
            for (int k = 0; k < caneCount; k++)
            {
                var t = canes.GetChild(k);
                view.Canes[k] = t;
                view.CanePos[k] = t.localPosition; view.CaneRot[k] = t.localRotation; view.CaneScale[k] = t.localScale;
                view.CaneDroop[k] = DroopAxis(t.localRotation, t.localPosition);
                view.Wood[laneCount + k] = t.GetComponent<Renderer>();
            }
            for (int k = 0; k < view.Wood.Length; k++)
            {
                var r = view.Wood[k];
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)) { view.WoodBase = r.sharedMaterial.GetColor(BaseColorId); break; }
            }
            // Позы частей пишутся в местных осях узлов: узлы «Lanes»/«Canes» стоят в нуле корня без поворота (сборщик).
            if (lanes != null) { lanes.localPosition = Vector3.zero; lanes.localRotation = Quaternion.identity; }
            if (canes != null) { canes.localPosition = Vector3.zero; canes.localRotation = Quaternion.identity; }
            foreach (var ps in vfx.Fx.Particles)
            {
                var main = ps.main; main.loop = true;
                var emission = ps.emission; emission.enabled = false;
                var buffer = new ParticleSystem.Particle[Mathf.Max(1, main.maxParticles)];
                switch (ps.name)
                {
                    case BushGroundSystem: view.Ground = ps; view.GroundBuffer = buffer; break;
                    case BushCrackSystem: view.Cracks = ps; view.CrackBuffer = buffer; break;
                    case BushLeafSystem: view.Leaves = ps; view.LeafBuffer = buffer; break;
                    case BushMoteSystem: view.Motes = ps; view.MoteBuffer = buffer; break;
                    case BushGlintSystem: view.Glint = ps; view.GlintBuffer = buffer; break;
                    case BushDustSystem: view.Dust = ps; view.DustBuffer = buffer; break;
                }
            }
            SetBushBody(view, false);
            return view;
        }

        /// <summary>Ось, вокруг которой часть клонится наружу и вниз: вверх × направление части по земле (или её места).</summary>
        private static Vector3 DroopAxis(Quaternion rotation, Vector3 position)
        {
            Vector3 up = rotation * Vector3.up;
            var outward = new Vector3(up.x, 0f, up.z);
            if (outward.sqrMagnitude < 1e-4f) outward = new Vector3(position.x, 0f, position.z);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
            return Vector3.Cross(Vector3.up, outward.normalized).normalized;
        }

        // ------------------------------------------------------------ frame

        /// <summary>Кусты из Sim: новые встают, сроки перечитываются, пропавшие раньше срока вянут сами; всплески — по разу.</summary>
        private void UpdateBushes(Simulation sim, float tick, float clock)
        {
            if (_bushes.Length == 0) return;
            for (int i = 0; i < _bushes.Length; i++) _bushes[i].Seen = false;
            if (_boss >= 0)
                for (int k = 0; k < Simulation.ThicketBushSlots; k++)
                {
                    if (!sim.TryGetThicketBush(_boss, k, out ThicketBushState b)) continue;
                    var view = FindBush(_boss, b.Serial) ?? TakeBush(_boss, in b);
                    if (view == null || view.Lost) continue;
                    view.Seen = true;
                    view.SproutTick = b.SproutTick; view.LaunchTick = b.LaunchTick; view.WitherTick = b.WitherTick; view.GoneTick = b.GoneTick;
                    view.Sprouted = b.Sprouted;
                    if (b.Sprouted && !view.SproutFired) FireBushSprout(view, b.SproutTick);
                    if (b.Launched && !view.LaunchFired) FireBushLaunch(view, b.LaunchTick);
                }
            for (int i = 0; i < _bushes.Length; i++)
            {
                var v = _bushes[i];
                if (!v.Shown) continue;
                if (!v.Seen && !v.Lost)
                {
                    // Пропал без срока (смерть, другой босс, сброс стенда) или ровно в GoneTick — вянет сам с этого кадра.
                    v.Lost = true;
                    v.LostClock = clock;
                }
                DrawBush(v, v.Boss == _boss ? clock : tick);
            }
        }

        private BushView FindBush(int boss, int serial)
        {
            for (int i = 0; i < _bushes.Length; i++)
                if (_bushes[i].Shown && _bushes[i].Boss == boss && _bushes[i].Serial == serial) return _bushes[i];
            return null;
        }

        /// <summary>Ближайший показанный куст босса к точке at (не дальше 1,2 м): центр на земле.</summary>
        private bool TryNearestBush(int boss, Vector3 at, out Vector3 centre)
        {
            centre = at;
            float best = 1.2f * 1.2f;
            bool found = false;
            for (int i = 0; i < _bushes.Length; i++)
            {
                var v = _bushes[i];
                if (!v.Shown || v.Boss != boss) continue;
                float dx = v.Centre.x - at.x, dz = v.Centre.z - at.z, d = dx * dx + dz * dz;
                if (d > best) continue;
                best = d; centre = v.Centre; found = true;
            }
            return found;
        }

        /// <summary>Свободный куст; нет — уступает тот, что вянет дольше всех (живые кусты важнее).</summary>
        private BushView FreeBush()
        {
            for (int i = 0; i < _bushes.Length; i++) if (!_bushes[i].Shown) return _bushes[i];
            BushView free = null;
            for (int i = 0; i < _bushes.Length; i++)
            {
                var v = _bushes[i];
                if (!v.Lost) continue;
                if (free == null || v.LostClock < free.LostClock) free = v;
            }
            if (free != null) HideBush(free);
            else if (!_bushOverflowReported)
            {
                _bushOverflowReported = true;
                Debug.LogError("[thicketmaster-vfx] Терновник: исчерпан пул кустов — куст невидим.");
            }
            return free;
        }

        private BushView TakeBush(int boss, in ThicketBushState b)
        {
            var view = FreeBush();
            if (view == null) return null;
            view.Shown = true; view.Boss = boss; view.Serial = b.Serial; view.Cast = b.Cast; view.Order = b.Order;
            view.Sprouted = view.Lost = view.SproutFired = view.LaunchFired = false;
            view.LostClock = 0f;
            view.Dry = -1f;
            float x = b.Center.X.ToFloat(), z = b.Center.Y.ToFloat();
            view.Centre = new Vector3(x, GroundY(x, z), z);
            view.Yaw = ThicketMasterSeedRules.AxisYawDegrees(b.Axis.X.ToFloat(), b.Axis.Y.ToFloat());
            view.SproutTick = b.SproutTick; view.LaunchTick = b.LaunchTick; view.WitherTick = b.WitherTick; view.GoneTick = b.GoneTick;
            view.Root.SetPositionAndRotation(view.Centre, Quaternion.Euler(0f, view.Yaw, 0f));
            view.Vfx.Fx.Root.SetActive(true);
            SetBushBody(view, false);
            foreach (var ps in view.Vfx.Fx.Particles) Play(ps);
            return view;
        }

        /// <summary>Земля рвётся под кустом (всплеск по обычным часам с тика прорастания, корень — центр, +Z — линия 0).</summary>
        private void FireBushSprout(BushView v, int sproutTick)
        {
            v.SproutFired = true;
            TakeVfx(_bushSprout, v.Boss, sproutTick, v.Centre, Quaternion.Euler(0f, v.Yaw, 0f), v.Serial * 8 + 1, 2.2f);
        }

        /// <summary>Выпуск: листья, щепки и искры по четырём линиям (всплеск по обычным часам с тика выпуска).</summary>
        private void FireBushLaunch(BushView v, int launchTick)
        {
            v.LaunchFired = true;
            TakeVfx(_bushLaunch, v.Boss, launchTick, v.Centre, Quaternion.Euler(0f, v.Yaw, 0f), v.Serial * 8 + 2, 1.8f);
        }

        private static void SetBushBody(BushView v, bool shown)
        {
            v.BodyShown = shown;
            if (v.CoreRenderer != null && v.CoreRenderer.enabled != shown) v.CoreRenderer.enabled = shown;
            for (int k = 0; k < v.Wood.Length; k++)
                if (v.Wood[k] != null && v.Wood[k].enabled != shown) v.Wood[k].enabled = shown;
        }

        private static void HideBush(BushView v)
        {
            v.Shown = false; v.Seen = false; v.Boss = -1; v.Serial = 0;
            v.Sprouted = v.Lost = v.SproutFired = v.LaunchFired = false;
            v.Dry = -1f;
            SetBushBody(v, false);
            if (v.Ground != null) v.Ground.SetParticles(v.GroundBuffer, 0);
            if (v.Cracks != null) v.Cracks.SetParticles(v.CrackBuffer, 0);
            if (v.Leaves != null) v.Leaves.SetParticles(v.LeafBuffer, 0);
            if (v.Motes != null) v.Motes.SetParticles(v.MoteBuffer, 0);
            if (v.Glint != null) v.Glint.SetParticles(v.GlintBuffer, 0);
            if (v.Dust != null) v.Dust.SetParticles(v.DustBuffer, 0);
            if (v.Vfx.Fx.Root.activeSelf) v.Vfx.Fx.Root.SetActive(false);
        }

        /// <summary>Все кусты — вон (сброс арены, смена поколения или глубины); всплески кустов — тоже.</summary>
        private void HideBushes()
        {
            for (int i = 0; i < _bushes.Length; i++) HideBush(_bushes[i]);
            RetirePool(_bushSprout, false, 0f);
            RetirePool(_bushLaunch, false, 0f);
        }
    }
}
