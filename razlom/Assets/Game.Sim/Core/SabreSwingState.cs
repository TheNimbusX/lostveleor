namespace Game.Sim
{
    /// <summary>
    /// Один удар обычной серии сабли (Simulation.SabreCombo): номер, место в
    /// серии, сроки и направление. Сроки и направление замораживаются на
    /// старте — скорость атаки, сменившаяся посреди удара, его не трогает,
    /// а представление может вести клип по этим тикам.
    /// </summary>
    public struct SabreSwingState
    {
        /// <summary>Номер удара в сцене, растёт с каждым ударом. 0 — ударов ещё не было.</summary>
        public int Serial;

        /// <summary>Место в серии: 0 — справа налево, 1 — слева направо, 2 — добивающий.</summary>
        public int Hit;

        public int StartTick, ContactTick, EndTick;

        /// <summary>Первый тик выпада добивающего; −1 — выпада нет. Выпад идёт до ContactTick.</summary>
        public int LungeStartTick;

        /// <summary>Куда бьёт удар; единичный вектор, задан на старте.</summary>
        public FixVec2 Direction;

        /// <summary>Контакт уже разобран (сектор проверен, урон нанесён).</summary>
        public bool ContactDone;

        /// <summary>Удар снят до конца: уход, способность, оглушение, смерть.</summary>
        public bool Interrupted;

        /// <summary>Сколько целей задел контакт. Для представления и возврата ресурса.</summary>
        public int Hits;

        public bool ActiveAt(int tick) => Serial > 0 && !Interrupted && tick < EndTick;
        public bool IsFinisher => Hit == 2;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Hit);
            Hashing.Mix(ref hash, StartTick); Hashing.Mix(ref hash, ContactTick);
            Hashing.Mix(ref hash, EndTick); Hashing.Mix(ref hash, LungeStartTick);
            Hashing.Mix(ref hash, Direction.X); Hashing.Mix(ref hash, Direction.Y);
            Hashing.Mix(ref hash, ContactDone ? 1 : 0);
            Hashing.Mix(ref hash, Interrupted ? 1 : 0);
            Hashing.Mix(ref hash, Hits);
        }
    }
}
