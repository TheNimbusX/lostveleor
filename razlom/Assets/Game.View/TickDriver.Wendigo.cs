using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        public static string WendigoReviewCase { get; set; }
        private int _wendigoCaptureGeneration = -1, _wendigoCaptureStart;
        private Game.Sim.FixVec2 _wendigoCaptureCentre;
        private void CaptureWendigoInput()
        {
            _pending = Game.Sim.InputFrame.Empty; AttackHeld = false; _abilityLatch = _commandLatch = 0;
            _abilityPressLatched = _pointerPressLatched = false; _targetAimSlot = -1;
            if (_wendigoCaptureGeneration != Generation)
            {
                _wendigoCaptureGeneration = Generation; _wendigoCaptureStart = Sim.Tick;
                _wendigoCaptureCentre = (Sim.Entities.Position[0] + Sim.Entities.Position[1]) * Game.Sim.Fix64.Ratio(1,2);
            }
            string reviewCase = WendigoReviewCase ?? CaptureRig.ForestBudCase;
            if (reviewCase == "walk-r04")
            {
                // Только маршрут контрольной записи; обычное управление не меняется.
                int corner = ((Sim.Tick - _wendigoCaptureStart) / 60) % 4;
                var offset = corner == 0 ? new Game.Sim.FixVec2(-Game.Sim.Fix64.FromInt(3), Game.Sim.Fix64.FromInt(3))
                    : corner == 1 ? new Game.Sim.FixVec2(Game.Sim.Fix64.FromInt(3), Game.Sim.Fix64.FromInt(3))
                    : corner == 2 ? new Game.Sim.FixVec2(Game.Sim.Fix64.FromInt(3), -Game.Sim.Fix64.FromInt(3))
                    : new Game.Sim.FixVec2(-Game.Sim.Fix64.FromInt(3), -Game.Sim.Fix64.FromInt(3));
                _pending.Flags = (byte)Game.Sim.InputFlags.MoveOrder;
                _pending.Aim = _wendigoCaptureCentre + offset;
            }
            if (Sim.TryGetWendigoAction(1, out var action) && reviewCase == "dodge"
                && Sim.Tick > action.StartTick + 10 && Sim.Tick < action.ImpactTick)
            {
                _pending.Flags = (byte)Game.Sim.InputFlags.MoveOrder;
                var side = new Game.Sim.FixVec2(-action.Direction.Y, action.Direction.X);
                _pending.Aim = action.Target + side * Game.Sim.Fix64.FromInt(4);
            }
            if (reviewCase == "kill" && Sim.Tick - _wendigoCaptureStart > 180)
            {
                _pending.Flags = (byte)Game.Sim.InputFlags.Attack; _pending.AttackTarget = 1;
                _pending.Aim = Sim.Entities.Position[1]; AttackHeld = true;
            }
        }

        public void StartWendigoTest(LocationTheme theme, ulong seed, bool withPack = false)
        {
            if (theme == null || theme.Gameplay == null) throw new System.ArgumentException("Нужен профиль лесной локации.");
            theme.Style.Validate(); GetComponent<ArenaView>().PrepareWendigo();
            Session.StartWendigoTest(theme.Gameplay.ToDefinition(), seed, withPack);
            var layout = GetComponent<LayoutView>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_developerThemeActive) _normalTheme = layout.Profile;
            _developerThemeActive = true;
#endif
            layout.Configure(theme); ClearCapturedInput(); SyncGeneration();
            Debug.Log("[wendigo-test] Вендиго: когти и охотничий прыжок. Разработческий бой не меняет прогресс.");
        }
    }
}
