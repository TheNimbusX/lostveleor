using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Шквал v2 (переделка 02.10, «резкое быстрое, чтобы тело не ломалось»): клипы
    /// Pelag_AN_Squall2_* на всё тело, время ведут тики Sim (кадр = тик), а не темп
    /// аниматора — как у рывка (CharacterAnimatorView.Dash).
    ///
    /// Лента каста — PelagSquallTimeline: каст → Load, каждый SquallJump → Forehand
    /// или Backhand (Flag) с полётом, растянутым на его тики, опора кадры 6–8; последний
    /// удар → FinishFore/Back с кадра 6; Неуловимый и «Возврат» → ReturnFore/Back на тики
    /// возврата. Стыки клипов — та же поза (timing.json, 0°), поэтому без смешивания.
    ///
    /// ВРЕМЯ — ТИК ПОКАЗА (sim.Tick − 2 + Alpha), тот же, по которому нарисовано тело:
    /// контакт (кадр 6) встаёт ровно тогда, когда тело долетело.
    ///
    /// КОРЕНЬ ВЕДЁТ ЭТОТ ВИД, А НЕ ВЗГЛЯД SIM. ArenaView берёт взгляд и сдвиг тела из
    /// TryGetSquallBody: поворот к следующей цели — S-кривой в опоре (кадры 7–8) и в
    /// первом тике полёта; пока левая стопа стоит, корень крутится вокруг её лодыжки,
    /// сдвиг гаснет к следующему удару. Прежний двойной поворот (клип + доворот корня
    /// к _playerAbilityFacing) и рывок взгляда до 180° в конце сюда не попадают: взгляд
    /// в конце серии равен взгляду Sim, после конца — плавный возврат за 0,28 с.
    ///
    /// Контроллер без Squall2_* (ещё не пересобран) — прежний показ по ChainStepHop.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        // Вход держит снимок стойки почти всё это смешивание (PelagSquallClipRules.EntryPoseWeight).
        private const float SquallEnterBlend = PelagSquallClipRules.EntryCrossFadeSeconds;
        private const float SquallExitBlend = .10f;
        private const float SquallCancelBlend = .08f;
        private const float SquallLateFinishBlend = .08f;
        private const float SquallFacingRecoverySeconds = .28f;
        private const float SquallShiftReleaseSeconds = .08f;

        private static readonly int[] SquallStates = SquallHashes(false);
        private static readonly int[] SquallPhaseIds = SquallHashes(true);

        private readonly PelagSquallFeed _squallFeed = new PelagSquallFeed();
        private int _squallSupport = -1;
        private bool _squallDriven;
        private int _squallEnterFrame, _squallReadTick;
        private PelagSquallClip _squallClip;
        private Simulation _squallSim;
        private float _squallRecoverUntil, _squallReleaseAt = -1f;
        private Vector3 _squallLastFacing, _squallLastShift, _squallReleaseShift;

        // Стойка покоя на входе и выходе (PelagSquallClipRules): кости стоп и где они стояли.
        private Transform _squallLeftFoot, _squallRightFoot;
        private bool _squallFootSearched, _squallEntryPin;
        private Vector3 _squallEntryFoot, _squallEntryOffset, _squallExitFoot, _squallExitRightFoot;
        private float _squallExitStepAt = -1f, _squallExitStepSeconds;
        private bool _squallExitLeft, _squallExitRight;
        // Вход: поза кадра каста переходит в позу аниматора (PelagFootPlantView.BlendSquallEntry).
        private PelagFootPlantView _squallFootPlant;
        private bool _squallFootPlantSearched, _squallEntryPose;
        private float _squallEntryAt, _squallEntryWeight;

        private static int[] SquallHashes(bool parameters)
        {
            var hashes = new int[8];
            foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
                hashes[(int)clip] = Animator.StringToHash(parameters
                    ? PelagSquallClipRules.PhaseParameter(clip) : PelagSquallClipRules.StatePath(clip));
            return hashes;
        }

        /// <summary>Контроллер собран со Шквалом v2 (RazlomPelagV5AnimatorBuilder.Squall2).</summary>
        private bool SupportsSquall2
        {
            get
            {
                if (_squallSupport < 0 && _animator != null && _animator.runtimeAnimatorController != null)
                {
                    bool ok = true;
                    foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
                        ok &= _animator.HasState(0, SquallStates[(int)clip])
                              && HasAnimatorParameter(PelagSquallClipRules.PhaseParameter(clip));
                    _squallSupport = ok ? 1 : 0;
                }
                return _squallSupport == 1;
            }
        }

        /// <summary>Шквал v2 ведёт тело героя.</summary>
        public bool SquallActive => _squallDriven && !IsDead;

        private float SquallScale => PelagSquallClipRules.TimingToWorld(transform.lossyScale.y);

        private float SquallNow(Simulation sim)
            => PelagSquallClipRules.ShownTick(sim.Tick, _cycloneDriver != null ? _cycloneDriver.Alpha : 0f);

        /// <summary>Каст (PlayAbilityDefinition). false — контроллер без Squall2_* или Шквала в Sim нет.</summary>
        private bool TryBeginSquall()
        {
            Simulation sim = TempoSim;
            if (sim == null || _faction != Faction.Wole || IsDead || _animator == null || !SupportsSquall2) return false;
            SquallState s = sim.Squall;
            if (s.Serial == 0 || s.Phase == SquallPhase.None) return false;
            // Каст приходит и AbilityCast, и ActionStageStarted: второй раз ленту не начинаем.
            if (_squallDriven && _squallFeed.Timeline.Serial == s.Serial && _squallSim == sim) return true;
            StopAttackWarp();
            CancelUpperBodyAttack(.02f);
            ResetAbilityTriggers();
            _leapLocomotion = false;
            _attackPresentationActive = false;
            _abilityDefinitionId = AbilityDefinition.ChainStepId;
            _abilityPresentationActive = true;
            _abilityUsesLowerBodyLayer = false;
            _chainPresentationVariant = 0;
            _chainFinishing = false;
            SetCombatReady(true);
            // Всё тело, как у рывка: слои удара и стойки иначе держат руки и ноги.
            if (_upperBodyLayer >= 0) _animator.SetLayerWeight(_upperBodyLayer, 0f);
            if (_lowerBodyLayer >= 0) _animator.SetLayerWeight(_lowerBodyLayer, 0f);
            if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
            if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);

            _squallFeed.Begin(sim);
            _squallDriven = true;
            _squallSim = sim;
            // События шага каста (SimulationTick = CastTick + 1) и позже.
            _squallReadTick = s.CastTick;
            _squallEnterFrame = Time.frameCount;
            _squallClip = PelagSquallClip.None;
            _squallRecoverUntil = 0f;
            _squallReleaseAt = -1f;
            BeginSquallEntryPin();
            SquallTrace($"begin serial={s.Serial} cast={s.CastTick} phase={s.Phase} simTick={sim.Tick}");
            UpdateSquallAnimation();
            return true;
        }

        /// <summary>Шквал сменило действие на слоях: через кадр проверить, не остался ли базовый слой в клипе Шквала.</summary>
        private int _squallBaseCheckFrame = -1;

        /// <summary>Шквал ведёт показ (а не сменён другим действием).</summary>
        private bool SquallOwnsPresentation => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.ChainStepId;

        private void UpdateSquallAnimation()
        {
            // Действие на слоях (удар сабли, Вихрь) базовый слой не трогает: ноги и корпус
            // уходят в бег или стойку. Полнотелое (рывок, якорь) уже увело слой — молча.
            if (_squallBaseCheckFrame >= 0 && Time.frameCount >= _squallBaseCheckFrame && _animator != null)
            {
                _squallBaseCheckFrame = -1;
                if (!IsDead && BaseInSquall())
                    _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, SquallCancelBlend, 0, 0f);
            }
            if (!_squallDriven) return;
            Simulation sim = TempoSim;
            if (sim == null || sim != _squallSim || IsDead || _animator == null) { ReleaseSquall("lost", false); return; }
            if (!SquallOwnsPresentation) { ReleaseSquall("replaced", true); return; }
            if (!BaseInSquall() && _squallClip != PelagSquallClip.None && Time.frameCount > _squallEnterFrame + 1)
            { ReleaseSquall("base layer", false); return; }
            ReadSquallEvents(sim);
            float now = SquallNow(sim);
            string late = _squallFeed.DetectLateFinish(sim, now);
            if (late != null) SquallTrace(late);
            PelagSquallPose pose = _squallFeed.Timeline.Sample(now, SquallScale);
            if (pose.Finished) { EndSquall(pose); return; }
            ApplySquallPose(pose, now);
            // Показ держится, пока ведёт лента; часы общего Update его не снимают.
            _abilityPresentationUntil = Time.time + .1f;
            _actionProtectedUntil = Time.time + .05f;
        }

        private void ApplySquallPose(in PelagSquallPose pose, float now)
        {
            int id = (int)pose.Clip;
            if (pose.Clip != _squallClip)
            {
                int hash = SquallStates[id];
                if (_squallClip == PelagSquallClip.None) _animator.CrossFadeInFixedTime(hash, SquallEnterBlend, 0, 0f);
                else if (pose.Crossfade || !PelagSquallClipRules.IsSeam(_squallClip, pose.Clip))
                    _animator.CrossFadeInFixedTime(hash, SquallLateFinishBlend, 0, 0f);
                else _animator.Play(hash, 0, 0f);
                SquallTrace($"clip {_squallClip}->{pose.Clip} frame={pose.Frame:F2} shown={now:F2} yaw={pose.Yaw:F1}");
                _squallClip = pose.Clip;
            }
            _animator.SetFloat(SquallPhaseIds[id], Mathf.Clamp01(pose.Frame / PelagSquallClipRules.LastFrame(pose.Clip)));
        }

        /// <summary>События Шквала из кадра. Читаются и из Update, и из LateUpdate (ArenaView) — дважды не берутся.</summary>
        private void ReadSquallEvents(Simulation sim)
        {
            if (_cycloneDriver == null || !_squallDriven) return;
            var events = _cycloneDriver.FrameEventContexts;
            int readTo = _squallReadTick;
            float now = SquallNow(sim);
            for (int i = 0; i < events.Count; i++)
            {
                FrameEventContext context = events[i];
                if (context.SimulationTick <= _squallReadTick) continue;
                if (context.SimulationTick > readTo) readTo = context.SimulationTick;
                string trace = _squallFeed.Apply(sim, context.Event,
                    PelagSquallClipRules.EventTick(context.SimulationTick), now);
                if (trace != null) SquallTrace(trace);
            }
            _squallReadTick = readTo;
        }

        /// <summary>
        /// Корень героя для ArenaView: взгляд и сдвиг тела от позиции Sim. lastShown —
        /// взгляд, показанный в прошлом кадре (с него начинается поворот замаха),
        /// proposed — то, что ArenaView поставил бы сам. false — Шквал корнем не правит.
        /// </summary>
        public bool TryGetSquallBody(Vector3 lastShown, Vector3 proposed, out Vector3 facing, out Vector3 shift)
        {
            facing = proposed;
            shift = Vector3.zero;
            if (_faction != Faction.Wole) return false;
            Simulation sim = TempoSim;
            // Сменили в этом же кадре (событие пришло в LateUpdate): корень уже не наш.
            if (_squallDriven && !SquallOwnsPresentation) ReleaseSquall("replaced", true);
            if (_squallDriven && sim != null && sim == _squallSim && !IsDead)
            {
                ReadSquallEvents(sim);
                float now = SquallNow(sim);
                if (!_squallFeed.Timeline.HasStartYaw && lastShown.sqrMagnitude > .25f)
                    _squallFeed.Timeline.SetStartYaw(PelagSquallClipRules.YawOf(lastShown.x, lastShown.z), now);
                PelagSquallPose pose = _squallFeed.Timeline.Sample(now, SquallScale);
                PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
                facing = _squallLastFacing = new Vector3(x, 0f, z);
                shift = new Vector3(pose.ShiftX, 0f, pose.ShiftY);
                // Сначала итоговая поза кадра (переход от стойки каста), потом замер лодыжки по ней.
                if (_squallEntryPose) BlendSquallEntryPose(now);
                if (_squallEntryPin) shift += SquallEntryShift(pose.Clip, now, lastShown, facing, shift);
                _squallLastShift = shift;
                return true;
            }
            bool used = false;
            // Рывок разворачивает сразу и сам (ArenaView, RollActive) — ему не мешаем.
            if (Time.time < _squallRecoverUntil && !RollActive
                && proposed.sqrMagnitude > .25f && _squallLastFacing.sqrMagnitude > .25f)
            {
                // Конец Шквала: взгляд догоняет Sim за четверть секунды, а не прыжком в кадр.
                _squallLastFacing = Vector3.Slerp(_squallLastFacing, proposed, 1f - Mathf.Exp(-16f * Time.deltaTime)).normalized;
                facing = _squallLastFacing;
                used = true;
            }
            if (_squallReleaseAt >= 0f)
            {
                float u = (Time.time - _squallReleaseAt) / SquallShiftReleaseSeconds;
                if (u >= 1f) _squallReleaseAt = -1f;
                else { shift = _squallReleaseShift * (1f - Mathf.SmoothStep(0f, 1f, u)); used = true; }
            }
            return used;
        }

        /// <summary>Показ доигран (или Sim сняла Шквал): тело уходит в стойку или бег, взгляд догоняет Sim.</summary>
        private void EndSquall(in PelagSquallPose pose)
        {
            if (!_squallDriven) return;
            _squallDriven = false;
            bool cancelled = _squallFeed.Timeline.Ended && _squallFeed.Timeline.EndReason == SquallEnd.Interrupted;
            _squallFeed.Timeline.Stop();
            if (_abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.ChainStepId)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _actionProtectedUntil = 0f;
            }
            PelagSquallClipRules.YawVector(pose.Yaw, out float x, out float z);
            _squallLastFacing = new Vector3(x, 0f, z);
            _squallRecoverUntil = Time.time + SquallFacingRecoverySeconds;
            StartSquallShiftRelease();
            SquallTrace($"end clip={pose.Clip} frame={pose.Frame:F2} cancelled={cancelled} moving={_locomotionMoving}");
            if (IsDead || _animator == null || !BaseInSquall()) return;
            float blend = cancelled ? SquallCancelBlend : SquallExitBlend;
            _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, blend, 0, 0f);
            if (!_locomotionMoving) BeginSquallExitStep(blend);
        }

        private Transform SquallLeftFoot
        {
            get
            {
                if (!_squallFootSearched)
                {
                    _squallFootSearched = true;
                    foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                    {
                        if (bone.name == "mixamorig:LeftFoot" && _squallLeftFoot == null) _squallLeftFoot = bone;
                        else if (bone.name == "mixamorig:RightFoot" && _squallRightFoot == null) _squallRightFoot = bone;
                        if (_squallLeftFoot != null && _squallRightFoot != null) break;
                    }
                }
                return _squallLeftFoot;
            }
        }

        private PelagFootPlantView SquallFootPlant
        {
            get
            {
                if (!_squallFootPlantSearched)
                {
                    _squallFootPlantSearched = true;
                    _squallFootPlant = GetComponent<PelagFootPlantView>();
                }
                return _squallFootPlant;
            }
        }

        private bool SquallFootPlanted(Transform foot)
            => foot.position.y - transform.position.y <= PelagSquallClipRules.PlantedAnkleHeight * SquallScale;

        /// <summary>
        /// Вход из стойки покоя (PelagSquallClipRules): запоминаем, где стоит левая лодыжка, —
        /// весь замах корень держит её там (SquallEntryShift). Бегущему не держим: стопа в шаге.
        /// И снимаем позу, которую сейчас видно (стойка сабли): слои стойки уже сброшены, а
        /// смешивание аниматора идёт от голого CombatIdle — первый его кадр дёргал таз на 15 см.
        /// </summary>
        private void BeginSquallEntryPin()
        {
            _squallEntryOffset = Vector3.zero;
            Transform foot = SquallLeftFoot;
            _squallEntryPin = foot != null && !_locomotionMoving && SquallFootPlanted(foot);
            if (_squallEntryPin) _squallEntryFoot = foot.position;
            PelagFootPlantView plant = SquallFootPlant;
            _squallEntryPose = _squallEntryPin && plant != null && plant.BeginSquallEntry();
            _squallEntryAt = Time.time;
            _squallEntryWeight = 0f;
            SquallTrace($"entry pin={_squallEntryPin} pose={_squallEntryPose} foot={(foot != null ? foot.position.ToString("F3") : "-")}");
        }

        /// <summary>
        /// Поза кадра входа: снимок стойки каста, пока аниматор смешивает голый CombatIdle,
        /// потом S-кривой в позу аниматора — к первому полёту (и кадру после него) целиком клип
        /// (EntryPoseWeight по длине этого кадра; <paramref name="now"/> — тик показа этого
        /// кадра). Вес не убывает: длина кадра скачет. Из TryGetSquallBody (LateUpdate
        /// ArenaView, аниматор этого кадра уже оценён) — до замера лодыжки в SquallEntryShift,
        /// поэтому стопа стоит по той же позе, что уйдёт в кадр.
        /// </summary>
        private void BlendSquallEntryPose(float now)
        {
            float weight = Mathf.Max(_squallEntryWeight, PelagSquallClipRules.EntryPoseWeight(Time.time - _squallEntryAt,
                PelagSquallClipRules.EntryFlightSeconds(_squallFeed.Timeline.CastTick, now), Time.deltaTime));
            _squallEntryWeight = weight;
            if (_squallFootPlant != null) _squallFootPlant.BlendSquallEntry(weight);
            if (weight >= 1f || _squallFootPlant == null) _squallEntryPose = false;
        }

        /// <summary>
        /// Сдвиг корня, держащий левую лодыжку там, где она стояла в покое. В замахе — замер по
        /// кости этого кадра (поза смешивания и поворот, который сейчас поставит ArenaView;
        /// transform ещё без сдвига, с поворотом прошлого кадра); в первом полёте — последний
        /// замер, гаснущий S-кривой (EntryWeight). baseShift — сдвиг ленты (поворот вокруг лодыжки).
        /// </summary>
        private Vector3 SquallEntryShift(PelagSquallClip clip, float now, Vector3 lastShown, Vector3 facing, Vector3 baseShift)
        {
            if (clip == PelagSquallClip.Load && _squallLeftFoot != null && lastShown.sqrMagnitude > .25f)
            {
                Vector3 ankle = _squallLeftFoot.position - transform.position;
                float turn = Vector3.SignedAngle(new Vector3(lastShown.x, 0f, lastShown.z), facing, Vector3.up);
                ankle = Quaternion.AngleAxis(turn, Vector3.up) * ankle;
                Vector3 offset = _squallEntryFoot - (transform.position + baseShift + ankle);
                offset.y = 0f;
                _squallEntryOffset = Vector3.ClampMagnitude(offset, PelagSquallClipRules.EntryShiftMax * SquallScale);
            }
            return _squallEntryOffset * _squallFeed.Timeline.EntryWeight(now);
        }

        /// <summary>
        /// Выход в стойку покоя (PelagSquallClipRules): корень не двигаем — полёта, который
        /// погасил бы сдвиг, больше нет; стопы переступают дугой по очереди — левая за время
        /// смешивания, правая за такое же время после неё (PelagFootPlantView, ExitSteps).
        /// Запоминаем, где они стоят в последнем кадре Finish/Return; оторванную не ведём.
        /// </summary>
        private void BeginSquallExitStep(float seconds)
        {
            _squallExitStepAt = -1f;
            Transform left = SquallLeftFoot, right = _squallRightFoot;
            _squallExitLeft = left != null && SquallFootPlanted(left);
            _squallExitRight = right != null && SquallFootPlanted(right);
            if (seconds <= 0f || (!_squallExitLeft && !_squallExitRight)) return;
            if (_squallExitLeft) _squallExitFoot = left.position;
            if (_squallExitRight) _squallExitRightFoot = right.position;
            _squallExitStepAt = Time.time;
            _squallExitStepSeconds = seconds;
            SquallTrace($"exit step {seconds:F3}s left={_squallExitLeft} {_squallExitFoot.ToString("F3")}"
                        + $" right={_squallExitRight} {_squallExitRightFoot.ToString("F3")}");
        }

        /// <summary>
        /// Шаги выхода идут: progress — в долях времени смешивания (левая 0…1, правая 1…2,
        /// PelagSquallClipRules.ExitSteps); from — где стояли лодыжки, left/right — шагает ли стопа.
        /// </summary>
        public bool TryGetSquallExitStep(out float progress, out Vector3 leftFrom, out bool left,
            out Vector3 rightFrom, out bool right)
        {
            leftFrom = _squallExitFoot;
            rightFrom = _squallExitRightFoot;
            left = _squallExitLeft;
            right = _squallExitRight;
            progress = PelagSquallClipRules.ExitStepsSpan;
            if (_squallExitStepAt < 0f) return false;
            progress = (Time.time - _squallExitStepAt) / _squallExitStepSeconds;
            float end = right ? PelagSquallClipRules.ExitStepsSpan : 1f;
            if (progress < end && !_squallDriven && !IsDead && !HasCommittedAction && !_locomotionMoving) return true;
            _squallExitStepAt = -1f;
            return false;
        }

        /// <summary>
        /// Тело забрал кто-то другой: ленту не ведём, сдвиг гаснет за 0,08 с. replaced —
        /// сменило действие: взгляд догоняет его плавно (кроме рывка), а через кадр, если
        /// базовый слой остался в клипе Шквала (действие на слоях), он уходит в бег или стойку.
        /// </summary>
        private void ReleaseSquall(string why, bool replaced)
        {
            if (!_squallDriven) return;
            _squallDriven = false;
            _squallFeed.Timeline.Stop();
            _squallRecoverUntil = replaced ? Time.time + SquallFacingRecoverySeconds : 0f;
            _squallBaseCheckFrame = replaced ? Time.frameCount + 1 : -1;
            StartSquallShiftRelease();
            SquallTrace("release " + why);
        }

        private void StartSquallShiftRelease()
        {
            _squallReleaseShift = _squallLastShift;
            _squallLastShift = Vector3.zero;
            _squallReleaseAt = _squallReleaseShift.sqrMagnitude > 1e-6f ? Time.time : -1f;
        }

        /// <summary>Базовый слой в клипе Шквала или идёт в него.</summary>
        private bool BaseInSquall()
        {
            AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
            foreach (PelagSquallClip clip in PelagSquallClipRules.Clips)
                if (SquallStates[(int)clip] == info.fullPathHash) return true;
            return false;
        }

        /// <summary>Строка в журнал только под съёмкой (как [dash-anim]): в игре молчит.</summary>
        private static void SquallTrace(string text)
        {
            if (CaptureRig.HasEnemyOverride) Debug.Log("[squall-anim] t=" + Time.time.ToString("F3") + " " + text);
        }

        private void ResetSquall()
        {
            _squallDriven = false;
            _squallSupport = -1;
            _squallFeed.Timeline.Stop();
            _squallClip = PelagSquallClip.None;
            _squallSim = null;
            _squallRecoverUntil = 0f;
            _squallReleaseAt = -1f;
            _squallBaseCheckFrame = -1;
            _squallLastShift = Vector3.zero;
            _squallLeftFoot = _squallRightFoot = null;
            _squallFootSearched = _squallEntryPin = false;
            _squallFootPlant = null;
            _squallFootPlantSearched = _squallEntryPose = false;
            _squallExitStepAt = -1f;
        }
    }
}
