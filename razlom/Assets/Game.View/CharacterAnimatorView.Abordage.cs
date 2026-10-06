using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 (переделка 02.10, «резкое быстрое»): клипы Pelag_AN_Abordage2_* на всё
    /// тело, время ведут тики Sim (кадр = тик), а не темп аниматора — как у рывка и Шквала.
    ///
    /// Лента каста — PelagAbordageTimeline: каст → Throw (v3: замах через плечо 3 тика, цель за
    /// спиной — 4, выпуск правой — кадр 3, полёт якоря растянут на его тики, кадр 9 — натяг в тик
    /// зацепа) → Pull (растянут на тики тяги, кадр 12 — контакт в тик удара; тяга 4–5 тиков —
    /// PullShort, контакт — кадр 5) → Punch (удержание 3; Гейзер — Uppercut, Обвал — Slam с тика
    /// до удара, контакт — их кадр 1) → Recover (выход 6) → стойка. Стыки — та же поза (timing.json, 0°),
    /// без смешивания. Контроллер без PullShort/Uppercut/Slam — на их месте Pull и Punch.
    /// Цели нет до зацепа — Throw обратно к стойке (заглушка клипа Recall).
    ///
    /// ВРЕМЯ — ТИК ПОКАЗА (sim.Tick − 2 + Alpha), тот же, по которому нарисовано тело.
    ///
    /// КОРЕНЬ ВЕДЁТ ЭТОТ ВИД (ArenaView → TryGetAbordageBody): поворот к цели S-кривой за
    /// замах, вокруг левой лодыжки, пока она стоит; сдвиг гаснет в тяге, к удару тело ровно
    /// в точке Sim. В конце взгляд = взгляд Sim, после конца — плавный возврат за 0,28 с.
    /// Вход из покоя держит левую стопу на месте (как Шквал), выход в покой — шаг выхода
    /// Шквала (BeginSquallExitStep → PelagFootPlantView.StepSquallExit): та же стойка.
    ///
    /// Руки (лист B): якорь в ПРАВОЙ от каста до выпуска (PelagEquipmentView по
    /// AbordageAnchorInRightHand), рукоять цепи в левой, сабля за кушаком (BeginAnchorUse).
    ///
    /// Контроллер без Abordage2_* (ещё не пересобран) — прежний показ AnchorLeap_v5.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        private const float AbordageEnterBlend = .035f;
        private const float AbordageExitBlend = .10f;
        private const float AbordageCancelBlend = .08f;
        private const float AbordageFacingRecoverySeconds = .28f;
        private const float AbordageShiftReleaseSeconds = .08f;

        private static readonly int[] AbordageStates = AbordageHashes(false);
        private static readonly int[] AbordagePhaseIds = AbordageHashes(true);

        private readonly PelagAbordageFeed _abordageFeed = new PelagAbordageFeed();
        private int _abordageSupport = -1;
        private bool _abordageShortPullReady, _abordageFormClipsReady;
        private bool _abordageDriven, _abordageHands;
        private int _abordageEnterFrame, _abordageReadTick, _abordageBaseCheckFrame = -1;
        private PelagAbordageClip _abordageClip;
        private PelagAbordagePose _abordagePose;
        private Simulation _abordageSim;
        private float _abordageRecoverUntil, _abordageReleaseAt = -1f;
        private Vector3 _abordageLastFacing, _abordageLastShift, _abordageReleaseShift;
        private bool _abordageEntryPin;
        private Vector3 _abordageEntryFoot, _abordageEntryOffset;

        private static int[] AbordageHashes(bool parameters)
        {
            var hashes = new int[8];
            foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
                hashes[(int)clip] = Animator.StringToHash(parameters
                    ? PelagAbordageClipRules.PhaseParameter(clip) : PelagAbordageClipRules.StatePath(clip));
            return hashes;
        }

        /// <summary>Контроллер собран с Абордажем v2 (RazlomPelagV5AnimatorBuilder.Abordage2).</summary>
        private bool SupportsAbordage2
        {
            get
            {
                if (_abordageSupport < 0 && _animator != null && _animator.runtimeAnimatorController != null)
                {
                    bool ok = true;
                    foreach (PelagAbordageClip clip in PelagAbordageClipRules.RequiredClips)
                        ok &= AbordageClipReady(clip);
                    _abordageSupport = ok ? 1 : 0;
                    // v3 (03.10): короткая тяга и клипы форм — по наличию, без них Pull и Punch.
                    _abordageShortPullReady = AbordageClipReady(PelagAbordageClip.PullShort);
                    _abordageFormClipsReady = AbordageClipReady(PelagAbordageClip.Uppercut)
                                              && AbordageClipReady(PelagAbordageClip.Slam);
                }
                return _abordageSupport == 1;
            }
        }

        private bool AbordageClipReady(PelagAbordageClip clip)
            => _animator.HasState(0, AbordageStates[(int)clip]) && HasAnimatorParameter(PelagAbordageClipRules.PhaseParameter(clip));

        /// <summary>Абордаж v2 ведёт тело героя.</summary>
        public bool AbordageActive => _abordageDriven && !IsDead;

        /// <summary>Этот бросок якоря — Абордаж v2: руки по листу B (PelagEquipmentView), а не по старым секундам.</summary>
        public bool AbordageHandsDriven => _abordageHands && !IsDead;

        /// <summary>Якорь в правой руке на тике показа: от каста до выпуска (кадр 3).</summary>
        public bool AbordageAnchorInRightHand => _abordageDriven && !IsDead && _abordagePose.AnchorInHand;

        private float AbordageNow(Simulation sim)
            => PelagSquallClipRules.ShownTick(sim.Tick, _cycloneDriver != null ? _cycloneDriver.Alpha : 0f);

        /// <summary>Каст (PlayAbilityDefinition). false — контроллер без Abordage2_* или Абордажа в Sim нет.</summary>
        private bool TryBeginAbordage()
        {
            _abordageHands = false;
            Simulation sim = TempoSim;
            if (sim == null || _faction != Faction.Wole || IsDead || _animator == null || !SupportsAbordage2) return false;
            AbordageState s = sim.Abordage;
            if (s.Serial == 0 || s.Phase == AbordagePhase.None) return false;
            _abordageHands = true;
            // Каст приходит и AbilityCast, и ActionStageStarted: второй раз ленту не начинаем.
            if (_abordageDriven && _abordageFeed.Timeline.Serial == s.Serial && _abordageSim == sim) return true;
            StopAttackWarp();
            CancelUpperBodyAttack(.02f);
            ResetAbilityTriggers();
            _leapLocomotion = false;
            _attackPresentationActive = false;
            _abilityDefinitionId = AbilityDefinition.AnchorLeapId;
            _abilityPresentationActive = true;
            _abilityUsesLowerBodyLayer = false;
            // Всё тело, как у рывка и Шквала: слои удара и стойки иначе держат руки и ноги.
            if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
            if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);
            if (_recoveryFootworkLayer >= 0) _animator.SetLayerWeight(_recoveryFootworkLayer, 0f);

            _abordageFeed.Timeline.ShortPullAvailable = _abordageShortPullReady;
            _abordageFeed.Timeline.FormClipsAvailable = _abordageFormClipsReady;
            _abordageFeed.Begin(sim);
            _abordageDriven = true;
            _abordageSim = sim;
            // События шага каста (SimulationTick = CastTick + 1) и позже.
            _abordageReadTick = s.CastTick;
            _abordageEnterFrame = Time.frameCount;
            _abordageClip = PelagAbordageClip.None;
            _abordagePose = default;
            _abordageRecoverUntil = 0f;
            _abordageReleaseAt = -1f;
            BeginAbordageEntryPin();
            AbordageTrace($"begin serial={s.Serial} cast={s.CastTick} release={s.ReleaseTick} bite={s.BiteTick} "
                          + $"arrive={s.ArriveTick} phase={s.Phase} simTick={sim.Tick}");
            UpdateAbordageAnimation();
            return true;
        }

        /// <summary>Абордаж ведёт показ (а не сменён другим действием).</summary>
        private bool AbordageOwnsPresentation => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.AnchorLeapId;

        private void UpdateAbordageAnimation()
        {
            // Действие на слоях (удар сабли, Вихрь) базовый слой не трогает: ноги и корпус
            // уходят в бег или стойку. Полнотелое (рывок, Шквал) уже увело слой — молча.
            if (_abordageBaseCheckFrame >= 0 && Time.frameCount >= _abordageBaseCheckFrame && _animator != null)
            {
                _abordageBaseCheckFrame = -1;
                if (!IsDead && BaseInAbordage())
                    _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, AbordageCancelBlend, 0, 0f);
            }
            if (!_abordageDriven) return;
            Simulation sim = TempoSim;
            if (sim == null || sim != _abordageSim || IsDead || _animator == null) { ReleaseAbordage("lost", false); return; }
            if (!AbordageOwnsPresentation) { ReleaseAbordage("replaced", true); return; }
            if (!BaseInAbordage() && _abordageClip != PelagAbordageClip.None && Time.frameCount > _abordageEnterFrame + 1)
            { ReleaseAbordage("base layer", false); return; }
            ReadAbordageEvents(sim);
            float now = AbordageNow(sim);
            string tracked = _abordageFeed.Track(sim, now);
            if (tracked != null) AbordageTrace(tracked);
            PelagAbordagePose pose = _abordageFeed.Timeline.Sample(now, AbordageScale);
            _abordagePose = pose;
            if (pose.Finished) { EndAbordage(pose); return; }
            ApplyAbordagePose(pose, now);
            // Показ держится, пока ведёт лента; часы общего Update его не снимают.
            _abilityPresentationUntil = Time.time + .1f;
            _actionProtectedUntil = Time.time + .05f;
        }

        private float AbordageScale => PelagSquallClipRules.TimingToWorld(transform.lossyScale.y);

        private void ApplyAbordagePose(in PelagAbordagePose pose, float now)
        {
            int id = (int)pose.Clip;
            if (pose.Clip != _abordageClip)
            {
                int hash = AbordageStates[id];
                if (_abordageClip == PelagAbordageClip.None) _animator.CrossFadeInFixedTime(hash, AbordageEnterBlend, 0, 0f);
                else if (!PelagAbordageClipRules.IsSeam(_abordageClip, pose.Clip))
                    _animator.CrossFadeInFixedTime(hash, AbordageCancelBlend, 0, 0f);
                else _animator.Play(hash, 0, 0f);
                AbordageTrace($"clip {_abordageClip}->{pose.Clip} frame={pose.Frame:F2} shown={now:F2} yaw={pose.Yaw:F1}");
                _abordageClip = pose.Clip;
            }
            _animator.SetFloat(AbordagePhaseIds[id], Mathf.Clamp01(pose.Frame / PelagAbordageClipRules.LastFrame(pose.Clip)));
        }

        /// <summary>События Абордажа из кадра. Читаются и из Update, и из LateUpdate (ArenaView) — дважды не берутся.</summary>
        private void ReadAbordageEvents(Simulation sim)
        {
            if (_cycloneDriver == null || !_abordageDriven) return;
            var events = _cycloneDriver.FrameEventContexts;
            int readTo = _abordageReadTick;
            float now = AbordageNow(sim);
            for (int i = 0; i < events.Count; i++)
            {
                FrameEventContext context = events[i];
                if (context.SimulationTick <= _abordageReadTick) continue;
                if (context.SimulationTick > readTo) readTo = context.SimulationTick;
                string trace = _abordageFeed.Apply(sim, context.Event,
                    PelagSquallClipRules.EventTick(context.SimulationTick), now);
                if (trace != null) AbordageTrace(trace);
            }
            _abordageReadTick = readTo;
        }

        /// <summary>
        /// Корень героя для ArenaView: взгляд и сдвиг тела от позиции Sim. lastShown —
        /// взгляд, показанный в прошлом кадре (с него начинается поворот замаха),
        /// proposed — то, что ArenaView поставил бы сам. false — Абордаж корнем не правит.
        /// </summary>
        public bool TryGetAbordageBody(Vector3 lastShown, Vector3 proposed, out Vector3 facing, out Vector3 shift)
        {
            facing = proposed;
            shift = Vector3.zero;
            if (_faction != Faction.Wole) return false;
            Simulation sim = TempoSim;
            // Сменили в этом же кадре (событие пришло в LateUpdate): корень уже не наш.
            if (_abordageDriven && !AbordageOwnsPresentation) ReleaseAbordage("replaced", true);
            if (_abordageDriven && sim != null && sim == _abordageSim && !IsDead)
            {
                ReadAbordageEvents(sim);
                float now = AbordageNow(sim);
                if (!_abordageFeed.Timeline.HasStartYaw && lastShown.sqrMagnitude > .25f)
                    _abordageFeed.Timeline.SetStartYaw(PelagSquallClipRules.YawOf(lastShown.x, lastShown.z), now);
                PelagAbordagePose pose = _abordageFeed.Timeline.Sample(now, AbordageScale);
                PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
                facing = _abordageLastFacing = new Vector3(x, 0f, z);
                shift = new Vector3(pose.ShiftX, 0f, pose.ShiftY);
                if (_abordageEntryPin) shift += AbordageEntryShift(pose.Clip, now, lastShown, facing, shift);
                _abordageLastShift = shift;
                return true;
            }
            // Корнем уже правит Шквал (каст сразу после Абордажа): хвост возврата ему не мешает.
            if (_squallDriven) { _abordageRecoverUntil = 0f; _abordageReleaseAt = -1f; return false; }
            bool used = false;
            // Рывок разворачивает сразу и сам (ArenaView, RollActive) — ему не мешаем.
            if (Time.time < _abordageRecoverUntil && !RollActive
                && proposed.sqrMagnitude > .25f && _abordageLastFacing.sqrMagnitude > .25f)
            {
                // Конец Абордажа: взгляд догоняет Sim за четверть секунды, а не прыжком в кадр.
                _abordageLastFacing = Vector3.Slerp(_abordageLastFacing, proposed, 1f - Mathf.Exp(-16f * Time.deltaTime)).normalized;
                facing = _abordageLastFacing;
                used = true;
            }
            if (_abordageReleaseAt >= 0f)
            {
                float u = (Time.time - _abordageReleaseAt) / AbordageShiftReleaseSeconds;
                if (u >= 1f) _abordageReleaseAt = -1f;
                else { shift = _abordageReleaseShift * (1f - Mathf.SmoothStep(0f, 1f, u)); used = true; }
            }
            return used;
        }

        /// <summary>Показ доигран (или Sim сняла Абордаж): тело уходит в стойку или бег, взгляд догоняет Sim.</summary>
        private void EndAbordage(in PelagAbordagePose pose)
        {
            if (!_abordageDriven) return;
            _abordageDriven = false;
            PelagAbordageTimeline line = _abordageFeed.Timeline;
            bool cut = line.Ended && (line.EndReason == AbordageEnd.Interrupted || line.EndReason == AbordageEnd.WalkedOut);
            line.Stop();
            if (AbordageOwnsPresentation)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _actionProtectedUntil = 0f;
            }
            PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
            _abordageLastFacing = new Vector3(x, 0f, z);
            _abordageRecoverUntil = Time.time + AbordageFacingRecoverySeconds;
            StartAbordageShiftRelease();
            AbordageTrace($"end clip={pose.Clip} frame={pose.Frame:F2} reason={line.EndReason} moving={_locomotionMoving}");
            if (IsDead || _animator == null || !BaseInAbordage()) return;
            float blend = cut ? AbordageCancelBlend : AbordageExitBlend;
            _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, blend, 0, 0f);
            // Выход в покой из стойки серии: левая стопа переступает дугой — тот же шаг, что у Шквала.
            if (!_locomotionMoving) BeginSquallExitStep(blend);
        }

        /// <summary>
        /// Вход из стойки покоя: запоминаем, где стоит левая лодыжка, — весь бросок корень
        /// держит её там (AbordageEntryShift). Бегущему не держим: стопа в шаге. Кость стопы
        /// и порог «стоит» — общие со Шквалом (стойка та же).
        /// </summary>
        private void BeginAbordageEntryPin()
        {
            _abordageEntryOffset = Vector3.zero;
            Transform foot = SquallLeftFoot;
            _abordageEntryPin = foot != null && !_locomotionMoving && SquallFootPlanted(foot);
            if (_abordageEntryPin) _abordageEntryFoot = foot.position;
            AbordageTrace($"entry pin={_abordageEntryPin} foot={(foot != null ? foot.position.ToString("F3") : "-")}");
        }

        /// <summary>
        /// Сдвиг корня, держащий левую лодыжку там, где она стояла в покое. В броске — замер по
        /// кости этого кадра (поза смешивания и поворот, который сейчас поставит ArenaView);
        /// в тяге — последний замер, гаснущий S-кривой (EntryWeight). baseShift — сдвиг ленты.
        /// </summary>
        private Vector3 AbordageEntryShift(PelagAbordageClip clip, float now, Vector3 lastShown, Vector3 facing, Vector3 baseShift)
        {
            if (clip == PelagAbordageClip.Throw && _squallLeftFoot != null && lastShown.sqrMagnitude > .25f
                && !_abordageFeed.Timeline.Recalling)
            {
                Vector3 ankle = _squallLeftFoot.position - transform.position;
                float turn = Vector3.SignedAngle(new Vector3(lastShown.x, 0f, lastShown.z), facing, Vector3.up);
                ankle = Quaternion.AngleAxis(turn, Vector3.up) * ankle;
                Vector3 offset = _abordageEntryFoot - (transform.position + baseShift + ankle);
                offset.y = 0f;
                _abordageEntryOffset = Vector3.ClampMagnitude(offset, PelagSquallClipRules.EntryShiftMax * AbordageScale);
            }
            return _abordageEntryOffset * _abordageFeed.Timeline.EntryWeight(now);
        }

        /// <summary>
        /// Тело забрал кто-то другой: ленту не ведём, сдвиг гаснет за 0,08 с. replaced —
        /// сменило действие: взгляд догоняет его плавно (кроме рывка), а через кадр, если
        /// базовый слой остался в клипе Абордажа (действие на слоях), он уходит в бег или стойку.
        /// </summary>
        private void ReleaseAbordage(string why, bool replaced)
        {
            if (!_abordageDriven) return;
            _abordageDriven = false;
            _abordageFeed.Timeline.Stop();
            _abordageRecoverUntil = replaced ? Time.time + AbordageFacingRecoverySeconds : 0f;
            _abordageBaseCheckFrame = replaced ? Time.frameCount + 1 : -1;
            StartAbordageShiftRelease();
            AbordageTrace("release " + why);
        }

        private void StartAbordageShiftRelease()
        {
            _abordageReleaseShift = _abordageLastShift;
            _abordageLastShift = Vector3.zero;
            _abordageReleaseAt = _abordageReleaseShift.sqrMagnitude > 1e-6f ? Time.time : -1f;
        }

        /// <summary>Базовый слой в клипе Абордажа или идёт в него.</summary>
        private bool BaseInAbordage()
        {
            AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
            foreach (PelagAbordageClip clip in PelagAbordageClipRules.Clips)
                if (AbordageStates[(int)clip] == info.fullPathHash) return true;
            return false;
        }

        /// <summary>Строка в журнал только под съёмкой (как [squall-anim]): в игре молчит.</summary>
        private static void AbordageTrace(string text)
        {
            if (CaptureRig.HasEnemyOverride) Debug.Log("[abordage-anim] t=" + Time.time.ToString("F3") + " " + text);
        }

        private void ResetAbordage()
        {
            _abordageDriven = false;
            _abordageHands = false;
            _abordageSupport = -1;
            _abordageFeed.Timeline.Stop();
            _abordageClip = PelagAbordageClip.None;
            _abordagePose = default;
            _abordageSim = null;
            _abordageRecoverUntil = 0f;
            _abordageReleaseAt = -1f;
            _abordageBaseCheckFrame = -1;
            _abordageLastShift = Vector3.zero;
            _abordageEntryPin = false;
        }
    }
}
