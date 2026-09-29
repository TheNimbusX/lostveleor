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
    /// урон и корни на 30 тиков, мимо — ничего; оглушение, волок и смерть
    /// снимают круг; 36 тиков стойки после контакта; крупный жетон — от
    /// начала позы до контакта.
    ///
    /// Контроль героя (29.09, поток C «Корни и контроль героя»): корни — не
    /// ходит и не кувыркается, но бьёт и кастует; с замедлением воя не
    /// сливаются. Оглушение (ApplyHeroStun, разбег Камнекопыта) — ни шага,
    /// ни удара, ни каста, ни кувырка, но отброс доезжает. После любого
    /// контроля 45 тиков иммунитета: цепочки нет.
    ///
    /// «Волна из корней» (27.09): лечит 10% здоровья союзника (элите 5%), не
    /// выше недостающего; ни себя, ни других Корнехватов; одного союзника —
    /// не чаще раза в 240 тиков, сколько бы Корнехватов ни было; оглушение
    /// или урон от 15% за 30 тиков сбора сбивают, и следующее — через 150;
    /// главнее удара, крупного жетона не берёт; к далёкому раненому сначала
    /// идёт, но не дольше 60 тиков.
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
        // immune — герой в круге, но неуязвим: урона нет — нет и корней.
        [TestCase(0.0, true, false)]
        [TestCase(1.9, true, false)]
        [TestCase(2.0, false, false)]
        [TestCase(3.5, false, false)]
        [TestCase(0.0, true, true)]
        public void RootOnlyOnHit(double offset, bool inCircle, bool immune)
        {
            Assert.IsTrue(Simulation.SnarerRoots, "решение владельца 29.09 — корни, не замедление");
            Assert.AreEqual(30, Simulation.RootSnarerRootTicks);
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
            Assert.AreEqual(inCircle, Simulation.TelegraphContains(circle, hero, sim.Entities.BodyRadius[0]));
            sim.PlayerInvulnerable = immune;

            bool? flag = null;
            var controls = new List<SimEvent>();
            while (sim.Tick <= a.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.AreEqual(full, sim.Entities.MoveStep[0], "до контакта корней нет");
                foreach (var e in sim.Events)
                {
                    if (e.Type == SimEventType.EnemyActionImpact && e.Source == Snarer) flag = e.Flag;
                    if (e.Type == SimEventType.HeroControl) controls.Add(e);
                }
            }
            Assert.IsTrue(flag.HasValue, "контакта не было");
            Assert.AreEqual(inCircle, flag.Value);
            bool rooted = inCircle && !immune;
            Assert.AreEqual(rooted ? EnemyArchetypes.RootSnarerDamage : 0, health - sim.Entities.Health[0]);
            Assert.AreEqual(rooted, sim.HeroRooted);
            Assert.AreEqual(rooted ? 30 : 0, sim.HeroRootTicksLeft);
            Assert.IsFalse(sim.HeroStunned);
            Assert.AreEqual(0, sim.HeroSlowTicksLeft, "корни — не замедление");
            Assert.AreEqual(0, sim.HeroSlowPercent);

            // Вид узнаёт о корнях из события HeroControl — одного, в тик контакта.
            Assert.AreEqual(rooted ? 1 : 0, controls.Count);
            if (rooted)
            {
                var e = controls[0];
                Assert.AreEqual(Snarer, e.Source);
                Assert.AreEqual(Simulation.PlayerId, e.Target);
                Assert.AreEqual(Simulation.RootSnarerRootTicks, e.Amount);
                Assert.IsTrue(e.Flag, "корни, а не оглушение");
                Assert.AreEqual(sim.Entities.Position[0], e.Position);
            }
            for (int k = 0; k < 45; k++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(full, sim.Entities.MoveStep[0]);
            Assert.IsFalse(sim.HeroRooted);
        }

        [Test]
        public void RootLasts30Ticks()
        {
            var sim = Arena();
            var a = Start(sim);
            Until(sim, a.ImpactTick + 1);
            Assert.IsTrue(sim.HeroRooted, "герой стоял в круге");

            // Герой всё это время рвётся в сторону — ни шага, пока держат корни.
            var walk = InputFrame.Empty;
            walk.Flags = (byte)InputFlags.MoveOrder;
            walk.Aim = sim.Entities.Position[0] + At(0, -10);
            var spot = sim.Entities.Position[0];
            int held = 0;
            for (int k = 0; k < 40 && sim.Entities.Position[0].Equals(spot); k++)
            {
                Assert.AreEqual(30 - k, sim.HeroRootTicksLeft, "шаг " + k);
                sim.Step(walk);
                if (sim.Entities.Position[0].Equals(spot)) held++;
            }
            Assert.AreEqual(Simulation.RootSnarerRootTicks, held, "корни — ровно 30 шагов героя");
            Assert.IsFalse(sim.HeroRooted);
            Assert.Less(sim.Entities.Position[0].Y.ToDouble(), spot.Y.ToDouble() - .05, "корни сошли — пошёл");
        }

        [Test]
        public void RootsDoNotMergeWithTheWendigoHowlSlow()
        {
            var sim = Arena();
            sim.BigAttackTokenLimit = 2;
            int wendigo = HowlingWendigo(sim, At(0, 4));
            var full = sim.Entities.MoveStep[0];

            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetWendigoAction(wendigo, out var howl));
            Assert.AreEqual(WendigoAction.Howl, howl.Kind);
            // Бюджет меток: следующая крупная метка — не раньше чем через 9 тиков.
            RootSnarerState a = default;
            while (!sim.TryGetRootSnarerAction(Snarer, out a) && sim.Tick < howl.StartTick + 30)
                sim.Step(InputFrame.Empty);
            Assert.AreNotEqual(0, a.Serial, "с пятой арены жетонов два");
            Assert.AreEqual(howl.StartTick + Simulation.BigMarkStaggerTicks, a.StartTick);
            Assert.Less(howl.ImpactTick, a.ImpactTick);

            // Вой первым: 30% на 30 тиков.
            Until(sim, howl.ImpactTick + 1);
            Assert.AreEqual(Simulation.WendigoHowlSlowPercent, sim.HeroSlowPercent);
            Assert.AreEqual(Simulation.WendigoHowlSlowTicks, sim.HeroSlowTicksLeft);

            // Корни поверх — своё состояние: ни процент, ни срок замедления не меняются.
            Until(sim, a.ImpactTick);
            int slowLeft = sim.HeroSlowTicksLeft, slowPercent = sim.HeroSlowPercent;
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.HeroRooted);
            Assert.AreEqual(Simulation.RootSnarerRootTicks, sim.HeroRootTicksLeft);
            Assert.AreEqual(System.Math.Max(0, slowLeft - 1), sim.HeroSlowTicksLeft, "срок замедления не тронут");
            if (slowLeft > 1) Assert.AreEqual(slowPercent, sim.HeroSlowPercent);

            // Замедление длиннее корней: сначала герой стоит, потом идёт на 70%.
            sim.ApplyHeroSlow(Simulation.WendigoHowlSlowPercent, 60);
            Assert.AreEqual(61, sim.HeroSlowTicksLeft);
            Assert.AreEqual(Simulation.RootSnarerRootTicks, sim.HeroRootTicksLeft, "замедление корни не продлевает");
            int held = 0, slowed = 0;
            for (int k = 0; k < 80; k++)
            {
                sim.Step(InputFrame.Empty);
                if (sim.Entities.MoveStep[0] == Fix64.Zero) { held++; continue; }
                if (sim.Entities.MoveStep[0] == full) continue;
                slowed++;
                Assert.That(sim.Entities.MoveStep[0].ToDouble(), Is.EqualTo(full.ToDouble() * .7).Within(1e-3),
                    "после корней — только замедление");
            }
            Assert.AreEqual(30, held, "корни — ровно 30 шагов");
            Assert.AreEqual(31, slowed, "замедление — остаток после корней");
            Assert.AreEqual(full, sim.Entities.MoveStep[0]);
        }

        // ---------- контроль героя: корни, оглушение, иммунитет ----------

        /// <summary>
        /// Пустое поле с полным набором героя (Вихрь в слоте 0, кувырок в
        /// PelagKit.DashSlot) и Выпадом в слоте 1; манекен в 1 м по +X — не
        /// ходит, не бьёт, здоровья много.
        /// </summary>
        private static Simulation HeroArena(out int dummy)
        {
            var sim = new Simulation(1234, 64);
            sim.SetupTestArena(0);
            new RunLoadout().ApplyTo(sim);
            sim.SetAbility(1, PelagKit.PoolDefinition(PelagKit.PoolIndexOf(AbilityDefinition.SkewerId)),
                new AbilityNode[0], 0);
            dummy = sim.Entities.Spawn(At(1, 0), 5000, Faction.Orvill);
            sim.Entities.Stats[dummy].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[dummy].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(dummy);
            sim.Entities.NextAttackTick[dummy] = int.MaxValue;
            return sim;
        }

        private static int Count(Simulation sim, SimEventType type)
        {
            int n = 0;
            foreach (var e in sim.Events) if (e.Type == type) n++;
            return n;
        }

        private static InputFrame Walk(FixVec2 to)
        {
            var walk = InputFrame.Empty;
            walk.Flags = (byte)InputFlags.MoveOrder;
            walk.Aim = to;
            return walk;
        }

        private static InputFrame Press(int slot, FixVec2 aim)
        {
            var press = InputFrame.Empty;
            press.AbilityMask = (byte)(1 << slot);
            press.Aim = aim;
            return press;
        }

        [Test]
        public void RootedHeroCannotMoveOrDashButCanAttack()
        {
            var sim = HeroArena(out int dummy);
            var away = At(-5, 0);
            Assert.IsTrue(sim.ApplyHeroRoot(90));
            Assert.AreEqual(1, Count(sim, SimEventType.HeroControl));

            // Приказ идти — ни шага.
            for (int k = 0; k < 5; k++) sim.Step(Walk(away));
            Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0], "в корнях герой не идёт");

            // Уходы — кувырок и Выпад — не начинаются: нажатие пропадает.
            foreach (int slot in new[] { PelagKit.DashSlot, 1 })
            {
                sim.Step(Press(slot, away));
                Assert.AreEqual(0, Count(sim, SimEventType.AbilityCast), "уход в корнях, слот " + slot);
                for (int k = 0; k < 8; k++) sim.Step(InputFrame.Empty);
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0], "уход в корнях, слот " + slot);
            }

            // Удар — есть.
            int health = sim.Entities.Health[dummy];
            var attack = InputFrame.Empty;
            attack.Flags = (byte)InputFlags.Attack;
            attack.AttackTarget = dummy;
            attack.Aim = At(1, 0);
            for (int k = 0; k < 30 && sim.Entities.Health[dummy] == health; k++) sim.Step(attack);
            Assert.Less(sim.Entities.Health[dummy], health, "в корнях герой бьёт");

            // Каст — есть: Вихрь из слота 0.
            int casts = 0;
            for (int k = 0; k < 10 && casts == 0; k++)
            {
                sim.Step(Press(0, At(1, 0)));
                casts = Count(sim, SimEventType.AbilityCast);
            }
            Assert.AreEqual(1, casts, "в корнях герой кастует");
            Assert.IsTrue(sim.HeroRooted);
            Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0]);

            // Корни сошли — тот же Выпад уходит.
            Until(sim, sim.Tick + sim.HeroRootTicksLeft);
            for (int k = 0; k < 40; k++) sim.Step(InputFrame.Empty);
            sim.Step(Press(1, away));
            Assert.AreEqual(1, Count(sim, SimEventType.AbilityCast), "Выпад после корней");
            Assert.AreEqual((byte)ForcedMotionKind.Skewer, sim.Entities.ForcedKind[0]);
        }

        /// <summary>Манекен, который не ходит, не бьёт и не умирает от пары ударов.</summary>
        private static int Dummy(Simulation sim, FixVec2 at)
        {
            int id = sim.Entities.Spawn(at, 5000, Faction.Orvill);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            return id;
        }

        private static void FillLavidium(Simulation sim)
            => sim.Entities.Lavidium[0] = Fix64.FromInt(sim.Entities.MaxLavidium[0]);

        [Test]
        public void OnlyAbilitiesThatMoveTheHeroAreHeldByRoots()
        {
            // Аудит 29.09: героя переставляют только эти пять — все через
            // ForcedMotion героя (CastDash, BeginMobility, CastBoarding,
            // BeginChainStep и «Возврат» Шага по цепи).
            var moving = new[]
            {
                AbilityDefinition.DashId, AbilityDefinition.SkewerId, AbilityDefinition.BackblastId,
                AbilityDefinition.AnchorLeapId, AbilityDefinition.ChainStepId,
            };
            Assert.IsTrue(Simulation.MovesHero(AbilityDefinition.DashId), "кувырок");
            for (int i = 0; i < PelagKit.PoolSize; i++)
            {
                int id = PelagKit.PoolDefinition(i).Id;
                Assert.AreEqual(System.Array.IndexOf(moving, id) >= 0, Simulation.MovesHero(id), "пул " + i);
            }
        }

        /// <summary>
        /// Решение владельца 29.09: в корнях герой не двигается НИКАК. Кроме
        /// шага и кувырка не начинаются Выпад, Отскок, Абордаж и Шаг по цепи:
        /// ни каста, ни шага, ни перезарядки, ни лавидия, начатый удар цел.
        /// Удар и Вихрь на месте — есть. Корни сошли — то же нажатие уходит в
        /// первый же тик. poolIndex — индекс пула; −1 — кувырок.
        /// </summary>
        [TestCase(-1)]  // кувырок
        [TestCase(8)]   // Выпад
        [TestCase(9)]   // Отскок
        [TestCase(6)]   // Абордаж
        [TestCase(3)]   // Шаг по цепи
        public void RootedHeroStartsNothingThatMovesHim_ThenGoesTheTickTheRootEnds(int poolIndex)
        {
            var sim = HeroArena(out int dummy);
            int slot = poolIndex < 0 ? PelagKit.DashSlot : 1;
            if (poolIndex >= 0) sim.SetAbility(slot, PelagKit.PoolDefinition(poolIndex), new AbilityNode[0], 0);
            // Дальняя цель: Абордажу и Шагу по цепи есть к кому лететь.
            int far = Dummy(sim, At(0, 4));
            Assert.IsTrue(Simulation.MovesHero(sim.GetAbility(slot).DefinitionId));
            Assert.IsFalse(sim.AbilityHeldByRoots(slot), "без корней кнопка свободна");

            var press = Press(slot, At(0, 4));
            press.AbilityTarget = far;
            if (poolIndex == 3) Assert.IsTrue(sim.ValidAbilityTarget(far, sim.GetAbility(slot)), "Шагу по цепи нужна цель");

            Assert.IsTrue(sim.ApplyHeroRoot(150));
            Assert.IsTrue(sim.AbilityHeldByRoots(slot), "HUD: кнопку держат корни");
            Assert.IsFalse(sim.AbilityHeldByRoots(0), "Вихрь корни не держат");
            Assert.IsFalse(sim.AbilityHeldByRoots(-1));

            // Замах по ближнему манекену — нажатие в корнях его не сбивает.
            int health = sim.Entities.Health[dummy];
            var attack = InputFrame.Empty;
            attack.Flags = (byte)InputFlags.Attack;
            attack.AttackTarget = dummy;
            attack.Aim = At(1, 0);
            for (int k = 0; k < 10 && sim.Entities.PendingAttackTarget[0] != dummy; k++) sim.Step(attack);
            Assert.AreEqual(dummy, sim.Entities.PendingAttackTarget[0], "замах начался");
            int impact = sim.Entities.AttackImpactTick[0];
            Assert.Greater(impact, sim.Tick);
            sim.Step(press);
            Assert.AreEqual(0, Count(sim, SimEventType.AbilityCast), "каст в корнях");
            Assert.AreEqual(dummy, sim.Entities.PendingAttackTarget[0], "нажатие в корнях сбило замах");
            Assert.AreEqual(impact, sim.Entities.AttackImpactTick[0]);

            // Жмёт без остановки дольше замаха Абордажа — ни каста, ни шага.
            FillLavidium(sim);
            Fix64 full = sim.Entities.Lavidium[0];
            int ready = sim.AbilityReadyTick(slot);
            for (int k = 0; k < 30; k++)
            {
                sim.Step(press);
                Assert.AreEqual(0, Count(sim, SimEventType.AbilityCast), "каст в корнях, шаг " + k);
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0], "герой сдвинулся в корнях, шаг " + k);
                Assert.AreEqual(0, sim.Entities.ForcedTicksLeft[0], "перемещение в корнях, шаг " + k);
            }
            Assert.AreEqual(ready, sim.AbilityReadyTick(slot), "перезарядка не пошла");
            Assert.AreEqual(full, sim.Entities.Lavidium[0], "лавидий не списан");
            Assert.Less(sim.Entities.Health[dummy], health, "в корнях герой бьёт");

            // Каст на месте — есть: Вихрь из слота 0.
            int casts = 0;
            for (int k = 0; k < 30 && casts == 0; k++)
            {
                sim.Step(Press(0, At(1, 0)));
                casts = Count(sim, SimEventType.AbilityCast);
            }
            Assert.AreEqual(1, casts, "в корнях герой кастует на месте");
            // Приказ стоять на месте снимает автоатаку: к концу корней герой свободен.
            sim.Step(Walk(FixVec2.Zero));
            Assert.IsTrue(sim.HeroRooted);
            Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0]);

            // Корни сошли — в тот же тик нажатие уходит, и герой едет.
            Until(sim, sim.Tick + sim.HeroRootTicksLeft);
            Assert.IsFalse(sim.HeroRooted);
            Assert.IsFalse(sim.AbilityHeldByRoots(slot), "корни сошли — кнопка свободна");
            FillLavidium(sim);
            sim.Step(press);
            Assert.AreEqual(1, Count(sim, SimEventType.AbilityCast), "первый тик без корней");
            Assert.Greater(sim.AbilityReadyTick(slot), sim.Tick, "перезарядка пошла");
            for (int k = 0; k < 40 && sim.Entities.Position[0].Equals(FixVec2.Zero); k++) sim.Step(InputFrame.Empty);
            Assert.Greater(sim.Entities.Position[0].Length.ToDouble(), .05, "после корней способность двигает героя");
        }

        /// <summary>
        /// Остальные способности пула героя с места не сдвигают — в корнях
        /// они кастуются как обычно, и герой стоит всё их действие.
        /// </summary>
        [TestCase(0)]   // Вихрь
        [TestCase(1)]   // Рассекающий удар
        [TestCase(2)]   // Смазка
        [TestCase(4)]   // Удар якорем
        [TestCase(5)]   // Крушение
        [TestCase(7)]   // Взрывная смесь
        public void RootedHeroCastsInPlace(int poolIndex)
        {
            var sim = HeroArena(out int dummy);
            sim.SetAbility(1, PelagKit.PoolDefinition(poolIndex), new AbilityNode[0], 0);
            Assert.IsFalse(Simulation.MovesHero(sim.GetAbility(1).DefinitionId));
            Assert.IsTrue(sim.ApplyHeroRoot(150));
            Assert.IsFalse(sim.AbilityHeldByRoots(1));
            FillLavidium(sim);

            var press = Press(1, At(1, 0));
            press.AbilityTarget = dummy;
            int casts = 0;
            for (int k = 0; k < 10 && casts == 0; k++)
            {
                sim.Step(press);
                casts = Count(sim, SimEventType.AbilityCast);
            }
            Assert.AreEqual(1, casts, "в корнях каст на месте проходит");
            for (int k = 0; k < 90; k++)
            {
                sim.Step(k % 2 == 0 ? press : InputFrame.Empty);
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0], "шаг " + k);
            }
            Assert.IsTrue(sim.HeroRooted);
        }

        [Test]
        public void StunBlocksEverything()
        {
            var sim = HeroArena(out int dummy);
            var attack = InputFrame.Empty;
            attack.Flags = (byte)InputFlags.Attack;
            attack.AttackTarget = dummy;
            attack.Aim = At(1, 0);
            sim.Step(attack);
            Assert.AreEqual(dummy, sim.Entities.PendingAttackTarget[0], "замах начался");
            int health = sim.Entities.Health[dummy];

            Assert.IsTrue(sim.ApplyHeroStun(30));
            Assert.IsTrue(sim.HeroStunned);
            Assert.IsFalse(sim.HeroRooted);
            // Событие для вида: оглушение (Flag = false) на 30 тиков.
            Assert.AreEqual(1, Count(sim, SimEventType.HeroControl));
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.HeroControl) continue;
                Assert.AreEqual(-1, e.Source);
                Assert.AreEqual(Simulation.PlayerId, e.Target);
                Assert.AreEqual(30, e.Amount);
                Assert.IsFalse(e.Flag, "оглушение, а не корни");
                Assert.AreEqual(sim.Entities.Position[0], e.Position);
            }

            // Вызов между шагами держит и ближайший шаг — как ApplyHeroSlow.
            int stunned = sim.HeroStunTicksLeft;
            Assert.AreEqual(31, stunned);
            var everything = attack;
            everything.Aim = At(-5, 0);
            everything.AbilityMask = (byte)(1 | 1 << 1 | 1 << PelagKit.DashSlot);
            for (int k = 0; k < stunned; k++)
            {
                Assert.IsTrue(sim.HeroStunned, "шаг " + k);
                sim.Step(k % 2 == 0 ? everything : Walk(At(-5, 0)));
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Position[0], "шаг в оглушении, шаг " + k);
                Assert.AreEqual(0, Count(sim, SimEventType.AbilityCast), "каст в оглушении, шаг " + k);
                Assert.AreEqual(-1, sim.Entities.PendingAttackTarget[0], "замах в оглушении, шаг " + k);
            }
            Assert.AreEqual(health, sim.Entities.Health[dummy], "в оглушении герой не бьёт, начатый замах снят");
            Assert.IsFalse(sim.HeroStunned);

            // Оглушение сошло — то же нажатие работает: уход главнее.
            sim.Step(everything);
            Assert.AreEqual(1, Count(sim, SimEventType.AbilityCast), "после оглушения");
            Assert.AreEqual((byte)ForcedMotionKind.Roll, sim.Entities.ForcedKind[0]);
        }

        [Test]
        public void StunKeepsTheKnockbackItCameWith_ButCutsTheHerosOwnRoll()
        {
            // Разбег Камнекопыта: отброс и оглушение в один тик — отброс доезжает.
            var sim = HeroArena(out _);
            var to = At(0, -2);
            Assert.IsTrue(ForcedMotion.Begin(sim.Entities, 0, to, 6, ForcedMotionKind.Knockback));
            Assert.IsTrue(sim.ApplyHeroStun(30));
            for (int k = 0; k < 6; k++) sim.Step(InputFrame.Empty);
            Assert.AreEqual(to, sim.Entities.Position[0], "отброс доехал");
            Assert.IsTrue(sim.HeroStunned);

            // Свой кувырок оглушение обрывает, как и любое своё действие.
            var rolled = HeroArena(out _);
            rolled.Step(Press(PelagKit.DashSlot, At(-5, 0)));
            Assert.AreEqual((byte)ForcedMotionKind.Roll, rolled.Entities.ForcedKind[0]);
            Assert.IsTrue(rolled.ApplyHeroStun(30));
            var at = rolled.Entities.Position[0];
            for (int k = 0; k < 10; k++) rolled.Step(InputFrame.Empty);
            Assert.AreEqual(at, rolled.Entities.Position[0], "кувырок в оглушении");
            Assert.AreEqual(0, rolled.Entities.ForcedTicksLeft[0]);
        }

        [Test]
        public void ControlImmunityPreventsChain()
        {
            Assert.AreEqual(45, Simulation.HeroControlImmunityTicks);
            var sim = HeroArena(out _);
            int t0 = sim.Tick;
            Assert.IsTrue(sim.ApplyHeroRoot(30));

            // Действующий контроль не продлевается и не сменяется.
            Assert.IsFalse(sim.ApplyHeroStun(30), "оглушение поверх корней");
            Assert.IsFalse(sim.ApplyHeroRoot(60), "корни поверх корней");
            Assert.IsFalse(sim.HeroStunned);
            Assert.AreEqual(31, sim.HeroRootTicksLeft);
            Assert.AreEqual(31 + 45, sim.HeroControlImmuneTicksLeft);
            Assert.AreEqual(1, Count(sim, SimEventType.HeroControl), "отбитый контроль события не даёт");

            // Корни сошли — ещё 45 тиков иммунитета.
            Until(sim, t0 + 31);
            Assert.IsFalse(sim.HeroRooted);
            while (sim.Tick < t0 + 31 + 45)
            {
                Assert.IsFalse(sim.ApplyHeroStun(30), "оглушение в иммунитете, тик " + sim.Tick);
                Assert.IsFalse(sim.ApplyHeroRoot(30), "корни в иммунитете, тик " + sim.Tick);
                Assert.AreEqual(0, Count(sim, SimEventType.HeroControl));
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.HeroStunned);
                Assert.IsFalse(sim.HeroRooted);
            }
            Assert.AreEqual(0, sim.HeroControlImmuneTicksLeft);
            Assert.IsTrue(sim.ApplyHeroStun(30), "иммунитет кончился");
            Assert.IsTrue(sim.HeroStunned);

            // После оглушения — те же 45 тиков.
            Until(sim, sim.Tick + sim.HeroStunTicksLeft);
            Assert.IsFalse(sim.HeroStunned);
            Assert.AreEqual(45, sim.HeroControlImmuneTicksLeft);
            Assert.IsFalse(sim.ApplyHeroRoot(30));
            Until(sim, sim.Tick + 45);
            Assert.IsTrue(sim.ApplyHeroRoot(30));
        }

        [Test]
        public void SlamOnAHeroInControlImmunity_DamageOnly()
        {
            var sim = Arena();
            int health = sim.Entities.Health[0];
            var a = Start(sim);
            // Героя только что оглушили (разбег Камнекопыта): корни удара ложатся в иммунитет.
            Until(sim, a.ImpactTick - 5);
            Assert.IsTrue(sim.ApplyHeroStun(3));
            Until(sim, a.ImpactTick);
            Assert.IsFalse(sim.HeroStunned, "оглушение сошло до контакта, остался иммунитет");
            Assert.Greater(sim.HeroControlImmuneTicksLeft, 0);
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(EnemyArchetypes.RootSnarerDamage, health - sim.Entities.Health[0], "урон проходит");
            Assert.IsFalse(sim.HeroRooted);
            Assert.AreEqual(0, Count(sim, SimEventType.HeroControl));
            Assert.AreEqual(0, sim.HeroSlowTicksLeft, "вместо корней и замедления нет");
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
            Assert.IsFalse(sim.HeroRooted);
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
            Assert.IsFalse(sim.HeroRooted);
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

            // С пятой арены крупных атак две, но бюджет меток разводит их на 9 тиков.
            var wide = Arena(5, count: 2);
            wide.BigAttackTokenLimit = 2;
            wide.BigMarkBudget = 4;
            wide.Step(InputFrame.Empty);
            Assert.IsTrue(wide.TryGetRootSnarerAction(Snarer, out var one));
            Assert.IsFalse(wide.TryGetRootSnarerAction(second, out _), "вторая крупная метка в тот же тик");
            RootSnarerState two = default;
            while (!wide.TryGetRootSnarerAction(second, out two) && wide.Tick < one.ImpactTick)
                wide.Step(InputFrame.Empty);
            Assert.AreNotEqual(0, two.Serial, "с пятой арены крупных атак две");
            Assert.AreEqual(one.StartTick + Simulation.BigMarkStaggerTicks, two.StartTick);
            Assert.Less(two.StartTick, one.ImpactTick, "второй встал, пока первый ещё держит жетон");
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
        public void RepeatSetupClearsTheActionTheRootsAndTheCooldown()
        {
            var sim = Arena();
            var a = Start(sim);
            Until(sim, a.ImpactTick + 1);
            Assert.IsTrue(sim.HeroRooted);
            Assert.Greater(sim.HeroControlImmuneTicksLeft, 0);
            sim.SetupKindTestArena(EnemyKind.ForestRootSnarer);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            Assert.IsFalse(sim.HeroRooted);
            Assert.AreEqual(0, sim.HeroRootTicksLeft);
            Assert.AreEqual(0, sim.HeroControlImmuneTicksLeft, "иммунитет прошлой расстановки не переходит");
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

        // ---------- «Волна из корней» ----------

        /// <summary>
        /// Союзник Корнехвата: Хранитель (или kind) в точке at, maxHealth
        /// здоровья, из них percent%. Не ходит и не бьёт — здоровье ему
        /// меняет только волна.
        /// </summary>
        private static int Ally(Simulation sim, FixVec2 at, int percent, int maxHealth = 1000,
            EnemyKind kind = EnemyKind.ForestGuardian)
        {
            int id = sim.SpawnEnemy(at, maxHealth, kind);
            sim.Entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            sim.Entities.Health[id] = sim.Entities.MaxHealth[id] * percent / 100;
            return id;
        }

        /// <summary>Шагает, пока Корнехват snarer не начнёт лечение (не дальше тика limit).</summary>
        private static RootSnarerState UntilMend(Simulation sim, int limit, int snarer = Snarer)
        {
            while (sim.Tick < limit)
            {
                sim.Step(InputFrame.Empty);
                if (sim.TryGetRootSnarerAction(snarer, out var a) && a.Action == RootSnarerAction.Mend
                    && a.StartTick == sim.Tick - 1) return a;
            }
            Assert.Fail("лечение не началось до тика " + limit);
            return default;
        }

        private static bool HasEvent(Simulation sim, SimEventType type, EnemyActionKind kind)
        {
            foreach (var e in sim.Events)
                if (e.Type == type && e.ActionVariant == (int)kind) return true;
            return false;
        }

        private static bool HasHeal(Simulation sim)
        {
            foreach (var e in sim.Events) if (e.Type == SimEventType.Heal) return true;
            return false;
        }

        [Test]
        public void MendHealsTenPercent_FiveForElites_CappedAtTheMissingHealth()
        {
            Assert.AreEqual(30, Simulation.RootSnarerMendChannelTicks);
            Assert.AreEqual(20, Simulation.RootSnarerMendRecoveryTicks);
            Assert.AreEqual(300, Simulation.RootSnarerMendCooldownTicks);
            Assert.AreEqual(150, Simulation.RootSnarerMendCancelCooldownTicks);
            Assert.AreEqual(90, Simulation.RootSnarerMendFirstDelayTicks);
            Assert.AreEqual(30, Simulation.RootSnarerMendGapTicks);
            Assert.AreEqual(240, Simulation.RootSnarerMendAllyCooldownTicks);
            Assert.AreEqual(10, Simulation.RootSnarerMendPercent);
            Assert.AreEqual(5, Simulation.RootSnarerMendElitePercent);
            Assert.AreEqual(Fix64.FromInt(5), Simulation.RootSnarerMendRadius);

            // Герой далеко: удара нет, только лечение.
            var sim = Arena(20);
            int hurt = Ally(sim, At(20, 3), 70);          // 700 → +100
            int scratched = Ally(sim, At(22, 2), 96);     // 960 → +40: не выше недостающего
            int elite = Ally(sim, At(18, 2), 60);         // 600 → +50
            sim.MarkElite(elite);
            int wendigo = Ally(sim, At(20, -3), 50, 4000, EnemyKind.ForestWendigo);  // 2000 → +200
            int full = Ally(sim, At(17, -1), 100);        // полному — ничего
            int far = Ally(sim, At(27, 0), 50);           // 7 м — вне волны

            var a = UntilMend(sim, 200);
            Assert.AreEqual(90, a.StartTick, "первое лечение — через 90 тиков после агро");
            Assert.AreEqual(RootSnarerAction.Mend, a.Action);
            Assert.AreEqual(120, a.ImpactTick);
            Assert.AreEqual(120, a.SlamTick, "кадр контакта позы — на волне");
            Assert.AreEqual(140, a.EndTick);
            Assert.IsFalse(a.Slammed);
            Assert.AreEqual(0, a.TelegraphSerial);
            Assert.AreEqual(sim.Entities.Health[Snarer], a.StartHealth);
            Assert.AreEqual(sim.Entities.Position[Snarer], a.Target);
            Assert.IsFalse(sim.RootSnarerHoldsBigToken(Snarer), "лечение крупного жетона не берёт");
            int started = 0;
            foreach (var e in sim.Events)
            {
                Assert.AreNotEqual(SimEventType.TelegraphOpened, e.Type, "у лечения метки на земле нет");
                if (e.Type != SimEventType.EnemyActionStarted) continue;
                started++;
                Assert.AreEqual(Snarer, e.Source);
                Assert.AreEqual((int)EnemyActionKind.SnarerMend, e.ActionVariant);
                Assert.AreEqual(sim.Entities.Position[Snarer], e.Position);
            }
            Assert.AreEqual(1, started);

            while (sim.Tick < a.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(HasHeal(sim), "лечение до волны, тик " + (sim.Tick - 1));
                Assert.AreEqual(FixVec2.Zero, sim.Entities.Velocity[Snarer]);
            }
            sim.Step(InputFrame.Empty);
            var healed = new Dictionary<int, int>();
            int impacts = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type == SimEventType.Heal)
                {
                    Assert.AreEqual(Snarer, e.Source);
                    Assert.AreEqual(sim.Entities.Position[e.Target], e.Position);
                    Assert.IsFalse(healed.ContainsKey(e.Target), "одно лечение на союзника");
                    healed[e.Target] = e.Amount;
                }
                if (e.Type != SimEventType.EnemyActionImpact) continue;
                impacts++;
                Assert.AreEqual(Snarer, e.Source);
                Assert.AreEqual((int)EnemyActionKind.SnarerMend, e.ActionVariant);
                Assert.AreEqual(4, e.Amount, "скольких вылечила");
                Assert.IsTrue(e.Flag);
                Assert.AreEqual(sim.Entities.Position[Snarer], e.Position);
            }
            Assert.AreEqual(1, impacts);
            Assert.AreEqual(4, healed.Count);
            Assert.AreEqual(100, healed[hurt]);
            Assert.AreEqual(40, healed[scratched]);
            Assert.AreEqual(50, healed[elite]);
            Assert.AreEqual(200, healed[wendigo]);
            Assert.AreEqual(800, sim.Entities.Health[hurt]);
            Assert.AreEqual(1000, sim.Entities.Health[scratched]);
            Assert.AreEqual(650, sim.Entities.Health[elite]);
            Assert.AreEqual(2200, sim.Entities.Health[wendigo]);
            Assert.AreEqual(1000, sim.Entities.Health[full]);
            Assert.AreEqual(500, sim.Entities.Health[far]);
            Assert.AreEqual(120, sim.LastMendTick(hurt));
            Assert.AreEqual(-1, sim.LastMendTick(full));
            Assert.AreEqual(-1, sim.LastMendTick(far));

            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var waved));
            Assert.IsTrue(waved.HitResolved);
            Assert.AreEqual(4, waved.Healed);
            Until(sim, a.EndTick + 1);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "стойка лечения — 20 тиков");
        }

        // Повод: один союзник на ≤ 75% или двое на ≤ 90%. 0 — второго нет.
        [TestCase(75, 0, true)]
        [TestCase(76, 0, false)]
        [TestCase(90, 90, true)]
        [TestCase(91, 90, false)]
        [TestCase(90, 100, false)]
        public void MendNeedsOneAllyAtThreeQuartersOrTwoAtNinetyPercent(int first, int second, bool mends)
        {
            var sim = Arena(20);
            Ally(sim, At(20, 3), first);
            if (second > 0) Ally(sim, At(20, -3), second);
            bool started = false;
            while (sim.Tick < 200)
            {
                sim.Step(InputFrame.Empty);
                started |= sim.TryGetRootSnarerAction(Snarer, out _);
            }
            Assert.AreEqual(mends, started);
        }

        [Test]
        public void MendNeverHealsItselfOrAnotherSnarer()
        {
            const int second = 2;
            var sim = Arena(20, count: 2);
            sim.Entities.Health[Snarer] = sim.Entities.MaxHealth[Snarer] / 2;
            sim.Entities.Health[second] = sim.Entities.MaxHealth[second] / 2;
            int own = sim.Entities.Health[Snarer], other = sim.Entities.Health[second];
            while (sim.Tick < 250)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(HasEvent(sim, SimEventType.EnemyActionStarted, EnemyActionKind.SnarerMend),
                    "раненые Корнехваты — не повод, тик " + (sim.Tick - 1));
                Assert.IsFalse(HasHeal(sim));
            }

            // Раненый Хранитель рядом с обоими — лечат только его.
            int guardian = Ally(sim, At(22.5, 1), 60);
            int heals = 0;
            while (sim.Tick < 450)
            {
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Heal) { heals++; Assert.AreEqual(guardian, e.Target); }
            }
            Assert.Greater(heals, 0, "Хранителя должны были лечить");
            Assert.AreEqual(own, sim.Entities.Health[Snarer]);
            Assert.AreEqual(other, sim.Entities.Health[second]);
        }

        [Test]
        public void OneAllyIsHealedAtMostOncePer240Ticks_EvenByTwoSnarers()
        {
            const int second = 2;
            var sim = Arena(20, count: 2);
            int guardian = Ally(sim, At(22.5, 1), 40);
            var ticks = new List<int>();
            var sources = new HashSet<int>();
            while (sim.Tick < 720)
            {
                sim.Step(InputFrame.Empty);
                bool oneMends = sim.TryGetRootSnarerAction(Snarer, out var x) && x.Action == RootSnarerAction.Mend;
                bool twoMends = sim.TryGetRootSnarerAction(second, out var y) && y.Action == RootSnarerAction.Mend;
                Assert.IsFalse(oneMends && twoMends, "лечат двое разом, тик " + (sim.Tick - 1));
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.Heal) continue;
                    Assert.AreEqual(guardian, e.Target);
                    ticks.Add(sim.Tick - 1); sources.Add(e.Source);
                }
            }
            Assert.GreaterOrEqual(ticks.Count, 3);
            Assert.AreEqual(120, ticks[0], "первым лечит младший");
            for (int k = 1; k < ticks.Count; k++)
                Assert.GreaterOrEqual(ticks[k] - ticks[k - 1], Simulation.RootSnarerMendAllyCooldownTicks,
                    "тик " + ticks[k]);
            Assert.IsTrue(sources.Contains(Snarer) && sources.Contains(second), "лечили оба по очереди");
        }

        // Помеха на 15-м тике сбора: 0 — оглушение, 1 — волок.
        [TestCase(0)]
        [TestCase(1)]
        public void StunOrDragInTheChannelCancels_NoHeal_NextMend150TicksLater(int reason)
        {
            var sim = Arena(20);
            int guardian = Ally(sim, At(20, 3), 70);
            var a = UntilMend(sim, 200);
            int cut = a.StartTick + 15;
            Until(sim, cut);
            if (reason == 0) sim.Statuses.ApplyStun(Snarer, sim.Tick + 3);
            else Assert.IsTrue(ForcedMotion.Begin(sim.Entities, Snarer, sim.Entities.Position[Snarer], 3,
                ForcedMotionKind.Dragged));
            sim.Step(InputFrame.Empty);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));
            int cancelled = 0;
            foreach (var e in sim.Events)
            {
                if (e.Type != SimEventType.EnemyActionCancelled) continue;
                cancelled++;
                Assert.AreEqual(Snarer, e.Source);
                Assert.AreEqual((int)EnemyActionKind.SnarerMend, e.ActionVariant);
            }
            Assert.AreEqual(1, cancelled, "отмена — одно событие");
            Assert.AreEqual(cut + Simulation.RootSnarerMendCancelCooldownTicks, sim.RootSnarerNextMendTick(Snarer));

            while (sim.Tick < cut + 150)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "лечение раньше срока, тик " + (sim.Tick - 1));
                Assert.IsFalse(HasHeal(sim), "волна после отмены");
                Assert.IsFalse(HasEvent(sim, SimEventType.EnemyActionImpact, EnemyActionKind.SnarerMend));
            }
            Assert.AreEqual(700, sim.Entities.Health[guardian]);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var again));
            Assert.AreEqual(RootSnarerAction.Mend, again.Action);
            Assert.AreEqual(cut + 150, again.StartTick);
        }

        // Здоровье Корнехвата 650: сбивает урон от 97,5 — на 5-м тике сбора и, если есть, ещё на 20-м.
        [TestCase(97, 0, false)]
        [TestCase(98, 0, true)]
        [TestCase(50, 48, true)]
        [TestCase(50, 47, false)]
        public void DamageOfFifteenPercentSinceThePlantBreaksTheMend(int first, int second, bool breaks)
        {
            var sim = Arena(20);
            Assert.AreEqual(650, sim.Entities.MaxHealth[Snarer]);
            Assert.AreEqual(15, Simulation.RootSnarerMendBreakPercent);
            int guardian = Ally(sim, At(20, 3), 70);
            var a = UntilMend(sim, 200);
            int cutAt = -1;
            while (sim.Tick <= a.ImpactTick)
            {
                if (sim.Tick == a.StartTick + 5) sim.Entities.Health[Snarer] -= first;
                if (sim.Tick == a.StartTick + 20) sim.Entities.Health[Snarer] -= second;
                sim.Step(InputFrame.Empty);
                if (HasEvent(sim, SimEventType.EnemyActionCancelled, EnemyActionKind.SnarerMend)) cutAt = sim.Tick - 1;
            }
            Assert.AreEqual(breaks, cutAt >= 0);
            Assert.AreEqual(breaks ? 700 : 800, sim.Entities.Health[guardian]);
            if (!breaks) return;
            Assert.AreEqual(a.StartTick + (first >= 98 ? 5 : 20), cutAt);
            Assert.AreEqual(cutAt + Simulation.RootSnarerMendCancelCooldownTicks, sim.RootSnarerNextMendTick(Snarer));
        }

        [Test]
        public void MendWinsOverTheSlam_TheSlamWaitsThirtyTicksAfterIt()
        {
            // Герой вне дальности, союзник цел: до 90-го тика ни удара, ни лечения.
            var sim = Arena(10);
            int guardian = Ally(sim, At(10, 3), 100);
            Until(sim, 90);
            Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _));

            // Разом: герой в 5 м — удар готов, и союзник на 70% — лечение готово.
            sim.Entities.Position[0] = At(5, 0);
            sim.Entities.Health[guardian] = 700;
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var mend));
            Assert.AreEqual(RootSnarerAction.Mend, mend.Action, "лечение главнее удара");
            Assert.AreEqual(90, mend.StartTick);
            Assert.IsFalse(HasEvent(sim, SimEventType.EnemyActionStarted, EnemyActionKind.SnarerSlam));

            while (sim.Tick < mend.EndTick + Simulation.RootSnarerMendGapTicks)
            {
                sim.Step(InputFrame.Empty);
                if (sim.TryGetRootSnarerAction(Snarer, out var s))
                    Assert.AreEqual(RootSnarerAction.Mend, s.Action, "удар раньше тишины, тик " + (sim.Tick - 1));
            }
            Assert.AreEqual(800, sim.Entities.Health[guardian]);
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var slam));
            Assert.AreEqual(RootSnarerAction.Slam, slam.Action);
            Assert.AreEqual(mend.EndTick + 30, slam.StartTick);
            Assert.IsTrue(sim.RootSnarerHoldsBigToken(Snarer));
        }

        [Test]
        public void SlamWorksAsBeforeWhenNobodyIsHurt()
        {
            var sim = Arena(5);
            Ally(sim, At(5, 3), 100);
            var starts = new List<int>();
            while (sim.Tick < 400)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(HasEvent(sim, SimEventType.EnemyActionStarted, EnemyActionKind.SnarerMend));
                Assert.IsFalse(HasHeal(sim));
                if (!HasEvent(sim, SimEventType.EnemyActionStarted, EnemyActionKind.SnarerSlam)) continue;
                Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var a));
                Assert.AreEqual(RootSnarerAction.Slam, a.Action);
                starts.Add(a.StartTick);
            }
            Assert.That(starts, Is.EqualTo(new[] { 0, 150, 300 }));
        }

        [Test]
        public void MendIgnoresTheBigTokenAndDoesNotHoldIt()
        {
            // Вой берёт единственный крупный жетон в тот же тик — лечение всё равно начинается.
            var sim = Arena(10);
            Assert.AreEqual(1, sim.BigAttackTokenLimit);
            int wendigo = HowlingWendigo(sim, At(0, 4));
            sim.SetWendigoCooldowns(wendigo, 100000, 90);
            Ally(sim, At(10, 3), 70);
            Until(sim, 91);
            Assert.IsTrue(sim.TryGetWendigoAction(wendigo, out var howl));
            Assert.AreEqual(WendigoAction.Howl, howl.Kind);
            Assert.AreEqual(90, howl.StartTick);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var mend), "лечение не ждёт крупного жетона");
            Assert.AreEqual(RootSnarerAction.Mend, mend.Action);
            Assert.AreEqual(90, mend.StartTick);

            // Лечение идёт — вой встаёт поверх: жетон лечением не занят.
            var other = Arena(10);
            int second = HowlingWendigo(other, At(0, 4));
            other.SetWendigoCooldowns(second, 100000, 95);
            Ally(other, At(10, 3), 70);
            Until(other, 96);
            Assert.IsTrue(other.TryGetRootSnarerAction(Snarer, out var running));
            Assert.AreEqual(RootSnarerAction.Mend, running.Action);
            Assert.IsTrue(other.TryGetWendigoAction(second, out var late));
            Assert.AreEqual(95, late.StartTick);
        }

        [Test]
        public void WalksToAFarHurtAlly_MendsFromTwoAndAHalfMetres()
        {
            var sim = Arena(20, walks: true);
            Until(sim, 89);
            var from = sim.Entities.Position[Snarer];
            int guardian = Ally(sim, from + At(0, 4.6), 70);
            var a = UntilMend(sim, 200);
            Assert.Greater(a.StartTick, 90, "самый раненый дальше 4 м — сначала идёт");
            Assert.Less(a.StartTick, 90 + Simulation.RootSnarerMendWalkTicks, "дошёл раньше срока");
            var at = sim.Entities.Position[Snarer];
            Assert.LessOrEqual((sim.Entities.Position[guardian] - at).Length.ToDouble(), 2.5 + 1e-3);
            Assert.Greater(at.Y.ToDouble() - from.Y.ToDouble(), 1.5, "шёл к союзнику, а не к герою");
            Until(sim, a.ImpactTick + 1);
            Assert.AreEqual(800, sim.Entities.Health[guardian]);
        }

        [Test]
        public void GivesUpTheWalkAfterSixtyTicks_MendsWhereItStands_ThenSlams()
        {
            // Стоит на месте (MoveSpeed 0): до союзника в 4,5 м ему не дойти.
            var sim = Arena(5);
            Ally(sim, At(5, 4.5), 70);
            var slam = Start(sim);
            Assert.AreEqual(RootSnarerAction.Slam, slam.Action);
            var mend = UntilMend(sim, 400);
            Assert.AreEqual(slam.EndTick + Simulation.RootSnarerMendGapTicks + Simulation.RootSnarerMendWalkTicks,
                mend.StartTick, "тишина после удара, потом 60 тиков похода");
            // Перезарядка удара вышла на 150-м, но поход и лечение главнее.
            while (sim.Tick < mend.EndTick + Simulation.RootSnarerMendGapTicks)
            {
                sim.Step(InputFrame.Empty);
                if (sim.TryGetRootSnarerAction(Snarer, out var s)) Assert.AreEqual(RootSnarerAction.Mend, s.Action);
            }
            sim.Step(InputFrame.Empty);
            Assert.IsTrue(sim.TryGetRootSnarerAction(Snarer, out var next));
            Assert.AreEqual(RootSnarerAction.Slam, next.Action);
            Assert.AreEqual(mend.EndTick + 30, next.StartTick);
        }

        [Test]
        public void AWalkIsDroppedWhenTheAllyStopsNeedingIt()
        {
            var sim = Arena(20);
            int guardian = Ally(sim, At(20, 4.5), 70);
            // На 90-м пошёл (стоя на месте); на 100-м союзник цел — поход брошен.
            Until(sim, 100);
            sim.Entities.Health[guardian] = 1000;
            while (sim.Tick < 200)
            {
                sim.Step(InputFrame.Empty);
                Assert.IsFalse(sim.TryGetRootSnarerAction(Snarer, out _), "тик " + (sim.Tick - 1));
            }
            // Снова ранен — поход заново, полные 60 тиков, а не сразу.
            sim.Entities.Health[guardian] = 700;
            var mend = UntilMend(sim, 400);
            Assert.AreEqual(200 + Simulation.RootSnarerMendWalkTicks, mend.StartTick);
        }

        [Test]
        public void RepeatSetupClearsTheMendMemory()
        {
            var sim = Arena(20);
            int guardian = Ally(sim, At(20, 3), 70);
            var a = UntilMend(sim, 200);
            Until(sim, a.ImpactTick + 1);
            Assert.AreEqual(a.ImpactTick, sim.LastMendTick(guardian));
            sim.SetupKindTestArena(EnemyKind.ForestRootSnarer, distance: Fix64.FromInt(20));
            Assert.AreEqual(-1, sim.LastMendTick(guardian));
            Assert.AreEqual(0, sim.RootSnarerNextMendTick(Snarer));
            int tick = sim.Tick;
            sim.Step(InputFrame.Empty);
            Assert.AreEqual(tick + Simulation.RootSnarerMendFirstDelayTicks, sim.RootSnarerNextMendTick(Snarer));
        }

        [Test]
        public void MendRunsAreDeterministic()
        {
            var a = Arena(8, count: 2, walks: true);
            var b = Arena(8, count: 2, walks: true);
            var sims = new[] { a, b };
            var guardians = new int[3];
            var spots = new[] { At(6, -2), At(6, 3.5), At(4, 5) };
            foreach (var sim in sims)
            {
                sim.BigAttackTokenLimit = 2;
                // Хранители ходят и бьют: волна ловит их в толчее.
                for (int n = 0; n < 3; n++)
                {
                    guardians[n] = sim.SpawnEnemy(spots[n], 1000, EnemyKind.ForestGuardian);
                    sim.Entities.Health[guardians[n]] = 400 + 200 * n;
                    sim.Entities.Aggro[guardians[n]] = true;
                }
            }
            int heals = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                double angle = tick * .02;
                var hero = At(4 * System.Math.Cos(angle), 4 * System.Math.Sin(angle));
                foreach (var sim in sims)
                {
                    sim.Entities.Position[0] = hero;
                    if (tick == 105) sim.Statuses.ApplyStun(Snarer, 110);
                    if (tick % 100 == 50) sim.ApplyAbilityDamage(0, guardians[tick / 100 % 3], 150, -1, DamageType.Physical);
                    if (tick == 400) sim.ApplyAbilityDamage(0, 2, 120, -1, DamageType.Physical);
                    sim.Step(InputFrame.Empty);
                }
                foreach (var e in a.Events) if (e.Type == SimEventType.Heal) heals++;
                Assert.AreEqual(a.StateHash(), b.StateHash(), "тик " + tick);
            }
            Assert.Greater(heals, 1, "Корнехваты должны были лечить");
        }
    }
}
