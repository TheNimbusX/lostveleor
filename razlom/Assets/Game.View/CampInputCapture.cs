using System;
using System.Collections;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Game.View
{
    // Только отдельный capture-процесс. Все действия после расстановки идут через очередь устройств.
    public sealed class CampInputCapture : MonoBehaviour
    {
        static CampInputCapture _instance;
        public static bool IsRunning => _instance != null && !_instance.Finished;
        public bool Finished { get; private set; }
        readonly StringBuilder _report = new StringBuilder();
        string _output;
        bool _passed = true, _settingsCaptured, _originalWasd, _offMapReported, _scenarioCompleted;
        bool _deviceHintCaptured, _originalUsingGamepad, _originalPadLastUsed;
        float _started;
        TickDriver _driver;
        CampPlayerView _player;
        CampServiceNpc _smith;
        ulong _campHash;
#if ENABLE_INPUT_SYSTEM
        Keyboard _keyboard, _previousKeyboard;
        Mouse _mouse, _previousMouse;
        Gamepad _pad, _previousPad;
        Vector2 _pointer = new Vector2(-20, -20);
#endif

        public void Initialize(string output)
        {
            var args = Environment.GetCommandLineArgs();
            if (!CaptureRig.Installed || Array.IndexOf(args, "-capture-camp") < 0
                || Array.IndexOf(args, "-capture-camp-input") < 0)
            { Finished = true; enabled = false; return; }
            _output = Path.GetFullPath(output);
            Directory.CreateDirectory(_output);
            _instance = this; _started = Time.realtimeSinceStartup;
        }

        IEnumerator Start()
        {
            if (Finished || _instance != this) yield break;
            try
            {
#if ENABLE_INPUT_SYSTEM
                _driver = FindAnyObjectByType<TickDriver>();
                _player = CampPlayerView.Instance;
                _originalWasd = GameUserSettings.WasdMovement; _settingsCaptured = true;
                _originalUsingGamepad = _driver.UsingGamepad; _originalPadLastUsed = TickDriver.GamepadLastUsed; _deviceHintCaptured = true;
                _previousKeyboard = Keyboard.current; _previousMouse = Mouse.current; _previousPad = Gamepad.current;
                _keyboard = InputSystem.AddDevice<Keyboard>("Camp input QA keyboard");
                _mouse = InputSystem.AddDevice<Mouse>("Camp input QA mouse");
                _pad = InputSystem.AddDevice<Gamepad>("Camp input QA gamepad");
                _keyboard.MakeCurrent(); _mouse.MakeCurrent(); _pad.MakeCurrent();
                Neutral();
                yield return new WaitForSecondsRealtime(.5f);
                if (!Check(_player != null && _player.Active && _player.WalkMap != null, "real camp and walk map ready")) yield break;
                _driver.Session.Camp.HashInto(ref _campHash);
                foreach (var npc in FindObjectsByType<CampServiceNpc>()) if (npc.Kind == CampServiceKind.Smith) _smith = npc;
                if (!Check(_smith != null, "Eni available on fresh profile")) yield break;
                if (!Check(FindSafePatch(out Vector3 patch), "safe bidirectional movement patch")) yield break;

                GameUserSettings.SetWasdMovement(true); Place(patch);
                yield return new WaitForSecondsRealtime(.25f);
                Vector3 start = Position;
                Keys(Key.W); yield return new WaitForSecondsRealtime(.65f);
                Keys(); yield return new WaitForSecondsRealtime(.2f);
                Check(PlanarDistance(start, Position) > 1f, "real W advances player");
                yield return Shot("input-wasd-forward.png");
                Keys(Key.S); yield return new WaitForSecondsRealtime(.65f);
                Keys(); yield return new WaitForSecondsRealtime(.3f);
                Check(PlanarDistance(start, Position) < .9f, "real S returns player");

                GameUserSettings.SetWasdMovement(false); Place(patch);
                yield return new WaitForSecondsRealtime(.25f); start = Position;
                Stick(1); yield return new WaitForSecondsRealtime(.65f);
                Stick(0); yield return new WaitForSecondsRealtime(.2f);
                Check(_driver.UsingGamepad && PlanarDistance(start, Position) > 1f, "real left stick advances player");
                yield return Shot("input-pad-forward.png");
                Stick(-1); yield return new WaitForSecondsRealtime(.65f);
                Stick(0); yield return new WaitForSecondsRealtime(.3f);
                Check(PlanarDistance(start, Position) < .9f, "real left stick returns player");

                Place(patch); yield return new WaitForSecondsRealtime(.25f);
                Vector3 goal = patch + Forward * 3f;
                yield return ClickWorld(goal, MouseButton.Right);
                yield return AwaitPosition(goal, "short real right click follows route");
                yield return Shot("input-mouse-forward.png");
                yield return ClickWorld(patch, MouseButton.Left);
                yield return AwaitPosition(patch, "real left click outside training returns along route");

                // Стик отменяет обычный маршрут мыши; после отпускания маршрут не возобновляется.
                Place(patch); yield return new WaitForSecondsRealtime(.25f);
                yield return ClickWorld(goal, MouseButton.Right);
                Stick(-1); yield return new WaitForSecondsRealtime(.35f);
                Stick(0); yield return new WaitForSecondsRealtime(.25f);
                Vector3 stopped = Position; yield return new WaitForSecondsRealtime(.65f);
                Check(PlanarDistance(stopped, Position) < .1f && PlanarDistance(goal, Position) > 1f,
                    "stick cancels mouse route without resuming after release");

                if (!Check(FindServiceStart(out Vector3 far), "reachable distant service test setup")) yield break;
                GameUserSettings.SetWasdMovement(true); Place(far);
                yield return new WaitForSecondsRealtime(.25f);
                yield return ClickService();
                Check(CampServicesView.Instance.Pending == _smith && !CampServicesView.Instance.IsOpen,
                    "real service right click starts approach in WASD mode");
                Keys(Key.W); yield return new WaitForSecondsRealtime(.35f); Keys();
                yield return new WaitForSecondsRealtime(.25f);
                Check(CampServicesView.Instance.Pending == null && !CampServicesView.Instance.IsOpen,
                    "W cancels service approach without opening shop");
                stopped = Position; yield return new WaitForSecondsRealtime(.45f);
                Check(PlanarDistance(stopped, Position) < .1f, "W cancellation does not resume service route");

                GameUserSettings.SetWasdMovement(false); Place(far);
                yield return new WaitForSecondsRealtime(.25f);
                yield return ClickService();
                Check(CampServicesView.Instance.Pending == _smith && !CampServicesView.Instance.IsOpen,
                    "real service right click starts approach in mouse mode");
                Stick(1); yield return new WaitForSecondsRealtime(.35f); Stick(0);
                yield return new WaitForSecondsRealtime(.25f);
                Check(CampServicesView.Instance.Pending == null && !CampServicesView.Instance.IsOpen,
                    "stick cancels service approach without opening shop");

                if (!Check(_player.TryServiceApproach(_smith, patch, out Vector3 near), "near Eni setup reachable")) yield break;
                Place(near); yield return new WaitForSecondsRealtime(.25f);
                int shopGold = _driver.Session.Camp.Money(CurrencyType.Gold);
                Keys(Key.E); yield return null; yield return null; Keys();
                yield return new WaitForSecondsRealtime(.2f);
                Check(CampServicesView.Instance.IsOpen && CampServicesView.Instance.Current == _smith
                    && _driver.Session.Mode == GameMode.Camp, "real E opens correct shop and stays in camp");
                yield return Shot("input-keyboard-shop.png");
                Pointer(new Vector2(6, 6), new Vector2(20, 0), MouseButton.Left, true);
                yield return null; yield return null;
                Pointer(new Vector2(6, 6), Vector2.zero, MouseButton.Left, false);
                yield return null;
                Check(!TickDriver.GamepadLastUsed, "mouse activity recognized inside open shop");
                Stick(1); yield return null; yield return null; Stick(0);
                yield return new WaitForSecondsRealtime(.15f);
                Check(CampServicesView.Instance.IsOpen && ValidShopFocus(), "mouse to pad inside open shop restores active focus");
                Check(_driver.Session.Camp.Money(CurrencyType.Gold) == shopGold, "device switch inside shop does not spend gold");
                yield return Shot("input-pad-in-open-shop.png");
                Keys(Key.Escape); yield return null; yield return null; Keys();
                yield return new WaitForSecondsRealtime(.3f);
                Check(!CampServicesView.Instance.IsOpen && !_player.InputBlocked, "real Escape closes shop and restores movement");

                Place(near); yield return new WaitForSecondsRealtime(.25f);
                Pointer(new Vector2(-20, -20), new Vector2(20, 0));
                yield return null; yield return null;
                Check(!TickDriver.GamepadLastUsed, "mouse is last device before first pad confirm");
                InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.South));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(_pad, new GamepadState());
                yield return new WaitForSecondsRealtime(.2f);
                Check(CampServicesView.Instance.IsOpen && CampServicesView.Instance.Current == _smith,
                    "first real pad A after mouse opens correct shop");
                Check(ValidShopFocus(), "pad opening sets valid active shop focus");
                Check(_driver.Session.Camp.Money(CurrencyType.Gold) == shopGold, "opening pad A does not spend gold");
                yield return Shot("input-pad-shop.png");
                InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.East));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(_pad, new GamepadState());
                yield return new WaitForSecondsRealtime(.3f);
                Check(!CampServicesView.Instance.IsOpen && !_player.InputBlocked, "real pad B closes shop and restores movement");
                yield return new WaitForSecondsRealtime(.3f);
                Check(_player.Body != null && PlanarDistance(_player.Body.position, Position) < .3f, "visible body finishes at simulation position");
                ulong after = 0; _driver.Session.Camp.HashInto(ref after);
                Check(after == _campHash, "input QA leaves camp profile unchanged");
                yield return Shot("input-finished.png");
                _report.AppendLine("SCOPE: virtual input queue, camp movement, route cancellation, E/A shop opening, Escape/B closing and opening focus. Physical controllers and every UI navigation direction remain owner playtest.");
                _scenarioCompleted = true;
#else
                Check(false, "Input System is unavailable");
#endif
            }
            finally
            {
                if (!_scenarioCompleted && _passed) Check(false, "input QA stopped before completing all checks");
                Finish();
            }
        }

        void Update()
        {
            if (Finished || _instance != this) return;
            if (Time.realtimeSinceStartup - _started > 90f)
            { Check(false, "input QA timed out at 90 seconds"); StopAllCoroutines(); Finish(); }
            if (!_offMapReported && _player?.WalkMap != null && _driver?.Session?.Mode == GameMode.Camp
                && !_player.WalkMap.Contains(CampTrainingView.Flat(Position)))
            { _offMapReported = true; Check(false, "player left walk map during real input"); }
        }

        Vector3 Position => _player.InteractionPosition;
        Vector3 Forward
        {
            get { var direction = TickDriver.CameraMovement(0, 1, Camera.main.transform.rotation); return new Vector3(direction.X.ToFloat(), 0, direction.Y.ToFloat()).normalized; }
        }
        bool FindSafePatch(out Vector3 point)
        {
            Vector3 origin = Position, forward = Forward;
            for (int ring = 0; ring <= 24; ring++) for (int i = 0; i < (ring == 0 ? 1 : 24); i++)
            {
                Vector3 at = origin + new Vector3(Mathf.Cos(i * Mathf.PI / 12), 0, Mathf.Sin(i * Mathf.PI / 12)) * ring;
                if (!_player.WalkMap.Contains(CampTrainingView.Flat(at))
                    || !_player.WalkMap.CanTravel(CampTrainingView.Flat(at - forward * 4), CampTrainingView.Flat(at + forward * 4))) continue;
                bool training = false;
                foreach (var dummy in FindObjectsByType<CampDummyView>())
                    if (PlanarDistance(at, dummy.TargetPosition) < 9f) { training = true; break; }
                var entrance = FindAnyObjectByType<CampRiftEntrance>();
                if (training || entrance != null && PlanarDistance(at, entrance.transform.position) < 9f) continue;
                point = at; return true;
            }
            point = origin; return false;
        }
        bool FindServiceStart(out Vector3 point)
        {
            for (float radius = 4.5f; radius <= 7.5f; radius += 1f) for (int i = 0; i < 32; i++)
            {
                Vector3 at = _smith.transform.position + new Vector3(Mathf.Cos(i * Mathf.PI / 16), 0, Mathf.Sin(i * Mathf.PI / 16)) * radius;
                if (_smith.Near(at) || !_player.WalkMap.Contains(CampTrainingView.Flat(at))
                    || !_player.WalkMap.CanTravel(CampTrainingView.Flat(at), CampTrainingView.Flat(at + Forward * 2))) continue;
                if (_player.TryServiceApproach(_smith, at, out _)) { point = at; return true; }
            }
            point = Position; return false;
        }
        void Place(Vector3 at)
        {
#if ENABLE_INPUT_SYSTEM
            Neutral();
#endif
            CampServicesView.Instance.CancelApproach();
            _driver.ClearCapturedInput(); _driver.Session.CampSim.StopPlayerMovement();
            _driver.Session.CampSim.Entities.Position[0] = CampTrainingView.Flat(at);
            var follow = FindAnyObjectByType<CameraFollow>();
            if (Camera.main != null && follow != null && !follow.enabled)
                Camera.main.transform.position = at + Vector3.up * .8f - Camera.main.transform.forward * 20f;
        }
        IEnumerator AwaitPosition(Vector3 target, string label)
        {
            float end = Time.realtimeSinceStartup + 4f;
            while (PlanarDistance(Position, target) > .45f && Time.realtimeSinceStartup < end) yield return null;
            Check(PlanarDistance(Position, target) <= .45f, label + " distance=" + PlanarDistance(Position, target).ToString("0.00"));
        }
        IEnumerator Shot(string filename)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_output, filename));
            yield return null;
        }
#if ENABLE_INPUT_SYSTEM
        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        void Stick(float y) => InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0, y) });
        void Pointer(Vector2 at, Vector2 delta, MouseButton button = MouseButton.Right, bool held = false)
        {
            _pointer = at;
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = at, delta = delta }.WithButton(button, held));
        }
        void Neutral()
        {
            Keys(); Stick(0); Pointer(new Vector2(-20, -20), Vector2.zero);
        }
        IEnumerator ClickWorld(Vector3 at, MouseButton button)
        {
            at.y = _player.SurfaceHeight(at.x, at.z);
            Vector2 screen = Camera.main.WorldToScreenPoint(at);
            Check(!_driver.PointerOverHud(screen), "world click projection outside HUD");
            Pointer(screen, screen - _pointer, button, true);
            yield return null; yield return null;
            Pointer(screen, Vector2.zero, button, false);
            yield return null;
        }
        IEnumerator ClickService()
        {
            Vector2 screen = Camera.main.WorldToScreenPoint(_smith.Shape.center);
            Check(!_driver.PointerOverHud(screen), "service projection outside HUD");
            Pointer(screen, screen - _pointer, MouseButton.Right, true);
            yield return null; yield return null;
            Pointer(screen, Vector2.zero);
            yield return null;
        }
#endif
        static float PlanarDistance(Vector3 a, Vector3 b) { var delta = a - b; delta.y = 0; return delta.magnitude; }
        bool ValidShopFocus()
        {
            var shop = FindAnyObjectByType<CampShopView>();
            var selection = EventSystem.current?.currentSelectedGameObject;
            var selected = selection != null ? selection.GetComponent<Selectable>() : null;
            return TickDriver.GamepadLastUsed && shop != null && selected != null && selected.isActiveAndEnabled
                && selected.IsInteractable() && selected.transform.IsChildOf(shop.Smith.Group.transform);
        }
        bool Check(bool condition, string reason)
        {
            _report.AppendLine((condition ? "PASS " : "FAIL ") + reason); _passed &= condition;
            if (!condition) Debug.LogError("[camp-input-qa] " + reason);
            else Debug.Log("[camp-input-qa] " + reason);
            return condition;
        }
        void Finish()
        {
            if (Finished) return;
            Finished = true;
#if ENABLE_INPUT_SYSTEM
            if (_keyboard != null && _keyboard.added) { InputState.Change(_keyboard, new KeyboardState()); InputSystem.RemoveDevice(_keyboard); }
            if (_mouse != null && _mouse.added) { InputState.Change(_mouse, new MouseState()); InputSystem.RemoveDevice(_mouse); }
            if (_pad != null && _pad.added) { InputState.Change(_pad, new GamepadState()); InputSystem.RemoveDevice(_pad); }
            if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent();
            if (_previousMouse != null && _previousMouse.added) _previousMouse.MakeCurrent();
            if (_previousPad != null && _previousPad.added) _previousPad.MakeCurrent();
#endif
            if (_deviceHintCaptured) _driver.RestoreCampInputCaptureDeviceHints(_originalUsingGamepad, _originalPadLastUsed);
            if (_settingsCaptured) GameUserSettings.SetWasdMovement(_originalWasd);
            if (_settingsCaptured) Check(GameUserSettings.WasdMovement == _originalWasd, "original movement setting restored");
            _driver?.ClearCapturedInput();
            _driver?.Session?.CampSim?.StopPlayerMovement();
            if (_output != null) File.WriteAllText(Path.Combine(_output, "input-qa.txt"), (_passed ? "PASS\n" : "FAIL\n") + _report);
        }
        void OnDestroy()
        {
            if (!Finished && _instance == this) Check(false, "input QA interrupted before completion");
            Finish(); if (_instance == this) _instance = null;
        }
    }
}
