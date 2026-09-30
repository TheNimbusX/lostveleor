using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Решения владельца от 29.09 по Вендиго и Шипомёту:
    /// • обоих нельзя толкать — вес 0: расталкивание их не сдвигает, волок не берёт;
    /// • «круг когтей» Вендиго — ответ на кружение: герой ≥30 из последних 45
    ///   тиков в 3 м, но вне конуса когтя (±70°) — замах 21 тик, круг 3,2 м на
    ///   земле (общая метка), урон когтя, отброс на 2 м, перезарядка 180 тиков.
    /// </summary>
    public sealed class WendigoSweepTests
    {
        private const int W = 1;

        /// <summary>
        /// Вендиго в начале координат смотрит по +X и стоит на месте; прыжок и вой
        /// выключены перезарядкой — остаются коготь и круг. Герой — в heroDistance
        /// прямо перед ним, с большим запасом здоровья.
        /// </summary>
        private static Simulation Arena(double heroDistance)
        {
            var sim = new Simulation(55, 64); sim.SetupWendigoEncounter(null, 55);
            sim.Entities.Position[W] = FixVec2.Zero;
            sim.Entities.Facing[W] = new FixVec2(Fix64.One, Fix64.Zero);
            sim.Entities.Position[0] = new FixVec2(Fix64.FromDouble(heroDistance), Fix64.Zero);
            sim.Entities.NextAttackTick[W] = 0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth, Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor, Fix64.Zero);
            sim.Entities.RefreshStats(0); sim.Entities.Health[0] = sim.Entities.MaxHealth[0];
            sim.Entities.Stats[W].SetBase(StatType.MoveSpeed, Fix64.Zero); sim.Entities.RefreshStats(W);
            sim.SetWendigoCooldowns(W, 100000, 100000);
            return sim;
        }

        /// <summary>
        /// Ставит героя на distance от Вендиго id под углом degrees к его взгляду.
        /// Зовётся перед каждым шагом: за шаг зверь доворачивает не больше 12°,
        /// так что «сзади» (180°) остаётся вне конуса ±70°, а «спереди» (0°) — в нём.
        /// </summary>
        private static void PlaceHero(Simulation sim, double distance, double degrees, int id = W)
        {
            var f = sim.Entities.Facing[id];
            double fx = f.X.ToDouble(), fy = f.Y.ToDouble();
            double r = degrees * System.Math.PI / 180, c = System.Math.Cos(r), s = System.Math.Sin(r);
            double dx = (fx * c - fy * s) * distance, dy = (fx * s + fy * c) * distance;
            sim.Entities.Position[0] = sim.Entities.Position[id] + new FixVec2(Fix64.FromDouble(dx), Fix64.FromDouble(dy));
        }

        private static bool Sweeping(Simulation sim, out WendigoActionState action, int id = W)
            => sim.TryGetWendigoAction(id, out action) && action.Kind == WendigoAction.Sweep;

        private static bool TelegraphOf(Simulation sim, int source, out EnemyTelegraph found)
        {
            found = default;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
                if (sim.TryGetTelegraph(slot, out var t) && t.Source == source && t.Serial > found.Serial) found = t;
            return found.Serial != 0;
        }

        private static int CountEvents(Simulation sim, SimEventType type, EnemyActionKind kind, int source = W)
        {
            int n = 0;
            foreach (var e in sim.Events)
                if (e.Type == type && e.Source == source && e.ActionVariant == (int)kind) n++;
            return n;
        }

        // ---- нельзя толкать ----

        /// <summary>
        /// Моб вида kind в начале координат, вплотную к нему — Хранитель (вес 1)
        /// и Корнеполз (вес 2). Все стоят: скорость 0, атаки выключены, герой в
        /// 30 м — без агро. Возвращает id моба; соседи — id + 1 и id + 2.
        /// </summary>
        private static Simulation Crowd(EnemyKind kind, out int id)
        {
            var sim = new Simulation(55, 64); sim.SetupTestArena(0);
            sim.Entities.Position[0] = new FixVec2(Fix64.FromInt(30), Fix64.Zero);
            id = sim.SpawnEnemy(FixVec2.Zero, 2000, kind);
            sim.SpawnEnemy(new FixVec2(Fix64.Ratio(1, 2), Fix64.Zero), 500, EnemyKind.ForestGuardian);
            sim.SpawnEnemy(new FixVec2(-Fix64.Ratio(2, 5), Fix64.Ratio(3, 10)), 500, EnemyKind.ForestRootSwarm);
            for (int e = id; e <= id + 2; e++)
            {
                sim.Entities.Stats[e].SetBase(StatType.MoveSpeed, Fix64.Zero); sim.Entities.RefreshStats(e);
                sim.Entities.NextAttackTick[e] = int.MaxValue; sim.Entities.Aggro[e] = false;
            }
            return sim;
        }

        private static void AssertImmovable(EnemyKind kind)
        {
            var sim = Crowd(kind, out int id);
            Assert.That(sim.Entities.PushWeight[id], Is.EqualTo(Fix64.Zero));
            var at = sim.Entities.Position[id];
            for (int t = 0; t < 90; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Position[id], Is.EqualTo(at), "расталкивание сдвинуло, тик " + t);
            }
            // Вся доля расталкивания досталась соседям: они вышли из тела целиком.
            for (int other = id + 1; other <= id + 2; other++)
            {
                var gap = FixVec2.Distance(sim.Entities.Position[other], at).ToDouble();
                var contact = (sim.Entities.BodyRadius[id] + sim.Entities.BodyRadius[other]).ToDouble();
                Assert.That(gap, Is.GreaterThanOrEqualTo(contact - 1e-3), "сосед " + other);
            }
            // Волок (вихрь с Подсечкой, волна Обета) не берёт: вес 0 — отказ ForcedMotion.Begin.
            Assert.That(ForcedMotion.Begin(sim.Entities, id, at + new FixVec2(Fix64.FromInt(3), Fix64.Zero), 8,
                ForcedMotionKind.Dragged), Is.False);
            Assert.That(ForcedMotion.IsActive(sim.Entities, id), Is.False);

            // Герой, влетевший в тело, отходит сам — зверь не уступает ни на миллиметр.
            sim.Entities.Position[0] = at + new FixVec2(Fix64.Zero, -Fix64.Ratio(3, 5));
            for (int t = 0; t < 60; t++)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Position[id], Is.EqualTo(at), "герой сдвинул, тик " + t);
            }
            var heroGap = FixVec2.Distance(sim.Entities.Position[0], at).ToDouble();
            Assert.That(heroGap, Is.GreaterThanOrEqualTo((sim.Entities.BodyRadius[id] + sim.Entities.BodyRadius[0]).ToDouble() - 1e-3));
        }

        [Test]
        public void WendigoIgnoresSeparation() => AssertImmovable(EnemyKind.ForestWendigo);

        [Test]
        public void ThorncasterIgnoresSeparation() => AssertImmovable(EnemyKind.ForestThorncaster);

        // ---- круг когтей ----

        [Test]
        public void SweepAfterCircling()
        {
            // Герой в 2,2 м — в досягаемости когтя — и всё время держится у бока
            // зверя, на 130° от взгляда: как бы тот ни повернулся, герой снова
            // заходит сбоку-сзади. Коготь его не достаёт, а круг — достаёт.
            var sim = Arena(2.2); int health = sim.Entities.Health[0];
            WendigoActionState sweep = default; bool started = false, clawed = false;
            for (int t = 0; t < 120 && !started; t++)
            {
                PlaceHero(sim, 2.2, 130);
                sim.Step(InputFrame.Empty);
                if (sim.TryGetWendigoAction(W, out var a) && a.Kind == WendigoAction.Claw) clawed = true;
                started = Sweeping(sim, out sweep);
                Assert.That(sim.Entities.Health[0], Is.EqualTo(health), "коготь задел кружащего, тик " + t);
            }
            Assert.That(clawed, Is.True, "сначала зверь бьёт когтем — герой уходит за спину");
            Assert.That(started, Is.True, "кружащий герой не дождался круга");
            Assert.That(sweep.StartTick, Is.LessThanOrEqualTo(2 * Simulation.WendigoClawCycleTicks));

            // Замах 21 тик, круг 3,2 м под самим зверем — общая метка на земле.
            Assert.That(sweep.ImpactTick - sweep.StartTick, Is.EqualTo(21));
            Assert.That(Simulation.WendigoSweepWindupTicks, Is.EqualTo(21));
            Assert.That(sweep.Origin, Is.EqualTo(sim.Entities.Position[W]));
            Assert.That(sweep.Target, Is.EqualTo(sweep.Origin));
            Assert.That(TelegraphOf(sim, W, out var circle), Is.True);
            Assert.That(circle.Shape, Is.EqualTo(TelegraphShape.Circle));
            Assert.That(circle.SharedView, Is.True, "круг рисует общий вид меток");
            Assert.That(circle.Origin, Is.EqualTo(sweep.Origin));
            Assert.That(circle.Radius, Is.EqualTo(Fix64.Ratio(16, 5)));
            Assert.That(circle.ImpactTick, Is.EqualTo(sweep.ImpactTick));
            // Начало — общим событием действий мобов, своё WendigoStarted круг не шлёт.
            Assert.That(CountEvents(sim, SimEventType.EnemyActionStarted, EnemyActionKind.WendigoSweep), Is.EqualTo(1));
            foreach (var e in sim.Events) Assert.That(e.Type, Is.Not.EqualTo(SimEventType.WendigoStarted));
            Assert.That(sim.WendigoFlankTicks(W), Is.Zero, "окно кружения начинается заново");

            // Герой продолжает кружить весь замах — и получает круг ровно на контакте.
            int impacts = 0;
            while (sim.Tick <= sweep.ImpactTick)
            {
                int tick = sim.Tick;
                PlaceHero(sim, 2.2, 130);
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionImpact && e.ActionVariant == (int)EnemyActionKind.WendigoSweep)
                    {
                        impacts++;
                        Assert.That(tick, Is.EqualTo(sweep.ImpactTick));
                        Assert.That(e.Flag, Is.True); Assert.That(e.Position, Is.EqualTo(sweep.Origin));
                    }
                if (tick < sweep.ImpactTick) Assert.That(sim.Entities.Health[0], Is.EqualTo(health), "tick " + tick);
            }
            Assert.That(impacts, Is.EqualTo(1));
            Assert.That(health - sim.Entities.Health[0], Is.EqualTo(sim.WendigoSweepDamageOf(W)));
            Assert.That(sim.WendigoSweepDamageOf(W), Is.EqualTo(sim.WendigoClawDamageOf(W)), "урон круга — урон когтя");
            Assert.That(sim.Entities.ForcedKind[0], Is.EqualTo((byte)ForcedMotionKind.Knockback));
        }

        [TestCase(2.2, 0.0)]
        [TestCase(2.2, 60.0)]
        [TestCase(2.85, 0.0)]
        public void NoSweepWhenInFront(double distance, double degrees)
        {
            // Герой в конусе когтя (±70°) — это обычный бой, не кружение: сколько
            // бы он ни стоял рядом, зверь бьёт когтем, а круга нет.
            var sim = Arena(distance);
            for (int t = 0; t < 400; t++)
            {
                PlaceHero(sim, distance, degrees);
                sim.Step(InputFrame.Empty);
                Assert.That(Sweeping(sim, out _), Is.False, "tick " + t);
                Assert.That(sim.WendigoFlankTicks(W), Is.Zero, "tick " + t);
            }
            if (distance < Simulation.WendigoClawRange.ToDouble())
                Assert.That(sim.Entities.Health[0], Is.LessThan(sim.Entities.MaxHealth[0]), "коготь должен был бить");
        }

        [Test]
        public void NoSweepFromBeyondWatchRange()
        {
            // За спиной, но дальше 3 м — это не кружение у бока, круга нет.
            var sim = Arena(3.4);
            for (int t = 0; t < 200; t++)
            {
                PlaceHero(sim, 3.4, 180);
                sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetWendigoAction(W, out _), Is.False, "tick " + t);
            }
            Assert.That(sim.WendigoFlankTicks(W), Is.Zero);
        }

        /// <summary>
        /// Герой в 2,85 м: дальше когтя (2,7), ближе прыжка (3), вой выключен —
        /// единственное, что может начать зверь, это круг. pattern — отрезки
        /// «сзади/спереди» по очереди, начиная со «сзади».
        /// </summary>
        private static int FirstSweepTick(int[] pattern, out int flankAtStart)
        {
            var sim = Arena(2.85); flankAtStart = -1; int t = 0;
            for (int part = 0; part < pattern.Length; part++)
                for (int k = 0; k < pattern[part]; k++, t++)
                {
                    PlaceHero(sim, 2.85, part % 2 == 0 ? 180 : 0);
                    int before = sim.WendigoFlankTicks(W);
                    sim.Step(InputFrame.Empty);
                    if (sim.TryGetWendigoAction(W, out var a))
                    {
                        Assert.That(a.Kind, Is.EqualTo(WendigoAction.Sweep));
                        flankAtStart = before + 1;
                        return t;
                    }
                }
            return -1;
        }

        [Test]
        public void SweepNeedsThirtyOfTheLast45Ticks()
        {
            // 29 тиков за спиной — мало, 30-й — круг.
            Assert.That(FirstSweepTick(new[] { 29 }, out _), Is.EqualTo(-1));
            Assert.That(FirstSweepTick(new[] { 30 }, out int flank), Is.EqualTo(29));
            Assert.That(flank, Is.EqualTo(Simulation.WendigoSweepFlankTicks));
            // Не подряд: 20 сзади, 10 спереди, ещё 10 сзади — 30 из 40, круг на 40-м тике.
            Assert.That(FirstSweepTick(new[] { 20, 10, 10 }, out _), Is.EqualTo(39));
            // Старое кружение забывается: 20 сзади, 25 спереди — окно полно, и
            // каждый новый тик сзади вытесняет старый. Круг — только на 30-м
            // тике нового захода, когда в 45 тиках снова 30 «сзади».
            Assert.That(FirstSweepTick(new[] { 20, 25, 29 }, out _), Is.EqualTo(-1));
            Assert.That(FirstSweepTick(new[] { 20, 25, 30 }, out _), Is.EqualTo(20 + 25 + 29));
            Assert.That(Simulation.WendigoSweepWindowTicks, Is.EqualTo(45));
        }

        [Test]
        public void SweepCooldown()
        {
            // Неуязвимый герой всё время за спиной: круги идут ровно раз в 180 тиков от начала.
            var sim = Arena(2.85); sim.PlayerInvulnerable = true;
            var starts = new List<int>(); int serial = 0;
            while (sim.Tick < 29 + 2 * Simulation.WendigoSweepCooldownTicks + 1)
            {
                PlaceHero(sim, 2.85, 180);
                sim.Step(InputFrame.Empty);
                if (!sim.TryGetWendigoAction(W, out var a) || a.Serial == serial) continue;
                serial = a.Serial;
                Assert.That(a.Kind, Is.EqualTo(WendigoAction.Sweep));
                starts.Add(a.StartTick);
            }
            Assert.That(Simulation.WendigoSweepCooldownTicks, Is.EqualTo(180));
            Assert.That(starts, Is.EqualTo(new[] { 29, 29 + 180, 29 + 360 }));

            // Перезарядку и окно слежки видит хеш: стенд, сдвинувший перезарядку, — другое состояние.
            ulong hash = sim.StateHash();
            sim.SetWendigoSweepCooldown(W, sim.Tick + 7);
            Assert.That(sim.StateHash(), Is.Not.EqualTo(hash));
        }

        // Тело героя 0,45: круг 3,2 м задевает его до 3,65 м от центра.
        [TestCase(2.85, true)]
        [TestCase(3.6, true)]
        [TestCase(3.7, false)]
        public void SweepKnocksBack(double heroDistance, bool hit)
        {
            var sim = Arena(2.85);
            WendigoActionState sweep = default;
            for (int t = 0; t < 30; t++) { PlaceHero(sim, 2.85, 180); sim.Step(InputFrame.Empty); }
            Assert.That(Sweeping(sim, out sweep), Is.True);

            // Пока круг заполняется, герой отходит — или остаётся — и замирает.
            var away = new FixVec2(-Fix64.One, Fix64.Zero);
            var hero = sweep.Origin + away * Fix64.FromDouble(heroDistance);
            sim.Entities.Position[0] = hero;
            Assert.That(Simulation.TelegraphContains(Simulation.WendigoSweepCircle(sweep.Origin), hero,
                sim.Entities.BodyRadius[0]), Is.EqualTo(hit));
            int health = sim.Entities.Health[0];
            while (sim.Tick <= sweep.ImpactTick)
            {
                int tick = sim.Tick; sim.Step(InputFrame.Empty);
                if (tick < sweep.ImpactTick) Assert.That(sim.Entities.Position[0], Is.EqualTo(hero), "до удара отброса нет");
                if (tick != sweep.ImpactTick) continue;
                Assert.That(CountEvents(sim, SimEventType.EnemyActionImpact, EnemyActionKind.WendigoSweep), Is.EqualTo(1));
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.EnemyActionImpact) Assert.That(e.Flag, Is.EqualTo(hit));
                Assert.That(TelegraphOf(sim, W, out var circle), Is.True);
                Assert.That(circle.State, Is.EqualTo(TelegraphState.Resolved));
            }
            Assert.That(health - sim.Entities.Health[0], Is.EqualTo(hit ? sim.WendigoClawDamageOf(W) : 0));
            Assert.That(ForcedMotion.IsActive(sim.Entities, 0), Is.EqualTo(hit));
            if (hit) Assert.That(sim.Entities.ForcedKind[0], Is.EqualTo((byte)ForcedMotionKind.Knockback));

            // Отброс — ровно 2 м от центра круга, по той же прямой, за 10 тиков.
            for (int k = 0; k < Simulation.WendigoSweepKnockbackTicks + 2; k++) sim.Step(InputFrame.Empty);
            Assert.That(ForcedMotion.IsActive(sim.Entities, 0), Is.False);
            var offset = sim.Entities.Position[0] - sweep.Origin;
            double expected = heroDistance + (hit ? 2.0 : 0.0);
            Assert.That(offset.Length.ToDouble(), Is.EqualTo(expected).Within(1e-3));
            Assert.That(offset.Y.ToDouble(), Is.EqualTo(0.0).Within(1e-3));
            Assert.That(offset.X.ToDouble(), Is.LessThan(0.0));
        }

        [Test]
        public void SweepIsCancelledByStunWithoutDamage()
        {
            var sim = Arena(2.85);
            for (int t = 0; t < 30; t++) { PlaceHero(sim, 2.85, 180); sim.Step(InputFrame.Empty); }
            Assert.That(Sweeping(sim, out var sweep), Is.True);
            int health = sim.Entities.Health[0];
            sim.Statuses.ApplyStun(W, sim.Tick + 3);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(W, out _), Is.False);
            Assert.That(CountEvents(sim, SimEventType.EnemyActionCancelled, EnemyActionKind.WendigoSweep), Is.EqualTo(1));
            foreach (var e in sim.Events) Assert.That(e.Type, Is.Not.EqualTo(SimEventType.WendigoCancelled));
            Assert.That(TelegraphOf(sim, W, out var circle), Is.True);
            Assert.That(circle.State, Is.EqualTo(TelegraphState.Cancelled));
            while (sim.Tick <= sweep.ImpactTick + 5) sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Health[0], Is.EqualTo(health));
        }

        [Test]
        public void SweepWaitsForTheBigTokenAndWeighsOneMark()
        {
            // Второй Вендиго прыгает и держит крупный жетон до приземления; первый
            // уже накружен (30 из 45), но круг ставит только после посадки.
            var sim = Arena(2.85);
            int other = sim.SpawnEnemy(new FixVec2(-Fix64.Ratio(285, 100), Fix64.FromInt(5)), 2000, EnemyKind.ForestWendigo);
            sim.Entities.Stats[other].SetBase(StatType.MoveSpeed, Fix64.Zero); sim.Entities.RefreshStats(other);
            sim.Entities.Aggro[other] = true; sim.Entities.NextAttackTick[other] = 0;
            sim.SetWendigoCooldowns(other, 0, 100000);
            sim.SetWendigoSweepCooldown(other, 100000);
            PlaceHero(sim, 2.85, 180);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(other, out var leap), Is.True);
            Assert.That(leap.Kind, Is.EqualTo(WendigoAction.Leap));
            while (sim.Tick <= leap.ImpactTick)
            {
                PlaceHero(sim, 2.85, 180);
                sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetWendigoAction(W, out _), Is.False, "круг поверх прыжка, тик " + (sim.Tick - 1));
            }
            Assert.That(sim.WendigoFlankTicks(W), Is.GreaterThanOrEqualTo(Simulation.WendigoSweepFlankTicks));
            PlaceHero(sim, 2.85, 180);
            sim.Step(InputFrame.Empty);
            Assert.That(Sweeping(sim, out var sweep), Is.True);
            Assert.That(sweep.StartTick, Is.EqualTo(leap.ImpactTick + 1));
            // Круг держит жетон до удара и лежит в бюджете меток весом 1.
            Assert.That(sim.BigMarkLoad(out int lastStart), Is.EqualTo(1));
            Assert.That(lastStart, Is.EqualTo(sweep.StartTick));
            Until(sim, sweep.ImpactTick + 1);
            Assert.That(sim.BigMarkLoad(out _), Is.Zero, "после удара жетон свободен");
        }

        private static void Until(Simulation sim, int tick) { while (sim.Tick < tick) sim.Step(InputFrame.Empty); }
    }
}
