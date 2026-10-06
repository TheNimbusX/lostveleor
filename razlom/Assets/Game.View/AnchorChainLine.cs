using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Натянутая цепь (DESIGN §4.1): звенья по прямой хват → кольцо с малой стрелой провиса, без решателя.
    /// Узлы считаются ОТ КОЛЬЦА: nodes[0] — кольцо, последний — хват; неполное звено — у рукояти,
    /// поэтому при выдаче (бросок) снаружи видно, что цепь сматывается с руки, а звенья у головы не «едут».
    /// </summary>
    public static class AnchorChainLine
    {
        public const float Pitch = .135f;
        /// <summary>Условная погонная масса цепи для стрелы, кг/м.</summary>
        public const float Density = 1.2f;

        /// <summary>Стрела провиса f = min(0,02·D, ρ·g·D² / (8·T)): на 1,6 м при 300 Н — 1,3 см, в махе (≈ 3300 Н) — около миллиметра.</summary>
        public static float Sag(float span, float tension, float gravity = 9.81f)
        {
            if (span <= 0) return 0;
            float cap = .02f * span;
            if (tension <= 1e-3f) return cap;
            return Math.Min(cap, Density * gravity * span * span / (8f * tension));
        }

        /// <summary>Направление стрелы: вниз, перпендикулярно хорде, в вертикальной плоскости хорды.</summary>
        public static Vector3 SagDirection(Vector3 chord)
        {
            float l = chord.Length();
            if (l < 1e-5f) return Vector3.Zero;
            Vector3 d = chord / l, down = -Vector3.UnitY;
            Vector3 s = down - d * Vector3.Dot(down, d);
            float sl = s.Length();
            return sl < 1e-4f ? Vector3.Zero : s / sl;
        }

        /// <summary>
        /// Узлы от кольца к хвату по параболе стрелы <paramref name="sag"/> с шагом звена. Возвращает число узлов
        /// (звеньев на одно меньше). Если цепь длиннее запаса массива — шаг растягивается (это видно в логе: Stretched).
        /// </summary>
        public static int Layout(Vector3 grip, Vector3 ring, float sag, Vector3[] nodes, out bool stretched)
        {
            Vector3 chord = grip - ring;
            float span = chord.Length();
            stretched = false;
            if (nodes.Length < 2) return 0;
            int count = Math.Max(2, (int)Math.Ceiling((span - 1e-5f) / Pitch) + 1);
            if (count > nodes.Length) { count = nodes.Length; stretched = true; }
            Vector3 s = SagDirection(chord) * sag;
            float step = stretched ? 1f / (count - 1) : (span > 1e-5f ? Pitch / span : 0);
            for (int k = 0; k < count - 1; k++)
            {
                float t = Math.Min(1f, k * step);
                nodes[k] = ring + chord * t + s * (4f * t * (1f - t));
            }
            nodes[count - 1] = grip;
            return count;
        }

        /// <summary>Скорость узла <paramref name="k"/> из скоростей кольца и хвата (засев решателя при уходе в провис).</summary>
        public static Vector3 NodeVelocity(int k, int count, Vector3 ringVelocity, Vector3 gripVelocity)
            => Vector3.Lerp(ringVelocity, gripVelocity, count > 1 ? (float)k / (count - 1) : 0f);

        /// <summary>Наибольшее отклонение узлов от прямой кольцо — хват (решение «вернуться на прямую»).</summary>
        public static float Deviation(Vector3[] nodes, int count)
        {
            if (count < 3) return 0;
            Vector3 a = nodes[0], b = nodes[count - 1], ab = b - a;
            float inv = 1f / Math.Max(1e-8f, ab.LengthSquared()), worst = 0;
            for (int i = 1; i < count - 1; i++)
            {
                float t = Math.Clamp(Vector3.Dot(nodes[i] - a, ab) * inv, 0f, 1f);
                worst = Math.Max(worst, Vector3.Distance(nodes[i], a + ab * t));
            }
            return worst;
        }
    }

    /// <summary>
    /// Выбор рисования цепи (DESIGN §4.1): натяг → прямая; провис → решатель, засеянный точками прямой;
    /// обратно на прямую — сразу по натягу, или когда решатель прям (&lt; 1 см) два кадра подряд при почти полной длине
    /// и пробыл в провисе не меньше <see cref="MinSolverFrames"/> кадров (не мигать на границе).
    /// </summary>
    public sealed class AnchorChainDrawState
    {
        public const int MinSolverFrames = 6;
        public const float StraightTolerance = .01f;
        public const float NearFullSlack = .04f;
        private int _solverFrames, _straightFrames;
        private bool _lineByStraight;

        public bool UseLine { get; private set; } = true;
        /// <summary>Решатель надо засеять точками прямой в этот кадр.</summary>
        public bool SeedSolver { get; private set; }

        public void Reset() { UseLine = true; SeedSolver = false; _solverFrames = _straightFrames = 0; _lineByStraight = false; }

        /// <param name="taut">натяг (флаг запечки, бросок или |кольцо − хват| ≥ L − допуск)</param>
        /// <param name="span">расстояние кольцо — хват</param>
        /// <param name="length">длина цепи</param>
        /// <param name="solverDeviation">отклонение решателя от прямой в прошлом кадре (в режиме решателя)</param>
        public void Update(bool taut, float span, float length, float solverDeviation)
        {
            SeedSolver = false;
            if (UseLine)
            {
                if (taut) { _lineByStraight = false; return; }
                // Прямая «по прямизне» держится, пока цепь почти во всю длину: иначе мигало бы прямая ↔ решатель.
                if (_lineByStraight && span >= length - NearFullSlack) return;
                UseLine = false; _lineByStraight = false; SeedSolver = true; _solverFrames = 0; _straightFrames = 0;
                return;
            }
            _solverFrames++;
            bool straight = solverDeviation < StraightTolerance && span >= length - NearFullSlack;
            _straightFrames = straight ? _straightFrames + 1 : 0;
            // Натяг — сразу на прямую (натянутая цепь и есть прямая); «прям, но не натянут» — только после выдержки.
            if (taut || (_solverFrames >= MinSolverFrames && _straightFrames >= 2)) { _lineByStraight = !taut; UseLine = true; _solverFrames = 0; }
        }
    }
}
