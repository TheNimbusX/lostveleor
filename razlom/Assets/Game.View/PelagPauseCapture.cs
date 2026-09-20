using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>Capture-only pause probe; installed only for the explicit command-line case.</summary>
    [DefaultExecutionOrder(3100)]
    public sealed class PelagPauseCapture : MonoBehaviour
    {
        TickDriver _driver;
        Transform[] _parts;
        Vector3[] _positions;
        ParticleSystem[] _particles;
        float[] _particleTimes;
        int _frames, _tick;
        float _scale, _maxPosition, _maxParticleTime;
        string _movingPart;
        bool _paused, _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-capture-slam-case");
            if (Array.IndexOf(args, "-razlom-capture") >= 0 && i >= 0 && i+1 < args.Length && args[i+1] == "pause")
                new GameObject("Pelag pause capture").AddComponent<PelagPauseCapture>();
        }

        void LateUpdate()
        {
            if (_done) return;
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            var sim = _driver != null ? _driver.Sim : null;
            if (sim == null) return;
            if (!_paused)
            {
                var action = sim.PlayerAction;
                int delay = action.DefinitionId == AbilityDefinition.FireFlaskId ? 22 : 10;
                if (action.Serial <= 0 || sim.Tick < action.StartTick + delay) return;
                var arena = FindAnyObjectByType<ArenaView>();
                if (arena == null || !arena.TryGetEntityView(Simulation.PlayerId, out var body)) return;
                _parts = body.GetComponentsInChildren<Transform>(true);
                _positions = new Vector3[_parts.Length];
                for (int p = 0; p < _parts.Length; p++) _positions[p] = _parts[p].position;
                _particles = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include);
                _particleTimes = new float[_particles.Length];
                for (int p = 0; p < _particles.Length; p++) _particleTimes[p] = _particles[p].time;
                _tick = sim.Tick; _scale = Time.timeScale;
                _driver.SetGameplayPaused(true); Time.timeScale = 0;
                _paused = true;
                return;
            }
            for (int p = 0; p < _parts.Length; p++)
                if (_parts[p] != null)
                {
                    float distance = Vector3.Distance(_positions[p], _parts[p].position);
                    if (distance > _maxPosition) { _maxPosition = distance; _movingPart = _parts[p].name; }
                }
            for (int p = 0; p < _particles.Length; p++)
                if (_particles[p] != null) _maxParticleTime = Mathf.Max(_maxParticleTime, Mathf.Abs(_particleTimes[p] - _particles[p].time));
            if (++_frames < 36) return;
            bool passed = sim.Tick == _tick && _maxPosition <= .002f && _maxParticleTime <= .0001f;
            string report = $"[pelag-pause] pass={passed} frames={_frames} tickDrift={sim.Tick-_tick} positionDrift={_maxPosition:F6} part={_movingPart} particleTimeDrift={_maxParticleTime:F6}";
            if (passed) Debug.Log(report); else Debug.LogError(report);
            _done = true;
            Resume();
        }
        void Resume()
        {
            if (!_paused) return;
            Time.timeScale = _scale;
            if (_driver != null) _driver.SetGameplayPaused(false);
            _paused = false;
        }
        void OnDestroy() => Resume();
    }
}
