#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Game.View
{
    // Только временный Play-стенд: не правит сцену, препятствия или карту.
    public sealed class CampBridgePolishProbe : MonoBehaviour
    {
        public bool Finished { get; private set; }
        readonly StringBuilder _report = new StringBuilder();
        string _output;
        bool _baselineOnly, _passed = true, _initialized, _restored, _offMap, _originalWasd, _completed;
        bool _originalUsingGamepad, _originalPadLastUsed, _cameraCaptured, _followEnabled, _juiceEnabled;
        TickDriver _driver;
        CampPlayerView _player;
        CampRiverPassage _passage;
        Camera _camera;
        CameraFollow _follow;
        CombatCameraJuice _juice;
        Vector3 _cameraPosition;
        Quaternion _cameraRotation;
        float _cameraSize, _started;
        ulong _campHash;
        string[] _saveBefore;
#if ENABLE_INPUT_SYSTEM
        Keyboard _keyboard, _previousKeyboard;
        Mouse _mouse, _previousMouse;
        Gamepad _pad, _previousPad;
        Vector2 _pointer = new Vector2(-20, -20);
#endif

        public void Initialize(string output, bool baselineOnly)
        {
            if (!UnityEditor.SessionState.GetBool("CampBridgePolish.Check", false))
                throw new InvalidOperationException("Isolated camp save guard is required before Play.");
            _output = Path.GetFullPath(output);
            _baselineOnly = baselineOnly;
            _started = Time.realtimeSinceStartup;
            _initialized = true;
        }

        IEnumerator Start()
        {
            if (!_initialized) yield break;
            try
            {
                _driver = GetComponent<TickDriver>();
                _player = CampPlayerView.Instance;
                _passage = FindAnyObjectByType<CampRiverPassage>();
                float readyEnd = Time.realtimeSinceStartup + 20;
                while ((_player == null || _player.WalkMap == null || _player.Body == null || MainMenuView.IsOpen)
                    && Time.realtimeSinceStartup < readyEnd) yield return null;
                if (!Check(_player != null && _player.WalkMap != null && _player.Body != null
                    && _passage != null && _passage.Bridge != null, "real camp, walk map, bridge and hero ready")) yield break;
                _saveBefore = SaveSignatures();
                _originalWasd = GameUserSettings.WasdMovement;
                _originalUsingGamepad = _driver.UsingGamepad;
                _originalPadLastUsed = TickDriver.GamepadLastUsed;
                _camera = Camera.main;
                if (!Check(_camera != null, "existing game camera ready")) yield break;
                _follow = _camera.GetComponent<CameraFollow>();
                _juice = _camera.GetComponent<CombatCameraJuice>();
                _cameraPosition = _camera.transform.position; _cameraRotation = _camera.transform.rotation;
                _cameraSize = _camera.orthographicSize; _cameraCaptured = true;
                _followEnabled = _follow != null && _follow.enabled; _juiceEnabled = _juice != null && _juice.enabled;
                if (_follow != null) _follow.enabled = false;
                if (_juice != null) _juice.enabled = false;
                var b = _passage.BridgeBounds;
                _camera.transform.position = new Vector3(b.center.x, _player.GroundHeight + .8f, b.center.z) - _camera.transform.forward * 20;
                yield return new WaitForSecondsRealtime(.4f);
                Snapshot();
                AuditWaterAndRailings();
                yield return Shot("01-bridge.png");
                if (_baselineOnly) { _completed = true; yield break; }
#if ENABLE_INPUT_SYSTEM
                _previousKeyboard = Keyboard.current; _previousMouse = Mouse.current; _previousPad = Gamepad.current;
                _keyboard = InputSystem.AddDevice<Keyboard>("Camp bridge QA keyboard");
                _mouse = InputSystem.AddDevice<Mouse>("Camp bridge QA mouse");
                _pad = InputSystem.AddDevice<Gamepad>("Camp bridge QA pad");
                _keyboard.MakeCurrent(); _mouse.MakeCurrent(); _pad.MakeCurrent(); Neutral();
                // Открытия меняются только в новой изолированной сессии, чтобы добраться до Лео.
                while (_driver.Session.Camp.AttemptCount < 3) _driver.Session.Camp.RecordRealAttemptEnded(1, 0);
                yield return new WaitForSecondsRealtime(.5f);
                _driver.Session.Camp.HashInto(ref _campHash);
                var near = new Vector3(b.center.x, _player.GroundHeight, b.max.z + .8f);
                var far = new Vector3(b.center.x, _player.GroundHeight, b.min.z - .8f);
                GameUserSettings.SetWasdMovement(false);
                Check(_player.RouteTo(near), "route from spawn to near bank starts");
                yield return AwaitPosition(near, "actual route arrives at near bank", 30);
                yield return Shot("02-near-bank.png");
                yield return MouseWalk(far, "mouse crosses to far bank");
                yield return Shot("03-mouse-far-bank.png");
                yield return MouseWalk(near, "mouse returns across bridge");
                yield return WasdWalk(far, "WASD crosses to far bank");
                yield return Shot("04-wasd-far-bank.png");
                yield return WasdWalk(near, "WASD returns across bridge");
                GameUserSettings.SetWasdMovement(false); Neutral();
                ulong movementHash = 0; _driver.Session.Camp.HashInto(ref movementHash);
                Check(movementHash == _campHash, "mouse/WASD movement preserves isolated camp profile before Leo");
                CampServiceNpc leo = null;
                foreach (var npc in FindObjectsByType<CampServiceNpc>())
                    if (npc.Kind == CampServiceKind.Alchemist) leo = npc;
                if (Check(leo != null && leo.isActiveAndEnabled, "Leo unlocked on isolated three-attempt profile"))
                {
                    // Первый разговор открывает знакомство и заказы: ожидаем этот переход,
                    // а остальные данные проверяем по независимой копии профиля.
                    var camp = _driver.Session.Camp;
                    var expected = CampSaveCodec.Decode(CampSaveCodec.Encode(camp), camp.Items);
                    expected.MeetAlchemist();
                    ulong expectedHash = 0; expected.HashInto(ref expectedHash);
                    Check(CampServicesView.Instance.Begin(leo), "ordinary approach to Leo starts");
                    float end = Time.realtimeSinceStartup + 30;
                    while (!CampServicesView.Instance.IsOpen && Time.realtimeSinceStartup < end) yield return null;
                    Check(CampServicesView.Instance.IsOpen && CampServicesView.Instance.Current == leo
                        && leo.Near(_player.InteractionPosition), "walking to Leo opens the matching service nearby");
                    yield return Shot("05-leo.png");
                    CampServicesView.Instance.Close();
                    ulong interactionHash = 0; camp.HashInto(ref interactionHash);
                    Check(camp.HasMetAlchemist && interactionHash == expectedHash,
                        "first Leo interaction changes only expected acquaintance and order availability");
                }
                Check(_player.Body != null && PlanarDistance(_player.Body.position, Position) < .35f,
                    "visible hero follows simulation position");
                _completed = true;
#else
                Check(false, "Input System required for actual mouse/WASD queue checks");
#endif
            }
            finally { Finish(); }
        }

        Vector3 Position => _player.InteractionPosition;
        static float PlanarDistance(Vector3 a, Vector3 b) { var d = a - b; d.y = 0; return d.magnitude; }
        bool Check(bool value, string label)
        {
            _passed &= value;
            _report.AppendLine((value ? "PASS " : "FAIL ") + label);
            return value;
        }
        void Update()
        {
            if (!_initialized || Finished) return;
            if (Time.realtimeSinceStartup - _started > 200) { Abort("probe timeout"); return; }
            if (!_offMap && _player?.WalkMap != null && _player.Active
                && !_player.WalkMap.Contains(CampTrainingView.Flat(Position)))
            { _offMap = true; Check(false, "hero left walk map"); }
        }

        IEnumerator AwaitPosition(Vector3 goal, string label, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (PlanarDistance(Position, goal) > .3f && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            Check(PlanarDistance(Position, goal) <= .45f, label + " distance=" + F(PlanarDistance(Position, goal)));
        }
#if ENABLE_INPUT_SYSTEM
        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        void Pointer(Vector2 at, bool down)
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = at, delta = at - _pointer }.WithButton(MouseButton.Right, down));
            _pointer = at;
        }
        void Neutral()
        {
            Keys(); Pointer(new Vector2(-20, -20), false);
            InputSystem.QueueStateEvent(_pad, new GamepadState());
        }
        IEnumerator MouseWalk(Vector3 goal, string label)
        {
            Neutral(); GameUserSettings.SetWasdMovement(false);
            _driver.ClearCapturedInput(); _driver.Session.CampSim.StopPlayerMovement();
            yield return null;
            goal.y = _player.SurfaceHeight(goal.x, goal.z);
            Vector3 projected = _camera.WorldToScreenPoint(goal);
            if (!Check(projected.z > 0 && projected.x >= 0 && projected.x < Screen.width
                && projected.y >= 0 && projected.y < Screen.height && !_driver.PointerOverHud(projected), label + " click lies in visible world")) yield break;
            Pointer(projected, true); yield return null; yield return null;
            Pointer(projected, false); yield return null;
            yield return AwaitPosition(goal, label, 12);
        }
        IEnumerator WasdWalk(Vector3 goal, string label)
        {
            Neutral(); GameUserSettings.SetWasdMovement(true);
            CampServicesView.Instance.CancelApproach();
            _driver.ClearCapturedInput(); _driver.Session.CampSim.StopPlayerMovement();
            float end = Time.realtimeSinceStartup + 12;
            bool sent = false;
            while (PlanarDistance(Position, goal) > .22f && Time.realtimeSinceStartup < end)
            {
                Vector3 wanted = goal - Position; wanted.y = 0; wanted.Normalize();
                int bestX = 0, bestY = 0; float best = -2;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    var flat = TickDriver.CameraMovement(x, y, _camera.transform.rotation);
                    float dot = Vector3.Dot(new Vector3(flat.X.ToFloat(), 0, flat.Y.ToFloat()).normalized, wanted);
                    if (dot > best) { best = dot; bestX = x; bestY = y; }
                }
                var keys = new List<Key>(2);
                if (bestX != 0) keys.Add(bestX > 0 ? Key.D : Key.A);
                if (bestY != 0) keys.Add(bestY > 0 ? Key.W : Key.S);
                Keys(keys.ToArray()); sent = true; yield return null;
            }
            Keys(); yield return new WaitForSecondsRealtime(.25f);
            Check(sent && PlanarDistance(Position, goal) <= .45f, label + " distance=" + F(PlanarDistance(Position, goal)));
        }
#endif
        IEnumerator Shot(string filename)
        {
            yield return null; yield return null;
            string path = Path.Combine(_output, filename);
            ScreenCapture.CaptureScreenshot(path);
            float end = Time.realtimeSinceStartup + 5;
            while (!File.Exists(path) && Time.realtimeSinceStartup < end) yield return null;
            Check(File.Exists(path), "runtime screenshot " + filename);
        }

        void AuditWaterAndRailings()
        {
            var river = _passage.GetComponent<CampRiver>();
            int waterLeaks = 0, railLeaks = 0;
            for (float x = -40; x < 40; x += .25f)
            {
                var point = river.transform.TransformPoint(new Vector3(x, 0, river.CentreAt(x)));
                if (!_passage.IsOpen(river, point) && _player.WalkMap.Contains(CampTrainingView.Flat(point))) waterLeaks++;
            }
            var b = _passage.BridgeBounds;
            for (float z = b.min.z + .2f; z < b.max.z - .2f; z += .2f)
                for (int side = -1; side <= 1; side += 2)
                {
                    var inside = CampTrainingView.Flat(new Vector3(b.center.x, 0, z));
                    var outside = CampTrainingView.Flat(new Vector3(b.center.x + side * (b.extents.x + .6f), 0, z));
                    var rail = CampTrainingView.Flat(new Vector3(b.center.x + side * (_passage.CrossingSize.x * .5f + .1f), 0, z));
                    if (_player.WalkMap.Contains(rail) || _player.WalkMap.CanTravel(inside, outside)) railLeaks++;
                }
            Check(waterLeaks == 0, "water blocked outside passage; leaks=" + waterLeaks);
            Check(railLeaks == 0, "railings block both sides; leaks=" + railLeaks);
            foreach (float lane in new[] { -.6f, 0f, .6f })
            {
                var near = CampTrainingView.Flat(new Vector3(b.center.x + lane, 0, b.max.z + 1));
                var far = CampTrainingView.Flat(new Vector3(b.center.x + lane, 0, b.min.z - 1));
                Check(_player.WalkMap.CanTravel(near, far) && _player.WalkMap.CanTravel(far, near), "continuous lane both ways x=" + F(lane));
            }
        }

        void Snapshot()
        {
            var map = _player.WalkMap;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(CampWalkMap);
            var cells = (bool[])type.GetField("_cells", flags).GetValue(map);
            var origin = (FixVec2)type.GetField("_origin", flags).GetValue(map);
            int width = (int)type.GetField("_width", flags).GetValue(map), height = (int)type.GetField("_height", flags).GetValue(map);
            var bytes = new byte[cells.Length];
            for (int i = 0; i < cells.Length; i++) bytes[i] = cells[i] ? (byte)1 : (byte)0;
            File.WriteAllBytes(Path.Combine(_output, "walkmap.bin"), bytes);
            var lines = new List<string>();
            foreach (var footprint in _player.NavigationFootprints)
            {
                var source = CampNavigationGeometry.Source(footprint, _player.GroundHeight);
                var line = new StringBuilder(HierarchyPath(footprint.Root));
                line.Append('|').Append(footprint.Role).Append('|').Append(source.shape).Append('|').Append(source.area);
                line.Append('|').Append(Vec(source.size));
                for (int i = 0; i < 16; i++) line.Append('|').Append(F(source.transform[i]));
                lines.Add(line.ToString());
            }
            lines.Sort(StringComparer.Ordinal);
            string sources = string.Join("\n", lines);
            File.WriteAllText(Path.Combine(_output, "navigation.txt"), sources);
            var b = _passage.BridgeBounds;
            var snapshot = new StringBuilder();
            snapshot.AppendLine("walkmap.sha256=" + Digest(bytes));
            snapshot.AppendLine("origin.raw=" + origin.X.Raw + "," + origin.Y.Raw + " cell.raw=" + map.CellSize.Raw + " width=" + width + " height=" + height);
            snapshot.AppendLine("walkable=" + map.WalkableCellCount + " components=" + map.ComponentCount);
            snapshot.AppendLine("sources.sha256=" + Digest(Encoding.UTF8.GetBytes(sources)) + " count=" + lines.Count);
            snapshot.AppendLine("bridge.center=" + Vec(b.center) + " size=" + Vec(b.size) + " crossing=" + F(_passage.CrossingSize.x) + "," + F(_passage.CrossingSize.y));
            snapshot.AppendLine("crossingCentre=" + Vec(_passage.CrossingCentre) + " farBank.center=" + Vec(_passage.FarBank.center) + " farBank.size=" + Vec(_passage.FarBank.size));
            snapshot.AppendLine("camera.rotation=" + Vec(_camera.transform.eulerAngles) + " size=" + F(_camera.orthographicSize) + " pixel=" + Screen.width + "x" + Screen.height);
            File.WriteAllText(Path.Combine(_output, "snapshot.txt"), snapshot.ToString());
            File.WriteAllText(Path.Combine(_output, "route-audit.txt"), CampRouteAudit.Report(true));
            var heights = new StringBuilder("x,z,y\n");
            for (int i = 0; i <= 64; i++) foreach (float dx in new[] { -.6f, 0f, .6f })
            {
                float z = Mathf.Lerp(b.min.z - .5f, b.max.z + .5f, i / 64f), x = b.center.x + dx;
                heights.AppendLine(F(x) + "," + F(z) + "," + F(_player.SurfaceHeight(x, z)));
            }
            File.WriteAllText(Path.Combine(_output, "bridge-heights.csv"), heights.ToString());
        }

        static string HierarchyPath(Transform root)
        {
            string path = root.name;
            for (var parent = root.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Vec(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string[] SaveSignatures()
        {
            var names = new[] { "camp-v1.sav", "camp-v1.sav.bak", "camp-v1.sav.tmp" };
            var values = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(Application.persistentDataPath, names[i]);
                values[i] = File.Exists(path) ? Digest(File.ReadAllBytes(path)) : "missing";
            }
            return values;
        }
        public void Abort(string reason)
        {
            Check(false, reason); StopAllCoroutines(); Finish();
        }
        void Finish()
        {
            if (Finished) return;
            if (!_completed && _passed) Check(false, "probe stopped before completing all scenarios");
            Restore();
            if (_saveBefore != null)
            {
                var after = SaveSignatures();
                for (int i = 0; i < after.Length; i++) Check(after[i] == _saveBefore[i], "user save file " + i + " unchanged");
            }
            if (_output != null) File.WriteAllText(Path.Combine(_output, "result.txt"), (_passed ? "PASS\n" : "FAIL\n") + _report);
            Finished = true;
        }
        void Restore()
        {
            if (_restored) return;
            _restored = true;
#if ENABLE_INPUT_SYSTEM
            if (_keyboard != null && _keyboard.added) { InputState.Change(_keyboard, new KeyboardState()); InputSystem.RemoveDevice(_keyboard); }
            if (_mouse != null && _mouse.added) { InputState.Change(_mouse, new MouseState()); InputSystem.RemoveDevice(_mouse); }
            if (_pad != null && _pad.added) { InputState.Change(_pad, new GamepadState()); InputSystem.RemoveDevice(_pad); }
            if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent();
            if (_previousMouse != null && _previousMouse.added) _previousMouse.MakeCurrent();
            if (_previousPad != null && _previousPad.added) _previousPad.MakeCurrent();
#endif
            if (_driver != null && _saveBefore != null)
            {
                _driver.RestoreCampInputCaptureDeviceHints(_originalUsingGamepad, _originalPadLastUsed);
                _driver.ClearCapturedInput(); _driver.Session?.CampSim?.StopPlayerMovement();
                GameUserSettings.SetWasdMovement(_originalWasd);
            }
            if (_cameraCaptured && _camera != null)
            {
                _camera.transform.SetPositionAndRotation(_cameraPosition, _cameraRotation);
                _camera.orthographicSize = _cameraSize;
                if (_follow != null) _follow.enabled = _followEnabled;
                if (_juice != null) _juice.enabled = _juiceEnabled;
            }
        }
        void OnDestroy()
        {
            if (!Finished) { Check(false, "probe interrupted before completion"); Finish(); }
        }
    }
}
#endif
