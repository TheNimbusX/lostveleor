using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря (03.10): клипы Pelag_AN_AnchorThrow_* на всё тело, время ведут тики Sim
    /// (кадр = тик), а не темп аниматора — как у рывка, Шквала и Абордажа v2.
    ///
    /// Лента каста — PelagAnchorThrowTimeline: Throw (замах 2 тика, курсор за спиной — 3; выпуск
    /// правой — кадр 2; проводка) → Fly (остаток полёта растянут на тики Sim) → Yank (натяг в тик
    /// натяга, рывок, тянет) → Haul (возврат на R тиков) → Catch (ловля в тик ловли, удержание 3,
    /// выход 6) → стойка. Стыки — одна поза (timing.json, 0°), без смешивания; между тиками кадр идёт
    /// по цепочке клипов. Укороченный полёт (босс, Гарпун) дотягивается без скачка.
    ///
    /// ВРЕМЯ — ТИК ПОКАЗА (sim.Tick − 2 + Alpha), тот же, по которому нарисовано тело.
    ///
    /// КОРЕНЬ ВЕДЁТ ЭТОТ ВИД (ArenaView → TryGetAnchorThrowBody): поворот к Dir S-кривой за замах
    /// вокруг левой лодыжки; левая стопа стоит ВЕСЬ бросок (клипы держат её в одной точке), поэтому
    /// сдвиг держится до конца. В конце корень возвращается к точке Sim, а стопы переступают шагом
    /// выхода Шквала (BeginSquallExitStep, PelagFootPlantView) за то же время; вышел ходьбой — бег
    /// прячет возврат. Вход из покоя держит левую стопу там, где стояла (как Абордаж).
    ///
    /// Руки: рукоять цепи в левой, сабля за кушаком весь каст (PelagEquipmentView по
    /// AnchorThrowBodyActive, пока риг или временный путь VFX не держат якорь). Голову и цепь ведёт
    /// риг якоря на цепи (PelagAnchorRig, кормилец — мост VFX PelagVfxController.AnchorThrowRig) или
    /// временный путь VFX; вид тела только говорит ригу кадр «якорь на спину» (Catch 3, AnchorThrowRigStowCue).
    ///
    /// Контроллер без AnchorThrow_* (ещё не собран) — тело не трогаем (прежний показ: стоит).
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        private const float AnchorThrowEnterBlend = .035f;
        private const float AnchorThrowExitBlend = .10f;
        private const float AnchorThrowCancelBlend = .08f;
        private const float AnchorThrowFacingRecoverySeconds = .28f;

        private static readonly int[] AnchorThrowStates = AnchorThrowHashes(false);
        private static readonly int[] AnchorThrowPhaseIds = AnchorThrowHashes(true);

        private readonly PelagAnchorThrowFeed _anchorThrowFeed = new PelagAnchorThrowFeed();
        private int _anchorThrowSupport = -1;
        private bool _anchorThrowDriven, _anchorThrowStowCued;
        private int _anchorThrowEnterFrame, _anchorThrowReadTick, _anchorThrowBaseCheckFrame = -1;
        private PelagAnchorThrowClip _anchorThrowClip;
        private Simulation _anchorThrowSim;
        private float _anchorThrowRecoverUntil, _anchorThrowReleaseAt = -1f, _anchorThrowReleaseSeconds = .1f;
        private Vector3 _anchorThrowLastFacing, _anchorThrowLastShift, _anchorThrowReleaseShift;
        private bool _anchorThrowEntryPin;
        private Vector3 _anchorThrowEntryFoot, _anchorThrowEntryOffset;

        /// <summary>Голова якоря на риге: кадр «якорь на спину» клипа ловли (тело — CharacterAnimatorView.AnchorThrowRig.cs, ставится с ригом).</summary>
        partial void AnchorThrowRigStowCue(int serial);

        private static int[] AnchorThrowHashes(bool parameters)
        {
            var hashes = new int[8];
            foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
                hashes[(int)clip] = Animator.StringToHash(parameters
                    ? PelagAnchorThrowClipRules.PhaseParameter(clip) : PelagAnchorThrowClipRules.StatePath(clip));
            return hashes;
        }

        /// <summary>Контроллер собран с Броском якоря (RazlomPelagV5AnimatorBuilder.AnchorThrow).</summary>
        private bool SupportsAnchorThrow
        {
            get
            {
                if (_anchorThrowSupport < 0 && _animator != null && _animator.runtimeAnimatorController != null)
                {
                    bool ok = true;
                    foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
                        ok &= _animator.HasState(0, AnchorThrowStates[(int)clip])
                              && HasAnimatorParameter(PelagAnchorThrowClipRules.PhaseParameter(clip));
                    _anchorThrowSupport = ok ? 1 : 0;
                    if (!ok) AnchorThrowTrace("controller without AnchorThrow_* states — body not driven");
                }
                return _anchorThrowSupport == 1;
            }
        }

        /// <summary>
        /// Бросок якоря ведёт тело героя (со второго кадра каста: в кадр каста саблю ещё держит
        /// рука — риг якоря сам уводит её за кушак, PelagAnchorRig.UpdateSaber). Пока true,
        /// PelagEquipmentView держит саблю за кушаком, даже если якорь уже отдан поясу.
        /// </summary>
        public bool AnchorThrowBodyActive => _anchorThrowDriven && !IsDead && Time.frameCount > _anchorThrowEnterFrame;

        private float AnchorThrowNow(Simulation sim)
            => PelagSquallClipRules.ShownTick(sim.Tick, _cycloneDriver != null ? _cycloneDriver.Alpha : 0f);

        private float AnchorThrowScale => PelagSquallClipRules.TimingToWorld(transform.lossyScale.y);

        /// <summary>Каст (PlayAbilityDefinition). false — контроллер без AnchorThrow_* или Броска в Sim нет.</summary>
        private bool TryBeginAnchorThrow()
        {
            Simulation sim = TempoSim;
            if (sim == null || _faction != Faction.Wole || IsDead || _animator == null || !SupportsAnchorThrow) return false;
            AnchorThrowState s = sim.AnchorThrow;
            if (s.Serial == 0 || s.Phase == AnchorThrowPhase.None) return false;
            if (_anchorThrowDriven && _anchorThrowFeed.Timeline.Serial == s.Serial && _anchorThrowSim == sim) return true;
            StopAttackWarp();
            CancelUpperBodyAttack(.02f);
            ResetAbilityTriggers();
            _leapLocomotion = false;
            _attackPresentationActive = false;
            _abilityDefinitionId = AbilityDefinition.AnchorThrowId;
            _abilityPresentationActive = true;
            _abilityUsesLowerBodyLayer = false;
            // Всё тело, как у рывка, Шквала и Абордажа: слои удара и стойки иначе держат руки и ноги.
            if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
            if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);
            if (_recoveryFootworkLayer >= 0) _animator.SetLayerWeight(_recoveryFootworkLayer, 0f);

            _anchorThrowFeed.Begin(sim);
            _anchorThrowDriven = true;
            _anchorThrowStowCued = false;
            _anchorThrowSim = sim;
            // События шага каста (SimulationTick = CastTick + 1) и позже.
            _anchorThrowReadTick = s.CastTick;
            _anchorThrowEnterFrame = Time.frameCount;
            _anchorThrowClip = PelagAnchorThrowClip.None;
            _anchorThrowRecoverUntil = 0f;
            _anchorThrowReleaseAt = -1f;
            BeginAnchorThrowEntryPin();
            AnchorThrowTrace($"begin serial={s.Serial} cast={s.CastTick} release={s.ReleaseTick} flight={s.FlightTicks} "
                             + $"taut={s.TautTick} return={s.ReturnTicks} catch={s.CatchTick} form={s.Form} simTick={sim.Tick}");
            UpdateAnchorThrowAnimation();
            return true;
        }

        /// <summary>Бросок ведёт показ (а не сменён другим действием).</summary>
        private bool AnchorThrowOwnsPresentation => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.AnchorThrowId;

        private void UpdateAnchorThrowAnimation()
        {
            // Действие на слоях сменило бросок: базовый слой не должен остаться в его клипе.
            if (_anchorThrowBaseCheckFrame >= 0 && Time.frameCount >= _anchorThrowBaseCheckFrame && _animator != null)
            {
                _anchorThrowBaseCheckFrame = -1;
                if (!IsDead && BaseInAnchorThrow())
                    _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, AnchorThrowCancelBlend, 0, 0f);
            }
            if (!_anchorThrowDriven) return;
            Simulation sim = TempoSim;
            if (sim == null || sim != _anchorThrowSim || IsDead || _animator == null) { ReleaseAnchorThrow("lost", false); return; }
            if (!AnchorThrowOwnsPresentation) { ReleaseAnchorThrow("replaced", true); return; }
            if (!BaseInAnchorThrow() && _anchorThrowClip != PelagAnchorThrowClip.None && Time.frameCount > _anchorThrowEnterFrame + 1)
            { ReleaseAnchorThrow("base layer", false); return; }
            ReadAnchorThrowEvents(sim);
            float now = AnchorThrowNow(sim);
            string tracked = _anchorThrowFeed.Track(sim, now);
            if (tracked != null) AnchorThrowTrace(tracked);
            PelagAnchorThrowPose pose = _anchorThrowFeed.Timeline.Sample(now, AnchorThrowScale);
            if (pose.StowCue && !_anchorThrowStowCued)
            {
                _anchorThrowStowCued = true;
                AnchorThrowRigStowCue(_anchorThrowFeed.Timeline.Serial);
                AnchorThrowTrace($"stow cue frame={pose.Frame:F2} shown={now:F2}");
            }
            if (pose.Finished) { EndAnchorThrow(pose); return; }
            ApplyAnchorThrowPose(pose, now);
            // Показ держится, пока ведёт лента; часы общего Update его не снимают.
            _abilityPresentationUntil = Time.time + .1f;
            _actionProtectedUntil = Time.time + .05f;
        }

        private void ApplyAnchorThrowPose(in PelagAnchorThrowPose pose, float now)
        {
            int id = (int)pose.Clip;
            if (pose.Clip != _anchorThrowClip)
            {
                int hash = AnchorThrowStates[id];
                if (_anchorThrowClip == PelagAnchorThrowClip.None) _animator.CrossFadeInFixedTime(hash, AnchorThrowEnterBlend, 0, 0f);
                else if (!PelagAnchorThrowClipRules.IsSeam(_anchorThrowClip, pose.Clip))
                    _animator.CrossFadeInFixedTime(hash, AnchorThrowCancelBlend, 0, 0f);
                else _animator.Play(hash, 0, 0f);
                AnchorThrowTrace($"clip {_anchorThrowClip}->{pose.Clip} frame={pose.Frame:F2} chain={pose.Chain:F2} shown={now:F2} yaw={pose.Yaw:F1}");
                _anchorThrowClip = pose.Clip;
            }
            _animator.SetFloat(AnchorThrowPhaseIds[id], Mathf.Clamp01(pose.Frame / PelagAnchorThrowClipRules.LastFrame(pose.Clip)));
        }

        /// <summary>События Броска из кадра. Читаются и из Update, и из LateUpdate (ArenaView) — дважды не берутся.</summary>
        private void ReadAnchorThrowEvents(Simulation sim)
        {
            if (_cycloneDriver == null || !_anchorThrowDriven) return;
            var events = _cycloneDriver.FrameEventContexts;
            int readTo = _anchorThrowReadTick;
            float now = AnchorThrowNow(sim);
            for (int i = 0; i < events.Count; i++)
            {
                FrameEventContext context = events[i];
                if (context.SimulationTick <= _anchorThrowReadTick) continue;
                if (context.SimulationTick > readTo) readTo = context.SimulationTick;
                string trace = _anchorThrowFeed.Apply(sim, context.Event, PelagSquallClipRules.EventTick(context.SimulationTick), now);
                if (trace != null) AnchorThrowTrace(trace);
            }
            _anchorThrowReadTick = readTo;
        }

        /// <summary>
        /// Корень героя для ArenaView: взгляд и сдвиг тела от позиции Sim. lastShown — взгляд,
        /// показанный в прошлом кадре (с него начинается поворот замаха), proposed — то, что ArenaView
        /// поставил бы сам. false — Бросок корнем не правит.
        /// </summary>
        public bool TryGetAnchorThrowBody(Vector3 lastShown, Vector3 proposed, out Vector3 facing, out Vector3 shift)
        {
            facing = proposed;
            shift = Vector3.zero;
            if (_faction != Faction.Wole) return false;
            Simulation sim = TempoSim;
            // Сменили в этом же кадре (событие пришло в LateUpdate): корень уже не наш.
            if (_anchorThrowDriven && !AnchorThrowOwnsPresentation) ReleaseAnchorThrow("replaced", true);
            if (_anchorThrowDriven && sim != null && sim == _anchorThrowSim && !IsDead)
            {
                ReadAnchorThrowEvents(sim);
                float now = AnchorThrowNow(sim);
                PelagAnchorThrowTimeline line = _anchorThrowFeed.Timeline;
                if (!line.HasStartYaw && lastShown.sqrMagnitude > .25f)
                    line.SetStartYaw(PelagSquallClipRules.YawOf(lastShown.x, lastShown.z));
                PelagAnchorThrowPose pose = line.Sample(now, AnchorThrowScale);
                PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
                facing = _anchorThrowLastFacing = new Vector3(x, 0f, z);
                shift = new Vector3(pose.ShiftX, 0f, pose.ShiftY);
                if (_anchorThrowEntryPin) shift += AnchorThrowEntryShift(pose.Clip, lastShown, facing, shift);
                _anchorThrowLastShift = shift;
                return true;
            }
            // Корнем уже правит другой навык с корнем (каст сразу после Броска): взгляд — его, а возврат корня к точке
            // Sim доигрывается поверх (до 0,9 м за 0,3 с — оборвать его значило бы прыжок тела за кадр).
            bool other = _squallDriven || _abordageDriven;
            if (other) _anchorThrowRecoverUntil = 0f;
            bool used = false;
            // Рывок разворачивает сразу и сам (ArenaView, RollActive) — ему не мешаем.
            if (!other && Time.time < _anchorThrowRecoverUntil && !RollActive
                && proposed.sqrMagnitude > .25f && _anchorThrowLastFacing.sqrMagnitude > .25f)
            {
                // Конец Броска: взгляд догоняет Sim за четверть секунды, а не прыжком в кадр.
                _anchorThrowLastFacing = Vector3.Slerp(_anchorThrowLastFacing, proposed, 1f - Mathf.Exp(-16f * Time.deltaTime)).normalized;
                facing = _anchorThrowLastFacing;
                used = true;
            }
            if (_anchorThrowReleaseAt >= 0f)
            {
                float u = (Time.time - _anchorThrowReleaseAt) / _anchorThrowReleaseSeconds;
                if (u >= 1f) _anchorThrowReleaseAt = -1f;
                else { shift = _anchorThrowReleaseShift * (1f - Mathf.SmoothStep(0f, 1f, u)); used = true; }
            }
            return used;
        }

        /// <summary>
        /// Показ доигран (или Sim сняла Бросок): тело уходит в стойку или бег, взгляд догоняет Sim,
        /// корень возвращается к точке Sim, стопы переступают шагом выхода за то же время.
        /// </summary>
        private void EndAnchorThrow(in PelagAnchorThrowPose pose)
        {
            if (!_anchorThrowDriven) return;
            _anchorThrowDriven = false;
            PelagAnchorThrowTimeline line = _anchorThrowFeed.Timeline;
            bool cut = line.Ended && line.EndReason != AnchorThrowEnd.Done;
            line.Stop();
            if (AnchorThrowOwnsPresentation)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _actionProtectedUntil = 0f;
            }
            PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
            _anchorThrowLastFacing = new Vector3(x, 0f, z);
            _anchorThrowRecoverUntil = Time.time + AnchorThrowFacingRecoverySeconds;
            float glide = StartAnchorThrowShiftRelease();
            AnchorThrowTrace($"end clip={pose.Clip} frame={pose.Frame:F2} reason={line.EndReason} moving={_locomotionMoving} glide={glide:F2}s");
            if (IsDead || _animator == null || !BaseInAnchorThrow()) return;
            float blend = cut && line.EndReason == AnchorThrowEnd.Interrupted ? AnchorThrowCancelBlend : AnchorThrowExitBlend;
            _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, blend, 0, 0f);
            // Выход в покой: стопы переступают дугой, пока корень возвращается к точке Sim (тот же шаг, что у Шквала).
            if (!_locomotionMoving) BeginSquallExitStep(Mathf.Max(blend, glide));
        }

        /// <summary>
        /// Вход из стойки покоя: запоминаем, где стоит левая лодыжка, — весь бросок корень держит её там
        /// (AnchorThrowEntryShift). Бегущему не держим: стопа в шаге. Кость стопы и порог — общие со Шквалом.
        /// </summary>
        private void BeginAnchorThrowEntryPin()
        {
            _anchorThrowEntryOffset = Vector3.zero;
            Transform foot = SquallLeftFoot;
            _anchorThrowEntryPin = foot != null && !_locomotionMoving && SquallFootPlanted(foot);
            if (_anchorThrowEntryPin) _anchorThrowEntryFoot = foot.position;
            AnchorThrowTrace($"entry pin={_anchorThrowEntryPin} foot={(foot != null ? foot.position.ToString("F3") : "-")}");
        }

        /// <summary>
        /// Сдвиг корня, держащий левую лодыжку там, где она стояла в покое: в Throw — замер по кости этого кадра
        /// (поза смешивания и поворот, который сейчас поставит ArenaView), дальше — последний замер (клипы держат
        /// лодыжку в одной точке до конца ловли). baseShift — сдвиг ленты (поворот вокруг лодыжки).
        /// </summary>
        private Vector3 AnchorThrowEntryShift(PelagAnchorThrowClip clip, Vector3 lastShown, Vector3 facing, Vector3 baseShift)
        {
            if (clip == PelagAnchorThrowClip.Throw && _squallLeftFoot != null && lastShown.sqrMagnitude > .25f)
            {
                Vector3 ankle = _squallLeftFoot.position - transform.position;
                float turn = Vector3.SignedAngle(new Vector3(lastShown.x, 0f, lastShown.z), facing, Vector3.up);
                ankle = Quaternion.AngleAxis(turn, Vector3.up) * ankle;
                Vector3 offset = _anchorThrowEntryFoot - (transform.position + baseShift + ankle);
                offset.y = 0f;
                _anchorThrowEntryOffset = Vector3.ClampMagnitude(offset, PelagSquallClipRules.EntryShiftMax * AnchorThrowScale);
            }
            return _anchorThrowEntryOffset;
        }

        /// <summary>
        /// Тело забрал кто-то другой: ленту не ведём, корень возвращается к точке Sim. replaced — сменило действие:
        /// взгляд догоняет его плавно (кроме рывка), а через кадр, если базовый слой остался в клипе Броска
        /// (действие на слоях), он уходит в бег или стойку.
        /// </summary>
        private void ReleaseAnchorThrow(string why, bool replaced)
        {
            if (!_anchorThrowDriven) return;
            _anchorThrowDriven = false;
            _anchorThrowFeed.Timeline.Stop();
            _anchorThrowRecoverUntil = replaced ? Time.time + AnchorThrowFacingRecoverySeconds : 0f;
            _anchorThrowBaseCheckFrame = replaced ? Time.frameCount + 1 : -1;
            StartAnchorThrowShiftRelease();
            AnchorThrowTrace("release " + why);
        }

        /// <summary>Сдвиг опоры гаснет за ExitGlideSeconds(|сдвиг|); возвращает это время.</summary>
        private float StartAnchorThrowShiftRelease()
        {
            _anchorThrowReleaseShift = _anchorThrowLastShift;
            _anchorThrowLastShift = Vector3.zero;
            _anchorThrowReleaseSeconds = PelagAnchorThrowClipRules.ExitGlideSeconds(_anchorThrowReleaseShift.magnitude);
            _anchorThrowReleaseAt = _anchorThrowReleaseShift.sqrMagnitude > 1e-6f ? Time.time : -1f;
            return _anchorThrowReleaseAt >= 0f ? _anchorThrowReleaseSeconds : 0f;
        }

        /// <summary>Базовый слой в клипе Броска или идёт в него.</summary>
        private bool BaseInAnchorThrow()
        {
            AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
            foreach (PelagAnchorThrowClip clip in PelagAnchorThrowClipRules.Clips)
                if (AnchorThrowStates[(int)clip] == info.fullPathHash) return true;
            return false;
        }

        /// <summary>Строка в журнал только под съёмкой (как [abordage-anim]): в игре молчит.</summary>
        private static void AnchorThrowTrace(string text)
        {
            if (CaptureRig.HasEnemyOverride) Debug.Log("[anchor-throw-anim] t=" + Time.time.ToString("F3") + " " + text);
        }

        private void ResetAnchorThrow()
        {
            _anchorThrowDriven = false;
            _anchorThrowStowCued = false;
            _anchorThrowSupport = -1;
            _anchorThrowFeed.Timeline.Stop();
            _anchorThrowClip = PelagAnchorThrowClip.None;
            _anchorThrowSim = null;
            _anchorThrowRecoverUntil = 0f;
            _anchorThrowReleaseAt = -1f;
            _anchorThrowBaseCheckFrame = -1;
            _anchorThrowLastShift = Vector3.zero;
            _anchorThrowEntryPin = false;
        }
    }
}
