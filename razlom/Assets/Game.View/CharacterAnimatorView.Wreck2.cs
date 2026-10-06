using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Крушение v4 (06.10, принято владельцем по превью): клипы Pelag_AN_Wreck4_* (Draw, Swing1, Swing2, Lunge, Stow),
    /// время ведут тики Sim (кадр = тик) по снимку WreckState, а не часы PlayerAction. Якорь на цепи — риг anchor-core
    /// (PelagAnchorRig): голова — запечка ТОГО ЖЕ клипа и кадра (TryGetWreck4Pose), цепь-хлыст всегда симулируется.
    ///
    /// Лента серии — PelagWreckTimeline: снятие в замахе маха 1 → мах влево → мах вправо → выпад → уборка. Нажатие
    /// раньше стыка — «догон» без смешивания, позже — смешивание 2 тика; выпад последнего этапа в кадре 16 переходит
    /// в Stow стыком; окно прошло — Stow со смешиванием из маха; срыв — сразу в бег или стойку.
    ///
    /// ВРЕМЯ — ТИК ПОКАЗА (sim.Tick − 2 + Alpha), тот же, по которому нарисовано тело и считает риг.
    /// В окне Sim отпускает ноги: идёт — ноги бегут слоем Recovery Footwork, верх держит позу маха.
    /// Корень (ArenaView → TryGetWreck2Body): поворот к направлению этапа за первые 3 тика замаха, без сдвига тела.
    ///
    /// Берётся, только если контроллер собран с Wreck4_* (RazlomPelagV5AnimatorBuilder.Wreck4) И риг взял серию
    /// (запечки v4 лежат): иначе — прежний путь WreckA/B/Finish_v5 + PelagAnchorSlamView, без смешения.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        private const float Wreck2EnterBlend = .035f;
        private const float Wreck2ExitBlend = .10f;
        private const float Wreck2CancelBlend = .08f;
        private const float Wreck2FacingRecoverySeconds = .28f;
        private const float Wreck2ShiftReleaseSeconds = .08f;
        private const float Wreck2LegsBlendSeconds = .08f;

        private static readonly int[] Wreck2States = Wreck2Hashes(false);
        private static readonly int[] Wreck2PhaseIds = Wreck2Hashes(true);

        private readonly PelagWreckFeed _wreck2Feed = new PelagWreckFeed();
        private int _wreck2Support = -1;
        private bool _wreck2Driven;
        private int _wreck2EnterFrame, _wreck2ReadTick, _wreck2BaseCheckFrame = -1;
        private PelagWreckClip _wreck2Clip;
        private PelagWreckPose _wreck2Pose;
        private Simulation _wreck2Sim;
        private bool _wreck2StowCued;
        private int _wreck2PoseFrame = -1;

        private static int[] Wreck2Hashes(bool parameters)
        {
            var hashes = new int[16];
            foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
                hashes[(int)clip] = Animator.StringToHash(parameters
                    ? PelagWreckClipRules.PhaseParameter(clip) : PelagWreckClipRules.StatePath(clip));
            return hashes;
        }

        private bool HasWreck2Clip(PelagWreckClip clip)
            => _animator.HasState(0, Wreck2States[(int)clip]) && HasAnimatorParameter(PelagWreckClipRules.PhaseParameter(clip));

        /// <summary>Контроллер собран с Крушением v4: пять клипов на базовом слое.</summary>
        private bool SupportsWreck2
        {
            get
            {
                if (_wreck2Support < 0 && _animator != null && _animator.runtimeAnimatorController != null)
                {
                    bool ok = true;
                    foreach (PelagWreckClip clip in PelagWreckClipRules.Required) ok &= HasWreck2Clip(clip);
                    _wreck2Support = ok ? 1 : 0;
                    if (!ok) Debug.LogWarning("[Разлом] Контроллер Пелага без Крушения v4 (Wreck4_*) — пересобери «Разлом/Собрать Pelag v5».", this);
                }
                return _wreck2Support == 1;
            }
        }

        /// <summary>Крушение v4 ведёт тело героя (с уборкой якоря после конца серии).</summary>
        public bool Wreck2Active => _wreck2Driven && !IsDead;

        /// <summary>
        /// Поза Крушения этого кадра для рига якоря: клип, кадр, темп (кадров на тик), было ли следующее нажатие, номер
        /// серии. false — лента не ведёт тело (риг тогда уходит в живую физику и на спину).
        /// </summary>
        public bool TryGetWreck4Pose(out PelagWreckClip clip, out float frame, out float rate, out bool nextStarted, out int serial)
        {
            clip = _wreck2Pose.Clip; frame = _wreck2Pose.Frame; rate = _wreck2Pose.Rate;
            nextStarted = _wreck2Pose.NextStarted; serial = _wreck2Feed.Timeline.Serial;
            return _wreck2Driven && !IsDead && clip != PelagWreckClip.None && _wreck2PoseFrame >= Time.frameCount - 1;
        }

        /// <summary>Смешивание тела при входе в клип этого кадра, тиков (0 — стык без смешивания): риг не пересевает сшивку на стыке.</summary>
        public float Wreck4EntryBlendTicks => _wreck2Pose.EntryBlendTicks;

        private float Wreck2Now(Simulation sim)
            => PelagSquallClipRules.ShownTick(sim.Tick, _cycloneDriver != null ? _cycloneDriver.Alpha : 0f);

        private bool Wreck2OwnsPresentation => _abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.WreckId;

        /// <summary>
        /// Этап Крушения (TryPlayTempoAbility: AbilityCast и ActionStageStarted). false — контроллер без Wreck4_*,
        /// серии в Sim нет или риг её не взял (нет запечек): тогда весь прежний путь.
        /// </summary>
        private bool TryBeginWreck2(Simulation sim)
        {
            if (_faction != Faction.Wole || IsDead || _animator == null || !SupportsWreck2) return false;
            WreckState s = sim.Wreck;
            if (s.Serial == 0 || s.Phase == WreckPhase.None) return false;
            // Следующее нажатие той же серии: лента уже идёт, этап она возьмёт из снимка.
            if (_wreck2Driven && _wreck2Feed.Timeline.Serial == s.Serial && _wreck2Sim == sim && Wreck2OwnsPresentation)
            {
                _tempoAbility = false;
                return true;
            }
            var rig = GetComponent<PelagAnchorRig>();
            if (rig == null || !rig.ClaimWreck())
            {
                Wreck2Trace($"rig declined serial={s.Serial}: прежний путь");
                return false;
            }
            StopAttackWarp();
            CancelUpperBodyAttack(.02f);
            ResetAbilityTriggers();
            _leapLocomotion = false;
            _attackPresentationActive = false;
            _abilityDefinitionId = AbilityDefinition.WreckId;
            _abilityPresentationActive = true;
            _abilityUsesLowerBodyLayer = false;
            _tempoAbility = false;
            SetCombatReady(true);
            if (_saberStanceLayer >= 0) _animator.SetLayerWeight(_saberStanceLayer, 0f);
            if (_saberFootworkLayer >= 0) _animator.SetLayerWeight(_saberFootworkLayer, 0f);

            _wreck2Feed.Begin(sim, !_locomotionMoving);
            _wreck2Driven = true;
            _wreck2Sim = sim;
            // События шага каста (SimulationTick = CastTick + 1) и позже.
            _wreck2ReadTick = s.CastTick;
            _wreck2EnterFrame = Time.frameCount;
            _wreck2Clip = PelagWreckClip.None;
            _wreck2Pose = default;
            _wreck2RecoverUntil = 0f;
            _wreck2ReleaseAt = -1f;
            _wreck2StowCued = false;
            Wreck2Trace($"begin serial={s.Serial} cast={s.CastTick} stage={s.Stage} contact={s.ContactTick} phase={s.Phase} simTick={sim.Tick}");
            UpdateWreck2Animation();
            return true;
        }

        private void UpdateWreck2Animation()
        {
            // Тело забрало действие на слоях (удар сабли после уборки): базовый слой не оставляем в клипе Крушения.
            if (_wreck2BaseCheckFrame >= 0 && Time.frameCount >= _wreck2BaseCheckFrame && _animator != null)
            {
                _wreck2BaseCheckFrame = -1;
                if (!IsDead && BaseInWreck2())
                    _animator.CrossFadeInFixedTime(_locomotionMoving ? RunState : CombatIdleState, Wreck2CancelBlend, 0, 0f);
            }
            if (!_wreck2Driven) return;
            Simulation sim = TempoSim;
            if (sim == null || sim != _wreck2Sim || IsDead || _animator == null) { ReleaseWreck2("lost", false); return; }
            if (!Wreck2OwnsPresentation) { ReleaseWreck2("replaced", true); return; }
            if (_wreck2Clip != PelagWreckClip.None && !BaseInWreck2() && Time.frameCount > _wreck2EnterFrame + 1)
            { ReleaseWreck2("base layer", false); return; }
            float now = Wreck2Now(sim);
            PelagWreckPose pose = SampleWreck2(sim, now);
            _wreck2Pose = pose;
            _wreck2PoseFrame = Time.frameCount;
            if (pose.Finished) { EndWreck2(pose); return; }
            ApplyWreck2Pose(pose, now);
            // Кости, которых клипы v4 не пишут, — в позе привязки клипов (иначе таз скручен на −15° от стойки сабли).
            HoldWreck4Bind();
            // Показ держится, пока ведёт лента (и уборка после конца серии); часы общего Update его не снимают.
            _abilityPresentationUntil = Time.time + .1f;
            _actionProtectedUntil = Time.time + .05f;
        }

        /// <summary>Снимок, события, корень: пошёл в окне — корень у ArenaView до следующего нажатия.</summary>
        private PelagWreckPose SampleWreck2(Simulation sim, float now)
        {
            ReadWreck2Events(sim);
            string tracked = _wreck2Feed.Track(sim);
            if (tracked != null) Wreck2Trace(tracked + $" shown={now:F2}");
            PelagWreckPose pose = _wreck2Feed.Timeline.Sample(now, Wreck2Scale);
            if (pose.LegsFree && _locomotionMoving && pose.RootOwned)
            {
                _wreck2Feed.Timeline.Replant(now);
                pose = _wreck2Feed.Timeline.Sample(now, Wreck2Scale);
            }
            return pose;
        }

        private float Wreck2Scale => PelagSquallClipRules.TimingToWorld(transform.lossyScale.y);

        private void ApplyWreck2Pose(in PelagWreckPose pose, float now)
        {
            int id = (int)pose.Clip;
            if (pose.Clip != _wreck2Clip)
            {
                int hash = Wreck2States[id];
                if (_wreck2Clip == PelagWreckClip.None) _animator.CrossFadeInFixedTime(hash, Wreck2EnterBlend, 0, 0f);
                else if (pose.EntryBlendTicks <= 0f) _animator.Play(hash, 0, 0f);
                else _animator.CrossFadeInFixedTime(hash, pose.EntryBlendTicks / Simulation.TicksPerSecond, 0, 0f);
                Wreck2Trace($"clip {_wreck2Clip}->{pose.Clip} frame={pose.Frame:F2} blend={pose.EntryBlendTicks:F1} shown={now:F2} "
                            + $"stage={pose.Stage} yaw={pose.Yaw:F1} rate={pose.Rate:F2}");
                _wreck2Clip = pose.Clip;
            }
            _animator.SetFloat(Wreck2PhaseIds[id], PelagWreckClipRules.Phase(pose.Clip, pose.Frame));
            if (pose.Clip == PelagWreckClip.Stow && pose.Frame >= PelagWreckClipRules.StowOnBackFrame && !_wreck2StowCued)
            {
                _wreck2StowCued = true;
                Wreck2Trace($"stow handle on back frame={pose.Frame:F2} shown={now:F2}");
            }
        }

        /// <summary>
        /// Ноги в окне — последним в Update (после общих весов слоёв): идущему — бег слоем Recovery Footwork.
        /// false — общая логика восстановления.
        /// </summary>
        private bool UpdateWreck2Footwork()
        {
            if (_animator == null || !_wreck2Driven || _recoveryFootworkLayer < 0) return false;
            float target = _wreck2Pose.LegsFree && _locomotionMoving ? 1f : 0f;
            _animator.SetLayerWeight(_recoveryFootworkLayer, Mathf.MoveTowards(
                _animator.GetLayerWeight(_recoveryFootworkLayer), target, Time.deltaTime / Wreck2LegsBlendSeconds));
            return true;
        }

        /// <summary>События Крушения из кадра. Читаются и из Update, и из LateUpdate (ArenaView) — дважды не берутся.</summary>
        private void ReadWreck2Events(Simulation sim)
        {
            if (_cycloneDriver == null || !_wreck2Driven) return;
            var events = _cycloneDriver.FrameEventContexts;
            int readTo = _wreck2ReadTick;
            for (int i = 0; i < events.Count; i++)
            {
                FrameEventContext context = events[i];
                if (context.SimulationTick <= _wreck2ReadTick) continue;
                if (context.SimulationTick > readTo) readTo = context.SimulationTick;
                string trace = _wreck2Feed.Apply(sim, context.Event, PelagSquallClipRules.EventTick(context.SimulationTick));
                if (trace != null) Wreck2Trace(trace);
            }
            _wreck2ReadTick = readTo;
        }

        /// <summary>Базовый слой в клипе Крушения v4 или идёт в него.</summary>
        private bool BaseInWreck2()
        {
            AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
            foreach (PelagWreckClip clip in PelagWreckClipRules.Required)
                if (Wreck2States[(int)clip] == info.fullPathHash) return true;
            return false;
        }

        /// <summary>Строка в журнал только под съёмкой (как [abordage-anim]): в игре молчит.</summary>
        private static void Wreck2Trace(string text)
        {
            if (CaptureRig.HasEnemyOverride) Debug.Log("[wreck-anim] t=" + Time.time.ToString("F3") + " " + text);
        }
    }
}
