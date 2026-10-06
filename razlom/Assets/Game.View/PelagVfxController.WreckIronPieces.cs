using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение «холодное железо» — куски удара оземь и выбросы частиц (V6): корона угловатых плит
    /// торчком наружу по кромке круга (наружный край — на ImpactRadius) и щебень внутри, плиты, блоки,
    /// комья и немного железа по краям полосы (наружный край — на полуширине), щебень между краем и
    /// звеньями, 3–4 крупных звена тёмного железа, вдавленных в паз по оси полосы — плашмя и ребром
    /// через одно, как лежит цепь; куски камня, комья и железо меш-частицами
    /// вылетают вверх из удара и с фронта на каждом шаге Sim (бросок расчётом полёта — падают внутри
    /// круга и полосы), крошка, пыль и искры. V7: кромка круга и края полосы — неровные кучки (правила),
    /// часть камней клонится внутрь, сидит глубже или в земле (_Earth); пыль клубами вразброс и брызги
    /// земли (CFXR flat debris, слой «Spray») рваным веером из воронки и с краёв полосы за фронтом.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private void PlaceWreckIronPieces(WreckIronRun run, System.Func<float, float, float> ground)
        {
            run.Count = 0;
            Transform root = run.Object.transform;
            Transform slabs = root.Find("Slabs"), rubble = root.Find("Rubble"), irons = root.Find("Irons"), links = root.Find("Links");
            int slabNext = 0, rubbleNext = 0, ironNext = 0;
            // Всё прошлое использование объекта пула — спрятать.
            foreach (Transform group in new[] { slabs, rubble, irons, links })
                if (group != null)
                    foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var perp = new Vector3(-run.Dir.z, 0f, run.Dir.x);

            int n = PelagWreckIronRules.CraterStones(run.Serial, run.Radius, run.Tick, _wiStones, 0);
            if (run.Lane)
                n += PelagWreckIronRules.LaneStones(run.Serial, run.Start, run.End, run.HalfWidth, run.Tick, run.Step, _wiStones, n);
            for (int i = 0; i < n; i++)
            {
                PelagWreckIronRules.Stone s = _wiStones[i];
                bool slab = !s.Iron && s.Look == PelagWreckIronRules.LookSlab;
                // Плиты — свои гранёные плиты/осколки/клинья (V6), блоки и комья — гранёный кусок, железо — пластина.
                Transform pivot = s.Iron ? Slot(irons, ref ironNext) : slab ? Slot(slabs, ref slabNext) : Slot(rubble, ref rubbleNext);
                if (pivot == null) pivot = slab ? Slot(rubble, ref rubbleNext) : Slot(slabs, ref slabNext);
                if (pivot == null) break;
                bool iron = pivot.parent == irons;
                bool slabMesh = pivot.parent == slabs;
                Vector3 flat = s.Crater ? run.Impact + run.Dir * s.Along + perp * s.Across : run.Origin + run.Dir * s.Along + perp * s.Across;
                // Side −1 у камня круга — клонится внутрь (V7: кто как лёг, не корона наружу).
                Vector3 outward = s.Crater ? (run.Dir * s.Along + perp * s.Across) * (s.Side < 0 ? -1f : 1f) : perp * s.Side;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f) outward = run.Dir;
                outward.Normalize();
                float y = ground(flat.x, flat.z);
                float h = PelagWreckIronRules.Hash01(run.Serial, 1000 + i), h2 = PelagWreckIronRules.Hash01(run.Serial, 1100 + i);
                // Наклон наружу: «верх» клонится к внешней стороне на Tilt; широкая грань плиты смотрит наружу,
                // свой поворот вокруг верха — у плит небольшой (корона вокруг якоря, а не каша).
                Vector3 up = Vector3.Slerp(Vector3.up, outward, s.Tilt / 90f);
                float twist = slabMesh ? (s.Spin / 360f - .5f) * 70f : s.Spin;
                float h3 = PelagWreckIronRules.Hash01(run.Serial, 1300 + i);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.LookRotation(outward, Vector3.up)
                                      * Quaternion.AngleAxis(twist, Vector3.up);
                Vector3 scale;
                float rise, earth = 0f;
                Color tint;
                if (iron)
                {
                    scale = new Vector3(s.Size * (.8f + .3f * h), s.Size * 1.2f, s.Size * 1.1f);
                    rise = .18f;
                    tint = Color.white;
                }
                else if (slabMesh)
                {
                    // Плита торчком: из земли выходит верхние две трети, подошва — в земле.
                    float k = s.Size * (.92f + .16f * h);
                    scale = new Vector3(k * (.9f + .2f * h2), k * 1.12f, k * (.85f + .3f * h));
                    // V7: одни плиты выперло выше, другие сидят глубже; часть — в земле (_Earth), не чистый камень.
                    rise = Mathf.Lerp(-.06f, .12f, h3);
                    earth = .4f * h2 * h2;
                    tint = Color.Lerp(new Color(.8f, .79f, .79f), new Color(1f, .97f, .95f), h2);
                }
                else if (s.Look == PelagWreckIronRules.LookClod)
                {
                    // Ком земли: плоский, тёмно-бурый (цвет земли), фактура — только светотенью.
                    scale = new Vector3(s.Size * (.95f + .05f * h), s.Size * (.5f + .15f * h), s.Size * (.85f + .1f * h));
                    rise = .05f;
                    earth = 1f;
                    tint = Color.Lerp(new Color(.85f, .85f, .85f), Color.white, h2);
                }
                else
                {
                    // Блок: гранёный, коренастый, серо-бурый.
                    scale = new Vector3(s.Size * (.95f + .1f * h), s.Size * (.66f + .2f * h), s.Size * (.85f + .15f * h));
                    rise = Mathf.Lerp(-.04f, .1f, h3);
                    earth = .45f * h * h;
                    tint = Color.Lerp(new Color(.78f, .77f, .77f), new Color(.95f, .93f, .9f), h2);
                }
                AddWreckIronPiece(run, pivot, new Vector3(flat.x, y + s.Size * rise, flat.z), rotation, scale,
                    s.ArrivalTick, s.Size * .95f, y, iron ? WiKind.Iron : WiKind.Stone, tint, earth);
            }

            int count = run.Lane ? PelagWreckIronRules.LinkCount(run.Start, run.End) : 0;
            for (int i = 0; i < count && links != null && i < links.childCount; i++)
            {
                float along = PelagWreckIronRules.LinkAlong(i, run.Start);
                Vector3 flat = run.Origin + run.Dir * along;
                float y = ground(flat.x, flat.z);
                bool edge = PelagWreckIronRules.LinkOnEdge(i);
                float h = PelagWreckIronRules.Hash01(run.Serial, 1200 + i);
                // V6: вдавлено в паз — плашмя прут утоплен чуть больше чем наполовину, ребром — над землёй
                // только верхний прут; лёгкий крен, чтобы цепь не лежала по линейке.
                Quaternion rotation = Quaternion.LookRotation(run.Dir, Vector3.up) * Quaternion.AngleAxis((h - .5f) * 8f, Vector3.up)
                                      * Quaternion.AngleAxis(edge ? 90f : (h - .5f) * 6f, Vector3.forward);
                AddWreckIronPiece(run, links.GetChild(i), new Vector3(flat.x, y + PelagWreckIronRules.LinkLift(edge), flat.z), rotation,
                    Vector3.one * PelagWreckIronRules.LinkLength, PelagWreckIronRules.LinkArrivalTick(i, run.Tick, run.Start, run.Step),
                    .45f, y, WiKind.Link, Color.white, 0f);
            }
        }

        private static Transform Slot(Transform group, ref int next)
        {
            if (group == null || next >= group.childCount) return null;
            return group.GetChild(next++);
        }

        private static void AddWreckIronPiece(WreckIronRun run, Transform pivot, Vector3 rest, Quaternion rotation, Vector3 scale,
            float arrival, float depth, float groundY, WiKind kind, Color tint, float earth)
        {
            if (run.Count >= run.Pieces.Length) return;
            Renderer renderer = pivot.GetComponentInChildren<Renderer>(true);
            if (renderer == null) return;
            renderer.enabled = false;
            run.Pieces[run.Count++] = new WiPiece
            {
                Pivot = pivot, Renderer = renderer, Rest = rest, Rotation = rotation, Scale = scale,
                Arrival = arrival, Depth = depth, GroundY = groundY, Kind = kind, Tint = tint, Earth = earth
            };
        }

        // ---------------------------------------------------------------- частицы

        private static readonly Color WiChipStone = new Color(.46f, .40f, .34f), WiChipIron = new Color(.22f, .25f, .30f);
        private static readonly Color WiCrumb = new Color(.24f, .17f, .12f);
        private static readonly Color WiDust = new Color(.42f, .34f, .26f, .55f);
        private static readonly Color WiHot = new Color(.7f, .88f, 1f), WiSky = new Color(.3f, .62f, 1f);
        // Куски меш-частицами (V6): серо-бурый камень двух тонов, ком земли, железо.
        private static readonly Color[] WiChunkColors =
        {
            new Color(.62f, .58f, .56f), new Color(.5f, .47f, .46f), new Color(.46f, .34f, .25f), new Color(.6f, .57f, .55f),
            new Color(.46f, .34f, .25f), new Color(.36f, .39f, .44f)
        };

        private static Color WiChunkColor() => WiChunkColors[Random.Range(0, WiChunkColors.Length)];

        /// <summary>Удар: куски камня и комья вверх из воронки, падают внутри круга; крошка, искры, пыль у кромки.</summary>
        private void BurstWreckIronCrater(WreckIronRun run, System.Func<float, float, float> ground)
        {
            Vector3 centre = run.Root + Vector3.up * .15f;
            float k = Mathf.Clamp(run.Radius / 1.2f, .6f, 1.5f);
            for (int i = 0; i < 10; i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * .9f + .25f;
                WiEmit(run.Sparks, centre, d.normalized * Random.Range(4.5f, 8f), Random.value < .3f ? WiHot : WiSky, Random.Range(.05f, .08f), Random.Range(.14f, .26f));
            }
            int chunks = Mathf.RoundToInt(14 * k);
            for (int i = 0; i < chunks; i++)
            {
                float a = (i + Random.value * .8f) * Mathf.PI * 2f / chunks;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 from = run.Root + radial * run.Radius * Random.Range(.1f, .35f) + Vector3.up * .1f;
                WiChunk(run.Chunks, from, run.Root + radial * run.Radius * Random.Range(.45f, .92f), Random.Range(.42f, .58f),
                    WiChunkColor(), Random.Range(.15f, .27f) * k, ground);
            }
            for (int i = 0; i < 16; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                WiThrow(run.Shards, centre, run.Root + radial * run.Radius * Random.Range(.3f, .95f), Random.Range(.3f, .45f),
                    Random.value < .2f ? WiChipIron : Random.value < .5f ? WiCrumb : WiChipStone, Random.Range(.07f, .13f), ground);
            }
            // V7: пыль клубами вразброс (не ровным кольцом), брызги земли низким рваным веером наружу.
            int puffs = Random.Range(7, 12);
            for (int i = 0; i < puffs; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 at = run.Root + radial * run.Radius * Random.Range(.4f, 1f) + Vector3.up * Random.Range(.15f, .35f);
                WiEmit(run.Dust, at, radial * Random.Range(.3f, 1.2f) + Vector3.up * Random.Range(.2f, .55f), WiDust, Random.Range(.5f, 1.1f), Random.Range(.5f, .9f));
            }
            int spray = Mathf.RoundToInt(70 * k);
            for (int i = 0; i < spray; i++)
            {
                // Веер неровный: гуще в нескольких случайных направлениях.
                float a = Random.value < .6f ? (Mathf.Floor(Random.value * 5f) + Random.Range(-.25f, .25f)) * Mathf.PI * 2f / 5f + run.Serial
                    : Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 from = run.Root + radial * run.Radius * Random.Range(.1f, .45f) + Vector3.up * .08f;
                WiThrow(run.Spray, from, run.Root + radial * run.Radius * Random.Range(.55f, 1.2f), Random.Range(.28f, .5f),
                    WiDirtColor(), Random.Range(.06f, .13f), ground);
            }
        }

        private static readonly Color[] WiDirtColors =
        {
            new Color(.24f, .17f, .12f), new Color(.34f, .24f, .16f), new Color(.17f, .12f, .09f), new Color(.42f, .31f, .21f)
        };

        private static Color WiDirtColor() => WiDirtColors[Random.Range(0, WiDirtColors.Length)];

        /// <summary>Фронт дошёл до нового шага Sim: куски вверх у краёв полосы, крошка и пыль у фронта, искры у звена, у конца — выброс.</summary>
        private void EmitWreckIronFront(WreckIronRun run, float shown, float front)
        {
            if (CaptureRig.NoVfx) return;
            int steps = Mathf.Clamp(Mathf.FloorToInt(shown - run.Tick + 1f + 1e-3f), 0, run.Travel);
            var perp = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            System.Func<float, float, float> ground = WreckIronGroundAt(run.Root.y);
            while (run.StepsDone < steps)
            {
                run.StepsDone++;
                float at = Mathf.Min(run.End, run.Start + run.Step * run.StepsDone);
                Vector3 lip = run.Origin + run.Dir * at;
                lip.y = ground(lip.x, lip.z) + .12f;
                for (int s = -1; s <= 1; s += 2)
                {
                    // Кусок с края полосы: вверх и чуть наружу, падает у края внутри полосы.
                    Vector3 from = lip + perp * (s * run.HalfWidth * Random.Range(.35f, .7f));
                    Vector3 land = run.Origin + run.Dir * Mathf.Clamp(at - Random.Range(.05f, .45f), run.Start, run.End)
                                   + perp * (s * Random.Range(.45f, .9f) * (run.HalfWidth - .1f));
                    WiChunk(run.Chunks, from, land, Random.Range(.4f, .52f), WiChunkColor(), Random.Range(.13f, .22f), ground);
                }
                for (int i = 0; i < 3; i++)
                {
                    Vector3 land = run.Origin + run.Dir * Mathf.Clamp(at - Random.Range(0f, .5f), run.Start, run.End) + perp * Random.Range(-1f, 1f) * (run.HalfWidth - .1f);
                    WiThrow(run.Shards, lip + perp * Random.Range(-.4f, .4f), land, Random.Range(.25f, .38f),
                        Random.value < .2f ? WiChipIron : Random.value < .5f ? WiCrumb : WiChipStone, Random.Range(.06f, .12f), ground);
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    if (Random.value < .8f)
                        WiEmit(run.Dust, lip + perp * (s * run.HalfWidth * Random.Range(.3f, .95f)) + run.Dir * Random.Range(-.2f, .2f) + Vector3.up * .1f,
                            perp * (s * Random.Range(.25f, .7f)) + Vector3.up * Random.Range(.25f, .45f) + run.Dir * Random.Range(.2f, .5f),
                            WiDust, Random.Range(.45f, .9f), Random.Range(.5f, .8f));
                    // Брызги земли с края полосы наружу (V7): рвано, то гуще, то реже.
                    int sprays = Random.Range(4, 11);
                    for (int i = 0; i < sprays; i++)
                    {
                        Vector3 from = lip + perp * (s * run.HalfWidth * Random.Range(.2f, .75f)) - Vector3.up * .06f;
                        Vector3 land = run.Origin + run.Dir * Mathf.Clamp(at + Random.Range(-.35f, .25f), run.Start, run.End)
                                       + perp * (s * run.HalfWidth * Random.Range(.7f, 1.3f));
                        WiThrow(run.Spray, from, land, Random.Range(.26f, .44f), WiDirtColor(), Random.Range(.06f, .12f), ground);
                    }
                }
            }
            int links = PelagWreckIronRules.LinkCount(run.Start, run.End);
            for (int i = 0; i < links; i++)
            {
                if ((run.LinkSparks & (1 << i)) != 0 || shown < PelagWreckIronRules.LinkArrivalTick(i, run.Tick, run.Start, run.Step)) continue;
                run.LinkSparks |= 1 << i;
                Vector3 c = run.Origin + run.Dir * PelagWreckIronRules.LinkAlong(i, run.Start);
                c.y = ground(c.x, c.z) + .15f;
                for (int k = 0; k < 6; k++)
                {
                    Vector3 d = Random.onUnitSphere;
                    d.y = Mathf.Abs(d.y) + .3f;
                    WiEmit(run.Sparks, c, d.normalized * Random.Range(4f, 7f), k % 3 == 0 ? WiHot : WiSky, Random.Range(.05f, .08f), Random.Range(.14f, .24f));
                }
            }
            if (!run.EndBurst && run.StepsDone >= run.Travel)
            {
                run.EndBurst = true;
                Vector3 end = run.Origin + run.Dir * run.End;
                end.y = ground(end.x, end.z) + .15f;
                int burst = run.Stopped ? 6 : 4;
                for (int i = 0; i < burst; i++)
                {
                    Vector3 land = run.Origin + run.Dir * (run.End - Random.Range(.05f, .5f)) + perp * Random.Range(-1f, 1f) * (run.HalfWidth - .15f);
                    WiChunk(run.Chunks, end, land, Random.Range(.38f, .5f), WiChunkColor(), Random.Range(.13f, .22f), ground);
                }
                for (int i = 0; i < (run.Stopped ? 4 : 2); i++)
                    WiEmit(run.Dust, end + perp * Random.Range(-.4f, .4f), Vector3.up * .5f - run.Dir * .3f, WiDust, Random.Range(.7f, 1f), Random.Range(.6f, .85f));
                for (int i = 0; run.Stopped && i < 6; i++)
                    WiEmit(run.Sparks, end, (Vector3.up * .8f - run.Dir + Random.insideUnitSphere * .6f).normalized * Random.Range(4f, 7f), WiSky, .07f, .2f);
            }
        }

        /// <summary>Обломок с высоты <paramref name="from"/> падает на землю в <paramref name="land"/> за <paramref name="flight"/> с (живёт до касания).</summary>
        private static void WiThrow(ParticleSystem system, Vector3 from, Vector3 land, float flight, Color color, float size,
            System.Func<float, float, float> ground)
        {
            if (system == null) return;
            WiEmit(system, from, WiLaunch(system, from, land, flight, ground), color, size, flight);
        }

        /// <summary>Кусок меш-частицей: тот же бросок, случайный поворот в трёх осях.</summary>
        private static void WiChunk(ParticleSystem system, Vector3 from, Vector3 land, float flight, Color color, float size,
            System.Func<float, float, float> ground)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = from, velocity = WiLaunch(system, from, land, flight, ground), startColor = color, startSize = size,
                startLifetime = flight, rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                applyShapeToPosition = false
            }, 1);
        }

        private static Vector3 WiLaunch(ParticleSystem system, Vector3 from, Vector3 land, float flight, System.Func<float, float, float> ground)
        {
            float g = 9.81f * system.main.gravityModifierMultiplier;
            float landY = ground(land.x, land.z) + .03f;
            Vector3 flat = new Vector3(land.x - from.x, 0f, land.z - from.z);
            PelagWreckVfxRules.Launch(flat.magnitude, flight, from.y - landY, g, out float horizontal, out float vertical);
            return (flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.zero) * horizontal + Vector3.up * vertical;
        }

        private static void WiEmit(ParticleSystem system, Vector3 at, Vector3 velocity, Color color, float size, float life)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = color, startSize = size, startLifetime = life, applyShapeToPosition = false
            }, 1);
        }
    }
}
