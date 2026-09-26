using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Корнехват (план новых мобов от 26.09).
    ///
    /// Приёмка: 15 тиков только поза, метки на земле нет; на 15-м круг 1,5 м
    /// встаёт на месте героя и дальше не двигается; через 21 тик контакт —
    /// урон и замедление 40% на 45 тиков, мимо — ничего; замедление общее с
    /// воем Вендиго, сильнейшее побеждает; оглушение, волок и смерть снимают
    /// круг; 36 тиков стойки после контакта; крупный жетон — от начала позы
    /// до контакта.
    /// </summary>
    public sealed class RootSnarerTests
    {
        private const int Snarer = 1;

        private static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        /// <summary>
        /// Герой в начале координат, Корнехваты шеренгой в distance метрах по
        /// оси X, лицом к герою. Герой толстый и без брони — урон считается
        /// ровно. Без walks мобы не ходят: тайминги не зависят от подхода.
        /// </summary>
        private static Simulation Arena(double distance = 5, int count = 1, bool walks = false,
            int arena = 1, int hardPercent = 100)
        {
            var sim = new Simulation(77, 64);
            sim.SetupKindTestArena(EnemyKind.ForestRootSnarer, count, arena: arena, hardPercent: hardPercent,
                distance: Fix64.FromDouble(distance));
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0);
            sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            if (!walks)
                for (int id = 1; id < sim.Entities.Count; id++)
                {
                    sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
                    sim.Entities.RefreshStats(id);
                }
            return sim;
        }

        private static void Until(Simulation sim, int tick)
        {
            while (sim.Tick < tick) sim.Step(InputFrame.Empty);
        }

        /// <summary>Один шаг — и удар корнями обязан начаться.</summary>
        private static RootSnarerState Start(Simulation sim)
        {
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var a), "удар корнями не начался");
            return a;
        }

        /// <summary>Последняя по номеру метка владельца.</summary>
        private static bool CircleOf(Simulation sim, int source, out EnemyTelegraph found)
        {
            found = default;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.Source == source && t.Serial > found.Serial) found = t;
            return found.Serial != 0;
        }

        /// <summary>Вендиго, у которого готов вой и не готов прыжок: воет, как только сможет.</summary>
        private static int HowlingWendigo(Simulation sim, FixVec2 at)
        {
            int id = sim.SpawnEnemy(at, 5000, EnemyKind.ForestWendigo);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Facing[id] = (sim.Entities.Position[0] - at).Normalized();
            sim.Entities.Aggro[id] = true;
            sim.SetWendigoCooldowns(id, 100000, 0);
            return id;
        }

        // ---------- поза, круг, контакт ----------

        [Test]
        public void PoseComesFirst_NoMarkUntilTheSlam()
        {
            Assert.AreEqual(15, Simulation.RootSnarerSlamTicks);
            Assert.AreEqual(21, Simulation.RootSnarerImpactDelayTicks);
            Assert.AreEqual(36, Simulation.RootSnarerRecoveryTicks);
            Assert.AreEqual(150, Simulation.RootSnarerCooldownTicks);

            var sim = Arena();
            var a = Start(sim);
            Assert.AreEqual(0, a.StartTick);
            Assert.AreEqual(15, a.SlamTick);
            Assert.AreEqual(36, a.ImpactTick);
            Assert.AreEqual(72, a.EndTick);
            Assert.IsFalse(a.Slammed);

            // Начало — событие для звука EnemyWarning, метки на земле нет.
            int started = 0;
            foreach (var e in sim.Events)
            {
                Assert.AreNotEqual(SimEventType.TelegraphOpened, e.Type, "метка в тик начала позы");
                if (e.Type != SimEventType.EnemyActionStarted) continue;
                started++;
                Assert.AreEqual(Snarer, e.Source);
                Assert.AreEqual(Simulation.PlayerId, e.Target);
                Assert.AreEqual((int)EnemyActionKind.SnarerSlam, e.ActionVariant);
                Assert.AreEqual(0, e.Amount);
                Assert.AreEqual(sim.Entities.Position[Snarer], e.Position);
            }
            Assert.AreEqual(1, started);

            while (sim.Tick < a.SlamTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(CircleOf(sim, Snarer, out _), "метка до удара корнями, тик " + (sim.Tick - 1));
                foreach (var e in sim.Events)
                    Assert.AreNotEqual(SimEventType.EnemyActionStarted, e.Type, "поза началась дважды");
            }

            sim.Step(InputFrame.Empty);
            Assert.IsTrue(CircleOf(sim, Snarer, out var circle), "круга нет в тик удара корнями");
            Assert.AreEqual(a.SlamTick, circle.StartTick);
            Assert.AreEqual(a.ImpactTick, circle.ImpactTick);
            Assert.AreEqual(TelegraphState.Active, circle.State);
            Assert.AreEqual(TelegraphShape.Circle, circle.Shape);
            Assert.IsTrue(circle.SharedView, "круг рисует общий вид меток");
            Assert.AreEqual(Simulation.RootSnarerCircleRadius, circle.Radius);
            Assert.AreEqual(Fix64.Ratio(3, 2), circle.Radius);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var slammed));
            Assert.IsTrue(slammed.Slammed);
            Assert.AreEqual(circle.Serial, slammed.TelegraphSerial);
        }

        [Test]
        public void CircleIsFixedWhereTheHeroStoodAtTheSlam()
        {
            var sim = Arena();
            var a = Start(sim);

            // Пока идёт поза, герой переходит — круг встаёт там, где он в тик удара.
            Until(sim, a.SlamTick - 3);
            sim.Entities.Position[0] = At(0, 2);
            Until(sim, a.SlamTick);
            var slamSpot = At(-1, 1.5);
            sim.Entities.Position[0] = slamSpot;
            sim.Step(InputFrame.Empty);

            Assert.IsTrue(CircleOf(sim, Snarer, out var circle));
            Assert.AreEqual(slamSpot, circle.Origin);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var slammed));
            Assert.AreEqual(slamSpot, slammed.Target);
            // С удара смотрит на круг.
            var look = (slamSpot - sim.Entities.Position[Snarer]).Normalized();
            Assert.AreEqual(look, slammed.Direction);
            Assert.AreEqual(look, sim.Entities.Facing[Snarer]);

            // Герой уходит — круг за ним не едет, взгляд не доворачивает.
            sim.Entities.Position[0] = At(2, -3);
            while (sim.Tick < a.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsTrue(CircleOf(sim, Snarer, out var still));
                Assert.AreEqual(slamSpot, still.Origin, "тик " + (sim.Tick - 1));
                Assert.AreEqual(look, sim.Entities.Facing[Snarer], "тик " + (sim.Tick - 1));
            }
        }

        [Test]
        public void ImpactLands21TicksAfterTheSlam_OnceWithTheTableDamage()
        {
            var sim = Arena();
            int health = sim.Entities.Health[0];
            var a = Start(sim);
            var impacts = new List<int>();
            var hits = new List<int>();
            while (sim.Tick < a.EndTick)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.Damage && e.Source == Snarer && e.Target == 0) hits.Add(tick);
                    if (e.Type != SimEventType.EnemyActionImpact || e.Source != Snarer) continue;
                    impacts.Add(tick);
                    Assert.IsTrue(e.Flag, "герой стоял в круге");
                    Assert.AreEqual((int)EnemyActionKind.SnarerSlam, e.ActionVariant);
                    Assert.AreEqual(Simulation.PlayerId, e.Target);
                    Assert.AreEqual(0, e.Amount);
                    Assert.AreEqual(sim.Entities.Position[0], e.Position, "контакт — в центре круга");
                }
                if (tick < a.ImpactTick)
                    Assert.AreEqual(health, sim.Entities.Health[0], "урон до контакта, тик " + tick);
                if (tick != a.ImpactTick) continue;
                // Метка сработала и доживает вспышку.
                Assert.IsTrue(CircleOf(sim, Snarer, out var circle));
                Assert.AreEqual(TelegraphState.Resolved, circle.State);
            }
            Assert.That(impacts, Is.EqualTo(new[] { a.SlamTick + Simulation.RootSnarerImpactDelayTicks }));
            Assert.That(hits, Is.EqualTo(new[] { a.ImpactTick }));
            Assert.AreEqual(EnemyArchetypes.RootSnarerDamage, sim.RootSnarerDamageOf(Snarer));
            Assert.AreEqual(20, health - sim.Entities.Health[0]);
        }

        // Тело героя 0,45: круг 1,5 м задевает его, пока центр ближе 1,95 м.
        [TestCase(0.0, true)]
        [TestCase(1.9, true)]
        [TestCase(2.0, false)]
        [TestCase(3.5, false)]
        public void HitSlowsTheHero_MissLeavesHimFree(double offset, bool hit)
        {
            var sim = Arena();
            var full = sim.Entities.MoveStep[0];
            int health = sim.Entities.Health[0];
            var a = Start(sim);
            Until(sim, a.SlamTick + 1);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out a));

            // После удара корнями герой шагает в сторону — или не успевает.
            var hero = a.Target + At(0, offset);
            sim.Entities.Position[0] = hero;
            Assert.IsTrue(CircleOf(sim, Snarer, out var circle));
            Assert.AreEqual(hit, Simulation.TelegraphContains(circle, hero, sim.Entities.BodyRadius[0]));

            bool? flag = null;
            while (sim.Tick <= a.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(full, sim.Entities.MoveStep[0], "до контакта замедления нет");
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionImpact && e.Source == Snarer) flag = e.Flag;
            }
            Assert.IsTrue(flag.HasValue, "контакта не было");
            Assert.AreEqual(hit, flag.Value);
            Assert.AreEqual(hit ? EnemyArchetypes.RootSnarerDamage : 0, health - sim.Entities.Health[0]);

            // Переключатель владельца: замедление 40% на 45 тиков или корни 100% на 12.
            int percent = Simulation.SnarerRoots ? Simulation.HeroRootPercent : Simulation.RootSnarerSlowPercent;
            int ticks = Simulation.SnarerRoots ? Simulation.RootSnarerRootTicks : Simulation.RootSnarerSlowTicks;
            Assert.AreEqual(40, Simulation.RootSnarerSlowPercent);
            Assert.AreEqual(45, Simulation.RootSnarerSlowTicks);
            Assert.AreEqual(12, Simulation.RootSnarerRootTicks);
            Assert.AreEqual(hit ? percent : 0, sim.HeroSlowPercent);
            Assert.AreEqual(hit ? ticks : 0, sim.HeroSlowTicksLeft);

            int slowed = 0;
            for (int k = 0; k < ticks + 15; k++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.Entities.MoveStep[0] == full) continue;
                slowed++;
                Assert.That(sim.Entities.MoveStep[0].ToDouble(),
                    Is.EqualTo(full.ToDouble() * (100 - percent) / 100).Within(1e-3));
            }
            Assert.AreEqual(hit ? ticks : 0, slowed, "замедление — ровно столько шагов героя");
            Assert.AreEqual(full, sim.Entities.MoveStep[0]);
            Assert.AreEqual(0, sim.HeroSlowTicksLeft);
        }

        [Test]
        public void SlowIsSharedWithTheWendigoHowl_StrongestAndLatestWin()
        {
            var sim = Arena();
            sim.BigAttackTokenLimit = 2;
            int wendigo = HowlingWendigo(sim, At(0, 4));
            var full = sim.Entities.MoveStep[0];

            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetWendigoAction(wendigo, out var howl));
            Assert.AreEqual(WendigoAction.Howl, howl.Kind);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var a), "с пятой арены жетонов два");
            Assert.Less(howl.ImpactTick, a.ImpactTick);

            // Вой первым: 30% на 30 тиков.
            Until(sim, howl.ImpactTick + 1);
            Assert.AreEqual(Simulation.WendigoHowlSlowPercent, sim.HeroSlowPercent);
            Assert.AreEqual(Simulation.WendigoHowlSlowTicks, sim.HeroSlowTicksLeft);

            // Корни поверх: сильнее и дольше — побеждают и процентом, и сроком.
            Until(sim, a.ImpactTick + 1);
            Assert.AreEqual(Simulation.RootSnarerSlowPercent, sim.HeroSlowPercent, "сильнейшее побеждает");
            Assert.AreEqual(Simulation.RootSnarerSlowTicks, sim.HeroSlowTicksLeft, "срок — самый поздний");
            Assert.AreEqual(sim.HeroSlowTicksLeft, sim.WendigoHowlSlowTicksLeft, "одно замедление на двоих");

            // Вой поверх корней — слабее, но дольше: процент остаётся, срок растёт.
            sim.ApplyHeroSlow(Simulation.WendigoHowlSlowPercent, 60);
            Assert.AreEqual(Simulation.RootSnarerSlowPercent, sim.HeroSlowPercent);
            Assert.AreEqual(61, sim.HeroSlowTicksLeft);

            int slowed = 0;
            for (int k = 0; k < 80; k++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.Entities.MoveStep[0] == full) continue;
                slowed++;
                Assert.That(sim.Entities.MoveStep[0].ToDouble(), Is.EqualTo(full.ToDouble() * .6).Within(1e-3),
                    "два замедления не перемножаются");
            }
            Assert.AreEqual(61, slowed);
            Assert.AreEqual(full, sim.Entities.MoveStep[0]);
        }

        // ---------- отмена ----------

        // Причина: 0 — оглушение, 1 — Alive = false, 2 — волок, 3 — смерть через урон, 4 — смерть героя.
        [TestCase(5, 0)] [TestCase(20, 0)] [TestCase(35, 0)]
        [TestCase(10, 1)] [TestCase(30, 1)]
        [TestCase(8, 2)] [TestCase(25, 2)]
        [TestCase(12, 3)] [TestCase(33, 3)]
        [TestCase(20, 4)]
        public void StunDragAndDeathCancelTheCircleBeforeImpact(int at, int reason)
        {
            var sim = Arena();
            var a = Start(sim);
            Until(sim, a.StartTick + at);
            if (reason == 0) sim.Statuses.ApplyStun(Snarer, sim.Tick + 3);
            else if (reason == 1) sim.Entities.Alive[Snarer] = false;
            else if (reason == 2)
                Assert.IsTrue(ForcedMotion.Begin(sim.Entities, Snarer, sim.Entities.Position[Snarer], 3,
                    ForcedMotionKind.Dragged));
            else if (reason == 3) sim.ApplyAbilityDamage(0, Snarer, 100000, -1, DamageType.Physical);
            else sim.Entities.Alive[0] = false;
            int health = sim.Entities.Health[0];

            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            int cancelled = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyActionCancelled || e.Source != Snarer) continue;
                cancelled++;
                Assert.AreEqual((int)EnemyActionKind.SnarerSlam, e.ActionVariant);
            }
            Assert.AreEqual(1, cancelled, "отмена — одно событие");
            if (a.StartTick + at > a.SlamTick)
            {
                Assert.IsTrue(CircleOf(sim, Snarer, out var circle));
                Assert.AreEqual(TelegraphState.Cancelled, circle.State, "круг гаснет вместе с действием");
            }
            else Assert.IsFalse(CircleOf(sim, Snarer, out _), "круг встал после отмены");

            while (sim.Tick < a.StartTick + 100)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "перезарядка пережила отмену");
                foreach (var e in sim.Events)
                    Assert.AreNotEqual(SimEventType.EnemyActionImpact, e.Type, "контакт после отмены");
            }
            Assert.AreEqual(health, sim.Entities.Health[0]);
            Assert.AreEqual(0, sim.HeroSlowTicksLeft);
        }

        [Test]
        public void FullTelegraphPool_NoCircle_NoHit()
        {
            var sim = Arena();
            int health = sim.Entities.Health[0];
            var a = Start(sim);
            // Пул забит чужими метками в стороне: кругу места нет — и удара нет.
            while (sim.OpenTelegraph(40, EnemyTelegraph.Circle(At(30, 30), Fix64.One), 100000, 100000,
                       TelegraphFlags.None) >= 0) { }
            int impacts = 0;
            while (sim.Tick < a.EndTick)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionImpact && e.Source == Snarer) impacts++;
            }
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var slammed));
            Assert.IsTrue(slammed.Slammed, "поза и удар корнями прошли");
            Assert.AreEqual(0, slammed.TelegraphSerial);
            Assert.IsFalse(CircleOf(sim, Snarer, out _));
            Assert.AreEqual(0, impacts, "не нарисовано — не бьёт");
            Assert.AreEqual(health, sim.Entities.Health[0]);
            Assert.AreEqual(0, sim.HeroSlowTicksLeft);
        }

        // ---------- стойка, дальность, перезарядка ----------

        [Test]
        public void StandsStillThroughPoseAndThe36TickPunish_ThenWalks()
        {
            var sim = Arena(6, walks: true);
            var a = Start(sim);
            var spot = sim.Entities.Position[Snarer];
            // Герой уходит далеко — в действии Корнехват за ним не идёт.
            sim.Entities.Position[0] = At(-5, 0);
            while (sim.Tick <= a.EndTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(spot, sim.Entities.Position[Snarer], "шаг в действии, тик " + (sim.Tick - 1));
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Velocity[Snarer], "тик " + (sim.Tick - 1));
            }
            Assert.AreEqual(Simulation.RootSnarerRecoveryTicks, a.EndTick - a.ImpactTick);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "стойка кончилась");

            // Дальше идёт к герою: он и так смотрит на круг под героем.
            for (int k = 0; k < 5; k++) sim.Step(InputFrame.Empty);
            Assert.Less(sim.Entities.Position[Snarer].X.ToDouble(), spot.X.ToDouble() - .05);
        }

        // Между центрами героя и Корнехвата: 1,5–7 м.
        [TestCase(1.45, false)]
        [TestCase(1.55, true)]
        [TestCase(6.95, true)]
        [TestCase(7.05, false)]
        public void StartsOnlyWithTheHeroOneAndAHalfToSevenMetresAway(double distance, bool starts)
        {
            var sim = Arena(distance);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(starts, sim.TryGetRootSnarerAction(Snarer, out _));
        }

        [Test]
        public void NextSlamWaitsTheFullCooldownFromTheStart_EvenAfterAStun()
        {
            var sim = Arena();
            var first = Start(sim);
            Until(sim, first.EndTick + 1);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            while (sim.Tick < first.StartTick + Simulation.RootSnarerCooldownTicks)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "тик " + (sim.Tick - 1));
            }
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var second));
            Assert.AreEqual(150, second.StartTick - first.StartTick);

            // Оглушение в позе снимает удар, но перезарядку не обнуляет.
            var stunned = Arena();
            var cut = Start(stunned);
            Until(stunned, cut.StartTick + 5);
            stunned.Statuses.ApplyStun(Snarer, stunned.Tick + 3);
            while (stunned.Tick < cut.StartTick + Simulation.RootSnarerCooldownTicks)
            {
                stunned.Step(InputFrame.Empty);
                Assert.IsFalse(stunned.TryGetRootSnarerAction(Snarer, out _), "тик " + (stunned.Tick - 1));
            }
            stunned.Step(InputFrame.Empty);
            Assert.IsTrue(stunned.TryGetRootSnarerAction(Snarer, out var again));
            Assert.AreEqual(cut.StartTick + 150, again.StartTick);
        }

        [Test]
        public void WalksAlongItsFacingIntoRange_SlamsFromTheEdge_ThenHoldsFiveMetres()
        {
            var sim = Arena(11, walks: true);
            Assert.That(sim.Entities.MoveStep[Snarer].ToDouble() * 30, Is.EqualTo(2.4).Within(.002));
            int started = -1;
            for (int t = 0; t < 150 && started < 0; t++)
            {
                sim.Step(InputFrame.Empty);
                var v = sim.Entities.Velocity[Snarer];
                if (v.LengthSq.Raw != 0)
                    Assert.Greater(FixVec2.Dot(v.Normalized(), sim.Entities.Facing[Snarer]).ToDouble(), .99,
                        "шаг только вдоль взгляда, тик " + t);
                if (sim.TryGetRootSnarerAction(Snarer, out var a)) started = a.StartTick;
            }
            Assert.GreaterOrEqual(started, 0, "так и не ударил");
            double distance = (sim.Entities.Position[0] - sim.Entities.Position[Snarer]).Length.ToDouble();
            Assert.That(distance, Is.InRange(6.9, 7.0), "бьёт с края дальности, не подходя вплотную");

            // После стойки подходит до 5 м и стоит, пока удар перезаряжается.
            Until(sim, started + 140);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            distance = (sim.Entities.Position[0] - sim.Entities.Position[Snarer]).Length.ToDouble();
            Assert.That(distance, Is.InRange(4.8, 5.0));
            Assert.Less(sim.Entities.Velocity[Snarer].Length.ToDouble(), 1e-3);
        }

        // ---------- жетон ----------

        [Test]
        public void HoldsTheBigTokenFromPoseToImpact()
        {
            const int second = 2;
            var sim = Arena(5, count: 2);
            Assert.AreEqual(1, sim.BigAttackTokenLimit);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var first));
            Assert.IsFalse(sim.TryGetRootSnarerAction(second, out _), "второй удар корнями поверх первого");
            while (sim.Tick <= first.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetRootSnarerAction(second, out _), "жетон занят до контакта, тик " + (sim.Tick - 1));
            }
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(second, out var next));
            Assert.AreEqual(first.ImpactTick + 1, next.StartTick, "жетон свободен сразу после контакта");

            var wide = Arena(5, count: 2);
            wide.BigAttackTokenLimit = 2;
            wide.Step(InputFrame.Empty);
            Assert.IsTrue(wide.TryGetRootSnarerAction(Snarer, out _));
            Assert.IsTrue(wide.TryGetRootSnarerAction(second, out _), "с пятой арены крупных атак две");
        }

        [Test]
        public void WaitsWhileTheWendigoHowlHoldsTheToken()
        {
            var sim = Arena();
            int wendigo = HowlingWendigo(sim, At(0, 4));
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetWendigoAction(wendigo, out var howl));
            Assert.AreEqual(WendigoAction.Howl, howl.Kind);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "удар корнями поверх воя");
            Until(sim, howl.ImpactTick + 1);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "вой держит жетон до удара кольца");
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var late));
            Assert.AreEqual(howl.ImpactTick + 1, late.StartTick);
        }

        // ---------- глубина, расстановка, детерминизм ----------

        [TestCase(1, 100, 20)]
        [TestCase(8, 100, 31)]   // 20 × 156% = 31,2
        [TestCase(1, 125, 25)]   // «Сложно»
        public void DamageAndHealthGrowWithDepthAndHard(int arena, int hard, int damage)
        {
            var sim = Arena(5, arena: arena, hardPercent: hard);
            Assert.AreEqual(damage, sim.RootSnarerDamageOf(Snarer));
            Assert.AreEqual(EnemyArchetypes.ScaleHealth(EnemyArchetypes.Get(EnemyKind.ForestRootSnarer).BaseHealth,
                EnemyArchetypes.DepthHealthPercent(arena), hard), sim.Entities.MaxHealth[Snarer]);
            Assert.AreEqual(EnemyArchetypes.RootSnarerBodyRadius, sim.Entities.BodyRadius[Snarer]);
            int health = sim.Entities.Health[0];
            var a = Start(sim);
            Until(sim, a.ImpactTick + 1);
            Assert.AreEqual(damage, health - sim.Entities.Health[0]);
        }

        [Test]
        public void RepeatSetupClearsTheActionTheSlowAndTheCooldown()
        {
            var sim = Arena();
            var a = Start(sim);
            Until(sim, a.ImpactTick + 1);
            Assert.Greater(sim.HeroSlowTicksLeft, 0);
            sim.SetupKindTestArena(EnemyKind.ForestRootSnarer);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            Assert.AreEqual(0, sim.HeroSlowTicksLeft);
            Assert.AreEqual(0, sim.HeroSlowPercent);
            Assert.AreEqual(2, sim.Entities.Count);
            // Перезарядка прошлой расстановки не переходит в новую.
            int tick = sim.Tick;
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var fresh));
            Assert.AreEqual(tick, fresh.StartTick);
        }

        [Test]
        public void SameInputsProduceTheSameState()
        {
            var a = Arena(8, count: 3, walks: true);
            var b = Arena(8, count: 3, walks: true);
            a.BigAttackTokenLimit = 2; b.BigAttackTokenLimit = 2;
            int impacts = 0;
            for (int tick = 0; tick < 480; tick++)
            {
                // Герой ходит по кругу: круги встают то под ним, то мимо.
                double angle = tick * .03;
                var hero = At(3 * System.Math.Cos(angle), 3 * System.Math.Sin(angle));
                a.Entities.Position[0] = hero; b.Entities.Position[0] = hero;
                if (tick == 200) { a.Statuses.ApplyStun(2, 210); b.Statuses.ApplyStun(2, 210); }
                a.Step(InputFrame.Empty); b.Step(InputFrame.Empty);
                foreach (var e in a.Events) if (e.Type == SimEventType.EnemyActionImpact) impacts++;
                Assert.AreEqual(a.StateHash(), b.StateHash(), "тик " + tick);
            }
            Assert.Greater(impacts, 2, "Корнехваты должны были бить");
        }
    }
}
