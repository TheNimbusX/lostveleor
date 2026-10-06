using System;
using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Обычная атака Пелага — серия сабли (Simulation.SabreCombo, 01.10).
    /// Таблица сроков и правила — razlom/Docs/PelagBasicCombo.md; здесь они
    /// проверяются по тикам. Мишени неподвижны и не бьют, герой неуязвим:
    /// меряется только серия.
    /// </summary>
    public sealed class SabreComboTests
    {
        const int Health = 1000000;
        static FixVec2 At(double x, double y) => new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y));

        static Simulation Arena(int damage = 54, ulong seed = 4242)
        {
            var sim = new Simulation(seed, 64);
            sim.SetupTestArena(0);
            var e = sim.Entities;
            e.Position[0] = FixVec2.Zero;
            e.Facing[0] = At(1, 0);
            e.PushWeight[0] = Fix64.Zero;
            e.Stats[0].SetBase(StatType.Damage, Fix64.FromInt(damage));
            e.Stats[0].SetBase(StatType.CritChance, Fix64.Zero);
            e.Stats[0].SetBase(StatType.LavidiumRegen, Fix64.Zero);
            sim.RefreshPlayerStats(false);
            sim.PlayerInvulnerable = true;
            return sim;
        }

        static int Dummy(Simulation sim, FixVec2 at, double radius = .5, bool pushable = false,
            EnemyKind kind = EnemyKind.None)
        {
            int id = sim.Entities.Spawn(at, Health, Faction.Orvill);
            var sheet = sim.Entities.Stats[id];
            sheet.SetBase(StatType.MaxHealth, Fix64.FromInt(Health));
            sheet.SetBase(StatType.MoveSpeed, Fix64.Zero);
            sheet.SetBase(StatType.AttackSpeed, Fix64.Zero);
            sim.Entities.RefreshStats(id);
            sim.Entities.Health[id] = Health;
            sim.Entities.NextAttackTick[id] = int.MaxValue;
            sim.Entities.PushWeight[id] = pushable ? Fix64.One : Fix64.Zero;
            sim.Entities.BodyRadius[id] = Fix64.FromDouble(radius);
            sim.Entities.Kind[id] = kind;
            sim.Grid.Rebuild(sim.Entities);
            return id;
        }

        static InputFrame Hold(FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.Aim = aim;
            input.Flags = (byte)InputFlags.Attack;
            return input;
        }

        static InputFrame Tap(FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.Aim = aim;
            input.Flags = (byte)InputFlags.AttackPressed;
            return input;
        }

        /// <summary>Кадр, в который ЛКМ нажали и держат: начало удержания.</summary>
        static InputFrame PressHold(FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.Aim = aim;
            input.Flags = (byte)(InputFlags.Attack | InputFlags.AttackPressed);
            return input;
        }

        static InputFrame Cast(int slot, FixVec2 aim)
        {
            var input = InputFrame.Empty;
            input.AbilityMask = (byte)(1 << slot);
            input.Aim = aim;
            return input;
        }

        struct Record { public int Tick, Hit, Amount, Target; }

        /// <summary>Шаг с записью стартов ударов (Attack героя) и урона героя.</summary>
        static void Step(Simulation sim, in InputFrame input, List<Record> starts, List<Record> damage)
        {
            int tick = sim.Tick;
            sim.Step(in input);
            foreach (var e in sim.Events)
            {
                if (e.Source != Simulation.PlayerId) continue;
                if (e.Type == SimEventType.Attack) starts?.Add(new Record { Tick = tick, Hit = e.Amount });
                if (e.Type == SimEventType.Damage && e.DamageOrigin == DamageOrigin.BasicAttack)
                    damage?.Add(new Record { Tick = tick, Hit = e.ActionVariant, Amount = e.Amount, Target = e.Target });
            }
        }

        static int Lost(Simulation sim, int id) => Health - sim.Entities.Health[id];

        /// <summary>Шаги одним вводом, пока Tick не дойдёт до tick.</summary>
        static void StepTo(Simulation sim, int tick, InputFrame input)
        {
            while (sim.Tick < tick) sim.Step(in input);
        }

        /// <summary>Ставит мишень относительно героя: выпад добивающего двигает героя.</summary>
        static void Place(Simulation sim, int id, double dx, double dy)
            => sim.Entities.Position[id] = sim.Entities.Position[Simulation.PlayerId] + At(dx, dy);

        static void AssertDirection(double x, double y, FixVec2 actual, string message)
        {
            Assert.AreEqual(x, actual.X.ToDouble(), 1e-3, message);
            Assert.AreEqual(y, actual.Y.ToDouble(), 1e-3, message);
        }

        // ---- сроки ----

        [Test]
        public void HeldAttack_RunsTheTickTable_45_45_90()
        {
            var sim = Arena();
            int dummy = Dummy(sim, At(1.6, 0));
            var starts = new List<Record>();
            var damage = new List<Record>();
            for (int t = 0; t < 40; t++) Step(sim, Hold(At(3, 0)), starts, damage);

            CollectionAssert.AreEqual(new[] { 0, 8, 16, 30, 38 }, starts.ConvertAll(r => r.Tick), "старты ударов");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1 }, starts.ConvertAll(r => r.Hit), "места в серии");
            CollectionAssert.AreEqual(new[] { 4, 12, 23, 34 }, damage.ConvertAll(r => r.Tick), "контакты");
            CollectionAssert.AreEqual(new[] { 45, 45, 90, 45 }, damage.ConvertAll(r => r.Amount), "урон от 54");
            Assert.IsTrue(damage.TrueForAll(r => r.Target == dummy));
            Assert.AreEqual(1f, (30f / Simulation.TicksPerSecond), 1e-6, "вся серия — секунда");
        }

        [Test]
        public void SwingState_FreezesTimingAndDirection()
        {
            var sim = Arena();
            sim.Step(Tap(At(0, 4)));
            var swing = sim.SabreSwing;
            Assert.AreEqual(1, swing.Serial);
            Assert.AreEqual(0, swing.Hit);
            Assert.AreEqual(0, swing.StartTick);
            Assert.AreEqual(4, swing.ContactTick);
            Assert.AreEqual(8, swing.EndTick);
            Assert.AreEqual(-1, swing.LungeStartTick);
            Assert.AreEqual(At(0, 1), swing.Direction);
            Assert.AreEqual(At(0, 1), sim.Entities.Facing[0], "корпус встаёт по удару сразу");
            // Курсор уехал — направление удара прежнее.
            for (int t = 0; t < 4; t++) sim.Step(Hold(At(-5, 0)));
            Assert.AreEqual(At(0, 1), sim.SabreSwing.Direction);
            Assert.IsTrue(sim.SabreSwing.ContactDone);
        }

        [Test]
        public void AttackSpeed_ScalesEveryPhase_WithTwoTickFloor()
        {
            var sim = Arena();
            sim.Entities.Stats[0].Add(StatModifier.Increased(StatType.AttackSpeed, Fix64.One, ModifierSource.Equipment, 77));
            sim.RefreshPlayerStats(false);
            var starts = new List<Record>();
            var damage = new List<Record>();
            Dummy(sim, At(1.6, 0));
            for (int t = 0; t < 20; t++) Step(sim, Hold(At(3, 0)), starts, damage);
            // ×2: удары 1–2 — 2/4 тика, добивающий — 4 (3,5 к ближайшему) и 4.
            CollectionAssert.AreEqual(new[] { 0, 4, 8, 16 }, starts.ConvertAll(r => r.Tick));
            CollectionAssert.AreEqual(new[] { 2, 6, 12, 18 }, damage.ConvertAll(r => r.Tick));
        }

        // ---- сектор ----

        [Test]
        public void Sector_HitsEveryBodyTouchingIt_Once()
        {
            var sim = Arena();
            int front = Dummy(sim, At(2, 0));
            double a = 50 * Math.PI / 180;
            int edge = Dummy(sim, At(1.5 * Math.Cos(a), 1.5 * Math.Sin(a)), .1);
            double b = 62 * Math.PI / 180;
            int wide = Dummy(sim, At(1.5 * Math.Cos(b), -1.5 * Math.Sin(b)), .1);
            int behind = Dummy(sim, At(-1.5, 0));
            int far = Dummy(sim, At(3.2, .8), .5);
            int bigBody = Dummy(sim, At(0, 3.3), .85);   // за сектором: смотрит вбок
            int bigFront = Dummy(sim, At(3.3, 0), .85);  // край тела на 2,45 м — в секторе

            var damage = new List<Record>();
            for (int t = 0; t < 5; t++) Step(sim, Hold(At(4, 0)), null, damage);
            var hit = new HashSet<int>(damage.ConvertAll(r => r.Target));
            Assert.AreEqual(damage.Count, hit.Count, "каждая цель — один раз");
            Assert.IsTrue(hit.Contains(front), "впереди");
            Assert.IsTrue(hit.Contains(edge), "50° — внутри сектора 110°");
            Assert.IsFalse(hit.Contains(wide), "62° — снаружи");
            Assert.IsFalse(hit.Contains(behind), "за спиной");
            Assert.IsFalse(hit.Contains(far), "дальше досягаемости");
            Assert.IsFalse(hit.Contains(bigBody), "сбоку");
            Assert.IsTrue(hit.Contains(bigFront), "тело касается края сектора");
            Assert.AreEqual(3, sim.SabreSwing.Hits);
        }

        [Test]
        public void ContactEvent_ComesOnMissToo_WithFinisherFlag()
        {
            var sim = Arena();
            var contacts = new List<SimEvent>();
            for (int t = 0; t < 24; t++)
            {
                sim.Step(Hold(At(4, 0)));
                foreach (var e in sim.Events) if (e.Type == SimEventType.SabreContact) contacts.Add(e);
            }
            Assert.AreEqual(3, contacts.Count);
            Assert.AreEqual(0, contacts[0].Amount, "промах — ноль целей");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, contacts.ConvertAll(e => e.ActionVariant));
            CollectionAssert.AreEqual(new[] { false, false, true }, contacts.ConvertAll(e => e.Flag));
        }

        // ---- ввод ----

        [Test]
        public void OneClick_IsOneHit_AndAPressDuringTheSwingStartsTheNextOnItsEnd()
        {
            var sim = Arena();
            var starts = new List<Record>();
            Step(sim, Tap(At(3, 0)), starts, null);                 // t0
            Step(sim, InputFrame.Empty, starts, null);               // t1
            Step(sim, Tap(At(0, 3)), starts, null);                  // t2 — во время замаха
            for (int t = 3; t < 30; t++) Step(sim, InputFrame.Empty, starts, null);
            CollectionAssert.AreEqual(new[] { 0, 8 }, starts.ConvertAll(r => r.Tick));
            CollectionAssert.AreEqual(new[] { 0, 1 }, starts.ConvertAll(r => r.Hit));
            Assert.AreEqual(At(0, 1), sim.SabreSwing.Direction, "запомненный прицел нажатия");
        }

        [Test]
        public void PressBlockedByAnAbility_LivesSixTicks()
        {
            foreach (int lead in new[] { 3, 8 })
            {
                var sim = Arena();
                sim.Entities.Lavidium[0] = Fix64.FromInt(100);
                sim.SetAbility(0, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
                sim.Step(Cast(0, At(3, 0)));
                int contact = sim.CleaveContactTick;
                Assert.Greater(contact, lead + 1);
                while (sim.Tick < contact - lead) sim.Step(InputFrame.Empty);
                var starts = new List<Record>();
                Step(sim, Tap(At(3, 0)), starts, null);
                for (int t = 0; t < 20; t++) Step(sim, InputFrame.Empty, starts, null);
                if (lead == 3)
                    CollectionAssert.AreEqual(new[] { contact + 1 }, starts.ConvertAll(r => r.Tick),
                        "нажатие дождалось контакта способности");
                else
                    Assert.IsEmpty(starts, "нажатие за 8 тиков до контакта сгорело");
            }
        }

        [Test]
        public void Series_ContinuesWithinTenTicksAfterTheEnd_ElseStartsOver()
        {
            foreach (int gap in new[] { 10, 11 })
            {
                var sim = Arena();
                var starts = new List<Record>();
                Step(sim, Tap(At(3, 0)), starts, null);
                while (sim.Tick < 8 + gap) Step(sim, InputFrame.Empty, starts, null);
                Step(sim, Tap(At(3, 0)), starts, null);
                Assert.AreEqual(2, starts.Count);
                Assert.AreEqual(gap == 10 ? 1 : 0, starts[1].Hit, "пауза " + gap);
            }
        }

        // ---- зажатая ЛКМ цепляется к ближайшему (владелец 02.10) ----

        [Test]
        public void Click_AimsAtTheCursor_EvenWithAnEnemyElsewhere()
        {
            var sim = Arena();
            Dummy(sim, At(0, 2));
            sim.Step(Tap(At(3, 0)));
            AssertDirection(1, 0, sim.SabreSwing.Direction, "клик — в курсор, не к врагу сбоку");
            Assert.AreEqual(-1, sim.SabreStickTarget);

            sim = Arena();
            Dummy(sim, At(0, 2));
            sim.Step(PressHold(At(3, 0)));
            AssertDirection(1, 0, sim.SabreSwing.Direction, "первый удар удержания — тоже в курсор");
            Assert.AreEqual(-1, sim.SabreStickTarget);
        }

        [Test]
        public void HeldRepeat_TurnsToTheNearestEnemy_OffCursor()
        {
            var sim = Arena();
            Dummy(sim, At(-3.5, 0));
            int near = Dummy(sim, At(0, 2));
            sim.Step(PressHold(At(3, 0)));                           // t0 — в курсор
            StepTo(sim, 8, Hold(At(3, 0)));
            sim.Step(Hold(At(3, 0)));                                // t8 — удар удержания
            Assert.AreEqual(8, sim.SabreSwing.StartTick);
            Assert.AreEqual(near, sim.SabreStickTarget, "ближайший из двух");
            AssertDirection(0, 1, sim.SabreSwing.Direction, "к врагу, а не в курсор (3; 0)");
            AssertDirection(0, 1, sim.Entities.Facing[0], "корпус — по удару");
            StepTo(sim, 13, Hold(At(3, 0)));
            Assert.AreEqual(45, Lost(sim, near), "второй удар лёг на врага сбоку");
        }

        [Test]
        public void Lock_HoldsAgainstASlightlyCloserEnemy_SwitchesWhenOneIsMuchCloser()
        {
            var sim = Arena();
            int a = Dummy(sim, At(0, 2));
            int b = Dummy(sim, At(0, -2.5));
            sim.Step(PressHold(At(3, 0)));
            StepTo(sim, 9, Hold(At(3, 0)));                          // t8 — захват
            Assert.AreEqual(a, sim.SabreStickTarget);

            StepTo(sim, 16, Hold(At(3, 0)));
            Place(sim, a, 0, 2);
            Place(sim, b, 0, -1.5);                                  // ближе на 0,5
            sim.Step(Hold(At(3, 0)));                                // t16 — добивающий
            Assert.AreEqual(16, sim.SabreSwing.StartTick);
            Assert.AreEqual(a, sim.SabreStickTarget, "на 0,5 м ближе — захват держится");
            AssertDirection(0, 1, sim.SabreSwing.Direction, "добивающий — по захвату");

            StepTo(sim, 30, Hold(At(3, 0)));
            Place(sim, a, 0, 2);
            Place(sim, b, 0, -1.2);                                  // ближе на 0,8
            sim.Step(Hold(At(3, 0)));                                // t30
            Assert.AreEqual(30, sim.SabreSwing.StartTick);
            Assert.AreEqual(b, sim.SabreStickTarget, "на 0,8 м ближе — перехват");
            AssertDirection(0, -1, sim.SabreSwing.Direction, "удар — к новому захвату");
        }

        [Test]
        public void Lock_MovesOnWhenTheLockedEnemyDies()
        {
            var sim = Arena();
            int a = Dummy(sim, At(0, 2));
            int b = Dummy(sim, At(0, -3));
            sim.Entities.Health[a] = 10;
            sim.Step(PressHold(At(3, 0)));
            StepTo(sim, 9, Hold(At(3, 0)));
            Assert.AreEqual(a, sim.SabreStickTarget);
            StepTo(sim, 16, Hold(At(3, 0)));                         // контакт t12 добил его
            Assert.IsFalse(sim.Entities.Alive[a]);
            sim.Step(Hold(At(3, 0)));                                // t16
            Assert.AreEqual(16, sim.SabreSwing.StartTick);
            Assert.AreEqual(b, sim.SabreStickTarget, "захват перешёл на живого");
            AssertDirection(0, -1, sim.SabreSwing.Direction, "добивающий — к новому захвату");
        }

        [Test]
        public void NoEnemyInRange_AimsAtTheCursor_AndTheLockLetsGoPastTheSlack()
        {
            // Дальше 5 м — новый захват не берётся: курсор, как без врагов.
            var sim = Arena();
            Dummy(sim, At(0, 5.3));
            sim.Step(PressHold(At(3, 0)));
            StepTo(sim, 9, Hold(At(3, 0)));
            Assert.AreEqual(8, sim.SabreSwing.StartTick);
            Assert.AreEqual(-1, sim.SabreStickTarget, "5,3 м — дальше радиуса захвата");
            AssertDirection(1, 0, sim.SabreSwing.Direction, "никого рядом — курсор");

            // Взятый захват держится до 6 м и отпускается дальше.
            sim = Arena();
            int a = Dummy(sim, At(0, 2));
            sim.Step(PressHold(At(3, 0)));
            StepTo(sim, 9, Hold(At(3, 0)));
            Assert.AreEqual(a, sim.SabreStickTarget);
            StepTo(sim, 16, Hold(At(3, 0)));
            Place(sim, a, 0, 5.8);
            sim.Step(Hold(At(3, 0)));                                // t16
            Assert.AreEqual(a, sim.SabreStickTarget, "5,8 м — в запасе 1 м");
            AssertDirection(0, 1, sim.SabreSwing.Direction, "по захвату");
            StepTo(sim, 30, Hold(At(3, 0)));
            Place(sim, a, 0, 6.2);
            // Выпад добивающего сдвинул героя: курсор — снова в 3 м справа от него.
            sim.Step(Hold(sim.Entities.Position[Simulation.PlayerId] + At(3, 0)));   // t30
            Assert.AreEqual(30, sim.SabreSwing.StartTick);
            Assert.AreEqual(-1, sim.SabreStickTarget, "6,2 м — захват отпущен");
            AssertDirection(1, 0, sim.SabreSwing.Direction, "снова курсор");
        }

        [Test]
        public void Release_And_Stun_LetTheLockGo()
        {
            var sim = Arena();
            int a = Dummy(sim, At(0, 2));
            sim.Step(PressHold(At(3, 0)));
            StepTo(sim, 9, Hold(At(3, 0)));
            Assert.AreEqual(a, sim.SabreStickTarget);
            sim.Step(InputFrame.Empty);                              // t9 — отпустил
            Assert.AreEqual(-1, sim.SabreStickTarget, "отпущенная ЛКМ отпускает захват");

            // Новое удержание: первый удар — снова в курсор, следующий — к врагу.
            StepTo(sim, 16, InputFrame.Empty);
            sim.Step(PressHold(At(3, 0)));                           // t16
            Assert.AreEqual(16, sim.SabreSwing.StartTick);
            AssertDirection(1, 0, sim.SabreSwing.Direction, "новое нажатие — в курсор");
            Assert.AreEqual(-1, sim.SabreStickTarget);
            StepTo(sim, 30, Hold(At(3, 0)));
            Place(sim, a, 0, 2);
            sim.Step(Hold(At(3, 0)));                                // t30
            Assert.AreEqual(a, sim.SabreStickTarget, "удержание снова цепляется");
            AssertDirection(0, 1, sim.SabreSwing.Direction, "к врагу");

            Assert.IsTrue(sim.ApplyHeroStun(5));
            sim.Step(Hold(At(3, 0)));
            Assert.AreEqual(-1, sim.SabreStickTarget, "оглушение отпускает захват");
        }

        // ---- отмены ----

        [Test]
        public void Dash_AfterContact_KeepsTheHitAndTheSeries()
        {
            var sim = Arena();
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            int dummy = Dummy(sim, At(1.6, 0));
            while (sim.Tick < 5) sim.Step(Hold(At(3, 0)));
            Assert.AreEqual(45, Lost(sim, dummy));
            sim.Step(Cast(4, At(0, -3)));
            Assert.IsTrue(sim.SabreSwing.Interrupted, "кувырок снял восстановление");
            Assert.Greater(sim.Entities.ForcedTicksLeft[0], 0);
            while (sim.Entities.ForcedTicksLeft[0] > 0) sim.Step(InputFrame.Empty);
            var starts = new List<Record>();
            Step(sim, Tap(At(3, 0)), starts, null);
            Assert.AreEqual(1, starts.Count);
            Assert.AreEqual(1, starts[0].Hit, "после кувырка серия продолжается вторым ударом");
            Assert.AreEqual(45, Lost(sim, dummy), "кувырок не добавил урона");
        }

        [Test]
        public void Dash_BeforeContact_CancelsTheHitAndRepeatsIt()
        {
            var sim = Arena();
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            int dummy = Dummy(sim, At(1.6, 0));
            sim.Step(Hold(At(3, 0)));
            sim.Step(Hold(At(3, 0)));
            sim.Step(Cast(4, At(0, -3)));
            while (sim.Entities.ForcedTicksLeft[0] > 0) sim.Step(InputFrame.Empty);
            Assert.AreEqual(0, Lost(sim, dummy), "снятый до контакта удар не бьёт");
            var starts = new List<Record>();
            Step(sim, Tap(At(3, 0)), starts, null);
            Assert.AreEqual(0, starts[0].Hit, "номер в серии не сдвинулся");
        }

        [Test]
        public void AbilityPressedDuringWindup_CastsAfterContact()
        {
            var sim = Arena();
            sim.Entities.Lavidium[0] = Fix64.FromInt(100);
            sim.SetAbility(0, AbilityDefinition.Cleave(), Array.Empty<AbilityNode>(), 0);
            int dummy = Dummy(sim, At(1.6, 0));
            sim.Step(Tap(At(3, 0)));               // t0 — удар 1
            sim.Step(Cast(0, At(3, 0)));           // t1 — Рассекающий ждёт контакта
            int castTick = -1;
            while (sim.Tick < 5) sim.Step(InputFrame.Empty);
            Assert.AreEqual(45, Lost(sim, dummy), "удар лёг до способности");
            Assert.IsFalse(sim.SabreSwing.Interrupted);
            for (int t = 5; t < 12 && castTick < 0; t++)
            {
                int tick = sim.Tick;
                sim.Step(InputFrame.Empty);
                foreach (var e in sim.Events) if (e.Type == SimEventType.AbilityCast) castTick = tick;
            }
            Assert.AreEqual(5, castTick, "способность — в тик после контакта удара");
            Assert.IsTrue(sim.SabreSwing.Interrupted, "способность сняла хвост удара");
        }

        // ---- выпад, корни, толчок ----

        [Test]
        public void Finisher_LungesSixTenthsInOpenGround()
        {
            var sim = Arena();
            for (int t = 0; t < 17; t++) sim.Step(Hold(At(5, 0)));
            Assert.AreEqual(2, sim.SabreSwing.Hit);
            double x0 = sim.Entities.Position[0].X.ToDouble();
            Assert.AreEqual(19, sim.SabreSwing.LungeStartTick, "выпад — t3…t6 добивающего");
            while (sim.Tick <= sim.SabreSwing.ContactTick) sim.Step(Hold(At(5, 0)));
            double moved = sim.Entities.Position[0].X.ToDouble() - x0;
            Assert.AreEqual(.6, moved, .002, "выпад 0,6 м");
        }

        [Test]
        public void Finisher_LungeStopsAtTheBody()
        {
            var sim = Arena();
            int dummy = Dummy(sim, At(1.2, 0));
            for (int t = 0; t < 24; t++) sim.Step(Hold(At(5, 0)));
            double gap = (sim.Entities.Position[dummy] - sim.Entities.Position[0]).Length.ToDouble();
            Assert.GreaterOrEqual(gap, .95 - .002, "тела касаются, но не входят друг в друга");
            Assert.AreEqual(.25, sim.Entities.Position[0].X.ToDouble(), .01);
        }

        [Test]
        public void RootedHero_StillSwingsButTheFinisherDoesNotMove()
        {
            var sim = Arena();
            int dummy = Dummy(sim, At(1.6, 0));
            for (int t = 0; t < 16; t++) sim.Step(Hold(At(5, 0)));
            Assert.IsTrue(sim.ApplyHeroRoot(30));
            FixVec2 before = sim.Entities.Position[0];
            int lost = Lost(sim, dummy);
            sim.Step(Hold(At(5, 0)));
            Assert.AreEqual(2, sim.SabreSwing.Hit, "в корнях удар начинается как обычно");
            while (sim.Tick <= sim.SabreSwing.ContactTick) sim.Step(Hold(At(5, 0)));
            Assert.IsTrue(sim.HeroRooted);
            Assert.AreEqual(before, sim.Entities.Position[0], "в корнях выпада нет");
            Assert.AreEqual(90, Lost(sim, dummy) - lost, "добивающий лёг с места");
        }

        [Test]
        public void Finisher_ShovesLightBodies_NotHeavyOnes()
        {
            var sim = Arena();
            int light = Dummy(sim, At(1.8, .6), .5, pushable: true, kind: EnemyKind.ForestGuardian);
            int heavy = Dummy(sim, At(1.8, -.6), .5, pushable: false);
            for (int t = 0; t < 17; t++) sim.Step(Hold(At(5, 0)));
            FixVec2 lightAt = sim.Entities.Position[light], heavyAt = sim.Entities.Position[heavy];
            int contact = sim.SabreSwing.ContactTick;
            while (sim.Tick <= contact + Simulation.SabreShoveTicks) sim.Step(InputFrame.Empty);
            double shoved = (sim.Entities.Position[light] - lightAt).Length.ToDouble();
            Assert.AreEqual(.45, shoved, .05, "толчок 0,45 м");
            Assert.AreEqual(heavyAt, sim.Entities.Position[heavy], "тяжёлого не толкает");
            Assert.AreEqual((byte)ForcedMotionKind.None, sim.Entities.ForcedKind[light]);
        }

        [Test]
        public void ShoveDoesNotBreakAGuardianSwing()
        {
            Assert.IsFalse(ForcedMotionInterrupts(ForcedMotionKind.Shoved));
            Assert.IsTrue(ForcedMotionInterrupts(ForcedMotionKind.Dragged));
        }

        static bool ForcedMotionInterrupts(ForcedMotionKind kind)
        {
            var store = new EntityStore(4);
            int id = store.Spawn(FixVec2.Zero, 10, Faction.Orvill);
            ForcedMotion.Begin(store, id, At(1, 0), 4, kind);
            return ForcedMotion.IsInterrupting(store, id);
        }

        // ---- ресурс, сброс ----

        [Test]
        public void Finisher_RefundsTenOnce_OnlyWhenItLands()
        {
            foreach (bool target in new[] { true, false })
            {
                var sim = Arena();
                sim.Entities.Lavidium[0] = Fix64.FromInt(50);
                if (target) { Dummy(sim, At(1.6, .4)); Dummy(sim, At(1.6, -.4)); }
                for (int t = 0; t < 24; t++) sim.Step(Hold(At(5, 0)));
                Assert.AreEqual(target ? 60 : 50, sim.Entities.Lavidium[0].ToInt(), target ? "две цели — один возврат" : "промах");
            }
        }

        [Test]
        public void StunResetsTheSeries()
        {
            var sim = Arena();
            for (int t = 0; t < 9; t++) sim.Step(Hold(At(5, 0)));
            Assert.AreEqual(1, sim.SabreSwing.Hit);
            Assert.IsTrue(sim.ApplyHeroStun(5));
            sim.Step(Hold(At(5, 0)));
            Assert.IsTrue(sim.SabreSwing.Interrupted, "оглушение сняло удар");
            var starts = new List<Record>();
            for (int t = 0; t < 30 && starts.Count == 0; t++) Step(sim, Hold(At(5, 0)), starts, null);
            Assert.AreEqual(sim.Entities.Health[0] > 0 ? 15 : -1, starts[0].Tick, "удар — в тик конца оглушения");
            Assert.AreEqual(0, starts[0].Hit, "после оглушения — снова первый удар");
        }

        // ---- детерминизм ----

        [Test]
        public void SameInputs_SameHashes_TwiceOver()
        {
            ulong[] a = Script(), b = Script();
            CollectionAssert.AreEqual(a, b);
        }

        static ulong[] Script()
        {
            var sim = Arena(seed: 991);
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Ratio(3, 10));
            sim.RefreshPlayerStats(false);
            Dummy(sim, At(1.7, .3), .6, pushable: true, kind: EnemyKind.ForestGuardian);
            Dummy(sim, At(2.1, -.9), .45, pushable: true, kind: EnemyKind.ForestRootSwarm);
            Dummy(sim, At(-1.5, 1), .5);
            var hashes = new ulong[150];
            for (int t = 0; t < hashes.Length; t++)
            {
                InputFrame input = t % 37 == 20 ? Cast(4, At(-2, 1))
                    : t % 11 == 3 ? Tap(At(Math.Cos(t), Math.Sin(t)))
                    : t % 50 < 30 ? Hold(At(3, t % 7 - 3)) : InputFrame.Empty;
                sim.Step(in input);
                hashes[t] = sim.StateHash();
            }
            return hashes;
        }

        /// <summary>Удержание с захватом, гибелью цели, кувырком и отпусканием — дважды тем же вводом.</summary>
        [Test]
        public void HeldStick_SameInputs_SameHashes_TwiceOver()
        {
            ulong[] a = StickScript(out int lockedA), b = StickScript(out int lockedB);
            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(lockedA, lockedB);
            Assert.Greater(lockedA, 0, "сценарий держит захват");
        }

        static ulong[] StickScript(out int lockedTicks)
        {
            var sim = Arena(seed: 517);
            sim.SetAbility(4, AbilityDefinition.Dash(), Array.Empty<AbilityNode>(), 0);
            sim.Entities.Stats[0].SetBase(StatType.CritChance, Fix64.Ratio(3, 10));
            sim.RefreshPlayerStats(false);
            Dummy(sim, At(0, 2), .6, pushable: true, kind: EnemyKind.ForestGuardian);
            Dummy(sim, At(-1.8, -1), .45, pushable: true, kind: EnemyKind.ForestRootSwarm);
            Dummy(sim, At(2.5, 3), .5);
            int fragile = Dummy(sim, At(.5, -2.2));
            sim.Entities.Health[fragile] = 100;
            var hashes = new ulong[160];
            lockedTicks = 0;
            for (int t = 0; t < hashes.Length; t++)
            {
                FixVec2 cursor = At(3 * Math.Cos(t * .1), 3 * Math.Sin(t * .1));
                InputFrame input = t % 53 == 40 ? Cast(4, At(-2, 1))
                    : t % 60 == 0 ? PressHold(cursor)
                    : t % 60 < 45 ? Hold(cursor) : InputFrame.Empty;
                sim.Step(in input);
                if (sim.SabreStickTarget >= 0) lockedTicks++;
                hashes[t] = sim.StateHash();
            }
            return hashes;
        }
    }
}
