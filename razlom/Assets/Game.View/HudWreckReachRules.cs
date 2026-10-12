using System;
using System.Collections.Generic;
using Game.Sim;

namespace Game.View
{
    /// <summary>Что пол показывает под Крушением в этот кадр (ритм v4 — мах влево, мах вправо, выпад; владелец 06.10).</summary>
    internal enum HudWreckHint : byte
    {
        /// <summary>Серия идёт — пол молчит: ход серии показывают звенья под плиткой и над героем.</summary>
        None = 0,

        /// <summary>До первого нажатия: один тонкий контур следа всей серии — сектор махов, круг и полоса выпада одной линией, без заливки.</summary>
        Footprint = 1,

        /// <summary>
        /// Выпад нажат, контакта ещё не было: тонкая линия полосы по направлению Sim (курсор третьего нажатия); у
        /// Призрачного якоря вместо линии — тонкий круг падения у точки выпада.
        /// </summary>
        LungeLine = 2,
    }

    /// <summary>
    /// След серии в метрах, в осях героя: x — вперёд (к курсору), y — влево. Числа — из сборки и Sim (Radius и
    /// ArcCosine, WreckLanePreview, WreckBreakwaterCrashRadius, WreckGhostRadius); своих здесь нет.
    /// </summary>
    internal struct HudWreckFootprint
    {
        /// <summary>Сектор махов: радиус и полуугол, рад; от π — круг вокруг героя («Четвёртый удар»).</summary>
        public float SweepRadius, SweepHalf;

        /// <summary>Круг удара выпада: центр в Impact м впереди, радиус.</summary>
        public float Impact, ImpactRadius;

        /// <summary>Полоса вала: от LaneFrom до LaneTo вперёд, полуширина. У Призрачного якоря полосы нет (LaneHalf 0).</summary>
        public float LaneFrom, LaneTo, LaneHalf;

        /// <summary>Волнорез: круг обрушения в конце полосы; 0 — нет.</summary>
        public float CrashRadius;

        /// <summary>Призрачный якорь: круг падения вокруг точки выпада (центр в Impact), радиус Sim; 0 — нет.</summary>
        public float GhostRadius;

        /// <summary>
        /// Девятый вал, пока зарядов меньше двух: полоса полного заряда (от LaneFrom до FullLaneTo, полуширина
        /// FullLaneHalf) — бледным контуром поверх следа; 0 — нет.
        /// </summary>
        public float FullLaneTo, FullLaneHalf;
    }

    /// <summary>Кусок контура: дуга (центр, радиус, углы от «вперёд» к «влево», рад) или отрезок A → B.</summary>
    internal readonly struct HudWreckOutlinePiece
    {
        public readonly bool Arc;
        public readonly float Ax, Ay, Bx, By;
        public readonly float Cx, Cy, Radius, From, To;

        HudWreckOutlinePiece(bool arc, float ax, float ay, float bx, float by, float cx, float cy, float radius, float from, float to)
        {
            Arc = arc; Ax = ax; Ay = ay; Bx = bx; By = by; Cx = cx; Cy = cy; Radius = radius; From = from; To = to;
        }

        public static HudWreckOutlinePiece Segment(float ax, float ay, float bx, float by)
            => new HudWreckOutlinePiece(false, ax, ay, bx, by, 0f, 0f, 0f, 0f, 0f);

        public static HudWreckOutlinePiece ArcOf(float cx, float cy, float radius, float from, float to)
            => new HudWreckOutlinePiece(true, 0f, 0f, 0f, 0f, cx, cy, radius, from, to);

        public float Length => Arc ? Radius * Math.Abs(To - From) : (float)Math.Sqrt((Bx - Ax) * (Bx - Ax) + (By - Ay) * (By - Ay));

        /// <summary>Точка на доле <paramref name="t"/> куска (0 — начало, 1 — конец).</summary>
        public void At(float t, out float x, out float y)
        {
            if (Arc)
            {
                double a = From + (To - From) * t;
                x = Cx + Radius * (float)Math.Cos(a);
                y = Cy + Radius * (float)Math.Sin(a);
                return;
            }
            x = Ax + (Bx - Ax) * t;
            y = Ay + (By - Ay) * t;
        }

        /// <summary>Часть куска от доли t0 до t1.</summary>
        public HudWreckOutlinePiece Slice(float t0, float t1)
        {
            if (Arc) return ArcOf(Cx, Cy, Radius, From + (To - From) * t0, From + (To - From) * t1);
            At(t0, out float ax, out float ay);
            At(t1, out float bx, out float by);
            return Segment(ax, ay, bx, by);
        }
    }

    /// <summary>
    /// ПОДСКАЗКИ КРУШЕНИЯ НА ПОЛУ — правило без Unity (ритм v4, решение владельца 06.10: «пусть будет так чище»).
    /// Язык принятого HUD — тонкий кремовый штрих HudRangePreview; заливок, кругов поверх и двойных фигур нет.
    ///
    /// * До первого нажатия (наведение на слот, прицел, зажатая клавиша): ОДИН тонкий контур следа всей серии —
    ///   сектор махов, круг удара выпада и полоса вала как одна фигура (Outline: граница объединения; участки,
    ///   лежащие внутри другой фигуры, не рисуются). «Четвёртый удар» — сектор становится кругом вокруг героя.
    ///   Формы (06.10 вечером, Simulation.Wreck.Forms): база — полоса вала LaneLength сборки; Волнорез — полоса
    ///   стены WreckBreakwaterLength × 2·WreckBreakwaterHalfWidth и круг обрушения в её конце; Девятый вал —
    ///   полоса по зарядам этой серии (до серии их нет — как у базы) и бледным контуром поверх — полоса полного
    ///   заряда (WreckNinthPercent двух зарядов, +WreckNinthLengthGain); Призрачный якорь — полосы нет, вместо неё
    ///   круг падения WreckGhostRadius у точки выпада (тело цели Sim прибавляет само: бит, кто касается линии).
    /// * В серии — на полу ничего: ход серии показывают звенья под плиткой и над героем.
    /// * Выпад нажат и ещё не ударил (замах, у Девятого вала — заряд): только тонкая линия полосы — куда пойдёт
    ///   удар, направление берёт Sim в тик третьего нажатия (WreckLanePreview держит его до контакта). Длина — по
    ///   форме и текущим зарядам Sim. У Призрачного якоря полосы нет и линии тоже: тем же штрихом — круг падения
    ///   WreckGhostRadius у точки выпада (LungeDropOf).
    /// Сроков здесь нет — только фаза и этап снимка WreckState: Sim переписывают под клипы v4.
    /// </summary>
    internal static class HudWreckReachRules
    {
        /// <summary>Этап выпада: третий удар серии (Simulation.WreckStages − 1).</summary>
        public const int LungeStage = Simulation.WreckStages - 1;

        /// <summary>Сила штрихов: контур следа и линия выпада.</summary>
        public const float FootprintStrength = 1f, LungeLineStrength = .85f;

        /// <summary>Девятый вал: бледный контур полосы полного заряда поверх следа — тот же штрих, тише.</summary>
        public const float FullChargeStrength = .45f;

        /// <summary>Фигуры меньше этого (м) не участвуют; допуск «внутри» — 1 мм; шаг проверки контура, м.</summary>
        const float MinSize = .01f, Eps = .001f, SampleStep = .025f;

        /// <summary>
        /// Подсказка кадра. <paramref name="series"/> — серия идёт В ЭТОМ слоте (WreckActive и Wreck.Slot == слот);
        /// иначе — до первого нажатия.
        /// </summary>
        public static HudWreckHint Hint(in WreckState wreck, bool series)
        {
            if (!series) return HudWreckHint.Footprint;
            if (wreck.Stage != LungeStage) return HudWreckHint.None;
            return wreck.Phase == WreckPhase.Windup || wreck.Phase == WreckPhase.Charge ? HudWreckHint.LungeLine : HudWreckHint.None;
        }

        /// <summary>
        /// След серии слота до первого нажатия — из сборки и Sim: сектор махов (Radius, ArcCosine; с «Четвёртым
        /// ударом» — круг), круг и полоса выпада по курсору (WreckLanePreview — форма и заряды этой серии), у
        /// Волнореза — круг обрушения, у Девятого вала — полоса полного заряда, пока зарядов меньше двух, у
        /// Призрачного якоря вместо полосы — круг падения у точки выпада. <paramref name="direction"/> — ось следа
        /// (к курсору). False — в слоте не Крушение.
        /// </summary>
        public static bool FootprintOf(Simulation sim, int slot, FixVec2 cursor, out HudWreckFootprint f, out FixVec2 direction)
        {
            f = default;
            if (!sim.WreckLanePreview(slot, cursor, out _, out direction, out Fix64 from, out Fix64 to, out Fix64 half, out _, out Fix64 radius))
                return false;
            AbilityBuild build = sim.GetAbility(slot);
            PelagForm form = sim.FormAt(slot);
            float cosine = build.Get(AbilityStatType.ArcCosine).ToFloat();
            f.SweepRadius = build.Get(AbilityStatType.Radius).ToFloat();
            // «Четвёртый удар» бьёт кругом Radius вокруг героя: сектор махов становится кругом.
            f.SweepHalf = build.Has(AbilityFlag.WreckFourthStrike) ? (float)Math.PI : (float)Math.Acos(Math.Max(-1f, Math.Min(1f, cosine)));
            f.Impact = f.LaneFrom = from.ToFloat();
            f.ImpactRadius = radius.ToFloat();
            f.LaneTo = to.ToFloat();
            f.LaneHalf = half.ToFloat();
            f.CrashRadius = form == PelagForm.WreckBreakwater ? Simulation.WreckBreakwaterCrashRadius.ToFloat() : 0f;
            if (form == PelagForm.WreckGhostAnchor)
            {
                // Вала у выпада нет: через WreckGhostDelayTicks в точку выпада падает якорь — круг WreckGhostRadius.
                f.LaneTo = f.LaneFrom;
                f.LaneHalf = 0f;
                f.GhostRadius = Simulation.WreckGhostRadius.ToFloat();
            }
            else if (form == PelagForm.WreckNinthWave && NinthCharges(sim, slot) < Simulation.WreckNinthMaxCharges
                     && sim.WreckLanePreview(slot, cursor, Simulation.WreckNinthMaxCharges, out _, out _, out _, out Fix64 fullTo,
                         out Fix64 fullHalf, out _, out _))
            {
                // Полная полоса — та же геометрия Sim с двумя зарядами: шире и длиннее, преграды оси те же.
                f.FullLaneTo = fullTo.ToFloat();
                f.FullLaneHalf = fullHalf.ToFloat();
            }
            return true;
        }

        /// <summary>Заряды Девятого вала, по которым Sim считает полосу слота сейчас: серии этого слота нет — 0.</summary>
        public static int NinthCharges(Simulation sim, int slot)
            => sim.WreckActive && sim.Wreck.Slot == slot ? sim.Wreck.NinthCharges : 0;

        /// <summary>
        /// Линия выпада: ось — направление, которое Sim взял на третьем нажатии (у Девятого вала в заряде Sim ведёт его
        /// за курсором), от руки (AbordageHandReach) до конца полосы (у Девятого вала — по текущим зарядам); метры от
        /// героя. False — линии нет; у Призрачного якоря её нет никогда: вала нет, вместо линии — круг падения
        /// (LungeDropOf).
        /// </summary>
        public static bool LungeLineOf(Simulation sim, int slot, FixVec2 cursor, out FixVec2 direction, out float from, out float to)
        {
            from = to = 0f;
            if (!sim.WreckLanePreview(slot, cursor, out _, out direction, out _, out Fix64 end, out _, out _, out _)) return false;
            if (sim.FormAt(slot) == PelagForm.WreckGhostAnchor) return false;
            to = end.ToFloat();
            from = Math.Min(Simulation.AbordageHandReach.ToFloat(), to);
            return to - from > .05f;
        }

        /// <summary>
        /// Призрачный якорь, выпад нажат и ещё не ударил: круг, куда упадёт якорь, — центр в точке выпада
        /// (<paramref name="impact"/> м от героя по оси Sim), радиус WreckGhostRadius (тело цели Sim прибавляет само).
        /// False — в слоте не Крушение или форма другая.
        /// </summary>
        public static bool LungeDropOf(Simulation sim, int slot, FixVec2 cursor, out FixVec2 direction, out float impact, out float radius)
        {
            impact = radius = 0f;
            if (!sim.WreckLanePreview(slot, cursor, out _, out direction, out Fix64 from, out _, out _, out _, out _)) return false;
            if (sim.FormAt(slot) != PelagForm.WreckGhostAnchor) return false;
            impact = from.ToFloat();
            radius = Simulation.WreckGhostRadius.ToFloat();
            return radius > MinSize;
        }

        /// <summary>Круг падения одним замкнутым куском (центр <paramref name="impact"/> м вперёд); список очищается.</summary>
        public static void DropOutline(float impact, float radius, List<HudWreckOutlinePiece> into)
        {
            into.Clear();
            if (radius > MinSize) into.Add(HudWreckOutlinePiece.ArcOf(impact, 0f, radius, (float)Math.PI, (float)(Math.PI * 3)));
        }

        /// <summary>Сектор махов — уже круг вокруг героя (полуугол от π).</summary>
        public static bool FullSweep(in HudWreckFootprint f) => f.SweepHalf >= Math.PI - .01;

        /// <summary>Точка строго внутри следа (дальше 1 мм от края хоть одной фигуры).</summary>
        public static bool Inside(in HudWreckFootprint f, float x, float y)
            => InSweep(f, x, y) || InDisc(x - f.Impact, y, f.ImpactRadius) || InLane(f, x, y)
               || InDisc(x - f.LaneTo, y, f.CrashRadius) || InDisc(x - f.Impact, y, f.GhostRadius);

        /// <summary>
        /// Контур следа одним штрихом: границы всех фигур минус то, что лежит внутри других. Куски в
        /// <paramref name="into"/> (список очищается) смыкаются концами в замкнутый контур.
        /// </summary>
        public static void Outline(in HudWreckFootprint f, List<HudWreckOutlinePiece> into)
        {
            into.Clear();
            float r = f.SweepRadius;
            if (r > MinSize)
            {
                if (FullSweep(f)) Visible(f, HudWreckOutlinePiece.ArcOf(0f, 0f, r, (float)Math.PI, (float)(Math.PI * 3)), true, into);
                else
                {
                    float half = f.SweepHalf, c = (float)Math.Cos(half) * r, s = (float)Math.Sin(half) * r;
                    Visible(f, HudWreckOutlinePiece.Segment(0f, 0f, c, -s), false, into);
                    Visible(f, HudWreckOutlinePiece.ArcOf(0f, 0f, r, -half, half), false, into);
                    Visible(f, HudWreckOutlinePiece.Segment(c, s, 0f, 0f), false, into);
                }
            }
            // Круги — от тыльной точки: она у удара и обрушения внутри другой фигуры, видимая дуга не рвётся.
            if (f.ImpactRadius > MinSize)
                Visible(f, HudWreckOutlinePiece.ArcOf(f.Impact, 0f, f.ImpactRadius, (float)Math.PI, (float)(Math.PI * 3)), true, into);
            if (f.LaneTo - f.LaneFrom > MinSize && f.LaneHalf > MinSize)
            {
                float a = f.LaneFrom, b = f.LaneTo, w = f.LaneHalf;
                Visible(f, HudWreckOutlinePiece.Segment(a, -w, b, -w), false, into);
                Visible(f, HudWreckOutlinePiece.Segment(b, -w, b, w), false, into);
                Visible(f, HudWreckOutlinePiece.Segment(b, w, a, w), false, into);
                Visible(f, HudWreckOutlinePiece.Segment(a, w, a, -w), false, into);
            }
            if (f.CrashRadius > MinSize)
                Visible(f, HudWreckOutlinePiece.ArcOf(f.LaneTo, 0f, f.CrashRadius, (float)Math.PI, (float)(Math.PI * 3)), true, into);
            if (f.GhostRadius > MinSize)
                Visible(f, HudWreckOutlinePiece.ArcOf(f.Impact, 0f, f.GhostRadius, (float)Math.PI, (float)(Math.PI * 3)), true, into);
        }

        /// <summary>
        /// Девятый вал: бледный контур полосы полного заряда (FullLaneTo × FullLaneHalf от LaneFrom) — только то, что
        /// лежит снаружи следа (сам след рисует Outline). Нет полной полосы — список пуст (список очищается).
        /// </summary>
        public static void FullChargeOutline(in HudWreckFootprint f, List<HudWreckOutlinePiece> into)
        {
            into.Clear();
            float a = f.LaneFrom, b = f.FullLaneTo, w = f.FullLaneHalf;
            if (b - a <= MinSize || w <= MinSize) return;
            Visible(f, HudWreckOutlinePiece.Segment(a, -w, b, -w), false, into);
            Visible(f, HudWreckOutlinePiece.Segment(b, -w, b, w), false, into);
            Visible(f, HudWreckOutlinePiece.Segment(b, w, a, w), false, into);
            Visible(f, HudWreckOutlinePiece.Segment(a, w, a, -w), false, into);
        }

        static bool InSweep(in HudWreckFootprint f, float x, float y)
        {
            float radius = f.SweepRadius;
            if (radius <= MinSize) return false;
            float r = (float)Math.Sqrt(x * x + y * y);
            if (r >= radius - Eps) return false;
            if (FullSweep(f)) return true;
            double off = f.SweepHalf - Math.Abs(Math.Atan2(y, x));
            return off > 0 && r * Math.Sin(Math.Min(off, Math.PI * .5)) > Eps;
        }

        static bool InDisc(float dx, float dy, float radius)
            => radius > MinSize && dx * dx + dy * dy < (radius - Eps) * (radius - Eps);

        static bool InLane(in HudWreckFootprint f, float x, float y)
            => f.LaneTo - f.LaneFrom > MinSize && f.LaneHalf > MinSize
               && x > f.LaneFrom + Eps && x < f.LaneTo - Eps && Math.Abs(y) < f.LaneHalf - Eps;

        // Видимые части куска: проба через SampleStep, переход «внутри/снаружи» уточняется делением пополам.
        static void Visible(in HudWreckFootprint f, in HudWreckOutlinePiece piece, bool closed, List<HudWreckOutlinePiece> into)
        {
            float length = piece.Length;
            if (length < MinSize) return;
            int steps = Math.Max(8, Math.Min(480, (int)Math.Ceiling(length / SampleStep)));
            int first = into.Count;
            float start = 0f, previous = 0f;
            bool shown = false;
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps;
                piece.At(t, out float x, out float y);
                bool visible = !Inside(f, x, y);
                if (k == 0) shown = visible;
                else if (visible != shown)
                {
                    float edge = Crossing(f, piece, previous, t, shown);
                    if (visible) start = edge;
                    else Emit(piece, start, edge, length, into);
                    shown = visible;
                }
                previous = t;
            }
            if (shown) Emit(piece, start, 1f, length, into);
            // Замкнутая дуга, видимая через свой шов: последний и первый куски — одна дуга.
            int count = into.Count - first;
            if (!closed || count < 2) return;
            HudWreckOutlinePiece head = into[first], tail = into[into.Count - 1];
            const float seam = 1e-5f;
            if (Math.Abs(head.From - piece.From) > seam || Math.Abs(tail.To - piece.To) > seam) return;
            into[first] = HudWreckOutlinePiece.ArcOf(piece.Cx, piece.Cy, piece.Radius, tail.From, head.To + (piece.To - piece.From));
            into.RemoveAt(into.Count - 1);
        }

        static void Emit(in HudWreckOutlinePiece piece, float t0, float t1, float length, List<HudWreckOutlinePiece> into)
        {
            if ((t1 - t0) * length > Eps) into.Add(piece.Slice(t0, t1));
        }

        // Доля, где кусок пересекает край следа: между a (видимость shownAtA) и b.
        static float Crossing(in HudWreckFootprint f, in HudWreckOutlinePiece piece, float a, float b, bool shownAtA)
        {
            for (int i = 0; i < 24; i++)
            {
                float mid = (a + b) * .5f;
                piece.At(mid, out float x, out float y);
                if (!Inside(f, x, y) == shownAtA) a = mid; else b = mid;
            }
            return (a + b) * .5f;
        }
    }
}
