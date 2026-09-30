using NUnit.Framework;
using Game.Sim;

namespace Game.Tests
{
    /// <summary>
    /// Зона полигона в лагере (владелец, 29 сентября): бить и колдовать можно только у
    /// манекенов, кувырок и зелья — везде, левый клик вне полигона просто ведёт героя.
    /// </summary>
    public class CampTrainingZoneTests
    {
        // Полигон — круг радиусом 3 м вокруг (10, 0); герой стоит в (0, 0), за его пределами.
        static readonly FixVec2 ZoneCenter = new FixVec2(Fix64.FromInt(10), Fix64.Zero);
        static readonly Fix64 ZoneRadius = Fix64.FromInt(3);
        const int Player = Simulation.PlayerId;
        const int Dash = PelagKit.DashSlot;
        // Первый манекен нарочно ВНЕ полигона и в шаге от героя: без запрета его били бы сразу.
        const int NearDummy = 1, ZoneDummy = 2;

        static GameSession Create(Camp camp = null)
        {
            var session = camp != null
                ? new GameSession(7123UL, camp, PrototypeContent.Modules(), PrototypeContent.ItemBaseIds())
                : PrototypeContent.NewSession(7123UL);
            session.ConfigureCampTraining(new[]
            {
                new CampDummyDefinition(new FixVec2(Fix64.Ratio(3, 2), Fix64.Zero), 100000, Fix64.Zero, Fix64.Zero),
                new CampDummyDefinition(new FixVec2(Fix64.FromInt(10), Fix64.One), 100000, Fix64.Zero, Fix64.Zero),
            }, ZoneCenter, ZoneRadius);
            // Вихрь в первом слоте и кувырок — набор лагеря, который в игре ставит представление.
            session.CampLoadout.ApplyTo(session.CampSim);
            var sim = session.CampSim;
            sim.Entities.Lavidium[Player] = Fix64.FromInt(sim.Entities.MaxLavidium[Player]);
            return session;
        }

        static void PutHero(GameSession session, FixVec2 at)
        {
            session.CampSim.Entities.Position[Player] = at;
            session.CampSim.StopPlayerMovement();
        }

        static FixVec2 At(int x, int y) => new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));

        static InputFrame AttackOn(int target, FixVec2 aim)
            => new InputFrame { Aim = aim, Flags = (byte)InputFlags.Attack, AttackTarget = target, AbilityTarget = -1 };

        static InputFrame Press(int slot, FixVec2 aim)
            => new InputFrame { Aim = aim, AbilityMask = (byte)(1 << slot), AbilityHoldMask = (byte)(1 << slot), AttackTarget = -1, AbilityTarget = -1 };

        static int Casts(Simulation sim, int slot)
        {
            int count = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.AbilityCast && e.Source == Player && e.Amount == slot) count++;
            return count;
        }

        static int PlayerSwings(Simulation sim)
        {
            int count = 0;
            foreach (var e in sim.Events)
                if (e.Type == SimEventType.Attack && e.Source == Player) count++;
            return count;
        }

        [Test]
        public void OutsideZoneAttackPressOnlyWalksTowardTheCursor()
        {
            var session = Create();
            var sim = session.CampSim;
            int health = sim.Entities.Health[NearDummy];
            int swings = 0;
            FixVec2 aim = new FixVec2(Fix64.FromInt(2), Fix64.FromInt(-2));
            for (int i = 0; i < 180; i++)
            {
                session.Step(AttackOn(NearDummy, aim));
                swings += PlayerSwings(sim);
            }
            Assert.That(swings, Is.Zero, "вне полигона ЛКМ не даёт даже пустого взмаха");
            Assert.That(sim.Entities.Health[NearDummy], Is.EqualTo(health));
            Assert.That(session.Training.Hits, Is.Zero);
            Assert.That(sim.AttackTarget, Is.EqualTo(-1), "цель удара вне полигона не защёлкивается");
            Assert.That(FixVec2.Distance(sim.Entities.Position[Player], aim), Is.LessThan(Fix64.One),
                "левый клик вне полигона ведёт героя к курсору");
            Assert.That(session.CampCombatAllowed, Is.False);
        }

        [Test]
        public void OutsideZoneAbilityPressDoesNothingButDashWorks()
        {
            var session = Create();
            var sim = session.CampSim;
            Assert.That(sim.GetAbility(0), Is.Not.Null);
            Assert.That(sim.GetAbility(Dash), Is.Not.Null);
            Fix64 lavidium = sim.Entities.Lavidium[Player];

            session.Step(Press(0, At(3, 0)));
            Assert.That(Casts(sim, 0), Is.Zero, "Вихрь вне полигона не начинается");
            Assert.That(sim.AbilityReadyTick(0), Is.LessThanOrEqualTo(sim.Tick), "кулдаун не тратится");
            Assert.That(sim.Entities.Lavidium[Player], Is.GreaterThanOrEqualTo(lavidium), "лавидий не тратится");

            FixVec2 before = sim.Entities.Position[Player];
            session.Step(Press(Dash, At(0, 5)));
            Assert.That(Casts(sim, Dash), Is.EqualTo(1), "кувырок работает везде");
            for (int i = 0; i < 20; i++) session.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[Player].Y - before.Y, Is.GreaterThan(Fix64.One), "кувырок унёс героя к курсору");
            Assert.That(session.CampCombatAllowed, Is.False);
        }

        [Test]
        public void OutsideZonePotionDoesNotDrinkOrSpendStock()
        {
            var camp = new Camp(PrototypeContent.Items());
            camp.Earn(CurrencyType.Gold, 500);
            camp.MeetAlchemist();
            Assert.That(camp.BuyPotion(PotionKind.SmallHealth), Is.True);
            var session = Create(camp);
            var sim = session.CampSim;
            Assert.That(session.CampCombatAllowed, Is.False);
            sim.Entities.Health[Player] = 1;
            session.Step(new InputFrame { AttackTarget = -1, AbilityTarget = -1, PotionMask = Camp.PotionInputBit(PotionKind.SmallHealth) });
            Assert.That(sim.Entities.Health[Player], Is.EqualTo(1));
            Assert.That(camp.PotionCount(PotionKind.SmallHealth), Is.EqualTo(1));
        }

        [Test]
        public void InsideZoneAttackAbilitiesAndDashAllWork()
        {
            var session = Create();
            var sim = session.CampSim;
            PutHero(session, ZoneCenter);
            Assert.That(session.CampCombatAllowed, Is.True);

            int swings = 0;
            for (int i = 0; i < 90; i++)
            {
                session.Step(AttackOn(ZoneDummy, At(10, 1)));
                swings += PlayerSwings(sim);
            }
            Assert.That(swings, Is.GreaterThan(0));
            Assert.That(session.Training.Hits, Is.GreaterThan(0));
            Assert.That(sim.Entities.Health[ZoneDummy], Is.LessThan(sim.Entities.MaxHealth[ZoneDummy]));

            PutHero(session, ZoneCenter);
            sim.Entities.Lavidium[Player] = Fix64.FromInt(sim.Entities.MaxLavidium[Player]);
            for (int i = 0; i < 60; i++) session.Step(InputFrame.Empty);
            session.Step(Press(0, At(10, 1)));
            Assert.That(Casts(sim, 0), Is.EqualTo(1), "Вихрь на полигоне начинается");

            for (int i = 0; i < 90; i++) session.Step(InputFrame.Empty);
            PutHero(session, ZoneCenter);
            session.Step(Press(Dash, At(10, -2)));
            Assert.That(Casts(sim, Dash), Is.EqualTo(1));
        }

        [Test]
        public void BoundaryBelongsToTheZone()
        {
            var session = Create();
            var training = session.Training;
            Assert.That(training.ZoneCenter, Is.EqualTo(ZoneCenter));
            Assert.That(training.ZoneRadius, Is.EqualTo(ZoneRadius));

            FixVec2 edge = new FixVec2(Fix64.FromInt(13), Fix64.Zero);
            FixVec2 beyond = new FixVec2(Fix64.FromInt(13) + Fix64.Ratio(1, 100), Fix64.Zero);
            FixVec2 inside = new FixVec2(Fix64.FromInt(13) - Fix64.Ratio(1, 100), Fix64.Zero);
            Assert.That(training.InZone(edge), Is.True, "граница — ещё полигон");
            Assert.That(training.InZone(inside), Is.True);
            Assert.That(training.InZone(beyond), Is.False);

            InputFrame input = Press(0, edge);
            input.Flags = (byte)InputFlags.Attack;
            input.AttackTarget = ZoneDummy;
            InputFrame atEdge = input;
            Assert.That(training.GateInput(ref atEdge, edge), Is.False);
            Assert.That(atEdge.AbilityMask, Is.EqualTo(input.AbilityMask));
            Assert.That(atEdge.Flags, Is.EqualTo(input.Flags));
            Assert.That(atEdge.AttackTarget, Is.EqualTo(ZoneDummy));

            InputFrame outside = input;
            outside.AbilityMask |= 1 << Dash;
            Assert.That(training.GateInput(ref outside, beyond), Is.True);
            Assert.That(outside.AbilityMask, Is.EqualTo((byte)(1 << Dash)), "вне зоны остаётся только кувырок");
            Assert.That(outside.AbilityHoldMask, Is.Zero);
            Assert.That(outside.Has(InputFlags.Attack), Is.False);
            Assert.That(outside.Has(InputFlags.MoveOrder), Is.True, "ЛКМ становится приказом идти");
            Assert.That(outside.AttackTarget, Is.EqualTo(-1));

            // С клавиатуры герой идёт сам: удар снимается, приказа к курсору не появляется.
            InputFrame keyboard = input;
            keyboard.Flags = (byte)(InputFlags.Attack | InputFlags.DirectMovement);
            training.GateInput(ref keyboard, beyond);
            Assert.That(keyboard.Flags, Is.EqualTo((byte)InputFlags.DirectMovement));

            PutHero(session, edge);
            Assert.That(session.CampCombatAllowed, Is.True);
            PutHero(session, beyond);
            Assert.That(session.CampCombatAllowed, Is.False);
        }

        [Test]
        public void UnmeasuredGroundFallsBackToACircleAroundTheDummies()
        {
            var session = PrototypeContent.NewSession(7123UL);
            session.ConfigureCampTraining(new[]
            {
                new CampDummyDefinition(At(2, 0), 1000, Fix64.Zero, Fix64.Zero),
                new CampDummyDefinition(At(8, 8), 1000, Fix64.Zero, Fix64.Zero),
            });
            var training = session.Training;
            Assert.That(training.ZoneCenter, Is.EqualTo(At(5, 4)));
            // Полудиагональ рамки манекенов 6×8 — ровно 5 м; корень Fix64 допускает последний знак.
            Fix64 expected = Fix64.FromInt(5) + CampTraining.FallbackZoneMargin;
            Assert.That(Fix64.Abs(training.ZoneRadius - expected), Is.LessThan(Fix64.Ratio(1, 1000)));
            Assert.That(training.InZone(At(2, 0)) && training.InZone(At(8, 8)), Is.True);
            Assert.That(training.InZone(At(5, 4) + new FixVec2(Fix64.FromInt(8), Fix64.Zero)), Is.False);
        }

        [Test]
        public void RiftIgnoresTheCampZone()
        {
            var session = Create();
            session.EnterRift();
            Assert.That(session.Mode, Is.EqualTo(GameMode.Rift));
            Assert.That(session.CampCombatAllowed, Is.True);
        }
    }
}
