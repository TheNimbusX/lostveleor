using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — пенная дуга за головой якоря (спека 5, «Мах 1–2»): с начала этапа до удара + 3
    /// тонкая лента воды идёт за НАСТОЯЩЕЙ головой (её ставит риг якоря по запечённому пути, разбор
    /// anchor-core), точки пишутся в позднем кадре (PelagWreckLateHook) — «след за когтем, не
    /// после»; тело маха — серп пака CFXR «sword_trail 180 thick» на радиусе головы
    /// (.WreckSweep), он приходит к удару вместе с головой; в удар — веер брызг по касательной,
    /// после — лента рвётся на капли. Голову ведёт прежний PelagAnchorSlamView (нет запечек
    /// Wreck2, риг серию не взял) — ни ленты, ни серпа: у прежнего пути свой след, смеси нет.
    /// Удар оземь — тонкая лента вертикальной дуги и вихрь
    /// брызг в тик «над головой». В окне — капли срываются с цепи. Девятый вал: в заряде —
    /// индиговая лента за головой, крутящейся над героем (длиннее и шире с зарядом), полный заряд —
    /// вспышка капель. Конец серии — капли с цепи (якорь на спину).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckArcRun
        {
            public bool Active, Superseded, Ended, Charging, OverheadDone, SweepDone;
            /// <summary>Серия в «холодном железе» (база, Панцирь): пенной ленты, серпа и капель нет — голова только отслеживается.</summary>
            public bool Iron;
            public int Fx = -1;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem Drops, Foam;
            public readonly PelagWreckArcWater Water = new PelagWreckArcWater();
            public int Serial, Stage, StageStart, Contact, Overhead = -1, ChargeStart = -1, ReleaseTick = -1, ReleasedCharge;
            /// <summary>Сторона маха Sim (WreckState.Side): +1 — справа налево, −1 — слева направо, 0 — удары оземь.</summary>
            public int SimSide;
            public float EndShown = -1f, Flow, DropCarry, BornAt;
            public PelagForm Form;
            public readonly Vector3[] Points = new Vector3[PelagWreckArcWater.MaxPoints];
            public readonly float[] Times = new float[PelagWreckArcWater.MaxPoints];
            public int Count;
            public Vector3 Head, Velocity;
            public bool HasHead;
        }

        private readonly WreckArcRun[] _wkArcs = { new WreckArcRun(), new WreckArcRun(), new WreckArcRun() };
        private WreckArcRun _wkArcNow;
        /// <summary>Касательная последнего маха (скорость головы в удар) и его сторона — для отдачи задетых.</summary>
        private Vector3 _wkSwingDir;
        private int _wkSwingSide = 1, _wkSwingTick = -1;
        private Transform _wkHeadBody, _wkHead;
        private Renderer[] _wkHeadRenderers;
        private PelagAnchorSlamView _wkSlamView;
        /// <summary>Последняя начатая серия — в «холодном железе» (знаки махов на телах).</summary>
        private bool _wkIronSeries;

        /// <summary>
        /// Голову ведёт прежний PelagAnchorSlamView (риг не взял серию: нет запечек Wreck2 или состояний
        /// контроллера, отдал её в первые кадры) — дуга и серп Крушения v2 не рисуются.
        /// </summary>
        private bool WreckLegacyHead => _wkSlamView != null && _wkSlamView.Active;

        /// <summary>Центр головы якоря этого кадра: рамка её рендеров (голову ставит риг), иначе точка головы ArenaView.</summary>
        private bool WreckHeadPoint(out Vector3 head)
        {
            // Тело сменилось или голова ещё не собрана (снаряжение строится после тела) — искать заново.
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && (body != _wkHeadBody || _wkHead == null))
            {
                _wkHeadBody = body;
                PelagEquipmentView equipment = body.GetComponentInChildren<PelagEquipmentView>(true);
                _wkHead = equipment != null ? equipment.SlamHead : null;
                _wkSlamView = equipment != null ? equipment.GetComponent<PelagAnchorSlamView>() : null;
                _wkHeadRenderers = _wkHead != null ? _wkHead.GetComponentsInChildren<Renderer>(true) : null;
            }
            if (_wkHead != null && _wkHead.gameObject.activeInHierarchy && _wkHeadRenderers != null)
            {
                Bounds bounds = default;
                bool any = false;
                foreach (Renderer r in _wkHeadRenderers)
                {
                    if (r == null || !r.enabled) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (any) { head = bounds.center; return true; }
            }
            head = _arena.PlayerAnchorHeadPosition;
            return _wkHeadBody != null;
        }

        private WreckArcRun TakeWreckArc()
        {
            WreckArcRun pick = null;
            foreach (WreckArcRun run in _wkArcs)
                if (!run.Active) { pick = run; break; }
                else if (pick == null || run.BornAt < pick.BornAt) pick = run;
            ReleaseWreckArc(pick);
            return pick;
        }

        private void ReleaseWreckArc(WreckArcRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
            if (_wkArcNow == run) _wkArcNow = null;
        }

        private void ForgetWreckArcs()
        {
            foreach (WreckArcRun run in _wkArcs) ReleaseWreckArc(run);
            _wkArcNow = null;
        }

        /// <summary>Нажатие этапа (тик показа): новая лента; прошлая дорывается сама. Панцирь — с первого нажатия.</summary>
        private void BeginWreckStageVfx(in WkPending p)
        {
            if (p.Stage == 0 && p.Flag) BeginWreckShell(p);
            if (_wkArcNow != null && _wkArcNow.Active) _wkArcNow.Superseded = true;
            WreckArcRun run = TakeWreckArc();
            run.Serial = p.Serial;
            run.Stage = p.Stage;
            run.StageStart = p.Tick;
            run.Contact = p.Contact;
            run.Overhead = p.Overhead;
            run.ChargeStart = run.ReleaseTick = -1;
            run.Charging = run.Superseded = run.Ended = run.OverheadDone = run.HasHead = false;
            // Серп пака — только у махов (удар оземь — вертикальная лента и круг).
            run.SweepDone = p.Stage >= 2;
            // Сабельный ритм v4 (06.10): мах 1 справа налево (+1), мах 2 слева направо (−1), выпад — 0.
            run.SimSide = Simulation.WreckSwingSide(p.Stage);
            run.EndShown = -1f;
            run.Count = 0;
            run.Flow = run.DropCarry = 0f;
            run.Form = PelagForm.None;
            run.Iron = WreckIronReady && WreckIronForm(p.Form);
            _wkIronSeries = run.Iron;
            run.BornAt = Time.time;
            run.Active = true;
            _wkArcNow = run;
            // Махи всех форм — «холодное железо» (.WreckSwing): железная дуга за головой якоря вместо пенной ленты,
            // серпа пака и капель (run.Iron); удар оземь — как был.
            BeginWreckSwingVfx(run, p);
            int fx = WkSpawn(PelagVfxId.WreckArc, PlayerPosition(), Quaternion.identity, 0f, 8f, PelagForm.None, out GameObject go);
            if (fx < 0) { run.Active = false; _wkArcNow = null; return; }
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            run.Filter = go.transform.Find("Water")?.GetComponent<MeshFilter>();
            run.Drops = go.transform.Find("Drops")?.GetComponent<ParticleSystem>();
            run.Foam = go.transform.Find("Foam")?.GetComponent<ParticleSystem>();
            // Объект пула общий для пены и «железа»: у железной серии лента воды не рисуется вовсе.
            MeshRenderer water = run.Filter != null ? run.Filter.GetComponent<MeshRenderer>() : null;
            if (water != null) water.enabled = !run.Iron;
            run.Water.Begin();
        }

        /// <summary>Удар этапа (тик показа): махи — веер брызг по касательной головы; четвёртый — земля вокруг героя.</summary>
        private void PlayWreckContact(in WkPending p)
        {
            WreckArcRun run = _wkArcNow != null && _wkArcNow.Active && _wkArcNow.Serial == p.Serial ? _wkArcNow : null;
            Vector3 hero = PlayerPosition();
            Vector3 tangent = run != null && run.HasHead && run.Velocity.sqrMagnitude > 1f ? run.Velocity : Vector3.zero;
            // Сторона маха Sim (v4: мах 2 — слева направо); у выпада и «Четвёртого удара» — прежняя +1.
            _wkSwingSide = Simulation.WreckSwingSide(p.Stage) < 0 ? -1 : 1;
            _wkSwingTick = p.Tick;
            Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
            _wkSwingDir = flat.sqrMagnitude > .25f ? flat.normalized : Vector3.zero;
            if (CaptureRig.NoVfx) return;
            if (WreckIronReady && (run != null ? run.Iron : _wkIronSeries) && (p.Stage < 2 || p.Stage == 3))
            {
                // «Холодное железо»: мах — небольшой росчерк у головы якоря; четвёртый — круг вокруг героя.
                if (p.Stage < 2)
                {
                    // Мах — стек серии сабли (.WreckCombo, 06.10 вечер); без его сборки — рисованный (.WreckPainted), затем «железо».
                    if (!PlayWreckComboSwing(p) && !PlayWreckPaintedSwing(p)) PlayWreckIronSwing(p);
                    // Махи «железом» (.WreckSwingHit): искры с головы, толчок по весу маха; без сборки — прежний росчерк.
                    if (!PlayWreckSwingContact(run, p))
                    {
                        PlayWreckIronStreak(run);
                        _juice?.PunchCamera(.08f, .03f);
                    }
                }
                else
                {
                    // «Четвёртый удар» — тяжёлый мах (как мах 2) и земля вокруг героя: «железо» у базы и Якорной брони,
                    // прежняя пена у Волнореза и Девятого вала до их переделки.
                    if (!PlayWreckComboSwing(p) && !PlayWreckPaintedSwing(p)) PlayWreckIronSwing(p);
                    bool swing = PlayWreckSwingContact(run, p);
                    if (!swing || WreckIronForm(_wsLastForm)) PlayWreckIronFourth(hero);
                    else PlayWreckFoamFourth(hero);
                }
                return;
            }
            if (p.Stage < 2)
            {
                if (run != null && run.HasHead && run.Drops != null)
                {
                    Vector3 along = tangent.sqrMagnitude > 1f ? tangent.normalized : PlayerFacing();
                    for (int i = 0; i < 18; i++)
                    {
                        Vector3 spread = Random.insideUnitSphere * .45f;
                        WreckEmit(run.Drops, run.Head + spread * .2f, (along + spread + Vector3.up * .25f).normalized * Random.Range(3.5f, 7f),
                            PelagWreckFormLook.DropColor(PelagForm.None, Random.Range(.2f, 1f)));
                    }
                    for (int i = 0; i < 6 && run.Foam != null; i++)
                        WreckEmit(run.Foam, run.Head, (along + Random.insideUnitSphere * .5f) * Random.Range(1.5f, 3f),
                            PelagWreckFormLook.DropColor(PelagForm.None, Random.Range(0f, .3f)));
                }
                _juice?.PunchCamera(.08f, .03f);
            }
            else if (p.Stage == 3)
            {
                // «Четвёртый удар»: земля вокруг героя — веер Пенных волн и корона; кольцом-волной не рисуется (спека 2.5).
                Vector3 foot = new Vector3(hero.x, WreckGroundAt(hero) + .02f, hero.z);
                WkSpawn(PelagVfxId.WreckBurst, foot, Quaternion.LookRotation(PlayerFacing(), Vector3.up), 1.1f, 0f, PelagForm.None, out _);
                WreckCrown(foot, PlayerFacing(), 1.2f, PelagForm.None);
                _juice?.PunchCamera(.26f, .05f);
                PulseCombatLight(.45f);
            }
        }

        /// <summary>Отдача задетого махом: по скорости головы в удар; без неё — касательная по стороне маха.</summary>
        private Vector3 WreckSwingDirection(Vector3 hero, Vector3 body)
        {
            if (_wkSwingDir.sqrMagnitude > .5f) return _wkSwingDir;
            Vector3 radial = body - hero;
            PelagWreckVfxRules.SwingTangent(radial.x, radial.z, _wkSwingSide, out float x, out float z);
            var tangent = new Vector3(x, 0f, z);
            return tangent.sqrMagnitude > .25f ? tangent : FlatDirection(hero, body);
        }

        /// <summary>Девятый вал: якорь над головой — лента становится индиговым вихрем (с нуля, без прошлой дуги).</summary>
        private void BeginWreckChargeArc(in WkPending p)
        {
            WreckArcRun run = _wkArcNow != null && _wkArcNow.Active && _wkArcNow.Serial == p.Serial ? _wkArcNow : null;
            if (run == null) return;
            run.Charging = true;
            run.ChargeStart = p.Tick;
            run.Form = PelagForm.WreckNinthWave;
            run.Count = 0;
            PelagWreckFormLook.Apply(run.Object, PelagForm.WreckNinthWave);
        }

        private void ReleaseWreckChargeArc(in WkPending p)
        {
            WreckArcRun run = _wkArcNow != null && _wkArcNow.Active && _wkArcNow.Serial == p.Serial ? _wkArcNow : null;
            if (run == null) return;
            run.ReleaseTick = p.Tick;
            run.ReleasedCharge = p.Amount;
            if (!p.Flag || CaptureRig.NoVfx || !run.HasHead || run.Drops == null) return;
            // Полный заряд — вспышка капель индиго от головы во все стороны.
            for (int i = 0; i < 26; i++)
            {
                Vector3 away = Random.onUnitSphere;
                away.y = Mathf.Abs(away.y) * .6f + .2f;
                WreckEmit(run.Drops, run.Head, away.normalized * Random.Range(2.5f, 5f),
                    PelagWreckFormLook.DropColor(PelagForm.WreckNinthWave, Random.Range(.4f, 1f)));
            }
        }

        /// <summary>Конец серии: капли с цепи (якорь уходит на спину), лента дорывается.</summary>
        private void EndWreckArcs(in WkPending p)
        {
            WreckArcRun run = _wkArcNow != null && _wkArcNow.Active && _wkArcNow.Serial == p.Serial ? _wkArcNow : null;
            if (run == null) return;
            run.Ended = true;
            run.EndShown = p.Tick;
            if (CaptureRig.NoVfx || run.Iron || run.Drops == null || !run.HasHead) return;
            Vector3 hand = _arena.PlayerChainHandPosition;
            for (int i = 0; i < 10; i++)
                WreckEmit(run.Drops, Vector3.Lerp(hand, run.Head, Random.value), Random.insideUnitSphere * .6f + Vector3.down * .3f,
                    PelagWreckFormLook.DropColor(PelagForm.None, Random.Range(.3f, 1f)));
        }

        private static void WreckEmit(ParticleSystem system, Vector3 at, Vector3 velocity, Color color)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = color, applyShapeToPosition = false
            }, 1);
        }
    }
}
