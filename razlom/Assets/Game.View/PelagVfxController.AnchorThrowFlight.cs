using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — кадр головы и цепи: риг (DriveLine) или временный путь (своя голова, прямая
    /// цепь), вода вдоль цепи, росчерк за головой, тень-линия на земле, срыв (голова к руке за 4 тика)
    /// и передача поясу в ловлю (см. PelagVfxController.AnchorThrowAnchor).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private void UpdateAnchorThrowRun(Simulation sim, float shown, float dt)
        {
            AnchorThrowRun run = _atRun;
            if (!run.Active) return;
            if (run.Sim != sim) { FinishAnchorThrowRun(false); return; }
            if (sim.AnchorThrow.Serial == run.Serial)
            {
                int release = run.Snap.ReleaseTick;
                run.Snap = sim.AnchorThrow;
                if (run.Snap.ReleaseTick <= 0) run.Snap.ReleaseTick = release;
            }
            AnchorThrowState s = run.Snap;
            if (!run.Released && !run.Ended && run.RetractStart < 0f && s.ReleaseTick > 0 && shown >= s.ReleaseTick)
                ReleaseAnchorThrowAnchor(run, shown);

            Vector3 dir = AtDir(s, 0);
            Vector3 head;
            if (!run.Released) head = run.Rig ? run.Ring : AnchorThrowFist();
            else
            {
                AtHeadOnLine(run.ReleaseHeight, s, 0, shown, out head);
                if (!run.Rig) head += run.ReleaseError * PelagAnchorThrowVfxRules.ReleaseSeam(shown, run.ReleaseShown);
                head -= dir * PelagAnchorThrowVfxRules.Jerk(shown, s.TautTick);
                head = AtHarpoonHead(run, s, shown, head);
            }
            if (run.RetractStart >= 0f)
                head = Vector3.Lerp(run.RetractFrom, AnchorThrowFist(), PelagAbordageVfxRules.Retract(shown, run.RetractStart, AtRetractTicks));
            run.Head = head;

            if (run.Rig) DriveAnchorThrowRig(run, s, shown, head, dir);
            else PlaceAnchorThrowHead(run, s, shown, head);

            if (run.Released)
            {
                UpdateAtSleeve(run, s, shown, dt);
                UpdateAtStreak(run, s, shown, dt, head, FlatDirection(run.Grip, head));
                UpdateAtShadow(run, s, shown);
            }

            bool home = run.RetractStart >= 0f ? shown >= run.RetractStart + AtRetractTicks : run.Released && shown >= s.CatchTick;
            if (home && !run.HandedOff) HandOffAnchorThrowAnchor(run);
            // Конец пришёл, голова у руки, вода на цепи доломалась на капли — показ закончен.
            if (run.Ended && run.HandedOff && run.SleeveFx < 0 && run.ShadowFx < 0 && run.StreakFx < 0) FinishAnchorThrowRun(true);
        }

        /// <summary>Риг: кадр линии от Sim (высота — профиль вида, Origin берёт её каждый кадр); хват и кольцо — от рига.</summary>
        private void DriveAnchorThrowRig(AnchorThrowRun run, in AnchorThrowState s, float shown, Vector3 head, Vector3 dir)
        {
            var phase = PelagAnchorThrowVfxRules.RigPhase(s, shown);
            if (run.Ended && run.EndReason == AnchorThrowEnd.Interrupted && phase != AnchorThrowRigPhase.Done) phase = AnchorThrowRigPhase.None;
            float along = PelagAnchorThrowVfxRules.RigAlong(s, shown);
            float speed = PelagAnchorThrowVfxRules.RigAlongSpeed(s, shown);
            if (s.Stuck && shown >= PelagAnchorThrowVfxRules.LaneArriveTick(s, 0))
            {
                // Гарпун: кольцо там, где голова в теле цели (проекция на линию), скорость — по ходу цели.
                Vector3 origin0 = new Vector3(s.Origin.X.ToFloat(), head.y, s.Origin.Y.ToFloat());
                float stuck = Mathf.Max(0f, Vector3.Dot(head - origin0, dir) - PelagAnchorThrowVfxRules.RingToCentre);
                speed = Time.deltaTime > 1e-4f ? (stuck - run.RigAlongLast) / Time.deltaTime : 0f;
                along = stuck;
            }
            run.RigAlongLast = along;
            var origin = new Vector3(s.Origin.X.ToFloat(), head.y, s.Origin.Y.ToFloat());
            bool driven = false;
            Vector3 grip = ChainHandPosition(), ring = head;
            AnchorThrowRigDrive(run.Serial, (byte)phase, origin, dir, along, speed, shown >= s.TautTick, ref driven, ref grip, ref ring);
            run.Grip = driven ? grip : ChainHandPosition();
            run.Ring = driven ? ring : head;
        }

        /// <summary>Временный путь: голова из пула на линии, кольцо к руке, крутка в полёте; цепь прямая хват → кольцо.</summary>
        private void PlaceAnchorThrowHead(AnchorThrowRun run, in AnchorThrowState s, float shown, Vector3 head)
        {
            Vector3 grip = ChainHandPosition();
            run.Grip = grip;
            run.Ring = head;
            if (!AtStill(run.HeadFx, run.HeadObject)) return;
            Vector3 outward = run.Released ? FlatDirection(grip, head) : AtDir(s, 0);
            run.HeadObject.transform.SetPositionAndRotation(head, Quaternion.LookRotation(outward, Vector3.up));
            Transform spinner = run.HeadElement.Spinner;
            if (spinner != null)
            {
                float arrive = PelagAnchorThrowVfxRules.LaneArriveTick(s, 0);
                float roll = !run.Released ? 0f
                    : shown < arrive ? Mathf.Lerp(-40f, 0f, (shown - s.ReleaseTick) / Mathf.Max(1f, arrive - s.ReleaseTick))
                    : shown > s.TautTick ? Mathf.Lerp(0f, 25f, (shown - s.TautTick) / Mathf.Max(1f, s.ReturnTicks)) : 0f;
                spinner.localRotation = Quaternion.Euler(0f, 0f, roll);
            }
            run.Ring = run.HeadElement.AnchorRingPosition;
            if (run.Released && AtStill(run.ChainFx, run.ChainObject))
            {
                _atChainLine[0] = grip;
                _atChainLine[1] = run.Ring;
                run.ChainElement.SetCurvePoints(_atChainLine);
            }
        }

        /// <summary>
        /// Гарпун (кадр D): голова вошла в цель на 0,25 м и держится в теле, пока цель тянется (за 2 тика до
        /// ловли выходит к руке); не тянется (вес 0, босс) — вырывается в натяг за тик и идёт по линии.
        /// </summary>
        private Vector3 AtHarpoonHead(AnchorThrowRun run, in AnchorThrowState s, float shown, Vector3 head)
        {
            int target = s.HarpoonTarget;
            if (!s.Stuck || target < 0 || shown < PelagAnchorThrowVfxRules.LaneArriveTick(s, 0)) return head;
            if (!_arena.TryGetEntityView(target, out Transform body)) return head;
            Vector3 bite = AtBitePoint(target, body.position);
            float w = run.HarpoonPulled
                ? (shown < s.CatchTick ? PelagAnchorThrowVfxRules.Smooth01((s.CatchTick - shown) / 2f) : 0f)
                : 1f - PelagAnchorThrowVfxRules.Smooth01(shown - s.TautTick);
            return Vector3.Lerp(head, bite, w);
        }

        /// <summary>Точка укуса: видимая грудь со стороны героя, на 0,25 м внутрь (спека §5.3).</summary>
        private Vector3 AtBitePoint(int target, Vector3 body)
        {
            Simulation sim = _driver.Sim;
            float r = sim != null && (uint)target < (uint)sim.Entities.Count ? sim.Entities.BodyRadius[target].ToFloat()
                : EntityStore.DefaultBodyRadius.ToFloat();
            Vector3 toHero = FlatDirection(body, PlayerPosition());
            return body + toHero * Mathf.Max(0f, PelagAbordageVfxRules.VisibleBodyRadius(r) - .25f)
                   + Vector3.up * PelagAbordageVfxRules.BiteHeight(r);
        }

        private void UpdateAtSleeve(AnchorThrowRun run, in AnchorThrowState s, float shown, float dt)
        {
            if (!AtStill(run.SleeveFx, run.SleeveObject)) return;
            float tension = run.RetractStart >= 0f ? .2f : PelagAnchorThrowVfxRules.Tension(s, shown);
            float half = PelagAnchorThrowVfxRules.SleeveHalfWidth(tension, run.Form);
            float from = run.RetractStart >= 0f ? run.RetractStart : s.CatchTick;
            float age = PelagAnchorThrowVfxRules.BreakAge(shown, from);
            run.SleeveFlow += dt * (shown < s.TautTick ? AtSleeveFlowFlight : AtSleeveFlowTaut);
            float appear = PelagAnchorThrowVfxRules.Smooth01((shown - s.ReleaseTick) / 1.2f);
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : run.Grip + Vector3.up * 10f;
            Vector3 down = camera != null ? -camera.transform.up : Vector3.down;
            run.SleeveObject.transform.position = run.Grip;
            if (run.SleeveFilter != null)
                run.Sleeve.Build(FormWaterMesh.MeshFor(run.SleeveFilter, PelagAbordageRibbon.MeshName), run.Grip, run.Grip, run.Ring, eye, down,
                    AtSleeveDrop, half * .6f, half, age, age, run.SleeveFlow, .05f + .25f * tension, appear, Time.time);
            // Капли с цепи: в натяг сыплет, вода бежит к герою.
            if (age <= 0f && tension > .9f && dt > 0f && !CaptureRig.NoVfx)
            {
                run.DropCarry += dt * PelagAnchorThrowVfxRules.SleeveDropRate(tension) * Vector3.Distance(run.Grip, run.Ring);
                int count = (int)run.DropCarry;
                run.DropCarry -= count;
                if (count > 0) EmitAnchorThrowDrops(run.SleeveDrops, run.Form, run.Grip, run.Ring, -(run.Ring - run.Grip).normalized, count, 1f);
            }
            if (age > .55f) { Release(run.SleeveFx); run.SleeveFx = -1; run.SleeveObject = null; }
        }

        private void UpdateAtStreak(AnchorThrowRun run, in AnchorThrowState s, float shown, float dt, Vector3 head, Vector3 outward)
        {
            if (run.StreakDone || !AtStill(run.StreakFx, run.StreakObject)) return;
            int arrive = PelagAnchorThrowVfxRules.LaneArriveTick(s, 0);
            bool flying = shown < arrive && run.RetractStart < 0f;
            if (flying) run.StreakTail = head - outward * PelagAbordageVfxRules.StreakLength(Vector3.Distance(run.Grip, head));
            float ageTail = PelagAbordageVfxRules.StreakAge(shown, arrive, 0f);
            float ageHead = PelagAbordageVfxRules.StreakAge(shown, arrive, 1f);
            run.StreakFlow += dt * AtStreakFlow;
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : head + Vector3.up * 10f;
            run.StreakObject.transform.position = run.StreakTail;
            if (run.StreakFilter != null)
                run.Streak.Build(FormWaterMesh.MeshFor(run.StreakFilter, PelagAbordageRibbon.MeshName), run.StreakTail, run.StreakTail, head,
                    eye, Vector3.down, 0f, .018f, .07f, ageTail, ageHead, run.StreakFlow, .25f, 1f, Time.time);
            if (flying && dt > 0f && !CaptureRig.NoVfx)
            {
                run.StreakCarry += dt * AtStreakDropRate;
                int count = (int)run.StreakCarry;
                run.StreakCarry -= count;
                if (count > 0) EmitAnchorThrowDrops(run.StreakDrops, run.Form, head - outward * .15f, head, -outward, count, 1.2f);
            }
            if (ageTail > .5f || ageHead > .45f)
            {
                run.StreakDone = true;
                Release(run.StreakFx);
                run.StreakFx = -1;
                run.StreakObject = null;
            }
        }

        /// <summary>Тень-линия: тонкая вода на земле под цепью от ног к голове — направление броска читается сверху.</summary>
        private void UpdateAtShadow(AnchorThrowRun run, in AnchorThrowState s, float shown)
        {
            if (!AtStill(run.ShadowFx, run.ShadowObject)) return;
            _atGroundBase = PlayerPosition().y;
            Vector3 a = run.Grip, b = run.Head;
            a.y = AnchorThrowGroundAt(a.x, a.z) + .03f;
            b.y = AnchorThrowGroundAt(b.x, b.z) + .03f;
            float from = run.RetractStart >= 0f ? run.RetractStart : s.CatchTick;
            float age = PelagAnchorThrowVfxRules.BreakAge(shown, from) * 1.4f;
            float half = .02f + .025f * PelagAnchorThrowVfxRules.Tension(s, shown);
            run.ShadowObject.transform.position = a;
            if (run.ShadowFilter != null)
                run.Shadow.Build(FormWaterMesh.MeshFor(run.ShadowFilter, PelagAbordageRibbon.MeshName), a, a, b, (a + b) * .5f + Vector3.up * 20f,
                    Vector3.down, 0f, half * .7f, half, age, age, run.SleeveFlow, .1f, .6f, Time.time);
            if (age > .55f) { Release(run.ShadowFx); run.ShadowFx = -1; run.ShadowObject = null; }
        }
    }
}
