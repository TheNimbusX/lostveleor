using Game.Sim;
using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    public sealed partial class PelagAnchorRig
    {
        private const float CorrectionSpanFrames = 4f, CorrectionLimit = .30f, TurnLimitDegrees = 60f;
        private const int AnimatorGraceFrames = 2;
        private bool _wreckOwned, _wreckShown;
        private int _wreckSerial, _rejectedSerial, _liveSegment = int.MinValue, _contactLoggedSegment = int.MinValue, _missLoggedSegment = int.MinValue;
        private int _turnLiveSegment = int.MinValue, _animMismatchFrames, _residualLoggedSegment = int.MinValue;
        private string _missingLogged;
        private bool _statesLogged;

        /// <summary>Снимок Крушения → вход рига. Реализация — PelagAnchorRig.WreckSnapshot.cs (читает Simulation.Wreck);
        /// без неё риг Крушение не берёт и остаётся прежний путь.</summary>
        static partial void ReadWreckSnapshot(Simulation sim, ref AnchorRigWreckInput input, ref bool ok);

        /// <summary>
        /// Зовёт CharacterAnimatorView.Tempo на каждом этапе Крушения вместо PelagAnchorSlamView.BeginWreck.
        /// true — серию ведёт риг; false — прежний путь: нет снимка Sim, нет запечек базовой серии, в контроллере нет
        /// состояний Wreck2_* (запечка одного клипа при руке из другого — брак), серия уже отдана прежнему пути, выключено.
        /// </summary>
        public bool ClaimWreck()
        {
            if (!AnchorRigSwitches.Wreck || !_started) return false;
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return false;
            var input = default(AnchorRigWreckInput);
            bool ok = false;
            ReadWreckSnapshot(sim, ref input, ref ok);
            if (!ok || input.Serial <= 0 || input.Phase == AnchorRigWreckInput.PhaseNone) return false;
            if (input.Serial == _rejectedSerial) return false;
            if (_wreckOwned && _owned && input.Serial == _wreckSerial) return true;
            if (!_library.HasAll(AnchorRigWreckPlan.RequiredClips))
            {
                if (_missingLogged == null) { _missingLogged = "base"; Debug.Log($"{Log} Крушение на прежнем пути: нет запечек {string.Join(", ", AnchorRigWreckPlan.RequiredClips)}"); }
                return false;
            }
            if (!ControllerHasWreckStates())
            {
                if (!_statesLogged) { _statesLogged = true; Debug.Log($"{Log} Крушение на прежнем пути: в контроллере нет состояний {AnchorRigWreckPlan.StateName(AnchorRigWreckPlan.Swing1)}…"); }
                return false;
            }
            if (!Claim("wreck")) return false;
            _wreckOwned = true; _wreckShown = false; _wreckSerial = input.Serial; _line.Active = false; _catching = false; _animMismatchFrames = 0;
            return true;
        }

        private bool ControllerHasWreckStates()
        {
            if (_animator == null) return false;
            foreach (var clip in AnchorRigWreckPlan.RequiredClips)
                if (!_animator.HasState(0, Animator.StringToHash(AnchorRigWreckPlan.StateName(clip)))) return false;
            return true;
        }

        /// <summary>Аниматор (слой 0) сейчас играет состояние этого клипа или переходит в него.</summary>
        private bool AnimatorPlays(string clip)
        {
            if (_animator == null) return false;
            int hash = Animator.StringToHash(AnchorRigWreckPlan.StateName(clip));
            if (_animator.GetCurrentAnimatorStateInfo(0).fullPathHash == hash) return true;
            return _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).fullPathHash == hash;
        }

        private Directive WreckDirective(Simulation sim, Vector3 grip)
        {
            var input = default(AnchorRigWreckInput);
            bool ok = false;
            ReadWreckSnapshot(sim, ref input, ref ok);
            if (!ok || input.Serial != _wreckSerial) { _wreckOwned = false; return new Directive { Mode = AnchorRigMode.Stow, Note = "series-gone" }; }
            AnchorWreckBeat beat = AnchorRigWreckPlan.Decide(input);
            switch (beat.Kind)
            {
                case AnchorWreckBeatKind.Bake:
                case AnchorWreckBeatKind.Loop:
                    if (!AnimatorPlays(beat.Clip))
                    {
                        if (++_animMismatchFrames > AnimatorGraceFrames)
                        {
                            // Рука играет не тот клип, что запечён: в первые кадры серии отдаём её прежнему пути целиком,
                            // позже (голова уже шла по запечке) — живой маятник от руки.
                            if (!_wreckShown && _slam != null) { AbandonWreck(beat.Clip); return new Directive { Mode = AnchorRigMode.External }; }
                            if (_animMismatchFrames == AnimatorGraceFrames + 1)
                                Debug.Log($"{Log} аниматор не играет {AnchorRigWreckPlan.StateName(beat.Clip)}: голова живая");
                            return new Directive { Mode = AnchorRigMode.InHandLive, Segment = beat.Segment, Note = "anim-mismatch" };
                        }
                    }
                    else _animMismatchFrames = 0;
                    return BakeDirective(input, beat, grip);
                case AnchorWreckBeatKind.Live:
                    if (beat.SideMismatch && _missLoggedSegment != beat.Segment && CaptureRig.LiveSkill)
                    { _missLoggedSegment = beat.Segment; Debug.Log($"{Log} side mismatch stage={input.Stage} side={input.Side}"); }
                    return new Directive { Mode = AnchorRigMode.InHandLive, Note = "live-stage" };
                default:
                    _wreckOwned = false;
                    return new Directive { Mode = AnchorRigMode.Stow, Note = "series-end" };
            }
        }

        /// <summary>Отдать серию прежнему PelagAnchorSlamView.BeginWreck с головой там, где она есть (BeginWreck наследует позу).</summary>
        private void AbandonWreck(string clip)
        {
            Transform head = _equipment.SlamHead;
            Vector3 p = head.position; Quaternion q = head.rotation;
            _rejectedSerial = _wreckSerial;
            _owned = false; ClearSkillState();
            HideChain();
            _equipment.SetRigOwnership(false, endUse: false);
            head.SetPositionAndRotation(p, q);
            _core.MarkExternal();
            Debug.Log($"{Log} Крушение отдано прежнему пути: аниматор не играет {AnchorRigWreckPlan.StateName(clip)}");
            _slam.BeginWreck();
        }

        private Directive BakeDirective(in AnchorRigWreckInput input, in AnchorWreckBeat beat, Vector3 grip)
        {
            var live = new Directive { Mode = AnchorRigMode.InHandLive, Segment = beat.Segment };
            if (_liveSegment == beat.Segment) { live.Note = "after-bake"; return live; }
            float frame, rate;
            int variant = -1;
            AnchorBake bake;
            bool exact;
            if (beat.Kind == AnchorWreckBeatKind.Loop)
            {
                bake = _library.Find(beat.Clip, 0, -1, out exact);
                if (bake == null || !bake.Looped) return Missing(beat.Clip, live);
                frame = bake.LoopFrom + AnchorRigWreckPlan.ChargeLoopFrame(input, _shown, bake.LoopTo - bake.LoopFrom, out rate);
            }
            else if (beat.Clip == AnchorRigWreckPlan.ChargeRelease)
            {
                AnchorBake loop = _library.Find(AnchorRigWreckPlan.ChargeLoop, 0, -1, out _);
                AnchorBake first = _library.Find(beat.Clip, 0, 0, out _);
                if (loop == null || first == null || !loop.Looped) return Missing(beat.Clip, live);
                variant = AnchorRigWreckPlan.ReleaseVariant(input, first.ContactFrame, loop.LoopTo - loop.LoopFrom, out float startTick);
                bake = _library.Find(beat.Clip, 0, variant, out exact) ?? first;
                frame = Mathf.Max(0f, _shown - startTick); rate = 1f;
            }
            else
            {
                bake = _library.Find(beat.Clip, beat.WindupTicks, -1, out exact);
                if (bake == null) return Missing(beat.Clip, live);
                frame = AnchorBakeClock.Frame(_shown, beat.StartTick, beat.ContactTick, bake.ContactFrame, beat.OverheadTick, bake.OverheadFrame, out rate);
            }
            if (bake.SheathFrame > 0f) { _sheathBakeFrame = bake.SheathFrame; _sheathBakeNow = frame; }
            if (!bake.Looped && frame >= bake.LiveFrom - 1e-3f)
            {
                // Конец запечки (или касание земли у удара оземь): голова уходит в живую физику с той же скоростью.
                _liveSegment = beat.Segment; live.Note = "bake-end"; return live;
            }
            // Разворот корня больше 60° (DESIGN §2.4): до контакт − 4 хват ведёт голову сам, потом вход в запечку со сроком.
            if (beat.Kind == AnchorWreckBeatKind.Bake && _shown < beat.ContactTick - 4)
            {
                Vector3 dir = new Vector3(input.Direction.X, 0f, input.Direction.Y);
                if (_turnLiveSegment == beat.Segment || (dir.sqrMagnitude > .5f && Vector3.Angle(_rootRotation * Vector3.forward, dir) > TurnLimitDegrees))
                { _turnLiveSegment = beat.Segment; live.Note = "turning"; return live; }
            }
            AnchorPose local = bake.Sample(frame, out bool taut, out NV gripLocal);
            AnchorPose world = AnchorBake.ToWorld(local, N(_rootPosition), NQ_(_rootRotation), N(_rootVelocity), N(_rootAngularVelocity), _bodyScale, rate);
            if (beat.CorrectContact && input.HasImpact && bake.ContactFrame > 0) Correct(ref world, input, bake, frame, rate);
            float deadline = beat.Kind == AnchorWreckBeatKind.Bake && _shown < beat.ContactTick - 1
                ? (beat.ContactTick - 1 - _shown) / AnchorBakeClock.TicksPerSecond : -1f;
            _wreckShown = true;
            return new Directive
            {
                Mode = AnchorRigMode.Baked, Target = world, Segment = beat.Segment, Deadline = deadline,
                Taut = taut, Tension = bake.TensionAt(frame), Note = beat.Clip + (exact ? "" : "~") + (variant >= 0 ? "_p" + variant : ""),
                HasBakeGrip = true, BakeGrip = _rootPosition + _rootRotation * (U(gripLocal) * _bodyScale),
                ContactCheck = beat.Kind == AnchorWreckBeatKind.Bake && _contactLoggedSegment != beat.Segment && _shown >= beat.ContactTick,
                Frame = frame, Variant = variant, Exact = exact, Impact = beat.CorrectContact && input.HasImpact,
                ImpactPoint = new Vector2(input.ImpactPoint.X, input.ImpactPoint.Y), Direction = new Vector3(input.Direction.X, 0f, input.Direction.Y),
            };
        }

        /// <summary>
        /// Удар оземь (DESIGN §2.3): смещение Δ = точка Sim (на высоте земли арены) − контакт запечки в мире, |Δ| ≤ 0,30 м,
        /// вносится пятистепенным окном за 4 кадра до контакта: ускорение не скачет, к кадру контакта Δ внесено целиком.
        /// </summary>
        private void Correct(ref AnchorPose world, in AnchorRigWreckInput input, AnchorBake bake, float frame, float rate)
        {
            float weight = AnchorBakeClock.CorrectionWeight(frame, bake.ContactFrame, CorrectionSpanFrames, out float slope);
            if (weight <= 0f) return;
            Vector3 bakeContact = _rootPosition + _rootRotation * (U(bake.ContactPoint) * _bodyScale);
            float ground = _layout != null ? _layout.WeaponGroundHeight(input.ImpactPoint.X, input.ImpactPoint.Y) : _rootPosition.y;
            Vector3 target = new Vector3(input.ImpactPoint.X, bakeContact.y + (ground - _rootPosition.y), input.ImpactPoint.Y);
            Vector3 delta = target - bakeContact;
            if (delta.magnitude > CorrectionLimit)
            {
                if (_missLoggedSegment != input.Serial * 16 + input.Stage * 2 && CaptureRig.LiveSkill)
                { _missLoggedSegment = input.Serial * 16 + input.Stage * 2; Debug.Log($"{Log} contact miss delta={delta.magnitude:F2} clip={bake.Clip}"); }
                delta = delta.normalized * CorrectionLimit;
            }
            world.Position += N(delta * weight);
            world.Velocity += N(delta * (slope * rate * AnchorBakeClock.TicksPerSecond));
        }

        private Directive Missing(string clip, Directive live)
        {
            if (_missingLogged != clip) { _missingLogged = clip; Debug.Log($"{Log} нет запечки {clip}: голова живая"); }
            live.Note = "no-bake";
            return live;
        }

        /// <summary>
        /// Проверка §5 #5 в игре — по ПОКАЗАННОЙ голове после сшивки (не по цели запечки): махи — дуга от корня,
        /// оземь — до точки Sim; плюс остаток стыка, ускорение поправки и зазор хвата запечки с кистью (§5 #1, #8, #9).
        /// </summary>
        private void LogContact(in Directive d, Vector3 grip)
        {
            _contactLoggedSegment = d.Segment;
            if (!CaptureRig.LiveSkill) return;
            if (_core.DeadlineResidual > 0f && _residualLoggedSegment != d.Segment)
            { _residualLoggedSegment = d.Segment; Debug.Log($"{Log} seam residual {_core.DeadlineResidual:F3} m at contact (accel limit {AnchorBlend.MaxAccel:F0} m/s²) clip={d.Note}"); }
            Vector3 head = U(_core.Output.Position), flat = head - _rootPosition; flat.y = 0f;
            string where = d.Impact
                ? $"toImpact={Vector2.Distance(new Vector2(head.x, head.z), d.ImpactPoint):F3}"
                : $"reach={flat.magnitude:F3} height={head.y - _rootPosition.y:F3} angle={Vector3.Angle(flat, d.Direction):F1}";
            Debug.Log($"{Log} contact clip={d.Note} exact={d.Exact} variant={d.Variant} frame={d.Frame:F2} shownSpeed={_core.Output.Velocity.Length():F2} {where} blend={_core.BlendError:F3} accel={_core.CorrectionAccel:F0} gripGap={Vector3.Distance(d.BakeGrip, grip):F3}");
        }
    }
}
