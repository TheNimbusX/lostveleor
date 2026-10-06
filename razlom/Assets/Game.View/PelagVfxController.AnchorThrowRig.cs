using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Мост Броска якоря к системе якоря на цепи (artifacts/anchor-core: PelagAnchorRig.Throw — BeginLine,
    /// DriveLine, ChainGrip, Ring). Ставится ТОЛЬКО вместе с ригом (apply.py кладёт файл, если в проекте есть
    /// PelagAnchorRig.Throw.cs): без него partial-методы PelagVfxController.AnchorThrowAnchor пустые и Бросок
    /// идёт временным путём Абордажа. Порядок: контроллер эффектов 1010 подаёт кадр до LateUpdate рига 1021.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private Transform _atRigBody;
        private PelagAnchorRig _atRig;

        private PelagAnchorRig AnchorThrowRigComponent()
        {
            if (!_arena.TryGetEntityView(Game.Sim.Simulation.PlayerId, out Transform body)) return null;
            if (body != _atRigBody)
            {
                _atRigBody = body;
                _atRig = body.GetComponentInChildren<PelagAnchorRig>(true);
            }
            return _atRig != null && _atRig.isActiveAndEnabled ? _atRig : null;
        }

        partial void AnchorThrowRigBegin(int serial, ref bool taken)
        {
            PelagAnchorRig rig = AnchorThrowRigComponent();
            taken = rig != null && rig.BeginLine(serial);
        }

        partial void AnchorThrowRigDrive(int serial, byte phase, Vector3 origin, Vector3 direction, float along, float alongSpeed,
            bool reached, ref bool driven, ref Vector3 grip, ref Vector3 ring)
        {
            PelagAnchorRig rig = AnchorThrowRigComponent();
            if (rig == null) return;
            rig.DriveLine(new AnchorLineFrame
            {
                Serial = serial,
                Phase = (AnchorLinePhase)phase,
                Origin = origin,
                Direction = direction,
                Along = along,
                AlongSpeed = alongSpeed,
                Reached = reached,
                // Замах Броска — от правого кулака (принятый замах Абордажа v2); своей запечки Throw_Windup пока нет —
                // риг держит голову живой (InHandLive) до выпуска (спека §4, конфликт с DESIGN §6.2 вынесен автору anchor-core).
                WindupClip = null,
                WindupFrame = 0f,
            });
            driven = rig.Owning;
            if (!driven) return;
            grip = rig.ChainGrip;
            ring = rig.Ring;
        }
    }
}
