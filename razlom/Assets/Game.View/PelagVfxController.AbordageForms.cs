using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 — фронт воды форм (только миг удара; тайминг каста у всех один):
    ///  • ОБВАЛ (кадр D-quake, кобальт): кулак в землю — комья земли и столб брызг в
    ///    центре (слои префаба), по земле бежит кольцо воды от места героя до радиуса
    ///    Sim (3 м) за 5 тиков — с разгоном и замедлением, полоса заливает круг и к
    ///    концу хода остаётся толстым кольцом, гребень дотекает ≤ 0,12 м и рвётся на
    ///    капли; с бегущего гребня летят брызги и клочья; каждый сбитый — корона у ног
    ///    (PelagVfxController.AbordageCues, Damage по тику фронта);
    ///  • ПРОБОИНА (кадр G-breach, маджента): прокол пены сквозь цель (всплеск по оси
    ///    на её спине) и за ней конус-струя по земле — вершина и ось из Sim, ширина —
    ///    край конуса ±25°, фронт до 4 м за 4 тика, вода бежит от вершины, с фронта
    ///    летит веер капель-лепестков; задетым — всплеск по ходу струи, отброшенным —
    ///    след волока (.AbordageWake).
    /// Видимый край = край урона Sim (PelagAbordageVfxRules.Front).
    /// Круг 2 (03.10): Обвал — не ровный тор, а рваный радиальный всплеск: 22 острых языка, кончики
    /// на гребне, просветы внутрь (RaggedOuter/RaggedInner), комья земли и бурые брызги грязи
    /// из центра, всплеск и веер пака у кулака. Пробоина — дальний конец дугой сектора Sim,
    /// нос в пене и каплях (BreachTipAge), в конце хода рассыпается клочьями.
    /// Круг 3 (03.10, ревью Обвала по кадру D): вода сплошная от ядра взбитой земли у героя до фронта,
    /// лучами от центра (Style.Splash), языки — только на внешнем фронте; белая пена — у внешней кромки,
    /// в центре — тёмная мокрая земля и кобальт (без большого веера пака над героем); комья — сплошные
    /// гранёные камни с тёмным обводом (материал M_Abordage_Clod), полосы грязи — тем же материалом.
    /// Круг 4 (03.10, ревью в игре: ровное «солнце» с пилой и чёрным обводом, сиреневая полоса у героя,
    /// распад белил весь диск): свой всплеск PelagAbordageQuakeWater на шейдере Razlom/Abordage Quake
    /// Splash — круглые лопасти кобальта разной длины с пеной на кончиках, под героем непрозрачная мокрая
    /// земля с лепестками и лучами между лопастями, распад дырами от центра, кайма пены рвётся на капли;
    /// капли летят с кончиков лопастей; комья вылетают снаружи тела героя, а не с его ног.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AbordageFrontRun
        {
            public bool Active, Burst, RimShed;
            public int Fx = -1, StartTick, Travel;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem Spray, Foam, Clods;
            public readonly PelagAbordageQuakeWater Quake = new PelagAbordageQuakeWater();
            public readonly PelagAbordageJetWater Jet = new PelagAbordageJetWater();
            public readonly FormGroundGrid Ground = new FormGroundGrid();
            public Vector3 Centre, Dir;
            /// <summary>Clock — секунды с удара на этом кадре (рябь края всплеска Обвала).</summary>
            public float Reach, SprayCarry, FoamCarry, Flow, Clock;
            /// <summary>Пробоина: вершина в виде глубже вершины Sim на столько (видимая спина цели), м; Обвал — 0.</summary>
            public float Inset;
            public PelagForm Form;
        }

        private readonly AbordageFrontRun[] _abFronts =
            { new AbordageFrontRun(), new AbordageFrontRun(), new AbordageFrontRun(), new AbordageFrontRun() };
        /// <summary>Последние Обвал и Пробоина — для знаков задетых (центр волны, ось струи).</summary>
        private AbordageFrontRun _abQuake = new AbordageFrontRun(), _abBreach = new AbordageFrontRun();

        private AbordageFrontRun TakeAbordageFront()
        {
            AbordageFrontRun pick = null;
            foreach (AbordageFrontRun run in _abFronts)
                if (!run.Active) { pick = run; break; }
                else if (pick == null || run.StartTick < pick.StartTick) pick = run;
            ReleaseAbordageFront(pick);
            return pick;
        }

        private void ReleaseAbordageFront(AbordageFrontRun run)
        {
            if (run.Active && AbStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetAbordageForms()
        {
            foreach (AbordageFrontRun run in _abFronts) ReleaseAbordageFront(run);
            foreach (AbordageWakeRun d in _abDrags) ReleaseAbordageWake(d);
            foreach (AbordageWakeRun w in _abWakes) ReleaseAbordageWake(w);
        }

        private bool BindAbordageFront(AbordageFrontRun run, PelagVfxId id, Vector3 root, string mesh)
        {
            int fx = AbSpawn(id, root, Quaternion.identity, 0f, PelagAbordageVfxRules.FrontLifeSeconds(run.Travel), run.Form, out GameObject go);
            if (fx < 0) return false;
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            run.Filter = go.transform.Find(mesh)?.GetComponent<MeshFilter>();
            run.Spray = go.transform.Find("Spray")?.GetComponent<ParticleSystem>();
            run.Foam = go.transform.Find("Foam")?.GetComponent<ParticleSystem>();
            run.Clods = go.transform.Find("Clods")?.GetComponent<ParticleSystem>();
            return true;
        }

        /// <summary>
        /// Кулак в землю (кадр D — радиальный всплеск с комьями): комья земли разлетаются по всему
        /// кругу (низкой дугой, падают внутри радиуса Sim), маленький всплеск у кулака. Круг 3: большой
        /// веер пака (AbordageBurst) и всплеск ×1 над кулаком закрывали героя ~0,2 с сливочными лепестками —
        /// веера нет, всплеск низкий и вдвое меньше. Круг 4: тёмных полос грязи («Dirt») больше нет — бурые
        /// лучи и лепестки мокрой земли рисует сам всплеск (шейдер Обвала), полосы читались чёрными шипами.
        /// </summary>
        private void AbordageQuakeImpact(AbordageFrontRun run)
        {
            Vector3 facing = PlayerFacing();
            Vector3 fist = run.Centre + facing * .35f;
            fist.y = run.Ground.At(fist.x, fist.z);
            AbordageBodySplash(fist + Vector3.up * .15f, Vector3.up + facing * .6f, AbordageQuakeFistSplash, PelagForm.AbordageQuake);
            if (run.Clods != null)
                for (int i = 0; i < AbordageQuakeClods; i++)
                {
                    float angle = (i + Random.Range(-.35f, .35f)) * (Mathf.PI * 2f / AbordageQuakeClods);
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    // Круг 4: вылетает снаружи тела героя (не кучей на ногах), падает внутри круга — дальность
                    // 0,20–0,68 радиуса за 0,3–0,46 с от старта на 0,50–0,70 м.
                    float reach = run.Reach * Random.Range(PelagAbordageVfxRules.QuakeClodReachMin, PelagAbordageVfxRules.QuakeClodReachMax);
                    float flight = Random.Range(.30f, .46f);
                    PelagAbordageVfxRules.ClodLaunch(reach, flight, out float horizontal, out float vertical);
                    run.Clods.Emit(new ParticleSystem.EmitParams
                    {
                        position = run.Centre + radial * Random.Range(PelagAbordageVfxRules.QuakeClodStartMin, PelagAbordageVfxRules.QuakeClodStartMax)
                                   + Vector3.up * .12f,
                        velocity = radial * horizontal + Vector3.up * vertical,
                        startSize = Random.Range(PelagAbordageVfxRules.QuakeClodSizeMin, PelagAbordageVfxRules.QuakeClodSizeMax),
                        startLifetime = flight,
                        applyShapeToPosition = false
                    }, 1);
                }
        }

        /// <summary>Комьев земли на удар Обвала и масштаб всплеска у кулака (круг 2 — 1,0 над кулаком).</summary>
        private const int AbordageQuakeClods = 16;
        private const float AbordageQuakeFistSplash = .5f;

        /// <summary>Обвал (тик показа удара): кольцо воды от места героя, комья и брызги кулака в землю.</summary>
        private void BeginAbordageQuake(in AbPending p)
        {
            AbordageFrontRun run = TakeAbordageFront();
            run.Form = PelagForm.AbordageQuake;
            run.Reach = p.Extra > 0 ? p.Extra / 100f : Simulation.AbordageQuakeRadius.ToFloat();
            run.Travel = p.Amount > 0 ? p.Amount : Simulation.AbordageQuakeTravelTicks;
            run.StartTick = p.Tick;
            run.Burst = run.RimShed = false;
            run.SprayCarry = run.FoamCarry = run.Flow = 0f;
            run.Centre = SampleAbordageGround(run.Ground, p.At, run.Reach + 1f);
            run.Inset = 0f;
            run.Active = true;
            _abQuake = run;
            if (BindAbordageFront(run, PelagVfxId.AbordageQuake, run.Centre, "Ring")) run.Quake.Begin();
            if (CaptureRig.NoVfx) return;
            AbordageQuakeImpact(run);
            _juice?.PunchCamera(.30f, .06f);
            PulseCombatLight(.55f);
        }

        /// <summary>Пробоина (тик показа удара): прокол сквозь цель и конус-струя за её спиной.</summary>
        private void BeginAbordageBreach(in AbPending p)
        {
            AbordageFrontRun run = TakeAbordageFront();
            run.Form = PelagForm.AbordageBreach;
            run.Reach = p.Extra > 0 ? p.Extra / 100f : Simulation.AbordageBreachLength.ToFloat();
            run.Travel = p.Amount > 0 ? p.Amount : Simulation.AbordageBreachTravelTicks;
            run.StartTick = p.Tick;
            run.Burst = false;
            run.SprayCarry = run.FoamCarry = run.Flow = 0f;
            run.Dir = p.Dir.sqrMagnitude > .01f ? p.Dir.normalized : PlayerFacing();
            // Вершина Sim — край тела (центр + ось·r) — у видимой модели в 0,5 м за спиной: струя в виде
            // начинается со спины модели (тот же отступ, что у укуса), край конуса и дальность — как в Sim.
            // Точка корпуса босса (Hull) — уже на теле, её не двигаем.
            Simulation sim = _driver.Sim;
            bool hull = _abRun.Active && _abRun.Serial == p.Serial && _abRun.Hull;
            run.Inset = !hull && _abRun.Serial == p.Serial && p.Target >= 0 && sim != null && (uint)p.Target < (uint)sim.Entities.Count
                ? PelagAbordageVfxRules.BodyInset(sim.Entities.BodyRadius[p.Target].ToFloat()) : 0f;
            Vector3 apex = p.At - run.Dir * run.Inset;
            Vector3 mid = SampleAbordageGround(run.Ground, apex + run.Dir * ((run.Reach + run.Inset) * .5f), (run.Reach + run.Inset) * .5f + 2.2f);
            run.Centre = new Vector3(apex.x, run.Ground.At(apex.x, apex.z), apex.z);
            run.Active = true;
            _abBreach = run;
            if (BindAbordageFront(run, PelagVfxId.AbordageBreach, run.Centre, "Jet")) run.Jet.Begin();
            if (CaptureRig.NoVfx) return;
            // Прокол: всплеск на спине цели по оси струи — пена выходит насквозь.
            float chest = p.Target >= 0 && sim != null && (uint)p.Target < (uint)sim.Entities.Count
                ? PelagAbordageVfxRules.BiteHeight(sim.Entities.BodyRadius[p.Target].ToFloat()) : 1f;
            AbordageBodySplash(new Vector3(apex.x, mid.y + chest, apex.z), run.Dir + Vector3.up * .15f, 1.1f, PelagForm.AbordageBreach);
            _juice?.PunchCamera(.20f, .04f);
        }

        /// <summary>Кадр фронтов: меш воды по непрерывному тику показа, брызги с бегущего гребня, разрыв.</summary>
        private void UpdateAbordageForms(Simulation sim, float shown, float dt)
        {
            foreach (AbordageFrontRun run in _abFronts)
            {
                if (!run.Active || run.Fx < 0) continue;
                if (!AbStill(run.Fx, run.Object)) { run.Fx = -1; run.Object = null; continue; }
                float x = PelagAbordageVfxRules.FrontProgress(shown, run.StartTick, run.Travel);
                float t = PelagAbordageVfxRules.Seconds(shown - run.StartTick);
                run.Clock = t;
                bool quake = run.Form == PelagForm.AbordageQuake;
                float ease = quake ? PelagAbordageVfxRules.QuakeEase : PelagAbordageVfxRules.BreachEase;
                // Пробоина: от видимой спины цели — сначала прокол сквозь тело (Inset), дальше фронт Sim.
                float front = run.Inset + PelagAbordageVfxRules.Front(shown, run.StartTick, run.Travel, run.Reach, ease);
                float speed = (run.Inset + PelagAbordageVfxRules.Front(shown + .25f, run.StartTick, run.Travel, run.Reach, ease) - front)
                              / PelagAbordageVfxRules.Seconds(.25f);
                float age = PelagAbordageVfxRules.FrontBreakAge(shown, run.StartTick, run.Travel, quake ? .02f : 0f);
                float churn = .10f + .30f * PelagAbordageVfxRules.Smooth01(x);
                if (quake)
                {
                    // Круг 4: всплеск лопастями с мокрой землёй у героя; распад — дырами от центра (шейдер).
                    if (run.Filter != null)
                        run.Quake.Build(FormWaterMesh.MeshFor(run.Filter, PelagAbordageQuakeWater.MeshName), run.Centre,
                            front, age, t, 1f, run.Ground);
                    // Кайма пены рвётся на капли (шейдер, с QuakeRimDropsFrom) — с кончиков слетают круглые клочья.
                    if (!run.RimShed && age >= PelagAbordageVfxRules.QuakeRimDropsFrom)
                    {
                        run.RimShed = true;
                        if (!CaptureRig.NoVfx) EmitAbordageQuakeRim(run, front, t);
                    }
                }
                else
                {
                    run.Flow += dt * 6f;
                    if (run.Filter != null)
                        run.Jet.Build(FormWaterMesh.MeshFor(run.Filter, PelagAbordageJetWater.MeshName), run.Centre, run.Dir,
                            front, age, run.Flow, churn, t, 1f, run.Ground, run.Inset);
                }
                if (CaptureRig.NoVfx || dt <= 0f) continue;
                if (x < 1.05f && front > .05f)
                {
                    float strength = Mathf.Clamp01(1.15f - x * .6f);
                    // Круг 4 (кадр D — много синих и белых капель наружу): у Обвала капель больше.
                    run.SprayCarry += dt * (quake ? 130f : 80f) * strength;
                    run.FoamCarry += dt * (quake ? 40f : 30f) * strength;
                    EmitAbordageFront(run, front, speed, ref run.SprayCarry, true);
                    EmitAbordageFront(run, front, speed, ref run.FoamCarry, false);
                }
                if (!run.Burst && x >= 1f)
                {
                    run.Burst = true;
                    float burst = quake ? 30f : 16f;
                    EmitAbordageFront(run, front, speed, ref burst, true);
                    // Круг 2 (кадр G): дальний конец струи рассыпается клочьями пены по дуге, а не срезается.
                    if (!quake) EmitAbordageBreachEnd(run, front);
                }
            }
        }

        /// <summary>Конец хода Пробоины: клочья пены и капли-лепестки по дуге дальнего конца, летят дальше по ходу.</summary>
        private void EmitAbordageBreachEnd(AbordageFrontRun run, float front)
        {
            var right = new Vector3(run.Dir.z, 0f, -run.Dir.x);
            float reach = front - run.Inset;
            float half = PelagAbordageVfxRules.BreachHalfWidth(reach);
            for (int i = 0; i < 18; i++)
            {
                bool foam = i % 3 != 2;
                ParticleSystem system = foam ? run.Foam : run.Spray;
                if (system == null) continue;
                float side = Random.Range(-1f, 1f);
                float across = side * half * Random.Range(.6f, 1f);
                float s = front - Random.Range(0f, .5f);
                Vector3 at = run.Centre + run.Dir * (s - PelagAbordageVfxRules.BreachArcBack(across, s - run.Inset)) + right * across;
                at.y = run.Ground.At(at.x, at.z) + .06f;
                Vector3 away = (run.Dir + right * side * .45f).normalized;
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = away * Random.Range(foam ? .8f : 1.6f, foam ? 2f : 3.2f) + Vector3.up * Random.Range(foam ? .3f : 1.2f, foam ? .9f : 2.4f),
                    startColor = PelagAbordageFormLook.DropColor(run.Form, foam ? Random.Range(0f, .3f) : Random.Range(.4f, 1f)),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>
        /// Брызги и клочья с бегущего фронта. Обвал — с гребня кольца наружу (круг 3: только с
        /// внешнего фронта — белое у кромки, внутрь к герою не летит); Пробоина — веер капель-лепестков
        /// с носа струи по её ходу.
        /// </summary>
        private void EmitAbordageFront(AbordageFrontRun run, float front, float speed, ref float carry, bool spray)
        {
            int count = (int)carry;
            carry -= count;
            ParticleSystem system = spray ? run.Spray : run.Foam;
            if (system == null || count <= 0) return;
            bool quake = run.Form == PelagForm.AbordageQuake;
            var right = new Vector3(run.Dir.z, 0f, -run.Dir.x);
            for (int i = 0; i < count; i++)
            {
                Vector3 at, away;
                if (quake)
                {
                    // Круг 4 (кадр D): капли летят с кончиков лопастей (3 из 4), остальные — с края где придётся.
                    float angle = Random.value < .75f ? run.Quake.TipAngle() : Random.Range(0f, Mathf.PI * 2f);
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    at = run.Centre + radial * Mathf.Max(.05f, run.Quake.Edge(angle, front, run.Clock) - Random.Range(0f, .2f));
                    away = radial;
                }
                else
                {
                    float side = Random.Range(-1f, 1f);
                    float s = spray ? front * Random.Range(.85f, 1f) : front * Random.Range(.15f, 1f);
                    float edge = spray ? side : Mathf.Sign(side);
                    // На дуге дальнего конца (PelagAbordageJetWater): ряд гнётся вокруг вершины Sim.
                    float across = edge * PelagAbordageVfxRules.BreachHalfWidth(s - run.Inset);
                    at = run.Centre + run.Dir * (s - PelagAbordageVfxRules.BreachArcBack(across, s - run.Inset)) + right * across;
                    away = spray ? (run.Dir + right * side * .55f).normalized : right * Mathf.Sign(side);
                }
                at.y = run.Ground.At(at.x, at.z) + (spray ? .07f : .05f);
                Vector3 velocity = spray
                    ? away * (Random.Range(1.0f, 2.4f) + speed * .25f) + Vector3.up * Random.Range(1.4f, 3.0f)
                    : away * (Random.Range(.4f, 1.2f) + speed * .12f) + Vector3.up * Random.Range(.2f, .6f);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = velocity,
                    startColor = quake && spray ? AbordageQuakeDropColor()
                        : PelagAbordageFormLook.DropColor(run.Form, spray ? Random.Range(.35f, 1f) : Random.Range(0f, .35f)),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Капля Обвала (кадр D): половина — насыщенный кобальт воды, половина — светлая с белым.</summary>
        private static Color AbordageQuakeDropColor()
        {
            if (Random.value < .5f) return PelagAbordageFormLook.DropColor(PelagForm.AbordageQuake, Random.Range(.2f, 1f));
            Color c = PelagAbordageFormLook.For(PelagForm.AbordageQuake).Water * Random.Range(1.05f, 1.3f);
            c.a = 1f;
            return c;
        }

        /// <summary>
        /// Круг 4: кайма пены Обвала рвётся на капли (шейдер) — с кончиков лопастей слетают круглые белые клочья
        /// и капли кобальта, невысоко и недалеко: распад читается брызгами, а не белым диском.
        /// </summary>
        private void EmitAbordageQuakeRim(AbordageFrontRun run, float front, float time)
        {
            for (int i = 0; i < 30; i++)
            {
                bool foam = i % 3 != 2;
                ParticleSystem system = foam ? run.Foam : run.Spray;
                if (system == null) continue;
                float angle = Random.value < .8f ? run.Quake.TipAngle() : Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = run.Centre + radial * Mathf.Max(.05f, run.Quake.Edge(angle, front, time) - Random.Range(0f, .3f));
                at.y = run.Ground.At(at.x, at.z) + .06f;
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = radial * Random.Range(.5f, 1.4f) + Vector3.up * Random.Range(foam ? .4f : 1f, foam ? 1.1f : 2.2f),
                    startColor = foam ? PelagAbordageFormLook.DropColor(run.Form, Random.Range(0f, .25f)) : AbordageQuakeDropColor(),
                    applyShapeToPosition = false
                }, 1);
            }
        }
    }
}
