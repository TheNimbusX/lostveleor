namespace Game.Sim
{
    /// <summary>
    /// Предмет, лежащий на арене: то, что уронила элита.
    ///
    /// Решение владельца от 15 сентября: вещь и способность при свободном
    /// слоте поднимаются сами, когда герой подходит; способность при полной
    /// панели ждёт выбора в мини-меню и лежит, пока игрок не решит. Не
    /// подобранное до выхода с арены пропадает вместе с ней.
    /// </summary>
    public struct RunDrop
    {
        public FixVec2 Position;
        public RewardOffer Offer;
        public bool Claimed;

        public RunDrop(FixVec2 position, in RewardOffer offer)
        {
            Position = position;
            Offer = offer;
            Claimed = false;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Position.X);
            Hashing.Mix(ref hash, Position.Y);
            Hashing.Mix(ref hash, Claimed ? 1 : 0);
            Offer.HashInto(ref hash);
        }
    }
}
