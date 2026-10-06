using System.Collections.Generic;

namespace Game.Sim
{
    /// <summary>
    /// Связь лагеря с забегом (план 06.10, A.2): победы над боссами, сердца,
    /// когда-либо взятые навыки, открытые артефакты и база героя.
    ///
    /// Начисляется «сразу, как опыт» (решение M1): GameSession зовёт эти методы в тот
    /// же шаг, когда событие случилось, поэтому выход из игры посреди забега ничего
    /// из этого не теряет, а смерть не отнимает.
    /// </summary>
    public sealed partial class Camp
    {
        // По индексу RunBossKeys: боссов в акте три, а ключ неизвестного босса в лагерь
        // не попадает вовсе — массив вместо списка пар ничего не теряет и не аллоцирует.
        readonly int[] _bossDefeats = new int[RunBossKeys.Count];
        readonly int[] _hearts = new int[RunBossKeys.Count];
        // Id способностей по возрастанию, без повторов: сохранение побайтово повторяется.
        readonly List<int> _everTakenSkills = new List<int>();
        // Бит i — RunArtifacts.At(i).
        uint _openedArtifacts;

        static uint ArtifactMask => (1u << RunArtifacts.Count) - 1;

        /// <summary>Победа над боссом: +победа, +сердце, сталь. Неизвестный ключ — ничего.</summary>
        public void RecordBossDefeat(int bossKey)
        {
            int index = RunBossKeys.IndexOf(bossKey);
            if (index < 0) return;
            _bossDefeats[index] = Saturated(_bossDefeats[index], 1);
            _hearts[index] = Saturated(_hearts[index], 1);
            Earn(CurrencyType.Steel, RunEconomy.BossSteel);
            RefreshUnlocks();
        }

        public bool BossDefeated(int bossKey) => BossDefeats(bossKey) > 0;

        /// <summary>Сколько раз побеждён босс с этим ключом.</summary>
        public int BossDefeats(int bossKey)
        {
            int index = RunBossKeys.IndexOf(bossKey);
            return index < 0 ? 0 : _bossDefeats[index];
        }

        /// <summary>Сколько сердец этого босса лежит в лагере.</summary>
        public int HeartCount(int bossKey)
        {
            int index = RunBossKeys.IndexOf(bossKey);
            return index < 0 ? 0 : _hearts[index];
        }

        /// <summary>Навык, взятый в реальном забеге: копилка стола сборов. Не из пула Пелага — мимо.</summary>
        public void RecordSkillTaken(int abilityId)
        {
            if (PelagKit.PoolIndexOf(abilityId) < 0) return;
            int at = _everTakenSkills.BinarySearch(abilityId);
            if (at < 0) _everTakenSkills.Insert(~at, abilityId);
        }

        public bool SkillEverTaken(int abilityId) => _everTakenSkills.BinarySearch(abilityId) >= 0;

        public int EverTakenSkillCount => _everTakenSkills.Count;

        /// <summary>Id когда-либо взятого навыка по порядку; вне 0..EverTakenSkillCount−1 — исключение, как у списка.</summary>
        public int EverTakenSkillAt(int index) => _everTakenSkills[index];

        /// <summary>Артефакт, взятый в реальном забеге, открывается в атласе.</summary>
        public void OpenArtifact(RunArtifact artifact)
        {
            if (!RunArtifacts.IsValid(artifact)) return;
            _openedArtifacts |= 1u << ArtifactBit(artifact);
        }

        public bool ArtifactOpened(RunArtifact artifact)
            => RunArtifacts.IsValid(artifact) && (_openedArtifacts & (1u << ArtifactBit(artifact))) != 0;

        static int ArtifactBit(RunArtifact artifact) => (int)artifact - (int)RunArtifacts.At(0);

        /// <summary>
        /// База героя на забег. Новый профиль прогрессивного лагеря — 200/40: остальное до
        /// эталона добирают вещи и клятвы. Тестовый забег и Sandbox — эталон 270/54, на нём
        /// меряют мобов (решение M6).
        /// </summary>
        public HeroBaseline HeroBaselineFor(bool developerRun)
            => developerRun || !IsProgressive ? HeroBaseline.Reference : HeroBaseline.Fresh;

        /// <summary>
        /// Дешёвый отпечаток того, что меняется в Разломе: по нему CampSaveStore решает,
        /// писать ли сохранение (решение M8). Сумка и надетое сюда не входят — в Разломе
        /// они не меняются; стоимость — десятки смешиваний, без обхода сумки.
        /// </summary>
        public ulong PersistStamp
        {
            get
            {
                ulong hash = Hashing.Offset;
                for (int i = 0; i < _wallet.Length; i++) Hashing.Mix(ref hash, _wallet[i]);
                Hashing.Mix(ref hash, Level); Hashing.Mix(ref hash, Experience);
                for (int i = 0; i < PotionKindCount; i++) Hashing.Mix(ref hash, _potions[i]);
                Hashing.Mix(ref hash, (int)_selectedPotions[0]); Hashing.Mix(ref hash, (int)_selectedPotions[1]);
                for (int i = 0; i < RunBossKeys.Count; i++) { Hashing.Mix(ref hash, _bossDefeats[i]); Hashing.Mix(ref hash, _hearts[i]); }
                Hashing.Mix(ref hash, _everTakenSkills.Count);
                Hashing.Mix(ref hash, (int)_openedArtifacts);
                Hashing.Mix(ref hash, (int)PendingUnlocks);
                Hashing.Mix(ref hash, AttemptCount);
                return hash;
            }
        }

        /// <summary>Только F8: засчитать босса акта по порядку, чтобы проверить ранги.</summary>
        public void DeveloperCreditBoss(int index) => RecordBossDefeat(RunBossKeys.At(index));

        /// <summary>
        /// Только F8 «+1 сердце Чащи»: сердце босса акта по порядку без победы — чтобы
        /// проверить вплавление, не трогая ранги и сталь. Номер вне акта — ничего.
        /// </summary>
        public void DeveloperAddHeart(int bossIndex)
        {
            int index = RunBossKeys.IndexOf(RunBossKeys.At(bossIndex));
            if (index >= 0) _hearts[index] = Saturated(_hearts[index], 1);
        }

        /// <summary>Вплавление у Эни тратит сердце. Нет сердца — false, ничего не меняется.</summary>
        internal bool SpendHeart(int bossKey)
        {
            int index = RunBossKeys.IndexOf(bossKey);
            if (index < 0 || _hearts[index] < 1) return false;
            _hearts[index]--;
            return true;
        }

        static int Saturated(int value, int add) => (int)System.Math.Min(int.MaxValue, (long)value + add);

        void HashRunLink(ref ulong hash)
        {
            for (int i = 0; i < RunBossKeys.Count; i++) { Hashing.Mix(ref hash, _bossDefeats[i]); Hashing.Mix(ref hash, _hearts[i]); }
            Hashing.Mix(ref hash, _everTakenSkills.Count);
            for (int i = 0; i < _everTakenSkills.Count; i++) Hashing.Mix(ref hash, _everTakenSkills[i]);
            Hashing.Mix(ref hash, (int)_openedArtifacts);
        }

        // ---- сохранение v10 ----

        internal int BossDefeatsAt(int index) => _bossDefeats[index];
        internal int HeartsAt(int index) => _hearts[index];
        internal uint OpenedArtifactMask => _openedArtifacts;

        // Из сохранения: неизвестный ключ уже отброшен кодеком, счётчики неотрицательны.
        internal void RestoreBossDefeats(int index, int defeats) => _bossDefeats[index] = defeats;
        internal void RestoreHearts(int index, int hearts) => _hearts[index] = hearts;

        /// <summary>Из сохранения: навык не из пула отбрасывается тем же правилом, что и в забеге.</summary>
        internal void RestoreSkillTaken(int abilityId) => RecordSkillTaken(abilityId);

        /// <summary>Лишние биты (снятый артефакт, битый файл) обрезаются, а не роняют загрузку.</summary>
        internal void RestoreOpenedArtifacts(uint mask) => _openedArtifacts = mask & ArtifactMask;
    }
}
