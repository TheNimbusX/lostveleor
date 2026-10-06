using Game.Sim;
using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    public sealed partial class PelagAnchorRig
    {
        /// <summary>Что риг показывает в этом кадре (собирают кормушки навыков, исполняет <see cref="Apply"/>).</summary>
        private struct Directive
        {
            public AnchorRigMode Mode;
            public AnchorPose Target;
            public int Segment;
            public float Deadline;
            public bool Taut;
            public float Tension;
            public string Note;
            /// <summary>Точка хвата запечки в мире (сверка с кистью в игре, §5 #1).</summary>
            public bool HasBakeGrip;
            public Vector3 BakeGrip;
            /// <summary>Тик контакта пройден: после Apply записать в лог ПОКАЗАННУЮ голову (§5 #5).</summary>
            public bool ContactCheck;
            public float Frame;
            public int Variant;
            public bool Exact, Impact;
            public Vector2 ImpactPoint;
            public Vector3 Direction;
        }

        private enum StowStep : byte { None, Hand, Reel, Handoff }
        private const float StowTimeout = .35f, StowHandDistance = .04f, CaughtSeconds = .3f;
        private const float StowReelSpeed = 5f, StowCatchDistance = .10f, StowCatchSpeed = 2.5f, StowReelLimit = .9f;
        private StowStep _stow;
        private float _stowAge, _caughtAge;
        private bool _gripToHandPending;
        private int _claimFrame;
        private long _costTicks, _costPeak;
        private int _costFrames;
        private float _accelMax, _gripGapMax, _kickTime = -1f;
        private float _sheathBakeFrame = -1f, _sheathBakeNow = -1f;

        /// <summary>Взять голову и цепь: рукоять — в левую (скольжением), сабля — за кушак (в кадр ножен или скольжением), голова — своя.</summary>
        private bool Claim(string why)
        {
            if (!_started) return false;
            if (_owned)
            {
                // Новое нажатие во время уборки: рукоять уже ушла на спину — снова в руку тем же скольжением, цепь снова L.
                _stow = StowStep.None;
                _core.ResetCable();
                if (!_equipment.GripInHand && !_equipment.GripSliding) { _gripToHandPending = true; _claimFrame = Time.frameCount; }
                return true;
            }
            AnchorPose start = default;
            bool inherit = _slam != null && _slam.Active;
            if (inherit)
            {
                // Прежний путь ещё возвращает голову: забираем её с того места, где она есть, без скачка.
                if (!_geometry) EnsureGeometry();
                start = CurrentHeadPose();
                _slam.Release(true);
            }
            _equipment.SetRigOwnership(true);
            EnsureGeometry();
            if (!inherit) start = _core.Mode == AnchorRigMode.External ? BackTarget(0f) : _core.Output;
            _core.Teleport(AnchorRigMode.OnBack, start);
            _owned = true; _stow = StowStep.None; _gripToHandPending = true; _claimFrame = Time.frameCount;
            _hasPrevious = false; _draw.Reset(); _chainVisible = true;
            _costTicks = _costPeak = 0; _costFrames = 0; _accelMax = _gripGapMax = 0f;
            _sheathBakeFrame = _sheathBakeNow = -1f;
            if (CaptureRig.LiveSkill) Debug.Log($"{Log} claim {why} inherit={inherit}");
            return true;
        }

        /// <summary>Отдать всё: голова на спине (сшита), рукоять на спине, сабля — штатным жестом эквипа.</summary>
        private void Release(string why, bool endUse = true)
        {
            if (!_owned) return;
            _owned = false; ClearSkillState();
            HideChain();
            _equipment.SetRigOwnership(false, endUse);
            _core.MarkExternal();
            if (CaptureRig.LiveSkill)
                Debug.Log($"{Log} release {why} meanMs={(_costFrames > 0 ? _costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames : 0):F3} peakMs={_costPeak * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F3} bounces={_core.Bounces} correctionAccelMax={_accelMax:F0} gripGapMax={_gripGapMax:F3}");
        }

        private void ClearSkillState()
        {
            _wreckOwned = false; _line.Active = false; _catching = false; _catchFromAbordage = false; _stow = StowStep.None;
            _core.ResetCable();
        }

        /// <summary>
        /// Голову взял чужой код. Голова остаётся, где была в этом кадре (SetRigOwnership кладёт её в позу спины — вернуть).
        /// Абордаж посреди навыка рига: как прежний путь — голова уходит на спину возвратом ReturnFlyingAnchor за 0,14 с
        /// (принятый показ), а не телепортом. Старый Удар якорем сам забирает голову с места (BeginPhysicalSlam наследует позу).
        /// </summary>
        private void Yield(string why)
        {
            if (_owned)
            {
                Transform head = _equipment.SlamHead;
                Vector3 p = head.position; Quaternion q = head.rotation;
                _equipment.SetRigOwnership(false, endUse: false);
                head.SetPositionAndRotation(p, q);
                if (_slam != null && !_slam.Active && _presentation != null && _presentation.AbordageActive) _slam.ReturnFlyingAnchor(p, q);
            }
            _owned = false; ClearSkillState();
            HideChain();
            _core.MarkExternal();
            if (CaptureRig.LiveSkill) Debug.Log($"{Log} yield {why}");
        }

        /// <summary>Пул тела: мгновенно на спину (единственный разрешённый телепорт — тела нет на экране).</summary>
        public void ResetForSpawn()
        {
            _owned = false; ClearSkillState();
            HideChain();
            _core.MarkExternal();
            _hasPrevious = false;
        }

        private bool ForeignOwner()
        {
            if (_slam != null && _slam.Active) return true;
            return !_catchFromAbordage && _presentation != null && _presentation.AbordageActive;
        }

        private void LateUpdate()
        {
            if (!_started) return;
            var sim = _driver != null ? _driver.Sim : null;
            if (sim != null && !_driver.GameplayPaused) FeedThrow(sim);
            if (!_owned) return;
            if (sim == null || !sim.Entities.Alive[Simulation.PlayerId] || (_presentation != null && _presentation.IsDead))
            { ResetForSpawn(); _equipment.SetRigOwnership(false); return; }
            if (_driver.GameplayPaused) return;
            if (ForeignOwner()) { Yield("foreign"); return; }
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            _shown = PelagAbordageVfxRules.ShownTick(sim.Tick, _driver.Alpha);
            SampleRoot(dt);
            UpdateCapsules();
            Vector3 grip = GripWorld();
            if (!_hasPrevious) _previousGrip = grip;
            UpdateGripHandoff(grip);
            Directive d = _wreckOwned ? WreckDirective(sim, grip) : _line.Active ? LineDirective(dt) : _catching ? CatchDirective()
                : new Directive { Mode = AnchorRigMode.Stow };
            if (!_owned) return;   // Крушение отдано прежнему пути (аниматор играет не Wreck2_*)
            if (d.Mode == AnchorRigMode.Stow) d = StowDirective(dt, grip);
            if (!_owned) return;
            UpdateSaber();
            Apply(d, dt, grip);
            WriteHead();
            ChainGrip = grip;
            DrawChain(dt, grip, d);
            PressSupportHand();
            TrackChecks(d, grip);
            _previousGrip = grip;
            _hasPrevious = true;
            long cost = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _costTicks += cost; _costFrames++; if (cost > _costPeak) _costPeak = cost;
            if (CaptureRig.LiveSkill) LogFrame(sim, d, grip, cost);
            if (_stow == StowStep.Handoff && _core.BlendError < .003f && _stowAge > .1f) Release("stowed");
        }

        private void Apply(in Directive d, float dt, Vector3 grip)
        {
            if (AnchorRigCore.IsLive(d.Mode))
            {
                if (!_core.Live) _core.EnterLive(d.Mode, N(grip));
                else _core.Relabel(d.Mode);
                _core.StepLive(dt, N(_previousGrip), N(grip), Ground, _capsules, _capsuleCount);
                return;
            }
            _core.Drive(d.Mode, d.Segment, d.Target, dt, d.Deadline);
        }

        /// <summary>Рукоять со спины в левую: когда кисть клипа у крепления (≤ 4 см) или с кадра 1 скольжением за 2 кадра.</summary>
        private void UpdateGripHandoff(Vector3 grip)
        {
            if (!_gripToHandPending || _equipment.GripSliding) return;
            Vector3 hand = _equipment.ChainGripPosition;
            bool near = Vector3.Distance(hand, grip) <= StowHandDistance * _bodyScale;
            if (!near && Time.frameCount <= _claimFrame) return;
            _equipment.SlideGrip(toHand: true, near ? 1 : AnchorRigSwitches.PropSlideFrames);
            _gripToHandPending = false;
        }

        /// <summary>
        /// Сабля за кушак (DESIGN §4.3): кисть у ножен (≤ 12 см) — сразу; иначе в кадр sheathFrame запечки (timing.json клипа),
        /// а без него — с кадра 1 скольжением за 2 кадра. До этого сабля в руке, а не прыгает за кадр.
        /// </summary>
        private void UpdateSaber()
        {
            if (!_equipment.SaberPending) return;
            if (_equipment.SaberNearSheath(.12f * _bodyScale)) { _equipment.SheatheSaber(1); return; }
            bool due = _sheathBakeFrame > 0f ? _sheathBakeNow >= _sheathBakeFrame : Time.frameCount > _claimFrame;
            if (due) _equipment.SheatheSaber(AnchorRigSwitches.PropSlideFrames);
        }

        /// <summary>
        /// Уборка (DESIGN §1.2 Stow): живая физика. Hand — ждём кисть у крепления (клип Stow) или срок; рукоять на спину
        /// (у крепления — сразу, без клипа — скольжением 0,22 с, а не прыжком за 2 кадра). Reel — цепь сматывается к спине
        /// со скоростью руки: голову к креплению тянет натяжение, не пружина. Handoff — голова у крепления (≤ 10 см,
        /// ≤ 2,5 м/с): короткая сшивка в позу спины по положению и скорости.
        /// </summary>
        private Directive StowDirective(float dt, Vector3 grip)
        {
            if (_stow == StowStep.None) { _stow = StowStep.Hand; _stowAge = 0f; }
            _stowAge += dt;
            if (_stow == StowStep.Hand)
            {
                Vector3 back = _equipment.GripBackPosition(_gripExitLocal);
                bool near = Vector3.Distance(grip, back) <= StowHandDistance * _bodyScale;
                if (!near && _stowAge < StowTimeout) return new Directive { Mode = AnchorRigMode.Stow, Note = "stow-hand" };
                if (!near && CaptureRig.LiveSkill) Debug.Log($"{Log} stow without hand contact gap={Vector3.Distance(grip, back):F3} slide={AnchorRigSwitches.StowSlideSeconds:F2}s");
                if (near) _equipment.SlideGrip(toHand: false, 1);
                else _equipment.SlideGripSeconds(toHand: false, AnchorRigSwitches.StowSlideSeconds);
                _stow = StowStep.Reel; _stowAge = 0f;
                _core.HoldIgnoreTorso = true;
                _core.ReelTo(Vector3.Distance(back, BackRing()), StowReelSpeed * _bodyScale);
                return new Directive { Mode = AnchorRigMode.Stow, Note = "stow-reel" };
            }
            if (_stow == StowStep.Reel)
            {
                AnchorPose target = BackTarget(dt);
                float gap = Vector3.Distance(Ring, BackRing());
                float speed = Vector3.Distance(U(_core.Output.Velocity), U(target.Velocity));
                bool caught = gap <= StowCatchDistance * _bodyScale && speed <= StowCatchSpeed;
                if (!caught && _stowAge < StowReelLimit) return new Directive { Mode = AnchorRigMode.Stow, Note = "stow-reel" };
                if (!caught && CaptureRig.LiveSkill) Debug.Log($"{Log} stow reel timeout gap={gap:F3} speed={speed:F2}");
                _stow = StowStep.Handoff; _stowAge = 0f;
                return new Directive { Mode = AnchorRigMode.OnBack, Target = target, Segment = -2, Deadline = -1f, Note = "stow-back" };
            }
            return new Directive { Mode = AnchorRigMode.OnBack, Target = BackTarget(dt), Segment = -2, Deadline = -1f, Note = "stow-back" };
        }

        /// <summary>Кольцо головы в позе спины (мир).</summary>
        private Vector3 BackRing()
        {
            Quaternion rotation = _equipment.SlamBeltRotation;
            return _equipment.SlamBeltPosition + rotation * (_headCenterLocal + U(_core.Body.EyeLocal));
        }

        /// <summary>Проверки §5 в игре: ускорение поправки стыка (кроме дёрга броска), зазор хвата запечки и кисти, контакт.</summary>
        private void TrackChecks(in Directive d, Vector3 grip)
        {
            bool kicked = _kickTime >= 0f && Time.time - _kickTime < .15f;
            bool limited = _core.Mode == AnchorRigMode.Baked || _core.Mode == AnchorRigMode.OnBack;
            if (limited && !kicked) _accelMax = Mathf.Max(_accelMax, _core.CorrectionAccel);
            if (d.HasBakeGrip) _gripGapMax = Mathf.Max(_gripGapMax, Vector3.Distance(d.BakeGrip, grip));
            if (d.ContactCheck) LogContact(d, grip);
        }

        private void LogFrame(Simulation sim, in Directive d, Vector3 grip, long cost)
        {
            Vector3 ring = Ring;
            float span = Vector3.Distance(ring, grip);
            int heads = (_equipment.AnchorHeadVisible ? 1 : 0) + (_vfx != null ? _vfx.VisibleFlyingAnchors : 0);
            string gap = d.HasBakeGrip ? $" gripGap={Vector3.Distance(d.BakeGrip, grip):F3}" : "";
            Debug.Log($"{Log} tick={sim.Tick} shown={_shown:F2} mode={_core.Mode} seg={d.Segment} note={d.Note} head={U(_core.Output.Position).ToString("F3")} v={_core.Output.Velocity.Length():F2} span={span:F3} L={_core.CableLength:F3} taut={d.Taut || span >= _core.CableLength - .02f} line={_draw.UseLine} blend={_core.BlendError:F3} accel={_core.CorrectionAccel:F0} seam={_core.SeamJump:F3}/{_core.SeamVelocityJump:F2}{gap} heads={heads} ms={cost * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F3}");
        }
    }
}
