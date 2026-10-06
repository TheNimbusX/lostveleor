using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — голова, цепь и вода на ней (кадр A-base). Голова каждой полосы летит
    /// ПО ЛИНИИ Sim (PelagAnchorThrowVfxRules.Head — формула Sim), в тик натяга дёргается к руке,
    /// возвращается по той же линии низко над полом и в тик ловли — в руке.
    ///
    /// Два пути (AnchorThrowVfxSwitches.UseRig):
    ///  • РИГ (artifacts/anchor-core, PelagAnchorRig.Throw): каст — BeginLine, каждый кадр — DriveLine
    ///    (фаза, Origin, Dir, Along и его скорость от Sim); голову и цепь рисует риг (Thrown → дёрг →
    ///    Yank → Caught → маятник/уборка), вода и капли берут его хват и кольцо. Мост — partial-методы
    ///    ниже, их тело — PelagVfxController.AnchorThrowRig.cs (ставится вместе с ригом);
    ///  • ВРЕМЕННЫЙ путь Абордажа v2 (спека §5.1), пока рига нет: голова и цепь из своих пулов
    ///    (AnchorThrowAnchor/Chain — префабы прежнего броска), цепь прямая хват → кольцо, голову в
    ///    ловлю принимает пояс (PelagAnchorSlamView.ReturnFlyingAnchor), как у Абордажа.
    /// Вода вдоль цепи — лента семьи (PelagAbordageRibbon), за головой в полёте — короткий росчерк,
    /// под цепью на земле — тонкая тень-линия воды (читается направление броска).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AnchorThrowRun
        {
            public bool Active, Released, Caught, Ended, HandedOff, Rig, StreakDone, HarpoonPulled, HarpoonRipped;
            public int Serial = -1, Slot = -1;
            public PelagForm Form;
            public Simulation Sim;
            public AnchorThrowState Snap;
            public AnchorThrowEnd EndReason;
            public float RetractStart = -1f, ReleaseHeight = 1.25f, RigAlongLast, ReleaseShown;
            public Vector3 Head, Ring, Grip, ReleaseError, RetractFrom, StreakTail;
            public float SleeveFlow, StreakFlow, DropCarry, StreakCarry;
            public int HeadFx = -1, ChainFx = -1, SleeveFx = -1, StreakFx = -1, ShadowFx = -1;
            public GameObject HeadObject, ChainObject, SleeveObject, StreakObject, ShadowObject;
            public PelagVfxElement HeadElement, ChainElement;
            public MeshFilter SleeveFilter, StreakFilter, ShadowFilter;
            public ParticleSystem SleeveDrops, StreakDrops;
            public readonly PelagAbordageRibbon Sleeve = new PelagAbordageRibbon(), Streak = new PelagAbordageRibbon(),
                Shadow = new PelagAbordageRibbon();
        }

        private readonly AnchorThrowRun _atRun = new AnchorThrowRun();
        private readonly Vector3[] _atChainLine = new Vector3[2];
        private Transform _atFistBody;
        private PelagEquipmentView _atEquipment;

        /// <summary>Вода бежит по ленте к герою, м/с: полёт, натяг и возврат; росчерк к хвосту и капли с головы.</summary>
        private const float AtSleeveFlowFlight = 1.6f, AtSleeveFlowTaut = 5f, AtStreakFlow = 6f, AtStreakDropRate = 70f;
        private const float AtSleeveDrop = .07f;
        /// <summary>Голова у руки после срыва: столько тиков (спека §5.2: «голова падает, цепь сматывается за 4 тика»).</summary>
        private const int AtRetractTicks = 4;

        /// <summary>Сколько голов Броска видно (VisibleFlyingAnchors: пояс не рисует второй якорь). У рига голова одна — его.</summary>
        private int AnchorThrowFlyingAnchors
            => _atRun.Active && !_atRun.Rig && AtStill(_atRun.HeadFx, _atRun.HeadObject) && _atRun.HeadObject.activeInHierarchy ? 1 : 0;

        // Мост к ригу якоря на цепи (тело — PelagVfxController.AnchorThrowRig.cs; без рига вызовы пустые).
        partial void AnchorThrowRigBegin(int serial, ref bool taken);
        partial void AnchorThrowRigDrive(int serial, byte phase, Vector3 origin, Vector3 direction, float along, float alongSpeed,
            bool reached, ref bool driven, ref Vector3 grip, ref Vector3 ring);

        /// <summary>AbilityCast: каст Броска — новый показ; голова до выпуска в правом кулаке (или у рига).</summary>
        private void BeginAnchorThrowVfxCast(int slot)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || (uint)slot >= Simulation.AbilitySlots || !AnchorThrowVfxReady) return;
            AbilityBuild build = sim.GetAbility(slot);
            if (build == null || build.DefinitionId != AbilityDefinition.AnchorThrowId) return;
            AnchorThrowState s = sim.AnchorThrow;
            if (s.Serial == 0 || s.Slot != slot) return;
            if (_atRun.Active) FinishAnchorThrowRun(false);
            AnchorThrowRun run = _atRun;
            run.Active = true;
            run.Released = run.Caught = run.Ended = run.HandedOff = run.StreakDone = run.HarpoonPulled = run.HarpoonRipped = false;
            run.Serial = s.Serial;
            run.Slot = slot;
            run.Form = s.Form;
            run.Sim = sim;
            run.Snap = s;
            run.RetractStart = -1f;
            run.SleeveFlow = run.StreakFlow = run.DropCarry = run.StreakCarry = 0f;
            run.HeadFx = run.ChainFx = run.SleeveFx = run.StreakFx = run.ShadowFx = -1;
            run.HeadObject = run.ChainObject = run.SleeveObject = run.StreakObject = run.ShadowObject = null;
            bool taken = false;
            if (AnchorThrowVfxSwitches.UseRig) AnchorThrowRigBegin(run.Serial, ref taken);
            run.Rig = taken;
            run.Ring = run.Grip = AnchorThrowFist();
            if (!run.Rig)
            {
                // Временный путь: сабля за кушак, якорь со спины прячется (как прежний бросок), голова — в правом кулаке.
                _arena.BeginPlayerAnchorUse(false);
                Vector3 fist = AnchorThrowFist();
                run.HeadFx = AtSpawnPart(PelagVfxId.AnchorThrowAnchor, fist, Quaternion.LookRotation(AtDir(s, 0), Vector3.up),
                    out run.HeadObject, out run.HeadElement);
                run.HeadElement?.SetTrailEmission(false);
                run.Head = fist;
            }
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[anchor-throw-vfx] cast serial={run.Serial} slot={slot} form={run.Form} rig={run.Rig} release={s.ReleaseTick} taut={s.TautTick} catch={s.CatchTick}");
        }

        /// <summary>Выпуск (72): цепь, вода на ней, росчерк и тень-линия — от руки; Веер — призраки.</summary>
        private void AnchorThrowReleased(in SimEvent e, int tick)
        {
            AnchorThrowRun run = _atRun;
            if (!run.Active || run.Serial != e.ActionVariant) return;
            Simulation sim = _driver.Sim;
            if (sim != null && sim.AnchorThrow.Serial == run.Serial) run.Snap = sim.AnchorThrow;
            run.Snap.ReleaseTick = tick;
        }

        private void ReleaseAnchorThrowAnchor(AnchorThrowRun run, float shown)
        {
            run.Released = true;
            Vector3 fist = run.Rig ? run.Ring : AnchorThrowFist();
            _atGroundBase = PlayerPosition().y;
            run.ReleaseHeight = Mathf.Clamp(fist.y - AnchorThrowGroundAt(fist.x, fist.z), .6f, 1.8f);
            AtHeadOnLine(run.ReleaseHeight, run.Snap, 0, shown, out Vector3 line);
            run.ReleaseError = fist - line;
            run.ReleaseShown = shown;
            run.StreakTail = fist;
            if (!run.Rig)
                run.ChainFx = AtSpawnPart(PelagVfxId.AnchorThrowChain, fist, Quaternion.identity, out run.ChainObject, out run.ChainElement);
            run.SleeveFx = AtSpawnRibbon(run.Form, fist, out run.SleeveObject, out run.SleeveFilter, out run.SleeveDrops);
            run.Sleeve.Begin();
            run.StreakFx = AtSpawnRibbon(run.Form, fist, out run.StreakObject, out run.StreakFilter, out run.StreakDrops);
            run.Streak.Begin();
            run.ShadowFx = AtSpawnRibbon(run.Form, fist, out run.ShadowObject, out run.ShadowFilter, out _);
            run.Shadow.Begin();
            EmitAnchorThrowDrops(run.SleeveDrops, run.Form, fist, fist, AtDir(run.Snap, 0), 8, 1.4f);
            if (run.Form == PelagForm.AnchorThrowFan) BeginAnchorThrowGhosts(run, fist, shown);
        }

        private int AtSpawnRibbon(PelagForm form, Vector3 at, out GameObject go, out MeshFilter filter, out ParticleSystem drops)
        {
            filter = null; drops = null;
            int fx = AtSpawn(PelagVfxId.AnchorThrowRibbon, at, Quaternion.identity, 0f, 6f, form, false, out go);
            if (fx < 0) return -1;
            go.transform.localScale = Vector3.one;
            filter = go.transform.Find("Water")?.GetComponent<MeshFilter>();
            drops = go.transform.Find("Drops")?.GetComponent<ParticleSystem>();
            return fx;
        }

        private static Vector3 AtDir(in AnchorThrowState s, int lane)
        {
            FixVec2 d = s.LaneDir(lane);
            var v = new Vector3(d.X.ToFloat(), 0f, d.Y.ToFloat());
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>Голова полосы на линии Sim этого тика показа, на высоте полёта/возврата над настоящей землёй.</summary>
        private void AtHeadOnLine(float releaseHeight, in AnchorThrowState s, int lane, float shown, out Vector3 head)
        {
            PelagAnchorThrowVfxRules.Head(s, lane, shown, out float x, out float z);
            float catchHeight = Mathf.Clamp(AnchorThrowFist().y - PlayerPosition().y, .6f, 1.6f);
            float y = AnchorThrowGroundAt(x, z) + PelagAnchorThrowVfxRules.HeadHeight(s, lane, shown, releaseHeight, catchHeight);
            head = new Vector3(x, y, z);
        }

        /// <summary>Правый кулак с якорем (Абордаж v2, лист B: голова по линии предплечья); без снаряжения — рука цепи.</summary>
        private Vector3 AnchorThrowFist()
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && body != _atFistBody)
            {
                _atFistBody = body;
                _atEquipment = body.GetComponentInChildren<PelagEquipmentView>(true);
            }
            return _atEquipment != null && _atEquipment.isActiveAndEnabled ? _atEquipment.AnchorThrowPosition : ChainHandPosition();
        }
    }
}
