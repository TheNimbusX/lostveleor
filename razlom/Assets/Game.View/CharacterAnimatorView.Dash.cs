using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Рывок Пелага (Simulation.Dash, 02.10) вместо кувырка: клип Pelag_AN_Dash
    /// на всё тело, время которого ведут тики Sim, а не темп аниматора.
    ///
    /// Клип собран в Blender так, что кадр равен тику рывка
    /// (ART/characters/pelag/dash-2026-10-02/animation/timing.json): 0–1 —
    /// замах, 2–5 — бросок, на 6 встаёт левая (передняя) нога, 6–12 —
    /// восстановление, кадр 12 — первый кадр удара 1 серии сабли.
    ///
    /// ТИК ПОКАЗА, А НЕ ТИК СОБЫТИЯ. Тело героя рисуется с отставанием на тик
    /// (TickDriver.GetRenderPosition: между двумя последними шагами), поэтому
    /// время клипа берётся по тому же тику, по которому нарисовано тело:
    /// sim.Tick − 2 + Alpha. Так тело едет на кадрах 0–6 и встаёт ровно в кадр
    /// постановки ноги, как клип и задуман; при тике события (−1, как у серии)
    /// нога вставала бы за тик до остановки и ехала по земле последние 0,67 м.
    /// Корону брызг и звук постановки PelagVfxController и CombatAudio дают
    /// в тот же миг показа, а не в миг события.
    ///
    /// Стена остановила раньше — бросок дожимается до постановки к тому тику
    /// показа, на котором встало тело: постановка всегда там, где DashEnded.
    /// Другая длительность рывка (талант) растягивает бросок — постановка
    /// тоже остаётся на конце рывка.
    /// </summary>
    public sealed partial class CharacterAnimatorView
    {
        // Сетка клипа, кадры (= тики рывка при длительности 6).
        private const float DashClipFrames = 12f;
        private const float DashPlantFrame = 6f;
        /// <summary>С этого кадра бегущему герою ноги отдаются бегу (слой Recovery Footwork), верх доигрывает.</summary>
        private const float DashLegsReleaseFrame = 7f;
        /// <summary>Узнали об остановке поздно (длинный кадр): до постановки не быстрее, чем за полтика.</summary>
        private const float DashMinCatchUpTicks = .5f;
        private const float DashEnterBlend = .035f;
        private const float DashExitBlend = .10f;
        /// <summary>Рывок сменило другое действие (удар серии, Вихрь): ноги и корпус уходят в бег или стойку.</summary>
        private const float DashCancelBlend = .08f;

        /// <summary>
        /// Фаза бега (Pelag_MX_Run, нормированное время), где левая нога
        /// встаёт впереди таза, а правая позади отрывается от земли — как на
        /// кадре 7 рывка (левая +0,38 м впереди таза на земле, правая −0,44 м
        /// позади). С неё ноги бегущего героя подхватывает бег: шаг
        /// продолжается, а не перескакивает. Замер — проба в редакторе 02.10
        /// на игровом теле: в беге на 0,28 левая +0,25 м и 0,06 м над землёй,
        /// правая −0,46 м и уходит вверх; полная опора левой — 0,33–0,68.
        /// </summary>
        private const float DashRunLeftPlantPhase = .28f;

        private static readonly int DashState = Animator.StringToHash("Base Layer.Dash_v5");
        private static readonly int DashPhaseId = Animator.StringToHash("DashPhase");
        private static readonly int RunState = Animator.StringToHash("Base Layer.Run_v5");
        private static readonly int CombatIdleState = Animator.StringToHash("Base Layer.CombatIdle_v5");
        private static readonly int RecoveryRunState = Animator.StringToHash("Recovery Footwork.RecoveryRun");

        private int _dashSupport = -1;
        private bool _dashDriven;
        private int _dashSerial, _dashStartTick, _dashEnterFrame;
        // Кадр, когда показ рывка забрало другое действие; −1 — не забирало.
        private int _dashReplacedFrame = -1;
        private float _dashTicks;
        private float _dashLastFrame;
        // Остановка раньше полной длительности: с какого тика показа и кадра дожимаем бросок; −1 — не было.
        private float _dashCutFrom = -1f, _dashCutFrame;
        private bool _dashLegsReleased;
        private float _dashLegsHoldUntil;

        /// <summary>Контроллер собран с рывком (RazlomPelagV5AnimatorBuilder, Dash_v5 и DashPhase).</summary>
        private bool SupportsDash
        {
            get
            {
                if (_dashSupport < 0 && _animator != null && _animator.runtimeAnimatorController != null)
                    _dashSupport = _animator.HasState(0, DashState) && HasAnimatorParameter("DashPhase") ? 1 : 0;
                return _dashSupport == 1;
            }
        }

        /// <summary>Тело ещё летит рывком (до постановки ноги): взгляд держит направление рывка.</summary>
        private bool DashFlying => _dashDriven && _dashLastFrame < DashPlantFrame;

        /// <summary>Тик, по которому сейчас нарисовано тело героя (см. описание класса).</summary>
        private float DashShownTick(Simulation sim) => sim.Tick - 2 + _cycloneDriver.Alpha;

        /// <summary>
        /// Начало рывка: клип входит с нуля, дальше время ведёт UpdateDashAnimation.
        /// false — контроллер без рывка или рывка в Sim нет: тогда старый кувырок.
        /// </summary>
        private bool BeginDash(Simulation sim)
        {
            if (sim == null || !SupportsDash) return false;
            PelagDashState dash = sim.PelagDash;
            if (dash.Serial == 0) return false;
            _dashDriven = true;
            _dashSerial = dash.Serial;
            _dashStartTick = dash.StartTick;
            _dashTicks = Mathf.Max(1, dash.InvulnerableUntilTick - dash.StartTick);
            _dashLastFrame = 0f;
            _dashCutFrom = -1f;
            _dashLegsReleased = false;
            _dashLegsHoldUntil = 0f;
            _dashEnterFrame = Time.frameCount;
            _dashReplacedFrame = -1;
            _animator.SetFloat(DashPhaseId, 0f);
            _animator.CrossFadeInFixedTime(DashState, DashEnterBlend, 0, 0f);
            DashTrace($"begin serial={dash.Serial} start={dash.StartTick} ticks={_dashTicks} simTick={sim.Tick}");
            UpdateDashAnimation();
            return true;
        }

        private void UpdateDashAnimation()
        {
            if (!_dashDriven) return;
            var sim = TempoSim;
            if (sim == null || IsDead || _animator == null) { _dashDriven = false; return; }
            // Базовый слой занял другой приём (Подсечка, смерть): рывок больше не ведём.
            if (!BaseInDash() && Time.frameCount > _dashEnterFrame + 1) { _dashDriven = false; return; }
            // Рывок сменило другое действие. Его вход в базовый слой (Подсечка, якорь)
            // аниматор примет только на своём обновлении, поэтому решаем через кадр:
            // базовый слой ушёл — проверка выше отпустит рывок молча; остался в клипе
            // рывка (удар серии, Вихрь — они на слоях) — низ и корпус уходят в бег
            // или стойку под ним.
            if (!(_abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.DashId))
            {
                if (_dashReplacedFrame < 0) _dashReplacedFrame = Time.frameCount;
                if (Time.frameCount > _dashReplacedFrame + 1) { EndDash(DashCancelBlend); return; }
            }
            PelagDashState dash = sim.PelagDash;
            // Новая арена (рывков ещё не было) или чужой номер — показ этого рывка кончен.
            if (dash.Serial != _dashSerial) { EndDash(DashCancelBlend); return; }

            float frame = Mathf.Max(DashClipFrame(in dash, DashShownTick(sim)), _dashLastFrame);
            if (_dashLastFrame < DashPlantFrame && frame >= DashPlantFrame)
                DashTrace($"plant frame={frame:F2} shown={DashShownTick(sim):F2} stop={dash.StopTick} cut={dash.CutShort}");
            _dashLastFrame = frame;
            if (frame >= DashClipFrames) { EndDash(DashExitBlend); return; }
            _animator.SetFloat(DashPhaseId, frame / DashClipFrames);
            if (_dashReplacedFrame >= 0) return;
            // Показ держится до конца восстановления; часы общего Update его не снимают.
            _abilityPresentationUntil = Time.time + (DashClipFrames - frame) / Simulation.TicksPerSecond + .05f;
            _actionProtectedUntil = Time.time + Mathf.Max(0f, DashPlantFrame - frame) / Simulation.TicksPerSecond;
        }

        /// <summary>
        /// Кадр клипа по тику показа: бросок [0, постановка] на отрезок рывка,
        /// восстановление после постановки — родным темпом (кадр на тик).
        /// </summary>
        private float DashClipFrame(in PelagDashState dash, float shown)
        {
            float elapsed = shown - _dashStartTick;
            if (elapsed <= 0f) return 0f;
            float plantAt = _dashTicks;
            float stopAt = dash.StopTick >= 0 ? dash.StopTick - _dashStartTick : -1f;
            if (stopAt >= 0f && stopAt < _dashTicks)
            {
                // Стена: о ней Sim сообщила за тик до того, как тело встанет на экране —
                // бросок дожимается к постановке ровно на этом тике показа.
                if (_dashCutFrom < 0f)
                {
                    _dashCutFrom = elapsed;
                    _dashCutFrame = Mathf.Clamp(Mathf.Max(_dashLastFrame, elapsed * DashPlantFrame / _dashTicks), 0f, DashPlantFrame);
                }
                plantAt = Mathf.Max(stopAt, _dashCutFrom + DashMinCatchUpTicks);
                if (elapsed < plantAt)
                    return Mathf.Lerp(_dashCutFrame, DashPlantFrame, Mathf.InverseLerp(_dashCutFrom, plantAt, elapsed));
            }
            else if (elapsed < plantAt) return elapsed * DashPlantFrame / plantAt;
            return DashPlantFrame + (elapsed - plantAt);
        }

        /// <summary>
        /// Рывок кончился (восстановление доиграно) или его сменили: базовый слой
        /// уходит в бег или боевую стойку. Бегущему — в ту же фазу шага, что уже
        /// идёт на слое ног, и слой держится, пока бег не проявится целиком.
        /// </summary>
        private void EndDash(float blend)
        {
            if (!_dashDriven) return;
            _dashDriven = false;
            if (_abilityPresentationActive && _abilityDefinitionId == AbilityDefinition.DashId)
            {
                _abilityPresentationActive = false;
                _abilityPresentationUntil = 0f;
                _actionProtectedUntil = 0f;
            }
            DashTrace($"end frame={_dashLastFrame:F2} blend={blend:F3} moving={_locomotionMoving} legs={_dashLegsReleased}");
            if (IsDead || _animator == null || !BaseInDash()) return;
            if (!_locomotionMoving)
            {
                _animator.CrossFadeInFixedTime(CombatIdleState, blend, 0, 0f);
                return;
            }
            float offset = 0f;
            if (_dashLegsReleased && _recoveryFootworkLayer >= 0
                && _animator.GetLayerWeight(_recoveryFootworkLayer) > .01f)
            {
                // length уже учитывает темп бега (LocomotionPlaybackSpeed), и смещение
                // CrossFadeInFixedTime берётся в тех же секундах: проба в редакторе
                // 02.10 — фаза бега на базовом слое совпала с фазой ног до 4-го знака
                // и при темпе 1, и при 1,4.
                AnimatorStateInfo legs = _animator.GetCurrentAnimatorStateInfo(_recoveryFootworkLayer);
                offset = Mathf.Repeat(legs.normalizedTime, 1f) * legs.length;
                _dashLegsHoldUntil = Time.time + blend;
            }
            _animator.CrossFadeInFixedTime(RunState, blend, 0, offset);
        }

        /// <summary>Базовый слой в клипе рывка или идёт в него; уходящий из него в чужое состояние — уже нет.</summary>
        private bool BaseInDash()
        {
            if (_animator.IsInTransition(0)) return _animator.GetNextAnimatorStateInfo(0).fullPathHash == DashState;
            return _animator.GetCurrentAnimatorStateInfo(0).fullPathHash == DashState;
        }

        /// <summary>
        /// Ноги в восстановлении рывка (слой Recovery Footwork): бегущему герою с
        /// кадра 7 — бег с фазы «левая нога впереди», стоящему — клип рывка.
        /// После конца рывка слой держится, пока базовый бег не проявится.
        /// true — слоем распорядились здесь.
        /// </summary>
        private bool UpdateDashFootwork()
        {
            if (!_dashDriven)
            {
                if (_dashLegsHoldUntil <= 0f) return false;
                if (Time.time < _dashLegsHoldUntil && !IsDead) return true;
                _dashLegsHoldUntil = 0f;
                return false;
            }
            bool run = _locomotionMoving && _dashLastFrame >= DashLegsReleaseFrame;
            float weight = _animator.GetLayerWeight(_recoveryFootworkLayer);
            if (run && !_dashLegsReleased)
            {
                _dashLegsReleased = true;
                _animator.Play(RecoveryRunState, _recoveryFootworkLayer, DashRunLeftPlantPhase);
                DashTrace($"legs to run frame={_dashLastFrame:F2}");
            }
            if (!run && weight <= .001f) _dashLegsReleased = false;
            _animator.SetLayerWeight(_recoveryFootworkLayer,
                Mathf.MoveTowards(weight, run ? 1f : 0f, Time.deltaTime / .08f));
            return true;
        }

        /// <summary>Строка в журнал только под съёмкой (как [dash-vfx]): в игре молчит.</summary>
        private static void DashTrace(string text)
        {
            if (CaptureRig.HasEnemyOverride) Debug.Log("[dash-anim] t=" + Time.time.ToString("F3") + " " + text);
        }

        private void ResetDash()
        {
            _dashDriven = false;
            _dashSupport = -1;
            _dashLastFrame = 0f;
            _dashCutFrom = -1f;
            _dashReplacedFrame = -1;
            _dashLegsReleased = false;
            _dashLegsHoldUntil = 0f;
        }
    }
}
