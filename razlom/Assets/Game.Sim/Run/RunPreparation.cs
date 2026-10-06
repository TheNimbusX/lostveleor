namespace Game.Sim
{
    // Номера даров сохраняются: новые добавляются только в конец.
    public enum CampGift : byte
    {
        None = 0, DryRation = 1, EniWhetstone = 2, LightPack = 3,
        SeaKnot = 4, BackupPlan = 5, SpareFlask = 6
    }

    /// <summary>Что лежит в ячейке «с собой» стола сборов. Номера в сохранении: только дописывать.</summary>
    public enum CarryKind : byte { None = 0, Gift = 1, Artifact = 2 }

    /// <summary>
    /// Ячейка «с собой» (06.10): до первого босса — дар, потом 1 из 3 среди даров
    /// и открытых артефактов. Одна ячейка на двоих, поэтому выбор — одно значение,
    /// а не пара полей, которые могли бы оказаться заполнены оба.
    /// </summary>
    public readonly struct CarryChoice
    {
        public readonly CarryKind Kind;
        public readonly CampGift Gift;
        public readonly RunArtifact Artifact;

        private CarryChoice(CarryKind kind, CampGift gift, RunArtifact artifact)
        { Kind = kind; Gift = gift; Artifact = artifact; }

        /// <summary>Дар; CampGift.None — пустая ячейка.</summary>
        public static CarryChoice Of(CampGift gift)
            => gift == CampGift.None ? default : new CarryChoice(CarryKind.Gift, gift, RunArtifact.None);

        /// <summary>Артефакт на этот забег; RunArtifact.None — пустая ячейка.</summary>
        public static CarryChoice Of(RunArtifact artifact)
            => artifact == RunArtifact.None ? default : new CarryChoice(CarryKind.Artifact, CampGift.None, artifact);

        /// <summary>Та же ячейка: вид и значение. Пустые равны между собой.</summary>
        public bool SameAs(in CarryChoice other) => Kind == other.Kind && Gift == other.Gift && Artifact == other.Artifact;

        /// <summary>Байт значения для сохранения: номер дара или артефакта, 0 — пусто.</summary>
        internal byte Value => Kind == CarryKind.Gift ? (byte)Gift : Kind == CarryKind.Artifact ? (byte)Artifact : (byte)0;

        /// <summary>Из пары байтов сохранения. Вид вне 0..2 читатель отвергает сам, как порчу.</summary>
        internal static CarryChoice FromSave(byte kind, byte value)
            => kind == (byte)CarryKind.Gift ? Of((CampGift)value) : kind == (byte)CarryKind.Artifact ? Of((RunArtifact)value) : default;
    }

    /// <summary>Снимок решений у стола. После входа в поход он не меняется.</summary>
    public readonly struct RunPreparation
    {
        public readonly int StarterId;
        public readonly CampGift Gift;
        public readonly PotionKind Potion1, Potion2;

        /// <summary>
        /// Артефакт из ячейки «с собой»: действует только этот забег — RiftRun ставит его в
        /// StartRun, в лагерь он не возвращается. None — ячейка пуста или в ней дар.
        /// </summary>
        public readonly RunArtifact Carried;

        public RunPreparation(int starterId, CampGift gift, PotionKind potion1, PotionKind potion2,
            RunArtifact carried = RunArtifact.None)
        { StarterId = starterId; Gift = gift; Potion1 = potion1; Potion2 = potion2; Carried = carried; }
        public int StarterPoolIndex => PelagKit.PoolIndexOf(StarterId);
        public PotionKind PotionAt(int slot) => slot == 0 ? Potion1 : Potion2;
        public static RunPreparation Default => new RunPreparation(AbilityDefinition.WhirlwindId,
            CampGift.None, PotionKind.SmallHealth, PotionKind.SmallLavidium);
        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, StarterId); Hashing.Mix(ref hash, (int)Gift);
            Hashing.Mix(ref hash, (int)Potion1); Hashing.Mix(ref hash, (int)Potion2);
            // Только когда артефакт взят: без него хеш совпадает с прежним бит в бит.
            if (Carried != RunArtifact.None) { Hashing.Mix(ref hash, 0x43415252); Hashing.Mix(ref hash, (int)Carried); }
        }
    }
}
