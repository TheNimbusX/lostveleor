#if UNITY_EDITOR
using System;
using System.IO;
using System.Globalization;
using Game.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    // Флаг ставится до загрузки сессии: даже меню не читает настоящий профиль.
    [InitializeOnLoad]
    public static class CampBridgePolishPlaycheck
    {
        const string Flag = "CampBridgePolish.Check";
        const string OutputKey = "CampBridgePolish.Output";
        const string StartedKey = "CampBridgePolish.Started";
        const string MovementKey = "settings.controls.wasd";
        static CampBridgePolishPlaycheck()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        public static void Begin(string output, bool baselineOnly = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || SessionState.GetBool(Flag, false))
                throw new InvalidOperationException("Проверка требует остановленного редактора без компиляции.");
            string folder = Path.GetFullPath(output);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "harness.txt"), "START " + DateTime.UtcNow.ToString("O") + "\n");
            SessionState.SetString(OutputKey, folder);
            SessionState.SetBool("CampBridgePolish.BaselineOnly", baselineOnly);
            SessionState.SetBool("CampBridgePolish.Attached", false);
            SessionState.SetBool("CampBridgePolish.Wasd", GameUserSettings.WasdMovement);
            SessionState.SetBool("CampBridgePolish.WasdHadKey", PlayerPrefs.HasKey(MovementKey));
            SessionState.SetInt("CampBridgePolish.WasdKey", PlayerPrefs.GetInt(MovementKey, 0));
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        static void Poll()
        {
            if (!SessionState.GetBool(Flag, false) || EditorApplication.isCompiling) return;
            string output = SessionState.GetString(OutputKey, "");
            try
            {
                if (DateTime.TryParse(SessionState.GetString(StartedKey, ""), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var started) && (DateTime.UtcNow - started).TotalSeconds > 240)
                { Fail("timeout waiting for menu/camp/probe"); return; }
                if (!EditorApplication.isPlaying) return;
                var menu = Object.FindAnyObjectByType<MainMenuView>();
                if (MainMenuView.IsOpen)
                {
                    if (menu != null) menu.StartGame();
                    return;
                }
                var probe = Object.FindAnyObjectByType<CampBridgePolishProbe>();
                if (probe != null)
                {
                    if (probe.Finished)
                    {
                        File.AppendAllText(Path.Combine(output, "harness.txt"), "DONE\n");
                        EditorApplication.isPlaying = false;
                    }
                    return;
                }
                if (SessionState.GetBool("CampBridgePolish.Attached", false))
                { Fail("probe disappeared before completion"); return; }
                var player = CampPlayerView.Instance;
                var driver = Object.FindAnyObjectByType<TickDriver>();
                if (player == null || !player.Active || driver?.Session == null || driver.GameplayPaused) return;
                SessionState.SetBool("CampBridgePolish.Attached", true);
                probe = player.gameObject.AddComponent<CampBridgePolishProbe>();
                probe.Initialize(output, SessionState.GetBool("CampBridgePolish.BaselineOnly", false));
                var gameType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameType != null) EditorWindow.GetWindow(gameType).Focus();
            }
            catch (Exception exception) { Fail(exception.ToString()); }
        }

        static void Fail(string reason)
        {
            string output = SessionState.GetString(OutputKey, "");
            if (!string.IsNullOrEmpty(output))
                File.WriteAllText(Path.Combine(output, "harness-error.txt"), reason);
            var probe = Object.FindAnyObjectByType<CampBridgePolishProbe>();
            if (probe != null) probe.Abort(reason);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Restore();
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Flag, false)) Restore();
        }

        static void Restore()
        {
            GameUserSettings.SetWasdMovement(SessionState.GetBool("CampBridgePolish.Wasd", false));
            if (SessionState.GetBool("CampBridgePolish.WasdHadKey", false))
                PlayerPrefs.SetInt(MovementKey, SessionState.GetInt("CampBridgePolish.WasdKey", 0));
            else PlayerPrefs.DeleteKey(MovementKey);
            PlayerPrefs.Save();
            SessionState.SetBool(Flag, false);
            SessionState.SetBool("CampBridgePolish.Attached", false);
            string output = SessionState.GetString(OutputKey, "");
            if (!string.IsNullOrEmpty(output)) File.AppendAllText(Path.Combine(output, "harness.txt"), "EDIT MODE; settings restored; save guard cleared\n");
        }
    }
}
#endif
