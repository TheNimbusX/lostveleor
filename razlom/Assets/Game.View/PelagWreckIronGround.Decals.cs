using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Пятна разбитой земли Крушения «холодное железо» V7 — квадраты на меше земли с фактурой пака Hovl
    /// Crater2 (шейдер Razlom/Wreck Iron Ground, kind 3: рваное тело с отлетевшими кусками) вдоль неровного
    /// края полосы (PelagWreckIronRules.LaneEdge — тот же шум, что у камней) и по кромке круга. Размеры,
    /// повороты и шаг — разные от удара к удару (Hash01 по номеру удара), появляются по фронту Sim (полоса)
    /// и по кольцу (круг). Трещины за край полосы и круга рисует шейдер (сеть Crack4 и звезда Hovl Crack).
    /// </summary>
    public sealed partial class PelagWreckIronGround
    {
        /// <summary>Наибольшее число квадратов пятен на удар.</summary>
        private const int MaxDecals = 40;
        private const int CraterSplats = 6;
        /// <summary>Пятно раскрывается наружу: снаружи появляется позже на столько секунд за метр.</summary>
        private const float CrackRunSeconds = .12f;

        private int _decals;

        private void AddDecals(in Slam s, Vector3 perp, Func<float, float, float> ground)
        {
            _decals = 0;
            float scale = Mathf.Min(1f, s.Radius / 1.2f);
            // Кромка круга: рваные пятна разной величины, не по линейке.
            for (int i = 0; i < CraterSplats; i++)
            {
                float a = 6.2831853f * (i + PelagWreckIronRules.Hash01(s.Serial, 920 + i) * .7f) / CraterSplats;
                float size = Mathf.Lerp(.6f, 1.05f, PelagWreckIronRules.Hash01(s.Serial, 930 + i)) * Mathf.Clamp(s.Radius / 1.2f, .7f, 1.6f);
                float r = s.Radius * Mathf.Lerp(.55f, .82f, PelagWreckIronRules.Hash01(s.Serial, 940 + i));
                Vector3 radial = s.Dir * Mathf.Cos(a) + perp * Mathf.Sin(a);
                float turn = 6.2831853f * PelagWreckIronRules.Hash01(s.Serial, 950 + i);
                Decal(s, perp, ground, s.Impact + radial * r, Rotate(s.Dir, perp, turn), size * .5f, size * .5f, 3f,
                    PelagWreckIronRules.Hash01(s.Serial, 960 + i), true);
            }
            if (!s.Lane) return;
            for (int side = -1; side <= 1; side += 2)
            {
                // Пятна вдоль неровного края полосы: тело пятна доходит до края, отлетевшие куски — чуть за него.
                int salt = side > 0 ? 0 : 50;
                float along = s.Start + .05f + .35f * PelagWreckIronRules.Hash01(s.Serial, 1010 + salt);
                for (int k = 0; along < s.End - .15f && _decals < MaxDecals; k++)
                {
                    float size = Mathf.Lerp(.7f, 1.1f, PelagWreckIronRules.Hash01(s.Serial, 1020 + salt + k));
                    float edge = PelagWreckIronRules.LaneEdge(s.Serial, side, along) * s.HalfWidth;
                    float across = side * Mathf.Max(.15f, edge - size * .22f);
                    float turn = 6.2831853f * PelagWreckIronRules.Hash01(s.Serial, 1030 + salt + k);
                    Decal(s, perp, ground, s.Origin + s.Dir * along + perp * across, Rotate(s.Dir, perp, turn), size * .5f, size * .5f, 3f,
                        PelagWreckIronRules.Hash01(s.Serial, 1040 + salt + k), false);
                    along += Mathf.Lerp(.3f, .6f, PelagWreckIronRules.Hash01(s.Serial, 1045 + salt + k));
                }
            }
        }

        /// <summary>Единичный вектор в плане: <paramref name="x"/>·cos + <paramref name="y"/>·sin.</summary>
        private static Vector3 Rotate(Vector3 x, Vector3 y, float angle)
        {
            Vector3 v = x * Mathf.Cos(angle) + y * Mathf.Sin(angle);
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>
        /// Квадрат фактуры: ось v фактуры (низ → верх) — по <paramref name="up"/> (у трещины — наружу), полуразмеры
        /// поперёк и вдоль. uv0.xy — фактура (вариант &gt; 0,5 — зеркально), zw — метры (шум шейдера); uv1 — приход,
        /// вид, вариант, сила света; r — «внутри» (у трещины 1 у внутреннего конца, 0 у наружного).
        /// </summary>
        private void Decal(in Slam s, Vector3 perp, Func<float, float, float> ground, Vector3 centre, Vector3 up,
            float halfU, float halfV, float kind, float variant, bool crater)
        {
            if (_decals >= MaxDecals) return;
            _decals++;
            var right = new Vector3(up.z, 0f, -up.x);
            int at = _v;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    float u = i, v = j;
                    Vector3 flat = centre + right * ((u * 2f - 1f) * halfU) + up * ((v * 2f - 1f) * halfV);
                    flat.y = 0f;
                    float arrival;
                    Vector3 fromImpact = flat - new Vector3(s.Impact.x, 0f, s.Impact.z);
                    Vector3 fromOrigin = flat - new Vector3(s.Origin.x, 0f, s.Origin.z);
                    float acrossLane = Vector3.Dot(fromOrigin, perp), alongLane = Vector3.Dot(fromOrigin, s.Dir);
                    if (crater)
                    {
                        float r = fromImpact.magnitude;
                        arrival = PelagWreckIronRules.Seconds(PelagWreckIronRules.CraterArrivalTick(Mathf.Min(r, s.Radius), s.SlamTick, s.Radius) - s.SlamTick)
                                  + Mathf.Max(0f, r - s.Radius * .8f) * CrackRunSeconds;
                    }
                    else
                    {
                        float a = Mathf.Clamp(alongLane, s.Start, s.End);
                        arrival = PelagWreckIronRules.Seconds(PelagWreckIronRules.LaneArrivalTick(a, s.SlamTick, s.Start, s.Step) - s.SlamTick)
                                  + Mathf.Max(0f, Mathf.Abs(acrossLane) - s.HalfWidth * .6f) * CrackRunSeconds;
                    }
                    float tu = variant > .5f ? 1f - u : u;
                    var world = new Vector3(flat.x, s.Root.y, flat.z);
                    Add(s, world, ground, new Vector4(tu, v, acrossLane, alongLane - s.Start), new Vector4(arrival, kind, variant, 1f), 1f, 1f - v, 1f);
                }
            _triangles[_t++] = at; _triangles[_t++] = at + 2; _triangles[_t++] = at + 1;
            _triangles[_t++] = at + 1; _triangles[_t++] = at + 2; _triangles[_t++] = at + 3;
        }
    }
}
