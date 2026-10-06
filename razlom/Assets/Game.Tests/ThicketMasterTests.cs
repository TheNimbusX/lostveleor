using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Хозяин Чащи, этап 1 «Ядро» (спецификация 01.10): сон и пробуждение,
    /// рёв на вступлении и на 66/50/33 после текущего действия, лапа
    /// 24/1/24 (двойная в фазе 3), топот 33/2/36 по правилу «60 из 90 тиков
    /// ближе 3,5 м», доли урона от лапы, отброс, одна волна подмоги на 66,
    /// иммунитет к оглушению и волоку, сдвиг Песочными Часами, детерминизм.
    /// Этап 2 «Атаки»: нырок 12/30/36/36 (бугор бьётся, круг под героем),
    /// прорастание 6 × r1,5 раз в 9 тиков, облака пыльцы (замедление 30%, 5 раз
    /// в 15 тиков, не больше 40), ливень 3 × 4 со щелями ≥ 2 м, фазы кастов.
    /// Этап 3 «Буря»: буря цветения (75/60, три круга света r2 на полу, урон
    /// только вне кругов, весь бюджет меток, раз в 20 с, Часы), связки фазы 3
    /// (Лапа→Лапа→Топот, Нырок→Топот, отдых 45), темп на стенде по фазам.
    /// Правки ревью: лапа больше 60 урона — замах 30; стойка после удара в
    /// фазах 2–3 — 75% и 50%; сроки нырка и бури — со множителями; нырок
    /// достаёт героя за поводком и вылезает на полу внутри своего круга.
    /// Тесты ядра, которым касты и буря мешают, зовут MeleeOnly.
    /// Темп 02.10 (artifacts/tools/wf/boss-tempo-spec.md): серия лапы 15 + 9 × n,
    /// окно 30; топот 24 + кольцо через 15, окно 30; нырок 12/30/24/24, связка
    /// фаз 2–3; наслоение кастов (жест 18, дальше только серии лапы); отдых
    /// 18/12/6; рёв 36; буря 60/45; под землёй неуязвим и не цель; рост ×1,15.
    /// «Мягче серии и топот» (владелец 02.10): второй и третий удары серии — 60%
    /// лапы (16 → 10, с базой 9 — 15 → 9); топот — только после 60 тиков ПОДРЯД ближе 4 м.
    /// Баланс 02.10 (artifacts/tools/boss-tune): ход 2,0 м/с, стойка после нырка 36,
    /// перезарядка топота 150, база лапы 25 → 10 (весь урон ×0,4), здоровье 6000 → 6600;
    /// после ревью вечера и арен из сегментов — база лапы 9 (эксперт 85,5% → 90,0%, artifacts/tools/boss-review-bal).
    /// Ревью 02.10, вечер: поворот 2,5° за тик, топот «за спиной» (20 тиков сзади ближе 5,2 м,
    /// перезарядка 90), лапа 17 + 10 с сектором на земле от бьющего плеча (100°, 3,58 м,
    /// доворот ≤ 25°), буря 90 / 75; поляна босса +10% и угол босса (ThicketMasterIntroTests).
    /// Ревью 02.10, ночь («лапа — основа, темп, дальники»): полосы от кромки корпуса (ближняя до 1,4 м,
    /// дальняя от 6,5), первый удар серии доворачивает к герою до 42,5°, топот «прижался» (45 тиков в
    /// круге 5,2 без действия, своя перезарядка 180, любые два — не ближе 90), замах топота 31 и ответ
    /// ногами, кольцо щадит центр внутри 5,2, нырок — только по дальней полосе (2 с, 360 / 300, бугор 18),
    /// отдых 42 / 26 / 20, ход 2,8 м/с по дальнему и кайтящему, касты издали, ливень фазы 3 по сроку
    /// раньше бури, круг 1 бури не под HUD.
    ///
    /// Стенд — SetupKindTestArena на арене 9: герой в (0, 0), босс в 6 м по +X
    /// лицом к герою; лапа там 10 × 164% = 16, топот — доля 62/41 = 24.
    /// </summary>
    public sealed class ThicketMasterTests
    {
        private const int Boss = 1;
        private const int WakeAt = Simulation.ThicketMinSleepTicks;                 // 90
        private const int RoarAt = WakeAt + Simulation.ThicketWakeTicks;           // 120
        private const int IntroDone = RoarAt + Simulation.ThicketRoarWindupTicks
            + Simulation.ThicketRoarRecoveryTicks;                                  // 180

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>Герой толстый и без брони, босс на месте (без walks) — тайминги не зависят от подхода.</summary>
        private static Simulation Arena(double distance = 6, bool walks = false, int arena = 9)
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: arena, distance: Fix64.FromDouble(distance));
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            if (!walks)
            {
                e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                e.RefreshStats(Boss);
            }
            return sim;
        }

        private static void Until(Simulation sim, int tick)
        {
            while (sim.Tick < tick) sim.Step(InputFrame.Empty);
        }

        /// <summary>Герой в distance метрах прямо перед боссом (по линии к началу координат).</summary>
        private static void HeroInFront(Simulation sim, double distance)
        {
            var boss = sim.Entities.Position[Boss];
            sim.Entities.Position[0] = boss + new FixVec2(-Fix64.FromDouble(distance), Fix64.Zero);
        }

        private static int Count(Simulation sim, SimEventType type, EnemyActionKind kind, bool? flag = null)
        {
            int n = 0;
            foreach (var e in sim.Events)
                if (e.Type == type && e.ActionVariant == (int)kind && (flag == null || e.Flag == flag.Value)) n++;
            return n;
        }

        /// <summary>Шагает, пока не начнётся действие kind; тик начала. −1 — не началось за limit тиков.</summary>
        private static int RunUntilStarted(Simulation sim, EnemyActionKind kind, int limit)
        {
            for (int k = 0; k < limit; k++)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                // Только начало действия (Stage 0): знаки следующих ударов серии и кольца — Amount ≥ 1.
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)kind && e.Amount == 0) return tick;
            }
            return -1;
        }

        private static bool MarkOf(Simulation sim, int serial, out EnemyTelegraph found)
        {
            int slot = sim.FindTelegraph(serial);
            return sim.TryGetTelegraph(slot, out found);
        }

        private static readonly ThicketMasterAction[] Specials =
        {
            ThicketMasterAction.Dive, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen, ThicketMasterAction.Rain,
            ThicketMasterAction.Storm, ThicketMasterAction.Seeds,
        };

        /// <summary>
        /// Из нырка, кастов этапа 2 и бури готовы только allowed; прочие — на перезарядке до конца теста
        /// (нырок — и сближение, и «под героя», 03.10).
        /// </summary>
        private static void Only(Simulation sim, params ThicketMasterAction[] allowed)
        {
            foreach (var action in Specials)
                if (System.Array.IndexOf(allowed, action) < 0) sim.SetThicketReadyTick(Boss, action, int.MaxValue / 2);
            if (System.Array.IndexOf(allowed, ThicketMasterAction.Dive) < 0) sim.SetThicketDiveDueTick(Boss, int.MaxValue / 2);
        }

        /// <summary>Топот в жребии рядом с лапой (03.10) закрыт: топот — только по правилам «прижался» и «за спиной».</summary>
        private static void NoStompPick(Simulation sim) => sim.SetThicketStompPickReadyTick(Boss, int.MaxValue / 2);

        /// <summary>Только лапа и топот ядра.</summary>
        private static void MeleeOnly(Simulation sim) => Only(sim);

        /// <summary>После вступления здоровье на percent и рёв порогов доигран: фаза 2 (60) или 3 (30).</summary>
        private static void ToPhase(Simulation sim, int percent)
        {
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * percent / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10), "рёв порога");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Until(sim, roar.EndTick);
        }

        /// <summary>Шагает до тика until и записывает контакты kind: {тик, Amount, Flag}.</summary>
        private static void StepRecording(Simulation sim, int until, EnemyActionKind kind, List<int[]> impacts)
        {
            while (sim.Tick < until)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)kind)
                        impacts.Add(new[] { tick, e.Amount, e.Flag ? 1 : 0 });
            }
        }

        /// <summary>Шагает до тика until и считает события type/kind (flag — фильтр по Flag).</summary>
        private static int StepCounting(Simulation sim, int until, SimEventType type, EnemyActionKind kind, bool? flag = null)
        {
            int n = 0;
            while (sim.Tick < until)
            {
                sim.Step(InputFrame.Empty);
                n += Count(sim, type, kind, flag);
            }
            return n;
        }

        // ---------- вступление ----------

        [Test]
        public void Intro_SleepsRootedNinetyTicks_ThenWakesNearTheHero_AndRoars()
        {
            var sim = Arena(distance: 6, walks: true);
            var home = sim.Entities.Position[Boss];
            Until(sim, WakeAt);
            Assert.IsFalse(sim.ThicketMasterAwake(Boss), "проснулся раньше 90 тиков");
            Assert.AreEqual(home, sim.Entities.Position[Boss], "спящий не ходит");
            Assert.AreEqual(0, sim.ThicketMasterPhase(Boss));

            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketWake));
            Assert.IsTrue(sim.ThicketMasterAwake(Boss));
            Assert.AreEqual(1, sim.ThicketMasterPhase(Boss));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var wake));
            Assert.AreEqual(ThicketMasterAction.Wake, wake.Action);
            Assert.AreEqual(RoarAt, wake.EndTick);
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss), "пробуждение жетона не берёт");

            Until(sim, RoarAt);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketRoar), "рёв сразу за пробуждением");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Assert.AreEqual(ThicketMasterAction.Roar, roar.Action);
            Assert.AreEqual(RoarAt + Simulation.ThicketRoarWindupTicks, roar.ImpactTick);
            Assert.AreEqual(Simulation.ThicketRoarIntroBit, roar.Tag);
            Assert.IsTrue(MarkOf(sim, roar.TelegraphSerial, out var ring), "кольцо рёва на земле");
            Assert.AreEqual(TelegraphShape.Ring, ring.Shape);
            Assert.IsTrue(ring.SharedView);
            Assert.AreEqual(Simulation.ThicketRoarInnerRadius, ring.InnerRadius);
            Assert.AreEqual(Simulation.ThicketRoarOuterRadius, ring.Radius);
            Assert.AreEqual(roar.ImpactTick, ring.ImpactTick);
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss));
        }

        [Test]
        public void Intro_FarHero_KeepsSleeping_UntilHeComesWithinNineMetres()
        {
            var sim = Arena(distance: 12, walks: true);
            Until(sim, 200);
            Assert.IsFalse(sim.ThicketMasterAwake(Boss), "герой в 12 м — спит");
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _));
            HeroInFront(sim, 8.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.ThicketMasterAwake(Boss));
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketWake));
        }

        [Test]
        public void Intro_BossSleepingBackToTheHero_TurnsToHimDuringWakeAndRoar()
        {
            var sim = Arena(walks: true);
            var home = sim.Entities.Position[Boss];
            var toHero = (sim.Entities.Position[0] - home).Normalized();
            sim.Entities.Facing[Boss] = -toHero;
            Until(sim, WakeAt + 1);
            Assert.AreEqual(-toHero, sim.Entities.Facing[Boss], "спящий не поворачивается");
            Until(sim, IntroDone);
            double dot = FixVec2.Dot(sim.Entities.Facing[Boss].Normalized(), toHero).ToDouble();
            Assert.That(dot, Is.GreaterThan(0.999), "к концу рёва — лицом к герою");
            Assert.AreEqual(home, sim.Entities.Position[Boss], "в пробуждении и рёве стоит");
        }

        [Test]
        public void Roar_DoesNoDamage_ButKnocksTheHeroInTheRingTwoMetresOut()
        {
            var sim = Arena(distance: 4);
            int health = sim.Entities.Health[0];
            Until(sim, RoarAt + Simulation.ThicketRoarWindupTicks);
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketRoar));
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketRoar, true), "кольцо задело героя");
            Assert.AreEqual(health, sim.Entities.Health[0], "рёв без урона");
            Assert.IsTrue(ForcedMotion.IsActive(sim.Entities, 0), "отброс");
            for (int k = 0; k < Simulation.ThicketKnockbackTicks; k++) sim.Step(InputFrame.Empty);
            double distance = FixVec2.Distance(sim.Entities.Position[0], sim.Entities.Position[Boss]).ToDouble();
            Assert.That(distance, Is.EqualTo(6.0).Within(0.05), "2 м прочь от босса");
        }

        [Test]
        public void Roar_HeroOutsideTheRing_IsNotTouched()
        {
            var sim = Arena(distance: 7.5);
            Until(sim, RoarAt + Simulation.ThicketRoarWindupTicks + 1);
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, 0));
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketRoar, true));
        }

        // ---------- лапа ----------

        [Test]
        public void Paw_PhaseOneSeriesOfTwo_Windup17_NextTenToTwelveLater_Window30_FirstForTheLeafDamage_SecondThirtyFivePercent()
        {
            var sim = Arena();
            // Терновник (08.10) бывает и в ближней полосе (вес 2) — здесь только серия лапы.
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Seeds, int.MaxValue / 2);
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            int health = sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw), "лапа сразу после вступления");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(ThicketMasterAction.Paw, paw.Action);
            Assert.AreEqual(IntroDone, paw.StartTick);
            // Ревью 02.10, вечер: «замедлить тычку лапой на 10%» — 15 → 17. Промежуток серии — бросок
            // 10–12 в фазе 1 (Tag, ThicketPawGapOf; баланс 02.10, ночь — 9–12, проверка находок 03.10 — не короче 10).
            Assert.AreEqual(IntroDone + 17, paw.ImpactTick, "первый замах 17");
            int gap = Simulation.ThicketPawGapOf(paw, 0);
            Assert.That(gap, Is.InRange(Simulation.ThicketPawGapPhase1Min, Simulation.ThicketPawGapPhase1Max), "промежуток фазы 1 — 10–12");
            Assert.That(gap, Is.GreaterThanOrEqualTo(10), "не быстрее тычка, принятого владельцем (10)");
            Assert.AreEqual(IntroDone + 17 + gap, paw.LastImpactTick, "второй удар — через свой промежуток");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick, "кончается кадром контакта");
            Assert.AreEqual(2, paw.Stages, "фаза 1 — серия из двух");
            Assert.AreEqual(0, paw.Stage);
            Assert.IsTrue(MarkOf(sim, paw.TelegraphSerial, out var sector));
            Assert.AreEqual(TelegraphShape.Sector, sector.Shape);
            // Ревью 02.10, вечер: «обозначить, чтоб она более видимая и читаемая» — сектор на земле
            // (общий вид) от знака до удара; вершина — правое плечо, раствор 100°, 3,58 м.
            Assert.IsTrue(sector.SharedView, "сектор лапы — на земле");
            Assert.AreEqual(IntroDone, sector.StartTick, "встаёт со знаком");
            Assert.AreEqual(paw.ImpactTick, sector.ImpactTick, "до удара");
            Assert.AreEqual(Simulation.ThicketPawReach, sector.Radius);
            Assert.AreEqual(Simulation.ThicketPawArcCos, sector.ArcCos);
            Assert.AreEqual(Simulation.ThicketPawShoulder(paw.Origin, paw.Direction, 0), sector.Origin, "от плеча первой (правой) лапы");
            var faceRight = new FixVec2(paw.Direction.Y, -paw.Direction.X);
            Assert.That(FixVec2.Dot(sector.Origin - paw.Origin, faceRight).ToDouble(), Is.EqualTo(0.5).Within(1e-3), "правое плечо");
            Assert.That(FixVec2.Dot(sector.Origin - paw.Origin, paw.Direction).ToDouble(), Is.EqualTo(0.6).Within(1e-3));
            Assert.That(FixVec2.Distance(paw.Origin, paw.Target).ToDouble(), Is.GreaterThan(2.0), "середина сектора впереди");
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss), "в замахе босс держит крупный жетон");

            while (sim.Tick < paw.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(health, sim.Entities.Health[0], "урон до контакта, тик " + (sim.Tick - 1));
            }
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw, true));
            Assert.AreEqual(23, sim.ThicketPawDamageOf(Boss), "лапа на арене 9: 14 × 164% (перебаланс 03.10; было 12 — 20)");
            Assert.AreEqual(23, health - sim.Entities.Health[0]);
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, 0), "лапа не отбрасывает");
            // Знак второго удара — в тик первого, за промежуток до своего удара.
            int started = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketPaw)
                { started++; Assert.AreEqual(1, e.Amount, "номер удара"); }
            Assert.AreEqual(1, started, "знак второго удара встал в тик первого");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var second));
            Assert.AreEqual(1, second.Stage);
            Assert.AreEqual(paw.ImpactTick, second.StageStartTick);
            Assert.AreEqual(paw.LastImpactTick, second.ImpactTick);
            Assert.IsFalse(second.HitResolved);
            Assert.IsTrue(MarkOf(sim, second.TelegraphSerial, out var left), "сектор второго удара");
            Assert.IsTrue(left.SharedView);
            Assert.AreEqual(paw.ImpactTick, left.StartTick, "встаёт в тик первого удара");
            Assert.AreEqual(second.ImpactTick, left.ImpactTick);
            Assert.AreEqual(Simulation.ThicketPawShoulder(second.Origin, second.Direction, 1), left.Origin, "от левого плеча");
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss), "серия держит жетон до последнего удара");

            Until(sim, paw.LastImpactTick + 1);
            // «Мягче серии» (02.10): второй удар — 35% лапы (проверка находок 03.10; было 45%, до того 60%), 20 → 7.
            Assert.AreEqual(23 + 8, health - sim.Entities.Health[0], "первый 23, второй 8");
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss));

            Until(sim, paw.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(paw.EndTick + Simulation.ThicketRestPhase1Ticks, memory.NextActionTick, "отдых фазы 1 — 30");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketWindowTicks, memory.QuietUntil, "окно ответа 30");
            // Следующая серия: замах — после отдыха, удар — не раньше конца окна.
            HeroInFront(sim, 2.5);
            int next = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreEqual(paw.EndTick + Simulation.ThicketRestPhase1Ticks, next, "серия раз в 17 + промежуток + 1 + 30 тиков (~2 с)");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var again));
            Assert.That(again.ImpactTick - paw.LastImpactTick, Is.GreaterThanOrEqualTo(Simulation.ThicketWindowTicks), "окно ответа");
        }

        [Test]
        public void Paw_SeriesLengthByPhase_TwoThenTwoOrThreeThenThree_SignsAGapAhead_GapsByPhase_ReaimWithin35()
        {
            foreach (int percent in new[] { 100, 60, 30 })
            {
                var sim = Arena();
                MeleeOnly(sim);
                if (percent < 100) ToPhase(sim, percent); else Until(sim, IntroDone);
                var seen = new HashSet<int>();
                var signs = new Dictionary<int, int>();
                var gaps = new HashSet<int>();
                int gapMin = percent == 100 ? Simulation.ThicketPawGapPhase1Min : percent == 60 ? Simulation.ThicketPawGapPhase2Min : Simulation.ThicketPawGapPhase3Min;
                int gapMax = percent == 100 ? Simulation.ThicketPawGapPhase1Max : percent == 60 ? Simulation.ThicketPawGapPhase2Max : Simulation.ThicketPawGapPhase3Max;
                FixVec2 lastDirection = default;
                int lastStage = -1;
                for (int k = 0; k < 1500; k++)
                {
                    // Герой ходит сбоку от морды: каждый удар доворачивает заново.
                    var f = sim.Entities.Facing[Boss].Normalized();
                    var side = new FixVec2(-f.Y, f.X);
                    if (k % 20 == 0)
                        sim.Entities.Position[0] = sim.Entities.Position[Boss] + f * Fix64.Ratio(5, 2)
                            + side * (k / 20 % 2 == 0 ? Fix64.One : -Fix64.One);
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                    // Топот не мешает: окно «рядом» сбрасывается.
                    sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
                    int tick = sim.Tick;
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                    {
                        if (e.Source != Boss || e.ActionVariant != (int)EnemyActionKind.ThicketPaw) continue;
                        if (e.Type == SimEventType.EnemyActionStarted) signs[e.Amount] = tick;
                        if (e.Type == SimEventType.EnemyActionImpact && e.Amount > 0)
                        {
                            // Знак следующего удара — за его промежуток фазы (не ровные 10; не короче 10).
                            int lead = tick - signs[e.Amount];
                            Assert.That(lead, Is.InRange(gapMin, gapMax), "знак удара — за промежуток фазы");
                            gaps.Add(lead);
                        }
                    }
                    if (sim.TryGetThicketMasterAction(Boss, out var a) && a.Action == ThicketMasterAction.Paw)
                    {
                        if (a.Stage == 0 && lastStage != 0) seen.Add(a.Stages);
                        if (a.Stage > 0 && a.Stage != lastStage)
                        {
                            double dot = FixVec2.Dot(lastDirection, a.Direction).ToDouble();
                            Assert.That(dot, Is.GreaterThanOrEqualTo(System.Math.Cos(35 * System.Math.PI / 180) - 1e-3),
                                "доворот удара серии — не больше 35° (3,5°/тик за промежуток)");
                        }
                        lastDirection = a.Direction;
                        lastStage = a.Stage;
                    }
                    else lastStage = -1;
                }
                string phase = "здоровье " + percent + "%";
                Assert.That(gaps.Count, Is.GreaterThan(1), phase + ": промежутки разные — читать, а не считать");
                if (percent == 100) { Assert.IsTrue(seen.Contains(2), phase); Assert.AreEqual(1, seen.Count, phase); }
                if (percent == 60) { Assert.IsTrue(seen.Contains(2) && seen.Contains(3), "фаза 2 — и 2, и 3"); Assert.AreEqual(2, seen.Count); }
                if (percent == 30) { Assert.IsTrue(seen.Contains(3), phase); Assert.AreEqual(1, seen.Count, phase); }
            }
        }

        [Test]
        public void Paw_BossDiesInTheWindup_ActionCancelled_MarkFades()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Until(sim, paw.StartTick + 10);
            int health = sim.Entities.Health[0];
            sim.Entities.Health[Boss] = 0;
            sim.Entities.Alive[Boss] = false;
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionCancelled, EnemyActionKind.ThicketPaw));
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _));
            Assert.IsTrue(MarkOf(sim, paw.TelegraphSerial, out var mark));
            Assert.AreEqual(TelegraphState.Cancelled, mark.State);
            Until(sim, paw.ImpactTick + 2);
            Assert.AreEqual(health, sim.Entities.Health[0], "снятая лапа не бьёт");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw));
        }

        [Test]
        public void Paw_HeroWhoLeftTheSector_TakesNothing()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            sim.Entities.Position[0] = sim.Entities.Position[Boss] + At(4.5, 0);
            int health = sim.Entities.Health[0];
            Until(sim, paw.LastImpactTick + 1);
            Assert.AreEqual(health, sim.Entities.Health[0]);
        }

        /// <summary>Зазор от точки до корпуса босса в его осях (взгляд +X): минус — внутри.</summary>
        private static double HullGapLocal(double forward, double side)
        {
            double gap = double.MaxValue;
            for (int k = 0; k < Simulation.ThicketHullCircleCount; k++)
            {
                Simulation.ThicketHullLocal(k, out Fix64 f, out Fix64 s, out Fix64 r);
                double d = System.Math.Sqrt((forward - f.ToDouble()) * (forward - f.ToDouble())
                    + (side - s.ToDouble()) * (side - s.ToDouble())) - r.ToDouble();
                if (d < gap) gap = d;
            }
            return gap;
        }

        /// <summary>Ближе всего к центру, где герой стоит у корпуса под углом degrees от взгляда (+ — влево).</summary>
        private static double TouchDistance(double degrees)
        {
            double a = degrees * System.Math.PI / 180, hero = EntityStore.DefaultBodyRadius.ToDouble();
            for (double d = 0.5; d < 6; d += 0.01)
                if (HullGapLocal(d * System.Math.Cos(a), d * System.Math.Sin(a)) >= hero) return d;
            return 6;
        }

        private static FixVec2 Polar(double degrees, double distance)
            => At(distance * System.Math.Cos(degrees * System.Math.PI / 180), distance * System.Math.Sin(degrees * System.Math.PI / 180));

        /// <summary>
        /// Ревью 02.10, вечер: «иногда попадает по герою, хотя вроде стоишь сбоку». Сектор удара —
        /// от бьющего плеча, 100°, 3,58 м (прямо вперёд — 4,14 от центра, как было): вся зона начала
        /// серии (±40°, до 3,68 м и вплотную к корпусу до 4,14) — под обеими лапами; герой у корпуса
        /// сбоку — дальше 60° на стороне бьющей лапы и дальше 45° на другой (и до 2 м от корпуса) —
        /// вне взмаха. Прежний сектор (от центра, 120°) задевал его у плеча.
        /// </summary>
        [Test]
        public void PawSector_FromTheStrikingShoulder_CoversTheStartZone_SparesTheFlanks()
        {
            var body = At(0, 0);
            var look = At(1, 0);
            Fix64 hero = EntityStore.DefaultBodyRadius;
            Assert.AreEqual(4.14, (Simulation.ThicketPawShoulderForward + Fix64.Sqrt(Simulation.ThicketPawReach * Simulation.ThicketPawReach
                - Simulation.ThicketPawShoulderSide * Simulation.ThicketPawShoulderSide)).ToDouble(), 0.01, "прямо вперёд — 4,14 от центра");
            Assert.AreEqual(System.Math.Cos(50 * System.Math.PI / 180), Simulation.ThicketPawArcCos.ToDouble(), 1e-3, "раствор 100°");
            var oldSector = EnemyTelegraph.Sector(body, look, Simulation.ThicketPawRadius, Fix64.Ratio(1, 2));
            int oldFlankHits = 0;
            for (int stage = 0; stage < 2; stage++)
            {
                var sector = Simulation.ThicketPawStrikeSector(body, look, stage);
                int right = Simulation.ThicketPawIsRight(stage) ? -1 : 1;
                Assert.That(sector.Origin.Y.ToDouble() * right, Is.EqualTo(0.5).Within(1e-3), "плечо бьющей лапы, удар " + stage);
                for (int degrees = -40; degrees <= 40; degrees += 2)
                    for (double d = 0.5; d <= 4.14; d += 0.02)
                    {
                        double a = degrees * System.Math.PI / 180;
                        double gap = HullGapLocal(d * System.Math.Cos(a), d * System.Math.Sin(a)) - hero.ToDouble();
                        if (gap < 0 || (d > 3.68 && gap > 0.3)) continue;
                        Assert.IsTrue(Simulation.TelegraphContains(in sector, Polar(degrees, d), hero),
                            "зона начала серии: удар " + stage + ", " + degrees + "°, " + d.ToString("0.00") + " м");
                    }
                for (int degrees = -120; degrees <= 120; degrees++)
                {
                    bool striking = degrees * right > 0;
                    if (System.Math.Abs(degrees) < (striking ? 60 : 45)) continue;
                    double touch = TouchDistance(degrees);
                    foreach (double extra in new[] { 0.0, 0.25, 0.5, 1.0, 2.0 })
                    {
                        var p = Polar(degrees, touch + extra);
                        Assert.IsFalse(Simulation.TelegraphContains(in sector, p, hero),
                            "бок: удар " + stage + ", " + degrees + "°, у корпуса + " + extra + " м");
                        if (Simulation.TelegraphContains(in oldSector, p, hero)) oldFlankHits++;
                    }
                }
            }
            Assert.That(oldFlankHits, Is.GreaterThan(0), "прежний сектор задевал героя у плеча");
        }

        /// <summary>
        /// То же на живой серии: правая лапа (первый удар) бьёт героя перед правым плечом и
        /// не бьёт стоящего у корпуса сбоку под 70°; сектор на земле — ровно фигура попадания.
        /// </summary>
        [Test]
        public void Paw_HeroBesideTheStrikingShoulder_OutsideTheSwing_IsNotHit_InFrontIsHit()
        {
            foreach (bool inside in new[] { true, false })
            {
                var sim = Arena();
                MeleeOnly(sim);
                Until(sim, IntroDone);
                HeroInFront(sim, 2.5);
                sim.Step(InputFrame.Empty);
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
                Assert.AreEqual(ThicketMasterAction.Paw, paw.Action);
                var f = paw.Direction.Normalized();
                var leftOf = new FixVec2(-f.Y, f.X);
                // Справа от взгляда: −20° в 3,6 м — перед правой лапой; −70° у корпуса + 0,3 — у правого плеча.
                double degrees = inside ? -20 : -70;
                double distance = inside ? 3.6 : TouchDistance(-70) + 0.3;
                var local = Polar(degrees, distance);
                var spot = sim.Entities.Position[Boss] + f * local.X + leftOf * local.Y;
                Assert.That(sim.ThicketHullGap(Boss, spot).ToDouble(), Is.GreaterThan(EntityStore.DefaultBodyRadius.ToDouble()),
                    "герой снаружи корпуса");
                int health = sim.Entities.Health[0];
                bool flag = false;
                while (sim.Tick <= paw.ImpactTick)
                {
                    sim.Entities.Position[0] = spot;
                    int tick = sim.Tick;
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.ThicketPaw && e.Amount == 0)
                            flag = e.Flag;
                }
                Assert.IsTrue(MarkOf(sim, paw.TelegraphSerial, out var mark));
                Assert.AreEqual(inside, Simulation.TelegraphContains(in mark, spot, sim.Entities.BodyRadius[0]), "сектор на земле = попадание");
                Assert.AreEqual(inside, flag, inside ? "перед правой лапой — попал" : "у правого плеча сбоку — мимо");
                Assert.AreEqual(inside, sim.Entities.Health[0] < health);
            }
        }

        [Test]
        public void Paw_HardAndEnragedOverSixty_WindupThirty_DamageKept()
        {
            // «Сложно» 125% и ярость RiftRun ×1,3: лапа 20 → 25 → 32 на арене 9 (проверка находок 03.10:
            // база 12) — до порога не доходит. Правило дока (больше 60 урона — замах от 30 тиков;
            // удлиняется замах, не режется урон) проверяется на лапе, поднятой стендом до 66.
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, hardPercent: 125, distance: Fix64.FromInt(6));
            var e = sim.Entities;
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            e.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.Stats[Boss].SetBase(StatType.Damage, e.Damage[Boss] * Fix64.Ratio(13, 10));
            e.RefreshStats(Boss);
            MeleeOnly(sim);
            Assert.AreEqual(38, sim.ThicketPawDamageOf(Boss), "лапа «Сложно» в ярости на арене 9");
            Assert.AreEqual(Simulation.ThicketPawWindupTicks, sim.ThicketPawWindupOf(Boss), "до 60 не дошла — замах 17");
            e.Stats[Boss].SetBase(StatType.Damage, Fix64.FromInt(66));
            e.RefreshStats(Boss);
            int damage = sim.ThicketPawDamageOf(Boss);
            Assert.That(damage, Is.GreaterThan(Simulation.ThicketPawHeavyDamage), "тяжёлая лапа стенда");
            Assert.AreEqual(Simulation.ThicketPawHeavyWindupTicks, sim.ThicketPawWindupOf(Boss));

            // Фаза 3 — серия из трёх: первый замах 30 (тяжёлый — при любой фазе), дальше удары через
            // промежутки фазы 3 (10–11).
            ToPhase(sim, 30);
            HeroInFront(sim, 2.5);
            int health = e.Health[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(3, paw.Stages);
            Assert.AreEqual(start + 30, paw.ImpactTick, "первый замах — 30");
            int gaps = Simulation.ThicketPawGapOf(paw, 0) + Simulation.ThicketPawGapOf(paw, 1);
            Assert.That(Simulation.ThicketPawGapOf(paw, 0), Is.InRange(Simulation.ThicketPawGapPhase3Min, Simulation.ThicketPawGapPhase3Max));
            Assert.That(Simulation.ThicketPawGapOf(paw, 1), Is.InRange(Simulation.ThicketPawGapPhase3Min, Simulation.ThicketPawGapPhase3Max));
            Assert.AreEqual(paw.ImpactTick + gaps, paw.LastImpactTick, "дальше — через промежутки фазы 3");
            Until(sim, paw.ImpactTick);
            Assert.AreEqual(health, e.Health[0], "до контакта урона нет");
            Until(sim, paw.ImpactTick + 1);
            Assert.AreEqual(damage, health - e.Health[0], "первый удар — лапа целиком: урон не срезан");
            Until(sim, paw.LastImpactTick + 1);
            // «Мягче серии» (02.10): второй и третий — 35% лапы (проверка находок 03.10), и «Сложно» с яростью растят их тоже.
            int followUp = sim.ThicketPawStrikeDamageOf(Boss, 1);
            Assert.AreEqual((damage * 35 + 50) / 100, followUp, "доля 35% от тяжёлой лапы");
            Assert.AreEqual(followUp, sim.ThicketPawStrikeDamageOf(Boss, 2));
            Assert.AreEqual(damage + 2 * followUp, health - e.Health[0], "первый целиком, два следующих по 35%");

            // Обычный маршрут — лапа 23, замах 17.
            Assert.AreEqual(Simulation.ThicketPawWindupTicks, Arena().ThicketPawWindupOf(Boss));
        }

        // ---------- топот ----------

        [Test]
        public void Stomp_Hugged240InARow_Windup42_Circle52_DamageShare_KnockbackTwoMetres_RingSparesTheHurt()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            // 3,85 м: в круге топота (5,2), но дальше 3,68 — лапа не достаёт (босс на стенде не ходит).
            HeroInFront(sim, 3.85);
            int health = sim.Entities.Health[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 300);
            // Баланс 02.10, ночь: «прижался» — 240 тиков ПОДРЯД в круге 5,2, что бы босс ни делал; счёт —
            // с тика, в котором героя поставили (IntroDone), топот — в тик 240-го.
            Assert.AreEqual(IntroDone + Simulation.ThicketStompHugTicks - 1, start, "240-й тик прижатым");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            Assert.AreEqual(2, stomp.Stages, "круг и кольцо");
            Assert.AreEqual(start + 42, stomp.ImpactTick, "замах 42 (проверка находок 03.10: 24 → 31 → 42 — ответ ногами от корпуса)");
            Assert.AreEqual(start + 42 + 15, stomp.LastImpactTick, "кольцо — через 15 после круга");
            Assert.AreEqual(stomp.LastImpactTick + Simulation.ThicketStompStrikeTicks, stomp.EndTick, "кончается кадром контакта кольца");
            Assert.IsTrue(MarkOf(sim, stomp.TelegraphSerial, out var circle));
            Assert.AreEqual(TelegraphShape.Circle, circle.Shape);
            Assert.IsTrue(circle.SharedView);
            Assert.AreEqual(Simulation.ThicketStompRadius, circle.Radius);
            Assert.AreEqual(1, sim.BigMarkLoad(out _), "топот — крупная метка весом 1");
            Assert.AreEqual(0, sim.ThicketMasterNearTicks(Boss), "окно начинается заново");
            Assert.AreEqual(0, sim.ThicketMasterHugTicks(Boss), "счёт «прижался» — заново");
            Assert.AreEqual(start + Simulation.ThicketStompCooldownTicks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Stomp),
                "своя перезарядка «прижался» — 180");
            Assert.AreEqual(start + Simulation.ThicketStompSpacingTicks, sim.ThicketRearStompReadyTick(Boss), "любой следующий — через 90");

            Until(sim, stomp.ImpactTick);
            Assert.AreEqual(health, sim.Entities.Health[0]);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStomp, true));
            Assert.AreEqual(30, sim.ThicketStompDamageOf(Boss), "доля 53/41 от 23 на арене 9 (перебаланс 03.10: доля 62 → 53, урон прежний)");
            Assert.AreEqual(30, health - sim.Entities.Health[0]);
            Assert.IsTrue(ForcedMotion.IsActive(sim.Entities, 0));
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp), "кольцо встало в тик круга");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var ring));
            Assert.AreEqual(1, ring.Stage);
            Assert.AreEqual(stomp.LastImpactTick, ring.ImpactTick);
            Assert.IsTrue(MarkOf(sim, ring.TelegraphSerial, out var band));
            Assert.AreEqual(TelegraphShape.Ring, band.Shape);
            Assert.IsTrue(band.SharedView);
            Assert.AreEqual(Simulation.ThicketStompRadius, band.InnerRadius);
            Assert.AreEqual(Simulation.ThicketStompRingOuterRadius, band.Radius);
            Assert.AreEqual(1, sim.BigMarkLoad(out _), "кольцо — та же метка весом 1");
            for (int k = 0; k < Simulation.ThicketKnockbackTicks; k++) sim.Step(InputFrame.Empty);
            double distance = FixVec2.Distance(sim.Entities.Position[0], sim.Entities.Position[Boss]).ToDouble();
            Assert.That(distance, Is.EqualTo(5.85).Within(0.05), "отброшен на 2 м — в полосу кольца");
            health = sim.Entities.Health[0];
            Until(sim, stomp.LastImpactTick + 1);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStomp, false),
                "кого ранил круг, кольцо не бьёт — одно попадание на топот");
            Assert.AreEqual(health, sim.Entities.Health[0]);
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss));
            Assert.AreEqual(0, sim.BigMarkLoad(out _));
            Until(sim, stomp.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(stomp.EndTick + Simulation.ThicketRestPhase1Ticks, memory.NextActionTick, "отдых фазы 1");
            Assert.AreEqual(stomp.LastImpactTick + Simulation.ThicketWindowTicks, memory.QuietUntil, "окно ответа 30 после кольца");
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext, "фаза 1 — без связки «топот → лапа»");
        }

        [Test]
        public void Stomp_SecondRing_FifteenAfter_FivePointTwoToSevenPointFive_ThreeQuartersDamage_Knockback()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            HeroInFront(sim, 3.85);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 300);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            // Ушёл из круга, но встал в полосу кольца.
            HeroInFront(sim, 6.5);
            int health = sim.Entities.Health[0];
            Until(sim, stomp.ImpactTick + 1);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStomp, false), "круг мимо");
            Assert.AreEqual(health, sim.Entities.Health[0]);
            Until(sim, stomp.LastImpactTick);
            Assert.AreEqual(health, sim.Entities.Health[0], "до кольца урона нет");
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStomp, true));
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.ThicketStomp)
                    Assert.AreEqual(1, e.Amount, "удар кольца — Amount 1");
            Assert.AreEqual(22, sim.ThicketStompRingDamageOf(Boss), "30 × 0,75 на арене 9");
            Assert.AreEqual(22, health - sim.Entities.Health[0]);
            Assert.IsTrue(ForcedMotion.IsActive(sim.Entities, 0), "отброс — как у топота");
            for (int k = 0; k < Simulation.ThicketKnockbackTicks; k++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(sim.Entities.Position[0], sim.Entities.Position[Boss]).ToDouble(),
                Is.EqualTo(8.5).Within(0.05));

            // Внутри круга после первого удара — укрыт от кольца (рывок внутрь). И центром на 5,0 м —
            // тело заходит на кромку кольца, но центр внутри 5,2 (ревью 02.10, ночь: «шагнул обратно внутрь»).
            foreach (double back in new[] { 2.0, 5.0 })
            {
                var inner = Arena();
                MeleeOnly(inner);
                Until(inner, IntroDone);
                HeroInFront(inner, 3.85);
                Assert.AreNotEqual(-1, RunUntilStarted(inner, EnemyActionKind.ThicketStomp, 300));
                Assert.IsTrue(inner.TryGetThicketMasterAction(Boss, out var second));
                HeroInFront(inner, 6.5);
                Until(inner, second.ImpactTick + 1);
                HeroInFront(inner, back);
                health = inner.Entities.Health[0];
                Until(inner, second.LastImpactTick + 1);
                Assert.AreEqual(health, inner.Entities.Health[0], "центр внутри 5,2 — кольцом не задет (" + back + " м)");
            }
            Assert.IsTrue(Simulation.ThicketStompRingHits(FixVec2.Zero, At(5.3, 0), Fix64.Ratio(45, 100)), "центр за 5,2 — задет");
            Assert.IsTrue(Simulation.ThicketStompRingHits(FixVec2.Zero, At(7.9, 0), Fix64.Ratio(45, 100)), "тело на внешней кромке — задет");
            Assert.IsFalse(Simulation.ThicketStompRingHits(FixVec2.Zero, At(7.96, 0), Fix64.Ratio(45, 100)), "за кольцом — цел");
            Assert.IsFalse(Simulation.ThicketStompRingHits(FixVec2.Zero, At(5.19, 0), Fix64.Ratio(45, 100)), "центр внутри — цел");
        }

        /// <summary>
        /// Ответ ногами на топот (ревью 02.10, ночь: «топот — либо рывок, либо попал»; проверка находок
        /// 03.10 — из настоящего места «прижался»). Герой ПРИЖАТ к корпусу (зазор до кромки = радиус тела) —
        /// по каждому направлению вокруг босса через 10°, хуже всего «подмышка» между лапой и бедром
        /// (1,73 м от центра), — стоит там в тик знака, через 12 тиков (0,4 с) или 15 (0,5 с) идёт прямо
        /// прочь от центра и до удара (замах 42) выходит из круга 5,2 + тело (выйдя — стоит); в тик удара
        /// круга разворачивается и до кольца (через 15) снова центром внутри 5,2 — кольцо его не задевает.
        /// Скорость и разгон — настоящие (приказ идти мышью). Стоять на месте — попадание (проверка жива).
        /// </summary>
        [Test]
        public void Stomp_FootAnswer_PressedToTheHull_EveryDirection_WalkAwayAfterPointFourOrPointFiveSeconds_StepBackBeforeTheRing()
        {
            double closest = double.MaxValue, deepest = double.MaxValue;
            var worst = new Dictionary<int, double> { [12] = double.MaxValue, [15] = double.MaxValue };
            for (int degrees = 0; degrees < 360; degrees += 10)
                foreach (int react in new[] { -1, 12, 15 })
                {
                    var sim = Arena();
                    MeleeOnly(sim);
                    Until(sim, IntroDone);
                    var e = sim.Entities;
                    // Топот по правилу «прижался» (3,85 м перед мордой — лапа не достаёт, 240 тиков подряд).
                    HeroInFront(sim, 3.85);
                    int start = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 300);
                    Assert.AreNotEqual(-1, start);
                    Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
                    Assert.AreEqual(start + 1, sim.Tick, "герой ставится в тик сразу после знака");
                    var center = e.Position[Boss];
                    var forward = e.Facing[Boss].Normalized();
                    var left = new FixVec2(-forward.Y, forward.X);
                    double a = degrees * System.Math.PI / 180;
                    var ray = (forward * Fix64.FromDouble(System.Math.Cos(a)) + left * Fix64.FromDouble(System.Math.Sin(a))).Normalized();
                    // Прижат к корпусу: ближайшая точка луча, где зазор до кромки — радиус тела героя.
                    Fix64 r = Fix64.Ratio(1, 2);
                    while (sim.ThicketHullGap(Boss, center + ray * r) < e.BodyRadius[0]) r += Fix64.Ratio(1, 100);
                    e.Position[0] = center + ray * r;
                    e.Velocity[0] = FixVec2.Zero;
                    closest = System.Math.Min(closest, r.ToDouble());
                    var away = center + ray * Fix64.FromInt(20);
                    var back = center + ray * Fix64.FromInt(3);
                    int health = e.Health[0];
                    while (sim.Tick <= stomp.LastImpactTick)
                    {
                        int t = sim.Tick - start;
                        InputFrame input;
                        // Прочь, пока тело в круге (5,2 + 0,45 и 10 см запаса), там — стоп; с удара круга — обратно внутрь.
                        if (react < 0 || t < react) input = Walk(e.Position[0]);
                        else if (sim.Tick <= stomp.ImpactTick)
                            input = Metres(e.Position[0], center) < 5.75 ? Walk(away) : Walk(e.Position[0]);
                        else input = Walk(back);
                        sim.Step(input);
                        if (react < 0) continue;
                        if (sim.Tick - 1 == stomp.ImpactTick)
                            worst[react] = System.Math.Min(worst[react], Metres(e.Position[0], center) - 5.2 - 0.45);
                        if (sim.Tick - 1 == stomp.LastImpactTick)
                            deepest = System.Math.Min(deepest, 5.2 - Metres(e.Position[0], center));
                    }
                    if (react < 0) Assert.Less(e.Health[0], health, "стоя на месте — попадание, " + degrees + "°");
                    else Assert.AreEqual(health, e.Health[0], "ногами — ни круга, ни кольца, " + degrees + "°, реакция " + react);
                }
            TestContext.WriteLine("pressed spot " + closest.ToString("0.00") + " m from the centre; margin out of the circle: react 12 "
                + worst[12].ToString("0.000") + " m, react 15 " + worst[15].ToString("0.000") + " m; back inside 5.2 by "
                + deepest.ToString("0.00") + " m at the ring");
            Assert.That(closest, Is.LessThan(1.8), "проверена и «подмышка» вплотную");
            Assert.That(worst[12], Is.GreaterThan(0.05), "реакция 0,4 с — вышел из круга с запасом");
            Assert.That(worst[15], Is.GreaterThan(0), "реакция 0,5 с — ещё выходит");
            Assert.That(deepest, Is.GreaterThan(0), "к кольцу — снова внутри 5,2");
        }

        // ---------- «Мягче серии и топот» (владелец 02.10) ----------

        [Test]
        public void SoftSeries_PhaseThree_StrikeByStrike_23_Then8_Then8()
        {
            var sim = Arena();
            MeleeOnly(sim);
            ToPhase(sim, 30);
            HeroInFront(sim, 3.3);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(3, paw.Stages, "фаза 3 — серия из трёх");
            var taken = new List<int>();
            int impact = paw.ImpactTick;
            for (int strike = 0; strike < paw.Stages; impact += Simulation.ThicketPawGapOf(paw, strike), strike++)
            {
                Until(sim, impact);
                HeroInFront(sim, 3.3);
                int before = sim.Entities.Health[0];
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw, true), "удар " + (strike + 1) + " попал");
                taken.Add(before - sim.Entities.Health[0]);
            }
            CollectionAssert.AreEqual(new[] { 23, 8, 8 }, taken, "первый — лапа целиком, второй и третий — 35% (лапа 23 — перебаланс 03.10)");
            Assert.AreEqual(23, sim.ThicketPawStrikeDamageOf(Boss, 0));
            Assert.AreEqual(8, sim.ThicketPawStrikeDamageOf(Boss, 1), "23 × 0,35 = 8,05");
            Assert.AreEqual(8, sim.ThicketPawStrikeDamageOf(Boss, 2));
        }

        [Test]
        public void SoftStomp_HeroWhoJustRunsPast_IsNotStomped()
        {
            // Пробегает мимо босса в 4,5 м от центра со скоростью бега (0,15 м за тик): в круге
            // топота (5,2) — около 35 тиков. «Прижался» — только 240 тиков ПОДРЯД.
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var boss = sim.Entities.Position[Boss];
            int stomps = 0, longest = 0;
            for (int k = 0; k <= 160; k++)
            {
                sim.Entities.Position[0] = boss + At(4.5, -12 + 0.15 * k);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                sim.Step(InputFrame.Empty);
                stomps += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp);
                longest = System.Math.Max(longest, sim.ThicketMasterHugTicks(Boss));
            }
            Assert.That(longest, Is.InRange(20, Simulation.ThicketStompHugTicks - 1), "был в круге, но меньше 8 с");
            Assert.AreEqual(0, stomps, "мимо пробежавшего не топчет");
        }

        [Test]
        public void HugStomp_StepsOutEvery200_NoStomp_Then240InARow_Stomps_CountsWhateverTheBossDoes()
        {
            // Баланс 02.10, ночь: топот «прижался» — 240 тиков (8 с) ПОДРЯД в круге 5,2, что бы босс ни
            // делал (было 45 тиков без действия и вне лапы — с лапой, достающей бок, не наступало).
            // Стоит в 3,85 м (лапа не достаёт, босс на стенде не ходит), но раз в 200 тиков на тик
            // выходит за 5,2: топота нет. Жребий топота рядом с лапой (03.10) закрыт — проверяется правило.
            var sim = Arena();
            MeleeOnly(sim);
            NoStompPick(sim);
            Until(sim, IntroDone);
            int stomps = 0, lastOut = -1;
            for (int k = 0; k < 800; k++)
            {
                bool outside = k % 200 == 199;
                HeroInFront(sim, outside ? 5.8 : 3.85);
                if (outside) lastOut = sim.Tick;
                sim.Step(InputFrame.Empty);
                stomps += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp);
                Assert.That(sim.ThicketMasterHugTicks(Boss), Is.LessThan(Simulation.ThicketStompHugTicks));
            }
            Assert.AreEqual(0, stomps, "тик за 5,2 начинает счёт заново");

            // Остался рядом: топот — ровно на 240-й тик подряд.
            int start = -1;
            for (int k = 0; k < 300 && start < 0; k++)
            {
                HeroInFront(sim, 3.85);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp) > 0) start = tick;
            }
            Assert.AreEqual(lastOut + Simulation.ThicketStompHugTicks, start, "240 тиков подряд прижатым — топот");
            Assert.AreEqual(0, sim.ThicketMasterHugTicks(Boss), "счёт после топота — заново");

            // В действии (сам топот) счёт идёт: «прижался» — время у тела, а не простой босса.
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            while (sim.Tick < stomp.ImpactTick)
            {
                HeroInFront(sim, 3.85);
                sim.Step(InputFrame.Empty);
            }
            Assert.AreEqual(stomp.ImpactTick - start - 1, sim.ThicketMasterHugTicks(Boss), "в действии копится");
        }

        [Test]
        public void HugStomp_HeroInFrontInReach_PawSeries_ThenAStompOnceIn240_PawsStayTheBase()
        {
            // Перед мордой в досягаемости лапы босс бьёт сериями; кто не отходит 240 тиков подряд, тому
            // топот вместо очередной серии (ритм «лапа, лапа, лапа — топот»), своя перезарядка 180.
            // Жребий топота рядом с лапой (03.10) закрыт — здесь только правило «прижался».
            var sim = Arena();
            MeleeOnly(sim);
            NoStompPick(sim);
            Until(sim, IntroDone);
            int stomps = 0, paws = 0, firstStomp = -1;
            for (int k = 0; k < 900; k++)
            {
                HeroInFront(sim, 3.0);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                NoStompPick(sim);   // каждый топот ставит перезарядку жребия заново — держим закрытым
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) paws++;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketStomp) { stomps++; if (firstStomp < 0) firstStomp = tick; }
                }
            }
            Assert.That(firstStomp - IntroDone, Is.GreaterThanOrEqualTo(Simulation.ThicketStompHugTicks - 1), "не раньше 240 тиков прижатым");
            Assert.That(stomps, Is.InRange(2, 4), "топот — раз в 240 тиков с небольшим");
            Assert.That(paws, Is.GreaterThanOrEqualTo(3 * stomps), "лапа — основа");
        }

        // ---------- топот «за спиной» (ревью 02.10, вечер) ----------

        /// <summary>Шагает, держа героя в spot (или за спиной в behind метрах — каждый тик заново), пока не начнётся топот; тик или −1.</summary>
        private static int HoldForStomp(Simulation sim, int limit, FixVec2? spot = null, double behind = 0)
        {
            for (int k = 0; k < limit; k++)
            {
                var e = sim.Entities;
                e.Position[0] = spot ?? e.Position[Boss] - e.Facing[Boss].Normalized() * Fix64.FromDouble(behind);
                e.Health[0] = e.MaxHealth[0];
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketStomp && ev.Amount == 0)
                        return tick;
            }
            return -1;
        }

        [Test]
        public void RearStomp_HeroBehindTwentyTicks_StompsInsteadOfTurning_CooldownNinety()
        {
            // «Он должен делать раньше АоЕ-атаку, когда пытается развернуться ударить героя»: герой
            // за спиной в 3 м — босс поворачивается 2,5° за тик, за 20 тиков — на 50°, герой всё ещё
            // сзади: топот на 20-й тик, не на 60-й (правило «рядом») и без разворота до лапы (56 тиков).
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var spot = sim.Entities.Position[Boss] - sim.Entities.Facing[Boss].Normalized() * Fix64.FromInt(3);
            int first = sim.Tick;
            int start = HoldForStomp(sim, 80, spot);
            Assert.AreEqual(first + Simulation.ThicketRearStompTicks - 1, start, "20-й тик за спиной");
            Assert.That(sim.ThicketMasterHugTicks(Boss), Is.LessThan(Simulation.ThicketStompHugTicks), "правило «прижался» не дошло");
            var toHero = (spot - sim.Entities.Position[Boss]).Normalized();
            Assert.That(FixVec2.Dot(sim.Entities.Facing[Boss].Normalized(), toHero), Is.LessThan(Simulation.ThicketRearCos),
                "так и стоит к нему спиной");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            Assert.AreEqual(ThicketMasterAction.Stomp, stomp.Action);
            Assert.AreEqual(start + Simulation.ThicketStompWindupTicks, stomp.ImpactTick, "замах тот же — 42");
            Assert.AreEqual(start + Simulation.ThicketRearStompCooldownTicks, sim.ThicketRearStompReadyTick(Boss));
            Assert.AreEqual(0, sim.ThicketReadyTick(Boss, ThicketMasterAction.Stomp), "«за спиной» перезарядку «прижался» не ставит");
            Assert.AreEqual(0, sim.ThicketMasterRearTicks(Boss), "счёт после топота — заново");

            // Остался за спиной (ходит за ним): следующий — через 90 от начала прошлого, но и не раньше
            // отдыха фазы 1 после конца топота (отдых 30: 59 + 30 = 89 — держит перезарядка 90).
            int next = HoldForStomp(sim, 200, behind: 3);
            Assert.AreEqual(System.Math.Max(start + Simulation.ThicketRearStompCooldownTicks, stomp.EndTick + Simulation.ThicketRestPhase1Ticks),
                next, "перезарядка «за спиной» — 90 (и отдых)");
        }

        [Test]
        public void Stomps_AnyTwoAtLeastNinetyApart_HugOwnCooldown180_EnrageDoesNotShortenThem()
        {
            // Фаза 3 (ниже половины здоровья: отдых и перезарядки ×0,85; отдых 12 → 10): топот кончается
            // через 59 после начала, отдых и окно отпустили бы следующий через 69 — но любые два топота
            // не ближе 90 (×0,85 их не режет), а «прижался» — не чаще раза в 180 (и 240 тиков прижатым).
            // Жребий топота рядом с лапой (03.10, фаза 1) здесь не идёт — фаза 3; закрыт для ясности.
            var sim = Arena();
            MeleeOnly(sim);
            NoStompPick(sim);
            ToPhase(sim, 30);
            HeroInFront(sim, 3.85);
            int hug = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 300);
            Assert.AreNotEqual(-1, hug);
            Assert.AreEqual(hug + Simulation.ThicketStompCooldownTicks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Stomp), "180, не 153");
            Assert.AreEqual(hug + Simulation.ThicketStompSpacingTicks, sim.ThicketRearStompReadyTick(Boss), "90, не 76");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var first));
            Assert.That(first.EndTick + Simulation.ThicketRestPhase3Ticks * Simulation.ThicketEnragedCooldownPercent / 100,
                Is.LessThan(hug + Simulation.ThicketStompSpacingTicks), "отдых сам по себе отпустил бы раньше");
            int rear = HoldForStomp(sim, 300, behind: 3);
            Assert.AreEqual(hug + Simulation.ThicketStompSpacingTicks, rear, "за спиной — через 90 от прошлого, не раньше");
            // Снова «прижался» (перед мордой дальше лапы): не раньше 180 от прошлого «прижался».
            int again = -1;
            for (int k = 0; k < 400 && again < 0; k++)
            {
                HeroInFront(sim, 3.85);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketStomp && e.Amount == 0)
                        again = tick;
            }
            Assert.That(again, Is.GreaterThanOrEqualTo(hug + Simulation.ThicketStompCooldownTicks), "своя перезарядка «прижался» — 6 с");
            Assert.That(again, Is.GreaterThanOrEqualTo(rear + Simulation.ThicketStompSpacingTicks));
        }

        [Test]
        public void RearStomp_HeroAtTheSide_BossTurnsAndPaws_FarBehind_NoStomp()
        {
            // Сбоку (90°) в 3,3 м: первый удар серии доворачивает к герою до 42,5° за замах (ревью 02.10,
            // ночь) — боссу хватает довернуться корпусом на ~10°, и лапа; топота нет.
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var f = sim.Entities.Facing[Boss].Normalized();
            var side = sim.Entities.Position[Boss] + new FixVec2(-f.Y, f.X) * Fix64.Ratio(33, 10);
            int paw = -1, stomps = 0;
            for (int k = 0; k < 60 && paw < 0; k++)
            {
                sim.Entities.Position[0] = side;
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                stomps += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp);
                if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw) > 0) paw = tick;
            }
            Assert.AreEqual(0, stomps, "сбоку — не «за спиной»");
            Assert.That(paw - IntroDone, Is.InRange(0, 12), "довернул чуть-чуть и ударил лапой с доворотом");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var series));
            Assert.IsTrue(Simulation.TelegraphContains(Simulation.ThicketPawStrikeSector(series.Origin, series.Direction, 0), side,
                sim.Entities.BodyRadius[0]), "сектор первого удара — на герое");

            // Сзади, но дальше круга топота (6 м): разворачивается, не топчет.
            sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var far = sim.Entities.Position[Boss] - sim.Entities.Facing[Boss].Normalized() * Fix64.FromInt(6);
            Assert.AreEqual(-1, HoldForStomp(sim, 150, far), "за спиной в 6 м — без топота");
            Assert.That(FixVec2.Dot(sim.Entities.Facing[Boss].Normalized(), (far - sim.Entities.Position[Boss]).Normalized()).ToDouble(),
                Is.GreaterThan(0.999), "развернулся к нему");
        }

        [Test]
        public void Shares_ScaleWithTheLeafDamage_AtTheFirstArena()
        {
            // Перебаланс 03.10: база лапы 14 (была 12), второй и третий удары — 35%; доли топота 53 и бури 60
            // (были 62 и 70) — их урон прежний.
            var sim = Arena(arena: 1);
            Assert.AreEqual(14, sim.ThicketPawDamageOf(Boss));
            Assert.AreEqual(5, sim.ThicketPawStrikeDamageOf(Boss, 1), "второй удар серии: 14 × 0,35 = 4,9");
            Assert.AreEqual(18, sim.ThicketStompDamageOf(Boss), "14 × 53/41 = 18,1");
            Assert.AreEqual(23, sim.ThicketShareOf(Boss, Simulation.ThicketDiveDamageA9), "14 × 66/41 = 22,5");
            Assert.AreEqual(20, sim.ThicketShareOf(Boss, Simulation.ThicketStormDamageA9), "14 × 60/41 = 20,5");
            Assert.AreEqual(13, sim.ThicketStompRingDamageOf(Boss), "18 × 0,75 = 13,5");
        }

        [Test]
        public void Size_PlusFifteenPercent_FromTheBodyOnly()
        {
            double Of(Fix64 v) => v.ToDouble();
            Assert.AreEqual(4.14, Of(Simulation.ThicketModelHeight), 1e-4, "модель 3,6 → 4,14 м");
            Assert.AreEqual(4.14, Of(Simulation.ThicketPawRadius), 1e-4, "лапа 3,6 → 4,14");
            Assert.AreEqual(3.68, Of(Simulation.ThicketPawStartRange), 1e-4);
            Assert.AreEqual(3.22, Of(Simulation.ThicketHoldDistance), 1e-4);
            Assert.AreEqual(5.2, Of(Simulation.ThicketStompRadius), 1e-4, "топот 4,5 → 5,2");
            Assert.AreEqual(7.5, Of(Simulation.ThicketStompRingOuterRadius), 1e-4, "второе кольцо до 7,5");
            Assert.AreEqual(2.3, Of(Simulation.ThicketRoarInnerRadius), 1e-4, "рёв 2 → 2,3");
            Assert.AreEqual(6.3, Of(Simulation.ThicketRoarOuterRadius), 1e-4, "рёв 5,5 → 6,3");
            Assert.AreEqual(4.0, Of(Simulation.ThicketDiveRadius), 1e-4, "выход 3,5 → 4,0");
            // Корпус (Simulation.ForestBoss.Hull) — от модели 3,6 м ×1,15: грудь 1,65 / 0,73, край лапы 3,3 м.
            Simulation.ThicketHullLocal(0, out var chest, out _, out var chestRadius);
            Assert.AreEqual(1.8975, Of(chest), 1e-4, "грудь 1,65 × 1,15");
            Assert.AreEqual(0.8395, Of(chestRadius), 1e-4, "грудь 0,73 × 1,15");
            Assert.AreEqual(3.297, Of(Simulation.ThicketHullReach), 1e-3, "край лапы");
            // Тело в Sim — потолок расталкивания (EntityStore.MaxBodyRadius), его рост не берёт.
            Assert.AreEqual(EntityStore.MaxBodyRadius, EnemyArchetypes.ThicketMasterBodyRadius);
            // Круги не от тела — прежние.
            Assert.AreEqual(1.5, Of(Simulation.ThicketSproutRadius), 1e-4);
            Assert.AreEqual(1.4, Of(Simulation.ThicketRainRadius), 1e-4);
            Assert.AreEqual(1.8, Of(Simulation.ThicketPollenRadius), 1e-4);
            Assert.AreEqual(2.0, Of(Simulation.ThicketStormSafeRadius), 1e-4);
        }

        // ---------- пороги и фазы ----------

        [Test]
        public void Roars_At66_50_33_AfterTheCurrentAction_OncePerThreshold()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            int max = sim.Entities.MaxHealth[Boss];
            Until(sim, paw.StartTick + 10);
            sim.Entities.Health[Boss] = max * 60 / 100;
            int health = sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(2, sim.ThicketMasterPhase(Boss));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var still));
            Assert.AreEqual(ThicketMasterAction.Paw, still.Action, "начатая лапа доигрывается");
            Assert.AreEqual(1, sim.ThicketMasterRoaredPhase(Boss), "внешность меняется на рёве, не раньше");
            int roarAt = RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 100);
            Assert.AreEqual(paw.EndTick, roarAt, "рёв — сразу после окна серии");
            Assert.AreEqual(23 + 8, health - sim.Entities.Health[0], "серия из двух ударила до рёва (второй — 35%)");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Assert.AreEqual(Simulation.ThicketRoar66Bit, roar.Tag);
            Assert.AreEqual(2, sim.ThicketMasterRoaredPhase(Boss));

            Until(sim, roar.EndTick + 1);
            Assert.AreEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 120), "66% — один раз");

            sim.Entities.Health[Boss] = max * 45 / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 200), "рёв на 50%");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar50));
            Assert.AreEqual(Simulation.ThicketRoar50Bit, roar50.Tag);
            Until(sim, roar50.EndTick + 1);

            sim.Entities.Health[Boss] = max * 30 / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 200), "рёв на 33%");
            Assert.AreEqual(3, sim.ThicketMasterPhase(Boss));
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(Simulation.ThicketRoarIntroBit | Simulation.ThicketRoar66Bit | Simulation.ThicketRoar50Bit
                | Simulation.ThicketRoar33Bit, memory.RoarsDone);
            Assert.AreEqual(0, memory.RoarsPending);
        }

        /// <summary>
        /// Ревью 02.10, вечер («закруживаешь его… прокручивается на месте»): в рёве порога корпус
        /// доворачивается к кружащему герою только первые ThicketRoarTurnTicks (10 тиков, 25°), дальше
        /// стоит до конца рёва — клип рёва не вертится на ногах; после рёва поворот — снова обычный.
        /// </summary>
        [Test]
        public void PhaseRoar_HeroCircles_BossTurnsOnlyTheFirstTenTicks_ThenStands()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 60 / 100;
            Assert.AreEqual(IntroDone, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10), "рёв порога сразу");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Assert.AreEqual(Simulation.ThicketRoar66Bit, roar.Tag);
            Assert.AreEqual(10, Simulation.ThicketRoarTurnTicks);
            // Герой кружит в 7 м (за кольцом рёва 6,3 и за «спиной» 5,2): 4° за тик — быстрее поворота.
            var center = sim.Entities.Position[Boss];
            var look = sim.Entities.Facing[Boss].Normalized();
            double angle = System.Math.Atan2(look.Y.ToDouble(), look.X.ToDouble()) + System.Math.PI / 2;
            double turned = 0;
            int lastTurn = -1;
            // Ход босса идёт раньше действий тика: в тик EndTick он ещё в рёве, свободен — со следующего.
            while (sim.Tick <= roar.EndTick + 1)
            {
                angle += 4 * System.Math.PI / 180;
                sim.Entities.Position[0] = center + At(7 * System.Math.Cos(angle), 7 * System.Math.Sin(angle));
                var before = sim.Entities.Facing[Boss].Normalized();
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                double dot = FixVec2.Dot(before, sim.Entities.Facing[Boss].Normalized()).ToDouble();
                double step = System.Math.Acos(System.Math.Min(1.0, dot)) * 180 / System.Math.PI;
                if (tick > roar.EndTick)
                {
                    Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "после рёва — без действия");
                    Assert.That(step, Is.GreaterThan(2.0), "после рёва — снова поворот к герою");
                    break;
                }
                Assert.That(step, Is.LessThanOrEqualTo(2.51), "не быстрее 2,5° за тик, тик " + tick);
                if (step > 1e-3) { lastTurn = tick; turned += step; }
            }
            Assert.AreEqual(roar.StageStartTick + Simulation.ThicketRoarTurnTicks, lastTurn, "доворот — только первые 10 тиков рёва");
            Assert.That(turned, Is.InRange(24.5, 25.1), "за рёв — не больше 25°");
        }

        [Test]
        public void PhaseThree_ThreeThresholdsInOneHit_OneRoar_ThenSeriesOfThree()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            int roarAt = RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10);
            Assert.AreEqual(IntroDone, roarAt);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Assert.AreEqual(Simulation.ThicketRoar66Bit | Simulation.ThicketRoar50Bit | Simulation.ThicketRoar33Bit, roar.Tag,
                "один рёв закрывает все пороги разом");
            Assert.AreEqual(3, sim.ThicketMasterRoaredPhase(Boss), "внешность фазы 3 — с начала рёва");
            Until(sim, roar.EndTick);
            HeroInFront(sim, 2.5);
            int health = sim.Entities.Health[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(3, paw.Stages, "в фазе 3 — серия из трёх");
            // Первый замах фазы 3 — 17, как во всех фазах (проверка находок 03.10; баланс 02.10, ночь, делал 15),
            // промежутки — броски 10–11.
            Assert.AreEqual(start + Simulation.ThicketPawWindupTicks, paw.ImpactTick);
            int gap1 = Simulation.ThicketPawGapOf(paw, 0), gap2 = Simulation.ThicketPawGapOf(paw, 1);
            Assert.That(gap1, Is.InRange(Simulation.ThicketPawGapPhase3Min, Simulation.ThicketPawGapPhase3Max));
            Assert.That(gap2, Is.InRange(Simulation.ThicketPawGapPhase3Min, Simulation.ThicketPawGapPhase3Max));
            Assert.AreEqual(paw.ImpactTick + gap1 + gap2, paw.LastImpactTick, "П/Л/П через промежутки фазы 3");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick);

            int[] at = { paw.ImpactTick, paw.ImpactTick + gap1, paw.ImpactTick + gap1 + gap2 };
            for (int stage = 1; stage < 3; stage++)
            {
                Until(sim, at[stage - 1]);
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw), "знак удара " + stage);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketPaw)
                        Assert.AreEqual(stage, e.Amount, "номер удара");
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var step));
                Assert.AreEqual(stage, step.Stage);
                Assert.AreEqual(at[stage], step.ImpactTick);
            }
            Until(sim, paw.LastImpactTick + 1);
            Assert.AreEqual(23 + 8 + 8, health - sim.Entities.Health[0], "23, потом два по 8 (35%)");
            // Фаза 3: отдых 12 × 0,85 = 10 после кадра контакта — короче окна 30: следующую серию держит окно
            // (замах 17 — за 17 до его конца, через 13 после последнего удара).
            int next = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.That(paw.EndTick + Simulation.ThicketRestPhase3Ticks * Simulation.ThicketEnragedCooldownPercent / 100,
                Is.LessThan(paw.LastImpactTick + Simulation.ThicketWindowTicks - Simulation.ThicketPawWindupTicks), "отдых короче окна");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketWindowTicks - Simulation.ThicketPawWindupTicks, next,
                "серия — сразу, как замах дотягивается до конца окна");
        }

        // ---------- подмога ----------

        [Test]
        public void BossArena_SwitchOn_ThicketMasterGetsOneAddWaveAt66_InterimBossStillTwo()
        {
            var location = ArenaEncounterTests.ForestLocation();
            for (int pass = 0; pass < 2; pass++)
            {
                bool thicket = pass == 0;
                var map = ArenaEncounterTests.ArenaMap(location, 9, 5);
                var sim = new Simulation(5, 512) { ThicketMasterBossEnabled = thicket };
                var plan = location.GetLevel(9).Spawn(sim, map, 5, null, 9);
                sim.PlayerInvulnerable = true;
                int boss = plan.BossId;
                Assert.That(boss, Is.GreaterThan(0));
                Assert.AreEqual(thicket ? EnemyKind.ForestThicketMaster : EnemyKind.ForestGuardian, sim.Entities.Kind[boss]);
                if (thicket)
                {
                    Assert.AreEqual(EnemyArchetypes.ScaleHealth(EnemyArchetypes.ThicketMasterHealth,
                        EnemyArchetypes.DepthHealthPercent(9)), sim.Entities.MaxHealth[boss]);
                    Assert.AreEqual(11388, sim.Entities.MaxHealth[boss], "7300 × 156% (перебаланс 03.10; было 7500 → 11700, 7600 → 11856, 6600 → 10296)");
                    Assert.AreEqual(23, sim.Entities.Damage[boss], "без надбавки ×1,5 временного босса (14 × 164%)");
                    Assert.AreEqual(Progression.BossKillXp, sim.Entities.XpReward[boss]);
                    Assert.AreEqual(EntityStore.MaxBodyRadius, sim.Entities.BodyRadius[boss]);
                    Assert.IsTrue(plan.IsElite(boss));
                }
                int max = sim.Entities.MaxHealth[boss];
                sim.Entities.Health[boss] = max * 66 / 100;
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, sim.BossAddWavesSpawned, "волна на 66%");
                int afterFirst = sim.Entities.Count;
                sim.Entities.Health[boss] = max * 20 / 100;
                for (int tick = 0; tick < 60; tick++) sim.Step(InputFrame.Empty);
                Assert.AreEqual(thicket ? 1 : 2, sim.BossAddWavesSpawned, thicket ? "на 33% подмоги нет" : "временный босс — две волны");
                if (thicket) Assert.AreEqual(afterFirst, sim.Entities.Count);
            }
        }

        [Test]
        public void Cooldowns_StretchWhileAddsLive()
        {
            // Герой с конца вступления в 9,5 м перед мордой — в дальней полосе (6,76 м от груди): нырок
            // через 2 с.
            var sim = Arena(distance: 9);
            Only(sim, ThicketMasterAction.Dive);
            // Подмога: живой Хранитель далеко за спиной героя.
            int add = sim.AddKindTestEnemy(EnemyKind.ForestGuardian, At(-20, 0), 100);
            sim.Entities.Stats[add].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(add);
            sim.Entities.NextAttackTick[add] = int.MaxValue;
            Until(sim, IntroDone);
            HeroInFront(sim, 9.5);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400);
            Assert.AreEqual(IntroDone + Simulation.ThicketDiveFarTicks - 1, start, "две секунды в дальней полосе");
            Assert.AreEqual(start + Simulation.ThicketDiveCooldownPhase1Ticks * 125 / 100,
                sim.ThicketReadyTick(Boss, ThicketMasterAction.Dive), "перезарядка нырка фазы 1 (360) ×1,25 при подмоге");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            Until(sim, dive.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase1Ticks * 125 / 100, memory.NextActionTick,
                "отдых фазы 1 ×1,25 при подмоге");
        }

        [Test]
        public void Cooldowns_ShrinkToEightyFivePercent_FromHalfHealth()
        {
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 45 / 100;
            Assert.AreEqual(IntroDone, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10), "рёв 66 и 50 разом");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Until(sim, roar.EndTick);
            HeroInFront(sim, 2.5);
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 30));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(paw.StartTick + Simulation.ThicketPawWindupTicks, paw.ImpactTick, "замах 17 ярость не сокращает");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick);
            Until(sim, paw.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(2, memory.Phase);
            Assert.AreEqual(paw.EndTick + Simulation.ThicketRestPhase2Ticks * 85 / 100, memory.NextActionTick, "отдых фазы 2 ×0,85");
            // Перезарядка каста — тоже ×0,85 (топоту — нет: Stomps_AnyTwoAtLeastNinetyApart_*).
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Pollen, 0);
            HeroInFront(sim, 8);
            int pollen = RunUntilStarted(sim, EnemyActionKind.ThicketPollen, 200);
            Assert.AreNotEqual(-1, pollen);
            // Герой в 8 м — лапа не достаёт: перезарядка каста «издали» (баланс 02.10, ночь), и она ×0,85.
            Assert.AreEqual(pollen + Simulation.ThicketPollenFarCooldownTicks * 85 / 100,
                sim.ThicketReadyTick(Boss, ThicketMasterAction.Pollen), "перезарядка ×0,85");
        }

        // ---------- иммунитет ----------

        [Test]
        public void Immune_StunAndForcedMotion_NeitherHoldsNorMovesNorCancels()
        {
            var sim = Arena();
            Assert.IsFalse(ForcedMotion.Begin(sim.Entities, Boss, sim.Entities.Position[Boss] + At(2, 0), 8,
                ForcedMotionKind.Dragged), "волок не берёт: вес 0");
            Assert.AreEqual(Fix64.Zero, sim.Entities.PushWeight[Boss]);
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            var at = sim.Entities.Position[Boss];
            int health = sim.Entities.Health[0];

            sim.Statuses.ApplyStun(Boss, sim.Tick + 90);
            ForcedMotion.Begin(sim.Entities, Boss, at + At(3, 0), 10, ForcedMotionKind.Knockback);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, sim.Statuses.StunUntilTick[Boss], "оглушение снято");
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, Boss), "принудительное движение снято");
            Assert.AreEqual(at, sim.Entities.Position[Boss], "отброс не сдвинул");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var still));
            Assert.AreEqual(paw.Serial, still.Serial, "лапа не сорвана");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionCancelled, EnemyActionKind.ThicketPaw));
            Until(sim, paw.ImpactTick + 1);
            Assert.AreEqual(23, health - sim.Entities.Health[0], "контакт в свой тик");
        }

        [Test]
        public void Immune_StunnedBossKeepsWalking()
        {
            var sim = Arena(distance: 8, walks: true);
            // Без кастов: герой в 8 м с начала вступления уже «кайтит» — иначе первым делом «Корни-плеть».
            MeleeOnly(sim);
            Until(sim, IntroDone);
            sim.Statuses.ApplyStun(Boss, sim.Tick + 120);
            var before = sim.Entities.Position[Boss];
            for (int k = 0; k < 20; k++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(before, sim.Entities.Position[Boss]).ToDouble(), Is.GreaterThan(0.5));
        }

        // ---------- ход ----------

        [Test]
        public void Walk_TurnsAtMostTwoAndAHalfDegrees_HalfCircleIn72Ticks_AndStaysOnTheLeash()
        {
            var sim = Arena(distance: 8, walks: true);
            // Ход, не нырок: нырок достаёт и героя за поводком (Dive_HeroBeyondTheLeash_*).
            Only(sim);
            Until(sim, IntroDone);
            // Герой далеко за спиной и за поводком.
            sim.Entities.Position[0] = sim.Entities.Position[Boss] + At(25, 0);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            // Ревью 02.10, вечер: «закруживаешь его и он тупо разворачивается» — 4,5° → 2,5° за тик.
            Assert.AreEqual(144, Simulation.ThicketTurnTicksPerCircle);
            double cos = System.Math.Cos(System.Math.PI / 72) - 1e-4;
            int facedAt = -1;
            for (int k = 0; k < 360; k++)
            {
                var facing = sim.Entities.Facing[Boss];
                sim.Step(InputFrame.Empty);
                double dot = FixVec2.Dot(facing.Normalized(), sim.Entities.Facing[Boss].Normalized()).ToDouble();
                Assert.That(dot, Is.GreaterThanOrEqualTo(cos), "поворот за тик, тик " + (sim.Tick - 1));
                double leash = FixVec2.Distance(sim.Entities.Position[Boss], memory.Home).ToDouble();
                Assert.That(leash, Is.LessThanOrEqualTo(10.01), "поводок, тик " + (sim.Tick - 1));
                var toHero = (sim.Entities.Position[0] - sim.Entities.Position[Boss]).Normalized();
                if (facedAt < 0 && FixVec2.Dot(sim.Entities.Facing[Boss].Normalized(), toHero).ToDouble() > 0.9995) facedAt = k + 1;
            }
            // Разворот на 180° — 72 тика (2,4 с), не 40.
            Assert.That(facedAt, Is.InRange(70, 74), "развернулся к герою за спиной");
            Assert.That(FixVec2.Distance(sim.Entities.Position[Boss], memory.Home).ToDouble(), Is.GreaterThan(9.5),
                "дошёл до края поводка");
        }

        [Test]
        public void Walk_ReachesTwoMetresPerSecond_KitingOrFarHero_TwoPointEight()
        {
            // Во вступлении герой под лапой (счёт «лапа не достаёт» — ноль), в тик конца рёва отходит на 8,5 м.
            var sim = Arena(distance: 3, walks: true);
            MeleeOnly(sim);
            Until(sim, IntroDone);
            HeroInFront(sim, 8.5);
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "после вступления — ход");
            Assert.AreEqual(ThicketBand.Mid, sim.ThicketHeroBand(Boss), "8,5 м — средняя полоса (5,76 м от груди)");
            var from = sim.Entities.Position[Boss];
            for (int k = 0; k < 30; k++) sim.Step(InputFrame.Empty);
            // Разгон за 3 тика, дальше 2,0 м/с (владелец 02.10, было 2,6): за 30 тиков 2,0 × 29/30 ≈ 1,93 м.
            double walked = FixVec2.Distance(from, sim.Entities.Position[Boss]).ToDouble();
            Assert.That(walked, Is.InRange(1.85, 2.01), "ход ~2,0 м/с");
            // Лапа не достаёт 45 тиков подряд — «кайтит» (ревью 02.10, ночь, дальники): ход ×1,4 = 2,8 м/с.
            while (sim.TryGetThicketMasterMemory(Boss, out var m) && m.OutOfReachTicks < Simulation.ThicketKiteTicks + 3)
            {
                HeroInFront(sim, 8.5);
                sim.Step(InputFrame.Empty);
            }
            HeroInFront(sim, 8.5);
            from = sim.Entities.Position[Boss];
            for (int k = 0; k < 10; k++) { HeroInFront(sim, 8.5); sim.Step(InputFrame.Empty); }
            walked = FixVec2.Distance(from, sim.Entities.Position[Boss]).ToDouble();
            Assert.That(walked, Is.EqualTo(2.8 / 3).Within(0.02), "ход 2,8 м/с");
            Assert.That(Simulation.ThicketMasterMoveSpeed.ToDouble() * Simulation.ThicketFarWalkPercent / 100, Is.EqualTo(2.8).Within(1e-6));

            // Дальняя полоса — сразу 2,8 м/с, даже без 1,5 с «кайта».
            var far = Arena(distance: 3, walks: true);
            MeleeOnly(far);
            Until(far, IntroDone);
            HeroInFront(far, 12);
            far.Step(InputFrame.Empty);
            Assert.AreEqual(ThicketBand.Far, far.ThicketHeroBand(Boss));
            for (int k = 0; k < 3; k++) { HeroInFront(far, 12); far.Step(InputFrame.Empty); }
            from = far.Entities.Position[Boss];
            for (int k = 0; k < 10; k++) { HeroInFront(far, 12); far.Step(InputFrame.Empty); }
            Assert.That(FixVec2.Distance(from, far.Entities.Position[Boss]).ToDouble(), Is.EqualTo(2.8 / 3).Within(0.02),
                "дальний герой — 2,8 м/с");
        }

        [Test]
        public void Rump_PushesTheHeroOutFromUnderTheBoss()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            // Круг корпуса 4 — левое бедро (Simulation.ForestBoss.Hull).
            sim.Entities.Position[0] = sim.ThicketHullCircleCenter(Boss, 4) + At(0, 0.1);
            for (int k = 0; k < 10; k++) sim.Step(InputFrame.Empty);
            double gap = (sim.ThicketHullGap(Boss, sim.Entities.Position[0]) - sim.Entities.BodyRadius[0]).ToDouble();
            Assert.That(gap, Is.GreaterThanOrEqualTo(-0.01), "герой выдавлен из-под крупа");
        }

        [Test]
        public void Rump_PushesTheHeroOut_EvenWhileTheBossSleeps()
        {
            var sim = Arena();
            sim.Entities.Position[0] = sim.ThicketHullCircleCenter(Boss, 4) + At(0, 0.1);
            for (int k = 0; k < 10; k++) sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.ThicketMasterAwake(Boss), "ещё спит");
            double gap = (sim.ThicketHullGap(Boss, sim.Entities.Position[0]) - sim.Entities.BodyRadius[0]).ToDouble();
            Assert.That(gap, Is.GreaterThanOrEqualTo(-0.01), "круп спящего — тоже тело");
        }

        [Test]
        public void Body_HeroWalkingIntoTheBoss_StaysOutside_DashPassesThrough()
        {
            // Общий SeparateBodies держит тело 0,05 м за тик, герой идёт 0,15: без своего
            // толчка тела герой заходил в босса (проверка 02.10 — каждое пятое начало лапы и топота).
            var sim = Arena();
            MeleeOnly(sim);
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            Until(sim, IntroDone);
            var e = sim.Entities;
            // Корпус (Simulation.ForestBoss.Hull): зазор между телом героя и корпусом.
            double Gap() => (sim.ThicketHullGap(Boss, e.Position[0]) - e.BodyRadius[0]).ToDouble();
            double nearest = double.MaxValue;
            for (int k = 0; k < 60; k++)
            {
                var walk = InputFrame.Empty;
                walk.Flags = (byte)InputFlags.MoveOrder;
                walk.Aim = e.Position[Boss];
                sim.Step(walk);
                nearest = System.Math.Min(nearest, Gap());
            }
            Assert.That(nearest, Is.GreaterThanOrEqualTo(-0.001), "в корпус не заходит");

            // Рывок сквозь босса от груди: корпус его не держит, встаёт на полной дальности за спиной.
            HeroInFront(sim, 3.2);
            var from = e.Position[0];
            var through = e.Position[Boss] - from;
            var dash = InputFrame.Empty;
            dash.Flags = (byte)InputFlags.MoveOrder;
            dash.AbilityMask = (byte)(1 << PelagKit.DashSlot);
            dash.Aim = from + through * Fix64.FromInt(3);
            sim.Step(dash);
            for (int k = 0; k < 6; k++) sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.PelagDash.CutShort, "рывок не сорван телом");
            Assert.That(FixVec2.Distance(from, sim.PelagDash.StoppedAt).ToDouble(), Is.GreaterThan(3.9), "рывок на полную дальность");
            Assert.That(FixVec2.Dot(sim.PelagDash.StoppedAt - e.Position[Boss], through).ToDouble(), Is.GreaterThan(0), "за спиной босса");
            for (int k = 0; k < 10; k++) sim.Step(InputFrame.Empty);
            Assert.That(Gap(), Is.GreaterThanOrEqualTo(-0.001), "после рывка корпус выводит наружу");
            Assert.That(FixVec2.Dot(e.Position[0] - e.Position[Boss], through).ToDouble(), Is.GreaterThan(0),
                "насквозь — за спину, а не назад к груди");
        }

        // ---------- Песочные Часы ----------

        [Test]
        public void Hourglass_DoesNotStun_ShiftsTimersBySixtyTicks()
        {
            var sim = Arena();
            sim.SetArtifact(RunArtifact.Hourglass);
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Until(sim, paw.StartTick + 10);
            int health = sim.Entities.Health[0];
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
            Assert.IsTrue(sim.TimeStopped);
            Assert.AreEqual(0, sim.Statuses.StunUntilTick[Boss], "босса Часы не оглушают");
            foreach (var e in sim.Events)
                Assert.IsFalse(e.Type == SimEventType.Stun && e.Target == Boss, "события оглушения босса нет");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var shifted));
            Assert.AreEqual(paw.Serial, shifted.Serial, "действие не сорвано");
            Assert.AreEqual(paw.ImpactTick + Simulation.ThicketHourglassShiftTicks, shifted.ImpactTick);
            Assert.AreEqual(paw.EndTick + Simulation.ThicketHourglassShiftTicks, shifted.EndTick);
            Assert.AreEqual(Simulation.ThicketHourglassShiftTicks - 1, sim.ThicketMasterFrozenTicksLeft(Boss));
            Assert.IsTrue(MarkOf(sim, shifted.TelegraphSerial, out var mark));
            Assert.AreEqual(shifted.ImpactTick, mark.ImpactTick, "метка ждёт вместе с боссом");
            Assert.IsTrue(mark.IsActive);

            Until(sim, paw.ImpactTick + 1);
            Assert.AreEqual(health, sim.Entities.Health[0], "в старый тик контакта удара нет");
            Until(sim, shifted.ImpactTick + 1);
            Assert.AreEqual(23, health - sim.Entities.Health[0], "лапа легла на 60 тиков позже");
        }

        [Test]
        public void Hourglass_FrozenTicksDoNotFillTheStompWindow()
        {
            var sim = Arena();
            sim.SetArtifact(RunArtifact.Hourglass);
            Until(sim, IntroDone);
            HeroInFront(sim, 3.85);
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
            int frozen = sim.ThicketMasterFrozenTicksLeft(Boss);
            Assert.That(frozen, Is.GreaterThan(0));
            for (int k = 0; k < frozen; k++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, sim.ThicketMasterNearTicks(Boss), "тики под Часами в окно топота не идут");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp));
        }

        [Test]
        public void Hourglass_InRecovery_ShiftsTheStance_NotThePastContact()
        {
            // Стойка после выхода из нырка (36 тиков).
            var sim = Arena(distance: 9);
            sim.SetArtifact(RunArtifact.Hourglass);
            StartDive(sim, out var dive);
            Until(sim, dive.ImpactTick + 3);
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss));
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
            Assert.IsTrue(sim.TimeStopped);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var shifted));
            Assert.AreEqual(dive.Serial, shifted.Serial);
            Assert.AreEqual(dive.ImpactTick, shifted.ImpactTick, "прошедший контакт не сдвигается");
            Assert.AreEqual(dive.EndTick + Simulation.ThicketHourglassShiftTicks, shifted.EndTick, "стойка ждёт вместе с боссом");
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss), "прошедший удар жетон не держит");
        }

        [Test]
        public void Hourglass_EndsWhileBurrowed_HeldDamageLandsOnTheEmerge()
        {
            // Урон под Часами копится; если Часы кончились, пока босс в нырке, он не
            // пропадает и не бьёт бугор, а приходит в тик выхода.
            var sim = Arena(distance: 9);
            sim.SetArtifact(RunArtifact.Hourglass);
            Only(sim);
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            Until(sim, IntroDone);
            var e = sim.Entities;
            // Нырок — только по дальнему герою (ревью 02.10, ночь): 2 с в дальней полосе набираются до Часов;
            // под Часами босс стоит, и счёт не идёт ни вверх, ни вниз.
            for (int k = 0; k < Simulation.ThicketDiveFarTicks + 5; k++) { HeroInFront(sim, 10); sim.Step(InputFrame.Empty); }
            Assert.That(sim.ThicketMasterFarTicks(Boss), Is.GreaterThanOrEqualTo(Simulation.ThicketDiveFarTicks));
            int start = e.Health[Boss];
            var use = InputFrame.Empty;
            use.Flags = (byte)InputFlags.UseArtifact;
            sim.Step(use);
            Assert.IsTrue(sim.TimeStopped);
            int release = sim.ArtifactActiveUntil;
            var hit = InputFrame.Empty;
            hit.Flags = (byte)InputFlags.Attack;
            hit.AttackTarget = Boss;
            for (int k = 0; k < 58; k++)
            {
                e.Position[0] = e.Position[Boss] + At(-1.6, 0);
                hit.Aim = e.Position[Boss];
                sim.Step(hit);
            }
            Assert.AreEqual(start, e.Health[Boss], "под Часами урон копится");
            // Герой ушёл далеко: босс ныряет за 20 тиков до конца Часов (под землёй 54 тика) — Часы
            // кончаются под землёй.
            int diveAt = -1;
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Dive, release - 20);
            while (sim.Tick < release && diveAt < 0)
            {
                e.Position[0] = e.Position[Boss] + At(-10, 0);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketDive && ev.Amount == 0)
                        diveAt = tick;
            }
            Assert.That(diveAt, Is.GreaterThanOrEqualTo(0), "нырок начался до конца Часов");
            Until(sim, release + 1);
            Assert.IsFalse(sim.TimeStopped);
            Assert.IsTrue(sim.ThicketShielded(Boss), "Часы кончились под землёй");
            Assert.AreEqual(start, e.Health[Boss], "по бугру накопленное не бьёт");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            Until(sim, dive.ImpactTick);
            sim.Step(InputFrame.Empty);
            int landed = 0;
            foreach (var ev in sim.Events)
                if (ev.Type == SimEventType.Damage && ev.Target == Boss) landed += ev.Amount;
            Assert.That(landed, Is.GreaterThan(0), "накопленное пришло в тик выхода");
            Assert.AreEqual(start - landed, e.Health[Boss]);
        }

        // ---------- нырок в корни ----------

        /// <summary>
        /// Будит героем в 9 м (стенд будит ближе 9), с конца вступления герой стоит в 9,5 м — в дальней
        /// полосе (6,76 м от груди): нырок — через ThicketDiveFarTicks (2 с) подряд в ней.
        /// </summary>
        private static int StartDive(Simulation sim, out ThicketMasterState dive)
        {
            // Касты не мешают: герой с начала вступления вне лапы — иначе первыми «Корни-плеть» (фаза 1).
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            HeroInFront(sim, 9.5);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400);
            Assert.AreEqual(IntroDone + Simulation.ThicketDiveFarTicks - 1, start, "нырок — после 2 с в дальней полосе");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out dive));
            Assert.AreEqual(ThicketMasterAction.Dive, dive.Action);
            return start;
        }

        [Test]
        public void Dive_FarHero_Burrow12_MoundTravelsAtSevenMetresASecond_CircleLocksUnderHim_Emerges24Later_Stands36()
        {
            var sim = Arena(distance: 9);
            int start = StartDive(sim, out var dive);
            var home = sim.Entities.Position[Boss];
            // Владелец 08.10: «само перемещение под землёй оч быстрое» — бугор по пути на 7 м/с: 9,5 м — 41 тик
            // (было 18 на любой путь, до 16,5 м/с; ревью 02.10, ночь — 30 → 18).
            int travel = Simulation.ThicketDiveTravelOf(dive);
            Assert.AreEqual(41, travel, "9,5 м на 7 м/с");
            Assert.AreEqual(start + 12 + travel + 24, dive.ImpactTick);
            Assert.AreEqual(start + 12, Simulation.ThicketDiveBurrowEndTick(dive), "бугор трогается через 12 тиков ухода");
            Assert.AreEqual(start + Simulation.ThicketDiveCooldownPhase1Ticks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Dive),
                "перезарядка нырка фазы 1 — 12 с");
            Assert.AreEqual(0, sim.ThicketMasterFarTicks(Boss), "счёт дальней полосы — заново");
            Assert.AreEqual(dive.ImpactTick + 36, dive.EndTick, "стойка 36 (владелец 02.10: окно для атаки, было 24)");
            Assert.AreEqual(1, sim.BigMarkLoad(out _), "место круга — с начала ухода");
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss));
            Assert.IsTrue(sim.ThicketShielded(Boss), "неуязвим с начала ухода");

            Until(sim, start + 12);
            Assert.IsFalse(sim.ThicketUnderground(Boss), "уходит 12 тиков");
            Assert.AreEqual(home, sim.Entities.Position[Boss], "уходит на месте");
            Assert.AreEqual(EnemyArchetypes.ThicketMasterBodyRadius, sim.Entities.BodyRadius[Boss]);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.ThicketUnderground(Boss));
            Assert.AreEqual(Simulation.ThicketMoundRadius, sim.Entities.BodyRadius[Boss], "под землёй тело меньше");

            while (sim.Tick < start + 12 + travel)
            {
                FixVec2 was = sim.Entities.Position[Boss];
                sim.Step(InputFrame.Empty);
                Assert.That(FixVec2.Distance(was, sim.Entities.Position[Boss]).ToDouble(), Is.LessThanOrEqualTo(7.0 / 30 + 1e-4),
                    "бугор к стоящему герою — не быстрее 7 м/с, тик " + (sim.Tick - 1));
            }
            Assert.That(FixVec2.Distance(sim.Entities.Position[Boss], sim.Entities.Position[0]).ToDouble(), Is.LessThan(1.0),
                "бугор доехал к герою (7 м/с)");
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var locked));
            Assert.AreEqual(2, locked.Stage);
            Assert.IsTrue(MarkOf(sim, locked.TelegraphSerial, out var circle), "круг нырка на земле");
            Assert.AreEqual(TelegraphShape.Circle, circle.Shape);
            Assert.IsTrue(circle.SharedView);
            Assert.AreEqual(Simulation.ThicketDiveRadius, circle.Radius);
            Assert.AreEqual(dive.ImpactTick, circle.ImpactTick, "24 тика от круга до выхода");
            Assert.AreEqual(sim.Entities.Position[0], circle.Origin, "круг — под героем");

            int health = sim.Entities.Health[0];
            Until(sim, dive.ImpactTick);
            Assert.AreEqual(health, sim.Entities.Health[0], "до выхода урона нет");
            Assert.IsTrue(sim.ThicketUnderground(Boss));
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketDive, true));
            Assert.AreEqual(37, sim.ThicketDiveDamageOf(Boss), "нырок на арене 9: 23 × 66/41");
            Assert.AreEqual(37, health - sim.Entities.Health[0]);
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, 0), "подброс без контроля");
            Assert.IsFalse(sim.ThicketUnderground(Boss));
            Assert.IsFalse(sim.ThicketShielded(Boss), "вылез — снова бьётся");
            Assert.AreEqual(EnemyArchetypes.ThicketMasterBodyRadius, sim.Entities.BodyRadius[Boss], "вылез — тело своё");
            Assert.That(FixVec2.Distance(sim.Entities.Position[Boss], circle.Origin).ToDouble(), Is.LessThan(0.05),
                "вылез в центре круга");
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss));

            Until(sim, dive.EndTick);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out _), "стоит 36 тиков после выхода");
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _));
        }

        /// <summary>
        /// Владелец 08.10: «само перемещение под землёй оч быстрое… холмик просто скользит по полу». Ход бугра —
        /// путь на 7 м/с: короткий нырок «под героя» (герой в 3 м) — прежние 18 тиков; путь 20 м (босс у края
        /// поводка, герой за противоположным краем) — потолок 60 тиков (2 с, 10 м/с); герой, убегающий от бугра,
        /// догоняется не быстрее 0,4 м за тик (12 м/с), и босс вылезает в своём круге.
        /// </summary>
        [Test]
        public void DiveTravel_GrowsWithThePath_ShortUnderHeroStays18_LongCapped60_FleeingHeroCaughtAtTwelveMetresASecond()
        {
            // Короткий «под героя».
            var near = Arena(distance: 3);
            Only(near, ThicketMasterAction.Dive);
            near.SetThicketReadyTick(Boss, ThicketMasterAction.Dive, int.MaxValue / 2);
            NoStompPick(near);
            Until(near, IntroDone);
            near.SetThicketDiveDueTick(Boss, near.Tick);
            int start = -1;
            for (int k = 0; k < 120 && start < 0; k++)
            {
                HeroInFront(near, 3);   // рёв вступления отбросил героя — ставим обратно
                int tick = near.Tick;
                near.Step(InputFrame.Empty);
                if (Count(near, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive) > 0) start = tick;
            }
            Assert.AreNotEqual(-1, start, "нырок «под героя»");
            Assert.IsTrue(near.TryGetThicketMasterAction(Boss, out var under));
            Assert.IsTrue(Simulation.ThicketDiveUnderHero(under));
            Assert.AreEqual(Simulation.ThicketDiveTravelTicks, Simulation.ThicketDiveTravelOf(under), "3 м — прежние 18 тиков");
            Assert.AreEqual(start + 12 + 18 + 24, under.ImpactTick, "под землёй 54, как было");

            // Длинный: 20 м — потолок 60.
            var far = Arena(distance: 9);
            Only(far, ThicketMasterAction.Dive);
            Until(far, IntroDone);
            Assert.IsTrue(far.TryGetThicketMasterMemory(Boss, out var memory));
            far.Entities.Position[Boss] = memory.Home + At(10, 0);
            FixVec2 spot = memory.Home + At(-12, 0);
            int longStart = -1;
            for (int k = 0; k < 200 && longStart < 0; k++)
            {
                far.Entities.Position[0] = spot;
                int tick = far.Tick;
                far.Step(InputFrame.Empty);
                if (Count(far, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive) > 0) longStart = tick;
            }
            Assert.AreNotEqual(-1, longStart, "нырок-сближение");
            Assert.IsTrue(far.TryGetThicketMasterAction(Boss, out var longDive));
            Assert.AreEqual(Simulation.ThicketDiveTravelMaxTicks, Simulation.ThicketDiveTravelOf(longDive), "20 м — потолок 60 тиков");
            double fastest = 0;
            while (far.Tick < longDive.ImpactTick)
            {
                far.Entities.Position[0] = spot;
                FixVec2 was = far.Entities.Position[Boss];
                far.Step(InputFrame.Empty);
                fastest = System.Math.Max(fastest, Metres(was, far.Entities.Position[Boss]));
            }
            Assert.That(fastest, Is.LessThanOrEqualTo(20.0 / 60 + 1e-3), "стоящего героя бугор везёт ровно: 20 м за 60 тиков");

            // Убегающий: каждый тик 0,15 м прочь (4,5 м/с) — бугор догоняет не быстрее 12 м/с, вылезает в своём круге.
            var flee = Arena(distance: 9);
            Only(flee, ThicketMasterAction.Dive);
            int fleeStart = StartDive(flee, out var fleeDive);
            FixVec2 hero = flee.Entities.Position[0];
            FixVec2 away = (hero - flee.Entities.Position[Boss]).Normalized();
            double top = 0;
            while (flee.Tick < fleeDive.ImpactTick - Simulation.ThicketDiveLockTicks)
            {
                hero = hero + away * Fix64.Ratio(15, 100);
                flee.Entities.Position[0] = hero;
                FixVec2 was = flee.Entities.Position[Boss];
                flee.Step(InputFrame.Empty);
                top = System.Math.Max(top, Metres(was, flee.Entities.Position[Boss]));
            }
            Assert.That(top, Is.LessThanOrEqualTo(0.4 + 1e-4), "догоняет не быстрее 0,4 м за тик");
            Until(flee, fleeDive.ImpactTick + 1);
            Assert.IsTrue(flee.TryGetThicketMasterAction(Boss, out var exit));
            Assert.That(Metres(flee.Entities.Position[Boss], exit.Target), Is.LessThanOrEqualTo(4.0), "вылез в своём круге");
            Assert.Greater(fleeStart, 0);
        }

        [Test]
        public void Dive_HeroWhoLeftTheCircle_TakesNothing()
        {
            var sim = Arena(distance: 9);
            StartDive(sim, out var dive);
            Until(sim, dive.ImpactTick - Simulation.ThicketDiveLockTicks + 1);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var locked));
            Assert.IsTrue(MarkOf(sim, locked.TelegraphSerial, out var circle));
            sim.Entities.Position[0] = circle.Origin + At(0, 5);
            int health = sim.Entities.Health[0];
            Assert.AreEqual(0, StepCounting(sim, dive.ImpactTick + 1, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketDive, true));
            Assert.AreEqual(health, sim.Entities.Health[0]);
        }
        [Test]
        public void Dive_ShieldedFromGoingUnderToEmerge_NoDamageFromAbilitiesDoTsBurnOrSabre_NotATarget()
        {
            var sim = Arena(distance: 9);
            sim.SetAbility(1, PelagKit.PoolDefinition(3), new AbilityNode[0], 0);   // Шаг по цепи: цель — враг
            int start = StartDive(sim, out var dive);
            int health = sim.Entities.Health[Boss];
            Assert.IsTrue(sim.ThicketShielded(Boss), "уход — уже неуязвим");
            Assert.IsFalse(sim.ThicketUnderground(Boss), "но ещё не бугор");
            sim.ApplyAbilityDamage(0, Boss, 200, -1, DamageType.Physical);
            sim.ApplyAbilityDamage(0, Boss, 50, -1, DamageType.Fire, overTime: true);
            Assert.AreEqual(health, sim.Entities.Health[Boss], "способность и урон по времени не проходят");
            sim.Statuses.ApplyBurn(Boss, Fix64.FromInt(10), 300, 0, -1);
            var attack = new InputFrame { Flags = (byte)InputFlags.Attack, AttackTarget = Boss, AbilityTarget = -1 };
            bool targetRefused = false;
            while (sim.Tick <= dive.ImpactTick)
            {
                attack.Aim = sim.Entities.Position[Boss];
                sim.Step(attack);
                if (sim.Tick <= dive.ImpactTick)
                {
                    Assert.AreEqual(health, sim.Entities.Health[Boss], "под землёй урона нет, тик " + (sim.Tick - 1));
                    Assert.IsFalse(sim.Statuses.IsBurning(Boss), "под землёй огонь гаснет");
                    Assert.IsTrue(sim.ThicketShielded(Boss));
                    if (FixVec2.Distance(sim.Entities.Position[Boss], sim.Entities.Position[0]).ToDouble() < 2)
                    {
                        Assert.IsFalse(sim.ValidAbilityTarget(Boss, sim.GetAbility(1)), "бугор — не цель способности");
                        targetRefused = true;
                    }
                }
            }
            Assert.IsTrue(targetRefused, "бугор доезжал к герою");
            Assert.IsFalse(sim.ThicketShielded(Boss), "вылез");
            Assert.IsTrue(sim.ValidAbilityTarget(Boss, sim.GetAbility(1)), "вылез — снова цель");
            // Тот же удар саблей после выхода — бьёт: путь урона в тесте живой.
            for (int k = 0; k < 60 && sim.Entities.Health[Boss] == health; k++)
            {
                attack.Aim = sim.Entities.Position[Boss];
                sim.Step(attack);
            }
            Assert.Less(sim.Entities.Health[Boss], health, "после выхода сабля бьёт");
            sim.ApplyAbilityDamage(0, Boss, 200, -1, DamageType.Physical);
            Assert.IsTrue(sim.Entities.Alive[Boss]);
        }

        [Test]
        public void Dive_ShieldedBody_DoesNotPushTheHero_NorBlockThePassage()
        {
            var sim = Arena(distance: 9);
            int start = StartDive(sim, out _);
            var boss = sim.Entities.Position[Boss];
            // Герой стоит в теле уходящего босса и под крупом: не выталкивается.
            var inside = boss + At(-0.6, 0);
            sim.Entities.Position[0] = inside;
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(inside, sim.Entities.Position[0], "тело в нырке не расталкивает");
            // Корень хвоста (круг корпуса 6): корпус в нырке не держит.
            var rump = sim.ThicketHullCircleCenter(Boss, 6);
            sim.Entities.Position[0] = rump;
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(rump, sim.Entities.Position[0], "круп в нырке не выталкивает");
        }

        [Test]
        public void Dive_HeroBeyondTheLeash_CircleOnTheEdgeOfReach_BossEmergesInsideIt_OnTheLeash()
        {
            // Фаза 1, герой в 16,5 м от точки появления — дальше поводка (10) и круга (4,0):
            // раньше босс тут стоял, а герой бил его издали без риска.
            var sim = Arena(distance: 9);
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            var spot = memory.Home + At(-16.5, 0);
            sim.Entities.Position[0] = spot;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400), "за поводком герой тоже под нырком");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            int health = sim.Entities.Health[0];
            while (sim.Tick <= dive.ImpactTick)
            {
                sim.Entities.Position[0] = spot;
                sim.Step(InputFrame.Empty);
                Assert.That(Metres(sim.Entities.Position[Boss], memory.Home), Is.LessThanOrEqualTo(10.01), "бугор — в поводке");
            }
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var exit));
            Assert.That(Metres(exit.Target, memory.Home), Is.LessThanOrEqualTo(14.01), "круг — не дальше поводка и круга");
            Assert.That(Metres(sim.Entities.Position[Boss], exit.Target), Is.LessThanOrEqualTo(4.0), "вылез в своём круге");
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketDive, true), "достал героя");
            Assert.AreEqual(37, health - sim.Entities.Health[0], "нырок на арене 9: 23 × 66/41");
        }

        [Test]
        public void Dive_HeroJustPastPawReachOfTheLeashedBoss_IsDivedEveryCooldown()
        {
            // Фаза 1, герой в 14,1 м от точки появления: босс дошёл до края поводка
            // (4,1 м от героя — лапа 3,68 не достаёт) и раньше стоял, пока герой бил его ударом
            // якоря. Пешком не достать — для нырка это дальняя полоса: раз в перезарядку (фаза 1 — 18 с,
            // баланс 02.10, ночь; было 12).
            var sim = Arena(distance: 9, walks: true);
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            var spot = memory.Home + At(-14.1, 0);
            int health = sim.Entities.Health[0], dives = 0, hits = 0;
            for (int k = 0; k < 1200; k++)
            {
                sim.Entities.Position[0] = spot;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Source != Boss || e.ActionVariant != (int)EnemyActionKind.ThicketDive) continue;
                    if (e.Type == SimEventType.EnemyActionStarted && e.Amount == 0) dives++;
                    if (e.Type == SimEventType.EnemyActionImpact && e.Flag) hits++;
                }
            }
            Assert.That(dives, Is.GreaterThanOrEqualTo(3), "ныряет раз в перезарядку (18 с), а не стоит у края");
            Assert.That(hits, Is.GreaterThanOrEqualTo(2));
            Assert.Less(sim.Entities.Health[0], health);
        }

        [Test]
        public void Dive_RealBossMaps_BossEmergesOnTheFloor_InsideItsCircle()
        {
            // Арены 9 леса, 6 сидов, точки героя в 9,5–17 м от точки появления по 16
            // направлениям (и за поводком; нырок — только по дальней полосе, от 6,5 м за
            // кромкой корпуса) и до 8 точек за деревьями и стенами — от точки появления по
            // прямой бугру не пройти. Герой стоит до выхода.
            // Поляна босса с 02.10 одна и та же и ровная (20,98 × 15,74 м с ревью 02.10, без озера):
            // препятствия — два тестовых дерева в 4,5 м от точки появления, по сиду
            // в разные стороны.
            var location = ArenaEncounterTests.ForestLocation();
            var body = EnemyArchetypes.ThicketMasterBodyRadius;
            int dives = 0, hits = 0, behind = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var map = ArenaEncounterTests.ArenaMap(location, 9, seed);
                var probe = new Simulation(78, 64);
                probe.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, map, seed, arena: 9);
                var home = probe.Entities.Position[Boss];
                var heroBody = probe.Entities.BodyRadius[0];
                for (int tree = 0; tree < 2; tree++)
                {
                    double treeAngle = ((int)seed * 50 + tree * 180) * System.Math.PI / 180;
                    map.AddTestObstacle(new LayoutObstacle(home + At(System.Math.Cos(treeAngle) * 4.5,
                        System.Math.Sin(treeAngle) * 4.5), Fix64.One, 0));
                }
                var spots = new List<FixVec2>();
                for (int k = 0; k < 16; k++)
                {
                    double angle = k * System.Math.PI / 8, distance = 9.5 + k % 4 * 2.5;
                    spots.Add(home + At(System.Math.Cos(angle) * distance, System.Math.Sin(angle) * distance));
                }
                for (int k = 0, extra = 0; k < 64 && extra < 8; k++)
                {
                    double angle = (k / 2) * System.Math.PI / 16, distance = 9.5 + k % 2 * 2.5;
                    var p = home + At(System.Math.Cos(angle) * distance, System.Math.Sin(angle) * distance);
                    if (!map.IsWalkable(p, heroBody) || map.CanTravel(home, p, Simulation.ThicketMoundRadius)) continue;
                    spots.Add(p);
                    extra++;
                }
                for (int spot = 0; spot < spots.Count; spot++)
                {
                    var hero = spots[spot];
                    if (!map.IsWalkable(hero, heroBody)) continue;
                    var sim = new Simulation(78, 64);
                    sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, map, seed, arena: 9);
                    sim.PlayerInvulnerable = true;
                    sim.Entities.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                    sim.Entities.RefreshStats(Boss);
                    Only(sim, ThicketMasterAction.Dive);
                    Until(sim, IntroDone);
                    Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
                    // С деревьями стенд ставит босса иначе, чем пробный (без них): точка ближе 9,5 м к нему —
                    // не дальняя полоса, нырка по ней нет.
                    if (Metres(hero, sim.Entities.Position[Boss]) < 9.5) continue;
                    if (!map.CanTravel(memory.Home, hero, Simulation.ThicketMoundRadius)) behind++;
                    sim.Entities.Position[0] = hero;
                    string where = "сид " + seed + ", точка " + spot;
                    Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400), "нырок, " + where);
                    Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
                    bool hit = false;
                    while (sim.Tick <= dive.ImpactTick)
                    {
                        sim.Entities.Position[0] = hero;
                        sim.Step(InputFrame.Empty);
                        hit |= Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketDive, true) > 0;
                    }
                    Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var exit), where);
                    var boss = sim.Entities.Position[Boss];
                    dives++;
                    if (hit) hits++;
                    Assert.IsTrue(map.IsWalkable(boss, body), "вылез на пол — тело помещается, " + where);
                    Assert.AreEqual(body, sim.Entities.BodyRadius[Boss]);
                    Assert.That(Metres(boss, exit.Target), Is.LessThanOrEqualTo(4.0), "вылез в своём круге, " + where);
                    Assert.That(Metres(boss, memory.Home), Is.LessThanOrEqualTo(10.01), "в поводке, " + where);
                    Assert.That(Metres(exit.Target, memory.Home), Is.LessThanOrEqualTo(14.01), where);
                }
            }
            TestContext.WriteLine("dives " + dives + ", hits " + hits + ", hero behind obstacles " + behind);
            Assert.That(dives, Is.GreaterThan(25));
            Assert.That(behind, Is.GreaterThan(10), "проверены и точки за препятствиями");
        }

        // ---------- прорастание ----------

        [Test]
        public void Sprout_Gesture18_ThenSixCirclesEveryNineOnTheirOwn_EachHitsOnceThirtyAfterItsMark()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Sprout);
            ToPhase(sim, 60);
            Assert.AreEqual(2, sim.ThicketMasterPhase(Boss));
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketSprout, 30);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var sprout));
            Assert.AreEqual(ThicketMasterAction.Sprout, sprout.Action);
            Assert.AreEqual(1, sprout.Stages);
            Assert.IsTrue(sprout.HitResolved, "жест без контакта");
            Assert.AreEqual(start + Simulation.ThicketCastGestureTicks, sprout.EndTick, "жест 18 тиков");
            Assert.That(Simulation.ThicketCastGestureTicks, Is.LessThanOrEqualTo(20));
            Assert.IsTrue(sim.TryGetThicketHazard(Boss, out var hazard), "опасность идёт сама");
            Assert.AreEqual(ThicketMasterAction.Sprout, hazard.Action);
            Assert.AreEqual(6, hazard.Stages);
            Assert.AreEqual(1, hazard.Stage, "первый круг — в тик каста");
            Assert.AreEqual(start + 30, hazard.ImpactTick);
            Assert.AreEqual(start + 5 * 9 + 30, hazard.LastImpactTick);
            Assert.AreEqual(2, sim.BigMarkLoad(out _), "вес — 2");
            Assert.IsTrue(sim.ThicketHazardActive(Boss));
            var hero = sim.Entities.Position[0];
            int health = sim.Entities.Health[0];

            var impacts = new List<int[]>();
            var marks = new List<int[]>();
            for (int k = 0; k < Simulation.ThicketSproutCircles; k++)
            {
                while (sim.Tick < start + 9 * k + 1)
                {
                    int tick = sim.Tick;
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                    {
                        if (e.ActionVariant != (int)EnemyActionKind.ThicketSprout) continue;
                        if (e.Type == SimEventType.EnemyActionImpact) impacts.Add(new[] { tick, e.Amount, e.Flag ? 1 : 0 });
                        if (e.Type == SimEventType.EnemyActionStarted) marks.Add(new[] { tick, e.Amount });
                    }
                }
                Assert.IsTrue(sim.TryGetThicketShape(Boss, k, out var center, out int impact, out _), "круг " + k);
                Assert.AreEqual(hero, center, "круг под героем");
                Assert.AreEqual(start + 9 * k + 30, impact, "удар через 30 тиков после метки, круг " + k);
                Assert.IsFalse(sim.TryGetThicketShape(Boss, k + 1, out _, out _, out _), "следующий — через 9 тиков");
            }
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "жест кончился, круги встают без босса");
            Assert.AreEqual(5, marks.Count, "знак каждого круга с 1-го");
            for (int k = 0; k < marks.Count; k++) { Assert.AreEqual(k + 1, marks[k][1]); Assert.AreEqual(start + 9 * (k + 1), marks[k][0]); }
            StepRecording(sim, hazard.LastImpactTick + 1, EnemyActionKind.ThicketSprout, impacts);
            Assert.AreEqual(6, impacts.Count, "каждый круг — один контакт");
            for (int k = 0; k < impacts.Count; k++)
            {
                Assert.AreEqual(k, impacts[k][1]);
                Assert.AreEqual(start + 9 * k + 30, impacts[k][0], "удар круга " + k);
                Assert.AreEqual(1, impacts[k][2], "стоящего задевает каждый");
            }
            Assert.AreEqual(25, sim.ThicketSproutDamageOf(Boss), "23 × 45/41 (баланс 02.10, ночь: доля 34 → 45; лапа 23 — перебаланс 03.10)");
            Assert.AreEqual(6 * 25, health - sim.Entities.Health[0]);
            Assert.AreEqual(0, sim.HeroRootTicksLeft, "без корней");
            Assert.IsFalse(sim.TryGetThicketHazard(Boss, out _), "всё отбито — опасность снята");
            Assert.IsFalse(sim.ThicketHazardActive(Boss));
            Assert.AreEqual(0, sim.BigMarkLoad(out _));
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 5, out _, out _, out bool resolved), "круги остаются виду");
            Assert.IsTrue(resolved);
        }

        [Test]
        public void Sprout_GestureEnds_RestOfPhaseTwo()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Sprout);
            ToPhase(sim, 60);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketSprout, 30);
            Until(sim, start + Simulation.ThicketCastGestureTicks + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(start + Simulation.ThicketCastGestureTicks + Simulation.ThicketRestPhase2Ticks, memory.NextActionTick);
        }

        [Test]
        public void Sprout_Hourglass_PendingCirclesAndNextMarkWaitSixtyTicks()
        {
            var sim = Arena(distance: 6);
            sim.SetArtifact(RunArtifact.Hourglass);
            Only(sim, ThicketMasterAction.Sprout);
            ToPhase(sim, 60);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketSprout, 30);
            Until(sim, start + 10);
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
            Assert.IsTrue(sim.TimeStopped);
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 0, out _, out int first, out _));
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 1, out _, out int second, out _));
            Assert.AreEqual(start + 30 + 60, first);
            Assert.AreEqual(start + 39 + 60, second);
            Until(sim, start + 18 + 60);
            Assert.IsFalse(sim.TryGetThicketShape(Boss, 2, out _, out _, out _), "третий круг ждёт вместе с боссом");
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 2, out _, out int third, out _));
            Assert.AreEqual(start + 18 + 60 + 30, third);
            Assert.AreEqual(0, StepCounting(sim, start + 30 + 60, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketSprout),
                "в старые тики удара нет");
            Assert.AreEqual(1, StepCounting(sim, start + 30 + 60 + 1, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketSprout),
                "первый круг бьёт на 60 тиков позже");
        }

        // ---------- облака пыльцы ----------

        [Test]
        public void Pollen_ThreeCloudsLandAfter24_Slow30Inside_BiteEveryFifteen_EightAtMost()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Pollen);
            ToPhase(sim, 60);
            var hero = sim.Entities.Position[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPollen, 30);
            Assert.AreNotEqual(-1, start);
            Assert.AreEqual(1, sim.BigMarkLoad(out _), "падение — крупная метка весом 1");
            int under = -1, clouds = 0;
            for (int slot = 0; slot < Simulation.ThicketPollenZones; slot++)
            {
                Assert.IsTrue(sim.TryGetThicketPollenZone(slot, out var zone));
                clouds++;
                Assert.AreEqual(start + 24, zone.LandTick);
                Assert.AreEqual(start + 24 + 120, zone.EndTick);
                Assert.AreEqual(Simulation.ThicketPollenRadius, zone.Radius);
                double d = FixVec2.Distance(zone.Center, hero).ToDouble();
                if (d < 1e-6) under = slot;
                else Assert.That(d, Is.EqualTo(3.4).Within(0.01), "два облака рядом");
            }
            Assert.AreEqual(3, clouds);
            Assert.AreNotEqual(-1, under, "одно облако — под героем");

            Until(sim, start + 24);
            Assert.AreEqual(0, sim.HeroSlowPercent, "падающее не замедляет");
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(3, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPollen));
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPollen, true));
            Assert.AreEqual(Simulation.ThicketPollenSlowPercent, sim.HeroSlowPercent, "внутри — замедление 30%");

            int land = start + 24, bites = 0, damage = 0;
            while (sim.Tick < land + 140)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.DamageOverTime && e.Source == Boss && e.Target == 0)
                    {
                        Assert.AreEqual(0, (tick - land) % 15, "укус раз в 15 тиков от падения");
                        bites++; damage += e.Amount;
                    }
                if (tick <= land + 120) Assert.AreEqual(Simulation.ThicketPollenSlowPercent, sim.HeroSlowPercent, "тик " + tick);
            }
            Assert.AreEqual(8, bites, "не больше 8 укусов за облако");
            Assert.AreEqual(3, sim.ThicketPollenDamageOf(Boss), "23 × 6/41 (баланс 02.10, ночь: доля 5 → 6)");
            Assert.AreEqual(8 * 3, damage, "не больше 8 укусов за облако");
            Assert.AreEqual(0, sim.HeroSlowPercent, "облака ушли");
            for (int slot = 0; slot < Simulation.ThicketPollenZones; slot++)
                Assert.IsFalse(sim.TryGetThicketPollenZone(slot, out _));
        }

        [Test]
        public void Pollen_TwoOverlappingClouds_OneBitePerFifteenTicks_LeavingEndsTheSlow()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Pollen);
            ToPhase(sim, 60);
            var hero = sim.Entities.Position[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPollen, 30);
            FixVec2 side = hero;
            for (int slot = 0; slot < Simulation.ThicketPollenZones; slot++)
                if (sim.TryGetThicketPollenZone(slot, out var zone) && !zone.Center.Equals(hero)) side = zone.Center;
            // Посередине между облаком героя и боковым — в обоих.
            sim.Entities.Position[0] = hero + (side - hero) / Fix64.FromInt(2);
            int land = start + 24, last = -1000, bites = 0;
            while (sim.Tick < land + 61)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.DamageOverTime && e.Source == Boss)
                    {
                        Assert.That(tick - last, Is.GreaterThanOrEqualTo(15), "один укус на все облака");
                        last = tick; bites++;
                    }
            }
            Assert.AreEqual(4, bites);
            sim.Entities.Position[0] = hero + At(0, 8);
            sim.Step(InputFrame.Empty);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, sim.HeroSlowPercent, "вышел — замедление кончилось");
            Assert.AreEqual(0, StepCounting(sim, land + 130, SimEventType.DamageOverTime, EnemyActionKind.None),
                "вне облака не жжёт");
        }

        // ---------- ягодный ливень ----------

        [Test]
        public void Rain_FiveVolleysOfFour_TwelveApart_GapsTwoMetres_OneHitPerVolley_OnTheirOwn()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Rain);
            ToPhase(sim, 30);
            Assert.AreEqual(3, sim.ThicketMasterPhase(Boss));
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketRain, 30);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var rain));
            Assert.AreEqual(start + Simulation.ThicketCastGestureTicks, rain.EndTick, "жест 18");
            Assert.IsTrue(sim.TryGetThicketHazard(Boss, out var hazard));
            Assert.AreEqual(5, hazard.Stages, "5 залпов");
            Assert.AreEqual(start + 30, hazard.ImpactTick);
            Assert.AreEqual(start + 4 * 12 + 30, hazard.LastImpactTick);
            Assert.AreEqual(2, sim.BigMarkLoad(out _), "вес — 2");
            var hero = sim.Entities.Position[0];
            int health = sim.Entities.Health[0];
            double minimum = 2 * Simulation.ThicketRainRadius.ToDouble() + Simulation.ThicketRainGap.ToDouble() - 1e-6;
            var impacts = new List<int[]>();
            for (int v = 0; v < Simulation.ThicketRainVolleys; v++)
            {
                StepRecording(sim, start + 12 * v + 1, EnemyActionKind.ThicketRain, impacts);
                var centers = new FixVec2[4];
                for (int k = 0; k < 4; k++)
                {
                    Assert.IsTrue(sim.TryGetThicketShape(Boss, v * 4 + k, out centers[k], out int impact, out _));
                    Assert.AreEqual(start + 12 * v + 30, impact, "залп " + v);
                }
                Assert.AreEqual(hero, centers[0], "один круг — на герое");
                for (int i = 0; i < 4; i++)
                    for (int j = i + 1; j < 4; j++)
                        Assert.That(FixVec2.Distance(centers[i], centers[j]).ToDouble(), Is.GreaterThanOrEqualTo(minimum),
                            "щель ≥ 2 м, залп " + v);
                if (v > 0)
                    Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketRain), "знак залпа " + v);
            }
            StepRecording(sim, hazard.LastImpactTick + 1, EnemyActionKind.ThicketRain, impacts);
            Assert.AreEqual(5, impacts.Count, "по контакту на залп, не на круг");
            for (int v = 0; v < impacts.Count; v++)
            {
                Assert.AreEqual(v, impacts[v][1]);
                Assert.AreEqual(start + 12 * v + 30, impacts[v][0], "удар залпа " + v);
                Assert.AreEqual(1, impacts[v][2]);
            }
            Assert.AreEqual(22, sim.ThicketRainDamageOf(Boss), "23 × 40/41 (баланс 02.10, ночь: доля 30 → 40; лапа 23 — перебаланс 03.10)");
            Assert.AreEqual(5 * 22, health - sim.Entities.Health[0]);
            Assert.IsFalse(sim.TryGetThicketHazard(Boss, out _));
        }

        [Test]
        public void Rain_Template_GapsAtLeastTwoMetres_ForManySeeds()
        {
            double minimum = 2 * Simulation.ThicketRainRadius.ToDouble() + Simulation.ThicketRainGap.ToDouble() - 1e-6;
            var centers = new FixVec2[Simulation.ThicketRainCircles];
            for (ulong seed = 1; seed <= 3000; seed++)
            {
                var rng = new Pcg32(seed, 0x5241494EUL);
                var hero = At((seed % 17) - 8.0, (seed % 11) - 5.0);
                Simulation.ThicketRainTemplate(ref rng, hero, centers);
                Assert.AreEqual(hero, centers[0]);
                for (int i = 0; i < centers.Length; i++)
                    for (int j = i + 1; j < centers.Length; j++)
                        Assert.That(FixVec2.Distance(centers[i], centers[j]).ToDouble(), Is.GreaterThanOrEqualTo(minimum),
                            "сид " + seed + ", круги " + i + "/" + j);
            }
        }

        // ---------- фазы ----------

        [Test]
        public void Phases_DiveFromOne_SproutAndPollenFromTwo_RainOnlyInThree()
        {
            var sim = Arena(distance: 6);
            Until(sim, IntroDone);
            var seen = new HashSet<int>();
            var sproutCircles = new HashSet<int>();
            void Play(int ticks)
            {
                seen.Clear();
                sproutCircles.Clear();
                for (int k = 0; k < ticks; k++)
                {
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.EnemyActionStarted && e.Source == Boss && e.Amount == 0)
                        {
                            seen.Add(e.ActionVariant);
                            if (e.ActionVariant == (int)EnemyActionKind.ThicketSprout && sim.TryGetThicketHazard(Boss, out var h))
                                sproutCircles.Add(h.Stages);
                        }
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                }
            }
            // Нырок — только по дальнему герою (ревью 02.10, ночь): герой в 10 м — в дальней полосе.
            HeroInFront(sim, 10);
            Play(900);
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketDive), "фаза 1: нырок по дальнему герою");
            // Баланс 02.10, ночь: дальнику в фазе 1 — короткие «Корни-плеть» (3 круга), полного прорастания нет.
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketSprout), "фаза 1: «Корни-плеть» по дальнему");
            CollectionAssert.AreEquivalent(new[] { Simulation.ThicketSproutPhase1Circles }, sproutCircles, "фаза 1: только 3 круга");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketPollen), "фаза 1: без пыльцы");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketRain), "фаза 1: без ливня");
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 60 / 100;
            Play(1200);
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketSprout), "фаза 2: прорастание");
            CollectionAssert.AreEquivalent(new[] { Simulation.ThicketSproutCircles }, sproutCircles, "фаза 2: полное — 6 кругов");
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketPollen), "фаза 2: пыльца");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketRain), "фаза 2: без ливня");
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            // Буря фазы 3 — 90 + 75 + 12 тиков (ревью 02.10, вечер): окно наблюдения длиннее (было 1500).
            Play(2400);
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketRain), "фаза 3: ливень");
        }

        // ---------- буря цветения ----------

        /// <summary>Шагает, пока не начнётся буря (Started, Amount 0); тик начала или −1.</summary>
        private static int RunUntilStorm(Simulation sim, int limit, System.Action before = null)
        {
            for (int k = 0; k < limit; k++)
            {
                before?.Invoke();
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketStorm
                        && e.Amount == 0) return tick;
            }
            return -1;
        }

        /// <summary>Вступление, здоровье 30%, рёв на все пороги — и буря в тик его конца. Тик начала бури.</summary>
        private static int ToStorm(Simulation sim, System.Action before = null)
        {
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            Assert.AreEqual(IntroDone, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10), "рёв порога");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            int start = RunUntilStorm(sim, roar.EndTick - sim.Tick + 5, before);
            Assert.AreEqual(roar.EndTick, start, "буря — сразу после рёва на 33%");
            return start;
        }

        /// <summary>Круги света волны wave (места 0–2 или 3–5).</summary>
        private static FixVec2[] StormCircles(Simulation sim, int wave, int boss = Boss)
        {
            var centers = new FixVec2[Simulation.ThicketStormSafeCircles];
            for (int k = 0; k < centers.Length; k++)
                Assert.IsTrue(sim.TryGetThicketShape(boss, wave * Simulation.ThicketStormSafeCircles + k, out centers[k], out _, out _),
                    "круг " + k + " волны " + wave);
            return centers;
        }

        private static List<EnemyTelegraph> SafeMarks(Simulation sim)
        {
            var marks = new List<EnemyTelegraph>();
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.IsActive && (t.Flags & TelegraphFlags.SafeZone) != 0) marks.Add(t);
            return marks;
        }

        private static double Metres(FixVec2 a, FixVec2 b) => FixVec2.Distance(a, b).ToDouble();

        [Test]
        public void Storm_AfterTheThirtyThreeRoar_Waves90And75_BossStands_ThreeSafeCirclesEachWave()
        {
            var sim = Arena();
            Only(sim, ThicketMasterAction.Storm);
            int start = ToStorm(sim);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
            Assert.AreEqual(ThicketMasterAction.Storm, storm.Action);
            // Ревью 02.10, вечер: «сделать бурю дольше по продолжительности» — 60/45 → 90/75.
            Assert.AreEqual(start + 90, storm.ImpactTick, "первая волна через 90");
            Assert.AreEqual(start + 90 + 75, storm.LastImpactTick, "вторая — через 75 после первой");
            Assert.AreEqual(start + 177, storm.EndTick, "буря целиком — 5,9 с");
            Assert.AreEqual(storm.LastImpactTick + Simulation.ThicketStormRecoveryTicks, storm.EndTick);

            var boss = sim.Entities.Position[Boss];
            var facing = sim.Entities.Facing[Boss];
            var hero = sim.Entities.Position[0];
            var marks = SafeMarks(sim);
            Assert.AreEqual(3, marks.Count, "три круга света");
            foreach (var t in marks)
            {
                Assert.AreEqual(TelegraphShape.Circle, t.Shape);
                Assert.AreEqual(Simulation.ThicketStormSafeRadius, t.Radius);
                Assert.IsTrue(t.SharedView);
                Assert.AreEqual(Boss, t.Source);
                Assert.AreEqual(storm.ImpactTick, t.ImpactTick);
            }
            var first = StormCircles(sim, 0);
            Assert.That(Metres(first[0], boss), Is.EqualTo(2.0).Within(0.01), "круг 0 вплотную к боссу");
            Assert.That(Metres(first[0], hero), Is.LessThan(Metres(boss, hero)), "круг 0 — со стороны героя");
            Assert.That(Metres(first[1], hero), Is.InRange(3.0, 5.0), "круг 1 — в 3–5 м от героя");
            Assert.That(Metres(first[2], hero), Is.InRange(5.0, 9.0), "круг 2 — в 5–9 м");
            Assert.IsFalse(sim.ThicketStormSafeAt(Boss, 0, hero), "стоя на месте не укрыться");

            int health = sim.Entities.Health[0];
            bool sawSecond = false;
            while (sim.Tick < storm.ImpactTick + 1)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(boss, sim.Entities.Position[Boss], "босс стоит");
                Assert.AreEqual(facing, sim.Entities.Facing[Boss]);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketStorm)
                    { Assert.AreEqual(1, e.Amount); sawSecond = true; }
            }
            Assert.AreEqual(34, health - sim.Entities.Health[0], "волна вне кругов — 34 на арене 9 (20 × 70/41)");
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStorm, true));
            Assert.IsTrue(sawSecond, "круги второй волны встают в тик удара первой");
            marks = SafeMarks(sim);
            Assert.AreEqual(3, marks.Count, "три новых круга");
            foreach (var t in marks) Assert.AreEqual(storm.LastImpactTick, t.ImpactTick);
            var second = StormCircles(sim, 1);
            Assert.That(Metres(second[0], boss), Is.EqualTo(2.0).Within(0.01));
            Assert.That(Metres(second[0], first[0]), Is.GreaterThan(2.0), "круг у босса — с другой стороны");

            // Вторая волна: герой в круге 1 — ничего.
            sim.Entities.Position[0] = second[1];
            health = sim.Entities.Health[0];
            Until(sim, storm.LastImpactTick + 1);
            Assert.AreEqual(health, sim.Entities.Health[0], "в круге света волна не задевает");
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStorm, false));
            Assert.AreEqual(0, SafeMarks(sim).Count);
            Until(sim, storm.EndTick + 1);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "буря кончилась");
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(start + Simulation.ThicketStormEveryTicks * Simulation.ThicketEnragedCooldownPercent / 100,
                memory.StormNextTick, "следующая — через ~20 с, ×0,85 ниже половины здоровья");
        }

        [Test]
        public void Storm_DamagesOnlyOutsideTheCircles_ByTheBossAndOnTheEdge()
        {
            var sim = Arena();
            Only(sim, ThicketMasterAction.Storm);
            ToStorm(sim);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
            var boss = sim.Entities.Position[Boss];
            var circles = StormCircles(sim, 0);
            // Круг у босса: герой в 2,5 м от центра босса — в круге и в досягаемости удара.
            var by = boss + (circles[0] - boss).Normalized() * Fix64.Ratio(5, 2);
            Assert.That(Metres(by, circles[0]), Is.LessThanOrEqualTo(2.0));
            Assert.That(Metres(circles[0], boss) - 2.0, Is.LessThan(EnemyArchetypes.ThicketMasterBodyRadius.ToDouble()),
                "круг заходит под тело босса");
            Until(sim, storm.ImpactTick);
            sim.Entities.Position[0] = by;
            int health = sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(health, sim.Entities.Health[0], "у босса в круге — укрыт");

            // Вторая волна: центр героя ровно на кромке круга 2 — укрыт; чуть дальше — нет.
            var second = StormCircles(sim, 1);
            var outward = (second[2] - sim.Entities.Position[Boss]).Normalized();
            var edge = second[2] + outward * Fix64.Ratio(199, 100);
            var beyond = second[2] + outward * Fix64.Ratio(21, 10);
            Assert.IsTrue(sim.ThicketStormSafeAt(Boss, 1, edge));
            bool clear = !sim.ThicketStormSafeAt(Boss, 1, beyond);
            Assert.IsTrue(clear, "точка за кромкой — вне всех кругов");
            Until(sim, storm.LastImpactTick);
            sim.Entities.Position[0] = beyond;
            health = sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(34, health - sim.Entities.Health[0], "за кромкой — 34");
        }

        [Test]
        public void Storm_Reachability_ManyMapsAndHeroSpots_CirclesOnTheFloor_OneWithinFiveMetres()
        {
            var location = ArenaEncounterTests.ForestLocation();
            int cases = 0, nearByBoss = 0;
            double worst = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var map = ArenaEncounterTests.ArenaMap(location, 9, seed);
                for (int spot = 0; spot < 6; spot++)
                {
                    var sim = new Simulation(seed * 16 + (ulong)spot, 64);
                    sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, map, seed, arena: 9);
                    var e = sim.Entities;
                    e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                    e.RefreshStats(Boss);
                    sim.PlayerInvulnerable = true;
                    Only(sim, ThicketMasterAction.Storm);
                    var body = e.BodyRadius[0];
                    double[] reach = { 1.6, 4, 8, 11 };
                    Fix64 angle = Fix64.TwoPi * Fix64.Ratio(spot * 5 + (int)seed, 31);
                    FixVec2 bossAt = e.Position[Boss];
                    FixVec2 a = map.ClampToWalkable(bossAt + FixVec2.FromAngle(angle) * Fix64.FromDouble(reach[spot % 4]), body);
                    FixVec2 b = map.ClampToWalkable(bossAt + FixVec2.FromAngle(angle + Fix64.Pi * 3 / 4)
                        * Fix64.FromDouble(reach[(spot + 2) % 4]), body);
                    // Сон кончается, когда герой ближе 9 м: он стоит в 6 м до конца вступления.
                    int start = ToStorm(sim, () => { if (sim.Tick >= IntroDone) e.Position[0] = a; });
                    Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
                    CheckStormWave(sim, map, 0, e.Position[0], ref worst, ref nearByBoss, seed, spot);
                    while (sim.Tick <= storm.ImpactTick)
                    {
                        e.Position[0] = b;
                        sim.Step(InputFrame.Empty);
                    }
                    CheckStormWave(sim, map, 1, e.Position[0], ref worst, ref nearByBoss, seed, spot);
                    cases += 2;
                }
            }
            TestContext.WriteLine("waves " + cases + ", nearest circle at most " + worst.ToString("0.00")
                + " m, hero already in the circle by the boss " + nearByBoss);
        }

        private static void CheckStormWave(Simulation sim, LayoutMap map, int wave, FixVec2 hero, ref double worst,
            ref int nearByBoss, ulong seed, int spot)
        {
            string where = " (сид " + seed + ", место " + spot + ", волна " + wave + ")";
            var e = sim.Entities;
            var body = e.BodyRadius[0];
            var circles = StormCircles(sim, wave);
            double nearest = double.MaxValue;
            int near = -1;
            for (int k = 0; k < circles.Length; k++)
            {
                Assert.IsTrue(map.IsWalkable(circles[k], body), "круг " + k + " на полу" + where);
                double d = Metres(circles[k], hero);
                if (d < nearest) { nearest = d; near = k; }
            }
            Assert.That(Metres(circles[0], e.Position[Boss]),
                Is.LessThanOrEqualTo((Simulation.ThicketStormSafeRadius + e.BodyRadius[Boss]).ToDouble()), "круг 0 у босса" + where);
            Assert.That(nearest, Is.LessThanOrEqualTo(5.0), "ближний круг не дальше 5 м" + where);
            if (near == 0 && nearest <= 2.0) nearByBoss++;
            Assert.That(Metres(circles[1], hero), Is.LessThanOrEqualTo(5.0), "круг 1 не дальше 5 м" + where);
            // Ревью 02.10, ночь: «круги иногда под нижней полосой HUD» — круг 1 не ниже героя на экране боя
            // больше 1,5 м по земле (камера: рыскание 25,65°, Simulation.ThicketCameraScreenDown).
            Assert.That(Simulation.ThicketScreenDown(hero, circles[1]).ToDouble(),
                Is.LessThanOrEqualTo(Simulation.ThicketStormScreenDownMax.ToDouble() + 1e-6), "круг 1 не под HUD" + where);
            if (Metres(circles[1], hero) > 0.01)
                Assert.IsTrue(map.CanTravel(hero, circles[1], body), "до круга 1 — по прямой без стен" + where);
            Assert.IsTrue(map.CanTravel(hero, circles[2], body) || Metres(circles[2], circles[1]) < 0.01,
                "до круга 2 — по прямой без стен" + where);
            if (nearest > worst) worst = nearest;
        }

        [Test]
        public void Storm_NearCircle_NeverDeepBelowTheHeroOnScreen_ManySeeds_FarCirclePrefersItToo()
        {
            // Камера боя (SampleScene): рыскание 25,65° — низ экрана на земле −(0,4329; 0,9015) в осях Sim.
            var down = Simulation.ThicketCameraScreenDown;
            Assert.That(down.Length.ToDouble(), Is.EqualTo(1.0).Within(1e-3));
            Assert.That(down.X.ToDouble(), Is.EqualTo(-System.Math.Sin(25.65 * System.Math.PI / 180)).Within(1e-3));
            Assert.That(down.Y.ToDouble(), Is.EqualTo(-System.Math.Cos(25.65 * System.Math.PI / 180)).Within(1e-3));
            // Стенд без карты (пол везде), 40 потоков босса, герой с разных сторон: круг 1 — в 3–5 м и не
            // ниже 1,5 м по экрану; круг 2 — не ниже 3 м (на открытом месте место всегда есть).
            int checkedWaves = 0;
            for (int run = 0; run < 40; run++)
            {
                var sim = new Simulation(500 + (ulong)run, 64);
                sim.SetupKindTestArena(EnemyKind.ForestThicketMaster, 1, arena: 9, distance: Fix64.FromInt(6));
                var e = sim.Entities;
                e.Stats[Boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
                e.RefreshStats(Boss);
                sim.PlayerInvulnerable = true;
                Only(sim, ThicketMasterAction.Storm);
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio(run, 40);
                var spot = e.Position[Boss] + FixVec2.FromAngle(angle) * Fix64.FromInt(4);
                ToStorm(sim, () => { if (sim.Tick >= IntroDone) e.Position[0] = spot; });
                var circles = StormCircles(sim, 0);
                var hero = e.Position[0];
                Assert.That(Metres(circles[1], hero), Is.InRange(2.999, 5.0), "круг 1 в 3–5 м, прогон " + run);
                Assert.That(Simulation.ThicketScreenDown(hero, circles[1]).ToDouble(), Is.LessThanOrEqualTo(1.5 + 1e-6), "круг 1, прогон " + run);
                Assert.That(Simulation.ThicketScreenDown(hero, circles[2]).ToDouble(), Is.LessThanOrEqualTo(3.0 + 1e-6), "круг 2, прогон " + run);
                checkedWaves++;
            }
            Assert.AreEqual(40, checkedWaves);
        }

        [Test]
        public void Storm_WaitsForTheGroundToClear_HoldsTheToken_ThenTakesTheWholeBudget()
        {
            var sim = Arena();
            Only(sim);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Until(sim, roar.EndTick + 1);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "буря пока закрыта стендом, герой в 6 м — босс стоит");
            // Плюй-плод стоит и стреляет залпами: залп — крупная метка весом 2.
            int add = sim.AddKindTestEnemy(EnemyKind.ForestBud, At(-6, 0), 100);
            int marked = -1;
            for (int k = 0; k < 900 && marked < 0; k++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.BigMarkLoad(out _) > 0) marked = sim.Tick;
            }
            Assert.AreNotEqual(-1, marked, "Плюй-плод кладёт залп");
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Storm, 0);
            Assert.IsTrue(sim.ThicketStormDue(Boss));
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss), "пора буре — подмога новых крупных не начинает");
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "чужая метка на земле — буря ждёт");

            int start = -1, waited = 0;
            for (int k = 0; k < 120 && start < 0; k++)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                if (sim.TryGetThicketMasterAction(Boss, out var now) && now.Action == ThicketMasterAction.Storm)
                {
                    start = tick;
                    Assert.AreEqual(Simulation.ThicketStormMarkWeight, sim.BigMarkLoad(out _), "на земле — только буря");
                }
                else
                {
                    waited++;
                    Assert.That(sim.BigMarkLoad(out _), Is.GreaterThan(0), "земля пуста, а буря не встала");
                }
            }
            Assert.AreNotEqual(-1, start, "буря дождалась земли");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
            int foreign = 0;
            while (sim.Tick <= storm.LastImpactTick)
            {
                Assert.That(sim.BigMarkLoad(out _), Is.GreaterThanOrEqualTo(Simulation.ThicketStormMarkWeight), "буря — весь бюджет");
                Assert.IsFalse(sim.BigMarkAllowed(add, 1, sim.Tick + 36), "чужая крупная метка не встаёт");
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Source != add) continue;
                    if (e.Type == SimEventType.ForestBudVolleyStarted) foreign++;
                    if (e.Type == SimEventType.TelegraphOpened && MarkOf(sim, e.ActionVariant, out var t)
                        && t.Shape != TelegraphShape.Sector) foreign++;
                }
            }
            Assert.AreEqual(0, foreign, "пока идёт буря, других крупных меток нет");
            TestContext.WriteLine("storm waited " + waited + " ticks for the volley to land");
        }

        [Test]
        public void Storm_EveryTwentySeconds_FromTheStartOfThePrevious()
        {
            var sim = Arena();
            Only(sim, ThicketMasterAction.Storm);
            int first = ToStorm(sim);
            HeroInFront(sim, 8);
            int next = RunUntilStorm(sim, Simulation.ThicketStormEveryTicks + 10);
            // Фаза 3 — ниже половины здоровья: срок ×0,85, как перезарядки (17 с).
            Assert.AreEqual(first + Simulation.ThicketStormEveryTicks * Simulation.ThicketEnragedCooldownPercent / 100, next);
        }

        [Test]
        public void Storm_Hourglass_WavesAndCirclesWaitSixtyTicks()
        {
            var sim = Arena();
            sim.SetArtifact(RunArtifact.Hourglass);
            Only(sim, ThicketMasterAction.Storm);
            int start = ToStorm(sim);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
            Until(sim, start + 20);
            sim.Step(new InputFrame { Flags = (byte)InputFlags.UseArtifact, Aim = sim.Entities.Position[0] });
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var shifted));
            int shift = Simulation.ThicketHourglassShiftTicks;
            Assert.AreEqual(storm.ImpactTick + shift, shifted.ImpactTick);
            Assert.AreEqual(storm.LastImpactTick + shift, shifted.LastImpactTick);
            foreach (var t in SafeMarks(sim)) Assert.AreEqual(shifted.ImpactTick, t.ImpactTick, "круги ждут вместе с боссом");
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 0, out _, out int impact, out _));
            Assert.AreEqual(shifted.ImpactTick, impact);
            int health = sim.Entities.Health[0];
            Until(sim, storm.ImpactTick + 1);
            Assert.AreEqual(health, sim.Entities.Health[0], "в старый тик волны нет");
            Until(sim, shifted.ImpactTick + 1);
            Assert.AreEqual(34, health - sim.Entities.Health[0], "волна легла на 60 тиков позже");
        }
        // ---------- связки фаз 2–3 ----------

        /// <summary>Герой в distance метрах прямо по взгляду босса.</summary>
        private static void HeroAhead(Simulation sim, double distance)
            => sim.Entities.Position[0] = sim.Entities.Position[Boss]
                + sim.Entities.Facing[Boss].Normalized() * Fix64.FromDouble(distance);

        /// <summary>Нырок фазы percent (100 — фаза 1): герой в 10 м (дальняя полоса), после выхода — в after метрах по взгляду до конца стойки.</summary>
        private static ThicketMasterState DiveAndStand(Simulation sim, int percent, double after)
        {
            Only(sim, ThicketMasterAction.Dive);
            if (percent < 100) ToPhase(sim, percent); else Until(sim, IntroDone);
            HeroInFront(sim, 10);
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400), "нырок");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            Until(sim, dive.ImpactTick + 1);
            while (sim.Tick < dive.EndTick) { HeroAhead(sim, after); sim.Step(InputFrame.Empty); }
            HeroAhead(sim, after);
            return dive;
        }

        [Test]
        public void Chain_PhaseTwo_DiveThenPawSeries_RightAfterTheStand36()
        {
            var sim = Arena();
            var dive = DiveAndStand(sim, 60, 2.5);
            Assert.AreEqual(dive.ImpactTick + 36, dive.EndTick, "стоит 36 после выхода");
            int pawAt = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 5);
            Assert.AreEqual(dive.EndTick, pawAt, "Нырок→Лапа: серия сразу, без отдыха");
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(ThicketMasterAction.Paw, memory.ChainNext);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(pawAt + Simulation.ThicketPawWindupTicks, paw.ImpactTick, "замах 17 связка не сокращает");
            Until(sim, paw.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext, "связка доиграна");
        }

        [Test]
        public void Chain_PhaseThree_DiveThenStomp_WhenTheHeroIsPastThePaw()
        {
            var sim = Arena();
            var dive = DiveAndStand(sim, 30, 6);
            int stompAt = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 5);
            Assert.AreEqual(dive.EndTick, stompAt, "Нырок→Топот: мимо правила «прижался» и его перезарядки");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            Assert.AreEqual(stompAt + Simulation.ThicketStompWindupTicks, stomp.ImpactTick);
            Until(sim, stomp.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext);
            Assert.AreEqual(stomp.EndTick + Simulation.ThicketRestPhase3Ticks * Simulation.ThicketEnragedCooldownPercent / 100,
                memory.NextActionTick, "после связки — отдых фазы 3 (×0,85 ниже половины здоровья)");
            Assert.AreEqual(stomp.LastImpactTick + Simulation.ThicketWindowTicks, memory.QuietUntil, "окно ответа и в фазе 3 — 30");
        }

        [Test]
        public void Chain_NotInPhaseOne_NorForAFarHero_RestByPhaseInstead()
        {
            var one = Arena();
            var dive = DiveAndStand(one, 100, 2.5);
            Assert.AreEqual(-1, RunUntilStarted(one, EnemyActionKind.ThicketPaw, 2), "фаза 1 — без связки");
            Assert.IsTrue(one.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext);
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase1Ticks, memory.NextActionTick, "отдых фазы 1");

            var far = Arena();
            dive = DiveAndStand(far, 60, 9);
            far.Step(InputFrame.Empty);
            Assert.IsTrue(far.TryGetThicketMasterMemory(Boss, out memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext, "герой дальше 7,5 м — связки нет");
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase2Ticks, memory.NextActionTick, "отдых фазы 2");
        }

        // ---------- наслоение ----------

        [Test]
        public void Layering_WhileTheSproutRuns_OnlyPawSeries_NoStompDiveOrCast_PawsApartFromItsHits()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Sprout, ThicketMasterAction.Dive);
            ToPhase(sim, 60);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketSprout, 30);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketHazard(Boss, out var hazard));
            // Дальше герой стоит перед мордой (в досягаемости лапы), а после первой серии — за спиной
            // в 3 м: правило топота «за спиной» набирается, но ждёт конца опасности.
            var spot = sim.Entities.Position[Boss] + At(-2.5, 0);
            var circleHits = new List<int>();
            var pawHits = new List<int>();
            int paws = 0, others = 0, stompAfter = -1;
            for (int k = 0; k < 400 && stompAfter < 0; k++)
            {
                bool seriesDone = paws > 0 && !(sim.TryGetThicketMasterAction(Boss, out var busy) && busy.Action == ThicketMasterAction.Paw);
                sim.Entities.Position[0] = seriesDone
                    ? sim.Entities.Position[Boss] - sim.Entities.Facing[Boss].Normalized() * Fix64.FromInt(3)
                    : spot;
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                bool active = sim.ThicketHazardActive(Boss);
                if (active && sim.TryGetThicketMasterAction(Boss, out var now) && now.Action == ThicketMasterAction.Paw)
                    Assert.AreEqual(2, sim.BigMarkLoad(out _), "лапа в бюджет не идёт — только опасность (2)");
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Source != Boss) continue;
                    if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.ThicketSprout) circleHits.Add(tick);
                    if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.ThicketPaw) pawHits.Add(tick);
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0) continue;
                    if (active)
                    {
                        if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) paws++;
                        else others++;
                    }
                    else if (e.ActionVariant == (int)EnemyActionKind.ThicketStomp) stompAfter = tick;
                }
            }
            Assert.That(paws, Is.GreaterThan(0), "пока идёт опасность — серии лапы");
            Assert.AreEqual(0, others, "пока идёт опасность — ни топота, ни нырка, ни каста");
            Assert.AreNotEqual(-1, stompAfter, "«за спиной» набралось — топот встаёт, как только опасность кончилась");
            Assert.That(stompAfter, Is.GreaterThan(hazard.LastImpactTick));
            Assert.AreEqual(6, circleHits.Count);
            foreach (int paw in pawHits)
                foreach (int circle in circleHits)
                    Assert.That(System.Math.Abs(paw - circle), Is.GreaterThanOrEqualTo(Simulation.ThicketOwnContactSpacingTicks),
                        "удар лапы не сливается с ударом круга");
        }

        [Test]
        public void Layering_PollenClouds_KeepTheBossOnPaws_UntilTheyFade()
        {
            var sim = Arena(distance: 6);
            Only(sim, ThicketMasterAction.Pollen);
            ToPhase(sim, 60);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPollen, 30);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var gesture));
            Assert.AreEqual(start + Simulation.ThicketCastGestureTicks, gesture.EndTick);
            Assert.That(gesture.EndTick, Is.LessThan(start + Simulation.ThicketPollenFallTicks), "жест кончается до падения");
            int landed = StepCounting(sim, start + Simulation.ThicketPollenFallTicks + 1, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPollen);
            Assert.AreEqual(3, landed, "падение — событие на каждое облако, уже без жеста");
            int fade = start + Simulation.ThicketPollenFallTicks + Simulation.ThicketPollenLifeTicks;
            while (sim.Tick <= fade)
            {
                Assert.IsTrue(sim.ThicketHazardActive(Boss), "облака лежат — опасность идёт, тик " + sim.Tick);
                sim.Step(InputFrame.Empty);
            }
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.ThicketHazardActive(Boss), "облака ушли");
        }

        // ---------- честность на поляне босса 20,98 × 15,74 (проверка 02.10, +10% площади — вечер) ----------

        /// <summary>Оси от центра поляны: по длинной (±X), по короткой (±Y, до края 7,87 м) и по диагонали.</summary>
        private static readonly FixVec2[] ClearingAxes = { At(1, 0), At(-1, 0), At(0, 1), At(0, -1), At(0.6, 0.8) };

        /// <summary>
        /// Настоящая поляна босса (уровень 9 леса): босс перенесён из своего угла в её
        /// центр (Home) и стоит на месте (ход 0 — фигуры от центра; так проверяется
        /// каждая ось поляны), герой 10000 HP с рывком, касты и буря закрыты.
        /// </summary>
        private static Simulation Clearing(out int boss, out LayoutMap map)
        {
            const ulong seed = 41;
            var location = ArenaEncounterTests.ForestLocation();
            map = ArenaEncounterTests.ArenaMap(location, 9, seed);
            var sim = new Simulation(seed, 512) { ThicketMasterBossEnabled = true };
            sim.ApplyHeroBaseline();
            var plan = location.GetLevel(9).Spawn(sim, map, seed, null, 9);
            sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(9);
            sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(9);
            boss = plan.BossId;
            var e = sim.Entities;
            Assert.IsTrue(sim.TryGetThicketMasterMemory(boss, out var memory));
            Assert.That(e.Position[boss], Is.Not.EqualTo(memory.Home), "встаёт в углу поляны");
            e.Position[boss] = memory.Home;
            sim.Grid.Rebuild(e);
            e.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            e.RefreshStats(0);
            e.Health[0] = e.MaxHealth[0];
            e.Stats[boss].SetBase(StatType.MoveSpeed, Fix64.Zero);
            e.RefreshStats(boss);
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            foreach (var action in new[] { ThicketMasterAction.Sprout, ThicketMasterAction.Pollen, ThicketMasterAction.Rain,
                         ThicketMasterAction.Storm })
                sim.SetThicketReadyTick(boss, action, int.MaxValue / 2);
            return sim;
        }

        private static InputFrame Walk(FixVec2 to)
        {
            var input = InputFrame.Empty;
            input.Flags = (byte)InputFlags.MoveOrder;
            input.Aim = to;
            return input;
        }

        private static InputFrame DashTo(FixVec2 to)
        {
            var input = Walk(to);
            input.AbilityMask = (byte)(1 << PelagKit.DashSlot);
            return input;
        }

        /// <summary>Шагает, держа героя в spot, пока босс не начнёт kind; тик начала.</summary>
        private static int HoldUntilStarted(Simulation sim, FixVec2 spot, EnemyActionKind kind, int limit)
        {
            for (int k = 0; k < limit; k++)
            {
                sim.Entities.Position[0] = spot;
                int tick = sim.Tick;
                sim.Step(Walk(spot));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)kind && e.Amount == 0) return tick;
            }
            return -1;
        }

        [Test]
        public void Stomp_SmallClearing_EveryAxis_FeetOrDashBeatTheCircleAndTheRing()
        {
            // Круг 5,2 через 42, кольцо 5,2–7,5 через 15. По короткой оси пол кончается в 7,87 м —
            // за кольцо не выйти, значит путь — наружу из круга и обратно внутрь кольца.
            string[] names = { "ноги без задержки", "рывок наружу через 8 тиков", "рывок вбок под удар круга" };
            foreach (var axis in ClearingAxes)
                for (int policy = -1; policy < 3; policy++)
                {
                    var sim = Clearing(out int boss, out var map);
                    sim.SetThicketReadyTick(boss, ThicketMasterAction.Dive, int.MaxValue / 2);
                    var e = sim.Entities;
                    var center = e.Position[boss];
                    // В окне топота (4 м), но дальше лапы (3,68): только топот.
                    int start = HoldUntilStarted(sim, center + axis * Fix64.Ratio(39, 10), EnemyActionKind.ThicketStomp, 900);
                    Assert.AreNotEqual(-1, start, "топот начался");
                    Assert.IsTrue(sim.TryGetThicketMasterAction(boss, out var stomp));
                    int health = e.Health[0];
                    var side = new FixVec2(-axis.Y, axis.X);
                    while (sim.Tick <= stomp.LastImpactTick + 1)
                    {
                        int t = sim.Tick - start;
                        double d = Metres(e.Position[0], center);
                        bool ringPhase = sim.Tick > stomp.ImpactTick;
                        InputFrame input;
                        if (ForcedMotion.IsActive(e, 0)) input = InputFrame.Empty;
                        else if (policy == 1 && t == 8) input = DashTo(center + axis * Fix64.FromInt(20));
                        else if (policy == 2 && t == Simulation.ThicketStompWindupTicks - 6) input = DashTo(e.Position[0] + side * Fix64.FromInt(4));
                        else if (policy < 0) input = Walk(e.Position[0]);
                        else if (!ringPhase && policy == 0) input = d < 5.8 ? Walk(center + axis * Fix64.FromInt(9)) : Walk(e.Position[0]);
                        // Рывком наружу — ждёт за кругом с запасом на торможение (замах 42: успевает дойти назад до удара).
                        else if (!ringPhase) input = policy == 1 && d > 6.3 ? Walk(center + axis * Fix64.Ratio(16, 10)) : Walk(e.Position[0]);
                        else input = d > 4.6 ? Walk(center + axis * Fix64.Ratio(16, 10)) : Walk(e.Position[0]);
                        sim.Step(input);
                    }
                    if (policy < 0) Assert.Less(e.Health[0], health, "стоя на месте — попадание (проверка жива), ось " + axis);
                    else Assert.AreEqual(health, e.Health[0], names[policy] + ", ось " + axis);
                }
        }

        [Test]
        public void Paw_PhaseThreeSeriesInMelee_DashAwayAfterReactionOrFeetAtOnce_NoStrikeLands()
        {
            // Серия из трёх перед мордой: первый замах 17 тиков. Из 2,5 м рывок прочь через
            // 8 тиков (реакция) уводит из всех трёх; ногами из 2,5 м не уйти даже сразу
            // (сектор — 4,14 от центра + тело героя), с края досягаемости сабли (3,3 м) — можно, если сразу.
            string[] names = { "рывок прочь через 8 тиков из 2,5 м", "ногами прочь сразу из 3,3 м" };
            for (int policy = -1; policy < 2; policy++)
            {
                var sim = Clearing(out int boss, out var map);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Dive, int.MaxValue / 2);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
                // Веер семян (07.10) — по герою в 6 м; здесь только серия: семя прошлого веера ловило рывок прочь.
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Seeds, int.MaxValue / 2);
                var e = sim.Entities;
                var center = e.Position[boss];
                for (int k = 0; k < 200; k++) { e.Position[0] = center + At(0, -6); sim.Step(Walk(e.Position[0])); }
                e.Health[boss] = e.MaxHealth[boss] * 30 / 100;
                for (int k = 0; k < 120; k++) { e.Position[0] = center + At(0, -6); sim.Step(Walk(e.Position[0])); }
                Assert.AreEqual(3, sim.ThicketPawSeriesMin(boss), "фаза 3 — серия из трёх");
                int start = -1;
                for (int k = 0; k < 300 && start < 0; k++)
                {
                    e.Position[0] = center + e.Facing[boss].Normalized() * (policy == 1 ? Fix64.Ratio(33, 10) : Fix64.Ratio(5, 2));
                    int tick = sim.Tick;
                    sim.Step(Walk(e.Position[0]));
                    foreach (var ev in sim.Events)
                        if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketPaw && ev.Amount == 0)
                            start = tick;
                }
                Assert.AreNotEqual(-1, start, "серия началась");
                Assert.IsTrue(sim.TryGetThicketMasterAction(boss, out var paw));
                Assert.AreEqual(3, paw.Stages);
                int health = e.Health[0];
                var away = (e.Position[0] - center).Normalized();
                while (sim.Tick <= paw.LastImpactTick + 1)
                {
                    int t = sim.Tick - start;
                    InputFrame input;
                    if (ForcedMotion.IsActive(e, 0)) input = InputFrame.Empty;
                    else if (policy < 0) input = Walk(e.Position[0]);
                    else if (policy == 0 && t < 8) input = Walk(e.Position[0]);
                    else if (policy == 0 && t == 8) input = DashTo(center + away * Fix64.FromInt(20));
                    else input = Walk(center + away * Fix64.FromInt(20));
                    sim.Step(input);
                }
                if (policy < 0) Assert.Less(e.Health[0], health, "стоя на месте — попадание (проверка жива)");
                else Assert.AreEqual(health, e.Health[0], names[policy]);
            }
        }

        [Test]
        public void Dive_HeroOnTheFloorEdge_EveryAxis_DashGetsHimOut()
        {
            // Круг нырка r4,0 встаёт под героем за 24 тика до выхода: ногами не уйти (нужно
            // 4,45 м, герой проходит 3,6), рывком — да, и у самого края пола.
            string[] names = { "рывок внутрь через 8 тиков", "рывок вдоль края под выход" };
            foreach (var axis in ClearingAxes)
                for (int policy = -1; policy < 2; policy++)
                {
                    var sim = Clearing(out int boss, out var map);
                    sim.SetThicketReadyTick(boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
                    var e = sim.Entities;
                    var center = e.Position[boss];
                    Fix64 radius = e.BodyRadius[0];
                    var edge = center;
                    for (Fix64 r = Fix64.Ratio(1, 10); r < Fix64.FromInt(12); r += Fix64.Ratio(1, 10))
                    {
                        if (!map.IsWalkable(center + axis * r, radius)) break;
                        edge = center + axis * r;
                    }
                    Assert.That(Metres(edge, center), Is.GreaterThan(6.5), "край пола найден");
                    // Нырок — только по дальней полосе (ревью 02.10, ночь): босс в 3 м от центра поляны по
                    // другую сторону от края — герой на краю от него в 9,5+ м (6,5+ м от кромки корпуса).
                    e.Position[boss] = center - axis * Fix64.FromInt(3);
                    sim.Grid.Rebuild(e);
                    // Разбудить рядом, потом встать на край (вне досягаемости лапы и круга топота).
                    for (int k = 0; k < 200; k++) { e.Position[0] = center + axis * Fix64.FromInt(3); sim.Step(Walk(e.Position[0])); }
                    int start = HoldUntilStarted(sim, edge, EnemyActionKind.ThicketDive, 600);
                    Assert.AreNotEqual(-1, start, "нырок начался");
                    Assert.IsTrue(sim.TryGetThicketMasterAction(boss, out var dive));
                    int health = e.Health[0];
                    int locked = dive.ImpactTick - Simulation.ThicketDiveLockTicks;
                    var along = new FixVec2(-axis.Y, axis.X);
                    while (sim.Tick <= dive.ImpactTick + 1)
                    {
                        InputFrame input;
                        if (ForcedMotion.IsActive(e, 0)) input = InputFrame.Empty;
                        else if (sim.Tick < locked) { e.Position[0] = edge; input = Walk(edge); }
                        else if (policy < 0) input = Walk(e.Position[0]);
                        else if (policy == 0 && sim.Tick == locked + 8) input = DashTo(center);
                        else if (policy == 0 && sim.Tick > locked + 8) input = Walk(center);
                        else if (policy == 1 && sim.Tick == dive.ImpactTick - 4) input = DashTo(e.Position[0] + along * Fix64.FromInt(4));
                        else input = Walk(e.Position[0]);
                        sim.Step(input);
                    }
                    if (policy < 0) Assert.Less(e.Health[0], health, "стоя на месте — попадание (проверка жива), ось " + axis);
                    else Assert.AreEqual(health, e.Health[0], names[policy] + ", ось " + axis);
                    Assert.IsTrue(map.IsWalkable(e.Position[boss], e.BodyRadius[boss]), "вылез на полу, ось " + axis);
                }
        }

        // ---------- нырок «под героя» и топот в жребии (03.10, владелец: «босс за игру ни разу не
        // залез под землю. надо участить. как и участить аое в ближнем бою») ----------

        /// <summary>
        /// Стенд ближника: герой в 3 м перед мордой, касты и буря закрыты, нырок открыт; без withPick — и
        /// топоты закрыты («прижался», «за спиной», в жребии): лапа и нырок, срок нырка не ждёт топота и связки.
        /// </summary>
        private static Simulation CloseHero(bool withPick)
        {
            var sim = Arena(distance: 3);
            Only(sim, ThicketMasterAction.Dive);
            if (!withPick)
            {
                NoStompPick(sim);
                sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
                sim.SetThicketRearStompReadyTick(Boss, int.MaxValue / 2);
            }
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), new AbilityNode[0], 0);
            return sim;
        }

        /// <summary>Держит героя в 3 м перед мордой (здоровье доливает), пока не начнётся нырок; тик начала или −1.</summary>
        private static int HoldCloseUntilDive(Simulation sim, int limit, out bool freeBefore)
        {
            freeBefore = false;
            for (int k = 0; k < limit; k++)
            {
                HeroInFront(sim, 3);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                bool free = !sim.TryGetThicketMasterAction(Boss, out _);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketDive && e.Amount == 0)
                    {
                        freeBefore = free;
                        return tick;
                    }
            }
            return -1;
        }

        [Test]
        public void DiveUnderHero_HeroWhoStaysClose_FirstTenSecondsAfterTheIntro_ThenEvery24_6_5Seconds_SeriesNotCut()
        {
            // Ближник у морды раньше не видел нырка вовсе (только сближение по дальнему). Теперь — «под героя»:
            // первый не раньше 10 с после вступления, в первый свободный тик (серию не режет), дальше — от
            // начала любого нырка 720 / 180 / 180 (×0,85 с половины здоровья: фаза 3 — 153; перебаланс 03.10 —
            // было 450 / 330 / 330), сближение — своё 540 / 450. Фаза 1 — правилом; фазы 2–3 — в жребии с лапой
            // (80 к 10): через серию-другую после срока. Здесь герой у морды с конца вступления (первая атака —
            // лапа); с кромки поляны (в игре — всегда) первая атака — сближение, и «под героя» — через те же 10 с
            // от него (ThicketMasterIntroTests.FromTheClearingEdge_OpenerDive_*, проверка находок 03.10).
            var sim = CloseHero(withPick: false);
            Until(sim, IntroDone + 1);   // тик конца рёва вступления (IntroDone) прошёл
            Assert.AreEqual(IntroDone + Simulation.ThicketDiveFirstTicks, sim.ThicketDiveDueTick(Boss), "срок первого — 10 с после рёва");
            int first = HoldCloseUntilDive(sim, 600, out bool free);
            Assert.That(first, Is.InRange(IntroDone + Simulation.ThicketDiveFirstTicks, IntroDone + Simulation.ThicketDiveFirstTicks + 60),
                "первый нырок — через 10 с, как только серия доиграна");
            Assert.IsTrue(free, "нырок не прерывает начатое действие");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            Assert.IsTrue(Simulation.ThicketDiveUnderHero(dive), "герой рядом — нырок «под героя»");
            Assert.AreEqual(first + Simulation.ThicketDiveEveryPhase1Ticks, sim.ThicketDiveDueTick(Boss), "фаза 1 — раз в 24 с");
            Assert.AreEqual(first + Simulation.ThicketDiveCooldownPhase1Ticks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Dive),
                "сбрасывает и срок сближения");
            int second = HoldCloseUntilDive(sim, Simulation.ThicketDiveEveryPhase1Ticks + 120, out free);
            Assert.That(second, Is.InRange(first + Simulation.ThicketDiveEveryPhase1Ticks, first + Simulation.ThicketDiveEveryPhase1Ticks + 60),
                "второй — через 24 с, раньше нет");
            Assert.IsTrue(free);

            // Фаза 2 (60%): 180; фаза 3 (30%, ярость ×0,85): 180 → 153.
            int[][] phases = { new[] { 60, Simulation.ThicketDiveEveryPhase2Ticks }, new[] { 30, Simulation.ThicketDiveEveryPhase3Ticks * 85 / 100 } };
            foreach (var row in phases)
            {
                int percent = row[0], every = row[1];
                var phase = CloseHero(withPick: false);
                ToPhase(phase, percent);
                int a = HoldCloseUntilDive(phase, 600, out _);
                Assert.AreNotEqual(-1, a, "нырок в фазе здоровья " + percent);
                Assert.AreEqual(a + every, phase.ThicketDiveDueTick(Boss), "срок по фазе, здоровье " + percent);
                int b = HoldCloseUntilDive(phase, 600, out bool f2);
                Assert.That(b, Is.InRange(a + every, a + every + 150), "следующий — по сроку фазы (жребий), здоровье " + percent);
                Assert.IsTrue(f2);
            }
        }

        [Test]
        public void DiveUnderHero_CircleWhereHeStoodWhenTheMoundSetOff_WalkAwayFromTheBurrow_StandStillIsHit_DashFromTheCircle()
        {
            // «Под героя»: круг ложится не под героем в тик фиксации (тогда ногами не уйти — 4,45 м за 24
            // тика), а там, где он стоял, когда бугор тронулся (12 тиков после ухода): кто пошёл прочь от
            // уходящего в землю босса (реакция до ~0,7 с от начала нырка), уходит ногами; кто стоял — рывок
            // от красного круга (реакция 0,4 с).
            string[] names = { "стоит", "пошёл прочь через 12", "пошёл прочь через 20", "рывок через 12 после круга" };
            for (int policy = 0; policy < names.Length; policy++)
            {
                var sim = CloseHero(withPick: false);
                Until(sim, IntroDone);
                int start = HoldCloseUntilDive(sim, 700, out _);
                Assert.AreNotEqual(-1, start, names[policy]);
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
                Assert.IsTrue(Simulation.ThicketDiveUnderHero(dive), names[policy]);
                var e = sim.Entities;
                int health = e.Health[0];
                var away = (e.Position[0] - e.Position[Boss]).Normalized();
                var far = e.Position[0] + away * Fix64.FromInt(12);
                FixVec2 setOff = default;
                bool hit = false, impact = false;
                int circle = dive.ImpactTick - Simulation.ThicketDiveLockTicks;
                while (sim.Tick <= dive.ImpactTick)
                {
                    int t = sim.Tick - start;
                    InputFrame input = InputFrame.Empty;
                    if ((policy == 1 && t >= 12) || (policy == 2 && t >= 20)) input = Walk(far);
                    if (policy == 3 && sim.Tick == circle + 12) input = DashTo(far);
                    else if (policy == 3 && sim.Tick > circle + 12) input = Walk(far);
                    sim.Step(input);
                    if (sim.TryGetThicketMasterAction(Boss, out var now) && now.Stage == 1 && now.StageStartTick == sim.Tick - 1)
                        setOff = now.Target;
                    foreach (var ev in sim.Events)
                        if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.ThicketDive)
                        { impact = true; hit = ev.Flag; }
                    if (sim.Tick == circle + 1)
                    {
                        Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var locked));
                        Assert.AreEqual(2, locked.Stage, names[policy]);
                        Assert.That(Metres(locked.Target, setOff), Is.LessThan(0.01), "круг — где он стоял, когда бугор тронулся: " + names[policy]);
                    }
                }
                Assert.IsTrue(impact, names[policy]);
                if (policy == 0)
                {
                    Assert.IsTrue(hit, "стоя на месте — попадание (проверка жива)");
                    Assert.Less(e.Health[0], health);
                }
                else
                {
                    Assert.IsFalse(hit, "вышел из круга: " + names[policy]);
                    Assert.AreEqual(health, e.Health[0], names[policy]);
                }
            }
        }

        [Test]
        public void DiveUnderHero_WaitsWhileTheMarkBudgetIsFull_BossKeepsSwinging_NotWhileHazardRuns()
        {
            // «Никогда, пока бюджет крупных меток занят подмогой»: срок пришёл, а залп Плюй-плода (вес 2) лежит
            // на земле при бюджете 2 — нырок не начинается и не держит босса: он бьёт сериями, нырок — когда
            // земля освободилась.
            var sim = CloseHero(withPick: false);
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            sim.SetThicketRearStompReadyTick(Boss, int.MaxValue / 2);
            Until(sim, IntroDone);
            sim.BigMarkBudget = 2;
            sim.SetThicketDiveDueTick(Boss, int.MaxValue / 2);
            int add = sim.AddKindTestEnemy(EnemyKind.ForestBud, sim.Entities.Position[0] + At(-5, 0), 100);
            int pawsWhileBlocked = 0, dives = 0, blocked = 0;
            for (int k = 0; k < 3000; k++)
            {
                HeroInFront(sim, 3);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                bool full = !sim.BigMarkAllowed(Boss, 1, sim.Tick + 54);
                // Срок нырка открывается, когда залп уже лежит: проверяется ожидание, не совпадение.
                if (full) sim.SetThicketDiveDueTick(Boss, sim.Tick);
                bool due = sim.ThicketDiveDueTick(Boss) <= sim.Tick;
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0 || e.Source != Boss) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketDive)
                    {
                        Assert.IsFalse(full, "нырок встал при занятом бюджете, тик " + tick);
                        dives++;
                    }
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw && due && full) pawsWhileBlocked++;
                }
                if (due && full) blocked++;
            }
            Assert.That(blocked, Is.GreaterThan(0), "срок пришёл при занятом бюджете (проверка жива)");
            Assert.That(pawsWhileBlocked, Is.GreaterThan(0), "пока ждёт — бьёт лапой, не стоит");
            Assert.That(dives, Is.GreaterThan(0), "земля освободилась — нырок");
            Assert.IsTrue(sim.Entities.Alive[add]);

            // Пока идут свои круги каста (наслоение) — только серии лапы, нырок ждёт их конца.
            var layer = Arena(distance: 3);
            Only(layer, ThicketMasterAction.Dive, ThicketMasterAction.Sprout);
            NoStompPick(layer);
            ToPhase(layer, 60);
            layer.SetThicketDiveDueTick(Boss, int.MaxValue / 2);
            int sprout = -1;
            for (int k = 0; k < 600 && sprout < 0; k++)
            {
                HeroInFront(layer, 3);
                layer.Entities.Health[0] = layer.Entities.MaxHealth[0];
                int tick = layer.Tick;
                layer.Step(InputFrame.Empty);
                if (Count(layer, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout) > 0) sprout = tick;
            }
            Assert.AreNotEqual(-1, sprout, "прорастание в жребии с лапой");
            layer.SetThicketDiveDueTick(Boss, layer.Tick);
            while (layer.ThicketHazardActive(Boss))
            {
                HeroInFront(layer, 3);
                layer.Step(InputFrame.Empty);
                Assert.AreEqual(0, Count(layer, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive), "нырок под кругами каста");
            }
            Assert.AreNotEqual(-1, HoldCloseUntilDive(layer, 200, out _), "круги отбиты — нырок");
        }

        [Test]
        public void StompPick_PhaseOne_HeroUnderThePaw_StompJoinsTheDraw_FromFourAndAHalfSeconds_135Apart_PawStaysTheBase()
        {
            // «Он только лапой машет на фазе 1 — скучно»: у героя под лапой в фазе 1 топот идёт в жребий с
            // серией (10 к 10), не раньше 4,5 с после вступления и 135 тиков от прошлого топота (перебаланс 03.10;
            // было 5 с и 150). Нырок закрыт.
            var sim = Arena(distance: 3);
            MeleeOnly(sim);
            Until(sim, IntroDone + 1);   // тик конца рёва вступления (IntroDone) прошёл
            Assert.AreEqual(IntroDone + Simulation.ThicketStompPickCooldownTicks, sim.ThicketStompPickReadyTick(Boss), "первый — через 4,5 с");
            var stomps = new List<int>();
            int paws = 0;
            for (int k = 0; k < 3600; k++)
            {
                HeroInFront(sim, 3);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw) paws++;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketStomp)
                    {
                        stomps.Add(tick);
                        Assert.AreEqual(tick + Simulation.ThicketStompPickCooldownTicks, sim.ThicketStompPickReadyTick(Boss));
                    }
                }
            }
            Assert.AreEqual(1, sim.ThicketMasterPhase(Boss));
            Assert.That(stomps.Count, Is.GreaterThanOrEqualTo(8), "топот — частый гость фазы 1");
            Assert.That(stomps[0], Is.GreaterThanOrEqualTo(IntroDone + Simulation.ThicketStompPickCooldownTicks), "бой открывают серии");
            for (int k = 1; k < stomps.Count; k++)
                Assert.That(stomps[k] - stomps[k - 1], Is.GreaterThanOrEqualTo(Simulation.ThicketStompPickCooldownTicks), "не чаще раза в 4,5 с");
            double share = stomps.Count / (double)(stomps.Count + paws);
            TestContext.WriteLine("phase 1: paw " + paws + ", stomp " + stomps.Count + " (" + (share * 100).ToString("0") + "%)");
            Assert.That(share, Is.InRange(0.22, 0.42), "топот — четверть-треть атак фазы 1");
            Assert.That(paws, Is.GreaterThan(stomps.Count), "лапа — основа");

            // Фазы 2–3 — как были: топот только по правилам («прижался» — 240 тиков подряд), касты в жребии.
            var two = Arena(distance: 3);
            MeleeOnly(two);
            ToPhase(two, 60);
            var later = new List<int>();
            for (int k = 0; k < 1800; k++)
            {
                HeroInFront(two, 3);
                two.Entities.Health[0] = two.Entities.MaxHealth[0];
                int tick = two.Tick;
                two.Step(InputFrame.Empty);
                if (Count(two, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp) > 0
                    && two.TryGetThicketMasterAction(Boss, out var s) && s.Action == ThicketMasterAction.Stomp && s.StartTick == tick) later.Add(tick);
            }
            for (int k = 1; k < later.Count; k++)
                Assert.That(later[k] - later[k - 1], Is.GreaterThanOrEqualTo(Simulation.ThicketStompHugTicks - 1), "фаза 2 — топот только «прижался»");
        }

        // ---------- темп ----------

        /// <summary>Начало атаки: Started со Stage 0 — серия лапы, топот (с кольцом), нырок, жест каста, буря — по одной.</summary>
        private static bool AttackStart(in SimEvent e)
        {
            if (e.Type != SimEventType.EnemyActionStarted || e.Source != Boss || e.Amount != 0) return false;
            switch ((EnemyActionKind)e.ActionVariant)
            {
                case EnemyActionKind.ThicketPaw:
                case EnemyActionKind.ThicketStomp:
                case EnemyActionKind.ThicketSprout:
                case EnemyActionKind.ThicketPollen:
                case EnemyActionKind.ThicketRain:
                case EnemyActionKind.ThicketStorm:
                case EnemyActionKind.ThicketDive:
                // Терновник (08.10) — тоже атака: с ближней полосы он редок (вес 2), но бывает.
                case EnemyActionKind.ThicketSeeds:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Стенд темпа: герой стоит в 3 м перед боссом (ставится каждый тик,
        /// не бьёт, здоровье доливается), здоровье босса держится на percent.
        /// Средний интервал между началами атак (серия лапы, топот с кольцом,
        /// нырок, жест каста, буря — по одной атаке) за ticks тиков после рёва порога;
        /// без бури (ревью 02.10, ночь) — промежутки, где буря одна из двух соседних атак, не в счёт.
        /// </summary>
        private static double MeanAttackSeconds(int percent, int ticks, out int attacks, out string mix)
            => MeanAttackSeconds(percent, ticks, out attacks, out mix, out _, out _);

        private static double MeanAttackSeconds(int percent, int ticks, out int attacks, out string mix, out double withoutStorm)
            => MeanAttackSeconds(percent, ticks, out attacks, out mix, out withoutStorm, out _);

        /// <summary>withoutLong — ещё и без нырка (03.10): промежутки, где буря или нырок одна из двух соседних атак, не в счёт.</summary>
        private static double MeanAttackSeconds(int percent, int ticks, out int attacks, out string mix, out double withoutStorm,
            out double withoutLong)
        {
            var sim = Arena(distance: 3);
            var e = sim.Entities;
            var away = new FixVec2(-Fix64.One, Fix64.Zero);
            int max = e.MaxHealth[Boss];
            void Hold()
            {
                e.Position[0] = e.Position[Boss] + away * Fix64.FromInt(3);
                e.Health[0] = e.MaxHealth[0];
                if (sim.Tick >= IntroDone) e.Health[Boss] = max * percent / 100;
            }
            while (sim.Tick < IntroDone + 90) { Hold(); sim.Step(InputFrame.Empty); }
            int first = -1, last = -1, previous = -1, gapsTicks = 0, gaps = 0, shortTicks = 0, shorts = 0;
            bool previousStorm = false, previousLong = false;
            attacks = 0;
            var kinds = new SortedDictionary<string, int>();
            int end = sim.Tick + ticks;
            while (sim.Tick < end)
            {
                Hold();
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var ev in sim.Events)
                {
                    if (!AttackStart(ev)) continue;
                    if (first < 0) first = tick;
                    last = tick;
                    attacks++;
                    bool storm = ev.ActionVariant == (int)EnemyActionKind.ThicketStorm;
                    bool isLong = storm || ev.ActionVariant == (int)EnemyActionKind.ThicketDive;
                    if (previous >= 0 && !storm && !previousStorm) { gapsTicks += tick - previous; gaps++; }
                    if (previous >= 0 && !isLong && !previousLong) { shortTicks += tick - previous; shorts++; }
                    previous = tick;
                    previousStorm = storm;
                    previousLong = isLong;
                    string name = ((EnemyActionKind)ev.ActionVariant).ToString().Replace("Thicket", "");
                    kinds[name] = kinds.TryGetValue(name, out int n) ? n + 1 : 1;
                }
            }
            mix = string.Join(" ", System.Linq.Enumerable.Select(kinds, kv => kv.Key + " " + kv.Value));
            withoutStorm = gaps == 0 ? double.MaxValue : gapsTicks / (double)gaps / Simulation.TicksPerSecond;
            withoutLong = shorts == 0 ? double.MaxValue : shortTicks / (double)shorts / Simulation.TicksPerSecond;
            return attacks < 2 ? double.MaxValue : (last - first) / (double)(attacks - 1) / Simulation.TicksPerSecond;
        }

        [Test]
        public void Cadence_PassiveHeroAtThreeMetres_PhaseByPhase_BuildsUp()
        {
            const int ticks = 3600;
            double p1 = MeanAttackSeconds(100, ticks, out int n1, out string m1, out double s1, out double l1);
            double p2 = MeanAttackSeconds(58, ticks, out int n2, out string m2, out double s2, out double l2);
            double p2b = MeanAttackSeconds(42, ticks, out int n2b, out string m2b, out double s2b, out double l2b);
            double p3 = MeanAttackSeconds(20, ticks, out int n3, out string m3, out double s3, out double l3);
            TestContext.WriteLine("phase 1: " + p1.ToString("0.00") + " s (" + n1 + "), phase 2 at 58%: " + p2.ToString("0.00")
                + " s (" + n2 + "), at 42%: " + p2b.ToString("0.00") + " s (" + n2b + "), phase 3: " + p3.ToString("0.00")
                + " s (" + n3 + ")");
            TestContext.WriteLine("without storm: " + s1.ToString("0.00") + " / " + s2.ToString("0.00") + " / " + s2b.ToString("0.00")
                + " / " + s3.ToString("0.00") + " s");
            TestContext.WriteLine("without storm and dive: " + l1.ToString("0.00") + " / " + l2.ToString("0.00") + " / " + l2b.ToString("0.00")
                + " / " + l3.ToString("0.00") + " s");
            TestContext.WriteLine("mix 1: " + m1 + " | 2 at 58%: " + m2 + " | 2 at 42%: " + m2b + " | 3: " + m3);
            // Ревью 02.10, ночь: «темп не строится — фаза 1 вялая, фаза 3 медленнее второй». Герой стоит
            // под лапой: серия за серией, отдых 30 / 20 / 12 (×0,85 с половины здоровья; проверка находок
            // 03.10 — темп, сыгранный владельцем с Костей; баланс 02.10, ночь — было 46 / 34 / 36), окно 30
            // (после серии — не меньше 13 тиков простоя), топот «прижался» раз в 240 тиков. Серия фазы 1 —
            // 17 + 10–12 + 1 + 30 ≈ 59 тиков (2,0 с); фазы 2 — 2–3 удара + 20 и касты; фазы 3 — 3 удара + окно,
            // ливень по сроку. Стенд боя (сильный игрок, без бури): 2,36 / 2,03 / 1,59 с; здесь герой не
            // уходит — быстрее, но порядок тот же: каждая фаза быстрее прошлой, без бури.
            // 03.10 («участить нырок и аое в ближнем бою»): в фазе 1 топот в жребии рядом с лапой и нырок
            // «под героя» раз в 15 с, в фазах 2–3 нырок раз в 13 / 11 с. Отдых, замахи и окно — прежние,
            // простой босса не вырос (на стенде боя 29% → 24%), но топот (42 + 15 + 2) и нырок (54 + 36)
            // длиннее серии (17 + 10–12 + 1) — между НАЧАЛАМИ атак дальше: здесь 2,0–2,4 → ≈ 2,5 с в фазе 1,
            // 1,8 → ≈ 1,9 с в фазе 3 (стенд боя: 2,36 / 2,03 / 1,59 → 2,64 / 2,18 / 1,83 с).
            Assert.That(s1, Is.InRange(2.2, 2.8), "фаза 1");
            Assert.That((s2 + s2b) / 2, Is.InRange(1.6, 2.1), "фаза 2");
            Assert.That(s3, Is.InRange(1.45, 2.0), "фаза 3");
            // Порядок — без бури и нырка (03.10): нырок (54 под землёй + 36 стойки) идёт по сроку во всех фазах,
            // у героя-столба фазы 3 — столько же нырков, сколько в фазе 2, и они съедали разницу (1,91 против 1,89 с).
            Assert.That((l2 + l2b) / 2, Is.LessThan(l1), "фаза 2 быстрее первой");
            Assert.That(l3, Is.LessThan((l2 + l2b) / 2), "фаза 3 быстрее второй (без бури и нырка)");
            Assert.That((s2 + s2b) / 2, Is.LessThan(s1), "фаза 2 быстрее первой (без бури)");
            StringAssert.Contains("Stomp", m1, "фаза 1 — не одна лапа: топот");
            StringAssert.Contains("Dive", m1, "фаза 1 — нырок и у того, кто стоит рядом");
        }

        // ---------- лапа — основа, темп, дальники (ревью 02.10, ночь) ----------

        [Test]
        public void Bands_FromTheHullEdgeToTheHeroCentre_Near14_Far65_FarAlsoBeyondTheLeash()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            var e = sim.Entities;
            var boss = e.Position[Boss];
            var forward = e.Facing[Boss].Normalized();
            var left = new FixVec2(-forward.Y, forward.X);
            Fix64 chest = sim.ThicketHullGap(Boss, boss + forward * Fix64.FromInt(3)) - Fix64.FromInt(3);   // −2,74
            void Expect(FixVec2 p, ThicketBand band, string what)
            {
                e.Position[0] = p;
                Assert.AreEqual(band, sim.ThicketHeroBand(Boss), what + ", зазор " + sim.ThicketHullGap(Boss, p).ToDouble().ToString("0.00"));
            }
            Expect(boss + forward * Fix64.Ratio(40, 10), ThicketBand.Near, "перед мордой 4,0 м (1,26 от груди)");
            Expect(boss + forward * Fix64.Ratio(42, 10), ThicketBand.Mid, "4,2 м (1,46 от груди)");
            Expect(boss + forward * Fix64.Ratio(92, 10), ThicketBand.Mid, "9,2 м (6,46)");
            Expect(boss + forward * Fix64.Ratio(93, 10), ThicketBand.Far, "9,3 м (6,56)");
            Expect(boss + left * Fix64.FromInt(8), ThicketBand.Mid, "сбоку 8 м — от края лапы 6,0");
            Expect(boss + left * Fix64.FromInt(9), ThicketBand.Far, "сбоку 9 м — 7,0");
            Expect(boss - forward * Fix64.FromInt(9), ThicketBand.Far, "за хвостом 9 м — 6,96");
            Assert.That(chest.ToDouble(), Is.EqualTo(-2.74).Within(0.01), "грудь — 2,74 м от центра");
            Assert.AreEqual(1.4, Simulation.ThicketNearGap.ToDouble(), 1e-6);
            Assert.AreEqual(6.5, Simulation.ThicketFarGap.ToDouble(), 1e-6);
            // За поводком дальше лапы (пешком не достать) — тоже дальняя, хоть босс и рядом.
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            e.Position[Boss] = memory.Home + At(-9.9, 0);
            Expect(memory.Home + At(-14.1, 0), ThicketBand.Far, "за поводком в 4,2 м от босса");
        }

        /// <summary>Точка в осях босса: degrees от взгляда (+ — влево), metres от центра.</summary>
        private static FixVec2 Beside(Simulation sim, double degrees, double metres)
        {
            var e = sim.Entities;
            var forward = e.Facing[Boss].Normalized();
            var left = new FixVec2(-forward.Y, forward.X);
            double a = degrees * System.Math.PI / 180;
            return e.Position[Boss] + (forward * Fix64.FromDouble(System.Math.Cos(a)) + left * Fix64.FromDouble(System.Math.Sin(a)))
                * Fix64.FromDouble(metres);
        }

        [Test]
        public void Paw_HeroAtTheSideOrInTheArmpit_FirstStrikePivotsUpTo59AndAHalf_ForepawsLiftedDoNotCarryHimOut()
        {
            // Сбоку у передней лапы (60° от взгляда, 3,1 м): раньше удар шёл вдоль взгляда (±40° — мимо),
            // босс крутился за героем, а бот и игрок в это время уходили «под мышку» и их бил только топот.
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var e = sim.Entities;
            var forward = e.Facing[Boss].Normalized();
            var spot = Beside(sim, 60, 3.1);
            e.Position[0] = spot;
            Assert.That(sim.ThicketHullGap(Boss, spot).ToDouble(), Is.GreaterThan(0.45), "у лапы тело помещается");
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 3);
            Assert.AreEqual(IntroDone, start, "серия — сразу, без разворота");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            // В тик конца рёва вступления корпус ещё доворачивал к герою (≤ 2,5°); удар — от взгляда в тик начала серии.
            var opened = e.Facing[Boss].Normalized();
            Assert.That(FixVec2.Dot(opened, forward).ToDouble(), Is.GreaterThan(System.Math.Cos(2.6 * System.Math.PI / 180)),
                "корпус не прыгнул к удару");
            // Баланс 02.10, ночь: доворот первого удара — к герою, но не дальше 17 × 3,5° = 59,5° (было 42,5°).
            double pivot = System.Math.Acos(System.Math.Min(1, FixVec2.Dot(opened, paw.Direction.Normalized()).ToDouble())) * 180 / System.Math.PI;
            Assert.AreEqual(595, Simulation.ThicketPawOpeningDecidegrees);
            Assert.That(pivot, Is.LessThanOrEqualTo(Simulation.ThicketPawOpeningDecidegrees / 10.0 + 0.2), "доворот первого удара — не больше 59,5°");
            Assert.That(pivot, Is.GreaterThan(55), "к герою (он в ~58° от взгляда)");
            Assert.IsTrue(Simulation.TelegraphContains(Simulation.ThicketPawStrikeSector(paw.Origin, paw.Direction, 0), spot, Fix64.Zero),
                "центр героя в секторе удара");
            Assert.AreEqual(paw.StartTick + Simulation.ThicketPawWindupTicks, paw.ImpactTick, "замах не растёт");
            // Корпус доворачивается 3,5° за тик (ход идёт раньше контакта в том же тике) и к контакту смотрит по удару.
            Until(sim, paw.ImpactTick);
            e.Position[0] = spot;
            int health = e.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Dot(e.Facing[Boss].Normalized(), paw.Direction.Normalized()).ToDouble(), Is.GreaterThan(0.9999),
                "к контакту корпус смотрит по удару");
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw, true), "достал");
            Assert.Less(e.Health[0], health);

            // «Под мышкой» с обеих сторон (±72°, 1,85 м — между лапой и бедром): герой стоит и бьёт (ввода
            // нет, его не переставляют). Раньше корпус, доворачивая к удару, лапой возил его вперёд себя —
            // из сектора, удар не попадал никогда. Теперь лапа достаёт (доворот до 59,5°), а передние лапы
            // в замахе подняты (ThicketPawsLifted) — к контакту он в секторе и ранен.
            foreach (double side in new[] { 72.0, -72.0 })
            {
                var deep = Arena();
                MeleeOnly(deep);
                Until(deep, IntroDone);
                var at = Beside(deep, side, 1.85);
                Assert.That(deep.ThicketHullGap(Boss, at).ToDouble(), Is.GreaterThanOrEqualTo(0.45), side + "°: тело помещается");
                deep.Entities.Position[0] = at;
                Assert.AreEqual(IntroDone, RunUntilStarted(deep, EnemyActionKind.ThicketPaw, 3), side + "°: лапа достаёт «подмышку»");
                Assert.IsTrue(deep.ThicketPawsLifted(Boss), "в замахе передние лапы подняты");
                Assert.IsTrue(deep.TryGetThicketMasterAction(Boss, out var series));
                int before = deep.Entities.Health[0];
                double moved = 0;
                while (deep.Tick <= series.ImpactTick)
                {
                    deep.Step(InputFrame.Empty);
                    moved = System.Math.Max(moved, Metres(deep.Entities.Position[0], at));
                }
                Assert.IsFalse(deep.ThicketPawsLifted(Boss) && deep.TryGetThicketMasterAction(Boss, out var now) && now.Stage == 0,
                    "после контакта первая лапа опущена");
                TestContext.WriteLine(side + "°: moved by the turning body " + moved.ToString("0.00") + " m");
                // Талия и грудь его чуть отжимают наружу (до ~1 м), лапы — не возят.
                Assert.That(moved, Is.LessThan(1.2), side + "°: корпус не увёз героя из сектора");
                Assert.AreEqual(deep.ThicketPawStrikeDamageOf(Boss, 0), before - deep.Entities.Health[0], side + "°: первый удар попал");
            }

            // Строго за спиной (150°) лапа не достаёт — там топот «за спиной».
            var back = Arena();
            MeleeOnly(back);
            Until(back, IntroDone);
            back.Entities.Position[0] = Beside(back, 150, 2.5);
            Assert.AreEqual(-1, RunUntilStarted(back, EnemyActionKind.ThicketPaw, 3), "за спиной — не лапа");
        }

        [Test]
        public void Dive_OnlyAfterTwoSecondsInTheFarBand_MidBandHeroIsNeverDived_Cooldown540Then450_UndergroundGrowsWithThePath()
        {
            // Средняя полоса (8 м перед мордой, 5,26 м от груди) — 30 с: ни одного нырка (03.10: и «под героя»
            // нет — герой дальше круга топота 5,2 м, это не ближник).
            var mid = Arena(distance: 8);
            Only(mid, ThicketMasterAction.Dive);
            Until(mid, IntroDone);
            Assert.AreEqual(-1, RunUntilStarted(mid, EnemyActionKind.ThicketDive, 900), "средняя полоса — без нырка");

            // Дальняя (10 м): нырок через 60 тиков подряд; на тик ближе 5,5 м — счёт заново; у края полосы
            // (6,0 м от груди — ближе 6,5, но дальше 5,5) счёт не рвётся.
            var sim = Arena(distance: 9);
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            for (int k = 0; k < 40; k++) { HeroInFront(sim, 10); sim.Step(InputFrame.Empty); }
            Assert.AreEqual(40, sim.ThicketMasterFarTicks(Boss));
            HeroInFront(sim, 8.74);   // 6,0 от груди — в запасе 1 м
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(41, sim.ThicketMasterFarTicks(Boss), "у края полосы счёт не рвётся");
            HeroInFront(sim, 8.0);    // 5,26 — ближе запаса
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, sim.ThicketMasterFarTicks(Boss), "ближе 5,5 м — заново");
            int from = sim.Tick;
            int start = -1;
            for (int k = 0; k < 200 && start < 0; k++)
            {
                HeroInFront(sim, 10);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive) > 0) start = tick;
            }
            Assert.AreEqual(from + Simulation.ThicketDiveFarTicks - 1, start, "2 с подряд в дальней полосе");
            Assert.AreEqual(start + Simulation.ThicketDiveCooldownPhase1Ticks, sim.ThicketReadyTick(Boss, ThicketMasterAction.Dive), "фаза 1 — 18 с (баланс 02.10, ночь; было 12)");
            Assert.AreEqual(start + Simulation.ThicketDiveEveryPhase1Ticks, sim.ThicketDiveDueTick(Boss), "срок «под героя» — тоже от этого нырка (03.10)");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var farDive));
            Assert.IsFalse(Simulation.ThicketDiveUnderHero(farDive), "сближение — круг под героем в тик фиксации, как было");
            // Владелец 08.10 («перемещение под землёй оч быстрое»): бугор 7 м/с — 10 м за 43 тика (было 18 на любой путь).
            Assert.AreEqual(43, Simulation.ThicketDiveTravelOf(farDive), "10 м на 7 м/с");
            int shielded = 0;
            while (sim.ThicketShielded(Boss) || sim.Tick == start + 1) { shielded += sim.ThicketShielded(Boss) ? 1 : 0; sim.Step(InputFrame.Empty); }
            Assert.AreEqual(Simulation.ThicketDiveBurrowTicks + Simulation.ThicketDiveTravelOf(farDive) + Simulation.ThicketDiveLockTicks, shielded,
                "под землёй — уход 12 + ход по пути + круг 24");
            Assert.AreEqual(79, shielded, "10 м: 12 + 43 + 24 (до 08.10 — 54 на любой путь)");

            // Фазы 2–3 — 450 (15 с; было 300).
            var two = Arena(distance: 9);
            Only(two, ThicketMasterAction.Dive);
            ToPhase(two, 60);
            int dive2 = -1;
            for (int k = 0; k < 200 && dive2 < 0; k++)
            {
                HeroInFront(two, 10);
                int tick = two.Tick;
                two.Step(InputFrame.Empty);
                if (Count(two, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketDive) > 0) dive2 = tick;
            }
            Assert.AreNotEqual(-1, dive2);
            Assert.AreEqual(dive2 + Simulation.ThicketDiveCooldownTicks, two.ThicketReadyTick(Boss, ThicketMasterAction.Dive), "фазы 2–3 — 15 с");
        }

        [Test]
        public void Ranged_PhaseTwo_HeroKeepsAway_BossCastsAtRange_InsteadOfOnlyWalking()
        {
            // Дальник держится в 12 м (7,26 м от груди): нырок закрыт перезарядкой — босс идёт 2,8 м/с, а в
            // фазах 2–3 кладёт прорастание и пыльцу под героя, на любом расстоянии.
            var sim = Arena(distance: 9, walks: true);
            Only(sim, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen);
            ToPhase(sim, 60);
            int sprouts = 0, pollens = 0;
            for (int k = 0; k < 600; k++)
            {
                HeroInFront(sim, 12);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                sim.Step(InputFrame.Empty);
                sprouts += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout);
                pollens += Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPollen);
            }
            Assert.That(sprouts, Is.GreaterThan(0), "прорастание под дальника");
            Assert.That(pollens, Is.GreaterThan(0), "пыльца у дальника");

            // Фаза 1 (баланс 02.10, ночь — ответ дальникам, решение владельца): только «Корни-плеть» —
            // прорастание из 3 кругов, перезарядка издали 240; пыльцы нет.
            var one = Arena(distance: 9, walks: true);
            Only(one, ThicketMasterAction.Sprout, ThicketMasterAction.Pollen);
            Until(one, IntroDone);
            int lashes = 0, clouds = 0, last = -1;
            for (int k = 0; k < 600; k++)
            {
                HeroInFront(one, 12);
                one.Entities.Health[0] = one.Entities.MaxHealth[0];
                int tick = one.Tick;
                one.Step(InputFrame.Empty);
                clouds += Count(one, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPollen);
                foreach (var e in one.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketSprout && e.Amount == 0)
                    {
                        lashes++;
                        Assert.IsTrue(one.TryGetThicketHazard(Boss, out var h));
                        Assert.AreEqual(Simulation.ThicketSproutPhase1Circles, h.Stages, "фаза 1 — 3 круга");
                        Assert.AreEqual(tick + Simulation.ThicketSproutFarCooldownTicks, one.ThicketReadyTick(Boss, ThicketMasterAction.Sprout),
                            "перезарядка «издали» — 240");
                        if (last >= 0) Assert.That(tick - last, Is.GreaterThanOrEqualTo(Simulation.ThicketSproutFarCooldownTicks));
                        last = tick;
                    }
            }
            Assert.That(lashes, Is.InRange(2, 3), "«Корни-плеть» раз в 8 с");
            Assert.AreEqual(0, clouds, "пыльцы в фазе 1 нет");
        }

        [Test]
        public void Rain_PhaseThree_RightAfterTheRoar_BeforeTheStorm_ThenByCooldown_PawsUnderIt()
        {
            // Ревью 02.10, ночь: «ливня почти нет». Фаза 3: рёв 33 → ливень (буря ждёт конца его кругов) →
            // буря → ливень раз в 357 тиков (420 × 0,85) — за 25 с два и больше; под ним — серии лапы.
            var sim = Arena(distance: 3);
            Only(sim, ThicketMasterAction.Rain, ThicketMasterAction.Storm);
            Until(sim, IntroDone);
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            Assert.AreNotEqual(-1, RunUntilStarted(sim, EnemyActionKind.ThicketRoar, 10));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var roar));
            Until(sim, roar.EndTick);
            var rains = new List<int>();
            int storm = -1, pawsUnderRain = 0;
            for (int k = 0; k < 750; k++)
            {
                HeroInFront(sim, 3);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                bool raining = sim.TryGetThicketHazard(Boss, out var h) && h.Action == ThicketMasterAction.Rain;
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0 || e.Source != Boss) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketRain) rains.Add(tick);
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketStorm && storm < 0) storm = tick;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketPaw && raining) pawsUnderRain++;
                }
            }
            Assert.That(rains.Count, Is.GreaterThanOrEqualTo(2), "два ливня за 25 с фазы 3");
            Assert.AreEqual(roar.EndTick, rains[0], "первый — сразу после рёва 33");
            Assert.That(storm, Is.GreaterThan(rains[0]), "буря — после ливня");
            Assert.That(storm, Is.GreaterThan(rains[0] + Simulation.ThicketRainImpactTicks), "буря ждёт конца кругов ливня");
            Assert.That(rains[1] - rains[0], Is.GreaterThanOrEqualTo(Simulation.ThicketRainCooldownTicks * Simulation.ThicketEnragedCooldownPercent / 100));
            Assert.That(pawsUnderRain, Is.GreaterThan(0), "под ливнем — серии лапы");
        }

        // ---------- баланс 02.10, ночь («поймать баланс») ----------

        [Test]
        public void PawWindupAndAnswerWindow_EveryPhase_17_And_30()
        {
            // Проверка находок 03.10: баланс 02.10 (ночь) сжимал первый замах до 16 / 15 и окно до 26 / 22 в
            // фазах 2–3 — это отменяло просьбу владельца «замедлить тычку на 10%» и принятое окно 30. Во всех
            // фазах — замах 17 и окно 30; фазы растут длиной серии, связками, кастами и отдыхом.
            foreach (int percent in new[] { 100, 60, 30 })
            {
                var sim = Arena();
                MeleeOnly(sim);
                if (percent < 100) ToPhase(sim, percent); else Until(sim, IntroDone);
                const int windup = 17, window = 30;
                Assert.AreEqual(windup, sim.ThicketPawWindupOf(Boss), "замах, здоровье " + percent + "%");
                Assert.AreEqual(window, sim.ThicketWindowOf(Boss), "окно, здоровье " + percent + "%");
                HeroInFront(sim, 2.5);
                int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
                Assert.AreNotEqual(-1, start);
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
                Assert.AreEqual(start + windup, paw.ImpactTick);
                Until(sim, paw.EndTick + 1);
                Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
                Assert.AreEqual(paw.LastImpactTick + window, memory.QuietUntil);
            }
        }

        /// <summary>Герой идёт полным ходом по прямой мимо босса (в 9–12 м), пока босс не начнёт kind; тик или −1.</summary>
        private static int WalkPastUntilStarted(Simulation sim, ThicketMasterAction open, EnemyActionKind kind, int limit,
            out FixVec2 hero, out FixVec2 velocity)
        {
            var boss = sim.Entities.Position[Boss];
            hero = velocity = FixVec2.Zero;
            for (int k = 0; k < limit; k++)
            {
                // Каст открывается, когда герой уже разогнался (разгон — 3 тика).
                if (k == 20) sim.SetThicketReadyTick(Boss, open, 0);
                // Поперёк линии на босса, туда-обратно: от (−10, −5) к (−10, +5) и назад.
                double y = (k / 70) % 2 == 0 ? 5 : -5;
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                int tick = sim.Tick;
                sim.Step(Walk(boss + At(-10, y * 4)));
                hero = sim.Entities.Position[0];
                velocity = sim.Entities.Velocity[0];
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)kind && e.Amount == 0) return tick;
            }
            return -1;
        }

        [Test]
        public void LeadAim_SproutRainPollen_LandWhereTheRunningHeroWillBe_StandingHeroUnderFoot()
        {
            // «Дальник уходит из всего ногами»: круги каста — туда, где герой будет, если не свернёт:
            // прорастание и ливень — ход за 20 тиков, пыльца — за 12, не дальше 3 м. Бегущий прямо
            // попадает, свернувший или вставший — нет; стоящему — под ноги, как было.
            var sim = Arena();   // проснётся только в 9 м — героя отводят после рёва
            MeleeOnly(sim);
            ToPhase(sim, 60);
            sim.Entities.Position[0] = sim.Entities.Position[Boss] + At(-10, 0);
            Assert.AreNotEqual(-1, WalkPastUntilStarted(sim, ThicketMasterAction.Sprout, EnemyActionKind.ThicketSprout, 600, out var hero, out var v));
            Assert.That(v.Length.ToDouble(), Is.GreaterThan(0.14), "бежал полным ходом");
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 0, out var circle, out _, out _));
            var lead = (v * Fix64.FromInt(Simulation.ThicketSproutLeadTicks)).ClampLength(Simulation.ThicketLeadMax);
            Assert.That(Metres(circle, hero + lead), Is.LessThan(0.01), "круг 0 — на упреждении 20 тиков");
            Assert.That(Metres(circle, hero), Is.InRange(2.8, 3.01), "впереди на ~3 м (потолок)");
            Assert.That(FixVec2.Dot((circle - hero).Normalized(), v.Normalized()).ToDouble(), Is.GreaterThan(0.999), "по ходу");

            // Пыльца — упреждение 12 тиков (≈ 1,8 м); боковые облака — от него.
            var dust = Arena();   // проснётся только в 9 м — героя отводят после рёва
            MeleeOnly(dust);
            ToPhase(dust, 60);
            dust.Entities.Position[0] = dust.Entities.Position[Boss] + At(-10, 0);
            Assert.AreNotEqual(-1, WalkPastUntilStarted(dust, ThicketMasterAction.Pollen, EnemyActionKind.ThicketPollen, 600, out var at, out var w));
            Assert.That(w.Length.ToDouble(), Is.GreaterThan(0.14), "бежал полным ходом");
            FixVec2 first = default;
            int serial = int.MaxValue;
            for (int k = 0; k < Simulation.ThicketPollenZones; k++)
                if (dust.TryGetThicketPollenZone(k, out var z) && z.Serial < serial) { serial = z.Serial; first = z.Center; }
            var pollenLead = (w * Fix64.FromInt(Simulation.ThicketPollenLeadTicks)).ClampLength(Simulation.ThicketLeadMax);
            Assert.That(Metres(first, at + pollenLead), Is.LessThan(0.01), "облако — на упреждении 12 тиков");

            // Стоящий герой — круг под ним, как было.
            var still = Arena();   // проснётся только в 9 м — героя отводят после рёва
            MeleeOnly(still);
            ToPhase(still, 60);
            still.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            int cast = -1;
            for (int k = 0; k < 600 && cast < 0; k++)
            {
                HeroInFront(still, 10);
                still.Entities.Velocity[0] = FixVec2.Zero;
                int tick = still.Tick;
                still.Step(InputFrame.Empty);
                if (Count(still, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout) > 0) cast = tick;
            }
            Assert.AreNotEqual(-1, cast);
            Assert.IsTrue(still.TryGetThicketShape(Boss, 0, out var under, out _, out _));
            Assert.That(Metres(under, still.Entities.Position[0]), Is.LessThan(0.01), "стоящему — под ноги");
        }

        [Test]
        public void FloorClamp_RainAndPollen_EveryCircleOnTheFloor_HeroAgainstTheClearingEdge()
        {
            // Внешние круги ливня (5–5,6 м от героя) и боковые облака пыльцы (3,4 м) у кромки поляны ложились
            // мимо пола в 13–39% случаев (стенд boss-review-sim, дальник). Теперь — поворот вокруг центра
            // шаблона на ±20/40/60°, иначе ближайшее место на полу.
            int rainCircles = 0, clouds = 0;
            foreach (var axis in ClearingAxes)
            {
                var sim = Clearing(out int boss, out var map);
                Assert.IsTrue(sim.TryGetThicketMasterMemory(boss, out var memory));
                // Герой у кромки: по оси от центра — последняя точка на полу (тело помещается).
                FixVec2 edge = memory.Home;
                for (double r = 4; r < 14; r += 0.1)
                {
                    var p = memory.Home + axis.Normalized() * Fix64.FromDouble(r);
                    if (!map.IsWalkable(p, Fix64.FromDouble(0.6))) break;
                    edge = p;
                }
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Rain, 0);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Pollen, 0);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Storm, int.MaxValue / 2);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Dive, int.MaxValue / 2);
                sim.Entities.Health[boss] = sim.Entities.MaxHealth[boss] * 30 / 100;
                bool rained = false, dusted = false;
                for (int k = 0; k < 1500 && !(rained && dusted); k++)
                {
                    sim.Entities.Position[0] = edge;
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                    sim.Step(Walk(edge));
                    foreach (var e in sim.Events)
                    {
                        if (e.Type != SimEventType.EnemyActionStarted || e.Source != boss) continue;
                        if (e.ActionVariant == (int)EnemyActionKind.ThicketRain)
                        {
                            rained = true;
                            for (int c = 0; c < Simulation.ThicketRainCircles; c++)
                            {
                                int index = e.Amount * Simulation.ThicketRainCircles + c;
                                Assert.IsTrue(sim.TryGetThicketShape(boss, index, out var center, out _, out _));
                                Assert.IsTrue(map.IsWalkable(center, Fix64.Ratio(1, 5)), "круг ливня " + index + " на полу, ось " + axis);
                                rainCircles++;
                            }
                        }
                        if (e.ActionVariant == (int)EnemyActionKind.ThicketPollen && e.Amount == 0)
                        {
                            dusted = true;
                            for (int z = 0; z < Simulation.ThicketPollenZones; z++)
                                if (sim.TryGetThicketPollenZone(z, out var cloud) && cloud.StartTick == sim.Tick - 1)
                                {
                                    Assert.IsTrue(map.IsWalkable(cloud.Center, Fix64.Ratio(1, 5)), "облако на полу, ось " + axis);
                                    clouds++;
                                }
                        }
                    }
                }
                Assert.IsTrue(rained && dusted, "ливень и пыльца у кромки, ось " + axis);
            }
            Assert.That(rainCircles, Is.GreaterThanOrEqualTo(ClearingAxes.Length * Simulation.ThicketRainCircles * Simulation.ThicketRainVolleys));
            Assert.That(clouds, Is.GreaterThanOrEqualTo(ClearingAxes.Length * Simulation.ThicketPollenZones));
        }

        [Test]
        public void FarCasts_ShorterCooldowns_UnderThePawTheOldOnes()
        {
            // Дальнику (лапа не достаёт) — прорастание раз в 240 (было 450), пыльца раз в 180 (было 270);
            // ближнику, у которого касты в жребии с лапой, — прежние 450 и 270.
            var far = Arena();   // проснётся только в 9 м — героя отводят после рёва
            MeleeOnly(far);
            ToPhase(far, 60);
            far.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            int sprout = -1;
            for (int k = 0; k < 600 && sprout < 0; k++)
            {
                HeroInFront(far, 10);
                int tick = far.Tick;
                far.Step(InputFrame.Empty);
                if (Count(far, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout) > 0) sprout = tick;
            }
            Assert.AreNotEqual(-1, sprout);
            Assert.AreEqual(sprout + Simulation.ThicketSproutFarCooldownTicks, far.ThicketReadyTick(Boss, ThicketMasterAction.Sprout), "издали — 240");

            var near = Arena();
            MeleeOnly(near);
            ToPhase(near, 60);
            near.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            near.SetThicketReadyTick(Boss, ThicketMasterAction.Pollen, 0);
            int sproutNear = -1, pollenNear = -1;
            for (int k = 0; k < 3000 && (sproutNear < 0 || pollenNear < 0); k++)
            {
                HeroInFront(near, 2.5);
                near.Entities.Health[0] = near.Entities.MaxHealth[0];
                int tick = near.Tick;
                near.Step(InputFrame.Empty);
                foreach (var e in near.Events)
                {
                    if (e.Type != SimEventType.EnemyActionStarted || e.Amount != 0) continue;
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketSprout && sproutNear < 0)
                    {
                        sproutNear = tick;
                        Assert.AreEqual(tick + Simulation.ThicketSproutCooldownTicks, near.ThicketReadyTick(Boss, ThicketMasterAction.Sprout), "под лапой — 450");
                    }
                    if (e.ActionVariant == (int)EnemyActionKind.ThicketPollen && pollenNear < 0)
                    {
                        pollenNear = tick;
                        Assert.AreEqual(tick + Simulation.ThicketPollenCooldownTicks, near.ThicketReadyTick(Boss, ThicketMasterAction.Pollen), "под лапой — 270");
                    }
                }
            }
            Assert.AreNotEqual(-1, sproutNear, "прорастание в жребии с лапой");
            Assert.AreNotEqual(-1, pollenNear, "пыльца в жребии с лапой");
        }

        [Test]
        public void LyingPollen_HoldsOnlyAHeroUnderThePaw_FarHeroGetsTheNextCastUnderIt()
        {
            // Лежащая пыльца (до 4,8 с) держала босса на одних сериях лапы — дальника он 5 с только догонял.
            // Теперь держит лишь героя под лапой; дальнему под лежащими облаками — прорастание.
            var far = Arena();   // проснётся только в 9 м — героя отводят после рёва
            MeleeOnly(far);
            ToPhase(far, 60);
            far.SetThicketReadyTick(Boss, ThicketMasterAction.Pollen, 0);
            int pollen = -1;
            for (int k = 0; k < 600 && pollen < 0; k++)
            {
                HeroInFront(far, 10);
                int tick = far.Tick;
                far.Step(InputFrame.Empty);
                if (Count(far, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPollen) > 0) pollen = tick;
            }
            Assert.AreNotEqual(-1, pollen);
            int land = pollen + Simulation.ThicketPollenFallTicks;
            Until(far, land + 1);
            far.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            int sprout = -1;
            for (int k = 0; k < Simulation.ThicketPollenLifeTicks && sprout < 0; k++)
            {
                HeroInFront(far, 10);
                Assert.IsTrue(far.ThicketHazardActive(Boss) || sprout >= 0, "облака ещё лежат");
                int tick = far.Tick;
                far.Step(InputFrame.Empty);
                if (Count(far, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout) > 0) sprout = tick;
            }
            Assert.AreNotEqual(-1, sprout, "дальнему — каст под лежащей пыльцой");
            Assert.That(sprout, Is.LessThan(land + Simulation.ThicketPollenLifeTicks), "до того, как облака ушли");

            // Под лапой — как было: пока облака лежат, только серии.
            var near = Arena();
            MeleeOnly(near);
            ToPhase(near, 60);
            near.SetThicketReadyTick(Boss, ThicketMasterAction.Pollen, 0);
            int dust = -1;
            for (int k = 0; k < 3000 && dust < 0; k++)
            {
                HeroInFront(near, 2.5);
                near.Entities.Health[0] = near.Entities.MaxHealth[0];
                int tick = near.Tick;
                near.Step(InputFrame.Empty);
                if (Count(near, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPollen) > 0) dust = tick;
            }
            Assert.AreNotEqual(-1, dust);
            near.SetThicketReadyTick(Boss, ThicketMasterAction.Sprout, 0);
            int casts = 0;
            while (near.ThicketHazardActive(Boss))
            {
                HeroInFront(near, 2.5);
                near.Entities.Health[0] = near.Entities.MaxHealth[0];
                near.Step(InputFrame.Empty);
                casts += Count(near, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketSprout);
            }
            Assert.AreEqual(0, casts, "под лапой, пока лежат облака, — только серии");
        }

        [Test]
        public void StompThenPaw_PhasesTwoAndThree_HeroBackUnderThePaw_SeriesWithoutRest_StrikeNotBeforeTheWindow()
        {
            // «Топот → лапа»: кто после кольца стоит под лапой — серия сразу, мимо отдыха; удар — не раньше
            // окна ответа после кольца. Фаза 1 — без связки (Stomp_Hugged240InARow_*).
            foreach (int percent in new[] { 60, 30 })
            {
                var sim = Arena();
                MeleeOnly(sim);
                ToPhase(sim, percent);
                // Топот «за спиной» — 20 тиков в 3 м за спиной.
                int stompAt = HoldForStomp(sim, 120, behind: 3);
                Assert.AreNotEqual(-1, stompAt);
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
                // Ушёл из круга и вернулся к морде к концу кольца (под лапой).
                while (sim.Tick < stomp.EndTick)
                {
                    if (sim.Tick < stomp.LastImpactTick) HeroInFront(sim, 8); else HeroInFront(sim, 2.5);
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                    sim.Step(InputFrame.Empty);
                }
                HeroInFront(sim, 2.5);
                int pawAt = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
                Assert.AreNotEqual(-1, pawAt);
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
                int window = sim.ThicketWindowOf(Boss);
                Assert.AreEqual(stomp.LastImpactTick + window, paw.ImpactTick, "здоровье " + percent + "%: удар ровно на конце окна ответа");
                Assert.That(pawAt - stomp.EndTick, Is.InRange(0, Simulation.ThicketChainWaitTicks), "серия — связкой, не после отдыха");
                Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var chained));
                Assert.AreEqual(ThicketMasterAction.Paw, chained.ChainNext, "связка «топот → лапа»");
            }
        }

        // ---------- детерминизм ----------

        [Test]
        public void BossArena_TwoRuns_SameHashAndEventsEveryTick()
        {
            var a = new Run(Run.Ticks);
            var b = new Run(Run.Ticks);
            Assert.AreEqual(a.Sim.StateHash(), b.Sim.StateHash(), "разошлись уже при расстановке");
            for (int t = 0; t < Run.Ticks; t++)
            {
                a.Step(t);
                b.Step(t);
                Assert.AreEqual(a.Sim.StateHash(), b.Sim.StateHash(), "StateHash разошёлся на тике " + t);
                Assert.AreEqual(a.TickEvents, b.TickEvents, "события разошлись на тике " + t);
            }
            TestContext.WriteLine("paws " + a.Paws + ", paw hits " + a.PawHits + ", stomps " + a.Stomps + ", roars " + a.Roars
                + ", waves " + a.Sim.BossAddWavesSpawned + ", dives " + a.Dives + ", sprouts " + a.Sprouts
                + ", pollens " + a.Pollens + ", rains " + a.Rains + ", storms " + a.Storms + " (wave hits " + a.StormHits + ")"
                + ", hash " + a.Sim.StateHash().ToString("X16"));
            Assert.That(a.Paws, Is.GreaterThan(0), "ни одной лапы");
            Assert.That(a.Stomps, Is.GreaterThan(0), "ни одного топота");
            Assert.That(a.Roars, Is.GreaterThanOrEqualTo(4), "вступление и три порога");
            Assert.AreEqual(1, a.Sim.BossAddWavesSpawned, "одна волна подмоги");
            Assert.That(a.Dives, Is.GreaterThan(0), "ни одного нырка");
            Assert.That(a.Sprouts, Is.GreaterThan(0), "ни одного прорастания");
            Assert.That(a.Pollens, Is.GreaterThan(0), "ни одной пыльцы");
            Assert.That(a.Rains, Is.GreaterThan(0), "ни одного ливня");
            Assert.That(a.Storms, Is.GreaterThan(0), "ни одной бури");
        }

        /// <summary>Один прогон: арена босса с включённым переключателем и сценарий героя — функция тика.</summary>
        private sealed class Run
        {
            public const int Ticks = 4500;
            private const ulong Seed = 31;
            public readonly Simulation Sim;
            private readonly LayoutMap _map;
            private readonly int _boss;
            public int Paws, PawHits, Stomps, Roars, Dives, Sprouts, Pollens, Rains, Storms, StormHits;
            public ulong TickEvents;

            private readonly int _ticks;

            public Run(int ticks)
            {
                _ticks = ticks;
                var location = ArenaEncounterTests.ForestLocation();
                _map = ArenaEncounterTests.ArenaMap(location, 9, Seed);
                Sim = new Simulation(Seed, 512) { ThicketMasterBossEnabled = true };
                Sim.ApplyHeroBaseline();
                var plan = location.GetLevel(9).Spawn(Sim, _map, Seed, null, 9);
                Sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(9);
                Sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(9);
                _boss = plan.BossId;
            }

            /// <summary>Доля здоровья босса по сценарию: пороги 66, 50, 33 — в заданные тики.</summary>
            private int BossPercent(int tick)
                => tick * 15 < _ticks * 4 ? 100 : tick * 15 < _ticks * 7 ? 64 : tick * 15 < _ticks * 9 ? 48 : 31;

            public void Step(int tick)
            {
                var e = Sim.Entities;
                const int hero = Simulation.PlayerId;
                var input = InputFrame.Empty;
                if (e.Alive[_boss])
                {
                    // По 120 тиков: перед мордой 2,5 м, сбоку 3,4 м, под крупом, в дальнем от босса углу
                    // поляны (дальняя полоса — нырок, ревью 02.10, ночь; было «в 7 м»).
                    var boss = e.Position[_boss];
                    var f = e.Facing[_boss].Normalized();
                    var side = new FixVec2(-f.Y, f.X);
                    FixVec2 offset;
                    switch ((tick / 120) % 4)
                    {
                        case 0: offset = f * Fix64.Ratio(5, 2); break;
                        case 1: offset = side * Fix64.Ratio(17, 5); break;
                        case 2: offset = -f * Fix64.Ratio(21, 10); break;
                        default: offset = Farthest(boss, e.BodyRadius[hero]) - boss; break;
                    }
                    if (tick < 100) offset = f * Fix64.FromInt(6);
                    if (tick % 30 == 0)
                        e.Position[hero] = _map.ClampToWalkable(boss + offset, e.BodyRadius[hero]);
                    input.Flags = (byte)InputFlags.Attack;
                    input.AttackTarget = _boss;
                    input.Aim = boss;
                }

                // Фаза 3 сценария: прорастание и пыльца закрыты, чтобы ливень точно выпал (одинаково в обоих
                // прогонах). Пыльца — с вступления-кат-сцены (02.10): бой начинается на 75 тиков раньше, и
                // взвешенный выбор фазы 3 без этого уходил в пыльцу и лапу.
                if (tick * 15 == _ticks * 9)
                {
                    Sim.SetThicketReadyTick(_boss, ThicketMasterAction.Sprout, int.MaxValue / 2);
                    Sim.SetThicketReadyTick(_boss, ThicketMasterAction.Pollen, int.MaxValue / 2);
                }

                Sim.Step(input);

                ulong h = 14695981039346656037UL;
                foreach (var ev in Sim.Events)
                {
                    h = Mix(h, (ulong)ev.Type); h = Mix(h, (ulong)(uint)ev.Source); h = Mix(h, (ulong)(uint)ev.Target);
                    h = Mix(h, (ulong)(uint)ev.Amount); h = Mix(h, ev.Flag ? 1UL : 0UL);
                    h = Mix(h, (ulong)ev.Position.X.Raw); h = Mix(h, (ulong)ev.Position.Y.Raw);
                    h = Mix(h, (ulong)(uint)ev.ActionVariant);
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketPaw) Paws++;
                    if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.ThicketPaw && ev.Flag) PawHits++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketStomp) Stomps++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketRoar) Roars++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketDive && ev.Amount == 0) Dives++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketSprout) Sprouts++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketPollen) Pollens++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketRain) Rains++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.ThicketStorm && ev.Amount == 0) Storms++;
                    if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.ThicketStorm && ev.Flag) StormHits++;
                }
                TickEvents = h;

                // Долив — после шага и одинаково в обоих прогонах.
                e.Health[hero] = e.MaxHealth[hero];
                if (e.Alive[_boss]) e.Health[_boss] = e.MaxHealth[_boss] * BossPercent(tick) / 100;
            }

            /// <summary>Самая дальняя от босса точка пола из 8 направлений по 14 м.</summary>
            private FixVec2 Farthest(FixVec2 boss, Fix64 body)
            {
                FixVec2 best = boss;
                Fix64 bestSq = Fix64.Zero;
                for (int k = 0; k < 8; k++)
                {
                    var p = _map.ClampToWalkable(boss + FixVec2.FromAngle(Fix64.TwoPi * k / 8) * Fix64.FromInt(14), body);
                    var d = FixVec2.DistanceSq(p, boss);
                    if (d > bestSq) { bestSq = d; best = p; }
                }
                return best;
            }

            private static ulong Mix(ulong h, ulong v)
            {
                for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
                return h;
            }
        }
    }
}
