using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Терновник» — след шипа-стручка (ThicketMasterCombatView.Seeds.cs; до 08.10 — след семени веера). Каждая частица
    /// встаёт в тот тик, когда остриё прошло её метр пути (выпуск + путь / скорость), и дальше — функция возраста: низкая
    /// пыль по земле каждые 0,5 м и клуб на каждом касании подскока, сорванные листья (меш листа CFXR) кувыркаются и
    /// ложатся, щепки коры отлетают назад, слабые угольки и ореол у стручка — на тёмной поляне его видно без пятна.
    /// Без аллокаций.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        private void DrawSeedTrail(SeedView v, Vector2 origin, Vector2 dir, float tip, float clock, float after)
        {
            var s = v.Seed;
            float speed = ThicketMasterSeedRules.Speed;
            float release = s.ReleaseTick;
            var forward = new Vector3(dir.x, 0f, dir.y);
            var side = new Vector3(-dir.y, 0f, dir.x);
            int serial = v.Serial;
            int dust = 0, leaves = 0, splinters = 0, motes = 0;
            float ease = ThicketMasterSeedRules.LaunchEaseMetres;

            // Пыль: низкие клубы по земле под линией, после схода с куста.
            if (v.Dust != null)
            {
                int puffs = Mathf.FloorToInt((tip - ease) / SeedDustSpacing);
                for (int j = 0; j < puffs && dust < v.DustBuffer.Length; j++)
                {
                    float d = ease + (j + .5f) * SeedDustSpacing;
                    float age = clock - (release + d / speed);
                    float life = SeedDustLife * (.8f + .4f * SeedRand(serial, j, 11));
                    if (age < 0f || age >= life) continue;
                    float u = age / life;
                    Vector2 p = origin + dir * d;
                    var at = new Vector3(p.x, GroundY(p.x, p.y), p.y)
                             + side * ((SeedRand(serial, j, 12) - .5f) * .3f) - forward * (.35f * u) + Vector3.up * (.12f + .18f * u);
                    float size = Mathf.Lerp(.2f, .45f, Mathf.Sqrt(u)) * (.8f + .4f * SeedRand(serial, j, 13));
                    SetSeedParticle(ref v.DustBuffer[dust++], at, size, Color.Lerp(SeedDustLight, SeedDustDark, SeedRand(serial, j, 14)),
                        .2f * (1f - u) * (1f - u) * Mathf.Clamp01(age / 1.5f), SeedRand(serial, j, 15) * 360f, SeedSeed(serial, j, 16));
                }
                // Касание подскока — клуб пошире в стороны.
                for (int k = 1; dust < v.DustBuffer.Length; k++)
                {
                    float d = ThicketMasterSeedRules.HopTouch(k);
                    if (d > tip) break;
                    float age = clock - (release + d / speed);
                    if (age < 0f || age >= SeedKickLife) continue;
                    float u = age / SeedKickLife;
                    Vector2 p = origin + dir * d;
                    var ground = new Vector3(p.x, GroundY(p.x, p.y), p.y);
                    for (int i = 0; i < SeedKickPuffs && dust < v.DustBuffer.Length; i++)
                    {
                        int n = 500 + k * 8 + i;
                        float spread = (i - (SeedKickPuffs - 1) * .5f) * .55f + (SeedRand(serial, n, 1) - .5f) * .2f;
                        var at = ground + side * (spread * (.3f + .7f * u)) - forward * (.2f * u) + Vector3.up * (.1f + .22f * u);
                        float size = Mathf.Lerp(.3f, .7f, Mathf.Sqrt(u)) * (.85f + .3f * SeedRand(serial, n, 2));
                        SetSeedParticle(ref v.DustBuffer[dust++], at, size, Color.Lerp(SeedDustLight, SeedDustDark, SeedRand(serial, n, 3)),
                            .26f * (1f - u) * (1f - u) * Mathf.Clamp01(age), SeedRand(serial, n, 4) * 360f, SeedSeed(serial, n, 5));
                    }
                }
                v.Dust.SetParticles(v.DustBuffer, dust);
            }

            // Листья: сорваны с шелухи, летят назад и вверх, кружат и ложатся; последние 30 % жизни тают.
            if (v.Leaves != null)
            {
                int count = Mathf.FloorToInt((tip - .3f) / SeedLeafSpacing) + 1;
                for (int j = 0; j < count && leaves < v.LeafBuffer.Length; j++)
                {
                    float d = .3f + (j + .6f * SeedRand(serial, j, 21)) * SeedLeafSpacing;
                    if (d > tip) break;
                    float age = clock - (release + d / speed);
                    float life = SeedLeafLife * (.8f + .4f * SeedRand(serial, j, 22));
                    if (age < 0f || age >= life) continue;
                    Vector3 at = SeedPoint(v, origin, dir, d);
                    var velocity = -forward * (.5f + 1.2f * SeedRand(serial, j, 23)) + side * ((SeedRand(serial, j, 24) - .5f) * 2.2f)
                                   + Vector3.up * (1f + 1.2f * SeedRand(serial, j, 25));
                    float t = age / Simulation.TicksPerSecond;
                    float drag = (1f - Mathf.Exp(-2f * t)) * .5f;
                    var p = at + new Vector3(velocity.x, 0f, velocity.z) * drag + Vector3.up * (velocity.y * t - .5f * SeedLeafGravity * t * t);
                    float floor = GroundY(p.x, p.z) + .03f;
                    bool landed = p.y <= floor;
                    if (landed) p.y = floor;
                    float tumble = landed ? Mathf.Min(t, .6f) : t;
                    var rotation = new Vector3(SeedRand(serial, j, 26) * 360f + 300f * tumble, SeedRand(serial, j, 27) * 360f + 120f * tumble,
                        SeedRand(serial, j, 28) * 360f + 260f * tumble);
                    SetSeedParticle(ref v.LeafBuffer[leaves], p, .16f + .08f * SeedRand(serial, j, 29),
                        Color.Lerp(SeedLeafLight, SeedLeafDark, SeedRand(serial, j, 30)), SeedFade(age / life), 0f, SeedSeed(serial, j, 31));
                    v.LeafBuffer[leaves++].rotation3D = rotation;
                }
                v.Leaves.SetParticles(v.LeafBuffer, leaves);
            }

            // Щепки коры: через раз, отлетают назад-вверх и падают.
            if (v.Splinters != null)
            {
                int count = Mathf.FloorToInt(tip / SeedSplinterSpacing);
                for (int j = 0; j < count && splinters < v.SplinterBuffer.Length; j++)
                {
                    if (SeedRand(serial, j, 41) > .6f) continue;
                    float d = (j + .5f) * SeedSplinterSpacing;
                    float age = clock - (release + d / speed);
                    float life = SeedSplinterLife * (.8f + .4f * SeedRand(serial, j, 42));
                    if (age < 0f || age >= life) continue;
                    var velocity = -forward * (.8f + 1.4f * SeedRand(serial, j, 43)) + side * ((SeedRand(serial, j, 44) - .5f) * 2.4f)
                                   + Vector3.up * (1f + 1.6f * SeedRand(serial, j, 45));
                    SetSeedParticle(ref v.SplinterBuffer[splinters++], SeedFly(SeedPoint(v, origin, dir, d), velocity, age),
                        .05f + .04f * SeedRand(serial, j, 46), Color.Lerp(SeedBarkLight, SeedBarkDark, SeedRand(serial, j, 47)),
                        SeedFade(age / life), SeedRand(serial, j, 48) * 360f + (SeedRand(serial, j, 49) - .5f) * 24f * age, SeedSeed(serial, j, 50));
                }
                v.Splinters.SetParticles(v.SplinterBuffer, splinters);
            }

            // Угольки следа и ореол стручка (только в полёте) — слабое свечение, не пятно.
            if (v.Motes != null)
            {
                if (after < 0f && v.Pod != null && motes < v.MoteBuffer.Length)
                    SetSeedParticle(ref v.MoteBuffer[motes++], v.Pod.position, .5f, Color.Lerp(SeedEmber, SeedEmberDeep, .3f), .12f, 0f,
                        SeedSeed(serial, 0, 90));
                int count = Mathf.FloorToInt(tip / SeedMoteSpacing);
                for (int j = 0; j < count && motes < v.MoteBuffer.Length; j++)
                {
                    float d = (j + .5f) * SeedMoteSpacing;
                    float age = clock - (release + d / speed);
                    float life = SeedMoteLife * (.75f + .5f * SeedRand(serial, j, 61));
                    if (age < 0f || age >= life) continue;
                    float u = age / life;
                    var at = SeedPoint(v, origin, dir, d) + side * ((SeedRand(serial, j, 62) - .5f) * .3f)
                             + Vector3.up * ((SeedRand(serial, j, 63) - .5f) * .2f + .2f * u) - forward * (.15f * u);
                    SetSeedParticle(ref v.MoteBuffer[motes++], at, (.05f + .04f * SeedRand(serial, j, 64)) * (1f - .5f * u),
                        Color.Lerp(SeedEmber, SeedEmberDeep, SeedRand(serial, j, 65)), .75f * (1f - u), 0f, SeedSeed(serial, j, 66));
                }
                v.Motes.SetParticles(v.MoteBuffer, motes);
            }
        }

        /// <summary>Баллистика щепки от at со скоростью velocity за age тиков Sim; на земле лежит.</summary>
        private Vector3 SeedFly(Vector3 at, Vector3 velocity, float age)
        {
            float t = age / Simulation.TicksPerSecond;
            var p = at + velocity * t + Vector3.down * (.5f * SeedGravity * t * t);
            float floor = GroundY(p.x, p.z) + .03f;
            if (p.y < floor) p.y = floor;
            return p;
        }

        /// <summary>Непрозрачна, последние 30 % жизни тает.</summary>
        private static float SeedFade(float u) => u < .7f ? 1f : Mathf.Clamp01(1f - (u - .7f) / .3f);

        private static void SetSeedParticle(ref ParticleSystem.Particle particle, Vector3 position, float size, Color color, float alpha,
            float rotation, uint seed)
        {
            color.a = Mathf.Clamp01(alpha);
            particle.position = position;
            particle.velocity = Vector3.zero;
            particle.startSize = size;
            particle.startColor = (Color32)color;
            particle.rotation = rotation;
            particle.angularVelocity = 0f;
            // Срок с запасом: частица кольцевого буфера с нулевым остатком не рисуется (ловушка частиц Unity).
            particle.startLifetime = 10f;
            particle.remainingLifetime = 10f;
            particle.randomSeed = seed;
        }

        /// <summary>Случайное в [0, 1) — функция семени, номера частицы и соли; одинаково на любом кадре.</summary>
        private static float SeedRand(int serial, int index, int salt) => (SeedSeed(serial, index, salt) & 0xFFFFFF) / 16777216f;

        private static uint SeedSeed(int serial, int index, int salt)
        {
            uint h = unchecked((uint)serial * 0x9E3779B1u ^ (uint)index * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du);
            h ^= h >> 15; h = unchecked(h * 0x2C1B3C6Du);
            h ^= h >> 12; h = unchecked(h * 0x297A2D39u);
            h ^= h >> 15;
            return h;
        }
    }
}
