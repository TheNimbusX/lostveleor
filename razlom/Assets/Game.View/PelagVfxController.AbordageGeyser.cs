using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 · ГЕЙЗЕР (кадр F-geyser-seagreen, морская зелень). Апперкот — под
    /// целью из земли бьёт столб воды: лёгкую цель (AbordageGeyserLift.Flag) вид
    /// поднимает на шапке столба (смещение показа тела — Sim держит её на месте) по
    /// кривой PelagAbordageVfxRules.GeyserLift01: резкий взлёт, зависание, падение с
    /// ускорением ровно к тику падения воды Sim; тяжёлые, элиты и босс не взлетают —
    /// столб бьёт вокруг тела и стоит. С шапки летят пена и капли; на земле — живое
    /// кольцо там, где упадёт вода (радиус падения Sim, 2 м). AbordageGeyserFall —
    /// столб оседает, кольцо раздаётся всплеском и рвётся на капли, в центре — веер
    /// брызг; каждый задетый — корона у ног (.AbordageCues). Цепь к этому мигу уже
    /// смотана (спека 5: столб без цепи).
    /// Круг 2 (03.10, с камеры игры столб читался низким широким «шатром»): узкий ствол
    /// ~3,4 м, крона пены вокруг ног подброшенной, струи капель вверх по стволу, у
    /// основания — рваная юбка брызг (меш «BaseRing») и корона пака раз в 0,22 с.
    /// Круг 3 (03.10, ревью по кадру F): ствол шире (≈0,9 героя) и зелёный во всю ширину — пена по
    /// краям тонкая, светлые струи сильнее (материал столба v4); короны пака с шипами ушли и с шапки,
    /// и от основания: крона — облако круглых клубов пены вокруг подброшенного тела (цель сидит В
    /// пене), основание — круглая розетка (лепестки Style.Rosette, клубы по кромке, капли наружу);
    /// кольцо падения — тонкая тихая полоса морской зелени, проявляется за 2 кадра без «хаки».
    /// Круг 4 (03.10, ревью в игре): пена — не россыпь кремовых «яиц» с обводом у каждого, а слитые массы:
    /// каждый клуб рисуется силуэтом («CrownBack», раньше) и заливкой без обвода («Crown») — обвод только
    /// по краю объединения; крона облегает подброшенное тело, у основания — круглая розетка с ровной пенной
    /// кромкой, клубов по стволу нет. Столб — под телом цели: место берётся по позиции тела без отдачи
    /// удара (отдача кулака сдвигала столб на ~0,3 м от тела), у подброшенной столб идёт за телом до
    /// падения. Розетка — сплошной диск воды: тёмного пятна пола у ствола нет.
    /// Круг 5 (03.10, проверка круга 4 в игре): у подброшенной столб, розетка и пена — под тазом поднятой модели
    /// каждый кадр (Base; модель висела на ~0,4 м ближе к герою, чем позиция тела); кольцо падения — по-прежнему
    /// вокруг позиции тела (зона Sim). Всплеск падения — слитая пена той же парой клубов, не веер пака и не
    /// короны («яйца»).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AbordageGeyserRun
        {
            public bool Active, Lifted, Fell;
            public int Target = -1, StartTick, FallTick, Fx = -1;
            public GameObject Object;
            public MeshFilter ColumnFilter, RingFilter, SkirtFilter;
            /// <summary>Crown — заливка клубов пены, CrownBack — их силуэт (обвод объединения), выбрасываются парой.</summary>
            public ParticleSystem Crown, CrownBack, Spray;
            public readonly PelagAbordageColumnWater Column = new PelagAbordageColumnWater();
            public readonly PelagAbordageRingWater Ring = new PelagAbordageRingWater();
            /// <summary>Юбка всплеска у основания (кадр F): рваное кольцо морской зелени вокруг ствола.</summary>
            public readonly PelagAbordageRingWater Skirt = new PelagAbordageRingWater();
            public readonly FormGroundGrid Ground = new FormGroundGrid();
            /// <summary>Centre — позиция тела на земле (корень, кольцо падения); Base — основание столба, розетки и пены.</summary>
            public Vector3 Centre, Base;
            /// <summary>Круг 5: таз поднятой модели (ищется раз на вид), иначе — середина её кожи.</summary>
            public Transform Pelvis, PelvisView;
            public SkinnedMeshRenderer[] Skins;
            public float Radius, Body, CrownCarry, StreakCarry, Flow;
        }

        private readonly AbordageGeyserRun[] _abGeysers =
            { new AbordageGeyserRun(), new AbordageGeyserRun(), new AbordageGeyserRun(), new AbordageGeyserRun() };
        /// <summary>Где упала последняя вода Гейзера — корона задетых клонится от неё.</summary>
        private Vector3 _abFallCentre;
        /// <summary>Столб живёт после падения воды, с: оседание и капли кольца.</summary>
        private const float AbordageGeyserAfterFall = .7f;
        /// <summary>
        /// Высота клуба к ширине. Круг 4: кадр листа капель пака (2:1) ложится на квадрат частицы — эллипс в нём
        /// уже почти круглый, а ×1,45 круга 3 вытягивал клубы в стоячие «яйца».
        /// </summary>
        private const float AbordageGeyserPuffAspect = 1f;

        private void BeginAbordageGeyser(in AbPending p)
        {
            AbordageGeyserRun run = null;
            foreach (AbordageGeyserRun g in _abGeysers)
                if (g.Active && g.Target == p.Target) { run = g; break; }
            if (run == null)
                foreach (AbordageGeyserRun g in _abGeysers)
                    if (!g.Active) { run = g; break; }
            if (run == null)
            {
                run = _abGeysers[0];
                foreach (AbordageGeyserRun g in _abGeysers) if (g.StartTick < run.StartTick) run = g;
            }
            ForgetAbordageGeyser(run);
            Simulation sim = _driver.Sim;
            run.Active = true;
            run.Fell = false;
            run.Target = p.Target;
            run.Lifted = p.Flag;
            run.StartTick = p.Tick;
            run.FallTick = p.Tick + (p.Amount > 0 ? p.Amount : Simulation.AbordageGeyserLiftTicks);
            run.Radius = p.Extra > 0 ? p.Extra / 100f : Simulation.AbordageGeyserFallRadius.ToFloat();
            run.Body = sim != null && (uint)p.Target < (uint)sim.Entities.Count ? sim.Entities.BodyRadius[p.Target].ToFloat() : .45f;
            run.CrownCarry = run.StreakCarry = run.Flow = 0f;
            Vector3 body = EntityPosition(p.Target, p.At);
            // Круг 4: столб — под телом без отдачи удара (вид тела в этот кадр ещё отброшен кулаком на ~0,3 м).
            Vector3 foot = BaseEntityPosition(p.Target, body);
            run.Centre = SampleAbordageGround(run.Ground, new Vector3(foot.x, PlayerPosition().y, foot.z), run.Radius + 1f);
            // Круг 5: у подброшенной столб встаёт под тазом поднятой модели (тяжёлые — где ударил).
            run.Pelvis = run.PelvisView = null;
            run.Skins = null;
            run.Base = run.Lifted ? AbordageGeyserPelvisBase(run) : run.Centre;
            int fx = AbSpawn(PelagVfxId.AbordageGeyser, run.Centre, Quaternion.identity, 0f,
                PelagAbordageVfxRules.Seconds(run.FallTick - run.StartTick) + AbordageGeyserAfterFall + .3f, PelagForm.AbordageGeyser, out GameObject go);
            if (fx >= 0)
            {
                go.transform.localScale = Vector3.one;
                run.Fx = fx;
                run.Object = go;
                run.ColumnFilter = go.transform.Find("Column")?.GetComponent<MeshFilter>();
                run.RingFilter = go.transform.Find("Ring")?.GetComponent<MeshFilter>();
                run.SkirtFilter = go.transform.Find("BaseRing")?.GetComponent<MeshFilter>();
                run.Crown = go.transform.Find("Crown")?.GetComponent<ParticleSystem>();
                run.CrownBack = go.transform.Find("CrownBack")?.GetComponent<ParticleSystem>();
                run.Spray = go.transform.Find("Spray")?.GetComponent<ParticleSystem>();
                run.Column.Begin();
                run.Ring.Begin();
                run.Skirt.Begin(PelagAbordageRingWater.Style.Rosette, PelagAbordageVfxRules.GeyserRosettePetals);
                PlaceAbordageGeyserLayers(run);
            }
            if (CaptureRig.NoVfx) return;
            // Апперкот: всплеск вверх по телу, толчок камеры; у основания — розетка (круг 3: не корона
            // пака с белыми шипами, а клубы пены по кругу и капли морской зелени наружу, кадр F).
            AbordageBodySplash(foot + Vector3.up * .8f, Vector3.up + FlatDirection(PlayerPosition(), foot) * .2f, 1f, PelagForm.AbordageGeyser);
            if (run.Fx >= 0) AbordageGeyserRosetteBurst(run);
            _juice?.PunchCamera(.18f, .04f);
        }

        /// <summary>Удар из земли: кольцо клубов пены и веер капель морской зелени у основания (кадр F).</summary>
        private void AbordageGeyserRosetteBurst(AbordageGeyserRun run)
        {
            float rosette = PelagAbordageVfxRules.GeyserSkirt(1f, run.Body);
            // Круг 4: клубы кромки — вплотную друг к другу, чтобы слиться в одну пенную кромку (обвод — по объединению).
            for (int i = 0; i < 20; i++)
            {
                float angle = (i + Random.Range(-.25f, .25f)) * (Mathf.PI * 2f / 20f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = run.Base + radial * rosette * Random.Range(.55f, .80f);
                at.y = run.Ground.At(at.x, at.z) + .08f;
                EmitGeyserPuff(run, at, radial * Random.Range(.9f, 1.6f) + Vector3.up * Random.Range(.4f, 1f),
                    Random.Range(PelagAbordageVfxRules.GeyserRimPuffMin, PelagAbordageVfxRules.GeyserRimPuffMax), Random.Range(.40f, .55f));
            }
            for (int i = 0; i < 24 && run.Spray != null; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = run.Base + radial * rosette * Random.Range(.3f, .6f);
                at.y = run.Ground.At(at.x, at.z) + .1f;
                run.Spray.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = radial * Random.Range(1.4f, 2.8f) + Vector3.up * Random.Range(2f, 3.6f),
                    startColor = PelagAbordageFormLook.DropColor(PelagForm.AbordageGeyser, Random.Range(.6f, 1f)),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Вода упала (Sim): столб оседает, тело на земле, кольцо — всплеском наружу.</summary>
        private void FallAbordageGeyser(in AbPending p)
        {
            _abFallCentre = p.At;
            foreach (AbordageGeyserRun run in _abGeysers)
            {
                if (!run.Active || run.Target != p.Target || run.Fell) continue;
                _abFallCentre = run.Centre;
                DropAbordageGeyser(run, p.Tick);
                return;
            }
        }

        private void DropAbordageGeyser(AbordageGeyserRun run, int tick)
        {
            run.Fell = true;
            run.FallTick = tick;
            if (run.Lifted) _arena.SetPresentationOffset(run.Target, Vector3.zero);
            if (CaptureRig.NoVfx) return;
            if (run.Spray != null)
                for (int i = 0; i < 40; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 at = run.Centre + radial * run.Radius * Random.Range(.85f, 1.05f);
                    at.y = run.Ground.At(at.x, at.z) + .08f;
                    run.Spray.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = radial * Random.Range(.8f, 2f) + Vector3.up * Random.Range(1.6f, 3.4f),
                        startColor = PelagAbordageFormLook.DropColor(PelagForm.AbordageGeyser, Random.Range(.3f, 1f)),
                        applyShapeToPosition = false
                    }, 1);
                }
            // Круг 5: всплеск падения — слитая пена (горка и вал клубов парой «Crown»/«CrownBack»), а не веер пака
            // и корона рывка: те рисовали россыпь отдельных кремовых «яиц» с обводом у каждого (+0,87…1,00 с).
            if (!AbordageGeyserFallFoam(run))
            {
                AbSpawn(PelagVfxId.AbordageBurst, run.Centre + Vector3.up * .02f,
                    Quaternion.LookRotation(FlatDirection(PlayerPosition(), run.Centre), Vector3.up), .9f, 0f, PelagForm.AbordageGeyser, out _);
                AbordageCrown(run.Centre, FlatDirection(PlayerPosition(), run.Centre), 1.2f, PelagForm.AbordageGeyser);
            }
            _juice?.PunchCamera(.14f, .03f);
        }

        /// <summary>
        /// Круг 5: вода упала — горка клубов пены там, где осел столб, и вал клубов внахлёст, бегущий наружу
        /// (PelagAbordageVfxRules.GeyserFall*). Клубы — парой силуэт/заливка: обвод только по краю массы.
        /// false — слоя клубов нет (старый префаб), падение рисует прежний пак.
        /// </summary>
        private bool AbordageGeyserFallFoam(AbordageGeyserRun run)
        {
            if (run.Crown == null || !AbStill(run.Fx, run.Object)) return false;
            for (int i = 0; i < PelagAbordageVfxRules.GeyserFallMoundPuffs; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = run.Base + radial * (PelagAbordageVfxRules.GeyserFallMoundReach * Mathf.Sqrt(Random.value));
                at.y = run.Ground.At(at.x, at.z) + Random.Range(.10f, .45f);
                EmitGeyserPuff(run, at, radial * Random.Range(.3f, .9f) + Vector3.up * Random.Range(.4f, 1.1f),
                    Random.Range(PelagAbordageVfxRules.GeyserPuffMin, PelagAbordageVfxRules.GeyserPuffMax) * 1.1f, Random.Range(.42f, .55f));
            }
            int wave = PelagAbordageVfxRules.GeyserFallWavePuffs;
            for (int i = 0; i < wave; i++)
            {
                float angle = (i + Random.Range(-.3f, .3f)) * (Mathf.PI * 2f / wave);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = run.Base + radial * (PelagAbordageVfxRules.GeyserFallWaveStart * Random.Range(.9f, 1.1f));
                at.y = run.Ground.At(at.x, at.z) + .10f;
                float speed = Random.Range(PelagAbordageVfxRules.GeyserFallWaveSpeedMin, PelagAbordageVfxRules.GeyserFallWaveSpeedMax);
                EmitGeyserPuff(run, at, radial * speed + Vector3.up * Random.Range(.4f, .8f),
                    Random.Range(PelagAbordageVfxRules.GeyserPuffMin, PelagAbordageVfxRules.GeyserPuffMax), Random.Range(.40f, .50f));
            }
            return true;
        }

        /// <summary>
        /// Круг 5: задетый падением воды — слитая пена у ног той же парой клубов ближнего Гейзера, а не корона Вихря
        /// (белые лепестки-«яйца»). Подброшенная цель лежит в горке пены падения — своей короны у неё нет.
        /// Без живого Гейзера с клубами — прежняя корона.
        /// </summary>
        private void AbordageGeyserFallKnock(int target, Vector3 body)
        {
            AbordageGeyserRun near = null;
            float best = float.MaxValue;
            foreach (AbordageGeyserRun run in _abGeysers)
            {
                if (!run.Active || run.Crown == null || !AbStill(run.Fx, run.Object)) continue;
                if (run.Target == target) return;
                float dx = body.x - run.Centre.x, dz = body.z - run.Centre.z;
                if (dx * dx + dz * dz < best) { best = dx * dx + dz * dz; near = run; }
            }
            if (near == null)
            {
                AbordageKnock(target, body, FlatDirection(_abFallCentre, body), PelagForm.AbordageGeyser);
                return;
            }
            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            float ring = Mathf.Max(.25f, PelagAbordageVfxRules.VisibleBodyRadius(radius)) * .9f;
            Vector3 outward = FlatDirection(near.Centre, body);
            int count = PelagAbordageVfxRules.GeyserFallKnockPuffs;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + Random.Range(-.25f, .25f)) * (Mathf.PI * 2f / count);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 at = body + radial * ring * Random.Range(.85f, 1.1f);
                at.y = near.Ground.At(at.x, at.z) + .08f;
                EmitGeyserPuff(near, at, (radial + outward * .6f) * Random.Range(.4f, .8f) + Vector3.up * Random.Range(.3f, .7f),
                    Random.Range(PelagAbordageVfxRules.GeyserRimPuffMin, PelagAbordageVfxRules.GeyserRimPuffMax), Random.Range(.38f, .48f));
            }
        }

        /// <summary>Кадр столбов: подъём тела, меш столба и кольца, пена с шапки, страховка без события падения.</summary>
        private void UpdateAbordageGeysers(Simulation sim, float shown, float dt)
        {
            foreach (AbordageGeyserRun run in _abGeysers)
            {
                if (!run.Active) continue;
                // Падение не пришло (сброс боя) — вода падает сама через 3 тика после срока.
                if (!run.Fell && shown >= run.FallTick + 3) DropAbordageGeyser(run, run.FallTick);
                // Высота следует Sim: если цель перестала быть в воздухе раньше срока падения (сброс
                // Абордажа), тело опускается сейчас. В срок падения снимок впереди показа — не трогаем.
                if (run.Lifted && !run.Fell && sim.Tick - 1 < run.FallTick && shown > run.StartTick + 1 && !sim.AbordageLifted(run.Target))
                    DropAbordageGeyser(run, Mathf.FloorToInt(shown));
                float t = PelagAbordageVfxRules.GeyserT(shown, run.StartTick, run.FallTick);
                if (run.Lifted && !run.Fell)
                    _arena.SetPresentationOffset(run.Target, Vector3.up * (PelagAbordageVfxRules.GeyserApex * PelagAbordageVfxRules.GeyserLift01(t)));
                float after = run.Fell ? PelagAbordageVfxRules.Seconds(shown - run.FallTick) : 0f;
                if (run.Fell && after > AbordageGeyserAfterFall) { ForgetAbordageGeyser(run); continue; }
                if (run.Fx < 0) continue;
                if (!AbStill(run.Fx, run.Object)) { run.Fx = -1; run.Object = null; continue; }
                // Круг 4: подброшенная цель — столб под её телом до падения (Sim держит тело, вид догоняет
                // интерполяцию); тяжёлые и элиты стоят — столб там, где ударил (точка падения Sim).
                if (run.Lifted && !run.Fell) FollowAbordageGeyserBody(run);
                float height = PelagAbordageVfxRules.GeyserColumn(t, run.Lifted)
                               * (run.Fell ? PelagAbordageVfxRules.GeyserCollapse(shown, run.FallTick) : 1f);
                // Круг 3 (кадр F): ствол ≈0,9 героя, зелёный во всю ширину — бурление малое, пена краёв тонкая;
                // раструб и округлая шапка — профиль PelagAbordageVfxRules.GeyserProfile.
                float half = PelagAbordageVfxRules.GeyserHalfWidth(run.Body);
                float ageColumn = run.Fell ? after * 1.2f + .05f : 0f;
                run.Flow += dt * 3.2f;
                float time = PelagAbordageVfxRules.Seconds(shown - run.StartTick);
                Camera camera = Camera.main;
                Vector3 eye = camera != null ? camera.transform.position : run.Centre + new Vector3(0f, 12f, -8f);
                if (run.ColumnFilter != null)
                    run.Column.Build(FormWaterMesh.MeshFor(run.ColumnFilter, PelagAbordageColumnWater.MeshName), run.Base,
                        height, eye, half, ageColumn, run.Flow, PelagAbordageVfxRules.GeyserTrunkChurn, time,
                        PelagAbordageVfxRules.Smooth01(t / .03f));
                // Розетка у основания (кадр F): круглые лепестки воды вокруг ствола, с падением воды рвётся.
                float skirt = PelagAbordageVfxRules.GeyserSkirt(t, run.Body) * (run.Fell ? 1f + .25f * PelagAbordageVfxRules.Smooth01(after / .12f) : 1f);
                if (run.SkirtFilter != null)
                    run.Skirt.Build(FormWaterMesh.MeshFor(run.SkirtFilter, PelagAbordageRingWater.MeshName), run.Base,
                        .08f, skirt, run.Fell ? after * 1.3f + .06f : 0f, .20f, .3f * time, time,
                        .95f * PelagAbordageVfxRules.Smooth01(t / .03f), run.Ground,
                        PelagAbordageVfxRules.GeyserRosetteNotch * PelagAbordageVfxRules.Clamp01(skirt / 1.1f));
                // Кольцо падения (круг 3): тонкая тихая полоса морской зелени на краю зоны — где упадёт вода;
                // непрозрачна со 2-го кадра (без полупрозрачного «хаки»), дорастает до ширины за 0,15 с;
                // после падения раздаётся и рвётся.
                float k = run.Fell ? PelagAbordageVfxRules.Smooth01(after / .12f) : 0f;
                float crest = run.Radius + .02f + .21f * k;
                float inner = run.Radius - 2f * PelagAbordageVfxRules.GeyserRingHalf * PelagAbordageVfxRules.GeyserRingGrow(time) - .25f * k;
                if (run.RingFilter != null)
                    run.Ring.Build(FormWaterMesh.MeshFor(run.RingFilter, PelagAbordageRingWater.MeshName), run.Centre,
                        inner, crest, run.Fell ? after * 1.1f + .04f : 0f, run.Fell ? .10f : .03f, .2f * time, time,
                        (run.Fell ? .85f : .75f) * PelagAbordageVfxRules.GeyserRingAlpha(time), run.Ground);
                // Струи по стволу (кадр F, вертикальные блики): капли пака, вытянутые по скорости, бьют вверх.
                if (!run.Fell && dt > 0f && run.Spray != null && !CaptureRig.NoVfx && height > .4f)
                {
                    // Круг 4: реже и бирюзовее — по стволу вода струями, а не россыпь белых «яиц».
                    run.StreakCarry += dt * 40f;
                    int streaks = (int)run.StreakCarry;
                    run.StreakCarry -= streaks;
                    for (int i = 0; i < streaks; i++)
                    {
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        run.Spray.Emit(new ParticleSystem.EmitParams
                        {
                            position = run.Base + Vector3.up * (height * Random.Range(.05f, .6f)) + radial * half * Random.Range(.2f, .85f),
                            velocity = Vector3.up * Random.Range(6f, 9f) + radial * Random.Range(.1f, .4f),
                            startSize = Random.Range(.08f, .13f),
                            startLifetime = Random.Range(.18f, .28f),
                            startColor = PelagAbordageFormLook.DropColor(PelagForm.AbordageGeyser, Random.Range(.45f, 1f)),
                            applyShapeToPosition = false
                        }, 1);
                    }
                }
                // Клубы пены (круг 4: слитые массы, не «яйца»): доля GeyserCrownPuffShare — крона, плотное облако
                // вокруг тела от ног до пояса, цель сидит В пене (у тяжёлого — у верха столба); остальное — кромка
                // розетки у земли, вплотную, чтобы клубы слились в одну пенную кромку. По стволу клубов нет.
                if (!run.Fell && dt > 0f && run.Crown != null && !CaptureRig.NoVfx && t > .02f)
                {
                    run.CrownCarry += dt * PelagAbordageVfxRules.GeyserPuffRate;
                    int count = (int)run.CrownCarry;
                    run.CrownCarry -= count;
                    float feet = run.Lifted ? PelagAbordageVfxRules.GeyserApex * PelagAbordageVfxRules.GeyserLift01(t) : height * .82f;
                    for (int i = 0; i < count; i++)
                    {
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        // На быстром взлёте клубов кроны нет (за телом тянулась бы лестница клубов по стволу): шапку
                        // даёт пена кроны столба, клубы — с конца взлёта, когда тело уже почти на вершине.
                        bool crownReady = !run.Lifted || t > PelagAbordageVfxRules.GeyserCrownFrom;
                        if (crownReady && Random.value < PelagAbordageVfxRules.GeyserCrownPuffShare && height > .5f)
                        {
                            float up = Mathf.Max(.3f, Mathf.Min(height, feet + PelagAbordageVfxRules.GeyserCrownHeight(Random.value)));
                            Vector3 at = run.Base + Vector3.up * up + radial * PelagAbordageVfxRules.GeyserCrownReach(run.Body, Random.value);
                            EmitGeyserPuff(run, at, radial * Random.Range(.15f, .5f) + Vector3.up * Random.Range(.2f, .8f),
                                Random.Range(PelagAbordageVfxRules.GeyserPuffMin, PelagAbordageVfxRules.GeyserPuffMax), Random.Range(.42f, .55f));
                        }
                        else
                        {
                            // Кромка розетки: низко над водой и почти без подскока — клубы сливаются в ровную пенную кромку.
                            Vector3 at = run.Base + radial * skirt * Random.Range(.80f, .98f);
                            at.y = run.Ground.At(at.x, at.z) + .04f;
                            EmitGeyserPuff(run, at, radial * Random.Range(.10f, .35f) + Vector3.up * Random.Range(.05f, .25f),
                                Random.Range(PelagAbordageVfxRules.GeyserRimPuffMin, PelagAbordageVfxRules.GeyserRimPuffMax), Random.Range(.40f, .52f));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Клуб пены парой (круг 4): заливка в «Crown» и тот же клуб силуэтом в «CrownBack» — место, скорость,
        /// размер, поворот и срок одни, слои ведут его одинаково; силуэты (очередь раньше) ложатся до всех
        /// заливок, и тёмный обвод остаётся только по краю слитой массы (шейдер Razlom/Abordage Foam Puff).
        /// </summary>
        private void EmitGeyserPuff(AbordageGeyserRun run, Vector3 at, Vector3 velocity, float size, float life)
        {
            if (run.Crown == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity,
                startSize3D = new Vector3(size, size * AbordageGeyserPuffAspect, size),
                // Без поворота: шейдер клуба кладёт тень на низ маски — это низ экрана (свет сверху).
                rotation = 0f,
                startLifetime = life,
                startColor = PelagAbordageFormLook.DropColor(PelagForm.AbordageGeyser, Random.Range(0f, .3f)),
                applyShapeToPosition = false
            };
            run.Crown.Emit(emit, 1);
            if (run.CrownBack != null) run.CrownBack.Emit(emit, 1);
        }

        /// <summary>
        /// Круг 4 (ревью: на части кастов тело висело рядом со столбом или за ним): основание столба, розетки и
        /// кольца — под телом подброшенной цели. Место — позиция тела без отдачи удара; Sim держит тело в
        /// воздухе, так что это поправка интерполяции вида, а не погоня.
        /// </summary>
        private void FollowAbordageGeyserBody(AbordageGeyserRun run)
        {
            Vector3 body = BaseEntityPosition(run.Target, run.Centre);
            float dx = body.x - run.Centre.x, dz = body.z - run.Centre.z;
            if (dx * dx + dz * dz >= 1e-6f && dx * dx + dz * dz <= 4f)
            {
                run.Centre = new Vector3(body.x, run.Ground.At(body.x, body.z), body.z);
                if (run.Object != null) run.Object.transform.position = run.Centre;
            }
            // Круг 5: столб, розетка и пена — под тазом поднятой модели каждый кадр (кольцо — у позиции тела).
            run.Base = AbordageGeyserPelvisBase(run);
            PlaceAbordageGeyserLayers(run);
        }

        /// <summary>
        /// Круг 5 (каст 0, лёгкая цель с 5 м: поднятая модель висела на ~0,4 м ближе к герою, чем столб): точка на
        /// земле под тазом поднятой модели. Сдвиг таза берётся от корня её вида (отдача удара сидит в корне — не в
        /// сдвиге) и кладётся на позицию тела; без кости таза — середина кожи модели. Негодный сдвиг
        /// (PelagAbordageVfxRules.GeyserPelvisUsable) и пропавший вид — позиция тела.
        /// </summary>
        private Vector3 AbordageGeyserPelvisBase(AbordageGeyserRun run)
        {
            Vector3 root = run.Centre;
            if (_arena == null || !_arena.TryGetEntityView(run.Target, out Transform view) || view == null) return root;
            if (view != run.PelvisView)
            {
                run.PelvisView = view;
                run.Pelvis = FindAbordagePelvis(view);
                run.Skins = run.Pelvis == null ? view.GetComponentsInChildren<SkinnedMeshRenderer>(false) : null;
            }
            Vector3 point;
            if (run.Pelvis != null && run.Pelvis.gameObject.activeInHierarchy) point = run.Pelvis.position;
            else if (!TryAbordageSkinCentre(run.Skins, out point)) return root;
            float dx = point.x - view.position.x, dz = point.z - view.position.z;
            if (!PelagAbordageVfxRules.GeyserPelvisUsable(dx, dz)) return root;
            float x = root.x + dx, z = root.z + dz;
            return new Vector3(x, run.Ground.At(x, z), z);
        }

        /// <summary>Таз модели: кость Hips гуманоида, иначе кость с «hips»/«pelvis» в имени (поиск раз на вид).</summary>
        private static Transform FindAbordagePelvis(Transform view)
        {
            Animator animator = view.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman && animator.avatar != null)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }
            foreach (Transform bone in view.GetComponentsInChildren<Transform>(true))
            {
                string key = bone.name.ToLowerInvariant();
                if (key.EndsWith("hips") || key.Contains("pelvis")) return bone;
            }
            return null;
        }

        private static bool TryAbordageSkinCentre(SkinnedMeshRenderer[] skins, out Vector3 centre)
        {
            centre = default;
            if (skins == null) return false;
            bool any = false;
            Bounds bounds = default;
            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                if (any) bounds.Encapsulate(skin.bounds);
                else { bounds = skin.bounds; any = true; }
            }
            if (any) centre = bounds.center;
            return any;
        }

        /// <summary>
        /// Круг 5: столб и розетка пишут меш от своего основания (Base), кольцо падения — от позиции тела (корень,
        /// Centre): слои столба и розетки сдвинуты от корня на Base − Centre (корень без поворота и масштаба).
        /// </summary>
        private static void PlaceAbordageGeyserLayers(AbordageGeyserRun run)
        {
            Vector3 shift = run.Base - run.Centre;
            if (run.ColumnFilter != null) run.ColumnFilter.transform.localPosition = shift;
            if (run.SkirtFilter != null) run.SkirtFilter.transform.localPosition = shift;
            if (run.RingFilter != null) run.RingFilter.transform.localPosition = Vector3.zero;
        }

        /// <summary>Столб отпущен: тело — на землю (смещение показа снято), объект — в пул.</summary>
        private void ForgetAbordageGeyser(AbordageGeyserRun run)
        {
            if (run.Active && run.Lifted && !run.Fell && _arena != null) _arena.SetPresentationOffset(run.Target, Vector3.zero);
            if (run.Active && AbStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
        }

        private void ForgetAbordageGeysers()
        {
            foreach (AbordageGeyserRun run in _abGeysers) ForgetAbordageGeyser(run);
        }
    }
}
