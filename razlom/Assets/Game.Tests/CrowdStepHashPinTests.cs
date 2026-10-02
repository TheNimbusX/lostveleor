using System.Text;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// ПРИБИТЫЙ ХЭШ ТОЛПЫ (поток D плана «Мобы леса v2»: починка просадки на
    /// 48 мобах). Оптимизации шага обязаны не менять бой ни на бит: этот тест
    /// снят ДО них и держит последовательность StateHash и событий по тикам.
    ///
    /// Сцена — та же, что у SimStepBenchmark: лесная арена 6, сид 42, 48 мобов
    /// вперемешку (рой, хранители, плюй-плод, камнекопыт, Корнехват, Расщепень,
    /// Шипомёт и Вендиго) на кольце 6–10 м, все сразу заметили героя. Два
    /// прогона: «круг» — бессмертный герой ходит приказом по кругу 5 м, стая
    /// окружает, замахивается, обходит деревья; «бой» — эталонный герой
    /// (270/54) по 4 с бьёт ближайшего и по 4 с ходит по кругу, здоровье
    /// доливается после шага: смерти, распад Расщепеня, лужи, плоды, корни.
    ///
    /// Каждый тик в свёртку идут StateHash и все поля событий; на тиках 300,
    /// 600, … сверяется свёртка — упавшая проверка говорит, между какими
    /// тиками бой разошёлся. МЕНЯЕШЬ БОЙ НАМЕРЕННО (числа, ИИ, карта) —
    /// перепиши ожидания из вывода теста: он печатает все фактические числа.
    /// Менял только скорость — тест обязан остаться зелёным.
    /// </summary>
    public sealed class CrowdStepHashPinTests
    {
        private const ulong Seed = 42;
        private const int Arena = 6;
        private const int Count = 48;
        private const int Ticks = 1800;
        private const int CheckEvery = 300;

        private static readonly EnemyKind[] Dozen =
        {
            EnemyKind.ForestRootSwarm, EnemyKind.ForestGuardian, EnemyKind.ForestRootSwarm, EnemyKind.ForestBud,
            EnemyKind.ForestRootSwarm, EnemyKind.ForestStonehoof, EnemyKind.ForestRootSwarm, EnemyKind.ForestRootSnarer,
            EnemyKind.ForestGuardian, EnemyKind.ForestSplitter, EnemyKind.ForestRootSwarm, EnemyKind.ForestThorncaster,
        };

        // Свёртка на тиках 300, 600, …, 1800 и StateHash в конце. Переснято
        // намеренно 29.09 (поток D, правки ИИ по выбору G2): такт ударов по
        // герою (Simulation.AttackRhythm, с окном оглушения тарана и плодами
        // мимо оглушённого), непоседа-стрелок и «последние идут сами» меняют
        // бой. Прежние числа держали оптимизацию шага бит в бит. Переснято
        // ещё раз 29.09 (поток B, подгонка чисел): Хранитель 500/17 и таран
        // Камнекопыта 22 меняют здоровье и удары толпы. И ещё раз 29.09
        // (ревью ночных правок): непоседа-стрелок идёт к одной огневой точке
        // не дольше RestlessWalkMaxTicks — Плюй-плоды толпы выбирают точки заново,
        // а клыки Камнекопыта спрашивают такт ударов (не по оглушённому герою).
        // И ещё раз 29.09 (съёмка stonehoof-hug): досягаемость клыков меряется от
        // поверхностей тел, а кабан с готовыми клыками не пятится от героя в их
        // досягаемости — четыре кабана толпы теперь бьют клыками по-настоящему.
        // И ещё раз 29.09 (слияние ветки Kostya): поляна арены меньше, река с бродом
        // и рунный круг меняют карту лесной арены 6 — бой расходится с первого тика.
        // И ещё раз 01.10 (ревью владельца, Лесной хранитель): ход 3,1 → 2,8 м/с,
        // удар 23/17/53 → 28/21/65, здоровье 500 → 270, между своими ударами не
        // пятится (если жетона никто не ждёт), а жетон при равном праве берёт тот,
        // кто дольше ждёт удара. Бой расходится с первого тика; два отдельных
        // прогона дали одни и те же свёртки бит в бит.
        // И ещё раз 01.10 (ревью владельца, Вендиго, Шипомёт, Корнехват): Вендиго
        // 900 HP, коготь 60 / прыжок 64 / вой 26 / круг 22; круг когтей — 14 из
        // 18 тиков за спиной, без паузы когтя и поверх стойки когтя; Шипомёт 900
        // HP, шип 60, тело 0,88; волна Корнехвата — 15% (элите 7,5%). В толпе
        // два Шипомёта, два Вендиго и четыре Корнехвата — бой расходится с
        // первого тика. Три отдельных прогона дали одни и те же свёртки бит в бит.
        // И ещё раз 01.10 вечером (серия сабли, Simulation.SabreCombo): обычная
        // атака героя — три удара сектором 45/45/90 без погони за целью, база
        // скорости атаки 3, в хеше состояние серии. «Круг» расходится от хеша
        // (герой не бьёт), «бой» — от первого удара. Два отдельных процесса
        // дали одни и те же свёртки бит в бит; в «бою» к концу живы 11 из 48.
        private static readonly ulong[] CircleFolds =
        {
            0x64557D919CA5F712UL, 0x331D911AD462C374UL, 0x8316CD37CD08E60EUL,
            0xAAE4252819AFF06CUL, 0xF52B2F0DB787C1B1UL, 0x89E41F4C352C64D3UL,
        };
        private const ulong CircleEnd = 0x5973ED8BB0A0FEC3UL;

        private static readonly ulong[] FightFolds =
        {
            0x90E0259A0B1168FBUL, 0xE235185A70D36503UL, 0x957EA480FE0E6AA9UL,
            0x58932AEEFFDD3BD9UL, 0xED3BE820F0E70BFBUL, 0x85319524528AA0B8UL,
        };
        private const ulong FightEnd = 0xE7134F99DFFF0062UL;

        [Test]
        public void CircleCrowd48_HashSequencePinned() => Check(false, CircleFolds, CircleEnd);

        [Test]
        public void FightingCrowd48_HashSequencePinned() => Check(true, FightFolds, FightEnd);

        private static void Check(bool fight, ulong[] expected, ulong expectedEnd)
        {
            var scene = new Scene(fight);
            var folds = new ulong[Ticks / CheckEvery];
            ulong fold = 14695981039346656037UL;
            for (int t = 0; t < Ticks; t++)
            {
                scene.Step(t);
                fold = Mix(fold, scene.Sim.StateHash());
                foreach (var ev in scene.Sim.Events)
                {
                    fold = Mix(fold, (ulong)ev.Type); fold = Mix(fold, (ulong)(uint)ev.Source);
                    fold = Mix(fold, (ulong)(uint)ev.Target); fold = Mix(fold, (ulong)(uint)ev.Amount);
                    fold = Mix(fold, ev.Flag ? 1UL : 0UL); fold = Mix(fold, (ulong)ev.Position.X.Raw);
                    fold = Mix(fold, (ulong)ev.Position.Y.Raw); fold = Mix(fold, (ulong)(uint)ev.ActionVariant);
                }
                if ((t + 1) % CheckEvery == 0) folds[(t + 1) / CheckEvery - 1] = fold;
            }
            ulong end = scene.Sim.StateHash();

            var actual = new StringBuilder((fight ? "бой" : "круг") + ": свёртки ");
            for (int i = 0; i < folds.Length; i++) actual.Append("0x").Append(folds[i].ToString("X16")).Append("UL, ");
            actual.Append("конец 0x").Append(end.ToString("X16")).Append("UL, живых ").Append(scene.AliveEnemies());
            TestContext.WriteLine(actual.ToString());

            for (int i = 0; i < folds.Length; i++)
                Assert.That(folds[i], Is.EqualTo(expected[i]),
                    "бой разошёлся между тиками " + (i * CheckEvery) + " и " + ((i + 1) * CheckEvery) + ". " + actual);
            Assert.That(end, Is.EqualTo(expectedEnd), actual.ToString());
        }

        private static ulong Mix(ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { h ^= (v >> (i * 8)) & 0xFF; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>Сцена SimStepBenchmark и сценарий героя — функция тика и состояния боя.</summary>
        private sealed class Scene
        {
            public readonly Simulation Sim;
            private readonly LayoutMap _map;
            private readonly FixVec2 _center;
            private readonly Fix64 _heroBody;
            private readonly bool _fight;

            public Scene(bool fight)
            {
                _fight = fight;
                var location = ArenaEncounterTests.ForestLocation();
                _map = ArenaEncounterTests.ArenaMap(location, Arena, Seed);
                Sim = new Simulation(Seed, 512);
                if (fight) Sim.ApplyHeroBaseline();
                Sim.SetupRift(_map, Seed, 0, 0, 100);
                Sim.BigAttackTokenLimit = Simulation.BigAttackTokensForArena(Arena);
                Sim.BigMarkBudget = Simulation.BigMarkBudgetForArena(Arena);
                Sim.PlayerInvulnerable = !fight;
                _heroBody = Sim.Entities.BodyRadius[Simulation.PlayerId];
                _center = _map.ClampToWalkable(_map.GetGlade(0).Center, _heroBody);
                Sim.Entities.Position[Simulation.PlayerId] = _center;
                for (int i = 0; i < Count; i++)
                {
                    EnemyKind kind = Dozen[i % Dozen.Length];
                    if (kind == EnemyKind.ForestThorncaster && (i / Dozen.Length) % 2 == 1) kind = EnemyKind.ForestWendigo;
                    Fix64 angle = Fix64.TwoPi * Fix64.Ratio((i * 618) % 1000, 1000);
                    Fix64 radius = Fix64.FromInt(6) + Fix64.Ratio(i % 5, 1);
                    FixVec2 at = _map.ClampToWalkable(_center + FixVec2.FromAngle(angle) * radius, Fix64.One);
                    Sim.AddKindTestEnemy(kind, at, 100);
                }
            }

            public void Step(int t)
            {
                var e = Sim.Entities;
                var input = InputFrame.Empty;
                int target = _fight && (t / 120) % 2 == 0 ? Nearest() : -1;
                if (target > 0)
                {
                    input.Flags = (byte)InputFlags.Attack;
                    input.AttackTarget = target;
                    input.Aim = e.Position[target];
                }
                else
                {
                    // Приказ идти по кругу: точка на 40° впереди на окружности 5 м.
                    input.Flags = (byte)InputFlags.MoveOrder;
                    Fix64 lead = Fix64.TwoPi * Fix64.Ratio(t, 600) + Fix64.Ratio(7, 10);
                    input.Aim = _map.ClampToWalkable(_center + FixVec2.FromAngle(lead) * Fix64.FromInt(5), _heroBody);
                }
                Sim.Step(input);
                if (_fight && e.Alive[Simulation.PlayerId]) e.Health[Simulation.PlayerId] = e.MaxHealth[Simulation.PlayerId];
            }

            private int Nearest()
            {
                var e = Sim.Entities;
                FixVec2 hero = e.Position[Simulation.PlayerId];
                int best = -1;
                Fix64 bestDistance = Fix64.MaxValue;
                for (int i = 1; i < e.Count; i++)
                {
                    if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                    Fix64 d = FixVec2.DistanceSq(hero, e.Position[i]);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                return best;
            }

            public int AliveEnemies()
            {
                int alive = 0;
                for (int i = 1; i < Sim.Entities.Count; i++)
                    if (Sim.Entities.Alive[i] && Sim.Entities.Side[i] != Faction.Wole) alive++;
                return alive;
            }
        }
    }
}
