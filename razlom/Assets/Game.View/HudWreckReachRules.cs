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

        /// <summary>Выпад нажат, контакта ещё не было: тонкая линия полосы по направлению Sim (курсор третьего нажатия).</summary>
        LungeLine = 2,
    }

    /// <summary>
    /// След серии в метрах, в осях героя: x — вперёд (к курсору), y — влево. Числа — из сборки и Sim (Radius и
    /// ArcCosine, WreckLanePreview, WreckBreakwaterCrashRadius); своих здесь нет.
    /// </summary>
    internal struct HudWreckFootprint
    {
        /// <summary>Сектор махов: радиус и полуугол, рад; от π — круг вокруг героя («Четвёртый удар»).</summary>
        public float SweepRadius, SweepHalf;

        /// <summary>Круг удара выпада: центр в Impact м впереди, радиус.</summary>
        public float Impact, ImpactRadius;

        /// <summary>Полоса вала: от LaneFrom до LaneTo вперёд, полуширина.</summary>
        public float LaneFrom, LaneTo, LaneHalf;

        /// <summary>Волнорез: круг обрушения в конце полосы; 0 — нет.</summary>
        public float CrashRadius;
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
    ///   лежащие внутри другой фигуры, не рисуются). Волнорез — с кругом обрушения, «Четвёртый удар» — сектор
    ///   становится кругом вокруг героя.
    /// * В серии — на полу ничего: ход серии показывают звенья под плиткой и над героем.
    /// * Выпад нажат и ещё не ударил (замах, у Девятого вала — заряд): только тонкая линия полосы — куда пойдёт
    ///   удар, направление берёт Sim в тик третьего нажатия (WreckLanePreview держит его до контакта).
    /// Сроков здесь нет — только фаза и этап снимка WreckState: Sim переписывают под клипы v4.
    /// </summary>
    internal static class HudWreckReachRules
    {
        /// <summary>Этап выпада: третий удар серии (Simulation.WreckStages − 1).</summary>
        public const int LungeStage = Simulation.WreckStages - 1;

        /// <summary>Сила штрихов: контур следа и линия выпада.</summary>
        public const float FootprintStrength = 1f, LungeLineStrength = .85f;

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
        /// ударом» — круг), круг и полоса выпада по курсору (WreckLanePreview), у Волнореза — круг обрушения.
        /// <paramref name="direction"/> — ось следа (к курсору). False — в слоте не Крушение.
        /// </summary>
        public static bool FootprintOf(Simulation sim, int slot, FixVec2 cursor, out HudWreckFootprint f, out FixVec2 direction)
        {
            f = default;
            if (!sim.WreckLanePreview(slot, cursor, out _, out direction, out Fix64 from, out Fix64 to, out Fix64 half, out _, out Fix64 radius))
                return false;
            AbilityBuild build = sim.GetAbility(slot);
            float cosine = build.Get(AbilityStatType.ArcCosine).ToFloat();
            f.SweepRadius = build.Get(AbilityStatType.Radius).ToFloat();
            // «Четвёртый удар» бьёт кругом Radius вокруг героя: сектор махов становится кругом.
            f.SweepHalf = build.Has(AbilityFlag.WreckFourthStrike) ? (float)Math.PI : (float)Math.Acos(Math.Max(-1f, Math.Min(1f, cosine)));
            f.Impact = f.LaneFrom = from.ToFloat();
            f.ImpactRadius = radius.ToFloat();
            f.LaneTo = to.ToFloat();
            f.LaneHalf = half.ToFloat();
            f.CrashRadius = sim.FormAt(slot) == PelagForm.WreckBreakwater ? Simulation.WreckBreakwaterCrashRadius.ToFloat() : 0f;
            return true;
        }

        /// <summary>
        /// Линия выпада: ось — направление, которое Sim взял на третьем нажатии (у Девятого вала в заряде Sim ведёт его
        /// за курсором), от руки (AbordageHandReach) до конца полосы, метры от героя. False — линии нет.
        /// </summary>
        public static bool LungeLineOf(Simulation sim, int slot, FixVec2 cursor, out FixVec2 direction, out float from, out float to)
        {
            from = to = 0f;
            if (!sim.WreckLanePreview(slot, cursor, out _, out direction, out _, out Fix64 end, out _, out _, out _)) return false;
            to = end.ToFloat();
            from = Math.Min(Simulation.AbordageHandReach.ToFloat(), to);
            return to - from > .05f;
        }

        /// <summary>Сектор махов — уже круг вокруг героя (полуугол от π).</summary>
        public static bool FullSweep(in HudWreckFootprint f) => f.SweepHalf >= Math.PI - .01;

        /// <summary>Точка строго внутри следа (дальше 1 мм от края хоть одной фигуры).</summary>
        public static bool Inside(in HudWreckFootprint f, float x, float y)
            => InSweep(f, x, y) || InDisc(x - f.Impact, y, f.ImpactRadius) || InLane(f, x, y)
               || InDisc(x - f.LaneTo, y, f.CrashRadius);

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
