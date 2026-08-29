namespace Game.Sim
{
    /// <summary>
    /// Лагерь. Хаб ровно один, и это он.
    ///
    /// ПРАВИЛО, РАДИ КОТОРОГО ОН СУЩЕСТВУЕТ: в лагере принимаются решения,
    /// в Разломе они исполняются. Всё, что здесь появляется, обязано порождать
    /// решение с потерей — иначе это меню, а меню игрок перестаёт открывать
    /// после третьего забега.
    ///
    /// Лагерь растёт по актам: награда за акт — новый глагол в лагере,
    /// а не строчка статов.
    /// </summary>
    public sealed class Camp
    {
        /// <summary>
        /// Слотов в сумке. ЗАГЛУШКА БАЛАНСА: настоящее число настраивается
        /// вместе с плотностью дропа, а она ещё не считана.
        /// </summary>
        public const int DefaultBagSlots = 48;

        private readonly int[] _wallet = new int[(int)CurrencyType.Count];

        public ItemDatabase Items { get; }
        public Inventory Bag { get; }

        /// <summary>Надетое. Живёт в лагере, а не в забеге: забег умирает, персонаж нет.</summary>
        public Equipment Worn { get; }

        public int Act { get; private set; }
        public CampService Services { get; private set; }

        public Camp(ItemDatabase items, int act = 1, int bagSlots = DefaultBagSlots)
        {
            Items = items;
            Bag = new Inventory(bagSlots);
            Worn = new Equipment(items);

            Act = 0;
            AdvanceToAct(act);
        }

        // ---- услуги ----

        public bool Has(CampService service) => (Services & service) != 0;

        /// <summary>
        /// Открывает то, что положено акту, и всё, что положено предыдущим.
        /// Двигаться можно только вперёд: услуга, которая закрылась обратно, —
        /// это отобранный у игрока глагол.
        /// </summary>
        public void AdvanceToAct(int act)
        {
            if (act <= Act) return;
            Act = act;

            // Полигон вне актов: он нужен ровно с первой найденной вещи,
            // потому что без него она — лотерея.
            Services |= CampService.ProvingGround;

            if (act >= 1) Services |= CampService.Smith | CampService.Trader;
            if (act >= 2) Services |= CampService.Chronicler | CampService.Stash;
            if (act >= 3) Services |= CampService.Founder | CampService.RiftPortal;
        }

        // ---- кошелёк ----

        public int Money(CurrencyType currency) => _wallet[(int)currency];

        public void Earn(CurrencyType currency, int amount)
        {
            if (amount <= 0) return;
            _wallet[(int)currency] += amount;
        }

        /// <summary>Тратит, если хватает. Возвращает false и не трогает кошелёк, если нет.</summary>
        public bool Spend(CurrencyType currency, int amount)
        {
            if (amount <= 0) return true;
            if (_wallet[(int)currency] < amount) return false;

            _wallet[(int)currency] -= amount;
            return true;
        }

        // ---- снаряжение ----

        /// <summary>
        /// Надевает предмет из сумки. Снятое возвращается в ТОТ ЖЕ слот сумки:
        /// иначе примерка вслепую переполняла бы её и заставляла разбирать
        /// вещи посреди сравнения.
        /// </summary>
        public bool EquipFromBag(int bagSlot)
        {
            ItemInstance item = Bag.At(bagSlot);
            if (item.IsEmpty) return false;

            ItemInstance replaced;
            if (!Worn.Equip(in item, out replaced)) return false;

            Bag.Remove(bagSlot);
            if (!replaced.IsEmpty) Bag.Add(in replaced);
            return true;
        }

        /// <summary>Снимает в сумку. Если места нет, вещь остаётся надетой.</summary>
        public bool UnequipToBag(EquipSlot slot)
        {
            if (!Worn.IsWorn(slot)) return false;
            if (Bag.IsFull) return false;

            Bag.Add(Worn.Unequip(slot));
            return true;
        }

        // ---- торговец и разбор ----

        /// <summary>
        /// Автразбор по фильтру: всё непомеченное уходит в осколки.
        /// Возвращает, сколько осколков получено.
        /// </summary>
        public int SalvageJunk()
        {
            int shards = Bag.SalvageUnkept();
            Earn(CurrencyType.Shards, shards);
            return shards;
        }

        /// <summary>
        /// Продаёт предмет торговцу. Золото — валюта потока, и входит оно
        /// именно так: не падает в Разломе, а выручается за мусор.
        ///
        /// Цена — ЗАГЛУШКА БАЛАНСА того же рода, что и выход осколков.
        /// </summary>
        public int SellToTrader(int bagSlot)
        {
            if (!Has(CampService.Trader)) return 0;

            ItemInstance item = Bag.At(bagSlot);
            if (item.IsEmpty) return 0;

            int gold = PriceOf(in item);
            Bag.Remove(bagSlot);
            Earn(CurrencyType.Gold, gold);
            return gold;
        }

        /// <summary>Цена скупки. Заглушка: важно только, что редкость и уровень влияют.</summary>
        public static int PriceOf(in ItemInstance item)
        {
            int byRarity;
            switch (item.Rarity)
            {
                case ItemRarity.Rare: byRarity = 25; break;
                case ItemRarity.Magic: byRarity = 9; break;
                default: byRarity = 3; break;
            }

            return byRarity + item.ItemLevel;
        }

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Act);
            Hashing.Mix(ref hash, (int)Services);
            for (int i = 0; i < _wallet.Length; i++) Hashing.Mix(ref hash, _wallet[i]);

            Bag.HashInto(ref hash);
            Worn.HashInto(ref hash);
        }
    }
}
