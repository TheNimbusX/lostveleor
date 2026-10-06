using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Подсказки Крушения на полу (ритм v4, владелец 06.10): что рисовать — HudWreckReachRules (без Unity), чем —
    /// тонкий штрих HudRangePreview. До первого нажатия — один контур следа всей серии (сектор махов — Radius и
    /// ArcCosine сборки, круг и полоса выпада — WreckLanePreview по курсору, у Волнореза — круг обрушения
    /// WreckBreakwaterCrashRadius); в серии — ничего, кроме тонкой линии полосы между третьим нажатием и контактом
    /// (направление держит Sim, у Девятого вала Sim сам ведёт его за курсором). Фигуры кладутся от рисуемого тела
    /// героя (center), направления и расстояния — Sim.
    /// </summary>
    internal static class HudWreckReach
    {
        static readonly List<HudWreckOutlinePiece> Pieces = new List<HudWreckOutlinePiece>(32);

        /// <summary>
        /// Слот, чьё превью Крушения показать без наведения на слот и без прицела: идёт серия — её слот (линия
        /// выпада); иначе — слот Крушения, чья клавиша зажата (контур до нажатия). −1 — нечего.
        /// </summary>
        public static int HeldSlot(Simulation sim)
        {
            if (sim == null || sim.Entities.Count == 0) return -1;
            if (sim.WreckActive) return IsWreck(sim, sim.Wreck.Slot) ? sim.Wreck.Slot : -1;
            for (int slot = 0; slot <= (int)GameAction.Ability4 && slot < Simulation.AbilitySlots; slot++)
                if (IsWreck(sim, slot) && GameKeyBindings.Held((GameAction)slot)) return slot;
            return -1;
        }

        /// <summary>Серия этой сборки идёт: линия выпада светится как готовая, хотя кулдаун от каста уже тикает.</summary>
        public static bool SeriesLit(Simulation sim, AbilityBuild build)
            => sim != null && build != null && build.DefinitionId == AbilityDefinition.WreckId && sim.WreckActive
               && IsWreck(sim, sim.Wreck.Slot) && sim.GetAbility(sim.Wreck.Slot) == build;

        static bool IsWreck(Simulation sim, int slot)
        {
            if ((uint)slot >= (uint)Simulation.AbilitySlots) return false;
            AbilityBuild build = sim.GetAbility(slot);
            return build != null && build.DefinitionId == AbilityDefinition.WreckId;
        }

        /// <summary>
        /// Подсказка Крушения слота <paramref name="slot"/> между Begin и End превью. <paramref name="center"/> —
        /// рисуемое тело героя у земли, <paramref name="cursor"/> — точка пола под курсором (InputFrame.Aim).
        /// </summary>
        public static void Draw(HudRangePreview preview, Simulation sim, AbilityBuild build, int slot, Vector3 center, FixVec2 cursor)
        {
            if (preview == null || sim == null || build == null || build.DefinitionId != AbilityDefinition.WreckId) return;
            if ((uint)slot >= (uint)Simulation.AbilitySlots || sim.GetAbility(slot) != build) return;
            WreckState wreck = sim.Wreck;
            HudWreckHint hint = HudWreckReachRules.Hint(wreck, sim.WreckActive && wreck.Slot == slot);
            if (hint == HudWreckHint.LungeLine)
            {
                // С третьего нажатия до контакта — направление, которое взял Sim; от руки до конца полосы, без круга.
                if (!HudWreckReachRules.LungeLineOf(sim, slot, cursor, out FixVec2 axis, out float from, out float to)) return;
                Vector3 along = Flat(axis, AimDirection(sim, cursor));
                preview.WreckLine(center + along * from, center + along * to, HudWreckReachRules.LungeLineStrength);
                return;
            }
            if (hint != HudWreckHint.Footprint) return;
            // До первого нажатия — по курсору: один контур сектора махов, круга и полосы выпада.
            if (!HudWreckReachRules.FootprintOf(sim, slot, cursor, out HudWreckFootprint footprint, out FixVec2 direction)) return;
            HudWreckReachRules.Outline(footprint, Pieces);
            preview.WreckOutline(center, Flat(direction, AimDirection(sim, cursor)), Pieces, HudWreckReachRules.FootprintStrength);
        }

        // От героя Sim к курсору; курсор на герое — взгляд Sim (как BeginWreckStage).
        static Vector3 AimDirection(Simulation sim, FixVec2 cursor)
        {
            FixVec2 aim = cursor - sim.Entities.Position[Simulation.PlayerId];
            if (aim.LengthSq.Raw == 0) aim = sim.Entities.Facing[Simulation.PlayerId];
            return Flat(aim, Vector3.right);
        }

        static Vector3 Flat(FixVec2 v, Vector3 fallback)
        {
            var flat = new Vector3(v.X.ToFloat(), 0f, v.Y.ToFloat());
            return flat.sqrMagnitude > 1e-8f ? flat.normalized : fallback;
        }
    }
}
