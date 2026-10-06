using Game.Sim;
using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    /// <summary>
    /// Кадр броска для рига в мире: кольцо головы на линии <c>Origin + Direction · Along</c>, Along и его скорость — от Sim
    /// (<see cref="AnchorRigThrowPlan"/>), не своя кривая.
    /// </summary>
    public struct AnchorLineFrame
    {
        public int Serial;
        public AnchorLinePhase Phase;
        public Vector3 Origin, Direction;
        public float Along, AlongSpeed;
        public bool Reached;
        /// <summary>Замах: путь запечки (Throw_Windup) и его кадр; null — голова в правом кулаке (как принятый замах Абордажа v2).</summary>
        public string WindupClip;
        public float WindupFrame;
    }

    public sealed partial class PelagAnchorRig
    {
        private const float JerkAmplitude = .15f, AbordageCatchSeconds = .25f;
        private struct LineState { public bool Active; public AnchorLineFrame Frame; public int Serial; public bool Jerked; }
        private LineState _line;
        private bool _catching, _catchFromAbordage;
        private int _lineDoneSerial;
        private Vector3 _previousFist;
        private bool _hasFist;

        /// <summary>
        /// Снимок Броска якоря → кадр рига. Реализация — PelagAnchorRig.ThrowSnapshot.cs, кладётся, когда в Sim есть
        /// AnchorThrowState (спека Броска §2.6); без неё кормушка молчит, а Бросок идёт своим временным путём.
        /// </summary>
        static partial void ReadThrowSnapshot(Simulation sim, float shown, ref AnchorRigThrowInput input, ref bool ok);

        /// <summary>Кормушка Броска: каждый кадр до решения рига; берёт голову в кадр каста, отпускает после ловли.</summary>
        private void FeedThrow(Simulation sim)
        {
            if (!AnchorRigSwitches.AnchorThrow) return;
            var input = default(AnchorRigThrowInput);
            bool ok = false;
            float shown = PelagAbordageVfxRules.ShownTick(sim.Tick, _driver.Alpha);
            ReadThrowSnapshot(sim, shown, ref input, ref ok);
            if (!ok || input.Serial <= 0 || input.Serial == _lineDoneSerial) return;
            if (!AnchorRigThrowPlan.Frame(input, shown, out AnchorLineState state)) return;
            if (!_line.Active || _line.Serial != input.Serial)
            {
                if (state.Phase == AnchorLinePhase.Catch || state.Phase == AnchorLinePhase.Exit) return;
                if (!BeginLine(input.Serial)) return;
            }
            float height = _equipment.ChainGripPosition.y;
            DriveLine(new AnchorLineFrame
            {
                Serial = input.Serial, Phase = state.Phase, Along = state.Along, AlongSpeed = state.AlongSpeed, Reached = state.Reached,
                Origin = new Vector3(input.Origin.X, height, input.Origin.Y), Direction = new Vector3(input.Direction.X, 0f, input.Direction.Y),
                WindupFrame = state.WindupFrame, WindupClip = _library.HasClip(ThrowWindupClip) ? ThrowWindupClip : null,
            });
        }

        private const string ThrowWindupClip = "Pelag_AN_AnchorThrow_Windup";

        /// <summary>Начать бросок на риге (кормушка навыка зовёт в кадр каста). false — риг занят чужим или выключен.</summary>
        public bool BeginLine(int serial)
        {
            if (!Claim("line")) return false;
            _wreckOwned = false; _catching = false; _hasFist = false;
            _line = new LineState { Active = true, Serial = serial };
            return true;
        }

        /// <summary>Кормушка броска подаёт кадр до решения рига.</summary>
        public void DriveLine(in AnchorLineFrame frame)
        {
            if (!_line.Active || frame.Serial != _line.Serial) return;
            _line.Frame = frame;
        }

        private Directive LineDirective(float dt)
        {
            AnchorLineFrame f = _line.Frame;
            switch (f.Phase)
            {
                case AnchorLinePhase.Windup:
                    AnchorBake bake = f.WindupClip != null ? _library.Find(f.WindupClip, 0, -1, out _) : null;
                    if (bake == null) return FistDirective(dt);
                    AnchorPose local = bake.Sample(f.WindupFrame, out bool taut, out _);
                    return new Directive
                    {
                        Mode = AnchorRigMode.Baked, Segment = -1001, Taut = taut, Tension = bake.TensionAt(f.WindupFrame), Deadline = -1f,
                        Target = AnchorBake.ToWorld(local, N(_rootPosition), NQ_(_rootRotation), N(_rootVelocity), N(_rootAngularVelocity), _bodyScale),
                        Note = "throw-windup",
                    };
                case AnchorLinePhase.Flight:
                case AnchorLinePhase.Taut:
                case AnchorLinePhase.Return:
                    var mode = f.Phase == AnchorLinePhase.Return ? AnchorRigMode.Yank : AnchorRigMode.Thrown;
                    var d = new Directive { Mode = mode, Segment = mode == AnchorRigMode.Yank ? -1004 : -1002, Taut = true, Tension = 2000f, Deadline = -1f, Target = LineTarget(f) };
                    if (f.Reached && !_line.Jerked && _core.Mode == AnchorRigMode.Thrown)
                    {
                        _line.Jerked = true; _kickTime = Time.time;
                        _core.Kick(N(-f.Direction.normalized * _core.KickSpeedFor(JerkAmplitude * _bodyScale)));
                    }
                    d.Note = f.Phase.ToString();
                    return d;
                case AnchorLinePhase.Catch:
                case AnchorLinePhase.Exit:
                    _lineDoneSerial = _line.Serial;
                    _line.Active = false; _catching = true; _caughtAge = 0f; _catchFromAbordage = false;
                    return new Directive { Mode = AnchorRigMode.Caught, Note = "caught" };
                default:
                    _line.Active = false;
                    return new Directive { Mode = AnchorRigMode.Stow, Note = "line-none" };
            }
        }

        /// <summary>
        /// Замах Броска без запечки (DESIGN §6.2): голова в правом кулаке, как принятый замах Абордажа v2
        /// (PelagEquipmentView.AnchorThrowPosition), 2 тика; в тик выпуска — полёт по линии Sim со сшивкой.
        /// </summary>
        private Directive FistDirective(float dt)
        {
            Vector3 fist = _equipment.AnchorThrowPosition;
            Vector3 forward = _rHand != null && _rForeArm != null && (_rHand.position - _rForeArm.position).sqrMagnitude > 1e-6f
                ? (_rHand.position - _rForeArm.position).normalized : transform.forward;
            Vector3 velocity = _hasFist && dt > 1e-5f ? (fist - _previousFist) / dt : Vector3.zero;
            _previousFist = fist; _hasFist = true;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            return new Directive
            {
                Mode = AnchorRigMode.Baked, Segment = -1001, Taut = false, Deadline = -1f, Note = "throw-fist",
                Target = new AnchorPose(N(fist + rotation * _headCenterLocal), NQ_(rotation), N(velocity), NV.Zero),
            };
        }

        /// <summary>Голова на линии: кольцо в точке Along, тело головы вперёд по полёту, ось кольца — назад к хвату.</summary>
        private AnchorPose LineTarget(in AnchorLineFrame f)
        {
            Vector3 dir = f.Direction.sqrMagnitude > 1e-6f ? f.Direction.normalized : transform.forward;
            NV eyeLocal = _core.Body.EyeLocal;
            float eyeDistance = eyeLocal.Length();
            Vector3 ring = f.Origin + dir * f.Along;
            var rotation = AnchorRigMath.Align(eyeLocal, N(-dir), NV.UnitZ, NV.UnitY);
            return new AnchorPose(N(ring + dir * eyeDistance), rotation, N(dir * f.AlongSpeed), NV.Zero);
        }

        /// <summary>Поймал: живой маятник, цепь выбирается до L; через CaughtSeconds — уборка.</summary>
        private Directive CatchDirective()
        {
            _caughtAge += Time.deltaTime;
            float limit = _catchFromAbordage ? AbordageCatchSeconds : CaughtSeconds;
            if (_caughtAge < limit) return new Directive { Mode = AnchorRigMode.Caught, Note = "caught" };
            _catching = false; _catchFromAbordage = false;
            return new Directive { Mode = AnchorRigMode.Stow, Note = "caught-stow" };
        }

        /// <summary>
        /// Абордаж (за флагом <see cref="AnchorRigSwitches.AbordageReturn"/>, по умолчанию выключено — прежний ReturnFlyingAnchor):
        /// голову, вернувшуюся к руке, ловит риг с её положением и скоростью, дальше маятник и уборка.
        /// </summary>
        public bool TryCatchReturning(Vector3 headPosition, Quaternion headRotation, Vector3 velocity)
        {
            if (!AnchorRigSwitches.AbordageReturn || !_started) return false;
            if (_slam != null && _slam.Active) return false;
            _catchFromAbordage = true;
            if (!Claim("abordage-return")) { _catchFromAbordage = false; return false; }
            _gripToHandPending = false;
            var pose = new AnchorPose(N(headPosition + headRotation * _headCenterLocal), NQ_(headRotation), N(velocity), NV.Zero);
            _core.Teleport(AnchorRigMode.Yank, pose);
            _equipment.SlamHead.SetPositionAndRotation(headPosition, headRotation);
            _wreckOwned = false; _line.Active = false; _catching = true; _caughtAge = 0f;
            return true;
        }
    }
}
