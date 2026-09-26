using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
#if UNITY_EDITOR
        public static string StonehoofReviewCase;
        private void CaptureStonehoofInput()
        {
            _pending = Game.Sim.InputFrame.Empty; AttackHeld = false; _abilityLatch = _commandLatch = 0;
            _abilityPressLatched = _pointerPressLatched = false; _targetAimSlot = -1;
            if (Sim == null || Sim.Entities.Count < 2) return;
            if (StonehoofReviewCase == "dodge" && Sim.TryGetStonehoofAction(1, out var action)
                && Sim.Tick >= action.StartTick + 12 && Sim.Tick < action.StopTick)
            {
                var side = new Game.Sim.FixVec2(-action.Direction.Y, action.Direction.X);
                _pending.Flags = (byte)Game.Sim.InputFlags.MoveOrder; _pending.Aim = action.Origin + action.Direction * Game.Sim.Fix64.FromInt(5) + side * Game.Sim.Fix64.FromInt(4);
            }
            if (StonehoofReviewCase == "death" && Sim.Tick > 100 && Sim.Entities.Alive[1])
            {
                Sim.Entities.Health[1] = 1;
                _pending.Flags = (byte)Game.Sim.InputFlags.Attack; _pending.AttackTarget = 1;
                _pending.Aim = Sim.Entities.Position[1]; AttackHeld = true;
            }
        }
#endif
        public void StartStonehoofTest(LocationTheme theme, ulong seed, int count = 1, bool obstacle = false)
        {
            if (theme == null || theme.Gameplay == null) throw new System.ArgumentException("Нужен профиль леса.");
            theme.Style.Validate(); GetComponent<ArenaView>().PrepareStonehoof();
            Session.StartStonehoofTest(theme.Gameplay.ToDefinition(), seed, count, obstacle);
            var layout = GetComponent<LayoutView>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_developerThemeActive) _normalTheme = layout.Profile;
            _developerThemeActive = true;
#endif
            layout.Configure(theme); ClearCapturedInput(); SyncGeneration();
            Debug.Log("[stonehoof-test] Камнекопыт: 180 HP, 30 урона, подготовка 1 с, разбег 12 м/с. Прогресс не сохраняется.");
        }
    }
}
