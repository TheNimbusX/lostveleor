namespace Game.Sim
{
    /// <summary>
    /// Лавидий и опыт — то, что бой копит и тратит для постоянной прокачки.
    ///
    /// Опыт здесь только копится: уровень живёт в лагере, а симуляция
    /// одноразовая — забег умирает вместе с ней. GameSession каждый тик
    /// забирает накопленное через TakePendingXp и отдаёт в Camp. Сам уровень
    /// симуляция не знает: с 29 сентября он статов не даёт (Progression).
    /// </summary>
    public sealed partial class Simulation
    {
        private int _pendingXp;
        private bool _heroBaseline;

        /// <summary>Стоит ли на герое база эталонного героя (<see cref="ApplyHeroBaseline"/>).</summary>
        public bool HasHeroBaseline => _heroBaseline;

        /// <summary>
        /// Ставит герою базу эталонного героя — прежний 5-й уровень лагеря:
        /// +120 здоровья, +20 урона, +40 лавидия (Progression.HeroBaseline*).
        /// Решение владельца от 29 сентября: уровень статов не даёт, герой
        /// одинаков на 1-м и на 20-м уровне.
        ///
        /// Зовёт GameSession для каждой своей симуляции — лагеря, Полигона,
        /// Разлома, стенда мобов. Голая симуляция тестов остаётся на базе
        /// 34 урона и 200 лавидия: на её числах стоят сотни проверок боя.
        ///
        /// ФЛАГ ХРАНИТСЯ В СИМУЛЯЦИИ, потому что Spawn стирает модификаторы:
        /// ConfigurePlayer вешает базу заново при каждой расстановке.
        ///
        /// Повтор ничего не меняет — лечения на повышении уровня больше нет.
        /// Первая постановка на живого героя доводит и текущее здоровье на
        /// прибавку, как было с уровнем: потолок и полоса растут вместе.
        /// </summary>
        public void ApplyHeroBaseline()
        {
            if (_heroBaseline) return;
            _heroBaseline = true;
            if (Entities.Count <= PlayerId) return;
            int before = Entities.MaxHealth[PlayerId];
            ApplyHeroBaselineModifiers(Entities.Stats[PlayerId]);
            RefreshPlayerStats(heal: false);
            int gained = Entities.MaxHealth[PlayerId] - before;
            if (gained > 0 && Entities.Alive[PlayerId]) Entities.Health[PlayerId] += gained;
        }

        /// <summary>
        /// Вешает на лист героя прибавки базы. Источник — прежний
        /// ModifierSource.Level: числа и порядок те же, что у 5-го уровня, так
        /// что герой на базе не отличается от прежнего героя 5-го уровня.
        /// </summary>
        private void ApplyHeroBaselineModifiers(StatSheet sheet)
        {
            sheet.RemoveSource(ModifierSource.Level, 0);
            if (!_heroBaseline) return;
            sheet.Add(StatModifier.Flat(StatType.MaxHealth, Fix64.FromInt(Progression.HeroBaselineHealth), ModifierSource.Level, 0));
            sheet.Add(StatModifier.Flat(StatType.Damage, Fix64.FromInt(Progression.HeroBaselineDamage), ModifierSource.Level, 0));
            sheet.Add(StatModifier.Flat(StatType.MaxLavidium, Fix64.FromInt(Progression.HeroBaselineLavidium), ModifierSource.Level, 0));
        }

        /// <summary>Опыт, набранный с прошлого забора и ещё не отданный в лагерь.</summary>
        public int PendingXp => _pendingXp;

        /// <summary>Забирает накопленный опыт и обнуляет счётчик.</summary>
        public int TakePendingXp()
        {
            int xp = _pendingXp;
            _pendingXp = 0;
            return xp;
        }

        /// <summary>Стоимость каста в целых единицах лавидия.</summary>
        public static int LavidiumCostOf(AbilityBuild build)
            => build == null ? 0 : build.Get(AbilityStatType.LavidiumCost).ToInt();

        /// <summary>
        /// Хватает ли лавидия на каст. Проверяется ВЕЗДЕ, где проверяется
        /// кулдаун: способность, на которую не хватает ресурса, не должна
        /// ни штрафовать движение, ни начинать удержание, ни тратить кулдаун.
        /// </summary>
        private bool CanAffordAbility(AbilityBuild build)
            => Entities.Lavidium[PlayerId] >= Fix64.FromInt(LavidiumCostOf(build));

        private void SpendLavidium(AbilityBuild build)
        {
            Fix64 left = Entities.Lavidium[PlayerId] - Fix64.FromInt(LavidiumCostOf(build));
            Entities.Lavidium[PlayerId] = left < Fix64.Zero ? Fix64.Zero : left;
        }

        /// <summary>
        /// Восстановление лавидия, раз в тик по возрастанию индекса. Мёртвые
        /// и те, у кого пула нет вовсе, пропускаются.
        /// </summary>
        private void RegenerateLavidium()
        {
            for (int i = 0; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.MaxLavidium[i] <= 0) continue;
                Fix64 cap = Fix64.FromInt(Entities.MaxLavidium[i]);
                Fix64 next = Entities.Lavidium[i] + Entities.LavidiumRegenPerTick[i];
                Entities.Lavidium[i] = next > cap ? cap : next;
            }
        }

        /// <summary>
        /// Опыт за убийство идёт игроку, если добил он — любым путём:
        /// автоатакой, способностью или горением, которое он повесил.
        /// </summary>
        private void GrantKillXp(int target, int killer)
        {
            if (killer != PlayerId || target == PlayerId) return;
            _pendingXp += Entities.XpReward[target];
        }

        private void HashProgression(ref ulong hash)
        {
            Hashing.Mix(ref hash, _pendingXp);
            // Уровня лагеря в хеше нет: статов он не даёт. База героя подмешивается,
            // только если стоит (хеши голых симуляций тестов не меняются), и тем же
            // числом, что прежний 5-й уровень: симуляция на базе хешируется как он.
            if (_heroBaseline) Hashing.Mix(ref hash, Progression.ReferenceHeroLevel);
        }
    }
}
