using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Серия сабли Пелага (Simulation.SabreCombo, 01.10): три клипа, время
    /// которых ведётся тиками Sim, а не собственным темпом аниматора.
    ///
    /// Клипы собраны в Blender так, что кадр клипа равен тику при базовой
    /// скорости атаки (ART/characters/pelag/basic-attack-2026-10-01/animation):
    /// контакт удара 1 и 2 — кадр 4, добивающего — 7; конец удара — 8 / 8 / 14;
    /// дальше хвост, который доигрывает, только если серию не продолжили.
    /// При другой скорости атаки замах и восстановление растягиваются каждый
    /// на свой отрезок — контакт клипа всегда ложится на тик контакта.
    ///
    /// Стоп-кадр на контакте (ConfirmSabreContact) держит позу героя, Sim идёт
    /// дальше; после стопа восстановление догоняет и кончается к концу удара.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        // Сетка клипов при базовой скорости, кадры (= тики). Должна совпадать с
        // таблицей HITS сборщика b_author.py и с Simulation.SabreBaseContactTicks.
        private static readonly int[] SabreClipFrames = { 14, 14, 22 };

        private static readonly int[] SabreUpperStates =
        {
            Animator.StringToHash("UpperBody Combat.Sabre1_v5"),
            Animator.StringToHash("UpperBody Combat.Sabre2_v5"),
            Animator.StringToHash("UpperBody Combat.Sabre3_v5"),
        };
        private static readonly int[] SabreLowerStates =
        {
            Animator.StringToHash("LowerBody Combat.Lower_Sabre1_v5"),
            Animator.StringToHash("LowerBody Combat.Lower_Sabre2_v5"),
            Animator.StringToHash("LowerBody Combat.Lower_Sabre3_v5"),
        };
        private static readonly int[] SabrePhaseIds =
        {
            Animator.StringToHash("SabrePhase1"),
            Animator.StringToHash("SabrePhase2"),
            Animator.StringToHash("SabrePhase3"),
        };

        private int _sabreSupport = -1;
        private bool _sabreActive;
        private SabreSwingState _sabreSwing;
        // Стоп-кадр: с какого тика держать позу и сколько тиков; −1 — стопа нет.
        private float _sabreHoldFrom = -1f, _sabreHoldTicks;
        private float _sabreLastFrame;

        /// <summary>Контроллер собран с тремя ударами серии (RazlomPelagV5AnimatorBuilder).</summary>
        public bool SupportsSabreCombo
        {
            get
            {
                if (_sabreSupport >= 0) return _sabreSupport == 1;
                if (_animator == null || _upperBodyLayer < 0 || _lowerBodyLayer < 0) return false;
                _sabreSupport = 1;
                for (int hit = 0; hit < 3; hit++)
                    if (!_animator.HasState(_upperBodyLayer, SabreUpperStates[hit])
                        || !_animator.HasState(_lowerBodyLayer, SabreLowerStates[hit])
                        || !HasAnimatorParameter("SabrePhase" + (hit + 1))) _sabreSupport = 0;
                if (_sabreSupport == 0)
                    Debug.LogWarning("[Разлом] Контроллер Пелага без серии сабли — пересобери «Разлом/Собрать Pelag v5».", this);
                return _sabreSupport == 1;
            }
        }

        /// <summary>Идёт ли сейчас удар серии (для вида, звука и ВФХ).</summary>
        public bool SabreSwingActive => _sabreActive && !IsDead;

        /// <summary>
        /// Ноги добивающего ведёт клип, а не бег: в тики выпада Sim двигает тело,
        /// и без этого ArenaView принял бы сдвиг за ходьбу и включил бы бег.
        /// </summary>
        public bool SabreLungeLegs
        {
            get
            {
                if (!SabreSwingActive || _sabreSwing.LungeStartTick < 0) return false;
                var sim = TempoSim;
                if (sim == null) return false;
                float tick = sim.Tick - 1 + _cycloneDriver.Alpha;
                return tick >= _sabreSwing.LungeStartTick - 1 && tick <= _sabreSwing.ContactTick + 1;
            }
        }

        /// <summary>Начало удара серии: клип входит с нуля, время дальше ведёт UpdateSabreAnimation.</summary>
        public void PlaySabreSwing(in SabreSwingState swing)
        {
            if (_faction != Faction.Wole || IsDead || _animator == null) return;
            if (!SupportsSabreCombo) { PlayAttack(swing.Hit); return; }
            GetComponent<PelagAnchorSlamView>()?.Release();
            _basicComboActive = false;
            _abilityPresentationActive = false;
            _abilityPresentationUntil = 0f;
            _abilityUsesLowerBodyLayer = false;
            _leapLocomotion = false;
            ResetAbilityTriggers();
            StopAttackWarp();
            SetCombatReady(true);

            int hit = Mathf.Clamp(swing.Hit, 0, 2);
            // Удар из удара: переход короче, позы стыкуются (конец удара 1 —
            // начало удара 2 в тейке один кадр); с места — чуть мягче.
            float blend = _sabreActive ? .045f : .07f;
            _sabreSwing = swing;
            _sabreActive = true;
            _sabreHoldFrom = -1f;
            _sabreHoldTicks = 0f;
            _sabreLastFrame = 0f;
            _animator.SetFloat(SabrePhaseIds[hit], 0f);
            _animator.CrossFadeInFixedTime(SabreUpperStates[hit], blend, _upperBodyLayer, 0f);
            _animator.CrossFadeInFixedTime(SabreLowerStates[hit], blend, _lowerBodyLayer, 0f);
            _attackPresentationActive = true;
            _attackPresentationUntil = float.MaxValue;
            // До контакта лёгкие попадания по герою не ломают фазу клинка.
            _actionProtectedUntil = Time.time + (swing.ContactTick - swing.StartTick) / (float)Simulation.TicksPerSecond;
            UpdateSabreAnimation();
        }

        /// <summary>
        /// Контакт удара серии: стоп-кадр героя на seconds (0 — без стопа, промах).
        /// Sim идёт дальше; хвост восстановления потом догоняет.
        /// </summary>
        public void ConfirmSabreContact(int serial, float seconds)
        {
            if (!_sabreActive || _sabreSwing.Serial != serial || seconds <= 0f) return;
            _sabreHoldFrom = _sabreSwing.ContactTick;
            _sabreHoldTicks = Mathf.Max(_sabreHoldTicks, seconds * Simulation.TicksPerSecond);
        }

        private void UpdateSabreAnimation()
        {
            if (!_sabreActive) return;
            var sim = TempoSim;
            if (sim == null || IsDead || _animator == null) { EndSabre(.08f, cancel: true); return; }
            SabreSwingState live = sim.SabreSwing;
            // Удар снят (уход, способность, оглушение) — клинок отпускается сразу.
            if (live.Serial == _sabreSwing.Serial && live.Interrupted) { EndSabre(.05f, cancel: true); return; }
            // Старт следующего удара приходит событием; если кадр его проглотил —
            // подхватываем по номеру, чтобы клинок не застыл на хвосте прежнего.
            if (live.Serial > _sabreSwing.Serial && live.ActiveAt(sim.Tick)) { PlaySabreSwing(live); return; }

            float tick = sim.Tick - 1 + _cycloneDriver.Alpha;
            int hit = Mathf.Clamp(_sabreSwing.Hit, 0, 2);
            float frame = SabreClipFrame(in _sabreSwing, hit, tick);
            // Кадр клипа не идёт назад: после стопа восстановление догоняет, а не перематывает.
            frame = Mathf.Max(frame, _sabreLastFrame);
            _sabreLastFrame = frame;
            int length = SabreClipFrames[hit];
            if (frame >= length) { EndSabre(.16f, cancel: false); return; }
            // Хвост после конца удара не держит бегущего героя: верх отдаётся бегу.
            if (_locomotionMoving && tick >= _sabreSwing.EndTick + .5f) { EndSabre(.12f, cancel: false); return; }
            _animator.SetFloat(SabrePhaseIds[hit], frame / length);
        }

        /// <summary>
        /// Кадр клипа по тику: замах на отрезок [0, контакт], восстановление на
        /// [контакт, конец] — каждый со своим масштабом, хвост родным темпом.
        /// </summary>
        private float SabreClipFrame(in SabreSwingState swing, int hit, float tick)
        {
            float windup = Mathf.Max(1, swing.ContactTick - swing.StartTick);
            float cycle = Mathf.Max(windup + 1, swing.EndTick - swing.StartTick);
            float baseContact = Simulation.SabreBaseContactTicks(hit);
            float baseEnd = Simulation.SabreBaseCycleTicks(hit);
            float elapsed = tick - swing.StartTick;
            if (_sabreHoldFrom >= 0f)
            {
                float from = _sabreHoldFrom - swing.StartTick;
                float until = from + _sabreHoldTicks;
                if (elapsed >= from && elapsed < until) elapsed = from;
                else if (elapsed >= until && elapsed < cycle)
                    elapsed = Mathf.Lerp(from, cycle, Mathf.InverseLerp(until, cycle, elapsed));
            }
            if (elapsed <= windup) return Mathf.Max(0f, elapsed) * baseContact / windup;
            if (elapsed <= cycle) return baseContact + (elapsed - windup) * (baseEnd - baseContact) / (cycle - windup);
            return baseEnd + (elapsed - cycle);
        }

        private void EndSabre(float blend, bool cancel)
        {
            if (!_sabreActive) return;
            _sabreActive = false;
            _sabreHoldFrom = -1f;
            if (cancel) { CancelUpperBodyAttack(blend); return; }
            _attackPresentationActive = false;
            _attackPresentationUntil = 0f;
            ReleaseUpperBodyToLocomotion(blend);
        }

        private void ResetSabre()
        {
            _sabreActive = false;
            _sabreHoldFrom = -1f;
            _sabreHoldTicks = 0f;
            _sabreSwing = default;
            _sabreSupport = -1;
        }
    }
}
