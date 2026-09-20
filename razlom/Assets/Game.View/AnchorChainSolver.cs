using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Верёвочный решатель геометрии оружия. Звенья выдаются со стороны хвата.</summary>
    public sealed class AnchorChainSolver
    {
        public const float Pitch = .135f;
        public float LinkPitch { get; }
        public AnchorChainSolver(float pitch = Pitch) { LinkPitch = pitch; }
        public const float Step = 1f / 120f;
        private const int Capacity = 96;
        private readonly Vector3[] _points = new Vector3[Capacity];
        private readonly Vector3[] _previous = new Vector3[Capacity];
        private readonly Vector3[] _scratch = new Vector3[Capacity];
        private readonly Vector3[] _scratchPrevious = new Vector3[Capacity];
        private readonly float[] _floors = new float[Capacity];
        public Func<Vector3, float> GroundHeight { get; set; }
        private float _length;
        public int Count { get; private set; }
        public float MaxStrain { get; private set; }
        public float AttachmentError { get; private set; }
        public int Iterations { get; private set; }
        public Vector3 this[int i] => _points[i];
        public float SegmentLength(int i) => i == 0 ? _length - (Count - 2) * LinkPitch : LinkPitch;

        public void Clear() { Count = 0; MaxStrain = AttachmentError = 0; }

        public void Advance(Vector3 grip, Vector3 ring, float payout, float floor,
            Vector3 capsuleBottom, Vector3 capsuleTop, float capsuleRadius, bool integrate)
        {
            payout = Math.Clamp(payout, LinkPitch, (Capacity - 2) * LinkPitch);
            int count = Math.Clamp((int)Math.Ceiling((payout - .00001f) / LinkPitch) + 1, 2, Capacity);
            if (Count == 0)
            {
                Count = count;
                for (int i = 0; i < Count; i++)
                {
                    float t = (float)i / (Count - 1);
                    _points[i] = Vector3.Lerp(grip, ring, t) - Vector3.UnitY * (.16f * (float)Math.Sin(t * Math.PI));
                    _previous[i] = _points[i];
                }
            }
            else if (count != Count)
            {
                // Конец у якоря сохраняет материальные узлы; новые входят из ладони.
                Array.Copy(_points, _scratch, Count);
                Array.Copy(_previous, _scratchPrevious, Count);
                int delta = count - Count;
                for (int i = count - 1; i >= 0; i--)
                {
                    int old = i - delta;
                    Vector3 p = old >= 0 && old < Count ? _scratch[old] : grip;
                    _points[i] = p;
                    _previous[i] = old >= 0 && old < Count ? _scratchPrevious[old] : grip;
                }
                Count = count;
            }
            _length = payout;
            for (int i = 1; i < Count - 1; i++)
                _floors[i] = Math.Max(floor, GroundHeight != null ? GroundHeight(_points[i]) : floor);
            if (integrate)
                for (int i = 1; i < Count - 1; i++)
                {
                    Vector3 p = _points[i];
                    Vector3 velocity = (p - _previous[i]) * .985f;
                    if (velocity.LengthSquared() > .0225f) velocity = Vector3.Normalize(velocity) * .15f;
                    _points[i] += velocity - Vector3.UnitY * (9.81f * Step * Step);
                    _previous[i] = p;
                }
            // Обе системы ограничений решаются совместно: проекция из тела
            // не остаётся последней операцией, способной растянуть цепь.
            Vector3 capsuleAxis = capsuleTop - capsuleBottom;
            float inverseAxisLength = 1f / Math.Max(.00001f, capsuleAxis.LengthSquared());
            for (int pass = 0; pass < 512; pass++)
            {
                Iterations = pass + 1;
                _points[0] = grip; _points[Count - 1] = ring;
                // Слабое сопротивление изгибу не даёт свободной цепи складываться зигзагом.
                // После него снова решаются длины и коллизии, а не сглаживается готовая картинка.
                // Сначала убираем изломы, затем даём ограничениям длины сойтись.
                // Постоянное сглаживание заново растягивало короткое выходящее
                // звено в конце возврата даже после сотен итераций.
                if (pass < 48 && pass % 4 == 0)
                    for (int i = 1; i < Count - 1; i++)
                    {
                        Vector3 middle = (_points[i - 1] + _points[i + 1]) * .5f;
                        _points[i] = Vector3.Lerp(_points[i], middle, .14f);
                    }
                // Два полных прохода FABRIK передают тягу сразу вдоль всей
                // цепи; локальное деление поправки пополам сходилось слишком медленно.
                for (int i = Count - 2; i > 0; i--)
                {
                    Vector3 d = _points[i] - _points[i + 1];
                    float length = d.Length();
                    Vector3 p = _points[i + 1] + (length > .000001f ? d / length : Vector3.UnitY) * SegmentLength(i);
                    _points[i] = Project(p, _floors[i], capsuleBottom, capsuleAxis, inverseAxisLength, capsuleRadius);
                }
                for (int i = 1; i < Count - 1; i++)
                {
                    Vector3 d = _points[i] - _points[i - 1];
                    float length = d.Length();
                    Vector3 p = _points[i - 1] + (length > .000001f ? d / length : Vector3.UnitY) * SegmentLength(i - 1);
                    _points[i] = Project(p, _floors[i], capsuleBottom, capsuleAxis, inverseAxisLength, capsuleRadius);
                }
                _points[0] = grip; _points[Count - 1] = ring;
                if (pass % 4 == 3 && Measure() < .008f) break;
            }
            MaxStrain = Measure();
            AttachmentError = Math.Max(Vector3.Distance(_points[0], grip), Vector3.Distance(_points[Count - 1], ring));
        }

        private static Vector3 Project(Vector3 p, float floor, Vector3 bottom, Vector3 axis, float inverseAxisLength, float radius)
        {
            p.Y = Math.Max(floor + .018f, p.Y);
            float t = Math.Clamp(Vector3.Dot(p - bottom, axis) * inverseAxisLength, 0, 1);
            Vector3 center = bottom + axis * t;
            Vector3 delta = p - center;
            float squared = delta.LengthSquared();
            if (squared < radius * radius)
                p = center + (squared > .0000001f ? delta / (float)Math.Sqrt(squared) : Vector3.UnitX) * radius;
            return p;
        }

        private float Measure()
        {
            float error = 0;
            for (int i = 0; i < Count - 1; i++)
                error = Math.Max(error, Math.Abs(Vector3.Distance(_points[i], _points[i + 1]) - SegmentLength(i)) / Math.Max(.02f, SegmentLength(i)));
            return error;
        }
    }
}
