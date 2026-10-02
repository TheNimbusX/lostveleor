using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;
using static Game.Tests.SquallTests;

namespace Game.Tests
{
    /// <summary>
    /// Формы Шквала (владелец 02.10, Simulation.Squall): Охота, Пенный след,
    /// Неуловимый. Форма ставится узлами таблицы, как её ставит набор забега.
    /// Числа — заглушки: тесты держат правила, а не баланс.
    /// </summary>
    public sealed class SquallFormTests
    {
        [Test]
        public void Table_SquallFormsAreFiveToSeven_OnTheSquallLine_Ready_SwitchStaysOff()
        {
            Assert.AreEqual(5, (int)PelagForm.SquallHunt);
            Assert.AreEqual(6, (int)PelagForm.SquallFoamTrail);
            Assert.AreEqual(7, (int)PelagForm.SquallElusive);
            int line = PelagKit.PoolIndexOf(AbilityDefinition.ChainStepId);
            Assert.AreEqual(3, line);
            Assert.AreEqual(3, PelagForms.FormCount(line, readyOnly: false));
            Assert.AreEqual(3, PelagForms.ReadyFormCount(line), "механика всех трёх написана");
            Assert.AreEqual(PelagForm.SquallHunt, PelagForms.FormAt(line, 0, false));
            Assert.AreEqual(PelagForm.SquallFoamTrail, PelagForms.FormAt(line, 1, false));
            Assert.AreEqual(PelagForm.SquallElusive, PelagForms.FormAt(line, 2, false));
            Assert.AreEqual("form.squall.hunt", PelagForms.KeyOf(PelagForm.SquallHunt));
            Assert.AreEqual("form.squall.foam_trail", PelagForms.KeyOf(PelagForm.SquallFoamTrail));
            Assert.AreEqual("form.squall.elusive", PelagForms.KeyOf(PelagForm.SquallElusive));
            Assert.IsFalse(FormRewardRules.UseSkillForms, "обычные забеги без форм до приёмки");
            Assert.AreEqual(PelagForm.SquallElusive, Arena(PelagForm.SquallElusive).FormAt(Slot));
        }

        // ---------- без формы ----------

        [Test]
        public void NoForm_HasNoFormEvents_AndIsNotShielded()
        {
            Simulation sim = Arena();
            int lone = Enemy(sim, 3000, 0);
            List<Frame> frames = Run(sim, 40, t =>
            {
                Assert.IsFalse(sim.SquallShielded, "без формы и таланта неуязвимости нет, тик " + sim.Tick);
                return t == 0 ? Press(lone) : InputFrame.Empty;
            });
            Assert.AreEqual(4, Of(frames, SimEventType.SquallStrike).Count);
            Assert.AreEqual(0, Of(frames, SimEventType.SquallHuntKill).Count);
            Assert.AreEqual(0, Of(frames, SimEventType.SquallFoamStrip).Count);
            Assert.AreEqual(0, Of(frames, SimEventType.SquallReturn).Count);
            Assert.Greater(Dist(frames[frames.Count - 1].Position, FixVec2.Zero), 1.5, "без возврата остался у цели");
        }

        // ---------- Охота ----------

        [Test]
        public void Hunt_JumpsToTheMostWounded_OverTheZigzagChoice()
        {
            Simulation plain = Arena();
            int first = Enemy(plain, 3000, 0), left = Enemy(plain, 5000, 1500), right = Enemy(plain, 5000, -1500);
            plain.Entities.Health[right] = Health * 3 / 10;
            Assert.AreEqual(left, Of(Cast(plain, first, 12), SimEventType.SquallJump)[1].ev.Target, "без формы — зигзаг налево");

            Simulation hunt = Arena(PelagForm.SquallHunt);
            first = Enemy(hunt, 3000, 0); Enemy(hunt, 5000, 1500); right = Enemy(hunt, 5000, -1500);
            hunt.Entities.Health[right] = Health * 3 / 10;
            Assert.AreEqual(right, Of(Cast(hunt, first, 12), SimEventType.SquallJump)[1].ev.Target, "Охота — к самому раненому");
        }

        private static int Kills(PelagForm form, out List<Frame> frames)
        {
            Simulation sim = Arena(form);
            int first = -1;
            for (int i = 0; i < 5; i++)
                for (int row = -1; row <= 1; row += 2)
                {
                    int id = Enemy(sim, 2500 + i * 1200, row * 1000, health: 50);
                    if (first < 0) first = id;
                }
            frames = Cast(sim, first, 80);
            int dead = 0;
            for (int id = 1; id < sim.Entities.Count; id++) if (!sim.Entities.Alive[id]) dead++;
            return dead;
        }

        [Test]
        public void Hunt_AKillingHitGivesAnExtraJump_UpToTheCap()
        {
            Assert.AreEqual(4, Kills(PelagForm.None, out List<Frame> plain), "без формы — четыре прыжка, четыре убийства");
            Assert.AreEqual(4, Of(plain, SimEventType.SquallStrike).Count);

            int dead = Kills(PelagForm.SquallHunt, out List<Frame> hunt);
            Assert.AreEqual(4 + Simulation.HuntBonusHopsMax, Of(hunt, SimEventType.SquallStrike).Count, "лишний прыжок за убийство, не больше потолка");
            Assert.AreEqual(4 + Simulation.HuntBonusHopsMax, dead);
            var bonus = Of(hunt, SimEventType.SquallHuntKill);
            Assert.AreEqual(Simulation.HuntBonusHopsMax, bonus.Count);
            for (int i = 0; i < bonus.Count; i++) Assert.AreEqual(i + 1, bonus[i].ev.Amount);
        }

        // ---------- Пенный след ----------

        [Test]
        public void FoamTrail_EveryJumpLaysAStripFromStartToLanding()
        {
            Simulation sim = Arena(PelagForm.SquallFoamTrail);
            int lone = Enemy(sim, 3000, 0);
            List<Frame> frames = Cast(sim, lone, 40);
            var strips = Of(frames, SimEventType.SquallFoamStrip);
            var jumps = Of(frames, SimEventType.SquallJump);
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(4, strips.Count, "полоса на каждый прыжок");
            for (int i = 0; i < strips.Count; i++)
            {
                Assert.AreEqual(strikes[i].frame.Tick, strips[i].frame.Tick, "полоса ложится в тик посадки");
                Assert.IsTrue(sim.TryGetSquallFoamStrip(strips[i].ev.Amount, out FixVec2 from, out FixVec2 to, out int until));
                Assert.Less(Dist(from, i == 0 ? FixVec2.Zero : strikes[i - 1].frame.Position), 1e-3);
                Assert.Less(Dist(to, jumps[i].ev.Position), 1e-3);
                Assert.AreEqual(strips[i].frame.Tick + Simulation.FoamTrailLifeTicks, until);
            }
            int lastUntil = strips[3].frame.Tick + Simulation.FoamTrailLifeTicks;
            while (sim.Tick < lastUntil) sim.Step(InputFrame.Empty);
            for (int s = 0; s < Simulation.FoamTrailCapacity; s++)
                Assert.IsFalse(sim.TryGetSquallFoamStrip(s, out _, out _, out _), "полоса живёт FoamTrailLifeTicks");
        }

        [Test]
        public void FoamTrail_StandingOnTheFoamHurtsAndSlows_LeavingItEndsTheSlow()
        {
            Simulation sim = Arena(PelagForm.SquallFoamTrail);
            int lone = Enemy(sim, 3000, 0);
            Cast(sim, lone, 30);

            // На середину первой полосы (0,0 → 2,0) — враг со своим шагом.
            int walker = Enemy(sim, 1000, 0);
            sim.Entities.Stats[walker].SetBase(StatType.MoveSpeed, Fix64.FromInt(2));
            sim.Entities.RefreshStats(walker);
            Fix64 fullStep = sim.Entities.MoveStep[walker];
            int ticks = Simulation.FoamTrailPulseTicks + 2;
            var hurt = new List<SimEvent>();
            for (int t = 0; t < ticks; t++)
            {
                sim.Entities.Position[walker] = new FixVec2(Fix64.One, Fix64.Zero);
                sim.Step(InputFrame.Empty);
                foreach (SimEvent ev in sim.Events)
                    if (ev.Type == SimEventType.DamageOverTime && ev.Target == walker && ev.Source == Simulation.PlayerId) hurt.Add(ev);
            }
            Assert.IsTrue(sim.SquallFoamSlowed(walker), "на пене — замедлен");
            Assert.AreEqual((fullStep * Fix64.Ratio(100 - Simulation.FoamTrailSlowPercent, 100)).ToDouble(),
                sim.Entities.MoveStep[walker].ToDouble(), 1e-6, "шаг −30%");
            Assert.AreEqual(1, hurt.Count, "импульс пены раз в полсекунды — тиком урона, не ударом");
            Assert.AreEqual(85 * Simulation.FoamTrailDamagePercent / 100, hurt[0].Amount);

            // Ушёл с пены — замедление держится ещё FoamTrailSlowLingerTicks и сходит.
            var away = new FixVec2(Fix64.FromInt(-30), Fix64.FromInt(-30));
            for (int t = 0; t < Simulation.FoamTrailSlowLingerTicks + 2; t++)
            {
                sim.Entities.Position[walker] = away;
                sim.Step(InputFrame.Empty);
            }
            Assert.IsFalse(sim.SquallFoamSlowed(walker));
            Assert.AreEqual(fullStep.ToDouble(), sim.Entities.MoveStep[walker].ToDouble(), 1e-6, "шаг вернулся");
        }

        // ---------- Неуловимый ----------

        [Test]
        public void Elusive_IsInvulnerableThroughTheJumps_AndNotInTheExit()
        {
            Simulation sim = Arena(PelagForm.SquallElusive);
            int lone = Enemy(sim, 3000, 0);
            bool sawJumpWindow = false, sawExit = false;
            for (int t = 0; t < 60; t++)
            {
                SquallPhase phase = sim.Squall.Phase;
                int before = sim.Entities.Health[Simulation.PlayerId];
                sim.ApplyAbilityDamage(lone, Simulation.PlayerId, 5, -1, DamageType.Physical);
                bool hurt = sim.Entities.Health[Simulation.PlayerId] < before;
                bool window = phase == SquallPhase.Windup || phase == SquallPhase.Flight
                              || phase == SquallPhase.Stop || phase == SquallPhase.Return;
                if (t > 0 && window) { Assert.IsFalse(hurt, "в прыжках урон не проходит, фаза " + phase); sawJumpWindow = true; }
                if (phase == SquallPhase.Exit) { Assert.IsTrue(hurt, "в выходе неуязвимости нет"); sawExit = true; }
                sim.Step(t == 0 ? Press(lone) : InputFrame.Empty);
            }
            Assert.IsTrue(sawJumpWindow && sawExit);
        }

        [Test]
        public void Elusive_LastJumpReturnsToTheCastSpot_InAnArc_FacingThePath()
        {
            Simulation sim = Arena(PelagForm.SquallElusive);
            int lone = Enemy(sim, 3000, 0);
            List<Frame> frames = Cast(sim, lone, 60);
            var returns = Of(frames, SimEventType.SquallReturn);
            var strikes = Of(frames, SimEventType.SquallStrike);
            Assert.AreEqual(1, returns.Count);
            Assert.AreEqual(4, strikes.Count, "возврат — после всех прыжков, не вместо");
            Assert.AreEqual(strikes[3].frame.Tick + Simulation.SquallStopTicks, returns[0].frame.Tick, "опора удара, потом возврат");
            Assert.AreEqual(2, returns[0].ev.ActionVariant, "дуга: две точки пути");

            SquallState s = returns[0].frame.Squall;
            FixVec2 line = s.Origin - s.From;
            double offset0 = System.Math.Abs(Cross(line.Normalized(), s.Via0 - s.From));
            double offset1 = System.Math.Abs(Cross(line.Normalized(), s.Via1 - s.From));
            Assert.That(offset0, Is.InRange(0.85, 1.4), "изгиб 1–1,5 м вбок");
            Assert.AreEqual(offset0, offset1, 1e-3);

            int returnFrame = frames.IndexOf(returns[0].frame);
            for (int i = returnFrame + 1; i < frames.Count && frames[i].Squall.Phase == SquallPhase.Return; i++)
            {
                FixVec2 step = frames[i].Position - frames[i - 1].Position;
                if (step.LengthSq.Raw == 0) continue;
                Assert.Greater(Dot(frames[i - 1].Facing, step), 0.9, "лицом по пути, не спиной, тик " + frames[i].Tick);
            }
            Assert.Less(Dist(frames[frames.Count - 1].Position, FixVec2.Zero), 1e-3, "вернулся в точку каста");
            Assert.AreEqual((int)SquallEnd.Done, Of(frames, SimEventType.SquallEnded)[0].ev.Amount);
            Assert.AreEqual(returns[0].frame.Tick + returns[0].ev.Amount, frames.Find(f => f.Squall.Phase == SquallPhase.Exit).Tick,
                "выход — с тика прибытия");
        }

        [Test]
        public void ReturnTalentAndElusive_AreOneReturn_NotTwo()
        {
            Simulation both = Arena(PelagForm.SquallElusive, extra: new[] { Talent(5) });
            int lone = Enemy(both, 3000, 0);
            List<Frame> frames = Cast(both, lone, 60);
            Assert.AreEqual(1, Of(frames, SimEventType.SquallReturn).Count);
            Assert.Less(Dist(frames[frames.Count - 1].Position, FixVec2.Zero), 1e-3);

            // Талант без формы — та же механика, но без неуязвимости.
            Simulation talent = Arena(extra: new[] { Talent(5) });
            lone = Enemy(talent, 3000, 0);
            bool shielded = false;
            frames = Run(talent, 60, t =>
            {
                shielded |= talent.SquallShielded;
                return t == 0 ? Press(lone) : InputFrame.Empty;
            });
            Assert.AreEqual(1, Of(frames, SimEventType.SquallReturn).Count);
            Assert.Less(Dist(frames[frames.Count - 1].Position, FixVec2.Zero), 1e-3, "«Возврат» — тот же прыжок назад");
            Assert.IsFalse(shielded, "«Возврат» не даёт неуязвимости");
        }

        // ---------- детерминизм ----------

        private static readonly EnemyKind[] Crowd =
        {
            EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm, EnemyKind.ForestBud,
            EnemyKind.ForestStonehoof, EnemyKind.ForestRootSnarer, EnemyKind.ForestGuardian, EnemyKind.ForestSplitter,
        };

        /// <summary>Толпа 16 мобов с ИИ; герой (здоровье доливается) кастует Шквал в ближайшего, как только готов.</summary>
        private static ulong[] CrowdRun(PelagForm form, out int strikes)
        {
            Simulation sim = Arena(form, seed: 42);
            for (int i = 0; i < 16; i++)
            {
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio((i * 618) % 1000, 1000);
                Fix64 radius = Fix64.FromInt(3) + Fix64.Ratio(i % 4, 1);
                sim.AddKindTestEnemy(Crowd[i % Crowd.Length], FixVec2.FromAngle(angle) * radius, 100);
            }
            var hashes = new ulong[600];
            strikes = 0;
            for (int t = 0; t < hashes.Length; t++)
            {
                var e = sim.Entities;
                int nearest = -1;
                Fix64 best = Fix64.MaxValue;
                for (int i = 1; i < e.Count; i++)
                {
                    if (!e.Alive[i] || e.Side[i] == e.Side[Simulation.PlayerId]) continue;
                    Fix64 d = FixVec2.DistanceSq(e.Position[i], e.Position[Simulation.PlayerId]);
                    if (d < best) { best = d; nearest = i; }
                }
                e.Lavidium[Simulation.PlayerId] = Fix64.FromInt(e.MaxLavidium[Simulation.PlayerId]);
                sim.Step(nearest > 0 ? Press(nearest) : InputFrame.Empty);
                if (e.Alive[Simulation.PlayerId]) e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId];
                ulong h = sim.StateHash();
                foreach (SimEvent ev in sim.Events)
                {
                    if (ev.Type == SimEventType.SquallStrike) strikes++;
                    Hashing.Mix(ref h, (int)ev.Type); Hashing.Mix(ref h, ev.Source); Hashing.Mix(ref h, ev.Target);
                    Hashing.Mix(ref h, ev.Amount); Hashing.Mix(ref h, ev.Flag ? 1 : 0); Hashing.Mix(ref h, ev.ActionVariant);
                    Hashing.Mix(ref h, ev.Position.X); Hashing.Mix(ref h, ev.Position.Y);
                }
                hashes[t] = h;
            }
            return hashes;
        }

        [Test]
        public void Determinism_TwoRunsAreBitIdentical_WithEveryForm()
        {
            PelagForm[] forms = { PelagForm.None, PelagForm.SquallHunt, PelagForm.SquallFoamTrail, PelagForm.SquallElusive };
            ulong[] plain = null;
            foreach (PelagForm form in forms)
            {
                ulong[] a = CrowdRun(form, out int strikes);
                ulong[] b = CrowdRun(form, out int again);
                Assert.GreaterOrEqual(strikes, 16, form + ": Шквал в толпе бил (перезарядка 4,2 с — пять кастов)");
                Assert.AreEqual(strikes, again);
                CollectionAssert.AreEqual(a, b, form + ": два прогона разошлись");
                if (form == PelagForm.None) plain = a;
                else CollectionAssert.AreNotEqual(plain, a, form + ": форма ничего не поменяла");
            }
        }
    }
}
