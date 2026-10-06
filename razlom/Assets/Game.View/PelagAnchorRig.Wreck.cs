using Game.Sim;
using UnityEngine;
using NV = System.Numerics.Vector3;

namespace Game.View
{
    /// <summary>
    /// Крушение v4 на риге (06.10): голова идёт по запечке ТОГО ЖЕ клипа и кадра, что играет тело
    /// (CharacterAnimatorView.TryGetWreck4Pose; Resources/Weapons/Pelag/AnchorBakes/Pelag_AN_Wreck4_*), выпад подводится
    /// к точке Sim, мах без следующего нажатия и пауза — живой маятник на короткой цепи (AnchorRigWreckPlan.GoesLive),
    /// уборка — запечка Stow до посадки на спину, потом сшивка в позу спины. Поворот головы — управляемый + вторичная
    /// пружина, цепь — симуляция (PelagAnchorRig.Whip). Абордаж и Бросок якоря этот файл не трогают.
    /// </summary>
    public sealed partial class PelagAnchorRig
    {
        private const float CorrectionSpanFrames = 4f;
        private bool _wreckOwned, _wreckShown;
        private int _wreckSerial, _rejectedSerial, _contactLoggedSegment = int.MinValue, _missLoggedSegment = int.MinValue;
        private string _missingLogged;
        private bool _statesLogged;
        private PelagWreckClip _liveClip = PelagWreckClip.None, _wreckClip = PelagWreckClip.None;
        private float _liveSince, _wreckFrame, _wreckRate;
        private AnchorBake _wreckBake;
        private bool _wreckLanded;

        /// <summary>Снимок Крушения → вход рига. Реализация — PelagAnchorRig.WreckSnapshot.cs (читает Simulation.Wreck).</summary>
        static partial void ReadWreckSnapshot(Simulation sim, ref AnchorRigWreckInput input, ref bool ok);

        /// <summary>Риг ведёт Крушение v4 (для цепи, головы и рукояти этого кадра).</summary>
        private bool WreckV4 => _wreckOwned && _wreckBake != null;

        /// <summary>
        /// Зовёт CharacterAnimatorView.Wreck2 на первом этапе серии. true — серию ведёт риг; false — прежний путь: нет
        /// снимка Sim, нет запечек v4, в контроллере нет состояний Wreck4_*, серия уже отдана прежнему пути, выключено.
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
            _wreckOwned = true; _wreckShown = false; _wreckSerial = input.Serial; _line.Active = false; _catching = false;
            // v4: рукоять — в обеих руках по запечке (PlaceWreckHandle), не в левую скольжением.
            _gripToHandPending = false;
            _core.Settings.ChainLength = AnchorRigWreckPlan.LiveCable * _bodyScale;
            _core.ResetCable();
            _liveClip = _wreckClip = PelagWreckClip.None;
            _wreckBake = null; _wreckLanded = false;
            ResetWhip();
            return true;
        }

        private bool ControllerHasWreckStates()
        {
            if (_animator == null) return false;
            foreach (var clip in AnchorRigWreckPlan.RequiredClips)
                if (!_animator.HasState(0, Animator.StringToHash(AnchorRigWreckPlan.StateName(clip)))) return false;
            return true;
        }

        /// <summary>Поза Крушения этого кадра от вида тела: клип, кадр, темп, запечка. false — лента не ведёт тело.</summary>
        private bool ReadWreckPose()
        {
            _wreckBake = null;
            if (_presentation == null || !_presentation.TryGetWreck4Pose(out PelagWreckClip clip, out float frame, out float rate,
                    out bool next, out int serial) || serial != _wreckSerial) return false;
            AnchorBake bake = _library.Find(PelagWreckClipRules.ClipName(clip), 0, -1, out _);
            if (bake == null) return false;
            _wreckBake = bake; _wreckFrame = frame; _wreckRate = Mathf.Max(.05f, rate); _wreckNext = next;
            if (clip != _wreckClip)
            {
                // Стык без смешивания тела (догон, Draw→Swing1, Lunge→Stow): путь запечек непрерывен — сшивка не пересевается.
                // Скорость прошлого кадра против цели этого кадра на хлёсте — до 50 м/с разницы: при пределе 400 м/с² сшивка
                // уносила голову на 3–5 м (съёмка 06.10, reach=3,8/5,1 в контакт махов). Новый отрезок — только когда тело смешивает.
                if (_wreckClip == PelagWreckClip.None || _presentation.Wreck4EntryBlendTicks > 0f) _wreckSegment++;
                _wreckClip = clip;
            }
            return true;
        }

        private bool _wreckNext;
        private int _wreckSegment;

        private Directive WreckDirective(Simulation sim, Vector3 grip)
        {
            var input = default(AnchorRigWreckInput);
            bool ok = false;
            ReadWreckSnapshot(sim, ref input, ref ok);
            if (_wreckBake == null)
            {
                // Уборка доиграна (вид снял ленту в кадре Stow@8): голова уже на креплении — сшивка в позу спины и отдача.
                if (_wreckLanded) return new Directive { Mode = AnchorRigMode.OnBack, Target = BackTarget(Time.deltaTime), Segment = -2, Deadline = -1f, Note = "stow-back", Settle = true };
                // Лента больше не ведёт тело (срыв, смерть, другой навык): живая физика и уборка на спину.
                _wreckOwned = false;
                return new Directive { Mode = AnchorRigMode.Stow, Note = "series-gone" };
            }
            string name = PelagWreckClipRules.ClipName(_wreckClip);
            AnchorBake bake = _wreckBake;
            float frame = _wreckFrame, rate = _wreckRate;
            if (_wreckClip == PelagWreckClip.Draw) { _sheathBakeFrame = bake.SheathFrame; _sheathBakeNow = frame; }
            else if (_sheathBakeFrame > 0f) _sheathBakeNow = float.MaxValue;
            int segment = _wreckSerial * 64 + _wreckSegment * 2;
            bool live = _liveClip == _wreckClip || AnchorRigWreckPlan.GoesLive(name, frame, bake.LiveFrom, _wreckNext);
            if (live)
            {
                if (_liveClip != _wreckClip) { _liveClip = _wreckClip; _liveSince = Time.time; }
                return new Directive { Mode = AnchorRigMode.InHandLive, Segment = segment + 1, Note = "hang " + _wreckClip };
            }
            _liveClip = PelagWreckClip.None;
            float land = bake.Phases.land > 0 ? bake.Phases.land : 5.5f;
            if (_wreckClip == PelagWreckClip.Stow && frame >= land)
            {
                // Голова села на крепление (запечка: посадка 1,5 м/с): дальше — поза спины игры, сшивкой ТОЛЬКО по положению.
                // Скорость крепления — от этого кадра: прошлый замер был в кадре взятия серии (выпад сдвинул тело на 0,6 м —
                // 36 м/с ложной скорости уносили голову на 4 м за спину, съёмка 06.10).
                if (!_wreckLanded) PrimeBackTarget();
                _wreckLanded = true;
                return new Directive { Mode = AnchorRigMode.OnBack, Target = BackTarget(Time.deltaTime), Segment = -2, Deadline = -1f, Note = "stow-back", Settle = true };
            }
            AnchorPose local = bake.Sample(frame, out bool taut, out NV gripLocal);
            AnchorPose world = AnchorBake.ToWorld(local, N(_rootPosition), NQ_(_rootRotation), N(_rootVelocity), N(_rootAngularVelocity), _bodyScale, rate);
            float contact = bake.ContactFrame;
            if (_wreckClip == PelagWreckClip.Lunge && input.HasImpact && contact > 0) CorrectLunge(ref world, input, bake, frame, rate);
            float deadline = contact > 0 && frame < contact - 1f ? (contact - 1f - frame) / rate / AnchorBakeClock.TicksPerSecond : -1f;
            _wreckShown = true;
            return new Directive
            {
                Mode = AnchorRigMode.Baked, Target = world, Segment = segment, Deadline = deadline,
                Taut = taut, Tension = 0f, Note = name,
                HasBakeGrip = true, BakeGrip = _rootPosition + _rootRotation * (U(gripLocal) * _bodyScale),
                ContactCheck = contact > 0 && _contactLoggedSegment != segment && frame >= contact,
                Frame = frame, Variant = -1, Exact = true, Impact = _wreckClip == PelagWreckClip.Lunge && input.HasImpact,
                ImpactPoint = new Vector2(input.ImpactPoint.X, input.ImpactPoint.Y), Direction = new Vector3(input.Direction.X, 0f, input.Direction.Y),
            };
        }

        /// <summary>
        /// Выпад: голова подводится к точке Sim (на высоте земли арены) — пятистепенно с выпуска до удара, держится в
        /// воронке, гаснет за выбор цепи (AnchorRigWreckPlan.ImpactWeight); предел — ImpactCorrectionLimit (стена ближе 2,2 м).
        /// </summary>
        private void CorrectLunge(ref AnchorPose world, in AnchorRigWreckInput input, AnchorBake bake, float frame, float rate)
        {
            const float h = .05f;
            float weight = AnchorRigWreckPlan.ImpactWeight(frame, bake.ContactFrame, bake.Phases);
            if (weight <= 0f) return;
            float slope = (AnchorRigWreckPlan.ImpactWeight(frame + h, bake.ContactFrame, bake.Phases) - weight) / h;
            Vector3 bakeContact = _rootPosition + _rootRotation * (U(bake.ContactPoint) * _bodyScale);
            float ground = _layout != null ? _layout.WeaponGroundHeight(input.ImpactPoint.X, input.ImpactPoint.Y) : _rootPosition.y;
            Vector3 target = new Vector3(input.ImpactPoint.X, bakeContact.y + (ground - _rootPosition.y), input.ImpactPoint.Y);
            Vector3 delta = target - bakeContact;
            float limit = AnchorRigWreckPlan.ImpactCorrectionLimit * _bodyScale;
            if (delta.magnitude > limit)
            {
                if (_missLoggedSegment != input.Serial && CaptureRig.LiveSkill)
                { _missLoggedSegment = input.Serial; Debug.Log($"{Log} lunge miss delta={delta.magnitude:F2}"); }
                delta = delta.normalized * limit;
            }
            world.Position += N(delta * weight);
            world.Velocity += N(delta * (slope * rate * AnchorBakeClock.TicksPerSecond));
        }

        /// <summary>Запас к длине цепи для предела досягаемости, м при росте 1,82 (вторичный поворот, зазор кисти).</summary>
        private const float ReachSlack = .10f;
        private int _reachClamps;
        private float _reachOverMax;

        /// <summary>
        /// Крушение v4: кольцо не дальше длины цепи от хвата — по запечке (cable кадра) плюс зазор хвата запечки и кисти игры;
        /// после посадки на спину — короткая цепь маха. Живой маятник держит цепь сам (ConstrainCable).
        /// </summary>
        private void LimitWreckReach(in Directive d, Vector3 grip)
        {
            float cable = _wreckBake != null && _wreckBake.HasCable ? _wreckBake.CableAt(_wreckFrame) : AnchorRigWreckPlan.WhipSwing;
            float limit = (Mathf.Max(cable, AnchorRigWreckPlan.WhipSwing) + ReachSlack) * _bodyScale;
            if (d.HasBakeGrip) limit += Vector3.Distance(d.BakeGrip, grip);
            Vector3 eye = U(_core.Body.Eye);
            float over = Vector3.Distance(eye, grip) - limit;
            if (!_core.LimitReach(N(grip), limit)) return;
            _reachClamps++;
            _reachOverMax = Mathf.Max(_reachOverMax, over);
        }

        /// <summary>Скорость позы спины считать с этого кадра (прошлый замер устарел).</summary>
        private void PrimeBackTarget()
        {
            Quaternion rotation = _equipment.SlamBeltRotation;
            _previousBackCenter = _equipment.SlamBeltPosition + rotation * _headCenterLocal;
        }

        private Directive Missing(string clip, Directive live)
        {
            if (_missingLogged != clip) { _missingLogged = clip; Debug.Log($"{Log} нет запечки {clip}: голова живая"); }
            live.Note = "no-bake";
            return live;
        }

        /// <summary>Уборка v4 доиграна: голова на спине, рукоять на креплении — риг отдаёт всё.</summary>
        private bool WreckStowDone => _wreckLanded && (_wreckBake == null || _wreckClip == PelagWreckClip.Stow && _wreckFrame >= PelagWreckClipRules.StowLast - .01f);

        /// <summary>Вид досмотрел уборку (Stow@8) — совместимость с прежним вызовом; v4 сажает рукоять по кадру сам.</summary>
        public void CueWreckStow(int serial) { }

        /// <summary>Проверка в игре — ПОКАЗАННАЯ голова в кадр контакта: махи — дуга от корня, выпад — до точки Sim.</summary>
        private void LogContact(in Directive d, Vector3 grip)
        {
            _contactLoggedSegment = d.Segment;
            if (!CaptureRig.LiveSkill) return;
            Vector3 head = U(_core.Output.Position), flat = head - _rootPosition; flat.y = 0f;
            string where = d.Impact
                ? $"toImpact={Vector2.Distance(new Vector2(head.x, head.z), d.ImpactPoint):F3}"
                : $"reach={flat.magnitude:F3} height={head.y - _rootPosition.y:F3} angle={Vector3.Angle(flat, d.Direction):F1}";
            Debug.Log($"{Log} contact clip={d.Note} frame={d.Frame:F2} shownSpeed={_core.Output.Velocity.Length():F2} {where} blend={_core.BlendError:F3} gripGap={Vector3.Distance(d.BakeGrip, grip):F3} whipL={_whipLength:F3} bow={_whip.Bow():F3}");
        }
    }
}
