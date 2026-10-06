using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Шквал v2 — вода прыжков (PelagSquallWater): струя на каждый прыжок, полоса
    /// Пенного следа, дуга возврата. Объект — VFX_Pelag_Squall_Water
    /// (Editor/PelagSquallFoamVfxSetup): меш воды «Water» и частицы, которые
    /// выбрасывает контроллер (мировые координаты, без своих вспышек):
    ///  • TailDrops/SkidFoam на отрыве — резкий старт: 3–5 капель позади старта и
    ///    клочья пены, отброшенные назад (пена «срывается», а не висит иглой);
    ///  • SkidFoam/SkidDrops по пройденному пути — занос по краям струи, как у рывка;
    ///  • EdgeFoam — пока живёт полоса Пенного следа: комья пены всплывают на гребнях
    ///    и медленно расходятся (полоса живая, не застывшая), за 0,65 с до конца стихают.
    /// Рождение — от SquallJump/SquallReturn, привязка к полосе — от SquallFoamStrip,
    /// конец роста — от SquallStrike / посадки возврата / SquallEnded.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Объектов воды разом: 12 полос Sim (FoamTrailCapacity) + струи серии + возврат.</summary>
        private const int SquallWaterSlots = 18;
        /// <summary>Занос по краям струи, штук на метр пути (у рывка 7 и 6 — струя Шквала уже).</summary>
        private const float SquallSkidFoamPerMetre = 5f, SquallSkidDropsPerMetre = 4f;
        /// <summary>Комья на гребнях живой полосы, штук в секунду на метр полосы.</summary>
        private const float SquallEdgeFoamPerMetre = 2.2f;
        /// <summary>За столько до конца полосы комья больше не всплывают — она белеет и рвётся.</summary>
        private const float SquallTrailEdgeQuiet = .65f;

        private sealed class SquallWaterRun
        {
            public readonly PelagSquallWater Water = new PelagSquallWater();
            public bool Active;
            public int Fx, Serial, Index, StartTick, Strip = -1;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem SkidFoam, SkidDrops, TailDrops, EdgeFoam;
            public float SkidAlong, FoamCarry, DropCarry, EdgeCarry;
        }

        private SquallWaterRun[] _sqWater;
        private readonly Vector3[] _sqLine = new Vector3[2];

        private SquallWaterRun BeginSquallWater(int index, int tick, Vector3 from, Vector3 to, PelagSquallWater.Kind kind)
        {
            _sqLine[0] = from;
            _sqLine[1] = to;
            return BeginSquallWater(index, tick, _sqLine, 2, false, kind);
        }

        private SquallWaterRun BeginSquallWater(int index, int tick, Vector3[] points, int count, bool smooth, PelagSquallWater.Kind kind)
        {
            if (CaptureRig.NoVfx) return null;
            if (_sqWater == null)
            {
                _sqWater = new SquallWaterRun[SquallWaterSlots];
                for (int i = 0; i < _sqWater.Length; i++) _sqWater[i] = new SquallWaterRun();
            }
            // Прежняя струя этой серии, если ещё растёт (прыжок кончился без удара), встаёт где есть.
            SquallWaterRun run = null;
            foreach (SquallWaterRun other in _sqWater)
            {
                if (other.Active && other.Serial == _sqSerial && !other.Water.Ended) other.Water.End(other.Water.Length);
                if (run == null && !other.Active) run = other;
            }
            if (run == null) run = StealSquallWater();
            if (!TryAcquire(PelagVfxId.SquallWater, out GameObject go, out PelagVfxElement element)) return null;
            if (run.Object != go)
            {
                run.Object = go;
                Transform root = go.transform;
                run.Filter = root.Find("Water")?.GetComponent<MeshFilter>();
                run.SkidFoam = root.Find("SkidFoam")?.GetComponent<ParticleSystem>();
                run.SkidDrops = root.Find("SkidDrops")?.GetComponent<ParticleSystem>();
                run.TailDrops = root.Find("TailDrops")?.GetComponent<ParticleSystem>();
                run.EdgeFoam = root.Find("EdgeFoam")?.GetComponent<ParticleSystem>();
            }
            _sqGroundBase = PlayerPosition().y;
            if (_sqGround == null) _sqGround = SquallGroundAt;
            run.Water.Begin(kind, points, count, smooth, _sqGround);
            Vector3 at = run.Water.Root;
            int fx = ReserveActive();
            // Корень — начало пути на земле, без поворота и масштаба: меш пишется в метрах.
            element.Begin(at, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            PelagSquallFormLook.Apply(go, _sqForm);
            _active[fx] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.SquallWater, Object = go, Element = element,
                // Страховочный срок; гасит воду PelagSquallWater.Done.
                Duration = kind == PelagSquallWater.Kind.Trail ? 8f : PelagSquallWater.MaxLife + .3f,
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            run.Active = true;
            run.Fx = fx;
            run.Serial = _sqSerial;
            run.Index = index;
            run.StartTick = tick;
            run.Strip = -1;
            run.SkidAlong = 0f;
            run.FoamCarry = run.DropCarry = .6f;
            run.EdgeCarry = 0f;
            if (run.Filter != null) run.Water.Build(PelagSquallWater.MeshFor(run.Filter));
            EmitSquallTakeoff(run);
            return run;
        }

        /// <summary>Мест нет: уступает вода, которой жить меньше всех.</summary>
        private SquallWaterRun StealSquallWater()
        {
            SquallWaterRun pick = _sqWater[0];
            float best = float.MaxValue;
            foreach (SquallWaterRun run in _sqWater)
            {
                PelagSquallWater w = run.Water;
                float left = w.Bound ? w.UntilAge - w.Age
                    : w.Ended ? PelagSquallWater.StreakLifeAfterEnd - (w.Age - w.EndedAt) : PelagSquallWater.MaxLife - w.Age;
                if (left < best) { best = left; pick = run; }
            }
            ReleaseSquallWater(pick);
            return pick;
        }

        private void ReleaseSquallWater(SquallWaterRun run)
        {
            if (run == null) return;
            if (run.Active && SquallStillActive(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Strip = -1;
        }

        /// <summary>Удар/посадка: струя прыжка <paramref name="index"/> текущей серии дальше не растёт.</summary>
        private void EndSquallWaterAt(int index, Vector3 at)
        {
            if (_sqWater == null) return;
            foreach (SquallWaterRun run in _sqWater)
                if (run.Active && run.Serial == _sqSerial && run.Index == index && !run.Water.Ended)
                    run.Water.End(Mathf.Max(0f, run.Water.Project(at)));
        }

        /// <summary>Серия кончилась (в том числе сорвана): всё, что растёт, встаёт где есть.</summary>
        private void EndAllSquallWater()
        {
            if (_sqWater == null) return;
            foreach (SquallWaterRun run in _sqWater)
                if (run.Active && !run.Water.Ended) run.Water.End(run.Water.Length);
        }

        private bool AnySquallTrailLive()
        {
            if (_sqWater == null) return false;
            foreach (SquallWaterRun run in _sqWater)
                if (run.Active && run.Water.Bound && !run.Water.Done) return true;
            return false;
        }

        /// <summary>
        /// Пенный след: Sim положил полосу (событие — в тик удара, до SquallStrike). Струя
        /// этого прыжка становится полосой: путь — ровно полоса урона, срок — до конца полосы.
        /// </summary>
        private void BindSquallFoamStrip(in SimEvent e, int tick)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || CaptureRig.NoVfx
                || !sim.TryGetSquallFoamStrip(e.Amount, out FixVec2 from, out FixVec2 to, out int until)) return;
            if (sim.Squall.Serial != _sqSerial) BeginSquallCast(sim, sim.Squall);
            float y = PlayerPosition().y;
            Vector3 a = SquallWorld(from, y), b = SquallWorld(to, y);
            SquallWaterRun run = null;
            if (_sqWater != null)
                foreach (SquallWaterRun other in _sqWater)
                    if (other.Active && other.Serial == _sqSerial && other.Water.Mode == PelagSquallWater.Kind.Trail
                        && !other.Water.Bound && (run == null || other.Index > run.Index))
                        run = other;
            // Прыжок прошёл без своей воды (эффекты были выключены) — полоса рождается сразу.
            if (run == null) run = BeginSquallWater(_sqFlightIndex, tick, a, b, PelagSquallWater.Kind.Trail);
            if (run == null) return;
            _sqGroundBase = y;
            float untilAge = (until - run.StartTick) / (float)Simulation.TicksPerSecond;
            run.Water.Bind(a, b, untilAge, _sqGround ?? (_sqGround = SquallGroundAt));
            run.Strip = e.Amount;
            run.Object.transform.position = run.Water.Root;
            ref ActiveFx fx = ref _active[run.Fx];
            fx.Duration = fx.Age + Mathf.Max(0f, untilAge - run.Water.Age) + 1f;
            EnsureSquallCue();
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[squall-vfx] foam strip slot={e.Amount} jump={run.Index} length={run.Water.FinalLength:F2} until={until} life={(untilAge - run.Water.Age):F2}");
        }

        /// <summary>Кадр воды: рост за героем, занос по краям, живые гребни полосы, распад, возврат в пул.</summary>
        private void UpdateSquallWater(float now, float dt)
        {
            if (_sqWater == null) return;
            Vector3 hero = PlayerPosition();
            foreach (SquallWaterRun run in _sqWater)
            {
                if (!run.Active) continue;
                // Смерть героя и сброс арены возвращают всё в пул мимо нас.
                if (!SquallStillActive(run.Fx, run.Object)) { run.Active = false; continue; }
                PelagSquallWater water = run.Water;
                float age = (now - run.StartTick) / Simulation.TicksPerSecond;
                // Струя текущего прыжка идёт за телом и после удара (тело рисуется с отставанием на тик):
                // после конца роста её держит длина пути из Sim (Advance), как след рывка.
                bool follow = run.Serial == _sqSerial && run.Index == _sqFlightIndex;
                water.Advance(age, follow ? water.Project(hero) : 0f);
                // Пенный след без полосы (удар сорвался) — доживает обычной струёй.
                if (water.Mode == PelagSquallWater.Kind.Trail && !water.Bound && water.Ended && water.Age - water.EndedAt > .15f)
                    water.Demote();
                if (water.Length > run.SkidAlong && run.SkidFoam != null && run.SkidDrops != null)
                {
                    EmitSquallSkid(run, run.SkidAlong, water.Length);
                    run.SkidAlong = water.Length;
                }
                if (water.Bound && run.EdgeFoam != null && dt > 0f && water.UntilAge - water.Age > SquallTrailEdgeQuiet)
                {
                    run.EdgeCarry += dt * SquallEdgeFoamPerMetre * water.FinalLength * water.Spread;
                    EmitSquallEdgeFoam(run);
                }
                if (run.Filter != null) water.Build(PelagSquallWater.MeshFor(run.Filter));
                if (water.Done) ReleaseSquallWater(run);
            }
        }

        /// <summary>Резкий старт: капли позади точки отрыва и клочья пены, отброшенные назад.</summary>
        private static void EmitSquallTakeoff(SquallWaterRun run)
        {
            PelagSquallWater water = run.Water;
            Vector3 start = water.Sample(0f, out Vector3 dir);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (run.TailDrops != null)
            {
                int count = Random.Range(3, 6);
                float back = .02f;
                for (int i = 0; i < count; i++)
                {
                    float k = i / (float)Mathf.Max(1, count - 1);
                    back += Random.Range(.07f, .16f);
                    Vector3 position = start - dir * back + right * (Random.Range(-1f, 1f) * (.08f + i * .04f));
                    position.y = start.y + .03f;
                    run.TailDrops.Emit(new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = -dir * Random.Range(0f, .3f),
                        startSize = Mathf.Lerp(.12f, .06f, k) * Random.Range(.88f, 1.12f),
                        startLifetime = Random.Range(.30f, .40f),
                        applyShapeToPosition = false
                    }, 1);
                }
            }
            if (run.SkidFoam != null)
                for (int i = 0; i < 4; i++)
                {
                    Vector3 position = start - dir * .05f + right * Random.Range(-.2f, .2f);
                    position.y = start.y + .05f;
                    run.SkidFoam.Emit(new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = -dir * Random.Range(1.2f, 2.4f) + right * Random.Range(-.6f, .6f) + Vector3.up * Random.Range(.3f, .8f),
                        startSize = Random.Range(.14f, .24f),
                        startLifetime = Random.Range(.30f, .42f),
                        applyShapeToPosition = false
                    }, 1);
                }
        }

        /// <summary>
        /// Занос на пройденном отрезке [a, b] м: клочья срываются с гребней и скользят
        /// наружу, капли летят низко наружу — внутрь воды и выше колена ничего не попадает.
        /// </summary>
        private static void EmitSquallSkid(SquallWaterRun run, float a, float b)
        {
            float distance = b - a;
            if (distance <= 0f) return;
            PelagSquallWater water = run.Water;
            run.FoamCarry += distance * SquallSkidFoamPerMetre;
            run.DropCarry += distance * SquallSkidDropsPerMetre;
            int foam = (int)run.FoamCarry, drops = (int)run.DropCarry;
            run.FoamCarry -= foam;
            run.DropCarry -= drops;
            for (int i = 0; i < foam + drops; i++)
            {
                bool isFoam = i < foam;
                float s = Random.Range(a, b) - .10f;
                Vector3 centre = water.Sample(s, out Vector3 dir);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                float side = Random.value < .5f ? -1f : 1f;
                float edge = water.HalfWidthAt(s) + .04f;
                Vector3 position = centre + right * side * edge * Random.Range(.9f, 1.12f);
                position.y = centre.y + (isFoam ? .05f : .06f);
                if (isFoam)
                    run.SkidFoam.Emit(new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = -dir * Random.Range(.2f, .8f) + right * side * Random.Range(.6f, 1.4f) + Vector3.up * Random.Range(.1f, .5f),
                        startSize = Random.Range(.12f, .24f),
                        startLifetime = Random.Range(.26f, .40f),
                        applyShapeToPosition = false
                    }, 1);
                else
                    run.SkidDrops.Emit(new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = -dir * Random.Range(.4f, 1.4f) + right * side * Random.Range(1.0f, 2.0f) + Vector3.up * Random.Range(1.0f, 2.0f),
                        startSize = Random.Range(.05f, .09f),
                        startLifetime = Random.Range(.22f, .34f),
                        applyShapeToPosition = false
                    }, 1);
            }
        }

        /// <summary>Живая полоса: комья пены всплывают на гребнях и медленно расходятся по течению.</summary>
        private static void EmitSquallEdgeFoam(SquallWaterRun run)
        {
            int count = (int)run.EdgeCarry;
            run.EdgeCarry -= count;
            PelagSquallWater water = run.Water;
            for (int i = 0; i < count; i++)
            {
                float s = Random.Range(0f, water.FinalLength);
                Vector3 centre = water.Sample(s, out Vector3 dir);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                float side = Random.value < .5f ? -1f : 1f;
                Vector3 position = centre + right * side * (water.HalfWidthAt(s) + Random.Range(-.02f, .06f));
                position.y = centre.y + .04f;
                run.EdgeFoam.Emit(new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = right * side * Random.Range(.10f, .30f) + Vector3.up * Random.Range(.05f, .20f) - dir * Random.Range(0f, .25f),
                    startSize = Random.Range(.10f, .18f),
                    startLifetime = Random.Range(.45f, .70f),
                    applyShapeToPosition = false
                }, 1);
            }
        }
    }
}
