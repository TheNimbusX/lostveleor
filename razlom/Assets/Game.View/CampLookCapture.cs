using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Game.View
{
    // Один маршрут и одни ракурсы для всех вариантов; запускается только capture-флагом.
    [DefaultExecutionOrder(2300)]
    public sealed class CampLookCapture : MonoBehaviour
    {
        Camera _camera;
        CameraFollow _follow;
        CombatCameraJuice _juice;
        CampPlayerView _camp;
        Transform _root;
        CampMagicCircle _magic;
        CampRiver _river;
        float _started;
        bool _walking, _ready, _perf;
        int _walkPhase = -1;
        string _output;

        public void Initialize(string output) { _output = output; }

        IEnumerator Start()
        {
            _camp = CampPlayerView.Instance;
            while (_camp == null || _camp.WalkMap == null) { yield return null; _camp = CampPlayerView.Instance; }
            _root = FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
            var look = _root.GetComponentInChildren<CampLookController>();
            if (look == null) { Debug.LogError("[camp-look] Controller missing"); yield break; }
            while (!look.HasCaptured) yield return null;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-capture-camp-look");
            if (index < 0 || index + 1 >= args.Length || !Enum.TryParse(args[index + 1], true, out CampLookStyle style))
            { Debug.LogError("[camp-look] Unknown style"); yield break; }
            _perf = CaptureRig.PerformanceCapture;
            // Проверяется фактический возврат общих источников, без запуска боя.
            look.SetStyle(CampLookStyle.Original);
            Color originalColor = look.Sun.color;
            float originalShadow = look.Sun.shadowStrength, originalFill = look.Fill.intensity;
            Quaternion rotation = look.Sun.transform.rotation;
            look.SetStyle(style);
            look.enabled = false;
            bool restored = look.Sun.color == originalColor && look.Sun.shadowStrength == originalShadow &&
                look.Fill.intensity == originalFill && look.Volume.sharedProfile == look.Original;
            look.enabled = true;
            while (!look.HasCaptured) yield return null;
            bool direction = look.Sun.transform.rotation == rotation;
            if (!restored || !direction) Debug.LogError("[camp-look] Scope restoration failed");
            _camera = Camera.main;
            _follow = _camera.GetComponent<CameraFollow>();
            _juice = _camera.GetComponent<CombatCameraJuice>();
            if (_follow != null) _follow.enabled = false;
            if (_juice != null) _juice.enabled = false;
            _magic = _root.GetComponentInChildren<CampMagicCircle>();
            _river = _root.GetComponentInChildren<CampRiver>();
            _started = Time.time;
            _ready = true;
            Directory.CreateDirectory(_output);
            File.WriteAllText(Path.Combine(_output, "look-check.txt"),
                $"style={style}\nrestored={restored}\nsunDirectionPreserved={direction}\n" +
                $"profile={look.Volume.sharedProfile.name}\nsun={look.Sun.color}\nshadow={look.Sun.shadowStrength}\nfill={look.Fill.intensity}\n");
            Debug.Log($"[camp-look] style={style} restored={restored} sunDirectionPreserved={direction}");
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float time = Time.time - _started;
            if (!_perf && time >= 11)
            {
                if (!_walking)
                {
                    _walking = true;
                    if (_follow != null) _follow.enabled = true;
                    if (_juice != null) _juice.enabled = true;
                }
                int phase = time < 14 ? 0 : time < 17 ? 1 : 2;
                if (phase != _walkPhase)
                {
                    _walkPhase = phase;
                    Vector3 wanted = phase == 0 ? _root.Find("Anchor - Smith").position :
                        phase == 1 ? _root.Find("Anchor - Trader").position :
                        _root.Find("Campfire").position + new Vector3(-1.8f, 0, -1);
                    Vector3 target = wanted;
                    float best = float.MaxValue;
                    for (float z = -2; z <= 2; z += .15f)
                    for (float x = -2; x <= 2; x += .15f)
                    {
                        Vector3 at = wanted + new Vector3(x, 0, z);
                        float distance = x * x + z * z;
                        if (distance < best && _camp.WalkMap.Contains(CampTrainingView.Flat(at))) { best = distance; target = at; }
                    }
                    bool routed = best < float.MaxValue && _camp.RouteTo(target);
                    File.AppendAllText(Path.Combine(_output, "look-check.txt"), $"route={routed} phase={phase} start={_camp.Position} target={target}\n");
                    if (!routed) Debug.LogError("[camp-look] Walking route failed");
                }
                return;
            }
            Vector3 focus; float size;
            if (_perf || time < 3) { focus = _root.Find("Campfire").position + Vector3.up; size = 18; }
            else if (time < 5) { focus = _root.Find("Campfire").position + Vector3.up * .6f; size = 6; }
            else if (time < 7) { focus = _root.Find("Anchor - Player").position + Vector3.up; size = 7; }
            else if (time < 9) { focus = _river.transform.TransformPoint(new Vector3(0, 0, 1.8f)); size = 10.8f; }
            else { focus = _magic.Centre.position + Vector3.up * .6f; size = 9.4f; }
            _camera.transform.rotation = Quaternion.Euler(48, 35, 0);
            _camera.transform.position = focus - _camera.transform.forward * 80;
            _camera.orthographicSize = size;
        }
    }
}
