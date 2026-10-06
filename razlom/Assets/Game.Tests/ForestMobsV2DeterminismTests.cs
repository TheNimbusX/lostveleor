using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// ДЕТЕРМИНИЗМ СВЕДЁННЫХ ПОТОКОВ «Мобы леса v2» (интеграция 29.09).
    ///
    /// Лесная арена 6 (сид, маршруты и поле пути — как в забеге), герой —
    /// эталонный (270/54), смешанная стая: Камнекопыт (клыки вплотную и таран с
    /// оглушением), Вендиго (круговой удар), два Корнехвата (корни), Хранитель
    /// и рой. Сценарий героя — функция состояния боя и тика, без случайностей:
    /// по 200 тиков он бьёт кабана, стоя у его клыков (полметра от тела по
    /// взгляду, пока тот не в разбеге), потом Корнехвата, потом держится у бока
    /// Вендиго (как в WendigoSweepTests: 2,2 м под 130° к взгляду зверя).
    /// Клыки и круговой удар так не зависят от того, куда сид положил озеро и
    /// камни: до 06.10 герой подходил к кабану сам, и река посреди локации
    /// (сдвинувшая озеро арены 6) оставила прогон без единого взмаха.
    /// Здоровье героя и троих «главных» мобов доливается после шага, чтобы бой
    /// шёл до конца, — одинаково в обоих прогонах.
    ///
    /// Два независимых прогона обязаны совпасть на КАЖДОМ тике: и StateHash, и
    /// поток событий (вид по ним рисует знаки, контроль и звук). Сценарий
    /// обязан задеть все три новые механики: взмах клыками, круговой удар и
    /// корни на герое, — иначе проверка ничего не проверила.
    /// </summary>
    public sealed class ForestMobsV2DeterminismTests
    {
        private const ulong Seed = 29;
        private const int Arena = 6;
        private const int Ticks = 2400;
        private const int PhaseTicks = 200;

        [Test]
        public void MixedArena_TuskSweepAndRootedHero_SameHashEveryTick()
        {
            var a = new Run();
            var b = new Run();
            Assert.That(b.Sim.StateHash(), Is.EqualTo(a.Sim.StateHash()), "разошлись уже при расстановке");
            for (int t = 0; t < Ticks; t++)
            {
                a.Step(t);
                b.Step(t);
                Assert.That(b.Sim.StateHash(), Is.EqualTo(a.Sim.StateHash()), "StateHash разошёлся на тике " + t);
                Assert.That(b.TickEvents, Is.EqualTo(a.TickEvents), "события разошлись на тике " + t);
            }
            TestContext.WriteLine("tusks " + a.Tusks + ", tusk hits " + a.TuskHits + ", sweeps " + a.Sweeps
                + ", sweep hits " + a.SweepHits + ", roots " + a.Roots + ", stuns " + a.Stuns
                + ", tick with root active " + a.RootedTicks + ", hash " + a.Sim.StateHash().ToString("X16"));
            Assert.That(a.Tusks, Is.GreaterThan(0), "ни одного взмаха клыками");
            Assert.That(a.Sweeps, Is.GreaterThan(0), "ни одного кругового удара Вендиго");
            Assert.That(a.Roots, Is.GreaterThan(0), "герой ни разу не был связан корнями");
            Assert.That(a.RootedTicks, Is.GreaterThan(0), "корни не удержали героя ни тика");
        }

        /// <summary>Один прогон: своя симуляция и счётчики механик.</summary>
        private sealed class Run
        {
            public readonly Simulation Sim;
            private readonly LayoutMap _map;
            private readonly int _boar, _wendigo, _snarerA, _snarerB;

            public int Tusks, TuskHits, Sweeps, SweepHits, Roots, Stuns, RootedTicks;
            /// <summary>Свёртка событий последнего тика (FNV-1a по всем полям, что читает вид).</summary>
            public ulong TickEvents;

            public Run()
            {
                var location = ArenaEncounterTests.ForestLocation();
                _map = ArenaEncounterTests.ArenaMap(location, Arena, Seed);
                Sim = new Simulation(Seed, 128);
                Sim.ApplyHeroBaseline();
                Sim.SetupRift(_map, Seed, 0, 0, 100);
                Sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(Arena);
                Sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(Arena);
                Fix64 heroBody = Sim.Entities.BodyRadius[Simulation.PlayerId];
                FixVec2 center = _map.ClampToWalkable(_map.GetGlade(0).Center, heroBody);
                Sim.Entities.Position[Simulation.PlayerId] = center;

                _boar = Add(EnemyKind.ForestStonehoof, center, 0);
                _wendigo = Add(EnemyKind.ForestWendigo, center, 1);
                _snarerA = Add(EnemyKind.ForestRootSnarer, center, 2);
                _snarerB = Add(EnemyKind.ForestRootSnarer, center, 3);
                Add(EnemyKind.ForestGuardian, center, 4);
                Add(EnemyKind.ForestRootSwarm, center, 5);
                Add(EnemyKind.ForestRootSwarm, center, 6);
            }

            /// <summary>Моб на кольце 6–8 м вокруг центра, каждый следующий — на золотой угол дальше.</summary>
            private int Add(EnemyKind kind, FixVec2 center, int n)
            {
                Fix64 angle = Fix64.TwoPi * Fix64.Ratio((n * 618) % 1000, 1000);
                Fix64 radius = Fix64.FromInt(6) + Fix64.Ratio(n % 3, 1);
                FixVec2 at = _map.ClampToWalkable(center + FixVec2.FromAngle(angle) * radius, Fix64.One);
                return Sim.AddKindTestEnemy(kind, at, 100);
            }

            public void Step(int tick)
            {
                var e = Sim.Entities;
                const int hero = Simulation.PlayerId;
                var input = InputFrame.Empty;
                int phase = (tick / PhaseTicks) % 3;
                if (phase == 2 && e.Alive[_wendigo])
                {
                    // Бок-сзади Вендиго: 130° от взгляда, 2,2 м — в досягаемости
                    // когтя, но вне его конуса. Ставим перед каждым шагом: за шаг
                    // зверь доворачивает не больше 12°, герой остаётся вне конуса.
                    var f = e.Facing[_wendigo];
                    Fix64 c = Fix64.Ratio(-643, 1000), s = Fix64.Ratio(766, 1000);
                    var flank = new FixVec2(f.X * c - f.Y * s, f.X * s + f.Y * c) * Fix64.Ratio(11, 5);
                    e.Position[hero] = _map.ClampToWalkable(e.Position[_wendigo] + flank, e.BodyRadius[hero]);
                    Attack(ref input, _wendigo);
                }
                else if (phase == 1)
                {
                    int snarer = e.Alive[_snarerA] ? _snarerA : _snarerB;
                    if (e.Alive[snarer]) Attack(ref input, snarer);
                }
                else if (e.Alive[_boar])
                {
                    // Перед мордой кабана: полметра от его тела по взгляду — в
                    // досягаемости клыков (метр от тела, ±60°). Ставим перед каждым
                    // шагом, пока он не в разбеге: взмах не зависит от того, куда сид
                    // положил озеро и камни (река посреди локации, 05.10, сдвинула
                    // озеро — и герой больше ни разу не оказывался у клыков).
                    if (!Sim.TryGetStonehoofAction(_boar, out _))
                    {
                        Fix64 gap = e.BodyRadius[_boar] + e.BodyRadius[hero] + Fix64.Ratio(1, 2);
                        e.Position[hero] = _map.ClampToWalkable(e.Position[_boar] + e.Facing[_boar] * gap, e.BodyRadius[hero]);
                    }
                    Attack(ref input, _boar);
                }

                Sim.Step(input);

                ulong h = 14695981039346656037UL;
                foreach (var ev in Sim.Events)
                {
                    h = Mix(h, (ulong)ev.Type); h = Mix(h, (ulong)(uint)ev.Source); h = Mix(h, (ulong)(uint)ev.Target);
                    h = Mix(h, (ulong)(uint)ev.Amount); h = Mix(h, ev.Flag ? 1UL : 0UL);
                    h = Mix(h, (ulong)ev.Position.X.Raw); h = Mix(h, (ulong)ev.Position.Y.Raw);
                    h = Mix(h, (ulong)(uint)ev.ActionVariant);
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.StonehoofTusk) Tusks++;
                    if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.StonehoofTusk && ev.Flag) TuskHits++;
                    if (ev.Type == SimEventType.EnemyActionStarted && ev.ActionVariant == (int)EnemyActionKind.WendigoSweep) Sweeps++;
                    if (ev.Type == SimEventType.EnemyActionImpact && ev.ActionVariant == (int)EnemyActionKind.WendigoSweep && ev.Flag) SweepHits++;
                    if (ev.Type == SimEventType.HeroControl) { if (ev.Flag) Roots++; else Stuns++; }
                }
                TickEvents = h;
                if (Sim.HeroRooted) RootedTicks++;

                // Долив здоровья — после шага и одинаково в обоих прогонах.
                e.Health[hero] = e.MaxHealth[hero];
                foreach (int id in new[] { _boar, _wendigo, _snarerA, _snarerB })
                    if (e.Alive[id]) e.Health[id] = e.MaxHealth[id];
            }

            private void Attack(ref InputFrame input, int target)
            {
                input.Flags = (byte)InputFlags.Attack;
                input.AttackTarget = target;
                input.Aim = Sim.Entities.Position[target];
            }

            private static ulong Mix(ulong h, ulong v)
            {
                for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
                return h;
            }
        }
    }
}
