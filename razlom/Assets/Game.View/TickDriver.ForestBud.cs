using UnityEngine;
using Game.Sim;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        public static bool ForestBudPlaytestRequested { get; } =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-forest-bud-playtest") >= 0;
        private bool _forestPlaytestStarted;
        private int _forestCaptureGeneration = -1, _forestCaptureStart;
        private FixVec2 _forestCaptureOrigin, _forestCaptureDodge;

        private void StartForestBudPlaytestIfRequested()
        {
            if (!ForestBudPlaytestRequested || _forestPlaytestStarted) return;
            _forestPlaytestStarted = true;
            Time.timeScale = 1f;
            StartForestBudTest(GetComponent<LayoutView>().Profile, 20260829UL);
        }

        private void PrepareForestBudRoster()
        {
            if (Sim == null) return;
            for (int i = 1; i < Sim.Entities.Count; i++)
                if (Sim.Entities.Kind[i] == EnemyKind.ForestBud)
                { GetComponent<ArenaView>().PrepareForestBud(); return; }
        }

        private void CaptureForestBudInput()
        {
            if (Sim == null) return;
            // В контрольную запись не попадают клавиши и клики пользователя из других окон.
            _pending = InputFrame.Empty; AttackHeld = false;
            _abilityLatch = 0; _commandLatch = 0;
            _abilityPressLatched = false; _pointerPressLatched = false; _targetAimSlot = -1;
            if (_forestCaptureGeneration != Generation)
            {
                _forestCaptureGeneration = Generation; _forestCaptureStart = Sim.Tick;
                _forestCaptureOrigin = Sim.Entities.Position[Simulation.PlayerId];
                _forestCaptureDodge = Sim.Entities.Count > 1
                    ? _forestCaptureOrigin + (Sim.Entities.Position[1] - _forestCaptureOrigin).Normalized() * Fix64.Ratio(11, 2)
                    : _forestCaptureOrigin;
            }
            int elapsed = Sim.Tick - _forestCaptureStart;
            if (CaptureRig.ForestBudCase == "dodge" && elapsed >= 52 && elapsed < 120)
            {
                _pending.Flags = (byte)InputFlags.MoveOrder;
                _pending.Aim = _forestCaptureDodge;
            }
            else if (CaptureRig.ForestBudCase == "approach" && elapsed >= 18 && elapsed < 100 && Sim.Entities.Count > 1)
            {
                _pending.Flags = (byte)InputFlags.MoveOrder;
                _pending.Aim = Sim.Entities.Position[1];
            }
            else if (CaptureRig.ForestBudCase == "kill" && elapsed >= 100 && Sim.Entities.Count > 1)
            {
                _pending.Flags = (byte)InputFlags.Attack; _pending.AttackTarget = 1;
                _pending.Aim = Sim.Entities.Position[1]; AttackHeld = true;
            }
        }

        public void StartForestBudTest(LocationTheme theme, ulong seed, int count = 1)
        {
            if (theme == null || theme.Gameplay == null)
                throw new System.ArgumentException("Для тестового боя нужен профиль лесной локации.");
            theme.Style.Validate();
            GetComponent<ArenaView>().PrepareForestBud();
            Session.StartForestBudTest(theme.Gameplay.ToDefinition(), seed, count);
            var layout = GetComponent<LayoutView>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_developerThemeActive) _normalTheme = layout.Profile;
            _developerThemeActive = true;
#endif
            layout.Configure(theme);
            ClearCapturedInput();
            SyncGeneration();
            Debug.Log("[forest-bud-test] Новый тестовый бой. F8: повтор или вернуться в лагерь.");
        }
    }
}
