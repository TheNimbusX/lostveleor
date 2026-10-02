using System;

namespace Game.Sim
{
    /// <summary>
    /// БЮДЖЕТ КРУПНЫХ МЕТОК (просьба владельца от 26.09: «количество
    /// телеграфов на полу иногда очень огромное, тяжело понять, куда двигаться»).
    ///
    /// Крупный жетон считает, СКОЛЬКО крупных атак идёт разом; бюджет — сколько
    /// крупных меток лежит на земле. Одновременно не больше 3 на аренах 1–4 и 4
    /// с пятой, и следующая крупная метка встаёт не раньше чем через 9 тиков
    /// (0,3 с) после предыдущей и не бьёт ближе 6 тиков к удару уже лежащей —
    /// удары не складываются в одну вспышку. Вес: залп Плюй-плода (пять дисков)
    /// — 2 от замаха до падения последнего плода; таран, прыжок, вой и круговой
    /// удар Вендиго, линия и всплеск Шипомёта, удар корнями, перекат Расщепеня
    /// — по 1; все кислые лужи в 8 м от героя вместе — 1. Ближние секторы, выстрел шипом
    /// и лечение Корнехвата не считаются: это мелкие или дружеские метки.
    ///
    /// Всплеск Шипомёта в бюджете виден, но сам им не ограничивается: это
    /// самозащита прижатого вплотную, без неё герой бил бы его безнаказанно.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int BigMarkStaggerTicks = 9;
        public const int BigMarkImpactSpacingTicks = 6;

        /// <summary>Вес залпа Плюй-плода в бюджете: пять дисков на земле — как две метки.</summary>
        public const int BudVolleyMarkWeight = 2;

        private int _bigMarkBudget = 3;

        /// <summary>Крупных меток на земле разом. Задаёт забег по номеру арены (BigMarkBudgetForArena).</summary>
        public int BigMarkBudget
        {
            get => _bigMarkBudget;
            set => _bigMarkBudget = value < 1 ? 1 : value;
        }

        /// <summary>Три крупные метки на аренах 1–4, четыре с пятой.</summary>
        public static int BigMarkBudgetForArena(int arena) => arena >= 5 ? 4 : 3;

        /// <summary>
        /// Сколько крупных меток держит на земле моб id прямо сейчас, когда
        /// начал и когда его ближайший удар. 0 — ничего крупного.
        /// </summary>
        private int BigMarkWeightOf(int id, out int start, out int impact)
        {
            start = int.MinValue; impact = int.MinValue;
            switch (Entities.Kind[id])
            {
                case EnemyKind.ForestBud:
                {
                    var attack = _forestBudAttacks[id];
                    int lastFruit = BudLastFruitImpact(id, out int firstFruit);
                    if (attack.Serial == 0 && lastFruit < Tick) return 0;
                    start = attack.Serial != 0 ? attack.StartTick : Tick - BigMarkStaggerTicks;
                    impact = attack.Serial != 0 && attack.ShotsFired == 0
                        ? attack.FirstShotTick + ForestBudConfig.FlightTicks : firstFruit;
                    return BudVolleyMarkWeight;
                }
                case EnemyKind.ForestStonehoof:
                {
                    var a = _stonehoofActions[id];
                    if (a.Serial == 0 || Tick > a.StopTick) return 0;
                    start = a.StartTick; impact = a.LaunchTick;
                    return 1;
                }
                case EnemyKind.ForestWendigo:
                {
                    if (!WendigoHoldsBigToken(id)) return 0;
                    var a = _wendigoActions[id];
                    start = a.StartTick; impact = a.ImpactTick;
                    return 1;
                }
                case EnemyKind.ForestThorncaster:
                {
                    var a = _thorncasters[id];
                    if (a.Serial == 0 || a.Action == ThornAction.Shot || a.Action == ThornAction.None || Tick > a.ImpactTick) return 0;
                    start = a.StartTick; impact = ThornContactTick(a, 0);
                    return 1;
                }
                case EnemyKind.ForestRootSnarer:
                {
                    if (!RootSnarerHoldsBigToken(id)) return 0;
                    var a = _rootSnarers[id];
                    start = a.StartTick; impact = a.ImpactTick;
                    return 1;
                }
                case EnemyKind.ForestSplitter:
                    return SplitterRollMarkWeight(id, out start, out impact);
                case EnemyKind.ForestThicketMaster:
                    return ThicketMasterMarkWeight(id, out start, out impact);
            }
            return 0;
        }

        /// <summary>Тик падения последнего летящего плода стрелка id (и первого — в firstFruit); MinValue — нет плодов.</summary>
        private int BudLastFruitImpact(int id, out int firstFruit)
        {
            int last = int.MinValue; firstFruit = int.MaxValue;
            int from = id * ForestFruitSlotsPerEnemy;
            for (int slot = from; slot < from + ForestFruitSlotsPerEnemy && slot < _forestFruitHighWater; slot++)
            {
                var fruit = _forestFruits[slot];
                if (fruit.Serial == 0) continue;
                if (fruit.ImpactTick > last) last = fruit.ImpactTick;
                if (fruit.ImpactTick < firstFruit) firstFruit = fruit.ImpactTick;
            }
            if (firstFruit == int.MaxValue) firstFruit = int.MinValue;
            return last;
        }

        /// <summary>
        /// Сколько крупных меток весом лежит на земле прямо сейчас (все мобы и
        /// лужи) и когда встала последняя. Для тестов бюджета и отладки.
        /// </summary>
        internal int BigMarkLoad(out int lastStart)
        {
            int used = PuddleNearHero() ? 1 : 0;
            lastStart = int.MinValue;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (!Entities.Alive[id] && Entities.Kind[id] != EnemyKind.ForestBud) continue;
                int w = BigMarkWeightOf(id, out int start, out _);
                if (w == 0) continue;
                used += w;
                if (start > lastStart) lastStart = start;
            }
            return used;
        }

        /// <summary>
        /// Можно ли мобу self положить на землю крупную метку весом weight с
        /// ударом в impactTick: бюджет не превышен, прошлая крупная встала не
        /// раньше чем BigMarkStaggerTicks назад, и удары не сходятся ближе
        /// BigMarkImpactSpacingTicks. Метки самого self не считаются.
        /// </summary>
        internal bool BigMarkAllowed(int self, int weight, int impactTick)
        {
            int used = PuddleNearHero() ? 1 : 0, lastStart = int.MinValue;
            for (int id = 1; id < Entities.Count; id++)
            {
                if (id == self) continue;
                // Плоды мёртвого стрелка ещё падают — их диски на земле.
                if (!Entities.Alive[id] && Entities.Kind[id] != EnemyKind.ForestBud) continue;
                int w = BigMarkWeightOf(id, out int start, out int impact);
                if (w == 0) continue;
                used += w;
                if (start > lastStart) lastStart = start;
                if (impact >= Tick && Math.Abs(impact - impactTick) < BigMarkImpactSpacingTicks) return false;
            }
            if (used + weight > _bigMarkBudget) return false;
            if (lastStart != int.MinValue && Tick - lastStart < BigMarkStaggerTicks) return false;
            // И в такт ударов по герою (Simulation.AttackRhythm): контакты крупной
            // атаки — не внахлёст с чужими и не по связанному герою.
            return HeroContactAllowed(self, impactTick, impactTick);
        }
    }
}
