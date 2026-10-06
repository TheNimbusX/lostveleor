using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>
    /// Провисшая цепь рига (DESIGN §4.1): один решатель на всё, длина постоянная (L рига), ≤ 13 узлов при 1,6 м.
    /// Узлы от кольца (0) к хвату (последний), как <see cref="AnchorChainLine"/>: засев из прямой — без пересчёта.
    /// Оба конца прибиты (голову держит <see cref="AnchorHeadDynamics"/>, хват — клип); тело — все капсулы рига
    /// (торс, руки, ноги), а не одна капсула корпуса, как у <see cref="AnchorChainSolver"/> старого Удара якорем.
    /// </summary>
    public sealed class AnchorSlackChain
    {
        public const float Step = 1f / 120f;
        public const int MaxPasses = 24, FinalPasses = 6;
        public const float LinkRadius = .02f;
        private Vector3[] _points = Array.Empty<Vector3>(), _previous = Array.Empty<Vector3>();
        private float[] _floor = Array.Empty<float>();
        private AnchorCapsule[] _near = Array.Empty<AnchorCapsule>();
        private int _nearCount;
        private float _accumulator;

        public float Length { get; private set; }
        public int Count { get; private set; }
        public float MaxStrain { get; private set; }
        public int Passes { get; private set; }
        public Vector3 this[int i] => _points[i];
        public Vector3[] Nodes => _points;

        /// <summary>Длина звена <paramref name="i"/> (между узлами i и i+1): последнее, у хвата, — неполное.</summary>
        public float SegmentLength(int i) => i == Count - 2 ? Length - (Count - 2) * AnchorChainLine.Pitch : AnchorChainLine.Pitch;

        public void Configure(float length)
        {
            Length = Math.Max(AnchorChainLine.Pitch, length);
            Count = (int)Math.Ceiling((Length - 1e-5f) / AnchorChainLine.Pitch) + 1;
            if (_points.Length < Count) { _points = new Vector3[Count]; _previous = new Vector3[Count]; }
            _accumulator = 0;
        }

        /// <summary>
        /// Засев теми же точками, что нарисовала прямая в этот кадр (шаг звена от кольца, та же стрела
        /// <paramref name="sag"/>), и скоростями концов: на стыке «прямая → провис» цепь не прыгает.
        /// Длины решатель доводит со следующего шага, уже с полом и капсулами.
        /// </summary>
        public void Seed(Vector3 ring, Vector3 grip, Vector3 ringVelocity, Vector3 gripVelocity, float sag)
        {
            Vector3 chord = grip - ring;
            float span = chord.Length();
            Vector3 s = AnchorChainLine.SagDirection(chord) * sag;
            float step = span > 1e-5f ? AnchorChainLine.Pitch / span : 0;
            for (int k = 0; k < Count; k++)
            {
                float t = k == Count - 1 ? 1f : Math.Min(1f, k * step);
                Vector3 p = ring + chord * t + s * (4f * t * (1f - t));
                _points[k] = p;
                _previous[k] = p - AnchorChainLine.NodeVelocity(k, Count, ringVelocity, gripVelocity) * Step;
            }
            _accumulator = 0;
        }

        private static float Func0(Vector3 p) => float.NegativeInfinity;

        /// <summary>
        /// Шаг за кадр: Верле 120 Гц, концы по кадру, затем длины, пол и капсулы. Цена (DESIGN §5 #16, ≤ 0,15 мс под Mono):
        /// пол читается раз на узел за подшаг (а не в каждом проходе), капсулы — только те, что касаются сферы цепи,
        /// проходов ≤ 24 с выходом по натягу 0,8 %, финальная подгонка концов — 6 проходов.
        /// </summary>
        public void Advance(float frameDt, Vector3 ringFrom, Vector3 ringTo, Vector3 gripFrom, Vector3 gripTo,
            Func<Vector3, float> ground, AnchorCapsule[] capsules, int capsuleCount)
        {
            if (Count < 2 || frameDt <= 0) return;
            ground ??= Func0;
            if (_floor.Length < Count) _floor = new float[Count];
            if (_near.Length < capsuleCount) _near = new AnchorCapsule[capsuleCount];
            _accumulator = Math.Min(_accumulator + frameDt, .1f);
            Vector3 gravityStep = new Vector3(0, -9.81f * Step * Step, 0);
            while (_accumulator >= Step)
            {
                _accumulator -= Step;
                float alpha = Math.Clamp(1f - _accumulator / frameDt, 0f, 1f);
                for (int i = 1; i < Count - 1; i++)
                {
                    Vector3 p = _points[i], velocity = (p - _previous[i]) * .985f;
                    float speed = velocity.Length();
                    if (speed > .25f) velocity *= .25f / speed;
                    _previous[i] = p;
                    _points[i] = p + velocity + gravityStep;
                }
                Vector3 ring = Vector3.Lerp(ringFrom, ringTo, alpha), grip = Vector3.Lerp(gripFrom, gripTo, alpha);
                Prepare(ring, grip, ground, capsules, capsuleCount);
                Relax(ring, grip, MaxPasses);
            }
            Prepare(ringTo, gripTo, ground, capsules, capsuleCount);
            Relax(ringTo, gripTo, FinalPasses);
        }

        /// <summary>Пол под узлами и капсулы рядом с цепью (сфера вокруг середины хорды радиусом L/2) — раз на подшаг.</summary>
        private void Prepare(Vector3 ring, Vector3 grip, Func<Vector3, float> ground, AnchorCapsule[] capsules, int capsuleCount)
        {
            for (int i = 1; i < Count - 1; i++) _floor[i] = ground(_points[i]) + .018f;
            _nearCount = 0;
            if (capsules == null) return;
            Vector3 centre = (ring + grip) * .5f;
            float reach = Length * .5f + LinkRadius + .05f;
            for (int c = 0; c < capsuleCount; c++)
                if (capsules[c].AxisDistance(centre, out _) <= reach + capsules[c].Radius) _near[_nearCount++] = capsules[c];
        }

        private void Relax(Vector3 ring, Vector3 grip, int passes)
        {
            int last = Count - 1;
            for (int pass = 0; pass < passes; pass++)
            {
                Passes = pass + 1;
                _points[0] = ring; _points[last] = grip;
                for (int i = last - 1; i > 0; i--)
                    _points[i] = Project(Toward(_points[i + 1], _points[i], SegmentLength(i)), i);
                for (int i = 1; i < last; i++)
                    _points[i] = Project(Toward(_points[i - 1], _points[i], SegmentLength(i - 1)), i);
                _points[0] = ring; _points[last] = grip;
                if (Measure() < .008f) break;
            }
            MaxStrain = Measure();
        }

        private static Vector3 Toward(Vector3 from, Vector3 p, float length)
        {
            Vector3 d = p - from;
            float l = d.Length();
            return from + (l > 1e-6f ? d / l : -Vector3.UnitY) * length;
        }

        private Vector3 Project(Vector3 p, int node)
        {
            if (p.Y < _floor[node]) p.Y = _floor[node];
            for (int c = 0; c < _nearCount; c++)
            {
                Vector3 a = _near[c].A, ab = _near[c].B - a;
                float r = _near[c].Radius + LinkRadius;
                float t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-8f, ab.LengthSquared()), 0f, 1f);
                Vector3 center = a + ab * t, delta = p - center;
                float sq = delta.LengthSquared();
                if (sq < r * r) p = center + (sq > 1e-10f ? delta / (float)Math.Sqrt(sq) : Vector3.UnitX) * r;
            }
            return p;
        }

        private float Measure()
        {
            float worst = 0;
            for (int i = 0; i < Count - 1; i++)
            {
                float s = SegmentLength(i);
                worst = Math.Max(worst, Math.Abs(Vector3.Distance(_points[i], _points[i + 1]) - s) / Math.Max(.02f, s));
            }
            return worst;
        }

        public float Deviation() => AnchorChainLine.Deviation(_points, Count);
    }
}
