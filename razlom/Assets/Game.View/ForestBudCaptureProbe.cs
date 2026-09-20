using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DefaultExecutionOrder(900)]
    public sealed class ForestBudCaptureProbe : MonoBehaviour
    {
        private TickDriver _driver;
        private ArenaView _arena;
        private int _generation = -1, _start;
        private bool _paused, _pauseDone;
        private bool _repeatDone;
        private float _pauseElapsed, _pausePhase, _maxPhaseDrift, _maxPositionDrift;
        private Vector3 _pausePosition;
        private ForestBudImpactView _impacts;
        private float _pauseImpactAge, _maxImpactAgeDrift;
        private int _expectedImpacts;

        private void Start()
        {
            _driver = FindAnyObjectByType<TickDriver>();
            _arena = _driver.GetComponent<ArenaView>();
        }

        private void LateUpdate()
        {
            if (_driver?.Sim == null || _driver.Sim.Entities.Count < 2) return;
            if (_generation != _driver.Generation)
            {
                _generation = _driver.Generation; _start = _driver.Sim.Tick; _expectedImpacts = 0;
                _impacts = _driver.GetComponent<ForestBudImpactView>();
                if (_repeatDone)
                    Debug.Log($"[forest-qa-impact-reset] active={_impacts.ActiveCount} emitted={_impacts.EmittedCount}");
            }
            bool impactRepeat = CaptureRig.ForestBudCase == "impact-repeat";
            if ((CaptureRig.ForestBudCase == "repeat" || impactRepeat) && !_repeatDone && _driver.Sim.Tick - _start >= (impactRepeat ? 117 : 70))
            {
                int previousGeneration = _driver.Generation;
                _repeatDone = true;
                _driver.StartForestBudTest(_driver.GetComponent<LayoutView>().Profile, CaptureRig.SeedOverride);
                Debug.Log($"[forest-qa-repeat] previousGeneration={previousGeneration} generation={_driver.Generation} activeFruits={_driver.Sim.ForestFruitActiveCount}");
                return;
            }
            if (!_arena.TryGetEntityView(1, out var body)) return;
            var animator = body.GetComponent<Animator>();
            foreach (var e in _driver.FrameEvents)
            {
                if (e.Type == SimEventType.ForestFruitImpact) _expectedImpacts++;
                if (e.Type == SimEventType.ForestFruitImpact || e.Type == SimEventType.Death)
                    Debug.Log($"[forest-qa] type={e.Type} tick={_driver.Sim.Tick-1} playerHp={_driver.Sim.Entities.Health[0]} budAlive={_driver.Sim.Entities.Alive[1]}");
                if (e.Type == SimEventType.ForestFruitLaunched && animator != null)
                {
                    int loaded = 0;
                    foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                        if (renderer.name.StartsWith("SM_LoadedFruit_") && renderer.enabled) loaded++;
                    Debug.Log($"[forest-qa-animation] tick={_driver.Sim.Tick-1} generic={!animator.isHuman} attackState={animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Ranged_Attack")} phase={animator.GetFloat("AttackPhase"):F4} loadedFruits={loaded}");
                }
            }
            if (_impacts != null && _impacts.EmittedCount != _expectedImpacts)
                Debug.LogError($"[forest-qa-impact-count] events={_expectedImpacts} effects={_impacts.EmittedCount}");
            bool impactPause = CaptureRig.ForestBudCase == "impact-pause";
            if ((CaptureRig.ForestBudCase != "pause" && !impactPause) || _pauseDone || animator == null) return;
            if (!_paused && _driver.Sim.Tick - _start >= (impactPause ? 117 : 61))
            {
                _paused = true; _pausePhase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                _pausePosition = body.position; _driver.SetGameplayPaused(true);
                _pauseImpactAge = _impacts != null ? _impacts.OldestAge : 0;
            }
            if (!_paused) return;
            _pauseElapsed += Time.deltaTime;
            _maxPhaseDrift = Mathf.Max(_maxPhaseDrift, Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - _pausePhase));
            _maxPositionDrift = Mathf.Max(_maxPositionDrift, Vector3.Distance(body.position, _pausePosition));
            if (_impacts != null) _maxImpactAgeDrift = Mathf.Max(_maxImpactAgeDrift, Mathf.Abs(_impacts.OldestAge - _pauseImpactAge));
            if (_pauseElapsed < .6f) return;
            _driver.SetGameplayPaused(false); _pauseDone = true;
            Debug.Log($"[forest-qa-pause] seconds={_pauseElapsed:F3} animatorDrift={_maxPhaseDrift:F6} positionDrift={_maxPositionDrift:F6} impactAge={_pauseImpactAge:F4} impactAgeDrift={_maxImpactAgeDrift:F6}");
        }

        private void OnDisable() { if (_paused && !_pauseDone && _driver != null) _driver.SetGameplayPaused(false); }
    }
}
