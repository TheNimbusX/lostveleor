namespace Game.Sim
{
    /// <summary>
    /// Лавидий и опыт — то, что бой копит и тратит для постоянной прокачки.
    ///
    /// Опыт здесь только копится: уровень живёт в лагере, а симуляция
    /// одноразовая — забег умирает вместе с ней. GameSession каждый тик
    /// забирает накопленное через TakePendingXp и отдаёт в Camp.
    /// </summary>
    public sealed partial class Simulation
    {
        private int _pendingXp;

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

        private void HashProgression(ref ulong hash) => Hashing.Mix(ref hash, _pendingXp);
    }
}
