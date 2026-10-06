using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Забег и выбег вместо порталов (владелец, 5 октября: «персонаж должен просто забегать в
    /// локацию и выбегать»).
    ///
    /// ЗАБЕГ — настоящий ход симуляции: первые IntroTicks тиков арены, пока игрок сам не отдал
    /// приказ, к вводу подмешивается приказ бежать IntroDistance метров от края поляны внутрь.
    /// Ноги, поворот и коллизии — те же, что у обычного бега. Съёмка забег не делает: её
    /// кадры рассчитаны на героя у входа.
    ///
    /// ВЫБЕГ — только представление: симуляция на выходе уже открыла выбор награды и стоит.
    /// ExitRunSeconds герой бежит дальше по тропе в лес (смещение в GetRenderPosition, ноги
    /// бегут — скорость тела в симуляции осталась прежней), экран награды и выбор ждут его.
    /// </summary>
    public sealed partial class TickDriver
    {
        private const int IntroTicks = 40;
        private const float IntroDistance = 5f;
        public const float ExitRunSeconds = .9f;
        private const float ExitRunSpeed = 5.5f;

        private int _introDepth = -1, _introUntilTick = -1;
        private FixVec2 _introTarget;
        private int _exitRunDepth = -1;
        private float _exitRunStart;
        private Vector3 _exitRunDirection;

        /// <summary>Идёт выбег: экран награды ещё закрыт, ввод выбора не принимается.</summary>
        public bool ExitRunPlaying => Session != null && Session.Mode == GameMode.Rift && Run != null
            && Run.Phase == RunPhase.ChoosingReward && _exitRunDepth == Run.Depth
            && Time.unscaledTime - _exitRunStart < ExitRunSeconds;

        private void ApplyArenaIntro(ref InputFrame frame)
        {
            if (Session == null || Session.Mode != GameMode.Rift || Run == null || Sim == null || CaptureRig.Installed) return;
            var map = Run.Map;
            if (map == null || map.Routes == null || !map.IsArena) return;
            if (_introDepth != Run.Depth)
            {
                _introDepth = Run.Depth;
                _introUntilTick = Sim.Tick + IntroTicks;
                _introTarget = map.EntryPoint + map.Routes.EntryFacing * Fix64.FromDouble(IntroDistance);
            }
            if (Sim.Tick >= _introUntilTick || Run.Phase != RunPhase.Clearing) return;
            // Свой приказ игрока — забег окончен.
            if (frame.Has(InputFlags.MoveOrder) || frame.AbilityMask != 0 || frame.Has(InputFlags.Attack))
            { _introUntilTick = -1; return; }
            frame.Aim = _introTarget;
            frame.Flags |= (byte)InputFlags.MoveOrder;
        }

        /// <summary>Смещение героя на выбеге; ноль — выбега нет.</summary>
        private Vector3 ExitRunOffset()
        {
            if (Session == null || Session.Mode != GameMode.Rift || Run == null || Run.Map == null || !Run.Map.IsArena)
                return Vector3.zero;
            if (Run.Phase != RunPhase.ChoosingReward) { if (Run.Phase != RunPhase.ReplacingAbility) _exitRunDepth = -1; return Vector3.zero; }
            if (_exitRunDepth != Run.Depth)
            {
                _exitRunDepth = Run.Depth;
                _exitRunStart = Time.unscaledTime;
                var map = Run.Map;
                var exit = map.ExitPoint(0);
                int cell = map.Routes != null ? map.Routes.CellAt(exit) : -1, parent = cell >= 0 ? map.Routes.ParentCell(cell) : -1;
                var direction = parent >= 0 ? exit - map.Routes.GetCell(parent).Center : map.Routes.EntryFacing;
                _exitRunDirection = new Vector3(direction.X.ToFloat(), 0, direction.Y.ToFloat()).normalized;
                // Тело смотрит туда, куда бежит.
                Sim.Entities.Facing[Simulation.PlayerId] = direction.Normalized();
            }
            float t = Mathf.Min(Time.unscaledTime - _exitRunStart, ExitRunSeconds);
            return _exitRunDirection * (t * ExitRunSpeed);
        }
    }
}
