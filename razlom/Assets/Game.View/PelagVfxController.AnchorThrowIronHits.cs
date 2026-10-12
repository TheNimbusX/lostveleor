using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// База Броска якоря и её формы — разовые знаки и следы на телах (см. PelagVfxController.AnchorThrowIron), в краске формы:
    ///  • знак на КАЖДОМ задетом (Hit в тик показа): стек знака Крушения — белое ядро, кобальтовые лучи втягиваются, эхо-звезда,
    ///    искры по ходу броска, сколы железа; от ног — комья, освещённые камни, пыль; стоп-кадр (тяжёлый — 2 кадра);
    ///  • тяжёлый, элита (Hit без тяги, Sim сразу оглушает) — звёзды оглушения до конца оглушения Sim (босса Sim не оглушает);
    ///  • тянутые (Hit «на тягу» и правда поехали — AnchorThrowReeled) — борозда по настоящему пути ног с рывка, у переднего
    ///    края комья, камни и пыль; доехавший встал (Stun ловли) — осыпь у ног и стоп-кадр;
    ///  • ловля — маленькая звезда у кулака, толчок камеры; срыв — струна рассыпается, борозды встают.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private const int AiFurrowSlots = 8, AiStunSlots = 4;

        private sealed class AiStun
        {
            public bool Active;
            public int Target = -1, Fx = -1;
            public GameObject Object;
            public float Height;
        }

        private sealed class AiFurrow
        {
            public bool Noted, Started, Stopped, Wide;
            public int Target = -1, Serial = -1, Fx = -1;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem Clods, Rocks, Dust;
            public float Half, ClodCarry, RockCarry, DustCarry, StopTime, Clock;
            public Vector3 Last;
            public readonly AnchorThrowIronFurrow Strip = new AnchorThrowIronFurrow();
        }

        private AiFurrow[] _aiFurrows;
        private AiStun[] _aiStuns;

        private AiFurrow[] AiFurrows
        {
            get
            {
                if (_aiFurrows != null) return _aiFurrows;
                _aiFurrows = new AiFurrow[AiFurrowSlots];
                for (int i = 0; i < _aiFurrows.Length; i++) _aiFurrows[i] = new AiFurrow();
                return _aiFurrows;
            }
        }

        private AiStun[] AiStuns
        {
            get
            {
                if (_aiStuns != null) return _aiStuns;
                _aiStuns = new AiStun[AiStunSlots];
                for (int i = 0; i < _aiStuns.Length; i++) _aiStuns[i] = new AiStun();
                return _aiStuns;
            }
        }

        // ---------------------------------------------------------------- знак на задетом

        /// <summary>
        /// Знак на задетом в краске формы. Веер — по ходу своей полосы; сеть Невода (полоса 3, в тик рывка) — к Пелагу, меньше, и
        /// борозда сразу (рывок уже был); укус Гарпуна (полоса 0) — крупнее, стоп-кадр и толчок сильнее. Оглушение — по Sim.
        /// </summary>
        private void PlayAnchorThrowIronHit(in AtPending p)
        {
            bool ours = AiOurs(p.Serial);
            PelagForm form = ours ? _aic.Form : PelagForm.None;
            bool net = p.Lane == 3;
            bool bite = form == PelagForm.AnchorThrowHarpoon && p.Lane == 0;
            if (bite && AtOurs(p.Serial)) _atRun.HarpoonPulled = p.Flag;
            if (ours && p.Flag)
            {
                AiNoteTow(p.Target, p.Serial, bite);
                if (net) StartAiFurrows(p.Serial);
            }
            if (CaptureRig.NoVfx) return;
            Vector3 hero = PlayerPosition();
            Vector3 body = EntityPosition(p.Target, p.At);
            Vector3 dir = !ours ? FlatDirection(hero, body) : net ? FlatDirection(body, hero) : AtDir(_aic.Snap, Mathf.Clamp(p.Lane, 0, 2));
            float r = AtBodyRadius(p.Target);
            Vector3 feet = AtFeet(body);
            Vector3 chest = feet + Vector3.up * PelagAnchorThrowIronLook.HitHeight(r);
            Camera camera = Camera.main;
            // Перед поверхностью тела, иначе вспышка тонет в модели.
            if (camera != null) chest += (camera.transform.position - chest).normalized * .45f;
            float scale = bite ? PelagAnchorThrowIronLook.HitBite : PelagAnchorThrowIronLook.HitScale(p.Flag, net);
            PelagVfxId id = ours ? AiFormId(PelagVfxId.AnchorThrowFormHitNet, PelagVfxId.AnchorThrowFormHitFan, PelagVfxId.AnchorThrowFormHitHarpoon,
                PelagVfxId.AnchorThrowIronHit) : PelagVfxId.AnchorThrowIronHit;
            int fx = AiSpawn(id, chest, Quaternion.LookRotation(dir, Vector3.up), scale, 0f, out GameObject go);
            if (fx >= 0)
            {
                // Укус Гарпуна: цель сразу едет к Пелагу — звезда и эхо (локальные частицы) едут с телом, а не висят пятном
                // там, где цель была (игра V9).
                if (bite)
                {
                    _aiBite.Fx = fx;
                    _aiBite.Object = go;
                    _aiBite.Target = p.Target;
                    _aiBite.Offset = chest - EntityPosition(p.Target, body);
                }
                // Камни, комья и пыль — от ног задетого; плоскость отскока камней — земля под ним.
                Transform t = go.transform;
                Transform f = t.Find("Feet"), g = t.Find("Ground");
                if (f != null) f.position = feet + Vector3.up * .3f;
                if (g != null) g.SetPositionAndRotation(feet - Vector3.up * .02f, Quaternion.identity);
            }
            _arena.HoldEntityPose(p.Target, bite ? PelagAnchorThrowIronLook.HoldHeavy + .004f
                : p.Flag ? PelagAnchorThrowIronLook.HoldLight : PelagAnchorThrowIronLook.HoldHeavy);
            if (!p.Flag || bite) _juice?.PunchCamera(bite ? .16f : .10f, bite ? .03f : .02f);
            Simulation sim = _driver.Sim;
            // Оглушение Sim: тяжёлый и элита сразу, цель Гарпуна — с укуса до ловли + 1 с; лёгких Sim оглушает в ловлю.
            if (sim != null && (uint)p.Target < (uint)sim.Entities.Count && sim.Statuses.StunUntilTick[p.Target] > p.Tick)
                StartAiStun(p.Target);
            if (net) AiNetCaught(p.Target);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[throw-iron] hit tick={p.Tick} target={p.Target} lane={p.Lane} form={form} pulled={p.Flag} r={r:F2} scale={scale:F2}");
        }

        /// <summary>Знак укуса Гарпуна, который едет с целью.</summary>
        private sealed class AiBiteMark
        {
            public int Fx = -1, Target = -1;
            public GameObject Object;
            public Vector3 Offset;
        }

        private readonly AiBiteMark _aiBite = new AiBiteMark();

        private void UpdateAiBite(Simulation sim)
        {
            AiBiteMark b = _aiBite;
            if (b.Fx < 0) return;
            if (!AtStill(b.Fx, b.Object) || (uint)b.Target >= (uint)sim.Entities.Count || !sim.Entities.Alive[b.Target])
            {
                b.Fx = -1;
                b.Object = null;
                return;
            }
            b.Object.transform.position = EntityPosition(b.Target, b.Object.transform.position - b.Offset) + b.Offset;
        }

        // ---------------------------------------------------------------- оглушение тяжёлого

        private void StartAiStun(int target)
        {
            AiStun free = null;
            foreach (AiStun st in AiStuns)
            {
                if (st.Active && st.Target == target) return;
                if (free == null && !st.Active) free = st;
            }
            if (free == null || !_arena.TryGetEntityView(target, out Transform body)) return;
            float top = 0f;
            bool found = false;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
                found = true;
            }
            free.Height = Mathf.Clamp((found ? top - body.position.y : 1.8f) + PelagAnchorThrowIronLook.StunAbove, 1.2f, 3.6f);
            PelagVfxId id = AiFormId(PelagVfxId.AnchorThrowFormStunNet, PelagVfxId.AnchorThrowFormStunFan, PelagVfxId.AnchorThrowFormStunHarpoon,
                PelagVfxId.AnchorThrowIronStun);
            free.Fx = AiSpawn(id, body.position + Vector3.up * free.Height, Quaternion.identity, 0f, 3f, out free.Object);
            if (free.Fx < 0) return;
            free.Target = target;
            free.Active = true;
        }

        private void UpdateAiStuns(Simulation sim, float shown)
        {
            if (_aiStuns == null) return;
            foreach (AiStun st in _aiStuns)
            {
                if (!st.Active) continue;
                if (!AtStill(st.Fx, st.Object)) { st.Active = false; continue; }
                bool alive = (uint)st.Target < (uint)sim.Entities.Count && sim.Entities.Alive[st.Target];
                if (!alive || shown >= sim.Statuses.StunUntilTick[st.Target]) { EndAiStun(st); continue; }
                st.Object.transform.position = EntityPosition(st.Target, st.Object.transform.position) + Vector3.up * st.Height;
            }
        }

        private void EndAiStun(AiStun st)
        {
            if (st.Active && AtStill(st.Fx, st.Object)) Release(st.Fx);
            st.Active = false;
            st.Fx = -1;
            st.Object = null;
        }

        private void EndAiStuns()
        {
            if (_aiStuns == null) return;
            foreach (AiStun st in _aiStuns) EndAiStun(st);
        }

        // ---------------------------------------------------------------- ловля, приземление, срыв

        private void PlayAnchorThrowIronCatch(in AtPending p)
        {
            if (!AiOurs(p.Serial)) return;
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : p.Tick;
            if (_aic.BreakFrom < 0f) _aic.BreakFrom = Mathf.Max(p.Tick, shown - 1f);
            CatchAiGhosts(shown);
            // Гарпун: цель доехала (её оглушение — с укуса, Stun ловли не придёт) — борозда кончается у её ног.
            AnchorThrowRun run = _atRun;
            int target = _aic.Snap.HarpoonTarget;
            if (_aic.Snap.Stuck && run.HarpoonPulled && target >= 0 && _arena.TryGetEntityView(target, out Transform body))
                PlayAnchorThrowIronLand(new AtPending { Kind = AtKind.Land, Tick = p.Tick, Target = target, Serial = p.Serial, At = body.position });
            if (CaptureRig.NoVfx) return;
            Vector3 fist = AnchorThrowFist();
            Vector3 outward = AtDir(_aic.Snap, 0);
            PelagVfxId id = AiFormId(PelagVfxId.AnchorThrowFormCatchNet, PelagVfxId.AnchorThrowFormCatchFan, PelagVfxId.AnchorThrowFormCatchHarpoon,
                PelagVfxId.AnchorThrowIronCatch);
            AiSpawn(id, fist, Quaternion.LookRotation(outward + Vector3.up * .3f), 0f, 0f, out _);
            _juice?.PunchCamera(.10f + .03f * Mathf.Min(4, p.Amount), .03f);
        }

        /// <summary>Stun следом за ловлей: тело доехало — борозда кончается у ног осыпью, стоп-кадр 2 кадра.</summary>
        private void PlayAnchorThrowIronLand(in AtPending p)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 body = EntityPosition(p.Target, p.At);
            AiFurrowLanded(p.Target, AtFeet(body));
            _arena.HoldEntityPose(p.Target, .07f);
        }

        /// <summary>Срыв до ловли (конец Interrupted): струны рассыпаются с тика конца, призраки тают, борозды встают сами.</summary>
        private void DropAnchorThrowIronVfx(float tick)
        {
            if (AiAnyLane && _aic.BreakFrom < 0f) _aic.BreakFrom = tick;
            CatchAiGhosts(tick);
        }
    }
}
