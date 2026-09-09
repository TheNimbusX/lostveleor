#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        private LocationTheme _normalTheme;
        private bool _developerThemeActive;

        public void StartDeveloperRift(LocationTheme theme, int level, bool nearBoss, ulong seed)
        {
            if (theme == null || theme.Gameplay == null) throw new System.ArgumentException("Выбери профиль локации.");
            theme.Style.Validate();
            var definition = theme.Gameplay.ToDefinition();
            var layout = GetComponent<LayoutView>();
            Session.StartDeveloperRift(definition, level, nearBoss, seed);
            if (!_developerThemeActive) _normalTheme = layout.Profile;
            _developerThemeActive = true;
            layout.Configure(theme);
            ClearCapturedInput();
            SyncGeneration();
        }

        private void RestoreDeveloperThemeIfNeeded()
        {
            if (!_developerThemeActive || Session == null || Session.IsDeveloperRun) return;
            GetComponent<LayoutView>().Configure(_normalTheme);
            _developerThemeActive = false;
        }
    }
}
#endif
