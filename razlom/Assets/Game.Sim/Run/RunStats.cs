using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>Кем был враг для статистики забега: обычный, элита или босс арены.</summary>
    public enum RunFoeRank : byte
    {
        /// <summary>Врага нет: удара ещё не было, герой не погиб или убил не враг.</summary>
        None = 0,
        Normal = 1,

        /// <summary>Элита расстановки (Simulation.IsElite), кроме босса.</summary>
        Elite = 2,

        /// <summary>Босс арены (RiftRun.BossId). Вид у него обычный — отличает только ранг.</summary>
        Boss = 3,
    }

    /// <summary>
    /// Статистика одного забега для экрана итогов (этап 4, п. 11): время,
    /// убийства по видам, урон, лучший удар, криты, уклонения, зелья,
    /// способности и уровни.
    ///
    /// ЭТО НЕ СОСТОЯНИЕ БОЯ. Счёт ведёт сессия — по событиям симуляции, как
    /// Полигон и заказ алхимика, и по зельям, которые пьёт сама. Симуляция о
    /// нём не знает и обратно его не читает, поэтому его нет ни в StateHash,
    /// ни в хеше сессии: счётчик ничего не решает в бою, и пины хешей не
    /// должны сдвигаться оттого, что мы решили что-то посчитать.
    ///
    /// Один объект — один забег. Вход в Разлом заводит новый, конец забега
    /// замораживает его и отдаёт в RunSummary: следующий забег считает с нуля,
    /// а итоги прошлого уже не меняются.
    /// </summary>
    public sealed class RunStats
    {
        /// <summary>Итоги до первого забега: всё по нулям, заморожены.</summary>
        public static readonly RunStats Empty = Frozen();

        // EnemyKind — байт: место под любой вид, в том числе будущий.
        private const int KindSlots = 256;

        private readonly int[] _killsByKind = new int[KindSlots];
        private readonly List<EnemyKind> _killedKinds = new List<EnemyKind>();
        private readonly int[] _potionsByKind = new int[Camp.PotionKindCount];

        public RunStats(int startLevel = 1, int startExperience = 0)
        {
            StartLevel = startLevel;
            StartExperience = startExperience;
            EndLevel = startLevel;
            EndExperience = startExperience;
        }

        /// <summary>Забег кончился, числа заморожены: запись дальше ничего не меняет.</summary>
        public bool IsFinished { get; private set; }

        // ---- время ----

        /// <summary>Все шаги забега, включая экраны наград, замены и выбора пути.</summary>
        public int TotalTicks { get; private set; }

        /// <summary>
        /// Шаги, в которые бой шёл. На экране награды симуляция стоит (RiftRun.Step),
        /// и это время сюда не входит.
        /// </summary>
        public int CombatTicks { get; private set; }

        /// <summary>Всё время забега в целых секундах, вниз.</summary>
        public int TotalSeconds => TotalTicks / Simulation.TicksPerSecond;

        /// <summary>Время боя в целых секундах, вниз.</summary>
        public int CombatSeconds => CombatTicks / Simulation.TicksPerSecond;

        // ---- убийства ----

        /// <summary>Сколько врагов убил герой: прямо, способностью или горением. Ушедшие в землю не в счёт.</summary>
        public int Kills { get; private set; }

        /// <summary>Из них элит — без босса.</summary>
        public int EliteKills { get; private set; }

        /// <summary>Из них боссов.</summary>
        public int BossKills { get; private set; }

        /// <summary>Убийства вида, включая элиту и босса этого вида.</summary>
        public int KillsOf(EnemyKind kind) => _killsByKind[(int)kind];

        /// <summary>Сколько разных видов убито — длина списка KilledKind.</summary>
        public int KilledKindCount => _killedKinds.Count;

        /// <summary>Убитые виды в порядке первого убийства: так экран итогов рассказывает забег по ходу.</summary>
        public EnemyKind KilledKind(int index) => _killedKinds[index];

        // ---- урон ----

        /// <summary>Весь урон героя по врагам: прямой и по времени.</summary>
        public long DamageDealt => DirectDamageDealt + DamageOverTimeDealt;

        /// <summary>Прямой урон героя: автоатаки и удары способностей (события Damage).</summary>
        public long DirectDamageDealt { get; private set; }

        /// <summary>Урон героя по времени — горение (события DamageOverTime).</summary>
        public long DamageOverTimeDealt { get; private set; }

        /// <summary>Весь урон по герою, прямой и по времени. Уклонённое и поглощённое не в счёт.</summary>
        public long DamageTaken { get; private set; }

        /// <summary>Прямых попаданий героя. Тики горения — не попадания.</summary>
        public int Hits { get; private set; }

        /// <summary>Сколько из попаданий героя были критами.</summary>
        public int Crits { get; private set; }

        /// <summary>Сколько ударов по герою прошло мимо (событие Evaded).</summary>
        public int Evades { get; private set; }

        // ---- лучший удар ----

        /// <summary>Самое большое прямое попадание героя; 0 — не было ни одного. При равенстве — первое.</summary>
        public int BestHit { get; private set; }

        /// <summary>Вид цели лучшего удара.</summary>
        public EnemyKind BestHitTarget { get; private set; }

        /// <summary>Ранг цели лучшего удара; None — удара не было.</summary>
        public RunFoeRank BestHitTargetRank { get; private set; }

        public bool BestHitCrit { get; private set; }

        /// <summary>Автоатака или способность.</summary>
        public DamageOrigin BestHitOrigin { get; private set; }

        /// <summary>DefinitionId способности лучшего удара; −1 — автоатака или способность не известна.</summary>
        public int BestHitAbility { get; private set; } = -1;

        // ---- действия героя ----

        /// <summary>Касты способностей (событие AbilityCast). Продолжения серий без оплаты не в счёт.</summary>
        public int AbilitiesCast { get; private set; }

        /// <summary>Выпито зелий в забеге.</summary>
        public int PotionsUsed { get; private set; }

        public int PotionsUsedOf(PotionKind kind)
            => (uint)kind < (uint)_potionsByKind.Length ? _potionsByKind[(int)kind] : 0;

        // ---- смерть ----

        /// <summary>Вид того, кто добил героя; None — герой жив или добил не враг.</summary>
        public EnemyKind KilledBy { get; private set; }

        /// <summary>Ранг добившего; None — герой жив или добил не враг.</summary>
        public RunFoeRank KilledByRank { get; private set; }

        // ---- уровни ----

        /// <summary>Уровень и опыт лагеря на входе в Разлом.</summary>
        public int StartLevel { get; }
        public int StartExperience { get; }

        /// <summary>Уровень и опыт лагеря на экране итогов.</summary>
        public int EndLevel { get; private set; }
        public int EndExperience { get; private set; }

        /// <summary>Опыт, который забег отдал лагерю. Разработческий забег опыта не даёт.</summary>
        public int ExperienceGained { get; private set; }

        /// <summary>
        /// Уровни, взятые опытом этого забега. Считаются по повышениям, а не
        /// разностью EndLevel − StartLevel: ручной уровень из меню разработчика
        /// посреди забега в счёт не идёт.
        /// </summary>
        public int LevelsGained { get; private set; }

        // ---- запись ----

        /// <summary>Один шаг сессии в Разломе; combat — шагнула ли симуляция.</summary>
        internal void CountStep(bool combat)
        {
            if (IsFinished) return;
            TotalTicks++;
            if (combat) CombatTicks++;
        }

        /// <summary>
        /// События одного шага боя. Звать только в тик, когда симуляция
        /// шагнула: на экране награды её список хранит события прошлого тика,
        /// и второй проход посчитал бы их дважды. bossId — босс арены или −1.
        /// </summary>
        internal void Record(IReadOnlyList<SimEvent> events, Simulation sim, int bossId)
        {
            if (IsFinished || events == null || sim == null) return;
            const int hero = Simulation.PlayerId;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Damage:
                        if (e.Target == hero) { DamageTaken += e.Amount; break; }
                        if (e.Source != hero || !IsFoe(sim, e.Target)) break;
                        DirectDamageDealt += e.Amount;
                        Hits++;
                        if (e.Flag) Crits++;
                        if (e.Amount > BestHit) RecordBestHit(in e, sim, bossId);
                        break;

                    case SimEventType.DamageOverTime:
                        if (e.Target == hero) { DamageTaken += e.Amount; break; }
                        if (e.Source == hero && IsFoe(sim, e.Target)) DamageOverTimeDealt += e.Amount;
                        break;

                    case SimEventType.Death:
                        if (e.Target == hero)
                        {
                            bool foe = IsFoe(sim, e.Source);
                            KilledBy = foe ? sim.Entities.Kind[e.Source] : EnemyKind.None;
                            KilledByRank = foe ? RankOf(sim, e.Source, bossId) : RunFoeRank.None;
                            break;
                        }
                        if (e.Source == hero && IsFoe(sim, e.Target)) RecordKill(sim, e.Target, bossId);
                        break;

                    case SimEventType.Evaded:
                        if (e.Source == hero) Evades++;
                        break;

                    case SimEventType.AbilityCast:
                        if (e.Source == hero) AbilitiesCast++;
                        break;
                }
            }
        }

        /// <summary>Зелье выпито в Разломе.</summary>
        internal void CountPotion(PotionKind kind)
        {
            if (IsFinished) return;
            PotionsUsed++;
            if ((uint)kind < (uint)_potionsByKind.Length) _potionsByKind[(int)kind]++;
        }

        /// <summary>Опыт забега ушёл в лагерь; levels — сколько повышений он дал.</summary>
        internal void CountExperience(int xp, int levels)
        {
            if (IsFinished) return;
            if (xp > 0) ExperienceGained += xp;
            if (levels > 0) LevelsGained += levels;
        }

        /// <summary>Конец забега: снимок уровня лагеря и заморозка.</summary>
        internal void Finish(int level, int experience)
        {
            if (IsFinished) return;
            EndLevel = level;
            EndExperience = experience;
            IsFinished = true;
        }

        private void RecordKill(Simulation sim, int target, int bossId)
        {
            Kills++;
            RunFoeRank rank = RankOf(sim, target, bossId);
            if (rank == RunFoeRank.Boss) BossKills++;
            else if (rank == RunFoeRank.Elite) EliteKills++;

            EnemyKind kind = sim.Entities.Kind[target];
            if (_killsByKind[(int)kind]++ == 0) _killedKinds.Add(kind);
        }

        private void RecordBestHit(in SimEvent e, Simulation sim, int bossId)
        {
            BestHit = e.Amount;
            BestHitTarget = sim.Entities.Kind[e.Target];
            BestHitTargetRank = RankOf(sim, e.Target, bossId);
            BestHitCrit = e.Flag;
            BestHitOrigin = e.DamageOrigin;
            AbilityBuild build = e.DamageOrigin == DamageOrigin.Ability
                && (uint)e.ActionVariant < (uint)Simulation.AbilitySlots
                    ? sim.GetAbility(e.ActionVariant) : null;
            BestHitAbility = build != null ? build.DefinitionId : -1;
        }

        /// <summary>Враг героя: существующая сущность не его стороны.</summary>
        private static bool IsFoe(Simulation sim, int entity)
            => entity != Simulation.PlayerId && (uint)entity < (uint)sim.Entities.Count
                && sim.Entities.Side[entity] != sim.Entities.Side[Simulation.PlayerId];

        // Босс помечен и элитой (SetupBossArena), поэтому проверяется первым.
        private static RunFoeRank RankOf(Simulation sim, int entity, int bossId)
            => entity == bossId ? RunFoeRank.Boss
                : sim.IsElite(entity) ? RunFoeRank.Elite
                : RunFoeRank.Normal;

        private static RunStats Frozen()
        {
            var stats = new RunStats();
            stats.IsFinished = true;
            return stats;
        }
    }
}
