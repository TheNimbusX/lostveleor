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
    /// лапы (16 → 10); топот — только после 60 тиков ПОДРЯД ближе 4 м.
    /// Баланс 02.10 (artifacts/tools/boss-tune): ход 2,0 м/с, стойка после нырка 36,
    /// перезарядка топота 150, база лапы 25 → 10 (весь урон ×0,4), здоровье 6000 → 6600.
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
            ThicketMasterAction.Storm,
        };

        /// <summary>Из нырка, кастов этапа 2 и бури готовы только allowed; прочие — на перезарядке до конца теста.</summary>
        private static void Only(Simulation sim, params ThicketMasterAction[] allowed)
        {
            foreach (var action in Specials)
                if (System.Array.IndexOf(allowed, action) < 0) sim.SetThicketReadyTick(Boss, action, int.MaxValue / 2);
        }

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
        public void Paw_PhaseOneSeriesOfTwo_Windup15_NextNineLater_Window30_FirstForTheLeafDamage_SecondSixtyPercent()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            HeroInFront(sim, 2.5);
            int health = sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw), "лапа сразу после вступления");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(ThicketMasterAction.Paw, paw.Action);
            Assert.AreEqual(IntroDone, paw.StartTick);
            Assert.AreEqual(IntroDone + 15, paw.ImpactTick, "первый замах 15");
            Assert.AreEqual(IntroDone + 15 + 9, paw.LastImpactTick, "второй удар — через 9");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick, "кончается кадром контакта");
            Assert.AreEqual(2, paw.Stages, "фаза 1 — серия из двух");
            Assert.AreEqual(0, paw.Stage);
            Assert.IsTrue(MarkOf(sim, paw.TelegraphSerial, out var sector));
            Assert.AreEqual(TelegraphShape.Sector, sector.Shape);
            Assert.IsFalse(sector.SharedView, "знак лапы — на теле, не на земле");
            Assert.AreEqual(Simulation.ThicketPawRadius, sector.Radius);
            Assert.AreEqual(Simulation.ThicketPawArcCos, sector.ArcCos);
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss), "в замахе босс держит крупный жетон");

            while (sim.Tick < paw.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(health, sim.Entities.Health[0], "урон до контакта, тик " + (sim.Tick - 1));
            }
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw, true));
            Assert.AreEqual(16, sim.ThicketPawDamageOf(Boss), "лапа на арене 9");
            Assert.AreEqual(16, health - sim.Entities.Health[0]);
            Assert.IsFalse(ForcedMotion.IsActive(sim.Entities, 0), "лапа не отбрасывает");
            // Знак второго удара — в тик первого, за 9 до своего удара.
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
            Assert.IsTrue(sim.ThicketMasterHoldsBigToken(Boss), "серия держит жетон до последнего удара");

            Until(sim, paw.LastImpactTick + 1);
            // «Мягче серии» (02.10): второй удар — 60% лапы, 16 → 10.
            Assert.AreEqual(16 + 10, health - sim.Entities.Health[0], "первый 16, второй 10");
            Assert.IsFalse(sim.ThicketMasterHoldsBigToken(Boss));

            Until(sim, paw.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(paw.EndTick + Simulation.ThicketRestPhase1Ticks, memory.NextActionTick, "отдых фазы 1 — 18");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketWindowTicks, memory.QuietUntil, "окно ответа 30");
            // Следующая серия: замах — после отдыха, удар — не раньше конца окна.
            HeroInFront(sim, 2.5);
            int next = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreEqual(paw.EndTick + Simulation.ThicketRestPhase1Ticks, next, "серия раз в 24 + 1 + 18 = 43 тика (1,4 с)");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var again));
            Assert.That(again.ImpactTick - paw.LastImpactTick, Is.GreaterThanOrEqualTo(Simulation.ThicketWindowTicks), "окно ответа");
        }

        [Test]
        public void Paw_SeriesLengthByPhase_TwoThenTwoOrThreeThenThree_SignsNineAhead_ReaimWithinTheTurn()
        {
            foreach (int percent in new[] { 100, 60, 30 })
            {
                var sim = Arena();
                MeleeOnly(sim);
                if (percent < 100) ToPhase(sim, percent); else Until(sim, IntroDone);
                var seen = new HashSet<int>();
                var signs = new Dictionary<int, int>();
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
                        if (e.Type == SimEventType.EnemyActionImpact)
                            Assert.That(tick - signs[e.Amount], Is.GreaterThanOrEqualTo(9), "знак удара не меньше чем за 9");
                    }
                    if (sim.TryGetThicketMasterAction(Boss, out var a) && a.Action == ThicketMasterAction.Paw)
                    {
                        if (a.Stage == 0 && lastStage != 0) seen.Add(a.Stages);
                        if (a.Stage > 0 && a.Stage != lastStage)
                        {
                            double dot = FixVec2.Dot(lastDirection, a.Direction).ToDouble();
                            Assert.That(dot, Is.GreaterThanOrEqualTo(System.Math.Cos(40.5 * System.Math.PI / 180) - 1e-3),
                                "доворот удара серии — в пределах поворота за 9 тиков");
                        }
                        lastDirection = a.Direction;
                        lastStage = a.Stage;
                    }
                    else lastStage = -1;
                }
                string phase = "здоровье " + percent + "%";
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

        [Test]
        public void Paw_HardAndEnragedOverSixty_WindupThirty_DamageKept()
        {
            // «Сложно» 125% и ярость RiftRun ×1,3: лапа 16 → 20 → 26 на арене 9 (баланс 02.10) —
            // до порога не доходит. Правило дока (больше 60 урона — замах от 30 тиков;
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
            Assert.AreEqual(26, sim.ThicketPawDamageOf(Boss), "лапа «Сложно» в ярости на арене 9");
            Assert.AreEqual(Simulation.ThicketPawWindupTicks, sim.ThicketPawWindupOf(Boss), "до 60 не дошла — замах 15");
            e.Stats[Boss].SetBase(StatType.Damage, Fix64.FromInt(66));
            e.RefreshStats(Boss);
            int damage = sim.ThicketPawDamageOf(Boss);
            Assert.That(damage, Is.GreaterThan(Simulation.ThicketPawHeavyDamage), "тяжёлая лапа стенда");
            Assert.AreEqual(Simulation.ThicketPawHeavyWindupTicks, sim.ThicketPawWindupOf(Boss));

            // Фаза 3 — серия из трёх: первый замах 30, дальше удары через 9.
            ToPhase(sim, 30);
            HeroInFront(sim, 2.5);
            int health = e.Health[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreNotEqual(-1, start);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var paw));
            Assert.AreEqual(3, paw.Stages);
            Assert.AreEqual(start + 30, paw.ImpactTick, "первый замах — 30");
            Assert.AreEqual(paw.ImpactTick + 2 * 9, paw.LastImpactTick, "дальше — через 9");
            Until(sim, paw.ImpactTick);
            Assert.AreEqual(health, e.Health[0], "до контакта урона нет");
            Until(sim, paw.ImpactTick + 1);
            Assert.AreEqual(damage, health - e.Health[0], "первый удар — лапа целиком: урон не срезан");
            Until(sim, paw.LastImpactTick + 1);
            // «Мягче серии» (02.10): второй и третий — 60% лапы, и «Сложно» с яростью растят их тоже.
            int followUp = sim.ThicketPawStrikeDamageOf(Boss, 1);
            Assert.AreEqual((damage * 60 + 50) / 100, followUp, "доля 60% от тяжёлой лапы");
            Assert.AreEqual(followUp, sim.ThicketPawStrikeDamageOf(Boss, 2));
            Assert.AreEqual(damage + 2 * followUp, health - e.Health[0], "первый целиком, два следующих по 60%");

            // Обычный маршрут — лапа 16, замах 15.
            Assert.AreEqual(Simulation.ThicketPawWindupTicks, Arena().ThicketPawWindupOf(Boss));
        }

        // ---------- топот ----------

        [Test]
        public void Stomp_AfterSixtyNearTicks_Windup24_Circle52_DamageShare_KnockbackTwoMetres_RingSparesTheHurt()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            // 3,85 м: ближе 4 (окно топота), но дальше 3,68 — лапа не достаёт.
            HeroInFront(sim, 3.85);
            int health = sim.Entities.Health[0];
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 200);
            Assert.AreEqual(IntroDone + Simulation.ThicketStompNearTicks - 1, start, "60-й тик рядом");
            Assert.AreEqual(0, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw));
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            Assert.AreEqual(2, stomp.Stages, "круг и кольцо");
            Assert.AreEqual(start + 24, stomp.ImpactTick);
            Assert.AreEqual(start + 24 + 15, stomp.LastImpactTick, "кольцо — через 15 после круга");
            Assert.AreEqual(stomp.LastImpactTick + Simulation.ThicketStompStrikeTicks, stomp.EndTick, "кончается кадром контакта кольца");
            Assert.IsTrue(MarkOf(sim, stomp.TelegraphSerial, out var circle));
            Assert.AreEqual(TelegraphShape.Circle, circle.Shape);
            Assert.IsTrue(circle.SharedView);
            Assert.AreEqual(Simulation.ThicketStompRadius, circle.Radius);
            Assert.AreEqual(1, sim.BigMarkLoad(out _), "топот — крупная метка весом 1");
            Assert.AreEqual(0, sim.ThicketMasterNearTicks(Boss), "окно начинается заново");

            Until(sim, stomp.ImpactTick);
            Assert.AreEqual(health, sim.Entities.Health[0]);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketStomp, true));
            Assert.AreEqual(24, sim.ThicketStompDamageOf(Boss), "доля 62/41 от 16 на арене 9");
            Assert.AreEqual(24, health - sim.Entities.Health[0]);
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
        }

        [Test]
        public void Stomp_SecondRing_FifteenAfter_FivePointTwoToSevenPointFive_ThreeQuartersDamage_Knockback()
        {
            var sim = Arena();
            Until(sim, IntroDone);
            HeroInFront(sim, 3.85);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 200);
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
            Assert.AreEqual(18, sim.ThicketStompRingDamageOf(Boss), "24 × 0,75 на арене 9");
            Assert.AreEqual(18, health - sim.Entities.Health[0]);
            Assert.IsTrue(ForcedMotion.IsActive(sim.Entities, 0), "отброс — как у топота");
            for (int k = 0; k < Simulation.ThicketKnockbackTicks; k++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(sim.Entities.Position[0], sim.Entities.Position[Boss]).ToDouble(),
                Is.EqualTo(8.5).Within(0.05));

            // Внутри круга после первого удара — укрыт от кольца (рывок внутрь).
            var inner = Arena();
            Until(inner, IntroDone);
            HeroInFront(inner, 3.85);
            Assert.AreNotEqual(-1, RunUntilStarted(inner, EnemyActionKind.ThicketStomp, 200));
            Assert.IsTrue(inner.TryGetThicketMasterAction(Boss, out var second));
            HeroInFront(inner, 6.5);
            Until(inner, second.ImpactTick + 1);
            HeroInFront(inner, 2.0);
            health = inner.Entities.Health[0];
            Until(inner, second.LastImpactTick + 1);
            Assert.AreEqual(health, inner.Entities.Health[0], "внутри кольца — укрыт");
        }

        // ---------- «Мягче серии и топот» (владелец 02.10) ----------

        [Test]
        public void SoftSeries_PhaseThree_StrikeByStrike_16_Then10_Then10()
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
            for (int strike = 0; strike < paw.Stages; strike++)
            {
                int impact = paw.ImpactTick + strike * Simulation.ThicketPawSeriesGapTicks;
                Until(sim, impact);
                HeroInFront(sim, 3.3);
                int before = sim.Entities.Health[0];
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionImpact, EnemyActionKind.ThicketPaw, true), "удар " + (strike + 1) + " попал");
                taken.Add(before - sim.Entities.Health[0]);
            }
            CollectionAssert.AreEqual(new[] { 16, 10, 10 }, taken, "первый — лапа целиком, второй и третий — на 40% слабее");
            Assert.AreEqual(16, sim.ThicketPawStrikeDamageOf(Boss, 0));
            Assert.AreEqual(10, sim.ThicketPawStrikeDamageOf(Boss, 1), "16 × 0,6 = 9,6");
            Assert.AreEqual(10, sim.ThicketPawStrikeDamageOf(Boss, 2));
        }

        [Test]
        public void SoftStomp_HeroWhoJustRunsPast_IsNotStomped()
        {
            // Пробегает за спиной босса в 3 м от центра со скоростью бега (0,15 м за тик):
            // ближе 4 м — около 35 тиков. Топот — только после 60 тиков ПОДРЯД.
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            var boss = sim.Entities.Position[Boss];
            int stomps = 0, longest = 0;
            for (int k = 0; k <= 160; k++)
            {
                sim.Entities.Position[0] = boss + At(3, -12 + 0.15 * k);
                sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                sim.Step(InputFrame.Empty);
                stomps += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp);
                longest = System.Math.Max(longest, sim.ThicketMasterNearRunTicks(Boss));
            }
            Assert.That(longest, Is.InRange(20, Simulation.ThicketStompNearTicks - 1), "был рядом, но меньше 2 с");
            Assert.AreEqual(0, stomps, "мимо пробежавшего не топчет");
        }

        [Test]
        public void SoftStomp_StepsOutForATickEveryFifty_NoStomp_ThenSixtyInARow_Stomps()
        {
            // Стоит в окне топота (3,85 м — дальше лапы), но раз в 50 тиков на тик выходит за 4 м:
            // из любых 90 тиков рядом ≥ 88 — правило «60 из 90» топтало бы, «60 подряд» — нет.
            var sim = Arena();
            MeleeOnly(sim);
            Until(sim, IntroDone);
            int stomps = 0, oldRule = 0, lastOut = -1;
            for (int k = 0; k < 400; k++)
            {
                bool outside = k % 50 == 49;
                HeroInFront(sim, outside ? 4.6 : 3.85);
                if (outside) lastOut = sim.Tick;
                sim.Step(InputFrame.Empty);
                stomps += Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp);
                if (sim.ThicketMasterNearTicks(Boss) >= Simulation.ThicketStompNearTicks) oldRule++;
                Assert.That(sim.ThicketMasterNearRunTicks(Boss), Is.LessThan(Simulation.ThicketStompNearTicks));
            }
            Assert.That(oldRule, Is.GreaterThan(0), "по старому правилу топот встал бы");
            Assert.AreEqual(0, stomps, "тик за 4 м начинает счёт заново");

            // Остался рядом: топот — ровно на 60-й тик подряд.
            int start = -1;
            for (int k = 0; k < 120 && start < 0; k++)
            {
                HeroInFront(sim, 3.85);
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                if (Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketStomp) > 0) start = tick;
            }
            Assert.AreEqual(lastOut + Simulation.ThicketStompNearTicks, start, "60 тиков подряд рядом — топот");
            Assert.AreEqual(0, sim.ThicketMasterNearRunTicks(Boss), "счёт после топота — заново");
        }

        [Test]
        public void Shares_ScaleWithTheLeafDamage_AtTheFirstArena()
        {
            var sim = Arena(arena: 1);
            Assert.AreEqual(10, sim.ThicketPawDamageOf(Boss));
            Assert.AreEqual(6, sim.ThicketPawStrikeDamageOf(Boss, 1), "второй удар серии: 10 × 0,6");
            Assert.AreEqual(15, sim.ThicketStompDamageOf(Boss), "10 × 62/41 = 15,1");
            Assert.AreEqual(16, sim.ThicketShareOf(Boss, Simulation.ThicketDiveDamageA9), "10 × 66/41 = 16,1");
            Assert.AreEqual(17, sim.ThicketShareOf(Boss, Simulation.ThicketStormDamageA9), "10 × 70/41 = 17,1");
            Assert.AreEqual(11, sim.ThicketStompRingDamageOf(Boss), "15 × 0,75 = 11,25");
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
            Assert.AreEqual(16 + 10, health - sim.Entities.Health[0], "серия из двух ударила до рёва (второй — 60%)");
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
            Assert.AreEqual(start + 15, paw.ImpactTick);
            Assert.AreEqual(start + 15 + 9 + 9, paw.LastImpactTick, "П/Л/П через 9");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick);

            for (int stage = 1; stage < 3; stage++)
            {
                Until(sim, paw.ImpactTick + 9 * (stage - 1));
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(1, Count(sim, SimEventType.EnemyActionStarted, EnemyActionKind.ThicketPaw), "знак удара " + stage);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionStarted && e.ActionVariant == (int)EnemyActionKind.ThicketPaw)
                        Assert.AreEqual(stage, e.Amount, "номер удара");
                Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var step));
                Assert.AreEqual(stage, step.Stage);
                Assert.AreEqual(paw.ImpactTick + 9 * stage, step.ImpactTick);
            }
            Until(sim, paw.LastImpactTick + 1);
            Assert.AreEqual(16 + 10 + 10, health - sim.Entities.Health[0], "16, потом два по 10 (60%)");
            // Фаза 3: отдых 6 × 0,85 = 5 короче окна — следующая серия ждёт окно: замах за 15 до его конца.
            int next = RunUntilStarted(sim, EnemyActionKind.ThicketPaw, 60);
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketWindowTicks - Simulation.ThicketPawWindupTicks, next);
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
                    Assert.AreEqual(10296, sim.Entities.MaxHealth[boss], "6600 × 156% (баланс 02.10, было 6000 → 9360)");
                    Assert.AreEqual(16, sim.Entities.Damage[boss], "без надбавки ×1,5 временного босса");
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
            var sim = Arena(distance: 9);
            Only(sim, ThicketMasterAction.Dive);
            // Подмога: живой Хранитель далеко за спиной героя.
            int add = sim.AddKindTestEnemy(EnemyKind.ForestGuardian, At(-20, 0), 100);
            sim.Entities.Stats[add].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(add);
            sim.Entities.NextAttackTick[add] = int.MaxValue;
            Until(sim, IntroDone);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400);
            Assert.AreEqual(IntroDone + Simulation.ThicketDiveCooldownTicks * 125 / 100, start, "первый нырок ×1,25 при подмоге");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var dive));
            Until(sim, dive.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase1Ticks * 125 / 100, memory.NextActionTick,
                "отдых фазы 1 (18) ×1,25 при подмоге");
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
            Assert.AreEqual(paw.StartTick + Simulation.ThicketPawWindupTicks, paw.ImpactTick, "замах не сокращается");
            Assert.AreEqual(paw.LastImpactTick + Simulation.ThicketPawStrikeTicks, paw.EndTick);
            Until(sim, paw.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(2, memory.Phase);
            // После серии отдыха нет: множитель видно по перезарядке топота.
            int stomp = RunUntilStarted(sim, EnemyActionKind.ThicketStomp, 200);
            Assert.AreNotEqual(-1, stomp);
            Assert.AreEqual(stomp + Simulation.ThicketStompCooldownTicks * 85 / 100,
                sim.ThicketReadyTick(Boss, ThicketMasterAction.Stomp), "перезарядка ×0,85");
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
            Assert.AreEqual(16, health - sim.Entities.Health[0], "контакт в свой тик");
        }

        [Test]
        public void Immune_StunnedBossKeepsWalking()
        {
            var sim = Arena(distance: 8, walks: true);
            Until(sim, IntroDone);
            sim.Statuses.ApplyStun(Boss, sim.Tick + 120);
            var before = sim.Entities.Position[Boss];
            for (int k = 0; k < 20; k++) sim.Step(InputFrame.Empty);
            Assert.That(FixVec2.Distance(before, sim.Entities.Position[Boss]).ToDouble(), Is.GreaterThan(0.5));
        }

        // ---------- ход ----------

        [Test]
        public void Walk_TurnsAtMostFourAndAHalfDegrees_AndStaysOnTheLeash()
        {
            var sim = Arena(distance: 8, walks: true);
            // Ход, не нырок: нырок достаёт и героя за поводком (Dive_HeroBeyondTheLeash_*).
            Only(sim);
            Until(sim, IntroDone);
            // Герой далеко за спиной и за поводком.
            sim.Entities.Position[0] = sim.Entities.Position[Boss] + At(25, 0);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            double cos = System.Math.Cos(System.Math.PI / 40) - 1e-4;
            for (int k = 0; k < 360; k++)
            {
                var facing = sim.Entities.Facing[Boss];
                sim.Step(InputFrame.Empty);
                double dot = FixVec2.Dot(facing.Normalized(), sim.Entities.Facing[Boss].Normalized()).ToDouble();
                Assert.That(dot, Is.GreaterThanOrEqualTo(cos), "поворот за тик, тик " + (sim.Tick - 1));
                double leash = FixVec2.Distance(sim.Entities.Position[Boss], memory.Home).ToDouble();
                Assert.That(leash, Is.LessThanOrEqualTo(10.01), "поводок, тик " + (sim.Tick - 1));
            }
            Assert.That(FixVec2.Distance(sim.Entities.Position[Boss], memory.Home).ToDouble(), Is.GreaterThan(9.5),
                "дошёл до края поводка");
        }

        [Test]
        public void Walk_ReachesTwoMetresPerSecond()
        {
            var sim = Arena(distance: 8.5, walks: true);
            Until(sim, IntroDone + 1);
            Assert.IsFalse(sim.TryGetThicketMasterAction(Boss, out _), "после вступления — ход");
            var from = sim.Entities.Position[Boss];
            for (int k = 0; k < 30; k++) sim.Step(InputFrame.Empty);
            // Разгон за 3 тика, дальше 2,0 м/с (владелец 02.10, было 2,6): за 30 тиков 2,0 × 29/30 ≈ 1,93 м.
            double walked = FixVec2.Distance(from, sim.Entities.Position[Boss]).ToDouble();
            Assert.That(walked, Is.InRange(1.85, 2.01), "ход ~2,0 м/с");
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
            Assert.AreEqual(16, health - sim.Entities.Health[0], "лапа легла на 60 тиков позже");
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
            var sim = Arena(distance: 3);
            sim.SetArtifact(RunArtifact.Hourglass);
            Only(sim, ThicketMasterAction.Dive);
            sim.SetThicketReadyTick(Boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
            Until(sim, IntroDone + 20);
            var e = sim.Entities;
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
            // Герой ушёл далеко: босс ныряет, как только оттаял, Часы кончаются под землёй.
            int diveAt = -1;
            while (sim.Tick < release && diveAt < 0)
            {
                e.Position[0] = e.Position[Boss] + At(-9, 0);
                if (sim.ThicketReadyTick(Boss, ThicketMasterAction.Dive) > sim.Tick)
                    sim.SetThicketReadyTick(Boss, ThicketMasterAction.Dive, sim.Tick);
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

        /// <summary>Герой в 9 м стоит: первый нырок — через 150 тиков после первого выбора (конец вступления).</summary>
        private static int StartDive(Simulation sim, out ThicketMasterState dive)
        {
            Until(sim, IntroDone);
            int start = RunUntilStarted(sim, EnemyActionKind.ThicketDive, 400);
            Assert.AreEqual(IntroDone + Simulation.ThicketDiveCooldownTicks, start, "первый нырок — через 5 с");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out dive));
            Assert.AreEqual(ThicketMasterAction.Dive, dive.Action);
            return start;
        }

        [Test]
        public void Dive_FarHero_Burrow12_MoundTravels30_CircleLocksUnderHim_Emerges24Later_For26_Stands36()
        {
            var sim = Arena(distance: 9);
            int start = StartDive(sim, out var dive);
            var home = sim.Entities.Position[Boss];
            Assert.AreEqual(start + 12 + 30 + 24, dive.ImpactTick);
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

            Until(sim, start + 42);
            Assert.That(FixVec2.Distance(sim.Entities.Position[Boss], sim.Entities.Position[0]).ToDouble(), Is.LessThan(1.0),
                "бугор доехал к герою");
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
            Assert.AreEqual(26, sim.ThicketDiveDamageOf(Boss), "нырок на арене 9: 16 × 66/41");
            Assert.AreEqual(26, health - sim.Entities.Health[0]);
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

        [Test]
        public void Dive_HeroWhoLeftTheCircle_TakesNothing()
        {
            var sim = Arena(distance: 9);
            int start = StartDive(sim, out var dive);
            Until(sim, start + 43);
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
            Assert.AreEqual(26, health - sim.Entities.Health[0]);
        }

        [Test]
        public void Dive_HeroJustPastPawReachOfTheLeashedBoss_IsDivedEveryFiveSeconds()
        {
            // Фаза 1, герой в 14,1 м от точки появления: босс дошёл до края поводка
            // (4,1 м от героя — ни лапа 3,68, ни топот 4, ни «дальше 7 м») и раньше стоял
            // до срока «раз в 10 с», пока герой бил его ударом якоря.
            var sim = Arena(distance: 9, walks: true);
            Only(sim, ThicketMasterAction.Dive);
            Until(sim, IntroDone);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            var spot = memory.Home + At(-14.1, 0);
            int health = sim.Entities.Health[0], dives = 0, hits = 0;
            for (int k = 0; k < 600; k++)
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
            Assert.That(dives, Is.GreaterThanOrEqualTo(3), "ныряет раз в перезарядку (5 с), а не стоит у края");
            Assert.That(hits, Is.GreaterThanOrEqualTo(2));
            Assert.Less(sim.Entities.Health[0], health);
        }

        [Test]
        public void Dive_RealBossMaps_BossEmergesOnTheFloor_InsideItsCircle()
        {
            // Арены 9 леса, 6 сидов, точки героя в 8–17 м от точки появления по 16
            // направлениям (и за поводком) и до 8 точек за деревьями и стенами — от
            // точки появления по прямой бугру не пройти. Герой стоит до выхода.
            // Поляна босса с 02.10 одна и та же и ровная (20 × 15 м, без озера):
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
                    double angle = k * System.Math.PI / 8, distance = 8 + k % 4 * 3;
                    spots.Add(home + At(System.Math.Cos(angle) * distance, System.Math.Sin(angle) * distance));
                }
                for (int k = 0, extra = 0; k < 64 && extra < 8; k++)
                {
                    double angle = (k / 2) * System.Math.PI / 16, distance = 8.5 + k % 2 * 3;
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
            Assert.That(dives, Is.GreaterThan(40));
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
            Assert.AreEqual(13, sim.ThicketSproutDamageOf(Boss), "16 × 34/41");
            Assert.AreEqual(6 * 13, health - sim.Entities.Health[0]);
            Assert.AreEqual(0, sim.HeroRootTicksLeft, "без корней");
            Assert.IsFalse(sim.TryGetThicketHazard(Boss, out _), "всё отбито — опасность снята");
            Assert.IsFalse(sim.ThicketHazardActive(Boss));
            Assert.AreEqual(0, sim.BigMarkLoad(out _));
            Assert.IsTrue(sim.TryGetThicketShape(Boss, 5, out _, out _, out bool resolved), "круги остаются виду");
            Assert.IsTrue(resolved);
        }

        [Test]
        public void Sprout_GestureEnds_RestTwelveInPhaseTwo()
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
            Assert.AreEqual(2, sim.ThicketPollenDamageOf(Boss), "16 × 5/41");
            Assert.AreEqual(8 * 2, damage, "не больше 8 укусов за облако");
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
            Assert.AreEqual(12, sim.ThicketRainDamageOf(Boss), "16 × 30/41");
            Assert.AreEqual(5 * 12, health - sim.Entities.Health[0]);
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
            void Play(int ticks)
            {
                seen.Clear();
                for (int k = 0; k < ticks; k++)
                {
                    sim.Step(InputFrame.Empty);
                    foreach (var e in sim.Events)
                        if (e.Type == SimEventType.EnemyActionStarted && e.Source == Boss && e.Amount == 0) seen.Add(e.ActionVariant);
                    sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
                }
            }
            Play(900);
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketDive), "фаза 1: нырок раз в ~10 с");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketSprout), "фаза 1: без прорастания");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketPollen), "фаза 1: без пыльцы");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketRain), "фаза 1: без ливня");
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 60 / 100;
            Play(1200);
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketSprout), "фаза 2: прорастание");
            Assert.IsTrue(seen.Contains((int)EnemyActionKind.ThicketPollen), "фаза 2: пыльца");
            Assert.IsFalse(seen.Contains((int)EnemyActionKind.ThicketRain), "фаза 2: без ливня");
            sim.Entities.Health[Boss] = sim.Entities.MaxHealth[Boss] * 30 / 100;
            Play(1500);
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
        public void Storm_AfterTheThirtyThreeRoar_Waves60And45_BossStands_ThreeSafeCirclesEachWave()
        {
            var sim = Arena();
            Only(sim, ThicketMasterAction.Storm);
            int start = ToStorm(sim);
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var storm));
            Assert.AreEqual(ThicketMasterAction.Storm, storm.Action);
            Assert.AreEqual(start + 60, storm.ImpactTick, "первая волна через 60");
            Assert.AreEqual(start + 60 + 45, storm.LastImpactTick, "вторая — через 45 после первой");
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
            Assert.AreEqual(27, health - sim.Entities.Health[0], "волна вне кругов — 27 на арене 9 (16 × 70/41)");
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
            Assert.AreEqual(27, health - sim.Entities.Health[0], "за кромкой — 27");
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
            if (Metres(circles[1], hero) > 0.01)
                Assert.IsTrue(map.CanTravel(hero, circles[1], body), "до круга 1 — по прямой без стен" + where);
            Assert.IsTrue(map.CanTravel(hero, circles[2], body) || Metres(circles[2], circles[1]) < 0.01,
                "до круга 2 — по прямой без стен" + where);
            if (nearest > worst) worst = nearest;
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
            Assert.AreEqual(27, health - sim.Entities.Health[0], "волна легла на 60 тиков позже");
        }
        // ---------- связки фаз 2–3 ----------

        /// <summary>Герой в distance метрах прямо по взгляду босса.</summary>
        private static void HeroAhead(Simulation sim, double distance)
            => sim.Entities.Position[0] = sim.Entities.Position[Boss]
                + sim.Entities.Facing[Boss].Normalized() * Fix64.FromDouble(distance);

        /// <summary>Нырок фазы percent (100 — фаза 1): герой в 8 м, после выхода — в after метрах по взгляду до конца стойки.</summary>
        private static ThicketMasterState DiveAndStand(Simulation sim, int percent, double after)
        {
            Only(sim, ThicketMasterAction.Dive);
            if (percent < 100) ToPhase(sim, percent); else Until(sim, IntroDone);
            HeroInFront(sim, 8);
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
            Assert.AreEqual(pawAt + Simulation.ThicketPawWindupTicks, paw.ImpactTick, "замах не сокращается");
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
            Assert.AreEqual(dive.EndTick, stompAt, "Нырок→Топот: мимо правила «60 из 90» и перезарядки");
            Assert.IsTrue(sim.TryGetThicketMasterAction(Boss, out var stomp));
            Assert.AreEqual(stompAt + Simulation.ThicketStompWindupTicks, stomp.ImpactTick);
            Until(sim, stomp.EndTick + 1);
            Assert.IsTrue(sim.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext);
            Assert.AreEqual(stomp.EndTick + Simulation.ThicketRestPhase3Ticks * Simulation.ThicketEnragedCooldownPercent / 100,
                memory.NextActionTick, "после связки — отдых фазы 3 (×0,85 ниже половины здоровья)");
            Assert.AreEqual(stomp.LastImpactTick + Simulation.ThicketWindowTicks, memory.QuietUntil);
        }

        [Test]
        public void Chain_NotInPhaseOne_NorForAFarHero_RestByPhaseInstead()
        {
            var one = Arena();
            var dive = DiveAndStand(one, 100, 2.5);
            Assert.AreEqual(-1, RunUntilStarted(one, EnemyActionKind.ThicketPaw, 2), "фаза 1 — без связки");
            Assert.IsTrue(one.TryGetThicketMasterMemory(Boss, out var memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext);
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase1Ticks, memory.NextActionTick, "отдых фазы 1 — 18");

            var far = Arena();
            dive = DiveAndStand(far, 60, 9);
            far.Step(InputFrame.Empty);
            Assert.IsTrue(far.TryGetThicketMasterMemory(Boss, out memory));
            Assert.AreEqual(ThicketMasterAction.None, memory.ChainNext, "герой дальше 7,5 м — связки нет");
            Assert.AreEqual(dive.EndTick + Simulation.ThicketRestPhase2Ticks, memory.NextActionTick, "отдых фазы 2 — 12");
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
            // Дальше герой стоит перед мордой: в досягаемости лапы и в окне топота.
            var spot = sim.Entities.Position[Boss] + At(-2.5, 0);
            var circleHits = new List<int>();
            var pawHits = new List<int>();
            int paws = 0, others = 0, stompAfter = -1;
            for (int k = 0; k < 400 && stompAfter < 0; k++)
            {
                sim.Entities.Position[0] = spot;
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
            Assert.AreNotEqual(-1, stompAfter, "окно топота набралось — топот встаёт, как только опасность кончилась");
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

        // ---------- честность на поляне босса 20 × 15 (проверка 02.10) ----------

        /// <summary>Оси от центра поляны: по длинной (±X), по короткой (±Y, до края 7,5 м) и по диагонали.</summary>
        private static readonly FixVec2[] ClearingAxes = { At(1, 0), At(-1, 0), At(0, 1), At(0, -1), At(0.6, 0.8) };

        /// <summary>
        /// Настоящая поляна босса (уровень 9 леса): босс в её центре стоит на месте
        /// (ход 0 — фигуры от центра), герой 10000 HP с рывком, касты и буря закрыты.
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
            // Круг 5,2 через 24, кольцо 5,2–7,5 через 15. По короткой оси пол кончается в 7,5 м —
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
                        else if (!ringPhase) input = policy == 1 && d > 5.9 ? Walk(center + axis * Fix64.Ratio(16, 10)) : Walk(e.Position[0]);
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
            // Серия из трёх перед мордой: первый замах 15 тиков. Из 2,5 м рывок прочь через
            // 8 тиков (реакция) уводит из всех трёх; ногами из 2,5 м не уйти даже сразу
            // (сектор 4,14 + тело героя), с края досягаемости сабли (3,3 м) — можно, если сразу.
            string[] names = { "рывок прочь через 8 тиков из 2,5 м", "ногами прочь сразу из 3,3 м" };
            for (int policy = -1; policy < 2; policy++)
            {
                var sim = Clearing(out int boss, out var map);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Dive, int.MaxValue / 2);
                sim.SetThicketReadyTick(boss, ThicketMasterAction.Stomp, int.MaxValue / 2);
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
                    // Разбудить рядом, потом встать на край (вне досягаемости лапы и окна топота).
                    for (int k = 0; k < 200; k++) { e.Position[0] = center + axis * Fix64.FromInt(6); sim.Step(Walk(e.Position[0])); }
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
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Стенд темпа: герой стоит в 3 м перед боссом (ставится каждый тик,
        /// не бьёт, здоровье доливается), здоровье босса держится на percent.
        /// Средний интервал между началами атак (серия лапы, топот с кольцом,
        /// нырок, жест каста, буря — по одной атаке) за ticks тиков после рёва порога.
        /// </summary>
        private static double MeanAttackSeconds(int percent, int ticks, out int attacks, out string mix)
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
            int first = -1, last = -1;
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
                    string name = ((EnemyActionKind)ev.ActionVariant).ToString().Replace("Thicket", "");
                    kinds[name] = kinds.TryGetValue(name, out int n) ? n + 1 : 1;
                }
            }
            mix = string.Join(" ", System.Linq.Enumerable.Select(kinds, kv => kv.Key + " " + kv.Value));
            return attacks < 2 ? double.MaxValue : (last - first) / (double)(attacks - 1) / Simulation.TicksPerSecond;
        }

        [Test]
        public void Cadence_PassiveHeroAtThreeMetres_PhaseByPhase()
        {
            const int ticks = 3600;
            double p1 = MeanAttackSeconds(100, ticks, out int n1, out string m1);
            double p2 = MeanAttackSeconds(58, ticks, out int n2, out string m2);
            double p2b = MeanAttackSeconds(42, ticks, out int n2b, out string m2b);
            double p3 = MeanAttackSeconds(20, ticks, out int n3, out string m3);
            TestContext.WriteLine("phase 1: " + p1.ToString("0.00") + " s (" + n1 + "), phase 2 at 58%: " + p2.ToString("0.00")
                + " s (" + n2 + "), at 42%: " + p2b.ToString("0.00") + " s (" + n2b + "), phase 3: " + p3.ToString("0.00")
                + " s (" + n3 + ")");
            TestContext.WriteLine("mix 1: " + m1 + " | 2 at 58%: " + m2 + " | 2 at 42%: " + m2b + " | 3: " + m3);
            // Темп 02.10: ориентир спецификации ~1,4 / 1,1 / 0,9 с (серии — одна атака). Серия лапы
            // фазы 1 подряд — ровно 1,4 с (24 + 1 + отдых 18), но герой стоит в 3 м — в окне топота:
            // топот (24 + 15 + 2 + 18 = 59 тиков) каждые 90 тиков и нырок раз в 10 с (108 тиков) тянут
            // среднее вверх. Фаза 3 медленнее второй: буря (60 + 45 + 12) — одна атака, серия из 3.
            // Замер 02.10: 2,04 / 1,66–1,62 / 1,85 с (было 2,59 / 2,16–2,32 / 2,15).
            Assert.That(p1, Is.InRange(1.8, 2.3), "фаза 1");
            Assert.That((p2 + p2b) / 2, Is.InRange(1.4, 1.9), "фаза 2");
            Assert.That(p3, Is.InRange(1.6, 2.1), "фаза 3");
            Assert.That((p2 + p2b) / 2, Is.LessThan(p1), "фаза 2 быстрее первой");
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
                    // По 120 тиков: перед мордой 2,5 м, сбоку 3,4 м, под крупом, в 7 м.
                    var boss = e.Position[_boss];
                    var f = e.Facing[_boss].Normalized();
                    var side = new FixVec2(-f.Y, f.X);
                    FixVec2 offset;
                    switch ((tick / 120) % 4)
                    {
                        case 0: offset = f * Fix64.Ratio(5, 2); break;
                        case 1: offset = side * Fix64.Ratio(17, 5); break;
                        case 2: offset = -f * Fix64.Ratio(21, 10); break;
                        default: offset = f * Fix64.FromInt(7); break;
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

            private static ulong Mix(ulong h, ulong v)
            {
                for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
                return h;
            }
        }
    }
}
