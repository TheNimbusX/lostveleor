using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class PelagVfxController
    {
        private GameObject _cycloneHeadObject, _cycloneChainObject, _cycloneWakeObject;
        private PelagVfxElement _cycloneHeadFx, _cycloneChainFx, _cycloneWakeFx;
        private Simulation _cycloneSimulation;
        private Vector3 _cycloneLastHead;
        private bool _cyclonePlaying;
        private float _cycloneReturnAge;
        private int _cycloneSampleTick;
        private float _cycloneSampleAngle, _cycloneSampleRadius, _cycloneAngleStep, _cycloneRadiusStep;

        private void UpdateCyclonePresentation()
        {
            Simulation sim = _driver.Sim;
            if (_cycloneSimulation != null && _cycloneSimulation != sim) ReleaseCyclone();
            bool active = sim != null && sim.CycloneActive;
            if (active && !_cyclonePlaying)
            {
                ReleaseCyclone();
                if (!TryAcquire(PelagVfxId.CycloneHook, out _cycloneHeadObject, out _cycloneHeadFx)) return;
                if (!TryAcquire(PelagVfxId.CycloneChain, out _cycloneChainObject, out _cycloneChainFx))
                { ReleaseCyclone(); return; }
                if (TryAcquire(PelagVfxId.CycloneWake, out _cycloneWakeObject, out _cycloneWakeFx))
                    _cycloneWakeFx.Begin(ChainHandPosition(), Quaternion.identity);
                _cycloneHeadFx.Begin(PlayerPosition(), Quaternion.identity);
                _cycloneChainFx.Begin(Vector3.zero, Quaternion.identity);
                _cyclonePlaying = true;
                _cycloneSimulation = sim;
                _cycloneSampleTick = sim.Tick;
                _cycloneSampleAngle = sim.CycloneAngle.ToFloat();
                _cycloneSampleRadius = sim.CycloneRadius.ToFloat();
                _cycloneAngleStep = _cycloneRadiusStep = 0f;
                _arena.BeginPlayerAnchorUse();
            }
            if (_cycloneHeadObject == null) return;
            Vector3 hand = ChainHandPosition();
            if (active)
            {
                if (_cycloneSampleTick != sim.Tick)
                {
                    int ticks = Mathf.Max(1, sim.Tick - _cycloneSampleTick);
                    float currentAngle = sim.CycloneAngle.ToFloat();
                    float currentRadius = sim.CycloneRadius.ToFloat();
                    _cycloneAngleStep = (currentAngle - _cycloneSampleAngle) / ticks;
                    _cycloneRadiusStep = (currentRadius - _cycloneSampleRadius) / ticks;
                    _cycloneSampleAngle = currentAngle; _cycloneSampleRadius = currentRadius;
                    _cycloneSampleTick = sim.Tick;
                }
                // Оружие и центр используют одну интерполяцию между тиками,
                // иначе звенья и светлый след дёргаются на 30 Гц.
                float angle = _cycloneSampleAngle - _cycloneAngleStep * (1f - _driver.Alpha);
                float radius = _cycloneSampleRadius - _cycloneRadiusStep * (1f - _driver.Alpha);
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                _cycloneLastHead = PlayerPosition() + radial * radius + Vector3.up * 1.0f;
                _cycloneHeadObject.transform.SetPositionAndRotation(_cycloneLastHead,
                    Quaternion.LookRotation(new Vector3(-radial.z, 0, radial.x)));
                _cycloneReturnAge = 0;
            }
            else
            {
                _cyclonePlaying = false;
                _cycloneReturnAge += Time.deltaTime;
                if (_cycloneReturnAge >= 0.3f || sim == null || !sim.Entities.Alive[Simulation.PlayerId])
                { ReleaseCyclone(); return; }
                float t = Mathf.SmoothStep(0, 1, _cycloneReturnAge / 0.3f);
                _cycloneHeadObject.transform.position = Vector3.Lerp(_cycloneLastHead, hand, t)
                    + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.15f;
            }
            Vector3 head = _cycloneHeadObject.transform.position;
            if (_cycloneWakeObject != null) _cycloneWakeObject.transform.position = head;
            float sag = active ? 0.12f : Mathf.Lerp(.12f, .32f, _cycloneReturnAge / .3f);
            _cycloneChainFx.SetLine(hand, Vector3.Lerp(hand, head, 0.5f) - Vector3.up * sag, head);
        }

        private void ReleaseCyclone()
        {
            bool ownedEquipment = _cycloneHeadObject != null;
            if (_cycloneHeadObject != null)
            {
                _cycloneHeadFx.End();
                _pools[(int)PelagVfxId.CycloneHook].Pool.Release(_cycloneHeadObject);
            }
            if (_cycloneChainObject != null)
            {
                _cycloneChainFx.End();
                _pools[(int)PelagVfxId.CycloneChain].Pool.Release(_cycloneChainObject);
            }
            if (_cycloneWakeObject != null)
            {
                _cycloneWakeFx.End();
                _pools[(int)PelagVfxId.CycloneWake].Pool.Release(_cycloneWakeObject);
            }
            _cycloneWakeObject = null;
            _cycloneHeadObject = _cycloneChainObject = null;
            _cyclonePlaying = false;
            _cycloneSimulation = null;
            if (ownedEquipment && _motionAbility != PelagVfxShowcase.AnchorLeap) _arena?.EndPlayerAnchorUse();
        }
    }
}
