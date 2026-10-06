using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Превью Броска якоря на земле (спека §6, язык принятого HUD — кремовые фигуры HudRangePreview).
    /// Геометрия — только из Sim: Simulation.AnchorThrowPreview (тот же план, что каст: направление к
    /// курсору, дальность до стены по каждой полосе, полуширины) или снимок AnchorThrow в броске (полосы
    /// застывают). Своей логики дальности во View нет.
    ///  • При наведении на слот — полоса удара 0,9 м от героя до дальности (видимый край = край урона:
    ///    тело задето, если касается полосы); Невод — ещё лист сети 3 м от руки, тише; Веер — три полосы;
    ///    стена или корпус оборвали полосу — поперечная засечка на конце.
    ///  • Пока навык готов (вопрос 1 спеки, ответ по умолчанию «да») — тонкая линия по каждой полосе от руки
    ///    к концу цепи; у Невода под ней — тихий лист сети. Без неё 7 м бросаются вслепую.
    /// «Кого заденет», «куда упадут» и маркер головы в броске — вторая очередь (AnchorThrowRing уже чистая).
    /// </summary>
    internal static class HudAnchorThrowReach
    {
        /// <summary>Тонкая линия по курсору: полуширина, м, и сила (кремовая кромка, без заливки).</summary>
        public const float AimHalfWidth = .035f, AimStrength = .55f;
        /// <summary>Лист сети Невода — тише полосы удара; в тонкой линии — ещё тише.</summary>
        public const float NetStrength = .5f, NetAimStrength = .22f;
        /// <summary>Засечка обрыва полосы (стена, корпус): полуширина штриха, м.</summary>
        public const float CutHalfWidth = .05f;

        /// <summary>Полное превью (наведение на слот). <paramref name="center"/> — герой на земле (позиция показа).</summary>
        public static void DrawReach(HudRangePreview preview, Simulation sim, int slot, Vector3 center, FixVec2 cursor)
        {
            if (!Plan(sim, slot, cursor, ref center, out AnchorThrowState plan)) return;
            float hand = PelagAnchorThrowVfxRules.HandReach;
            if (plan.NetHalfWidth.Raw > 0)
            {
                Vector3 dir = Dir(plan, 0);
                preview.Lane(center + dir * hand, dir, Mathf.Max(0f, plan.Reach0.ToFloat() - hand), plan.NetHalfWidth.ToFloat(), NetStrength);
            }
            float half = plan.HalfWidth.ToFloat();
            for (int lane = 0; lane < PelagAnchorThrowVfxRules.LaneCount(plan); lane++)
            {
                Vector3 dir = Dir(plan, lane);
                float reach = plan.LaneReach(lane).ToFloat();
                preview.Lane(center, dir, reach, half);
                if (PelagAnchorThrowVfxRules.LaneCut(plan, lane)) Cut(preview, center + dir * reach, dir, half, 1f);
            }
        }

        /// <summary>Тонкая линия, пока Бросок готов: от руки до конца цепи по каждой полосе.</summary>
        public static void DrawAimLine(HudRangePreview preview, Simulation sim, int slot, Vector3 center, FixVec2 cursor)
        {
            if (!sim.AnchorThrowPreview(slot, cursor, out AnchorThrowState plan)) return;
            float hand = PelagAnchorThrowVfxRules.HandReach;
            if (plan.NetHalfWidth.Raw > 0)
            {
                Vector3 dir = Dir(plan, 0);
                preview.Lane(center + dir * hand, dir, Mathf.Max(0f, plan.Reach0.ToFloat() - hand), plan.NetHalfWidth.ToFloat(), NetAimStrength);
            }
            for (int lane = 0; lane < PelagAnchorThrowVfxRules.LaneCount(plan); lane++)
            {
                Vector3 dir = Dir(plan, lane);
                float reach = plan.LaneReach(lane).ToFloat();
                if (reach <= hand + .05f) continue;
                preview.Capsule(center + dir * hand, dir, reach - hand, AimHalfWidth, AimStrength);
                if (PelagAnchorThrowVfxRules.LaneCut(plan, lane)) Cut(preview, center + dir * reach, dir, plan.HalfWidth.ToFloat(), AimStrength);
            }
        }

        /// <summary>План: в броске этого слота — застывший снимок Sim (от точки каста), иначе — план каста к курсору.</summary>
        private static bool Plan(Simulation sim, int slot, FixVec2 cursor, ref Vector3 center, out AnchorThrowState plan)
        {
            AnchorThrowState live = sim.AnchorThrow;
            if (sim.AnchorThrowActive && live.Slot == slot && live.Serial != 0)
            {
                plan = live;
                center = new Vector3(live.Center.X.ToFloat(), center.y, live.Center.Y.ToFloat());
                return true;
            }
            return sim.AnchorThrowPreview(slot, cursor, out plan);
        }

        private static Vector3 Dir(in AnchorThrowState plan, int lane)
        {
            FixVec2 d = plan.LaneDir(lane);
            var v = new Vector3(d.X.ToFloat(), 0f, d.Y.ToFloat());
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>Поперечная засечка на конце оборванной полосы.</summary>
        private static void Cut(HudRangePreview preview, Vector3 end, Vector3 dir, float half, float strength)
        {
            var side = new Vector3(dir.z, 0f, -dir.x);
            float reach = half + .12f;
            preview.Capsule(end - side * reach, side, reach * 2f, CutHalfWidth, strength);
        }
    }
}
