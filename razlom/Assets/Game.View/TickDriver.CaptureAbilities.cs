using System;
using Game.Sim;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        private void ConfigureCapturedAbilities()
        {
            var mode=CaptureRig.VfxShowcase;
            if(mode==PelagVfxShowcase.Rotation)
            {
                var pool=new[]{6,4,0,3};
                for(int i=0;i<4;i++)Sim.SetAbility(i,PelagKit.PoolDefinition(pool[i]),Array.Empty<AbilityNode>(),0);
            }
            else
            {
                int pool=mode==PelagVfxShowcase.AnchorLeap?6:mode==PelagVfxShowcase.AnchorSweep?4:
                    mode==PelagVfxShowcase.ChainStep?3:mode==PelagVfxShowcase.Cleave?1:
                    mode==PelagVfxShowcase.Blaze?2:mode==PelagVfxShowcase.Wreck?5:
                    mode==PelagVfxShowcase.FireFlask?7:mode==PelagVfxShowcase.Skewer?8:
                    mode==PelagVfxShowcase.Backblast?9:0;
                Sim.SetAbility(0,PelagKit.PoolDefinition(pool),Array.Empty<AbilityNode>(),0);
            }
            Sim.SetAbility(PelagKit.DashSlot,AbilityDefinition.Dash(),Array.Empty<AbilityNode>(),0);
        }

        /// <summary>
        /// -capture-skill-form: форма Вихря на стенде (CaptureRig.SkillForm). Ставит её набору забега
        /// тем же путём, что F8 «Вихрь: форма» (RunLoadout.DebugSetForm), и держит клавишу Бури
        /// от нажатия все StormHoldTicks — у capture-нажатий своего удержания нет.
        /// </summary>
        private RunLoadout _captureFormRefused;

        private void UpdateCaptureSkillForm()
        {
            PelagForm form = CaptureRig.SkillForm;
            RunLoadout loadout = Session != null && Session.Mode == GameMode.Rift ? Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(form);
            if (loadout != null && loadout.FormOf(line) != form && loadout != _captureFormRefused)
            {
                bool set = loadout.DebugSetForm(line, form);
                if (!set) _captureFormRefused = loadout;
                RefreshAbilityBuild();
                UnityEngine.Debug.Log($"[capture-skill-form] {form} line={line} set={set} slot0={Sim.FormAt(0)}");
            }
            // Стенд без чужого ввода (как -LiveSkill): клик владельца в окне съёмки не уводит героя в серию сабли.
            _pending.Flags = 0;
            _pending.AttackTarget = -1;
            AttackHeld = false;
            if (form != PelagForm.WhirlwindStorm) return;
            if ((_abilityLatch & 1) != 0) _whirlwindHoldUntilTick = Sim.Tick + Simulation.StormHoldTicks + 1;
            if (Sim.Tick < _whirlwindHoldUntilTick) _pending.AbilityHoldMask |= 1;
        }

        /// <summary>За сколько тиков до нажатия мишени встают на кольцо -capture-whirlwind-ring.</summary>
        private const int CaptureRingLeadTicks = 15;
        private bool _captureRingStandPlaced;
        private int _captureRingCastPlaced = -1;

        /// <summary>
        /// -capture-whirlwind-ring: мишени стенда Вихря — на заданном расстоянии от героя, по прежним
        /// направлениям: сразу и за CaptureRingLeadTicks до каждого нажатия. Здоровье мишеней
        /// доливается каждый тик — оба нажатия бьют по живым (Буря убивала стенд первым же нажатием).
        /// Только стенд съёмки (неподвижные мишени); без ключа не зовётся.
        /// </summary>
        private void UpdateCaptureWhirlwindRing()
        {
            if (!CaptureRig.WhirlwindShowcase || Session == null || Session.Mode != GameMode.Rift) return;
            EntityStore store = Sim.Entities;
            for (int i = 1; i < store.Count; i++)
                if (store.Alive[i] && store.Side[i] != store.Side[Simulation.PlayerId]) store.Health[i] = store.MaxHealth[i];
            LogCaptureRingDistances(store);
            bool place = !_captureRingStandPlaced;
            if (CaptureRig.TryNextWhirlwindCast(Sim.Tick, out int cast, out int left)
                && left <= CaptureRingLeadTicks && cast != _captureRingCastPlaced)
            {
                _captureRingCastPlaced = cast;
                _captureRingLogUntil = Sim.Tick + CaptureRingLeadTicks + 40;
                place = true;
            }
            if (!place) return;
            EntityStore entities = Sim.Entities;
            FixVec2 from = entities.Position[Simulation.PlayerId];
            FixVec2 centre = from;
            if (!_captureRingStandPlaced && CaptureRig.WhirlwindShift != UnityEngine.Vector2.zero)
            {
                // Сдвиг стенда — один раз, до первого нажатия: герой встаёт на открытую землю.
                centre = from + new FixVec2(Fix64.FromDouble(CaptureRig.WhirlwindShift.x), Fix64.FromDouble(CaptureRig.WhirlwindShift.y));
                entities.Position[Simulation.PlayerId] = centre;
            }
            _captureRingStandPlaced = true;
            Fix64 ring = Fix64.FromDouble(CaptureRig.WhirlwindRing);
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == entities.Side[Simulation.PlayerId]) continue;
                FixVec2 delta = entities.Position[i] - from;
                if (delta.Length.Raw == 0) continue;
                FixVec2 direction = delta.Normalized();
                entities.Position[i] = centre + direction * ring;
                entities.Facing[i] = -direction;
            }
            UnityEngine.Debug.Log($"[capture-whirlwind-ring] tick={Sim.Tick} cast={cast} ring={CaptureRig.WhirlwindRing:F2}");
        }

        private int _captureRingLogUntil = -1, _captureRingLoggedTick = -1;

        /// <summary>Замер тяги: расстояние мишеней до героя по Sim и по виду, раз в тик, вокруг каждого нажатия.</summary>
        private void LogCaptureRingDistances(EntityStore store)
        {
            if (Sim.Tick > _captureRingLogUntil || Sim.Tick == _captureRingLoggedTick) return;
            _captureRingLoggedTick = Sim.Tick;
            FixVec2 hero = store.Position[Simulation.PlayerId];
            UnityEngine.Vector3 heroView = GetRenderPosition(Simulation.PlayerId);
            var line = new System.Text.StringBuilder($"[capture-ring-dist] tick={Sim.Tick} t={UnityEngine.Time.time:F3}");
            for (int i = 1; i < store.Count; i++)
            {
                if (!store.Alive[i] || store.Side[i] == store.Side[Simulation.PlayerId]) continue;
                UnityEngine.Vector3 view = GetRenderPosition(i) - heroView;
                view.y = 0f;
                line.Append($" e{i} sim={(store.Position[i] - hero).Length.ToFloat():F2} view={view.magnitude:F2}");
            }
            UnityEngine.Debug.Log(line.ToString());
        }
    }
}
