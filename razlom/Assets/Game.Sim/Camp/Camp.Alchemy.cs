namespace Game.Sim
{
    public enum AlchemistOrder : byte { Resin = 0, Surge = 1 }
    public enum AlchemistOrderStatus : byte { Hidden = 0, Available = 1, Accepted = 2, Ready = 3, Unlocked = 4 }
    public enum AlchemistActionResult : byte
    {
        Success, NotMet, NotAccepted, AlreadyAccepted, NotReady, AlreadyUnlocked,
        InvalidOrder, InvalidItem, ProtectedItem, WrongRarity, InsufficientShards
    }

    public sealed partial class Camp
    {
        public const int SurgeUnlockShards = 12;
        readonly AlchemistOrderStatus[] _alchemyOrders = new AlchemistOrderStatus[2];
        public bool HasMetAlchemist { get; private set; }
        public AlchemistOrderStatus AlchemyStatus(AlchemistOrder order)
            => (uint)order < 2 ? _alchemyOrders[(int)order] : AlchemistOrderStatus.Hidden;

        public void MeetAlchemist()
        {
            if (!Has(CampService.Alchemist) || HasMetAlchemist) return;
            HasMetAlchemist = true;
            for (int i = 0; i < 2; i++) if (_alchemyOrders[i] == AlchemistOrderStatus.Hidden)
                _alchemyOrders[i] = AlchemistOrderStatus.Available;
        }
        public AlchemistActionResult AcceptAlchemyOrder(AlchemistOrder order)
        {
            if ((uint)order >= 2) return AlchemistActionResult.InvalidOrder;
            if (!HasMetAlchemist) return AlchemistActionResult.NotMet;
            var state = AlchemyStatus(order);
            if (state == AlchemistOrderStatus.Unlocked) return AlchemistActionResult.AlreadyUnlocked;
            if (state != AlchemistOrderStatus.Available) return AlchemistActionResult.AlreadyAccepted;
            _alchemyOrders[(int)order] = AlchemistOrderStatus.Accepted;
            return AlchemistActionResult.Success;
        }
        internal void CompleteAlchemyOrder(AlchemistOrder order)
        {
            if (AlchemyStatus(order) == AlchemistOrderStatus.Accepted)
                _alchemyOrders[(int)order] = AlchemistOrderStatus.Ready;
        }
        public AlchemistActionResult TurnInAlchemyOrder(AlchemistOrder order)
        {
            if ((uint)order >= 2) return AlchemistActionResult.InvalidOrder;
            if (!HasMetAlchemist) return AlchemistActionResult.NotMet;
            var state = AlchemyStatus(order);
            if (state == AlchemistOrderStatus.Unlocked) return AlchemistActionResult.AlreadyUnlocked;
            if (state != AlchemistOrderStatus.Ready) return AlchemistActionResult.NotReady;
            _alchemyOrders[(int)order] = AlchemistOrderStatus.Unlocked;
            return AlchemistActionResult.Success;
        }
        public AlchemistActionResult ExchangeRareForResin(int bagSlot)
        {
            if (!HasMetAlchemist) return AlchemistActionResult.NotMet;
            var state = AlchemyStatus(AlchemistOrder.Resin);
            if (state == AlchemistOrderStatus.Unlocked) return AlchemistActionResult.AlreadyUnlocked;
            if (state != AlchemistOrderStatus.Accepted) return AlchemistActionResult.NotAccepted;
            if ((uint)bagSlot >= Bag.Capacity || Bag.IsEmpty(bagSlot)) return AlchemistActionResult.InvalidItem;
            if (Bag.IsKept(bagSlot)) return AlchemistActionResult.ProtectedItem;
            if (Bag.At(bagSlot).Rarity != ItemRarity.Magic) return AlchemistActionResult.WrongRarity;
            Bag.Remove(bagSlot);
            _alchemyOrders[(int)AlchemistOrder.Resin] = AlchemistOrderStatus.Unlocked;
            return AlchemistActionResult.Success;
        }
        public AlchemistActionResult ExchangeShardsForSurge()
        {
            if (!HasMetAlchemist) return AlchemistActionResult.NotMet;
            var state = AlchemyStatus(AlchemistOrder.Surge);
            if (state == AlchemistOrderStatus.Unlocked) return AlchemistActionResult.AlreadyUnlocked;
            if (state != AlchemistOrderStatus.Accepted) return AlchemistActionResult.NotAccepted;
            if (!Spend(CurrencyType.Shards, SurgeUnlockShards)) return AlchemistActionResult.InsufficientShards;
            _alchemyOrders[(int)AlchemistOrder.Surge] = AlchemistOrderStatus.Unlocked;
            return AlchemistActionResult.Success;
        }
        internal void RestoreAlchemy(bool met, AlchemistOrderStatus resin, AlchemistOrderStatus surge)
        {
            if ((uint)resin > 4 || (uint)surge > 4 || !met && (resin != AlchemistOrderStatus.Hidden || surge != AlchemistOrderStatus.Hidden)
                || met && (resin == AlchemistOrderStatus.Hidden || surge == AlchemistOrderStatus.Hidden))
                throw new System.IO.InvalidDataException("Некорректные заказы алхимика");
            HasMetAlchemist = met;
            _alchemyOrders[0] = resin; _alchemyOrders[1] = surge;
            if (!PotionUnlocked(_selectedPotions[0]) || !PotionUnlocked(_selectedPotions[1]))
                throw new System.IO.InvalidDataException("Выбрано закрытое зелье");
            if (_potions[4] > 0 && resin != AlchemistOrderStatus.Unlocked ||
                _potions[5] > 0 && surge != AlchemistOrderStatus.Unlocked)
                throw new System.IO.InvalidDataException("Запас закрытого зелья");
        }
        void HashAlchemy(ref ulong hash)
        {
            Hashing.Mix(ref hash, HasMetAlchemist ? 1 : 0);
            for (int i = 0; i < 2; i++) Hashing.Mix(ref hash, (int)_alchemyOrders[i]);
        }
    }
}
